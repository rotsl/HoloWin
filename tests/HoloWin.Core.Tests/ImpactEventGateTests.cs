using HoloWin.Core;
using Xunit;

namespace HoloWin.Core.Tests;

public class ImpactEventGateTests
{
    private static DetectedTap MakeTap(float[][] channels, int onsetOffset, double noiseFloor = 0.0005) =>
        new(channels, onsetOffset, 0, noiseFloor);

    private static float[] MakeShortImpulse(int length, int peakAt, double amp = 0.5)
    {
        var s = new float[length];
        s[peakAt] = (float)amp;
        for (int i = 1; i < 8 && peakAt + i < length; i++)
            s[peakAt + i] = (float)(amp * Math.Exp(-i / 2.0));
        return s;
    }

    private static float[] MakeSustained(int length, double amp)
    {
        var s = new float[length];
        for (int i = 0; i < length; i++) s[i] = (float)amp;
        return s;
    }

    [Fact]
    public void Accepts_ShortImpulse_ReturnsTrue()
    {
        var signal = MakeShortImpulse(1500, 100, 0.5);
        var ev = MakeTap(new[] { signal }, onsetOffset: 100);
        Assert.True(ImpactEventGate.Accepts(ev, 16000));
    }

    [Fact]
    public void Accepts_SustainedSpeech_ReturnsFalse()
    {
        var signal = MakeSustained(1500, 0.3);
        var ev = MakeTap(new[] { signal }, onsetOffset: 0);
        Assert.False(ImpactEventGate.Accepts(ev, 16000));
    }

    [Fact]
    public void Metrics_HaveExpectedShape()
    {
        var signal = MakeShortImpulse(1500, 100);
        var ev = MakeTap(new[] { signal }, onsetOffset: 100);
        var m = ImpactEventGate.MetricsFor(ev, 16000);
        Assert.NotNull(m);
        Assert.True(m!.OnsetContrast > 0);
        Assert.True(m.EarlyEnergyFraction >= 0 && m.EarlyEnergyFraction <= 1);
    }
}
