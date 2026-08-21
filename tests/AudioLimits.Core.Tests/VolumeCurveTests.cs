using AudioLimits.Core.Models;

namespace AudioLimits.Core.Tests;

public sealed class VolumeCurveTests
{
    [Fact]
    public void RoundTrip_LinearCurve_IsStable()
    {
        var values = Enumerable.Range(0, 101).Select(x => -100.0 + x).ToArray();
        var curve = new VolumeCurve(values);

        Assert.Equal(-90.0, curve.DbAtPercent(10), 6);
        Assert.Equal(10.0, curve.PercentAtDb(-90), 6);
        Assert.Equal(33.5, curve.PercentAtDb(-66.5), 6);
    }

    [Fact]
    public void InvalidNonMonotonicCurve_IsRejected()
    {
        var values = Enumerable.Range(0, 101).Select(x => (double)x).ToArray();
        values[50] = 10;
        Assert.False(VolumeCurve.IsValid(values));
    }
}
