using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AudioLimits.Launcher;

internal static class Program
{
    private const string Version = "1.0.0-rc.2";
    private const string AppSubdirectoryName = "app";
    private const string AppExecutableName = "AudioLimits.App.exe";

    private const string DotNetRegistryKey = @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App";
    private const string VcRuntimeRegistryKey = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";

    private const string DotNetUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe";
    private const string VcRuntimeUrl = "https://aka.ms/vc14/vc_redist.x64.exe";
    private const string WindowsAppRuntimeUrl = "https://aka.ms/windowsappsdk/2.3/2.3.1/windowsappruntimeinstall-x64.exe";

    private static readonly System.Version MinimumVcRuntimeVersion = new(14, 50, 0, 0);

    private const uint MbOk = 0x00000000;
    private const uint MbYesNo = 0x00000004;
    private const uint MbIconError = 0x00000010;
    private const uint MbIconInformation = 0x00000040;
    private const uint MbSetForeground = 0x00010000;
    private const uint MbTaskModal = 0x00002000;
    private const int IdYes = 6;
    private const int ErrorCancelled = 1223;

    [STAThread]
    private static int Main(string[] args)
    {
        LauncherLog.Initialize();
        LauncherLog.Write($"Audio Limits {Version} launcher starting from {AppContext.BaseDirectory}");

        try
        {
            var appDirectory = Path.Combine(AppContext.BaseDirectory, AppSubdirectoryName);
            var appPath = Path.Combine(appDirectory, AppExecutableName);
            if (!File.Exists(appPath))
            {
                ShowError(
                    "Audio Limits is incomplete",
                    $"{AppSubdirectoryName}\\{AppExecutableName} is missing.\n\nReinstall Audio Limits or copy/extract the complete Audio Limits folder, then try again.");
                return 2;
            }

            var missing = Prerequisites.GetMissing();
            if (missing.Count > 0)
            {
                LauncherLog.Write("Missing prerequisites: " + string.Join(", ", missing.Select(x => x.DisplayName)));
                if (!AskToInstall(missing))
                {
                    LauncherLog.Write("User declined prerequisite installation.");
                    return 3;
                }

                var result = Prerequisites.InstallMissingAsync(missing).GetAwaiter().GetResult();
                if (!result.Success)
                {
                    ShowError("Audio Limits couldn't prepare this PC", result.Message);
                    return 4;
                }

                if (result.RestartRequired)
                {
                    ShowInformation(
                        "Restart required",
                        "A required Microsoft component was installed, but Windows needs to restart before Audio Limits can start.\n\nRestart Windows, then open Audio Limits again.");
                    return 0;
                }

                var remaining = Prerequisites.GetMissing();
                if (remaining.Count > 0)
                {
                    ShowError(
                        "Audio Limits still needs a Microsoft component",
                        "Audio Limits installed the required components, but Windows still reports that one or more are unavailable:\n\n" +
                        string.Join("\n", remaining.Select(x => "• " + x.DisplayName)) +
                        "\n\nRestart Windows and try again. If the problem continues, run Audio Limits Setup to repair the installation.");
                    return 5;
                }
            }

            return LaunchApplication(appPath, appDirectory, args);
        }
        catch (Exception ex)
        {
            LauncherLog.Write("Fatal launcher error: " + ex);
            ShowError(
                "Audio Limits couldn't start",
                "An unexpected error occurred while preparing Audio Limits.\n\n" + ex.Message +
                "\n\nA diagnostic log was written to:\n" + LauncherLog.PathForDisplay);
            return 1;
        }
    }

    private static bool AskToInstall(IReadOnlyList<Prerequisite> missing)
    {
        var items = string.Join("\n", missing.Select(x => "• " + x.DisplayName));
        var text =
            "Audio Limits needs the following Microsoft component" + (missing.Count == 1 ? "" : "s") + " before it can start:\n\n" +
            items +
            "\n\nAudio Limits can download them directly from Microsoft and install only what is missing. Windows may ask for administrator permission.\n\nContinue?";

        return MessageBoxW(0, text, "Prepare Audio Limits", MbYesNo | MbIconInformation | MbSetForeground | MbTaskModal) == IdYes;
    }

    private static int LaunchApplication(string appPath, string appDirectory, string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = appPath,
            WorkingDirectory = appDirectory,
            UseShellExecute = false
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("Windows did not start the Audio Limits application process.");

        LauncherLog.Write($"Started {AppExecutableName} (PID {process.Id}).");
        return 0;
    }

    private static void ShowError(string title, string message) =>
        MessageBoxW(0, message, title, MbOk | MbIconError | MbSetForeground | MbTaskModal);

    private static void ShowInformation(string title, string message) =>
        MessageBoxW(0, message, title, MbOk | MbIconInformation | MbSetForeground | MbTaskModal);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(nint hWnd, string lpText, string lpCaption, uint uType);

