using System.Runtime.InteropServices;

namespace TaskbarCat.App.Interop;

/// <summary>
/// Every P/Invoke in the app lives here. Nothing else declares extern — this is the
/// only untestable surface, so it is also the only thing that gets mocked.
/// </summary>
internal static class Win32
{
    // ---- messages ----
    internal const int WM_NCHITTEST = 0x0084;
    internal const int WM_DISPLAYCHANGE = 0x007E;
    internal const int WM_DPICHANGED = 0x02E0;
    internal const int WM_SETTINGCHANGE = 0x001A;

    internal const int HTTRANSPARENT = -1;
    internal const int HTCLIENT = 1;

    // ---- window styles ----
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    internal static readonly IntPtr HWND_TOPMOST = new(-1);

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;

    // ---- appbar ----
    internal const uint ABM_NEW = 0x00000000;
    internal const uint ABM_REMOVE = 0x00000001;
    internal const uint ABM_GETTASKBARPOS = 0x00000005;
    internal const uint ABM_GETSTATE = 0x00000004;

    internal const int ABS_AUTOHIDE = 0x0000001;

    internal const int ABN_STATECHANGE = 0x0000000;
    internal const int ABN_POSCHANGED = 0x0000001;
    internal const int ABN_FULLSCREENAPP = 0x0000002;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    internal static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>Win10 1607+. Per-monitor DPI for the window's current monitor.</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    /// <summary>
    /// The shell's taskbar window. Needed because an auto-hide bar sliding in or out sends NO
    /// appbar notification — ABN_STATECHANGE fires when the auto-hide SETTING changes, not when
    /// the bar moves — so the only way to know it is on screen is to look at where it is.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // ---- cursor ----
    // SetSystemCursor changes the pointer for the WHOLE desktop and TAKES OWNERSHIP of the
    // handle it is given, so it must be handed a copy. SPI_SETCURSORS is the undo: it reloads
    // every cursor from the user's scheme. See ToyCursorService for how restoration is
    // guaranteed even when this process dies badly.

    internal const uint OCR_NORMAL = 32512;
    internal const uint SPI_SETCURSORS = 0x0057;
    internal const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetSystemCursor(IntPtr hcur, uint id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr CreateIcon(IntPtr hInstance, int nWidth, int nHeight,
        byte cPlanes, byte cBitsPixel, byte[] lpbANDbits, byte[] lpbXORbits);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X, Y;
    }

    // ---- low-level mouse hook ----
    // Only used to see the right-click that cancels toy mode. The callback runs on the thread
    // that installed it and blocks ALL desktop input while it executes, so it must do nothing
    // but check a flag.

    internal const int WH_MOUSE_LL = 14;
    internal const int WM_RBUTTONDOWN = 0x0204;
    internal const int WM_RBUTTONUP = 0x0205;

    internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    internal const int WS_EX_TRANSPARENT = 0x00000020;

    internal const uint SPI_GETWORKAREA = 0x0030;

    // ---- icons ----

    /// <summary>
    /// Pulls icon frames straight out of an exe's own resources. Used for the tray icon so it
    /// comes from the single embedded icon group rather than a loose file that can be deleted,
    /// or dropped by a publish step, leaving the app unquittable.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    internal static extern int ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, int nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);
}
