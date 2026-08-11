using System.Windows.Threading;
using TaskbarCat.App.Views;
using TaskbarCat.Models;
using TaskbarCat.Services;

namespace TaskbarCat.App.Services;

/// <summary>
/// The one place the four layers meet: BehaviourEngine (what) -> MotionController (where)
/// -> AnimationPlayer (which frame) -> CatWindow (draw it). Each of those knows nothing
/// about the others; this class owns the clock and wires them together.
/// </summary>
internal sealed class CatController : IDisposable
{
    // Sleeping is ~72% of the cat's life, so the sleep tick rate dominates average CPU — which
    // is what buys the awake rate. 30 rather than 15 because the tick is the ceiling on every
    // clip's own fps: the run cycles are authored at 16 and zoomies at 18, and at a 15 Hz tick
    // those lose every other frame and look worse than the sparse art they replaced.
    private const int AwakeFps = 30;
    private const int SleepFps = 4;

    private readonly CatWindow _window;
    private SpriteLibrary _sprites;
    private readonly BehaviourEngine _engine;
    private readonly MotionController _motion;
    private readonly AnimationPlayer _animation;
    private readonly NeedsSimulator _sim;
    private readonly ChonkTracker _chonk;
    private readonly PerchAnimator _perch = new();
    private ToyController? _toys;
    private CatAction _toyAction = CatAction.Idle;
    private bool _toyDriving;
    private readonly Needs _needs;
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    private readonly Func<string, int, SpriteLibrary> _loadSprites;
    private readonly DispatcherTimer _timer;

    private DateTime _lastTick = DateTime.UtcNow;
    private DateTime _lastSave = DateTime.UtcNow;
    private int _currentFps;

    public CatController(CatWindow window, SpriteLibrary sprites, SettingsStore store, Settings settings,
        Func<string, int, SpriteLibrary> loadSprites)
    {
        _window = window;
        _sprites = sprites;
        _store = store;
        _settings = settings;
        _loadSprites = loadSprites;

        _needs = settings.ToNeeds();
        _sim = new NeedsSimulator();

        _chonk = new ChonkTracker(settings.ChonkLevel, settings.ChonkFeeds,
            TimeSpan.FromSeconds(settings.ChonkSinceChangeSeconds));

        // Time away is charged to the cat before anything else, so a user returning after
        // a weekend finds it hungry rather than exactly as they left it.
        var away = DateTime.UtcNow - settings.LastSeenUtc;
        _sim.ApplyOffline(_needs, away);

        // Slim down for the time away — uncapped, unlike the needs meters, so a cat left for a
        // week is its normal size again rather than still chonky from Tuesday.
        //
        // This runs BEFORE LevelChanged is subscribed, and that ordering is load-bearing: the
        // handler reloads sprites and touches _engine and _animation, which do not exist yet.
        // Subscribing first crashed the app on startup for any cat that had slimmed while it
        // was closed — a null reference from inside a constructor, with the cat simply never
        // appearing.
        _chonk.Tick(away);

        _engine = new BehaviourEngine(_needs, _sim, new SystemClock());
        _motion = new MotionController();
        _animation = new AnimationPlayer(ResolveClip(_engine.Current));

        _animation.Completed += _engine.OnClipCompleted;
        _engine.DecisionChanged += OnDecisionChanged;

        _window.SpanChanged += span =>
        {
            _motion.SetSpan(span);
            _window.SetAlong(_motion.Along);
        };
        _window.CatClicked += OnCatClicked;

        _motion.SetSpan(_window.TravelSpan);
        _motion.PlaceAt(settings.AlongRail);
        _window.SetAlong(_motion.Along);
        _chonk.LevelChanged += (level, _) =>
        {
            // Reload at the new size: some clips have drawn chonk art, and without a reload the
            // cat would only ever be stretched.
            ReloadSprites(_settings.ColorPreset, level);
            _window.ChonkLevel = level;
            // Persist immediately: a size change is the visible result of the user feeding it,
            // and losing it to a kill between the 20s autosaves would look like a bug.
            Persist();
        };

        // Reconcile: the library was loaded for the size in settings, which the offline
        // slim-down above may have just changed.
        if (_sprites.ChonkLevel != _chonk.Level)
            ReloadSprites(_settings.ColorPreset, _chonk.Level);

        _window.ChonkLevel = _chonk.Level;

        // Start already standing wherever the bar currently is, rather than animating up on
        // launch: the cat should appear in place, not vault onto a bar it was never off.
        _perch.Snap(_window.BarRevealed ? 1 : 0);
        _window.Perch = _perch.Progress;
        _window.BarRevealedChanged += OnBarRevealedChanged;

        _window.ShowClip(_animation.Clip, _animation.FrameIndex);

        _timer = new DispatcherTimer(DispatcherPriority.Render);
        _timer.Tick += (_, _) => Tick();
        SetFps(AwakeFps);
        _timer.Start();
    }

