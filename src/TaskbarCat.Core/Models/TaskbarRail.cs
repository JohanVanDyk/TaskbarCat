namespace TaskbarCat.Models;

public enum TaskbarEdge
{
    Left = 0,
    Top = 1,
    Right = 2,
    Bottom = 3,
}

/// <summary>
/// The line the cat walks along, in PHYSICAL pixels (what SHAppBarMessage reports).
/// Pure data, no Win32 types, so placement maths is unit-testable without a desktop.
/// </summary>
public readonly record struct TaskbarRail(
    TaskbarEdge Edge,
    int Left,
    int Top,
    int Right,
    int Bottom,
    bool IsAutoHide)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsHorizontal => Edge is TaskbarEdge.Top or TaskbarEdge.Bottom;

    /// <summary>Length of the travelable span along the bar.</summary>
    public int TravelLength => IsHorizontal ? Width : Height;
}

public static class RailPlacement
{
    /// <summary>
    /// Where the cat sits for a given position along the rail.
    /// <paramref name="along"/> is 0..1 from the rail's left/top end.
    ///
    /// The cat rests ON the bar's outer face — on a bottom taskbar it stands on the bar's
    /// top edge, so it overlaps the desktop, not the buttons.
    ///
    /// An auto-hide bar is the interesting case. While it is hidden it has slid off-screen and
    /// the cat clamps back to the screen edge; while it is REVEALED the cat stands on it like
    /// any other bar. <paramref name="barRevealed"/> is what tells the two apart — without it
    /// the bar slides up and covers the cat.
    /// </summary>
    public static (int X, int Y) Resting(
        TaskbarRail rail, int catWidth, int catHeight, double along,
        int screenWidth, int screenHeight, bool barRevealed = false)
    {
        along = Math.Clamp(along, 0.0, 1.0);

        if (rail.IsHorizontal)
        {
            int span = Math.Max(0, rail.Width - catWidth);
            int x = rail.Left + (int)Math.Round(span * along);

            int y = rail.Edge == TaskbarEdge.Bottom
                ? rail.Top - catHeight
                : rail.Bottom;

            if (rail.IsAutoHide && !barRevealed)
                y = rail.Edge == TaskbarEdge.Bottom ? screenHeight - catHeight : 0;

            return (x, y);
        }

        int vspan = Math.Max(0, rail.Height - catHeight);
        int cy = rail.Top + (int)Math.Round(vspan * along);

        int cx = rail.Edge == TaskbarEdge.Left
            ? rail.Right
            : rail.Left - catWidth;

        if (rail.IsAutoHide && !barRevealed)
            cx = rail.Edge == TaskbarEdge.Left ? 0 : screenWidth - catWidth;

        return (cx, cy);
    }
}
