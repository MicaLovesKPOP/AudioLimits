using System.Collections.ObjectModel;
using AudioLimits.App.ViewModels;
using AudioLimits.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AudioLimits.App.Pages;

public sealed partial class DevicesPage : Page
{
    private bool _loadedOnce;
    private bool _operationInProgress;

    public DevicesPage()
    {
        InitializeComponent();
        Loaded += DevicesPage_Loaded;
        Unloaded += DevicesPage_Unloaded;
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = new();

    private async void DevicesPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce)
            return;
        _loadedOnce = true;

        try
        {
            await App.Services.EnsureInitializedAsync();
            ConfigureStateInfo();
            await RefreshDevicesAsync();

            var notice = App.Services.Limits.TakeOperationNotice();
            if (!string.IsNullOrWhiteSpace(notice))
                ShowOperationInfo("Audio state restored", notice);
        }
        catch (Exception ex)
        {
            AppLog.Error("WinUI device surface could not initialize", ex);
            LoadingPanel.Visibility = Visibility.Collapsed;
            DeviceRepeater.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ShowOperationError(
                "Playback devices couldn't be read",
                "Audio Limits left existing audio processing as safely as possible. Close and reopen the app, or check the log if this persists.");
        }
    }

    private async Task RefreshDevicesAsync()
    {
        ClearDevices();

        var services = App.Services;
        var deviceInfos = await Task.Run(services.AudioDevices.GetActiveRenderDevices);
        foreach (var info in deviceInfos)
        {
            Devices.Add(new DeviceViewModel(
                info,
                services.Limits,
                services.AudioDevices,
                services.EqualizerApo,
                DispatcherQueue));
        }

        LoadingPanel.Visibility = Visibility.Collapsed;
        DeviceRepeater.Visibility = Devices.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyStatePanel.Visibility = Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConfigureStateInfo()
    {
        var services = App.Services;
        var limits = services.Limits;
        var load = services.SettingsLoad;
        var apo = services.EqualizerApo;

        StateInfoBar.ActionButton = null;
        StateInfoBar.IsClosable = false;
        StateInfoBar.IsOpen = false;

        if (!load.IsAuthoritative || limits.StateUncertain)
        {
            StateInfoBar.Severity = InfoBarSeverity.Error;
            StateInfoBar.Title = "Saved state couldn't be verified";
            StateInfoBar.Message = limits.StartupIssueMessage ?? load.Message ??
                "Audio Limits left existing audio processing unchanged.";
            StateInfoBar.IsOpen = true;
            return;
        }

        if (limits.HasPendingChange)
        {
            StateInfoBar.Severity = InfoBarSeverity.Warning;
            StateInfoBar.Title = "A previous change still needs recovery";
            StateInfoBar.Message = limits.StartupIssueMessage ??
                "Make the affected playback device available and restart Audio Limits before making another change.";
            if (apo.IsInstalled)
                StateInfoBar.ActionButton = CreateSetupButton("Manage audio devices…");
            StateInfoBar.IsOpen = true;
            return;
        }

        if (!apo.IsInstalled)
        {
            StateInfoBar.Severity = InfoBarSeverity.Warning;
            StateInfoBar.Title = "Equalizer APO is required";
            StateInfoBar.Message = "Install Equalizer APO before setting, changing, or removing limits, then reopen Audio Limits.";
            StateInfoBar.ActionButton = CreateSetupButton("Get Equalizer APO…");
            StateInfoBar.IsOpen = true;
            return;
        }

        if (load.Status == SettingsLoadStatus.RecoveredFromBackup && !string.IsNullOrWhiteSpace(load.Message))
        {
            StateInfoBar.Severity = InfoBarSeverity.Informational;
            StateInfoBar.Title = "Settings recovered";
            StateInfoBar.Message = load.Message;
            StateInfoBar.IsClosable = true;
            StateInfoBar.IsOpen = true;
        }
    }

    private Button CreateSetupButton(string text)
    {
        var button = new Button { Content = text };
        button.Click += async (_, _) => await OpenAudioSetupAsync();
        return button;
    }

    private DeviceViewModel? FindDeviceFromButton(object sender)
    {
        if (sender is not Button { Tag: string id })
            return null;

        return Devices.FirstOrDefault(x => string.Equals(x.Device.Id, id, StringComparison.Ordinal));
    }

    private async void SetLimitButton_Click(object sender, RoutedEventArgs e)
    {
        var device = FindDeviceFromButton(sender);
        if (device is null || _operationInProgress)
            return;

        if (!await EnsureSetupForChangeAsync(device))
            return;

        var initial = device.LimitPercent;
        if (initial is null)
        {
            initial = device.CurrentWindowsPercent >= 100
                ? 50
                : Math.Clamp(device.CurrentWindowsPercent, 1, 99);
        }

        var chosen = await ShowLimitDialogAsync(device, initial.Value);
        if (chosen is null)
            return;

        await RunLimitOperationAsync(
            device,
            chosen.Value >= 100
                ? "Removing limit…"
                : device.HasLimit ? "Changing limit…" : "Applying limit…",
            () => App.Services.Limits.SetLimitAsync(device.Device, chosen.Value));
    }

    private async void SetCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        var device = FindDeviceFromButton(sender);
        if (device is null || _operationInProgress)
            return;

        if (!await EnsureSetupForChangeAsync(device))
            return;

        await RunLimitOperationAsync(
            device,
            "Setting current output as limit…",
            () => App.Services.Limits.SetCurrentOutputAsLimitAsync(device.Device));
    }

    private async void RemoveLimitButton_Click(object sender, RoutedEventArgs e)
    {
        var device = FindDeviceFromButton(sender);
        if (device is null || _operationInProgress)
            return;

        if (device.HasHardwareVolume && device.IsLimitActive && !await ConfirmHardwareLimitRemovalAsync(device))
            return;

        await RunLimitOperationAsync(
            device,
            "Removing limit…",
            () => App.Services.Limits.RemoveLimitAsync(device.Device));
    }

    private async Task<int?> ShowLimitDialogAsync(DeviceViewModel device, int initialPercent)
    {
        var numberBox = new NumberBox
        {
            Header = "Output limit (%)",
            Minimum = 1,
            Maximum = 100,
            SmallChange = 1,
            Value = Math.Clamp(initialPercent, 1, 100),
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            Width = 150,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        AutomationProperties.SetName(numberBox, $"Output limit percentage for {device.DisplayName}");

        var explanation = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };
        var transitionNote = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };

        var content = new StackPanel { Spacing = 12, MaxWidth = 440 };
        content.Children.Add(new TextBlock
        {
            Text = device.DisplayName,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });
        content.Children.Add(numberBox);
        content.Children.Add(explanation);
        content.Children.Add(transitionNote);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = device.HasLimit ? "Change limit" : "Set limit",
            Content = content,
            PrimaryButtonText = "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        void UpdateDialogState()
        {
            if (double.IsNaN(numberBox.Value) ||
                numberBox.Value < 1 ||
                numberBox.Value > 100 ||
                Math.Abs(numberBox.Value - Math.Round(numberBox.Value)) > 0.0001)
            {
                dialog.IsPrimaryButtonEnabled = false;
                explanation.Text = "Enter a whole percentage from 1 to 100.";
                transitionNote.Text = string.Empty;
                return;
            }

            var percent = Math.Clamp(
                (int)Math.Round(numberBox.Value, MidpointRounding.AwayFromZero),
                1,
                100);
            var removing = percent == 100;
            var unchanged = device.HasLimit && device.LimitPercent == percent;

            dialog.PrimaryButtonText = removing && device.HasLimit
                ? "Remove limit"
                : "Apply";
            dialog.IsPrimaryButtonEnabled = device.HasLimit
                ? !unchanged
                : !removing;

            if (removing)
            {
                explanation.Text = device.HasHardwareVolume
                    ? "100% removes the limit. This device may become louder because Audio Limits can't calculate an exact equivalent without the limit."
                    : "100% removes the limit. Windows volume will use its normal unrestricted range.";
                transitionNote.Text = device.HasHardwareVolume
                    ? "Lower Windows volume first if needed. Audio may briefly mute while the change is applied."
                    : "Audio may briefly mute while the limit is removed safely.";
                return;
            }

            if (device.HasHardwareVolume)
            {
                explanation.Text =
                    "This device controls Windows volume in hardware. The actual loudness may not match unrestricted Windows volume at the same percentage.";

                if (!device.HasLimit)
                {
                    transitionNote.Text =
                        "Applying this limit may make the current output quieter. Audio may briefly mute while the change is applied.";
                }
                else if (device.LimitPercent is { } currentLimit && percent > currentLimit)
                {
                    transitionNote.Text =
                        "Raising the limit may make the current output louder. Lower Windows volume first if needed. Audio may briefly mute while the change is applied.";
                }
                else if (device.LimitPercent is { } currentLimit2 && percent < currentLimit2)
                {
                    transitionNote.Text =
                        "Lowering the limit may make the current output quieter. Audio may briefly mute while the change is applied.";
                }
                else
                {
                    transitionNote.Text =
                        "Changing the limit may change the current loudness. Audio may briefly mute while the change is applied.";
                }
            }
            else
            {
                explanation.Text =
                    $"At Windows volume 100%, this device will be as loud as unrestricted Windows volume at {percent}%.";
                transitionNote.Text =
                    "Audio may briefly mute while Audio Limits safely applies the change.";
            }
        }

        numberBox.ValueChanged += (_, _) => UpdateDialogState();
        UpdateDialogState();

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || double.IsNaN(numberBox.Value))
            return null;

        return Math.Clamp(
            (int)Math.Round(numberBox.Value, MidpointRounding.AwayFromZero),
            1,
            100);
    }

    private async Task<bool> ConfirmHardwareLimitRemovalAsync(DeviceViewModel device)
    {
        var content = new StackPanel { Spacing = 10, MaxWidth = 440 };
        content.Children.Add(new TextBlock
        {
            Text = device.DisplayName,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
        });
        content.Children.Add(new TextBlock
        {
            Text = "This device controls Windows volume in hardware, so Audio Limits can't preserve the current loudness exactly. Removing the limit may make the device louder.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "Lower Windows volume first if needed.",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Remove limit?",
            Content = content,
            PrimaryButtonText = "Remove limit",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<bool> EnsureSetupForChangeAsync(DeviceViewModel device)
    {
        var limits = App.Services.Limits;
        var apo = App.Services.EqualizerApo;

        if (!limits.CanModifyLimits)
        {
            ShowOperationError(
                "Limits can't be changed yet",
                limits.StartupIssueMessage ??
                "Audio Limits is still checking or repairing the current audio state.");
            return false;
        }

        if (apo.IsInstalled && device.ApoReady)
            return true;

        var selectorAvailable = apo.DeviceSelectorPath is not null;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Audio setup required",
            Content = new TextBlock
            {
                Text = !apo.IsInstalled
                    ? "Audio Limits needs Equalizer APO before it can apply limits. Open the official download page now?"
                    : selectorAvailable
                        ? "This playback device needs to be enabled for Audio Limits before it can be limited. Open Manage devices now?"
                        : "This playback device needs audio setup, but Equalizer APO's Device Selector couldn't be found. Open the download page so you can repair or reinstall Equalizer APO?",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = apo.IsInstalled && selectorAvailable
                ? "Manage devices"
                : "Get Equalizer APO",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await OpenAudioSetupAsync();

        return false;
    }

    private async Task RunLimitOperationAsync(
        DeviceViewModel target,
        string operationStatus,
        Func<Task> operation)
    {
        if (_operationInProgress)
            return;

        _operationInProgress = true;
        SetAllActionsEnabled(false);
        target.SetOperationState(true, operationStatus);

        Exception? failure = null;
        string? notice = null;

        try
        {
            await operation();
            notice = App.Services.Limits.TakeOperationNotice();
        }
        catch (Exception ex)
        {
            failure = ex;
            AppLog.Error("User-requested WinUI limit operation failed", ex);
        }
        finally
        {
            target.SetOperationState(false);
            _operationInProgress = false;

            try
            {
                ConfigureStateInfo();
                await RefreshDevicesAsync();
            }
            catch (Exception refreshEx)
            {
                AppLog.Error("Could not refresh device cards after a limit operation", refreshEx);
                failure ??= refreshEx;
            }
        }

        if (failure is not null)
        {
            ShowOperationError("Limit couldn't be changed", failure.Message);
            return;
        }

        if (!string.IsNullOrWhiteSpace(notice))
            ShowOperationInfo("Audio Limits", notice);
    }

    private void SetAllActionsEnabled(bool enabled)
    {
        foreach (var device in Devices)
            device.SetGlobalActionsEnabled(enabled);
    }

    private async Task OpenAudioSetupAsync()
    {
        try
        {
            var apo = App.Services.EqualizerApo;
            if (apo.DeviceSelectorPath is not null)
                apo.OpenDeviceSelector();
            else
                apo.OpenDownloadPage();
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not open audio setup", ex);
            ShowOperationError("Audio setup couldn't be opened", ex.Message);
        }

        await Task.CompletedTask;
    }

    private void ShowOperationInfo(string title, string message)
    {
        StateInfoBar.ActionButton = null;
        StateInfoBar.Severity = InfoBarSeverity.Informational;
        StateInfoBar.Title = title;
        StateInfoBar.Message = message;
        StateInfoBar.IsClosable = true;
        StateInfoBar.IsOpen = true;
    }

    private void ShowOperationError(string title, string message)
    {
        StateInfoBar.ActionButton = null;
        StateInfoBar.Severity = InfoBarSeverity.Error;
        StateInfoBar.Title = title;
        StateInfoBar.Message = message;
        StateInfoBar.IsClosable = true;
        StateInfoBar.IsOpen = true;
    }

    private void ClearDevices()
    {
        foreach (var device in Devices)
            device.Dispose();
        Devices.Clear();
    }

    private void DevicesPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ClearDevices();
        _loadedOnce = false;
    }
}