    public Needs Needs => _needs;
    public Mood Mood => _engine.Mood;
    public CatAction Action => _engine.Action;
    public double Along => _motion.Along;
    public string SettingsPath => _store.Path;

    /// <summary>Fires on every action change: (action, mood, clipId). Diagnostics/self-test.</summary>
    public event Action<CatAction, Mood, string>? ActionChanged;

    /// <summary>Feeds a user action into the engine. The engine decides how to react.</summary>
    public void Send(Stimulus stimulus)
    {
        if (stimulus == Stimulus.Fed) _chonk.Fed();
        _engine.Notify(stimulus);
    }

    public int ChonkLevel => _chonk.Level;

    /// <summary>
    /// Toy mode drives the cat directly instead of through the behaviour engine.
    ///
    /// The engine picks what a cat does when nothing is happening TO it; a toy on screen is the
    /// opposite of that, and letting the engine keep choosing would have it wander off
    /// mid-chase when a dwell expired. The engine still ticks — needs keep decaying, moods keep
    /// updating — its decisions are simply not what is rendered while a toy is out.
    /// </summary>
    public void AttachToys(ToyController toys)
    {
        _toys = toys;
        toys.Stopped += () =>
        {
            // Hand control back by replaying the engine's current decision, so the cat resumes
            // from whatever it was already thinking rather than snapping to a default.
            _toyAction = CatAction.Idle;
            _motion.SpeedPixelsPerSecond = StrollSpeed;
            OnDecisionChanged(_engine.Current);
        };
    }

    public bool ToyModeActive => _toys?.IsActive == true;

    /// <summary>Clips currently coming from drawn chonk art rather than being stretched.</summary>
    public int ChonkArtClips => _sprites.ChonkArtClips;

    /// <summary>
    /// Jumps straight to a size, reloading the sheets for it. Self-test only: setting the
    /// window's level alone changes the stretch but not which art is loaded, which is exactly
    /// the mistake this method exists to stop a harness making.
    /// </summary>
    public void ForceChonk(int level)
    {
        level = Math.Clamp(level, 0, ChonkTracker.MaxLevel);
        _chonk.SetLevel(level);
        ReloadSprites(_settings.ColorPreset, level);
        _window.ChonkLevel = level;
    }

    public string Name => _settings.Name;

    public string PresetId => _sprites.PresetId;

    /// <summary>Raised when the cat is renamed, so the tray label can follow.</summary>
    public event Action<string>? Renamed;

    public void Rename(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Cat" : name.Trim();
        if (trimmed == _settings.Name) return;

        _settings.Name = trimmed;
        Persist();
        Renamed?.Invoke(trimmed);
    }

    /// <summary>
    /// Swaps the coat without restarting. Reloads at the CURRENT size, so picking a new colour
    /// while the cat is fat keeps it fat.
    /// </summary>
    public void SetCoat(string presetId) => ReloadSprites(presetId, _chonk.Level);

