using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioLimits.Core.Services;

/// <summary>
/// Verifies that the official Equalizer APO 1.4.2 reports Initialize for the
/// exact endpoint and requested processing stage while a silent shared-mode
/// render stream is created. Does not modify endpoint volume or restart
/// Windows Audio. Stage initialization is evidence, NOT an acoustic test.
/// </summary>
internal static class ApoRuntimeStageProbe
{
    private const string RegistryPath = @"SOFTWARE\EqualizerAPO";
    private const string TestValueName = "DeviceTestPipeName";

    internal static bool IsExpectedInitializationEvent(
        string json, string endpointGuid, string requiredStage)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("deviceGuid", out var endpoint) ||
                !root.TryGetProperty("stage", out var stage) ||
                !root.TryGetProperty("phase", out var phase))
                return false;

            return Guid.TryParse(endpoint.GetString(), out var actual) &&
                Guid.TryParse(endpointGuid, out var expected) &&
                actual == expected &&
                string.Equals(stage.GetString(), requiredStage,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(phase.GetString(), "Initialize",
                    StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static async Task<bool> TryVerifyAsync(
        string endpointGuid, string requiredStage)
    {
        if (requiredStage is not ("PreMix" or "PostMix") ||
            !Guid.TryParse(endpointGuid, out var endpoint))
            return false;

        // The actual runtime events come from audiodg running as LocalService.
        // A random pipe name and a narrow ACL prevent ordinary interactive
        // processes from posing as the audio engine.
        var pipeName = "AudioLimitsAPO-" + Guid.NewGuid().ToString("N");
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));

        using var hklm = RegistryKey.OpenBaseKey(
            RegistryHive.LocalMachine, RegistryView.Registry64);
        using var registry = hklm.OpenSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException(
                "Equalizer APO's global registry configuration is unavailable.");

        // Never steal Device Selector's diagnostic channel. We install our
        // temporary value only when it is initially absent.
        if (registry.GetValueNames().Contains(TestValueName,
                StringComparer.OrdinalIgnoreCase))
            return false;

        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(8));
        var ready = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var initialized = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new ConcurrentQueue<string>();

        var listener = Task.Run(async () =>
        {
            try
            {
                while (!timeout.IsCancellationRequested &&
                       !initialized.Task.IsCompleted)
                {
                    using var pipe = NamedPipeServerStreamAcl.Create(
                        pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
                        2048, 2048, security);
                    ready.TrySetResult();
                    await pipe.WaitForConnectionAsync(timeout.Token);

                    var buffer = new byte[2048];
                    var value = new StringBuilder();
                    int received;
                    while ((received = await pipe.ReadAsync(
                        buffer, timeout.Token)) > 0 && value.Length < 4096)
                        value.Append(Encoding.UTF8.GetString(
                            buffer, 0, received));

                    var message = value.ToString();
                    if (IsExpectedInitializationEvent(message,
                            endpoint.ToString("B"), requiredStage))
                        initialized.TrySetResult();
                    else if (messages.Count < 12 && message.Length > 0)
                        messages.Enqueue(message);
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                ready.TrySetResult();
            }
            catch (Exception ex)
            {
                AppLog.Error("Audio processing initialization listener failed", ex);
                ready.TrySetException(ex);
            }
        });

        var registryWritten = false;
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (timeout.IsCancellationRequested)
                return false;

            // The helper performs no driver-effect edits in this phase.
            registry.SetValue(TestValueName, pipeName,
                RegistryValueKind.String);
            registryWritten = true;

            using var enumerator = new MMDeviceEnumerator();
            var candidates = enumerator.EnumerateAudioEndPoints(
                DataFlow.Render, DeviceState.Active);
            using var device = candidates.FirstOrDefault(x =>
                x.ID.Contains(endpoint.ToString("B"),
                    StringComparison.OrdinalIgnoreCase));
            if (device is null)
                return false;

            // No audible signal and no changes to Windows endpoint volume.
            using var output = new WasapiOut(
                device, AudioClientShareMode.Shared, false, 100);
            output.Init(new SilenceProvider(device.AudioClient.MixFormat));
            output.Play();

            await Task.WhenAny(
                initialized.Task,
                Task.Delay(TimeSpan.FromMilliseconds(3800), timeout.Token));
            output.Stop();

            var verified = initialized.Task.IsCompletedSuccessfully;
            AppLog.Info(
                $"APO_STAGE_INIT endpoint={endpoint:B}; stage={requiredStage}; " +
                $"received={verified}; ignoredMessages={messages.Count}");
            return verified;
        }
        finally
        {
            // Only remove our own diagnostic value. Never delete a changed
            // value belonging to the upstream selector or another process.
            if (registryWritten)
            {
                if (string.Equals(registry.GetValue(TestValueName) as string,
                        pipeName, StringComparison.Ordinal))
                    registry.DeleteValue(TestValueName, false);
                else
                    AppLog.Warn(
                        "Another process changed the APO test pipe; leaving its value intact.");
            }

            timeout.Cancel();
            try { await listener.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
        }
    }
}
