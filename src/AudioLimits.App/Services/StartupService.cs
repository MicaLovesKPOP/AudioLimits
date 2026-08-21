using Microsoft.Win32;

namespace AudioLimits.App.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Audio Limits";

    private static string CurrentExecutablePath =>
        Environment.ProcessPath
        ?? throw new InvalidOperationException("Could not determine the Audio Limits executable path.");

    // Installed/no-install ZIP builds keep the framework-dependent WinUI host in
    // an app subfolder and a small self-contained AudioLimits.exe launcher one level
    // above it. Route Windows startup through that launcher so a copied/extracted
    // application can repair missing Microsoft runtimes before WinUI starts.
    // Development builds without that canonical layout register the current process.
    private static string LaunchExecutablePath
    {
        get
        {
            var current = CurrentExecutablePath;
            var directory = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(directory))
                return current;

            var siblingLauncher = Path.Combine(directory, "AudioLimits.exe");
            if (!string.Equals(current, siblingLauncher, StringComparison.OrdinalIgnoreCase) && File.Exists(siblingLauncher))
                return siblingLauncher;

            var parent = Directory.GetParent(directory)?.FullName;
            if (string.Equals(Path.GetFileName(directory), "app", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(parent))
            {
                var parentLauncher = Path.Combine(parent, "AudioLimits.exe");
                if (File.Exists(parentLauncher))
                    return parentLauncher;
            }

            return current;
        }
    }

    private static string ExpectedCommand =>
        $"\"{LaunchExecutablePath}\" --background";

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
}
