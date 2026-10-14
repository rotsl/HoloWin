using System.Globalization;

namespace HoloWin.Core;

/// <summary>
/// Produces a tap feature vector combining temporal, spectral,
/// log-mel/MFCC-style cepstral, broad-band and interchannel spatial features.
/// </summary>
public sealed class TapFeatureExtractor
{
    public double SampleRate { get; }
    public SensingStrategy Strategy { get; }

    public TapFeatureExtractor(double sampleRate, SensingStrategy strategy)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        SampleRate = sampleRate;
        Strategy = strategy;
    }

    public TapFeatureVector Extract(DetectedTap ev) => MakeFeature(ev, DateTimeOffset.UtcNow);
    public TapAnalysis Analyze(DetectedTap ev) =>
        new(MakeFeature(ev, DateTimeOffset.UtcNow), SpectrumBandsFromEvent(ev, 16));

    public IReadOnlyList<SpectrumBand> SpectrumBandsFromEvent(DetectedTap ev, int count = 16)
    {
        var mono = MixDown(ev.Channels).Select(Convert.ToDouble).ToArray();
        var spectrum = Radix2Fft.PowerSpectrum(mono, Math.Min(Radix2Fft.NextPowerOfTwo(mono.Length), 4096));
        return SpectrumBands(spectrum, count);
    }

    public IReadOnlyList<SpectrumBand> SpectrumBandsFromSpectrum(double[] spectrum, int count = 16) =>
        SpectrumBands(spectrum, count);

    private TapFeatureVector MakeFeature(DetectedTap ev, DateTimeOffset capturedAt)
    {
        var prepared = Prepare(ev);
        (var names, var values) = Strategy switch
        {
            SensingStrategy.Passive => PassiveFeatures(prepared.Mono, prepared.Channels, ev.OnsetOffset, prepared.Spectrum),
            SensingStrategy.Active  => ActiveProbe.ResponseFeatures(prepared.Mono, SampleRate, prepared.Spectrum),
            SensingStrategy.Hybrid =>
                Combine(
                    PassiveFeatures(prepared.Mono, prepared.Channels, ev.OnsetOffset, prepared.Spectrum),
                    ActiveProbe.ResponseFeatures(prepared.Mono, SampleRate, prepared.Spectrum)
                ),
            _ => (Array.Empty<string>(), Array.Empty<double>())
        };

        var rms  = RootMeanSquare(prepared.Mono);
        var peak = prepared.Mono.Length == 0 ? 0 : prepared.Mono.Max(Math.Abs);
        var clipping = (double)prepared.Mono.Count(x => Math.Abs(x) >= 0.995) /
                       Math.Max(prepared.Mono.Length, 1);
        var snr = 20 * Math.Log10(Math.Max(rms, 1e-12) / Math.Max(ev.NoiseFloorRMS, 1e-12));
        var quality = new SignalQuality
        {
            SignalToNoiseDB = snr,
            PeakAmplitude = peak,
            RmsAmplitude = rms,
            ClippingFraction = clipping,
            NoiseFloorRMS = ev.NoiseFloorRMS,
            DurationMilliseconds = prepared.Mono.Length / SampleRate * 1000.0
        };

        var safeValues = values.Select(v => double.IsFinite(v) ? v : 0).ToArray();

        return new TapFeatureVector
        {
            Strategy = Strategy,
            Names = names,
            Values = safeValues,
            Quality = quality,
            CapturedAt = capturedAt
        };
    }

    private (float[][] Channels, double[] Mono, double[] Spectrum) Prepare(DetectedTap ev)
    {
        var channels = ev.Channels.Where(c => c.Length > 0).ToArray();
        var monoFloat = MixDown(channels);
        var mono = monoFloat.Select(Convert.ToDouble).ToArray();
        var spectrum = Radix2Fft.PowerSpectrum(mono, Math.Min(Radix2Fft.NextPowerOfTwo(mono.Length), 4096));
        return (channels, mono, spectrum);
    }

    private static (string[] names, double[] values) Combine(
        (string[] names, double[] values) a,
        (string[] names, double[] values) b)
    {
        var names = new string[a.names.Length + b.names.Length];
        Array.Copy(a.names, names, a.names.Length);
        Array.Copy(b.names, 0, names, a.names.Length, b.names.Length);
        var values = new double[a.values.Length + b.values.Length];
        Array.Copy(a.values, values, a.values.Length);
        Array.Copy(b.values, 0, values, a.values.Length, b.values.Length);
        return (names, values);
    }

    private (string[] names, double[] values) PassiveFeatures(double[] signal, float[][] channels, int onset, double[] spectrum)
    {
        if (signal.Length == 0)
            return (PassiveFeatureNames, Enumerable.Repeat(0.0, PassiveFeatureNames.Length).ToArray());

        var rms = RootMeanSquare(signal);
        var peak = signal.Length == 0 ? 0 : signal.Max(Math.Abs);
        var crest = peak / Math.Max(rms, 1e-12);
        int zeroCrossings = 0;
        for (int i = 1; i < signal.Length; i++)
        {
            if ((signal[i - 1] >= 0) != (signal[i] >= 0)) zeroCrossings++;
        }
        var zcr = (double)zeroCrossings / Math.Max(signal.Length - 1, 1);
        int peakIndex = onset;
        double peakValue = -1;
        for (int i = 0; i < signal.Length; i++)
        {
            var abs = Math.Abs(signal[i]);
            if (abs > peakValue)
            {
                peakValue = abs;
                peakIndex = i;
            }
        }
        var attackPosition = (double)Math.Max(peakIndex - onset, 0) / Math.Max(signal.Length - onset, 1);

        // Energies for temporal centroid
        double totalEnergy = 1e-15;
        var energies = new double[signal.Length];
        for (int i = 0; i < signal.Length; i++)
        {
            energies[i] = signal[i] * signal[i];
            totalEnergy += energies[i];
        }
        double temporalCentroidRaw = 0;
        for (int i = 0; i < signal.Length; i++)
            temporalCentroidRaw += (double)i * energies[i];
        var temporalCentroid = temporalCentroidRaw / totalEnergy / Math.Max(signal.Length - 1, 1);

        int split = Math.Min(Math.Max(onset + (int)(SampleRate * 0.025), 1), signal.Length - 1);
        double early = 0, late = 0;
        for (int i = 0; i < split; i++) early += energies[i];
        for (int i = split; i < signal.Length; i++) late += energies[i];
        var earlyLateRatio = Math.Log10((early + 1e-12) / (late + 1e-12));

        var spectral = SpectralFeatures(spectrum);
        var mel      = MelCepstralFeatures(spectrum, 18, 8);
        var bands    = BroadBandFeatures(spectrum, 10);
        var spatial  = SpatialFeatures(channels);

        var temporal = new double[]
        {
            Math.Log10(rms + 1e-12), crest, zcr, attackPosition, temporalCentroid, earlyLateRatio
        };
        var values = new double[temporal.Length + spectral.Length + mel.Length + bands.Length + spatial.Length];
        int idx = 0;
        Array.Copy(temporal, 0, values, idx, temporal.Length); idx += temporal.Length;
        Array.Copy(spectral, 0, values, idx, spectral.Length); idx += spectral.Length;
        Array.Copy(mel, 0, values, idx, mel.Length); idx += mel.Length;
        Array.Copy(bands, 0, values, idx, bands.Length); idx += bands.Length;
        Array.Copy(spatial, 0, values, idx, spatial.Length);
        return (PassiveFeatureNames, values);
    }

    private double[] SpectralFeatures(double[] spectrum)
    {
        var usable = new List<(int Offset, double Value)>(spectrum.Length);
        int lastIdx = spectrum.Length - 1;
        for (int i = 0; i < spectrum.Length; i++)
        {
            var freq = (double)i * SampleRate / Math.Max((spectrum.Length - 1) * 2, 1);
            if (freq >= 60 && freq <= Math.Min(18000, SampleRate * 0.48))
                usable.Add((i, spectrum[i]));
        }
        var total = usable.Sum(u => u.Value) + 1e-15;
        double centroidRaw = 0;
        double flatSum = 0;
        foreach (var (i, v) in usable)
        {
            var freq = (double)i * SampleRate / Math.Max((spectrum.Length - 1) * 2, 1);
            centroidRaw += freq * v;
            flatSum += Math.Log(v + 1e-15);
        }
        var centroid = centroidRaw / total;
        double bandwidthRaw = 0;
        foreach (var (i, v) in usable)
        {
            var freq = (double)i * SampleRate / Math.Max((spectrum.Length - 1) * 2, 1);
            bandwidthRaw += (freq - centroid) * (freq - centroid) * v;
        }
        var bandwidth = Math.Sqrt(bandwidthRaw / total);

        double cumulative = 0;
        double rolloff = 0;
        foreach (var (i, v) in usable)
        {
            cumulative += v;
            if (cumulative >= total * 0.85)
            {
                rolloff = (double)i * SampleRate / Math.Max((spectrum.Length - 1) * 2, 1);
                break;
            }
        }
        var arithmetic = total / Math.Max(usable.Count, 1);
        var geometric = Math.Exp(flatSum / Math.Max(usable.Count, 1));
        var flatness = geometric / Math.Max(arithmetic, 1e-15);
        return new[] { centroid / SampleRate, bandwidth / SampleRate, rolloff / SampleRate, flatness };
    }

    private double[] MelCepstralFeatures(double[] spectrum, int filterCount, int coefficientCount)
    {
        var minMel = HzToMel(80);
        var maxMel = HzToMel(Math.Min(16000, SampleRate * 0.46));
        var points = new double[filterCount + 2];
        var bins   = new int[filterCount + 2];
        for (int i = 0; i < filterCount + 2; i++)
        {
            var mel = minMel + (maxMel - minMel) * i / (filterCount + 1);
            var hz  = MelToHz(mel);
            points[i] = hz;
            bins[i]   = FrequencyBin(hz, spectrum.Length);
        }
        var logEnergies = new double[filterCount];
        for (int f = 0; f < filterCount; f++)
        {
            var left   = bins[f];
            var center = Math.Max(bins[f + 1], left + 1);
            var right  = Math.Max(bins[f + 2], center + 1);
            double energy = 0;
            if (left < spectrum.Length)
            {
                int lo = left;
                int hi = Math.Min(center, spectrum.Length);
                for (int i = lo; i < hi; i++)
                    energy += spectrum[i] * (double)(i - lo) / Math.Max(center - left, 1);
                int co = center;
                int ro = Math.Min(right, spectrum.Length);
                for (int i = co; i < ro; i++)
                    energy += spectrum[i] * (double)(right - i) / Math.Max(right - center, 1);
            }
            logEnergies[f] = Math.Log(energy + 1e-15);
        }
        var result = new double[coefficientCount];
        for (int c = 1; c <= coefficientCount; c++)
        {
            double sum = 0;
            for (int m = 0; m < filterCount; m++)
            {
                sum += logEnergies[m] * Math.Cos(Math.PI * c * (m + 0.5) / filterCount);
            }
            result[c - 1] = sum / filterCount;
        }
        return result;
    }

    private double[] BroadBandFeatures(double[] spectrum, int count)
    {
        var minimum = 80.0;
        var maximum = Math.Min(SampleRate * 0.46, 18000);
        var total = spectrum.Sum() + 1e-15;
        var result = new double[count];
        for (int band = 0; band < count; band++)
        {
            var low  = minimum * Math.Pow(maximum / minimum, (double)band / count);
            var high = minimum * Math.Pow(maximum / minimum, (double)(band + 1) / count);
            var start = FrequencyBin(low, spectrum.Length);
            var end   = Math.Max(start, FrequencyBin(high, spectrum.Length));
            double energy = 0;
            for (int i = start; i <= Math.Min(end, spectrum.Length - 1); i++) energy += spectrum[i];
            result[band] = Math.Log10(energy / total + 1e-12);
        }
        return result;
    }

    private double[] SpatialFeatures(float[][] channels)
    {
        if (channels.Length < 2 || channels[0].Length == 0 || channels[1].Length == 0)
            return new double[] { 0, 0 };
        var first  = channels[0].Select(Convert.ToDouble).ToArray();
        var second = channels[1].Select(Convert.ToDouble).ToArray();
        var firstRMS  = RootMeanSquare(first);
        var secondRMS = RootMeanSquare(second);
        var energyDelta = Math.Log10((firstRMS + 1e-12) / (secondRMS + 1e-12));
        var maximumLag = Math.Min((int)(SampleRate * 0.00075), 36);
        int bestLag = 0;
        double bestCorrelation = double.NegativeInfinity;
        for (int lag = -maximumLag; lag <= maximumLag; lag++)
        {
            double correlation = 0;
            int count = 0;
            for (int i = 0; i < first.Length; i++)
            {
                int si = i + lag;
                if (si >= 0 && si < second.Length)
                {
                    correlation += first[i] * second[si];
                    count++;
                }
            }
            if (count > 0 && correlation > bestCorrelation)
            {
                bestCorrelation = correlation;
                bestLag = lag;
            }
        }
        return new double[] { energyDelta, (double)bestLag / Math.Max(maximumLag, 1) };
    }

    private static float[] MixDown(float[][] channels)
    {
        if (channels.Length == 0) return Array.Empty<float>();
        if (channels.Length == 1) return channels[0];
        int count = channels.Min(c => c.Length);
        var result = new float[count];
        var scale = 1f / channels.Length;
        for (int ch = 0; ch < channels.Length; ch++)
            for (int i = 0; i < count; i++)
                result[i] += channels[ch][i] * scale;
        return result;
    }

    private static double RootMeanSquare(double[] values)
    {
        if (values.Length == 0) return 0;
        double sum = 0;
        for (int i = 0; i < values.Length; i++) sum += values[i] * values[i];
        return Math.Sqrt(sum / values.Length);
    }

    private int FrequencyBin(double frequency, int spectrumCount)
    {
        var fftSize = Math.Max((spectrumCount - 1) * 2, 1);
        return Math.Min(Math.Max((int)(frequency / SampleRate * fftSize), 0), spectrumCount - 1);
    }

    private static double HzToMel(double hz) => 2595 * Math.Log10(1 + hz / 700);
    private static double MelToHz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);

    private IReadOnlyList<SpectrumBand> SpectrumBands(double[] spectrum, int count)
    {
        var minimum = 80.0;
        var maximum = Math.Min(SampleRate * 0.46, 18000);
        var result = new SpectrumBand[count];
        for (int band = 0; band < count; band++)
        {
            var low  = minimum * Math.Pow(maximum / minimum, (double)band     / count);
            var high = minimum * Math.Pow(maximum / minimum, (double)(band+1) / count);
            var center = Math.Sqrt(low * high);
            var start = FrequencyBin(low, spectrum.Length);
            var end   = Math.Max(start, FrequencyBin(high, spectrum.Length));
            double energy = 0;
            int hi = Math.Min(end, spectrum.Length - 1);
            for (int i = start; i <= hi; i++) energy += spectrum[i];
            var level = 10 * Math.Log10(energy / Math.Max(end - start + 1, 1) + 1e-15);
            result[band] = new SpectrumBand(center, level);
        }
        return result;
    }

    public static readonly string[] PassiveFeatureNames =
    {
        "log_rms", "crest_factor", "zero_crossing_rate", "attack_position",
        "temporal_centroid", "early_late_ratio",
        "spectral_centroid", "spectral_bandwidth", "spectral_rolloff_85", "spectral_flatness",
        "mfcc_1", "mfcc_2", "mfcc_3", "mfcc_4", "mfcc_5", "mfcc_6", "mfcc_7", "mfcc_8",
        "band_0", "band_1", "band_2", "band_3", "band_4",
        "band_5", "band_6", "band_7", "band_8", "band_9",
        "channel_energy_delta", "interchannel_delay"
    };
}
