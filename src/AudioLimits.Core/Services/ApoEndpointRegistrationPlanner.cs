using Microsoft.Win32;

namespace AudioLimits.Core.Services;

public enum ApoProvisioningReadiness
{
    Ready,
    AlreadyRegistered,
    EnhancementsDisabled,
    CannotInspect,
    UnsupportedRegistration
}

public sealed record ApoRegistryMutation(
    string Name,
    string? Text = null,
    string[]? MultiText = null)
{
    public bool Delete => Text is null && MultiText is null;
}

public sealed record ApoEndpointRegistrationPlan(
    ApoProvisioningReadiness Readiness,
    ApoInstallModeHint? Mode,
    IReadOnlyList<ApoRegistryMutation> FxChanges,
    IReadOnlyList<ApoRegistryMutation> ChildValues,
    string Explanation)
{
    public bool CanInstall => Readiness == ApoProvisioningReadiness.Ready;
}

/// <summary>
/// A deterministic endpoint-only translation of upstream Equalizer APO 1.4.2's
/// default install mode and child APO preservation. This plan does NOT write
/// the registry. The elevated helper must snapshot current values to durable
/// storage, validate preconditions again, and roll back failed transactions.
/// </summary>
public static class ApoEndpointRegistrationPlanner
{
    public const string EffectGuid = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}";
    public const string PreMixGuid = "{EACD2258-FCAC-4FF4-B36D-419E924A6D79}";
    public const string PostMixGuid = "{EC1CC9CE-FAED-4822-828A-82A81A6F018F}";
    public const string DefaultProcessingMode = "{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}";
    public const string ModeGuid = "{d3993a3f-99c2-4402-b5ec-a92a0367664b}";
    private static readonly int[] OriginalSlots = [1, 2, 5, 6, 7];

    public static string SlotName(int slot) => $"{EffectGuid},{slot}";
    public static string ModeName(int slot) => $"{ModeGuid},{slot}";

    public static ApoEndpointRegistrationPlan Build(
        ApoFxRegistrationSnapshot state, bool isRegistered)
    {
        if (!state.InspectionSucceeded)
            return Block(ApoProvisioningReadiness.CannotInspect,
                "The Windows audio registry could not be inspected.");
        if (state.EnhancementsDisabled)
            return Block(ApoProvisioningReadiness.EnhancementsDisabled,
                "Enable Windows audio enhancements before installing Equalizer APO.");
        if (isRegistered)
            return Block(ApoProvisioningReadiness.AlreadyRegistered,
                "Equalizer APO is already registered on this endpoint.");
        if (state.EffectSlots.Any(x =>
                x.Kind != RegistryValueKind.String ||
                !Guid.TryParse(x.ClassId, out _)))
            return Block(ApoProvisioningReadiness.UnsupportedRegistration,
                "This endpoint uses effect-registration values that cannot be preserved safely.");

        // Do not overwrite an orphaned/unknown Equalizer APO installation.
        if (state.EffectSlots.Any(x => Guid.TryParse(x.ClassId, out var guid) &&
              (guid == Guid.Parse(PreMixGuid) || guid == Guid.Parse(PostMixGuid))))
            return Block(ApoProvisioningReadiness.UnsupportedRegistration,
                "An existing Equalizer APO registration could not be verified.");

        var mode = ApoInstallModeAdvisor.Suggest(state);
        if (mode is null)
            return Block(ApoProvisioningReadiness.CannotInspect,
                "The appropriate audio processing mode could not be determined.");

        var previous = state.EffectSlots.ToDictionary(
            x => x.Slot,
            x => x.ClassId!,
            EqualityComparer<int>.Default);

        var original = OriginalSlots.ToDictionary(
            slot => slot, slot => previous.GetValueOrDefault(slot) ??
                (state.FxKeyExists ? "!VALUE" : "!KEY"));

        string ReadOriginal(int slot) =>
            original[slot] is "!VALUE" or "!KEY" ? "" : original[slot];

        // Mirror DeviceAPOInfo.cpp getOriginalAPOPreMix/PostMix(), including
        // Windows 8.1+ migration fallback from unused legacy processing slots.
        string pre, post;
        var noLegacy = original[1] == "!VALUE" && original[2] == "!VALUE";
        switch (mode.Value)
        {
            case ApoInstallModeHint.LegacyLfxGfx:
                pre = noLegacy ? ReadOriginal(5) : ReadOriginal(1);
                post = noLegacy ? ReadOriginal(6) : ReadOriginal(2);
                break;
            case ApoInstallModeHint.StreamSfxMfx:
                var noSfxMfx = original[5] == "!VALUE" && original[6] == "!VALUE";
                pre = noSfxMfx ? ReadOriginal(1) : ReadOriginal(5);
                post = noSfxMfx ? ReadOriginal(2) : ReadOriginal(6);
                break;
            default:
                var noSfxEfx = original[5] == "!VALUE" && original[7] == "!VALUE";
                pre = noSfxEfx ? ReadOriginal(1) : ReadOriginal(5);
                post = noSfxEfx ? ReadOriginal(2) : ReadOriginal(7);
                break;
        }

        var child = OriginalSlots
            .Select(slot => new ApoRegistryMutation(SlotName(slot), original[slot]))
            .ToList();
        child.Add(new("Version", "2"));
        child.Add(new("PreMixChild", pre));
        child.Add(new("PostMixChild", post));
        child.Add(new("AllowSilentBufferModification", "false"));

        var fx = new List<ApoRegistryMutation>();
        switch (mode)
        {
            case ApoInstallModeHint.LegacyLfxGfx:
                fx.Add(new(SlotName(1), PreMixGuid));
                fx.Add(new(SlotName(2), PostMixGuid));
                fx.Add(new(SlotName(5)));
                fx.Add(new(SlotName(6)));
                fx.Add(new(SlotName(7)));
                break;
            case ApoInstallModeHint.StreamSfxMfx:
                fx.Add(new(SlotName(1)));
                fx.Add(new(SlotName(2)));
                fx.Add(new(SlotName(5), PreMixGuid));
                fx.Add(new(SlotName(6), PostMixGuid));
                fx.Add(new(ModeName(5), MultiText: [DefaultProcessingMode]));
                fx.Add(new(ModeName(6), MultiText: [DefaultProcessingMode]));
                break;
            case ApoInstallModeHint.StreamSfxEfx:
                fx.Add(new(SlotName(1)));
                fx.Add(new(SlotName(2)));
                fx.Add(new(SlotName(5), PreMixGuid));
                fx.Add(new(SlotName(7), PostMixGuid));
                fx.Add(new(ModeName(5), MultiText: [DefaultProcessingMode]));
                fx.Add(new(ModeName(7), MultiText: [DefaultProcessingMode]));
                break;
        }

        return new(ApoProvisioningReadiness.Ready, mode, fx, child,
            "Device-specific registration can be staged without discarding the original audio effects. " +
            "Changes may not become audible until Windows Audio or Windows is restarted.");
    }

    private static ApoEndpointRegistrationPlan Block(
        ApoProvisioningReadiness reason, string message) =>
        new(reason, null, Array.Empty<ApoRegistryMutation>(),
            Array.Empty<ApoRegistryMutation>(), message);
}
