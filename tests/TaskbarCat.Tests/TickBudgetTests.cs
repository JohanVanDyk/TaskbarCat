using TaskbarCat.Models;
using TaskbarCat.Services;
using Xunit;

namespace TaskbarCat.Tests;

/// <summary>
/// The resume path. Verifiable here precisely because the split is a pure function —
/// otherwise the only way to test it would be to suspend the machine.
/// </summary>
public class TickBudgetTests
{
    [Fact]
    public void NormalTick_PassesThrough_WithNoAwayTime()
    {
        var split = TickBudget.For(TimeSpan.FromMilliseconds(66));

        Assert.Equal(TimeSpan.FromMilliseconds(66), split.Simulated);
        Assert.Equal(TimeSpan.Zero, split.Away);
    }

    [Fact]
    public void BriefStall_IsClampedButNotChargedAsAway()
    {
        // A GC pause or a busy machine, not a suspend.
        var split = TickBudget.For(TimeSpan.FromSeconds(3));

        Assert.Equal(TickBudget.MaxSimulated, split.Simulated);
        Assert.Equal(TimeSpan.Zero, split.Away);
    }

    [Fact]
    public void Resume_ClampsAnimation_ButChargesTheRestAsAway()
    {
        var raw = TimeSpan.FromHours(8);

        var split = TickBudget.For(raw);

        Assert.Equal(TickBudget.MaxSimulated, split.Simulated);
        // Charged exactly once: the simulated slice is not also billed as away-time.
        Assert.Equal(raw - TickBudget.MaxSimulated, split.Away);
    }

    [Fact]
    public void ClockGoingBackwards_IsIgnored()
    {
        // NTP correction or a manual clock change can hand back a negative delta.
        var split = TickBudget.For(TimeSpan.FromMinutes(-5));

        Assert.Equal(TimeSpan.Zero, split.Simulated);
        Assert.Equal(TimeSpan.Zero, split.Away);
    }

    [Fact]
    public void OvernightSuspend_LeavesTheCatHungry()
    {
        // The regression this whole type exists for: before the split, an 8h suspend advanced
        // the meters by one clamped second and the cat woke up exactly as it went under.
        var sim = new NeedsSimulator();
        var needs = new Needs { Fullness = 90, Tiredness = 60 };

        var split = TickBudget.For(TimeSpan.FromHours(8));
        sim.ApplyOffline(needs, split.Away);
        sim.Advance(needs, split.Simulated, isSleeping: true);

        // 8h at the fullness decay rate, not the ~0 the clamped-only path produced.
        Assert.Equal(90 - 6.0 * 8, needs.Fullness, 1);
        Assert.True(needs.Tiredness < 20, $"expected a rested cat, got Tiredness={needs.Tiredness}");
    }

    [Fact]
    public void AwayTime_IsCappedByTheOfflineCeiling()
    {
        // A week hibernating must not read as a broken cat, same rule as a week closed.
        var sim = new NeedsSimulator();
        var week = new Needs { Fullness = 100 };
        var ceiling = new Needs { Fullness = 100 };

        sim.ApplyOffline(week, TickBudget.For(TimeSpan.FromDays(7)).Away);
        sim.ApplyOffline(ceiling, NeedsSimulator.MaxOfflineDecay);

        Assert.Equal(ceiling.Fullness, week.Fullness, 3);
    }
}