    /// <summary>
    /// Swaps the library without restarting. The behaviour engine is untouched — mood, needs
    /// and the action in flight all carry over, so a sleeping cat stays asleep and simply
    /// changes colour or size, which is the whole point of applying it live.
    /// </summary>
    private void ReloadSprites(string presetId, int chonkLevel)
    {
        var sprites = _loadSprites(presetId, chonkLevel);
        _sprites = sprites;
        _settings.ColorPreset = sprites.PresetId;

        // Re-resolve the CURRENT decision against the new library rather than resetting to a
        // default clip: the old Clip objects hold frames from the old sheets and would keep
        // rendering the old coat until the next action change, which can be minutes away.
        var clip = ResolveClip(_engine.Current);
        _animation.Play(clip);
        _window.ShowClip(_animation.Clip, _animation.FrameIndex);

        Persist();
    }

    private void OnCatClicked() => _window.ShowRadialMenu();

    /// <summary>
    /// The taskbar slid in or out. Going up is always a deliberate hop. Coming down depends on
    /// whether the cat was asleep: an awake cat jumps off, a sleeping one has the floor removed
    /// from under it and drops, faster, with a fright.
    /// </summary>
    private void OnBarRevealedChanged(bool revealed)
    {
        if (revealed)
        {
            _perch.MoveTo(1, PerchAnimator.JumpUpDuration);
            _engine.Notify(Stimulus.TaskbarRose);
            return;
        }

        bool wasAsleep = _engine.IsSleeping;
        _perch.MoveTo(0, wasAsleep ? PerchAnimator.StartledDropDuration : PerchAnimator.FallDownDuration);
        _engine.Notify(wasAsleep ? Stimulus.Startled : Stimulus.TaskbarFell);
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;
        var raw = now - _lastTick;
        _lastTick = now;

        // A suspended/resumed machine reports an enormous delta. Motion and animation get a
        // clamped slice so the cat does not teleport; the needs meters get the rest as
        // away-time, so a laptop shut overnight wakes a hungry cat rather than a fresh one.
        var (dt, away) = TickBudget.For(raw);
        if (away > TimeSpan.Zero) _sim.ApplyOffline(_needs, away);

        // Slimming runs on wall-clock time including any suspend, so a laptop shut for an hour
        // wakes a slimmer cat. Both slices count: the clamped one is still real elapsed time.
        _chonk.Tick(dt + away);

        // Poll before the engine ticks: if the bar just moved, the cat should react on this
        // frame rather than one frame into an action it would not have chosen.
        _window.PollTaskbarReveal();

        if (_perch.IsMoving)
        {
            _perch.Tick(dt);
            _window.Perch = _perch.Progress;
        }

        // Toy mode takes the wheel. The engine still runs underneath — this only replaces what
        // is drawn and where the cat walks.
        // Set before the engine ticks, because the engine raises DecisionChanged from INSIDE
        // Tick — discarding its return value is not enough to stop it repainting the cat.
        _toyDriving = TickToys(dt);
        bool toyDriving = _toyDriving;

        _engine.Tick(dt);

        if (_motion.Tick(dt))
            _window.SetAlong(_motion.Along);

        if (_animation.Tick(dt))
            _window.ShowClip(_animation.Clip, _animation.FrameIndex);

        // Walking is the one action whose animation and motion must agree: once the cat
        // has arrived, end the action rather than let it tread air until dwell expires.
        if (!toyDriving && _engine.Action == CatAction.Walk && !_motion.IsWalking)
            _engine.CutShort();

        int wantFps = _engine.IsSleeping ? SleepFps : AwakeFps;
        if (wantFps != _currentFps) SetFps(wantFps);

        if (now - _lastSave > TimeSpan.FromSeconds(20))
        {
            _lastSave = now;
            Persist();
        }
    }

