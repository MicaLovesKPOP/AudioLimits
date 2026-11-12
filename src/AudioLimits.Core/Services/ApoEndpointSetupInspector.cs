using Microsoft.Win32;

namespace AudioLimits.Core.Services;

/// <summary>
/// Read-only inventory of the Windows playback endpoint's APO slots. This is
/// never authorization to rewrite the driver effects or to restart audio.
/// The official Equalizer APO Device Selector backs up and replaces additional
/// values; an installer must preserve that behavior and prove rollback first.
/// </summary>
public enum ApoEndpointSetupStatus
{
    Unavailable,
    AlreadyRegistered,
    EnhancementsDisabled,
    CleanRegistrationNeeded,
    ExistingDriverEffects,
    UnusualEffectValues,
    MissingFxProperties,
    AwaitingAudioRestart,
    ActivationVerificationRequired,
    CompatibilityFixRequired,
    SetupRepairRequired
}

public sealed record ApoFxSlotSnapshot(
    int Slot,
    RegistryValueKind Kind,
    string? ClassId);

public sealed record ApoFxRegistrationSnapshot(
    bool InspectionSucceeded,
    bool FxKeyExists,
    bool EnhancementsDisabled,
    IReadOnlyList<ApoFxSlotSnapshot> EffectSlots,
    bool IsCombinedBluetoothEndpoint = false)
{
    public static ApoFxRegistrationSnapshot Unavailable { get; } =
        new(false, false, false, Array.Empty<ApoFxSlotSnapshot>());
}

public sealed record ApoEndpointSetupAssessment(
    ApoEndpointSetupStatus Status,
    int ExistingEffectCount,
    bool RequiresDriverEffectPreservation,
    string Explanation)
{
    public bool CanAssumeLiveProcessing => false;

    public static ApoEndpointSetupAssessment Evaluate(
        ApoFxRegistrationSnapshot snapshot,
        bool equalizerApoRegistered)
    {
        if (!snapshot.InspectionSucceeded)
            return new(
                ApoEndpointSetupStatus.Unavailable, 0, true,
                "Audio Limits could not inspect this endpoint's driver audio processing. No setup changes were made.");

        // An equalizer's registration is not proof that its DLL is already
        // running in audiodg. Never label these results 'working audio'.
        if (snapshot.EnhancementsDisabled)
            return new(
                ApoEndpointSetupStatus.EnhancementsDisabled,
                snapshot.EffectSlots.Count, true,
                "Windows audio enhancements are disabled for this playback device. Enable audio enhancements in Windows sound settings before trying to apply a cap.");

        if (equalizerApoRegistered)
            return new(
                ApoEndpointSetupStatus.AlreadyRegistered,
                snapshot.EffectSlots.Count, true,
                "Equalizer APO is registered for this endpoint. An audio-engine restart may still be needed before its processing is effective.");

        if (snapshot.EffectSlots.Any(x =>
                x.Kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString) ||
                !Guid.TryParse(x.ClassId, out _)))
            return new(
                ApoEndpointSetupStatus.UnusualEffectValues,
                snapshot.EffectSlots.Count, true,
                "This device has unusual or unreadable audio-effect registrations. Audio Limits will not overwrite them automatically.");

        if (snapshot.EffectSlots.Count > 0)
            return new(
                ApoEndpointSetupStatus.ExistingDriverEffects,
                snapshot.EffectSlots.Count, true,
                "This device already has manufacturer or other audio effects. They must be preserved when enabling the limiter.");

        if (!snapshot.FxKeyExists)
            return new(
                ApoEndpointSetupStatus.MissingFxProperties, 0, true,
                "This endpoint has no Windows audio-effects registry key. Creating it may require driver-specific permissions.");

        return new(
            ApoEndpointSetupStatus.CleanRegistrationNeeded, 0, false,
            "This playback endpoint has no existing effects in the inspected slots. Equalizer APO still needs to be installed and verified for this endpoint.");
    }
}

