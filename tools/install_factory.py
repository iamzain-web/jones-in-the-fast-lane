r"""Installs the renamed Factory (`THE INDUSTRIAL REVOLUTION`) into an art set, 4x and 12x.

TWO SURFACES, AND THE COUNT COMES FROM THE RESOURCES
-----------------------------------------------------
`tools/find_name_art.py --who factory` lifts the two painted forms of the name off Sierra's
own art as pixel masks and searches all 829 ported views. It reports exactly two:

    the board sign     cel 11 of pic 11, 32x13 at (9,176) - board AND lettering together
    the interior chip  view 705 loop 0 cel 0, `THE FACTORY` on an IC on a circuit board

**NOT the picPatch.** That strip is 183 wide at x 68, so it covers game x 68..250; the
Factory sits at x 9..40 and is nowhere near it. The Employment Office and the University
both are inside it, which is what made those two renames look broken for three rounds, and
checking rather than assuming is why this one does not repeat it.

WHAT GETS WRITTEN
-----------------
    board.png, pic_11.png   the building, blended by SILHOUETTE, then the new sign
    view_705_l0_c0.png      the chip, re-lettered

THE SIGN IS A SEPARATE CEL, WHICH MAKES THE GUARANTEE STRUCTURAL
-----------------------------------------------------------------
The Employment Office's sign is part of its building cel, so that pipeline had to leave a
band blank and overpaint it. Here the building is in pic 11's BACKGROUND and the sign is
its own cel, so the model is handed a region with no sign in it at all and the sign is
composited afterwards. There is no band to leave blank because there is no band.

The new sign is bigger than Sierra's: `THE INDUSTRIAL REVOLUTION` is 89px on one line in
her own 3px-cap metric against a board that offers 28, so nothing fits and the board has to
grow. Option 2 of three was chosen - two lines, 55x21 at (9,171) - because it keeps the
full name and leaves 16 rows of building visible above it, which is the most of the three.

USAGE
    python tools/install_factory.py --set cast0
    python tools/install_factory.py --set cast0 --verify
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_factory as gf
import install_employment as ie

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")
SAMPLES = os.path.join(ROOT, "tools", "samples", "factory-rename")
CELS_HINT = "run tools/decode_pic_full.py --cels first"

LINES = ["THE INDUSTRIAL", "REVOLUTION"]
SIGN_X, SIGN_H_AT = 9, 191            # x, and the row the sign's BOTTOM sits on

# Sierra's sign palette, off cel 11
FIELD = (0xE0, 0xB8, 0x68)
SHADE = (0x90, 0x78, 0x58)
INK = (0x30, 0x58, 0x48)
POST = (0xC0, 0xD0, 0xE0)
POST_LO = (0x58, 0x40, 0x20)
POSTS = 4                              # rows of hanger above the board

# --- the interior chip, measured off view_705_l0_c0.png -------------------------
CHIP = "view_705_l0_c0.png"
CHIP_X0, CHIP_Y0, CHIP_X1, CHIP_Y1 = 30, 13, 145, 24      # the plate to repaint
CHIP_BODY = (0x58, 0x50, 0x48)
CHIP_INK = (0x98, 0x9F, 0x9F)
CHIP_LINE = "THE INDUSTRIAL REVOLUTION"


def sign_cel(F):
    """The hanging sign at factor F: Sierra's board, this port's name.

    The 1x geometry comes from the 3x5 bitmap because that is the rung with no room to
    spare; at 4x and 12x the same board carries the name in a real typeface, exactly as
    Hire Ground does. `install_employment.text_mask` falls back to the block font when no
    TrueType face is present, so a machine without them still produces correct art."""
    tw = max(ie.tw(l) for l in LINES)
    th = len(LINES) * 5 + (len(LINES) - 1)
    bw, bh = tw + 6, th + 4 + 2                     # pad 3 each side, 2 top/bottom, +2 rule
    w, h = bw, bh + POSTS

    a = np.zeros((h * F, w * F, 4), np.uint8)
    for i in range(3):
        x = int((i + 0.5) * bw / 3)
        a[0:POSTS * F, (x - 1) * F:(x + 1) * F] = (*POST, 255)
        a[(POSTS - 1) * F:POSTS * F, max(0, x - 2) * F:(x + 2) * F] = (*POST_LO, 255)
    a[POSTS * F:h * F, :] = (*FIELD, 255)
    a[POSTS * F:(POSTS + 1) * F, :] = (*SHADE, 255)
    a[(h - 1) * F:h * F, :] = (*SHADE, 255)
    a[POSTS * F:h * F, 0:F] = (*SHADE, 255)
    a[POSTS * F:h * F, (w - 1) * F:w * F] = (*SHADE, 255)
    cel = Image.fromarray(a, "RGBA").copy()

    mask = ie.text_mask(LINES, (w - 2) * F, (h - POSTS - 2) * F, fill=0.88)
    if mask is not None:
        rgb = ie.composite_text(cel, mask, (F, (POSTS + 1) * F), lambda r, hh: INK)
        out = Image.new("RGBA", cel.size)
        out.paste(rgb)
        out.putalpha(cel.getchannel("A"))
        cel = out
    else:
        px = cel.load()
        y0 = (POSTS + 2) * F
        for li, ln in enumerate(LINES):
            ie.stamp_scaled(cel, ln, ((bw - ie.tw(ln)) // 2) * F, y0 + li * 6 * F,
                            F, F, lambda ry, dy, sy: INK)
    return cel, w, h


def build_board(F, out_dir, ported, celdir):
    base = os.path.join(out_dir, "board.png")
    if not os.path.exists(base):
        base = os.path.join(ported, "board.png")
    print("  board base: %s" % base)
    board = Image.open(base).convert("RGB")
    if board.size != (320 * F, 200 * F):
        raise SystemExit("board is %s, expected %s" % (board.size, (320 * F, 200 * F)))

    bg = Image.open(os.path.join(celdir, "cel_00_319x199_at_1_0.png")).convert("RGB")
    sierra = bg.crop((gf.REG_X - 1, gf.REG_Y,
                      gf.REG_X - 1 + gf.REG_W, gf.REG_Y + gf.REG_H))
    gen = Image.open(os.path.join(SAMPLES, "generated_%dx%d.png"
                                  % (gf.GEN_W, gf.GEN_H))).convert("RGB")
    new = gen.resize((gf.REG_W * F, gf.REG_H * F), Image.LANCZOS)
    under = board.crop((gf.REG_X * F, gf.REG_Y * F,
                        (gf.REG_X + gf.REG_W) * F, (gf.REG_Y + gf.REG_H) * F))
    m = gf.silhouette(sierra, F)
    blended = Image.fromarray(
        np.where(m[..., None], np.asarray(new), np.asarray(under)).astype(np.uint8), "RGB")
    board.paste(blended, (gf.REG_X * F, gf.REG_Y * F))

    cel, w, h = sign_cel(F)
    sy = SIGN_H_AT - h + 1
    board.paste(cel, (SIGN_X * F, sy * F), cel)
    print("  building %dx%d at (%d,%d); sign %dx%d at (%d,%d)"
          % (gf.REG_W, gf.REG_H, gf.REG_X, gf.REG_Y, w, h, SIGN_X, sy))
    l, t, r, b = gf.RECT
    inside = SIGN_X >= l and SIGN_X + w - 1 <= r and sy >= t and sy + h - 1 <= b
    if not inside:
        raise SystemExit("the sign leaves the Factory hotspot %s" % (gf.RECT,))
    return board


def build_chip(F, out_dir, ported):
    src = os.path.join(out_dir, CHIP)
    if not os.path.exists(src):
        src = os.path.join(ported, CHIP)
    img = Image.open(src).convert("RGBA")
    x0, y0 = CHIP_X0 * F, CHIP_Y0 * F
    x1, y1 = CHIP_X1 * F + F - 1, CHIP_Y1 * F + F - 1
    a = np.asarray(img).astype(np.uint8).copy()
    a[y0:y1 + 1, x0:x1 + 1, :3] = CHIP_BODY
    a[y0:y1 + 1, x0:x1 + 1, 3] = 255
    img = Image.fromarray(a, "RGBA").copy()

    bw, bh = x1 - x0 + 1, y1 - y0 + 1
    mask = ie.text_mask([CHIP_LINE], bw, bh, fill=0.90)
    if mask is not None:
        rgb = ie.composite_text(img, mask, (x0, y0), lambda r, hh: CHIP_INK)
        out = Image.new("RGBA", img.size)
        out.paste(rgb)
        out.putalpha(img.getchannel("A"))
        img = out
    return img, (x0, y0, bw, bh)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--set", default="cast0")
    ap.add_argument("--cels", required=True, help=CELS_HINT)
    ap.add_argument("--factors", default="4,12")
    ap.add_argument("--verify", action="store_true")
    a = ap.parse_args()

    names = ["board.png", "pic_11.png", CHIP]
    print("name: %s / %s   (1x widths %d and %d px of %d usable on the ORIGINAL board)"
          % (LINES[0], LINES[1], ie.tw(LINES[0]), ie.tw(LINES[1]), 28))

    for F in (int(s) for s in a.factors.split(",")):
        ported = os.path.join(ASSETS, "png%dx" % F)
        out = os.path.join(ASSETS, "png%dx-%s" % (F, a.set))
        print("\n=== %dx -> %s" % (F, out))
        if a.verify:
            for n in names:
                p = os.path.join(out, n)
                print("  %-20s %s" % (n, "%dx%d in the SET" % Image.open(p).size
                                      if os.path.exists(p) else "ABSENT"))
            continue
        os.makedirs(out, exist_ok=True)

        board = build_board(F, out, ported, a.cels)
        for n in ("board.png", "pic_11.png"):
            board.save(os.path.join(out, n))
        print("  wrote board.png and pic_11.png %dx%d" % board.size)

        chip, rect = build_chip(F, out, ported)
        chip.save(os.path.join(out, CHIP))
        print("  wrote %-20s %dx%d, plate at (%d,%d) %dx%d"
              % (CHIP, chip.width, chip.height, *rect))

    if not a.verify:
        print("\nuntouched: assets/png, assets/png4x, assets/png12x (opened read-only)")
        print("verify with: python tools/find_name_art.py --who factory --cels <dir> "
              "--set %s" % a.set)


if __name__ == "__main__":
    main()
