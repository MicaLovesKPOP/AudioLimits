using System.ComponentModel;
using System.Runtime.CompilerServices;
using AudioLimits.Core.Models;
using AudioLimits.Core.Services;

namespace AudioLimits.App.ViewModels;

public sealed class DeviceViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioDeviceService _audio;
    private readonly DeviceLimit? _limit;
    private readonly bool _limitActive;
    private readonly VolumeCurve? _curve;
    private readonly IDisposable _subscription;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private readonly string _idleStatusText;
    private readonly bool _apoInstalled;
    private readonly bool _baseActionsEnabled;

    private int _windowsPercent;
    private bool _muted;
    private string _sameOutputWithoutLimit = "—";
    private bool _operationInProgress;
    private string _operationStatus = string.Empty;
    private bool _globalActionsEnabled = true;

    public DeviceViewModel(
        AudioDeviceInfo device,
        LimitService limits,
        AudioDeviceService audio,
        EqualizerApoService apo,
        Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
    {
        Device = device;
        _audio = audio;
        _dispatcherQueue = dispatcherQueue;
        _limit = limits.FindLimit(device.EndpointGuid)?.Clone();
        _limitActive = _limit is not null && limits.IsLimitActive(_limit);
        _apoInstalled = apo.IsInstalled;
        ApoReady = apo.IsActiveForEndpoint(device.EndpointGuid);
        _baseActionsEnabled = limits.CanModifyLimits;

        var candidateCurve = _limit?.TryGetCurve();
        _curve = candidateCurve is not null &&
                 Math.Abs(candidateCurve.DbByPercent[0] - device.MinDb) < 1.0 &&
                 Math.Abs(candidateCurve.DbByPercent[^1] - device.MaxDb) < 0.1
            ? candidateCurve
            : null;

        (DisplayName, DeviceDescription) = SplitFriendlyName(device.FriendlyName);
        _idleStatusText = _limit switch
        {
            null => "No limit",
            _ when _limitActive => $"Limited to {_limit.LimitPercent}%",
            _ => "Limit inactive"
        };

        ApplyVolumeState(new AudioVolumeState(device.CurrentScalar, device.Muted));
        _subscription = _audio.SubscribeToVolume(device.Id, OnVolumeChanged);
    }

    public AudioDeviceInfo Device { get; }
    public string DisplayName { get; }
    public string DeviceDescription { get; }
    public bool HasDeviceDescription => !string.IsNullOrWhiteSpace(DeviceDescription);
    public bool HasLimit => _limit is not null;
    public int? LimitPercent => _limit?.LimitPercent;
    public bool ApoReady { get; }
    public bool HasHardwareVolume => Device.SupportsHardwareVolume;
    public bool IsLimitActive => _limitActive;
    public bool IsLimitInactive => _limit is not null && !_limitActive;
    public string InactiveLimitTitle => !_apoInstalled
        ? "Audio processing setup required"
        : "Saved limit isn't active";
    public string InactiveLimitMessage => !_apoInstalled
        ? "Equalizer APO must be installed before this saved limit can affect audio."
        : !ApoReady
            ? "This playback device is not enabled for Audio Limits. Use Manage devices in Settings to enable it."
            : "Audio Limits couldn't verify this saved limit as active. Reopen the app before changing it.";

    public string StatusText => _operationInProgress ? _operationStatus : _idleStatusText;
    public bool IsOperationInProgress => _operationInProgress;
    public string SetLimitButtonText => HasLimit ? "Change limit" : "Set limit";
    public string SetLimitAutomationName => HasLimit
        ? $"Change limit for {DisplayName}"
        : $"Set limit for {DisplayName}";
    public string SetCurrentAutomationName => $"Set current output as limit for {DisplayName}";
    public string SetCurrentHelpText
    {
        get
        {
            if (!_apoInstalled)
                return "Install Equalizer APO before changing limits.";
            if (HasHardwareVolume)
                return "Unavailable on this device because Windows volume is hardware-controlled. Audio Limits can set and remove limits, but it can't reliably turn the current loudness into an equivalent limit.";
            if (_muted || _windowsPercent <= 0)
                return "Choose an audible Windows volume first.";
            if (_windowsPercent >= 100)
                return "The current output is already this device's maximum.";
            if (!CanSetLimit)
                return "Unavailable while Audio Limits is checking or changing the audio state.";
            return "Use the current audible output as this device's maximum.";
        }
    }
    public string HardwareVolumeNotice =>
        "This device controls Windows volume in hardware, so Audio Limits can't calculate an exact equivalent.";
    public string RemoveLimitAutomationName => $"Remove limit for {DisplayName}";

    public int CurrentWindowsPercent => _windowsPercent;
    public bool IsMuted => _muted;
    public string WindowsVolumeText => _muted ? $"{_windowsPercent}% (muted)" : $"{_windowsPercent}%";
    public string SameOutputWithoutLimitText => _sameOutputWithoutLimit;
    public string SameOutputHelpText => HasHardwareVolume && _limit is not null && _limitActive
        ? "This device controls Windows volume in hardware, so Audio Limits can't reliably calculate an unrestricted Windows volume that would sound exactly the same."
        : _sameOutputWithoutLimit == "—"
            ? "This value becomes available after Audio Limits calibrates this device and verifies that its saved limit is active."
            : "The Windows volume position that would produce the same audible output with no Audio Limits ceiling.";

    public bool CanSetLimit => _apoInstalled && _baseActionsEnabled && _globalActionsEnabled && !_operationInProgress;
    public bool CanSetCurrent => CanSetLimit && !HasHardwareVolume && !_muted && _windowsPercent is > 0 and < 100;
    public bool CanRemoveLimit => HasLimit && _apoInstalled && CanSetLimit;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetGlobalActionsEnabled(bool enabled)
    {
        if (_globalActionsEnabled == enabled)
            return;

        _globalActionsEnabled = enabled;
        RaiseActionProperties();
    }

    public void SetOperationState(bool inProgress, string? status = null)
    {
        _operationInProgress = inProgress;
        _operationStatus = inProgress
            ? (string.IsNullOrWhiteSpace(status) ? "Working…" : status)
            : string.Empty;

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsOperationInProgress));
        RaiseActionProperties();
    }

    private void OnVolumeChanged(AudioVolumeState state)
    {
        _dispatcherQueue.TryEnqueue(() => ApplyVolumeState(state));
    }

    private void ApplyVolumeState(AudioVolumeState state)
    {
        _windowsPercent = Math.Clamp(
            (int)Math.Round(state.Scalar * 100.0, MidpointRounding.AwayFromZero),
            0,
            100);
        _muted = state.Muted;

        if (_muted)
        {
            _sameOutputWithoutLimit = "Muted";
        }
        else if (_limit is null)
        {
            _sameOutputWithoutLimit = $"{_windowsPercent}%";
        }
        else if (!_limitActive)
        {
            _sameOutputWithoutLimit = "—";
        }
        else if (HasHardwareVolume)
        {
            _sameOutputWithoutLimit = "Not available";
        }
        else if (_curve is null)
        {
            _sameOutputWithoutLimit = _windowsPercent switch
            {
                0 => "0%",
                100 => $"{_limit.LimitPercent}%",
                _ => "—"
            };
        }
        else
        {
            var endpointDb = _curve.DbAtPercent(state.Scalar * 100.0);
            var actualDb = endpointDb + _limit.AttenuationDb;
            var equivalent = Math.Clamp(
                (int)Math.Round(_curve.PercentAtDb(actualDb), MidpointRounding.AwayFromZero),
                0,
                100);
            _sameOutputWithoutLimit = $"{equivalent}%";
        }

        OnPropertyChanged(nameof(CurrentWindowsPercent));
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(WindowsVolumeText));
        OnPropertyChanged(nameof(SameOutputWithoutLimitText));
        OnPropertyChanged(nameof(SameOutputHelpText));
        OnPropertyChanged(nameof(CanSetCurrent));
        OnPropertyChanged(nameof(SetCurrentHelpText));
    }

    private void RaiseActionProperties()
    {
        OnPropertyChanged(nameof(CanSetLimit));
        OnPropertyChanged(nameof(CanSetCurrent));
        OnPropertyChanged(nameof(CanRemoveLimit));
        OnPropertyChanged(nameof(SetCurrentHelpText));
    }

    private static (string DisplayName, string Description) SplitFriendlyName(string friendlyName)
    {
        if (string.IsNullOrWhiteSpace(friendlyName))
            return ("Playback device", string.Empty);

        var trimmed = friendlyName.Trim();
        if (!trimmed.EndsWith(')'))
            return (trimmed, string.Empty);

        var open = trimmed.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0 || open >= trimmed.Length - 2)
            return (trimmed, string.Empty);

        var display = trimmed[..open].Trim();
        var description = trimmed[(open + 2)..^1].Trim();
        return string.IsNullOrWhiteSpace(description)
            ? (trimmed, string.Empty)
            : (display, description);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose() => _subscription.Dispose();
}
