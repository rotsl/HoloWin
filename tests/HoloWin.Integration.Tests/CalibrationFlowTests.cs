using HoloWin.Core;
using HoloWin.Calibration;
using HoloWin.Persistence;
using Xunit;

namespace HoloWin.Integration.Tests;

public class CalibrationFlowTests
{
    private static TapFeatureVector MakeVector(DeskZone zone, int seed)
    {
        var zOffset = (int)zone * 2.0;
        var values = Enumerable.Range(0, 8).Select(i => (Math.Sin(seed + i + zOffset) + 1.0) * 0.5).ToArray();
        var names = new[] { "f0", "f1", "f2", "f3", "f4", "f5", "f6", "f7" };
        return new TapFeatureVector
        {
            Strategy = SensingStrategy.Passive,
            Names = names,
            Values = values,
            Quality = new SignalQuality
            {
                SignalToNoiseDB = 25, PeakAmplitude = 0.5, RmsAmplitude = 0.1, ClippingFraction = 0,
                NoiseFloorRMS = 0.001, DurationMilliseconds = 90
            },
            CapturedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public void GuidedCapture_AcceptsTenTapsPerZone()
    {
        var session = new GuidedCaptureSession { ProfileId = Guid.NewGuid(), Strategy = SensingStrategy.Passive };
        foreach (var zone in DeskZoneExtensions.All)
        {
            Assert.True(session.ArmZone(zone));
            for (int i = 0; i < 10; i++)
            {
                var feat = MakeVector(zone, i);
                Assert.True(session.AcceptSample(feat, out var reason));
                Assert.Null(reason);
            }
        }
        Assert.Equal(GuidedCaptureSession.SessionState.Complete, session.State);
    }

    [Fact]
    public void GuidedCapture_RejectsClipped()
    {
        var session = new GuidedCaptureSession { ProfileId = Guid.NewGuid(), Strategy = SensingStrategy.Passive };
        session.ArmZone(DeskZone.LeftRear);
        var clipped = MakeVector(DeskZone.LeftRear, 0) with
        {
            Quality = new SignalQuality { SignalToNoiseDB = 25, PeakAmplitude = 0.5, RmsAmplitude = 0.1, ClippingFraction = 0.5, NoiseFloorRMS = 0.001, DurationMilliseconds = 90 }
        };
        Assert.False(session.AcceptSample(clipped, out var reason));
        Assert.Equal(RejectionReason.ClippedSignal, reason);
    }

    [Fact]
    public async Task ProfileRepository_RoundTrip_PersistsAndLoads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "holowin-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new JsonProfileRepository(dir);
            var p = new HoloProfile { Name = "Test", DeskDescription = "Wood" };
            await repo.SaveAsync(p, default);
            var loaded = await repo.LoadAsync(p.Id, default);
            Assert.NotNull(loaded);
            Assert.Equal("Test", loaded!.Name);
            await repo.SetCurrentAsync(p.Id, default);
            var cur = await repo.GetCurrentAsync(default);
            Assert.NotNull(cur);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EvaluationSession_CanRecordAndReport()
    {
        var session = new EvaluationSession { ProfileId = Guid.NewGuid(), Strategy = SensingStrategy.Passive };
        session.Arm(DeskZone.LeftRear);
        for (int i = 0; i < 15; i++)
        {
            session.RecordTrial(new ClassificationDecision
            {
                Zone = DeskZone.LeftRear,
                Confidence = 0.8,
                SignalStrength = 0.7,
                ZoneDistances = Array.Empty<double>(),
                RejectionReason = null,
                ProcessingLatencyMilliseconds = 12
            });
        }
        var report = session.ToReport();
        Assert.Equal(15, report.TotalTaps);
        Assert.Equal(15, report.CorrectTaps);
        Assert.True(report.OverallAccuracy > 0.99);
    }
}
