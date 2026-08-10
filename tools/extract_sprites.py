#!/usr/bin/env python3
"""
Extracts cat sprites from the spec sheet screenshot into per-clip strips.

The sheet is a flattened screenshot of a spec document, NOT the original transparent
PNG drop, so this is a best-effort recovery:
  * background is keyed out by luminance; there is no real alpha to recover
  * source sprites are ~50-90px and get upscaled into the 128px frame box
  * the right-hand column of the sheet is cropped, so those clips have fewer frames

Three things this has to get right or the cat looks wrong on screen:
  1. ONE global scale for every frame of every clip. Fitting each frame to its own box
     independently makes the cat visibly grow and shrink as it animates.
  2. Reject frames whose sprite touches the run boundary — that means the splitter cut
     through a cat, and a sliced cat is worse than a missing frame.
  3. Drop small speckles before measuring, or leftover heading text lands in a frame.

Usage:  python3 tools/extract_sprites.py [--debug]
"""
import json
import os
import sys
import numpy as np
from PIL import Image

SRC = '/tmp/.wsl-screenshot-cli/77c6bf1880ce776099b7a8659da4637e1e4e2d16dfefbe0f29806acd7e44764f.png'
PANEL_X = 750
OUT = os.path.join(os.path.dirname(__file__), '..', 'assets', 'cat', 'orange_white')
ART = os.path.join(os.path.dirname(__file__), '..', 'artifacts')
# Frames are wider than they are tall: a walking or stretching cat is long, and a
# square box would force the whole set to scale down to fit the longest pose.
FRAME_W = 160
FRAME_H = 128
MARGIN = 6
# Source-pixel width above which a "frame" is really a cat plus a prop (the meow frame
# bakes in a speech bubble). Bubbles are a separate overlay asset, so drop those.
MAX_SRC_W = 110

# (clip, y0, y1, x0, x1, target_frames)
BANDS = [
    ("idle_blink",          40, 108,   0, 600, 8),
    ("walk_right",         110, 178,   0, 600, 8),
    ("walk_left",          180, 248,   0, 600, 8),
    ("sleep",              250, 316,   0, 600, 6),
    ("groom",              320, 378,   0, 600, 6),
    ("play_pounce",        382, 450,   0, 600, 8),
    ("eat",                455, 522,   0, 600, 5),
    ("meow_attention",     530, 598,   0, 440, 5),
    ("loaf",               600, 668,   0, 380, 3),
    ("sit_look",            40, 108, 600, 786, 2),
    ("stretch_yawn",       180, 248, 600, 786, 1),
    ("scratch",            455, 522, 600, 786, 3),
    ("happy_hearts",       530, 598, 440, 600, 2),
    ("cursor_interaction", 600, 668, 600, 786, 2),
]


def load():
    im = Image.open(SRC).convert('RGB')
    a = np.asarray(im).astype(np.int16)[:, PANEL_X:, :]
    R, G, B = a[..., 0], a[..., 1], a[..., 2]
    lum = (R + G + B) / 3
    mask = (lum > 45) & ~(B > R + 12)
    return a, despeckle(mask, min_area=45)


def despeckle(mask, min_area):
    """Drop tiny components (leftover heading glyphs, JPEG-ish noise)."""
    h, w = mask.shape
    seen = np.zeros_like(mask, dtype=bool)
    out = np.zeros_like(mask)
    for y in range(h):
        xs = np.flatnonzero(mask[y] & ~seen[y])
        for x0 in xs:
            if seen[y, x0]:
                continue
            stack, cells = [(y, x0)], []
            seen[y, x0] = True
            while stack:
                cy, cx = stack.pop()
                cells.append((cy, cx))
                for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    ny, nx = cy + dy, cx + dx
                    if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        stack.append((ny, nx))
            if len(cells) >= min_area:
                for cy, cx in cells:
                    out[cy, cx] = True
    return out


def runs(cols, min_w=10, bridge=3):
    out, in_run, gap, xs = [], False, 0, 0
    for x, v in enumerate(cols):
        if v > 0:
            if not in_run:
                xs, in_run = x, True
            gap = 0
        elif in_run:
            gap += 1
            if gap > bridge:
                in_run = False
                if x - gap - xs >= min_w:
                    out.append((xs, x - gap))
    if in_run and len(cols) - xs >= min_w:
        out.append((xs, len(cols)))
    return out


def coerce(rs, cols, target):
    """Split widest / drop narrowest until the run count matches the labelled frame count."""
    rs = list(rs)
    for _ in range(40):
        if len(rs) >= target:
            break
        i = max(range(len(rs)), key=lambda k: rs[k][1] - rs[k][0])
        s, e = rs[i]
        if e - s < 24:
            break
        lo, hi = s + int((e - s) * 0.30), s + int((e - s) * 0.70)
        if hi <= lo:
            break
        cut = min(range(lo, hi), key=lambda x: cols[x])
        rs[i:i + 1] = [(s, cut), (cut, e)]
    while len(rs) > target:
        i = min(range(len(rs)), key=lambda k: rs[k][1] - rs[k][0])
        rs.pop(i)
    return rs


