using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoloWin.Core;

/// <summary>
/// Quality metrics about an audio analysis window.
/// <see cref="Score"/> collapses the metrics into [0,1] for use
/// as a confidence-weighting signal in fusion.
/// </summary>
public sealed record SignalQuality
{
    public const double MinimumReliablePeakAmplitude = 0.003;
    public const double MaximumReliableClippingFraction = 0.20;
    public const double MinimumClassificationSignalToNoiseDB = 6.0;

    public double SignalToNoiseDB { get; init; }
    public double PeakAmplitude { get; init; }
    public double RmsAmplitude { get; init; }
    public double ClippingFraction { get; init; }
    public double NoiseFloorRMS { get; init; }
    public double DurationMilliseconds { get; init; }

    public double Score
    {
        get
        {
            var snr = Math.Clamp((SignalToNoiseDB - 4) / 30.0, 0, 1);
            var strength = Math.Clamp((PeakAmplitude - MinimumReliablePeakAmplitude) / 0.15, 0, 1);
            var clean = 1 - Math.Min(ClippingFraction * 4, 1);
            return 0.55 * snr + 0.25 * strength + 0.20 * clean;
        }
    }

    public string Summary
    {
        get
        {
            if (ClippingFraction > MaximumReliableClippingFraction) return "Clipped";
            if (PeakAmplitude < MinimumReliablePeakAmplitude) return "Weak";
            if (SignalToNoiseDB < MinimumClassificationSignalToNoiseDB) return "Noisy";
            if (Score > 0.72) return "Excellent";
            if (Score > 0.48) return "Good";
            return "Fair";
        }
    }
}

/// <summary>
/// A normalized feature vector for a single tap, saved in profiles without raw audio.
/// </summary>
public sealed record TapFeatureVector
{
    public const int SchemaVersion = 1;

    public int Version { get; init; } = SchemaVersion;
    public SensingStrategy Strategy { get; init; }
    public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();
    public IReadOnlyList<double> Values { get; init; } = Array.Empty<double>();
    public SignalQuality Quality { get; init; } = new();
    public DateTimeOffset CapturedAt { get; init; }

    public bool SchemaMatches(TapFeatureVector other)
    {
        if (other == null) return false;
        if (Strategy != other.Strategy) return false;
        if (Names.Count != other.Names.Count) return false;
        if (Values.Count != other.Values.Count) return false;
        for (int i = 0; i < Names.Count; i++)
            if (!string.Equals(Names[i], other.Names[i], StringComparison.Ordinal))
                return false;
        return true;
    }
}

public sealed record LabeledTap
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DeskZone? Zone { get; init; }
    public string? NegativeLabel { get; init; }
    public required TapFeatureVector Feature { get; init; }
}

/// <summary>
/// Raw audio that has been captured as a candidate tap, before the impact gate
/// and feature extractor run.
/// </summary>
public sealed class DetectedTap
{
    public float[][] Channels { get; }
    public int OnsetOffset { get; }
    public long StreamSampleIndex { get; }
    public double NoiseFloorRMS { get; }

    public DetectedTap(float[][] channels, int onsetOffset, long streamSampleIndex, double noiseFloorRMS)
    {
        Channels = channels;
        OnsetOffset = onsetOffset;
        StreamSampleIndex = streamSampleIndex;
        NoiseFloorRMS = noiseFloorRMS;
    }

    public int FrameCount => Channels.Length == 0 ? 0 : Channels[0].Length;
}

public sealed record SpectrumBand(double CenterFrequency, double LevelDB);

public sealed record TapAnalysis(TapFeatureVector Feature, IReadOnlyList<SpectrumBand> Spectrum);
