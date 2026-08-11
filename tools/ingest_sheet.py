#!/usr/bin/env python3
"""Turns a generated animation sheet into a spec-conforming 160x128 horizontal strip.

Image models do not honour a pixel grid. What comes back is a big canvas (1536x1024 here)
with the frames laid out roughly in a row, at arbitrary scale, unevenly spaced, and drifting
off the baseline. Re-drawing that by hand is the only alternative to this script.

What it does:
  1. Segments frames as connected components (one cat body = one component), splitting any
     blob that is a multiple of the median width because two cats were drawn touching.
  2. Scales every clip by ONE shared factor so the cat is the same size in all of them.
  3. Registers vertically against the clip's own ground line — the MEDIAN bbox bottom — so
     grounded frames land on the baseline while genuinely airborne frames keep their lift.
  4. Centres each frame horizontally: the cat animates in place and the app moves the window.

    python3 tools/ingest_sheet.py --report                 # measure, change nothing
    python3 tools/ingest_sheet.py --write                  # write the strips
"""

import argparse
import json
import pathlib
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = pathlib.Path("/mnt/c/Users/Windows Pc/Downloads")
DST = ROOT / "assets" / "cat" / "orange_white"

FRAME_W, FRAME_H = 160, 128
BASELINE_Y = 118          # where the feet belong
TARGET_MEDIAN_H = 96      # median cat height after scaling, in frame pixels
MAX_CAT_W = 150           # keep clear of the frame edges

ALPHA_MIN = 16            # below this a pixel is background
MIN_RUN_PX = 12           # narrower than this is speckle, not a cat
MERGE_GAP_PX = 10         # a detached tail tip should not become its own frame

# name -> expected frame count, from the brief
# Only sheets present in SRC are processed, so both briefs can share this list.
CLIPS = {
    "run_right": 12,
    "run_left": 12,
    "scratch_icons": 14,
    "zoomies": 16,
    "jump": 10,
    "sleep": 12,
    "paw_screen": 14,
    "watch_bug": 18,
    # second brief — every clip ClipMap can still reach that is on the original art
    "idle_blink": 14,
    "groom": 14,
    "eat": 12,
    "happy_hearts": 12,
    "loaf": 12,
    "stretch_yawn": 14,
    "cursor_interaction": 14,
}


