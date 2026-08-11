using TaskbarCat.Models;

namespace TaskbarCat.Services;

/// <summary>
/// Decides WHAT the cat does. It never decides where the window sits (MotionController)
/// or which frame is on screen (AnimationPlayer), and it references no WPF type — that
/// separation is what keeps a second cat a loop instead of a rewrite.
///
/// Two tiers:
///   Mood   — slow, derived from Needs, picks a weight table.
///   Action — fast, drawn from that table when the current action's dwell expires.
///
/// Determinism: all randomness comes from the injected <see cref="Random"/> and all time
/// from the injected <see cref="IClock"/>, so a seeded 24h simulation is a unit test.
/// </summary>
public sealed class BehaviourEngine
{
    /// <summary>
    /// How much of its life the cat spends asleep. A product of weight AND dwell, not
    /// weight alone:
    ///
    ///     sleepShare = (Wsleep * Dsleep) / (Wsleep * Dsleep + SUM(Wi * Di))
    ///
    /// With the Content table below: Wsleep=6, Dsleep~37s, other weights sum ~75 at a
    /// mean dwell ~8s  =>  225 / (225 + 563) = 0.29. Content is the dominant mood, so the
    /// aggregate lands in band. BehaviourEngineTests.SleepShare_IsWithinSpecBand simulates
    /// 24h and asserts the band — retune these numbers there, not by eye.
    ///
    /// The original spec asked for 70-80%, and that is what a real cat does, but watching
    /// it is dull: the pet was a motionless loaf almost every time you looked at it. Now a
    /// quarter to a half. The cost is CPU — the awake tick is 30fps against sleep's 4 —
    /// so roughly double the average of the 70-80% build.
    /// </summary>
    public const double TargetSleepShareLow = 0.25;
    public const double TargetSleepShareHigh = 0.45;

    private readonly Needs _needs;
    private readonly NeedsSimulator _sim;
    private readonly IClock _clock;
    private readonly Random _rng;

    private readonly Queue<Stimulus> _pending = new();
    private TimeSpan _dwellRemaining;
    private bool _clipFinished = true;

    public BehaviourEngine(Needs needs, NeedsSimulator sim, IClock clock, Random? rng = null)
    {
        _needs = needs;
        _sim = sim;
        _clock = clock;
        _rng = rng ?? Random.Shared;

        Mood = _sim.DeriveMood(_needs);

        // Start awake and briefly idle, NOT asleep. Sleep dwell is 25-50s, so opening
        // with it meant the app launched to a motionless cat for the best part of a minute — the
        // worst possible first impression for a pet. The long-run sleep share is
        // unaffected; this only shapes the first few seconds.
        Current = Decide(CatAction.Idle, Facing.Right);
        _dwellRemaining = Current.MinDuration;
    }

    public BehaviourDecision Current { get; private set; }
    public Mood Mood { get; private set; }
    public CatAction Action => Current.Action;
    public bool IsSleeping => Current.Action == CatAction.Sleep;

    /// <summary>Raised only when the decision actually changes, never per tick.</summary>
    public event Action<BehaviourDecision>? DecisionChanged;

    /// <summary>Input goes in here as an event. Nothing outside this class starts an animation.</summary>
    public void Notify(Stimulus stimulus)
    {
        _sim.Apply(_needs, stimulus);
        _pending.Enqueue(stimulus);
    }

    /// <summary>AnimationPlayer calls this when a non-looping clip reaches its last frame.</summary>
    public void OnClipCompleted() => _clipFinished = true;

    /// <summary>
    /// Ends the current action early. Used when the world finishes the action before its
    /// dwell does — chiefly a walk whose travel completed, where letting the dwell run on
    /// would leave the cat treading air with the walk cycle still playing.
    /// </summary>
    public void CutShort()
    {
        if (_dwellRemaining > TimeSpan.Zero)
            _dwellRemaining = TimeSpan.Zero;
    }

    /// <summary>
    /// Advances the world by <paramref name="dt"/>. Returns the new decision if it changed,
    /// otherwise null. Safe to call at any rate; nothing here assumes a fixed FPS.
    /// </summary>
    public BehaviourDecision? Tick(TimeSpan dt)
    {
        _sim.Advance(_needs, dt, IsSleeping);
        Mood = _sim.DeriveMood(_needs);
        _dwellRemaining -= dt;

        if (TryTakeStimulus(out var reaction))
            return Commit(reaction);

        // Dwell is the sole authority on when an action ends. Clip length is NOT: the art
        // is only a few frames (eat is 5 frames at 10fps = 0.5s), so ending on clip
        // completion made the cat take one bite and wander off. Short clips loop for the
        // duration instead, and Interruptible governs only whether something may cut in.
        if (_dwellRemaining > TimeSpan.Zero)
            return null;

        return Commit(Decide(PickAction(), Current.Facing));
    }

    // ---- stimulus handling -------------------------------------------------

