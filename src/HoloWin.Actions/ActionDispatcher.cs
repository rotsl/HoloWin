using System.Diagnostics;
using System.Runtime.Versioning;
using HoloWin.Core;

namespace HoloWin.Actions;

/// <summary>
/// Default action dispatcher. Implements safe, opt-in dispatching for the
/// core action types. Shell/script/executable/HTTP actions always require
/// explicit user approval before they can be auto-dispatched.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ActionDispatcher : IActionDispatcher
{
    private readonly object _lock = new();
    private readonly Dictionary<string, long> _lastDispatchTicks = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
    public Func<HoloAction, CancellationToken, Task<bool>>? ConfirmAsync { get; set; }
    public Func<string, CancellationToken, Task>? RunMacroAsync { get; set; }
    public event EventHandler<ActionResult>? Completed;

    public async Task<ActionResult> ExecuteAsync(HoloAction action, CancellationToken ct)
        => await ExecuteAsync(action, 1.0, ct);

    public async Task<ActionResult> ExecuteAsync(HoloAction action, double confidence, CancellationToken ct)
    {
        var gate = _gates.GetOrAdd(action.Id, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct)) return Failed(action, "Action is already running.");
        try
        {
            if (!double.IsFinite(confidence) || confidence < action.ConfidenceThreshold)
                return Failed(action, "Confidence is below the action threshold.");
            if (action.RequireConfirmation && (ConfirmAsync is null || !await ConfirmAsync(action, ct)))
                return Failed(action, "Confirmation declined or unavailable.");
            var result = await ExecuteCheckedAsync(action, ct);
            Completed?.Invoke(this, result);
            return result;
        }
        finally { gate.Release(); }
    }

    private async Task<ActionResult> ExecuteCheckedAsync(HoloAction action, CancellationToken ct)
    {
        if (!action.Enabled)
            return Failed(action, "Action is disabled.");
        if (!await CooldownRespectedAsync(action))
            return new ActionResult(action.Id, false, "Cooldown", 0, DateTimeOffset.UtcNow);
        if (!await VerifyApprovalAsync(action))
            return Failed(action, "Action requires explicit user approval before being dispatched.");

        var sw = Stopwatch.StartNew();
        try
        {
            ct.ThrowIfCancellationRequested();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(action.TimeoutSeconds > 0 ? action.TimeoutSeconds : 5));
            await ExecuteCoreAsync(action, cts.Token);
            sw.Stop();
            RecordDispatch(action.Id);
            return new ActionResult(action.Id, true, null, (long)(sw.Elapsed.TotalMilliseconds * 1000), DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
        {
            return Failed(action, "Action timed out.");
        }
        catch (Exception ex)
        {
            return Failed(action, ex.Message);
        }
    }

    public bool RequiresApproval(HoloAction action) => action.Type switch
    {
        ActionType.RunPowerShell     => true,
        ActionType.RunExecutable    => true,
        ActionType.InvokeHttpEndpoint => true,
        ActionType.TakeScreenshot   => true,
        ActionType.RunMacro         => true,
        ActionType.SmartHome        => true,
        _ => false
    };

    private Task<bool> CooldownRespectedAsync(HoloAction action)
    {
        if (action.CooldownMilliseconds <= 0) return Task.FromResult(true);
        lock (_lock)
        {
            if (_lastDispatchTicks.TryGetValue(action.Id, out var last))
            {
                var elapsedMs = (Stopwatch.GetTimestamp() - last) * 1000.0 / Stopwatch.Frequency;
                if (elapsedMs < action.CooldownMilliseconds)
                    return Task.FromResult(false);
            }
        }
        return Task.FromResult(true);
    }

    private Task<bool> VerifyApprovalAsync(HoloAction action)
    {
        if (!RequiresApproval(action)) return Task.FromResult(true);
        return Task.FromResult(action.RequiresApproval && action.Enabled);
    }

    private void RecordDispatch(string id)
    {
        lock (_lock) _lastDispatchTicks[id] = Stopwatch.GetTimestamp();
    }

    private static ActionResult Failed(HoloAction action, string? err) =>
        new(action.Id, false, err, 0, DateTimeOffset.UtcNow);

    private async Task ExecuteCoreAsync(HoloAction action, CancellationToken ct)
    {
        switch (action.Type)
        {
            case ActionType.VisualOnly:
                return; // No side effect; UI handles visualization.
            case ActionType.RunMacro:
                if (RunMacroAsync is null || !action.Parameters.TryGetValue("macro_id", out var macroId))
                    throw new InvalidOperationException("Choose a macro first.");
                await RunMacroAsync(macroId, ct);
                break;
            case ActionType.PlaySound:
            case ActionType.TypeText:
            case ActionType.FocusApplication:
            case ActionType.TakeScreenshot:
            case ActionType.SetVolume:
            case ActionType.IncreaseVolume:
            case ActionType.DecreaseVolume:
            case ActionType.IncreaseBrightness:
            case ActionType.DecreaseBrightness:
                await DesktopActions.ExecuteAsync(action, ct);
                break;
            case ActionType.OpenWebsite:
            case ActionType.OpenFileOrFolder:
            case ActionType.OpenApplication:
                var key = action.Type == ActionType.OpenWebsite ? ActionParameterKeys.Url
                    : action.Type == ActionType.OpenApplication ? ActionParameterKeys.AppPath : ActionParameterKeys.Path;
                if (!action.Parameters.TryGetValue(key, out var loc) || string.IsNullOrWhiteSpace(loc))
                    throw new InvalidOperationException("Missing target parameter.");
                if (OperatingSystem.IsWindows())
                    Process.Start(new ProcessStartInfo(loc!) { UseShellExecute = true });
                break;
            case ActionType.CopyText:
                if (!action.Parameters.TryGetValue(ActionParameterKeys.Text, out var text))
                    throw new InvalidOperationException("Missing text parameter.");
                if (OperatingSystem.IsWindows())
                {
                    await DesktopActions.OnStaAsync(() => System.Windows.Forms.Clipboard.SetText(text), ct);
                }
                break;
            case ActionType.SpeakText:
                await WindowsSpeech.SpeakAsync(action.Parameters);
                break;
            case ActionType.RunPowerShell:
                if (!action.Parameters.TryGetValue(ActionParameterKeys.Script, out var script))
                    throw new InvalidOperationException("Missing script parameter.");
                await ShellExecutor.RunPowerShellAsync(script, ct);
                break;
            case ActionType.RunExecutable:
                if (!action.Parameters.TryGetValue(ActionParameterKeys.Executable, out var exe))
                    throw new InvalidOperationException("Missing executable parameter.");
                action.Parameters.TryGetValue(ActionParameterKeys.Arguments, out var args);
                await ShellExecutor.RunExecutableAsync(exe!, args ?? string.Empty, ct);
                break;
            case ActionType.InvokeHttpEndpoint:
                if (!action.Parameters.TryGetValue(ActionParameterKeys.Endpoint, out var ep))
                    throw new InvalidOperationException("Missing endpoint parameter.");
                var method = action.Parameters.TryGetValue(ActionParameterKeys.Method, out var m) ? m : "GET";
                var body = action.Parameters.TryGetValue(ActionParameterKeys.Body, out var b) ? b : null;
                await HttpInvoker.InvokeAsync(ep!, method!, body, ct);
                break;
            case ActionType.MuteAudio:
            case ActionType.SendMediaKey:
            case ActionType.TriggerKeyboardShortcut:
            case ActionType.SwitchVirtualDesktop:
                await InputController.DispatchAsync(action.Type, action.Parameters, ct);
                break;
            case ActionType.FunctionKey:
            case ActionType.SwitchTab:
                await InputController.DispatchAsync(ActionType.TriggerKeyboardShortcut,
                    new Dictionary<string, string> { ["shortcut"] = ActionOptions.Shortcut(action.Type, action.Parameters) }, ct);
                break;
            default:
                throw new NotSupportedException($"Action type {action.Type} is not implemented.");
        }
    }
}
