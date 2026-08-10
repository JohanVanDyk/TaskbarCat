using TaskbarCat.Models;
using Xunit;

namespace TaskbarCat.Tests;

public class RailPlacementTests
{
    // 1920x1080 physical at 125% DPI, bottom bar 60px tall — the dev machine's real
    // geometry, captured from a live --selftest run on 2026-08-09.
    private const int ScreenW = 1920;
    private const int ScreenH = 1080;
    private const int CatW = 160;    // 128 DIP at 1.25
    private const int CatH = 160;

    private static readonly TaskbarRail BottomBar =
        new(TaskbarEdge.Bottom, 0, 1020, 1920, 1080, IsAutoHide: false);

    [Fact]
    public void BottomBar_CatStandsOnTopOfTheBar_NotOverIt()
    {
        var (x, y) = RailPlacement.Resting(BottomBar, CatW, CatH, 0.5, ScreenW, ScreenH);

        Assert.Equal(880, x);              // centred: (1920 - 160) / 2
        Assert.Equal(1020 - CatH, y);      // feet exactly on the bar's top edge
        Assert.Equal(1020, y + CatH);
    }

    [Fact]
    public void AutoHiddenBar_ClampsToScreenEdge_NotToTheHiddenBarRect()
    {
        // Live-verified path: this machine's taskbar is auto-hide, so the bar rect sits
        // off-screen. Following it would float the cat above nothing.
        var hidden = BottomBar with { IsAutoHide = true };
        var (_, y) = RailPlacement.Resting(hidden, CatW, CatH, 0.5, ScreenW, ScreenH);

        Assert.Equal(ScreenH - CatH, y);
        Assert.Equal(ScreenH, y + CatH);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.5, 880)]
    [InlineData(1.0, 1760)]
    public void AlongRail_MapsZeroToOneAcrossTheTravelSpan(double along, int expectedX)
    {
        var (x, _) = RailPlacement.Resting(BottomBar, CatW, CatH, along, ScreenW, ScreenH);
        Assert.Equal(expectedX, x);
    }

    [Theory]
    [InlineData(-5.0)]
    [InlineData(9.0)]
    public void AlongRail_IsClampedSoTheCatNeverLeavesTheBar(double along)
    {
        var (x, _) = RailPlacement.Resting(BottomBar, CatW, CatH, along, ScreenW, ScreenH);
        Assert.InRange(x, 0, ScreenW - CatW);
    }

    [Fact]
    public void TopBar_CatHangsBelowTheBar()
    {
        var top = new TaskbarRail(TaskbarEdge.Top, 0, 0, 1920, 60, false);
        var (_, y) = RailPlacement.Resting(top, CatW, CatH, 0.5, ScreenW, ScreenH);
        Assert.Equal(60, y);
    }

    [Fact]
    public void LeftBar_CatSitsOnTheInnerFace()
    {
        var left = new TaskbarRail(TaskbarEdge.Left, 0, 0, 80, 1080, false);
        var (x, _) = RailPlacement.Resting(left, CatW, CatH, 0.5, ScreenW, ScreenH);
        Assert.Equal(80, x);
    }

    [Fact]
    public void RightBar_CatSitsLeftOfTheBar()
    {
        var right = new TaskbarRail(TaskbarEdge.Right, 1840, 0, 1920, 1080, false);
        var (x, _) = RailPlacement.Resting(right, CatW, CatH, 0.5, ScreenW, ScreenH);
        Assert.Equal(1840 - CatW, x);
    }

    [Fact]
    public void BarNarrowerThanTheCat_DoesNotProduceNegativeTravel()
    {
        var tiny = new TaskbarRail(TaskbarEdge.Bottom, 0, 1020, 100, 1080, false);
        var (x, _) = RailPlacement.Resting(tiny, CatW, CatH, 1.0, ScreenW, ScreenH);
        Assert.Equal(0, x);
    }
}
