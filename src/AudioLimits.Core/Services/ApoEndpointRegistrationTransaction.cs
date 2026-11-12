using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;

namespace AudioLimits.Core.Services;

public enum ApoEndpointInstallResult
{
    RegisteredRestartRequired,
    RolledBack,
    AlreadyInstalled,
    UnsupportedEndpoint,
    NoAdministratorPrivileges,
    FailedAndRolledBack,
    RollbackIncomplete
}

public enum ApoModeFallbackResult
{
    SwitchedRestartRequired,
    UnsupportedOrChanged,
    NotAuthorized,
    FailedAndRolledBack,
    RollbackIncomplete
}

public enum ApoEndpointVerificationResult
{
    StageInitialized,
    AlreadyInitialized,
    RestartRequired,
    MissingRequiredStage,
    UnsafeOrUnsupported,
    NoAdministratorPrivileges
}

public enum ApoSetupPendingState
{
    None,
    RestartRequired,
    ActivationVerificationRequired,
    CompatibilityFixRequired,
    RecoveryRequired
}

public sealed class ApoEndpointRegistrationTransaction
{
    private const string RenderRoot =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";
    private const string ChildRoot = @"SOFTWARE\EqualizerAPO\Child APOs";
    private const string SetupDirName = "AudioLimits.EndpointSetup.Backups";
    private const string FxTitle = "{b725f130-47ef-101a-a5f1-02608c9eebac},10";

    private readonly EqualizerApoService _apo;

    public ApoEndpointRegistrationTransaction(EqualizerApoService apo) => _apo = apo;

