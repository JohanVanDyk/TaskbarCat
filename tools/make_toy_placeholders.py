#!/usr/bin/env python3
"""Draws stand-in toy sprites so toy mode can be built and tested before the art lands.

These are deliberately crude — flat shapes, no shading — so nobody mistakes them for the real
thing. Replace assets/toys/*.png with the generated sheets from taskbarcat_toys.zip and
delete this script's output; nothing in the code changes.

    python3 tools/make_toy_placeholders.py
"""

import math
import pathlib

from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "assets" / "toys"
UI = ROOT / "assets" / "ui"

SIZE = 64          # toy frames are 64x64, not the cat's 160x128
YARN_FRAMES = 8
LASER_FRAMES = 6


def yarn_sheet() -> Image.Image:
    """A ball of yarn rotating through one full turn, so the strip loops."""
    sheet = Image.new("RGBA", (SIZE * YARN_FRAMES, SIZE), (0, 0, 0, 0))

    for i in range(YARN_FRAMES):
        f = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        d = ImageDraw.Draw(f)
        cx = cy = SIZE // 2
        r = 20

        d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(214, 84, 96, 255))

        # Wrap lines rotating with the frame index — the only thing that says "this is yarn"
        # and not "this is a red circle".
        turn = 2 * math.pi * i / YARN_FRAMES
        for k in range(3):
            a = turn + k * math.pi / 3
            d.arc((cx - r + 3, cy - r + 3, cx + r - 3, cy + r - 3),
                  start=math.degrees(a), end=math.degrees(a) + 190,
                  fill=(168, 54, 68, 255), width=3)

        # Loose trailing end, so it reads as something to chase.
        tail_a = turn + math.pi
        tx = cx + int(math.cos(tail_a) * (r + 12))
        ty = cy + int(math.sin(tail_a) * (r + 12))
        d.line((cx, cy, tx, ty), fill=(214, 84, 96, 255), width=3)

        sheet.paste(f, (i * SIZE, 0))

    return sheet


def laser_sheet() -> Image.Image:
    """A red dot pulsing brighter and dimmer, looping."""
    sheet = Image.new("RGBA", (SIZE * LASER_FRAMES, SIZE), (0, 0, 0, 0))

    for i in range(LASER_FRAMES):
        f = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
        d = ImageDraw.Draw(f)
        cx = cy = SIZE // 2

        # Triangle wave so frame 0 flows back from the last one.
        t = i / (LASER_FRAMES - 1)
        pulse = 1 - abs(t * 2 - 1)

        for rad, alpha in ((14, 40), (10, 70), (6, 140)):
            a = int(alpha * (0.6 + 0.4 * pulse))
            d.ellipse((cx - rad, cy - rad, cx + rad, cy + rad), fill=(255, 40, 40, a))

        core = 3
        d.ellipse((cx - core, cy - core, cx + core, cy + core), fill=(255, 220, 220, 255))
        sheet.paste(f, (i * SIZE, 0))

    return sheet


def laser_menu_icon() -> Image.Image:
    """Radial-menu icon for the laser, matching the 64x64 of the existing ui icons."""
    f = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(f)
    cx = cy = SIZE // 2

    for rad, alpha in ((22, 45), (15, 90), (9, 160)):
        d.ellipse((cx - rad, cy - rad, cx + rad, cy + rad), fill=(255, 40, 40, alpha))
    d.ellipse((cx - 5, cy - 5, cx + 5, cy + 5), fill=(255, 235, 235, 255))
    return f


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    UI.mkdir(parents=True, exist_ok=True)

    yarn_sheet().save(OUT / "toy_yarn.png")
    laser_sheet().save(OUT / "toy_laser.png")

    icon = UI / "laser.png"
    if not icon.exists():
        laser_menu_icon().save(icon)

    print(f"placeholder toys -> {OUT.relative_to(ROOT)}")
    print(f"  toy_yarn.png   {YARN_FRAMES} frames @ {SIZE}x{SIZE}")
    print(f"  toy_laser.png  {LASER_FRAMES} frames @ {SIZE}x{SIZE}")
    print(f"  ui/laser.png   radial menu icon")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
