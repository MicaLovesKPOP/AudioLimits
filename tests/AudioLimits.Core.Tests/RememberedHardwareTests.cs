using AudioLimits.Core.Models;
using AudioLimits.Core.Services;

namespace AudioLimits.Core.Tests;

public sealed class RememberedHardwareTests
{
    private const string Receiver = "Headset Earphone (CORSAIR HS80 MAX WIRELESS Gaming Receiver)";
    private const string UsbModel = @"USB\VID_1B1C&PID_0A97&MI_00";
    private const string Parent = @"USB\VID_1B1C&PID_0A97\3E2FAE313F358DA8";

    [Fact]
    public void GenuineUsbSerial_RemainsStableWhenPortChanges()
    {
        var first = PlaybackHardwareIdentity.FromDeviceAncestry(
            new[] { @"SWD\MMDEVAPI\{0.0.0.00000000}.{AAAA}", UsbModel + @"\7&123&0&0000", Parent },
            Receiver);
        var second = PlaybackHardwareIdentity.FromDeviceAncestry(
            new[] { @"SWD\MMDEVAPI\{0.0.0.00000000}.{BBBB}", UsbModel + @"\7&456&0&0000", Parent },
            Receiver);

        Assert.Equal(first, second);
        Assert.StartsWith("USB-SERIAL:", first);
        Assert.DoesNotContain("3E2FAE313F358DA8", first);
    }

    [Fact]
    public void PortSpecificPseudoSerial_IsNotMistakenForPhysicalIdentity()
    {
        var key = PlaybackHardwareIdentity.FromDeviceAncestry(
            new[] { UsbModel + @"\7&123&0&0000", @"USB\VID_1B1C&PID_0A97\6&DEADBEEF&0&2" },
            Receiver);

        Assert.StartsWith("USB-MODEL:", key);
    }

    [Fact]
    public void DistinctRenderRoles_DoNotShareIdentity()
    {
        var chain = new[] { UsbModel + @"\7&abc&0&0000", Parent };
        Assert.NotEqual(
            PlaybackHardwareIdentity.FromDeviceAncestry(chain, Receiver),
            PlaybackHardwareIdentity.FromDeviceAncestry(chain, "Speakers (CORSAIR HS80 MAX WIRELESS Gaming Receiver)"));
    }

    [Fact]
    public void NonUsbEndpoint_IsNotGuessed()
    {
        Assert.Null(PlaybackHardwareIdentity.FromDeviceAncestry(
            new[] { @"SWD\MMDEVAPI\foo", @"HDAUDIO\FUNC_01&VEN_10EC\bar" }, "Speakers"));
    }

    [Fact]
    public void UniqueSerialWithConsistentSavedPreset_MayAutoRestore()
    {
        var key = PlaybackHardwareIdentity.FromDeviceAncestry(
            new[] { UsbModel + @"\7&1&0&0000", Parent }, Receiver);
        var target = Device("new", key);
        var saved = new[] { Saved("old1", 32, key), Saved("old2", 32, key) };

        var match = RememberedLimitResolver.Find(target, new[] { target }, saved);

        Assert.Equal(RememberedLimitMatchKind.Automatic, match.Kind);
        Assert.Equal(32, match.LimitPercent);
    }

    [Fact]
    public void LearnedSerialWithOldConflictingNamePreset_RequiresConfirmation()
    {
        const string key = "USB-SERIAL:VID_1B1C&PID_0A97&MI_00:SERIAL123|HEADSET";
        var target = Device("new", key);
        var match = RememberedLimitResolver.Find(
            target, new[] { target },
            new[] { Saved("old-current", 32, key), Saved("old-legacy", 55, null) });

        Assert.Equal(RememberedLimitMatchKind.NeedsConfirmation, match.Kind);
        Assert.Equal(32, match.LimitPercent);
    }

    [Fact]
    public void LearnedSerialWithConsistentLegacyPresets_CanAutoRestore()
    {
        const string key = "USB-SERIAL:VID_1B1C&PID_0A97&MI_00:SERIAL123|HEADSET";
        var target = Device("new", key);
        var match = RememberedLimitResolver.Find(
            target, new[] { target },
            new[] { Saved("old-current", 32, key), Saved("old-legacy", 32, null) });

        Assert.Equal(RememberedLimitMatchKind.Automatic, match.Kind);
    }

    [Fact]
    public void ModelOnlyMatch_NeverAutoRestores()
    {
        const string key = "USB-MODEL:VID_1B1C&PID_0A97&MI_00|HEADSET";
        var target = Device("new", key);
        var match = RememberedLimitResolver.Find(
            target, new[] { target }, new[] { Saved("old", 25, key) });
        Assert.Equal(RememberedLimitMatchKind.NeedsConfirmation, match.Kind);
    }

    [Fact]
    public void IdenticalConcurrentHeadsets_AreAmbiguous()
    {
        const string key = "USB-SERIAL:VID_1B1C&PID_0A97&MI_00:FAKESERIAL|HEADSET";
        var target = Device("new", key);
        var another = Device("other", key);
        var match = RememberedLimitResolver.Find(
            target, new[] { target, another }, new[] { Saved("old", 25, key) });
        Assert.Equal(RememberedLimitMatchKind.None, match.Kind);
    }

    [Fact]
    public void ConflictingSavedCaps_CannotBeAutoOrManuallyGuessed()
    {
        const string key = "USB-SERIAL:VID_1B1C&PID_0A97&MI_00:SERIAL123|HEADSET";
        var target = Device("new", key);
        var match = RememberedLimitResolver.Find(
            target, new[] { target },
            new[] { Saved("old1", 20, key), Saved("old2", 60, key) });
        Assert.Equal(RememberedLimitMatchKind.None, match.Kind);
    }

    [Fact]
    public void LegacyNameOnlySettings_RequireConfirmation()
    {
        var target = Device("new", "USB-SERIAL:TEST");
        var match = RememberedLimitResolver.Find(
            target, new[] { target }, new[] { Saved("old", 38, null) });
        Assert.Equal(RememberedLimitMatchKind.NeedsConfirmation, match.Kind);
        Assert.Equal(38, match.LimitPercent);
    }

    [Fact]
    public void ExistingEndpointSetting_TakesPrecedenceOverSimilarHardware()
    {
        const string key = "USB-SERIAL:TEST";
        var target = Device("new", key);
        var match = RememberedLimitResolver.Find(
            target, new[] { target }, new[] { Saved("new", 50, key) });
        Assert.Equal(RememberedLimitMatchKind.None, match.Kind);
    }

    private static AudioDeviceInfo Device(string endpointGuid, string? key) =>
        new($"device-{endpointGuid}", endpointGuid, Receiver, .5f, -12f, -90f, 0f, false, false, key);

    private static DeviceLimit Saved(string endpointGuid, int percent, string? key) =>
        new() { EndpointGuid = endpointGuid, FriendlyName = Receiver, HardwareKey = key,
                LimitPercent = percent, AttenuationDb = -12 };
}
