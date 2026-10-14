using HoloWin.Core;
using HoloWin.Audio;
using HoloWin.Gestures;

namespace HoloWin.Calibration;

public sealed class GuidedCaptureSession
{
    public enum SessionState { Idle, Preparing, Listening, Disarmed, Complete, Cancelled, Error }

    public required Guid ProfileId { get; init; }
    public required SensingStrategy Strategy { get; init; }
    public SessionState State { get; private set; } = SessionState.Idle;
    public DeskZone? ActiveZone { get; private set; }
    public IReadOnlyDictionary<DeskZone, int> CountsPerZone => _counts;
    public IReadOnlyDictionary<DeskZone, int> TargetPerZone => _targets;
    public IReadOnlyList<LabeledTap> AcceptedExamples => _accepted;
    public IReadOnlyList<LabeledTap> NegativeExamples => _negative;
    public int RejectedCount => _rejected.Count;
    private readonly List<LabeledTap> _negative = new();
    public void Cancel() { lock (_lock) { State = SessionState.Cancelled; ActiveZone = null; } }
    public async Task PrepareAsync(DeskZone zone, CancellationToken ct)
    {
        lock (_lock) { State = SessionState.Preparing; ActiveZone = zone; }
        try { await Task.Delay(800, ct); ct.ThrowIfCancellationRequested(); }
        catch (OperationCanceledException) { Cancel(); throw; }
        lock (_lock) { if (State != SessionState.Preparing) return; State = SessionState.Disarmed; ArmZone(zone); }
    }
    public void AddNegative(TapFeatureVector feature, string label)
    {
        lock (_lock)
        {
            if (feature.Strategy != Strategy) throw new InvalidOperationException("Sensing strategy changed.");
            _negative.Add(new LabeledTap { Feature = feature, NegativeLabel = label });
        }
    }
    public double ValidateLeaveOneOut()
    {
        LabeledTap[] samples;
        lock (_lock) samples = _accepted.ToArray();
        if (samples.Length < 8) throw new InvalidOperationException("Collect calibration samples first.");
        int correct = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var training = samples.Where((_, index) => index != i).ToArray();
            var result = TrainedTapClassifier.Train(training, _negative).Predict(samples[i].Feature);
            if (result.WasAccepted && result.Zone == samples[i].Zone) correct++;
        }
        return (double)correct / samples.Length;
    }

    private readonly Dictionary<DeskZone, int> _counts = new();
    private readonly Dictionary<DeskZone, int> _targets = new();
    private readonly List<LabeledTap> _accepted = new();
    private readonly List<LabeledTap> _rejected = new();
    private readonly object _lock = new();

    public GuidedCaptureSession(int targetPerZone = 10)
    {
        foreach (var z in DeskZoneExtensions.All)
        {
            _counts[z] = 0;
            _targets[z] = targetPerZone;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            State = SessionState.Idle;
            ActiveZone = null;
            foreach (var z in DeskZoneExtensions.All) _counts[z] = 0;
            _accepted.Clear();
            _rejected.Clear();
            _negative.Clear();
        }
    }

    public bool ArmZone(DeskZone zone)
    {
        lock (_lock)
        {
            if (State == SessionState.Listening) return false;
            if (State is SessionState.Cancelled or SessionState.Preparing) return false;
            if (_counts[zone] >= _targets[zone]) return false;
            ActiveZone = zone;
            State = SessionState.Listening;
            return true;
        }
    }

    public void Disarm()
    {
        lock (_lock) { State = SessionState.Disarmed; ActiveZone = null; }
    }

    public bool AcceptSample(TapFeatureVector feature, out RejectionReason? reason)
    {
        lock (_lock)
        {
            reason = null;
            if (feature.Strategy != Strategy) { reason = RejectionReason.SchemaMismatch; return false; }
            if (State != SessionState.Listening || ActiveZone is not { } zone)
            {
                reason = RejectionReason.Paused;
                return false;
            }
            // Quality gate: reject clipped/weak/noisy
            if (feature.Quality.ClippingFraction > SignalQuality.MaximumReliableClippingFraction)
            {
                reason = RejectionReason.ClippedSignal;
                _rejected.Add(new LabeledTap { Zone = zone, Feature = feature });
                return false;
            }
            if (feature.Quality.PeakAmplitude < SignalQuality.MinimumReliablePeakAmplitude)
            {
                reason = RejectionReason.WeakSignal;
                _rejected.Add(new LabeledTap { Zone = zone, Feature = feature });
                return false;
            }
            if (feature.Quality.SignalToNoiseDB < SignalQuality.MinimumClassificationSignalToNoiseDB)
            {
                reason = RejectionReason.LowSignalToNoise;
                _rejected.Add(new LabeledTap { Zone = zone, Feature = feature });
                return false;
            }
            _accepted.Add(new LabeledTap { Zone = zone, Feature = feature });
            _counts[zone]++;
            if (_counts[zone] >= _targets[zone])
            {
                var next = NextUnfilledZone();
                if (next is null)
                {
                    State = SessionState.Complete;
                    ActiveZone = null;
                }
                else
                {
                    ActiveZone = next;
                    // brief disarmed state during transition
                    State = SessionState.Disarmed;
                }
            }
            return true;
        }
    }

    public void UndoLast()
    {
        lock (_lock)
        {
            if (_accepted.Count == 0) return;
            var last = _accepted[^1];
            _accepted.RemoveAt(_accepted.Count - 1);
            if (last.Zone is not null)
                _counts[last.Zone.Value] = Math.Max(0, _counts[last.Zone.Value] - 1);
            State = SessionState.Disarmed;
            ActiveZone = last.Zone;
        }
    }

    public DeskZone? NextUnfilledZone()
    {
        foreach (var z in DeskZoneExtensions.All)
            if (_counts[z] < _targets[z]) return z;
        return null;
    }
}
