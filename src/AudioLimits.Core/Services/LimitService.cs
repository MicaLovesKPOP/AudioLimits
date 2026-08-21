using AudioLimits.Core.Models;

namespace AudioLimits.Core.Services;

public sealed class LimitService
{
    private const int ApoReloadDelayMs = 750;
    private const int TransitionMuteSettleDelayMs = 100;
    private static readonly TimeSpan MaxAutomaticRecoveryAge = TimeSpan.FromDays(30);
    private readonly AudioDeviceService _audio;
    private readonly EqualizerApoService _apo;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly SettingsLoadResult _loadResult;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsBusy { get; private set; }
    public bool InitializationComplete { get; private set; }
    public bool StateUncertain { get; private set; }
    public string? StartupIssueMessage { get; private set; }
    private string? _operationNotice;
    public bool HasPendingChange => _settings.PendingChange is not null;
    public bool CanModifyLimits =>
        InitializationComplete &&
        !StateUncertain &&
        _loadResult.IsAuthoritative &&
        !HasPendingChange;

    public LimitService(
        AudioDeviceService audio,
        EqualizerApoService apo,
        SettingsStore store,
        SettingsLoadResult loadResult)
    {
        _audio = audio;
        _apo = apo;
        _store = store;
        _loadResult = loadResult;
        _settings = loadResult.Settings;
    }

    public DeviceLimit? FindLimit(string endpointGuid) => _settings.Find(endpointGuid);

    public string? TakeOperationNotice()
    {
        var notice = _operationNotice;
        _operationNotice = null;
        return notice;
    }

    public bool IsLimitActive(DeviceLimit limit) =>
        _apo.IsManagedLimitConfigured(limit);

