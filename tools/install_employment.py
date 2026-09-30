r"""Installs the renamed Employment Office (`HIRE GROUND`) into an ART SET, at 12x and 4x.
Writes no original.

WHERE THIS WRITES, AND WHY THERE
--------------------------------
`RenderScale.ArtDirectories` searches `assets/png{F}x-<set>` BEFORE `assets/png{F}x` when
`JONES_ART_SET=<set>` is in the environment, file by file, and falls back to the ported art
for everything the set is silent about. So a set need only carry the files it replaces.

`RenderScale` takes the largest factor at which the SET exists, so BOTH RUNGS MUST CARRY
EVERY FILE: a 4x set that is silent about a file the 12x set replaces will show the 1990
art whenever the game settles on 4x. This writes all five files at both 4 and 12.

    board.png            the town board with the new building composited in
    pic_11.png           the same image under its resource number, which
                         MainViewModel asks for separately
    view_0_l1_c0.png     the picPatch strip - SEE BELOW, this is the one that bites
    view_706_l0_c0.png   the interior backdrop, re-lettered
    view_706_l0_c1.png   the job-list backdrop, re-lettered - IT CARRIES THE NAME TOO

THE FOUR SURFACES WERE FOUND FROM THE RESOURCES, NOT FROM A LIST
----------------------------------------------------------------
`tools/find_employment_art.py` takes the two painted forms of the name off Sierra's own
art as pixel masks and searches all 829 ported views for them. It reports exactly four,
and finding `view_706_l0_c1.png` is why it exists: the university had ONE interior
backdrop carrying its name and the obvious assumption was that this building would too.
It has two, because the job list draws over a different cel of the same view. Run that
tool with `--set` after this one; it is the acceptance check.

WHY THE picPatch MATTERS
------------------------
`MainViewModel.BuildBoard` draws the board, then the character body cel over it, then
**`picPatch` - view 0 loop 1 cel 0, 183x25 at (68,138)** - which repaints the BUILDING TOPS
back over the panel so they stand in front of it. That strip contains Sierra's own painted
copy of this building's sign, at strip-local (9,15). It is a VIEW cel, not part of pic 11,
so replacing `board.png` does not touch it: the set would fall through to the ported art
and `EMPLOYMENT OFFICE` would be stamped over the new sign on every board screen. It only
shows on the TOWN BOARD - the setup screens call `TitleBackdrop`, not `BuildBoard` - which
is why a setup-screen capture looks clean while the bug is plainly there.

    picPatch covers game x 68..250, y 138..162.
    This building's cel is at (75,151) 47x34, so its top 12 rows fall inside the strip, at
    strip-local x 7..53, y 13..24.

**THE STRIP'S ALPHA IS PRESERVED, which is where this departs from
`install_university_picpatch.py`.** That script forced the region to alpha 255 because the
university's cel is a solid rectangle there. This one is not: strip row 13 (the cel's row
0) is fully TRANSPARENT in Sierra's strip, and forcing it opaque would add a row of
building above the roofline she drew. So only RGB is replaced and the alpha channel is
left exactly as it is, which reproduces her silhouette by construction rather than by
assertion.

WHY THE BOARD BUILDING COMES FROM THE 752x544 GENERATION, NOT FROM THE 47x34 CEL
--------------------------------------------------------------------------------
The 47x34 cel is what the ORIGINAL's coordinate space needs, but it is not what gets drawn
at 12x. Blowing 47x34 up twelve times would throw away every pixel of architecture the
generator made. The building is therefore LANCZOS-downscaled from the 752x544 generation
to each factor's size, and the lettering is stamped on afterwards AT THAT FACTOR, exactly
as it is at 1x. The plate-blank-then-stamp split is preserved at every scale; the model
never sees text at any of them.

THE FOOTPRINT IS UNCHANGED. The building occupies (75,151)..(121,184) in 320x200 space at
every scale, so `Board.All`'s Employment Office rectangle (68,158)-(128,192) answers for
the same pixels it always did. `--verify` re-reports the coverage.

USAGE
    python tools/install_employment.py
    python tools/install_employment.py --set cast0 --verify
"""

