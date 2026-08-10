# Sprite animation brief (prompt for ChatGPT / image models)

The current sheets are 1–8 frames per clip, which is why the cat looks jerky: `sit_look` is
2 frames, `scratch` and `stretch_yawn` are 1. No playback tuning fixes that — the frames do
not exist. This is the brief for replacing them.

## How to use it

Paste the block below into ChatGPT **and attach `assets/cat/orange_white/sit_look.png` and
`walk_right.png`** so it has the existing style to match. Ask for **one clip per request** —
image models degrade badly when asked for a 24-cell grid in one shot, and clip-at-a-time
also lets you re-roll a bad one without losing the others.

Expect to iterate. If sheets come back misaligned, ask for single frames (one image per
frame) and stitch them with `tools/stitch_frames.py`-style code rather than fighting the
grid. Whatever comes back, run `python3 tools/make_preset.py` afterwards to regenerate the
grey and blue coats from the new orange sheets.

---

## THE PROMPT

> You are producing 2D sprite-sheet animation for a Windows desktop pet — a small cat that
> lives on the taskbar. I am attaching two existing sheets. **Match their character design,
> palette, line weight and shading exactly**: same orange-and-white short-haired cat, same
> big dark eyes with a single specular highlight, same soft cel shading with a thin darker
> outline, same chunky chibi proportions (big head, short legs, ~3 heads tall).
>
> **Output format — follow exactly:**
> - One PNG per clip, a **horizontal strip**: frames left to right in a single row.
> - Each frame is **exactly 160 x 128 pixels**. Sheet width = 160 x frame count, height = 128.
> - **Transparent background** (real alpha, RGBA). No checkerboard, no white box, no border,
>   no drop shadow, no ground shadow, no background scenery, no text, no frame numbers.
> - The cat must sit on a consistent **baseline: its feet touch y = 118** in every frame of
>   every clip (except while genuinely airborne). It must not drift up or down between frames.
> - The cat is horizontally centred in its frame and occupies roughly 80–110 px wide,
>   100–120 px tall. Same scale in every clip — it must not change size between clips.
> - Consecutive frames must differ by a **small increment**. This is animation, not six
>   separate drawings of a cat: trace the same body between poses, keep volumes constant,
>   move by eased in-betweens. Smooth is the entire point.
> - Clips marked LOOPING must cycle seamlessly: the last frame flows into the first with the
>   same spacing as every other pair. Do not repeat the first frame at the end.
>
> **Animation direction:** favour weight and follow-through. Ears and tail lag behind the
> body and settle a frame late. Squash on impact, stretch at the top of a leap. Keep the eyes
> alive — blinks are 2 frames, not 1.
>
> Produce these clips:
>
> 1. **`run_right`** — 12 frames, LOOPING. A full running gait cycle facing right: gather,
>    push off, extended airborne reach, front paws land, body compresses, rear legs swing
>    through. Tail streams out behind and whips on the turns. All four paws leave the ground
>    at full extension. The cat stays centred — it runs in place; the app moves the window.
> 2. **`run_left`** — 12 frames, LOOPING. The same cycle mirrored to face left. It must be a
>    true mirror of `run_right` so the two read as one animation in different directions.
> 3. **`scratch_icons`** — 14 frames, LOOPING. Sitting upright, facing right, reaching up and
>    raking one front paw down a vertical surface just off the right edge of the frame, like a
>    cat clawing a door frame. Claws out. Alternate paws every 7 frames. Body rocks slightly
>    with each pull; ears flick back on the down-stroke.
> 4. **`zoomies`** — 16 frames, LOOPING. A manic burst: crouch, explosive leap up and forward,
>    airborne with all legs splayed and back arched, land in a skid with front paws braced and
>    haunches sliding, whip around, coil, repeat. Wild eyes, fur puffed, tail fat and lashing.
>    Sell it with big shape change between frames — this clip is allowed to be the loudest.
> 5. **`jump`** — 10 frames, ONE-SHOT (does not loop). Deep crouch, spring, high arc with the
>    body stretched, a beat at the apex, tuck, land with a squash, recover to a neutral sit.
> 6. **`sleep`** — 12 frames, LOOPING. Curled up nose-to-tail, eyes shut, a slow breathing
>    cycle — the ribcage rises over 6 frames and falls over 6. Almost nothing else moves; one
>    ear twitch at frame 9. This must be extremely subtle and perfectly seamless; it is on
>    screen more than everything else combined.
> 7. **`paw_screen`** — 14 frames, LOOPING. Sitting facing the viewer, patting the inside of
>    the screen with one front paw — reaching toward camera so the paw foreshortens and gets
>    slightly larger as it comes forward, tapping the glass, pulling back. Head tilts, eyes
>    wide and pleading, mouth opens on the second tap. Reads as "pay attention to me".
> 8. **`watch_bug`** — 18 frames, LOOPING. Sitting still, tracking an invisible insect: eyes
>    and head follow a small erratic path — left, pause, up, quick dart right, pause. Ears
>    swivel independently toward it. Tail tip flicks. On frames 14–16 the head does the tiny
>    twitchy chatter cats do at prey, then it settles. Body stays planted; all the life is in
>    the head, eyes, ears and tail tip.
>
> Deliver each clip as a separate PNG named `run_right.png`, `run_left.png`,
> `scratch_icons.png`, `zoomies.png`, `jump.png`, `sleep.png`, `paw_screen.png`,
> `watch_bug.png`. For each, state the frame count and the intended playback fps.

---

## Wiring the results in

Drop the PNGs into `assets/cat/orange_white/` and add each clip to `assets/sprites.json`:

```json
{ "id": "run_right", "sheet": "run_right.png", "frames": 12, "fps": 16, "loop": true }
```

Suggested fps: runs 16, `zoomies` 18, `jump` 14 (`"loop": false`), `scratch_icons` 12,
`paw_screen` 12, `watch_bug` 10, `sleep` 6.

Two code-side notes:

- `CatController.AwakeFps` is **15** — a 16–18 fps clip will not play at its own rate. Raise
  the awake tick to 30 for these, or the new frames get dropped and it will still look jerky.
  Sleeping stays at 4 fps; it dominates average CPU and `sleep` at 6 fps is close enough.
- New clip ids need mapping in `ClipMap.ClipFor` before the behaviour engine will ever pick
  them. Unmapped clips are silently never shown — `ResolveClip` falls back to `idle_blink`.
