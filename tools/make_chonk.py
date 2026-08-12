#!/usr/bin/env python3
"""Derive chonk sheets for the SPRAWLED clips by deepening the cat's barrel.

Stand-in art, in the same spirit as make_preset.py deriving the grey and blue coats from the
orange one: better than the renderer's flat stretch, and replaced the moment drawn sheets land
(docs/ANIMATION_PROMPT.md, sixth brief).

Why this exists at all. A sprawled pose - running, walking, pouncing - is drawn fat by making
the cat DEEPER, not wider: widening it just makes it longer, which reads as a smear. The
renderer can only scale the whole frame, so it drags the head, legs and tail along with the
body. This does the one thing a scale cannot: it stretches the torso vertically and leaves the
head, the tail and the ground line where they are.

What it cannot do, and why the drawn art is still wanted: resampling can only move pixels that
exist. A real fat cat has a belly hanging BELOW the line of its legs, and there is nothing down
there to stretch, so what comes out is a deep-backed cat rather than a heavy-bellied one.

    python3 tools/make_chonk.py                 # both levels, every sprawled clip
    python3 tools/make_chonk.py --level 3 --clips run_left,run_right
"""

from __future__ import annotations

import argparse
import json
import pathlib

import numpy as np
from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
FRAME_W, FRAME_H = 160, 128

# Per level: (base width, base height, extra torso depth).
#
# The base is the whole cat, and it exists because a file in chonkN/ is treated as drawn art and
# renders 1:1 - the renderer stops applying ChonkVisuals entirely. Deepening the torso alone
# therefore came out SMALLER than the stretch it replaced, and read as a normal cat. Baking the
# same overall growth in first means this can only be an improvement on the fallback: same size,
# rounder middle.
#
# The extra is torso-only, on top of the base, and is the part a scale cannot do. Totals land
# near the drawn sleep_chonk sheets - about 1.16 wide and 1.26 tall at level 3 - with the growth
# concentrated in the barrel instead of spread over the head and paws.
GAIN = {
    2: (1.13, 1.12, 1.08),
    3: (1.16, 1.13, 1.12),
}

# How much of the bounding box at each end is protected from the stretch. The head is at one end
# and the tail at the other, and neither grows on a fat cat; only the barrel between them does.
END_GUARD = 0.28


def smoothstep(t: np.ndarray) -> np.ndarray:
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def column_weights(x0: int, x1: int, width: int) -> np.ndarray:
    """1.0 across the barrel, easing to 0 at the head and tail ends of the bounding box."""
    xs = np.arange(width, dtype=np.float64)
    span = max(1.0, float(x1 - x0))
    guard = max(1.0, span * END_GUARD)

    rising = smoothstep((xs - x0) / guard)
    falling = smoothstep((x1 - xs) / guard)
    w = np.minimum(rising, falling)
    w[(xs < x0) | (xs > x1)] = 0.0
    return w


def deepen(frame: np.ndarray, gain: float) -> np.ndarray:
    """Stretch the torso upward about the cat's ground line, leaving head, tail and feet put."""
    alpha = frame[:, :, 3]
    ys, xs = np.nonzero(alpha > 16)
    if len(ys) == 0:
        return frame

    x0, x1 = int(xs.min()), int(xs.max())
    ground = float(ys.max())

    w = column_weights(x0, x1, frame.shape[1])
    scale = 1.0 + (gain - 1.0) * w              # per column

    # Sample the source at ground - (ground - y)/scale: everything above the ground line moves
    # away from it in proportion to how far up it already was, so the back rises, the barrel
    # thickens, and the paws on the ground do not move at all.
    yy = np.arange(frame.shape[0], dtype=np.float64)[:, None]
    src_y = ground - (ground - yy) / scale[None, :]

    y_lo = np.floor(src_y).astype(np.int64)
    frac = (src_y - y_lo)[:, :, None]
    y_hi = y_lo + 1

    valid = (y_lo >= 0) & (y_hi < frame.shape[0])
    y_lo_c = np.clip(y_lo, 0, frame.shape[0] - 1)
    y_hi_c = np.clip(y_hi, 0, frame.shape[0] - 1)

    cols = np.arange(frame.shape[1])[None, :]
    lo = frame[y_lo_c, cols].astype(np.float64)
    hi = frame[y_hi_c, cols].astype(np.float64)

    out = lo * (1.0 - frac) + hi * frac
    out[~valid] = 0.0
    return np.clip(out, 0, 255).astype(np.uint8)


def grow(frame: np.ndarray, sx: float, sy: float) -> np.ndarray:
    """Scale the whole cat about its ground line and centre, keeping its feet on the baseline."""
    alpha = frame[:, :, 3]
    ys, xs = np.nonzero(alpha > 16)
    if len(ys) == 0:
        return frame

    x0, x1, y0, y1 = int(xs.min()), int(xs.max()), int(ys.min()), int(ys.max())
    cat = Image.fromarray(frame[y0:y1 + 1, x0:x1 + 1])
    w = max(1, int(round(cat.width * sx)))
    h = max(1, int(round(cat.height * sy)))
    cat = cat.resize((w, h), Image.LANCZOS)

    out = Image.new("RGBA", (frame.shape[1], frame.shape[0]), (0, 0, 0, 0))
    left = int(round((x0 + x1) / 2 - w / 2))
    top = y1 - h + 1                                   # feet stay where they were
    out.alpha_composite(cat, (max(0, left), max(0, top)))
    return np.array(out)


def process(sheet: pathlib.Path, out: pathlib.Path, frames: int, gain: tuple[float, float, float]) -> None:
    im = Image.open(sheet).convert("RGBA")
    src = np.array(im)
    dst = np.zeros_like(src)

    base_w, base_h, torso = gain
    for i in range(frames):
        x = i * FRAME_W
        frame = grow(src[:, x:x + FRAME_W], base_w, base_h)
        dst[:, x:x + FRAME_W] = deepen(frame, torso)

    out.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(dst).save(out)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--level", type=int, choices=(2, 3), action="append",
                    help="chonk level (default: both)")
    ap.add_argument("--clips", help="comma-separated clip ids (default: every sprawled clip)")
    ap.add_argument("--preset", default="orange_white",
                    help="coat to derive from; run make_preset.py afterwards for the others")
    args = ap.parse_args()

    manifest = json.loads((ASSETS / "sprites.json").read_text(encoding="utf-8"))
    clips = {c["id"]: c for c in manifest["clips"]}

    wanted = ([c.strip() for c in args.clips.split(",")] if args.clips
              else [c["id"] for c in manifest["clips"] if c.get("sprawl")])
    levels = args.level or [2, 3]

    coat = ASSETS / "cat" / args.preset
    for level in levels:
        for clip_id in wanted:
            clip = clips.get(clip_id)
            if clip is None:
                print(f"!! {clip_id}: not in the manifest")
                continue

            src = coat / clip["sheet"]
            if not src.exists():
                print(f"!! {clip_id}: no sheet at {src}")
                continue

            # Never overwrite drawn art. sleep already has real chonk sheets, and this tool is
            # explicitly the worse option where a human drew one.
            dst = coat / f"chonk{level}" / clip["sheet"]
            if dst.exists():
                print(f"-- {clip_id} chonk{level}: drawn art present, left alone")
                continue

            process(src, dst, clip["frames"], GAIN[level])
            frames = clip["frames"]
            print(f"OK {clip_id} chonk{level} -> {dst.relative_to(ROOT)} "
                  f"({frames} frames, {GAIN[level]})")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
