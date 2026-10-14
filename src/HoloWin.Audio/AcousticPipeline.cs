using HoloWin.Core;

namespace HoloWin.Audio;

/// <summary>
/// Ties together a streaming tap detector, feature extractor, and (optional)
/// trained classifier. Consumes raw audio buffers, emits classification
/// decisions and acoustic impact observations. Runs on a dedicated worker
/// task. All work is non-blocking; if the consumer cannot keep up, the
/// bounded queue drops oldest observations.
/// </summary>
public sealed class AcousticPipeline : IAsyncDisposable
{
    private readonly IAudioCapture _capture;
    private readonly StreamingTapDetector _detector;
    private readonly TapFeatureExtractor _extractor;
    private readonly BoundedAsyncQueue<DetectedTap> _tapsQueue;
    private readonly object _classifierLock = new();
    private TrainedTapClassifier? _classifier;
    private CancellationTokenSource? _cts;
    private Task? _consumerTask;
    private volatile bool _running;
    private long _totalBuffers;
    private long _totalTaps;
    private long _totalAccepted;
    private long _totalRejected;
    private double _inputPeak;

    public AcousticPipeline(IAudioCapture capture, SensingStrategy strategy)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _detector = new StreamingTapDetector(capture.SampleRate, capture.ChannelCount);
        _extractor = new TapFeatureExtractor(capture.SampleRate, strategy);
        _tapsQueue = new BoundedAsyncQueue<DetectedTap>(64);
        capture.BufferAvailable += OnBufferAvailable;
        capture.StatusChanged += OnCaptureStatus;
    }

    public TrainedTapClassifier? Classifier
    {
        get { lock (_classifierLock) return _classifier; }
        set { lock (_classifierLock) _classifier = value; }
    }

    public long TotalBuffers   => Interlocked.Read(ref _totalBuffers);
    public double InputPeak => Volatile.Read(ref _inputPeak);
    public long TotalTaps      => Interlocked.Read(ref _totalTaps);
    public long TotalAccepted  => Interlocked.Read(ref _totalAccepted);
    public long TotalRejected  => Interlocked.Read(ref _totalRejected);
    public long QueueDrops     => _tapsQueue.DroppedCount;
    public double NoiseFloorRMS => _detector.NoiseFloorRMS;
    public bool IsRunning => _running;

    public event EventHandler<ClassificationDecision>? DecisionAvailable;
    public event EventHandler<AcousticImpactObservation>? ObservationAvailable;
    public event EventHandler<SensorStatus>? StatusChanged;
    public event EventHandler<TapFeatureVector>? FeatureAvailable;
    public event EventHandler<DetectedTap>? TapAvailable;
    private void OnCaptureStatus(object? sender, SensorStatus status) => StatusChanged?.Invoke(this, status);

    public async Task StartAsync(CancellationToken ct)
    {
        if (_running) return;
        _running = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _consumerTask = Task.Run(() => ConsumerLoop(_cts.Token), _cts.Token);
        try
        {
            await _capture.StartAsync(ct);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, new SensorStatus(_capture.Id, false, ex.Message));
            _running = false;
            _cts.Cancel();
            throw;
        }
        StatusChanged?.Invoke(this, new SensorStatus(_capture.Id, true, null));
    }

    public async Task StopAsync()
    {
        _running = false;
        _cts?.Cancel();
        try { await _capture.StopAsync(CancellationToken.None); } catch { /* swallow */ }
        try { if (_consumerTask is not null) await _consumerTask; } catch { /* swallow */ }
        StatusChanged?.Invoke(this, new SensorStatus(_capture.Id, false, null));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _tapsQueue.Dispose();
        _capture.BufferAvailable -= OnBufferAvailable;
        _capture.StatusChanged -= OnCaptureStatus;
        await _capture.DisposeAsync();
        _cts?.Dispose();
    }

    private void OnBufferAvailable(object? sender, AudioBuffer buffer)
    {
        Interlocked.Increment(ref _totalBuffers);
        if (!_running) return;
        try
        {
            double peak = 0;
            foreach (var channel in buffer.Channels)
                foreach (var sample in channel) peak = Math.Max(peak, Math.Abs(sample));
            Volatile.Write(ref _inputPeak, peak);
            var events = _detector.Process(buffer.Channels);
            foreach (var ev in events)
            {
                Interlocked.Increment(ref _totalTaps);
                _tapsQueue.TryWrite(ev);
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, new SensorStatus(_capture.Id, false, ex.Message));
        }
    }

    private async Task ConsumerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            DetectedTap ev;
            try { ev = await _tapsQueue.ReadAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var feature = _extractor.Extract(ev);
                TapAvailable?.Invoke(this, ev);
                FeatureAvailable?.Invoke(this, feature);
                TrainedTapClassifier? classifier;
                lock (_classifierLock) classifier = _classifier;
                ClassificationDecision decision;
                if (classifier is null)
                {
                    decision = new ClassificationDecision
                    {
                        Zone = null,
                        Confidence = 0,
                        SignalStrength = feature.Quality.Score,
                        ZoneDistances = Array.Empty<double>(),
                        RejectionReason = RejectionReason.Paused,
                        Timestamp = MonotonicClock.Now
                    };
                }
                else
                {
                    decision = classifier.Predict(feature);
                }
                sw.Stop();
                decision = decision with { ProcessingLatencyMilliseconds = sw.Elapsed.TotalMilliseconds };

                if (decision.WasAccepted)
                {
                    Interlocked.Increment(ref _totalAccepted);
                    var obs = new AcousticImpactObservation(
                        decision.Timestamp ?? MonotonicClock.Now,
                        _capture.Id,
                        decision.Confidence,
                        decision.Zone,
                        decision.SignalStrength,
                        decision.ZoneDistances,
                        decision.RejectionReason
                    );
                    DecisionAvailable?.Invoke(this, decision);
                    ObservationAvailable?.Invoke(this, obs);
                }
                else
                {
                    Interlocked.Increment(ref _totalRejected);
                    DecisionAvailable?.Invoke(this, decision);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, new SensorStatus(_capture.Id, false, ex.Message));
            }
        }
    }
}
