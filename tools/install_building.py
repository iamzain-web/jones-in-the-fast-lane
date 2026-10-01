r"""One installer for every renamed board building. Differences live in tools/buildings.py.

    python tools/install_building.py --who socketcity --cels <dir> --set cast0
    python tools/install_building.py --who socketcity --cels <dir> --set cast0 --device bulb
    python tools/install_building.py --who socketcity --cels <dir> --set cast0 --verify

WHAT IT WRITES, AND WHERE
-------------------------
Into `assets/png{4,12}x-<set>` only, and it takes the SET's own files as its base so an
earlier building's work is not undone - rebuilding from the ported art is what silently
reverted the university's picPatch fix once.

    board.png, pic_11.png   the building, blended by silhouette, then the name
    <each surface>          any interior the detector found carrying the name

BOTH RUNGS CARRY EVERY FILE. `RenderScale` takes the largest factor at which the set
exists, so a 4x set silent about a file the 12x set replaces shows 1990 art whenever the
game settles on 4x.

LETTERING
---------
The 3x5 bitmap at 1x, where the plate is too small for anything else, and a REAL TYPEFACE
at 4x and 12x. At 12x a sign plate is hundreds of pixels across and stamping a block font
there spends that resolution reproducing 1990's staircase - which is what "I don't want it
to look pixelated, it's 2026 not 1986" was about. The model never sees text at any scale.
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_building as gb
import install_employment as ie          # the glyph set, text_mask and composite_text
from buildings import BUILDINGS

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")


def letter_region(img, lines, rect, ink, F, fill=0.88):
    """Write `lines` into `rect` (region-local, 1x) at factor F. Typeface if available,
    the 3x5 bitmap otherwise, so a machine without the fonts still produces correct art."""
    x0, y0, x1, y1 = [v * F for v in rect[:2]] + [rect[2] * F + F - 1, rect[3] * F + F - 1]
    bw, bh = x1 - x0 + 1, y1 - y0 + 1
    mask = ie.text_mask(lines, bw, bh, fill=fill)
    if mask is not None:
        return ie.composite_text(img, mask, (x0, y0), lambda r, h: ink), (x0, y0, bw, bh)
    sx = sy = max(1, min(bw // max(1, max(ie.tw(l) for l in lines)),
                         bh // (len(lines) * 6)))
    ty = y0 + (bh - (len(lines) * 6 - 1) * sy) // 2
    for i, ln in enumerate(lines):
        ie.stamp_scaled(img, ln, x0 + (bw - ie.tw(ln) * sx) // 2, ty + i * 6 * sy,
                        sx, sy, lambda ry, dy, s: ink)
    return img, (x0, y0, bw, bh)


def build_region(b, F, out_dir, ported, celdir, samples, device):
    """The building at factor F: generation inside Sierra's silhouette, then the name."""
    x, y, w, h = b.region
    gen = Image.open(os.path.join(samples, "generated_%dx%d.png"
                                  % (w * b.scale, h * b.scale))).convert("RGB")
    new = gen.resize((w * F, h * F), Image.LANCZOS)
    sierra = gb.sierra_region(b, celdir).resize((w * F, h * F), Image.NEAREST)
    m = gb.silhouette(b, gb.sierra_region(b, celdir), F)
    img = Image.fromarray(
        np.where(m[..., None], np.asarray(new), np.asarray(sierra)).astype(np.uint8),
        "RGB").copy()

    # the device, if this building has one and one was chosen
    if device and b.device_rect:
        dp = os.path.join(samples, "device_%s_384x512.png" % device)
        if os.path.exists(dp):
            dx0, dy0, dx1, dy1 = b.device_rect
            dw, dh = (dx1 - dx0 + 1) * F, (dy1 - dy0 + 1) * F
            d = Image.open(dp).convert("RGB").resize((dw, dh), Image.LANCZOS)
            img.paste(cut_out(d), (dx0 * F, dy0 * F), cut_out(d))
        else:
            print("    device '%s' not generated yet (%s)" % (device, dp))

    if b.name_plate:
        a = np.asarray(img).astype(np.uint8).copy()
        nx0, ny0, nx1, ny1 = b.name_rect
        a[ny0 * F:(ny1 + 1) * F, nx0 * F:(nx1 + 1) * F] = b.palette[b.name_plate]
        img = Image.fromarray(a, "RGB").copy()

    img, rect = letter_region(img, b.name_lines, b.name_rect, b.name_ink, F)
    return img, rect


