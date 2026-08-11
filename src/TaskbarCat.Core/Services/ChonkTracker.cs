namespace TaskbarCat.Services;

/// <summary>
/// How fat the cat currently is, from overfeeding.
///
/// Every <see cref="FeedingsPerLevel"/> feedings the cat goes up a size, to a maximum of
/// <see cref="MaxLevel"/>. It then works back down one size per <see cref="SlimInterval"/>
/// without being fed.
///
/// No WPF and no timer of its own — the caller supplies elapsed time — so the twenty minute
/// intervals are a unit test rather than a twenty minute wait.
/// </summary>
public sealed class ChonkTracker
{
    public const int FeedingsPerLevel = 3;
    public const int MaxLevel = 3;

    /// <summary>Time at a level before dropping to the next one down.</summary>
    public static readonly TimeSpan SlimInterval = TimeSpan.FromMinutes(20);

    private TimeSpan _sinceChange;

    public ChonkTracker(int level = 0, int feedsAtLevel = 0, TimeSpan? sinceChange = null)
    {
        Level = Math.Clamp(level, 0, MaxLevel);
        FeedsAtLevel = Math.Clamp(feedsAtLevel, 0, FeedingsPerLevel - 1);
        _sinceChange = sinceChange ?? TimeSpan.Zero;
    }

    /// <summary>0 = the cat's normal build, 3 = chonkiest.</summary>
    public int Level { get; private set; }

    /// <summary>Feedings banked toward the next size. Resets on every level change.</summary>
    public int FeedsAtLevel { get; private set; }

    /// <summary>Time since the size last changed, i.e. progress toward slimming down.</summary>
    public TimeSpan SinceChange => _sinceChange;

    /// <summary>Raised only when the size actually changes: (newLevel, gainedWeight).</summary>
    public event Action<int, bool>? LevelChanged;

    public void Fed()
    {
        // Feeding always restarts the slim-down clock, even at max size. Otherwise a cat kept
        // permanently stuffed would still shed a size every twenty minutes.
        _sinceChange = TimeSpan.Zero;

        if (Level >= MaxLevel) return;

        if (++FeedsAtLevel < FeedingsPerLevel) return;

        FeedsAtLevel = 0;
        Level++;
        LevelChanged?.Invoke(Level, true);
    }

    /// <summary>
    /// Advances the slim-down clock. Handles multi-level catch-up in one call, so an hour of
    /// away-time drops three sizes rather than one.
    /// </summary>
    public void Tick(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero || Level <= 0) return;

        _sinceChange += elapsed;

        while (Level > 0 && _sinceChange >= SlimInterval)
        {
            _sinceChange -= SlimInterval;
            Level--;
            // Half-finished progress toward the next size up does not survive slimming down.
            FeedsAtLevel = 0;
            LevelChanged?.Invoke(Level, false);
        }

        if (Level == 0) _sinceChange = TimeSpan.Zero;
    }
}
