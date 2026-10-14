using HoloWin.Core;

namespace HoloWin.Gestures;

public sealed record CameraGesture(string Key, string Name, double Confidence);

/// <summary>Time-based pose dwell and directional palm movement, independent of frame rate.</summary>
public sealed class CameraGestureRecognizer
{
    private readonly List<(double Time, double X, double Y)> _trail = new();
    private string? _hand, _candidate, _emitted;
    private double _since, _last = double.NegativeInfinity, _blockedUntil;
    public CameraGesture? Recognize(IReadOnlyList<HandTrackingResult> hands, double milliseconds, HoloProfile profile)
    {
        // Multiple hands are ambiguous: require one hand to control actions.
        var hand = hands.Count == 1 ? hands[0] : null;
        if (hand is null || !double.IsFinite(hand.Confidence) || hand.Confidence < profile.CameraMinimumConfidence || hand.Landmarks.Length != 21
            || hand.Landmarks.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)))
        { Reset(); return null; }
        if (milliseconds <= _last || milliseconds - _last > 400 || _hand != hand.Handedness) Reset();
        _hand = hand.Handedness; _last = milliseconds;
        var l = hand.Landmarks;
        var x = new[] { 0, 5, 9, 13, 17 }.Average(i => l[i].X);
        var y = new[] { 0, 5, 9, 13, 17 }.Average(i => l[i].Y);
        if (_trail.Count > 0 && Math.Abs(x - _trail[^1].X) + Math.Abs(y - _trail[^1].Y) > .3)
        { Reset(); return null; }
        _trail.RemoveAll(p => milliseconds - p.Time > 700);
        _trail.Add((milliseconds, x, y));
        var first = _trail[0]; var dx = x - first.X; var dy = y - first.Y;
        var travel = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (milliseconds < _blockedUntil) { _candidate = null; _trail.Clear(); return null; }
        if (_trail.Count >= 3 && milliseconds - first.Time >= 100 && travel >= profile.SwipeDistance
            && Math.Max(Math.Abs(dx), Math.Abs(dy)) > 1.5 * Math.Min(Math.Abs(dx), Math.Abs(dy)))
        {
            var key = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? "SwipeRight" : "SwipeLeft") : (dy > 0 ? "SwipeDown" : "SwipeUp");
            _blockedUntil = milliseconds + 800; _trail.Clear(); _candidate = null; _emitted = null;
            return new(key, key, hand.Confidence);
        }
        if (travel > .04) { _candidate = null; return null; }
        var fingers = FingerStates(hand);
        var custom = profile.FingerGestures.FirstOrDefault(g => g.Fingers.Length == 5 && g.Fingers.Any(f => f != FingerState.Any)
            && g.Fingers.Select((f, i) => f == FingerState.Any || (f == FingerState.Extended) == fingers[i]).All(v => v));
        var pose = LandmarkGestureRecognizer.Classify(hand);
        var keyPose = custom?.Key ?? (pose == GestureClass.Unknown ? null : pose.ToString());
        if (keyPose != _candidate) { _candidate = keyPose; _since = milliseconds; }
        if (milliseconds - _since < profile.GestureHoldMilliseconds) return null;
        if (keyPose is null) { _emitted = null; return null; }
        if (keyPose == _emitted) return null;
        _emitted = keyPose;
        return new(keyPose, custom?.Name ?? pose.DisplayName(), hand.Confidence);
    }
    public static bool[] FingerStates(HandTrackingResult hand)
    {
        var l = hand.Landmarks;
        double Distance(int a, int b) => Math.Sqrt(Math.Pow(l[a].X - l[b].X, 2) + Math.Pow(l[a].Y - l[b].Y, 2) + Math.Pow(l[a].Z - l[b].Z, 2));
        return new[] { Distance(4, 17) > Distance(2, 17) * 1.2,
            Distance(8, 0) > Distance(5, 0) * 1.5, Distance(12, 0) > Distance(9, 0) * 1.5,
            Distance(16, 0) > Distance(13, 0) * 1.5, Distance(20, 0) > Distance(17, 0) * 1.5 };
    }
    public void Reset() { _trail.Clear(); _candidate = null; _emitted = null; _hand = null; _last = double.NegativeInfinity; }
}
