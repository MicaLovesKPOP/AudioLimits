using AudioLimits.App.Pages;
using AudioLimits.App.Services;
using AudioLimits.Core.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;

namespace AudioLimits.App;

public sealed partial class MainWindow : Window
{
    private bool _initialPlacementApplied;
    private bool _closeBlockedDialogOpen;
    private bool _hiddenToTray;
    private bool _exitCleanupStarted;
    private OverlappedPresenterState _lastVisiblePresenterState = OverlappedPresenterState.Restored;
    private TrayIconService? _trayIcon;
    private TrayMenuWindow? _trayMenu;

    public bool TrayAvailable => _trayIcon is not null;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
            _lastVisiblePresenterState = presenter.State == OverlappedPresenterState.Maximized
                ? OverlappedPresenterState.Maximized
                : OverlappedPresenterState.Restored;
        }

        AppWindow.Changed += AppWindow_Changed;
        AppWindow.Closing += AppWindow_Closing;

        if (Content is FrameworkElement root)
            root.Loaded += Root_Loaded;

        RootFrame.Navigate(typeof(DevicesPage));
        InitializeTrayIcon();
    }

    private void InitializeTrayIcon()
    {
        try
        {
            _trayIcon = new TrayIconService(this);
            _trayIcon.OpenRequested += TrayIcon_OpenRequested;
            _trayIcon.MenuRequested += TrayIcon_MenuRequested;
        }
        catch (Exception ex)
        {
            // Fail visibly rather than hiding a window to a tray icon that does not exist.
            // Audio limiting itself remains available because the tray is only shell lifecycle.
            _trayIcon = null;
            AppLog.Error("Could not initialize the Audio Limits system tray. Minimize-to-tray is disabled for this run.", ex);
        }
    }

    public void ShowAndActivate()
    {
        _trayMenu?.HideMenu();
        var intendedRestoreState = _lastVisiblePresenterState;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            if (presenter.State == OverlappedPresenterState.Minimized)
                presenter.Restore(false);

            // Restoring can synchronously raise AppWindow.Changed and update the
            // tracked state, so use the state captured before Restore().
            if (_hiddenToTray &&
                intendedRestoreState == OverlappedPresenterState.Maximized &&
                presenter.State != OverlappedPresenterState.Maximized)
            {
                presenter.Maximize();
            }
        }

        _hiddenToTray = false;
        AppWindow.Show();
        Activate();
    }

    public void ShowSettingsAndActivate()
    {
        ShowAndActivate();

        if (App.Services.Limits.IsBusy)
            return;

        if (RootFrame.CurrentSourcePageType != typeof(SettingsPage))
            RootFrame.Navigate(typeof(SettingsPage));
    }

    public void RequestExit()
    {
        if (CanExitSafely())
        {
            Close();
            return;
        }

        // A tray Exit can be requested while the window is hidden. Bring the app
        // forward before explaining why a safety-critical operation cannot be cut off.
        ShowAndActivate();
        DispatcherQueue.TryEnqueue(async () => await ShowCloseBlockedDialogAsync());
    }

    private bool CanExitSafely() =>
        App.Services.Limits.InitializationComplete &&
        !App.Services.Limits.IsBusy;

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialPlacementApplied || sender is not FrameworkElement root || root.XamlRoot is null)
            return;

        _initialPlacementApplied = true;
        var scale = root.XamlRoot.RasterizationScale;
        var width = Math.Max(1, (int)Math.Round(820 * scale));
        var height = Math.Max(1, (int)Math.Round(650 * scale));

        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (display is null)
        {
            AppWindow.Resize(new SizeInt32(width, height));
            return;
        }

        var work = display.WorkArea;
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);
        var x = work.X + Math.Max(0, (work.Width - width) / 2);
        var y = work.Y + Math.Max(0, (work.Height - height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Services.Limits.IsBusy)
            return;

        if (RootFrame.CurrentSourcePageType != typeof(SettingsPage))
            RootFrame.Navigate(typeof(SettingsPage));
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args)
    {
        if (App.Services.Limits.IsBusy)
            return;

        if (RootFrame.CanGoBack)
            RootFrame.GoBack();
    }

    private void RootFrame_Navigated(object sender, NavigationEventArgs e)
    {
        var canGoBack = RootFrame.CanGoBack;
        AppTitleBar.IsBackButtonVisible = canGoBack;
        AppTitleBar.IsBackButtonEnabled = canGoBack && !App.Services.Limits.IsBusy;
        SettingsButton.Visibility = e.SourcePageType == typeof(SettingsPage)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPresenterChange || sender.Presenter is not OverlappedPresenter presenter)
            return;

        if (presenter.State == OverlappedPresenterState.Minimized)
        {
            if (_trayIcon is not null && !_hiddenToTray)
            {
                _hiddenToTray = true;
                sender.Hide();
            }

            return;
        }

        if (presenter.State is OverlappedPresenterState.Restored or OverlappedPresenterState.Maximized)
            _lastVisiblePresenterState = presenter.State;
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (CanExitSafely())
        {
            PrepareForExit();
            return;
        }

        args.Cancel = true;
        if (_closeBlockedDialogOpen)
            return;

        DispatcherQueue.TryEnqueue(async () => await ShowCloseBlockedDialogAsync());
    }

    private void PrepareForExit()
    {
        if (_exitCleanupStarted)
            return;

        _exitCleanupStarted = true;
        AppWindow.Changed -= AppWindow_Changed;

        if (_trayMenu is not null)
        {
            _trayMenu.OpenRequested -= TrayMenu_OpenRequested;
            _trayMenu.SettingsRequested -= TrayMenu_SettingsRequested;
            _trayMenu.ExitRequested -= TrayMenu_ExitRequested;
            _trayMenu.MenuDismissed -= TrayMenu_MenuDismissed;
            _trayMenu.CloseForAppExit();
            _trayMenu = null;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.OpenRequested -= TrayIcon_OpenRequested;
            _trayIcon.MenuRequested -= TrayIcon_MenuRequested;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private void TrayIcon_OpenRequested(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ShowAndActivate);
    }

    private void TrayIcon_MenuRequested(object? sender, TrayMenuRequestedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _trayMenu ??= CreateTrayMenu();
                _trayMenu.ShowAt(e.X, e.Y, settingsEnabled: !App.Services.Limits.IsBusy);
            }
            catch (Exception ex)
            {
                AppLog.Error("Could not show the Audio Limits tray menu", ex);
                _trayIcon?.RestoreShellFocus();
            }
        });
    }

    private TrayMenuWindow CreateTrayMenu()
    {
        var menu = new TrayMenuWindow();
        menu.OpenRequested += TrayMenu_OpenRequested;
        menu.SettingsRequested += TrayMenu_SettingsRequested;
        menu.ExitRequested += TrayMenu_ExitRequested;
        menu.MenuDismissed += TrayMenu_MenuDismissed;
        return menu;
    }

    private void TrayMenu_OpenRequested(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ShowAndActivate);
    }

    private void TrayMenu_SettingsRequested(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ShowSettingsAndActivate);
    }

    private void TrayMenu_ExitRequested(object? sender, EventArgs e)
    {
        // Defer destruction until after the tray-menu click and hide sequence returns.
        DispatcherQueue.TryEnqueue(RequestExit);
    }

    private void TrayMenu_MenuDismissed(object? sender, TrayMenuDismissedEventArgs e)
    {
        // A pointer dismissal is itself a focus-changing user action. Returning
        // focus to the notification area in that case can keep Explorer's overflow
        // flyout alive after the user clicks the taskbar. Restore shell focus only
        // for keyboard cancellation (Escape / Alt+F4), where no new pointer target
        // has claimed focus.
        if (e.ShouldRestoreShellFocus)
            _trayIcon?.RestoreShellFocus();
    }

    private async Task ShowCloseBlockedDialogAsync()
    {
        if (_closeBlockedDialogOpen || Content is not FrameworkElement root || root.XamlRoot is null)
            return;

        _closeBlockedDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = root.XamlRoot,
                Title = "Audio operation in progress",
                Content = new TextBlock
                {
                    Text = "Audio Limits is checking or changing the audio state. Wait for it to finish before closing the app so the device can be restored safely.",
                    TextWrapping = TextWrapping.Wrap
                },
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
        finally
        {
            _closeBlockedDialogOpen = false;
        }
    }
}
