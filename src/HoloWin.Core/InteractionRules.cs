namespace HoloWin.Core;

public enum FingerState { Any, Extended, Folded }

public sealed record FingerGesture
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Finger gesture";
    // Thumb, index, middle, ring, little finger.
    public FingerState[] Fingers { get; init; } = new FingerState[5];
    public string Key => "custom:" + Id;
}

public enum LightCondition { Above, Below, Range }

public sealed record LightRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Light rule";
    public string ActionId { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public LightCondition Condition { get; init; }
    public double LowerLux { get; init; } = 100;
    public double UpperLux { get; init; } = 500;
    public double HysteresisLux { get; init; } = 10;
    public int HoldMilliseconds { get; init; } = 1000;
    public int CooldownMilliseconds { get; init; } = 3000;

    public void Validate()
    {
        if (!Enum.IsDefined(Condition) || !double.IsFinite(LowerLux) || !double.IsFinite(UpperLux)
            || !double.IsFinite(HysteresisLux) || LowerLux < 0 || UpperLux < 0 || HysteresisLux < 0
            || (Condition == LightCondition.Range && UpperLux <= LowerLux)
            || HoldMilliseconds < 0 || CooldownMilliseconds < 0 || string.IsNullOrWhiteSpace(ActionId)
            || string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Enter valid lux limits, a nonnegative hold/cooldown and an action.");
    }
}

/// <summary>Fires once on entry after dwell; resets only beyond the hysteresis boundary.</summary>
public sealed class LightRuleEvaluator
{
    private readonly Dictionary<string, State> _states = new();
    private readonly object _lock = new();
    private sealed class State
    {
        public LightRule? Rule;
        public TimeSpan? Entered;
        public TimeSpan? LastFired;
        public TimeSpan? LastReading;
        public bool Latched;
    }
    public void Reset() { lock (_lock) _states.Clear(); }
    public IReadOnlyList<LightRule> Evaluate(double lux, TimeSpan now, IReadOnlyList<LightRule> rules)
    {
        lock (_lock)
        {
            var fired = new List<LightRule>();
            if (!double.IsFinite(lux) || lux < 0) { _states.Clear(); return fired; }
            var ids = rules.Where(r => r.Enabled).Select(r => r.Id).ToHashSet();
            foreach (var id in _states.Keys.Where(id => !ids.Contains(id)).ToArray()) _states.Remove(id);
            foreach (var rule in rules.Where(r => r.Enabled))
            {
                rule.Validate();
                if (!_states.TryGetValue(rule.Id, out var state) || state.Rule != rule)
                    _states[rule.Id] = state = new State { Rule = rule };
                if (state.LastReading is { } previous && (now < previous || now - previous > TimeSpan.FromSeconds(2)))
                { state.Entered = null; state.Latched = false; }
                state.LastReading = now;
                bool inside = rule.Condition switch
                {
                    LightCondition.Above => lux > rule.LowerLux,
                    LightCondition.Below => lux < rule.LowerLux,
                    _ => lux >= rule.LowerLux && lux <= rule.UpperLux
                };
                bool outsideReset = rule.Condition switch
                {
                    LightCondition.Above => lux <= Math.Max(0, rule.LowerLux - rule.HysteresisLux),
                    LightCondition.Below => lux >= rule.LowerLux + rule.HysteresisLux,
                    _ => lux < Math.Max(0, rule.LowerLux - rule.HysteresisLux) || lux > rule.UpperLux + rule.HysteresisLux
                };
                if (state.Latched) { if (outsideReset) { state.Latched = false; state.Entered = null; } else continue; }
                if (!inside) { state.Entered = null; continue; }
                state.Entered ??= now;
                if (now - state.Entered.Value < TimeSpan.FromMilliseconds(rule.HoldMilliseconds)) continue;
                if (state.LastFired is { } last && now - last < TimeSpan.FromMilliseconds(rule.CooldownMilliseconds)) continue;
                state.Latched = true; state.LastFired = now; fired.Add(rule);
            }
            return fired;
        }
    }
}
