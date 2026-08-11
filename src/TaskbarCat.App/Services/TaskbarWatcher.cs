using System.Windows;
using TaskbarCat.App.Interop;
using TaskbarCat.Models;

namespace TaskbarCat.App.Services;

/// <summary>
/// Tracks where the taskbar is. Registers as an appbar purely to receive ABN_*
/// notifications — it never calls ABM_SETPOS, so no screen space is reserved and the
/// user's desktop layout is untouched.
///
/// Event-driven, not polled: ABN_POSCHANGED fires on move/resize/auto-hide, and
/// WM_DPICHANGED / WM_DISPLAYCHANGE cover monitor changes.
/// </summary>
internal sealed class TaskbarWatcher : IDisposable
{
    /// <summary>
    /// How far the bar must have slid on-screen to count as revealed. An auto-hidden bar keeps
    /// a couple of pixels showing as the hover target, so "any of it visible" is not the test.
    /// </summary>
    private const int RevealedMarginPx = 8;

    private readonly IntPtr _hwnd;
    private readonly uint _callbackMessage;
    private IntPtr _trayHwnd;
    private bool _registered;

    public TaskbarWatcher(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _callbackMessage = Win32.RegisterWindowMessage("TaskbarCat_AppBarCallback");

        var data = new Win32.APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.APPBARDATA>(),
            hWnd = hwnd,
            uCallbackMessage = _callbackMessage,
        };
        _registered = Win32.SHAppBarMessage(Win32.ABM_NEW, ref data) != IntPtr.Zero;

        Rail = Query();
        BarRevealed = QueryRevealed();
    }

    public TaskbarRail Rail { get; private set; }

    /// <summary>
    /// True while the bar is actually on screen. Always true for a bar that does not auto-hide;
    /// for one that does, it tracks the slide in and out.
    /// </summary>
    public bool BarRevealed { get; private set; } = true;

    /// <summary>Raised when the bar slides in or out.</summary>
    public event Action<bool>? BarRevealedChanged;

    /// <summary>
    /// Polls the bar's real position. Called from the cat's own tick rather than run on a timer
    /// of its own: it is two syscalls, and it must not fire while the app is asleep at 4fps and
    /// the cat is not being drawn anyway.
    /// </summary>
    public void PollReveal()
    {
        bool revealed = QueryRevealed();
        if (revealed == BarRevealed) return;
        BarRevealed = revealed;
        BarRevealedChanged?.Invoke(revealed);
    }

    private bool QueryRevealed()
    {
        // A bar that never hides is always there; do not pay for the lookup.
        if (!Rail.IsAutoHide) return true;

        if (_trayHwnd == IntPtr.Zero)
            _trayHwnd = Win32.FindWindow("Shell_TrayWnd", null);
        if (_trayHwnd == IntPtr.Zero || !Win32.GetWindowRect(_trayHwnd, out var r))
            return false;

        // Hidden means slid off its own edge. Compare against the rail the shell reports rather
        // than the screen: on a multi-monitor desktop the bar's edge is not the screen's.
        return Rail.Edge switch
        {
            TaskbarEdge.Bottom => r.Top <= Rail.Bottom - RevealedMarginPx,
            TaskbarEdge.Top => r.Bottom >= Rail.Top + RevealedMarginPx,
            TaskbarEdge.Left => r.Right >= Rail.Left + RevealedMarginPx,
            TaskbarEdge.Right => r.Left <= Rail.Right - RevealedMarginPx,
            _ => true,
        };
    }

    /// <summary>Raised when the rail actually moves — not on every notification.</summary>
    public event Action<TaskbarRail>? RailChanged;

    /// <summary>True while a full-screen app owns the foreground; the cat should hide.</summary>
    public bool FullScreenAppActive { get; private set; }

    public event Action<bool>? FullScreenChanged;

    /// <summary>Hook this from the window's HwndSource.</summary>
    public IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _callbackMessage)
        {
            switch ((int)wParam)
            {
                case Win32.ABN_POSCHANGED:
                case Win32.ABN_STATECHANGE:
                    Refresh();
                    break;
                case Win32.ABN_FULLSCREENAPP:
                    FullScreenAppActive = lParam != IntPtr.Zero;
                    FullScreenChanged?.Invoke(FullScreenAppActive);
                    break;
            }
        }
        else if (msg is Win32.WM_DISPLAYCHANGE or Win32.WM_DPICHANGED or Win32.WM_SETTINGCHANGE)
        {
            Refresh();
        }

        return IntPtr.Zero;
    }

    public void Refresh()
    {
        var next = Query();
        if (next == Rail) return;
        Rail = next;
        RailChanged?.Invoke(next);
    }

    private TaskbarRail Query()
    {
        var data = new Win32.APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.APPBARDATA>(),
            hWnd = _hwnd,
        };

        if (Win32.SHAppBarMessage(Win32.ABM_GETTASKBARPOS, ref data) == IntPtr.Zero)
            return FallbackRail();

        var state = Win32.SHAppBarMessage(Win32.ABM_GETSTATE, ref data);
        bool autoHide = ((int)state & Win32.ABS_AUTOHIDE) != 0;

        return new TaskbarRail(
            (TaskbarEdge)data.uEdge,
            data.rc.Left, data.rc.Top, data.rc.Right, data.rc.Bottom,
            autoHide);
    }

    /// <summary>
    /// If the shell will not answer (rare, but happens when explorer is restarting),
    /// assume a standard bottom bar off the work area rather than dropping the cat at 0,0.
    /// </summary>
    private static TaskbarRail FallbackRail()
    {
        var work = new Win32.RECT();
        Win32.SystemParametersInfo(Win32.SPI_GETWORKAREA, 0, ref work, 0);
        int screenH = (int)SystemParameters.PrimaryScreenHeight;
        int screenW = (int)SystemParameters.PrimaryScreenWidth;
        return new TaskbarRail(TaskbarEdge.Bottom, 0, work.Bottom, screenW, screenH, false);
    }

    public void Dispose()
    {
        if (!_registered) return;
        var data = new Win32.APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.APPBARDATA>(),
            hWnd = _hwnd,
        };
        Win32.SHAppBarMessage(Win32.ABM_REMOVE, ref data);
        _registered = false;
    }
}
