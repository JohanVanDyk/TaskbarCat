using TaskbarCat.App.Interop;

namespace TaskbarCat.App.Services;

/// <summary>
/// Watches for the right-click that cancels toy mode, from anywhere on the desktop.
///
/// A low-level hook is the only way to see a click that lands on somebody else's window, and
/// it is installed ONLY while toy mode is active. The callback blocks all desktop input while
/// it runs, so it does nothing but compare a message id — no allocation, no logging, no work.
///
/// The cancelling click is swallowed rather than passed on, so no context menu opens behind
/// the cat as the toy disappears.
/// </summary>
internal sealed class MouseHookService : IDisposable
{
    private readonly Action _onRightClick;
    private readonly Win32.HookProc _proc;      // field, not a local: a collected delegate crashes the hook
    private IntPtr _hook;
    private bool _swallowNextUp;

    public MouseHookService(Action onRightClick)
    {
        _onRightClick = onRightClick;
        _proc = Callback;
    }

    public bool IsInstalled => _hook != IntPtr.Zero;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        Win32.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _swallowNextUp = false;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;

            if (msg == Win32.WM_RBUTTONDOWN)
            {
                _swallowNextUp = true;
                _onRightClick();
                return 1;                     // eaten: no context menu behind the cancel
            }

            // The matching up must go too, or the app underneath sees half a click.
            if (msg == Win32.WM_RBUTTONUP && _swallowNextUp)
            {
                _swallowNextUp = false;
                return 1;
            }
        }

        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
