r"""Installs the renamed university into the ART SET, at 12x. Writes no original.

WHERE THIS WRITES, AND WHY THERE
--------------------------------
`RenderScale.ArtDirectories` searches `assets/png12x-<set>` BEFORE `assets/png12x` when
`JONES_ART_SET=<set>` is in the environment, file by file, and falls back to the ported art
for everything the set is silent about. So a set need only carry the files it replaces.
This writes three files into `assets/png12x-new`:

    board.png            the town board with the new university composited in
    pic_11.png           the same image under its resource number, which
                         MainViewModel asks for separately
    view_807_l0_c0.png   the university interior backdrop, re-lettered

NOTHING in `assets/png`, `assets/png4x` or `assets/png12x` is written. Clearing
`JONES_ART_SET` puts Sierra's art back exactly, with no code path of its own.

    TO SEE IT:   $env:JONES_ART_SET = 'new'
    TO GO BACK:  Remove-Item Env:\JONES_ART_SET

WHY THE BOARD BUILDING COMES FROM THE 944x656 GENERATION, NOT FROM THE 59x41 CEL
-------------------------------------------------------------------------------
The 59x41 cel is what the ORIGINAL's coordinate space needs and it is what the click
rectangle is measured against, but it is not what gets drawn at 12x. Blowing 59x41 up
twelve times would throw away every pixel of architecture the generator made. The 12x
building is therefore LANCZOS-downscaled from the 944x656 generation - 944 -> 708 is a
reduction, so it keeps real detail - and the lettering is stamped onto it afterwards at
12x, exactly as it is at 1x. The band-blank-then-stamp split is preserved at both scales;
the model never sees text at either.

THE FOOTPRINT IS UNCHANGED. The building occupies cel pixels (191,141)..(249,181) in
320x200 space at every scale, so `Board.All`'s University rectangle (190,158)-(250,192)
answers for the same pixels it always did. `--verify` re-reports the coverage.

USAGE
    python tools/install_university.py
    python tools/install_university.py --verify
"""

import argparse
import os
import shutil

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")
PORTED = os.path.join(ASSETS, "png12x")
SET = os.path.join(ASSETS, "png12x-new")
SAMPLES = os.path.join(ROOT, "tools", "samples", "university-rename")

F = 12                                  # the art factor these files are built at
CEL_W, CEL_H = 59, 41
CEL_X, CEL_Y = 191, 141

# the sign band on the building, in cel coordinates
BAND_X0, BAND_X1, BAND_Y0, BAND_Y1 = 3, 55, 11, 24
# the plaque on the interior backdrop, in its own 183x112 coordinates - MEASURED, by
# finding the solid blue field, not guessed
PLQ_X0, PLQ_X1, PLQ_Y0, PLQ_Y1 = 68, 181, 2, 25

LINES = ["OPEN DOOR", "UNIVERSITY"]
GOLD, GOLD_HI = (0xE0, 0x80, 0x40), (0xE0, 0xB8, 0x68)
BLUE = (0x40, 0x58, 0x90)
# Sierra's own gradient ramp off her interior lettering, top to bottom
RAMP = [(0xF8, 0xE8, 0x98), (0xF8, 0xC0, 0x60), (0xF8, 0xA0, 0x68),
        (0xE0, 0x80, 0x40), (0xE0, 0xB8, 0x68)]

