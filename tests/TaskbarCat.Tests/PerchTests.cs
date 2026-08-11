using TaskbarCat.Models;
using TaskbarCat.Services;
using Xunit;

namespace TaskbarCat.Tests;

public class PerchTests
{
    private const int ScreenW = 1920, ScreenH = 1080;
    private const int CatW = 200, CatH = 160;

    private static TaskbarRail BottomAutoHide => new(TaskbarEdge.Bottom, 0, 1020, 1920, 1080, IsAutoHide: true);
    private static TaskbarRail TopAutoHide => new(TaskbarEdge.Top, 0, 0, 1920, 60, IsAutoHide: true);

    [Fact]
    public void HiddenBar_CatStandsOnTheScreenEdge()
    {
        var (_, y) = RailPlacement.Resting(BottomAutoHide, CatW, CatH, 0.5, ScreenW, ScreenH, barRevealed: false);
        Assert.Equal(ScreenH - CatH, y);
    }

    [Fact]
    public void RevealedBar_CatStandsOnTopOfIt()
    {
        // The bug in the screenshot: the bar slid up and covered a cat still sitting at the
        // screen edge. On top means its feet are on the bar's top edge.
        var (_, y) = RailPlacement.Resting(BottomAutoHide, CatW, CatH, 0.5, ScreenW, ScreenH, barRevealed: true);
        Assert.Equal(1020 - CatH, y);
    }

    [Fact]
    public void RevealedBar_LiftsTheCatByExactlyTheBarHeight()
    {
        var hidden = RailPlacement.Resting(BottomAutoHide, CatW, CatH, 0.5, ScreenW, ScreenH, false);
        var shown = RailPlacement.Resting(BottomAutoHide, CatW, CatH, 0.5, ScreenW, ScreenH, true);

        Assert.Equal(BottomAutoHide.Height, hidden.Y - shown.Y);
        Assert.Equal(hidden.X, shown.X);      // only the perch changes, not the position along it
    }

    [Fact]
    public void TopBar_MovesTheCatDown_NotUp()
    {
        var hidden = RailPlacement.Resting(TopAutoHide, CatW, CatH, 0.5, ScreenW, ScreenH, false);
        var shown = RailPlacement.Resting(TopAutoHide, CatW, CatH, 0.5, ScreenW, ScreenH, true);

        Assert.Equal(0, hidden.Y);
        Assert.Equal(TopAutoHide.Bottom, shown.Y);
        Assert.True(shown.Y > hidden.Y, "a revealed top bar should push the cat downward");
    }

    // ---- the animator -------------------------------------------------------

    [Fact]
    public void Snap_IsImmediate()
    {
        var a = new PerchAnimator();
        a.Snap(1);
        Assert.Equal(1, a.Progress, 3);
        Assert.False(a.IsMoving);
    }

    [Fact]
    public void JumpUp_ReachesTheTop()
    {
        var a = new PerchAnimator();
        a.MoveTo(1, PerchAnimator.JumpUpDuration);

        for (int i = 0; i < 60 && a.IsMoving; i++) a.Tick(TimeSpan.FromMilliseconds(16));

        Assert.False(a.IsMoving);
        Assert.Equal(1, a.Progress, 3);
    }

    [Fact]
    public void JumpUp_OvershootsOnTheWay()
    {
        // The spring at the top of the jump. Without it the cat slides up like a lift.
        var a = new PerchAnimator();
        a.MoveTo(1, PerchAnimator.JumpUpDuration);

        double peak = 0;
        while (a.IsMoving)
        {
            a.Tick(TimeSpan.FromMilliseconds(8));
            peak = Math.Max(peak, a.Progress);
        }

        Assert.True(peak > 1.0, $"expected an overshoot past the bar, peaked at {peak:F3}");
    }

    [Fact]
    public void Falling_Accelerates()
    {
        // Gravity: the first half of the drop covers less ground than the second.
        var a = new PerchAnimator();
        a.Snap(1);
        a.MoveTo(0, PerchAnimator.FallDownDuration);

        var half = PerchAnimator.FallDownDuration / 2;
        a.Tick(half);
        double movedFirstHalf = 1 - a.Progress;

        Assert.True(movedFirstHalf < 0.5, $"fall should start slow, covered {movedFirstHalf:P0} in the first half");
    }

    [Fact]
    public void StartledDrop_IsFasterThanADeliberateOne()
    {
        Assert.True(PerchAnimator.StartledDropDuration < PerchAnimator.FallDownDuration);
    }

    [Fact]
    public void RetargetingMidFlight_StartsFromWhereItIs()
    {
        // The bar can vanish while the cat is still jumping onto it.
        var a = new PerchAnimator();
        a.MoveTo(1, PerchAnimator.JumpUpDuration);
        a.Tick(TimeSpan.FromMilliseconds(80));
        double mid = a.Progress;

        a.MoveTo(0, PerchAnimator.FallDownDuration);
        a.Tick(TimeSpan.FromMilliseconds(1));

        Assert.True(a.Progress < mid, "should fall from where it was, not snap to the top first");
    }
}
