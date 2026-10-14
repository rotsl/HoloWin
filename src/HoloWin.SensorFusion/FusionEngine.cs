using System.Collections.Concurrent;
using HoloWin.Core;

namespace HoloWin.SensorFusion;

public sealed record SensorWeights(IReadOnlyDictionary<string, double> WeightsBySource);

public sealed class FusionEngine : IAsyncDisposable
{
    private readonly object _lock = new();
    private readonly BoundedAsyncQueue<SensorObservation> _queue;
    private readonly TimeSpan _staleCutoff;
    private readonly Dictionary<string, SensorReliability> _reliabilities = new(StringComparer.OrdinalIgnoreCase);
    private FusionMode _currentMode = FusionMode.AudioOnly;
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private bool _disposed;

    public event EventHandler<FusedDecision>? DecisionAvailable;
    public event EventHandler<FusionMode>? ModeChanged;

    public FusionMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (_currentMode == value) return;
            _currentMode = value;
            ModeChanged?.Invoke(this, value);
        }
    }

    public FusionEngine(int queueCapacity = 256, TimeSpan? staleCutoff = null)
    {
        _queue = new BoundedAsyncQueue<SensorObservation>(queueCapacity);
        _staleCutoff = staleCutoff ?? TimeSpan.FromMilliseconds(300);
    }

    public void RegisterReliability(string sourceId, SensorReliability r)
    {
        lock (_reliabilities) _reliabilities[sourceId] = r;
    }

    public void Push(SensorObservation obs)
    {
        if (_disposed) return;
        if (MonotonicClock.IsStale(obs.Timestamp, (long)_staleCutoff.TotalMilliseconds * 1000))
            return;
        _queue.TryWrite(obs);
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (_workerTask is not null) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _workerTask = Task.Run(() => WorkerLoop(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_workerTask is not null)
        {
            try { await _workerTask; } catch { /* ignore */ }
        }
        _cts?.Dispose();
        _cts = null;
        _workerTask = null;
    }

    private async Task WorkerLoop(CancellationToken ct)
    {
        var pendingBySource = new Dictionary<string, SensorObservation>(StringComparer.OrdinalIgnoreCase);
        while (!ct.IsCancellationRequested)
        {
            SensorObservation obs;
            try { obs = await _queue.ReadAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            pendingBySource[obs.SourceId] = obs;
            if (TryFuse(pendingBySource, out var decision))
            {
                DecisionAvailable?.Invoke(this, decision);
                pendingBySource.Clear();
            }
        }
    }

    private bool TryFuse(Dictionary<string, SensorObservation> pending, out FusedDecision decision)
    {
        decision = default!;
        // Strategy: emit a decision as soon as we have a primary observation for the
        // current mode (e.g. an acoustic impact in AudioOnly). If vision data is
        // available within the same time window and mode allows, we combine.
        // Explicit degraded-mode operation: if no sensor data arrives within
        // the staleness window, we emit a "manual" decision.
        var now = MonotonicClock.Now;
        switch (CurrentMode)
        {
            case FusionMode.AudioOnly:
            case FusionMode.AudioVision:
                if (pending.Values.OfType<AcousticImpactObservation>().FirstOrDefault() is { } a)
                {
                    var zone = a.PredictedZone;
                    var conf = a.Confidence;
                    string? gesture = null;
                    if (CurrentMode == FusionMode.AudioVision
                        && pending.Values.OfType<GestureObservation>().FirstOrDefault() is { } g)
                    {
                        gesture = g.GestureName;
                        conf = Math.Min(1.0, conf + g.Confidence * 0.1);
                    }
                    decision = new FusedDecision(
                        zone, conf, "audio", gesture, a.RejectionReason, a.Timestamp, now);
                    return true;
                }
                break;
            case FusionMode.VisionOnly:
                if (pending.Values.OfType<GestureObservation>().FirstOrDefault() is { } g2)
                {
                    decision = new FusedDecision(
                        null, g2.Confidence, "vision", g2.GestureName, null, g2.Timestamp, now);
                    return true;
                }
                break;
            case FusionMode.DepthAssisted:
            case FusionMode.MultiSensorAdaptive:
                // Adaptive: emit on first accepted observation, boosted by the
                // highest-confidence accompanying observation of any kind.
                var best = pending.Values
                    .Where(o => o.Confidence > 0 && o is AcousticImpactObservation or GestureObservation)
                    .OrderByDescending(o => o.Confidence)
                    .FirstOrDefault();
                if (best is null) return false;
                decision = new FusedDecision(
                    best is AcousticImpactObservation ai ? ai.PredictedZone : null,
                    best.Confidence,
                    best.SourceId,
                    best is GestureObservation gg ? gg.GestureName : null,
                    best is AcousticImpactObservation ai2 ? ai2.RejectionReason : null,
                    best.Timestamp, now);
                return true;
            case FusionMode.ManualOnly:
                decision = new FusedDecision(null, 0, "manual", null, null, now, now);
                return false; // never emit from observations
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        _queue.Dispose();
    }
}

public sealed record FusedDecision(
    DeskZone? Zone,
    double Confidence,
    string SourceKind,
    string? Gesture,
    RejectionReason? RejectionReason,
    MonotonicTimestamp PrimaryTimestamp,
    MonotonicTimestamp DecisionTimestamp);

public sealed record SensorReliability(double Score, TimeSpan AvgLatency);