def segment(alpha: np.ndarray, expected: int):
    """
    Frame x-ranges. Connected components, not column projection: the generated frames often
    touch or overlap horizontally, and a projection then merges a whole row of cats into one
    run. Components are then grouped into `expected` clusters by gap size, which reattaches
    the bits of a cat that are separate blobs (a tail tip, a lifted paw).
    """
    labels, n = ndimage.label(alpha > ALPHA_MIN)
    if n == 0:
        return []

    objects = ndimage.find_objects(labels)
    parts = []
    for sl in objects:
        ys, xs = sl
        if (xs.stop - xs.start) < 8 or (ys.stop - ys.start) < 8:
            continue                      # speckle
        parts.append([xs.start, ys.start, xs.stop, ys.stop])
    if not parts:
        return []

    parts.sort(key=lambda b: (b[0] + b[2]) / 2)

    # One component IS one frame: the cat is drawn as a single connected body, and the gaps
    # between frames are real (3-24px here) even where the spacing is uneven. Do NOT merge on
    # small gaps — that was the first thing tried and it fused genuine frames.
    #
    # The one real defect is the opposite: on the tightly packed sheets neighbouring cats
    # touch and label as a single blob. Anything much wider than the median is that, and gets
    # split into equal parts.
    widths = sorted(p[2] - p[0] for p in parts)
    w_med = widths[len(widths) // 2]

    density = (alpha > ALPHA_MIN).sum(axis=0)

    out = []
    for x0, _, x1, _ in parts:
        k = max(1, round((x1 - x0) / w_med))
        if k == 1:
            out.append((x0, x1))
            continue

        # Cut where the two cats actually touch, not at the arithmetic boundary. An equal
        # split slices straight through a body; the real seam is the column with the least
        # ink, searched in a window around where the boundary ought to be.
        step = (x1 - x0) / k
        cuts = [x0]
        for i in range(1, k):
            guess = x0 + i * step
            lo = int(max(x0 + 4, guess - step * 0.3))
            hi = int(min(x1 - 4, guess + step * 0.3))
            cuts.append(lo + int(np.argmin(density[lo:hi])) if hi > lo else int(guess))
        cuts.append(x1)
        out.extend((cuts[i], cuts[i + 1]) for i in range(k))

    return out


def drop_slivers(frame: Image.Image, keep_ratio: float = 0.12) -> Image.Image:
    """
    Erases everything but the cat. Cutting two touching cats apart leaves a thin strip of the
    neighbour against the frame edge; it is tiny next to the body, so keep only blobs above a
    fraction of the largest area (the tail and a lifted paw stay, the sliver goes).
    """
    a = np.array(frame)
    labels, n = ndimage.label(a[..., 3] > ALPHA_MIN)
    if n <= 1:
        return frame

    areas = ndimage.sum(np.ones_like(labels), labels, range(1, n + 1))
    biggest = areas.max()
    keep = {i + 1 for i, area in enumerate(areas) if area >= biggest * keep_ratio}

    mask = np.isin(labels, list(keep))
    a[..., 3] = np.where(mask, a[..., 3], 0)
    return Image.fromarray(a)


def boxes_for(path: pathlib.Path, expected: int):
    """Per-frame bounding boxes in source pixels."""
    im = Image.open(path).convert("RGBA")
    alpha = np.array(im)[..., 3]

    out = []
    for x0, x1 in segment(alpha, expected):
        strip = alpha[:, x0:x1]
        rows = np.where((strip > ALPHA_MIN).any(axis=1))[0]
        if len(rows) == 0:
            continue
        out.append((x0, int(rows[0]), x1, int(rows[-1]) + 1))
    return im, out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true", help="write strips into assets/")
    ap.add_argument("--report", action="store_true", help="measure only")
    args = ap.parse_args()

    measured = {}
    for name in CLIPS:
        path = SRC / f"{name}.png"
        if not path.exists():
            continue        # not in this drop; leave whatever is already installed alone
        im, boxes = boxes_for(path, CLIPS[name])
        measured[name] = (im, boxes)

    if not measured:
        return 1

    # One scale for every clip, from the median cat height across all frames of all clips.
    # Per-clip scaling would blow up the curled sleep pose and shrink the stretched jump.
    all_h = [b[3] - b[1] for _, boxes in measured.values() for b in boxes]
    median_h = float(np.median(all_h))
    scale = TARGET_MEDIAN_H / median_h

    widest = max((b[2] - b[0]) for _, boxes in measured.values() for b in boxes)
    if widest * scale > MAX_CAT_W:
        scale = MAX_CAT_W / widest

    print(f"frames total={len(all_h)}  median source height={median_h:.0f}px  scale={scale:.3f}\n")

    manifest_rows = []
    for name, expected in CLIPS.items():
        if name not in measured:
            continue
        im, boxes = measured[name]
        got = len(boxes)
        flag = "OK " if got == expected else "!! "
        heights = [b[3] - b[1] for b in boxes]
        print(f"{flag}{name:<15} frames {got:>2}/{expected:<3} "
              f"h {min(heights)}-{max(heights)}px -> {min(heights)*scale:.0f}-{max(heights)*scale:.0f}px")

        if not args.write:
            continue

        # The model drew some clips noticeably bigger than others, so the cat would visibly
        # resize when it switched from sitting to zoomies. Pull each clip's median toward the
        # global median, but only partway (exponent < 1): a full correction would stretch the
        # curled sleeping cat to the height of a sitting one and flatten the poses that are
        # SUPPOSED to differ.
        clip_median = float(np.median(heights))
        clip_scale = scale * (median_h / clip_median) ** 0.6

        # The clip's own ground line. Median, not min: one airborne frame must not drag the
        # whole clip down, and one crouch must not lift it.
        ground = float(np.median([b[3] for b in boxes]))

        sheet = Image.new("RGBA", (FRAME_W * got, FRAME_H), (0, 0, 0, 0))
        for i, (x0, y0, x1, y1) in enumerate(boxes):
            cat = drop_slivers(im.crop((x0, y0, x1, y1)))
            w = max(1, round((x1 - x0) * clip_scale))
            h = max(1, round((y1 - y0) * clip_scale))

            # A stretched leap or a full-height scratch pose can exceed the box. Shrink that
            # FRAME to fit rather than scaling every clip down to the worst case, which would
            # leave the cat small in all the poses it actually spends its time in.
            if h > FRAME_H - 4 or w > MAX_CAT_W:
                fit = min((FRAME_H - 4) / h, MAX_CAT_W / w)
                w, h = max(1, round(w * fit)), max(1, round(h * fit))

            cat = cat.resize((w, h), Image.LANCZOS)

            lift = round((y1 - ground) * clip_scale)     # negative = airborne
            top = BASELINE_Y - h + lift
            top = max(0, min(FRAME_H - h, top))

            sheet.alpha_composite(cat, (i * FRAME_W + (FRAME_W - w) // 2, top))

        sheet.save(DST / f"{name}.png")
        manifest_rows.append({"id": name, "sheet": f"{name}.png", "frames": got})

    if args.write:
        print("\nwrote", len(manifest_rows), "strips to", DST.relative_to(ROOT))
        print(json.dumps(manifest_rows, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
