namespace TaskbarCat.Models;

/// <summary>What the cat is doing right now. Maps 1:1 onto a clip id via <see cref="ActionCatalog"/>.</summary>
public enum CatAction
{
    Sleep,
    Idle,
    SitLook,
    Loaf,
    Walk,
    Stretch,
    Groom,
    Play,
    Pounce,
    Scratch,
    Eat,
    Meow,
    Happy,
    WatchCursor,

    /// <summary>Got a fright. Only ever reached from a stimulus, never chosen at random.</summary>
    Startled,
}

public enum Facing
{
    Left,
    Right,
}

/// <summary>
/// Slow-changing disposition derived from <see cref="Needs"/>. Selects which weight
/// table the engine draws the next action from.
/// </summary>
public enum Mood
{
    Content,
    Sleepy,
    Hungry,
    Dirty,
    Playful,
    Affectionate,
}

/// <summary>External events pushed into the engine. Never call an animation directly from input.</summary>
public enum Stimulus
{
    Petted,
    Brushed,
    Fed,
    PlayToyOffered,
    CursorNearby,
    CursorLeft,
    TaskbarIconNearby,
    Woken,

    /// <summary>An auto-hide taskbar slid into view — something to hop up onto.</summary>
    TaskbarRose,

    /// <summary>It slid away again while the cat was awake enough to jump down deliberately.</summary>
    TaskbarFell,

    /// <summary>The floor vanished from under a sleeping cat.</summary>
    Startled,
}

/// <summary>The engine's output for one decision. The view layer only ever consumes this.</summary>
public readonly record struct BehaviourDecision(
    CatAction Action,
    Facing Facing,
    TimeSpan MinDuration,
    bool Interruptible,
    string? BubbleId = null);

/// <summary>Abstracted clock so a 24h behaviour simulation runs in milliseconds under test.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
