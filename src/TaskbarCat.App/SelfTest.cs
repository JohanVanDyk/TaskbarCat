using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using TaskbarCat.App.Services;
using TaskbarCat.App.Views;
using TaskbarCat.Models;

using Application = System.Windows.Application;

namespace TaskbarCat.App;

/// <summary>
/// Headless verification hook. WinExe has no console attached and this app is developed
/// from WSL, so the app reports what it resolved to a file instead: run it, read the
/// file, assert on the numbers. Without this the only way to check placement or
/// behaviour is a human watching the screen.
///
/// --selftest-stimulus=feed,pet fires menu actions on a timer so the reaction path can
/// be exercised without a mouse.
///
/// --selftest-startup=on|off drives the "Start with Windows" toggle without the tray menu,
/// which cannot be clicked headlessly.
///
/// --selftest-name=, --selftest-coat= and --selftest-customize exercise the Customize panel's
/// three effects — rename, coat swap, and the dialog itself — without a mouse.
///
/// --selftest-chonk=0..3 forces the overfed size, so all four can be photographed without
/// feeding the cat nine times and waiting an hour for it to slim back down.
/// </summary>
internal static class SelfTest
{
    public const string DefaultReportPath = @"C:\dev\TaskbarCat\artifacts\selftest.txt";

    public static void Arm(
        Application app,
        CatWindow window,
        CatController controller,
        TrayIconService tray,
        Action showCustomize,
        Action<string> setCoat,
        ToyController toys,
        string[] args)
    {
        double seconds = 3.0;
        string path = DefaultReportPath;
        var stimuli = new List<Stimulus>();

        foreach (var arg in args)
        {
            if (arg.StartsWith("--selftest-seconds=", StringComparison.OrdinalIgnoreCase))
                double.TryParse(arg["--selftest-seconds=".Length..], out seconds);
            else if (arg.StartsWith("--selftest-out=", StringComparison.OrdinalIgnoreCase))
                path = arg["--selftest-out=".Length..];
            else if (arg.StartsWith("--selftest-startup=", StringComparison.OrdinalIgnoreCase))
            {
                var want = arg["--selftest-startup=".Length..].Trim();
                StartupService.SetEnabled(want.Equals("on", StringComparison.OrdinalIgnoreCase));
            }
            else if (arg.StartsWith("--selftest-name=", StringComparison.OrdinalIgnoreCase))
                controller.Rename(arg["--selftest-name=".Length..]);
            else if (arg.StartsWith("--selftest-chonk=", StringComparison.OrdinalIgnoreCase))
            {
                // Bypasses the feed counter: screenshotting all four sizes otherwise means
                // feeding nine times and waiting an hour. Goes through the controller so the
                // drawn chonk sheets are actually loaded, not just the stretch applied.
                if (int.TryParse(arg["--selftest-chonk=".Length..], out var lvl))
                    controller.ForceChonk(lvl);
            }
            else if (arg.StartsWith("--selftest-toy=", StringComparison.OrdinalIgnoreCase))
            {
                var want = arg["--selftest-toy=".Length..].Trim();
                if (Enum.TryParse<ToyKind>(want, true, out var toy)) toys.Start(toy);
            }
            else if (arg.StartsWith("--selftest-coat=", StringComparison.OrdinalIgnoreCase))
                setCoat(arg["--selftest-coat=".Length..].Trim());
            else if (arg.StartsWith("--selftest-stimulus=", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var name in arg["--selftest-stimulus=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Enum.TryParse<Stimulus>(name.Trim(), true, out var s))
                        stimuli.Add(s);
                }
            }
        }

        // Opens the radial menu without a mouse. Synthetic clicks are unreliable here: the
        // taskbar is auto-hide, so moving the cursor to the cat's row pops the taskbar up
        // over it and eats the click.
        if (args.Contains("--selftest-menu"))
        {
            var open = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Min(3, seconds / 2)) };
            open.Tick += (_, _) => { open.Stop(); window.ShowRadialMenu(); };
            open.Start();
        }

