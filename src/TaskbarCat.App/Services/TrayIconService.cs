using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TaskbarCat.App.Interop;

namespace TaskbarCat.App.Services;

/// <summary>
/// System tray presence. The cat window is a tool window with no chrome, so this is the
/// only guaranteed way to quit — without it an invisible always-on-top window can only
/// be killed from Task Manager.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon? _owned;

    public TrayIconService(string assetsDir, Action onExit, Action onReposition)
    {
        var startup = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = false, // the registry, not the click, decides what the tick shows
            Checked = StartupService.IsEnabled(),
            Enabled = StartupService.IsSupported,
        };
        startup.Click += (_, _) => startup.Checked = StartupService.SetEnabled(!startup.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Reposition cat", null, (_, _) => onReposition());
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => onExit());

        (_owned, IconSource) = LoadIcon(assetsDir);

        _icon = new NotifyIcon
        {
            Icon = _owned ?? SystemIcons.Application,
            Text = "Taskbar Cat",
            Visible = true,
            ContextMenuStrip = menu,
        };
    }

    /// <summary>
    /// Takes the small (16px) frame from the exe's own embedded icon group, falling back to
    /// assets/app.ico.
    ///
    /// The exe first, deliberately: the icon is compiled in by ApplicationIcon, and MSBuild
    /// then drops that same file from the publish output, so the file-based path shipped a
    /// drop with no tray icon. Reading the exe cannot go stale or go missing.
    ///
    /// The SMALL frame specifically — hand NotifyIcon the 256px frame and the shell downscales
    /// it on every paint, which turns the outline to mush.
    /// </summary>
    /// <summary>Where the icon actually came from: exe / file / stock. Reported by the self-test.</summary>
    public string IconSource { get; }

    private static (Icon? Icon, string Source) LoadIcon(string assetsDir)
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
        {
            var small = new IntPtr[1];
            try
            {
                if (Win32.ExtractIconEx(exe, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                {
                    // FromHandle does not own the handle, so clone into a managed icon we can
                    // dispose and release the native one immediately.
                    using var borrowed = Icon.FromHandle(small[0]);
                    return ((Icon)borrowed.Clone(), "exe");
                }
            }
            catch (Exception ex) when (ex is ArgumentException or ExternalException)
            {
                // fall through to the file
            }
            finally
            {
                if (small[0] != IntPtr.Zero) Win32.DestroyIcon(small[0]);
            }
        }

        var path = Path.Combine(assetsDir, "app.ico");
        try
        {
            if (File.Exists(path)) return (new Icon(path, SystemInformation.SmallIconSize), "file");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // A missing or corrupt icon falls back to the stock one rather than killing the tray,
            // which would leave the app unquittable.
        }

        return (null, "stock");
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _owned?.Dispose();
    }
}
