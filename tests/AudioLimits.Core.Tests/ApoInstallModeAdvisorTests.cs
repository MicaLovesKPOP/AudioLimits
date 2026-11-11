using AudioLimits.Core.Services;
using Microsoft.Win32;

namespace AudioLimits.Core.Tests;

public sealed class ApoInstallModeAdvisorTests
{
    private const string Effect = "{01234567-89AB-CDEF-1234-567890ABCDEF}";

    [Fact]
    public void UnreadableEndpoint_HasNoModeSuggestion()
    {
        Assert.Null(ApoInstallModeAdvisor.Suggest(
            ApoFxRegistrationSnapshot.Unavailable));
    }

    [Fact]
    public void CleanStandardEndpoint_DefaultsToSfxEfx()
    {
        var result = ApoInstallModeAdvisor.Suggest(
            new ApoFxRegistrationSnapshot(true, true, false,
                Array.Empty<ApoFxSlotSnapshot>()));
        Assert.Equal(ApoInstallModeHint.StreamSfxEfx, result);
    }

    [Fact]
    public void MissingFxProperties_UsesLegacyDefaultLikeOfficialSelector()
    {
        var result = ApoInstallModeAdvisor.Suggest(
            new ApoFxRegistrationSnapshot(true, false, false,
                Array.Empty<ApoFxSlotSnapshot>()));
        Assert.Equal(ApoInstallModeHint.LegacyLfxGfx, result);
    }

    [Fact]
    public void CombinedBluetoothEndpoint_PrefersSfxMfx()
    {
        var result = ApoInstallModeAdvisor.Suggest(
            new ApoFxRegistrationSnapshot(true, true, false,
                Array.Empty<ApoFxSlotSnapshot>(), IsCombinedBluetoothEndpoint: true));
        Assert.Equal(ApoInstallModeHint.StreamSfxMfx, result);
    }

    [Fact]
    public void LegacyOnlyDriverEffects_PreferLfxGfx()
    {
        var result = ApoInstallModeAdvisor.Suggest(
            Snapshot([1, 2]));
        Assert.Equal(ApoInstallModeHint.LegacyLfxGfx, result);
    }

    [Fact]
    public void ModernEffectSlots_OverrideLegacyPreference()
    {
        var result = ApoInstallModeAdvisor.Suggest(
            Snapshot([1, 2, 7]));
        Assert.Equal(ApoInstallModeHint.StreamSfxEfx, result);
    }

    [Fact]
    public void ModernMultiEffectSlot_OverridesLegacyPreference()
    {
        var result = ApoInstallModeAdvisor.Suggest(
            Snapshot([1, 15]));
        Assert.Equal(ApoInstallModeHint.StreamSfxEfx, result);
    }

    private static ApoFxRegistrationSnapshot Snapshot(int[] slots) =>
        new(true, true, false,
            slots.Select(slot => new ApoFxSlotSnapshot(
                slot, RegistryValueKind.String, Effect)).ToArray());
}