        // Same trick for the Customize dialog, so a capture script has something to photograph.
        if (args.Contains("--selftest-customize"))
        {
            var open = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Min(2, seconds / 2)) };
            open.Tick += (_, _) => { open.Stop(); showCustomize(); };
            open.Start();
        }

        var trace = new List<string>();
        controller.ActionChanged += (action, mood, clip) =>
            trace.Add($"{DateTime.UtcNow:HH:mm:ss.fff} {action} mood={mood} clip={clip}");

        for (int i = 0; i < stimuli.Count; i++)
        {
            var s = stimuli[i];
            var fire = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(0.5, seconds * (i + 1) / (stimuli.Count + 1.0))),
            };
            fire.Tick += (_, _) =>
            {
                fire.Stop();
                trace.Add($"{DateTime.UtcNow:HH:mm:ss.fff} STIMULUS {s}");
                controller.Send(s);
            };
            fire.Start();
        }

        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(seconds),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            // Stop toy mode BEFORE reporting: the report records whether the cursor came back,
            // and a self-test that left the desktop without a pointer would be worse than no
            // self-test at all.
            toys.Stop();
            try { Report(window, controller, tray, toys, trace, path); }
            finally { app.Shutdown(); }
        };
        timer.Start();
    }

    private static void Report(CatWindow window, CatController controller, TrayIconService tray, ToyController toys, List<string> trace, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"utc={DateTime.UtcNow:O}");

        if (window.Rail is { } r)
        {
            sb.AppendLine($"rail.edge={r.Edge}");
            sb.AppendLine($"rail.rect={r.Left},{r.Top},{r.Right},{r.Bottom}");
            sb.AppendLine($"rail.autohide={r.IsAutoHide}");
            sb.AppendLine($"bar.revealed={window.BarRevealed}");
            sb.AppendLine($"perch={window.Perch:F2}");
        }
        else
        {
            sb.AppendLine("rail=UNRESOLVED");
        }

        sb.AppendLine($"window.visible={window.IsVisible}");
        sb.AppendLine($"dpi.scale={window.Scale}");
        sb.AppendLine($"screen.physical={window.ScreenPhysicalWidth}x{window.ScreenPhysicalHeight}");
        sb.AppendLine($"reposition.count={window.RepositionCount}");
        sb.AppendLine($"clicks.received={window.ClickCount}");
        sb.AppendLine($"menu.opened={window.MenuOpenCount}");
        sb.AppendLine($"raw.buttonmsgs={window.RawButtonMessages}");
        sb.AppendLine($"reposition.last={window.LastPlacement}");

        sb.AppendLine($"cat.name={controller.Name}");
        sb.AppendLine($"cat.preset={controller.PresetId}");
        sb.AppendLine($"cat.chonk={controller.ChonkLevel}");
        sb.AppendLine($"cat.chonkart={controller.ChonkArtClips}");
        sb.AppendLine($"toy.active={toys.Active}");
        sb.AppendLine($"toy.yarnart={toys.CanStart(ToyKind.Yarn)} toy.laserart={toys.CanStart(ToyKind.Laser)}");
        sb.AppendLine($"cat.action={controller.Action}");
        sb.AppendLine($"cat.mood={controller.Mood}");
        sb.AppendLine($"cat.clip={window.CurrentClipId}");
        sb.AppendLine($"cat.along={controller.Along:F3}");
        sb.AppendLine($"needs.fullness={controller.Needs.Fullness:F1}");
        sb.AppendLine($"needs.cleanliness={controller.Needs.Cleanliness:F1}");
        sb.AppendLine($"needs.affection={controller.Needs.Affection:F1}");
        sb.AppendLine($"needs.tiredness={controller.Needs.Tiredness:F1}");
        sb.AppendLine($"settings.path={controller.SettingsPath}");
        sb.AppendLine($"tray.icon={tray.IconSource}");
        sb.AppendLine($"startup.supported={StartupService.IsSupported}");
        sb.AppendLine($"startup.enabled={StartupService.IsEnabled()}");
        sb.AppendLine($"startup.exe={StartupService.ExePath}");

        sb.AppendLine($"trace.count={trace.Count}");
        foreach (var line in trace.TakeLast(40)) sb.AppendLine("  " + line);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
    }
}
