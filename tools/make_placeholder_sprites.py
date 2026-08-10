#!/usr/bin/env python3
"""
Generates PLACEHOLDER sprite strips so the app can render before the real art drop.

These are crude vector-ish cats, not the provided sheets. They exist so the window,
hit-test and animation pipeline can be verified against real geometry (128x128 frames,
horizontal strips) and then swapped out by dropping the real PNGs over them.

Usage:  python3 tools/make_placeholder_sprites.py
"""
from PIL import Image, ImageDraw
import os, math

W = H = 128
OUT = os.path.join(os.path.dirname(__file__), "..", "assets", "cat", "orange_white")

FUR = (245, 160, 90, 255)
FUR_DARK = (222, 132, 62, 255)
BELLY = (255, 244, 232, 255)
EYE = (40, 40, 48, 255)


def frame(draw, *, crouch=0.0, eye_open=1.0, tail=0.0, lying=False, facing=1):
    """Draw one cat frame. crouch/tail/eye_open drive the per-frame variation."""
    cx = W // 2
    base = H - 14

    if lying:
        body = (cx - 44, base - 26, cx + 40, base)
        draw.ellipse(body, fill=FUR)
        draw.ellipse((cx - 30, base - 16, cx + 18, base - 2), fill=BELLY)
        hx, hy = cx + 30 * facing, base - 30
        draw.ellipse((hx - 20, hy - 18, hx + 20, hy + 18), fill=FUR)
        ear = [(hx - 16, hy - 12), (hx - 22, hy - 30), (hx - 4, hy - 20)]
        draw.polygon(ear, fill=FUR_DARK)
        ear2 = [(hx + 16, hy - 12), (hx + 22, hy - 30), (hx + 4, hy - 20)]
        draw.polygon(ear2, fill=FUR_DARK)
        # sleeping eyes = closed lines
        draw.line((hx - 12, hy, hx - 4, hy), fill=EYE, width=2)
        draw.line((hx + 4, hy, hx + 12, hy), fill=EYE, width=2)
        ty = base - 8 + int(6 * math.sin(tail * math.tau))
        draw.line((cx - 42, base - 12, cx - 60, ty), fill=FUR_DARK, width=7)
        return

    top = base - 52 + crouch
    draw.ellipse((cx - 26, top + 18, cx + 26, base), fill=FUR)
    draw.ellipse((cx - 15, top + 32, cx + 15, base - 4), fill=BELLY)

    hy = top + 6
    draw.ellipse((cx - 22, hy - 18, cx + 22, hy + 20), fill=FUR)
    draw.polygon([(cx - 20, hy - 8), (cx - 26, hy - 30), (cx - 6, hy - 16)], fill=FUR_DARK)
    draw.polygon([(cx + 20, hy - 8), (cx + 26, hy - 30), (cx + 6, hy - 16)], fill=FUR_DARK)

    if eye_open > 0.15:
        r = max(1, int(4 * eye_open))
        draw.ellipse((cx - 12 - r, hy - r, cx - 12 + r, hy + r), fill=EYE)
        draw.ellipse((cx + 12 - r, hy - r, cx + 12 + r, hy + r), fill=EYE)
    else:
        draw.line((cx - 17, hy, cx - 7, hy), fill=EYE, width=2)
        draw.line((cx + 7, hy, cx + 17, hy), fill=EYE, width=2)

    tx = cx - 30 * facing
    ty = base - 20 + int(14 * math.sin(tail * math.tau))
    draw.line((tx, base - 6, tx - 16 * facing, ty), fill=FUR_DARK, width=7)


def strip(name, frames, fn):
    img = Image.new("RGBA", (W * frames, H), (0, 0, 0, 0))
    for i in range(frames):
        cell = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        fn(ImageDraw.Draw(cell), i, frames)
        img.paste(cell, (i * W, 0))
    os.makedirs(OUT, exist_ok=True)
    path = os.path.abspath(os.path.join(OUT, name))
    img.save(path)
    print("wrote", path, f"({frames} frames)")


strip("sleep.png", 6, lambda d, i, n: frame(
    d, lying=True, tail=i / n * 0.5))

strip("idle_blink.png", 8, lambda d, i, n: frame(
    d, eye_open=0.0 if i in (4, 5) else 1.0, tail=i / n))

strip("sit_look.png", 6, lambda d, i, n: frame(
    d, eye_open=1.0, tail=i / n, facing=1 if i < 3 else -1))

strip("loaf.png", 4, lambda d, i, n: frame(
    d, lying=True, tail=i / n * 0.25))

strip("walk_right.png", 8, lambda d, i, n: frame(
    d, crouch=2 * math.sin(i / n * math.tau), tail=i / n, facing=1))

strip("walk_left.png", 8, lambda d, i, n: frame(
    d, crouch=2 * math.sin(i / n * math.tau), tail=i / n, facing=-1))
