namespace HoloWin.Core;

/// <summary>
/// Active acoustic probe. Generates a low-amplitude ultrasonic chirp and
/// computes correlation features against a captured tap signal.
/// Active sensing is opt-in
/// only; the chirp can be audible on some hardware and is never assumed to
/// outperform passive sensing.
/// </summary>
public static class ActiveProbe
{
    public const double DefaultStartFrequency = 15500;
    public const double DefaultEndFrequency   = 21000;
    public const double DefaultDuration       = 0.024;
    public const double DefaultAmplitude      = 0.035;

    public static float[] Chirp(
        double sampleRate,
        double duration = DefaultDuration,
        double startFrequency = DefaultStartFrequency,
        double endFrequency   = DefaultEndFrequency,
        double amplitude = DefaultAmplitude)
    {
        var count = Math.Max((int)(sampleRate * duration), 32);
        var result = new float[count];
        var slope = (endFrequency - startFrequency) / duration;
        for (int i = 0; i < count; i++)
        {
            var t = (double)i / sampleRate;
            var phase = 2 * Math.PI * (startFrequency * t + 0.5 * slope * t * t);
            var edgeUp   = (double)i / Math.Max(count / 8, 1);
            var edgeDown = (double)(count - 1 - i) / Math.Max(count / 8, 1);
            var env = Math.Min(Math.Min(edgeUp, edgeDown), 1);
            if (env < 0) env = 0;
            result[i] = (float)(Math.Sin(phase) * amplitude * env);
        }
        return result;
    }

    public static (string[] names, double[] values) ResponseFeatures(double[] signal, double sampleRate, double[]? spectrum = null)
    {
        var probe = Chirp(sampleRate).Select(Convert.ToDouble).ToArray();
        if (signal.Length < probe.Length)
            return (ActiveFeatureNames, Enumerable.Repeat(0.0, ActiveFeatureNames.Length).ToArray());

        var stride = Math.Max(probe.Length / 4, 1);
        var correlations = new List<double>(signal.Length / stride + 1);
        var probeEnergy = Math.Sqrt(probe.Sum(p => p * p)) + 1e-12;
        int index = 0;
        while (index + probe.Length <= signal.Length)
        {
            double dot = 0;
            double energy = 0;
            for (int k = 0; k < probe.Length; k++)
            {
                var s = signal[index + k];
                dot    += s * probe[k];
                energy += s * s;
            }
            correlations.Add(dot / (probeEnergy * Math.Sqrt(energy) + 1e-12));
            index += stride;
        }

        var peak = correlations.Count == 0 ? 0 : correlations.Max(c => Math.Abs(c));
        int peakIndex = 0;
        if (correlations.Count > 0)
        {
            double pk = double.NegativeInfinity;
            for (int i = 0; i < correlations.Count; i++)
            {
                var abs = Math.Abs(correlations[i]);
                if (abs > pk) { pk = abs; peakIndex = i; }
            }
        }
        var mean    = correlations.Count == 0 ? 0 : correlations.Average();
        var variance= correlations.Count == 0 ? 0 : correlations.Average(m => (m - mean) * (m - mean));
        var responseSpectrum = spectrum ?? Radix2Fft.PowerSpectrum(signal, Math.Min(Radix2Fft.NextPowerOfTwo(signal.Length), 4096));
        var responseBands = NormalizedLogBands(responseSpectrum, sampleRate, 8);
        var values = new double[4 + responseBands.Length];
        values[0] = peak;
        values[1] = (double)peakIndex / Math.Max(correlations.Count - 1, 1);
        values[2] = mean;
        values[3] = Math.Sqrt(variance);
        Array.Copy(responseBands, 0, values, 4, responseBands.Length);
        return (ActiveFeatureNames, values);
    }

    public static readonly string[] ActiveFeatureNames =
    {
        "probe_correlation_peak", "probe_correlation_lag",
        "probe_correlation_mean", "probe_correlation_spread",
        "probe_band_0", "probe_band_1", "probe_band_2", "probe_band_3",
        "probe_band_4", "probe_band_5", "probe_band_6", "probe_band_7"
    };

    private static double[] NormalizedLogBands(double[] spectrum, double sampleRate, int count)
    {
        var minimum = 8000.0;
        var maximum = Math.Min(sampleRate * 0.48, 22000);
        var total = spectrum.Sum() + 1e-15;
        var result = new double[count];
        for (int band = 0; band < count; band++)
        {
            var lowRatio  = (double)band / count;
            var highRatio = (double)(band + 1) / count;
            var low  = minimum * Math.Pow(maximum / minimum, lowRatio);
            var high = minimum * Math.Pow(maximum / minimum, highRatio);
            var start = Math.Max((int)(low  / sampleRate * (spectrum.Length - 1) * 2), 0);
            var end   = Math.Min((int)(high / sampleRate * (spectrum.Length - 1) * 2), spectrum.Length - 1);
            if (end < start) { result[band] = -30; continue; }
            double energy = 0;
            for (int i = start; i <= end; i++) energy += spectrum[i];
            result[band] = Math.Log10(energy / total + 1e-12);
        }
        return result;
    }
}
