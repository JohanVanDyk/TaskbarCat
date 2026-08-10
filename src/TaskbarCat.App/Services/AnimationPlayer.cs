using System.Windows.Media.Imaging;

namespace TaskbarCat.App.Services;

/// <summary>
/// Decides WHICH FRAME is on screen. Owns clip playback timing only — it never picks a
/// clip (BehaviourEngine does) and never moves the window (MotionController does).
///
/// Frame timing is accumulator-based rather than one-frame-per-tick so a clip's authored
/// fps is honoured independently of the tick rate, which changes when the cat sleeps.
/// </summary>
internal sealed class AnimationPlayer
{
    private double _accumulator;
    private bool _completed;

    public AnimationPlayer(Clip clip) => Clip = clip;

    public Clip Clip { get; private set; }
    public int FrameIndex { get; private set; }
    public BitmapSource CurrentFrame => Clip.Frames[FrameIndex];

    /// <summary>Fires once when a non-looping clip reaches its final frame.</summary>
    public event Action? Completed;

    public void Play(Clip clip)
    {
        Clip = clip;
        FrameIndex = 0;
        _accumulator = 0;
        _completed = false;
    }

    /// <summary>Advances playback. Returns true when the visible frame changed.</summary>
    public bool Tick(TimeSpan dt)
    {
        if (Clip.Frames.Length <= 1)
        {
            // A single-frame clip still has to report completion, or a one-shot pose
            // would wedge the behaviour engine waiting for a frame that never advances.
            if (!Clip.Loop && !_completed)
            {
                _completed = true;
                Completed?.Invoke();
            }
            return false;
        }

        _accumulator += dt.TotalSeconds;
        double frameTime = 1.0 / Math.Max(1, Clip.Fps);
        if (_accumulator < frameTime) return false;

        int advance = (int)(_accumulator / frameTime);
        _accumulator -= advance * frameTime;

        int next = FrameIndex + advance;
        if (next >= Clip.Frames.Length)
        {
            if (Clip.Loop)
            {
                next %= Clip.Frames.Length;
            }
            else
            {
                next = Clip.Frames.Length - 1;
                if (!_completed)
                {
                    _completed = true;
                    Completed?.Invoke();
                }
            }
        }

        bool changed = next != FrameIndex;
        FrameIndex = next;
        return changed;
    }
}
