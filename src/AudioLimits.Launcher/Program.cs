using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace AudioLimits.Launcher;

internal static class Program
{
    private const string Version = "1.0.0";
    private const string AppSubdirectoryName = "app";
    private const string AppExecutableName = "AudioLimits.App.exe";
    private const string InstallEqualizerApoArgument = "--install-equalizer-apo";

    private const string DotNetRegistryKey = @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App";
    private const string VcRuntimeRegistryKey = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";

    private const string DotNetUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe";
    private const string VcRuntimeUrl = "https://aka.ms/vc14/vc_redist.x64.exe";
    private const string WindowsAppRuntimeUrl = "https://aka.ms/windowsappsdk/2.3/2.3.1/windowsappruntimeinstall-x64.exe";
    private const string EqualizerApoUrl = "https://sourceforge.net/projects/equalizerapo/files/1.4.2/EqualizerAPO-x64-1.4.2.exe/download";
    private const string EqualizerApoSha256 = "7403be7427bbe1936a40dded082829b6e217fc4f5990fee5cba501f0ae055afa";

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

        var equalizerOnly = args.Any(x =>
            string.Equals(x, InstallEqualizerApoArgument, StringComparison.OrdinalIgnoreCase));
        var appArgs = args
            .Where(x => !string.Equals(x, InstallEqualizerApoArgument, StringComparison.OrdinalIgnoreCase))
            .ToArray();

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

            var missing = Prerequisites.GetMissing(equalizerOnly);
            if (missing.Count > 0)
            {
                LauncherLog.Write("Missing prerequisites: " + string.Join(", ", missing.Select(x => x.DisplayName)));
                if (!AskToInstall(missing, equalizerOnly))
                {
                    LauncherLog.Write("User declined prerequisite installation.");
                    return 3;
                }

                using var progress = PreparationWindow.Show(
                    missing.Count == 1
                        ? $"Downloading {missing[0].UiDisplayName}…\n\nPreparing installation."
                        : "Downloading required components…\n\nPreparing installation.");

                var result = Prerequisites.InstallMissingAsync(missing, progress).GetAwaiter().GetResult();
                if (result.Canceled)
                {
                    LauncherLog.Write("Prerequisite setup canceled by user.");
                    return 3;
                }

                if (!result.Success)
                {
                    progress.Close();
                    ShowError("Audio Limits couldn't prepare this PC", result.Message);
                    return 4;
                }

                if (result.RestartRequired)
                {
                    progress.Close();
                    ShowInformation(
                        "Restart required",
                        "A required component was installed, but Windows needs to restart before Audio Limits can continue.\n\nRestart Windows, then open Audio Limits again.");
                    return 0;
                }

                progress.Update("Checking the installed components…");
                var remaining = Prerequisites.GetMissing(equalizerOnly, verifyOnly: true);
                if (remaining.Count > 0)
                {
                    progress.Close();
                    ShowError(
                        "Audio Limits still needs a component",
                        "Audio Limits installed the required components, but Windows still reports that one or more are unavailable:\n\n" +
                        string.Join("\n", remaining.Select(x => "• " + x.UiDisplayName)) +
                        "\n\nRestart Windows and try again. If the problem continues, run Audio Limits Setup to repair the installation.");
                    return 5;
                }

                if (equalizerOnly)
                {
                    progress.Update("Equalizer APO is ready.");
                    Thread.Sleep(250);
                    return 0;
                }

                progress.Update("Starting Audio Limits…");
                var launchResult = LaunchApplication(appPath, appDirectory, appArgs);
                Thread.Sleep(150);
                return launchResult;
            }

            if (equalizerOnly)
                return 0;

            return LaunchApplication(appPath, appDirectory, appArgs);
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