G = {
    "A": ["111", "101", "111", "101", "101"], "B": ["110", "101", "110", "101", "110"],
    "C": ["111", "100", "100", "100", "111"], "D": ["110", "101", "101", "101", "110"],
    "E": ["111", "100", "111", "100", "111"], "F": ["111", "100", "111", "100", "100"],
    "G": ["111", "100", "101", "101", "111"], "H": ["101", "101", "111", "101", "101"],
    "I": ["1", "1", "1", "1", "1"], "J": ["001", "001", "001", "101", "111"],
    "K": ["101", "101", "110", "101", "101"], "L": ["100", "100", "100", "100", "111"],
    "M": ["101", "111", "111", "101", "101"], "N": ["101", "111", "111", "111", "101"],
    "O": ["111", "101", "101", "101", "111"], "P": ["111", "101", "111", "100", "100"],
    "Q": ["111", "101", "101", "111", "011"], "R": ["111", "101", "111", "110", "101"],
    "S": ["111", "100", "111", "001", "111"], "T": ["111", "010", "010", "010", "010"],
    "U": ["101", "101", "101", "101", "111"], "V": ["101", "101", "101", "101", "010"],
    "W": ["101", "101", "111", "111", "101"], "X": ["101", "101", "010", "101", "101"],
    "Y": ["101", "101", "111", "010", "010"], "Z": ["111", "001", "010", "100", "111"],
    ".": ["0", "0", "0", "0", "1"], " ": ["0", "0", "0", "0", "0"],
}
GAP = 1


def tw(s):
    return 0 if not s else sum(len(G[c][0]) for c in s) + GAP * (len(s) - 1)


def stamp_scaled(img, s, x, y, sx, sy, colour_for):
    """Stamp `s` with each glyph pixel drawn as an sx by sy block."""
    px = img.load()
    cx = x
    for ch in s:
        gl = G[ch]
        for ry, bits in enumerate(gl):
            for rx, b in enumerate(bits):
                if b != "1":
                    continue
                for dy in range(sy):
                    col = colour_for(ry, dy, sy)
                    for dx in range(sx):
                        X, Y = cx + rx * sx + dx, y + ry * sy + dy
                        if 0 <= X < img.width and 0 <= Y < img.height:
                            px[X, Y] = col
        cx += (len(gl[0]) + GAP) * sx


def build_building_12x():
    """The 708x492 building: architecture from the 944x656 generation, lettering stamped
    on at 12x afterwards."""
    gen = Image.open(os.path.join(SAMPLES, "generated_944x656.png")).convert("RGB")
    cel = gen.resize((CEL_W * F, CEL_H * F), Image.LANCZOS)

    bx0, bx1 = BAND_X0 * F, BAND_X1 * F + F - 1
    by0, by1 = BAND_Y0 * F, BAND_Y1 * F + F - 1
    a = np.asarray(cel).astype(int).copy()
    med = np.median(a[by0:by1 + 1, bx0:bx1 + 1].reshape(-1, 3), axis=0)
    body = tuple(int(max(0, min(255, c * 0.62))) for c in med)
    edge = tuple(int(max(0, min(255, c * 0.40))) for c in med)
    rim = F                                       # a one-cel-pixel rim, so it reads as a board
    a[by0:by1 + 1, bx0:bx1 + 1] = body
    a[by0:by0 + rim, bx0:bx1 + 1] = edge
    a[by1 - rim + 1:by1 + 1, bx0:bx1 + 1] = edge
    a[by0:by1 + 1, bx0:bx0 + rim] = edge
    a[by0:by1 + 1, bx1 - rim + 1:bx1 + 1] = edge
    img = Image.fromarray(a.astype(np.uint8), "RGB")

    bw, bh = bx1 - bx0 + 1, by1 - by0 + 1
    th = (len(LINES) * 5 + (len(LINES) - 1)) * F
    ty = by0 + (bh - th) // 2
    for i, ln in enumerate(LINES):
        cx = bx0 + (bw - tw(ln) * F) // 2
        stamp_scaled(img, ln, cx, ty + i * 6 * F, F, F,
                     lambda ry, dy, sy: GOLD_HI if ry == 0 else GOLD)
    return img, body, edge


