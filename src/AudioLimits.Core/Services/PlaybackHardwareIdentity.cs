using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AudioLimits.Core.Services;

/// <summary>
/// Recognizes the model and playback role of a USB endpoint without using
/// Windows' port-specific MMDevice GUID or USB instance suffix. A model key
/// is not a unique physical serial number: callers must reject ambiguous
/// matches when multiple identical endpoints or conflicting presets exist.
/// </summary>
public static partial class PlaybackHardwareIdentity
{
    private const int Success = 0;

    public static string? TryRead(string endpointId, string friendlyName)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(endpointId))
            return null;

        try
        {
            // Core Audio returns {0.0.0...}.{guid}; Configuration Manager
            // names the same endpoint devnode SWD\\MMDEVAPI\\{0.0.0...}.{guid}.
            var pnpId = endpointId.StartsWith(@"SWD\MMDEVAPI\", StringComparison.OrdinalIgnoreCase)
                ? endpointId
                : @"SWD\MMDEVAPI\" + endpointId;
            if (CM_Locate_DevNodeW(out var node, pnpId, 0) != Success)
                return null;

            var ancestors = new List<string>();
            for (var depth = 0; depth < 12; depth++)
            {
                var buffer = new StringBuilder(1024);
                if (CM_Get_Device_IDW(node, buffer, buffer.Capacity, 0) != Success)
                    break;
                ancestors.Add(buffer.ToString());
                if (CM_Get_Parent(out node, node, 0) != Success)
                    break;
            }

            return FromDeviceAncestry(ancestors, friendlyName);
        }
        catch (Exception ex)
        {
            AppLog.Error("Could not identify USB playback hardware", ex);
            return null;
        }
    }

    // Kept pure for fixture-based tests without touching a user's audio devices.
    public static string? FromDeviceAncestry(
        IEnumerable<string> deviceInstanceIds,
        string friendlyName)
    {
        var role = NormalizeRole(friendlyName);
        if (role.Length == 0)
            return null;

        var ancestry = deviceInstanceIds.Where(x => x is not null).ToArray();
        var closestUsb = ancestry
            .Select(id => UsbInstancePattern().Match(id))
            .FirstOrDefault(match => match.Success);
        if (closestUsb is null)
            return null;

        // Retain interface identity while dropping the port-specific USB
        // instance suffix. A real parent USB serial supplies physical identity
        // across ports; a model-only key cannot authorize automatic reuse.
        var model = closestUsb.Groups["model"].Value.ToUpperInvariant();
        var serial = ancestry
            .Select(id => SerialUsbInstancePattern().Match(id))
            .FirstOrDefault(match => match.Success &&
                model.StartsWith(match.Groups["model"].Value,
                    StringComparison.OrdinalIgnoreCase))
            ?.Groups["serial"].Value.ToUpperInvariant();

        // Persist a deterministic short digest rather than the receiver's raw
        // factory serial. This stays stable across USB ports without exposing
        // the physical serial in user-readable settings and backups.
        var serialDigest = serial is null
            ? null
            : Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{model}:{serial}")))[..32];

        return serialDigest is null
            ? $"USB-MODEL:{model}|{role}"
            : $"USB-SERIAL:{model}:{serialDigest}|{role}";
    }

    private static string NormalizeRole(string value) =>
        string.Join(" ", (value ?? "").Trim().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    [GeneratedRegex(
        @"^USB\\(?<model>VID_[0-9A-F]{4}&PID_[0-9A-F]{4}(?:&MI_[0-9A-F]{2})?)(?:\\|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsbInstancePattern();

    // Composite USB instance suffixes containing '&' encode topology and are
    // deliberately excluded: those values change when the receiver moves.
    [GeneratedRegex(
        @"^USB\\(?<model>VID_[0-9A-F]{4}&PID_[0-9A-F]{4})\\(?<serial>[A-Z0-9_.-]{4,128})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SerialUsbInstancePattern();

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Locate_DevNodeW(
        out uint deviceInstance, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_Device_IDW(
        uint deviceInstance, StringBuilder buffer, int bufferLength, uint flags);

    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern int CM_Get_Parent(
        out uint parentDeviceInstance, uint deviceInstance, uint flags);
}