    public static string BackupPath(string endpointGuid) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            SetupDirName,
            $"apo-{Normalize(endpointGuid).Trim('{', '}')}.json");

    public static ApoSetupPendingState GetPendingState(string endpointGuid)
    {
        var path = BackupPath(endpointGuid);
        if (!File.Exists(path))
            return ApoSetupPendingState.None;
        try
        {
            var state = ReadBackup(path);
            var bootTime = DateTimeOffset.UtcNow -
                TimeSpan.FromMilliseconds(Environment.TickCount64);
            return ClassifyPendingState(state, bootTime);
        }
        catch
        {
            return ApoSetupPendingState.RecoveryRequired;
        }
    }

    // Prevent new or automatic caps until a newly registered endpoint has
    // passed both the requested restart and an actual APO-stage init test.
    public static bool IsSetupUnsafeToUse(string endpointGuid) =>
        GetPendingState(endpointGuid) != ApoSetupPendingState.None;

    internal static ApoSetupPendingState ClassifyPendingState(
        ApoRegistryBackup state, DateTimeOffset bootTime) =>
        state.Phase switch
        {
            "RolledBack" or "StageInitialized" => ApoSetupPendingState.None,
            "RegisteredRestartRequired" or "ModeSwitchRestartRequired" =>
                RequiresRestart(state, bootTime)
                    ? ApoSetupPendingState.RestartRequired
                    : ApoSetupPendingState.ActivationVerificationRequired,
            "StageNotInitialized" => ApoSetupPendingState.CompatibilityFixRequired,
            _ => ApoSetupPendingState.RecoveryRequired
        };

    internal static bool RequiresRestart(
        ApoRegistryBackup state, DateTimeOffset bootTime) =>
        state.Phase switch
        {
            "Prepared" or "RollbackIncomplete" => true,
            "RolledBack" => false,
            // An actual OS boot after registration clears *pending*, but
            // does not prove the APO is audible or running in the engine.
            "RegisteredRestartRequired" =>
                state.CreatedUtc >= bootTime.AddSeconds(-3),
            "ModeSwitchRestartRequired" =>
                state.FallbackChangedUtc is null ||
                state.FallbackChangedUtc >= bootTime.AddSeconds(-3),
            _ => true
        };

    public ApoEndpointInstallResult Install(string endpointGuid)
    {
        var guid = Normalize(endpointGuid);
        if (!IsAdministrator())
            return ApoEndpointInstallResult.NoAdministratorPrivileges;

        using var mutex = new Mutex(false, @"Global\AudioLimits.EndpointSetup.v1");
        if (!mutex.WaitOne(TimeSpan.FromSeconds(15)))
            return ApoEndpointInstallResult.UnsupportedEndpoint;

        try
        {
            return InstallLocked(guid);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// A registry registration isn't evidence that audiodg initialized the
    /// processing stage selected by Audio Limits. Gate the newly provisioned
    /// endpoint until that exact stage has initialized during silent playback.
    /// No audio service restart, endpoint-volume change, or test sound.
    /// </summary>
    public ApoEndpointVerificationResult VerifyActivation(string endpointGuid)
    {
        var guid = Normalize(endpointGuid);
        if (!IsAdministrator())
            return ApoEndpointVerificationResult.NoAdministratorPrivileges;

        using var mutex = new Mutex(false, @"Global\AudioLimits.EndpointSetup.v1");
        if (!mutex.WaitOne(TimeSpan.FromSeconds(15)))
            return ApoEndpointVerificationResult.UnsafeOrUnsupported;

        try
        {
            EnsureSecureBackupDirectory();
            var path = BackupPath(guid);
            if (!File.Exists(path) || !VerifyRegisteredApoClasses() ||
                !_apo.IsActiveForEndpoint(guid))
                return ApoEndpointVerificationResult.UnsafeOrUnsupported;

            var backup = ReadBackup(path);
            if (backup.EndpointGuid != guid)
                return ApoEndpointVerificationResult.UnsafeOrUnsupported;

            var pending = GetPendingState(guid);
            if (pending == ApoSetupPendingState.None &&
                backup.Phase == "StageInitialized")
                return ApoEndpointVerificationResult.AlreadyInitialized;
            if (pending == ApoSetupPendingState.RestartRequired)
                return ApoEndpointVerificationResult.RestartRequired;
            if (pending != ApoSetupPendingState.ActivationVerificationRequired ||
                backup.Phase is not ("RegisteredRestartRequired" or "ModeSwitchRestartRequired"))
                return ApoEndpointVerificationResult.UnsafeOrUnsupported;

            if (Process.GetProcessesByName("DeviceSelector").Length > 0 ||
                Process.GetProcessesByName("Configurator").Length > 0)
                return ApoEndpointVerificationResult.UnsafeOrUnsupported;

            var stage = _apo.GetPreferredProcessingStage(guid);
            if (stage is not (ApoProcessingStage.PreMix or ApoProcessingStage.PostMix))
                return ApoEndpointVerificationResult.UnsafeOrUnsupported;

            // A missing event is a fail-closed result: the selected stage may
            // not be supported by this driver (as demonstrated by VB-CABLE
            // with SFX/EFX). Do not infer success from a loaded DLL.
            // Mutex ownership is bound to the thread that called WaitOne.
            // Blocking the dedicated elevated helper process here keeps
            // acquisition and ReleaseMutex on the same thread.
            var detected = ApoRuntimeStageProbe.TryVerifyAsync(
                guid, stage.ToString()).GetAwaiter().GetResult();
            if (!detected)
            {
                // A real failed init test is required before a one-time
                // compatibility-mode change can even be offered.
                WriteBackup(path, backup with { Phase = "StageNotInitialized" });
                return ApoEndpointVerificationResult.MissingRequiredStage;
            }

            WriteBackup(path, backup with { Phase = "StageInitialized" });
            AppLog.Info(
                $"APO_STAGE_INIT_VERIFIED endpoint={guid}; stage={stage}; no-volume-change");
            return ApoEndpointVerificationResult.StageInitialized;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Audio stage verification failed for {guid}", ex);
            return ApoEndpointVerificationResult.UnsafeOrUnsupported;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public static bool CanOfferCompatibleMode(string endpointGuid)
    {
        try
        {
            var backup = ReadBackup(BackupPath(endpointGuid));
            return backup.Phase == "StageNotInitialized" &&
                backup.FallbackIntendedFxChanges is null &&
                backup.FallbackOriginalFxValues is null &&
                backup.OriginalFxValues.Count > 0 &&
                backup.OriginalFxValues.All(x => !x.IsPresent);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// One bounded fallback after a *real* failed stage-init test. Only
    /// clean SFX/EFX registrations created by our own helper are eligible.
    /// This never restarts Windows Audio or raises the endpoint volume.
    /// </summary>
    public ApoModeFallbackResult TrySwitchToCompatibleMode(string endpointGuid)
    {
        var guid = Normalize(endpointGuid);
        if (!IsAdministrator())
            return ApoModeFallbackResult.NotAuthorized;

        using var mutex = new Mutex(false, @"Global\AudioLimits.EndpointSetup.v1");
        if (!mutex.WaitOne(TimeSpan.FromSeconds(15)))
            return ApoModeFallbackResult.UnsupportedOrChanged;

        try
        {
            if (!VerifyRegisteredApoClasses() ||
                !_apo.IsActiveForEndpoint(guid) ||
                _apo.GetPreferredProcessingStage(guid) != ApoProcessingStage.PostMix ||
                Process.GetProcessesByName("DeviceSelector").Length > 0 ||
                Process.GetProcessesByName("Configurator").Length > 0 ||
                !new AudioDeviceService().GetActiveRenderDevices().Any(x =>
                    string.Equals(x.EndpointGuid, guid, StringComparison.OrdinalIgnoreCase)))
                return ApoModeFallbackResult.UnsupportedOrChanged;

            EnsureSecureBackupDirectory();
            var path = BackupPath(guid);
            if (!File.Exists(path))
                return ApoModeFallbackResult.UnsupportedOrChanged;

            var backup = ReadBackup(path);
            if (backup.EndpointGuid != guid)
                return ApoModeFallbackResult.UnsupportedOrChanged;

            using var hklm = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64);
            using var fx = ApoRegistryValueAccess.OpenExisting(
                hklm, $@"{RenderRoot}\{guid}\FxProperties");
            using var child = hklm.OpenSubKey($@"{ChildRoot}\{guid}");
            var plan = ApoModeFallbackPlanner.Build(backup, fx, child);
            if (!plan.CanSwitch)
            {
                AppLog.Info($"APO fallback refused for {guid}: {plan.Reason}");
                return ApoModeFallbackResult.UnsupportedOrChanged;
            }

            var original = Capture(fx, plan.Changes.Select(x => x.Name));
            var prepared = backup with
            {
                Phase = "ModeSwitchPrepared",
                FallbackChangedUtc = DateTimeOffset.UtcNow,
                FallbackOriginalFxValues = original,
                FallbackIntendedFxChanges = plan.Changes.ToArray()
            };
            // Recovery journal must reach disk before the first registry write.
            WriteBackup(path, prepared);
            try
            {
                foreach (var change in plan.Changes)
                    Apply(fx, change);
                foreach (var change in plan.Changes)
                    Verify(fx, change);

                WriteBackup(path, prepared with
                {
                    Phase = "ModeSwitchRestartRequired"
                });
                AppLog.Info(
                    $"APO mode fallback SFX/EFX -> SFX/MFX registered for {guid}; restart required");
                return ApoModeFallbackResult.SwitchedRestartRequired;
            }
            catch (Exception ex)
            {
                AppLog.Error($"APO mode fallback failed for {guid}", ex);
                try
                {
                    ApoModeFallbackPlanner.RestoreCaptured(fx, prepared);
                    WriteBackup(path, prepared with { Phase = "StageNotInitialized" });
                    return ApoModeFallbackResult.FailedAndRolledBack;
                }
                catch (Exception rollbackError)
                {
                    AppLog.Error(
                        $"APO mode fallback rollback incomplete for {guid}", rollbackError);
                    try
                    {
                        WriteBackup(path, prepared with
                        {
                            Phase = "ModeSwitchRollbackIncomplete"
                        });
                    }
                    catch (Exception writeError)
                    {
                        AppLog.Error("Could not persist mode rollback failure state", writeError);
                    }
                    return ApoModeFallbackResult.RollbackIncomplete;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Compatibility mode change aborted for {guid}", ex);
            return ApoModeFallbackResult.UnsupportedOrChanged;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Owner-requested recovery after a crash/partial setup, never automatic.
    /// Only rolls back when affected values are still either the original
    /// snapshot or the values our helper intended to write. If a driver or
    /// another installer has since changed the endpoint, stop for manual review.
    /// </summary>
    public ApoEndpointInstallResult RestoreInterrupted(string endpointGuid)
    {
        var guid = Normalize(endpointGuid);
        if (!IsAdministrator())
            return ApoEndpointInstallResult.NoAdministratorPrivileges;

        using var mutex = new Mutex(false, @"Global\AudioLimits.EndpointSetup.v1");
        if (!mutex.WaitOne(TimeSpan.FromSeconds(15)))
            return ApoEndpointInstallResult.UnsupportedEndpoint;

        try
        {
            EnsureSecureBackupDirectory();
            var path = BackupPath(guid);
            if (!File.Exists(path))
                return ApoEndpointInstallResult.UnsupportedEndpoint;
            var backup = ReadBackup(path);
            if (backup.EndpointGuid != guid)
                return ApoEndpointInstallResult.UnsupportedEndpoint;

            // Repair an interrupted mode switch by reverting only the four
            // values in its durable journal. Never delete the original
            // Equalizer APO installation or its manufacturer-effect chain.
            if (backup.Phase is "ModeSwitchPrepared" or
                "ModeSwitchRollbackIncomplete")
                return RestoreInterruptedModeSwitch(guid, path, backup);

            if (backup.Phase is not ("Prepared" or "RollbackIncomplete"))
                return ApoEndpointInstallResult.UnsupportedEndpoint;

            using var hklm = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64);
            using var endpoint = hklm.OpenSubKey(
                $@"{RenderRoot}\{guid}", writable: false);
            using var childRoot = hklm.OpenSubKey(ChildRoot, writable: true);
            if (endpoint is null)
                return ApoEndpointInstallResult.UnsupportedEndpoint;
            using (var fx = endpoint.OpenSubKey("FxProperties"))
            using (var child = childRoot?.OpenSubKey(guid))
            {
                if (!CanRestoreOwnValues(fx, child, backup))
                    return ApoEndpointInstallResult.UnsupportedEndpoint;
            }

            try
            {
                using var writableFx = ApoRegistryValueAccess.OpenExisting(
                    hklm, $@"{RenderRoot}\{guid}\FxProperties");
                RestoreOriginal(endpoint, childRoot, backup, writableFx);
                WriteBackup(path, backup with { Phase = "RolledBack" });
                return ApoEndpointInstallResult.RolledBack;
            }
            catch (Exception ex)
            {
                AppLog.Error($"Interrupted setup rollback could not complete for {guid}", ex);
                try { WriteBackup(path, backup with { Phase = "RollbackIncomplete" }); }
                catch { }
                return ApoEndpointInstallResult.RollbackIncomplete;
            }
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private ApoEndpointInstallResult RestoreInterruptedModeSwitch(
        string guid, string path, ApoRegistryBackup backup)
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine, RegistryView.Registry64);
            using var readFx = hklm.OpenSubKey(
                $@"{RenderRoot}\{guid}\FxProperties");
            using var child = hklm.OpenSubKey($@"{ChildRoot}\{guid}");
            if (!ApoModeFallbackPlanner.CanRestoreInterrupted(
                    backup, readFx, child))
                return ApoEndpointInstallResult.UnsupportedEndpoint;

            using var writableFx = ApoRegistryValueAccess.OpenExisting(
                hklm, $@"{RenderRoot}\{guid}\FxProperties");
            ApoModeFallbackPlanner.RestoreCaptured(writableFx, backup);
            WriteBackup(path, backup with { Phase = "StageNotInitialized" });
            AppLog.Info(
                $"Interrupted APO mode switch safely rolled back for {guid}");
            return ApoEndpointInstallResult.RolledBack;
        }
        catch (Exception ex)
        {
            AppLog.Error(
                $"Interrupted APO mode switch recovery failed for {guid}", ex);
            try
            {
                WriteBackup(path, backup with
                {
                    Phase = "ModeSwitchRollbackIncomplete"
                });
            }
            catch (Exception journalError)
            {
                AppLog.Error("Could not save incomplete mode-switch recovery", journalError);
            }
            return ApoEndpointInstallResult.RollbackIncomplete;
        }
    }

    internal static bool CanRestoreOwnValues(
        RegistryKey? fx, RegistryKey? child, ApoRegistryBackup backup)
    {
        foreach (var original in backup.OriginalFxValues)
        {
            var change = backup.IntendedFxChanges.FirstOrDefault(x =>
                string.Equals(x.Name, original.Name, StringComparison.OrdinalIgnoreCase));
            if (!MatchesOriginal(fx, original) &&
                (change is null || !MatchesMutation(fx, change)))
                return false;
        }

        if (child is null)
            return true;
        if (child.SubKeyCount > 0)
            return false; // Never delete another installer's nested data.

        var expected = backup.IntendedChildValues.ToDictionary(
            x => x.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var key in child.GetValueNames())
        {
            if (!expected.TryGetValue(key, out var change) ||
                !MatchesMutation(child, change))
                return false;
        }
        return true;
    }

    internal static bool MatchesOriginal(RegistryKey? key, RegistryValueBackup original)
    {
        var found = key?.GetValueNames().Contains(
            original.Name, StringComparer.OrdinalIgnoreCase) == true;
        if (!original.IsPresent)
            return !found;
        if (!found || key!.GetValueKind(original.Name) != original.Kind)
            return false;

        var data = key.GetValue(
            original.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return original.Kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString =>
                string.Equals(data as string, original.Text, StringComparison.Ordinal),
            RegistryValueKind.MultiString =>
                data is string[] a && a.SequenceEqual(original.MultiText ?? []),
            RegistryValueKind.Binary or RegistryValueKind.None =>
                data is byte[] b && b.SequenceEqual(original.Binary ?? []),
            RegistryValueKind.DWord or RegistryValueKind.QWord =>
                data is not null && Convert.ToInt64(data) == original.Numeric,
            _ => false
        };
    }

    internal static bool MatchesMutation(RegistryKey? key, ApoRegistryMutation mutation)
    {
        var found = key?.GetValueNames().Contains(
            mutation.Name, StringComparer.OrdinalIgnoreCase) == true;
        if (mutation.Delete)
            return !found;
        if (!found)
            return false;

        if (mutation.MultiText is not null)
            return key!.GetValueKind(mutation.Name) == RegistryValueKind.MultiString &&
                key.GetValue(mutation.Name) is string[] list &&
                list.SequenceEqual(mutation.MultiText);

        return key!.GetValueKind(mutation.Name) == RegistryValueKind.String &&
            string.Equals(key.GetValue(mutation.Name) as string,
                mutation.Text, StringComparison.OrdinalIgnoreCase);
    }

    private ApoEndpointInstallResult InstallLocked(string guid)
    {
        if (!_apo.IsInstalled || !VerifyRegisteredApoClasses() ||
            Process.GetProcessesByName("DeviceSelector").Length > 0 ||
            Process.GetProcessesByName("Configurator").Length > 0)
            return ApoEndpointInstallResult.UnsupportedEndpoint;

        // Never write to a disconnected, disabled, or capture endpoint: an
        // endpoint GUID cannot be used as a general-purpose registry selector.
        if (!new AudioDeviceService().GetActiveRenderDevices()
                .Any(x => string.Equals(x.EndpointGuid, guid,
                    StringComparison.OrdinalIgnoreCase)))
            return ApoEndpointInstallResult.UnsupportedEndpoint;

        var snapshot = ApoEndpointSetupInspector.ReadRegistration(guid);
        var plan = ApoEndpointRegistrationPlanner.Build(
            snapshot, _apo.IsActiveForEndpoint(guid));
        if (plan.Readiness == ApoProvisioningReadiness.AlreadyRegistered)
            return ApoEndpointInstallResult.AlreadyInstalled;
        if (!plan.CanInstall)
            return ApoEndpointInstallResult.UnsupportedEndpoint;

        // A recovery backup must not live in an ordinary user-writable
        // ProgramData subfolder. Reject pre-created or tampered directories.
        EnsureSecureBackupDirectory();
        var path = BackupPath(guid);
        // A previously interrupted or successful setup cannot be overwritten
        // by a new attempt. A fully rolled-back attempt may be archived and
        // retried, but its original recovery evidence is retained.
        if (File.Exists(path))
        {
            var previous = ReadBackup(path);
            if (previous.Phase != "RolledBack" || previous.EndpointGuid != guid)
                return ApoEndpointInstallResult.UnsupportedEndpoint;

            File.Move(path, path + ".rolledback-" +
                DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json");
        }

        using var hklm = RegistryKey.OpenBaseKey(
            RegistryHive.LocalMachine, RegistryView.Registry64);
        using var endpoint = hklm.OpenSubKey($@"{RenderRoot}\{guid}", writable: false);
        if (endpoint is null)
            return ApoEndpointInstallResult.UnsupportedEndpoint;

        using var eapoRoot = hklm.OpenSubKey(@"SOFTWARE\EqualizerAPO", writable: true);
        if (eapoRoot is null)
            return ApoEndpointInstallResult.UnsupportedEndpoint;
        using var existingChildRoot = eapoRoot.OpenSubKey("Child APOs");
        using var existingChild = existingChildRoot?.OpenSubKey(guid);
        if (existingChild is not null)
            return ApoEndpointInstallResult.UnsupportedEndpoint;

        // Windows normally grants value-write permissions on FxProperties,
        // but denies creating subkeys under the protected endpoint. Request
        // only KEY_QUERY_VALUE + KEY_SET_VALUE for this existing key.
        using var existingFx = ApoRegistryValueAccess.OpenExisting(
            hklm, $@"{RenderRoot}\{guid}\FxProperties");

        // Freeze the exact affected original registry values before any write.
        // A snapshot of the whole endpoint is intentionally unnecessary: unrelated
        // OEM properties are never rewritten during either commit or rollback.
        var operations = plan.FxChanges.ToList();
        if (!snapshot.FxKeyExists)
            operations.Add(new ApoRegistryMutation(FxTitle, "Equalizer APO"));
        var original = Capture(existingFx, operations.Select(x => x.Name));
        var backup = new ApoRegistryBackup(
            guid, DateTimeOffset.UtcNow, "Prepared",
            snapshot.FxKeyExists, original, operations, plan.ChildValues.ToList());

        WriteBackup(path, backup); // Durable intent precedes registry mutation.
        try
        {
            // Reuse the narrowly permissioned handle; do not ask for the
            // broader KEY_WRITE access denied by the audio endpoint ACL.
            var fx = existingFx;

            using var childParent = eapoRoot.CreateSubKey("Child APOs",
                RegistryKeyPermissionCheck.ReadWriteSubTree);
            using var child = childParent?.CreateSubKey(guid,
                RegistryKeyPermissionCheck.ReadWriteSubTree);
            if (child is null)
                throw new UnauthorizedAccessException("Equalizer APO child registry cannot be created.");

            foreach (var mutation in plan.ChildValues)
                Apply(child, mutation);
            foreach (var mutation in operations)
            {
                // Equalizer APO only creates processing mode values if absent;
                // never overwrite an OEM processing-mode list.
                if (mutation.Name.StartsWith(ApoEndpointRegistrationPlanner.ModeGuid,
                        StringComparison.OrdinalIgnoreCase) &&
                    fx.GetValueNames().Contains(mutation.Name,
                        StringComparer.OrdinalIgnoreCase))
                    continue;

                Apply(fx, mutation);
            }

            foreach (var mutation in plan.ChildValues)
                Verify(child, mutation);
            foreach (var mutation in operations.Where(x =>
                !x.Name.StartsWith(ApoEndpointRegistrationPlanner.ModeGuid,
                    StringComparison.OrdinalIgnoreCase) ||
                !original.Any(y => y.Name == x.Name && y.IsPresent)))
                Verify(fx, mutation);

            if (!_apo.IsActiveForEndpoint(guid))
                throw new InvalidOperationException("Equalizer APO registration was not recognized.");

            WriteBackup(path, backup with { Phase = "RegisteredRestartRequired" });
            return ApoEndpointInstallResult.RegisteredRestartRequired;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Endpoint APO setup failed for {guid}", ex);
            try
            {
                using var rollbackChildRoot = eapoRoot.OpenSubKey("Child APOs", writable: true);
                RestoreOriginal(endpoint, rollbackChildRoot, backup, existingFx);
                WriteBackup(path, backup with { Phase = "RolledBack" });
                return ApoEndpointInstallResult.FailedAndRolledBack;
            }
            catch (Exception rollbackError)
            {
                AppLog.Error($"Endpoint APO setup rollback failed for {guid}", rollbackError);
                try { WriteBackup(path, backup with { Phase = "RollbackIncomplete" }); }
                catch { }
                return ApoEndpointInstallResult.RollbackIncomplete;
            }
        }
    }

    internal static void RestoreOriginal(
        RegistryKey endpoint, RegistryKey? childParent, ApoRegistryBackup backup,
        RegistryKey? writableFxOverride = null)
    {
        // Restore the old effect registrations BEFORE deleting the child APO
        // record, avoiding a window where an installed Equalizer APO has lost
        // access to its original manufacturer's effect chain.
        var removeNewFxKey = false;
        // The production endpoint's FxProperties handle is opened with only
        // SET_VALUE and QUERY_VALUE; request broad writable access only in
        // isolated unit-test keys when no override was provided.
        using (var openedFx = writableFxOverride is null
            ? endpoint.OpenSubKey("FxProperties", writable: true)
            : null)
        {
            var fx = writableFxOverride ?? openedFx;
            if (fx is not null)
            {
                foreach (var value in backup.OriginalFxValues)
                {
                    if (value.IsPresent)
                        Restore(fx, value);
                    else
                        fx.DeleteValue(value.Name, throwOnMissingValue: false);
                }

                removeNewFxKey = !backup.HadFxKey &&
                    fx.ValueCount == 0 && fx.SubKeyCount == 0;
            }
        }
        // All open handles must be disposed before deleting the key.
        if (removeNewFxKey)
            endpoint.DeleteSubKey("FxProperties", throwOnMissingSubKey: false);

        childParent?.DeleteSubKeyTree(backup.EndpointGuid, throwOnMissingSubKey: false);
    }

    public static IReadOnlyList<RegistryValueBackup> Capture(
        RegistryKey? key, IEnumerable<string> names)
    {
        var existingNames = key?.GetValueNames() ?? Array.Empty<string>();
        var values = new List<RegistryValueBackup>();
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (key is null || !existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(new(name, false, RegistryValueKind.Unknown, null, null, null, null));
                continue;
            }

            var kind = key.GetValueKind(name);
            var data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            values.Add(kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString =>
                    new(name, true, kind, data as string, null, null, null),
                RegistryValueKind.MultiString =>
                    new(name, true, kind, null, data as string[], null, null),
                RegistryValueKind.Binary or RegistryValueKind.None =>
                    new(name, true, kind, null, null, data as byte[], null),
                RegistryValueKind.DWord or RegistryValueKind.QWord =>
                    new(name, true, kind, null, null, null, Convert.ToInt64(data)),
                _ => throw new InvalidOperationException(
                    $"Cannot safely back up Windows registry value {name} of kind {kind}")
            });
        }
        return values;
    }

    internal static void Restore(RegistryKey key, RegistryValueBackup value)
    {
        object data = value.Kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString => value.Text!,
            RegistryValueKind.MultiString => value.MultiText!,
            RegistryValueKind.Binary or RegistryValueKind.None => value.Binary!,
            RegistryValueKind.DWord => checked((int)value.Numeric!.Value),
            RegistryValueKind.QWord => value.Numeric!.Value,
            _ => throw new InvalidDataException("Unsupported registry rollback type.")
        };
        key.SetValue(value.Name, data, value.Kind);
    }

    internal static void Apply(RegistryKey key, ApoRegistryMutation mutation)
    {
        if (mutation.Delete)
            key.DeleteValue(mutation.Name, throwOnMissingValue: false);
        else if (mutation.MultiText is not null)
            key.SetValue(mutation.Name, mutation.MultiText, RegistryValueKind.MultiString);
        else
            key.SetValue(mutation.Name, mutation.Text!, RegistryValueKind.String);
    }

    internal static void Verify(RegistryKey key, ApoRegistryMutation mutation)
    {
        var has = key.GetValueNames().Contains(
            mutation.Name, StringComparer.OrdinalIgnoreCase);
        if (mutation.Delete)
        {
            if (has)
                throw new InvalidOperationException("Registry removal failed.");
            return;
        }
        if (!has)
            throw new InvalidOperationException("Registry write disappeared.");
        if (mutation.MultiText is { } multi)
        {
            if (key.GetValueKind(mutation.Name) != RegistryValueKind.MultiString ||
                key.GetValue(mutation.Name) is not string[] result ||
                !multi.SequenceEqual(result))
                throw new InvalidOperationException("Processing mode verification failed.");
        }
        else if (key.GetValue(mutation.Name) is not string value ||
                 !string.Equals(value, mutation.Text, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("APO registration verification failed.");
    }

    private static bool VerifyRegisteredApoClasses()
    {
        try
        {
            using var hkcr = RegistryKey.OpenBaseKey(
                RegistryHive.ClassesRoot, RegistryView.Registry64);
            string? registeredDll = null;
            foreach (var clsid in new[]
                     { ApoEndpointRegistrationPlanner.PreMixGuid,
                       ApoEndpointRegistrationPlanner.PostMixGuid })
            {
                using var server = hkcr.OpenSubKey($@"CLSID\{clsid}\InprocServer32");
                var path = (server?.GetValue(null) as string)?.Trim('"');
                if (string.IsNullOrWhiteSpace(path) ||
                    !string.Equals(Path.GetFileName(path),
                        "EqualizerAPO.dll", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(path))
                    return false;

                // Only use the installation layout that has been audited,
                // never silently write into an unrecognized future EAPO fork.
                var version = FileVersionInfo.GetVersionInfo(path);
                if (version.FileMajorPart != 1 ||
                    version.FileMinorPart != 4 ||
                    version.FileBuildPart != 2)
                    return false;

                if (registeredDll is not null &&
                    !string.Equals(registeredDll, path, StringComparison.OrdinalIgnoreCase))
                    return false;
                registeredDll = path;
            }
            return true;
        }
        catch { return false; }
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }


    private static readonly SecurityIdentifier AdminsSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier SystemSid =
        new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier ReadersSid =
        new(WellKnownSidType.AuthenticatedUserSid, null);

    /// <summary>
    /// Create a protected, admin-owned recovery directory in one operation.
    /// The default ProgramData ACL lets ordinary users create children;
    /// trusting a backup from such a location would turn a UAC helper into
    /// an arbitrary registry-writer through manipulated rollback content.
    /// </summary>
    private static void EnsureSecureBackupDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            SetupDirName);

        if (!Directory.Exists(directory))
            FileSystemAclExtensions.CreateDirectory(CreateBackupAcl(), directory);

        var info = new DirectoryInfo(directory);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException(
                "The audio backup directory is a symbolic link or junction. No changes were made.");
        var current = FileSystemAclExtensions.GetAccessControl(
            info, AccessControlSections.Access | AccessControlSections.Owner);
        if (!IsSecureBackupAcl(current))
            throw new UnauthorizedAccessException(
                "The Audio Limits backup directory is not protected from ordinary users. " +
                "No endpoint changes were made.");
    }

    internal static DirectorySecurity CreateBackupAcl()
    {
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        acl.SetOwner(AdminsSid);
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        acl.AddAccessRule(new FileSystemAccessRule(AdminsSid,
            FileSystemRights.FullControl, inheritance, PropagationFlags.None,
            AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(SystemSid,
            FileSystemRights.FullControl, inheritance, PropagationFlags.None,
            AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(ReadersSid,
            FileSystemRights.ReadAndExecute, inheritance, PropagationFlags.None,
            AccessControlType.Allow));
        return acl;
    }

    internal static bool IsSecureBackupAcl(DirectorySecurity acl)
    {
        if (!acl.AreAccessRulesProtected)
            return false;

        SecurityIdentifier? owner;
        try { owner = acl.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier; }
        catch { return false; }
        if (owner is null || (!owner.Equals(AdminsSid) && !owner.Equals(SystemSid)))
            return false;

        const FileSystemRights writerRights =
            FileSystemRights.Write | FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

        foreach (FileSystemAccessRule rule in acl.GetAccessRules(
                     includeExplicit: true, includeInherited: true,
                     targetType: typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow ||
                (rule.FileSystemRights & writerRights) == 0)
                continue;
            if (rule.IdentityReference is not SecurityIdentifier sid ||
                (!sid.Equals(AdminsSid) && !sid.Equals(SystemSid)))
                return false;
        }
        return true;
    }

    internal static void WriteBackup(string path, ApoRegistryBackup backup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                   FileShare.None))
        {
            JsonSerializer.Serialize(stream, backup);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }

    internal static ApoRegistryBackup ReadBackup(string path) =>
        JsonSerializer.Deserialize<ApoRegistryBackup>(File.ReadAllText(path))
        ?? throw new InvalidDataException("Invalid APO endpoint backup.");

    private static string Normalize(string endpointGuid) =>
        Guid.TryParse(endpointGuid, out var parsed)
            ? parsed.ToString("B").ToUpperInvariant()
            : throw new ArgumentException("Invalid playback endpoint GUID.");
}

public sealed record RegistryValueBackup(
    string Name, bool IsPresent, RegistryValueKind Kind,
    string? Text, string[]? MultiText, byte[]? Binary, long? Numeric);

public sealed record ApoRegistryBackup(
    string EndpointGuid, DateTimeOffset CreatedUtc, string Phase,
    bool HadFxKey, IReadOnlyList<RegistryValueBackup> OriginalFxValues,
    IReadOnlyList<ApoRegistryMutation> IntendedFxChanges,
    IReadOnlyList<ApoRegistryMutation> IntendedChildValues,
    DateTimeOffset? FallbackChangedUtc = null,
    IReadOnlyList<RegistryValueBackup>? FallbackOriginalFxValues = null,
    IReadOnlyList<ApoRegistryMutation>? FallbackIntendedFxChanges = null);
