# Toy mode — design

Selecting the yarn (or the laser) from the radial menu turns the mouse pointer into the toy.
The cat chases it. Right-click cancels.

## Why this is the risky feature so far

Everything until now has been contained inside the cat's own window. This one reaches out and
changes **global system state** — the mouse cursor, and a system-wide input hook. Both outlive
the process if it dies badly. That single fact drives most of the design below.

## Components

```
Core (no WPF, unit-testable)
  ToyKind            Yarn | Laser
  ToyChase           the state machine: given the toy's screen position and the cat's rail
                     geometry, decides Chase / ReachUp / Caught / Confused and a target x

App
  ToyCursorService   replaces the system cursor, restores it, and guarantees restoration
  ToyOverlayWindow   click-through topmost sprite drawn at the pointer (the visible toy)
  MouseHookService   WH_MOUSE_LL, only to see the cancelling right-click
  ToyController      owns the three above, feeds ToyChase, drives CatController
```

`ToyChase` is where the behaviour lives and it takes no Win32 types, so "cat reaches up when
the toy is 300px above the rail" is a unit test rather than something you verify by waving a
mouse around.

## The cursor, and how it gets restored

The pointer must actually BECOME the yarn — an overlay sprite next to a normal arrow is not
what was asked for. So:

1. `SetSystemCursor` installs a fully transparent 1x1 cursor over `OCR_NORMAL`, hiding the arrow.
2. `ToyOverlayWindow` draws the animated toy at `GetCursorPos`, at 60Hz, click-through
   (`WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`) so it never eats a click.

Animation is why the overlay exists at all: a static cursor could be installed directly, but a
yarn ball that does not roll is a worse toy, and re-installing a system cursor every frame is
not a real option.

**Restoration is the hard part.** `SystemParametersInfo(SPI_SETCURSORS)` puts the arrow back,
and it is called on: exiting toy mode, `Dispose`, `SessionEnding`, and both unhandled-exception
handlers. None of that saves a process killed from Task Manager, which would leave the user
with an invisible pointer until they log off.

Mitigation, and it is the important one: **the app restores system cursors unconditionally at
startup**, before anything else. A previous instance that died mid-play is repaired by the next
launch, and launching is exactly what a user does when something is broken.

## Cancelling

Right-click has to cancel from anywhere, not just over the cat, so it needs a low-level mouse
hook. The hook does one thing — swallow the right-button-down that cancels, so no context menu
opens behind it — and passes everything else straight through. It is installed only while toy
mode is active and removed the moment it ends.

A hook that is slow blocks the entire desktop's input, so it does no work beyond a flag check.

## The chase

`ToyChase` compares the toy's position to the cat's rail:

| toy is | cat does | clip |
|---|---|---|
| far along the rail | runs toward it | `run_left` / `run_right` |
| close, at rail height | catches it | `play_yarn` (yarn) / `jump` then `confused` (laser) |
| close, well above the rail | rears up and bats at it | `reach_up` |
| gone (mode cancelled) | returns to normal behaviour | — |

The laser is the one with a joke in it: the pounce always "succeeds" and always fails, because
there is nothing to catch. Catching it plays `jump`, then `confused`, then back to normal.

New actions never enter the mood weight tables — like `Startled`, they can only happen because
something is happening TO the cat.

## Cost of the art

Every new clip now costs three sheets (normal, chonk2, chonk3) because the coats derive
automatically but the weights do not. That is the real budget line, and it is why `reach_up`
is a new clip rather than reusing `scratch_icons`: worth 3 sheets, not 30.
