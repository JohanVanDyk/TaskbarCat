# Taskbar Cat

A cat that lives on your Windows taskbar. It sleeps, walks, grooms, gets hungry, and reacts
when you feed, brush, pet or play with it. Click it for the radial menu — Feed, Brush, Pet,
Play, Customize, Close. Right-click the tray icon to reposition, toggle autostart, or quit.

**Customize** names the cat and picks its coat. Both apply live: the name shows on the tray
tooltip, and the coat swaps without restarting or interrupting what the cat is doing. Cancel
or Escape puts both back.

C# / .NET 8, WPF. Windows 10 1607+ (per-monitor DPI), x64.

## Install

Grab the zip from `dist/`, unpack it anywhere, run `TaskbarCat.exe`. Keep the `assets`
folder next to the exe — the sprites load from there.

Right-click the tray icon → **Start with Windows** to have it come back at logon. That writes
the exe's current path to `HKCU\...\Run`, so re-tick it if you move the folder.

Only one cat runs per session; launching a second time is a no-op.

State (needs, name, position) lives in `%APPDATA%\TaskbarCat\settings.json`. Delete it for a
fresh cat.

## Build

```powershell
dotnet build src/TaskbarCat.App          # debug
dotnet test  tests/TaskbarCat.Tests      # 42 tests
powershell -File tools/publish.ps1       # self-contained exe + zip -> dist/
powershell -File tools/publish.ps1 -FrameworkDependent   # ~1MB, needs .NET 8 Desktop Runtime
```

Developed from WSL against a Windows toolchain — `dotnet.exe`, `powershell.exe` and
`python3` all work from bash, so the whole build/run/verify loop is available without
leaving the shell.

## Layout

```
src/TaskbarCat.Core   behaviour, needs, motion, placement maths, settings.
                      net8.0 with NO WPF reference, so the layering is compiler-enforced
                      and a 24h simulation is a unit test.
src/TaskbarCat.App    net8.0-windows WPF exe: window, sprites, tray, Win32 interop.
tests/TaskbarCat.Tests  xunit, Core only.
tools/                sprite/icon extraction (python) and verification (powershell).
```

## Verifying it

A WinExe has no stdout and the cat window is transparent and always-on-top, so both the
usual ways to check a GUI are closed off. The app reports on itself instead:

```powershell
.\TaskbarCat.exe --selftest --selftest-seconds=6 --selftest-out=artifacts\selftest.txt
```

That runs for N seconds, writes the resolved taskbar rail, DPI scale, window placement, needs,
name, coat, tray icon source and the action trace to a file, then exits. Extra flags:
`--selftest-stimulus=feed,pet` fires menu actions on a timer, `--selftest-menu` opens the
radial menu, `--selftest-customize` opens the Customize dialog, `--selftest-name=` and
`--selftest-coat=` drive what that dialog drives, and `--selftest-startup=on|off` toggles
autostart.

The radial menu closes as soon as it loses focus, so a capture script has to poll for it
rather than sleep for a fixed time and shoot once.

`tools/capture_window.ps1` screenshots the cat (`PrintWindow` with
`PW_RENDERFULLCONTENT` — BitBlt silently skips layered windows), and
`tools/probe_hittest.ps1` checks that clicks pass through the transparent pixels.

**Every capture or query script must call `SetProcessDpiAwarenessContext(-4)` first.** A
DPI-unaware caller gets `GetWindowRect` results silently scaled by 1/scale, so the tools
report a bug that is not there.

## Assets

`assets/sprites.json` is the manifest: clips, frame counts, fps, and the colour presets.
Sheets are 160x128-per-frame horizontal strips under `assets/cat/<preset>/`.

Eight clips (`run_left`, `run_right`, `zoomies`, `jump`, `sleep`, `scratch_icons`,
`paw_screen`, `watch_bug`) are 9–18 frames and drive most of what you see. The original
spec-sheet extractions they replaced were 1–4 frames each, which is what made the cat look
like a slideshow; they are still in the manifest as the fallback `SpriteLibrary` lands on if
a sheet goes missing. `docs/ANIMATION_PROMPT.md` is the brief they were generated from and
`tools/ingest_sheet.py` is what turns a generated canvas into a conforming strip.

Note `CatController.AwakeFps` is the ceiling on every clip's own fps — a 30 Hz tick is what
lets a 16 or 18 fps run cycle actually play at its authored rate.

Only **Orange & White** is drawn art. **Grey & White** and **Blue & White** are derived from it
by `tools/make_preset.py`, which recolours the saturated coat pixels and leaves the whites,
outline and eyes alone — replace them with real sheets when there are any. A preset is only
offered in the picker if its sheet directory actually exists, so dropping a folder in and
adding a manifest entry is the whole job.

```bash
python3 tools/make_preset.py tuxedo --name "Tuxedo" --sat 0.05 --val 0.55
```

`tools/make_icon.py` regenerates `assets/app.ico` from a sprite frame; the icon is compiled
into the exe and the tray reads it back out of there.
