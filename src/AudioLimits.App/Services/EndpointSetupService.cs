using System.ComponentModel;
using System.Diagnostics;
using AudioLimits.Core.Services;

namespace AudioLimits.App.Services;

/// <summary>
/// Launches the narrowly scoped endpoint-only helper after explicit user
/// approval. Windows UAC handles elevation; the WinUI process never holds
/// administrator privileges and never writes the audio driver registry.
/// </summary>
public sealed class EndpointSetupService
{
    private int _running;

    public bool IsBusy => Volatile.Read(ref _running) != 0;

    public bool HelperAvailable =>
        File.Exists(Path.Combine(AppContext.BaseDirectory,
            "AudioLimits.EndpointSetup.exe"));

    public Task<EndpointSetupOutcome> EnableAsync(string endpointGuid) =>
        RunAsync(endpointGuid, "--install");

    public Task<EndpointSetupOutcome> RestoreAsync(string endpointGuid) =>
        RunAsync(endpointGuid, "--restore");

    public Task<EndpointSetupOutcome> VerifyAsync(string endpointGuid) =>
        RunAsync(endpointGuid, "--verify");

    public Task<EndpointSetupOutcome> TryCompatibleModeAsync(string endpointGuid) =>
        RunAsync(endpointGuid, "--switch-mode");

    private async Task<EndpointSetupOutcome> RunAsync(string endpointGuid, string operation)
    {
        if (!Guid.TryParse(endpointGuid, out var parsed))
            return new(false, "The playback endpoint identifier is invalid.");

        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return new(false, "Audio device setup or recovery is already running.");

        try
        {
            var path = Path.Combine(
                AppContext.BaseDirectory, "AudioLimits.EndpointSetup.exe");
            if (!File.Exists(path))
                return new(false, "The audio setup helper is missing. Repair Audio Limits or use advanced setup.");

            var info = new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = AppContext.BaseDirectory,
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            info.ArgumentList.Add(operation);
            info.ArgumentList.Add(parsed.ToString("B").ToUpperInvariant());

            try
            {
                using var process = Process.Start(info)
                    ?? throw new InvalidOperationException(
                        "Windows did not start the audio setup helper.");
                await process.WaitForExitAsync();

                if (operation == "--switch-mode")
                {
                    return process.ExitCode switch
                    {
                        13 => new(true,
                            "A compatible Equalizer APO processing mode has been registered for this playback device. " +
                            "Restart Windows, then verify processing again before setting a limit. " +
                            "Windows volume and manufacturer audio effects were not changed."),
                        14 => new(false,
                            "The automatic compatibility change is unavailable because a driver effect, previous attempt, " +
                            "or saved registry value is different than expected. No new change was applied; use advanced setup."),
                        15 => new(false,
                            "The compatibility change failed, but the original audio effect values were restored. " +
                            "A volume limit remains inactive."),
                        16 => new(false,
                            "The compatibility change could not be completely rolled back. " +
                            "Don't rely on the volume limit. Use the recovery option before trying again."),
                        3 => new(false,
                            "Windows administrator permission is required for this device-only compatibility change."),
                        _ => new(false,
                            "The compatibility helper couldn't complete the change. No limit was applied.")
                    };
                }

                if (operation == "--verify")
                {
                    return process.ExitCode switch
                    {
                        7 => new(true,
                            "Equalizer APO reported that the selected audio processing stage initialized for this playback device during a silent shared-mode test. " +
                            "You can now set a limit; this initialization check cannot guarantee every audio path is attenuated."),
                        8 => new(false,
                            "The selected Equalizer APO processing stage did not initialize during the test. " +
                            "No limit was applied. Your audio driver may need a different processing mode in advanced setup."),
                        9 => new(false,
                            "Windows must be restarted before checking the newly registered audio processing stage."),
                        3 => new(false,
                            "Administrator permission is required for the temporary Equalizer APO diagnostic channel. No limit was applied."),
                        _ => new(false,
                            "Audio processing could not be verified. No limit was applied; consult the log or advanced audio setup.")
                    };
                }

                if (operation == "--restore")
                {
                    return process.ExitCode switch
                    {
                        6 => new(true,
                            "The previous audio effect registrations were restored. " +
                            "Restart Windows if the audio processing behavior has changed. " +
                            "No playback volume was increased."),
                        2 => new(false,
                            "The saved audio backup could not safely be restored because the endpoint or its registry entries no longer match the original setup attempt. " +
                            "Audio Limits did not overwrite anything; manual review is required."),
                        3 => new(false,
                            "Windows administrator permission is required to restore the original effects."),
                        _ => new(false,
                            "Automatic recovery could not finish. Review the backup and Audio Limits log before changing the device volume.")
                    };
                }

                return process.ExitCode switch
                {
                    0 => new(true,
                        "Equalizer APO was registered for this playback device. " +
                        "Restart Windows to load the new processing before setting a limit. " +
                        "Your existing volume has not been raised."),
                    1 => new(false,
                        "Equalizer APO is already registered. Reopen Audio Limits or restart Windows if audio processing is not yet effective."),
                    2 => new(false,
                        "This endpoint couldn't be installed safely using the automatic method. " +
                        "Your existing audio processing was left unchanged; advanced setup remains available."),
                    3 => new(false,
                        "Windows administrator privileges are required to register audio processing. No changes were made."),
                    4 => new(false,
                        "Automatic setup failed. Its registry changes were rolled back. " +
                        "Your previous audio processing was preserved."),
                    5 => new(false,
                        "Automatic setup could not fully restore the previous audio registry state. " +
                        "Do not change the device's volume until the log and backup are reviewed."),
                    _ => new(false,
                        "Automatic audio setup could not finish. Check the Audio Limits log for details.")
                };
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return new(false, "Setup was canceled at the Windows administrator prompt. No changes were made.");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Audio Limits endpoint helper launch failed", ex);
            return new(false, "Audio setup couldn't start: " + ex.Message);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}

public sealed record EndpointSetupOutcome(bool Succeeded, string Message);