    private sealed record Prerequisite(
        string Id,
        string DisplayName,
        string DownloadUrl,
        string FileName,
        string InstallArguments);

    private sealed record InstallResult(bool Success, bool RestartRequired, string Message)
    {
        public static InstallResult Ok(bool restartRequired = false) => new(true, restartRequired, string.Empty);
        public static InstallResult Fail(string message) => new(false, false, message);
    }

    private static class Prerequisites
    {
        private static readonly Prerequisite VcRuntime = new(
            "vcredist",
            "Microsoft Visual C++ Runtime (x64)",
            VcRuntimeUrl,
            "AudioLimits-vc-redist-x64.exe",
            "/install /quiet /norestart");

        private static readonly Prerequisite DotNet = new(
            "dotnet",
            ".NET 8 Desktop Runtime (x64)",
            DotNetUrl,
            "AudioLimits-dotnet8-desktop-x64.exe",
            "/install /quiet /norestart");

        private static readonly Prerequisite WindowsAppRuntime = new(
            "wasdk",
            "Windows App Runtime 2.3.1 (x64)",
            WindowsAppRuntimeUrl,
            "AudioLimits-WindowsAppRuntime-2.3.1-x64.exe",
            "--quiet");

        public static IReadOnlyList<Prerequisite> GetMissing()
        {
            var missing = new List<Prerequisite>(3);

            if (!IsVcRuntimeInstalled())
                missing.Add(VcRuntime);

            if (!IsDotNetDesktop8Installed())
                missing.Add(DotNet);

            if (!IsWindowsAppRuntimeInstalled())
                missing.Add(WindowsAppRuntime);

            return missing;
        }

        public static async Task<InstallResult> InstallMissingAsync(IReadOnlyList<Prerequisite> missing)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "AudioLimits-bootstrap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var restartRequired = false;

            try
            {
                using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
                {
                    Timeout = TimeSpan.FromMinutes(10)
                };
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AudioLimits", Version));

                foreach (var prerequisite in missing)
                {
                    var targetPath = Path.Combine(tempRoot, prerequisite.FileName);
                    LauncherLog.Write($"Downloading {prerequisite.DisplayName} from {prerequisite.DownloadUrl}");

                    try
                    {
                        using var response = await client.GetAsync(prerequisite.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                        await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                        await input.CopyToAsync(output).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Write($"Download failed for {prerequisite.DisplayName}: {ex}");
                        return InstallResult.Fail(
                            $"Audio Limits couldn't download {prerequisite.DisplayName} from Microsoft.\n\nCheck your internet connection and try again.\n\n{ex.Message}");
                    }

                    if (!HasValidMicrosoftSignature(targetPath))
                    {
                        LauncherLog.Write($"Authenticode validation failed for {targetPath}");
                        return InstallResult.Fail(
                            $"The downloaded {prerequisite.DisplayName} installer did not pass Microsoft signature validation, so Audio Limits did not run it.\n\nDelete the temporary download and try again later.");
                    }

                    var install = RunInstallerElevated(targetPath, prerequisite.InstallArguments, prerequisite.DisplayName);
                    if (!install.Success)
                        return install;

                    restartRequired |= install.RestartRequired;
                }

                return InstallResult.Ok(restartRequired);
            }
            finally
            {
                try { Directory.Delete(tempRoot, recursive: true); }
                catch (Exception ex) { LauncherLog.Write("Could not remove bootstrap temp folder: " + ex.Message); }
            }
        }

        private static bool IsDotNetDesktop8Installed()
        {
            return HasDotNetDesktop8InRegistryView(RegistryView.Registry32) ||
                   HasDotNetDesktop8InRegistryView(RegistryView.Registry64);
        }

        private static bool HasDotNetDesktop8InRegistryView(RegistryView view)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(DotNetRegistryKey, writable: false);
                if (key is null)
                    return false;

                foreach (var valueName in key.GetValueNames())
                {
                    if (!valueName.StartsWith("8.", StringComparison.Ordinal))
                        continue;

                    if (key.GetValue(valueName) is int installed && installed == 1)
                    {
                        LauncherLog.Write($"Detected .NET 8 Desktop Runtime in {view}: {valueName}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Write($".NET detection failed in {view}: {ex.Message}");
            }

            return false;
        }

        private static bool IsVcRuntimeInstalled()
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(VcRuntimeRegistryKey, writable: false);
                if (key is null)
                    return false;

                if (key.GetValue("Installed") is not int installed || installed != 1)
                    return false;

                var versionText = key.GetValue("Version") as string;
                if (string.IsNullOrWhiteSpace(versionText))
                    return false;

                versionText = versionText.Trim().TrimStart('v', 'V');
                return System.Version.TryParse(versionText, out var version) && version >= MinimumVcRuntimeVersion;
            }
            catch (Exception ex)
            {
                LauncherLog.Write("VC++ runtime detection failed: " + ex.Message);
                return false;
            }
        }

