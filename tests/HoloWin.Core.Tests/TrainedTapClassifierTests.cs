using HoloWin.Core;
using Xunit;

namespace HoloWin.Core.Tests;

public class TrainedTapClassifierTests
{
    private static TapFeatureVector MakeVector(SensingStrategy s, double seed)
    {
        var values = Enumerable.Range(0, 8).Select(i => (Math.Sin(seed + i) + 1.0) * 0.5).ToArray();
        var names = new[] { "f0", "f1", "f2", "f3", "f4", "f5", "f6", "f7" };
        return new TapFeatureVector
        {
            Strategy = s,
            Names = names,
            Values = values,
            Quality = new SignalQuality
            {
                SignalToNoiseDB = 25,
                PeakAmplitude = 0.5,
                RmsAmplitude = 0.1,
                ClippingFraction = 0,
                NoiseFloorRMS = 0.001,
                DurationMilliseconds = 90
            },
            CapturedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public void Train_WithOnlyOneZone_Throws()
    {
        var positives = Enumerable.Range(0, 5)
            .Select(i => new LabeledTap { Zone = DeskZone.LeftRear, Feature = MakeVector(SensingStrategy.Passive, i) })
            .ToList();
        Assert.Throws<ClassifierTrainingException>(() =>
            TrainedTapClassifier.Train(positives));
    }

    [Fact]
    public void Train_WithTwoZonesAndPredict_AcceptsCloseExample()
    {
        var lr = Enumerable.Range(0, 10)
            .Select(i => new LabeledTap { Zone = DeskZone.LeftRear, Feature = MakeVector(SensingStrategy.Passive, i * 0.01) });
        var rf = Enumerable.Range(0, 10)
            .Select(i => new LabeledTap { Zone = DeskZone.RightFront, Feature = MakeVector(SensingStrategy.Passive, i * 0.01 + 2.0) });
        var all = lr.Concat(rf).ToList();
        var clf = TrainedTapClassifier.Train(all);
        // A feature very close to the LR cluster should classify as LR.
        var probe = MakeVector(SensingStrategy.Passive, 0.001);
        var decision = clf.Predict(probe);
        Assert.True(decision.WasAccepted);
        Assert.Equal(DeskZone.LeftRear, decision.Zone);
    }

    [Fact]
    public void Train_WithClippedFeature_RejectsAsClipped()
    {
        var lr = Enumerable.Range(0, 10)
            .Select(i => new LabeledTap { Zone = DeskZone.LeftRear, Feature = MakeVector(SensingStrategy.Passive, i * 0.01) });
        var rf = Enumerable.Range(0, 10)
            .Select(i => new LabeledTap { Zone = DeskZone.RightFront, Feature = MakeVector(SensingStrategy.Passive, i * 0.01 + 2.0) });
        var all = lr.Concat(rf).ToList();
        var clf = TrainedTapClassifier.Train(all);
        var clipped = MakeVector(SensingStrategy.Passive, 0.001) with
        {
            Quality = new SignalQuality
            {
                SignalToNoiseDB = 25,
                PeakAmplitude = 0.5,
                RmsAmplitude = 0.1,
                ClippingFraction = 0.5,
                NoiseFloorRMS = 0.001,
                DurationMilliseconds = 90
            }
        };
        var decision = clf.Predict(clipped);
        Assert.Equal(RejectionReason.ClippedSignal, decision.RejectionReason);
    }

    [Fact]
    public void Train_WithWeakSignal_RejectsAsWeak()
    {
        var lr = Enumerable.Range(0, 10)
            .Select(i => new LabeledTap { Zone = DeskZone.LeftRear, Feature = MakeVector(SensingStrategy.Passive, i * 0.01) });
        var rf = Enumerable.Range(0, 10)
            .Select(i => new LabeledTap { Zone = DeskZone.RightFront, Feature = MakeVector(SensingStrategy.Passive, i * 0.01 + 2.0) });
        var all = lr.Concat(rf).ToList();
        var clf = TrainedTapClassifier.Train(all);
        var weak = MakeVector(SensingStrategy.Passive, 0.001) with
        {
            Quality = new SignalQuality
            {
                SignalToNoiseDB = 25,
                PeakAmplitude = 0.0001,
                RmsAmplitude = 0.00001,
                ClippingFraction = 0,
                NoiseFloorRMS = 0.001,
                DurationMilliseconds = 90
            }
        };
        var decision = clf.Predict(weak);
        Assert.Equal(RejectionReason.WeakSignal, decision.RejectionReason);
    }

    [Fact]
    public void Train_WithSchemaMismatch_RejectsAsSchemaMismatch()
    {
        var lr = Enumerable.Range(0, 10)
            .Select(i => new LabeledTap { Zone = DeskZone.LeftRear, Feature = MakeVector(SensingStrategy.Passive, i * 0.01) });
        var rf = Enumerable.Range(0, 10)
            .Select(i => new LabeledTap { Zone = DeskZone.RightFront, Feature = MakeVector(SensingStrategy.Passive, i * 0.01 + 2.0) });
        var all = lr.Concat(rf).ToList();
        var clf = TrainedTapClassifier.Train(all);
        // Active strategy with same names → schema mismatch
        var mismatched = new TapFeatureVector
        {
            Strategy = SensingStrategy.Active,
            Names = clf.FeatureNames,
            Values = new double[clf.FeatureNames.Length],
            Quality = new SignalQuality { SignalToNoiseDB = 25, PeakAmplitude = 0.5, RmsAmplitude = 0.1, ClippingFraction = 0, NoiseFloorRMS = 0.001, DurationMilliseconds = 90 },
            CapturedAt = DateTimeOffset.UtcNow
        };
        var decision = clf.Predict(mismatched);
        Assert.Equal(RejectionReason.SchemaMismatch, decision.RejectionReason);
    }
}
