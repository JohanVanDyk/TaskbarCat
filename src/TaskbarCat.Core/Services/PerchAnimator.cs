namespace TaskbarCat.Services;

/// <summary>
/// Animates the cat between standing on the screen edge and standing on top of a revealed
/// auto-hide taskbar. Produces a 0..1 progress that the renderer lerps two placements with,
/// so it stays edge-agnostic — a top bar moves the cat down, a bottom bar moves it up, and
/// this type does not need to know which.
///
/// The two directions are deliberately not symmetric. Going up is a jump: it overshoots and
/// settles back, which reads as effort. Coming down is a fall: it starts slow and accelerates,
/// which reads as gravity.
/// </summary>
public sealed class PerchAnimator
{
    public static readonly TimeSpan JumpUpDuration = TimeSpan.FromMilliseconds(260);
    public static readonly TimeSpan FallDownDuration = TimeSpan.FromMilliseconds(200);

    /// <summary>A startled cat drops faster than one that meant to.</summary>
    public static readonly TimeSpan StartledDropDuration = TimeSpan.FromMilliseconds(130);

    private double _from;
    private double _to;
    private TimeSpan _elapsed;
    private TimeSpan _duration;

    /// <summary>0 = on the screen edge, 1 = on top of the bar. Overshoots past 1 mid-jump.</summary>
    public double Progress { get; private set; }

    /// <summary>Where the animation is heading, without the overshoot.</summary>
    public double Target => _to;

    public bool IsMoving => _elapsed < _duration;

    public void Snap(double progress)
    {
        Progress = _from = _to = Math.Clamp(progress, 0, 1);
        _elapsed = _duration = TimeSpan.Zero;
    }

    public void MoveTo(double target, TimeSpan duration)
    {
        target = Math.Clamp(target, 0, 1);
        if (Math.Abs(target - _to) < 0.001 && IsMoving) return;

        _from = Progress;
        _to = target;
        _elapsed = TimeSpan.Zero;
        _duration = duration <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : duration;
    }

    public void Tick(TimeSpan dt)
    {
        if (!IsMoving)
        {
            Progress = _to;
            return;
        }

        _elapsed += dt;
        double t = Math.Clamp(_elapsed.TotalSeconds / _duration.TotalSeconds, 0, 1);

        double eased = _to > _from ? EaseOutBack(t) : EaseInQuad(t);
        Progress = _from + (_to - _from) * eased;

        if (t >= 1) Progress = _to;
    }

    /// <summary>Overshoots past the target and settles: the top of a jump.</summary>
    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        double u = t - 1;
        return 1 + c3 * u * u * u + c1 * u * u;
    }

    /// <summary>Accelerates: falling.</summary>
    private static double EaseInQuad(double t) => t * t;
}