import argparse
import os

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")
SAMPLES = os.path.join(ROOT, "tools", "samples", "employment-rename")

CEL_W, CEL_H = 47, 34
CEL_X, CEL_Y = 75, 151
RECT = (68, 158, 128, 192)

# the sign board on the building, in cel coordinates - measured, see gen_employment.py
BAND_X0, BAND_X1, BAND_Y0, BAND_Y1 = 1, 45, 1, 13
# The building's own footprint inside the cel. Everything outside it is scenery Sierra
# painted into the same cel - see build_building. Kept in step with gen_employment.py.
PLATE_RECT = (BAND_X0, BAND_Y0, BAND_X1, BAND_Y1)      # x 1..45, y 1..13
BODY_RECT = (2, 14, 44, 31)                            # x 2..44, y 14..31
BOARD_LINES = ["HIRE", "GROUND"]
LETTER, LETTER_HI = (0xD0, 0xD8, 0xE0), (0xE0, 0xF0, 0xF8)
# Sierra's own sign palette off cel 10 - see build_building for why the plate keeps it.
PLATE, PLATE_HI, PLATE_LO = (0xC0, 0x38, 0x38), (0xE0, 0x38, 0x38), (0x68, 0x20, 0x28)

# the picPatch strip
PATCH = "view_0_l1_c0.png"
PATCH_X, PATCH_Y, PATCH_W, PATCH_H = 68, 138, 183, 25

# --- the interior title band, in view 706's own 183x112 coordinates ---------------
# MEASURED off view_706_l0_c0.png by finding the solid green field and the gold lettering
# inside it, not guessed: the field is #205840 over x 3..179, y 3..12, and Sierra's caps
# are 6 rows tall (y 5..10) at a 10px pitch, spanning x 6..175.
TITLE_VIEWS = ["view_706_l0_c0.png", "view_706_l0_c1.png"]
FLD_X0, FLD_X1, FLD_Y0, FLD_Y1 = 3, 179, 3, 12
FIELD = (0x20, 0x58, 0x40)
TITLE_LINE = "HIRE GROUND"

# Sierra's own gold ramp through the cap height, sampled row by row off her lettering.
# Brightest in the middle, not at the top - which is why it is measured and not invented.
RAMP = [(0xD0, 0x90, 0x58), (0xE0, 0xB8, 0x68), (0xF8, 0xE8, 0x98),
        (0xE0, 0xC8, 0x98), (0xE0, 0xC8, 0x98), (0xE0, 0xC8, 0x98)]

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


# --- smooth lettering for the high-resolution rungs -----------------------------
#
# WHY THE 3x5 BITMAP IS NOT USED AT 12x.
#
# At 1x the sign plate's interior is 43 x 11 px and a 3x5 block font is the only thing that
# fits - there is no argument to have. At 12x the same plate is 516 x 132 px, and stamping
# the same bitmap with each glyph pixel as a 12x12 square spends all of that resolution
# reproducing 1990's staircase.
#
# That is exactly what it looked like. With the building generated at 752x544 the stucco,
# the louvre slats, the door mullion and the individual leaves all came through smooth at
# 564x408 - and the LETTERING was the only hard-edged thing left in the frame, markedly
# blockier than Sierra's own, whose smoothness is merely the accident of being an upscale.
# The verdict on it was "I don't want it to look pixelated, it's 2026 not 1986", and the
# lettering is what that is about.
#
# So the high-resolution rungs draw the name with a real typeface and the 1x cel keeps the
# bitmap. The deterministic guarantee is untouched: this is still drawn by code, after the
# generation, into a band the model was never shown, and it still cannot be garbled.
#
# Bahnschrift first - it is the condensed grotesque Windows ships and it is the right
# register for a shopfront sign. The rest are fallbacks, and if none is present the block
# font is used, so a machine without these fonts still produces correct art.
SIGN_FONTS = ["bahnschrift.ttf", "seguisb.ttf", "segoeuib.ttf", "arialbd.ttf",
              "trebucbd.ttf", "verdanab.ttf"]


