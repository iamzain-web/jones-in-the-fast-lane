"""Regenerates the university building (board cel 9) as realistic art, then letters it.

WHY THIS IS A SEPARATE TOOL FROM gen_board.py
---------------------------------------------
`gen_board.py` regenerates the GROUND and stamps the thirteen buildings back as originals,
precisely so a diffusion model never sees their lettering and never gets the chance to
garble it. That is the right default and it stays the default. This tool is the one
exception the user asked for: the university is RENAMED, so its cel cannot be stamped back
unchanged, and something has to draw the new one.

It keeps the guarantee the same way gen_board.py does - by never letting the model near the
text:

  A. THE ARCHITECTURE IS GENERATED with the sign band left blank. The init paints a flat
     plate where the band goes and the negative prompt forbids text outright.
  B. THE BAND AND THE LETTERING ARE COMPOSITED afterwards, deterministically, from a 3x5
     capital set at Sierra's own 3px cap width. Whatever the model did inside the band is
     overpainted, so the words cannot be garbled even if the model tried to write some.

  THE FOOTPRINT IS FIXED BY CONSTRUCTION. The output is a 59x41 cel stamped at (191,141),
  exactly as Sierra's is, so `Board.All`'s University rectangle (190,158)-(250,192) answers
  for the same pixels it always did. `--overlay` measures it rather than asserting it.

THE INIT BEATS THE PROMPT  (tools/samples/CAST.md section 0)
------------------------------------------------------------
Colour, position and extent live in the INIT; semantic category lives in the PROMPT. So the
init here is Sierra's own cel 9, upscaled - which hands the model the real stone grey, the
real roof brown, the real ivy green and the real doorway position - with only the sign band
repainted. The prompt says the one thing the init cannot: what KIND of building this is.

Both prompts are checked against CLIP's 77 tokens at runtime, positive AND negative. The
cast work lost a round to a 142-token negative whose second half was silently discarded.

THE LAYOUT, AND WHY THE TOWERS MOVE
-----------------------------------
Measured off the original: the frieze between its two corner towers is x 14..45, y 14..21 -
32 x 8 px, 30 x 6 usable. `OPEN DOOR` over `UNIVERSITY` needs 35 x 11. It does not fit, and
neither does any other form of the full name; only a monogram does. So the new building
spends its width on a full-width sign band at y 11..24 instead of two full-height towers,
which gives 53 x 14. The tower ROOFS stay (y 0..10) and the colonnade, doorway, ivy and
shrubs stay (y 25..40), so it reads as the same collegiate building.

USAGE
    python tools/decode_pic_full.py assets/raw/pic/11.pic out.png --cels <celdir> --alpha
    python tools/gen_university.py --cels <celdir> --out tools/samples/university-rename
    python tools/gen_university.py --cels <celdir> --out <dir> --dry     # no GPU, init only
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
                     "jones-upscale-models")
MODELS = os.path.join(CACHE, "gen")

# --- the cel, and where it lives -----------------------------------------------
CEL_W, CEL_H = 59, 41
CEL_X, CEL_Y = 191, 141
RECT = (190, 158, 250, 192)            # Board.All's University rectangle
SCALE = 16                             # 59x41 -> 944x656, both divisible by 8
GEN_W, GEN_H = CEL_W * SCALE, CEL_H * SCALE

# --- the new sign band, in cel coordinates -------------------------------------
BAND_X0, BAND_X1, BAND_Y0, BAND_Y1 = 3, 55, 11, 24
BAND_W, BAND_H = BAND_X1 - BAND_X0 + 1, BAND_Y1 - BAND_Y0 + 1      # 53 x 14

LINES = ["OPEN DOOR", "UNIVERSITY"]

# Sierra's own lettering gold off cel 9, so nothing here is a new colour.
GOLD, GOLD_HI = (0xE0, 0x80, 0x40), (0xE0, 0xB8, 0x68)
PLATE, PLATE_LO = (0x58, 0x40, 0x40), (0x40, 0x30, 0x30)

# THE PROMPT NAMES A KIND OF BUILDING, NOT THIS BUILDING'S ARTWORK. Nothing here names
# Sierra or the original's palette; the composition arrives as an init, because it is
# functional, and the colours arrive with it.
PROMPT = ("a photograph of an old collegiate university building, weathered stone and "
          "brick walls, tall sash windows with glass, climbing ivy, slate roof, stone "
          "towers, arched wooden door, a large blank sign board across the front, leafy "
          "green shrubs, overcast daylight, sharp focus, architectural photography")

# `text` and its synonyms are FIRST, because this is the clause that has to survive: the
# band is overpainted afterwards regardless, but a model that writes words also distorts
# the architecture around them.
NEGATIVE = ("text, letters, words, writing, lettering, signage, numbers, logo, watermark, "
            "caption, blurry, pixelated, lowres, jpeg artifacts, noise, people, faces, "
            "cars, cartoon, flat, distorted, deformed, frame, border, vignette")

# --- a 3x5 condensed capital set, at Sierra's own 3px cap width -----------------
G = {
    "A": ["111", "101", "111", "101", "101"], "B": ["110", "101", "110", "101", "110"],
    "C": ["111", "100", "100", "100", "111"], "D": ["110", "101", "101", "101", "110"],
    "E": ["111", "100", "111", "100", "111"], "F": ["111", "100", "111", "100", "100"],
    "G": ["111", "100", "101", "101", "111"], "H": ["101", "101", "111", "101", "101"],
    "I": ["1", "1", "1", "1", "1"],           "J": ["001", "001", "001", "101", "111"],
    "K": ["101", "101", "110", "101", "101"], "L": ["100", "100", "100", "100", "111"],
    "M": ["101", "111", "111", "101", "101"], "N": ["101", "111", "111", "111", "101"],
    "O": ["111", "101", "101", "101", "111"], "P": ["111", "101", "111", "100", "100"],
    "Q": ["111", "101", "101", "111", "011"], "R": ["111", "101", "111", "110", "101"],
    "S": ["111", "100", "111", "001", "111"], "T": ["111", "010", "010", "010", "010"],
    "U": ["101", "101", "101", "101", "111"], "V": ["101", "101", "101", "101", "010"],
    "W": ["101", "101", "111", "111", "101"], "X": ["101", "101", "010", "101", "101"],
    "Y": ["101", "101", "111", "010", "010"], "Z": ["111", "001", "010", "100", "111"],
    ".": ["0", "0", "0", "0", "1"], "-": ["00", "00", "11", "00", "00"],
    " ": ["0", "0", "0", "0", "0"],
}
GAP = 1


def tw(s):
    return 0 if not s else sum(len(G[c][0]) for c in s) + GAP * (len(s) - 1)


def stamp(img, s, x, y, fg, hi):
    px = img.load()
    cx = x
    for ch in s:
        gl = G[ch]
        for ry, bits in enumerate(gl):
            for rx, b in enumerate(bits):
                if b == "1" and 0 <= cx + rx < img.width and 0 <= y + ry < img.height:
                    px[cx + rx, y + ry] = hi if ry == 0 else fg
        cx += len(gl[0]) + GAP


# ------------------------------------------------------------------------------


def find_cel9(celdir):
    for n in os.listdir(celdir):
        if n.startswith("cel_09_"):
            return os.path.join(celdir, n)
    raise SystemExit(f"cel_09_* not found in {celdir}; run decode_pic_full.py --cels")


def build_init(cel9):
    """Sierra's cel with the sign band painted blank, upscaled to generation size.

    The band is painted FLAT and DARK so the model reads it as one object - a board - and
    renders lighting and a surround for it, rather than continuing the colonnade through
    it. What it actually paints inside is irrelevant: it is overpainted in `letter()`.

    THIS INIT IS THE WRONG TOOL FOR A REALISTIC BUILDING and is kept only for comparison.
    Denoising FROM the original pixel art returns the original pixel art with its edges
    smoothed off, at every strength that still preserves the layout - measured twice, at
    0.55/0.85 and at 0.78/0.45. The init is the strongest signal in the system
    (tools/samples/CAST.md section 0), and an init made of flat 1990 cels is an instruction
    to paint flat 1990 cels. Use `--synthetic`."""
    a = np.asarray(cel9.convert("RGB")).astype(np.uint8).copy()
    for y in range(BAND_Y0, BAND_Y1 + 1):
        for x in range(BAND_X0, BAND_X1 + 1):
            edge = y in (BAND_Y0, BAND_Y1) or x in (BAND_X0, BAND_X1)
            a[y, x] = PLATE_LO if edge else PLATE
    small = Image.fromarray(a, "RGB")
    # LANCZOS, not NEAREST: the model denoises this directly and hard 16px squares read as
    # an object boundary at every pixel edge.
    return small, small.resize((GEN_W, GEN_H), Image.LANCZOS)


# --- the synthetic init ---------------------------------------------------------
# Flat colour masses and nothing else: roof, wall, band, windows, door, ivy, shrubs, sky,
# at the ORIGINAL's positions and in colours sampled from it, then blurred until no edge
# is hard. It carries colour, position and extent - the three things the init decides -
# and carries NO texture, so every texture in the output is one the model invented. That
# is the whole difference between a realistic building and a smoothed cel.
SKY = (0xDC, 0xDC, 0xD0)
ROOF = (0x48, 0x38, 0x2E)
WALL = (0x9C, 0x8A, 0x6E)
WIN = (0x1E, 0x16, 0x14)
DOOR = (0x3E, 0x2A, 0x16)
IVY = (0x2E, 0x7A, 0x46)
SHRUB = (0x4F, 0xA8, 0x3C)
PATH = (0xB8, 0xB8, 0xC0)


def synthetic_init():
    """A 59x41 block painting of the building, then blurred and upscaled."""
    a = np.zeros((CEL_H, CEL_W, 3), np.uint8)
    a[:, :] = SKY

    def box(x0, y0, x1, y1, c):
        a[max(0, y0):y1 + 1, max(0, x0):x1 + 1] = c

    # the two towers, kept: this has to read as the same collegiate building
    box(1, 1, 15, 24, ROOF)
    box(43, 1, 57, 24, ROOF)
    # a gable notch so the towers are not plain slabs
    box(1, 1, 4, 4, SKY)
    box(12, 1, 15, 4, SKY)
    box(43, 1, 46, 4, SKY)
    box(54, 1, 57, 4, SKY)

    # the facade below the band
    box(0, 25, 58, 40, WALL)
    # the sign band, flat, full width
    box(BAND_X0, BAND_Y0, BAND_X1, BAND_Y1, PLATE)
    box(BAND_X0, BAND_Y0, BAND_X1, BAND_Y0, PLATE_LO)
    box(BAND_X0, BAND_Y1, BAND_X1, BAND_Y1, PLATE_LO)

    # windows, two runs either side of the door
    for x in (6, 13, 39, 46):
        box(x, 27, x + 3, 36, WIN)
    # the arched doorway and its steps
    box(25, 27, 33, 40, DOOR)
    box(27, 37, 31, 40, PATH)
    # Ivy climbing the facade. TWO pixels wide, not one: at one pixel the Gaussian at
    # GEN_W/60 erases it entirely and the model returns a building with no ivy on it at
    # all, however plainly the prompt asks for ivy. Extent is the init's job and a mass
    # too small to survive the blur is a mass that was never in the init.
    for x in (10, 20, 36, 46):
        box(x, 26, x + 1, 38, IVY)
    # shrubs along the front only, not up the wall
    for x in range(0, 59, 9):
        box(x, 38, x + 5, 40, SHRUB)
    box(27, 37, 31, 40, PATH)

    small = Image.fromarray(a, "RGB")
    big = small.resize((GEN_W, GEN_H), Image.LANCZOS)
    # HEAVILY blurred: no hard edge anywhere, so nothing in the output is a traced outline.
    return small, big.filter(ImageFilter.GaussianBlur(GEN_W / 60.0))


def soft_edges(im):
    """A soft-edge conditioning map from the image's own gradient. Same construction as
    gen_board.soft_edges - a Sobel magnitude, gamma'd and blurred, which is the same KIND
    of picture an HED detector makes and needs no further model."""
    g = np.asarray(im.convert("L"), np.float32)
    gx = np.zeros_like(g)
    gy = np.zeros_like(g)
    gx[:, 1:-1] = g[:, 2:] - g[:, :-2]
    gy[1:-1, :] = g[2:, :] - g[:-2, :]
    mag = np.hypot(gx, gy)
    mag = np.clip(mag / max(1.0, np.percentile(mag, 99.0)), 0.0, 1.0) ** 0.7
    e = Image.fromarray((mag * 255.0).astype(np.uint8), "L").filter(
        ImageFilter.GaussianBlur(0.8))
    return Image.merge("RGB", (e, e, e))


def generate(args, init_big, cond):
    import torch
    import gen_walkers as gw

    device = "cuda" if torch.cuda.is_available() else "cpu"
    # Attention slicing ON: this card is 4GB and the measured ceiling is about 810k pixels
    # WITH slicing. 944x656 is 619k, so it fits either way, but 768x1536 crashes the driver
    # outright with 0xC000070A rather than degrading, and a crash costs a whole round.
    txt, img = gw.build(device, offload=True, slicing=True,
                        controlnet=os.path.join(MODELS, "cn-softedge"))

    # CLIP TAKES 77 TOKENS AND DROPS THE REST SILENTLY - for the NEGATIVE exactly as for
    # the positive. The cast work lost a round to a 142-token negative whose second half
    # never reached the model. Check both, here, where it can still be fixed.
    for what, s in (("positive", PROMPT), ("negative", NEGATIVE)):
        n = len(txt.tokenizer(s).input_ids)
        if n > txt.tokenizer.model_max_length:
            raise SystemExit(f"{what} prompt is {n} tokens, over CLIP's "
                             f"{txt.tokenizer.model_max_length}:\n  {s}")
        print(f"  {what} prompt: {n}/{txt.tokenizer.model_max_length} tokens")

    g = torch.Generator(device).manual_seed(args.seed)
    out = img(prompt=PROMPT, negative_prompt=NEGATIVE, image=init_big, control_image=cond,
              strength=args.strength,
              num_inference_steps=max(args.steps, int(args.steps / args.strength)),
              guidance_scale=args.cfg, controlnet_conditioning_scale=args.control,
              generator=g).images[0]
    return out


def letter(cel):
    """Overpaint the sign band and stamp the name. DETERMINISTIC - no model involved.

    The plate colour is sampled from what the generator actually painted there, so the
    board matches the new building's palette rather than the old one's, but its geometry
    and its text are drawn here and cannot be garbled."""
    a = np.asarray(cel.convert("RGB")).astype(int).copy()
    region = a[BAND_Y0:BAND_Y1 + 1, BAND_X0:BAND_X1 + 1].reshape(-1, 3)
    med = np.median(region, axis=0)
    # Darken a little so the gold reads against it, and keep it in the generated palette.
    body = tuple(int(max(0, min(255, c * 0.62))) for c in med)
    edge = tuple(int(max(0, min(255, c * 0.40))) for c in med)
    for y in range(BAND_Y0, BAND_Y1 + 1):
        for x in range(BAND_X0, BAND_X1 + 1):
            a[y, x] = edge if (y in (BAND_Y0, BAND_Y1) or x in (BAND_X0, BAND_X1)) else body
    img = Image.fromarray(a.astype(np.uint8), "RGB")

    th = len(LINES) * 5 + (len(LINES) - 1)
    ty = BAND_Y0 + (BAND_H - th) // 2
    for i, ln in enumerate(LINES):
        stamp(img, ln, BAND_X0 + (BAND_W - tw(ln)) // 2, ty + i * 6, GOLD, GOLD_HI)
    return img, body, edge


def coverage(cel_x, cel_y, cel_w, cel_h, rect):
    l, t, r, b = rect
    rw, rh = r - l + 1, b - t + 1
    ox0, ox1 = max(l, cel_x), min(r, cel_x + cel_w - 1)
    oy0, oy1 = max(t, cel_y), min(b, cel_y + cel_h - 1)
    if ox1 < ox0 or oy1 < oy0:
        return 0, 0, 0.0
    ow, oh = ox1 - ox0 + 1, oy1 - oy0 + 1
    return ow * oh, rw * rh, 100.0 * ow * oh / (rw * rh)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cels", required=True, help="dir from decode_pic_full.py --cels")
    ap.add_argument("--out", required=True)
    ap.add_argument("--board", default=None, help="decoded pic 11 png, for the composite")
    ap.add_argument("--strength", type=float, default=0.55)
    ap.add_argument("--steps", type=int, default=28)
    ap.add_argument("--cfg", type=float, default=7.5)
    ap.add_argument("--control", type=float, default=0.85)
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--dry", action="store_true", help="init only, no GPU")
    ap.add_argument("--synthetic", action="store_true",
                    help="denoise from a block painting instead of from Sierra's cel - the "
                         "only one of the two that produces a realistic building")
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)
    cel9 = Image.open(find_cel9(args.cels)).convert("RGB")
    if cel9.size != (CEL_W, CEL_H):
        raise SystemExit(f"cel 9 is {cel9.size}, expected {(CEL_W, CEL_H)}")

    init_small, init_big = synthetic_init() if args.synthetic else build_init(cel9)
    print("init: %s" % ("synthetic block painting" if args.synthetic else "Sierra's cel"))
    init_small.save(os.path.join(args.out, "init_59x41.png"))
    init_big.save(os.path.join(args.out, "init_%dx%d.png" % (GEN_W, GEN_H)))
    cond = soft_edges(init_big)
    cond.save(os.path.join(args.out, "cond_softedge.png"))
    print("band %d x %d px at x %d..%d, y %d..%d"
          % (BAND_W, BAND_H, BAND_X0, BAND_X1, BAND_Y0, BAND_Y1))
    for ln in LINES:
        print("  %-12s %2d px of %d usable" % (ln, tw(ln), BAND_W - 2))

    if args.dry:
        print("--dry: init written, no GPU used")
        return

    print("generating at %dx%d, strength %.2f, control %.2f, seed %d"
          % (GEN_W, GEN_H, args.strength, args.control, args.seed))
    big = generate(args, init_big, cond)
    big.save(os.path.join(args.out, "generated_%dx%d.png" % (GEN_W, GEN_H)))

    # BOX, not LANCZOS: 944x656 -> 59x41 is exactly 16:1, so BOX is the mean of each 16x16
    # block - supersampling, with no ringing and no invented high frequencies.
    cel = big.resize((CEL_W, CEL_H), Image.BOX)
    cel.save(os.path.join(args.out, "building_unlettered_59x41.png"))
    final, body, edge = letter(cel)
    final.save(os.path.join(args.out, "building_59x41.png"))
    print("plate sampled from the generated art: body #%02X%02X%02X edge #%02X%02X%02X"
          % (*body, *edge))

    px, tot, pct = coverage(CEL_X, CEL_Y, CEL_W, CEL_H, RECT)
    print("footprint: cel %dx%d at (%d,%d); rect %s = %d px; covered %d px = %.1f%%"
          % (CEL_W, CEL_H, CEL_X, CEL_Y, RECT, tot, px, pct))

    board = args.board or os.path.join(args.out, "pic11.png")
    if os.path.exists(board):
        b = Image.open(board).convert("RGB")
        b.paste(final, (CEL_X, CEL_Y))
        b.save(os.path.join(args.out, "board_with_new_university.png"))
        print("wrote board_with_new_university.png")


if __name__ == "__main__":
    main()
