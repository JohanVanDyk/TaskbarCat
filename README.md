# Taskbar Cat

A cat that lives on your Windows taskbar. It sleeps, walks, grooms, gets hungry, and reacts
when you feed, brush, pet or play with it. Click it for the radial menu — Feed, Brush, Pet,
Play, Customize, Close. Right-click the tray icon to reposition, toggle autostart, or quit.

**Toy mode.** Pick the yarn or the laser from the wheel and the mouse pointer *becomes* the
toy. The cat chases it along the taskbar, rears up and bats at it when you hold it overhead,
and wrestles the yarn when it catches it. Catching the laser gets it a pounce and then a look
of profound confusion, because there was never anything there. **Right-click anywhere cancels.**

**It rides the taskbar.** When an auto-hide taskbar slides up, the cat jumps on top of it and
carries on there — walking, sleeping, everything. When the bar slides away it jumps back down,
unless it was asleep, in which case the floor vanishes from under it and it drops with a
fright — a one-shot clip that lands, bristles, checks where the floor went, and recovers.

**Overfeed it and it gets fat.** Every 3 feedings the cat goes up a size, to a maximum of 3
sizes. It works back down one size per 20 minutes without being fed, and the size persists
across restarts — time the app was closed counts toward slimming, so a cat left for a week is
its normal shape again.

**Customize** names the cat and picks its coat. Both apply live: the name shows on the tray
tooltip, and the coat swaps without restarting or interrupting what the cat is doing. Cancel
or Escape puts both back.

C# / .NET 8, WPF. Windows 10 1607+ (per-monitor DPI), x64.

## Install

Run **`TaskbarCat-Setup-<version>.exe`** from `dist/`. It installs per-user into
`%LOCALAPPDATA%\Programs\TaskbarCat`, so there is no UAC prompt and no admin needed, and it
offers to start the cat at sign-in.

Or grab the zip instead, unpack it anywhere, and run `TaskbarCat.exe`. Keep the `assets`
folder next to the exe — the sprites load from there.

### The SmartScreen warning

**The installer is not code-signed, so Windows will warn whoever runs it.** They get a blue
"Windows protected your PC" box saying the publisher is unknown, and the Run button is hidden
behind **More info**. Nothing is wrong with the file; Windows is telling the truth — it cannot
tell who built it.

To get past it as a recipient: **More info → Run anyway**. If it was downloaded through a
browser it may also need unblocking first: right-click the file → Properties → tick **Unblock**
→ OK. That clears the Mark of the Web the browser attached. (Unzipping a downloaded zip
propagates that mark to every file inside it, which is one more reason to hand people the
installer rather than the zip.)

To make the warning stop for everyone, it has to be signed:

| option | cost | what it does |
|---|---|---|
| **Azure Trusted Signing** | ~$10/month | Microsoft-run signing service. Cheapest real answer. Needs an identity check — an organisation with 3+ years of verifiable history, or an individual account where available. |
| **EV code-signing certificate** | ~$300-600/year | Clears SmartScreen **immediately** on first release. Ships on a hardware token or in a cloud HSM. |
| **OV code-signing certificate** | ~$200-400/year | Signs the file, but SmartScreen still warns until the certificate builds reputation across enough installs. Cheaper, slower, and confusing in the meantime. |

A **self-signed certificate does not help.** SmartScreen trusts publishers, not signatures; a
certificate no one trusts changes nothing, and it adds a second failure mode when the cert
expires. Don't bother.

Once there is a certificate, `tools\build_installer.ps1` takes `-SignTool` and
`-CertThumbprint` and signs both the app exe and the setup exe — in that order, because the
installer embeds the app, and signing only the installer leaves the thing SmartScreen watches
long-term (the installed exe) unsigned.

Expect antivirus false positives regardless of signing. An always-on-top window that reads the
taskbar's position and writes a Run key is, structurally, what some adware does. If a scanner
flags it, submit it to that vendor as a false positive — there is no code change that avoids
this.

Right-click the tray icon → **Start with Windows** to have it come back at logon. That writes
the exe's current path to `HKCU\...\Run`, so re-tick it if you move the folder.

Only one cat runs per session; launching a second time is a no-op.

State (needs, name, position, size) lives in `%APPDATA%\TaskbarCat\settings.json`, and any
unhandled exception is appended to `crash.log` beside it. Delete the settings for a fresh cat.

## Build

```powershell
dotnet build src/TaskbarCat.App          # debug
dotnet test  tests/TaskbarCat.Tests      # 71 tests
powershell -File tools/publish.ps1       # self-contained exe + zip -> dist/
powershell -File tools/publish.ps1 -FrameworkDependent   # ~1MB, needs .NET 8 Desktop Runtime
powershell -File tools/build_installer.ps1               # publishes, then builds the installer
```

The installer needs Inno Setup: `winget install -e --id JRSoftware.InnoSetup`.
`installer/TaskbarCat.iss` is the script; it reads the version straight off the built exe so
the two can never disagree, and it declares the app's single-instance mutex as `AppMutex` so
setup asks the user to quit a running cat instead of failing partway through overwriting it.

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
`--selftest-coat=` drive what that dialog drives, `--selftest-chonk=0..3` forces the overfed
size (feeding nine times and waiting an hour is the alternative), `--selftest-toy=yarn|laser`
starts toy mode (and the harness always stops it before reporting, so a self-test can never
leave the desktop without a pointer), and `--selftest-startup=on|off` toggles autostart.

The radial menu closes as soon as it loses focus, so a capture script has to poll for it
rather than sleep for a fixed time and shoot once.

Windows sends **no** notification when an auto-hide bar slides in or out — `ABN_STATECHANGE`
fires when the auto-hide *setting* changes, not when the bar moves — so `TaskbarWatcher` polls
`Shell_TrayWnd`'s real rect from the cat's own tick and compares it against the rail the shell
reports. Comparing against the screen instead breaks on multi-monitor, where the bar's edge is
not the screen's.

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

Chonk levels 2 and 3 have **drawn art** for the six clips the cat spends its time in, under
`assets/cat/<preset>/chonk2/` and `chonk3/`. Everything else — level 1, and the other nine
clips at any level — falls back to stretching the normal sheet (`CatWindow.ChonkStretch`).
A clip loaded from drawn art renders 1:1; stretching it as well would fatten it twice, so the
decision is per clip, not per level. Drop more sheets into those folders and they take over
automatically.

`assets/toys/` holds the toys themselves on their own **64x64** grid — not the cat's 160x128,
and not coat-dependent. `ingest_sheet.py --toys` is the path for those: a toy hangs off the
pointer rather than standing on the ground, so it is centred in both axes instead of registered
to a baseline.

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
