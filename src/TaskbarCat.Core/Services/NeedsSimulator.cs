using TaskbarCat.Models;

namespace TaskbarCat.Services;

/// <summary>
/// Advances the Tamagotchi meters. No WPF, no timers of its own — the caller
/// supplies elapsed time, which makes a 24h simulation a unit test.
/// </summary>
public sealed class NeedsSimulator
{
    /// <summary>
    /// Ceiling on decay applied for time the app was closed. Without this, a week
    /// away pins every meter at rock bottom and the cat reads as broken rather than
    /// neglected. The spec's "no death or sadness" rule makes clamping safe.
    /// </summary>
    public static readonly TimeSpan MaxOfflineDecay = TimeSpan.FromHours(12);

    // Per-hour rates, tuned so a cat left alone overnight is hungry and scruffy
    // by morning but never bottomed out.
    private const double FullnessDecayPerHour = 6.0;
    private const double CleanlinessDecayPerHour = 2.5;
    private const double AffectionDecayPerHour = 3.0;
    private const double TirednessGainPerHour = 8.0;
    private const double TirednessDrainPerHour = 26.0;
    private const double WeightDriftPerHour = 0.35;

    private const double IdealWeight = 50.0;

    /// <summary>
    /// Self-grooming stops here. Deliberate: it keeps an ignored cat from bottoming out
    /// at "filthy" forever, but only the user's brush gets it spotless — so the Brush
    /// action keeps a visible payoff instead of being cosmetic.
    /// </summary>
    private const double SelfGroomCeiling = 65.0;

    public void Advance(Needs needs, TimeSpan elapsed, bool isSleeping)
    {
        if (elapsed <= TimeSpan.Zero) return;
        double h = elapsed.TotalHours;

        needs.Fullness = Needs.Clamp(needs.Fullness - FullnessDecayPerHour * h);
        needs.Cleanliness = Needs.Clamp(needs.Cleanliness - CleanlinessDecayPerHour * h);
        needs.Affection = Needs.Clamp(needs.Affection - AffectionDecayPerHour * h);

        needs.Tiredness = Needs.Clamp(needs.Tiredness +
            (isSleeping ? -TirednessDrainPerHour : TirednessGainPerHour) * h);

        // Weight tracks feeding: overfed drifts up, underfed drifts back toward ideal.
        double target = needs.Fullness > 85 ? 100 : needs.Fullness < 30 ? 30 : IdealWeight;
        double step = WeightDriftPerHour * h;
        needs.Weight = Needs.Clamp(needs.Weight + Math.Clamp(target - needs.Weight, -step, step));
    }

    /// <summary>
    /// Catch-up decay for time the app was not running, capped at <see cref="MaxOfflineDecay"/>.
    /// Assumes the cat slept through it.
    /// </summary>
    public void ApplyOffline(Needs needs, TimeSpan awaySpan)
    {
        if (awaySpan <= TimeSpan.Zero) return;
        Advance(needs, awaySpan > MaxOfflineDecay ? MaxOfflineDecay : awaySpan, isSleeping: true);
    }

    public void Apply(Needs needs, Stimulus stimulus)
    {
        switch (stimulus)
        {
            case Stimulus.Fed:
                needs.Fullness = Needs.Clamp(needs.Fullness + 30);
                needs.Affection = Needs.Clamp(needs.Affection + 4);
                break;
            case Stimulus.Brushed:
                needs.Cleanliness = Needs.Clamp(needs.Cleanliness + 35);
                needs.Affection = Needs.Clamp(needs.Affection + 6);
                break;
            case Stimulus.Petted:
                needs.Affection = Needs.Clamp(needs.Affection + 10);
                break;
            case Stimulus.PlayToyOffered:
                needs.Affection = Needs.Clamp(needs.Affection + 8);
                needs.Tiredness = Needs.Clamp(needs.Tiredness + 5);
                needs.Weight = Needs.Clamp(needs.Weight - 1);
                break;
            case Stimulus.Woken:
                needs.Tiredness = Needs.Clamp(needs.Tiredness + 5);
                break;
        }
    }

    /// <summary>
    /// Payoff for an action the cat just finished on its own. Without this, self-grooming
    /// is pure animation and cleanliness decays to zero no matter how attentive the user is.
    /// </summary>
    public void OnActionCompleted(Needs needs, CatAction action)
    {
        switch (action)
        {
            case CatAction.Groom:
                if (needs.Cleanliness < SelfGroomCeiling)
                    needs.Cleanliness = Needs.Clamp(Math.Min(SelfGroomCeiling, needs.Cleanliness + 9));
                break;
            case CatAction.Play or CatAction.Pounce:
                needs.Weight = Needs.Clamp(needs.Weight - 0.5);
                needs.Tiredness = Needs.Clamp(needs.Tiredness + 3);
                break;
            case CatAction.Scratch:
                needs.Cleanliness = Needs.Clamp(needs.Cleanliness - 1);
                break;
        }
    }

    /// <summary>
    /// Derives mood from needs. Order is priority order: the most pressing need wins,
    /// so a hungry-and-dirty cat begs before it grooms.
    /// </summary>
    public Mood DeriveMood(Needs needs) => needs switch
    {
        { Tiredness: > 75 } => Mood.Sleepy,
        { IsHungry: true } => Mood.Hungry,
        { IsDirty: true } => Mood.Dirty,
        { IsNeedy: true } => Mood.Affectionate,
        { Affection: > 70, Tiredness: < 45 } => Mood.Playful,
        _ => Mood.Content,
    };
}
