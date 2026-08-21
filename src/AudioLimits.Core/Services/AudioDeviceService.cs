using AudioLimits.Core.Models;
using NAudio.CoreAudioApi;

namespace AudioLimits.Core.Services;

public sealed class AudioDeviceService
{
    public static readonly Guid InternalChangeContext = new("B3DCD107-0D7B-4209-A0F8-B7BCF68284E9");

    public IReadOnlyList<AudioDeviceInfo> GetActiveRenderDevices()
    {
        var result = new List<AudioDeviceInfo>();

        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        foreach (var device in devices)
        {
            using (device)
            {
                try
                {
                    result.Add(ReadInfo(device));
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Could not read playback endpoint '{SafeName(device)}'", ex);
                }
            }
        }

        return result
            .OrderBy(x => x.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public AudioDeviceSnapshot GetSnapshot(string deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        var info = ReadInfo(device);
        return new AudioDeviceSnapshot(
            info.Id,
            info.EndpointGuid,
            info.FriendlyName,
            info.CurrentScalar,
            info.CurrentDb,
            info.MinDb,
            info.MaxDb,
            info.Muted,
            info.SupportsHardwareVolume);
    }

    public AudioEndpointDiagnostics GetDiagnostics(string deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        var volume = device.AudioEndpointVolume;
        var endpointGuid = ExtractEndpointGuid(device.ID)
            ?? throw new InvalidOperationException(
                $"Could not determine the endpoint GUID for '{device.FriendlyName}'.");
        var range = volume.VolumeRange;
        var step = volume.StepInformation;

        return new AudioEndpointDiagnostics(
            device.ID,
            endpointGuid,
            device.FriendlyName,
            volume.MasterVolumeLevelScalar,
            volume.MasterVolumeLevel,
            range.MinDecibels,
            range.MaxDecibels,
            range.IncrementDecibels,
            volume.Mute,
            volume.HardwareSupport.ToString(),
            step.Step,
            step.StepCount);
    }

    public IDisposable SubscribeToVolume(string deviceId, Action<AudioVolumeState> callback) =>
        new VolumeSubscription(deviceId, callback);

    public void SetMasterDb(string deviceId, double db)
    {
        WithVolume(deviceId, volume =>
        {
            var range = volume.VolumeRange;
            volume.NotificationGuid = InternalChangeContext;
            volume.MasterVolumeLevel = (float)Math.Clamp(db, range.MinDecibels, range.MaxDecibels);
        });
    }

    public void SetMute(string deviceId, bool muted)
    {
        WithVolume(deviceId, volume =>
        {
            volume.NotificationGuid = InternalChangeContext;
            volume.Mute = muted;
        });
    }

    public IReadOnlyList<double> BuildVolumeCurveSafely(string deviceId)
    {
        return WithVolume(deviceId, volume =>
        {
            var originalScalar = volume.MasterVolumeLevelScalar;
            var originalMute = volume.Mute;
            var points = new double[VolumeCurve.ExpectedPointCount];
            volume.NotificationGuid = InternalChangeContext;

            try
            {
                volume.Mute = true;
                for (var percent = 0; percent <= 100; percent++)
                {
                    volume.MasterVolumeLevelScalar = percent / 100f;
                    points[percent] = volume.MasterVolumeLevel;
                }
            }
            finally
            {
                // Restore volume BEFORE restoring mute. If restoring the endpoint
                // position fails, leave it muted rather than risk exposing a changed
                // calibration position to the user.
                try
                {
                    volume.MasterVolumeLevelScalar = originalScalar;
                }
                catch (Exception ex)
                {
                    AppLog.Error("Volume-curve calibration could not restore the endpoint volume; leaving the device muted", ex);
                    throw new InvalidOperationException(
                        "Audio Limits could not safely restore the playback device after calibration. The device was left muted. " +
                        "Set its Windows volume manually before continuing.",
                        ex);
                }

                try
                {
                    volume.Mute = originalMute;
                }
                catch (Exception ex)
                {
                    AppLog.Error("Volume-curve calibration could not restore the endpoint mute state", ex);
                    throw new InvalidOperationException(
                        "Audio Limits restored the volume position but could not restore the device's mute state. Check the Windows volume control before continuing.",
                        ex);
                }
            }

            return (IReadOnlyList<double>)points;
        });
    }

    private static AudioDeviceInfo ReadInfo(MMDevice device)
    {
        var id = device.ID;
        var endpointGuid = ExtractEndpointGuid(id)
            ?? throw new InvalidOperationException(
                $"Could not determine the endpoint GUID for '{device.FriendlyName}'.");

        var volume = device.AudioEndpointVolume;
        var range = volume.VolumeRange;

        return new AudioDeviceInfo(
            id,
            endpointGuid,
            device.FriendlyName,
            volume.MasterVolumeLevelScalar,
            volume.MasterVolumeLevel,
            range.MinDecibels,
            range.MaxDecibels,
            volume.Mute,
            (volume.HardwareSupport & EEndpointHardwareSupport.Volume) != 0);
    }

    private static string SafeName(MMDevice device)
    {
        try { return device.FriendlyName; }
        catch { return "unknown endpoint"; }
    }

    private static string? ExtractEndpointGuid(string id)
    {
        var close = id.LastIndexOf('}');
        if (close < 0)
            return null;

        var open = id.LastIndexOf('{', close);
        if (open < 0)
            return null;

        var candidate = id[open..(close + 1)];
        return Guid.TryParse(candidate, out var parsed)
            ? parsed.ToString("B").ToUpperInvariant()
            : null;
    }

    private static void WithVolume(string deviceId, Action<AudioEndpointVolume> action)
    {
        WithVolume<object?>(deviceId, volume =>
        {
            action(volume);
            return null;
        });
    }

    private static T WithVolume<T>(string deviceId, Func<AudioEndpointVolume, T> action)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(deviceId);
        return action(device.AudioEndpointVolume);
    }

    private sealed class VolumeSubscription : IDisposable
    {
        private readonly MMDevice _device;
        private readonly AudioEndpointVolume _volume;
        private readonly Action<AudioVolumeState> _callback;
        private bool _disposed;

        public VolumeSubscription(string deviceId, Action<AudioVolumeState> callback)
        {
            _callback = callback;
            using var enumerator = new MMDeviceEnumerator();
            _device = enumerator.GetDevice(deviceId);
            _volume = _device.AudioEndpointVolume;
            _volume.OnVolumeNotification += OnVolumeNotification;
        }

        private void OnVolumeNotification(AudioVolumeNotificationData data)
        {
            if (data.EventContext == InternalChangeContext)
                return;

            try { _callback(new AudioVolumeState(data.MasterVolume, data.Muted)); }
            catch (Exception ex) { AppLog.Error("Volume notification handler failed", ex); }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try { _volume.OnVolumeNotification -= OnVolumeNotification; } catch { }
            try { _device.Dispose(); } catch (Exception ex) { AppLog.Error("Audio device subscription cleanup failed", ex); }
        }
    }
}
