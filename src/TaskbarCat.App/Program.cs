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
        // One cat per user session. Autostart makes a second instance easy to trigger — log on,
        // then launch it by hand — and two instances fight over settings.json, each overwriting
        // the other's needs every 20s. Local\ scopes the mutex to the session so Fast User
        // Switching still gets a cat each.
        using var single = new Mutex(initiallyOwned: true, @"Local\TaskbarCat.SingleInstance", out bool isFirst);
        if (!isFirst) return 0;

        // A WinExe has nowhere to print a stack trace, so an unhandled exception just makes the
        // cat disappear with no explanation. Write it down where the settings live.
        var crashLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarCat", "crash.log");
        void LogCrash(object? ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(crashLog)!);
                File.AppendAllText(crashLog, $"=== {DateTime.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { ToyCursorService.Restore(); LogCrash(e.ExceptionObject); };

        // Toy mode hides the system cursor, and a process killed from Task Manager never gets
        // to put it back. Repair it on every launch, before anything else: the user's response
        // to a missing pointer is to start things, and this makes that the fix.
        ToyCursorService.RestoreAtStartup();

        var assets = Path.Combine(AppContext.BaseDirectory, "assets");

        var store = new SettingsStore();
        var settings = store.Load();

        SpriteLibrary sprites;
        try
        {
            sprites = SpriteLibrary.Load(assets, settings.ColorPreset, settings.ChonkLevel);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load sprites from {assets}:\n\n{ex.Message}",
                "Taskbar Cat", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { ToyCursorService.Restore(); LogCrash(e.Exception); };
        var window = new CatWindow(sprites, assets);
        window.Show();

        var controller = new CatController(window, sprites, store, settings,
            (preset, chonk) => SpriteLibrary.Load(assets, preset, chonk));

        var toys = new ToyController(assets);
        controller.AttachToys(toys);

        void Quit()
        {
            toys.Dispose();          // puts the cursor and the hook back before anything else
            controller.Dispose();   // stops the clock and persists; safe to call twice
            app.Shutdown();
        }

        using var tray = new TrayIconService(
            assets,
            onExit: Quit,
            onReposition: () => window.Reposition());

        tray.SetLabel(controller.Name);
        controller.Renamed += tray.SetLabel;

        void SetCoat(string presetId)
        {
            try { controller.SetCoat(presetId); }
            catch (Exception ex)
            {
                // A coat whose sheets are missing or corrupt leaves the cat as it was.
                MessageBox.Show($"Could not load the '{presetId}' coat:\n\n{ex.Message}",
                    "Taskbar Cat", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        CustomizeWindow? customize = null;
        void ShowCustomize()
        {
            // Re-focus the open one rather than stacking dialogs; two of them would fight over
            // which coat is applied.
            if (customize is { IsVisible: true }) { customize.Activate(); return; }

            customize = new CustomizeWindow(assets, SpriteLibrary.LoadPresets(assets), controller.Name, controller.PresetId);
            customize.NameChanged += controller.Rename;
            customize.PresetChanged += SetCoat;
            customize.Closed += (_, _) => customize = null;
            customize.Show();
        }

        window.MenuChosen += id =>
        {
            switch (id)
            {
                case "customize": ShowCustomize(); break;
                case "close": Quit(); break;

                // The yarn IS the play action: picking it hands the toy to the pointer rather
                // than just making the cat play by itself.
                case "play": toys.Start(ToyKind.Yarn); break;
                case "laser": toys.Start(ToyKind.Laser); break;
                case "feed": controller.Send(Stimulus.Fed); break;
                case "brush": controller.Send(Stimulus.Brushed); break;
                case "pet": controller.Send(Stimulus.Petted); break;
            }
        };

        // Logoff and shutdown kill the process without OnClosed, so save here too.
        app.SessionEnding += (_, _) => { toys.Stop(); controller.Persist(); };

        if (args.Contains("--selftest"))
            SelfTest.Arm(app, window, controller, tray, ShowCustomize, SetCoat, toys, args);

        int code = app.Run();
        toys.Dispose();
        controller.Dispose();
        return code;
    }
}
