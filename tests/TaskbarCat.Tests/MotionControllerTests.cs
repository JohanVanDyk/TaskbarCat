using TaskbarCat.Models;
using TaskbarCat.Services;
using Xunit;

namespace TaskbarCat.Tests;

public class MotionControllerTests
{
    private static MotionController Make(double span = 1000, int seed = 5)
    {
        var m = new MotionController(new Random(seed));
        m.SetSpan(span);
        return m;
    }

    private static void Run(MotionController m, double seconds, double step = 0.05)
    {
        for (double t = 0; t < seconds; t += step)
            m.Tick(TimeSpan.FromSeconds(step));
    }

    [Fact]
    public void Walk_MovesTheCatAndThenStops()
    {
        var m = Make();
        m.PlaceAt(0.5);
        double start = m.Position;

        m.StartWalk(Facing.Right);
        Assert.True(m.IsWalking);

        Run(m, 12);

        Assert.False(m.IsWalking);
        Assert.True(m.Position > start);
    }

    [Fact]
    public void Walk_NeverLeavesTheRail()
    {
        var m = Make(span: 400);
        m.PlaceAt(1.0);

        for (int i = 0; i < 40; i++)
        {
            m.StartWalk();
            Run(m, 12);
            Assert.InRange(m.Position, 0, 400);
            Assert.InRange(m.Along, 0.0, 1.0);
        }
    }

    [Fact]
    public void Walk_AtTheEnd_TurnsAround()
    {
        var m = Make(span: 500);
        m.PlaceAt(1.0);            // hard against the right end

        m.StartWalk(Facing.Right); // asked to keep going right, but there is no room
        Assert.Equal(Facing.Left, m.Facing);
    }

    [Fact]
    public void SetSpan_KeepsProportionalPosition_NotPixelOffset()
    {
        // The taskbar shrinking must not fling the cat to a different relative spot.
        var m = Make(span: 1000);
        m.PlaceAt(0.25);

        m.SetSpan(600);

        Assert.Equal(0.25, m.Along, 3);
        Assert.Equal(150, m.Position, 3);
    }

    [Fact]
    public void ZeroSpan_IsSurvivable()
    {
        var m = Make(span: 0);
        m.StartWalk(Facing.Right);
        Run(m, 2);

        Assert.False(m.IsWalking);
        Assert.Equal(0, m.Along);
    }

    [Fact]
    public void Tick_WithoutWalking_DoesNotMove()
    {
        var m = Make();
        m.PlaceAt(0.4);
        double before = m.Position;

        Assert.False(m.Tick(TimeSpan.FromSeconds(1)));
        Assert.Equal(before, m.Position);
    }

    [Fact]
    public void Speed_IsRespected()
    {
        var m = Make(span: 10_000);
        m.PlaceAt(0.0);
        m.SpeedPixelsPerSecond = 100;
        m.StartWalk(Facing.Right);

        m.Tick(TimeSpan.FromSeconds(0.5));

        // 100 px/s for 0.5s = 50px, unless the randomly chosen target was nearer.
        Assert.InRange(m.Position, 0, 50);
    }

    /// <summary>
    /// The contract toy mode's run clip depends on: carrying the toy past the cat must flip
    /// Facing, on the retarget and on every tick after it. The clip is chosen from this, and a
    /// chase that ignored it had the cat running backwards after the pointer.
    /// </summary>
    [Fact]
    public void WalkTo_AcrossTheCat_FlipsFacing()
    {
        var m = Make(span: 1000);
        m.SpeedPixelsPerSecond = 200;
        m.PlaceAt(0.5);

        m.WalkTo(0.9);
        Assert.Equal(Facing.Right, m.Facing);

        // The toy is carried to the cat's other side.
        m.WalkTo(0.1);
        Assert.Equal(Facing.Left, m.Facing);

        m.Tick(TimeSpan.FromSeconds(0.1));
        Assert.Equal(Facing.Left, m.Facing);
    }

    /// <summary>
    /// A toy hovering on top of the cat must not flip it: WalkTo inside ArrivalSlack leaves
    /// facing alone, which is what stops the run clip strobing left/right every frame.
    /// </summary>
    [Fact]
    public void WalkTo_WithinArrivalSlack_LeavesFacingAlone()
    {
        var m = Make(span: 1000);
        m.PlaceAt(0.5);
        m.WalkTo(0.9);
        Assert.Equal(Facing.Right, m.Facing);

        m.WalkTo(0.5 - 0.005);   // 5px away on a 1000px rail: inside the 12px slack
        Assert.Equal(Facing.Right, m.Facing);
        Assert.False(m.IsWalking);
    }
}
