using HoloWin.Core;
using System.Diagnostics;

namespace HoloWin.Automation;

public sealed class MacroRunner
{
    private readonly IActionDispatcher _dispatcher;
    private readonly object _lock = new();
    private readonly Dictionary<string, long> _lastRun = new(StringComparer.Ordinal);
    private readonly Func<string, HoloAction?> _resolve;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MacroRunner(IActionDispatcher dispatcher, Func<string, HoloAction?>? resolve = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _resolve = resolve ?? (_ => throw new InvalidOperationException("No action store is connected."));
    }

    public async Task RunMacroAsync(HoloMacro macro, CancellationToken ct)
    {
        if (!macro.Enabled) throw new InvalidOperationException("Macro is disabled.");
        if (!await _gate.WaitAsync(0, ct)) throw new InvalidOperationException("A macro is already running.");
        try
        {
        // Cooldown
        if (macro.CooldownMilliseconds > 0)
        {
            lock (_lock)
            {
                if (_lastRun.TryGetValue(macro.Id, out var last))
                {
                    var elapsedMs = (Stopwatch.GetTimestamp() - last) * 1000.0 / Stopwatch.Frequency;
                    if (elapsedMs < macro.CooldownMilliseconds) return;
                }
            }
        }
        var actions = macro.Steps.OrderBy(s => s.Order).ToList();
        foreach (var step in actions)
        {
            if (step.DelayMilliseconds > 0)
                await Task.Delay(step.DelayMilliseconds, ct);
            var action = _resolve(step.ActionId) ?? throw new InvalidOperationException($"Missing action: {step.ActionId}");
            if (action.Type == ActionType.RunMacro) throw new InvalidOperationException("Nested macros are not supported.");
            var result = await _dispatcher.ExecuteAsync(action, ct);
            if (!result.Succeeded) throw new InvalidOperationException($"Macro stopped: {result.Error}");
        }
        lock (_lock) _lastRun[macro.Id] = Stopwatch.GetTimestamp();
        }
        finally { _gate.Release(); }
    }
}
