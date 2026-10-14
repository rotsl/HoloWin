using HoloWin.Core;
using Xunit;

namespace HoloWin.Core.Tests;

public class LightRuleTests
{
    [Theory]
    [InlineData(LightCondition.Above, 110, 90)]
    [InlineData(LightCondition.Below, 90, 110)]
    [InlineData(LightCondition.Range, 150, 220)]
    public void RequiresDwellAndReset(LightCondition condition, double inside, double outside)
    {
        var evaluator = new LightRuleEvaluator();
        var rules = new[] { new LightRule { Condition = condition, LowerLux = 100, UpperLux = 200, ActionId = "a", HoldMilliseconds = 500, CooldownMilliseconds = 1000 } };
        IReadOnlyList<LightRule> At(double lux, int ms) => evaluator.Evaluate(lux, TimeSpan.FromMilliseconds(ms), rules);
        Assert.Empty(At(inside, 0)); Assert.Empty(At(inside, 499)); Assert.Single(At(inside, 500));
        Assert.Empty(At(inside, 1000)); Assert.Empty(At(outside, 1100));
        Assert.Empty(At(inside, 1200)); Assert.Single(At(inside, 1700));
    }
    [Fact]
    public void HysteresisPreventsThresholdChatter()
    {
        var evaluator = new LightRuleEvaluator();
        var rules = new[] { new LightRule { ActionId = "a", LowerLux = 100, HoldMilliseconds = 0, CooldownMilliseconds = 0 } };
        Assert.Single(evaluator.Evaluate(101, TimeSpan.Zero, rules));
        Assert.Empty(evaluator.Evaluate(99, TimeSpan.FromMilliseconds(100), rules));
        Assert.Empty(evaluator.Evaluate(101, TimeSpan.FromMilliseconds(200), rules));
        Assert.Empty(evaluator.Evaluate(89, TimeSpan.FromMilliseconds(300), rules));
        Assert.Single(evaluator.Evaluate(101, TimeSpan.FromMilliseconds(400), rules));
    }
    [Fact]
    public void InterruptedDwellDoesNotFire()
    {
        var evaluator = new LightRuleEvaluator();
        var rules = new[] { new LightRule { ActionId = "a" } };
        Assert.Empty(evaluator.Evaluate(200, TimeSpan.Zero, rules));
        Assert.Empty(evaluator.Evaluate(200, TimeSpan.FromSeconds(3), rules));
        Assert.Empty(evaluator.Evaluate(double.NaN, TimeSpan.FromSeconds(4), rules));
        Assert.Throws<ArgumentException>(() => (rules[0] with { Condition = LightCondition.Range, UpperLux = 0 }).Validate());
    }
}
