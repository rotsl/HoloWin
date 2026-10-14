using HoloWin.Core;
using HoloWin.Gestures;
using Xunit;

namespace HoloWin.Vision.Tests;

public class LandmarkGestureRecognizerTests
{
    private static Landmark[] LandmarksForPalm() => Enumerable.Range(0, 21)
        .Select(_ => new Landmark(0.5, 0.5, 0))
        .ToArray();

    private static Landmark[] LandmarksForFist() => Enumerable.Range(0, 21)
        .Select(_ => new Landmark(0.5, 0.5, 0))
        .ToArray();

    [Fact]
    public void Recognize_NoHands_ReturnsUnknown()
    {
        var rec = new LandmarkGestureRecognizer();
        var r = rec.Recognize(Array.Empty<HandTrackingResult>());
        Assert.Equal(GestureClass.Unknown, r.Gesture);
    }

    [Fact]
    public void Recognize_AllFingersExtended_ReturnsOpenPalmAfterValidation()
    {
        var rec = new LandmarkGestureRecognizer(requiredFrames: 1, cooldownFrames: 0);
        var landmarks = new Landmark[21];
        for (int i = 0; i < 21; i++) landmarks[i] = new Landmark(0, 0, 0);
        // Wrist at origin
        landmarks[0] = new Landmark(0.5, 0.9, 0);
        // Index extended up
        landmarks[5] = new Landmark(0.45, 0.7, 0);  // MCP
        landmarks[8] = new Landmark(0.45, 0.3, 0);  // tip — far from wrist
        landmarks[9]  = new Landmark(0.50, 0.7, 0);
        landmarks[12] = new Landmark(0.50, 0.3, 0);
        landmarks[13] = new Landmark(0.55, 0.7, 0);
        landmarks[16] = new Landmark(0.55, 0.3, 0);
        landmarks[17] = new Landmark(0.60, 0.7, 0);
        landmarks[20] = new Landmark(0.60, 0.3, 0);
        var hand = new HandTrackingResult("Right", 0.9, landmarks, DateTime.UtcNow);
        var r = rec.Recognize(new[] { hand });
        Assert.Equal(GestureClass.OpenPalm, r.Gesture);
    }
}
