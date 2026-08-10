#!/usr/bin/env python3
"""
Extracts the radial-menu icons from the spec sheet.

Separate from extract_sprites.py because the masks are opposites: cat sprites are warm
and the blue channel identifies heading text to EXCLUDE, whereas half these icons (the
food bowl, the water) are themselves blue. Sharing one mask would silently drop them.
"""
import os
import numpy as np
from PIL import Image

SRC = '/tmp/.wsl-screenshot-cli/77c6bf1880ce776099b7a8659da4637e1e4e2d16dfefbe0f29806acd7e44764f.png'
PANEL_X = 750
OUT = os.path.join(os.path.dirname(__file__), '..', 'assets', 'ui')
ART = os.path.join(os.path.dirname(__file__), '..', 'artifacts')
SIZE = 64

# Icon glyphs sit above their captions; this y range is the glyph band only.
BAND_Y = (793, 851)
# Left edge of each icon cell, read off the contact sheet. Fixed cells rather than
# detected runs: the gear is low-contrast and the bowl is blue, so projection-based
# detection finds them unreliably while the layout itself is a clean grid.
CELLS = [
    ("feed", 22, 96),
    ("brush", 104, 170),
    ("pet", 178, 240),
    ("play", 244, 306),
    ("customize", 312, 374),
    ("close", 384, 446),
]


def main():
    im = Image.open(SRC).convert('RGB')
    a = np.asarray(im).astype(np.int16)[:, PANEL_X:, :]
    lum = a.sum(axis=2) / 3
    mask = lum > 40          # permissive: no colour filtering, these icons span the wheel

    os.makedirs(OUT, exist_ok=True)
    os.makedirs(ART, exist_ok=True)

    tiles = []
    for name, x0, x1 in CELLS:
        y0, y1 = BAND_Y
        sub_mask = mask[y0:y1, x0:x1]
        ys = np.flatnonzero(sub_mask.any(axis=1))
        xs = np.flatnonzero(sub_mask.any(axis=0))
        if not len(ys) or not len(xs):
            print(f"{name:12s} NOT FOUND")
            continue

        bx0, bx1 = x0 + xs[0], x0 + xs[-1] + 1
        by0, by1 = y0 + ys[0], y0 + ys[-1] + 1
        rgb = a[by0:by1, bx0:bx1].astype(np.uint8)
        m = mask[by0:by1, bx0:bx1]
        l = rgb.astype(np.int16).sum(axis=2) / 3
        alpha = np.clip((l - 26) * (255 / 40), 0, 255).astype(np.uint8)
        alpha[~m] = 0
        icon = Image.fromarray(np.dstack([rgb, alpha]), 'RGBA')

        canvas = Image.new('RGBA', (SIZE, SIZE), (0, 0, 0, 0))
        s = min((SIZE - 4) / icon.width, (SIZE - 4) / icon.height)
        w, h = max(1, round(icon.width * s)), max(1, round(icon.height * s))
        r = icon.resize((w, h), Image.LANCZOS)
        canvas.paste(r, ((SIZE - w) // 2, (SIZE - h) // 2), r)

        canvas.save(os.path.join(OUT, name + '.png'))
        tiles.append(canvas)
        print(f"{name:12s} {bx1 - bx0}x{by1 - by0} -> {SIZE}x{SIZE}")

    if tiles:
        sheet = Image.new('RGBA', (SIZE * len(tiles), SIZE), (30, 30, 36, 255))
        for i, t in enumerate(tiles):
            sheet.paste(t, (i * SIZE, 0), t)
        sheet.save(os.path.join(ART, 'icons_contact.png'))
        print("contact ->", os.path.abspath(os.path.join(ART, 'icons_contact.png')))


main()
