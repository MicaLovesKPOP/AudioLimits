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
        EndpointSetup = new ApoEndpointSetupInspector(EqualizerApo);
        Provisioning = new EndpointSetupService();
        Startup = new StartupService();
        Limits = new LimitService(
            AudioDevices,
            EqualizerApo,
            SettingsStore,
            SettingsLoad);
        Reconnections = new DeviceReconnectionService(AudioDevices, EqualizerApo, Limits);
    }

    public SettingsStore SettingsStore { get; }
    public SettingsLoadResult SettingsLoad { get; }
    public AudioDeviceService AudioDevices { get; }
    public EqualizerApoService EqualizerApo { get; }
    public ApoEndpointSetupInspector EndpointSetup { get; }
    public EndpointSetupService Provisioning { get; }
    public StartupService Startup { get; }
    public LimitService Limits { get; }
    public DeviceReconnectionService Reconnections { get; }

    public Task EnsureInitializedAsync()
    {
        lock (_initializationLock)
        {
            return _initializationTask ??= Limits.RepairOnStartupAsync();
        }
    }
}
