using System.Collections.ObjectModel;

namespace HoloWin.Core;

/// <summary>
/// A persisted profile. Profiles contain only feature vectors and classifier
/// parameters — never raw audio. The schema version is checked on load.
/// </summary>
public sealed class HoloProfile
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Default";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? DeskDescription { get; set; }
    public string? SurfaceMaterial { get; set; }
    public string? DeviceDescription { get; set; }
    public string? MicrophoneDeviceId { get; set; }
    public string? CameraDeviceId { get; set; }
    public SensingStrategy Strategy { get; set; } = SensingStrategy.Passive;
    public IReadOnlyList<LabeledTap> PositiveExamples { get; set; } = Array.Empty<LabeledTap>();
    public IReadOnlyList<LabeledTap> NegativeExamples { get; set; } = Array.Empty<LabeledTap>();
    public double MinimumConfidence { get; set; } = ClassifierDefaults.MinimumConfidence;
    public IReadOnlyDictionary<DeskZone, ZoneActionMapping> ActionMappings { get; set; } = new Dictionary<DeskZone, ZoneActionMapping>();
    public IReadOnlyList<string> EnabledTriggers { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> GestureActions { get; set; } = new();
    public IReadOnlyList<FingerGesture> FingerGestures { get; set; } = Array.Empty<FingerGesture>();
    public IReadOnlyList<LightRule> LightRules { get; set; } = Array.Empty<LightRule>();
    public IReadOnlyList<PresenceRule> PresenceRules { get; set; } = Array.Empty<PresenceRule>();
    public double CameraMinimumConfidence { get; set; } = 0.8;
    public double SwipeDistance { get; set; } = 0.18;
    public int GestureHoldMilliseconds { get; set; } = 450;
    public double[][] DeskCorners { get; set; } = Array.Empty<double[]>();
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    public bool CalibrationIsComplete => PositiveExamples.Count >= 40
        && PositiveExamples.GroupBy(e => e.Zone).Count() == 4
        && PositiveExamples.GroupBy(e => e.Zone).All(g => g.Count() >= 10);
}

public sealed class ZoneActionMapping
{
    public required DeskZone Zone { get; init; }
    public string? ActionId { get; set; }
    public bool Enabled { get; set; } = true;
    public double ConfidenceThreshold { get; set; } = ClassifierDefaults.MinimumConfidence;
    public int CooldownMilliseconds { get; set; } = 200;
    public bool RequireConfirmation { get; set; } = false;
}

public sealed class ProfileSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string? DeskDescription { get; init; }
    public required DateTimeOffset ModifiedAt { get; init; }
    public int PositiveExampleCount { get; init; }
    public int NegativeExampleCount { get; init; }
    public bool CalibrationIsComplete { get; init; }
}
