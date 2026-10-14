using HoloWin.Core;
using Xunit;

namespace HoloWin.Core.Tests;

public class PresenceRuleTests
{
    private static PresenceReading Reading(bool? value) => new("device", "Device", PresenceSourceKind.Bluetooth, value);
    private static PresenceRule Rule(bool present = true) => new() { SourceId = "device", Kind = PresenceSourceKind.Bluetooth, ActionId = "a", WhenPresent = present, HoldMilliseconds = 1000, CooldownMilliseconds = 3000 };
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FiresOnlyAfterStableTransition(bool present)
    {
        var evaluator = new PresenceRuleEvaluator(); var rules = new[] { Rule(present) };
        Assert.Empty(evaluator.Evaluate([Reading(!present)], rules, TimeSpan.Zero));
        Assert.Empty(evaluator.Evaluate([Reading(present)], rules, TimeSpan.FromSeconds(1)));
        Assert.Single(evaluator.Evaluate([Reading(present)], rules, TimeSpan.FromSeconds(2)));
        Assert.Empty(evaluator.Evaluate([Reading(present)], rules, TimeSpan.FromSeconds(3)));
    }
    [Fact]
    public void StartupUnknownAndRearmingDoNotFire()
    {
        var evaluator = new PresenceRuleEvaluator(); var rules = new[] { Rule() };
        Assert.Empty(evaluator.Evaluate([Reading(true)], rules, TimeSpan.Zero));
        Assert.Empty(evaluator.Evaluate([Reading(null)], rules, TimeSpan.FromSeconds(1)));
        Assert.Empty(evaluator.Evaluate([Reading(false)], rules, TimeSpan.FromSeconds(2)));
        evaluator.Reset();
        Assert.Empty(evaluator.Evaluate([Reading(true)], rules, TimeSpan.FromSeconds(3)));
    }
    [Fact]
    public void FlappingCannotSatisfyHoldAndCooldownDropsRapidRepeat()
    {
        var e = new PresenceRuleEvaluator(); var r = new[] { Rule() };
        e.Evaluate([Reading(false)], r, TimeSpan.Zero);
        e.Evaluate([Reading(true)], r, TimeSpan.FromMilliseconds(100));
        e.Evaluate([Reading(false)], r, TimeSpan.FromMilliseconds(200));
        Assert.Empty(e.Evaluate([Reading(true)], r, TimeSpan.FromSeconds(1)));
        Assert.Single(e.Evaluate([Reading(true)], r, TimeSpan.FromSeconds(2)));
        e.Evaluate([Reading(false)], r, TimeSpan.FromMilliseconds(2100));
        e.Evaluate([Reading(false)], r, TimeSpan.FromMilliseconds(3100));
        e.Evaluate([Reading(true)], r, TimeSpan.FromMilliseconds(3200));
        Assert.Empty(e.Evaluate([Reading(true)], r, TimeSpan.FromMilliseconds(4200)));
    }
}
