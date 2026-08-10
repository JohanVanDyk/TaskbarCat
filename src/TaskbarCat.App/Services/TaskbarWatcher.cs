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
    private readonly IntPtr _hwnd;
    private readonly uint _callbackMessage;
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
    }

    public TaskbarRail Rail { get; private set; }

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
