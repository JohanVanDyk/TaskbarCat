namespace TaskbarCat.Services;

/// <summary>
/// Splits a wall-clock tick delta into the part the animation may see and the part that
/// must be charged to the needs meters as away-time.
///
/// The two consumers want opposite things from a large delta. Motion and animation need it
/// clamped: a resumed machine reporting a 4-hour delta would teleport the cat across the
/// taskbar and burn a whole clip in one frame. The needs simulator needs the opposite — if
/// the delta is silently clamped, an 8-hour laptop suspend ages the cat by one second and
/// it wakes up as full and rested as it went under, which is the bug this type exists to
/// prevent.
///
/// Lives in Core with no timer of its own so the resume path is a unit test rather than a
/// thing you can only verify by suspending a real machine.
/// </summary>
public static class TickBudget
{
    /// <summary>
    /// Longest delta handed to motion/animation. One second is already ~15 frames of
    /// catch-up; beyond that the cat visibly jumps.
    /// </summary>
    public static readonly TimeSpan MaxSimulated = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Deltas under this are ordinary scheduler jitter (GC pause, a busy machine); the excess
    /// over <see cref="MaxSimulated"/> is dropped, which at these rates costs the meters well
    /// under a hundredth of a point. Above it, the process was genuinely not running —
    /// suspend, hibernate, or a starved dispatcher — and the excess becomes away-time.
    /// </summary>
    public static readonly TimeSpan ResumeGap = TimeSpan.FromSeconds(5);

    /// <param name="simulated">Delta to advance motion, animation and the behaviour engine by.</param>
    /// <param name="away">
    /// Time to charge as offline decay, over and above <paramref name="simulated"/>.
    /// Zero on every normal tick.
    /// </param>
    public readonly record struct Split(TimeSpan Simulated, TimeSpan Away);

    public static Split For(TimeSpan raw)
    {
        if (raw <= TimeSpan.Zero) return new Split(TimeSpan.Zero, TimeSpan.Zero);
        if (raw <= ResumeGap) return new Split(raw <= MaxSimulated ? raw : MaxSimulated, TimeSpan.Zero);

        // The simulated slice is not free: subtract it so a resume is charged exactly once.
        return new Split(MaxSimulated, raw - MaxSimulated);
    }
}
