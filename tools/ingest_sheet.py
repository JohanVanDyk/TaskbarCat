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
# Drops land either loose in Downloads or in a subfolder of it; search both.
SRC_ROOTS = [
    pathlib.Path("/mnt/c/Users/Windows Pc/Downloads/taskbar_cat_new_clips"),
    pathlib.Path("/mnt/c/Users/Windows Pc/Downloads"),
]


def find_sheet(name: str):
    for root in SRC_ROOTS:
        p = root / f"{name}.png"
        if p.exists():
            return p
    return None
DST = ROOT / "assets" / "cat" / "orange_white"

FRAME_W, FRAME_H = 160, 128
BASELINE_Y = 118          # where the feet belong
TARGET_MEDIAN_H = 96      # median cat height after scaling, in frame pixels
TARGET_MEDIAN_W = 106     # the same, by width, for --sprawl. Measured off the installed
                          # sprawled clips (run/walk/zoomies/pounce/play_yarn median ~106px),
                          # so fat art lands at the length the thin art already has.
                          # Only a fallback now: a --sprawl chonk ingest sizes each clip
                          # against its own thin sheet, see SPRAWL_FAT_W.

SPRAWL_FAT_W = 1.15       # how much wider than its own thin sheet a fat sprawled clip lands.
                          # A fat sprawled cat is barely longer - the weight goes into the
                          # barrel, not the length - so this stays near 1.
MAX_CAT_W = 150           # keep clear of the frame edges

ALPHA_MIN = 16            # below this a pixel is background
MIN_RUN_PX = 12           # narrower than this is speckle, not a cat
MERGE_GAP_PX = 10         # a detached tail tip should not become its own frame

# name -> expected frame count, from the brief
# Only sheets present in SRC are processed, so both briefs can share this list.
CLIPS = {
    "run_right": 11,
    "run_left": 10,
    "scratch_icons": 14,
    "zoomies": 13,
    "jump": 9,
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
    # fright brief — the drop when an auto-hide taskbar vanishes under a sleeping cat
    "fright": 16,
    # toy mode
    "reach_up": 14,
    "play_yarn": 14,
    "confused": 14,
    # sixth brief — the sprawled movement clips
    "walk_left": 4,
    "walk_right": 6,
    "play_pounce": 4,
}

