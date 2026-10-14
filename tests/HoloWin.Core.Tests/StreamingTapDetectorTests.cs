using HoloWin.Core;
using Xunit;

namespace HoloWin.Core.Tests;

public class StreamingTapDetectorTests
{
    private static float[][] MakeMonoBuffer(int frames, double amplitude, double noiseFloor)
    {
        var rng = new Random(42);
        var buf = new float[1][];
        buf[0] = new float[frames];
        for (int i = 0; i < frames; i++)
            buf[0][i] = (float)(noiseFloor * (rng.NextDouble() * 2 - 1));
        return buf;
    }

    private static float[][] MakeImpulseBuffer(int frames, int peakAt, double amplitude)
    {
        var buf = new float[1][];
        buf[0] = new float[frames];
        buf[0][peakAt] = (float)amplitude;
        // Add a tiny decay so the detector's impulse criteria pass.
        for (int i = 1; i < 32 && peakAt + i < frames; i++)
            buf[0][peakAt + i] = (float)(amplitude * Math.Exp(-i / 4.0));
        return buf;
    }

    [Fact]
    public void Process_DuringWarmup_ProducesNoEvents()
    {
        var det = new StreamingTapDetector(16000, 1, warmUpDuration: 0.5);
        var buf = MakeMonoBuffer(1024, 0.001, 0.000_5);
        var events = det.Process(buf);
        Assert.Empty(events);
    }

    [Fact]
    public void Process_StrongImpulseAfterWarmup_EmitsEvent()
    {
        var det = new StreamingTapDetector(16000, 1, warmUpDuration: 0.05);
        // Feed warmup
        for (int i = 0; i < 10; i++) det.Process(MakeMonoBuffer(1024, 0.001, 0.000_5));
        // Feed impulse
        var events = new List<DetectedTap>();
        for (int i = 0; i < 50; i++)
        {
            var buf = MakeImpulseBuffer(2048, 100, 0.5);
            events.AddRange(det.Process(buf));
        }
        Assert.NotEmpty(events);
    }

    [Fact]
    public void Process_NoiseOnlyAfterWarmup_ProducesNoEvents()
    {
        var det = new StreamingTapDetector(16000, 1, warmUpDuration: 0.05);
        for (int i = 0; i < 10; i++) det.Process(MakeMonoBuffer(1024, 0.001, 0.000_5));
        var events = new List<DetectedTap>();
        for (int i = 0; i < 50; i++)
            events.AddRange(det.Process(MakeMonoBuffer(1024, 0.000_5, 0.000_5)));
        Assert.Empty(events);
    }

    [Fact]
    public void Reset_RestoresWarmup()
    {
        var det = new StreamingTapDetector(16000, 1, warmUpDuration: 0.05);
        for (int i = 0; i < 10; i++) det.Process(MakeMonoBuffer(1024, 0.001, 0.000_5));
        det.Reset();
        // Right after reset we should be back in warmup; no events.
        var events = det.Process(MakeImpulseBuffer(1024, 100, 0.5));
        Assert.Empty(events);
    }
}
