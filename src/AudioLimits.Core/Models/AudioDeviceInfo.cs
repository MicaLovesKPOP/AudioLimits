namespace AudioLimits.Core.Models;

public sealed record AudioDeviceInfo(
    string Id,
    string EndpointGuid,
    string FriendlyName,
    float CurrentScalar,
    float CurrentDb,
    float MinDb,
    float MaxDb,
    bool Muted,
    bool SupportsHardwareVolume);

public sealed record AudioDeviceSnapshot(
    string Id,
    string EndpointGuid,
    string FriendlyName,
    float CurrentScalar,
    float CurrentDb,
    float MinDb,
    float MaxDb,
    bool Muted,
    bool SupportsHardwareVolume);

public sealed record AudioVolumeState(float Scalar, bool Muted);
public sealed record AudioEndpointDiagnostics(
    string Id,
    string EndpointGuid,
    string FriendlyName,
    float CurrentScalar,
    float CurrentDb,
    float MinDb,
    float MaxDb,
    float IncrementDb,
    bool Muted,
    string HardwareSupport,
    uint Step,
    uint StepCount);

