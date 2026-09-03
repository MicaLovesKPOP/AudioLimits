using System.Diagnostics;

namespace AudioLimits.App.Services;

public static class DependencySetupService
{
    private const string InstallEqualizerApoArgument = "--install-equalizer-apo";

    public static async Task<int> InstallOrRepairEqualizerApoAsync()
    {
        var launcherPath = FindLauncherPath()
                           ?? throw new FileNotFoundException(
                               "Audio Limits' prerequisite launcher was not found. Reinstall Audio Limits or use the complete no-install folder.");

        var workingDirectory = Path.GetDirectoryName(launcherPath)
                               ?? throw new InvalidOperationException(
                                   "Could not determine Audio Limits' application folder.");

        var startInfo = new ProcessStartInfo
        {
            FileName = launcherPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            Arguments = InstallEqualizerApoArgument
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException(
                                "Windows did not start Audio Limits' setup launcher.");

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static string? FindLauncherPath()
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current))
            return null;

        var directory = Path.GetDirectoryName(current);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        if (string.Equals(
                Path.GetFileName(directory),
                "app",
                StringComparison.OrdinalIgnoreCase))
        {
            var root = Directory.GetParent(directory)?.FullName;
            if (!string.IsNullOrWhiteSpace(root))
            {
                var rootLauncher = Path.Combine(root, "AudioLimits.exe");
                if (File.Exists(rootLauncher))
                    return rootLauncher;
            }
        }

        var sibling = Path.Combine(directory, "AudioLimits.exe");
        if (File.Exists(sibling) &&
            !string.Equals(sibling, current, StringComparison.OrdinalIgnoreCase))
            return sibling;

        return null;
    }
}