def _font(px):
    from PIL import ImageFont
    for n in SIGN_FONTS:
        for d in (r"C:\Windows\Fonts", "/usr/share/fonts"):
            p = os.path.join(d, n)
            if os.path.exists(p):
                try:
                    return ImageFont.truetype(p, px)
                except OSError:
                    pass
    return None


def text_mask(lines, box_w, box_h, fill=0.86, leading=0.16):
    """An L-mode mask of `lines` set as large as fits in box_w x box_h, or None.

    Rendered at 4x and boxed down, so the edges are properly anti-aliased rather than
    relying on the rasteriser's own hinting at very large point sizes."""
    from PIL import ImageDraw
    S = 4
    W, H = box_w * S, box_h * S
    lo, hi = 4, H
    best = None
    while lo <= hi:                                   # binary search the point size
        mid = (lo + hi) // 2
        f = _font(mid)
        if f is None:
            return None
        widths, heights = [], []
        for ln in lines:
            l, t, r, b = f.getbbox(ln)
            widths.append(r - l)
            heights.append(b - t)
        tw_ = max(widths)
        th_ = sum(heights) + int(leading * mid) * (len(lines) - 1)
        if tw_ <= W * fill and th_ <= H * fill:
            best = (mid, f, widths, heights, tw_, th_)
            lo = mid + 1
        else:
            hi = mid - 1
    if best is None:
        return None
    size, f, widths, heights, tw_, th_ = best

    m = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(m)
    y = (H - th_) // 2
    for i, ln in enumerate(lines):
        l, t, r, b = f.getbbox(ln)
        d.text(((W - (r - l)) // 2 - l, y - t), ln, font=f, fill=255)
        y += heights[i] + int(leading * size)
    return m.resize((box_w, box_h), Image.BOX)


def composite_text(img, mask, box, colour_for):
    """Paint `mask` into `img` at `box`, taking the colour from `colour_for(row, height)`."""
    x0, y0 = box
    a = np.asarray(img.convert("RGB")).astype(np.float32).copy()
    mk = np.asarray(mask).astype(np.float32) / 255.0
    h, w = mk.shape
    rows = np.where(mk.max(axis=1) > 0.02)[0]
    if len(rows) == 0:
        return img
    top, bot = int(rows[0]), int(rows[-1])
    for y in range(h):
        col = np.array(colour_for(y - top, max(1, bot - top + 1)), np.float32)
        al = mk[y][:, None]
        seg = a[y0 + y:y0 + y + 1, x0:x0 + w]
        if seg.shape[1] != w:
            continue
        a[y0 + y:y0 + y + 1, x0:x0 + w] = seg * (1 - al) + col * al
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGB")


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


def build_building(F):
    """The building at factor F: architecture from the 752x544 generation, sign plate and
    lettering stamped on afterwards at F."""
    gen = Image.open(os.path.join(SAMPLES, "generated_752x544.png")).convert("RGB")
    cel = gen.resize((CEL_W * F, CEL_H * F), Image.LANCZOS)

    bx0, bx1 = BAND_X0 * F, BAND_X1 * F + F - 1
    by0, by1 = BAND_Y0 * F, BAND_Y1 * F + F - 1
    a = np.asarray(cel).astype(int).copy()
    # THE PLATE KEEPS SIERRA'S RED, and only its LIGHTING comes from the generation.
    #
    # Sampling the generated plate outright is what `gen_university` does and it was right
    # there, because a stone frieze should take the new stone's colour. It is wrong here.
    # The prompt asks for brick and render so the model paints the plate as brickwork, the
    # median of that region comes back #874432 - a muddy brown - and the sign stops reading
    # as a red sign. The plate is not architecture, it is a painted board; its colour is a
    # fact about the building's identity, not about the material the model chose.
    #
    # So the hue is Sierra's own #C03838 and the generation contributes only brightness,
    # which keeps the board sitting in the same light as the wall above it.
    med = np.median(a[by0:by1 + 1, bx0:bx1 + 1].reshape(-1, 3), axis=0)
    lum = float(np.clip(med.mean() / 0x6E, 0.80, 1.20))    # 0x6E = Sierra's own plate mean
    body = tuple(int(max(0, min(255, c * lum))) for c in PLATE)
    hi = tuple(int(max(0, min(255, c * lum * 1.16))) for c in PLATE_HI)
    lo = tuple(int(max(0, min(255, c * lum))) for c in PLATE_LO)
    rim = F                                   # a one-cel-pixel rim, so it reads as a board
    a[by0:by1 + 1, bx0:bx1 + 1] = body
    a[by0:by0 + rim, bx0:bx1 + 1] = hi
    a[by0:by1 + 1, bx0:bx0 + rim] = hi
    a[by1 - rim + 1:by1 + 1, bx0:bx1 + 1] = lo
    a[by0:by1 + 1, bx1 - rim + 1:bx1 + 1] = lo
    img = Image.fromarray(a.astype(np.uint8), "RGB").copy()   # .copy(): see build_title

    bw, bh = bx1 - bx0 + 1, by1 - by0 + 1
    # Inset by one cel pixel so the letters sit inside the plate's rim, not on it.
    inset = F
    mask = text_mask(BOARD_LINES, bw - 2 * inset, bh - 2 * inset)
    if mask is not None:
        img = composite_text(
            img, mask, (bx0 + inset, by0 + inset),
            lambda r, h: LETTER_HI if r < max(1, h // 6) else LETTER)
    else:
        th = (len(BOARD_LINES) * 5 + (len(BOARD_LINES) - 1)) * F
        ty = by0 + (bh - th) // 2
        for i, ln in enumerate(BOARD_LINES):
            cx = bx0 + (bw - tw(ln) * F) // 2
            stamp_scaled(img, ln, cx, ty + i * 6 * F, F, F,
                         lambda ry, dy, sy: LETTER_HI if ry == 0 else LETTER)

    # ALPHA OUTSIDE THE BUILDING'S FOOTPRINT, so the board's own scenery shows through.
    # Sierra painted the trees and the water around this building INTO its cel; the
    # generation paints a pale sky there instead, and pasting the full rectangle boxes the
    # building in a cream halo. Pasting through this mask leaves whatever the board already
    # has around it - which, at this point in the pipeline, is Sierra's scenery.
    out = img.convert("RGBA")
    a = np.asarray(out).astype(np.uint8).copy()
    a[..., 3] = 0
    for (x0, y0, x1, y1) in (PLATE_RECT, BODY_RECT):
        a[y0 * F:(y1 + 1) * F, x0 * F:(x1 + 1) * F, 3] = 255
    return Image.fromarray(a, "RGBA"), body, lo


def build_title(src, F):
    """A 706 backdrop with the title band re-lettered. Everything outside the band is the
    ported art, untouched.

    The glyph scale is derived from Sierra's own metrics rather than picked: her caps are
    6 of the band's 10 rows, so sy = 1.2F, and her pitch is 10px against this set's 4-unit
    glyph cell, so sx = 3.2F puts the new name at very nearly her letter size. `HIRE
    GROUND` is 11 characters against her 17, so it is legitimately narrower - it is the
    LETTERS that are matched to hers, not the total width."""
    img = Image.open(src).convert("RGBA")
    x0, x1 = FLD_X0 * F, FLD_X1 * F + F - 1
    y0, y1 = FLD_Y0 * F, FLD_Y1 * F + F - 1
    a = np.asarray(img).astype(np.uint8).copy()
    a[y0:y1 + 1, x0:x1 + 1, :3] = FIELD
    a[y0:y1 + 1, x0:x1 + 1, 3] = 255
    # .copy(): an Image wrapping a numpy buffer is READ-ONLY, and `stamp_scaled` writes
    # through `load()`. Without this the stamp raises "image is readonly".
    img = Image.fromarray(a, "RGBA").copy()

    bw, bh = x1 - x0 + 1, y1 - y0 + 1
    inset = max(1, F // 2)
    mask = text_mask([TITLE_LINE], bw - 2 * inset, bh - 2 * inset, fill=0.92)
    if mask is not None:
        rgb = composite_text(
            img, mask, (x0 + inset, y0 + inset),
            lambda r, h: RAMP[min(len(RAMP) - 1, max(0, r) * len(RAMP) // max(1, h))])
        out = Image.new("RGBA", img.size)
        out.paste(rgb)
        out.putalpha(img.getchannel("A"))
        return out, (x0 + inset, y0 + inset, bw, bh)

    sx, sy = max(1, round(3.2 * F)), max(1, round(1.2 * F))
    gh = 5 * sy
    tx = x0 + (bw - tw(TITLE_LINE) * sx) // 2
    ty = y0 + (bh - gh) // 2

    def ramp(ry, dy, sy_):
        return RAMP[min(len(RAMP) - 1, (ry * sy_ + dy) * len(RAMP) // gh)]

    stamp_scaled(img, TITLE_LINE, tx, ty, sx, sy, ramp)
    return img, (tx, ty, sx, sy)


def build_picpatch(ported_patch, board, F):
    """Sierra's strip with ONLY this building's region repainted from the set's own board,
    AND HER ALPHA LEFT ALONE - see the module docstring."""
    patch = Image.open(ported_patch).convert("RGBA")
    if patch.size != (PATCH_W * F, PATCH_H * F):
        raise SystemExit("%s is %s, expected %s"
                         % (ported_patch, patch.size, (PATCH_W * F, PATCH_H * F)))

    lx0, ly0 = CEL_X - PATCH_X, CEL_Y - PATCH_Y                  # 7, 13
    lx1 = min(CEL_X + CEL_W, PATCH_X + PATCH_W) - PATCH_X        # 54
    ly1 = PATCH_H                                                # 25

    region = board.crop((CEL_X * F, CEL_Y * F,
                         (PATCH_X + lx1) * F, (PATCH_Y + ly1) * F)).convert("RGB")
    a = np.asarray(patch).astype(np.uint8).copy()
    r = np.asarray(region).astype(np.uint8)
    y0, y1, x0, x1 = ly0 * F, ly1 * F, lx0 * F, lx1 * F
    kept = int((a[y0:y1, x0:x1, 3] == 0).sum())
    a[y0:y1, x0:x1, :3] = r                       # RGB only; alpha deliberately untouched
    return Image.fromarray(a, "RGBA"), (x0, y0, x1, y1), kept


def coverage():
    l, t, r, b = RECT
    ox0, ox1 = max(l, CEL_X), min(r, CEL_X + CEL_W - 1)
    oy0, oy1 = max(t, CEL_Y), min(b, CEL_Y + CEL_H - 1)
    ow, oh = ox1 - ox0 + 1, oy1 - oy0 + 1
    return ow * oh, (r - l + 1) * (b - t + 1), 100.0 * ow * oh / ((r - l + 1) * (b - t + 1))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--set", default="cast0", help="art set name (assets/png{F}x-<set>)")
    ap.add_argument("--factors", default="4,12")
    ap.add_argument("--verify", action="store_true", help="report only, write nothing")
    args = ap.parse_args()

    names = ["board.png", "pic_11.png", PATCH] + TITLE_VIEWS
    px, tot, pct = coverage()
    print("footprint: cel %dx%d at (%d,%d) -> %d of %d px = %.1f%% of the click rect"
          % (CEL_W, CEL_H, CEL_X, CEL_Y, px, tot, pct))
    print("board sign: %s (%d px) over %s (%d px), plate interior %d x %d"
          % (BOARD_LINES[0], tw(BOARD_LINES[0]), BOARD_LINES[1], tw(BOARD_LINES[1]),
             BAND_X1 - BAND_X0 - 1, BAND_Y1 - BAND_Y0 - 1))

    for F in (int(s) for s in args.factors.split(",")):
        ported = os.path.join(ASSETS, "png%dx" % F)
        out = os.path.join(ASSETS, "png%dx-%s" % (F, args.set))
        print("\n=== %dx  ->  %s" % (F, out))
        if args.verify:
            for n in names:
                p = os.path.join(out, n)
                if os.path.exists(p):
                    im = Image.open(p)
                    print("  %-20s %dx%d  in the SET" % (n, im.width, im.height))
                else:
                    print("  %-20s ABSENT from the set" % n)
            continue

        os.makedirs(out, exist_ok=True)

        # --- the board ---------------------------------------------------------
        # The SET's own board first: it already carries the renamed university, and
        # rebuilding from the ported art would silently undo that job.
        base = os.path.join(out, "board.png")
        if not os.path.exists(base):
            base = os.path.join(ported, "board.png")
        print("  board base: %s" % base)
        board = Image.open(base).convert("RGB")
        if board.size != (320 * F, 200 * F):
            raise SystemExit("board is %s, expected %s" % (board.size, (320 * F, 200 * F)))

        bld, body, lo = build_building(F)
        board.paste(bld, (CEL_X * F, CEL_Y * F), bld)      # masked: see build_building
        for n in ("board.png", "pic_11.png"):
            board.save(os.path.join(out, n))
        print("  wrote board.png and pic_11.png  %dx%d" % (board.width, board.height))
        print("  plate body #%02X%02X%02X edge #%02X%02X%02X" % (*body, *lo))

        # --- the picPatch ------------------------------------------------------
        # THE SET'S OWN STRIP FIRST, for the same reason the board takes the set's own
        # board: this strip carries the tops of SEVERAL buildings, and the university's
        # rename already rebuilt it once. Starting from the ported strip throws that away
        # and stamps Sierra's `HI-TECH U` back over the new university - which is the exact
        # bug `install_university_picpatch.py` was written to fix, reintroduced from the
        # other end. Caught by looking at the running game, not by any test here.
        patch_src = os.path.join(out, PATCH)
        if not os.path.exists(patch_src):
            patch_src = os.path.join(ported, PATCH)
        print("  picPatch base: %s" % patch_src)
        patch, rect, kept = build_picpatch(patch_src, board, F)
        patch.save(os.path.join(out, PATCH))
        print("  wrote %s  region x %d..%d y %d..%d, %d transparent px preserved"
              % (PATCH, rect[0], rect[2] - 1, rect[1], rect[3] - 1, kept))

        # --- the two interiors -------------------------------------------------
        for n in TITLE_VIEWS:
            src = os.path.join(out, n)
            if not os.path.exists(src):
                src = os.path.join(ported, n)
            im, (tx, ty, sx, sy) = build_title(src, F)
            im.save(os.path.join(out, n))
            print("  wrote %-20s %dx%d  '%s' at (%d,%d) glyph %dx%d"
                  % (n, im.width, im.height, TITLE_LINE, tx, ty, sx, sy))

    if not args.verify:
        print("\nuntouched, by construction - this script opens them read-only:")
        for d in ("png", "png4x", "png12x"):
            print("  assets/%s" % d)
        print("\n  $env:JONES_ART_SET = '%s'      to see it" % args.set)
        print("  Remove-Item Env:\\JONES_ART_SET  to go back to Sierra's")
        print("\nnow run:  python tools/find_employment_art.py --cels <celdir> --set %s"
              % args.set)


if __name__ == "__main__":
    main()
