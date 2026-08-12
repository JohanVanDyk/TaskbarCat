namespace TaskbarCat.Services;

/// <summary>
/// How much wider and taller an overfed cat is drawn when the clip has no drawn chonk art and
/// has to be stretched instead.
///
/// Only six clips per level were ever drawn fat, and they are the ones the cat idles in. Every
/// other clip — all of toy mode, both walks, both runs, zoomies, the fright drop — comes through
/// here, so getting this wrong shows up as the cat visibly slimming the moment it moves.
///
/// **The stretch depends on the pose, not just the level.** Widening a cat that is stretched out
/// along the ground makes it longer, not fatter: a running cat at 1.5x width is a lean cat that
/// has been smeared sideways. The drawn art agrees — the one sprawled pose that was drawn fat
/// (sleep) grew barely at all across (~1.16) and mostly downward (~1.24), while the upright
/// poses grew ~1.5 across and ~1.18 down. So sprawls thicken perpendicular to their long axis.
///
/// The numbers are measured off the art, not chosen. Recipe, worth repeating if the sheets are
/// ever redrawn: take the mean opaque bounding box of every frame in
/// <c>assets/cat/orange_white/&lt;clip&gt;.png</c> and in <c>chonk&lt;level&gt;/&lt;clip&gt;.png</c>,
/// and divide. Upright measured W 1.44 / H 1.19 at level 2 and W 1.58 / H 1.18 at level 3;
/// sprawl (n=1, sleep) measured W 1.18 / H 1.29 and W 1.14 / H 1.20. Level 1 has no drawn art at
/// all and interpolates halfway to level 2, and the levels are pinned monotonic — hand-drawn
/// sheets vary a little either way, and a cat that shrank as it got fatter would read as a bug.
///
/// This is a stand-in, and an honest one only up to a point: no amount of stretching makes a
/// sprawled run pose look genuinely overfed. The real fix is drawn chonk sheets for the movement
/// clips, the same route the six idle clips already took — see docs/ANIMATION_PROMPT.md.
/// </summary>
public static class ChonkVisuals
{
    public static (double W, double H) Stretch(int level, bool sprawl = false) => sprawl
        ? level switch
        {
            1 => (1.07, 1.11),
            2 => (1.13, 1.21),
            3 => (1.16, 1.26),
            _ => (1.0, 1.0),
        }
        : level switch
        {
            1 => (1.22, 1.09),
            2 => (1.44, 1.18),
            3 => (1.58, 1.19),
            _ => (1.0, 1.0),
        };
}
