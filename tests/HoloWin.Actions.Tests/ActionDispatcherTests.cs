using HoloWin.Core;
using HoloWin.Actions;
using Xunit;

namespace HoloWin.Actions.Tests;

public class ActionDispatcherTests
{
    [Fact]
    public async Task Execute_VisualOnly_Succeeds()
    {
        var d = new ActionDispatcher();
        var a = new HoloAction { Type = ActionType.VisualOnly };
        var r = await d.ExecuteAsync(a, default);
        Assert.True(r.Succeeded);
    }

    [Fact]
    public void RequiresApproval_PowerShell_ReturnsTrue()
    {
        var d = new ActionDispatcher();
        var a = new HoloAction { Type = ActionType.RunPowerShell, Parameters = new Dictionary<string, string> { ["script"] = "echo hi" } };
        Assert.True(d.RequiresApproval(a));
    }

    [Fact]
    public void RequiresApproval_VisualOnly_ReturnsFalse()
    {
        var d = new ActionDispatcher();
        var a = new HoloAction { Type = ActionType.VisualOnly };
        Assert.False(d.RequiresApproval(a));
    }

    [Fact]
    public async Task Execute_DisabledAction_Fails()
    {
        var d = new ActionDispatcher();
        var a = new HoloAction { Type = ActionType.VisualOnly, Enabled = false };
        var r = await d.ExecuteAsync(a, default);
        Assert.False(r.Succeeded);
    }

    [Fact]
    public async Task Execute_OpenWebsite_WithoutUrl_Fails()
    {
        var d = new ActionDispatcher();
        var a = new HoloAction
        {
            Type = ActionType.OpenWebsite,
            Parameters = new Dictionary<string, string>()
        };
        var r = await d.ExecuteAsync(a, default);
        Assert.False(r.Succeeded);
    }
}