public sealed class ApoEndpointSetupInspector
{
    // Official Equalizer APO 1.4.2 DeviceAPOInfo.cpp (Windows 7/8/10/11
    // registration slots). Include multi-effect slots 13/14/15: they
    // influence the upstream selector's install-mode selection.
    private static readonly int[] EffectSlots = [1, 2, 5, 6, 7, 13, 14, 15];
    private const string EffectPropertyGuid = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}";
    private const string DisabledEnhancementsProperty = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";
    private const string CombinedBluetoothProperty = "{b3f8fa53-0004-438e-9003-51a46e139bfc},41";

    private readonly EqualizerApoService _apo;

    public ApoEndpointSetupInspector(EqualizerApoService apo) => _apo = apo;

    public ApoEndpointSetupAssessment InspectPlaybackEndpoint(string endpointGuid)
    {
        if (!Guid.TryParse(endpointGuid, out var parsed))
            return ApoEndpointSetupAssessment.Evaluate(
                ApoFxRegistrationSnapshot.Unavailable, false);

        var guid = parsed.ToString("B").ToUpperInvariant();
        var snapshot = ReadRegistration(guid);
        var registered = _apo.IsActiveForEndpoint(guid);
        var pending = ApoEndpointRegistrationTransaction.GetPendingState(guid);
        if (pending == ApoSetupPendingState.RecoveryRequired)
            return new ApoEndpointSetupAssessment(
                ApoEndpointSetupStatus.SetupRepairRequired,
                snapshot.EffectSlots.Count, true,
                "An earlier device setup was interrupted or its rollback could not be verified. Do not assume this limit is active. Restore the previous audio registration from the saved backup before trying again.");
        if (pending == ApoSetupPendingState.RestartRequired)
            return new ApoEndpointSetupAssessment(
                ApoEndpointSetupStatus.AwaitingAudioRestart,
                snapshot.EffectSlots.Count, true,
                "Equalizer APO registration was changed for this device. Restart Windows before Audio Limits applies a limit. The effect is not yet verified active in the audio engine.");
        if (pending == ApoSetupPendingState.CompatibilityFixRequired)
        {
            var eligible = ApoEndpointRegistrationTransaction.CanOfferCompatibleMode(guid);
            return new ApoEndpointSetupAssessment(
                ApoEndpointSetupStatus.CompatibilityFixRequired,
                snapshot.EffectSlots.Count, true,
                eligible
                    ? "The playback device did not initialize Equalizer APO's selected post-mix stage. " +
                      "The cap is NOT active. Audio Limits can try one bounded compatibility mode with " +
                      "the original registry values protected for rollback."
                    : "The required processing stage failed its initialization test. " +
                      "A compatibility change was already tried or this driver has settings that must be " +
                      "handled manually. The cap is NOT active. Open advanced audio setup.");
        }
        if (pending == ApoSetupPendingState.ActivationVerificationRequired)
            return new ApoEndpointSetupAssessment(
                ApoEndpointSetupStatus.ActivationVerificationRequired,
                snapshot.EffectSlots.Count, true,
                "Equalizer APO is registered, but Audio Limits has not yet verified that this playback device initializes the required audio processing stage. The cap is NOT active. Verify the processing stage before applying a limit.");
        return ApoEndpointSetupAssessment.Evaluate(snapshot, registered);
    }

    public static ApoFxRegistrationSnapshot ReadRegistration(string guid)
    {
        if (!OperatingSystem.IsWindows())
            return ApoFxRegistrationSnapshot.Unavailable;

        try
        {
            // MMDevices registry state belongs to the 64-bit machine view on
            // supported Windows x64. Read-only: no ownership/ACL changes.
            using var hklm = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64);
            using var endpoint = hklm.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{guid}");
            if (endpoint is null)
                return ApoFxRegistrationSnapshot.Unavailable;

            using var props = endpoint.OpenSubKey("Properties");
            var combined = props?.GetValueNames().Contains(
                CombinedBluetoothProperty, StringComparer.OrdinalIgnoreCase) == true;
            using var fx = endpoint.OpenSubKey("FxProperties");
            if (fx is null)
                return new(true, false, false, Array.Empty<ApoFxSlotSnapshot>(), combined);

            var names = fx.GetValueNames();
            var hasEnhancementsSetting = names.Contains(
                DisabledEnhancementsProperty, StringComparer.OrdinalIgnoreCase);
            var enhancementsSetting = fx.GetValue(DisabledEnhancementsProperty);
            if (hasEnhancementsSetting && enhancementsSetting is not int)
                return ApoFxRegistrationSnapshot.Unavailable;
            var disabled = enhancementsSetting is int n && n != 0;
            var slots = new List<ApoFxSlotSnapshot>();
            foreach (var slot in EffectSlots)
            {
                var key = $"{EffectPropertyGuid},{slot}";
                if (!names.Contains(key, StringComparer.OrdinalIgnoreCase))
                    continue;
                var kind = fx.GetValueKind(key);
                var classId = fx.GetValue(key) as string;
                slots.Add(new ApoFxSlotSnapshot(slot, kind, classId));
            }

            return new(true, true, disabled, slots, combined);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not inspect APO registrations for {guid}", ex);
            return ApoFxRegistrationSnapshot.Unavailable;
        }
    }
}
