using AudioLimits.Core.Services;

namespace AudioLimits.Core.Tests;

public sealed class LimitTransitionPlannerTests
{
    [Fact]
    public void StrongerLimit_AppliesConfigBeforeRaisingEndpoint()
    {
        var plan = LimitTransitionPlanner.Plan(-40, -100, 0, 0, -20);
        Assert.Equal(LimitTransitionOrder.ConfigThenEndpoint, plan.Order);
        Assert.Equal(-20, plan.TargetEndpointDb, 6);
        Assert.True(plan.RequiresTransitionMute);
    }

    [Fact]
    public void WeakerLimit_LowersEndpointBeforeRelaxingConfig()
    {
        var plan = LimitTransitionPlanner.Plan(-20, -100, 0, -20, -10);
        Assert.Equal(LimitTransitionOrder.EndpointThenConfig, plan.Order);
        Assert.Equal(-30, plan.TargetEndpointDb, 6);
        Assert.True(plan.RequiresTransitionMute);
    }

    [Fact]
    public void RemovingLimit_LowersEndpointBeforeRemovingAttenuation()
    {
        var plan = LimitTransitionPlanner.Plan(0, -100, 0, -20, 0);
        Assert.Equal(LimitTransitionOrder.EndpointThenConfig, plan.Order);
        Assert.Equal(-20, plan.TargetEndpointDb, 6);
        Assert.True(plan.RequiresTransitionMute);
    }


    [Fact]
    public void UnchangedAttenuation_DoesNotRequireTransitionMute()
    {
        var plan = LimitTransitionPlanner.Plan(-20, -100, 0, -12, -12);
        Assert.Equal(LimitTransitionOrder.None, plan.Order);
        Assert.False(plan.RequiresTransitionMute);
    }

    [Fact]
    public void NewLimitBelowCurrentOutput_CapsAtEndpointMaximum()
    {
        var plan = LimitTransitionPlanner.Plan(0, -100, 0, 0, -20);
        Assert.Equal(LimitTransitionOrder.ConfigThenEndpoint, plan.Order);
        Assert.Equal(0, plan.TargetEndpointDb, 6);
        Assert.False(plan.RequiresSafetyMute);
    }

    [Fact]
    public void RemovingLimitBelowEndpointFloor_RequiresFailQuieterMute()
    {
        var plan = LimitTransitionPlanner.Plan(-100, -100, 0, -20, 0);
        Assert.Equal(LimitTransitionOrder.EndpointThenConfig, plan.Order);
        Assert.Equal(-100, plan.TargetEndpointDb, 6);
        Assert.True(plan.RequiresSafetyMute);
    }


[Theory]
[InlineData(-40, -100, 0, 0, -20)]
[InlineData(-25, -90, 0, -5, -30)]
[InlineData(-10, -80, 0, -10, -40)]
public void StrongerLimit_FirstIntermediateStateCannotBeLouder(
    double endpointDb,
    double minDb,
    double maxDb,
    double oldAttenuation,
    double newAttenuation)
{
    var plan = LimitTransitionPlanner.Plan(
        endpointDb,
        minDb,
        maxDb,
        oldAttenuation,
        newAttenuation);

    Assert.Equal(LimitTransitionOrder.ConfigThenEndpoint, plan.Order);

    var before = endpointDb + oldAttenuation;
    var afterConfigBeforeEndpoint = endpointDb + newAttenuation;
    Assert.True(afterConfigBeforeEndpoint <= before + 0.0001);
}

[Theory]
[InlineData(-20, -100, 0, -20, -10)]
[InlineData(-10, -100, 0, -30, 0)]
[InlineData(-50, -100, 0, -40, -5)]
public void WeakerLimit_EndpointFirstStateCannotBeLouder(
    double endpointDb,
    double minDb,
    double maxDb,
    double oldAttenuation,
    double newAttenuation)
{
    var plan = LimitTransitionPlanner.Plan(
        endpointDb,
        minDb,
        maxDb,
        oldAttenuation,
        newAttenuation);

    Assert.Equal(LimitTransitionOrder.EndpointThenConfig, plan.Order);

    var before = endpointDb + oldAttenuation;
    var afterEndpointBeforeConfig = plan.TargetEndpointDb + oldAttenuation;
    Assert.True(afterEndpointBeforeConfig <= before + 0.0001);
}

}
