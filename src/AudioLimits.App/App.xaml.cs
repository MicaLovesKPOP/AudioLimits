using AudioLimits.App.Services;
using AudioLimits.Core.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AudioLimits.App;

public partial class App : Application
{
    private MainWindow? _window;
    private SingleInstanceCoordinator? _singleInstance;

    public App()
    {
        InitializeComponent();
        AppLog.Initialize();
        UnhandledException += OnUnhandledException;
    }

    public static AppServices Services { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.Info("Audio Limits 1.0.0-rc.2 starting");

        _singleInstance = new SingleInstanceCoordinator();
        if (!_singleInstance.IsPrimary)
        {
            _singleInstance.ActivatePrimary();
            _singleInstance.Dispose();
            _singleInstance = null;
            Exit();
            return;
        }

        Services = new AppServices();

        _window = new MainWindow();
        var startInBackground = Environment.GetCommandLineArgs().Any(
            x => string.Equals(x, "--background", StringComparison.OrdinalIgnoreCase));

        // Startup reconciliation must run even when Windows launches Audio Limits
        // in the background and no page has been made visible yet. DevicesPage awaits
        // the same cached task on normal foreground launch.
        _ = InitializeServicesAsync();

        var uiQueue = DispatcherQueue.GetForCurrentThread();
        _singleInstance.ActivationRequested += (_, _) =>
        {
            uiQueue.TryEnqueue(() => _window?.ShowAndActivate());
        };
        _singleInstance.StartListening();

        _window.Closed += (_, _) =>
        {
            _singleInstance?.Dispose();
            _singleInstance = null;
        };

        // A background launch must never leave an inaccessible invisible process.
        // If tray initialization failed, fall back to showing the main window.
        if (!startInBackground || !_window.TrayAvailable)
            _window.Activate();
    }

    private static async Task InitializeServicesAsync()
    {
        try
        {
            await Services.EnsureInitializedAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("Background startup reconciliation failed", ex);
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled Audio Limits WinUI exception", e.Exception);
    }
}
