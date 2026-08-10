#!/usr/bin/env python3
"""Per-row frame detection by vertical projection inside each row band."""
import numpy as np
from PIL import Image

SRC = '/tmp/.wsl-screenshot-cli/77c6bf1880ce776099b7a8659da4637e1e4e2d16dfefbe0f29806acd7e44764f.png'
PANEL_X = 750
SPLIT_X = 600          # right-hand column of the sheet starts about here

# y-bands read off the debug contact sheet (panel coords)
BANDS = [
    ("idle_blink",        40, 108),
    ("walk_right",       110, 178),
    ("walk_left",        180, 248),
    ("sleep",            250, 316),
    ("groom",            320, 378),
    ("play_pounce",      382, 450),
    ("eat",              455, 522),
    ("meow_attention",   530, 598),
    ("loaf",             600, 668),
]

im = Image.open(SRC).convert('RGB')
a = np.asarray(im).astype(np.int16)[:, PANEL_X:, :]
R, G, B = a[..., 0], a[..., 1], a[..., 2]
lum = (R + G + B) / 3
mask = (lum > 45) & ~(B > R + 12)


def runs(cols, min_w=10, bridge=3):
    """Column runs, bridging gaps of <= `bridge` px so a tail does not split a cat."""
    out, in_run, gap = [], False, 0
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


for name, y0, y1 in BANDS:
    band = mask[y0:y1]
    for side, x0, x1 in (("L", 0, SPLIT_X), ("R", SPLIT_X, band.shape[1])):
        sub = band[:, x0:x1]
        r = runs(sub.sum(axis=0))
        if not r:
            continue
        widths = [b - a_ for a_, b in r]
        print(f"{name:16s} {side} n={len(r):2d} widths={widths} starts={[x0 + s for s, _ in r]}")
