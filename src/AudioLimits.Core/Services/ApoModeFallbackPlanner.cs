using Microsoft.Win32;

namespace AudioLimits.Core.Services;

/// <summary>
/// A deliberately narrow, once-only SFX/EFX -> SFX/MFX fallback for an endpoint
/// that failed its actual PostMix initialization test. The current driver
/// registrations and our original backed-up intentions must match exactly.
/// No manufacturer effect may be displaced. All writes and crash recovery
/// belong to ApoEndpointRegistrationTransaction, not this planner.
/// </summary>
public sealed record ApoModeFallbackPlan(
    bool CanSwitch,
    IReadOnlyList<ApoRegistryMutation> Changes,
    string Reason)
{
    public static ApoModeFallbackPlan Refuse(string reason) =>
        new(false, Array.Empty<ApoRegistryMutation>(), reason);
}

public static class ApoModeFallbackPlanner
{
    public static ApoModeFallbackPlan Build(
        ApoRegistryBackup backup,
        RegistryKey? fx,
        RegistryKey? child)
    {
        if (backup.Phase != "StageNotInitialized")
            return ApoModeFallbackPlan.Refuse("The selected stage has not failed its verified initialization test.");

        if (backup.FallbackIntendedFxChanges is not null ||
            backup.FallbackOriginalFxValues is not null)
            return ApoModeFallbackPlan.Refuse("A compatibility mode was already attempted.");

        if (!backup.HadFxKey || fx is null || child is null ||
            child.SubKeyCount != 0)
            return ApoModeFallbackPlan.Refuse("The saved device registration cannot be trusted.");

        // Automatic migration is restricted to clean endpoints. An OEM APO or
        // processing-mode value from before our installation must not be
        // overwritten or disconnected by a speculative fallback.
        if (backup.OriginalFxValues.Count == 0 ||
            backup.OriginalFxValues.Any(x => x.IsPresent))
            return ApoModeFallbackPlan.Refuse("Original driver effects require advanced setup.");

        var initial = backup.IntendedFxChanges;
        if (initial.Count != 6 ||
            !ContainsText(initial, ApoEndpointRegistrationPlanner.SlotName(5),
                ApoEndpointRegistrationPlanner.PreMixGuid) ||
            !ContainsText(initial, ApoEndpointRegistrationPlanner.SlotName(7),
                ApoEndpointRegistrationPlanner.PostMixGuid) ||
            !ContainsMode(initial, 5) ||
            !ContainsMode(initial, 7) ||
            !ContainsDelete(initial, 1) ||
            !ContainsDelete(initial, 2))
            return ApoModeFallbackPlan.Refuse("The prior installation mode was not the supported SFX/EFX layout.");

        // Reject displaced or conflicting pre-existing driver registrations,
        // including multi-effect registrations in additional processing slots.
        var names = fx.GetValueNames();
        foreach (var slot in new[] { 6, 13, 14, 15 })
        {
            if (names.Contains(ApoEndpointRegistrationPlanner.SlotName(slot),
                    StringComparer.OrdinalIgnoreCase) ||
                names.Contains(ApoEndpointRegistrationPlanner.ModeName(slot),
                    StringComparer.OrdinalIgnoreCase))
                return ApoModeFallbackPlan.Refuse("Another effect is registered in a required processing slot.");
        }

        if (initial.Any(x => !ApoEndpointRegistrationTransaction.MatchesMutation(fx, x)))
            return ApoModeFallbackPlan.Refuse("Audio effect settings have changed since the original installation.");

        if (backup.IntendedChildValues.Count == 0 ||
            child.GetValueNames().Length != backup.IntendedChildValues.Count ||
            backup.IntendedChildValues.Any(x =>
                !ApoEndpointRegistrationTransaction.MatchesMutation(child, x)))
            return ApoModeFallbackPlan.Refuse("Equalizer APO child registrations changed unexpectedly.");

        var result = new[]
        {
            new ApoRegistryMutation(ApoEndpointRegistrationPlanner.SlotName(6),
                ApoEndpointRegistrationPlanner.PostMixGuid),
            new ApoRegistryMutation(ApoEndpointRegistrationPlanner.ModeName(6),
                MultiText: [ApoEndpointRegistrationPlanner.DefaultProcessingMode]),
            new ApoRegistryMutation(ApoEndpointRegistrationPlanner.SlotName(7)),
            new ApoRegistryMutation(ApoEndpointRegistrationPlanner.ModeName(7))
        };

        return new(true, result, "Try SFX/MFX without changing manufacturer APOs or Windows volume. " +
            "A Windows restart and a fresh post-mix initialization check are required.");
    }


