using HoloWin.Core;
using Xunit;

namespace HoloWin.Core.Tests;

public class ZonesTests
{
    [Fact]
    public void DeskZone_DisplayNames_AreStable()
    {
        Assert.Equal("Left Rear",  DeskZone.LeftRear.DisplayName());
        Assert.Equal("Left Front", DeskZone.LeftFront.DisplayName());
        Assert.Equal("Right Rear", DeskZone.RightRear.DisplayName());
        Assert.Equal("Right Front",DeskZone.RightFront.DisplayName());
    }

    [Fact]
    public void DeskZone_ShortNames_MatchExpected()
    {
        Assert.Equal("LR", DeskZone.LeftRear.ShortName());
        Assert.Equal("LF", DeskZone.LeftFront.ShortName());
        Assert.Equal("RR", DeskZone.RightRear.ShortName());
        Assert.Equal("RF", DeskZone.RightFront.ShortName());
    }

    [Fact]
    public void DeskZone_HasExactlyFourZones()
    {
        Assert.Equal(4, DeskZoneExtensions.All.Count);
        Assert.Equal(4, Enum.GetValues<DeskZone>().Length);
    }

    [Fact]
    public void DeskZone_RowAndColumn_AreCorrect()
    {
        Assert.Equal(0, DeskZone.LeftRear.Row());
        Assert.Equal(1, DeskZone.LeftFront.Row());
        Assert.Equal(0, DeskZone.RightRear.Row());
        Assert.Equal(1, DeskZone.RightFront.Row());
        Assert.Equal(0, DeskZone.LeftRear.Column());
        Assert.Equal(1, DeskZone.RightRear.Column());
    }
}
