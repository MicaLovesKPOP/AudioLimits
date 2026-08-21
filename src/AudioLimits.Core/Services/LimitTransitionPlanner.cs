namespace AudioLimits.Core.Services;

public enum LimitTransitionOrder
{
    None,
    EndpointThenConfig,
    ConfigThenEndpoint
}

public sealed record LimitTransitionPlan(
    LimitTransitionOrder Order,
    double TargetEndpointDb,
    double PreviousActualDb,
    bool RequiresSafetyMute)
{
    // Windows endpoint-volume changes and Equalizer APO configuration reloads are
    // separate asynchronous subsystems. Mathematical ordering alone cannot
    // guarantee that their audible effects reach the render path in the same
    // order. Any real gain transition therefore happens behind an endpoint mute.
    public bool RequiresTransitionMute => Order != LimitTransitionOrder.None;
}

public static class LimitTransitionPlanner
{
    public static LimitTransitionPlan Plan(
        double currentEndpointDb,
        double minEndpointDb,
        double maxEndpointDb,
        double previousAttenuationDb,
        double desiredAttenuationDb)
    {
        var previousActualDb = currentEndpointDb + previousAttenuationDb;
        var rawTargetEndpointDb = previousActualDb - desiredAttenuationDb;
        var targetEndpointDb = Math.Clamp(
            rawTargetEndpointDb,
            minEndpointDb,
            maxEndpointDb);

        var delta = desiredAttenuationDb - previousAttenuationDb;
        var order = delta switch
        {
            < -0.0001 => LimitTransitionOrder.ConfigThenEndpoint,
            > 0.0001 => LimitTransitionOrder.EndpointThenConfig,
            _ => LimitTransitionOrder.None
        };

        var requiresSafetyMute =
            order == LimitTransitionOrder.EndpointThenConfig &&
            rawTargetEndpointDb < minEndpointDb - 0.0001;

        return new LimitTransitionPlan(
            order,
            targetEndpointDb,
            previousActualDb,
            requiresSafetyMute);
    }
}
