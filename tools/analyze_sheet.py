#!/usr/bin/env python3
"""Probe the spec sheet: find sprite row bands and per-row sprite columns."""
import sys
import numpy as np
from PIL import Image

SRC = sys.argv[1] if len(sys.argv) > 1 else \
    '/tmp/.wsl-screenshot-cli/77c6bf1880ce776099b7a8659da4637e1e4e2d16dfefbe0f29806acd7e44764f.png'
PANEL_X = 750

im = Image.open(SRC).convert('RGB')
a = np.asarray(im).astype(np.int16)
a = a[:, PANEL_X:, :]
R, G, B = a[..., 0], a[..., 1], a[..., 2]

lum = (R + G + B) / 3
# Sprites are warm (orange fur) or near-white (belly/bubbles). Section labels are blue.
blue_text = (B > R + 12)
mask = (lum > 45) & ~blue_text

rows = mask.sum(axis=1)
bands = []
in_band = False
for y, v in enumerate(rows):
    if v > 3 and not in_band:
        start, in_band = y, True
    elif v <= 3 and in_band:
        in_band = False
        if y - start >= 12:
            bands.append((start, y))
if in_band:
    bands.append((start, len(rows)))

print(f"panel {a.shape[1]}x{a.shape[0]}  bands={len(bands)}")
for i, (y0, y1) in enumerate(bands):
    sub = mask[y0:y1]
    cols = sub.sum(axis=0)
    runs, in_run = [], False
    for x, v in enumerate(cols):
        if v > 0 and not in_run:
            xs, in_run = x, True
        elif v == 0 and in_run:
            in_run = False
            if x - xs >= 8:
                runs.append((xs, x))
    if in_run:
        runs.append((xs, len(cols)))
    widths = [b - a_ for a_, b in runs]
    print(f"band {i:2d}: y={y0:4d}-{y1:4d} h={y1-y0:3d} sprites={len(runs):2d} "
          f"w={widths[:10]}{'...' if len(widths) > 10 else ''}")
