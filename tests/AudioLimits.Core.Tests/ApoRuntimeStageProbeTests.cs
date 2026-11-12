using AudioLimits.Core.Services;

namespace AudioLimits.Core.Tests;

public sealed class ApoRuntimeStageProbeTests
{
    private const string Endpoint = "{F32364D3-F6D0-413D-8751-5CF60EC70C55}";
    private const string Other = "{0A32E5C6-A49E-478E-97F5-4C4A330E1A05}";

    [Fact]
    public void CorrectStageInitializationForExactEndpoint_IsAccepted()
    {
        Assert.True(ApoRuntimeStageProbe.IsExpectedInitializationEvent(
            $"{{\"deviceGuid\":\"{Endpoint}\",\"stage\":\"PostMix\",\"phase\":\"Initialize\"}}",
            Endpoint, "PostMix"));
    }

    [Theory]
    [InlineData("PreMix", "Initialize")]
    [InlineData("PostMix", "ChildAPO")]
    [InlineData("PostMix", "APOProcess")]
    public void WrongStageOrWrongPhase_DoesNotMarkReady(
        string stage, string phase)
    {
        Assert.False(ApoRuntimeStageProbe.IsExpectedInitializationEvent(
            $"{{\"deviceGuid\":\"{Endpoint}\",\"stage\":\"{stage}\",\"phase\":\"{phase}\"}}",
            Endpoint, "PostMix"));
    }

    [Fact]
    public void OtherDeviceCannotSatisfyStageCheck()
    {
        Assert.False(ApoRuntimeStageProbe.IsExpectedInitializationEvent(
            $"{{\"deviceGuid\":\"{Other}\",\"stage\":\"PostMix\",\"phase\":\"Initialize\"}}",
            Endpoint, "PostMix"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"deviceGuid\":42,\"stage\":\"PostMix\",\"phase\":\"Initialize\"}")]
    [InlineData("{\"deviceGuid\":\"bad\",\"stage\":\"PostMix\",\"phase\":\"Initialize\"}")]
    public void MalformedDiagnosticEvent_FailsClosed(string message)
    {
        Assert.False(ApoRuntimeStageProbe.IsExpectedInitializationEvent(
            message, Endpoint, "PostMix"));
    }

    [Theory]
    [InlineData("Prepared", -30, ApoSetupPendingState.RecoveryRequired)]
    [InlineData("RollbackIncomplete", 30, ApoSetupPendingState.RecoveryRequired)]
    [InlineData("RegisteredRestartRequired", -30, ApoSetupPendingState.RestartRequired)]
    [InlineData("RegisteredRestartRequired", 30, ApoSetupPendingState.ActivationVerificationRequired)]
    [InlineData("StageInitialized", 30, ApoSetupPendingState.None)]
    [InlineData("StageNotInitialized", 30, ApoSetupPendingState.CompatibilityFixRequired)]
    [InlineData("ModeSwitchPrepared", 30, ApoSetupPendingState.RecoveryRequired)]
    [InlineData("ModeSwitchRollbackIncomplete", 30, ApoSetupPendingState.RecoveryRequired)]
    [InlineData("ModeSwitchRestartRequired", -30, ApoSetupPendingState.RestartRequired)]
    [InlineData("ModeSwitchRestartRequired", 30, ApoSetupPendingState.ActivationVerificationRequired)]
    [InlineData("RolledBack", -30, ApoSetupPendingState.None)]
    [InlineData("Unknown", 30, ApoSetupPendingState.RecoveryRequired)]
    public void NewEndpointNeverBecomesReadyFromRebootAlone(
        string phase, int rebootOffsetMinutes, ApoSetupPendingState expected)
    {
        var registrationTime = new DateTimeOffset(
            2026, 11, 11, 16, 0, 0, TimeSpan.Zero);
        var state = new ApoRegistryBackup(Endpoint, registrationTime, phase,
            true, Array.Empty<RegistryValueBackup>(),
            Array.Empty<ApoRegistryMutation>(),
            Array.Empty<ApoRegistryMutation>(),
            FallbackChangedUtc: registrationTime);
        var actual = ApoEndpointRegistrationTransaction.ClassifyPendingState(
            state, registrationTime.AddMinutes(rebootOffsetMinutes));
        Assert.Equal(expected, actual);
    }
}
