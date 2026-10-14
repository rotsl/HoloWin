using HoloWin.Core;
using HoloWin.Gestures;
using Xunit;

namespace HoloWin.Vision.Tests;

public class CameraGestureRecognizerTests
{
    private static HandTrackingResult Hand(double x, double y, string side = "Right")
    {
        var l = Enumerable.Range(0, 21).Select(_ => new Landmark(x, y, 0)).ToArray();
        l[0] = new(x, y + .1, 0);
        foreach (var i in new[] { 5, 9, 13, 17 }) { l[i] = new(x + i * .001, y, 0); l[i + 3] = new(x + i * .001, y - .15, 0); }
        l[4] = new(x - .15, y, 0); l[2] = new(x - .02, y, 0);
        return new(side, .95, l, DateTime.UtcNow);
    }
    [Theory]
    [InlineData(.2, 0, "SwipeRight")]
    [InlineData(-.2, 0, "SwipeLeft")]
    [InlineData(0, .2, "SwipeDown")]
    [InlineData(0, -.2, "SwipeUp")]
    public void DetectsDirectionalTravel(double dx, double dy, string key)
    {
        var rec = new CameraGestureRecognizer(); var p = new HoloProfile();
        Assert.Null(rec.Recognize([Hand(.5, .5)], 0, p));
        Assert.Null(rec.Recognize([Hand(.5 + dx / 2, .5 + dy / 2)], 100, p));
        Assert.Equal(key, rec.Recognize([Hand(.5 + dx, .5 + dy)], 200, p)?.Key);
        Assert.Null(rec.Recognize([Hand(.5, .5)], 300, p));
    }
    [Fact]
    public void CustomPoseRequiresDwellAndDoesNotRepeat()
    {
        var rec = new CameraGestureRecognizer();
        var g = new FingerGesture { Name = "Raised fingers", Fingers = [FingerState.Any, FingerState.Extended, FingerState.Extended, FingerState.Extended, FingerState.Extended] };
        var p = new HoloProfile { FingerGestures = [g] };
        Assert.Null(rec.Recognize([Hand(.5, .5)], 0, p));
        Assert.Null(rec.Recognize([Hand(.5, .5)], 250, p));
        Assert.Equal(g.Key, rec.Recognize([Hand(.5, .5)], 500, p)?.Key);
        Assert.Null(rec.Recognize([Hand(.5, .5)], 750, p));
        Assert.Null(rec.Recognize([], 800, p));
        Assert.Null(rec.Recognize([Hand(.5, .5)], 900, p));
        Assert.Null(rec.Recognize([Hand(.5, .5)], 1150, p));
        Assert.Equal(g.Key, rec.Recognize([Hand(.5, .5)], 1400, p)?.Key);
    }
    [Fact]
    public void LostFramesAndHandSwitchCannotBecomeSwipes()
    {
        var rec = new CameraGestureRecognizer(); var p = new HoloProfile();
        Assert.Null(rec.Recognize([Hand(.3, .5)], 0, p));
        Assert.Null(rec.Recognize([Hand(.4, .5)], 100, p));
        Assert.Null(rec.Recognize([Hand(.5, .5, "Left")], 200, p));
        Assert.Null(rec.Recognize([Hand(.7, .5, "Left")], 1000, p));
        Assert.Null(rec.Recognize([Hand(.7, .5), Hand(.3, .5)], 1100, p));
    }
}