        private static bool IsWindowsAppRuntimeInstalled()
        {
            const string script =
                "$p = @(Get-AppxPackage); $min = [version]'2.3.1.0'; " +
                "$fw64 = @($p | Where-Object { $_.Name -eq 'Microsoft.WindowsAppRuntime.2' -and $_.Architecture -eq 'X64' -and [version]$_.Version -ge $min }).Count -gt 0; " +
                "$fw86 = @($p | Where-Object { $_.Name -eq 'Microsoft.WindowsAppRuntime.2' -and $_.Architecture -eq 'X86' -and [version]$_.Version -ge $min }).Count -gt 0; " +
                "$main = @($p | Where-Object { $_.Name -eq 'MicrosoftCorporationII.WinAppRuntime.Main.2' -and $_.Architecture -eq 'X64' -and [version]$_.Version -ge $min }).Count -gt 0; " +
                "$singleton = @($p | Where-Object { $_.Name -eq 'MicrosoftCorporationII.WinAppRuntime.Singleton' -and $_.Architecture -eq 'X64' -and ([version]$_.Version).Major -ge 8000 }).Count -gt 0; " +
                "$ddlm64 = @($p | Where-Object { $_.Name -like 'Microsoft.WinAppRuntime.DDLM.2.3.*-x6' -and $_.Architecture -eq 'X64' -and [version]$_.Version -ge $min }).Count -gt 0; " +
                "$ddlm86 = @($p | Where-Object { $_.Name -like 'Microsoft.WinAppRuntime.DDLM.2.3.*-x8' -and $_.Architecture -eq 'X86' -and [version]$_.Version -ge $min }).Count -gt 0; " +
                "if ($fw64 -and $fw86 -and $main -and $singleton -and $ddlm64 -and $ddlm86) { exit 0 } else { exit 1 }";

            var exitCode = RunPowerShell(script);
            if (exitCode is null)
            {
                LauncherLog.Write("Windows App Runtime detection could not run; treating runtime as missing so the official installer can repair it.");
                return false;
            }

            return exitCode == 0;
        }

        private static bool HasValidMicrosoftSignature(string path)
        {
            var escaped = path.Replace("'", "''", StringComparison.Ordinal);
            var script =
                "$s = Get-AuthenticodeSignature -LiteralPath '" + escaped + "'; " +
                "if ($s.Status -eq 'Valid' -and $null -ne $s.SignerCertificate -and $s.SignerCertificate.Subject -match 'Microsoft Corporation') { exit 0 } else { exit 1 }";

            return RunPowerShell(script) == 0;
        }

        private static int? RunPowerShell(string script)
        {
            try
            {
                var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                if (!File.Exists(powershell))
                    powershell = "powershell.exe";

                var psi = new ProcessStartInfo
                {
                    FileName = powershell,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                psi.ArgumentList.Add("-NoLogo");
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-NonInteractive");
                psi.ArgumentList.Add("-ExecutionPolicy");
                psi.ArgumentList.Add("Bypass");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(script);

                using var process = Process.Start(psi);
                if (process is null)
                    return null;

                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                LauncherLog.Write("PowerShell helper failed: " + ex.Message);
                return null;
            }
        }

        private static InstallResult RunInstallerElevated(string path, string arguments, string displayName)
        {
            try
            {
                LauncherLog.Write($"Installing {displayName} with arguments: {arguments}");
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(psi);
                if (process is null)
                    return InstallResult.Fail($"Windows could not start the {displayName} installer.");

                process.WaitForExit();
                LauncherLog.Write($"{displayName} installer exited with code {process.ExitCode}.");

                if (process.ExitCode == 0)
                    return InstallResult.Ok();
                if (process.ExitCode is 3010 or 1641)
                    return InstallResult.Ok(restartRequired: true);

                return InstallResult.Fail($"{displayName} installation failed with exit code {process.ExitCode}.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                LauncherLog.Write($"Elevation was cancelled for {displayName}.");
                return InstallResult.Fail($"The {displayName} installation was cancelled. Audio Limits was not started.");
            }
            catch (Exception ex)
            {
                LauncherLog.Write($"Could not run {displayName} installer: {ex}");
                return InstallResult.Fail($"Audio Limits could not run the {displayName} installer.\n\n{ex.Message}");
            }
        }
    }

    private static class LauncherLog
    {
        private static string? _path;

        public static string PathForDisplay => _path ?? "(log unavailable)";

        public static void Initialize()
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Audio Limits");
                Directory.CreateDirectory(folder);
                _path = Path.Combine(folder, "bootstrap.log");
            }
            catch
            {
                _path = null;
            }
        }

        public static void Write(string message)
        {
            if (_path is null)
                return;

            try
            {
                File.AppendAllText(_path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Bootstrap logging must never prevent Audio Limits from starting.
            }
        }
    }
}
