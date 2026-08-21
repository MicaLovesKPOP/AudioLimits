namespace AudioLimits.Core.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 3;
    public List<DeviceLimit> Limits { get; set; } = new();
    public PendingLimitChange? PendingChange { get; set; }

    public DeviceLimit? Find(string endpointGuid) =>
        Limits.FirstOrDefault(x =>
            string.Equals(x.EndpointGuid, endpointGuid, StringComparison.OrdinalIgnoreCase));

    public void Upsert(DeviceLimit limit)
    {
        Remove(limit.EndpointGuid);
        Limits.Add(limit);
    }

    public void Remove(string endpointGuid) =>
        Limits.RemoveAll(x =>
            string.Equals(x.EndpointGuid, endpointGuid, StringComparison.OrdinalIgnoreCase));
}

public sealed class DeviceLimit
{
    public string EndpointGuid { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public int LimitPercent { get; set; }
    public double AttenuationDb { get; set; }
    public List<double> VolumeCurveDb { get; set; } = new();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public DeviceLimit Clone() => new()
    {
        EndpointGuid = EndpointGuid,
        FriendlyName = FriendlyName,
        LimitPercent = LimitPercent,
        AttenuationDb = AttenuationDb,
        VolumeCurveDb = new List<double>(VolumeCurveDb),
        UpdatedUtc = UpdatedUtc
    };

    public VolumeCurve? TryGetCurve() =>
        VolumeCurve.IsValid(VolumeCurveDb) ? new VolumeCurve(VolumeCurveDb) : null;
}

public enum PendingChangePhase
{
    Prepared,
    FirstExternalStepApplied,
    SecondExternalStepApplied,
    Recovering
}

public sealed class PendingLimitChange
{
    public string DeviceId { get; set; } = "";
    public string EndpointGuid { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public DeviceLimit? PreviousLimit { get; set; }
    public DeviceLimit? DesiredLimit { get; set; }
    public bool PreviousLimitWasActive { get; set; }
    public double? PreviousAppliedAttenuationDb { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public PendingChangePhase Phase { get; set; } = PendingChangePhase.Prepared;
    public int RecoveryAttempts { get; set; }
    public DateTime? LastRecoveryAttemptUtc { get; set; }
    public string? LastError { get; set; }
}
