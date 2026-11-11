using System.Collections.ObjectModel;
using AudioLimits.App.Services;
using AudioLimits.App.ViewModels;
using AudioLimits.Core.Models;
using AudioLimits.Core.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AudioLimits.App.Pages;

public sealed partial class DevicesPage : Page
{
    private bool _loadedOnce;
    private bool _operationInProgress;
    private bool _deviceMonitorChecking;
    private string _deviceStateSignature = string.Empty;
    private DispatcherQueueTimer? _deviceMonitor;

    private sealed record DeviceListSnapshot(IReadOnlyList<AudioDeviceInfo> Devices, string Signature);

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
            if (!_loadedOnce)
                return;
            await RefreshDevicesAsync();
            if (!_loadedOnce)
                return;
            ConfigureStateInfo();
            StartDeviceMonitor();

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

    private static DeviceListSnapshot ReadDeviceListSnapshot()
    {
        var services = App.Services;
        var deviceInfos = services.AudioDevices.GetActiveRenderDevices();

        // Include both Windows endpoint identity and Equalizer APO registration.
        // An external Device Selector edit or a USB device reconnect can change
        // these while Audio Limits is already running.
        var signature = $"APO installed: {services.EqualizerApo.IsInstalled}\n" +
            string.Join("\n", deviceInfos.Select(info =>
                $"{info.Id}|{info.FriendlyName}|{info.HardwareKey}|{services.EqualizerApo.GetPreferredProcessingStage(info.EndpointGuid)}|{services.Limits.FindLimit(info.EndpointGuid)?.LimitPercent}"));
        return new DeviceListSnapshot(deviceInfos, signature);
    }

    private async Task RefreshDevicesAsync()
    {
        var snapshot = await Task.Run(ReadDeviceListSnapshot);
        if (_loadedOnce)
            ShowDeviceSnapshot(snapshot);
    }

    private void ShowDeviceSnapshot(DeviceListSnapshot snapshot)
    {
        ClearDevices();

        var services = App.Services;
        foreach (var info in snapshot.Devices)
        {
            try
            {
                Devices.Add(new DeviceViewModel(
                    info,
                    services.Limits,
                    services.AudioDevices,
                    services.EqualizerApo,
                    services.Limits.FindRememberedLimit(info, snapshot.Devices),
                    DispatcherQueue));
            }
            catch (Exception ex)
            {
                // An endpoint can vanish between the enumeration and volume
                // subscription, especially during a USB unplug/replug.
                AppLog.Error($"Could not attach to playback device '{info.FriendlyName}'", ex);
            }
        }

        _deviceStateSignature = snapshot.Signature;
        LoadingPanel.Visibility = Visibility.Collapsed;
        DeviceRepeater.Visibility = Devices.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyStatePanel.Visibility = Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartDeviceMonitor()
    {
        if (_deviceMonitor is not null)
            return;

        _deviceMonitor = DispatcherQueue.CreateTimer();
        _deviceMonitor.Interval = TimeSpan.FromSeconds(3);
        _deviceMonitor.IsRepeating = true;
        _deviceMonitor.Tick += DeviceMonitor_Tick;
        _deviceMonitor.Start();
    }

    private async void DeviceMonitor_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_deviceMonitorChecking || _operationInProgress || App.Services.Limits.IsBusy)
            return;