    private static bool AskToInstall(IReadOnlyList<Prerequisite> missing, bool equalizerOnly)
    {
        var items = string.Join("\n", missing.Select(x => "• " + x.UiDisplayName));
        var includesEqualizerApo = missing.Any(x => x.Id == "equalizerapo");
        var includesMicrosoft = missing.Any(x => x.Id != "equalizerapo");

        string sourceText;
        if (includesEqualizerApo && includesMicrosoft)
        {
            sourceText =
                "\n\nAudio Limits will download the required Microsoft components from Microsoft and Equalizer APO from its official SourceForge release. Each download is verified before installation.";
        }
        else if (includesEqualizerApo)
        {
            sourceText =
                "\n\nAudio Limits will download Equalizer APO from its official SourceForge release and verify the download before installation.";
        }
        else
        {
            sourceText =
                "\n\nAudio Limits will download the required components from Microsoft and verify each download before installation.";
        }

        var purpose = equalizerOnly
            ? " to control playback devices:"
            : " before it can run:";

        var text =
            "Audio Limits needs the following component" + (missing.Count == 1 ? "" : "s") + purpose + "\n\n" +
            items +
            sourceText +
            "\n\nWindows may ask for administrator permission during installation. Continue?";

        return MessageBoxW(
                   0,
                   text,
                   "Set up Audio Limits",
                   MbYesNo | MbIconInformation | MbSetForeground | MbTaskModal) == IdYes;
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
        string InstallArguments,
        string? ExpectedSha256 = null)
    {
        public string UiDisplayName => Id switch
        {
            "vcredist" => "Microsoft Visual C++ Runtime",
            "dotnet" => ".NET 8 Desktop Runtime",
            "wasdk" => "Windows App Runtime",
            "equalizerapo" => "Equalizer APO",
            _ => DisplayName
        };
    }

