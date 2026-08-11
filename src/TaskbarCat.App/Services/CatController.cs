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
    private readonly Needs _needs;
    private readonly Settings _settings;
    private readonly SettingsStore _store;
    private readonly DispatcherTimer _timer;

    private DateTime _lastTick = DateTime.UtcNow;
    private DateTime _lastSave = DateTime.UtcNow;
    private int _currentFps;

    public CatController(CatWindow window, SpriteLibrary sprites, SettingsStore store, Settings settings)
    {
        _window = window;
        _sprites = sprites;
        _store = store;
        _settings = settings;

        _needs = settings.ToNeeds();
        _sim = new NeedsSimulator();

        // Time away is charged to the cat before anything else, so a user returning after
        // a weekend finds it hungry rather than exactly as they left it.
        _sim.ApplyOffline(_needs, DateTime.UtcNow - settings.LastSeenUtc);

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
    public void Send(Stimulus stimulus) => _engine.Notify(stimulus);

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
    /// Swaps the coat without restarting. The behaviour engine is untouched — mood, needs and
    /// the action in flight all carry over, so a sleeping cat stays asleep and simply changes
    /// colour, which is the whole point of applying the choice live.
    /// </summary>
    public void SetSprites(SpriteLibrary sprites)
    {
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
        _store.Save(_settings);
    }

    public void Dispose()
    {
        _timer.Stop();
        Persist();
    }
}
