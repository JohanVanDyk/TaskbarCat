using System.IO;
using System.Windows;
using TaskbarCat.App.Services;
using TaskbarCat.App.Views;
using TaskbarCat.Models;

using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TaskbarCat.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "assets");

        var store = new SettingsStore();
        var settings = store.Load();

        SpriteLibrary sprites;
        try
        {
            sprites = SpriteLibrary.Load(assets, settings.ColorPreset);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load sprites from {assets}:\n\n{ex.Message}",
                "Taskbar Cat", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new CatWindow(sprites, assets);
        window.Show();

        var controller = new CatController(window, sprites, store, settings);

        window.MenuChosen += id => controller.Send(id switch
        {
            "feed" => Stimulus.Fed,
            "brush" => Stimulus.Brushed,
            "pet" => Stimulus.Petted,
            "play" => Stimulus.PlayToyOffered,
            _ => Stimulus.Petted,
        });

        using var tray = new TrayIconService(
            onExit: () => { controller.Dispose(); app.Shutdown(); },
            onReposition: () => window.Reposition());

        // Logoff and shutdown kill the process without OnClosed, so save here too.
        app.SessionEnding += (_, _) => controller.Persist();

        if (args.Contains("--selftest"))
            SelfTest.Arm(app, window, controller, args);

        int code = app.Run();
        controller.Dispose();
        return code;
    }
}