    /// <summary>
    /// Recovery accepts only our original SFX/EFX values or the four values
    /// that the interrupted mode switch intended to write. Never overwrite a
    /// later driver installer or another Audio Limits change.
    /// </summary>
    public static bool CanRestoreInterrupted(
        ApoRegistryBackup backup,
        RegistryKey? fx,
        RegistryKey? child)
    {
        if (backup.Phase is not ("ModeSwitchPrepared" or "ModeSwitchRollbackIncomplete") ||
            backup.FallbackOriginalFxValues is null ||
            backup.FallbackIntendedFxChanges is null ||
            fx is null || child is null || child.SubKeyCount != 0)
            return false;

        // The journal must describe exactly the four scoped values of our
        // audited one-time fallback, not arbitrary registry modifications.
        var expected = new[]
        {
            new ApoRegistryMutation(ApoEndpointRegistrationPlanner.SlotName(6),
                ApoEndpointRegistrationPlanner.PostMixGuid),
            new ApoRegistryMutation(ApoEndpointRegistrationPlanner.ModeName(6),
                MultiText: [ApoEndpointRegistrationPlanner.DefaultProcessingMode]),
            new ApoRegistryMutation(ApoEndpointRegistrationPlanner.SlotName(7)),
            new ApoRegistryMutation(ApoEndpointRegistrationPlanner.ModeName(7))
        };
        if (backup.FallbackIntendedFxChanges.Count != expected.Length ||
            backup.FallbackOriginalFxValues.Count != expected.Length ||
            backup.FallbackIntendedFxChanges.Select(x => x.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Length ||
            backup.FallbackOriginalFxValues.Select(x => x.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Length)
            return false;

        foreach (var intended in expected)
        {
            var journalMutation = backup.FallbackIntendedFxChanges.SingleOrDefault(x =>
                string.Equals(x.Name, intended.Name, StringComparison.OrdinalIgnoreCase));
            var original = backup.FallbackOriginalFxValues.SingleOrDefault(x =>
                string.Equals(x.Name, intended.Name, StringComparison.OrdinalIgnoreCase));
            if (journalMutation is null || original is null ||
                journalMutation.Delete != intended.Delete ||
                !string.Equals(journalMutation.Text, intended.Text,
                    StringComparison.OrdinalIgnoreCase) ||
                !(journalMutation.MultiText ?? []).SequenceEqual(intended.MultiText ?? []))
                return false;
        }

        if (child.GetValueNames().Length != backup.IntendedChildValues.Count ||
            backup.IntendedChildValues.Any(x =>
                !ApoEndpointRegistrationTransaction.MatchesMutation(child, x)))
            return false;

        foreach (var original in backup.FallbackOriginalFxValues)
        {
            var change = backup.FallbackIntendedFxChanges.FirstOrDefault(x =>
                string.Equals(x.Name, original.Name, StringComparison.OrdinalIgnoreCase));
            if (change is null ||
                (!ApoEndpointRegistrationTransaction.MatchesOriginal(fx, original) &&
                 !ApoEndpointRegistrationTransaction.MatchesMutation(fx, change)))
                return false;
        }

        return true;
    }

    public static void RestoreCaptured(RegistryKey fx, ApoRegistryBackup backup)
    {
        if (backup.FallbackOriginalFxValues is null)
            throw new InvalidOperationException("Missing original effects for recovery.");

        foreach (var original in backup.FallbackOriginalFxValues)
        {
            if (original.IsPresent)
                ApoEndpointRegistrationTransaction.Restore(fx, original);
            else
                fx.DeleteValue(original.Name, throwOnMissingValue: false);
        }

        if (backup.FallbackOriginalFxValues.Any(x =>
            !ApoEndpointRegistrationTransaction.MatchesOriginal(fx, x)))
            throw new InvalidOperationException("Audio processing mode rollback verification failed.");
    }

    private static bool ContainsText(
        IReadOnlyList<ApoRegistryMutation> operations, string name, string expected) =>
        operations.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Text, expected, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsMode(IReadOnlyList<ApoRegistryMutation> operations, int slot) =>
        operations.Any(x =>
            string.Equals(x.Name, ApoEndpointRegistrationPlanner.ModeName(slot),
                StringComparison.OrdinalIgnoreCase) &&
            x.MultiText is { Length: 1 } values &&
            string.Equals(values[0], ApoEndpointRegistrationPlanner.DefaultProcessingMode,
                StringComparison.OrdinalIgnoreCase));

    private static bool ContainsDelete(IReadOnlyList<ApoRegistryMutation> operations, int slot) =>
        operations.Any(x =>
            string.Equals(x.Name, ApoEndpointRegistrationPlanner.SlotName(slot),
                StringComparison.OrdinalIgnoreCase) && x.Delete);
}
