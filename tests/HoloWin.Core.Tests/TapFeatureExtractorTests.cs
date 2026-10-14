using Xunit;

namespace HoloWin.Core.Tests;

public class TapFeatureExtractorTests
{
    [Fact]
    public void Extract_Hybrid_PreservesPassiveAndActiveFeatureOrder()
    {
        var samples = new float[4320];
        samples[576] = 0.5f;
        var tap = new DetectedTap(new[] { samples }, 576, 0, 0.0005);
        var passive = new TapFeatureExtractor(48000, SensingStrategy.Passive).Extract(tap);
        var active = new TapFeatureExtractor(48000, SensingStrategy.Active).Extract(tap);
        var hybrid = new TapFeatureExtractor(48000, SensingStrategy.Hybrid).Extract(tap);

        Assert.Equal(passive.Names.Concat(active.Names), hybrid.Names);
        Assert.Equal(passive.Values.Concat(active.Values), hybrid.Values);
        Assert.All(hybrid.Values, value => Assert.True(double.IsFinite(value)));
    }
}