        _deviceMonitorChecking = true;
        try
        {
            var snapshot = await Task.Run(ReadDeviceListSnapshot);
            if (!_loadedOnce || _operationInProgress || App.Services.Limits.IsBusy ||
                string.Equals(snapshot.Signature, _deviceStateSignature, StringComparison.Ordinal))
                return;

            ShowDeviceSnapshot(snapshot);
            ConfigureStateInfo();
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not refresh playback device registration", ex);
        }
        finally
        {
            _deviceMonitorChecking = false;
        }
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
            StateInfoBar.Message = "Audio Limits uses Equalizer APO to apply volume limits. Install it to continue.";
            StateInfoBar.ActionButton = CreateInstallApoButton();
            StateInfoBar.IsOpen = true;
            return;
        }

        if (Devices.Count > 0 && Devices.All(x => !x.ApoReady))
        {
            StateInfoBar.Severity = InfoBarSeverity.Warning;
            StateInfoBar.Title = "Audio setup required";
            StateInfoBar.Message = "Equalizer APO is installed. Choose the playback devices you want Audio Limits to control.";
            StateInfoBar.ActionButton = CreateSetupButton("Set up playback devices…");
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

    private Button CreateInstallApoButton()
    {
        var button = new Button { Content = "Install Equalizer APO…" };
        button.Click += async (_, _) => await InstallEqualizerApoAsync();
        return button;
    }
    private DeviceViewModel? FindDeviceFromButton(object sender)
    {
        if (sender is not Button { Tag: string id })
            return null;

        return Devices.FirstOrDefault(x => string.Equals(x.Device.Id, id, StringComparison.Ordinal));
    }

    private async void RestoreLimitButton_Click(object sender, RoutedEventArgs e)
    {
        var device = FindDeviceFromButton(sender);
        if (device is null || !device.HasRememberedLimit || _operationInProgress)
            return;

        if (!await EnsureSetupForChangeAsync(device))
            return;

        await RunLimitOperationAsync(
            device,
            "Restoring saved limit...",
            () => App.Services.Limits.SetLimitAsync(
                device.Device, device.RememberedLimitPercent));
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
        var valueText = new TextBlock
        {
            Text = Math.Clamp(initialPercent, 1, 100).ToString(),
            MinWidth = 28,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var slider = new Slider
        {
            Minimum = 1,
            Maximum = 100,
            Value = Math.Clamp(initialPercent, 1, 100),
            StepFrequency = 1,
            SmallChange = 1,
            LargeChange = 10,
            IsThumbToolTipEnabled = true,
            MinWidth = 280,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(
            slider,
            $"Output limit percentage for {device.DisplayName}");

        var sliderRow = new Grid
        {
            ColumnSpacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sliderRow.Children.Add(valueText);
        Grid.SetColumn(slider, 1);
        sliderRow.Children.Add(slider);

        var limitInput = new StackPanel { Spacing = 4 };
        limitInput.Children.Add(new TextBlock { Text = "Output limit (%)" });
        limitInput.Children.Add(sliderRow);

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
        content.Children.Add(limitInput);
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
            var percent = Math.Clamp(
                (int)Math.Round(slider.Value, MidpointRounding.AwayFromZero),
                1,
                100);
            valueText.Text = percent.ToString();

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
                        "Changing the limit may change the current loudness. Audio may briefly mute while the change is applied.";
                }
                else if (device.LimitPercent is { } currentLimit2 && percent < currentLimit2)
                {
                    transitionNote.Text =
                        "Changing the limit may change the current loudness. Audio may briefly mute while the change is applied.";
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

        slider.ValueChanged += (_, _) => UpdateDialogState();
        slider.PointerWheelChanged += (_, e) =>
        {
            var delta = e.GetCurrentPoint(slider).Properties.MouseWheelDelta;
            if (delta == 0)
                return;

            var current = Math.Round(slider.Value, MidpointRounding.AwayFromZero);
            slider.Value = Math.Clamp(
                current + (delta > 0 ? 1 : -1),
                slider.Minimum,
                slider.Maximum);
            e.Handled = true;
        };

        UpdateDialogState();

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return null;

        return Math.Clamp(
            (int)Math.Round(slider.Value, MidpointRounding.AwayFromZero),
            1,
            100);
    }    private async Task<bool> ConfirmHardwareLimitRemovalAsync(DeviceViewModel device)
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

        if (!apo.IsInstalled)
        {
            var installDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Equalizer APO is required",
                Content = new TextBlock
                {
                    Text = "Audio Limits uses Equalizer APO to apply volume limits. Install it now?",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "Install Equalizer APO",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await installDialog.ShowAsync() == ContentDialogResult.Primary)
                await InstallEqualizerApoAsync();

            return false;
        }

        if (device.ApoReady)
            return true;

        var selectorAvailable = apo.DeviceSelectorPath is not null;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Audio setup required",
            Content = new TextBlock
            {
                Text = selectorAvailable
                    ? $"To set a limit for '{device.Device.FriendlyName}', Equalizer APO must first be enabled for that exact playback device.\n\nIn the Device Selector, tick the playback device with this name (not its microphone), then click OK. Leave troubleshooting options unchanged unless there is a problem. Audio Limits will automatically recheck when the selector closes."
                    : "Equalizer APO is installed, but its Device Selector couldn't be found. Repair Equalizer APO now?",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = selectorAvailable
                ? "Set up playback devices"
                : "Repair Equalizer APO",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (selectorAvailable)
            {
                await OpenAudioSetupAsync();
                // If Device Selector successfully registered this endpoint,
                // resume the original action instead of making the user click
                // Set/Restore a second time. Do not trust the old VM's status.
                if (apo.IsActiveForEndpoint(device.Device.EndpointGuid) &&
                    limits.CanModifyLimits)
                    return true;

                ShowOperationInfo(
                    "Playback device still needs setup",
                    $"Equalizer APO has not been detected for '{device.Device.FriendlyName}'. " +
                    "No new volume limit was applied. Audio Limits checks for device changes automatically; " +
                    "if the selector requested an audio-service or Windows restart, complete that step first.");
                return false;
            }

            await InstallEqualizerApoAsync();
        }

        return false;
    }    private async Task RunLimitOperationAsync(
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

    private async Task InstallEqualizerApoAsync()
    {
        if (_operationInProgress)
            return;

        _operationInProgress = true;
        SetAllActionsEnabled(false);

        try
        {
            StateInfoBar.ActionButton = null;
            StateInfoBar.Severity = InfoBarSeverity.Informational;
            StateInfoBar.Title = "Preparing Equalizer APO";
            StateInfoBar.Message = "Audio Limits will show setup progress in a separate window.";
            StateInfoBar.IsClosable = false;
            StateInfoBar.IsOpen = true;

            var exitCode = await DependencySetupService.InstallOrRepairEqualizerApoAsync();

            if (exitCode == 3)
            {
                await RefreshDevicesAsync();
                ConfigureStateInfo();
                return;
            }

            if (exitCode != 0)
            {
                ShowOperationError(
                    "Equalizer APO setup didn't finish",
                    "Audio Limits couldn't complete Equalizer APO setup. The preparation window may contain more details.");
                return;
            }

            if (!App.Services.EqualizerApo.IsInstalled)
            {
                ShowOperationError(
                    "Equalizer APO wasn't detected",
                    "Setup finished, but Audio Limits still can't find Equalizer APO. Restart Windows and try again.");
                return;
            }

            await RefreshDevicesAsync();
            ConfigureStateInfo();
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not install or repair Equalizer APO", ex);
            ShowOperationError("Equalizer APO setup couldn't start", ex.Message);
        }
        finally
        {
            _operationInProgress = false;
            SetAllActionsEnabled(true);
        }
    }

    private async Task OpenAudioSetupAsync()
    {
        try
        {
            var apo = App.Services.EqualizerApo;
            if (apo.DeviceSelectorPath is null)
            {
                await InstallEqualizerApoAsync();
                return;
            }

            using var selector = apo.OpenDeviceSelector();
            await selector.WaitForExitAsync();

            await RefreshDevicesAsync();
            ConfigureStateInfo();
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not open audio setup", ex);
            ShowOperationError("Audio setup couldn't be opened", ex.Message);
        }
    }    private void ShowOperationInfo(string title, string message)
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
        _loadedOnce = false;
        if (_deviceMonitor is not null)
        {
            _deviceMonitor.Stop();
            _deviceMonitor.Tick -= DeviceMonitor_Tick;
            _deviceMonitor = null;
        }
        ClearDevices();
    }
}
