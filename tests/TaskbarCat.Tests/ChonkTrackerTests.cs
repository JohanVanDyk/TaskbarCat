using TaskbarCat.Services;
using Xunit;

namespace TaskbarCat.Tests;

public class ChonkTrackerTests
{
    private static ChonkTracker Fed(int times, ChonkTracker? t = null)
    {
        t ??= new ChonkTracker();
        for (int i = 0; i < times; i++) t.Fed();
        return t;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 0)]     // two is not enough
    [InlineData(3, 1)]
    [InlineData(6, 2)]
    [InlineData(9, 3)]
    public void EveryThreeFeedings_GainsASize(int feedings, int expected)
    {
        Assert.Equal(expected, Fed(feedings).Level);
    }

    [Fact]
    public void ChonkinessIsCapped()
    {
        Assert.Equal(ChonkTracker.MaxLevel, Fed(60).Level);
    }

    [Fact]
    public void SlimsDown_OneSizePerInterval()
    {
        var t = Fed(9);
        Assert.Equal(3, t.Level);

        t.Tick(ChonkTracker.SlimInterval);
        Assert.Equal(2, t.Level);

        t.Tick(ChonkTracker.SlimInterval);
        Assert.Equal(1, t.Level);

        t.Tick(ChonkTracker.SlimInterval);
        Assert.Equal(0, t.Level);
    }

    [Fact]
    public void JustUnderTheInterval_KeepsItsSize()
    {
        var t = Fed(3);
        t.Tick(ChonkTracker.SlimInterval - TimeSpan.FromSeconds(1));
        Assert.Equal(1, t.Level);
    }

    [Fact]
    public void PartialProgressAccumulates_AcrossTicks()
    {
        var t = Fed(3);
        for (int i = 0; i < 20; i++) t.Tick(TimeSpan.FromMinutes(1));
        Assert.Equal(0, t.Level);
    }

    [Fact]
    public void LongAwayTime_DropsEverySizeItShould()
    {
        // The resume path: an hour away is three intervals, not one.
        var t = Fed(9);
        t.Tick(TimeSpan.FromHours(1));
        Assert.Equal(0, t.Level);
    }

    [Fact]
    public void SlimmingPastZero_Stops()
    {
        var t = Fed(3);
        t.Tick(TimeSpan.FromDays(1));
        Assert.Equal(0, t.Level);
        Assert.Equal(TimeSpan.Zero, t.SinceChange);
    }

    [Fact]
    public void FeedingRestartsTheClock()
    {
        var t = Fed(3);
        t.Tick(ChonkTracker.SlimInterval - TimeSpan.FromMinutes(1));
        t.Fed();                                   // 19 minutes in, one more feeding
        t.Tick(TimeSpan.FromMinutes(2));           // would have slimmed without the feed

        Assert.Equal(1, t.Level);
    }

    [Fact]
    public void FeedingAtMaxSize_StillRestartsTheClock()
    {
        // A cat kept permanently stuffed must not shed a size on schedule anyway.
        var t = Fed(9);
        for (int i = 0; i < 10; i++)
        {
            t.Tick(ChonkTracker.SlimInterval - TimeSpan.FromMinutes(1));
            t.Fed();
        }
        Assert.Equal(3, t.Level);
    }

    [Fact]
    public void SlimmingDown_ClearsProgressTowardTheNextSize()
    {
        var t = Fed(3);       // level 1
        t.Fed();
        t.Fed();              // 2 banked toward level 2
        Assert.Equal(2, t.FeedsAtLevel);

        t.Tick(ChonkTracker.SlimInterval);

        Assert.Equal(0, t.Level);
        Assert.Equal(0, t.FeedsAtLevel);
    }

    [Fact]
    public void LevelChanged_FiresOnlyOnRealChanges()
    {
        var changes = new List<(int Level, bool Gained)>();
        var t = new ChonkTracker();
        t.LevelChanged += (lvl, gained) => changes.Add((lvl, gained));

        Fed(6, t);                                   // two gains
        t.Tick(TimeSpan.FromMinutes(5));             // nothing
        t.Tick(ChonkTracker.SlimInterval * 2);       // two losses

        Assert.Equal([(1, true), (2, true), (1, false), (0, false)], changes);
    }

    [Fact]
    public void OfflineSlimming_FiresLevelChanged_WhichIsWhyOrderingMatters()
    {
        // The app crashed on startup because this event fired from inside CatController's
        // constructor, before the fields its handler touches existed. The tracker is right to
        // raise it; the subscriber has to be attached after construction, not before.
        var t = new ChonkTracker(level: 3, feedsAtLevel: 0);
        int fired = 0;
        t.LevelChanged += (_, _) => fired++;

        t.Tick(TimeSpan.FromHours(1));

        Assert.Equal(3, fired);
        Assert.Equal(0, t.Level);
    }

    [Fact]
    public void RestoredState_ContinuesWhereItLeftOff()
    {
        // What a restart does: level and banked feedings come back from settings.json.
        var t = new ChonkTracker(level: 2, feedsAtLevel: 2, sinceChange: TimeSpan.FromMinutes(5));
        t.Fed();
        Assert.Equal(3, t.Level);
    }
}