    public async Task RepairOnStartupAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_loadResult.IsAuthoritative)
            {
                StateUncertain = true;
                StartupIssueMessage = _loadResult.Message ??
                    "Audio Limits could not safely reconstruct its saved state. Existing audio processing was left unchanged.";
                AppLog.Error(StartupIssueMessage);
                return;
            }

            // A missing settings file is a normal first run only if Audio Limits does
            // not already own an active Equalizer APO include. Silently treating an
            // orphaned managed config as "no limits" could remove attenuation and make
            // a device suddenly louder.
            if (_loadResult.Status == SettingsLoadStatus.New &&
                _apo.HasManagedConfiguration)
            {
                StateUncertain = true;
                StartupIssueMessage =
                    "Audio Limits found existing managed audio processing but no matching settings file. " +
                    "It left the existing attenuation unchanged.";
                AppLog.Error(StartupIssueMessage);
                return;
            }

            if (_apo.HasManagedConfiguration &&
                !_apo.TryReadManagedEntries(out _, out var managedError))
            {
                StateUncertain = true;
                StartupIssueMessage =
                    "Audio Limits found managed audio processing that it could not safely validate. " +
                    "It left the existing attenuation unchanged.";
                AppLog.Error(
                    $"{StartupIssueMessage} Details: {managedError}");
                return;
            }

            if (_settings.PendingChange is not null)
            {
                await RollBackPendingChangeAsync();
                return;
            }

            if (_apo.IsInstalled)
                await ReconcileCommittedConfigurationAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Startup repair could not complete", ex);
            StateUncertain = true;
            StartupIssueMessage =
                "Audio Limits could not finish checking the current audio state. " +
                "Existing processing was left as safely as possible; restart the app before changing limits.";
        }
        finally
        {
            InitializationComplete = true;
            _gate.Release();
        }
    }


    private async Task ReconcileCommittedConfigurationAsync()
    {
        if (!_apo.TryReadManagedEntries(out var managedEntries, out var error))
            throw new InvalidOperationException(
                $"Audio Limits could not validate its existing audio configuration: {error}");

        var devices = _audio.GetActiveRenderDevices();
        var transitions = new List<StartupTransition>();
        var hardwareTransitions = new List<HardwareStartupTransition>();
        var externalLimits = _settings.Limits.Select(x => x.Clone()).ToList();

        foreach (var device in devices)
        {
            var currentEntry = managedEntries.FirstOrDefault(x =>
                string.Equals(
                    x.EndpointGuid,
                    device.EndpointGuid,
                    StringComparison.OrdinalIgnoreCase) &&
                _apo.IsStageActiveForEndpoint(device.EndpointGuid, x.Stage));

            var currentAttenuation = currentEntry?.AttenuationDb ?? 0.0;
            var desiredLimit = _settings.Find(device.EndpointGuid);
            var desiredAttenuation =
                desiredLimit is not null &&
                _apo.IsActiveForEndpoint(device.EndpointGuid)
                    ? desiredLimit.AttenuationDb
                    : 0.0;

            if (Math.Abs(currentAttenuation - desiredAttenuation) <= 0.002)
                continue;

            if (device.SupportsHardwareVolume)
            {
                // Hardware endpoint gain and APO gain are not guaranteed to be
                // acoustically interchangeable. Never move the Windows hardware
                // volume behind the user's back during reconciliation.
                if (currentAttenuation < desiredAttenuation - 0.002)
                {
                    // The live processing is stricter than the saved intent. An
                    // automatic relaxation could make the device louder, so preserve
                    // the stronger live attenuation and let the user explicitly
                    // replace it later.
                    if (desiredLimit is null)
                    {
                        throw new InvalidOperationException(
                            $"Audio Limits found active processing for hardware-controlled device '{device.FriendlyName}' without a matching saved limit. " +
                            "It left the stronger processing unchanged rather than making the device louder automatically.");
                    }

                    var preserved = desiredLimit.Clone();
                    preserved.AttenuationDb = currentAttenuation;
                    ReplaceLimit(externalLimits, preserved);
                    StartupIssueMessage =
                        $"Audio Limits found a stronger active limit than the saved setting for '{device.FriendlyName}' and left the stronger limit in place. " +
                        "Change the limit manually if you want to replace it.";
                    AppLog.Warn(StartupIssueMessage);
                    continue;
                }

                // Restoring a stricter saved attenuation can only reduce output.
                // Use a mute barrier for a clean transition, but leave the endpoint
                // hardware volume exactly where the user left it.
                hardwareTransitions.Add(new HardwareStartupTransition(device, device.Muted));
                continue;
            }

            var plan = LimitTransitionPlanner.Plan(
                device.CurrentDb,
                device.MinDb,
                device.MaxDb,
                currentAttenuation,
                desiredAttenuation);

            transitions.Add(new StartupTransition(
                device,
                plan,
                device.Muted));
        }

        // Endpoint-volume changes and Equalizer APO reloads do not share an
        // audible transaction boundary. Mute every endpoint that needs a real gain
        // transition before touching either subsystem.
        var temporarilyMuted = transitions
            .Where(x => x.Plan.RequiresTransitionMute && !x.WasMuted)
            .ToList();
        var hardwareTemporarilyMuted = hardwareTransitions
            .Where(x => !x.WasMuted)
            .ToList();

        foreach (var transition in temporarilyMuted)
            _audio.SetMute(transition.Device.Id, true);
        foreach (var transition in hardwareTemporarilyMuted)
            _audio.SetMute(transition.Device.Id, true);

        if (temporarilyMuted.Count > 0 || hardwareTemporarilyMuted.Count > 0)
            await Task.Delay(TransitionMuteSettleDelayMs);

        // Software-volume endpoints can preserve output exactly. For any
        // relaxation/removal, move that endpoint down before touching config.
        foreach (var transition in transitions.Where(
                     x => x.Plan.Order == LimitTransitionOrder.EndpointThenConfig))
        {
            _audio.SetMasterDb(
                transition.Device.Id,
                transition.Plan.TargetEndpointDb);
        }

        _apo.ApplyLimits(externalLimits);
        await Task.Delay(ApoReloadDelayMs);

        // For stronger/reintroduced limits on software-volume endpoints,
        // attenuation is now in place before the endpoint can move upward.
        foreach (var transition in transitions.Where(
                     x => x.Plan.Order == LimitTransitionOrder.ConfigThenEndpoint))
        {
            _audio.SetMasterDb(
                transition.Device.Id,
                transition.Plan.TargetEndpointDb);
        }

        foreach (var transition in temporarilyMuted)
        {
            if (transition.Plan.RequiresSafetyMute)
            {
                AppLog.Warn(
                    $"Left {transition.Device.FriendlyName} muted during startup reconciliation because the endpoint dB floor could not preserve the previous very quiet output.");
                continue;
            }

            _audio.SetMute(transition.Device.Id, false);
        }

        foreach (var transition in hardwareTemporarilyMuted)
            _audio.SetMute(transition.Device.Id, false);
    }

    private static void ReplaceLimit(List<DeviceLimit> limits, DeviceLimit replacement)
    {
        limits.RemoveAll(x => string.Equals(
            x.EndpointGuid,
            replacement.EndpointGuid,
            StringComparison.OrdinalIgnoreCase));
        limits.Add(replacement);
    }

    private sealed record StartupTransition(
        AudioDeviceInfo Device,
        LimitTransitionPlan Plan,
        bool WasMuted);

    private sealed record HardwareStartupTransition(
        AudioDeviceInfo Device,
        bool WasMuted);

    public async Task SetLimitAsync(AudioDeviceInfo device, int percent)
    {
        if (percent >= 100)
        {
            await RemoveLimitAsync(device);
            return;
        }

        percent = Math.Clamp(percent, 1, 99);
        await _gate.WaitAsync();
        IsBusy = true;
        _operationNotice = null;
        try
        {
            EnsureReadyForChange(device);

            // Reconcile any external/manual drift before using the live processing
            // state as the starting point for a new transaction. Software-volume
            // endpoints compensate through Windows endpoint gain; hardware-volume
            // endpoints preserve the user's hardware-volume position and never
            // auto-relax a stronger live attenuation.
            await ReconcileCommittedConfigurationAsync();

            var previous = _settings.Find(device.EndpointGuid)?.Clone();
            var (previousWasActive, previousAppliedAttenuation) = GetAppliedState(device, previous);
            var snapshot = _audio.GetSnapshot(device.Id);
            var curve = await GetOrBuildCurveAsync(previous, snapshot);
            var desiredDb = curve.DbAtPercent(percent);
            var attenuationDb = Math.Min(0.0, desiredDb - snapshot.MaxDb);
            if (attenuationDb >= -0.001)
                throw new InvalidOperationException(
                    $"On this playback device, {percent}% maps to the same maximum output as 100%. Choose a lower limit or leave the limit off.");

            var desired = new DeviceLimit
            {
                EndpointGuid = device.EndpointGuid,
                FriendlyName = device.FriendlyName,
                LimitPercent = percent,
                AttenuationDb = attenuationDb,
                VolumeCurveDb = curve.DbByPercent.ToList(),
                UpdatedUtc = DateTime.UtcNow
            };

            await ExecuteChangeAsync(
                device,
                snapshot,
                previous,
                desired,
                previousWasActive,
                previousAppliedAttenuation);
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    public async Task SetCurrentOutputAsLimitAsync(AudioDeviceInfo device)
    {
        await _gate.WaitAsync();
        IsBusy = true;
        _operationNotice = null;
        try
        {
            EnsureReadyForChange(device);

            if (device.SupportsHardwareVolume)
                throw new InvalidOperationException(
                    "Set current output as limit isn't available for this device because Windows volume is controlled in hardware and exact loudness matching isn't reliable.");

            // Reconcile any external/manual drift before using the live processing
            // state as the starting point for a new transaction. Software-volume
            // endpoints compensate through Windows endpoint gain; hardware-volume
            // endpoints preserve the user's hardware-volume position and never
            // auto-relax a stronger live attenuation.
            await ReconcileCommittedConfigurationAsync();

            var previous = _settings.Find(device.EndpointGuid)?.Clone();
            var (previousWasActive, previousAppliedAttenuation) = GetAppliedState(device, previous);
            var snapshot = _audio.GetSnapshot(device.Id);
            if (snapshot.Muted || snapshot.CurrentScalar <= 0.0001f)
                throw new InvalidOperationException(
                    "This device is muted or at 0%. Choose an audible level first, then set the current output as the limit.");

            var currentActualDb = snapshot.CurrentDb + previousAppliedAttenuation;

            if (currentActualDb >= snapshot.MaxDb - 0.01)
                throw new InvalidOperationException(
                    "The current output is already this device's full output, so it would not create a useful limit.");

            var curve = await GetOrBuildCurveAsync(previous, snapshot);
            var displayPercent = Math.Clamp(
                (int)Math.Round(
                    curve.PercentAtDb(currentActualDb),
                    MidpointRounding.AwayFromZero),
                1,
                99);

            var desired = new DeviceLimit
            {
                EndpointGuid = device.EndpointGuid,
                FriendlyName = device.FriendlyName,
                LimitPercent = displayPercent,
                AttenuationDb = currentActualDb - snapshot.MaxDb,
                VolumeCurveDb = curve.DbByPercent.ToList(),
                UpdatedUtc = DateTime.UtcNow
            };

            await ExecuteChangeAsync(
                device,
                snapshot,
                previous,
                desired,
                previousWasActive,
                previousAppliedAttenuation);
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    public async Task RemoveLimitAsync(AudioDeviceInfo device)
    {
        await _gate.WaitAsync();
        IsBusy = true;
        _operationNotice = null;
        try
        {
            EnsureNoPendingRecovery();
            EnsureStateAuthoritative();

            var previous = _settings.Find(device.EndpointGuid)?.Clone();
            if (previous is null)
                return;

            if (!_apo.IsInstalled)
                throw new InvalidOperationException(
                    "Audio setup is unavailable, so Audio Limits cannot safely remove this saved limit yet. " +
                    "Restore Equalizer APO first, then remove the limit.");

            _apo.ValidateCanChangeManagedConfiguration();

            // A managed entry may still be active even when it no longer exactly
            // matches the saved limit (for example after a manual config edit).
            // Restore the committed state with safe transition ordering before
            // deciding whether removal is an active or inactive operation.
            await ReconcileCommittedConfigurationAsync();

            var (previousWasActive, previousAppliedAttenuation) = GetAppliedState(device, previous);
            if (!previousWasActive)
            {
                await RemoveInactiveLimitAsync(device, previous);
                return;
            }

            EnsureEndpointStageReady(device);
            var snapshot = _audio.GetSnapshot(device.Id);
            await ExecuteChangeAsync(
                device,
                snapshot,
                previous,
                desired: null,
                previousWasActive: true,
                previousAppliedAttenuation: previousAppliedAttenuation);
        }
        finally
        {
            IsBusy = false;
            _gate.Release();
        }
    }

    private async Task RemoveInactiveLimitAsync(
        AudioDeviceInfo device,
        DeviceLimit previous)
    {
        var pending = NewPending(
            device,
            previous,
            desired: null,
            previousLimitWasActive: false);

        _settings.PendingChange = pending;
        _store.Save(_settings);

        try
        {
            // The saved limit is not currently part of the processing path. Removing
            // only our config/settings intent therefore cannot make the endpoint louder.
            _apo.ApplyLimits(BuildDesiredLimitList(device.EndpointGuid, desired: null));
            MarkPendingPhase(pending, PendingChangePhase.FirstExternalStepApplied);

            _settings.Remove(device.EndpointGuid);
            _settings.PendingChange = null;
            _store.Save(_settings);
            AppLog.Info($"Removed inactive limit for {device.FriendlyName}");
        }
        catch (Exception ex)
        {
            pending.LastError = ex.Message;
            BestEffortSavePending(pending);
            _settings.Upsert(previous);
            throw;
        }

        await Task.CompletedTask;
    }

    private async Task<VolumeCurve> GetOrBuildCurveAsync(
        DeviceLimit? previous,
        AudioDeviceSnapshot snapshot)
    {
        var existing = previous?.TryGetCurve();
        if (existing is not null &&
            Math.Abs(existing.DbByPercent[0] - snapshot.MinDb) < 1.0 &&
            Math.Abs(existing.DbByPercent[^1] - snapshot.MaxDb) < 0.1)
            return existing;

        // Calibration performs 101 endpoint writes while the device is muted.
        // Keep that synchronous Core Audio work off the UI thread; cancellation
        // is intentionally not offered because the restore sequence must finish.
        var points = await Task.Run(
            () => _audio.BuildVolumeCurveSafely(snapshot.Id));
        return new VolumeCurve(points);
    }

    private async Task ExecuteChangeAsync(
        AudioDeviceInfo device,
        AudioDeviceSnapshot snapshot,
        DeviceLimit? previous,
        DeviceLimit? desired,
        bool previousWasActive,
        double previousAppliedAttenuation)
    {
        var pending = NewPending(
            device,
            previous,
            desired,
            previousWasActive,
            previousAppliedAttenuation);

        _settings.PendingChange = pending;
        _store.Save(_settings); // Durable recovery intent before external state changes.

        var newAttenuation = desired?.AttenuationDb ?? 0.0;
        var hardwareVolume = device.SupportsHardwareVolume;
        var plan = hardwareVolume
            ? null
            : LimitTransitionPlanner.Plan(
                snapshot.CurrentDb,
                snapshot.MinDb,
                snapshot.MaxDb,
                previousAppliedAttenuation,
                newAttenuation);

        var diagnosticId = Guid.NewGuid().ToString("N")[..8];
        LogTransitionDiagnostics(diagnosticId, "start", device, previous);
        if (hardwareVolume)
        {
            AppLog.Info(
                $"AUDIO_DIAG {diagnosticId} plan; operation={(desired is null ? "remove" : previous is null ? "set" : "change")}; " +
                "mode=HardwareVolumeCompatibility; endpointVolume=unchanged; " +
                $"previousLimit={(previous is null ? "none" : $"{previous.LimitPercent}%/{previous.AttenuationDb:0.000}dB")}; " +
                $"desiredLimit={(desired is null ? "none" : $"{desired.LimitPercent}%/{desired.AttenuationDb:0.000}dB")}; " +
                $"previousAppliedAttenuation={previousAppliedAttenuation:0.000}dB");
        }
        else
        {
            AppLog.Info(
                $"AUDIO_DIAG {diagnosticId} plan; operation={(desired is null ? "remove" : previous is null ? "set" : "change")}; " +
                $"previousLimit={(previous is null ? "none" : $"{previous.LimitPercent}%/{previous.AttenuationDb:0.000}dB")}; " +
                $"desiredLimit={(desired is null ? "none" : $"{desired.LimitPercent}%/{desired.AttenuationDb:0.000}dB")}; " +
                $"previousAppliedAttenuation={previousAppliedAttenuation:0.000}dB; " +
                $"previousActual={plan!.PreviousActualDb:0.000}dB; targetEndpoint={plan.TargetEndpointDb:0.000}dB; " +
                $"order={plan.Order}; safetyMute={plan.RequiresSafetyMute}");
        }

        var transitionMuteApplied = false;

        try
        {
            if (hardwareVolume)
            {
                // On hardware-volume endpoints, keep the Windows volume exactly where
                // the user left it. The confirmed Corsair test showed that endpoint
                // hardware dB and APO dB are not acoustically interchangeable, so
                // attempting mathematical compensation is misleading.
                if (!snapshot.Muted)
                {
                    _audio.SetMute(device.Id, true);
                    transitionMuteApplied = true;
                    await Task.Delay(TransitionMuteSettleDelayMs);
                    LogTransitionDiagnostics(diagnosticId, "transition-muted", device, previous);
                }

                var desiredLimits = BuildDesiredLimitList(device.EndpointGuid, desired);
                _apo.ApplyLimits(desiredLimits);
                await Task.Delay(ApoReloadDelayMs);
                MarkPendingPhase(pending, PendingChangePhase.FirstExternalStepApplied);
                LogTransitionDiagnostics(diagnosticId, "after-config-reload", device, desired);

                if (transitionMuteApplied)
                {
                    _audio.SetMute(device.Id, false);
                    transitionMuteApplied = false;
                    LogTransitionDiagnostics(diagnosticId, "after-unmute", device, desired);
                }
            }
            else
            {
                var firstStepDone = false;

                if (plan!.RequiresTransitionMute && !snapshot.Muted)
                {
                    _audio.SetMute(device.Id, true);
                    transitionMuteApplied = true;
                    await Task.Delay(TransitionMuteSettleDelayMs);
                    LogTransitionDiagnostics(diagnosticId, "transition-muted", device, previous);
                }

                if (plan.Order == LimitTransitionOrder.EndpointThenConfig)
                {
                    _audio.SetMasterDb(device.Id, plan.TargetEndpointDb);
                    firstStepDone = true;
                    MarkPendingPhase(pending, PendingChangePhase.FirstExternalStepApplied);
                    LogTransitionDiagnostics(diagnosticId, "after-endpoint-first", device, previous);
                }

                var desiredLimits = BuildDesiredLimitList(device.EndpointGuid, desired);
                _apo.ApplyLimits(desiredLimits);
                await Task.Delay(ApoReloadDelayMs);
                LogTransitionDiagnostics(diagnosticId, "after-config-reload", device, desired);

                MarkPendingPhase(
                    pending,
                    firstStepDone
                        ? PendingChangePhase.SecondExternalStepApplied
                        : PendingChangePhase.FirstExternalStepApplied);

                if (plan.Order == LimitTransitionOrder.ConfigThenEndpoint)
                {
                    _audio.SetMasterDb(device.Id, plan.TargetEndpointDb);
                    MarkPendingPhase(pending, PendingChangePhase.SecondExternalStepApplied);
                    LogTransitionDiagnostics(diagnosticId, "after-endpoint-second", device, desired);
                }

                if (transitionMuteApplied && !plan.RequiresSafetyMute)
                {
                    _audio.SetMute(device.Id, false);
                    transitionMuteApplied = false;
                    LogTransitionDiagnostics(diagnosticId, "after-unmute", device, desired);
                }
            }

            if (desired is null)
                _settings.Remove(device.EndpointGuid);
            else
                _settings.Upsert(desired);

            _settings.PendingChange = null;
            _store.Save(_settings);
            LogTransitionDiagnostics(diagnosticId, "committed", device, desired);

            if (!hardwareVolume && plan!.RequiresSafetyMute && !snapshot.Muted)
            {
                _operationNotice =
                    $"Windows could not lower '{device.FriendlyName}' enough to preserve the previous very quiet output while changing the limit. " +
                    "Audio Limits left the device muted for safety. Unmute it when you're ready.";
                AppLog.Warn(_operationNotice);
            }

            AppLog.Info(desired is null
                ? $"Removed limit for {device.FriendlyName}"
                : $"Applied {desired.LimitPercent}% limit to {device.FriendlyName}");
        }
        catch (Exception ex)
        {
            AppLog.Error(
                $"Limit change failed for {device.FriendlyName}; attempting safe rollback",
                ex);

            _settings.PendingChange = pending;
            pending.LastError = ex.Message;
            if (previous is null)
                _settings.Remove(device.EndpointGuid);
            else
                _settings.Upsert(previous);

            BestEffortSavePending(pending);

            try
            {
                await RollBackPendingChangeAsync();

                // If this transaction introduced the temporary mute and rollback
                // successfully restored the previous processing state, restore the
                // user's original unmuted state as well. If recovery explicitly
                // decided the device must remain muted, preserve that safer state.
                if (transitionMuteApplied &&
                    !snapshot.Muted &&
                    string.IsNullOrWhiteSpace(_operationNotice))
                {
                    _audio.SetMute(device.Id, false);
                    transitionMuteApplied = false;
                }
            }
            catch (Exception rollbackEx)
            {
                AppLog.Error("Automatic rollback was incomplete", rollbackEx);
                throw new InvalidOperationException(
                    "The limit change failed, and Audio Limits could not fully verify the rollback. " +
                    "It kept the safer attenuation where possible. Restart Audio Limits before making another change.",
                    ex);
            }

            var rollbackMessage =
                "The limit could not be changed. Audio Limits restored the previous setting.";
            if (!string.IsNullOrWhiteSpace(_operationNotice))
                rollbackMessage += " " + _operationNotice;

            throw new InvalidOperationException(rollbackMessage, ex);
        }
    }

    private void LogTransitionDiagnostics(
        string diagnosticId,
        string phase,
        AudioDeviceInfo device,
        DeviceLimit? expectedLimit)
    {
        try
        {
            var endpoint = _audio.GetDiagnostics(device.Id);
            var managed = _apo.TryGetManagedEntry(device.EndpointGuid);
            var preferredStage = _apo.GetPreferredProcessingStage(device.EndpointGuid);
            var expectedActive = expectedLimit is not null && _apo.IsManagedLimitConfigured(expectedLimit);
            var managedText = managed is null
                ? "none"
                : $"{managed.Stage}/{managed.AttenuationDb:0.000}dB";
            var expectedText = expectedLimit is null
                ? "none"
                : $"{expectedLimit.LimitPercent}%/{expectedLimit.AttenuationDb:0.000}dB";

            AppLog.Info(
                $"AUDIO_DIAG {diagnosticId} {phase}; " +
                $"device='{endpoint.FriendlyName}'; endpointGuid={endpoint.EndpointGuid}; scalar={endpoint.CurrentScalar * 100.0:0.000}%; " +
                $"endpointDb={endpoint.CurrentDb:0.000}dB; range={endpoint.MinDb:0.000}..{endpoint.MaxDb:0.000}dB; " +
                $"increment={endpoint.IncrementDb:0.000}dB; muted={endpoint.Muted}; " +
                $"hardwareSupport={endpoint.HardwareSupport}; step={endpoint.Step}/{endpoint.StepCount}; " +
                $"preferredApoStage={preferredStage}; managed={managedText}; expected={expectedText}; expectedActive={expectedActive}");
        }
        catch (Exception ex)
        {
            AppLog.Error($"AUDIO_DIAG {diagnosticId} {phase} capture failed for {device.FriendlyName}", ex);
        }
    }

    private PendingLimitChange NewPending(
        AudioDeviceInfo device,
        DeviceLimit? previous,
        DeviceLimit? desired,
        bool previousLimitWasActive,
        double? previousAppliedAttenuationDb = null) =>
        new()
        {
            DeviceId = device.Id,
            EndpointGuid = device.EndpointGuid,
            FriendlyName = device.FriendlyName,
            PreviousLimit = previous?.Clone(),
            DesiredLimit = desired?.Clone(),
            PreviousLimitWasActive = previousLimitWasActive,
            PreviousAppliedAttenuationDb = previousAppliedAttenuationDb,
            CreatedUtc = DateTime.UtcNow,
            Phase = PendingChangePhase.Prepared
        };

    private List<DeviceLimit> BuildDesiredLimitList(
        string endpointGuid,
        DeviceLimit? desired)
    {
        var list = _settings.Limits
            .Where(x => !string.Equals(
                x.EndpointGuid,
                endpointGuid,
                StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Clone())
            .ToList();

        if (desired is not null)
            list.Add(desired.Clone());

        return list;
    }

    private async Task RollBackPendingChangeAsync()
    {
        var pending = _settings.PendingChange;
        if (pending is null)
            return;

        if (!_apo.IsInstalled)
            throw new InvalidOperationException(
                "Equalizer APO is unavailable, so the interrupted change cannot be repaired yet.");

        if (_apo.HasManagedConfiguration &&
            !_apo.TryReadManagedEntries(out _, out var managedError))
            throw new InvalidOperationException(
                $"The existing managed audio configuration could not be validated, so recovery was not allowed to change it: {managedError}");

        var activeDevice = _audio.GetActiveRenderDevices()
            .FirstOrDefault(x =>
                string.Equals(
                    x.EndpointGuid,
                    pending.EndpointGuid,
                    StringComparison.OrdinalIgnoreCase));

        if (activeDevice is null)
            throw new InvalidOperationException(
                $"The playback device '{pending.FriendlyName}' is not currently available, so its interrupted limit change cannot be repaired yet.");

        if (!_apo.IsActiveForEndpoint(activeDevice.EndpointGuid))
            throw new InvalidOperationException(
                $"Audio setup is not currently enabled for '{pending.FriendlyName}', so its interrupted limit change cannot be repaired yet.");

        var age = DateTime.UtcNow - pending.CreatedUtc.ToUniversalTime();
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        // Automatic recovery intent has a finite lifetime. After 30 days the
        // previous context may no longer represent the user's intent, so do not
        // mutate Windows/Equalizer APO from stale recovery data. Preserve the
        // current processing and require a later explicit repair path instead.
        if (age > MaxAutomaticRecoveryAge)
        {
            pending.LastError =
                $"Automatic recovery expired after {age.TotalDays:0} days.";
            BestEffortSavePending(pending);
            throw new InvalidOperationException(
                "An interrupted Audio Limits change is more than 30 days old. " +
                "Audio Limits left the current audio processing unchanged instead of applying stale recovery data.");
        }

        pending.DeviceId = activeDevice.Id;
        pending.RecoveryAttempts++;
        pending.LastRecoveryAttemptUtc = DateTime.UtcNow;
        pending.Phase = PendingChangePhase.Recovering;
        _store.Save(_settings);

        AppLog.Info(
            $"Recovering interrupted change for {pending.FriendlyName}; " +
            $"age={age.TotalHours:0.0}h, attempts={pending.RecoveryAttempts}");

        var previousAppliedAttenuation =
            pending.PreviousAppliedAttenuationDb ??
            (pending.PreviousLimitWasActive
                ? pending.PreviousLimit?.AttenuationDb ?? 0.0
                : 0.0);

        var desiredAttenuation = pending.DesiredLimit?.AttenuationDb ?? 0.0;
        var managedEntry = _apo.TryGetManagedEntry(pending.EndpointGuid);
        var managedAttenuation = managedEntry is not null &&
                                 _apo.IsStageActiveForEndpoint(
                                     pending.EndpointGuid,
                                     managedEntry.Stage)
            ? managedEntry.AttenuationDb
            : 0.0;

        // Establish the strictest plausible attenuation first. Regardless of which
        // external step completed before interruption, this cannot make the device
        // louder than any state represented by the pending transaction.
        var safeAttenuation = Math.Min(
            previousAppliedAttenuation,
            Math.Min(desiredAttenuation, managedAttenuation));

        var safeList = BuildDesiredLimitList(
            pending.EndpointGuid,
            BuildRecoveryBasis(pending, safeAttenuation));

        _apo.ApplyLimits(safeList);
        await Task.Delay(ApoReloadDelayMs);

        var snapshot = _audio.GetSnapshot(activeDevice.Id);
        var wasMutedAtRecovery = snapshot.Muted;
        var recoveryMuteApplied = false;
        var previousExternalLimit = BuildPreviousExternalBasis(
            pending,
            previousAppliedAttenuation);
        var committedExternalLimits = BuildDesiredLimitList(
            pending.EndpointGuid,
            previousExternalLimit);

        if (activeDevice.SupportsHardwareVolume)
        {
            // Restore the exact pre-operation APO attenuation while leaving the
            // user's hardware volume position untouched. The strictest plausible
            // attenuation was already established above, so mute before any
            // relaxation back to the previous state.
            if (!wasMutedAtRecovery)
            {
                _audio.SetMute(activeDevice.Id, true);
                recoveryMuteApplied = true;
                await Task.Delay(TransitionMuteSettleDelayMs);
            }

            _apo.ApplyLimits(committedExternalLimits);
            await Task.Delay(ApoReloadDelayMs);

            if (recoveryMuteApplied)
                _audio.SetMute(activeDevice.Id, false);
        }
        else
        {
            var plan = LimitTransitionPlanner.Plan(
                snapshot.CurrentDb,
                snapshot.MinDb,
                snapshot.MaxDb,
                safeAttenuation,
                previousAppliedAttenuation);

            if (plan.RequiresTransitionMute && !wasMutedAtRecovery)
            {
                _audio.SetMute(activeDevice.Id, true);
                recoveryMuteApplied = true;
                await Task.Delay(TransitionMuteSettleDelayMs);
            }

            if (plan.Order == LimitTransitionOrder.EndpointThenConfig)
                _audio.SetMasterDb(activeDevice.Id, plan.TargetEndpointDb);

            _apo.ApplyLimits(committedExternalLimits);
            await Task.Delay(ApoReloadDelayMs);

            if (plan.RequiresSafetyMute && !wasMutedAtRecovery)
            {
                _operationNotice =
                    $"Audio Limits restored the previous limit for '{pending.FriendlyName}', but Windows could not lower the endpoint enough to preserve the previous very quiet output. " +
                    "The device was left muted for safety.";
                AppLog.Warn(_operationNotice);
            }
            else if (recoveryMuteApplied)
            {
                _audio.SetMute(activeDevice.Id, false);
            }
        }

        // The pending record is the durable source of truth for rollback. Restore
        // the saved intent as well as the external audio state before clearing it.
        // This matters if a failure happened while the final settings write itself
        // was being attempted.
        if (pending.PreviousLimit is null)
            _settings.Remove(pending.EndpointGuid);
        else
            _settings.Upsert(pending.PreviousLimit.Clone());

        _settings.PendingChange = null;
        _store.Save(_settings);
        StartupIssueMessage = null;
        AppLog.Info($"Recovered interrupted limit change for {pending.FriendlyName}");
    }

    private static DeviceLimit? BuildPreviousExternalBasis(
        PendingLimitChange pending,
        double previousAppliedAttenuation)
    {
        if (!pending.PreviousLimitWasActive || previousAppliedAttenuation >= -0.001)
            return null;

        var basis = (pending.PreviousLimit ?? pending.DesiredLimit)?.Clone()
                    ?? throw new InvalidOperationException(
                        "Pending limit recovery data is incomplete.");
        basis.AttenuationDb = previousAppliedAttenuation;
        return basis;
    }

    private static DeviceLimit? BuildRecoveryBasis(
        PendingLimitChange pending,
        double safeAttenuation)
    {
        if (safeAttenuation >= -0.001)
            return null;

        var basis = (pending.PreviousLimit ?? pending.DesiredLimit)?.Clone()
                    ?? throw new InvalidOperationException(
                        "Pending limit recovery data is incomplete.");

        basis.AttenuationDb = safeAttenuation;
        return basis;
    }

    private void MarkPendingPhase(
        PendingLimitChange pending,
        PendingChangePhase phase)
    {
        pending.Phase = phase;
        _settings.PendingChange = pending;
        _store.Save(_settings);
    }

    private void BestEffortSavePending(PendingLimitChange pending)
    {
        _settings.PendingChange = pending;
        try
        {
            _store.Save(_settings);
        }
        catch (Exception saveEx)
        {
            AppLog.Error(
                "Could not update pending recovery metadata; the earlier durable recovery record remains authoritative",
                saveEx);
        }
    }

    private (bool IsActive, double AttenuationDb) GetAppliedState(
        AudioDeviceInfo device,
        DeviceLimit? savedLimit)
    {
        if (savedLimit is null)
            return (false, 0.0);

        var managed = _apo.TryGetManagedEntry(device.EndpointGuid);
        if (managed is null || !_apo.IsStageActiveForEndpoint(device.EndpointGuid, managed.Stage))
            return (false, 0.0);

        return (true, managed.AttenuationDb);
    }

    private void EnsureReadyForChange(AudioDeviceInfo device)
    {
        EnsureNoPendingRecovery();
        EnsureStateAuthoritative();

        if (!InitializationComplete)
            throw new InvalidOperationException(
                "Audio Limits is still checking the current audio state. Try again in a moment.");

        if (StateUncertain)
            throw new InvalidOperationException(
                StartupIssueMessage ??
                "Audio Limits cannot safely change limits until the current audio state has been repaired.");

        if (!_apo.IsInstalled)
            throw new InvalidOperationException(
                "Audio setup is required before a limit can be applied. Install Equalizer APO first.");

        _apo.ValidateCanChangeManagedConfiguration();
        EnsureEndpointStageReady(device);
    }

    private void EnsureStateAuthoritative()
    {
        // Every user-initiated mutation must wait for startup reconciliation.
        // This also protects RemoveLimitAsync, which has a different preflight
        // path from add/change operations. A second-instance activation can open
        // the window while startup repair is still running, so UI state alone is
        // not a sufficient guard.
        if (!InitializationComplete)
            throw new InvalidOperationException(
                "Audio Limits is still checking the current audio state. Try again in a moment.");

        if (!_loadResult.IsAuthoritative || StateUncertain)
            throw new InvalidOperationException(
                StartupIssueMessage ??
                "Audio Limits cannot safely change limits because its saved state could not be verified.");
    }

    private void EnsureNoPendingRecovery()
    {
        if (_settings.PendingChange is not null)
            throw new InvalidOperationException(
                "A previous limit change still needs repair. Restart Audio Limits after the affected device is available before making another change.");
    }

    private void EnsureEndpointStageReady(AudioDeviceInfo device)
    {
        if (!_apo.IsActiveForEndpoint(device.EndpointGuid))
            throw new InvalidOperationException(
                "Audio setup is not enabled for this playback device. Open Manage audio devices, enable this device, and apply the change first.");
    }
}
