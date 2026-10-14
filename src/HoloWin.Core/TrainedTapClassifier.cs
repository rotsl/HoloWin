using System.Globalization;
using System.Text.Json.Serialization;

namespace HoloWin.Core;

/// <summary>
/// A compact ridge-regression zone model trained on normalized calibration
/// features. Combined with the nearest-example novelty gate inside
/// <see cref="TrainedTapClassifier"/>.
/// </summary>
public sealed class RegularizedLinearZoneModel
{
    public double[][] Coefficients { get; set; } = Array.Empty<double[]>();
}

public sealed class TrainedTapClassifier
{
    public SensingStrategy Strategy { get; init; }
    public string[] FeatureNames { get; init; } = Array.Empty<string>();
    public double[] Center { get; init; } = Array.Empty<double>();
    public double[] Scales { get; init; } = Array.Empty<double>();
    public double[] FeatureWeights { get; init; } = Array.Empty<double>();
    public LabeledTap[] PositiveExamples { get; init; } = Array.Empty<LabeledTap>();
    public LabeledTap[] NegativeExamples { get; init; } = Array.Empty<LabeledTap>();
    public double OutlierThreshold { get; init; }
    public double? PositiveNoveltyThreshold { get; init; }
    public RegularizedLinearZoneModel? LinearZoneModel { get; init; }
    public double MinimumConfidence { get; init; } = ClassifierDefaults.MinimumConfidence;

    public static TrainedTapClassifier Train(
        IReadOnlyList<LabeledTap> positiveExamples,
        IReadOnlyList<LabeledTap>? negativeExamples = null,
        double minimumConfidence = ClassifierDefaults.MinimumConfidence)
    {
        var negatives = negativeExamples ?? Array.Empty<LabeledTap>();
        var positives = positiveExamples.Where(p => p.Zone is not null).ToList();
        var grouped = positives.GroupBy(p => p.Zone!.Value).ToList();
        if (grouped.Count < 2)
            throw new ClassifierTrainingException("Examples from at least two zones are required.");
        if (!grouped.All(g => g.Count() >= 2))
            throw new ClassifierTrainingException("At least two examples per represented zone are required.");
        var first = positives[0];
        var names = first.Feature.Names;
        var strategy = first.Feature.Strategy;
        var allExamples = positives.Concat(negatives).ToList();
        if (names.Count == 0 || !allExamples.All(e => e.Feature.Names.SequenceEqual(names)
                                                   && e.Feature.Values.Count == names.Count
                                                   && e.Feature.Strategy == strategy))
            throw new ClassifierTrainingException(
                "All calibration examples must use the same sensing strategy and feature schema.");

        int dimensions = names.Count;
        var center = new double[dimensions];
        var scales = new double[dimensions];
        for (int d = 0; d < dimensions; d++)
        {
            var values = positives.Select(p => p.Feature.Values[d]).ToList();
            center[d] = Median(values);
            var deviations = values.Select(v => Math.Abs(v - center[d])).ToList();
            var robustScale = Median(deviations) * 1.4826;
            var mean = values.Average();
            var sd = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / Math.Max(values.Count - 1, 1));
            scales[d] = Math.Max(Math.Max(robustScale, sd * 0.35), 1e-6);
        }

        var normalized = positives
            .Select(p => Normalize(p.Feature.Values, center, scales))
            .ToArray();
        var weights = new double[dimensions];
        var overall = new double[dimensions];
        for (int d = 0; d < dimensions; d++)
            overall[d] = normalized.Average(n => n[d]);
        for (int d = 0; d < dimensions; d++)
        {
            double between = 0, within = 0;
            foreach (var g in grouped)
            {
                var zoneValues = positives.Where(p => p.Zone == g.Key).Select(p => normalized[positives.IndexOf(p)]).ToList();
                var zoneMean = zoneValues.Average(v => v[d]);
                between += zoneValues.Count * (zoneMean - overall[d]) * (zoneMean - overall[d]);
                within += zoneValues.Sum(v => (v[d] - zoneMean) * (v[d] - zoneMean));
            }
            var ratio = between / Math.Max(within, 1e-6);
            weights[d] = Math.Min(Math.Max(Math.Sqrt(ratio + 0.02), 0.12), 4.0);
        }
        var weightMean = weights.Average();
        for (int d = 0; d < dimensions; d++) weights[d] /= Math.Max(weightMean, 1e-9);

