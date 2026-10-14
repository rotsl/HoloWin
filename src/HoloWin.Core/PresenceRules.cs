namespace HoloWin.Core;

public enum PresenceSourceKind { Bluetooth, Hid, Proximity, HumanPresence }
public sealed record PresenceReading(string Id, string Name, PresenceSourceKind Kind, bool? Present)
{
    public string DisplayName => $"{Kind}: {Name} · {(Present is true ? "connected / detected" : Present is false ? "disconnected / absent" : "unknown")}";
}
public sealed record PresenceRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Presence rule";
    public string SourceId { get; init; } = "";
    public string SourceName { get; init; } = "";
    public PresenceSourceKind Kind { get; init; }
    public bool WhenPresent { get; init; } = true;
    public string ActionId { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public int HoldMilliseconds { get; init; } = 2000;
    public int CooldownMilliseconds { get; init; } = 5000;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(SourceId)
            || string.IsNullOrWhiteSpace(ActionId) || !Enum.IsDefined(Kind) || HoldMilliseconds < 0 || CooldownMilliseconds < 0)
            throw new ArgumentException("Choose a presence source, action and nonnegative timing values.");
    }
}
/// <summary>Transitions only: first observation and recovery from unknown establish a baseline.</summary>
public sealed class PresenceRuleEvaluator
{
    private sealed class State
    {
        public required PresenceRule Rule;
        public bool? Stable, Candidate;
        public TimeSpan Since, LastReading;
        public TimeSpan? Fired;
    }
    private readonly Dictionary<string, State> _states = new();
    private readonly object _lock = new();
    public void Reset() { lock (_lock) _states.Clear(); }
    public IReadOnlyList<PresenceRule> Evaluate(IReadOnlyList<PresenceReading> readings, IReadOnlyList<PresenceRule> rules, TimeSpan now)
    {
        lock (_lock)
        {
            var fired = new List<PresenceRule>();
            var enabled = rules.Where(r => r.Enabled).ToArray();
            foreach (var id in _states.Keys.Except(enabled.Select(r => r.Id)).ToArray()) _states.Remove(id);
            foreach (var rule in enabled)
            {
                rule.Validate();
                if (!_states.TryGetValue(rule.Id, out var s) || s.Rule != rule) _states[rule.Id] = s = new State { Rule = rule };
                var value = readings.FirstOrDefault(r => r.Id == rule.SourceId && r.Kind == rule.Kind)?.Present;
                if (now < s.LastReading || now - s.LastReading > TimeSpan.FromSeconds(20)) s.Stable = null;
                s.LastReading = now;
                if (value is null) { s.Stable = null; s.Candidate = null; continue; }
                if (s.Stable is null) { s.Stable = value; s.Candidate = value; continue; }
                if (value == s.Stable) { s.Candidate = value; continue; }
                if (s.Candidate != value) { s.Candidate = value; s.Since = now; }
                if (now - s.Since < TimeSpan.FromMilliseconds(rule.HoldMilliseconds)) continue;
                s.Stable = value;
                if (value != rule.WhenPresent || s.Fired is { } last && now - last < TimeSpan.FromMilliseconds(rule.CooldownMilliseconds)) continue;
                s.Fired = now; fired.Add(rule);
            }
            return fired;
        }
    }
}
