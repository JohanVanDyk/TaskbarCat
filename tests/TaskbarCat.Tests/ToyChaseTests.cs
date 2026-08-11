using TaskbarCat.Models;
using TaskbarCat.Services;
using Xunit;

namespace TaskbarCat.Tests;

public class ToyChaseTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(33);

    /// <summary>Cat sitting mid-rail on a 1920 bottom bar: centre 960, head 920, feet 1080.</summary>
    private static ToyFrame At(double toyX, double toyY) =>
        new(toyX, toyY, CatCentreX: 960, CatTop: 920, CatBottom: 1080, RailLeft: 0, RailRight: 1920);

    private static ToyChase Run(ToyChase c, ToyKind kind, ToyFrame f, int frames)
    {
        for (int i = 0; i < frames; i++) c.Update(Frame, kind, f);
        return c;
    }

    [Fact]
    public void ToyAcrossTheScreen_IsChased()
    {
        var d = new ToyChase().Update(Frame, ToyKind.Yarn, At(1700, 1000));

        Assert.Equal(ToyResponse.Chase, d.Response);
        Assert.Equal(1700.0 / 1920, d.TargetAlong, 3);
    }

    [Fact]
    public void ChaseTarget_IsClampedToTheRail()
    {
        // The pointer can be past the end of the bar; the cat cannot.
        var d = new ToyChase().Update(Frame, ToyKind.Yarn, At(-500, 1000));
        Assert.Equal(0, d.TargetAlong, 3);
    }

    [Fact]
    public void ToyHeldOverhead_MakesItRearUp_NotCatch()
    {
        // A toy dangled above the cat is the one it can never have.
        var d = new ToyChase().Update(Frame, ToyKind.Yarn, At(960, 820));

        Assert.Equal(ToyResponse.ReachUp, d.Response);
    }

    [Fact]
    public void ToyFarAboveTheCat_IsNotWorthReachingFor()
    {
        var d = new ToyChase().Update(Frame, ToyKind.Yarn, At(960, 300));
        Assert.Equal(ToyResponse.Chase, d.Response);
    }

    [Fact]
    public void YarnAtCatHeight_IsCaughtAndPlayedWith()
    {
        var d = new ToyChase().Update(Frame, ToyKind.Yarn, At(960, 1000));
        Assert.Equal(ToyResponse.Play, d.Response);
    }

    [Fact]
    public void Laser_PouncedOn_ThenLeavesTheCatConfused()
    {
        // The joke: catching light always "works" and always fails.
        var c = new ToyChase();
        var f = At(960, 1000);

        Assert.Equal(ToyResponse.Pounce, c.Update(Frame, ToyKind.Laser, f).Response);

        Run(c, ToyKind.Laser, f, (int)(ToyChase.PounceDuration / Frame) + 1);
        Assert.Equal(ToyResponse.Confused, c.Response);

        Run(c, ToyKind.Laser, f, (int)(ToyChase.ConfusedDuration / Frame) + 1);
        Assert.Equal(ToyResponse.Chase, c.Response);
    }

    [Fact]
    public void ReactionRunsToCompletion_EvenIfTheToyRunsAway()
    {
        // Yanking the pointer away mid-pounce must not cancel it, or the laser gag never lands.
        var c = new ToyChase();
        c.Update(Frame, ToyKind.Laser, At(960, 1000));

        var d = c.Update(Frame, ToyKind.Laser, At(1800, 1000));

        Assert.Equal(ToyResponse.Pounce, d.Response);
        Assert.True(c.InReaction);
    }

    [Fact]
    public void AfterACatch_ItDoesNotImmediatelyCatchAgain()
    {
        // A stationary pointer would otherwise re-trigger every frame and the cat would
        // pounce on the spot forever.
        var c = new ToyChase();
        var f = At(960, 1000);

        c.Update(Frame, ToyKind.Yarn, f);
        Run(c, ToyKind.Yarn, f, (int)(ToyChase.PlayDuration / Frame) + 2);

        Assert.Equal(ToyResponse.Chase, c.Update(Frame, ToyKind.Yarn, f).Response);
    }

    [Fact]
    public void OnceTheCooldownExpires_ItCanCatchAgain()
    {
        var c = new ToyChase();
        var f = At(960, 1000);

        c.Update(Frame, ToyKind.Yarn, f);
        Run(c, ToyKind.Yarn, f, (int)((ToyChase.PlayDuration + ToyChase.Cooldown) / Frame) + 4);

        Assert.Equal(ToyResponse.Play, c.Update(Frame, ToyKind.Yarn, f).Response);
    }

    [Fact]
    public void Reset_ClearsAReactionInFlight()
    {
        // Cancelling toy mode mid-pounce must not leave the cat stuck in it.
        var c = new ToyChase();
        c.Update(Frame, ToyKind.Laser, At(960, 1000));
        Assert.True(c.InReaction);

        c.Reset();

        Assert.False(c.InReaction);
        Assert.Equal(ToyResponse.Chase, c.Response);
    }
}
