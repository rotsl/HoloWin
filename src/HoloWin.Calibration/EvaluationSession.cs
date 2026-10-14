using HoloWin.Core;
using HoloWin.Persistence;

namespace HoloWin.Calibration;

public sealed class EvaluationSession
{
    public enum State { Idle, Armed, Complete, Cancelled }

    public required Guid ProfileId { get; init; }
    public required SensingStrategy Strategy { get; init; }
    public State CurrentState { get; private set; } = State.Idle;
    public DeskZone? ArmedZone { get; private set; }
    public IReadOnlyList<EvaluationTrial> Trials => _trials;
    private readonly List<EvaluationTrial> _trials = new();
    private readonly object _lock = new();
    private int _targetPerZone = 15;
    public int TargetPerZone => _targetPerZone;
    public void Cancel() { lock (_lock) { CurrentState = State.Cancelled; ArmedZone = null; } }
    public void Reset() { lock (_lock) { _trials.Clear(); CurrentState = State.Idle; ArmedZone = null; } }

    public void Arm(DeskZone zone)
    {
        lock (_lock)
        {
            if (_trials.Count(t => t.ExpectedZone == zone) >= _targetPerZone)
                throw new InvalidOperationException("This zone already has fifteen trials.");
            CurrentState = State.Armed; ArmedZone = zone;
        }
    }

    public void RecordTrial(ClassificationDecision decision)
    {
        lock (_lock)
        {
            if (CurrentState != State.Armed || ArmedZone is not { } expected)
                return;
            _trials.Add(new EvaluationTrial(expected, decision.Zone, decision.Confidence,
                decision.RejectionReason, decision.Timestamp ?? MonotonicClock.Now,
                decision.ProcessingLatencyMilliseconds));
            if (DeskZoneExtensions.All.All(z => _trials.Count(t => t.ExpectedZone == z) >= _targetPerZone))
            {
                CurrentState = State.Complete;
                ArmedZone = null;
            }
            else
            {
                if (_trials.Count(t => t.ExpectedZone == expected) >= _targetPerZone)
                { CurrentState = State.Idle; ArmedZone = null; }
            }
        }
    }

    public EvaluationReport ToReport()
    {
        var totals = new Dictionary<DeskZone, int>();
        var correct = new Dictionary<DeskZone, int>();
        foreach (var z in DeskZoneExtensions.All) { totals[z] = 0; correct[z] = 0; }
        foreach (var t in _trials)
        {
            totals[t.ExpectedZone]++;
            if (t.PredictedZone == t.ExpectedZone && t.RejectionReason is null)
                correct[t.ExpectedZone]++;
        }
        var totalTaps = _trials.Count;
        var correctTaps = _trials.Count(t => t.PredictedZone == t.ExpectedZone && t.RejectionReason is null);
        var rejectedTaps = _trials.Count(t => t.RejectionReason is not null);
        var matrix = new int[4][];
        for (int i = 0; i < 4; i++) { matrix[i] = new int[4]; }
        foreach (var t in _trials)
        {
            if (t.PredictedZone is { } p)
                matrix[(int)t.ExpectedZone][(int)p]++;
        }
        var latencies = _trials.Select(t => t.LatencyMs).OrderBy(v => v).ToArray();
        var medianLatency = latencies.Length == 0 ? 0
            : (latencies[(latencies.Length - 1) / 2] + latencies[latencies.Length / 2]) / 2;
        return new EvaluationReport
        {
            ProfileId = ProfileId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Strategy = Strategy,
            TotalTaps = totalTaps,
            CorrectTaps = correctTaps,
            RejectedTaps = rejectedTaps,
            OverallAccuracy = totalTaps == 0 ? 0 : (double)correctTaps / totalTaps,
            MedianLatencyMs = medianLatency,
            PerZoneCorrect = correct,
            PerZoneTotal = totals,
            ConfusionMatrix = matrix
        };
    }
}

public sealed record EvaluationTrial(
    DeskZone ExpectedZone,
    DeskZone? PredictedZone,
    double Confidence,
    RejectionReason? RejectionReason,
    MonotonicTimestamp Timestamp,
    double LatencyMs);
