using System.Text.Json;
using AudioLimits.Core.Models;

namespace AudioLimits.Core.Services;

public enum SettingsLoadStatus
{
    New,
    Loaded,
    Migrated,
    RecoveredFromBackup,
    Unrecoverable
}

public sealed record SettingsLoadResult(
    AppSettings Settings,
    SettingsLoadStatus Status,
    bool IsAuthoritative,
    string? Message = null);

public sealed class SettingsStore
{
    private const int CurrentSchemaVersion = 3;
    private readonly string _directory;

    public SettingsStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioLimits");
    }

    private string SettingsPath => Path.Combine(_directory, "settings.json");
    private string BackupPath => Path.Combine(_directory, "settings.backup.json");

    public SettingsLoadResult Load()
    {
        Directory.CreateDirectory(_directory);

        if (!File.Exists(SettingsPath))
        {
            if (!File.Exists(BackupPath))
                return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.New, true);

            try
            {
                var backupJson = File.ReadAllText(BackupPath);
                var (backup, _) = DeserializeAndMigrate(backupJson);
                Validate(backup);
                Save(backup);
                AppLog.Info("Recovered missing Audio Limits settings from validated backup");
                return new SettingsLoadResult(
                    backup,
                    SettingsLoadStatus.RecoveredFromBackup,
                    true,
                    "Settings were recovered from backup.");
            }
            catch (Exception backupEx)
            {
                AppLog.Error("Settings file was missing and the backup was invalid", backupEx);
                QuarantineCorruptSettings(BackupPath, "settings.backup.corrupt");
                return new SettingsLoadResult(
                    new AppSettings(),
                    SettingsLoadStatus.Unrecoverable,
                    false,
                    "Audio Limits could not safely recover its missing settings from backup. Existing audio processing was left unchanged.");
            }
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var (settings, migrated) = DeserializeAndMigrate(json);
            Validate(settings);

            if (migrated)
            {
                Save(settings);
                AppLog.Info($"Migrated Audio Limits settings to schema {CurrentSchemaVersion}");
                return new SettingsLoadResult(settings, SettingsLoadStatus.Migrated, true);
            }

            return new SettingsLoadResult(settings, SettingsLoadStatus.Loaded, true);
        }
        catch (Exception ex)
        {
            AppLog.Error("Settings file was invalid; attempting backup recovery", ex);
            QuarantineCorruptSettings(SettingsPath, "settings.corrupt");

            try
            {
                if (File.Exists(BackupPath))
                {
                    var backupJson = File.ReadAllText(BackupPath);
                    var (backup, _) = DeserializeAndMigrate(backupJson);
                    Validate(backup);
                    Save(backup);
                    AppLog.Info("Recovered Audio Limits settings from validated backup");
                    return new SettingsLoadResult(
                        backup,
                        SettingsLoadStatus.RecoveredFromBackup,
                        true,
                        "Settings were recovered from backup.");
                }
            }
            catch (Exception backupEx)
            {
                AppLog.Error("Settings backup was also invalid", backupEx);
                QuarantineCorruptSettings(BackupPath, "settings.backup.corrupt");
            }

            return new SettingsLoadResult(
                new AppSettings(),
                SettingsLoadStatus.Unrecoverable,
                false,
                "Audio Limits could not safely read its settings or a validated backup. Existing audio processing was left unchanged.");
        }
    }

    public void Save(AppSettings settings)
    {
        Validate(settings);
        Directory.CreateDirectory(_directory);

        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

        var temp = Path.Combine(_directory, $"settings.{Guid.NewGuid():N}.tmp");
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        try
        {
            if (File.Exists(SettingsPath))
            {
                if (TryValidateExistingSettings(SettingsPath))
                {
                    File.Replace(temp, SettingsPath, BackupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    QuarantineCorruptSettings(SettingsPath, "settings.corrupt");
                    File.Move(temp, SettingsPath, overwrite: true);
                }
            }
            else
            {
                File.Move(temp, SettingsPath);
            }
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private (AppSettings Settings, bool Migrated) DeserializeAndMigrate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var schema = doc.RootElement.TryGetProperty("SchemaVersion", out var schemaElement)
            ? schemaElement.GetInt32()
            : 1;

        if (schema > CurrentSchemaVersion)
            throw new InvalidDataException(
                $"Settings schema {schema} is newer than this version of Audio Limits supports.");

        if (schema == 1)
            return (MigrateSchema1(json), true);

        var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)
                       ?? throw new InvalidDataException("Settings could not be deserialized.");

        var migrated = schema < CurrentSchemaVersion;
        settings.SchemaVersion = CurrentSchemaVersion;
        return (settings, migrated);
    }

    private static AppSettings MigrateSchema1(string json)
    {
        var legacy = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.LegacySettings)
                     ?? new LegacySettings();
        var migrated = new AppSettings();

        foreach (var old in legacy.Limits ?? new List<LegacyLimit>())
        {
            if (!Guid.TryParse(old.EndpointGuid, out var parsedGuid) ||
                !double.IsFinite(old.AttenuationDb) ||
                !double.IsFinite(old.EquivalentWindowsPercent))
                continue;

            migrated.Limits.Add(new DeviceLimit
            {
                EndpointGuid = parsedGuid.ToString("B").ToUpperInvariant(),
                FriendlyName = string.IsNullOrWhiteSpace(old.FriendlyName) ? "Audio device" : old.FriendlyName,
                LimitPercent = Math.Clamp(
                    (int)Math.Round(old.EquivalentWindowsPercent, MidpointRounding.AwayFromZero),
                    1,
                    99),
                AttenuationDb = Math.Clamp(old.AttenuationDb, -200.0, 0.0),
                VolumeCurveDb = new List<double>(),
                UpdatedUtc = DateTime.UtcNow
            });
        }

        return migrated;
    }

    private bool TryValidateExistingSettings(string path)
    {
        try
        {
            var (settings, _) = DeserializeAndMigrate(File.ReadAllText(path));
            Validate(settings);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Validate(AppSettings settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        settings.Limits ??= new List<DeviceLimit>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var limit in settings.Limits)
        {
            ValidateLimit(limit);
            if (!seen.Add(limit.EndpointGuid))
                throw new InvalidDataException($"Duplicate limit for endpoint {limit.EndpointGuid}.");
        }

        if (settings.PendingChange is { } pending)
        {
            if (string.IsNullOrWhiteSpace(pending.DeviceId))
                throw new InvalidDataException("Pending recovery is missing the Windows device ID.");
            pending.EndpointGuid = NormalizeGuid(pending.EndpointGuid);
            pending.FriendlyName = string.IsNullOrWhiteSpace(pending.FriendlyName)
                ? "Audio device"
                : pending.FriendlyName.Trim();

            if (pending.PreviousLimit is not null)
            {
                ValidateLimit(pending.PreviousLimit);
                if (!string.Equals(
                        pending.PreviousLimit.EndpointGuid,
                        pending.EndpointGuid,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Pending recovery previous-limit endpoint does not match the pending endpoint.");
            }

            if (pending.DesiredLimit is not null)
            {
                ValidateLimit(pending.DesiredLimit);
                if (!string.Equals(
                        pending.DesiredLimit.EndpointGuid,
                        pending.EndpointGuid,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Pending recovery desired-limit endpoint does not match the pending endpoint.");
            }

            if (pending.PreviousLimit is null && pending.DesiredLimit is null)
                throw new InvalidDataException(
                    "Pending recovery contains neither a previous nor a desired limit.");

            if (pending.PreviousLimitWasActive && pending.PreviousLimit is null)
                throw new InvalidDataException(
                    "Pending recovery says a previous limit was active but contains no previous limit.");

            if (pending.PreviousAppliedAttenuationDb is { } previousApplied &&
                (!double.IsFinite(previousApplied) || previousApplied > 0.0001 || previousApplied < -200.0))
                throw new InvalidDataException(
                    "Pending recovery previous applied attenuation is invalid.");

            if (!Enum.IsDefined(typeof(PendingChangePhase), pending.Phase))
                throw new InvalidDataException("Pending recovery phase is invalid.");

            if (pending.CreatedUtc == default)
                throw new InvalidDataException("Pending recovery is missing its creation time.");
            if (pending.CreatedUtc.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5))
                throw new InvalidDataException("Pending recovery creation time is implausibly in the future.");
            if (pending.RecoveryAttempts < 0 || pending.RecoveryAttempts > 1000)
                throw new InvalidDataException("Pending recovery attempt count is invalid.");
            if (pending.LastRecoveryAttemptUtc is { } lastAttempt &&
                lastAttempt.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5))
                throw new InvalidDataException(
                    "Pending recovery last-attempt time is implausibly in the future.");
        }
    }

    private static void ValidateLimit(DeviceLimit limit)
    {
        limit.EndpointGuid = NormalizeGuid(limit.EndpointGuid);
        limit.FriendlyName = string.IsNullOrWhiteSpace(limit.FriendlyName)
            ? "Audio device"
            : limit.FriendlyName.Trim();

        if (limit.LimitPercent is < 1 or > 99)
            throw new InvalidDataException($"Limit percentage for {limit.FriendlyName} is outside 1–99%.");

        if (!double.IsFinite(limit.AttenuationDb) ||
            limit.AttenuationDb > 0.0001 ||
            limit.AttenuationDb < -200.0)
            throw new InvalidDataException($"Attenuation for {limit.FriendlyName} is invalid.");

        limit.VolumeCurveDb ??= new List<double>();
        if (limit.VolumeCurveDb.Count != 0 && !VolumeCurve.IsValid(limit.VolumeCurveDb))
            throw new InvalidDataException($"Stored volume calibration for {limit.FriendlyName} is invalid.");

        if (limit.UpdatedUtc == default)
            limit.UpdatedUtc = DateTime.UtcNow;
    }

    private static string NormalizeGuid(string value)
    {
        if (!Guid.TryParse(value, out var parsed))
            throw new InvalidDataException($"Invalid audio endpoint GUID: {value}");
        return parsed.ToString("B").ToUpperInvariant();
    }

    private void QuarantineCorruptSettings(string path, string prefix)
    {
        try
        {
            if (!File.Exists(path))
                return;

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            File.Move(path, Path.Combine(_directory, $"{prefix}-{stamp}.json"), true);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Could not quarantine invalid settings at {path}", ex);
        }
    }

}
