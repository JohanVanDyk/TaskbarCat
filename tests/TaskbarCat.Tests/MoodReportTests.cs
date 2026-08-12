using TaskbarCat.Models;
using TaskbarCat.Services;
using Xunit;

namespace TaskbarCat.Tests;

public class MoodReportTests
{
    private static Needs Fine() => new()
    {
        Fullness = 80,
        Cleanliness = 80,
        Affection = 50,
        Tiredness = 20,
    };

    [Fact]
    public void ContentCatWantsNothing()
    {
        var needs = Fine();
        Assert.Null(MoodReport.TopWant(needs));
        Assert.Equal("content", MoodReport.Phrase(needs));
        Assert.All(MoodReport.Gauges(needs), g => Assert.False(g.IsWanted));
    }

    [Fact]
    public void EveryGaugeReadsAsSatisfactionSoOneRendererFitsAll()
    {
        // Tiredness is stored as pressure; the gauge must invert it. Without this the rest
        // ring would fill up as the cat got more exhausted.
        var needs = Fine();
        needs.Tiredness = 90;

        var rest = Assert.Single(MoodReport.Gauges(needs), g => g.Kind == NeedKind.Rest);
        Assert.Equal(10, rest.Level, 3);
        Assert.True(rest.IsWanted);
    }

    [Fact]
    public void GaugeCarriesTheActionThatRefillsIt()
    {
        var gauges = MoodReport.Gauges(Fine());

        Assert.Equal("feed", Assert.Single(gauges, g => g.Kind == NeedKind.Hunger).MenuId);
        Assert.Equal("brush", Assert.Single(gauges, g => g.Kind == NeedKind.Grooming).MenuId);
        Assert.Equal("pet", Assert.Single(gauges, g => g.Kind == NeedKind.Affection).MenuId);

        // Rest has no button, and must not acquire one by accident: sleeping is the cat's
        // decision, not the user's.
        Assert.Null(Assert.Single(gauges, g => g.Kind == NeedKind.Rest).MenuId);
    }

    [Theory]
    [InlineData(10, Mood.Hungry)]
    [InlineData(34, Mood.Hungry)]
    public void HungerWantIsTheSameThresholdTheBehaviourUses(double fullness, Mood expected)
    {
        var needs = Fine();
        needs.Fullness = fullness;

        Assert.Equal(expected, new NeedsSimulator().DeriveMood(needs));
        Assert.Equal(NeedKind.Hunger, MoodReport.TopWant(needs)!.Value.Kind);
    }

    /// <summary>
    /// The whole point of the wheel marker: it must point at the need actually driving the
    /// cat, or it tells the user to do something that changes nothing on screen.
    /// </summary>
    [Fact]
    public void TopWantAgreesWithDerivedMoodAcrossTheGrid()
    {
        var sim = new NeedsSimulator();

        for (double fullness = 5; fullness <= 95; fullness += 10)
        for (double clean = 5; clean <= 95; clean += 10)
        for (double affection = 5; affection <= 95; affection += 10)
        for (double tired = 5; tired <= 95; tired += 10)
        {
            var needs = new Needs
            {
                Fullness = fullness,
                Cleanliness = clean,
                Affection = affection,
                Tiredness = tired,
            };

            var mood = sim.DeriveMood(needs);
            var want = MoodReport.TopWant(needs);

            var expected = mood switch
            {
                Mood.Sleepy => (NeedKind?)NeedKind.Rest,
                Mood.Hungry => NeedKind.Hunger,
                Mood.Dirty => NeedKind.Grooming,
                Mood.Affectionate => NeedKind.Affection,
                _ => null,
            };

            Assert.Equal(expected, want?.Kind);
        }
    }

    [Fact]
    public void SleepAndHungerTogetherLeadWithSleep()
    {
        // DeriveMood puts Sleepy first, so the phrase has to as well.
        var needs = Fine();
        needs.Tiredness = 90;
        needs.Fullness = 10;

        Assert.Equal(NeedKind.Rest, MoodReport.TopWant(needs)!.Value.Kind);
        Assert.Equal("exhausted and starving", MoodReport.Phrase(needs));
    }

    [Fact]
    public void PhraseListsAtMostTwoComplaints()
    {
        var needs = new Needs { Fullness = 5, Cleanliness = 5, Affection = 5, Tiredness = 95 };
        var phrase = MoodReport.Phrase(needs);

        Assert.Equal(1, phrase.Split(" and ").Length - 1);
    }

    [Fact]
    public void PlayfulCatSaysSoRatherThanJustContent()
    {
        var needs = Fine();
        needs.Affection = 85;
        needs.Tiredness = 20;

        Assert.Equal(Mood.Playful, new NeedsSimulator().DeriveMood(needs));
        Assert.Equal("up for a game", MoodReport.Phrase(needs));
    }

    [Fact]
    public void TrayTextFitsTheShellsSixtyThreeCharacterLimit()
    {
        var needs = new Needs { Fullness = 5, Cleanliness = 5, Affection = 5, Tiredness = 95 };
        var text = MoodReport.TrayText(new string('M', 60), needs);

        Assert.True(text.Length <= 63, $"tray text was {text.Length} chars");
    }

    [Fact]
    public void TrayTextNamesTheCat()
    {
        Assert.Equal("Mochi is content", MoodReport.TrayText("Mochi", Fine()));
        Assert.StartsWith("The cat is", MoodReport.TrayText("  ", Fine()));
    }
}
