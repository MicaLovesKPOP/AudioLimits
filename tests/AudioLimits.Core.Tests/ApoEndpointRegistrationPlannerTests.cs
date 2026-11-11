using AudioLimits.Core.Services;
using Microsoft.Win32;

namespace AudioLimits.Core.Tests;

public sealed class ApoEndpointRegistrationPlannerTests
{
    private const string VendorPre = "{11111111-2222-3333-4444-555555555555}";
    private const string VendorPost = "{66666666-7777-8888-9999-AAAAAAAAAAAA}";

    [Fact]
    public void CleanEndpoint_UsesStreamAndEndpointEffects()
    {
        var result = ApoEndpointRegistrationPlanner.Build(Clean(), false);
        Assert.True(result.CanInstall);
        Assert.Equal(ApoInstallModeHint.StreamSfxEfx, result.Mode);
        Assert.Equal(ApoEndpointRegistrationPlanner.PreMixGuid,
            Find(result.FxChanges, ApoEndpointRegistrationPlanner.SlotName(5)).Text);
        Assert.Equal(ApoEndpointRegistrationPlanner.PostMixGuid,
            Find(result.FxChanges, ApoEndpointRegistrationPlanner.SlotName(7)).Text);
        Assert.Equal("!VALUE",
            Find(result.ChildValues, ApoEndpointRegistrationPlanner.SlotName(1)).Text);
        Assert.Equal("", Find(result.ChildValues, "PreMixChild").Text);
        Assert.Equal("", Find(result.ChildValues, "PostMixChild").Text);
        Assert.Equal("2", Find(result.ChildValues, "Version").Text);
        Assert.Equal(ApoEndpointRegistrationPlanner.DefaultProcessingMode,
            Find(result.FxChanges, ApoEndpointRegistrationPlanner.ModeName(7)).MultiText![0]);
    }

    [Fact]
    public void VendorEffects_AreKeptForChainingAndRollback()
    {
        var result = ApoEndpointRegistrationPlanner.Build(
            Clean() with { EffectSlots =
            [
                Slot(5, VendorPre), Slot(7, VendorPost),
                Slot(6, "{ABCDEF00-0000-0000-0000-111111111111}")
            ]}, false);
        Assert.True(result.CanInstall);
        Assert.Equal(VendorPre, Find(result.ChildValues, "PreMixChild").Text);
        Assert.Equal(VendorPost, Find(result.ChildValues, "PostMixChild").Text);
        Assert.Equal(VendorPre,
            Find(result.ChildValues, ApoEndpointRegistrationPlanner.SlotName(5)).Text);
        // Upstream leaves an original MFX slot in place in SFX/EFX mode.
        Assert.DoesNotContain(result.FxChanges,
            x => x.Name == ApoEndpointRegistrationPlanner.SlotName(6));
    }

    [Fact]
    public void LegacyMode_ClearsOnlyUnusedModernFxSlots()
    {
        var result = ApoEndpointRegistrationPlanner.Build(
            Clean() with { EffectSlots = [Slot(1, VendorPre), Slot(2, VendorPost)] },
            false);
        Assert.Equal(ApoInstallModeHint.LegacyLfxGfx, result.Mode);
        Assert.Equal(VendorPre, Find(result.ChildValues, "PreMixChild").Text);
        Assert.Equal(VendorPost, Find(result.ChildValues, "PostMixChild").Text);
        Assert.True(Find(result.FxChanges, ApoEndpointRegistrationPlanner.SlotName(5)).Delete);
    }

    [Fact]
    public void BluetoothCombined_UsesModeEffectNotEndpointEffect()
    {
        var result = ApoEndpointRegistrationPlanner.Build(
            Clean() with { IsCombinedBluetoothEndpoint = true }, false);
        Assert.Equal(ApoInstallModeHint.StreamSfxMfx, result.Mode);
        Assert.Equal(ApoEndpointRegistrationPlanner.PostMixGuid,
            Find(result.FxChanges, ApoEndpointRegistrationPlanner.SlotName(6)).Text);
        Assert.DoesNotContain(result.FxChanges,
            x => x.Name == ApoEndpointRegistrationPlanner.SlotName(7));
    }

    [Theory]
    [InlineData(RegistryValueKind.MultiString)]
    [InlineData(RegistryValueKind.Binary)]
    public void NonStringEffectSlots_RefuseUnattendedInstall(RegistryValueKind kind)
    {
        var result = ApoEndpointRegistrationPlanner.Build(
            Clean() with { EffectSlots =
                [new ApoFxSlotSnapshot(5, kind, VendorPre)] }, false);
        Assert.False(result.CanInstall);
        Assert.Empty(result.FxChanges);
    }

    [Fact]
    public void UnverifiableEqualizerApoRegistration_FailsClosed()
    {
        var result = ApoEndpointRegistrationPlanner.Build(
            Clean() with { EffectSlots =
                [Slot(5, ApoEndpointRegistrationPlanner.PreMixGuid)] }, false);
        Assert.Equal(ApoProvisioningReadiness.UnsupportedRegistration, result.Readiness);
    }

    [Fact]
    public void DisabledEnhancements_CannotRegister()
    {
        var result = ApoEndpointRegistrationPlanner.Build(
            Clean() with { EnhancementsDisabled = true }, false);
        Assert.Equal(ApoProvisioningReadiness.EnhancementsDisabled, result.Readiness);
    }

    [Fact]
    public void ExistingRegistration_IsNotReplaced()
    {
        var result = ApoEndpointRegistrationPlanner.Build(Clean(), true);
        Assert.Equal(ApoProvisioningReadiness.AlreadyRegistered, result.Readiness);
    }

    [Fact]
    public void MissingFxKey_RequiresAdvancedSetup()
    {
        var result = ApoEndpointRegistrationPlanner.Build(
            Clean() with { FxKeyExists = false }, false);
        Assert.False(result.CanInstall);
        Assert.Equal(ApoProvisioningReadiness.UnsupportedRegistration, result.Readiness);
        Assert.Empty(result.FxChanges);
        Assert.Contains("FxProperties", result.Explanation);
    }

    private static ApoRegistryMutation Find(
        IReadOnlyList<ApoRegistryMutation> changes, string name) =>
        Assert.Single(changes, x => x.Name == name);

    private static ApoFxSlotSnapshot Slot(int index, string guid) =>
        new(index, RegistryValueKind.String, guid);

    private static ApoFxRegistrationSnapshot Clean() =>
        new(true, true, false, Array.Empty<ApoFxSlotSnapshot>());
}
