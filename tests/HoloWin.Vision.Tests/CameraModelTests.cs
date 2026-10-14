using HoloWin.Core;
using HoloWin.Gestures;
using HoloWin.Vision;
using Xunit;

namespace HoloWin.Vision.Tests;

public sealed class CameraModelTests
{
    [Fact]
    public async Task BundledModels_BlankFrame_HasNoHands()
    {
        using var tracker = new OnnxHandTracker();
        var frame = new Frame { Width = 640, Height = 480, Stride = 1920, RgbPixels = new byte[640 * 480 * 3], Timestamp = MonotonicClock.Now };
        Assert.Empty(await tracker.TrackAsync(frame, default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tracker.TrackAsync(frame, new CancellationToken(true)));
    }

    [Fact]
    public void MissingModels_FailBeforeCapture()
    {
        Assert.ThrowsAny<Exception>(() => new OnnxHandTracker(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void HeldPose_DoesNotRepeat_AndHandLossRearms()
    {
        var recognizer = new LandmarkGestureRecognizer(2, 0);
        var landmarks = Enumerable.Repeat(new Landmark(0.5, 0.9, 0), 21).ToArray();
        landmarks[9] = new Landmark(0.5, 0.7, 0);
        landmarks[4] = new Landmark(0.1, 0.9, 0); // separate thumb and index, producing a fist
        var hand = new HandTrackingResult("Right", 0.95, landmarks, DateTime.UtcNow);
        Assert.Equal(GestureClass.Unknown, recognizer.Recognize(new[] { hand }).Gesture);
        Assert.Equal(GestureClass.ClosedFist, recognizer.Recognize(new[] { hand }).Gesture);
        for (int i = 0; i < 20; i++) Assert.Equal(GestureClass.Unknown, recognizer.Recognize(new[] { hand }).Gesture);
        recognizer.Recognize(Array.Empty<HandTrackingResult>());
        recognizer.Recognize(Array.Empty<HandTrackingResult>());
        Assert.Equal(GestureClass.Unknown, recognizer.Recognize(new[] { hand }).Gesture);
        Assert.Equal(GestureClass.ClosedFist, recognizer.Recognize(new[] { hand }).Gesture);
    }

    [Fact]
    public void LowConfidenceHands_DoNotTrigger()
    {
        var recognizer = new LandmarkGestureRecognizer(1, 0);
        var landmarks = Enumerable.Repeat(new Landmark(0.5, 0.5, 0), 21).ToArray();
        landmarks[9] = new Landmark(0.5, 0.7, 0);
        var hand = new HandTrackingResult("Right", 0.1, landmarks, DateTime.UtcNow);
        Assert.Equal(GestureClass.Unknown, recognizer.Recognize(new[] { hand }).Gesture);
    }
}