def build_interior_12x():
    """The 2196x1344 backdrop with the plaque re-lettered. Everything outside the plaque
    is the ported 12x art, untouched."""
    src = os.path.join(SET, "view_807_l0_c0.png")
    if not os.path.exists(src):
        src = os.path.join(PORTED, "view_807_l0_c0.png")
    img = Image.open(src).convert("RGB")

    x0, x1 = PLQ_X0 * F, PLQ_X1 * F + F - 1
    y0, y1 = PLQ_Y0 * F, PLQ_Y1 * F + F - 1
    a = np.asarray(img).astype(np.uint8).copy()
    a[y0:y1 + 1, x0:x1 + 1] = BLUE
    img = Image.fromarray(a, "RGB")

    S = 2 * F                                      # option B is 2x glyphs, at 12x art
    pw, ph = x1 - x0 + 1, y1 - y0 + 1
    gh = 5 * S
    th = len(LINES) * gh + (len(LINES) - 1) * S
    ty = y0 + (ph - th) // 2

    def ramp(ry, dy, sy):
        return RAMP[min(len(RAMP) - 1, (ry * sy + dy) * len(RAMP) // gh)]

    for i, ln in enumerate(LINES):
        cx = x0 + (pw - tw(ln) * S) // 2
        stamp_scaled(img, ln, cx, ty + i * (gh + S), S, S, ramp)
    return img, (x0, y0, x1, y1)


def coverage():
    l, t, r, b = 190, 158, 250, 192
    rw, rh = r - l + 1, b - t + 1
    ox0, ox1 = max(l, CEL_X), min(r, CEL_X + CEL_W - 1)
    oy0, oy1 = max(t, CEL_Y), min(b, CEL_Y + CEL_H - 1)
    ow, oh = ox1 - ox0 + 1, oy1 - oy0 + 1
    return ow * oh, rw * rh, 100.0 * ow * oh / (rw * rh)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--verify", action="store_true", help="report only, write nothing")
    a = ap.parse_args()

    os.makedirs(SET, exist_ok=True)
    px, tot, pct = coverage()
    print("footprint: cel %dx%d at (%d,%d) -> %d of %d px = %.1f%% of the click rect"
          % (CEL_W, CEL_H, CEL_X, CEL_Y, px, tot, pct))

    if a.verify:
        for n in ("board.png", "pic_11.png", "view_807_l0_c0.png"):
            p = os.path.join(SET, n)
            if os.path.exists(p):
                im = Image.open(p)
                print("  %-20s %dx%d  in the SET" % (n, im.width, im.height))
            else:
                print("  %-20s ABSENT from the set" % n)
        return

    # --- the board -------------------------------------------------------------
    board_src = os.path.join(SET, "board.png")
    if not os.path.exists(board_src):
        board_src = os.path.join(PORTED, "board.png")
    print("board base: %s" % board_src)
    board = Image.open(board_src).convert("RGB")
    if board.size != (320 * F, 200 * F):
        raise SystemExit("board is %s, expected %s" % (board.size, (320 * F, 200 * F)))

    bld, body, edge = build_building_12x()
    board.paste(bld, (CEL_X * F, CEL_Y * F))
    for n in ("board.png", "pic_11.png"):
        board.save(os.path.join(SET, n))
        print("  wrote %s  %dx%d" % (os.path.join(SET, n), board.width, board.height))
    print("  band body #%02X%02X%02X edge #%02X%02X%02X" % (*body, *edge))

    # --- the interior ----------------------------------------------------------
    interior, rect = build_interior_12x()
    p = os.path.join(SET, "view_807_l0_c0.png")
    interior.save(p)
    print("  wrote %s  %dx%d, plaque %s" % (p, interior.width, interior.height, rect))

    # --- prove no original was written -----------------------------------------
    print("\nuntouched, by construction - this script opens them read-only:")
    for d in ("png", "png4x", "png12x"):
        print("  assets/%s" % d)
    print("\n  $env:JONES_ART_SET = 'new'      to see it")
    print("  Remove-Item Env:\\JONES_ART_SET  to go back to Sierra's")


if __name__ == "__main__":
    main()
