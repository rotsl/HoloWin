using System.Runtime.Versioning;

namespace HoloWin.Core;

/// <summary>
/// The four logical desk interaction zones.
/// The numeric values are stable across persisted profiles, so do not
/// renumber them after release.
/// </summary>
[Flags]
public enum DeskZone
{
    LeftRear  = 0,
    LeftFront = 1,
    RightRear = 2,
    RightFront = 3,
}

public static class DeskZoneExtensions
{
    public static readonly IReadOnlyList<DeskZone> All = new[]
    {
        DeskZone.LeftRear, DeskZone.LeftFront, DeskZone.RightRear, DeskZone.RightFront
    };

    public static int Index(this DeskZone z) => (int)z;

    public static int Row(this DeskZone z) => (int)z % 2;     // 0 = rear, 1 = front
    public static int Column(this DeskZone z) => (int)z / 2; // 0 = left, 1 = right
    public static bool IsLeft(this DeskZone z) => (int)z < 2;

    public static string PositionName(this DeskZone z) =>
        z.Row() == 0 ? "Rear" : "Front";

    public static string ShortName(this DeskZone z) => z switch
    {
        DeskZone.LeftRear   => "LR",
        DeskZone.LeftFront  => "LF",
        DeskZone.RightRear  => "RR",
        DeskZone.RightFront => "RF",
        _ => "??"
    };

    public static string DisplayName(this DeskZone z) => z switch
    {
        DeskZone.LeftRear   => "Left Rear",
        DeskZone.LeftFront  => "Left Front",
        DeskZone.RightRear  => "Right Rear",
        DeskZone.RightFront => "Right Front",
        _ => z.ToString()
    };

    public static string Instruction(this DeskZone z)
    {
        var edge = z.Row() == 0 ? "display" : "trackpad";
        var side = z.IsLeft() ? "left" : "right";
        return $"Tap beside the device on the {side}, near the {edge} edge";
    }
}

/// <summary>
/// Passive sensing is the default and never
/// emits audio. Active emits a low-amplitude ultrasonic chirp and is opt-in.
/// Hybrid combines both feature sets.
/// </summary>
public enum SensingStrategy
{
    Passive = 0,
    Active  = 1,
    Hybrid  = 2
}

public static class SensingStrategyExtensions
{
    public static string DisplayName(this SensingStrategy s) => s switch
    {
        SensingStrategy.Passive => "Passive tap acoustics",
        SensingStrategy.Active  => "Active acoustic probe",
        SensingStrategy.Hybrid  => "Hybrid",
        _ => s.ToString()
    };

    public static string Detail(this SensingStrategy s) => s switch
    {
        SensingStrategy.Passive => "Uses only the sound and vibration produced by a tap.",
        SensingStrategy.Active  => "Measures how a quiet repeating chirp changes around a tap.",
        SensingStrategy.Hybrid  => "Combines tap acoustics with the chirp response.",
        _ => s.ToString()
    };
}

public enum RejectionReason
{
    WeakSignal,
    LowSignalToNoise,
    ClippedSignal,
    OutOfDistribution,
    AmbiguousZone,
    ResemblesNegativeExample,
    SchemaMismatch,
    Paused
}

public static class RejectionReasonExtensions
{
    public static string DisplayName(this RejectionReason r) => r switch
    {
        RejectionReason.WeakSignal               => "Signal too weak",
        RejectionReason.LowSignalToNoise         => "Background noise too high",
        RejectionReason.ClippedSignal            => "Signal clipped",
        RejectionReason.OutOfDistribution        => "Unlike calibrated taps",
        RejectionReason.AmbiguousZone            => "Zone ambiguous",
        RejectionReason.ResemblesNegativeExample => "Recognized non-desk sound",
        RejectionReason.SchemaMismatch           => "Profile is incompatible",
        RejectionReason.Paused                   => "Listening paused",
        _ => r.ToString()
    };
}
