using System.Drawing;
using System.Windows.Forms;

namespace TaskbarCat.App.Services;

/// <summary>
/// System tray presence. The cat window is a tool window with no chrome, so this is the
/// only guaranteed way to quit — without it an invisible always-on-top window can only
/// be killed from Task Manager.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIconService(Action onExit, Action onReposition)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Reposition cat", null, (_, _) => onReposition());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => onExit());

        _icon = new NotifyIcon
        {
            // TODO: replace with the real cat .ico once the art drop lands.
            Icon = SystemIcons.Application,
            Text = "Taskbar Cat",
            Visible = true,
            ContextMenuStrip = menu,
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
