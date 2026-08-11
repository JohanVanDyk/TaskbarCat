#!/usr/bin/env python3
"""Derives a new colour preset from an existing one by recolouring the coat.

The spec shipped a single orange-and-white cat. Rather than invent new pixel art, this
recolours the SATURATED pixels (the ginger coat) and leaves the near-greys alone, so the
white bib, paws, outline and eyes survive untouched — a flat hue rotation over the whole
sheet turns the whites and the black outline into tinted mush.

    python3 tools/make_preset.py grey_white --hue -12 --sat 0.18 --val 0.94

Writes assets/cat/<id>/*.png and prints the sprites.json block to paste in.
"""

import argparse
import colorsys
import json
import pathlib
import sys

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"

# Below this saturation a pixel is coat-neutral (white fur, outline, eye highlights) and is
# left exactly as it was. The ginger sits far above it, so the split is clean.
COAT_SATURATION_FLOOR = 0.25


def recolour(im: Image.Image, hue_shift: float, sat_scale: float, val_scale: float) -> Image.Image:
    px = im.convert("RGBA").load()
    w, h = im.size
    out = Image.new("RGBA", (w, h))
    op = out.load()

    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0:
                op[x, y] = (0, 0, 0, 0)
                continue

            hh, ss, vv = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if ss >= COAT_SATURATION_FLOOR:
                hh = (hh + hue_shift / 360.0) % 1.0
                ss = min(1.0, ss * sat_scale)
                vv = min(1.0, vv * val_scale)
                r2, g2, b2 = colorsys.hsv_to_rgb(hh, ss, vv)
                op[x, y] = (round(r2 * 255), round(g2 * 255), round(b2 * 255), a)
            else:
                op[x, y] = (r, g, b, a)

    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("id", help="new preset id, e.g. grey_white")
    ap.add_argument("--name", help='display name, e.g. "Grey & White"')
    ap.add_argument("--from", dest="source", default="orange_white", help="preset id to derive from")
    ap.add_argument("--hue", type=float, default=0.0, help="degrees to rotate the coat hue")
    ap.add_argument("--sat", type=float, default=1.0, help="coat saturation multiplier")
    ap.add_argument("--val", type=float, default=1.0, help="coat brightness multiplier")
    args = ap.parse_args()

    src_dir = ASSETS / "cat" / args.source
    if not src_dir.is_dir():
        print(f"no such preset: {src_dir}", file=sys.stderr)
        return 1

    dst_dir = ASSETS / "cat" / args.id
    dst_dir.mkdir(parents=True, exist_ok=True)

    # rglob, not glob: the chonk levels live in chonk2/ and chonk3/ subdirectories and a
    # coat that only recoloured the top level would snap back to ginger when the cat got fat.
    sheets = sorted(src_dir.rglob("*.png"))
    for sheet in sheets:
        rel = sheet.relative_to(src_dir)
        out = dst_dir / rel
        out.parent.mkdir(parents=True, exist_ok=True)
        recolour(Image.open(sheet), args.hue, args.sat, args.val).save(out)
        print(f"  {args.source}/{rel} -> {args.id}/{rel}")

    block = {
        "id": args.id,
        "name": args.name or args.id.replace("_", " ").title(),
        "sheetDir": f"cat/{args.id}",
    }
    print(f"\n{len(sheets)} sheets written. Add to sprites.json colorPresets:\n")
    print(json.dumps(block, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