    private bool TryTakeStimulus(out BehaviourDecision decision)
    {
        decision = default;
        if (_pending.Count == 0) return false;

        // Direct user actions always win — an unresponsive pet reads as broken, so they
        // cut through both a running one-shot and sleep. Ambient stimuli wait their turn.
        var stimulus = _pending.Peek();
        // Startled belongs here even though the user did not cause it: the whole point is that
        // it happens TO a sleeping cat, so a rule that lets sleep ignore it would delete the
        // feature. TaskbarRose/Fell likewise — the ground moved, the cat cannot sleep through it.
        bool isUserAction = stimulus is Stimulus.Petted or Stimulus.Brushed
            or Stimulus.Fed or Stimulus.PlayToyOffered or Stimulus.Woken
            or Stimulus.Startled or Stimulus.TaskbarRose or Stimulus.TaskbarFell;

        if (!isUserAction && (!Current.Interruptible || IsSleeping))
        {
            _pending.Dequeue();   // drop stale ambient cues rather than replaying them late
            return false;
        }

        _pending.Dequeue();
        var action = ReactionTo(stimulus);
        if (action is null) return false;

        decision = Decide(action.Value, Current.Facing);
        return true;
    }

    private CatAction? ReactionTo(Stimulus stimulus) => stimulus switch
    {
        Stimulus.Fed => CatAction.Eat,
        Stimulus.Brushed => CatAction.Groom,
        Stimulus.Petted => CatAction.Happy,
        Stimulus.PlayToyOffered => CatAction.Play,
        Stimulus.Woken => CatAction.Stretch,
        Stimulus.TaskbarIconNearby => CatAction.Pounce,
        // The bar arriving is something to hop onto; the bar vanishing underfoot is a fall.
        // Which one the cat gets depends on whether it saw it coming, decided by the caller.
        Stimulus.TaskbarRose => CatAction.Pounce,
        Stimulus.TaskbarFell => CatAction.Pounce,
        Stimulus.Startled => CatAction.Startled,
        Stimulus.CursorNearby => _rng.NextDouble() < 0.6 ? CatAction.WatchCursor : CatAction.SitLook,
        Stimulus.CursorLeft => null,
        _ => null,
    };

    // ---- action selection --------------------------------------------------

    private CatAction PickAction()
    {
        var table = WeightsFor(Mood);

        // Never repeat the action just finished; variety is most of the "alive" feeling.
        int total = 0;
        foreach (var (action, weight) in table)
            if (action != Current.Action) total += weight;

        if (total <= 0) return CatAction.Idle;

        int roll = _rng.Next(total);
        foreach (var (action, weight) in table)
        {
            if (action == Current.Action) continue;
            roll -= weight;
            if (roll < 0) return action;
        }
        return CatAction.Idle;
    }

    // Static tables: allocated once at type init, never per decision. (These cannot be
    // returned as ReadOnlySpan collection expressions on C# 12 — CS9203, may escape scope.)

    // Sleep weight is deliberately modest — its long dwell does the heavy lifting, so this
    // number moves the sleep share far more than it looks like it should.
    private static readonly (CatAction Action, int Weight)[] ContentWeights =
    [
        (CatAction.Sleep, 6), (CatAction.Idle, 22), (CatAction.SitLook, 14),
        (CatAction.Loaf, 12), (CatAction.Walk, 12), (CatAction.Groom, 8),
        (CatAction.Stretch, 5), (CatAction.Scratch, 2),
    ];

    private static readonly (CatAction Action, int Weight)[] SleepyWeights =
    [
        (CatAction.Sleep, 60), (CatAction.Loaf, 16), (CatAction.Stretch, 10),
        (CatAction.Idle, 10), (CatAction.Groom, 4),
    ];

    private static readonly (CatAction Action, int Weight)[] HungryWeights =
    [
        (CatAction.Meow, 26), (CatAction.Walk, 22), (CatAction.SitLook, 16),
        (CatAction.Scratch, 12), (CatAction.Idle, 12), (CatAction.Sleep, 5),
    ];

    private static readonly (CatAction Action, int Weight)[] DirtyWeights =
    [
        (CatAction.Groom, 40), (CatAction.Scratch, 16), (CatAction.Idle, 16),
        (CatAction.Loaf, 14), (CatAction.Sleep, 6),
    ];

    private static readonly (CatAction Action, int Weight)[] PlayfulWeights =
    [
        (CatAction.Play, 26), (CatAction.Pounce, 20), (CatAction.Walk, 18),
        (CatAction.WatchCursor, 14), (CatAction.Idle, 12), (CatAction.Sleep, 4),
    ];

    private static readonly (CatAction Action, int Weight)[] AffectionateWeights =
    [
        (CatAction.Meow, 28), (CatAction.WatchCursor, 20), (CatAction.SitLook, 18),
        (CatAction.Walk, 14), (CatAction.Happy, 10), (CatAction.Sleep, 4),
    ];

