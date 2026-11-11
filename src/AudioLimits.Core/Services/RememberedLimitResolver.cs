using AudioLimits.Core.Models;

namespace AudioLimits.Core.Services;

public enum RememberedLimitMatchKind
{
    None,
    Automatic,
    NeedsConfirmation
}

public sealed record RememberedLimitMatch(
    RememberedLimitMatchKind Kind, int LimitPercent)
{
    public static RememberedLimitMatch None { get; } =
        new(RememberedLimitMatchKind.None, 0);
}

/// <summary>
/// Never equates a shared USB model number with a unique physical unit.
/// Automatic recovery requires a real USB serial, one active matching endpoint
/// and a consistent previous limit. Model-only and old name-only matches
/// require explicit confirmation, never automatic modification.
/// </summary>
public static class RememberedLimitResolver
{
    public static RememberedLimitMatch Find(
        AudioDeviceInfo target,
        IReadOnlyList<AudioDeviceInfo> active,
        IReadOnlyList<DeviceLimit> saved)
    {
        if (saved.Any(limit => string.Equals(
                limit.EndpointGuid, target.EndpointGuid, StringComparison.OrdinalIgnoreCase)))
            return RememberedLimitMatch.None;

        if (!string.IsNullOrWhiteSpace(target.HardwareKey))
        {
            var sameModelActive = active.Count(device =>
                string.Equals(device.HardwareKey, target.HardwareKey,
                    StringComparison.OrdinalIgnoreCase));
            if (sameModelActive != 1)
                return RememberedLimitMatch.None;

            var learned = saved.Where(limit => string.Equals(
                limit.HardwareKey, target.HardwareKey,
                StringComparison.OrdinalIgnoreCase)).ToList();

            var match = ResolveConsistentPercent(learned);
            if (match is { } percent)
            {
                // A v1.0 profile may contain several historical endpoint GUIDs
                // for the same headset with different saved caps. Learning the
                // current serial on one of them must not silently override the
                // unresolved legacy preferences on the next USB port.
                var conflictingLegacy = saved.Any(limit =>
                    string.IsNullOrWhiteSpace(limit.HardwareKey) &&
                    string.Equals(limit.FriendlyName, target.FriendlyName,
                        StringComparison.OrdinalIgnoreCase) &&
                    limit.LimitPercent != percent);
                var confidentSerial = target.HardwareKey.StartsWith("USB-SERIAL:",
                    StringComparison.OrdinalIgnoreCase);
                return new RememberedLimitMatch(
                    confidentSerial && !conflictingLegacy
                        ? RememberedLimitMatchKind.Automatic
                        : RememberedLimitMatchKind.NeedsConfirmation,
                    percent);
            }

            // Conflicting learned presets must not fall back to a name guess.
            if (learned.Count > 0)
                return RememberedLimitMatch.None;
        }

        // Pre-existing v1.0 settings had no hardware metadata. A matching name
        // is only a suggestion, never permission for automatic audio changes.
        if (active.Count(device => string.Equals(
                device.FriendlyName, target.FriendlyName,
                StringComparison.OrdinalIgnoreCase)) != 1)
            return RememberedLimitMatch.None;

        var legacy = saved.Where(limit =>
            string.IsNullOrWhiteSpace(limit.HardwareKey) &&
            string.Equals(limit.FriendlyName, target.FriendlyName,
                StringComparison.OrdinalIgnoreCase)).ToList();
        var legacyPercent = ResolveConsistentPercent(legacy);
        return legacyPercent is { } suggested
            ? new RememberedLimitMatch(
                RememberedLimitMatchKind.NeedsConfirmation, suggested)
            : RememberedLimitMatch.None;
    }

    private static int? ResolveConsistentPercent(IReadOnlyList<DeviceLimit> candidates)
    {
        if (candidates.Count == 0)
            return null;
        var percent = candidates[0].LimitPercent;
        return percent is >= 1 and <= 99 && candidates.All(x => x.LimitPercent == percent)
            ? percent : null;
    }
}
