using System.IO;
using Microsoft.Win32;

namespace TaskbarCat.App.Services;

/// <summary>
/// "Start with Windows", via the per-user Run key.
///
/// HKCU\...\Run rather than a scheduled task or the Startup folder: it needs no elevation,
/// no installer, and no shortcut file to go stale when the exe moves. A desktop pet that
/// only exists while a terminal is open is not a pet, so this is what makes the app real.
/// </summary>
internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskbarCat";

    /// <summary>
    /// Path Windows should launch. Empty when the host cannot report one (single-file
    /// publishes always can; some test hosts cannot), which disables the whole feature
    /// rather than registering a path that will not start.
    /// </summary>
    public static string ExePath => Environment.ProcessPath ?? string.Empty;

    public static bool IsSupported => !string.IsNullOrEmpty(ExePath);

    /// <summary>
    /// True only when the key points at THIS exe. A stale entry from a previous install
    /// location must read as off, otherwise the menu shows a tick for a cat that never
    /// appears at logon.
    /// </summary>
    public static bool IsEnabled()
    {
        if (!IsSupported) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string v && Unquote(v) == ExePath;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Returns the state actually achieved, so a failed write does not leave the menu lying.</summary>
    public static bool SetEnabled(bool enabled)
    {
        if (!IsSupported) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return IsEnabled();

            if (enabled) key.SetValue(ValueName, $"\"{ExePath}\"", RegistryValueKind.String);
            else key.DeleteValue(ValueName, throwOnMissingValue: false);

            return enabled;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Locked-down or policy-managed machines refuse the write. Losing autostart is
            // survivable; taking the cat down over it is not.
            return IsEnabled();
        }
    }

    // Run values are conventionally quoted so paths with spaces survive; compare unquoted.
    private static string Unquote(string value)
    {
        var v = value.Trim();
        return v.Length >= 2 && v[0] == '"' && v[^1] == '"' ? v[1..^1] : v;
    }
}
