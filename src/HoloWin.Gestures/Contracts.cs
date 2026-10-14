using HoloWin.Core;

namespace HoloWin.Gestures;

/// <summary>
/// A 2D normalized landmark. Normalized means coordinates are in [0,1] relative
/// to the image. Z is depth, relative to the wrist.
/// </summary>
public readonly record struct Landmark(double X, double Y, double Z);

public enum GestureClass
{
    Unknown,
    OpenPalm,
    ClosedFist,
    Pinch,
    Pointing,
    SwipeLeft,
    SwipeRight,
    SwipeUp,
    SwipeDown,
    HandRaise,
    TwoFinger,
    Hold,
    Custom
}

public static class GestureClassExtensions
{
    public static string DisplayName(this GestureClass g) => g switch
    {
        GestureClass.Unknown    => "Unknown",
        GestureClass.OpenPalm    => "Open palm",
        GestureClass.ClosedFist  => "Closed fist",
        GestureClass.Pinch       => "Pinch",
        GestureClass.Pointing    => "Pointing",
        GestureClass.SwipeLeft   => "Swipe left",
        GestureClass.SwipeRight  => "Swipe right",
        GestureClass.SwipeUp     => "Swipe up",
        GestureClass.SwipeDown   => "Swipe down",
        GestureClass.HandRaise   => "Hand raise",
        GestureClass.TwoFinger   => "Two-finger",
        GestureClass.Hold        => "Hold",
        GestureClass.Custom      => "Custom",
        _ => g.ToString()
    };
}

public sealed record GestureResult(
    GestureClass Gesture,
    double Confidence,
    string? Handedness,
    IReadOnlyList<Landmark>? Landmarks);

public interface IHandTracker
{
    /// <summary>
    /// Process a single camera frame and return the recognized hands. Must be
    /// non-blocking; a slow inference must not stall other sensor modules.
    /// </summary>
    Task<IReadOnlyList<HandTrackingResult>> TrackAsync(Frame frame, CancellationToken ct);
}

public sealed record HandTrackingResult(
    string Handedness,
    double Confidence,
    Landmark[] Landmarks,
    DateTime CapturedAtUtc);

/// <summary>
/// A camera frame in raw RGB format. Owned by the caller; tracking
/// implementations must copy any data they retain.
/// </summary>
public sealed class Frame
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Stride { get; init; }
    public required byte[] RgbPixels { get; init; }
    public required MonotonicTimestamp Timestamp { get; init; }
    public string? SourceId { get; init; }
}

public interface IGestureRecognizer
{
    /// <summary>
    /// Recognize a gesture from a sequence of recent hand tracking results.
    /// Avoids one-frame decisions: temporal validation, confidence thresholds,
    /// and cooldowns are applied internally.
    /// </summary>
    GestureResult Recognize(IReadOnlyList<HandTrackingResult> recentHands);
}
