using HoloWin.Core;

namespace HoloWin.Gestures;

/// <summary>
/// Geometric gesture recognizer operating directly on hand landmarks.
/// Uses simple per-frame pose heuristics combined with a temporal validation
/// window: a gesture is only emitted after N consistent consecutive frames,
/// with cooldown between emits. This is a deliberately CPU-only fallback when
/// a full ML model is unavailable; it can be replaced by an ONNX-based
/// recognizer via the plugin architecture.
/// </summary>
public sealed class LandmarkGestureRecognizer : IGestureRecognizer
{
    private readonly int _requiredFrames;
    private readonly int _cooldownFrames;
    private readonly object _lock = new();
    private GestureClass _currentCandidate = GestureClass.Unknown;
    private int _candidateCount;
    private int _cooldownRemaining;
    private GestureClass _lastEmitted = GestureClass.Unknown;

    public LandmarkGestureRecognizer(int requiredFrames = 3, int cooldownFrames = 12)
    {
        _requiredFrames = Math.Max(1, requiredFrames);
        _cooldownFrames = Math.Max(0, cooldownFrames);
    }

    public GestureResult Recognize(IReadOnlyList<HandTrackingResult> recentHands)
    {
        var latest = recentHands.OrderByDescending(h => h.Confidence).FirstOrDefault();
        var cls = latest is null || latest.Confidence < 0.8 ? GestureClass.Unknown : Classify(latest);
        var confidence = cls == GestureClass.Unknown ? 0 : Math.Min(1.0, latest!.Confidence);

        lock (_lock)
        {
            if (_cooldownRemaining > 0)
                _cooldownRemaining--;
            if (cls == _currentCandidate)
            {
                _candidateCount = Math.Min(_candidateCount + 1, _requiredFrames);
            }
            else
            {
                _currentCandidate = cls;
                _candidateCount = 1;
            }
            if (cls == GestureClass.Unknown && _candidateCount >= _requiredFrames) _lastEmitted = GestureClass.Unknown;
            if (_cooldownRemaining == 0 && _candidateCount >= _requiredFrames && cls != GestureClass.Unknown && cls != _lastEmitted)
            {
                _lastEmitted = cls;
                _cooldownRemaining = _cooldownFrames;
                return new GestureResult(cls, confidence, latest!.Handedness, latest.Landmarks);
            }
            return new GestureResult(GestureClass.Unknown, 0, latest?.Handedness, latest?.Landmarks);
        }
    }

    /// <summary>
    /// Single-frame pose classification using distance ratios that are
    /// robust to hand size and distance from the camera.
    /// </summary>
    public static GestureClass Classify(HandTrackingResult hand)
    {
        if (hand.Landmarks.Length < 21) return GestureClass.Unknown;
        var l = hand.Landmarks;

        // Landmark indices match MediaPipe Hands topology:
        // 0=wrist, 1-4=thumb, 5-8=index, 9-12=middle, 13-16=ring, 17-20=pinky
        var wrist = l[0];
        var indexTip   = l[8];
        var indexMcp   = l[5];
        var middleTip  = l[12];
        var middleMcp  = l[9];
        var ringTip    = l[16];
        var ringMcp    = l[13];
        var pinkyTip   = l[20];
        var pinkyMcp   = l[17];
        var thumbTip   = l[4];
        var thumbMcp   = l[2];

        var palmRef = Dist(wrist, middleMcp);
        if (palmRef <= 0) return GestureClass.Unknown;

        bool indexExt  = Dist(indexTip,  wrist) > Dist(indexMcp,  wrist) * 1.5;
        bool middleExt = Dist(middleTip, wrist) > Dist(middleMcp, wrist) * 1.5;
        bool ringExt   = Dist(ringTip,   wrist) > Dist(ringMcp,   wrist) * 1.5;
        bool pinkyExt  = Dist(pinkyTip,  wrist) > Dist(pinkyMcp,  wrist) * 1.5;

        int extendedCount = (indexExt ? 1 : 0) + (middleExt ? 1 : 0) + (ringExt ? 1 : 0) + (pinkyExt ? 1 : 0);
        var pinchDist = Dist(thumbTip, indexTip) / palmRef;

        if (pinchDist < 0.4)
            return GestureClass.Pinch;
        if (extendedCount == 0)
            return GestureClass.ClosedFist;
        if (extendedCount >= 4)
            return GestureClass.OpenPalm;
        if (indexExt && !middleExt && !ringExt && !pinkyExt)
            return GestureClass.Pointing;
        if (indexExt && middleExt && !ringExt && !pinkyExt)
            return GestureClass.TwoFinger;
        return GestureClass.Unknown;
    }

    private static double Dist(Landmark a, Landmark b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
