using TaskbarCat.App.Interop;

namespace TaskbarCat.App.Services;

/// <summary>
/// Hides the mouse pointer so the toy overlay can be the pointer.
///
/// This is the only thing in the app that changes state belonging to the whole desktop, and
/// that state outlives the process. Restoration therefore happens in four places — leaving toy
/// mode, Dispose, session end, and both crash handlers — none of which help if the process is
/// killed from Task Manager mid-play, which would leave the user with an invisible cursor
/// until they log off.
///
/// <see cref="RestoreAtStartup"/> is the answer to that: every launch puts the system cursors
/// back before anything else runs, so a previous instance that died badly is repaired by the
/// next start — which is exactly what somebody does when their pointer has vanished.
/// </summary>
internal static class ToyCursorService
{
    private static bool _hidden;

    /// <summary>Reloads the user's cursor scheme. Safe to call when nothing is wrong.</summary>
    public static void RestoreAtStartup() =>
        Win32.SystemParametersInfo(Win32.SPI_SETCURSORS, 0, IntPtr.Zero, Win32.SPIF_SENDCHANGE);

    public static void Hide()
    {
        if (_hidden) return;

        // A 32x32 icon whose AND mask is all 1s and XOR mask all 0s is fully transparent:
        // "leave every pixel as it was". No bitmap, no GDI+, nothing to leak.
        var and = new byte[32 * 32 / 8];
        var xor = new byte[32 * 32 / 8];
        Array.Fill(and, (byte)0xFF);

        var blank = Win32.CreateIcon(IntPtr.Zero, 32, 32, 1, 1, and, xor);
        if (blank == IntPtr.Zero) return;

        // SetSystemCursor takes ownership of the handle, so it gets one it may destroy.
        _hidden = Win32.SetSystemCursor(blank, Win32.OCR_NORMAL);
    }

    public static void Restore()
    {
        if (!_hidden) return;
        _hidden = false;
        RestoreAtStartup();
    }
}