# The toys are not cats: 64x64, and they float at the pointer rather than standing on a
# baseline, so they are centred vertically instead of being registered to the ground.
TOYS = {
    "toy_yarn": 8,
    "toy_laser": 6,
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

    # Separate the cats from their loose bits. happy_hearts floats hearts above the cat and
    # every one of them labels as its own component — 82 components for a 12 frame clip. A
    # component only starts a frame if it is cat-sized; everything smaller is scenery and gets
    # attached to the nearest cat instead (this also catches a detached tail tip).
    # Threshold off the BIGGEST components, not the median: with 70 hearts and 12 cats the
    # median is a heart, and then every heart qualifies as a cat. The typical cat is the
    # median of the largest `expected` components.
    tallest = sorted((p[3] - p[1] for p in parts), reverse=True)[:max(1, expected)]
    h_cat = tallest[len(tallest) // 2]
    primaries = [p for p in parts if (p[3] - p[1]) >= h_cat * 0.55]
    extras = [p for p in parts if (p[3] - p[1]) < h_cat * 0.55]

    cat_span = {id(p): (p[1], p[3]) for p in primaries}

    if primaries and extras:
        for e in extras:
            ecx = (e[0] + e[2]) / 2
            near = min(primaries, key=lambda p: abs((p[0] + p[2]) / 2 - ecx))
            near[0], near[1] = min(near[0], e[0]), min(near[1], e[1])
            near[2], near[3] = max(near[2], e[2]), max(near[3], e[3])
        parts = primaries

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
    for p in parts:
        x0, x1 = p[0], p[2]
        # The cat's OWN vertical extent, before its hearts were folded in. Scale and baseline
        # come from the cat; if the hearts counted, a clip that floats them overhead would
        # shrink the cat to fit the pair into 128px and it would not match the other clips.
        cy0, cy1 = cat_span.get(id(p), (p[1], p[3]))
        k = max(1, round((x1 - x0) / w_med))
        if k == 1:
            out.append((x0, x1, cy0, cy1))
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
        out.extend((cuts[i], cuts[i + 1], cy0, cy1) for i in range(k))

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

    # Small is not enough to condemn a blob: happy_hearts floats deliberate little hearts over
    # the cat and an area test alone deleted every one of them. A neighbour's sliver is small
    # AND runs off the side of the crop, which is what actually distinguishes it.
    width = labels.shape[1]
    keep = set()
    for i, area in enumerate(areas, start=1):
        if area >= biggest * keep_ratio:
            keep.add(i)
            continue
        cols = np.where((labels == i).any(axis=0))[0]
        if len(cols) and cols[0] > 0 and cols[-1] < width - 1:
            keep.add(i)          # free-floating and fully inside: part of the art

    mask = np.isin(labels, list(keep))
    a[..., 3] = np.where(mask, a[..., 3], 0)
    return Image.fromarray(a)


def pick_row(alpha: np.ndarray, expected: int) -> tuple[int, int]:
    """
    Vertical band holding the animation. Some sheets come back with the clip drawn TWICE, one
    row above the other; segmenting the whole canvas then fuses the two rows into tall boxes.
    Bands are split on empty rows, and the one whose frame count is closest to what was asked
    for wins (earliest on a tie, i.e. the first take).
    """
    rows = (alpha > ALPHA_MIN).any(axis=1)
    bands, start = [], None
    for y, on in enumerate(rows):
        if on and start is None:
            start = y
        elif not on and start is not None:
            if y - start >= 24:
                bands.append((start, y))
            start = None
    if start is not None and len(rows) - start >= 24:
        bands.append((start, len(rows)))

    if len(bands) <= 1:
        return (0, alpha.shape[0])

    scored = []
    for y0, y1 in bands:
        count = len(segment(alpha[y0:y1, :], expected))
        scored.append((abs(count - expected), y0, y1))
    scored.sort()
    return (scored[0][1], scored[0][2])


def boxes_for(path: pathlib.Path, expected: int):
    """Per-frame bounding boxes in source pixels."""
    im = Image.open(path).convert("RGBA")
    alpha = np.array(im)[..., 3]

    y0, y1 = pick_row(alpha, expected)
    if (y0, y1) != (0, alpha.shape[0]):
        im = im.crop((0, y0, im.width, y1))
        alpha = alpha[y0:y1, :]

    out = []
    for x0, x1, cy0, cy1 in segment(alpha, expected):
        strip = alpha[:, x0:x1]
        rows = np.where((strip > ALPHA_MIN).any(axis=1))[0]
        if len(rows) == 0:
            continue
        out.append((x0, int(rows[0]), x1, int(rows[-1]) + 1, int(cy0), int(cy1)))
    return im, out


def ingest_toys(args) -> int:
    """
    The toys, on their own 64x64 grid. Separate path because every registration rule for the
    cat is wrong here: a toy has no feet, is never airborne, and hangs off the mouse pointer,
    so it is centred in both axes and scaled to fill the frame.
    """
    out_dir = ROOT / "assets" / "toys"
    out_dir.mkdir(parents=True, exist_ok=True)

    size = 64
    target = 56          # leave a little air so the glow/tail is not clipped

    for name, expected in TOYS.items():
        path = find_sheet(name)
        if path is None:
            print(f"{name:<12} not in this drop")
            continue

        im, boxes = boxes_for(path, expected)
        got = len(boxes)
        flag = "OK " if got == expected else "!! "

        widest = max(b[2] - b[0] for b in boxes)
        tallest = max(b[5] - b[4] for b in boxes)
        scale = target / max(widest, tallest)

        print(f"{flag}{name:<12} frames {got:>2}/{expected:<3} "
              f"source {widest}x{tallest}px -> {round(widest*scale)}x{round(tallest*scale)}px")

        if not args.write:
            continue

        sheet = Image.new("RGBA", (size * got, size), (0, 0, 0, 0))
        for i, (x0, y0, x1, y1, _, _) in enumerate(boxes):
            toy = drop_slivers(im.crop((x0, y0, x1, y1)))
            w = max(1, round((x1 - x0) * scale))
            h = max(1, round((y1 - y0) * scale))
            toy = toy.resize((w, h), Image.LANCZOS)
            sheet.alpha_composite(toy, (i * size + (size - w) // 2, (size - h) // 2))

        sheet.save(out_dir / f"{name}.png")

    if args.write:
        print("\nwrote toys to", out_dir.relative_to(ROOT))
    return 0


def frame_widths(sheet: pathlib.Path) -> list[int]:
    """Opaque width of each frame of an INSTALLED strip, i.e. one already on the 160x128 grid."""
    im = Image.open(sheet).convert("RGBA")
    a = np.array(im)[:, :, 3]
    out = []
    for i in range(a.shape[1] // FRAME_W):
        cols = np.nonzero((a[:, i * FRAME_W:(i + 1) * FRAME_W] > ALPHA_MIN).any(axis=0))[0]
        if len(cols):
            out.append(int(cols.max() - cols.min()))
    return out or [FRAME_W]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true", help="write strips into assets/")
    ap.add_argument("--report", action="store_true", help="measure only")
    ap.add_argument("--src", help="extra directory to search for sheets")
    ap.add_argument("--chonk", type=int, default=0,
                    help="chonk level: reads <clip>_chonkN.png and writes into assets/cat/<preset>/chonkN/")
    ap.add_argument("--toys", action="store_true",
                    help="ingest the 64x64 toy sprites into assets/toys/ instead of cat clips")
    ap.add_argument("--sprawl", action="store_true",
                    help="normalise scale by WIDTH, not height. Required for chonk art of the "
                         "sprawled clips (runs, walks, zoomies, pounce, play_yarn): those are "
                         "drawn fat by deepening the belly, so height-normalising undoes it")
    args = ap.parse_args()

    if args.src:
        SRC_ROOTS.insert(0, pathlib.Path(args.src))

    suffix = f"_chonk{args.chonk}" if args.chonk else ""
    out_dir = DST / f"chonk{args.chonk}" if args.chonk else DST
    out_dir.mkdir(parents=True, exist_ok=True)

    if args.toys:
        return ingest_toys(args)

    measured = {}
    for name in CLIPS:
        path = find_sheet(name + suffix)
        if path is None:
            continue        # not in this drop; leave whatever is already installed alone
        im, boxes = boxes_for(path, CLIPS[name])
        measured[name] = (im, boxes)

    if not measured:
        return 1

    # One scale for every clip, from the median cat height across all frames of all clips.
    # Per-clip scaling would blow up the curled sleep pose and shrink the stretched jump.
    #
    # --sprawl normalises by WIDTH instead, and it is required for chonk art of the sprawled
    # clips. Those poses are drawn fat by deepening the belly, so the fat cat is taller and
    # barely wider than the thin one; normalising its HEIGHT back to the usual median scales the
    # belly straight back out and returns the thin cat. Width is the stable axis there, because
    # a fat sprawled cat is not longer. Do not use it for the seated clips, where the reverse
    # holds and height is what stays put.
    all_h = [b[5] - b[4] for _, boxes in measured.values() for b in boxes]
    median_h = float(np.median(all_h))
    scale = TARGET_MEDIAN_H / median_h

    all_w = [b[2] - b[0] for _, boxes in measured.values() for b in boxes]
    median_w = float(np.median(all_w))

    if args.sprawl:
        scale = TARGET_MEDIAN_W / median_w
        print(f"--sprawl: normalising by width (median {median_w:.0f}px -> {TARGET_MEDIAN_W}px)")

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
        heights = [b[5] - b[4] for b in boxes]
        print(f"{flag}{name:<15} frames {got:>2}/{expected:<3} "
              f"h {min(heights)}-{max(heights)}px -> {min(heights)*scale:.0f}-{max(heights)*scale:.0f}px")

        if not args.write:
            continue

        # The model drew some clips noticeably bigger than others, so the cat would visibly
        # resize when it switched from sitting to zoomies. Pull each clip's median toward the
        # global median, but only partway (exponent < 1): a full correction would stretch the
        # curled sleeping cat to the height of a sitting one and flatten the poses that are
        # SUPPOSED to differ.
        if args.sprawl:
            widths = [b[2] - b[0] for b in boxes]
            clip_median = float(np.median(widths))

            # Size the fat cat against the SAME clip's installed thin sheet, not against a
            # global median. The thin sheets are not drawn at a common size - walk_right is
            # 125px wide where zoomies is 90 - so one shared target fattened zoomies by 1.18x,
            # run_right by 1.03x and walk_right by 0.85x, i.e. the fat walking cat came out
            # SMALLER than the thin one. Per-clip is the only thing that holds the weight
            # consistent across clips, which is the whole point of the exercise.
            thin = DST / f"{name}.png"
            if thin.exists():
                thin_w = np.median(frame_widths(thin))
                clip_scale = (thin_w * SPRAWL_FAT_W) / clip_median
            else:
                clip_scale = scale * (median_w / clip_median) ** 0.6
        else:
            clip_median = float(np.median(heights))
            clip_scale = scale * (median_h / clip_median) ** 0.6

        # The clip's own ground line. Median, not min: one airborne frame must not drag the
        # whole clip down, and one crouch must not lift it.
        ground = float(np.median([b[5] for b in boxes]))

        sheet = Image.new("RGBA", (FRAME_W * got, FRAME_H), (0, 0, 0, 0))
        for i, (x0, y0, x1, y1, cy0, cy1) in enumerate(boxes):
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

            lift = round((cy1 - ground) * clip_scale)          # negative = airborne
            # Place the CAT's feet on the baseline; whatever sits above it in the crop
            # (floating hearts) rides along at its own offset.
            top = BASELINE_Y - round((cy1 - y0) * clip_scale) + lift
            top = max(0, min(FRAME_H - h, top))

            sheet.alpha_composite(cat, (i * FRAME_W + (FRAME_W - w) // 2, top))

        sheet.save(out_dir / f"{name}.png")
        manifest_rows.append({"id": name, "sheet": f"{name}.png", "frames": got})

    if args.write:
        print("\nwrote", len(manifest_rows), "strips to", out_dir.relative_to(ROOT))
        print(json.dumps(manifest_rows, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
