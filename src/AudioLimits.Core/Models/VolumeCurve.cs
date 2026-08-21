namespace AudioLimits.Core.Models;

public sealed class VolumeCurve
{
    public const int ExpectedPointCount = 101;
    private readonly double[] _dbByPercent;

    public IReadOnlyList<double> DbByPercent => _dbByPercent;

    public VolumeCurve(IEnumerable<double> dbByPercent)
    {
        _dbByPercent = dbByPercent.ToArray();
        if (!IsValid(_dbByPercent))
            throw new ArgumentException("The volume curve must contain 101 monotonic dB samples.", nameof(dbByPercent));
    }

    public double DbAtPercent(double percent)
    {
        percent = Math.Clamp(percent, 0.0, 100.0);
        var lower = (int)Math.Floor(percent);
        var upper = (int)Math.Ceiling(percent);
        if (lower == upper)
            return _dbByPercent[lower];

        var t = percent - lower;
        return _dbByPercent[lower] + ((_dbByPercent[upper] - _dbByPercent[lower]) * t);
    }

    public double PercentAtDb(double db)
    {
        if (db <= _dbByPercent[0])
            return 0.0;
        if (db >= _dbByPercent[^1])
            return 100.0;

        var lo = 0;
        var hi = _dbByPercent.Length - 1;
        while (lo + 1 < hi)
        {
            var mid = (lo + hi) / 2;
            if (_dbByPercent[mid] < db)
                lo = mid;
            else
                hi = mid;
        }

        var lowerDb = _dbByPercent[lo];
        var upperDb = _dbByPercent[hi];
        if (Math.Abs(upperDb - lowerDb) < 0.000001)
            return hi;

        var t = (db - lowerDb) / (upperDb - lowerDb);
        return lo + t;
    }

    public static bool IsValid(IReadOnlyList<double>? values)
    {
        if (values is null || values.Count != ExpectedPointCount)
            return false;

        for (var i = 0; i < values.Count; i++)
        {
            if (double.IsNaN(values[i]) || double.IsInfinity(values[i]))
                return false;
            if (i > 0 && values[i] + 0.0001 < values[i - 1])
                return false;
        }

        return true;
    }
}
