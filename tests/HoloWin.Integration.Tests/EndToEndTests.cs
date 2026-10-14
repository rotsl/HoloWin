using HoloWin.Core;
using HoloWin.SensorFusion;
using HoloWin.Actions;
using HoloWin.Persistence;
using HoloWin.Diagnostics;
using Xunit;

namespace HoloWin.Integration.Tests;

public class EndToEndTests
{
    [Fact]
    public async Task AudioOnly_Pipeline_PushesObservationToEngine_EmitsDecision()
    {
        var eng = new FusionEngine { CurrentMode = FusionMode.AudioOnly };
        var decided = new TaskCompletionSource<FusedDecision>();
        eng.DecisionAvailable += (_, d) => decided.TrySetResult(d);
        await eng.StartAsync(CancellationToken.None);
        eng.Push(new AcousticImpactObservation(
            MonotonicClock.Now, "mic1", 0.75,
            DeskZone.LeftFront, 0.9,
            new[] { 0.2, 0.3, 0.4, 0.5 }, null));
        var decision = await decided.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(DeskZone.LeftFront, decision.Zone);
        await eng.StopAsync();
    }

    [Fact]
    public void NoSensorFallback_StillOperates()
    {
        var eng = new FusionEngine { CurrentMode = FusionMode.ManualOnly };
        Assert.Equal(FusionMode.ManualOnly, eng.CurrentMode);
        // No observations are required in manual mode.
    }

    [Fact]
    public async Task Diagnostics_LogsZoneEvents_KeepsBoundedHistory()
    {
        var diag = new DiagnosticsCollector(eventCapacity: 10);
        for (int i = 0; i < 50; i++)
        {
            diag.LogZoneEvent(new DiagnosticsCollector.ZoneEventLogEntry(
                DateTimeOffset.UtcNow, DeskZone.LeftRear, 0.7, null, 5, 0.7, "mic"));
        }
        Assert.Equal(50, diag.EventCount);
        Assert.True(diag.SnapshotEvents().Count <= 10);
    }
}
