using System.Text.Json;
using AudioLimits.Core.Models;
using AudioLimits.Core.Services;

namespace AudioLimits.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void MissingSettings_AreAuthoritativeFirstRun()
    {
        using var temp = new TemporaryDirectory();
        var store = new SettingsStore(temp.Path);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.New, result.Status);
        Assert.True(result.IsAuthoritative);
        Assert.Empty(result.Settings.Limits);
    }

    [Fact]
    public void MissingPrimaryWithValidBackup_RecoversAuthoritativeState()
    {
        using var temp = new TemporaryDirectory();
        var store = new SettingsStore(temp.Path);
        var endpoint = Guid.NewGuid().ToString("B").ToUpperInvariant();

        var settings = new AppSettings();
        settings.Limits.Add(new DeviceLimit
        {
            EndpointGuid = endpoint,
            FriendlyName = "Headphones",
            LimitPercent = 10,
            AttenuationDb = -20
        });

        store.Save(settings);
        File.Copy(
            Path.Combine(temp.Path, "settings.json"),
            Path.Combine(temp.Path, "settings.backup.json"));
        File.Delete(Path.Combine(temp.Path, "settings.json"));

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromBackup, result.Status);
        Assert.True(result.IsAuthoritative);
        var limit = Assert.Single(result.Settings.Limits);
        Assert.Equal(10, limit.LimitPercent);
        Assert.True(File.Exists(Path.Combine(temp.Path, "settings.json")));
    }

    [Fact]
    public void MissingPrimaryWithInvalidBackup_IsNotTreatedAsFirstRun()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "settings.backup.json"), "{not json");
        var store = new SettingsStore(temp.Path);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.Unrecoverable, result.Status);
        Assert.False(result.IsAuthoritative);
        Assert.Contains(
            Directory.GetFiles(temp.Path),
            path => Path.GetFileName(path).StartsWith(
                "settings.backup.corrupt-",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CorruptPrimaryWithValidBackup_RecoversAuthoritativeState()
    {
        using var temp = new TemporaryDirectory();
        var store = new SettingsStore(temp.Path);
        var endpoint = Guid.NewGuid().ToString("B").ToUpperInvariant();

        var settings = new AppSettings();
        settings.Limits.Add(new DeviceLimit
        {
            EndpointGuid = endpoint,
            FriendlyName = "TV",
            LimitPercent = 33,
            AttenuationDb = -12
        });

        store.Save(settings);
        File.Copy(
            Path.Combine(temp.Path, "settings.json"),
            Path.Combine(temp.Path, "settings.backup.json"));
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), "{not json");

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromBackup, result.Status);
        Assert.True(result.IsAuthoritative);
        var limit = Assert.Single(result.Settings.Limits);
        Assert.Equal(33, limit.LimitPercent);
        Assert.Contains(
            Directory.GetFiles(temp.Path),
            path => Path.GetFileName(path).StartsWith(
                "settings.corrupt-",
                StringComparison.Ordinal));
    }

    [Fact]
    public void CorruptSettingsWithoutBackup_AreNotTreatedAsEmptyAuthoritativeState()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), "{not json");
        var store = new SettingsStore(temp.Path);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.Unrecoverable, result.Status);
        Assert.False(result.IsAuthoritative);
        Assert.Contains(
            Directory.GetFiles(temp.Path),
            path => Path.GetFileName(path).StartsWith(
                "settings.corrupt-",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Schema1_IsMigratedWithoutInventingVolumeCurve()
    {
        using var temp = new TemporaryDirectory();
        var endpoint = Guid.NewGuid().ToString("B");
        var legacy = $$"""
        {
          "SchemaVersion": 1,
          "Limits": [
            {
              "EndpointGuid": "{{endpoint}}",
              "FriendlyName": "Headphones",
              "AttenuationDb": -23.5,
              "EquivalentWindowsPercent": 10.4
            }
          ]
        }
        """;

        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), legacy);
        var store = new SettingsStore(temp.Path);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.Migrated, result.Status);
        var limit = Assert.Single(result.Settings.Limits);
        Assert.Equal(10, limit.LimitPercent);
        Assert.Equal(-23.5, limit.AttenuationDb, 6);
        Assert.Empty(limit.VolumeCurveDb);
        Assert.Equal(3, result.Settings.SchemaVersion);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValidatedState()
    {
        using var temp = new TemporaryDirectory();
        var store = new SettingsStore(temp.Path);
        var endpoint = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var curve = Enumerable.Range(0, 101)
            .Select(x => -100.0 + x)
            .ToList();

        var settings = new AppSettings();
        settings.Limits.Add(new DeviceLimit
        {
            EndpointGuid = endpoint,
            FriendlyName = "TV",
            LimitPercent = 33,
            AttenuationDb = -12.25,
            VolumeCurveDb = curve
        });

        store.Save(settings);
        var result = store.Load();

        Assert.True(result.IsAuthoritative);
        var limit = Assert.Single(result.Settings.Limits);
        Assert.Equal(33, limit.LimitPercent);
        Assert.Equal(-12.25, limit.AttenuationDb, 6);
        Assert.Equal(101, limit.VolumeCurveDb.Count);
    }



    [Fact]
    public void SaveThenLoad_RoundTripsPreviousAppliedAttenuation()
    {
        using var temp = new TemporaryDirectory();
        var store = new SettingsStore(temp.Path);
        var endpoint = Guid.NewGuid().ToString("B").ToUpperInvariant();

        var previous = new DeviceLimit
        {
            EndpointGuid = endpoint,
            FriendlyName = "Headphones",
            LimitPercent = 25,
            AttenuationDb = -20.0
        };

        var settings = new AppSettings
        {
            PendingChange = new PendingLimitChange
            {
                DeviceId = "device-id",
                EndpointGuid = endpoint,
                FriendlyName = "Headphones",
                PreviousLimit = previous,
                DesiredLimit = new DeviceLimit
                {
                    EndpointGuid = endpoint,
                    FriendlyName = "Headphones",
                    LimitPercent = 10,
                    AttenuationDb = -30.0
                },
                PreviousLimitWasActive = true,
                PreviousAppliedAttenuationDb = -21.5,
                CreatedUtc = DateTime.UtcNow
            }
        };

        store.Save(settings);
        var result = store.Load();

        Assert.True(result.IsAuthoritative);
        Assert.NotNull(result.Settings.PendingChange);
        Assert.Equal(-21.5, result.Settings.PendingChange!.PreviousAppliedAttenuationDb!.Value, 6);
    }

    [Fact]
    public void Save_RejectsInvalidPreviousAppliedAttenuation()
    {
        using var temp = new TemporaryDirectory();
        var store = new SettingsStore(temp.Path);
        var endpoint = Guid.NewGuid().ToString("B").ToUpperInvariant();

        var settings = new AppSettings
        {
            PendingChange = new PendingLimitChange
            {
                DeviceId = "device-id",
                EndpointGuid = endpoint,
                FriendlyName = "Headphones",
                PreviousLimit = new DeviceLimit
                {
                    EndpointGuid = endpoint,
                    FriendlyName = "Headphones",
                    LimitPercent = 25,
                    AttenuationDb = -20.0
                },
                DesiredLimit = new DeviceLimit
                {
                    EndpointGuid = endpoint,
                    FriendlyName = "Headphones",
                    LimitPercent = 10,
                    AttenuationDb = -30.0
                },
                PreviousLimitWasActive = true,
                PreviousAppliedAttenuationDb = double.PositiveInfinity,
                CreatedUtc = DateTime.UtcNow
            }
        };

        Assert.Throws<InvalidDataException>(() => store.Save(settings));
    }

    [Fact]
    public void Save_RejectsPendingChangeWithInvalidPhase()
    {
        using var temp = new TemporaryDirectory();
        var store = new SettingsStore(temp.Path);
        var endpoint = Guid.NewGuid().ToString("B").ToUpperInvariant();

        var settings = new AppSettings
        {
            PendingChange = new PendingLimitChange
            {
                DeviceId = "device-id",
                EndpointGuid = endpoint,
                FriendlyName = "Headphones",
                DesiredLimit = new DeviceLimit
                {
                    EndpointGuid = endpoint,
                    FriendlyName = "Headphones",
                    LimitPercent = 10,
                    AttenuationDb = -20
                },
                Phase = (PendingChangePhase)999,
                CreatedUtc = DateTime.UtcNow
            }
        };

        Assert.Throws<InvalidDataException>(() => store.Save(settings));
    }

    [Fact]
    public void Save_RejectsPendingChangeWhoseNestedEndpointDoesNotMatch()
    {
        using var temp = new TemporaryDirectory();
        var store = new SettingsStore(temp.Path);
        var endpoint = Guid.NewGuid().ToString("B").ToUpperInvariant();
        var other = Guid.NewGuid().ToString("B").ToUpperInvariant();

        var settings = new AppSettings
        {
            PendingChange = new PendingLimitChange
            {
                DeviceId = "device-id",
                EndpointGuid = endpoint,
                FriendlyName = "Headphones",
                PreviousLimit = new DeviceLimit
                {
                    EndpointGuid = other,
                    FriendlyName = "Other device",
                    LimitPercent = 10,
                    AttenuationDb = -20
                },
                CreatedUtc = DateTime.UtcNow
            }
        };

        Assert.Throws<InvalidDataException>(() => store.Save(settings));
    }


    [Fact]
    public void Save_RejectsActivePreviousStateWithoutPreviousLimit()
    {
        using var temp = new TemporaryDirectory();
        var store = new SettingsStore(temp.Path);
        var endpoint = Guid.NewGuid().ToString("B").ToUpperInvariant();

        var settings = new AppSettings
        {
            PendingChange = new PendingLimitChange
            {
                DeviceId = "device-id",
                EndpointGuid = endpoint,
                FriendlyName = "Headphones",
                PreviousLimitWasActive = true,
                DesiredLimit = new DeviceLimit
                {
                    EndpointGuid = endpoint,
                    FriendlyName = "Headphones",
                    LimitPercent = 5,
                    AttenuationDb = -30
                },
                CreatedUtc = DateTime.UtcNow
            }
        };

        Assert.Throws<InvalidDataException>(() => store.Save(settings));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "AudioLimits.Tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Test temp cleanup is best effort.
            }
        }
    }
}
