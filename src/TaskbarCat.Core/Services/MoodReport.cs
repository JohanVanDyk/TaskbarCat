using TaskbarCat.Models;

namespace TaskbarCat.Services;

/// <summary>Which meter a gauge is showing, and therefore which action refills it.</summary>
public enum NeedKind
{
    Rest,
    Hunger,
    Grooming,
    Affection,
}

/// <summary>
/// One meter, already turned into something a UI can draw without knowing the rules.
///
/// <see cref="Level"/> is SATISFACTION, not pressure: 100 is always good, for every kind.
/// Tiredness is stored the other way round and is inverted here precisely so a renderer can
/// treat all four identically — a ring that fills clockwise and reddens as it empties.
/// </summary>
public readonly record struct NeedGauge(
    NeedKind Kind,
    string Label,
    double Level,
    bool IsWanted,
    string? MenuId,
    string Phrase);

/// <summary>
/// Everything a UI needs to show the cat's state, resolved once. Handed to both the tray and
/// the wheel so the tooltip and the marked button can never contradict each other.
/// </summary>
public sealed record MoodSnapshot(
    IReadOnlyList<NeedGauge> Gauges,
    NeedGauge? TopWant,
    string Phrase,
    string Headline);

/// <summary>
/// The cat's needs, rendered for a human. Pure: no WPF, no clock, no I/O.
///
/// This exists because every meter driving the cat's behaviour was invisible — the only way
/// to see why it was sulking was to run the self-test and read a file. It deliberately
/// produces both a one-line phrase (tray tooltip) and per-action gauges (radial menu), from
/// one set of rules, so the two can never disagree about what the cat wants.
///
/// Priority order is <see cref="NeedsSimulator.DeriveMood"/>'s order, and
/// <c>MoodReportTests</c> pins them together: the wheel must point at the need that is
/// actually driving what the cat is doing, or the marker is a lie.
/// </summary>
public static class MoodReport
{
    /// <summary>Tiredness above this is "sleepy" — the threshold DeriveMood uses.</summary>
    private const double SleepyPressure = 75;

    public static IReadOnlyList<NeedGauge> Gauges(Needs needs) => new[]
    {
        // Order here is arc order (Feed, Brush, Pet on the wheel), not priority order.
        new NeedGauge(NeedKind.Hunger, "Hunger", needs.Fullness, needs.IsHungry, "feed",
            Describe(needs.Fullness, "starving", "hungry", "peckish")),
        new NeedGauge(NeedKind.Grooming, "Coat", needs.Cleanliness, needs.IsDirty, "brush",
            Describe(needs.Cleanliness, "filthy", "grubby", "a bit scruffy")),
        new NeedGauge(NeedKind.Affection, "Affection", needs.Affection, needs.IsNeedy, "pet",
            Describe(needs.Affection, "starved of attention", "lonely", "after a fuss")),
        // Rest has no MenuId on purpose: there is no "make it sleep" button, and inventing one
        // would let the user override the one thing the cat is supposed to decide for itself.
        new NeedGauge(NeedKind.Rest, "Rest", 100 - needs.Tiredness, needs.Tiredness > SleepyPressure, null,
            Describe(100 - needs.Tiredness, "exhausted", "sleepy", "drowsy")),
    };

    /// <summary>
    /// The single most pressing need, or null when nothing is wanted. Ties break in
    /// DeriveMood's priority order — sleepy, then hungry, then dirty, then needy — rather
    /// than by which meter happens to be lowest, so the wheel agrees with the behaviour.
    /// </summary>
    public static NeedGauge? TopWant(Needs needs)
    {
        var gauges = Gauges(needs);
        foreach (var kind in new[] { NeedKind.Rest, NeedKind.Hunger, NeedKind.Grooming, NeedKind.Affection })
        {
            foreach (var g in gauges)
                if (g.Kind == kind && g.IsWanted) return g;
        }

        return null;
    }

    /// <summary>
    /// One line for the tray tooltip: "hungry and a bit scruffy", or "content".
    ///
    /// Two wants at most. A cat that lists four complaints reads as a status bar, and the
    /// tooltip is capped at 63 characters by the shell anyway.
    /// </summary>
    public static string Phrase(Needs needs)
    {
        var wanted = new List<NeedGauge>();
        foreach (var kind in new[] { NeedKind.Rest, NeedKind.Hunger, NeedKind.Grooming, NeedKind.Affection })
        {
            foreach (var g in Gauges(needs))
                if (g.Kind == kind && g.IsWanted) wanted.Add(g);
        }

        return wanted.Count switch
        {
            0 => needs is { Affection: > 70, Tiredness: < 45 } ? "up for a game" : "content",
            1 => wanted[0].Phrase,
            _ => $"{wanted[0].Phrase} and {wanted[1].Phrase}",
        };
    }

    /// <summary>Tray tooltip text. NotifyIcon.Text throws above 63 characters.</summary>
    public static string TrayText(string name, Needs needs)
    {
        var who = string.IsNullOrWhiteSpace(name) ? "The cat" : name.Trim();
        var text = $"{who} is {Phrase(needs)}";
        return text.Length <= 63 ? text : text[..63];
    }

    /// <summary>One resolve of everything a UI shows, so callers do not each recompute it.</summary>
    public static MoodSnapshot Snapshot(string name, Needs needs) =>
        new(Gauges(needs), TopWant(needs), Phrase(needs), TrayText(name, needs));

    private static string Describe(double level, string dire, string bad, string mild) =>
        level < 15 ? dire : level < 30 ? bad : mild;
}
