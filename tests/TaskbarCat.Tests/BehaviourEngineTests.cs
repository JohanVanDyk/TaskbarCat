using TaskbarCat.Models;
using TaskbarCat.Services;
using Xunit;

namespace TaskbarCat.Tests;

/// <summary>
/// The behaviour tuning harness. Weight/dwell numbers in <see cref="BehaviourEngine"/> are
/// retuned here, against measured output — not by watching the cat and guessing.
/// </summary>
public class BehaviourEngineTests
{
    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    /// <summary>Runs a simulated day and returns seconds spent in each action.</summary>
    private static Dictionary<CatAction, double> Simulate(
        TimeSpan span,
        TimeSpan? feedEvery = null,
        TimeSpan? petEvery = null,
        int seed = 7)
    {
        var clock = new FakeClock();
        var needs = new Needs();
        var sim = new NeedsSimulator();
        var engine = new BehaviourEngine(needs, sim, clock, new Random(seed));

        var time = new Dictionary<CatAction, double>();
        var elapsed = TimeSpan.Zero;
        var nextFeed = feedEvery;
        var nextPet = petEvery;

        // Stands in for AnimationPlayer: a one-shot clip reports completion only once it
        // has actually played for its dwell. Reporting every tick would cut one-shots to a
        // single frame and silently inflate the sleep share.
        var inCurrent = TimeSpan.Zero;
        engine.DecisionChanged += _ => inCurrent = TimeSpan.Zero;

        while (elapsed < span)
        {
            time[engine.Action] = time.GetValueOrDefault(engine.Action) + Tick.TotalSeconds;
            inCurrent += Tick;

            if (!engine.Current.Interruptible && inCurrent >= engine.Current.MinDuration)
                engine.OnClipCompleted();

            engine.Tick(Tick);
            elapsed += Tick;
            clock.UtcNow += Tick;

            if (nextFeed is { } f && elapsed >= f)
            {
                engine.Notify(Stimulus.Fed);
                nextFeed = f + feedEvery!.Value;
            }
            if (nextPet is { } p && elapsed >= p)
            {
                engine.Notify(Stimulus.Petted);
                nextPet = p + petEvery!.Value;
            }
        }
        return time;
    }

    private static double SleepShare(Dictionary<CatAction, double> time)
        => time.GetValueOrDefault(CatAction.Sleep) / time.Values.Sum();

    [Theory]
    [InlineData(null, null)]        // unattended
    [InlineData(4.0, 2.0)]          // light use
    [InlineData(2.0, 0.5)]          // heavy use
    public void SleepShare_IsWithinSpecBand(double? feedHours, double? petHours)
    {
        var time = Simulate(
            TimeSpan.FromHours(24),
            feedHours is { } f ? TimeSpan.FromHours(f) : null,
            petHours is { } p ? TimeSpan.FromHours(p) : null);

        double share = SleepShare(time);
        Assert.InRange(share, BehaviourEngine.TargetSleepShareLow, BehaviourEngine.TargetSleepShareHigh);
    }

    [Fact]
    public void EveryAction_HasAClip()
    {
        foreach (CatAction action in Enum.GetValues<CatAction>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ClipMap.ClipFor(action, Facing.Right)));
            Assert.False(string.IsNullOrWhiteSpace(ClipMap.ClipFor(action, Facing.Left)));
        }
    }

    /// <summary>
    /// Ticks until the cat falls asleep. The engine deliberately starts awake, so sleep
    /// tests must drive it there rather than rely on the opening action.
    /// </summary>
    private static BehaviourEngine SleepingEngine(int seed = 1)
    {
        var engine = new BehaviourEngine(new Needs { Tiredness = 95 }, new NeedsSimulator(),
            new FakeClock(), new Random(seed));

        for (int i = 0; i < 4000 && !engine.IsSleeping; i++)
            engine.Tick(Tick);

        Assert.True(engine.IsSleeping, "engine never chose Sleep despite a Sleepy mood");
        return engine;
    }

    [Fact]
    public void UserAction_InterruptsSleepImmediately()
    {
        var engine = SleepingEngine();

        engine.Notify(Stimulus.Fed);
        var decision = engine.Tick(Tick);

        Assert.NotNull(decision);
        Assert.Equal(CatAction.Eat, decision!.Value.Action);
    }

    [Fact]
    public void AmbientStimulus_DoesNotInterruptSleep()
    {
        var engine = SleepingEngine();

        engine.Notify(Stimulus.CursorNearby);
        engine.Tick(Tick);

        Assert.True(engine.IsSleeping);
    }

    [Fact]
    public void EngineStartsAwake_SoTheAppNeverOpensToAMotionlessCat()
    {
        var engine = new BehaviourEngine(new Needs(), new NeedsSimulator(), new FakeClock(), new Random(9));

        Assert.False(engine.IsSleeping);
        Assert.True(engine.Current.MinDuration < TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Regression: actions used to end the moment their CLIP finished. The art is only a
    /// few frames (eat = 5 frames at 10fps = 0.5s), so a 4-second groom lasted 0.4s and
    /// the cat twitched between actions. Dwell, not clip length, decides.
    /// </summary>
    [Fact]
    public void OneShotAction_RunsForItsFullDwell_EvenAfterTheClipEnds()
    {
        var engine = new BehaviourEngine(new Needs(), new NeedsSimulator(), new FakeClock(), new Random(3));
        engine.Notify(Stimulus.Brushed);
        engine.Tick(Tick);
        Assert.Equal(CatAction.Groom, engine.Action);

        // The clip finishes almost immediately; the action must not.
        engine.OnClipCompleted();

        var elapsed = TimeSpan.Zero;
        while (elapsed < TimeSpan.FromSeconds(3.0))
        {
            engine.Tick(Tick);
            elapsed += Tick;
            engine.OnClipCompleted();          // keeps finishing, over and over
        }
        Assert.Equal(CatAction.Groom, engine.Action);

        // ...but it does end once the dwell is genuinely spent.
        for (int i = 0; i < 12; i++) engine.Tick(Tick);
        Assert.NotEqual(CatAction.Groom, engine.Action);
    }

    [Fact]
    public void CutShort_EndsTheActionEarly()
    {
        var engine = new BehaviourEngine(new Needs(), new NeedsSimulator(), new FakeClock(), new Random(3));
        engine.Notify(Stimulus.Brushed);
        engine.Tick(Tick);
        Assert.Equal(CatAction.Groom, engine.Action);

        engine.CutShort();
        engine.Tick(Tick);

        Assert.NotEqual(CatAction.Groom, engine.Action);
    }

    [Fact]
    public void SelfGrooming_KeepsCleanlinessOffTheFloor_ButNotSpotless()
    {
        var needs = new Needs { Cleanliness = 20, Fullness = 90, Affection = 60 };
        var sim = new NeedsSimulator();
        for (int i = 0; i < 20; i++) sim.OnActionCompleted(needs, CatAction.Groom);

        Assert.InRange(needs.Cleanliness, 60, 65);   // ceiling holds; brushing is still required
    }

    [Fact]
    public void OfflineDecay_IsCappedSoLongAbsencesAreSurvivable()
    {
        var sim = new NeedsSimulator();
        var week = new Needs { Fullness = 100 };
        var capped = new Needs { Fullness = 100 };

        sim.ApplyOffline(week, TimeSpan.FromDays(7));
        sim.ApplyOffline(capped, NeedsSimulator.MaxOfflineDecay);

        Assert.Equal(capped.Fullness, week.Fullness, 3);
        Assert.True(week.Fullness > 0);
    }
}
