using TaskbarCat.Models;

namespace TaskbarCat.Services;

/// <summary>
/// Decides WHERE the cat is along the taskbar rail. Knows nothing about windows, DPI or
/// sprites — it works in "travel pixels" (0..Span) and the view maps that onto the rail.
///
/// Split from BehaviourEngine on purpose: the engine picks Walk, this decides how far and
/// which way, and neither needs to know about the other's internals.
/// </summary>
public sealed class MotionController
{
    private readonly Random _rng;
    private double _target;

    public MotionController(Random? rng = null) => _rng = rng ?? Random.Shared;

    /// <summary>Travelable length in physical pixels (rail length minus the cat's width).</summary>
    public double Span { get; private set; }

    /// <summary>Current offset from the rail's left/top end, in physical pixels.</summary>
    public double Position { get; private set; }

    public double SpeedPixelsPerSecond { get; set; } = 55;

    public Facing Facing { get; private set; } = Facing.Right;

    public bool IsWalking { get; private set; }

    /// <summary>Normalised 0..1 position, which is what RailPlacement consumes.</summary>
    public double Along => Span <= 0 ? 0 : Math.Clamp(Position / Span, 0, 1);

    /// <summary>
    /// Rail length changed (taskbar resized, monitor swapped, cat clip changed width).
    /// Keeps the cat at the same proportional spot rather than teleporting it to a pixel
    /// offset that means something different on the new rail.
    /// </summary>
    public void SetSpan(double span)
    {
        span = Math.Max(0, span);
        double along = Along;
        Span = span;
        Position = span * along;
        _target = Math.Clamp(_target, 0, span);
    }

    public void PlaceAt(double along)
    {
        Position = Math.Clamp(along, 0, 1) * Span;
        _target = Position;
        IsWalking = false;
    }

    /// <summary>
    /// Starts a walk. Distance is deliberately short and random — a cat that crosses the
    /// whole taskbar every time reads as a screensaver, not a pet.
    /// </summary>
    public void StartWalk(Facing? preferred = null)
    {
        if (Span <= 1)
        {
            IsWalking = false;
            return;
        }

        var facing = preferred ?? (_rng.Next(2) == 0 ? Facing.Left : Facing.Right);
        double distance = 80 + _rng.NextDouble() * 220;
        double target = facing == Facing.Right ? Position + distance : Position - distance;

        // Turn around at the ends instead of piling up against them.
        if (target < 0 || target > Span)
        {
            facing = facing == Facing.Right ? Facing.Left : Facing.Right;
            target = facing == Facing.Right ? Position + distance : Position - distance;
        }

        _target = Math.Clamp(target, 0, Span);
        Facing = facing;
        IsWalking = Math.Abs(_target - Position) > 0.5;
    }

    public void Stop() => IsWalking = false;

    /// <summary>
    /// Walks toward a specific point on the rail instead of a randomly chosen one. Used by toy
    /// mode, where the destination is wherever the pointer is and changes every frame.
    /// Arriving stops the walk, so a cat that has caught up stands still rather than jittering
    /// back and forth across the target.
    /// </summary>
    public void WalkTo(double along)
    {
        double target = Math.Clamp(along, 0, 1) * Span;
        if (Math.Abs(target - Position) < ArrivalSlack)
        {
            IsWalking = false;
            return;
        }

        _target = target;
        Facing = target > Position ? Facing.Right : Facing.Left;
        IsWalking = true;
    }

    /// <summary>Close enough to count as arrived. Roughly a paw's width at 100%.</summary>
    private const double ArrivalSlack = 12;

    /// <summary>Advances the walk. Returns true if the position actually changed.</summary>
    public bool Tick(TimeSpan dt)
    {
        if (!IsWalking || dt <= TimeSpan.Zero) return false;

        double step = SpeedPixelsPerSecond * dt.TotalSeconds;
        double remaining = _target - Position;

        if (Math.Abs(remaining) <= step)
        {
            Position = _target;
            IsWalking = false;
            return true;
        }

        Position += Math.Sign(remaining) * step;
        Facing = remaining > 0 ? Facing.Right : Facing.Left;
        return true;
    }
}