    private static readonly (CatAction Action, int Weight)[] FallbackWeights =
    [
        (CatAction.Idle, 1),
    ];

    private static (CatAction Action, int Weight)[] WeightsFor(Mood mood) => mood switch
    {
        Mood.Content => ContentWeights,
        Mood.Sleepy => SleepyWeights,
        Mood.Hungry => HungryWeights,
        Mood.Dirty => DirtyWeights,
        Mood.Playful => PlayfulWeights,
        Mood.Affectionate => AffectionateWeights,
        _ => FallbackWeights,
    };

    // ---- action catalog ----------------------------------------------------

    private BehaviourDecision Decide(CatAction action, Facing facing)
    {
        var (min, max, interruptible) = ActionCatalog(action);
        var dwell = TimeSpan.FromSeconds(min + _rng.NextDouble() * (max - min));

        if (action == CatAction.Walk)
            facing = _rng.Next(2) == 0 ? Facing.Left : Facing.Right;

        string? bubble = action switch
        {
            CatAction.Meow => _needs.IsHungry ? "food" : "meow",
            CatAction.Happy => "heart",
            CatAction.Sleep => "ellipsis",
            _ => null,
        };

        return new BehaviourDecision(action, facing, dwell, interruptible, bubble);
    }

    /// <summary>Dwell range in seconds plus whether the clip may be cut short.</summary>
    private static (double Min, double Max, bool Interruptible) ActionCatalog(CatAction action) => action switch
    {
        CatAction.Sleep => (25, 50, true),
        CatAction.Idle => (4, 12, true),
        CatAction.SitLook => (4, 10, true),
        CatAction.Loaf => (8, 20, true),
        CatAction.Walk => (3, 9, true),
        CatAction.WatchCursor => (2, 6, true),
        CatAction.Scratch => (2, 5, true),
        CatAction.Stretch => (2.0, 2.0, false),
        CatAction.Groom => (4.0, 4.0, false),
        CatAction.Play => (3.5, 3.5, false),
        CatAction.Pounce => (1.5, 1.5, false),
        CatAction.Eat => (4.0, 4.0, false),
        CatAction.Meow => (2.5, 2.5, false),
        CatAction.Happy => (2.0, 2.0, false),
        // 16 frames at 10fps. Matched to the clip so it plays out exactly once and the
        // last frame — a normal seated pose — is what the cat cuts away from.
        CatAction.Startled => (1.6, 1.6, false),
        // Toy dwells are governed by ToyChase, not by the catalog; these are only the floor.
        CatAction.ChaseToy => (0.4, 0.4, true),
        CatAction.ReachUp => (0.4, 0.4, true),
        CatAction.PlayToy => (3.5, 3.5, false),
        CatAction.PounceToy => (1.0, 1.0, false),
        // 14 frames at 10fps = 1.4s, matched so the clip lands on its seated last frame.
        CatAction.Confused => (1.4, 1.4, false),
        _ => (5, 5, true),
    };

    private BehaviourDecision Commit(BehaviourDecision decision)
    {
        _sim.OnActionCompleted(_needs, Current.Action);
        Current = decision;
        _dwellRemaining = decision.MinDuration;
        _clipFinished = decision.Interruptible;   // looping clips never "finish"
        DecisionChanged?.Invoke(decision);
        return decision;
    }
}

/// <summary>Maps engine actions onto clip ids in sprites.json. The only place the two vocabularies meet.</summary>
public static class ClipMap
{
    public static string ClipFor(CatAction action, Facing facing) => action switch
    {
        // Where a purpose-drawn clip exists it wins over the original spec-sheet extraction,
        // which was 1-4 frames for most of these and read as a slideshow. The old ids stay in
        // sprites.json as the fallback SpriteLibrary lands on if a new sheet is missing.
        CatAction.Sleep => "sleep",
        CatAction.Idle => "idle_blink",
        CatAction.SitLook => "watch_bug",                                   // was sit_look, 2 frames
        CatAction.Loaf => "loaf",
        CatAction.Walk => facing == Facing.Left ? "run_left" : "run_right",  // was walk_*, 4-6 frames
        CatAction.Stretch => "stretch_yawn",
        CatAction.Groom => "groom",
        CatAction.Play => "zoomies",                                        // was play_pounce, 4 frames
        CatAction.Pounce => "jump",
        CatAction.Scratch => "scratch_icons",                               // was scratch, 1 frame
        CatAction.Eat => "eat",
        CatAction.Meow => "paw_screen",                                     // was meow_attention, 3 frames
        CatAction.Happy => "happy_hearts",
        CatAction.WatchCursor => "cursor_interaction",
        CatAction.Startled => "fright",

        // Toy mode.
        CatAction.ChaseToy => facing == Facing.Left ? "run_left" : "run_right",
        CatAction.ReachUp => "reach_up",
        CatAction.PlayToy => "play_yarn",
        CatAction.PounceToy => "jump",
        CatAction.Confused => "confused",
        _ => "idle_blink",
    };
}
