using AudioLimits.Core.Services;
using Microsoft.Win32;

namespace AudioLimits.App.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Audio Limits";

    private static string CurrentExecutablePath =>
        Environment.ProcessPath
        ?? throw new InvalidOperationException("Could not determine the Audio Limits executable path.");

    private static string ExpectedCommand =>
        $"\"{CurrentExecutablePath}\" --background";

    public StartupService()
    {
        MigrateLegacyLauncherRegistration();
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value &&
                   string.Equals(value.Trim(), ExpectedCommand, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException("Could not open the Windows startup registry key.");

        if (enabled)
            key.SetValue(ValueName, ExpectedCommand, RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static void MigrateLegacyLauncherRegistration()
    {
        try
        {
            var appPath = CurrentExecutablePath;
            var appDirectory = Path.GetDirectoryName(appPath);
            if (string.IsNullOrWhiteSpace(appDirectory) ||
                !string.Equals(Path.GetFileName(appDirectory), "app", StringComparison.OrdinalIgnoreCase))
                return;

            var rootDirectory = Directory.GetParent(appDirectory)?.FullName;
            if (string.IsNullOrWhiteSpace(rootDirectory))
                return;

            var launcherPath = Path.Combine(rootDirectory, "AudioLimits.exe");
            if (!File.Exists(launcherPath))
                return;

            var legacyCommand = $"\"{launcherPath}\" --background";
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not string current ||
                !string.Equals(current.Trim(), legacyCommand, StringComparison.OrdinalIgnoreCase))
                return;

            key.SetValue(ValueName, ExpectedCommand, RegistryValueKind.String);
            AppLog.Info("Migrated Start with Windows from the prerequisite launcher to the direct app host.");
        }
        catch (Exception ex)
        {
            // A best-effort migration must never prevent Audio Limits from starting.
            AppLog.Warn("Could not migrate the legacy Start with Windows registration: " + ex.Message);
        }
    }
}
