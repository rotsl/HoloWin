using HoloWin.Core;
using HoloWin.Platform.Windows;

namespace HoloWin.App.Services;

public sealed partial class WorkspaceController
{
    private readonly PresenceMonitor _presence = new();
    private readonly PresenceRuleEvaluator _presenceRules = new();
    private readonly SemaphoreSlim _presenceScanGate = new(1, 1);
    private CancellationTokenSource? _presenceCts, _presenceActionsCts;
    private Task? _presenceWorker;
    private volatile bool _presenceArmed;
    private CancellationToken _presenceActionToken = new(true);
    public IReadOnlyList<PresenceReading> PresenceReadings { get; private set; } = Array.Empty<PresenceReading>();
    public string PresenceStatus { get; private set; } = "Presence monitoring off";
    public bool PresenceRunning => _presenceCts is { IsCancellationRequested: false };
    public bool PresenceActionsEnabled
    {
        get => _presenceArmed;
        set
        {
            if (_presenceArmed == value) return;
            _presenceArmed = false;
            _presenceActionsCts?.Cancel(); _presenceActionsCts?.Dispose();
            _presenceActionsCts = value ? CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token) : null;
            _presenceActionToken = _presenceActionsCts?.Token ?? new CancellationToken(true);
            _presenceRules.Reset(); _presenceArmed = value;
        }
    }
    public async Task ScanPresenceAsync()
    {
        await _presenceScanGate.WaitAsync(_lifetime.Token);
        try
        {
            PresenceReadings = await _presence.ScanAsync(Profile?.PresenceRules ?? Array.Empty<PresenceRule>(), _lifetime.Token);
            PresenceStatus = _presence.Status; Notify();
        }
        finally { _presenceScanGate.Release(); }
    }
    public async Task StartPresenceAsync()
    {
        await Ready;
        await StopPresenceAsync();
        _presenceCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var ct = _presenceCts.Token;
        _presenceWorker = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await _presenceScanGate.WaitAsync(ct);
                    try { PresenceReadings = await _presence.ScanAsync(Profile?.PresenceRules ?? Array.Empty<PresenceRule>(), ct); }
                    finally { _presenceScanGate.Release(); }
                    PresenceStatus = _presence.Status; Notify();
                    var p = Profile;
                    if (!PresenceActionsEnabled || Calibration is not null || Evaluation is not null || p is null) _presenceRules.Reset();
                    else
                    {
                        var actionToken = _presenceActionToken;
                        var fired = _presenceRules.Evaluate(PresenceReadings, p.PresenceRules, TimeSpan.FromMilliseconds(MonotonicClock.Now.Milliseconds));
                        if (fired.Count > 0)
                        {
                            if (await _dispatchGate.WaitAsync(0, ct))
                            {
                                try
                                {
                                    foreach (var rule in fired)
                                    {
                                        if (!PresenceActionsEnabled || p != Profile || Calibration is not null || Evaluation is not null || actionToken.IsCancellationRequested) break;
                                        var action = Library.Actions.FirstOrDefault(a => a.Id == rule.ActionId);
                                        Log($"Presence rule: {rule.Name}");
                                        if (action is not null) await Actions.ExecuteAsync(action, 1, actionToken);
                                        else Log("Presence action is missing. Reassign it in Actions.");
                                    }
                                }
                                catch (OperationCanceledException) when (actionToken.IsCancellationRequested) { }
                                finally { _dispatchGate.Release(); }
                            }
                            else Log("Presence trigger skipped: another action is running.");
                        }
                    }
                    await Task.Delay(3000, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { PresenceStatus = "Presence monitoring failed: " + ex.Message; Log(PresenceStatus); _presenceCts?.Cancel(); }
            finally { PresenceActionsEnabled = false; Notify(); }
        }, ct);
        Notify();
    }
    public async Task StopPresenceAsync()
    {
        PresenceActionsEnabled = false;
        _presenceCts?.Cancel();
        if (_presenceWorker is not null) { try { await _presenceWorker; } catch (OperationCanceledException) { } }
        _presenceWorker = null; _presenceCts?.Dispose(); _presenceCts = null;
        PresenceStatus = "Presence monitoring off"; Notify();
    }
}
