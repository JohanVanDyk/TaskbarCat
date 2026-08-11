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

        // Time away is charged to the cat before anything else, so a user returning after
        // a weekend finds it hungry rather than exactly as they left it.
        var away = DateTime.UtcNow - settings.LastSeenUtc;
        _sim.ApplyOffline(_needs, away);
        // Uncapped, unlike the needs meters: a cat left for a week should be its normal size
        // again, not still chonky from Tuesday.
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
        _window.ChonkLevel = _chonk.Level;
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

        _engine.Tick(dt);

        if (_motion.Tick(dt))
            _window.SetAlong(_motion.Along);

        if (_animation.Tick(dt))
            _window.ShowClip(_animation.Clip, _animation.FrameIndex);

        // Walking is the one action whose animation and motion must agree: once the cat
        // has arrived, end the action rather than let it tread air until dwell expires.
        if (_engine.Action == CatAction.Walk && !_motion.IsWalking)
            _engine.CutShort();

        int wantFps = _engine.IsSleeping ? SleepFps : AwakeFps;
        if (wantFps != _currentFps) SetFps(wantFps);

        if (now - _lastSave > TimeSpan.FromSeconds(20))
        {
            _lastSave = now;
            Persist();
        }
    }

    private void OnDecisionChanged(BehaviourDecision decision)
    {
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
