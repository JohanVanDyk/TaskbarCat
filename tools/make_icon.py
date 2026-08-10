#!/usr/bin/env python3
"""Builds assets/app.ico (taskbar/tray/exe icon) from a cat sprite frame.

The sprite frames are 160x128 with a lot of transparent margin, so a naive resize
gives a tiny cat floating in a big empty square. This trims to the alpha bounding
box first, then pads to square with a small margin, which keeps the cat readable
at 16px where the tray actually draws it.

Regenerate after any art drop:  python3 tools/make_icon.py
"""

import json
import pathlib
import sys

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
OUT = ASSETS / "app.ico"

# Pose used for the icon. A seated, front-facing cat reads better at 16px than a
# walk frame, which is mid-stride and asymmetric.
CLIP_ID = "sit_look"
FRAME = 0

# Windows picks the nearest size; supplying the small ones explicitly avoids the
# shell downscaling 256px and turning the outline to mush in the tray.
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

MARGIN = 0.06  # fraction of the square left empty around the cat


def main() -> int:
    manifest = json.loads((ASSETS / "sprites.json").read_text())
    defaults = manifest["defaults"]
    clip = next(c for c in manifest["clips"] if c["id"] == CLIP_ID)

    preset = next(p for p in manifest["colorPresets"] if p.get("default"))
    sheet_path = ASSETS / preset["sheetDir"] / clip["sheet"]

    fw = clip.get("frameWidth", defaults["frameWidth"])
    fh = clip.get("frameHeight", defaults["frameHeight"])

    sheet = Image.open(sheet_path).convert("RGBA")
    frame = sheet.crop((FRAME * fw, 0, (FRAME + 1) * fw, fh))

    bbox = frame.getbbox()
    if bbox is None:
        print(f"frame {FRAME} of {sheet_path.name} is fully transparent", file=sys.stderr)
        return 1
    cat = frame.crop(bbox)

    side = int(max(cat.size) * (1 + 2 * MARGIN))
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    # Horizontally centred, sat on the bottom margin: a cat pinned to the floor of
    # the icon looks deliberate, one floating in the middle looks like a bug.
    square.paste(cat, ((side - cat.width) // 2, side - cat.height - int(side * MARGIN)))

    square.save(OUT, sizes=[(s, s) for s in SIZES])
    print(f"{OUT.relative_to(ROOT)}  source={sheet_path.name}#{FRAME}  trimmed={cat.size}  sizes={SIZES}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