    private sealed record InstallResult(
        bool Success,
        bool RestartRequired,
        bool Canceled,
        string Message)
    {
        public static InstallResult Ok(bool restartRequired = false) =>
            new(true, restartRequired, false, string.Empty);

        public static InstallResult Fail(string message) =>
            new(false, false, false, message);

        public static InstallResult Cancel() =>
            new(false, false, true, string.Empty);
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

        private static readonly Prerequisite EqualizerApo = new(
            "equalizerapo",
            "Equalizer APO 1.4.2 (x64)",
            EqualizerApoUrl,
            "AudioLimits-EqualizerAPO-x64-1.4.2.exe",
            "/S",
            EqualizerApoSha256);

        public static IReadOnlyList<Prerequisite> GetMissing(
            bool equalizerOnly = false,
            bool verifyOnly = false)
        {
            if (equalizerOnly)
            {
                // Explicit in-app setup/repair intentionally reinstalls Equalizer APO.
                if (!verifyOnly || !IsEqualizerApoInstalled())
                    return new[] { EqualizerApo };
                return Array.Empty<Prerequisite>();
            }

            var missing = new List<Prerequisite>(4);

            if (!IsVcRuntimeInstalled())
                missing.Add(VcRuntime);

            if (!IsDotNetDesktop8Installed())
                missing.Add(DotNet);

            if (!IsWindowsAppRuntimeInstalled())
                missing.Add(WindowsAppRuntime);

            if (!IsEqualizerApoInstalled())
                missing.Add(EqualizerApo);

            return missing;
        }

        public static async Task<InstallResult> InstallMissingAsync(
            IReadOnlyList<Prerequisite> missing,
            PreparationWindow progress)
        {
            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                "AudioLimits-bootstrap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            var restartRequired = false;
            using var cancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    progress.CancellationToken);

            Task? producerTask = null;

            try
            {
                using var client = new HttpClient(
                    new HttpClientHandler { AllowAutoRedirect = true })
                {
                    Timeout = TimeSpan.FromMinutes(45)
                };
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("AudioLimits", Version));

                static IReadOnlyList<string> GetOfficialUrls(
                    Prerequisite prerequisite)
                {
                    var urls = new List<string>
                    {
                        prerequisite.DownloadUrl
                    };

                    if (prerequisite.Id is "wasdk" or "windowsappruntime")
                    {
                        urls.Add(
                            "https://download.microsoft.com/download/cbb0f858-7923-48f0-bc2e-ba8ae187b65c/WindowsAppRuntimeInstall-x64.exe");
                    }

                    return urls;
                }

                static string FormatSize(long? bytes)
                {
                    if (bytes is null)
                        return "size unknown";

                    var mib = bytes.Value / 1024d / 1024d;
                    return $"{mib:0.0} MiB";
                }

                async Task<(
                    Prerequisite Prerequisite,
                    int OriginalIndex,
                    long? Size,
                    List<string> DownloadUrls)> ProbeAsync(
                        Prerequisite prerequisite,
                        int originalIndex)
                {
                    var officialUrls =
                        GetOfficialUrls(prerequisite).ToList();

                    using var probeCancellation =
                        CancellationTokenSource.CreateLinkedTokenSource(
                            cancellation.Token);
                    probeCancellation.CancelAfter(
                        TimeSpan.FromSeconds(6));

                    foreach (var sourceUrl in officialUrls)
                    {
                        foreach (var method in new[]
                                 {
                                     HttpMethod.Head,
                                     HttpMethod.Get
                                 })
                        {
                            try
                            {
                                using var request =
                                    new HttpRequestMessage(
                                        method,
                                        sourceUrl);

                                using var response =
                                    await client.SendAsync(
                                            request,
                                            HttpCompletionOption.ResponseHeadersRead,
                                            probeCancellation.Token)
                                        .ConfigureAwait(false);

                                if (!response.IsSuccessStatusCode)
                                    continue;

                                var resolvedUrl =
                                    response.RequestMessage?
                                        .RequestUri?
                                        .AbsoluteUri;

                                var downloadUrls =
                                    new List<string> { sourceUrl };

                                if (!string.IsNullOrWhiteSpace(
                                        resolvedUrl) &&
                                    !downloadUrls.Contains(
                                        resolvedUrl,
                                        StringComparer.OrdinalIgnoreCase))
                                {
                                    downloadUrls.Add(resolvedUrl);
                                }

                                foreach (var officialUrl in officialUrls)
                                {
                                    if (!downloadUrls.Contains(
                                            officialUrl,
                                            StringComparer.OrdinalIgnoreCase))
                                    {
                                        downloadUrls.Add(officialUrl);
                                    }
                                }

                                var size =
                                    response.Content.Headers.ContentLength;

                                LauncherLog.Write(
                                    $"Probed {prerequisite.DisplayName}: {FormatSize(size)}" +
                                    (string.IsNullOrWhiteSpace(resolvedUrl)
                                        ? string.Empty
                                        : $"; resolved to {resolvedUrl}"));

                                return (
                                    prerequisite,
                                    originalIndex,
                                    size,
                                    downloadUrls);
                            }
                            catch (OperationCanceledException)
                                when (probeCancellation.IsCancellationRequested)
                            {
                                if (cancellation.IsCancellationRequested)
                                    throw;

                                break;
                            }
                            catch (Exception ex)
                            {
                                LauncherLog.Write(
                                    $"Probe failed for {prerequisite.DisplayName} at {sourceUrl}: {ex.Message}");
                            }
                        }

                        if (probeCancellation.IsCancellationRequested)
                            break;
                    }

                    LauncherLog.Write(
                        $"Could not determine download size for {prerequisite.DisplayName}; using stable fallback order.");

                    return (
                        prerequisite,
                        originalIndex,
                        null,
                        officialUrls);
                }

                var probeTasks = missing
                    .Select((prerequisite, index) =>
                        ProbeAsync(prerequisite, index))
                    .ToArray();

                var plan =
                    (await Task.WhenAll(probeTasks)
                        .ConfigureAwait(false))
                    .OrderBy(x => x.Size.HasValue ? 0 : 1)
                    .ThenBy(x => x.Size ?? long.MaxValue)
                    .ThenBy(x => x.OriginalIndex)
                    .ToList();

                LauncherLog.Write(
                    "Download plan (smallest known first): " +
                    string.Join(
                        " -> ",
                        plan.Select(x =>
                            $"{x.Prerequisite.DisplayName} ({FormatSize(x.Size)})")));

                async Task<(
                    Prerequisite Prerequisite,
                    string TargetPath,
                    bool Success,
                    string ErrorMessage)> DownloadAndVerifyAsync(
                        Prerequisite prerequisite,
                        IReadOnlyList<string> downloadUrls)
                {
                    var targetPath =
                        Path.Combine(
                            tempRoot,
                            prerequisite.FileName);

                    Exception? lastException = null;
                    var verificationFailed = false;

                    foreach (var sourceUrl in
                             downloadUrls.Distinct(
                                 StringComparer.OrdinalIgnoreCase))
                    {
                        for (var attempt = 1;
                             attempt <= 2;
                             attempt++)
                        {
                            try
                            {
                                LauncherLog.Write(
                                    $"Downloading {prerequisite.DisplayName} from {sourceUrl}" +
                                    (attempt == 1
                                        ? string.Empty
                                        : " (retry)"));

                                using var response =
                                    await client.GetAsync(
                                            sourceUrl,
                                            HttpCompletionOption.ResponseHeadersRead,
                                            cancellation.Token)
                                        .ConfigureAwait(false);

                                response.EnsureSuccessStatusCode();

                                await using var input =
                                    await response.Content
                                        .ReadAsStreamAsync(
                                            cancellation.Token)
                                        .ConfigureAwait(false);

                                await using (var output =
                                    new FileStream(
                                        targetPath,
                                        FileMode.Create,
                                        FileAccess.Write,
                                        FileShare.None,
                                        81920,
                                        useAsync: true))
                                {
                                    await input.CopyToAsync(
                                            output,
                                            cancellation.Token)
                                        .ConfigureAwait(false);

                                    await output.FlushAsync(
                                            cancellation.Token)
                                        .ConfigureAwait(false);
                                }

                                LauncherLog.Write(
                                    $"Verifying {prerequisite.DisplayName}.");

                                if (VerifyDownload(
                                        prerequisite,
                                        targetPath))
                                {
                                    LauncherLog.Write(
                                        $"{prerequisite.DisplayName} download is ready to install.");

                                    return (
                                        prerequisite,
                                        targetPath,
                                        true,
                                        string.Empty);
                                }

                                verificationFailed = true;
                                LauncherLog.Write(
                                    $"Verification failed for {prerequisite.DisplayName} from {sourceUrl}.");
                            }
                            catch (OperationCanceledException)
                                when (cancellation.IsCancellationRequested)
                            {
                                return (
                                    prerequisite,
                                    targetPath,
                                    false,
                                    string.Empty);
                            }
                            catch (Exception ex)
                            {
                                lastException = ex;
                                LauncherLog.Write(
                                    $"Download attempt failed for {prerequisite.DisplayName} from {sourceUrl}: {ex.Message}");
                            }
                        }
                    }

                    if (verificationFailed)
                    {
                        return (
                            prerequisite,
                            targetPath,
                            false,
                            $"The downloaded {prerequisite.UiDisplayName} installer did not pass verification, so Audio Limits did not run it.\n\nTry again later.");
                    }

                    return (
                        prerequisite,
                        targetPath,
                        false,
                        $"Audio Limits couldn't download {prerequisite.UiDisplayName}.\n\nCheck your internet connection and try again." +
                        (lastException is null
                            ? string.Empty
                            : $"\n\n{lastException.Message}"));
                }

                var ready =
                    System.Threading.Channels.Channel.CreateUnbounded<(
                        Prerequisite Prerequisite,
                        string TargetPath,
                        bool Success,
                        string ErrorMessage)>(
                        new System.Threading.Channels.UnboundedChannelOptions
                        {
                            SingleReader = true,
                            SingleWriter = true
                        });

                async Task ProduceDownloadsAsync()
                {
                    try
                    {
                        // One active payload download at a time. The producer
                        // continues while the consumer is waiting for an installer,
                        // so downloading/verifying overlaps with installation.
                        foreach (var item in plan)
                        {
                            cancellation.Token
                                .ThrowIfCancellationRequested();

                            var result =
                                await DownloadAndVerifyAsync(
                                        item.Prerequisite,
                                        item.DownloadUrls)
                                    .ConfigureAwait(false);

                            if (cancellation.IsCancellationRequested)
                                return;

                            await ready.Writer.WriteAsync(
                                    result,
                                    cancellation.Token)
                                .ConfigureAwait(false);

                            if (!result.Success)
                                return;
                        }
                    }
                    catch (OperationCanceledException)
                        when (cancellation.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Write(
                            "Download pipeline failed: " + ex);
                        ready.Writer.TryComplete(ex);
                        return;
                    }
                    finally
                    {
                        ready.Writer.TryComplete();
                    }
                }

                producerTask = ProduceDownloadsAsync();
                var installedCount = 0;

                while (installedCount < missing.Count)
                {
                    if (progress.CancellationToken
                        .IsCancellationRequested)
                    {
                        return InstallResult.Cancel();
                    }

                    (
                        Prerequisite Prerequisite,
                        string TargetPath,
                        bool Success,
                        string ErrorMessage) downloaded;

                    if (!ready.Reader.TryRead(out downloaded))
                    {
                        progress.Update(
                            missing.Count == 1
                                ? $"Downloading {missing[0].UiDisplayName}…\n\nPreparing installation."
                                : "Downloading required components…\n\nPreparing installation.");

                        try
                        {
                            downloaded =
                                await ready.Reader.ReadAsync(
                                        cancellation.Token)
                                    .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                            when (progress.CancellationToken
                                .IsCancellationRequested)
                        {
                            return InstallResult.Cancel();
                        }
                        catch (System.Threading.Channels
                            .ChannelClosedException)
                        {
                            if (progress.CancellationToken
                                .IsCancellationRequested)
                            {
                                return InstallResult.Cancel();
                            }

                            return InstallResult.Fail(
                                "Audio Limits couldn't finish downloading the required components. Check the bootstrap log for details, then try again.");
                        }
                    }

                    if (!downloaded.Success)
                    {
                        return progress.CancellationToken
                            .IsCancellationRequested
                            ? InstallResult.Cancel()
                            : InstallResult.Fail(
                                downloaded.ErrorMessage);
                    }

                    if (progress.CancellationToken
                        .IsCancellationRequested)
                    {
                        return InstallResult.Cancel();
                    }

                    progress.Update(
                        $"Installing {downloaded.Prerequisite.UiDisplayName}…\n\nComponent {installedCount + 1} of {missing.Count}");

                    progress.SetInstallingComponent(
                        downloaded.Prerequisite.UiDisplayName);

                    InstallResult install;
                    try
                    {
                        install =
                            downloaded.Prerequisite.Id ==
                            "equalizerapo"
                                ? await RunEqualizerApoInstallerElevatedAsync(
                                        downloaded.TargetPath,
                                        downloaded.Prerequisite.DisplayName,
                                        tempRoot)
                                    .ConfigureAwait(false)
                                : RunInstallerElevated(
                                    downloaded.TargetPath,
                                    downloaded.Prerequisite.InstallArguments,
                                    downloaded.Prerequisite.DisplayName);
                    }
                    finally
                    {
                        progress.SetInstallingComponent(null);
                    }

                    if (!install.Success)
                        return install;

                    restartRequired |=
                        install.RestartRequired;
                    installedCount++;

                    // A running installer is never terminated. If the user
                    // confirmed cancellation while it was running, stop now
                    // before starting another prerequisite.
                    if (progress.CancellationToken
                        .IsCancellationRequested)
                    {
                        return InstallResult.Cancel();
                    }
                }

                if (producerTask is not null)
                    await producerTask.ConfigureAwait(false);

                return InstallResult.Ok(
                    restartRequired);
            }
            catch (OperationCanceledException)
                when (progress.CancellationToken
                    .IsCancellationRequested)
            {
                return InstallResult.Cancel();
            }
            finally
            {
                cancellation.Cancel();

                if (producerTask is not null)
                {
                    try
                    {
                        await producerTask.ConfigureAwait(false);
                    }
                    catch
                    {
                        // Cancellation/failure has already been handled above.
                    }
                }

                try
                {
                    Directory.Delete(
                        tempRoot,
                        recursive: true);
                }
                catch (Exception ex)
                {
                    LauncherLog.Write(
                        "Could not remove bootstrap temp folder: " +
                        ex.Message);
                }
            }
        }
        private static bool VerifyDownload(Prerequisite prerequisite, string path)
        {
            if (!string.IsNullOrWhiteSpace(prerequisite.ExpectedSha256))
                return HasExpectedSha256(path, prerequisite.ExpectedSha256);

            return HasValidMicrosoftSignature(path);
        }