def tight(mask, x0, x1, y0, y1):
    sub = mask[y0:y1, x0:x1]
    ys = np.flatnonzero(sub.any(axis=1))
    xs = np.flatnonzero(sub.any(axis=0))
    if not len(ys) or not len(xs):
        return None
    return (x0 + xs[0], y0 + ys[0], x0 + xs[-1] + 1, y0 + ys[-1] + 1)


def is_cut(mask, box, thresh=0.45):
    """True if the sprite runs into the left/right edge of its box — i.e. it got sliced."""
    x0, y0, x1, y1 = box
    sub = mask[y0:y1, x0:x1]
    if sub.shape[1] < 3:
        return True
    height = sub.shape[0]
    return (sub[:, 0].sum() / height > thresh) or (sub[:, -1].sum() / height > thresh)


def cutout(rgb, mask, box):
    x0, y0, x1, y1 = box
    sub = rgb[y0:y1, x0:x1].astype(np.uint8)
    m = mask[y0:y1, x0:x1]
    lum = sub.astype(np.int16).sum(axis=2) / 3
    alpha = np.clip((lum - 30) * (255 / 40), 0, 255).astype(np.uint8)
    alpha[~m] = 0
    return Image.fromarray(np.dstack([sub, alpha]), 'RGBA')


def place(img, scale):
    """Apply the shared scale, centre horizontally, stand on the frame floor."""
    canvas = Image.new('RGBA', (FRAME_W, FRAME_H), (0, 0, 0, 0))
    w = max(1, round(img.width * scale))
    h = max(1, round(img.height * scale))
    if w > FRAME_W - 2:
        w, h = FRAME_W - 2, max(1, round(h * (FRAME_W - 2) / w))
    if h > FRAME_H - 2:
        h, w = FRAME_H - 2, max(1, round(w * (FRAME_H - 2) / h))
    r = img.resize((w, h), Image.LANCZOS)
    canvas.paste(r, ((FRAME_W - w) // 2, FRAME_H - MARGIN - h), r)
    return canvas


def main():
    rgb, mask = load()
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(ART, exist_ok=True)

    # Pass 1: locate every usable frame box.
    plan, cut_drops, size_drops = {}, {}, {}
    for clip, y0, y1, x0, x1, target in BANDS:
        band = mask[y0:y1, x0:x1]
        cols = band.sum(axis=0)
        boxes, drop = [], 0
        for s, e in coerce(runs(cols), cols, target):
            box = tight(mask, x0 + s, x0 + e, y0, y1)
            if box is None:
                continue
            if is_cut(mask, box):
                drop += 1
                continue
            if box[2] - box[0] > MAX_SRC_W:
                drop += 1
                continue
            boxes.append(box)
        # Drop size outliers. The spec sheet draws one sleep pose at roughly half the
        # scale of its neighbours; kept, the cat visibly shrinks mid-animation. Every
        # legitimate frame measures 89-117% of its clip's median height, so 70% is a
        # wide margin that catches only genuine anomalies.
        if len(boxes) >= 3:
            heights = sorted(b[3] - b[1] for b in boxes)
            median = heights[len(heights) // 2]
            keep = [b for b in boxes if (b[3] - b[1]) >= median * 0.70]
            size_drops[clip] = len(boxes) - len(keep)
            boxes = keep

        plan[clip] = boxes
        cut_drops[clip] = drop

    # One scale for every frame everywhere: the biggest sprite defines it, so relative
    # sizes across clips (a stretched cat IS longer than a loafing one) are preserved.
    # Scale from HEIGHT only. Cat heights cluster tightly (24-61px) while widths vary
    # 3x with pose, so a width-driven scale would shrink every sitting cat to suit one
    # stretched one.
    tall = max(max(b[3] - b[1] for b in bs) for bs in plan.values() if bs)
    scale = (FRAME_H - MARGIN * 2) / tall

    contact, produced = [], {}
    for clip, boxes in plan.items():
        if not boxes:
            print(f"{clip:20s} SKIP (no usable frames)")
            continue
        frames = [place(cutout(rgb, mask, b), scale) for b in boxes]
        strip = Image.new('RGBA', (FRAME_W * len(frames), FRAME_H), (0, 0, 0, 0))
        for i, f in enumerate(frames):
            strip.paste(f, (i * FRAME_W, 0), f)
        strip.save(os.path.join(OUT, clip + '.png'))
        produced[clip] = len(frames)
        contact.append(strip)
        notes = []
        if cut_drops.get(clip):
            notes.append(f"{cut_drops[clip]} sliced")
        if size_drops.get(clip):
            notes.append(f"{size_drops[clip]} size-outlier")
        note = f"  (dropped {', '.join(notes)})" if notes else ""
        print(f"{clip:20s} {len(frames)} frames{note}")

    with open(os.path.join(ART, 'extracted.json'), 'w') as fh:
        json.dump(produced, fh, indent=2)

    if contact:
        w = max(s.width for s in contact)
        sheet = Image.new('RGBA', (w, FRAME_H * len(contact)), (24, 24, 28, 255))
        for i, s in enumerate(contact):
            sheet.paste(s, (0, i * FRAME_H), s)
        sheet.save(os.path.join(ART, 'contact.png'))
        print("scale=%.3f  contact -> %s" % (scale, os.path.abspath(os.path.join(ART, 'contact.png'))))


main()