def cut_out(d):
    """Key the device off its plain studio background so it sits ON the facade rather than
    in a box. The init paints that background a flat light grey on purpose."""
    a = np.asarray(d.convert("RGB")).astype(int)
    r, g, bl = a[..., 0], a[..., 1], a[..., 2]
    mx, mn = np.maximum(np.maximum(r, g), bl), np.minimum(np.minimum(r, g), bl)
    bg = (mn > 150) & ((mx - mn) < 42)
    out = np.dstack([a.astype(np.uint8), np.where(bg, 0, 255).astype(np.uint8)])
    return Image.fromarray(out, "RGBA")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--who", required=True, choices=sorted(BUILDINGS))
    ap.add_argument("--cels", required=True)
    ap.add_argument("--set", default="cast0")
    ap.add_argument("--device", default=None)
    ap.add_argument("--factors", default="4,12")
    ap.add_argument("--verify", action="store_true")
    a = ap.parse_args()

    b = BUILDINGS[a.who]
    samples = os.path.join(ROOT, "tools", "samples", "%s-rename" % b.key)
    names = ["board.png", "pic_11.png"] + [s.file for s in b.surfaces]
    print("%s -> %s   device=%s" % (b.sierra, b.new_name, a.device or "none"))

    for F in (int(s) for s in a.factors.split(",")):
        ported = os.path.join(ASSETS, "png%dx" % F)
        out = os.path.join(ASSETS, "png%dx-%s" % (F, a.set))
        print("\n=== %dx -> %s" % (F, out))
        if a.verify:
            for n in names:
                p = os.path.join(out, n)
                print("  %-22s %s" % (n, ("%dx%d in the SET" % Image.open(p).size)
                                      if os.path.exists(p) else "ABSENT"))
            continue
        os.makedirs(out, exist_ok=True)

        base = os.path.join(out, "board.png")
        if not os.path.exists(base):
            base = os.path.join(ported, "board.png")
        print("  board base: %s" % base)
        board = Image.open(base).convert("RGB")
        if board.size != (320 * F, 200 * F):
            raise SystemExit("board is %s, expected %s" % (board.size, (320 * F, 200 * F)))

        region, rect = build_region(b, F, out, ported, a.cels, samples, a.device)
        x, y, w, h = b.region
        board.paste(region, (x * F, y * F))
        for n in ("board.png", "pic_11.png"):
            board.save(os.path.join(out, n))
        print("  wrote board.png and pic_11.png %dx%d; name at (%d,%d) %dx%d"
              % (board.width, board.height, *rect))

        l, t, r, bm = b.hotspot
        if not (x >= l - 6 and x + w - 1 <= r + 6):
            print("  NOTE: the region sits outside the hotspot %s" % (b.hotspot,))

        for s in b.surfaces:
            src = os.path.join(out, s.file)
            if not os.path.exists(src):
                src = os.path.join(ported, s.file)
            im = Image.open(src).convert("RGBA")
            px0, py0, px1, py1 = s.plate
            arr = np.asarray(im).astype(np.uint8).copy()
            arr[py0 * F:(py1 + 1) * F, px0 * F:(px1 + 1) * F, :3] = s.body
            arr[py0 * F:(py1 + 1) * F, px0 * F:(px1 + 1) * F, 3] = 255
            im2 = Image.fromarray(arr, "RGBA").copy()
            rgb, r2 = letter_region(im2.convert("RGB"), s.lines, s.plate, s.ink, F,
                                    fill=0.90)
            outim = Image.new("RGBA", im2.size)
            outim.paste(rgb)
            outim.putalpha(im2.getchannel("A"))
            outim.save(os.path.join(out, s.file))
            print("  wrote %-22s %dx%d, plate (%d,%d) %dx%d"
                  % (s.file, outim.width, outim.height, *r2))

    if not a.verify:
        print("\nuntouched: assets/png, assets/png4x, assets/png12x (opened read-only)")
        print("verify: python tools/find_name_art.py --who %s --cels <dir> --set %s"
              % (b.key, a.set))


if __name__ == "__main__":
    main()