        private static bool HasExpectedSha256(string path, string expected)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var actual = Convert.ToHexString(SHA256.HashData(stream));
                var valid = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
                LauncherLog.Write(
                    valid
                        ? $"SHA-256 verified for {Path.GetFileName(path)}."
                        : $"SHA-256 mismatch for {Path.GetFileName(path)}: {actual}");
                return valid;
            }
            catch (Exception ex)
            {
                LauncherLog.Write("SHA-256 verification failed: " + ex);
                return false;
            }
        }

        private static bool IsEqualizerApoInstalled()
        {
            var configDir = FindEqualizerApoConfigDirectory();
            return configDir is not null &&
                   File.Exists(Path.Combine(configDir, "config.txt"));
        }

        private static string? FindEqualizerApoConfigDirectory()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = hklm.OpenSubKey(@"SOFTWARE\EqualizerAPO");
                    if (key?.GetValue("ConfigPath") is string configPath &&
                        Directory.Exists(configPath))
                        return configPath;
                }
                catch
                {
                    // Try the other registry view and then the conventional path.
                }
            }

            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "EqualizerAPO",
                "config");

            return Directory.Exists(fallback) ? fallback : null;
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
                return System.Version.TryParse(versionText, out var version) &&
                       version >= MinimumVcRuntimeVersion;
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
                LauncherLog.Write(
                    "Windows App Runtime detection could not run; treating runtime as missing so the official installer can repair it.");
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
                var powershell = GetPowerShellPath();
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

        private static async Task<InstallResult> RunEqualizerApoInstallerElevatedAsync(
            string path,
            string displayName,
            string tempRoot)
        {
            try
            {
                LauncherLog.Write(
                    $"Installing {displayName} silently and suppressing its first-run device selector.");

                var helperPath = Path.Combine(tempRoot, "Install-EqualizerApo.ps1");
                File.WriteAllText(helperPath, EqualizerApoInstallHelperScript);

                var psi = new ProcessStartInfo
                {
                    FileName = GetPowerShellPath(),
                    Arguments =
                        $"-NoLogo -NoProfile -ExecutionPolicy Bypass -File {QuoteArgument(helperPath)} -InstallerPath {QuoteArgument(path)}",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(psi);
                if (process is null)
                    return InstallResult.Fail($"Windows could not start the {displayName} installer.");

                await process.WaitForExitAsync().ConfigureAwait(false);
                LauncherLog.Write($"{displayName} installer helper exited with code {process.ExitCode}.");

                if (process.ExitCode == 0)
                    return InstallResult.Ok();
                if (process.ExitCode is 3010 or 1641)
                    return InstallResult.Ok(restartRequired: true);

                return InstallResult.Fail(
                    $"{displayName} installation failed with exit code {process.ExitCode}.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                LauncherLog.Write($"Elevation was cancelled for {displayName}.");
                return InstallResult.Fail(
                    $"The {displayName} installation was cancelled. Audio Limits was not changed.");
            }
            catch (Exception ex)
            {
                LauncherLog.Write($"Could not run {displayName} installer: {ex}");
                return InstallResult.Fail(
                    $"Audio Limits could not run the {displayName} installer.\n\n{ex.Message}");
            }
        }

        private static InstallResult RunInstallerElevated(
            string path,
            string arguments,
            string displayName)
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

                return InstallResult.Fail(
                    $"{displayName} installation failed with exit code {process.ExitCode}.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                LauncherLog.Write($"Elevation was cancelled for {displayName}.");
                return InstallResult.Fail(
                    $"The {displayName} installation was cancelled. Audio Limits was not started.");
            }
            catch (Exception ex)
            {
                LauncherLog.Write($"Could not run {displayName} installer: {ex}");
                return InstallResult.Fail(
                    $"Audio Limits could not run the {displayName} installer.\n\n{ex.Message}");
            }
        }

        private static string GetPowerShellPath()
        {
            var powershell = Path.Combine(
                Environment.SystemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            return File.Exists(powershell) ? powershell : "powershell.exe";
        }

        private static string QuoteArgument(string value) =>
            "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

        private const string EqualizerApoInstallHelperScript = """
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath
)

$ErrorActionPreference = 'Stop'
$names = @('DeviceSelector', 'Configurator')
$existing = @{}

foreach ($name in $names) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
        $existing[$_.Id] = $true
    }
}

$installer = Start-Process -FilePath $InstallerPath -ArgumentList '/S' -PassThru

while (-not $installer.HasExited) {
    foreach ($name in $names) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            if ($existing.ContainsKey($_.Id)) {
                return
            }

            try {
                if ($_.MainWindowHandle -ne 0) {
                    $null = $_.CloseMainWindow()
                    if (-not $_.WaitForExit(1500)) {
                        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
                    }
                }
                else {
                    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
                }
            }
            catch {
                Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Start-Sleep -Milliseconds 250
}

exit $installer.ExitCode
""";
    }

    private sealed class PreparationWindow : IDisposable
    {
        private const uint TdfAllowDialogCancellation = 0x00000008;
        private const uint TdfShowProgressBar = 0x00000200;
        private const uint TdfShowMarqueeProgressBar = 0x00000400;
        private const uint TdfCanBeMinimized = 0x00008000;

        private const uint WmUser = 0x0400;
        private const uint TdmClickButton = WmUser + 102;
        private const uint TdmSetProgressBarMarquee = WmUser + 107;
        private const uint TdmSetElementText = WmUser + 108;
        private const uint TdmEnableButton = WmUser + 111;

        private const uint TdnCreated = 0;
        private const uint TdnButtonClicked = 2;

        private const int TdeContent = 0;
        private const uint TdcbfCancelButton = 0x00000008;
        private const int IdCancel = 2;

        private readonly ManualResetEventSlim _created =
            new(false);
        private readonly CancellationTokenSource _cancel =
            new();
        private readonly Thread _thread;
        private readonly TaskDialogCallback _callback;
        private readonly string _initialText;

        private nint _hwnd;
        private string? _installingComponent;
        private int _closing;
        private int _closed;

        private PreparationWindow(string initialText)
        {
            _initialText = initialText;
            _callback = OnTaskDialogCallback;

            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "Audio Limits preparation UI"
            };

            _thread.SetApartmentState(
                ApartmentState.STA);
            _thread.Start();
            _created.Wait(
                TimeSpan.FromSeconds(5));
        }

        public static PreparationWindow Show(
            string initialText) =>
            new(initialText);

        public CancellationToken CancellationToken =>
            _cancel.Token;

        public void SetInstallingComponent(
            string? displayName) =>
            Volatile.Write(
                ref _installingComponent,
                displayName);

        public void Update(string text)
        {
            if (Volatile.Read(ref _closed) != 0)
                return;

            _created.Wait(
                TimeSpan.FromSeconds(2));

            var hwnd = _hwnd;
            if (hwnd == 0)
                return;

            SendMessageW(
                hwnd,
                TdmSetElementText,
                new nint(TdeContent),
                text);
        }

        public void Close()
        {
            if (Interlocked.Exchange(
                    ref _closed,
                    1) != 0)
            {
                return;
            }

            _created.Wait(
                TimeSpan.FromSeconds(2));

            var hwnd = _hwnd;
            if (hwnd != 0)
            {
                Interlocked.Exchange(
                    ref _closing,
                    1);

                SendMessage(
                    hwnd,
                    TdmClickButton,
                    new nint(IdCancel),
                    0);
            }

            if (_thread.IsAlive)
            {
                _thread.Join(
                    TimeSpan.FromSeconds(2));
            }
        }

        public void Dispose() =>
            Close();

        private void Run()
        {
            try
            {
                var config =
                    new TaskDialogConfig
                    {
                        cbSize =
                            (uint)Marshal.SizeOf<
                                TaskDialogConfig>(),

                        dwFlags =
                            TdfAllowDialogCancellation |
                            TdfShowProgressBar |
                            TdfShowMarqueeProgressBar |
                            TdfCanBeMinimized,

                        dwCommonButtons =
                            TdcbfCancelButton,

                        nDefaultButton =
                            IdCancel,

                        pszWindowTitle =
                            "Audio Limits",

                        pszMainInstruction =
                            "Preparing Audio Limits",

                        pszContent =
                            _initialText,

                        pfCallback =
                            _callback,

                        cxWidth =
                            360
                    };

                LauncherLog.Write(
                    $"TASKDIALOGCONFIG size: {Marshal.SizeOf<TaskDialogConfig>()} bytes; pointer size: {IntPtr.Size} bytes.");

                var hr =
                    TaskDialogIndirect(
                        ref config,
                        out _,
                        out _,
                        out _);

                if (hr < 0)
                {
                    LauncherLog.Write(
                        $"Preparation TaskDialog failed with HRESULT 0x{hr:X8}.");
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Write(
                    "Preparation UI failed: " +
                    ex);
            }
            finally
            {
                _created.Set();
            }
        }

        private int OnTaskDialogCallback(
            nint hwnd,
            uint notification,
            nint wParam,
            nint lParam,
            nint callbackData)
        {
            if (notification == TdnCreated)
            {
                _hwnd = hwnd;
                _created.Set();

                SendMessage(
                    hwnd,
                    TdmSetProgressBarMarquee,
                    new nint(1),
                    new nint(30));

                return 0;
            }

            if (notification != TdnButtonClicked ||
                wParam.ToInt32() != IdCancel)
            {
                return 0;
            }

            // Programmatic close after normal completion or after setup
            // has reached the safe cancellation point.
            if (Volatile.Read(ref _closing) != 0)
                return 0;

            // The user already confirmed cancellation. Keep the window
            // open while the bootstrapper finishes the current installer,
            // if any, and reaches a safe stopping point.
            if (_cancel.IsCancellationRequested)
                return 1;

            var installingComponent =
                Volatile.Read(
                    ref _installingComponent);

            string message;
            string progressText;

            if (string.IsNullOrWhiteSpace(
                    installingComponent))
            {
                message =
                    "Audio Limits is still preparing this PC.\n\n" +
                    "If you cancel now, any downloads in progress will stop and no additional components will be installed. " +
                    "Components that have already been installed will remain installed.\n\n" +
                    "Cancel setup?";

                progressText =
                    "Cancelling setup…\n\n" +
                    "Stopping downloads.";
            }
            else
            {
                message =
                    $"Audio Limits is currently installing {installingComponent}.\n\n" +
                    "To avoid interrupting the installer, Audio Limits will let this component finish, then stop. " +
                    "No additional components will be installed. Components that have already been installed will remain installed.\n\n" +
                    "Cancel after this installation finishes?";

                progressText =
                    $"Finishing {installingComponent}…\n\n" +
                    "Setup will stop when this installation finishes.";
            }

            var answer =
                MessageBoxW(
                    hwnd,
                    message,
                    "Cancel Audio Limits setup?",
                    MbYesNo |
                    MbIconInformation |
                    MbSetForeground |
                    MbTaskModal);

            if (answer != IdYes)
                return 1;

            LauncherLog.Write(
                string.IsNullOrWhiteSpace(
                    installingComponent)
                    ? "User requested setup cancellation."
                    : $"User requested setup cancellation while installing {installingComponent}; waiting for the current installer to finish.");

            _cancel.Cancel();

            // Prevent repeated clicks while cancellation is pending.
            SendMessage(
                hwnd,
                TdmEnableButton,
                new nint(IdCancel),
                0);

            SendMessageW(
                hwnd,
                TdmSetElementText,
                new nint(TdeContent),
                progressText);

            // Keep the dialog open. The bootstrapper closes it after it
            // reaches a safe stopping point.
            return 1;
        }

        [UnmanagedFunctionPointer(
            CallingConvention.Winapi)]
        private delegate int TaskDialogCallback(
            nint hwnd,
            uint notification,
            nint wParam,
            nint lParam,
            nint callbackData);

        [StructLayout(
            LayoutKind.Sequential,
            CharSet = CharSet.Unicode,
            Pack = 1)]
        private struct TaskDialogConfig
        {
            public uint cbSize;
            public nint hwndParent;
            public nint hInstance;
            public uint dwFlags;
            public uint dwCommonButtons;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pszWindowTitle;

            public nint hMainIcon;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pszMainInstruction;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pszContent;

            public uint cButtons;
            public nint pButtons;
            public int nDefaultButton;
            public uint cRadioButtons;
            public nint pRadioButtons;
            public int nDefaultRadioButton;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pszVerificationText;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pszExpandedInformation;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pszExpandedControlText;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pszCollapsedControlText;

            public nint hFooterIcon;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? pszFooter;

            public TaskDialogCallback? pfCallback;
            public nint lpCallbackData;
            public uint cxWidth;
        }

        [DllImport(
            "comctl32.dll",
            CharSet = CharSet.Unicode,
            PreserveSig = true)]
        private static extern int TaskDialogIndirect(
            ref TaskDialogConfig pTaskConfig,
            out int pnButton,
            out int pnRadioButton,
            [MarshalAs(UnmanagedType.Bool)]
            out bool pfVerificationFlagChecked);

        [DllImport("user32.dll")]
        private static extern nint SendMessage(
            nint hWnd,
            uint msg,
            nint wParam,
            nint lParam);

        [DllImport(
            "user32.dll",
            CharSet = CharSet.Unicode)]
        private static extern nint SendMessageW(
            nint hWnd,
            uint msg,
            nint wParam,
            string lParam);
    }
    private static class LauncherLog
    {
        private static string? _path;

        public static string PathForDisplay => _path ?? "(log unavailable)";

        public static void Initialize()
        {
            try
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Audio Limits");
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
                File.AppendAllText(
                    _path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Bootstrap logging must never prevent Audio Limits from starting.
            }
        }
    }
}