    /// <summary>
    /// Feeds one frame of toy chasing. Returns true while a toy is driving the cat.
    /// </summary>
    private bool TickToys(TimeSpan dt)
    {
        if (_toys is null || !_toys.IsActive) return false;

        double scale = _window.Scale;
        var rail = _window.Rail;
        double railLeft = rail?.Left ?? 0;
        double railRight = rail?.Right ?? _window.ScreenPhysicalWidth;

        double catW = _clipWidthPhysical(scale);
        double catCentre = railLeft + (railRight - railLeft - catW) * _motion.Along + catW / 2;
        double catTop = _window.CatTopPhysical;

        var decision = _toys.Tick(dt, catCentre, catTop, catTop + _clipHeightPhysical(scale), railLeft, railRight);
        if (decision is null) return false;

        var d = decision.Value;
        var action = d.Response switch
        {
            ToyResponse.Chase => CatAction.ChaseToy,
            ToyResponse.ReachUp => CatAction.ReachUp,
            ToyResponse.Play => CatAction.PlayToy,
            ToyResponse.Pounce => CatAction.PounceToy,
            _ => CatAction.Confused,
        };

        // A cat ambling at its normal 55px/s is not chasing anything. The run clips are
        // authored at 16fps for a reason; the motion has to match them.
        _motion.SpeedPixelsPerSecond = ChaseSpeed;

        // Only chasing moves the cat; the reactions play on the spot.
        if (d.Response == ToyResponse.Chase && !_toys.InReaction)
            _motion.WalkTo(d.TargetAlong);
        else
            _motion.Stop();

        if (action != _toyAction)
        {
            _toyAction = action;
            var facing = _motion.Facing;
            var clip = ResolveClip(new BehaviourDecision(action, facing, TimeSpan.Zero, true, null));
            _animation.Play(clip);
            _window.ShowClip(_animation.Clip, _animation.FrameIndex);
            ActionChanged?.Invoke(action, _engine.Mood, _animation.Clip.Id);
        }

        return true;
    }

    /// <summary>Chase pace. Four times a stroll, which is what a run cycle looks like.</summary>
    private const double ChaseSpeed = 220;
    private const double StrollSpeed = 55;

    private double _clipWidthPhysical(double scale) => _animation.Clip.FrameWidth * scale;
    private double _clipHeightPhysical(double scale) => _animation.Clip.FrameHeight * scale;

    private void OnDecisionChanged(BehaviourDecision decision)
    {
        // While a toy is out, the engine keeps thinking but does not get to draw. Without this
        // the two fought over the screen: a chase would be interrupted by whatever the engine
        // had just decided, mid-run.
        if (_toyDriving) return;

        if (decision.Action == CatAction.Walk)
            _motion.StartWalk(decision.Facing);
        else
            _motion.Stop();

        _animation.Play(ResolveClip(decision));
        _window.ShowClip(_animation.Clip, _animation.FrameIndex);
        ActionChanged?.Invoke(decision.Action, _engine.Mood, _animation.Clip.Id);
    }

    /// <summary>
    /// Maps an engine decision onto an actual loaded clip. Not every action has art in
    /// this asset set, so fall back rather than crash: a cat that idles instead of
    /// scratching is fine, a cat that throws is not.
    /// </summary>
    private Clip ResolveClip(BehaviourDecision decision)
    {
        var id = ClipMap.ClipFor(decision.Action, decision.Facing);
        if (_sprites.Clips.TryGetValue(id, out var clip)) return clip;
        if (_sprites.Clips.TryGetValue("idle_blink", out var idle)) return idle;
        return _sprites.Clips.Values.First();
    }

    private void SetFps(int fps)
    {
        _currentFps = fps;
        _timer.Interval = TimeSpan.FromSeconds(1.0 / fps);
    }

    public void Persist()
    {
        _settings.CopyFrom(_needs, _motion.Along);
        _settings.ChonkLevel = _chonk.Level;
        _settings.ChonkFeeds = _chonk.FeedsAtLevel;
        _settings.ChonkSinceChangeSeconds = _chonk.SinceChange.TotalSeconds;
        _store.Save(_settings);
    }

    public void Dispose()
    {
        _timer.Stop();
        Persist();
    }
}