        var sameZoneNearest = new List<double>();
        var sameZoneNovelty = new List<double>();
        for (int i = 0; i < positives.Count; i++)
        {
            var candIdx = Enumerable.Range(0, positives.Count)
                .Where(j => j != i && positives[j].Zone == positives[i].Zone).ToList();
            if (candIdx.Count == 0) continue;
            var nearest = candIdx.Min(j => Distance(normalized[i], normalized[j], weights));
            var nearestNov = candIdx.Min(j => UnweightedDistance(normalized[i], normalized[j]));
            sameZoneNearest.Add(nearest);
            sameZoneNovelty.Add(nearestNov);
        }
        var percentile = Quantile(sameZoneNearest, 0.95);
        var threshold = Math.Max(percentile * 2.6, 0.85);
        var noveltyPercentile = Quantile(sameZoneNovelty, 0.95);
        var noveltyThreshold = Math.Max(noveltyPercentile * 2.6, 0.95);
        var linearModel = TrainLinearZoneModel(normalized, positives);

        return new TrainedTapClassifier
        {
            Strategy = strategy,
            FeatureNames = names.ToArray(),
            Center = center,
            Scales = scales,
            FeatureWeights = weights,
            PositiveExamples = positives.ToArray(),
            NegativeExamples = negatives.ToArray(),
            OutlierThreshold = threshold,
            PositiveNoveltyThreshold = noveltyThreshold,
            LinearZoneModel = linearModel,
            MinimumConfidence = minimumConfidence
        };
    }

    public ClassificationDecision Predict(TapFeatureVector feature)
    {
        if (feature.Strategy != Strategy
            || !feature.Names.SequenceEqual(FeatureNames)
            || feature.Values.Count != Center.Length)
        {
            return Rejected(feature, RejectionReason.SchemaMismatch);
        }
        if (feature.Quality.ClippingFraction > SignalQuality.MaximumReliableClippingFraction)
            return Rejected(feature, RejectionReason.ClippedSignal);
        if (feature.Quality.PeakAmplitude < SignalQuality.MinimumReliablePeakAmplitude)
            return Rejected(feature, RejectionReason.WeakSignal);
        if (feature.Quality.SignalToNoiseDB < SignalQuality.MinimumClassificationSignalToNoiseDB)
            return Rejected(feature, RejectionReason.LowSignalToNoise);

        var normalizedInput = Normalize(feature.Values, Center, Scales);
        var distances = new double[DeskZoneExtensions.All.Count];
        Array.Fill(distances, double.PositiveInfinity);
        for (int zi = 0; zi < DeskZoneExtensions.All.Count; zi++)
        {
            var zone = DeskZoneExtensions.All[zi];
            var candidates = PositiveExamples
                .Where(p => p.Zone == zone)
                .Select(p => Distance(normalizedInput, Normalize(p.Feature.Values, Center, Scales), FeatureWeights))
                .OrderBy(v => v)
                .Take(3)
                .ToArray();
            if (candidates.Length > 0)
            {
                double[] rankWeights = { 0.58, 0.28, 0.14 };
                var usedWeights = rankWeights.Take(candidates.Length).ToArray();
                var denom = usedWeights.Sum();
                distances[zi] = candidates.Zip(usedWeights).Sum(p => p.First * p.Second) / denom;
            }
        }

        var ranked = distances
            .Select((v, i) => (Index: i, Value: v))
            .Where(p => double.IsFinite(p.Value))
            .OrderBy(p => p.Value)
            .ToList();
        if (ranked.Count < 2)
            return Rejected(feature, RejectionReason.SchemaMismatch, distances);

        double best = ranked[0].Value;
        double second = ranked[1].Value;

        if (best > OutlierThreshold)
            return Rejected(feature, RejectionReason.OutOfDistribution, distances);

        var positiveNoveltyDistances = PositiveExamples
            .Select(p => UnweightedDistance(normalizedInput, Normalize(p.Feature.Values, Center, Scales)))
            .ToArray();
        var nearestPositiveNovelty = positiveNoveltyDistances.Length == 0
            ? double.PositiveInfinity
            : positiveNoveltyDistances.Min();
        if (PositiveNoveltyThreshold is { } nt && nearestPositiveNovelty > nt)
            return Rejected(feature, RejectionReason.OutOfDistribution, distances);

        if (NegativeExamples.Length > 0)
        {
            var nearestNegative = NegativeExamples
                .Select(p => UnweightedDistance(normalizedInput, Normalize(p.Feature.Values, Center, Scales)))
                .Min();
            if (nearestNegative <= nearestPositiveNovelty * 1.10)
                return Rejected(feature, RejectionReason.ResemblesNegativeExample, distances);
        }

        var linearDecision = LinearDecision(normalizedInput, nearestPositiveNovelty, distances, feature);
        if (linearDecision is not null) return linearDecision;

        var bestZone = DeskZoneExtensions.All[ranked[0].Index];
        var separation = (second - best) / Math.Max(second, 1e-9);
        var separationScore = Math.Clamp(separation / 0.38, 0, 1);
        var fitScore = Math.Clamp(1 - best / OutlierThreshold, 0, 1);
        var confidence = 0.72 * separationScore + 0.28 * fitScore;
        if (separation < ClassifierDefaults.MinimumRelativeSeparation || confidence < MinimumConfidence)
            return Rejected(feature, RejectionReason.AmbiguousZone, distances, confidence);

        return new ClassificationDecision
        {
            Zone = bestZone,
            Confidence = confidence,
            SignalStrength = feature.Quality.Score,
            ZoneDistances = distances,
            RejectionReason = null,
            Timestamp = MonotonicClock.Now
        };
    }

    private ClassificationDecision? LinearDecision(double[] normalizedInput, double nearestPositiveNovelty, double[] distances, TapFeatureVector feature)
    {
        if (LinearZoneModel is not { Coefficients: var coeffs }
            || coeffs.Length != DeskZoneExtensions.All.Count
            || !coeffs.All(c => c.Length == normalizedInput.Length + 1))
            return null;

        var representedZones = PositiveExamples.Where(p => p.Zone is not null).Select(p => p.Zone!.Value).ToHashSet();
        var input = new double[normalizedInput.Length + 1];
        input[0] = 1.0;
        Array.Copy(normalizedInput, 0, input, 1, normalizedInput.Length);

        var scores = new List<(DeskZone Zone, double Score)>();
        for (int zi = 0; zi < DeskZoneExtensions.All.Count; zi++)
        {
            var zone = DeskZoneExtensions.All[zi];
            if (!representedZones.Contains(zone)) continue;
            double score = 0;
            for (int k = 0; k < coeffs[zi].Length; k++)
                score += coeffs[zi][k] * input[k];
            if (double.IsFinite(score))
                scores.Add((zone, score));
        }
        if (scores.Count < 2) return null;
        scores.Sort((a, b) => b.Score.CompareTo(a.Score));
        var margin = scores[0].Score - scores[1].Score;
        var marginScore = Math.Clamp(margin / 0.30, 0, 1);
        var fitThreshold = PositiveNoveltyThreshold ?? Math.Max(OutlierThreshold, 1e-9);
        var fitScore = Math.Clamp(1 - nearestPositiveNovelty / fitThreshold, 0, 1);
        var confidence = 0.76 * marginScore + 0.24 * fitScore;
        if (margin < ClassifierDefaults.MinimumLinearScoreMargin || confidence < MinimumConfidence)
            return null;
        return new ClassificationDecision
        {
            Zone = scores[0].Zone,
            Confidence = confidence,
            SignalStrength = feature.Quality.Score,
            ZoneDistances = distances,
            RejectionReason = null,
            Timestamp = MonotonicClock.Now
        };
    }

    private static ClassificationDecision Rejected(
        TapFeatureVector feature, RejectionReason reason, double[]? distances = null, double confidence = 0) =>
        new()
        {
            Zone = null,
            Confidence = confidence,
            SignalStrength = feature.Quality.Score,
            ZoneDistances = distances ?? Array.Empty<double>(),
            RejectionReason = reason,
            Timestamp = MonotonicClock.Now
        };

    private static double[] Normalize(IReadOnlyList<double> values, double[] center, double[] scales)
    {
        var result = new double[values.Count];
        for (int i = 0; i < values.Count; i++)
            result[i] = (values[i] - center[i]) / Math.Max(scales[i], 1e-9);
        return result;
    }

    private static double Distance(double[] lhs, double[] rhs, double[] weights)
    {
        double sum = 0;
        double weightSum = 0;
        for (int i = 0; i < Math.Min(Math.Min(lhs.Length, rhs.Length), weights.Length); i++)
        {
            var d = lhs[i] - rhs[i];
            sum += weights[i] * d * d;
            weightSum += weights[i];
        }
        return Math.Sqrt(sum / Math.Max(weightSum, 1e-9));
    }

    private static double UnweightedDistance(double[] lhs, double[] rhs)
    {
        double sum = 0;
        int n = Math.Min(lhs.Length, rhs.Length);
        for (int i = 0; i < n; i++)
        {
            var d = lhs[i] - rhs[i];
            sum += d * d;
        }
        return Math.Sqrt(sum / Math.Max(n, 1));
    }

    private static RegularizedLinearZoneModel? TrainLinearZoneModel(double[][] normalized, List<LabeledTap> positives)
    {
        int dimension = normalized.Length > 0 ? normalized[0].Length : 0;
        if (dimension <= 0 || normalized.Length != positives.Count) return null;
        int parameterCount = dimension + 1;
        int zoneCount = DeskZoneExtensions.All.Count;
        var normal = new double[parameterCount][];
        for (int i = 0; i < parameterCount; i++) normal[i] = new double[parameterCount];
        var rhs = new double[parameterCount][];
        for (int i = 0; i < parameterCount; i++) rhs[i] = new double[zoneCount];

        for (int n = 0; n < normalized.Length; n++)
        {
            if (positives[n].Zone is not { } zone) continue;
            var row = new double[parameterCount];
            row[0] = 1.0;
            Array.Copy(normalized[n], 0, row, 1, dimension);
            int zi = (int)zone;
            for (int l = 0; l < parameterCount; l++)
            {
                rhs[l][zi] += row[l];
                for (int r = 0; r < parameterCount; r++)
                    normal[l][r] += row[l] * row[r];
            }
        }

        var regularization = Math.Max(3.0, positives.Count * 0.25);
        for (int i = 1; i < parameterCount; i++)
            normal[i][i] += regularization;

        var solution = SolvePositiveDefinite(normal, rhs);
        if (solution == null) return null;
        var coeffs = new double[zoneCount][];
        for (int z = 0; z < zoneCount; z++)
        {
            coeffs[z] = new double[parameterCount];
            for (int r = 0; r < parameterCount; r++)
                coeffs[z][r] = solution[r][z];
        }
        if (!coeffs.SelectMany(c => c).All(double.IsFinite)) return null;
        return new RegularizedLinearZoneModel { Coefficients = coeffs };
    }

    private static double[][]? SolvePositiveDefinite(double[][] matrix, double[][] rhs)
    {
        int count = matrix.Length;
        if (count == 0
            || !matrix.All(r => r.Length == count)
            || rhs.Length != count)
            return null;
        int resultCols = rhs[0].Length;
        if (!rhs.All(r => r.Length == resultCols)) return null;

        var lower = new double[count][];
        for (int i = 0; i < count; i++) lower[i] = new double[count];
        for (int r = 0; r < count; r++)
        {
            for (int c = 0; c <= r; c++)
            {
                double value = matrix[r][c];
                if (c > 0)
                {
                    for (int k = 0; k < c; k++)
                        value -= lower[r][k] * lower[c][k];
                }
                if (r == c)
                {
                    if (!double.IsFinite(value) || value <= 1e-10) return null;
                    lower[r][c] = Math.Sqrt(value);
                }
                else
                {
                    lower[r][c] = value / lower[c][c];
                }
            }
        }

        var intermediate = new double[count][];
        for (int i = 0; i < count; i++) intermediate[i] = new double[resultCols];
        for (int r = 0; r < count; r++)
        {
            for (int o = 0; o < resultCols; o++)
            {
                double value = rhs[r][o];
                if (r > 0)
                    for (int k = 0; k < r; k++)
                        value -= lower[r][k] * intermediate[k][o];
                intermediate[r][o] = value / lower[r][r];
            }
        }

        var solution = new double[count][];
        for (int i = 0; i < count; i++) solution[i] = new double[resultCols];
        for (int r = count - 1; r >= 0; r--)
        {
            for (int o = 0; o < resultCols; o++)
            {
                double value = intermediate[r][o];
                if (r + 1 < count)
                    for (int k = r + 1; k < count; k++)
                        value -= lower[k][r] * solution[k][o];
                solution[r][o] = value / lower[r][r];
            }
        }
        return solution;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count % 2 == 0)
            return (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
        return sorted[sorted.Count / 2];
    }

    private static double Quantile(IReadOnlyList<double> values, double probability)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var position = Math.Clamp(probability, 0, 1) * (sorted.Count - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        if (lower == upper) return sorted[lower];
        var frac = position - lower;
        return sorted[lower] * (1 - frac) + sorted[upper] * frac;
    }
}
