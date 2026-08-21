using AudioLimits.Core.Models;

namespace AudioLimits.Core.Tests;

public sealed class ReadOnlyProjectionTests
{
    [Fact]
    public void StoredLimitCurve_CanComputeUncappedEquivalentWithoutMutation()
    {
        var curve = new VolumeCurve(Enumerable.Range(0, 101).Select(x => -100.0 + x));
        var endpointDb = curve.DbAtPercent(60);
        var attenuation = -15.0;
        var equivalent = curve.PercentAtDb(endpointDb + attenuation);

        Assert.Equal(45.0, equivalent, 6);
    }
}
