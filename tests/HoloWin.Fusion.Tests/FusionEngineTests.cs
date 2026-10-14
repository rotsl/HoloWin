using HoloWin.Core;
using HoloWin.SensorFusion;
using Xunit;

namespace HoloWin.Fusion.Tests;

public class FusionEngineTests
{
    [Fact]
    public async Task AdaptiveMotionDoesNotReplaceTapZone()
    {
        await using var engine = new FusionEngine { CurrentMode = FusionMode.MultiSensorAdaptive };
        var result = new TaskCompletionSource<FusedDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.DecisionAvailable += (_, decision) => result.TrySetResult(decision);
        await engine.StartAsync(default);
        engine.Push(new MotionObservation(MonotonicClock.Now, "imu", 1, 0, 0, 1));
        engine.Push(new AcousticImpactObservation(MonotonicClock.Now, "mic", .7, DeskZone.RightRear, .8, [], null));
        Assert.Equal(DeskZone.RightRear, (await result.Task.WaitAsync(TimeSpan.FromSeconds(2))).Zone);
    }
    [Fact]
    public async Task Push_AudioOnlyMode_EmitsDecisionOnAcousticImpact()
    {
        var eng = new FusionEngine();
        eng.CurrentMode = FusionMode.AudioOnly;
        var decided = new TaskCompletionSource<FusedDecision>();
        eng.DecisionAvailable += (_, d) => decided.TrySetResult(d);
        await eng.StartAsync(CancellationToken.None);
        eng.Push(new AcousticImpactObservation(
            MonotonicClock.Now, "mic", 0.85,
            DeskZone.LeftRear, 0.9,
            new[] { 1.0, 2.0, 3.0, 4.0 }, null));
        var decision = await decided.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(DeskZone.LeftRear, decision.Zone);
        Assert.True(decision.Confidence > 0);
        await eng.StopAsync();
    }

    [Fact]
    public async Task Push_VisionOnlyMode_EmitsDecisionOnGesture()
    {
        var eng = new FusionEngine();
        eng.CurrentMode = FusionMode.VisionOnly;
        var decided = new TaskCompletionSource<FusedDecision>();
        eng.DecisionAvailable += (_, d) => decided.TrySetResult(d);
        await eng.StartAsync(CancellationToken.None);
        eng.Push(new GestureObservation(MonotonicClock.Now, "cam", 0.7, "OpenPalm"));
        var decision = await decided.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("OpenPalm", decision.Gesture);
        await eng.StopAsync();
    }

    [Fact]
    public async Task Push_StaleObservation_IsDropped()
    {
        var eng = new FusionEngine(staleCutoff: TimeSpan.FromMilliseconds(100));
        eng.CurrentMode = FusionMode.AudioOnly;
        var fired = false;
        eng.DecisionAvailable += (_, _) => fired = true;
        await eng.StartAsync(CancellationToken.None);
        var old = new MonotonicTimestamp(-1_000_000, DateTimeOffset.UtcNow.Subtract(TimeSpan.FromSeconds(1)));
        eng.Push(new AcousticImpactObservation(old, "mic", 0.9, DeskZone.LeftRear, 1.0, Array.Empty<double>(), null));
        await Task.Delay(200);
        Assert.False(fired);
        await eng.StopAsync();
    }
}
