namespace HoloWin.Core;

// === Device / capability contracts =====================================================

public enum DeviceClass
{
    AudioInput, AudioOutput, Camera, Depth, Imu, Ambient, Touch, Hid, Bluetooth, Serial, Other
}

public enum DeviceState
{
    Available,      // Detected and ready
    Active,         // Currently in use
    PermissionRequired,
    Unsupported,
    Disconnected,
    Error
}

public sealed record DeviceCapability
{
    public required string Id { get; init; }
    public required DeviceClass Class { get; init; }
    public required string FriendlyName { get; init; }
    public string Manufacturer { get; init; } = string.Empty;
    public string? HardwareId { get; init; }
    public DeviceState State { get; init; } = DeviceState.Available;
    public string? StateDetail { get; init; }
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
    public bool IsDefault { get; init; }
}

public interface IHardwareCapabilityManager
{
    IReadOnlyList<DeviceCapability> Snapshot();
    event EventHandler<DeviceCapability>? DeviceChanged;
    Task RescanAsync(CancellationToken ct);
    Task<bool> RequestPermissionAsync(DeviceCapability device, CancellationToken ct);
    void SetPreferredDevice(DeviceClass cls, string id);
    string? GetPreferredDevice(DeviceClass cls);
}

// === Sensor pipeline contracts =========================================================

public interface ISensor : IAsyncDisposable
{
    string Id { get; }
    string DisplayName { get; init; }
    bool IsRunning { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    event EventHandler<SensorStatus>? StatusChanged;
}

public sealed record SensorStatus(string SensorId, bool Running, string? Detail);

public interface IObservationProducer
{
    /// <summary>Queues an observation onto the fusion queue. May drop oldest if full.</summary>
    void EmitObservation(SensorObservation observation);
}

public abstract record SensorObservation(
    MonotonicTimestamp Timestamp,
    string SourceId,
    double Confidence)
{
    public abstract string Kind { get; }
}

public sealed record AcousticImpactObservation(
    MonotonicTimestamp Timestamp,
    string SourceId,
    double Confidence,
    DeskZone? PredictedZone,
    double SignalStrength,
    IReadOnlyList<double> ZoneDistances,
    RejectionReason? RejectionReason
) : SensorObservation(Timestamp, SourceId, Confidence)
{
    public override string Kind => "acoustic.impact";
}

public sealed record HandLandmarkObservation(
    MonotonicTimestamp Timestamp,
    string SourceId,
    double Confidence,
    string Handedness,
    IReadOnlyList<(double X, double Y, double Z)> Landmarks
) : SensorObservation(Timestamp, SourceId, Confidence)
{
    public override string Kind => "vision.hand.landmark";
}

public sealed record GestureObservation(
    MonotonicTimestamp Timestamp,
    string SourceId,
    double Confidence,
    string GestureName
) : SensorObservation(Timestamp, SourceId, Confidence)
{
    public override string Kind => "vision.gesture";
}

public sealed record TouchObservation(
    MonotonicTimestamp Timestamp,
    string SourceId,
    double Confidence,
    double X,
    double Y
) : SensorObservation(Timestamp, SourceId, Confidence)
{
    public override string Kind => "touch";
}

public sealed record MotionObservation(
    MonotonicTimestamp Timestamp,
    string SourceId,
    double Confidence,
    double Ax, double Ay, double Az
) : SensorObservation(Timestamp, SourceId, Confidence)
{
    public override string Kind => "motion";
}

public enum FusionMode
{
    AudioOnly,
    VisionOnly,
    AudioVision,
    DepthAssisted,
    MultiSensorAdaptive,
    ManualOnly
}

public static class FusionModeExtensions
{
    public static string DisplayName(this FusionMode m) => m switch
    {
        FusionMode.AudioOnly           => "Audio only",
        FusionMode.VisionOnly          => "Vision only",
        FusionMode.AudioVision         => "Audio + Vision",
        FusionMode.DepthAssisted       => "Depth assisted",
        FusionMode.MultiSensorAdaptive => "Multi-sensor adaptive",
        FusionMode.ManualOnly          => "Manual / automation only",
        _ => m.ToString()
    };
}
