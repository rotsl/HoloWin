namespace HoloWin.Core;

/// <summary>
/// Rejects obviously sustained acoustic events after the streaming onset
/// detector has captured a complete candidate. Conservative gate: ambiguous
/// short sounds continue to the profile classifier; speech-like events with
/// energy that remains high across most of the window are discarded.
/// </summary>
public static class ImpactEventGate
{
    public sealed record Metrics(
        double OnsetContrast,
        double EffectiveDurationSeconds,
        double EarlyEnergyFraction,
        double LateToImpactRMS);

    public static bool Accepts(DetectedTap ev, double sampleRate) =>
        MetricsFor(ev, sampleRate) is { } m && Accepts(m);

    public static bool Accepts(Metrics m)
    {
        if (m.OnsetContrast < 1.8) return false;
        var clearlySustained = m.EffectiveDurationSeconds >= 0.040
                            && m.LateToImpactRMS >= 0.36
                            && m.EarlyEnergyFraction < 0.60;
        return !clearlySustained;
    }

    public static Metrics? MetricsFor(DetectedTap ev, double sampleRate)
    {
        if (sampleRate <= 0 || ev.Channels.Length == 0 || ev.Channels[0].Length == 0)
            return null;

        int frameCount = ev.Channels.Min(c => c.Length);
        if (frameCount <= 0) return null;
        var usable = ev.Channels.Where(c => c.Length > 0).ToArray();
        if (usable.Length == 0) return null;
        var channelScale = 1.0 / usable.Length;

        var mono = new double[frameCount];
        for (int ch = 0; ch < usable.Length; ch++)
        {
            var src = usable[ch];
            for (int i = 0; i < frameCount; i++)
                mono[i] += src[i] * channelScale;
        }

        var filtered = LowPass(mono, sampleRate);
        var onset = Math.Clamp(ev.OnsetOffset, 0, filtered.Length - 1);
        var frameSamples = Math.Max((int)(sampleRate * 0.005), 32);

        var frameRMS = new List<double>();
        var frameLengths = new List<int>();
        int offset = onset;
        while (offset < filtered.Length)
        {
            int end = Math.Min(offset + frameSamples, filtered.Length);
            frameRMS.Add(Rms(filtered.AsSpan(offset, end - offset)));
            frameLengths.Add(end - offset);
            offset = end;
        }
        if (frameRMS.Count == 0) return null;

        int impactSearchFrames = Math.Min(frameRMS.Count,
            Math.Max((int)Math.Ceiling(0.025 * sampleRate / frameSamples), 1));
        double impactRMS = 0;
        for (int i = 0; i < impactSearchFrames; i++) impactRMS = Math.Max(impactRMS, frameRMS[i]);
        if (impactRMS <= 0) return null;

        int preStart = Math.Max(0, onset - (int)(sampleRate * 0.012));
        int preLen   = onset - preStart;
        double preRMS = preLen <= 0 ? 0 : Rms(filtered.AsSpan(preStart, preLen));
        var referenceFloor = Math.Max(Math.Max(preRMS, ev.NoiseFloorRMS), 0.000_001);
        int impactSearchEnd = Math.Min(onset + impactSearchFrames * frameSamples, filtered.Length);
        double impactPeak = 0;
        for (int i = onset; i < impactSearchEnd; i++)
            impactPeak = Math.Max(impactPeak, Math.Abs(filtered[i]));
        var onsetContrast = Math.Max(impactRMS / referenceFloor, 0.45 * impactPeak / referenceFloor);

        var effectiveThreshold = Math.Max(impactRMS * 0.40, ev.NoiseFloorRMS * 2.2);
        int effectiveSamples = 0;
        for (int i = 0; i < frameRMS.Count; i++)
            effectiveSamples += frameRMS[i] >= effectiveThreshold ? frameLengths[i] : 0;
        var effectiveDuration = (double)effectiveSamples / sampleRate;

        int earlyEnd = Math.Min(onset + (int)(sampleRate * 0.025), filtered.Length);
        double totalEnergy = Energy(filtered.AsSpan(onset, filtered.Length - onset));
        int earlyLen = earlyEnd - onset;
        double earlyEnergy = earlyLen <= 0 ? 0 : Energy(filtered.AsSpan(onset, earlyLen));
        var earlyFraction = earlyEnergy / Math.Max(totalEnergy, 1e-15);

        int lateStart = Math.Min(onset + (int)(sampleRate * 0.040), filtered.Length);
        int lateLen   = filtered.Length - lateStart;
        double lateRMS = lateLen <= 0 ? 0 : Rms(filtered.AsSpan(lateStart, lateLen));

        return new Metrics(onsetContrast, effectiveDuration, earlyFraction, lateRMS / Math.Max(impactRMS, 1e-12));
    }

    private static double[] LowPass(double[] values, double sampleRate)
    {
        var cutoff = Math.Min(6000.0, sampleRate * 0.20);
        var alpha = 1 - Math.Exp(-2 * Math.PI * cutoff / sampleRate);
        var states = new double[4];
        var result = new double[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var filtered = values[i];
            for (int s = 0; s < states.Length; s++)
            {
                states[s] += alpha * (filtered - states[s]);
                filtered = states[s];
            }
            result[i] = filtered;
        }
        return result;
    }

    private static double Rms(ReadOnlySpan<double> values)
    {
        if (values.Length == 0) return 0;
        return Math.Sqrt(Energy(values) / values.Length);
    }

    private static double Energy(ReadOnlySpan<double> values)
    {
        double sum = 0;
        for (int i = 0; i < values.Length; i++) sum += values[i] * values[i];
        return sum;
    }
}
