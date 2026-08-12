using TaskbarCat.Services;
using Xunit;

namespace TaskbarCat.Tests;

public class ChonkVisualsTests
{
    /// <summary>
    /// Measured off assets/cat/orange_white: the mean opaque bounding box of the drawn chonk
    /// sheets over the clips that have them, divided by the same measure on the normal sheets.
    /// A stretched clip has to land near these or the cat changes size depending on what it
    /// happens to be doing, which is the bug this table was wrong for.
    ///
    /// Sprawl figures come from one clip (sleep is the only drawn-fat pose that lies down), so
    /// they carry a wider tolerance than the five upright ones.
    /// </summary>
    [Theory]
    [InlineData(2, false, 1.44, 1.19, 0.03)]
    [InlineData(3, false, 1.58, 1.18, 0.03)]
    [InlineData(2, true, 1.18, 1.29, 0.09)]
    [InlineData(3, true, 1.14, 1.20, 0.09)]
    public void StretchMatchesTheDrawnArtItStandsInFor(
        int level, bool sprawl, double drawnW, double drawnH, double tolerance)
    {
        var (w, h) = ChonkVisuals.Stretch(level, sprawl);

        Assert.InRange(w, drawnW - tolerance, drawnW + tolerance);
        Assert.InRange(h, drawnH - tolerance, drawnH + tolerance);
    }

    /// <summary>
    /// The reason the pose matters at all. Widening a cat that is stretched out along the ground
    /// makes it longer, not fatter, so a sprawl must thicken downward and an upright pose across.
    /// Getting this backwards produced a running cat smeared to 1.5x its length.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SprawlsThickenDownwardAndUprightsAcross(int level)
    {
        var (uprightW, uprightH) = ChonkVisuals.Stretch(level, sprawl: false);
        var (sprawlW, sprawlH) = ChonkVisuals.Stretch(level, sprawl: true);

        Assert.True(uprightW > uprightH, $"upright level {level}: {uprightW} should exceed {uprightH}");
        Assert.True(sprawlH > sprawlW, $"sprawl level {level}: {sprawlH} should exceed {sprawlW}");

        // And a sprawl is never widened as hard as an upright pose, whatever the level.
        Assert.True(sprawlW < uprightW);
    }

    [Fact]
    public void NormalWeightIsNotStretched()
    {
        Assert.Equal((1.0, 1.0), ChonkVisuals.Stretch(0));
        Assert.Equal((1.0, 1.0), ChonkVisuals.Stretch(0, sprawl: true));

        // Levels the tracker cannot produce still have to be safe: a negative or over-max level
        // must not silently shrink the cat to nothing.
        Assert.Equal((1.0, 1.0), ChonkVisuals.Stretch(-1));
        Assert.Equal((1.0, 1.0), ChonkVisuals.Stretch(99, sprawl: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EachLevelIsBiggerThanTheOneBelow(bool sprawl)
    {
        for (int lvl = 1; lvl <= ChonkTracker.MaxLevel; lvl++)
        {
            var (w, h) = ChonkVisuals.Stretch(lvl, sprawl);
            var (pw, ph) = ChonkVisuals.Stretch(lvl - 1, sprawl);

            Assert.True(w >= pw, $"level {lvl} is narrower than {lvl - 1}");
            Assert.True(h >= ph, $"level {lvl} is shorter than {lvl - 1}");
            Assert.True(w > pw || h > ph, $"level {lvl} is the same size as {lvl - 1}");
        }
    }
}
