using AudioLimits.Core.Services;

namespace AudioLimits.App.Services;

public sealed class AppServices
{
    private readonly object _initializationLock = new();
    private Task? _initializationTask;

    public AppServices()
    {
        SettingsStore = new SettingsStore();
        SettingsLoad = SettingsStore.Load();
        AudioDevices = new AudioDeviceService();
        EqualizerApo = new EqualizerApoService();
        Startup = new StartupService();
        Limits = new LimitService(
            AudioDevices,
            EqualizerApo,
            SettingsStore,
            SettingsLoad);
    }

    public SettingsStore SettingsStore { get; }
    public SettingsLoadResult SettingsLoad { get; }
    public AudioDeviceService AudioDevices { get; }
    public EqualizerApoService EqualizerApo { get; }
    public StartupService Startup { get; }
    public LimitService Limits { get; }

    public Task EnsureInitializedAsync()
    {
        lock (_initializationLock)
        {
            return _initializationTask ??= Limits.RepairOnStartupAsync();
        }
    }
}
