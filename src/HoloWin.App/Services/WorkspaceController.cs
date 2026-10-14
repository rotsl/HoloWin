using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using HoloWin.Actions;
using HoloWin.Audio;
using HoloWin.Automation;
using HoloWin.Calibration;
using HoloWin.Core;
using HoloWin.Gestures;
using HoloWin.Persistence;
using HoloWin.Platform.Windows;
using HoloWin.SensorFusion;
using HoloWin.Sensors;
using HoloWin.Vision;

namespace HoloWin.App.Services;

/// <summary>Owns the application session. Devices are opened only by explicit UI commands.</summary>
public sealed partial class WorkspaceController : IAsyncDisposable
{
    public ApplicationPaths Paths { get; } = new();
    public WorkspaceSettings Settings { get; private set; } = new();
    public ActionLibrary Library { get; private set; } = new();
    public JsonProfileRepository Profiles { get; }
    public JsonEvaluationReportRepository Reports { get; }
    public HardwareCapabilityManager Hardware { get; } = new(new[] { new WindowsHardwareProvider() });
    public ActionDispatcher Actions { get; } = new();
    public FusionEngine Fusion { get; } = new();
    public DeskMapCalibrator DeskMap { get; } = new();
    public HoloProfile? Profile { get; private set; }
    public GuidedCaptureSession? Calibration { get; private set; }
    public EvaluationSession? Evaluation { get; private set; }
    public string? NegativeLabel { get; set; }
    public double? CalibrationAgreement { get; private set; }
    public string Status { get; private set; } = "Stopped";
    public string SensorReading { get; private set; } = "Sensor readings off";
    public ClassificationDecision? LastDecision { get; private set; }
    public bool IsRunning => _audio?.IsRunning == true;
    public bool CameraRunning => _camera?.IsRunning == true;
    public bool MotionRunning { get; private set; }
    public double? CurrentLux { get; private set; }
    public bool HasLightSensor => _motion.HasLightSensor;
    private volatile bool _lightActionsEnabled;
    private CancellationTokenSource? _lightActionsCts;
    private readonly LightRuleEvaluator _lightRules = new();
    public bool LightActionsEnabled
    {
        get => _lightActionsEnabled;
        set
        {
            if (_lightActionsEnabled == value) return;
            _lightActionsEnabled = false;
            var previous = _lightActionsCts;
            _lightActionsCts = value ? CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token) : null;
            previous?.Cancel(); previous?.Dispose();
            _lightRules.Reset(); _lightActionsEnabled = value;
        }
    }
    public long QueueDrops => _audio?.QueueDrops ?? 0;
    public double NoiseFloor => _audio?.NoiseFloorRMS ?? 0;
    public string AudioDiagnostics => _audio is { } audio
        ? $"Microphone: {(audio.IsRunning ? "running" : "stopped")} · Buffers: {audio.TotalBuffers} · Input peak: {audio.InputPeak:P1} · Detected tap candidates: {audio.TotalTaps}"
        : "Microphone off";
    public long CameraFrameCount => _camera?.TotalFrames ?? 0;
    public Task Ready { get; }
    public event EventHandler? Changed;
    public event EventHandler<HoloWin.Gestures.Frame>? CameraFrameAvailable;
    private readonly WorkspaceRepository _store;
    private readonly ConcurrentQueue<string> _events = new();
    private readonly ConcurrentQueue<string> _logQueue = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ActiveProbeEmitter _probe = new();
    private readonly WindowsMotionMonitor _motion = new();
    private readonly Dictionary<DeskZone, long> _lastZoneDispatch = new();
    private readonly Task _logWriter;
    private AcousticPipeline? _audio;
    private OpenCvCameraCapture? _camera;
    private readonly SemaphoreSlim _cameraLifecycle = new(1, 1);
    private CancellationTokenSource? _trackingCts;
    private Task? _trackingWorker;
    private System.Threading.Channels.Channel<HoloWin.Gestures.Frame>? _trackingFrames;
    public bool TrackingRunning { get; private set; }
    public bool GestureActionsEnabled { get; set; }
    public IReadOnlyList<HandTrackingResult> CurrentHands { get; private set; } = Array.Empty<HandTrackingResult>();
    public string TrackingStatus { get; private set; } = "Tracking off";
    private CancellationTokenSource? _sessionCts;
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private bool _disposed;
    public WorkspaceController()
    {
        Profiles = new(Paths.AppDataRoot); Reports = new(Paths.AppDataRoot); _store = new(Paths.AppDataRoot);
        var macros = new MacroRunner(Actions, id => Library.Actions.FirstOrDefault(a => a.Id == id));
        Actions.RunMacroAsync = (id, ct) => macros.RunMacroAsync(Library.Macros.FirstOrDefault(m => m.Id == id)
            ?? throw new InvalidOperationException("Macro not found."), ct);
        Actions.Completed += (_, result) => Log($"Action {result.ActionId}: {(result.Succeeded ? "completed" : result.Error)}");
        Hardware.DeviceChanged += OnDeviceChanged;
        Fusion.DecisionAvailable += (_, result) => _ = DispatchDecisionAsync(result);
        _motion.ReadingAvailable += (_, value) => { SensorReading = value; Notify(); };
        _motion.LightAvailable += (_, lux) => _ = OnLightAsync(lux);
        _motion.MotionAvailable += (_, value) => Fusion.Push(value);
        _logWriter = WriteLogsAsync();
        Ready = InitializeAsync();
    }
    private async Task InitializeAsync()
    {
        try
        {
            Settings = await _store.LoadSettingsAsync(); Library = await _store.LoadActionsAsync();
            Profile = await Profiles.GetCurrentAsync(default); RestoreDeskMap();
            Fusion.CurrentMode = Settings.FusionMode;
            await Hardware.RescanAsync(_lifetime.Token);
            Hardware.StartHotPlugMonitoring(TimeSpan.FromSeconds(8));
            Log("Ready. Sensors stay off until explicitly started.");
        }
        catch (Exception ex) { Log("Startup: " + ex.Message); }
        Notify();
    }
    public string[] RecentEvents => _events.Reverse().ToArray();
    public void Log(string message)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss}  {message}";
        _events.Enqueue(line); _logQueue.Enqueue(line);
        while (_events.Count > 200) _events.TryDequeue(out _);
        while (_logQueue.Count > 1000) _logQueue.TryDequeue(out _);
        Notify();
    }
    private async Task WriteLogsAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                var batch = new List<string>();
                while (_logQueue.TryDequeue(out var line)) batch.Add(line);
                if (batch.Count == 0) continue;
                var path = System.IO.Path.Combine(Paths.LogsDir, "session.log");
                try
                {
                    if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".previous", true);
                    await File.AppendAllLinesAsync(path, batch, _lifetime.Token);
                }
                catch (IOException) { /* In-memory diagnostics remain available if the disk is unavailable. */ }
            }
        }
        catch (OperationCanceledException) { }
    }
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    public async Task SaveSettingsAsync()
    { await _store.SaveSettingsAsync(Settings); Fusion.CurrentMode = Settings.FusionMode; Notify(); }
    public async Task SaveLibraryAsync() { await _store.SaveActionsAsync(Library); Notify(); }
    public async Task SelectProfileAsync(Guid id)
    {
        PresenceActionsEnabled = false;
        LightActionsEnabled = false;
        await StopCameraAsync();
        await StopAudioAsync(); Calibration = null; Evaluation = null;
        Profile = await Profiles.LoadAsync(id, default) ?? throw new InvalidOperationException("Profile not found.");
        await Profiles.SetCurrentAsync(id, default); RestoreDeskMap(); _lastZoneDispatch.Clear(); Log($"Selected profile: {Profile.Name}");
    }
    public async Task CreateProfileAsync(string name, SensingStrategy strategy)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a profile name.");
        var profile = new HoloProfile { Name = name.Trim(), Strategy = strategy };
        profile.ActionMappings = DeskZoneExtensions.All.ToDictionary(z => z, z => new ZoneActionMapping { Zone = z });
        await Profiles.SaveAsync(profile, default); await SelectProfileAsync(profile.Id);
    }
    public async Task SaveProfileAsync()
    { await Profiles.SaveAsync(Profile ?? throw new InvalidOperationException("Select a profile first."), default); Notify(); }
    public async Task DeleteProfileAsync(Guid id)
    {
        if (Profile?.Id == id) { await StopAudioAsync(); Profile = null; Calibration = null; Evaluation = null; }
        await Profiles.DeleteAsync(id, default); Notify();
    }
    public async Task ImportProfileAsync(string path)
    {
        var profile = JsonSerializer.Deserialize<HoloProfile>(await File.ReadAllTextAsync(path), WorkspaceRepository.JsonOptions)
            ?? throw new InvalidDataException("Empty profile.");
        JsonProfileRepository.MigrateProfile(profile);
        profile.Id = Guid.NewGuid(); profile.Name += " (imported)";
        // Imported mappings cannot silently activate locally approved actions.
        foreach (var mapping in profile.ActionMappings.Values) { mapping.ActionId = null; mapping.Enabled = false; }
        profile.GestureActions.Clear();
        profile.LightRules = Array.Empty<LightRule>();
        profile.PresenceRules = Array.Empty<PresenceRule>();
        await Profiles.SaveAsync(profile, default); await SelectProfileAsync(profile.Id);
    }
    public Task ExportProfileAsync(string path) => File.WriteAllTextAsync(path,
        JsonSerializer.Serialize(Profile ?? throw new InvalidOperationException("Select a profile."), WorkspaceRepository.JsonOptions));
    public async Task StartListeningAsync()
    {
        await Ready;
        if (Calibration is not null || Evaluation is not null)
            throw new InvalidOperationException("Save or cancel the calibration/evaluation session before listening for actions.");
        if (Profile?.CalibrationIsComplete != true)
            throw new InvalidOperationException("Select a profile and save ten calibration taps per zone first.");
        if (Settings.FusionMode == FusionMode.ManualOnly)
            throw new InvalidOperationException("Select Audio only in Settings to trigger actions from microphone taps.");
        await StartAudioAsync();
    }
    public async Task StartAudioAsync()
    {
        await Ready; await _lifecycle.WaitAsync();
        try
        {
            if (IsRunning) return;
            if (!Settings.MicrophoneEnabled) throw new InvalidOperationException("Enable the microphone on Hardware first.");
            var profile = Profile ?? throw new InvalidOperationException("Create or select a profile first.");
            if (profile.Strategy != SensingStrategy.Passive && !Settings.ActiveProbeEnabled)
                throw new InvalidOperationException("This profile uses an active probe. Enable the probe in Settings or select a passive profile.");
            if (_audio is not null) { await _audio.DisposeAsync(); _audio = null; }
            _sessionCts?.Dispose(); _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var capture = new WasapiAudioCapture(Settings.MicrophoneId);
            _audio = new AcousticPipeline(capture, profile.Strategy);
            if (profile.CalibrationIsComplete) _audio.Classifier = TrainedTapClassifier.Train(profile.PositiveExamples, profile.NegativeExamples, profile.MinimumConfidence);
            _audio.FeatureAvailable += (_, feature) => OnFeature(feature);
            _audio.TapAvailable += (_, tap) => SaveDebugTap(tap, capture.SampleRate);
            _audio.DecisionAvailable += (_, decision) =>
            {
                if (Calibration is not null) return;
                LastDecision = decision; Evaluation?.RecordTrial(decision);
                Log(decision.WasAccepted ? $"Tap: {decision.Zone} ({decision.Confidence:P0})" : $"Tap rejected: {decision.RejectionReason}");
            };
            _audio.ObservationAvailable += (_, observation) => { if (Calibration is null && Evaluation is null) Fusion.Push(observation); };
            _audio.StatusChanged += (_, status) => { Status = status.Running ? "Listening" : status.Detail ?? "Stopped"; Notify(); };
            await Fusion.StartAsync(_sessionCts.Token);
            if (profile.Strategy != SensingStrategy.Passive)
            { _probe.IsEnabled = true; _probe.Amplitude = Settings.ProbeAmplitude; _probe.Start(capture.SampleRate); }
            await _audio.StartAsync(_sessionCts.Token);
            Status = "Listening"; Log("Microphone started.");
        }
        catch
        {
            _probe.Stop(); _sessionCts?.Cancel();
            if (_audio is not null) { await _audio.DisposeAsync(); _audio = null; }
            await Fusion.StopAsync(); Status = "Stopped"; throw;
        }
        finally { _lifecycle.Release(); Notify(); }
    }
    public async Task StopAudioAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            _sessionCts?.Cancel(); _probe.Stop();
            if (_audio is not null) { await _audio.DisposeAsync(); _audio = null; }
            await Fusion.StopAsync(); Calibration?.Disarm(); NegativeLabel = null;
            Status = "Stopped";
        }
        finally { _lifecycle.Release(); Notify(); }
    }
    private void OnFeature(TapFeatureVector feature)
    {
        if (Calibration is not { } calibration) return;
        if (NegativeLabel is { } label) { calibration.AddNegative(feature, label); Notify(); return; }
        if (calibration.AcceptSample(feature, out var reason)) Log("Calibration: sample accepted.");
        else if (reason != RejectionReason.Paused) Log("Calibration: " + reason);
        Notify();
    }
    private void SaveDebugTap(DetectedTap tap, double rate)
    {
        if (!Settings.DebugCaptureEnabled) return;
        try
        {
            if (Directory.EnumerateFiles(Paths.DebugCapturesDir, "*.wav").Take(100).Count() >= 100)
            { Settings.DebugCaptureEnabled = false; Log("Debug capture stopped at 100 recordings. Delete captures to continue."); return; }
            using var file = new HoloWin.Core.WaveFileWriter(Path.Combine(Paths.DebugCapturesDir, $"tap-{Guid.NewGuid():N}.wav"), tap.Channels.Length, rate);
            file.WriteFrame(tap.Channels, tap.FrameCount);
        }
        catch (Exception ex) { Log("Debug recording: " + ex.Message); }
    }
    public async Task BeginCalibrationAsync()
    {
        await StopAudioAsync();
        var p = Profile ?? throw new InvalidOperationException("Create a profile first.");
        Evaluation = null; CalibrationAgreement = null;
        Calibration = new GuidedCaptureSession { ProfileId = p.Id, Strategy = p.Strategy };
        try { await StartAudioAsync(); }
        catch { Calibration = null; Notify(); throw; }
    }
    public async Task ArmCalibrationAsync(DeskZone zone)
    {
        if (Calibration is null || !IsRunning) throw new InvalidOperationException("Begin calibration first.");
        NegativeLabel = null; var task = Calibration.PrepareAsync(zone, _sessionCts!.Token); Notify(); await task; Notify();
    }
    public async Task ValidateCalibrationAsync()
    {
        var session = Calibration ?? throw new InvalidOperationException("Begin calibration first.");
        session.Disarm(); NegativeLabel = null;
        CalibrationAgreement = await Task.Run(session.ValidateLeaveOneOut); Notify();
    }
    public async Task SaveCalibrationAsync()
    {
        var session = Calibration ?? throw new InvalidOperationException("No calibration session.");
        if (session.CountsPerZone.Any(kv => kv.Value < 10)) throw new InvalidOperationException("Collect ten taps in each zone first.");
        var p = Profile!; p.PositiveExamples = session.AcceptedExamples.ToArray(); p.NegativeExamples = session.NegativeExamples.ToArray();
        p.MicrophoneDeviceId = Settings.MicrophoneId;
        await StopAudioAsync(); await SaveProfileAsync(); Calibration = null; Log("Calibration saved. Press Start to use it.");
    }
    public async Task BeginEvaluationAsync()
    {
        await StopAudioAsync();
        var p = Profile ?? throw new InvalidOperationException("Select a profile.");
        if (!p.CalibrationIsComplete) throw new InvalidOperationException("Complete calibration first.");
        Calibration = null; Evaluation = new EvaluationSession { ProfileId = p.Id, Strategy = p.Strategy };
        await StartAudioAsync();
    }
    public async Task SaveEvaluationAsync()
    {
        var evaluation = Evaluation ?? throw new InvalidOperationException("No evaluation session.");
        if (evaluation.CurrentState != EvaluationSession.State.Complete) throw new InvalidOperationException("Complete fifteen trials per zone before saving.");
        await Reports.SaveAsync(evaluation.ToReport(), default); await StopAudioAsync(); Evaluation = null; Log("Evaluation report saved.");
    }
    public async Task CancelSessionsAsync()
    { Calibration?.Cancel(); Evaluation?.Cancel(); await StopAudioAsync(); Calibration = null; Evaluation = null; Notify(); }
    private async Task DispatchDecisionAsync(FusedDecision decision)
    {
        try { if (decision.Zone is { } zone && decision.RejectionReason is null) await TriggerZoneAsync(zone, decision.Confidence, _sessionCts?.Token ?? default); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log("Dispatch: " + ex.Message); }
    }
    public async Task TriggerZoneAsync(DeskZone zone, double confidence = 1, CancellationToken ct = default)
    {
        if (!await _dispatchGate.WaitAsync(0, ct)) return;
        try
        {
            if (Profile is null) throw new InvalidOperationException("Select a profile first.");
            if (!Profile.ActionMappings.TryGetValue(zone, out var mapping) || !mapping.Enabled)
            { Log($"{zone}: zone is disabled or has no assignment. Configure it in Actions."); return; }
            if (confidence < mapping.ConfidenceThreshold)
            { Log($"{zone}: confidence {confidence:P0} is below zone threshold {mapping.ConfidenceThreshold:P0}."); return; }
            if (_lastZoneDispatch.TryGetValue(zone, out var last) && Stopwatch.GetElapsedTime(last).TotalMilliseconds < mapping.CooldownMilliseconds)
            { Log($"{zone}: waiting for cooldown."); return; }
            _lastZoneDispatch[zone] = Stopwatch.GetTimestamp();
            if (mapping.ActionId is null) { Log($"{zone}: visual feedback only"); return; }
            var action = Library.Actions.FirstOrDefault(a => a.Id == mapping.ActionId) ?? throw new InvalidOperationException("Mapped action is missing.");
            if (mapping.RequireConfirmation && !action.RequireConfirmation
                && (Actions.ConfirmAsync is null || !await Actions.ConfirmAsync(action, ct))) return;
            var result = await Actions.ExecuteAsync(action, confidence, ct);
            if (!result.Succeeded) Log(result.Error ?? "Action failed.");
        }
        finally { _dispatchGate.Release(); }
    }
    public async Task StartCameraAsync() => await StartCameraCoreAsync(false);
    public async Task StartTrackingAsync() => await StartCameraCoreAsync(true);
    private async Task StartCameraCoreAsync(bool tracking)
    {
        await _cameraLifecycle.WaitAsync();
        OnnxHandTracker? tracker = null;
        try
        {
            await StopCameraCoreAsync();
            if (tracking) tracker = await Task.Run(() => new OnnxHandTracker());
            _camera = await Task.Run(() => new OpenCvCameraCapture(Settings.CameraIndex));
            if (tracker is not null)
            {
                _trackingFrames = System.Threading.Channels.Channel.CreateBounded<HoloWin.Gestures.Frame>(new System.Threading.Channels.BoundedChannelOptions(1)
                { FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
                _trackingCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                var ownedTracker = tracker; tracker = null;
                var frames = _trackingFrames.Reader; var token = _trackingCts.Token;
                TrackingRunning = true;
                _trackingWorker = Task.Run(() => TrackFramesAsync(ownedTracker, frames, token));
            }
            var camera = _camera;
            _camera.FrameAvailable += (_, frame) => { if (!ReferenceEquals(_camera, camera)) return; _trackingFrames?.Writer.TryWrite(frame); CameraFrameAvailable?.Invoke(this, frame); };
            _camera.StatusChanged += (_, status) =>
            {
                if (!ReferenceEquals(_camera, camera)) return;
                if (!status.Running) { GestureActionsEnabled = false; _trackingFrames?.Writer.TryComplete(); }
                if (status.Detail is not null) Log("Camera: " + status.Detail); Notify();
            };
            await _camera.StartAsync(_lifetime.Token);
            Log(tracking ? "Camera tracking started. Gesture actions are disarmed until enabled." : "Camera preview started.");
        }
        catch { tracker?.Dispose(); await StopCameraCoreAsync(); throw; }
        finally { _cameraLifecycle.Release(); }
    }
    public async Task StopCameraAsync()
    {
        await _cameraLifecycle.WaitAsync();
        try { await StopCameraCoreAsync(); } finally { _cameraLifecycle.Release(); }
    }
    private async Task StopCameraCoreAsync()
    {
        GestureActionsEnabled = false; _trackingCts?.Cancel();
        if (_camera is not null) { await _camera.DisposeAsync(); _camera = null; }
        if (_trackingWorker is not null) { try { await _trackingWorker; } catch (OperationCanceledException) { } _trackingWorker = null; }
        _trackingCts?.Dispose(); _trackingCts = null; _trackingFrames = null;
        TrackingRunning = false; CurrentHands = Array.Empty<HandTrackingResult>(); TrackingStatus = "Tracking off"; Notify();
    }
    private async Task TrackFramesAsync(OnnxHandTracker tracker, System.Threading.Channels.ChannelReader<HoloWin.Gestures.Frame> frames, CancellationToken ct)
    {
        using (tracker)
        {
            var recognizer = new CameraGestureRecognizer();
            try
            {
                await foreach (var frame in frames.ReadAllAsync(ct))
                {
                    if (MonotonicClock.IsStale(frame.Timestamp, 500_000)) continue;
                    CurrentHands = await tracker.TrackAsync(frame, ct); ct.ThrowIfCancellationRequested();
                    if (MonotonicClock.IsStale(frame.Timestamp, 500_000)) { CurrentHands = Array.Empty<HandTrackingResult>(); continue; }
                    var gesture = recognizer.Recognize(CurrentHands, frame.Timestamp.Milliseconds, Profile ?? new HoloProfile());
                    TrackingStatus = CurrentHands.Count == 0 ? "No hand detected" : $"{CurrentHands.Count} hand(s) detected";
                    if (gesture is not null)
                    {
                        TrackingStatus = gesture.Name; Log($"Gesture: {TrackingStatus} ({gesture.Confidence:P0})");
                        if (GestureActionsEnabled && Calibration is null && Evaluation is null && Profile is { } p
                            && p.GestureActions.TryGetValue(gesture.Key, out var id))
                        {
                            if (await _dispatchGate.WaitAsync(0, ct))
                            {
                                try
                                {
                                    var action = Library.Actions.FirstOrDefault(a => a.Id == id);
                                    if (action is not null) await Actions.ExecuteAsync(action, gesture.Confidence, ct);
                                    else Log("Gesture action no longer exists. Reassign it in Actions.");
                                }
                                finally { _dispatchGate.Release(); }
                            }
                        }
                    }
                    Notify();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { TrackingStatus = "Tracking failed: " + ex.Message; Log(TrackingStatus); }
            finally { TrackingRunning = false; GestureActionsEnabled = false; CurrentHands = Array.Empty<HandTrackingResult>(); Notify(); }
        }
    }
    private async Task OnLightAsync(double lux)
    {
        CurrentLux = lux;
        try
        {
            var ct = _lightActionsCts?.Token ?? new CancellationToken(true);
            var profile = Profile;
            if (!LightActionsEnabled || !MotionRunning || Calibration is not null || Evaluation is not null || profile is null)
            { _lightRules.Reset(); return; }
            var rules = _lightRules.Evaluate(lux, TimeSpan.FromMilliseconds(MonotonicClock.Now.Milliseconds), profile.LightRules);
            if (rules.Count == 0) return;
            if (!await _dispatchGate.WaitAsync(0, ct)) { Log("Light trigger skipped: another action is running."); return; }
            try
            {
                foreach (var rule in rules)
                {
                    if (!LightActionsEnabled || !MotionRunning || Profile != profile || Calibration is not null || Evaluation is not null) break;
                    var action = Library.Actions.FirstOrDefault(a => a.Id == rule.ActionId);
                    Log($"Light rule: {rule.Name} ({lux:F1} lux)");
                    ct.ThrowIfCancellationRequested();
                    if (action is not null) await Actions.ExecuteAsync(action, 1, ct);
                    else Log("Light action no longer exists. Reassign the rule in Actions.");
                }
            }
            finally { _dispatchGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log("Light rule failed: " + ex.Message); }
        finally { Notify(); }
    }
    public void SetMotionEnabled(bool enabled)
    {
        LightActionsEnabled = false; CurrentLux = null;
        if (enabled) _motion.Start(); else _motion.Stop();
        MotionRunning = enabled; Settings.MotionEnabled = enabled;
        if (!enabled) SensorReading = "Sensor readings off"; Notify();
    }
    public async Task SaveDeskMapAsync()
    {
        if (Profile is null) throw new InvalidOperationException("Select a profile.");
        if (!DeskMap.IsCalibrated) throw new InvalidOperationException("Select four non-collinear corners in clockwise order.");
        Profile.DeskCorners = Enumerable.Range(0, 4).Select(i => { var c = DeskMap.GetCorner(i)!.Value; return new[] { c.ImgX, c.ImgY }; }).ToArray();
        Profile.CameraDeviceId = Settings.CameraIndex.ToString(); await SaveProfileAsync();
    }
    private void RestoreDeskMap()
    {
        DeskMap.ClearCorners();
        if (Profile?.DeskCorners is not { Length: 4 } corners) return;
        for (int i = 0; i < 4; i++) if (corners[i].Length == 2) DeskMap.SetCorner(i, corners[i][0], corners[i][1]);
    }
    private void OnDeviceChanged(object? sender, DeviceCapability device)
    {
        if (device.State == DeviceState.Disconnected && device.Class == DeviceClass.AudioInput && IsRunning
            && (Settings.MicrophoneId is null || device.Id == Settings.MicrophoneId))
        { Log("Microphone disconnected; processing will stop. Reconnect and press Start."); _ = StopAudioAsync(); }
        Notify();
    }
    public Task ExportDiagnosticsAsync(string path) => File.WriteAllTextAsync(path, string.Join(Environment.NewLine, RecentEvents.Reverse()));
    public void DeleteDebugCaptures() { foreach (var path in Directory.EnumerateFiles(Paths.DebugCapturesDir, "*.wav")) File.Delete(path); Log("Debug captures deleted."); }
    public async Task ResetDataAsync()
    {
        await StopPresenceAsync();
        await CancelSessionsAsync(); await StopCameraAsync(); SetMotionEnabled(false);
        foreach (var p in await Profiles.ListAsync(default)) await Profiles.DeleteAsync(p.Id, default);
        foreach (var r in await Reports.ListAsync(null, default)) await Reports.DeleteAsync(r.Id, default);
        DeleteDebugCaptures(); Settings = new(); Library = new(); Profile = null;
        await SaveSettingsAsync(); await SaveLibraryAsync(); StartupManager.DisableAutoStart(); Notify();
    }
    public async Task PauseInputsAsync()
    {
        await StopPresenceAsync();
        await CancelSessionsAsync(); await StopCameraAsync(); SetMotionEnabled(false);
        Log("Sensors paused. Hotkeys remain available.");
    }
    public async ValueTask DisposeAsync()
    {
        PresenceActionsEnabled = false;
        LightActionsEnabled = false;
        if (_disposed) return; _disposed = true;
        _lifetime.Cancel();
        await StopPresenceAsync();
        await StopAudioAsync(); await StopCameraAsync(); _motion.Dispose(); await Hardware.DisposeAsync();
        await Fusion.DisposeAsync(); await _probe.DisposeAsync(); _lifetime.Cancel(); await _logWriter;
        _sessionCts?.Dispose(); _lifetime.Dispose();
    }
}
