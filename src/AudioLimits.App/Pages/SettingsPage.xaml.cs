using System.Diagnostics;
using System.Reflection;
using AudioLimits.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AudioLimits.App.Pages;

public sealed partial class SettingsPage : Page
{
    private const string RepositoryUrl = "https://github.com/MicaLovesKPOP/AudioLimits";
    private bool _updatingStartup;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshSettings();
    }

    private void RefreshSettings()
    {
        _updatingStartup = true;
        try
        {
            StartupToggle.IsOn = App.Services.Startup.IsEnabled;
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not read Windows startup setting", ex);
            ShowError("Windows startup couldn't be read", ex.Message);
        }
        finally
        {
            _updatingStartup = false;
        }

        var apo = App.Services.EqualizerApo;
        if (!apo.IsInstalled)
        {
            AudioProcessingCard.Header = "Equalizer APO";
            AudioProcessingCard.Description =
                "Required before Audio Limits can set, change, or remove limits. Install it, then reopen Audio Limits.";
            AudioProcessingStatusText.Text = "Not installed";
            AudioProcessingStatusText.Visibility = Visibility.Visible;
            ManageAudioButton.Content = "Get Equalizer APO…";
        }
        else if (apo.DeviceSelectorPath is null)
        {
            AudioProcessingCard.Header = "Equalizer APO";
            AudioProcessingCard.Description =
                "Installed, but its Device Selector couldn't be found. Repair or reinstall Equalizer APO to manage playback devices.";
            AudioProcessingStatusText.Text = "Needs repair";
            AudioProcessingStatusText.Visibility = Visibility.Visible;
            ManageAudioButton.Content = "Get Equalizer APO…";
        }
        else
        {
            AudioProcessingCard.Header = "Playback devices";
            AudioProcessingCard.Description =
                "Choose which playback devices Audio Limits can control.";
            AudioProcessingStatusText.Text = string.Empty;
            AudioProcessingStatusText.Visibility = Visibility.Collapsed;
            ManageAudioButton.Content = "Manage devices…";
        }

        var informationalVersion = typeof(SettingsPage).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        VersionText.Text = string.IsNullOrWhiteSpace(informationalVersion)
            ? "1.0.0-rc.2"
            : informationalVersion.Split('+')[0];
    }

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updatingStartup)
            return;

        try
        {
            App.Services.Startup.SetEnabled(StartupToggle.IsOn);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not change Windows startup setting", ex);
            _updatingStartup = true;
            StartupToggle.IsOn = !StartupToggle.IsOn;
            _updatingStartup = false;
            ShowError("Windows startup couldn't be changed", ex.Message);
        }
    }

    private void ManageAudioButton_Click(object sender, RoutedEventArgs e)
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
            AppLog.Error("Could not open audio setup from Settings", ex);
            ShowError("Audio setup couldn't be opened", ex.Message);
        }
    }

    private void OpenGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not open the Audio Limits GitHub repository", ex);
            ShowError("GitHub couldn't be opened", ex.Message);
        }
    }

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLog.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLog.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not open diagnostic log folder", ex);
            ShowError("Log folder couldn't be opened", ex.Message);
        }
    }

    private void ShowError(string title, string message)
    {
        SettingsInfoBar.Severity = InfoBarSeverity.Error;
        SettingsInfoBar.Title = title;
        SettingsInfoBar.Message = message;
        SettingsInfoBar.IsOpen = true;
    }
}
