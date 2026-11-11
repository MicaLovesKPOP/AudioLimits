using AudioLimits.Core.Services;

namespace AudioLimits.App.Services;

/// <summary>
/// While the app is in the tray, retry safe cap restoration when Windows
/// recreates an already-known serialized USB endpoint. Device provisioning
/// is NOT performed here: Equalizer APO must be registered and reported ready
/// for the exact new endpoint before we touch any volume or attenuation.
/// </summary>
public sealed class DeviceReconnectionService
{
    private readonly AudioDeviceService _audio;
    private readonly EqualizerApoService _apo;
    private readonly LimitService _limits;
    private readonly CancellationTokenSource _stopToken = new();
    private readonly HashSet<string> _failedEndpointIds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lifecycle = new();
    private Task? _monitorTask;
    private bool _stopped;
    private bool _applying;

    public DeviceReconnectionService(
        AudioDeviceService audio,
        EqualizerApoService apo,
        LimitService limits)
    {
        _audio = audio;
        _apo = apo;
        _limits = limits;
    }

    public bool IsApplying
    {
        get { lock (_lifecycle) return _applying; }
    }

    public void Start()
    {
        lock (_lifecycle)
        {
            if (_stopped || _monitorTask is not null)
                return;
            _monitorTask = Task.Run(MonitorAsync);
        }
    }

    /// <summary>
    /// Prevents new automatic transactions before the normal tray Exit safety
    /// check. If a transaction has already started, IsApplying keeps Exit blocked.
    /// </summary>
    public void Stop()
    {
        lock (_lifecycle)
        {
            if (_stopped)
                return;
            _stopped = true;
            _stopToken.Cancel();
        }
    }

    private async Task MonitorAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            do
            {
                try { await CheckForNewEndpointsAsync(); }
                catch (Exception ex)
                {
                    // Device enumeration can transiently fail during unplug or
                    // driver reinitialization. Do not lose monitoring forever.
                    AppLog.Error("Audio reconnect scan failed; retrying", ex);
                }
            }
            while (await timer.WaitForNextTickAsync(_stopToken.Token));
        }
        catch (OperationCanceledException) when (_stopToken.IsCancellationRequested)
        {
            // Normal explicit app exit.
        }
    }

    private async Task CheckForNewEndpointsAsync()
    {
        if (_stopToken.IsCancellationRequested || !_limits.CanModifyLimits ||
            _limits.IsBusy || !_apo.IsInstalled)
            return;

        var active = _audio.GetActiveRenderDevices();
        foreach (var device in active)
        {
            if (_stopToken.IsCancellationRequested)
                return;
            if (_failedEndpointIds.Contains(device.EndpointGuid) ||
                ApoEndpointRegistrationTransaction.IsAwaitingAudioRestart(device.EndpointGuid) ||
                !_apo.IsActiveForEndpoint(device.EndpointGuid) ||
                _limits.FindRememberedLimit(device, active).Kind !=
                    RememberedLimitMatchKind.Automatic)
                continue;

            lock (_lifecycle)
            {
                if (_stopped)
                    return;
                _applying = true;
            }

            try
            {
                await _limits.TryRestoreRememberedLimitAsync(device, active);
            }
            catch (Exception ex)
            {
                // Do not repeatedly alter a failing device. The user can
                // inspect and retry via the explicit Restore limit button.
                _failedEndpointIds.Add(device.EndpointGuid);
                AppLog.Error(
                    $"Automatic remembered-limit restoration failed for {device.FriendlyName}", ex);
            }
            finally
            {
                lock (_lifecycle)
                    _applying = false;
            }
        }
    }
}
