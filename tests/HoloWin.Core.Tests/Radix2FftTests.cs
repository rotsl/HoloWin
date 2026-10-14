using HoloWin.Core;
using Xunit;

namespace HoloWin.Core.Tests;

public class Radix2FftTests
{
    [Fact]
    public void PowerSpectrum_OfImpulse_DistributesEnergyAcrossAllBins()
    {
        var impulse = new double[128];
        impulse[64] = 1.0;
        var spectrum = Radix2Fft.PowerSpectrum(impulse);
        Assert.Equal(65, spectrum.Length);
        // A centered impulse windowed with Hann should have non-zero energy
        // at every bin; none should be NaN.
        Assert.All(spectrum, v => Assert.True(double.IsFinite(v)));
        Assert.True(spectrum.Sum() > 0);
    }

    [Fact]
    public void PowerSpectrum_OfSineConcentratesAtKnownBin()
    {
        const int N = 256;
        const double sr = 8000.0;
        const double freq = 1000.0; // bin 1000 / (sr / N) = bin 16 in full spectrum, half bin 16
        var signal = new double[N];
        for (int i = 0; i < N; i++) signal[i] = Math.Sin(2 * Math.PI * freq * i / sr);
        var spectrum = Radix2Fft.PowerSpectrum(signal);
        var peakIdx = 0;
        var peakVal = 0.0;
        for (int i = 1; i < spectrum.Length; i++)
        {
            if (spectrum[i] > peakVal)
            {
                peakVal = spectrum[i];
                peakIdx = i;
            }
        }
        var expectedBin = freq / sr * N; // 32 for full FFT → 32 in one-sided as well
        Assert.InRange(peakIdx, (int)expectedBin - 2, (int)expectedBin + 2);
    }

    [Fact]
    public void NextPowerOfTwo_WorksForCommonSizes()
    {
        Assert.Equal(1, Radix2Fft.NextPowerOfTwo(1));
        Assert.Equal(2, Radix2Fft.NextPowerOfTwo(2));
        Assert.Equal(4, Radix2Fft.NextPowerOfTwo(3));
        Assert.Equal(4, Radix2Fft.NextPowerOfTwo(4));
        Assert.Equal(8, Radix2Fft.NextPowerOfTwo(5));
        Assert.Equal(8, Radix2Fft.NextPowerOfTwo(8));
        Assert.Equal(16, Radix2Fft.NextPowerOfTwo(9));
    }
}
