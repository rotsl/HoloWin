using HoloWin.Core;
using Xunit;

namespace HoloWin.Core.Tests;

public class ActiveProbeTests
{
    [Fact]
    public void Chirp_HasExpectedLength()
    {
        var chirp = ActiveProbe.Chirp(48000, duration: 0.024);
        Assert.Equal((int)(48000 * 0.024), chirp.Length);
        Assert.All(chirp, v => Assert.True(Math.Abs(v) <= 0.035 + 1e-6));
    }

    [Fact]
    public void ResponseFeatures_ProducesExpectedCount()
    {
        var rng = new Random(1);
        var signal = Enumerable.Range(0, 5000).Select(_ => (rng.NextDouble() * 2 - 1) * 0.1).ToArray();
        var (names, values) = ActiveProbe.ResponseFeatures(signal, 48000);
        Assert.Equal(ActiveProbe.ActiveFeatureNames.Length, names.Length);
        Assert.Equal(names.Length, values.Length);
    }
}
