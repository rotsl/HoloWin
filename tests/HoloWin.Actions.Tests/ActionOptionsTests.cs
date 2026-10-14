using HoloWin.Actions;
using HoloWin.Core;
using Xunit;

namespace HoloWin.Actions.Tests;

public class ActionOptionsTests
{
    [Theory]
    [InlineData("F1")]
    [InlineData("F10")]
    public void FunctionKeysSupportModifiers(string key)
    {
        Assert.Equal("Ctrl+Shift+" + key, ActionOptions.Shortcut(ActionType.FunctionKey,
            new Dictionary<string, string> { ["key"] = key, ["modifiers"] = "Ctrl+Shift" }));
    }
    [Theory]
    [InlineData("F0")]
    [InlineData("F11")]
    [InlineData("Alt+F4")]
    public void InvalidFunctionKeyIsRejected(string key) => Assert.Throws<ArgumentException>(() =>
        ActionOptions.Shortcut(ActionType.FunctionKey, new Dictionary<string, string> { ["key"] = key }));
    [Fact]
    public void TabActionsSupportDirectionAndOverride()
    {
        Assert.Equal("Ctrl+Tab", ActionOptions.Shortcut(ActionType.SwitchTab, new Dictionary<string, string>()));
        var p = new Dictionary<string, string> { ["direction"] = "previous" };
        Assert.Equal("Ctrl+Shift+Tab", ActionOptions.Shortcut(ActionType.SwitchTab, p));
        p["shortcut"] = "Ctrl+PageUp";
        Assert.Equal("Ctrl+PageUp", ActionOptions.Shortcut(ActionType.SwitchTab, p));
    }
    [Fact]
    public void RelativeStepsClampAndValidate()
    {
        Assert.Equal(100, ActionOptions.Adjust(98, 5, true));
        Assert.Equal(0, ActionOptions.Adjust(2, 5, false));
        Assert.Equal(45, ActionOptions.Adjust(50, 5, false));
        Assert.Equal(5, ActionOptions.Step(new Dictionary<string, string>()));
        foreach (var value in new[] { "0", "101", "NaN", "5.5", "-1" })
            Assert.Throws<ArgumentException>(() => ActionOptions.Step(new Dictionary<string, string> { ["step_percent"] = value }));
    }
}
