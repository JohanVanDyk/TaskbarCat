namespace TaskbarCat.Models;

/// <summary>
/// Tamagotchi meters, all 0..100. Persisted verbatim to settings.json.
/// Spec rule: no death, no sadness — every value is clamped and nothing is terminal.
/// </summary>
public sealed class Needs
{
    /// <summary>0 = starving, 100 = full.</summary>
    public double Fullness { get; set; } = 70;

    /// <summary>0 = filthy, 100 = spotless.</summary>
    public double Cleanliness { get; set; } = 85;

    /// <summary>0 = aloof, 100 = devoted.</summary>
    public double Affection { get; set; } = 50;

    /// <summary>0 = skinny, 50 = ideal, 100 = chonk. Cosmetic only.</summary>
    public double Weight { get; set; } = 50;

    /// <summary>Sleep pressure. Rises while awake, falls while sleeping.</summary>
    public double Tiredness { get; set; } = 20;

    public bool IsHungry => Fullness < 35;
    public bool IsDirty => Cleanliness < 40;
    public bool IsNeedy => Affection < 30;
    public bool IsChonky => Weight > 75;

    public Needs Clone() => new()
    {
        Fullness = Fullness,
        Cleanliness = Cleanliness,
        Affection = Affection,
        Weight = Weight,
        Tiredness = Tiredness,
    };

    internal static double Clamp(double v) => v < 0 ? 0 : v > 100 ? 100 : v;
}
