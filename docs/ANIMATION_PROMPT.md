# Sprite animation brief (prompt for ChatGPT / image models)

The current sheets are 1–8 frames per clip, which is why the cat looks jerky: `sit_look` is
2 frames, `scratch` and `stretch_yawn` are 1. No playback tuning fixes that — the frames do
not exist. This is the brief for replacing them.

## How to use it

Paste the block below into ChatGPT **and attach `assets/cat/orange_white/sit_look.png` and
`walk_right.png`** so it has the existing style to match. Ask for **one clip per request** —
image models degrade badly when asked for a 24-cell grid in one shot, and clip-at-a-time
also lets you re-roll a bad one without losing the others.

**What actually came back** (2026-08-11 run): eight 1536x1024 canvases, each a rough
horizontal row — right idea, none of the pixel spec. The cat was a different size in every
clip, spacing was uneven, baselines drifted, neighbouring cats touched, and most clips had
1–3 fewer frames than asked for. Do not expect to fix that by rewording the brief; it is
what the medium does.

`tools/ingest_sheet.py` exists to absorb it: it segments the frames as connected components,
splits blobs where two cats were drawn touching (cutting at the lowest-ink column, not the
midpoint), drops the sliver of the neighbour left behind, normalises scale across clips, and
registers every frame against the clip's own median ground line so grounded poses sit on the
baseline while airborne ones keep their lift.

```bash
python3 tools/ingest_sheet.py --report     # frame counts and sizes, changes nothing
python3 tools/ingest_sheet.py --write      # writes the 160x128 strips into assets/
python3 tools/make_preset.py grey_white --name "Grey & White" --sat 0.16 --val 0.92
python3 tools/make_preset.py blue_white --name "Blue & White" --hue 205 --sat 0.55 --val 0.98
```

Take the frame counts `--report` prints into `sprites.json` rather than the counts you asked
for — a clip declaring more frames than the sheet holds crashes the slicer.

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

## Second brief: every clip still on the original art

Audit of the manifest against `ClipMap` (only clips the engine can actually reach matter):

| clip | frames | why it looks wrong |
|---|---|---|
| `stretch_yawn` | **1** | a still image — there is no animation at all |
| `happy_hearts` | 2 | two-frame flicker |
| `cursor_interaction` | 2 | two-frame flicker |
| `loaf` | 3 | |
| `eat` | 5 | |
| `groom` | 6 | |
| `idle_blink` | 8 | the default resting pose, so it is the most-seen clip after sleep |

Nine other old clips (`sit_look`, `walk_left`, `walk_right`, `play_pounce`, `scratch`,
`meow_attention`) are unreachable since `ClipMap` was repointed at the new art — they stay as
missing-sheet fallbacks and are not worth regenerating.

Attach `style_reference.png` and `watch_bug.png`, **not** the original spec sheets: matching
the generated look is the target now. Ids must stay exactly as they are — the engine maps its
actions onto these names and a renamed clip is silently never shown.

> Continuing the same cat and the same sprite sheets. Match the attached reference exactly —
> that is the established look: soft painterly cel shading, large glossy eyes with a bright
> highlight, warm orange tabby markings over a cream chest and paws, no hard outline.
>
> Same output rules as before: one PNG per clip, horizontal strip, each frame exactly
> 160 x 128 px, transparent RGBA background, no shadow or backdrop, the cat centred with its
> feet at y = 118 in every frame, same body scale as the reference, and small eased
> increments between consecutive frames so it reads as motion rather than separate drawings.
> All seven LOOP seamlessly.
>
> 1. **`idle_blink`** — 14 frames. Sitting front-on doing almost nothing: a slow breath across
>    the whole cycle, one unhurried double blink around frames 5-8, ears adjusting slightly,
>    tail tip curling once. This is the default resting clip and plays constantly, so it must
>    be calm and completely seamless — no pose that draws the eye.
> 2. **`groom`** — 14 frames. Licking a raised front paw, then wiping it over the ear and
>    cheek: lift the paw, three quick licks, paw over the ear twice, settle back. Head tilts
>    into the paw, eyes half closed and content, tongue visible on the licks.
> 3. **`eat`** — 12 frames. Head down, eating from a bowl on the ground in front: dip to the
>    bowl, three chewing beats with the cheeks working, lift the head, lick the muzzle, dip
>    again. Include the bowl in frame at the cat's feet, low and simple.
> 4. **`happy_hearts`** — 12 frames. Delighted: eyes squeezed into happy arcs, a wide open
>    smile, a small bouncing wiggle, tail up and quivering. Two or three small hearts float up
>    and fade near the head over the cycle.
> 5. **`loaf`** — 12 frames. Settled in a loaf: paws tucked completely out of sight, body a
>    rounded bread shape, eyes open and half-lidded, content. Only a slow breath, one lazy
>    blink and a single ear swivel across the whole cycle. Awake, unlike the curled sleep pose.
> 6. **`stretch_yawn`** — 14 frames. The full waking stretch: rise from sitting, front legs
>    extended forward with the chest dropped and the rear end up, back arched deep, a wide
>    yawn with the tongue curling and eyes screwed shut, then relax back to sitting. The one
>    clip that should feel luxurious and slow — hold the deepest stretch for two frames.
> 7. **`cursor_interaction`** — 14 frames. Tracking and batting at the mouse pointer: head
>    follows something just off to the right, then two quick swipes of a front paw at it, a
>    pause with the paw raised and ears forward, then a third swipe. Alert and playful, body
>    stays seated.
>
> For each, state the frame count and intended playback fps.

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
