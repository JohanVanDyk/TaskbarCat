using TaskbarCat.Models;

namespace TaskbarCat.Services;

/// <summary>Geometry for one frame of the chase, in physical pixels.</summary>
public readonly record struct ToyFrame(
    double ToyX, double ToyY,
    double CatCentreX, double CatTop, double CatBottom,
    double RailLeft, double RailRight);

public enum ToyResponse
{
    /// <summary>Toy is out of reach along the rail; run at it.</summary>
    Chase,

    /// <summary>Toy is overhead: rear up and bat at it.</summary>
    ReachUp,

    /// <summary>Yarn caught — wrestle it.</summary>
    Play,

    /// <summary>Laser "caught" — pounce.</summary>
    Pounce,

    /// <summary>There was nothing under the paw. It was light.</summary>
    Confused,
}

/// <summary>Decision for one frame: what to do, and where along the rail to be.</summary>
public readonly record struct ToyDecision(ToyResponse Response, double TargetAlong);

/// <summary>
/// Decides how the cat reacts to a toy following the mouse pointer.
///
/// Pure: it is handed geometry and elapsed time and returns a decision, so "the cat rears up
/// when the toy is 120px above it" is a unit test rather than something you check by waving a
/// mouse around. Nothing here knows about cursors, hooks or windows.
/// </summary>
public sealed class ToyChase
{
    /// <summary>Horizontal distance within which the cat can touch the toy.</summary>
    public const double CatchRadiusX = 70;

    /// <summary>How far above the cat's head the toy has to be before it rears up instead.</summary>
    public const double OverheadMargin = 40;

    /// <summary>Beyond this above the cat, it is not worth reaching for at all — keep chasing.</summary>
    public const double UnreachableAbove = 420;

    public static readonly TimeSpan PlayDuration = TimeSpan.FromSeconds(3.5);
    public static readonly TimeSpan PounceDuration = TimeSpan.FromSeconds(1.0);
    /// <summary>Matched to the confused clip: 14 frames at 10fps.</summary>
    public static readonly TimeSpan ConfusedDuration = TimeSpan.FromSeconds(1.4);

    /// <summary>
    /// After a catch the cat ignores the toy for a moment. Without it a stationary pointer
    /// re-triggers the catch every frame and the cat pounces on the spot forever.
    /// </summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(1.5);

    private ToyResponse _response = ToyResponse.Chase;
    private TimeSpan _hold;          // time left in a non-interruptible reaction
    private TimeSpan _cooldown;

    public ToyResponse Response => _response;

    /// <summary>True while playing out a catch; the caller must not retarget motion.</summary>
    public bool InReaction => _hold > TimeSpan.Zero;

    public void Reset()
    {
        _response = ToyResponse.Chase;
        _hold = TimeSpan.Zero;
        _cooldown = TimeSpan.Zero;
    }

    public ToyDecision Update(TimeSpan dt, ToyKind kind, in ToyFrame f)
    {
        if (_cooldown > TimeSpan.Zero) _cooldown -= dt;

        // A reaction in flight runs to completion. Chasing again mid-pounce would cancel the
        // joke the laser exists for.
        if (_hold > TimeSpan.Zero)
        {
            _hold -= dt;
            if (_hold <= TimeSpan.Zero)
            {
                // The laser's punchline: pouncing on light resolves to confusion, not success.
                if (_response == ToyResponse.Pounce)
                {
                    _response = ToyResponse.Confused;
                    _hold = ConfusedDuration;
                }
                else
                {
                    _response = ToyResponse.Chase;
                    _cooldown = Cooldown;
                }
            }
            return new ToyDecision(_response, AlongFor(f, f.CatCentreX));
        }

        double target = AlongFor(f, f.ToyX);
        double dx = Math.Abs(f.ToyX - f.CatCentreX);
        double above = f.CatTop - f.ToyY;      // positive when the toy is overhead

        bool withinReach = dx <= CatchRadiusX;
        bool overhead = above > OverheadMargin && above <= UnreachableAbove;

        if (withinReach && _cooldown <= TimeSpan.Zero)
        {
            if (overhead)
            {
                // Directly above and reachable: bat at it, but this is not a catch — the cat
                // never gets a toy held over its head, which is the point of the pose.
                _response = ToyResponse.ReachUp;
                return new ToyDecision(_response, target);
            }

            if (f.ToyY >= f.CatTop - OverheadMargin && f.ToyY <= f.CatBottom + OverheadMargin)
            {
                _response = kind == ToyKind.Laser ? ToyResponse.Pounce : ToyResponse.Play;
                _hold = kind == ToyKind.Laser ? PounceDuration : PlayDuration;
                return new ToyDecision(_response, AlongFor(f, f.CatCentreX));
            }
        }

        if (withinReach && overhead)
        {
            // Still overhead but cooling down: keep reaching rather than snapping to a run,
            // which would read as the cat losing interest for no reason.
            _response = ToyResponse.ReachUp;
            return new ToyDecision(_response, target);
        }

        _response = ToyResponse.Chase;
        return new ToyDecision(_response, target);
    }

    /// <summary>Screen x to a 0..1 position along the rail, clamped to its ends.</summary>
    private static double AlongFor(in ToyFrame f, double x)
    {
        double span = f.RailRight - f.RailLeft;
        if (span <= 0) return 0.5;
        return Math.Clamp((x - f.RailLeft) / span, 0, 1);
    }
}
