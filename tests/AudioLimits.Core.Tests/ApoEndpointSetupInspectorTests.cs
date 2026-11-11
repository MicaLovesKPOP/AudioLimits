using AudioLimits.Core.Services;
using Microsoft.Win32;

namespace AudioLimits.Core.Tests;

public sealed class ApoEndpointSetupInspectorTests
{
    private const string OtherEffect = "{01234567-89AB-CDEF-1234-567890ABCDEF}";

    [Fact]
    public void InaccessibleRegistry_FailsClosed()
    {
        var result = ApoEndpointSetupAssessment.Evaluate(
            ApoFxRegistrationSnapshot.Unavailable, false);
        Assert.Equal(ApoEndpointSetupStatus.Unavailable, result.Status);
        Assert.True(result.RequiresDriverEffectPreservation);
        Assert.False(result.CanAssumeLiveProcessing);
    }

    [Fact]
    public void RegisteredEqualizerApo_IsNotClaimedRuntimeVerified()
    {
        var result = ApoEndpointSetupAssessment.Evaluate(Clean(), true);
        Assert.Equal(ApoEndpointSetupStatus.AlreadyRegistered, result.Status);
        Assert.False(result.CanAssumeLiveProcessing);
        Assert.Contains("restart", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnhancementsDisabled_TakesPriorityOverRegistration()
    {
        var result = ApoEndpointSetupAssessment.Evaluate(
            Clean() with { EnhancementsDisabled = true }, true);
        Assert.Equal(ApoEndpointSetupStatus.EnhancementsDisabled, result.Status);
        Assert.True(result.RequiresDriverEffectPreservation);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void ExistingEffectInAnyOfficialSlot_RequiresPreservation(int slot)
    {
        var result = ApoEndpointSetupAssessment.Evaluate(
            Clean() with
            {
                EffectSlots = new[] { new ApoFxSlotSnapshot(
                    slot, RegistryValueKind.String, OtherEffect) }
            }, false);
        Assert.Equal(ApoEndpointSetupStatus.ExistingDriverEffects, result.Status);
        Assert.Equal(1, result.ExistingEffectCount);
        Assert.True(result.RequiresDriverEffectPreservation);
    }

    [Fact]
    public void MultiValueSlot_RequiresExpertReview()
    {
        var result = ApoEndpointSetupAssessment.Evaluate(
            Clean() with { EffectSlots = new[]
            {
                new ApoFxSlotSnapshot(7, RegistryValueKind.MultiString, OtherEffect)
            }}, false);
        Assert.Equal(ApoEndpointSetupStatus.UnusualEffectValues, result.Status);
        Assert.True(result.RequiresDriverEffectPreservation);
    }

    [Fact]
    public void MalformedClsid_RequiresExpertReview()
    {
        var result = ApoEndpointSetupAssessment.Evaluate(
            Clean() with { EffectSlots = new[]
            {
                new ApoFxSlotSnapshot(6, RegistryValueKind.String, "not-a-guid")
            }}, false);
        Assert.Equal(ApoEndpointSetupStatus.UnusualEffectValues, result.Status);
    }

    [Fact]
    public void MissingFxKey_ReportsDistinctAccessRequirement()
    {
        var result = ApoEndpointSetupAssessment.Evaluate(
            Clean() with { FxKeyExists = false }, false);
        Assert.Equal(ApoEndpointSetupStatus.MissingFxProperties, result.Status);
        Assert.True(result.RequiresDriverEffectPreservation);
    }

    [Fact]
    public void CleanFxKey_HasNoOriginalEffects()
    {
        var result = ApoEndpointSetupAssessment.Evaluate(Clean(), false);
        Assert.Equal(ApoEndpointSetupStatus.CleanRegistrationNeeded, result.Status);
        Assert.False(result.RequiresDriverEffectPreservation);
        Assert.False(result.CanAssumeLiveProcessing);
    }

    private static ApoFxRegistrationSnapshot Clean() =>
        new(true, true, false, Array.Empty<ApoFxSlotSnapshot>());
}
