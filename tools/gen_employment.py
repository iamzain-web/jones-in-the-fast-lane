"""Regenerates the Employment Office (board cel 10) as realistic art, then letters it
`HIRE GROUND`.

WHY THIS IS A SEPARATE TOOL FROM gen_board.py
---------------------------------------------
`gen_board.py` regenerates the GROUND and stamps the thirteen buildings back as originals,
precisely so a diffusion model never sees their lettering and never gets the chance to
garble it. That is the right default and it stays the default. This tool is the second
exception the user asked for, after `gen_university.py`: this building is RENAMED, so its
cel cannot be stamped back unchanged, and something has to draw the new one.

It keeps the guarantee the same way gen_university.py does - by never letting the model
near the text:

  A. THE ARCHITECTURE IS GENERATED with the sign board left blank. The init paints a flat
     red plate where the board goes and the negative prompt forbids text outright.
  B. THE BOARD AND THE LETTERING ARE COMPOSITED afterwards, deterministically, from the
     same 3x5 capital set gen_university.py uses, at Sierra's own 3px cap width. Whatever
     the model did inside the plate is overpainted, so the words cannot be garbled even if
     the model tried to write some.

  THE FOOTPRINT IS FIXED BY CONSTRUCTION. The output is a 47x34 cel stamped at (75,151),
  exactly as Sierra's is, so `Board.All`'s Employment Office rectangle (68,158)-(128,192)
  answers for the same pixels it always did.

THE INIT BEATS THE PROMPT  (tools/samples/CAST.md section 0)
------------------------------------------------------------
Colour, position and extent live in the INIT; semantic category lives in the PROMPT. And
the init must be a SYNTHETIC BLOCK PAINTING, not Sierra's cel upscaled: the university work
measured that twice, at strength/control 0.55/0.85 and 0.78/0.45, and denoising FROM 1990
pixel art returns 1990 pixel art with its edges smoothed off at every strength that still
preserves the layout. There is no `--sierra` path here because that experiment is finished;
the answer is written down instead.

Every colour below is SAMPLED FROM SIERRA'S OWN CEL 10, so the init carries the real
peach stucco, the real red board, the real louvre black and the real planter green. It
carries NO texture, so every texture in the output is one the model invented.

Both prompts are checked against CLIP's 77 tokens at runtime, positive AND negative. The
cast work lost a round to a 142-token negative whose second half was silently discarded,
and a guard on only one of two identical inputs is worse than no guard at all.

THE LAYOUT, AND WHY THE NAME FITS ON TWO LINES
-----------------------------------------------
Measured off the original by tracing the cel row by row: the red board is x 1..45, y 1..13,
so its interior is 43 x 11 px. Sierra spent it on `EMPLOYMENT` over `OFFICE`.

In the 3x5 set `HIRE` is 13px and `GROUND` is 23px, and two lines of 5 rows with a 1-row
gap is 11 rows - which is EXACTLY the interior height. So the new name takes Sierra's own
two-line composition at her own cap height, with room to spare on both lines. (One line of
`HIRE GROUND` is 39px and would also fit the 43px width, but it would use 5 of the 11 rows
and leave the board looking half empty; the original's rhythm is two lines.)

USAGE
    python tools/decode_pic_full.py assets/raw/pic/11.pic out.png --cels <celdir> --alpha
    python tools/gen_employment.py --cels <celdir> --out tools/samples/employment-rename
    python tools/gen_employment.py --cels <celdir> --out <dir> --dry    # no GPU, init only
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
CEL_W, CEL_H = 47, 34
CEL_X, CEL_Y = 75, 151
RECT = (68, 158, 128, 192)             # Board.All's Employment Office rectangle
SCALE = 16                             # 47x34 -> 752x544, both divisible by 8
GEN_W, GEN_H = CEL_W * SCALE, CEL_H * SCALE

# GEN_W/60 - the university's own formula, verbatim, and NOT a tidied-up version of it.
#
# This was first written as `SCALE * 0.98`, reasoning that the university's 15.73px against
# a 16x scale is "about one cel pixel" and that one cel pixel is the meaningful unit. THAT
# IS THE WRONG UNIT AND IT COST A ROUND. The diffusion model sees a canvas, not cels: the
# university blurs 1.67% of its canvas width and `SCALE * 0.98` blurs 2.09% of this one,
# because this canvas is smaller. The first generation came back as a soft depth-of-field
# 3D render rather than a photograph, and an init that is blurrier relative to its frame
# is an init that says "out of focus".
#
# CAST.md section 0 mode 3 still holds - a mass thinner than the blur is not in the init at
# all - and nothing below is drawn thinner than 2px, which at 12.5px of blur is still
# comfortably above the threshold. The louvre slats were checked in the composited init.
BLUR = GEN_W / 60.0

# --- the sign board, in cel coordinates -----------------------------------------
# MEASURED, not chosen: the plate's outer edge runs x 1..45, y 1..13, with Sierra's own
# #E03838 highlight on the top and left and #682028 shadow on the bottom and right.
BAND_X0, BAND_X1, BAND_Y0, BAND_Y1 = 1, 45, 1, 13
BAND_W, BAND_H = BAND_X1 - BAND_X0 + 1, BAND_Y1 - BAND_Y0 + 1      # 45 x 13

LINES = ["HIRE", "GROUND"]

# Sierra's own sign palette off cel 10. The lettering is pale blue-white, not gold - this
# building is not the university and does not borrow its colours.
LETTER, LETTER_HI = (0xD0, 0xD8, 0xE0), (0xE0, 0xF0, 0xF8)
PLATE, PLATE_HI, PLATE_LO = (0xC0, 0x38, 0x38), (0xE0, 0x38, 0x38), (0x68, 0x20, 0x28)

# THE PROMPT NAMES A KIND OF BUILDING, NOT THIS BUILDING'S ARTWORK. Nothing here names
# Sierra or the original's palette; the composition arrives as an init, because it is
# functional, and the colours arrive with it.
#
# EVERY COLOUR WORD BELOW WAS CHECKED AGAINST SIERRA'S OWN PIXELS before it was written,
# because the hand-typed description is the one input nobody measures - three garments in
# the cast came out wrong from colour words typed against slots that measured something
# else. The census of cel 10, as fractions of the cel:
#
#     peach stucco wall   #F8A068   6.9%      red sign board   #C03838  16.2%
#     glass door          #D0D8E0  10.3%      louvre black     #282828   3.6%
#     planter box         #202830   1.0%      shrub green      #205840   1.3%
#     forecourt           #285068   5.3%
#
# `concrete planters` was in the first draft of this line and is struck: the planters
# measure #202830, near black, and naming a pale grey material against near-black pixels
# is precisely the mistake being avoided. They are described as dark boxes instead.
#
# NOT ONE COLOUR WORD, AND THAT IS THE POINT OF THE THIRD REVISION.
#
# This line used to read "peach stucco walls ... dark louvered windows ... green shrubs in
# dark planter boxes ... a large blank RED sign board", and it produced a building that was
# tidier than Sierra's and every bit as FLAT. The colours were not wrong - they were
# measured off her cel - they were simply being said in the wrong place. CAST.md section 0:
# COLOUR, POSITION AND EXTENT LIVE IN THE INIT. The init already says peach, already says
# red, already says green, in the right places and at the right sizes. Every colour word
# here was therefore doing nothing except spending budget that the only thing the prompt
# CAN say needed: the material.
#
# Compare the university's, which is the same pipeline getting a photograph: "weathered
# stone and brick walls, tall sash windows WITH GLASS, slate roof, arched WOODEN door,
# climbing ivy". Those are texture-bearing nouns. Flat is the cheaper answer to "an office
# front" and the model will take it unless something insists on a surface.
#
# So the whole budget now goes on MATERIAL, LIGHT and SURFACE, and the init is left to say
# what colour those materials are.
PROMPT = ("a photograph of a small shopfront office, weathered painted brick and rough "
          "render, raking sunlight and cast shadows across the facade, aluminium framed "
          "glass doors with reflections, slatted metal louvre shutters, a large blank sign "
          "board above, leafy plants with individual leaves, sharp focus, fine detail, "
          "architectural photography")

# `text` and its synonyms are FIRST, because this is the clause that has to survive: the
# board is overpainted afterwards regardless, but a model that writes words also distorts
# the architecture around them.
#
# `3d render, cgi, depth of field, bokeh, miniature` are NOT padding and were added in
# response to a measured failure. The first generation came back as a soft, plasticky
# architectural VISUALISATION - correct massing, real mullions, but plainly a render with
# a tilt-shift blur across it. The university's negative, which this started as, forbids
# `blurry` and `cartoon` and nothing else in that family, because the university never
# produced a render and so never needed them.
#
# Photograph versus 3D render is a SEMANTIC CATEGORY, and CAST.md section 0 is explicit
# that a category is the one thing the init cannot argue and only the prompt can settle.
# So this belongs here rather than in the init - the init change beside it (the blur) fixes
# a different defect, the softness, and the two were changed together deliberately.
#
# `flat` IS LOAD-BEARING AND WAS ONCE DELETED FROM HERE BY ACCIDENT. Round 2 added the
# render terms above by making room for them, and the word that made room was `flat` - the
# university's negative reads `cartoon, flat, distorted` and this one briefly read
# `cartoon, distorted`. The result came back as a FLAT VECTOR ILLUSTRATION: no texture
# anywhere, pure fills, clip art. One word, one round. Anything added to this string has to
# fit in the budget WITHOUT evicting a term that is already earning its place, which is why
# the token count is printed on every run and not merely asserted.
#
# Trimmed instead, all genuinely redundant: `caption` (covered by text/words), `faces`
# (covered by people), `miniature` and `depth of field` (covered by bokeh/out of focus,
# and the real fix for softness was the init's blur, not these).
NEGATIVE = ("text, letters, words, writing, lettering, signage, numbers, logo, watermark, "
            "blurry, out of focus, bokeh, 3d render, cgi, vector, illustration, flat, "
            "cartoon, pixelated, lowres, jpeg artifacts, noise, people, cars, distorted, "
            "deformed, frame, border, vignette")

# --- the 3x5 condensed capital set, at Sierra's own 3px cap width ----------------
# Identical to gen_university.py's, deliberately: the two renamed buildings letter from
# one alphabet so they read as the same hand.
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
    return cx


# ------------------------------------------------------------------------------


def find_cel10(celdir):
    for n in os.listdir(celdir):
        if n.startswith("cel_10_"):
            return os.path.join(celdir, n)
    raise SystemExit(f"cel_10_* not found in {celdir}; run decode_pic_full.py --cels")


# --- the synthetic init ---------------------------------------------------------
# Flat colour masses and nothing else, at the ORIGINAL's positions and in colours sampled
# from it, then blurred until no edge is hard. Every band below was read off the row map
# of cel 10, not guessed.
BACK = (0xF8, 0xF8, 0xE0)      # the board's own pale ground, above the sign
ROOF = (0x80, 0x60, 0x48)      # cel row 14, the roof edge
FASCIA = (0xE0, 0x80, 0x40)    # cel row 15, the awning lip
WALL = (0xF8, 0xA0, 0x68)      # the peach stucco, 6.9% of the cel
WIN = (0x20, 0x20, 0x20)       # the louvered window blocks
LOUVRE = (0x78, 0x60, 0x58)    # the slats inside them
DOOR = (0xD0, 0xD8, 0xE0)      # the glass double door
FRAME = (0x78, 0x60, 0x58)     # its surround
SHRUB = (0x58, 0xA0, 0x60)     # the planter greenery
SHRUB_LO = (0x20, 0x58, 0x40)
PLANTER = (0x20, 0x28, 0x30)   # the dark boxes they sit in
GROUND = (0x28, 0x50, 0x68)    # the tarmac forecourt
WATER = (0xA0, 0xE8, 0xF8)     # the river at the bottom corners


def synthetic_init():
    """A 47x34 block painting of the building, then blurred and upscaled."""
    a = np.zeros((CEL_H, CEL_W, 3), np.uint8)
    a[:, :] = BACK

    def box(x0, y0, x1, y1, c):
        a[max(0, y0):y1 + 1, max(0, x0):x1 + 1] = c

    # --- the sign board, flat: whatever the model paints inside is overpainted -----
    box(BAND_X0, BAND_Y0, BAND_X1, BAND_Y1, PLATE)
    box(BAND_X0, BAND_Y0, BAND_X1, BAND_Y0, PLATE_HI)
    box(BAND_X0, BAND_Y0, BAND_X0, BAND_Y1, PLATE_HI)
    box(BAND_X0, BAND_Y1, BAND_X1, BAND_Y1, PLATE_LO)
    box(BAND_X1, BAND_Y0, BAND_X1, BAND_Y1, PLATE_LO)

    # --- the building below it ----------------------------------------------------
    box(1, 14, 45, 15, ROOF)             # the flat roof's edge, 2px so it survives BLUR
    box(1, 15, 45, 16, FASCIA)           # the awning lip under it
    box(2, 16, 44, 31, WALL)             # the stucco face

    # Two louvered windows. Drawn as solid dark blocks with 2px slats: a 1px slat is
    # thinner than BLUR and would not be in the init at all (CAST.md section 0, mode 3).
    for x0 in (6, 33):
        box(x0, 17, x0 + 7, 24, WIN)
        for y in (18, 21, 24):
            box(x0, y, x0 + 7, y + 1, LOUVRE)

    # The glass double door, centred, with a frame - it is the brightest mass on the
    # facade in the original and the one feature that has to survive.
    box(16, 18, 29, 31, FRAME)
    box(17, 19, 28, 31, DOOR)

    # Planters along the front, either side of the door.
    for x0, x1 in ((2, 15), (30, 44)):
        box(x0, 26, x1, 28, SHRUB)
        box(x0, 28, x1, 29, SHRUB_LO)
        box(x0, 29, x1, 31, PLANTER)

    # The forecourt, and the river at the two bottom corners.
    box(0, 31, 46, 33, GROUND)
    box(0, 30, 2, 33, WATER)
    box(44, 30, 46, 33, WATER)

    small = Image.fromarray(a, "RGB")
    big = small.resize((GEN_W, GEN_H), Image.LANCZOS)
    # HEAVILY blurred: no hard edge anywhere, so nothing in the output is a traced outline.
    return small, big.filter(ImageFilter.GaussianBlur(BLUR))


def soft_edges(im):
    """A soft-edge conditioning map from the image's own gradient. Same construction as
    gen_board.soft_edges and gen_university.soft_edges - a Sobel magnitude, gamma'd and
    blurred, which is the same KIND of picture an HED detector makes and needs no model."""
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


def claim_card(out):
    """Refuse to start while anything else is holding the card.

    `gen_cast.claim_gpu` is taken as well, but IT IS NOT ENOUGH ON ITS OWN and that is
    worth stating plainly: it writes its pid file into the RUN'S OWN OUTPUT DIRECTORY, so
    two different tools writing to two different directories never see each other's lock.
    It protects a tool from itself, not the card from two tools. Believing otherwise is how
    two jobs end up on a 4GB card.

    So the real resource is checked directly: any OTHER process holding GPU memory, as
    nvidia-smi reports it, is grounds to refuse. That cannot be fooled by a lock file in a
    directory nobody else looks at."""
    import subprocess
    try:
        r = subprocess.run(
            ["nvidia-smi", "--query-compute-apps=pid,process_name,used_memory",
             "--format=csv,noheader"],
            capture_output=True, text=True, timeout=30)
        rows = [ln.strip() for ln in r.stdout.splitlines() if ln.strip()]
    except (OSError, subprocess.SubprocessError):
        rows = []                      # no nvidia-smi: fall back to the pid file alone

    # ONLY ANOTHER PYTHON COUNTS. The first version of this guard refused on any compute
    # process at all and was therefore useless on this machine: `dwm` (the desktop
    # compositor), Teams and the Edge WebView are permanently on the card, so it would
    # have refused every run for ever. The thing being guarded against is a second
    # DIFFUSION job, and that is a python.
    mine = os.getpid()
    others = []
    for ln in rows:
        parts = [p.strip() for p in ln.split(",")]
        if len(parts) < 2 or not parts[0].isdigit() or int(parts[0]) == mine:
            continue
        if "python" in os.path.basename(parts[1]).lower():
            others.append(ln)
    if others:
        raise SystemExit(
            "another python is already on the card - refusing to start a second job "
            "on 4GB:\n  " + "\n  ".join(others)
            + "\nwait for it to finish, then run this again.")

    try:
        import gen_cast
        gen_cast.claim_gpu(out)
    except ImportError:
        pass


def generate(args, init_big, cond):
    import torch
    import gen_walkers as gw

    device = "cuda" if torch.cuda.is_available() else "cpu"
    # Attention slicing ON: this card is 4GB and the measured ceiling is about 810k pixels
    # WITH slicing. 752x544 is 409k, so it fits comfortably, but the failure mode past the
    # ceiling is a driver crash (0xC000070A), not a slowdown, and a crash costs a round.
    txt, img = gw.build(device, offload=True, slicing=True,
                        controlnet=os.path.join(MODELS, "cn-softedge"))

    # CLIP TAKES 77 TOKENS AND DROPS THE REST SILENTLY - for the NEGATIVE exactly as for
    # the positive. Check both, here, where it can still be fixed.
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


# THE BUILDING'S OWN FOOTPRINT INSIDE THE CEL, and everything outside it is Sierra's.
#
# Her cel is 47x34 and fully opaque, but the building does not fill it: she painted the
# TREES AND THE WATER around it into the same cel, which is what makes it sit in the board
# rather than on it. The generated cel has a pale sky there instead, and pasted as a solid
# rectangle that reads as a cream HALO boxing the building in - plainly visible against the
# board's water on the first composite.
#
# So the generated pixels are kept only where the building actually is, and Sierra's are
# kept everywhere else. Measured off her row map: the sign plate runs x 1..45, y 1..13 and
# the body below it runs x 2..44, y 14..31, with row 0, the outer columns and rows 32..33
# all scenery. The plate is listed separately because it is one pixel wider than the body
# on each side, exactly as she drew it.
PLATE_RECT = (BAND_X0, BAND_Y0, BAND_X1, BAND_Y1)      # x 1..45, y 1..13
BODY_RECT = (2, 14, 44, 31)                            # x 2..44, y 14..31


def building_mask(w=CEL_W, h=CEL_H, scale=1):
    """True where the generated building belongs, False where Sierra's scenery does."""
    m = np.zeros((h * scale, w * scale), bool)
    for (x0, y0, x1, y1) in (PLATE_RECT, BODY_RECT):
        m[y0 * scale:(y1 + 1) * scale, x0 * scale:(x1 + 1) * scale] = True
    return m


def blend_surround(cel, sierra):
    """Sierra's cel with the generated building dropped into its footprint."""
    a = np.asarray(cel.convert("RGB")).astype(np.uint8).copy()
    s = np.asarray(sierra.convert("RGB")).astype(np.uint8)
    m = building_mask()
    out = np.where(m[..., None], a, s)
    return Image.fromarray(out.astype(np.uint8), "RGB")


def letter(cel):
    """Overpaint the sign board and stamp the name. DETERMINISTIC - no model involved.

    The plate colour is sampled from what the generator actually painted there, so the
    board matches the new building's palette rather than the old one's, but its geometry
    and its text are drawn here and cannot be garbled."""
    a = np.asarray(cel.convert("RGB")).astype(int).copy()
    region = a[BAND_Y0:BAND_Y1 + 1, BAND_X0:BAND_X1 + 1].reshape(-1, 3)
    med = np.median(region, axis=0)
    # Sierra's red, lit by the generation - see install_employment.build_building. The
    # prompt asks for brick, the model paints the plate as brickwork, and sampling it
    # outright turns the sign brown.
    lum = float(np.clip(med.mean() / 0x6E, 0.80, 1.20))
    body = tuple(int(max(0, min(255, c * lum))) for c in PLATE)
    hi = tuple(int(max(0, min(255, c * lum * 1.16))) for c in PLATE_HI)
    lo = tuple(int(max(0, min(255, c * lum))) for c in PLATE_LO)
    for y in range(BAND_Y0, BAND_Y1 + 1):
        for x in range(BAND_X0, BAND_X1 + 1):
            if y == BAND_Y1 or x == BAND_X1:
                a[y, x] = lo
            elif y == BAND_Y0 or x == BAND_X0:
                a[y, x] = hi
            else:
                a[y, x] = body
    img = Image.fromarray(a.astype(np.uint8), "RGB")

    th = len(LINES) * 5 + (len(LINES) - 1)
    ty = BAND_Y0 + (BAND_H - th) // 2
    for i, ln in enumerate(LINES):
        stamp(img, ln, BAND_X0 + (BAND_W - tw(ln)) // 2, ty + i * 6, LETTER, LETTER_HI)
    return img, body, lo


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
    # The four parameters the university settled on after three failed rounds. They are
    # defaults here rather than something to rediscover.
    # 0.88, the university's own figure, and it was briefly raised to 0.92 and put back.
    # At 0.92 the init stops holding IDENTITY: the two louvered windows came back as green
    # shuttered doors and the planters disappeared into a plinth. CAST.md section 0 says
    # extent and colour survive but identity does not, and 0.04 of strength is the
    # difference between a window staying a window and becoming the likeliest other thing
    # of that size and colour. The softness that prompted the raise was the BLUR, not this.
    ap.add_argument("--strength", type=float, default=0.88)
    ap.add_argument("--steps", type=int, default=32)
    # 9.0, not the university's 7.5. The complaint this answers is precisely that the
    # PROMPT's category was losing to the init, and guidance scale is the one knob that
    # weights the prompt against everything else competing with it. Raised at the same time
    # as the prompt was rewritten for material, because both address the same defect from
    # the same side; the init was deliberately left alone, since its structure is correct.
    ap.add_argument("--cfg", type=float, default=9.0)
    ap.add_argument("--control", type=float, default=0.35)
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--dry", action="store_true", help="init only, no GPU")
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)
    cel10 = Image.open(find_cel10(args.cels)).convert("RGB")
    if cel10.size != (CEL_W, CEL_H):
        raise SystemExit(f"cel 10 is {cel10.size}, expected {(CEL_W, CEL_H)}")

    init_small, init_big = synthetic_init()
    init_small.save(os.path.join(args.out, "init_%dx%d.png" % (CEL_W, CEL_H)))
    init_big.save(os.path.join(args.out, "init_%dx%d.png" % (GEN_W, GEN_H)))
    cond = soft_edges(init_big)
    cond.save(os.path.join(args.out, "cond_softedge.png"))
    print("init: synthetic block painting, blur %.1f px = %.2f cel px"
          % (BLUR, BLUR / SCALE))
    print("sign board %d x %d px at x %d..%d, y %d..%d (interior %d x %d)"
          % (BAND_W, BAND_H, BAND_X0, BAND_X1, BAND_Y0, BAND_Y1, BAND_W - 2, BAND_H - 2))
    for ln in LINES:
        print("  %-8s %2d px of %d usable" % (ln, tw(ln), BAND_W - 2))
    print("  %d lines x 5 rows + %d gap = %d rows of %d usable"
          % (len(LINES), len(LINES) - 1, len(LINES) * 5 + len(LINES) - 1, BAND_H - 2))

    if args.dry:
        print("--dry: init written, no GPU used")
        return

    claim_card(args.out)
    print("generating at %dx%d, strength %.2f, control %.2f, steps %d, seed %d"
          % (GEN_W, GEN_H, args.strength, args.control, args.steps, args.seed))
    big = generate(args, init_big, cond)
    big.save(os.path.join(args.out, "generated_%dx%d.png" % (GEN_W, GEN_H)))

    # BOX, not LANCZOS: 752x544 -> 47x34 is exactly 16:1, so BOX is the mean of each 16x16
    # block - supersampling, with no ringing and no invented high frequencies.
    cel = big.resize((CEL_W, CEL_H), Image.BOX)
    cel.save(os.path.join(args.out, "building_unlettered_%dx%d.png" % (CEL_W, CEL_H)))
    cel = blend_surround(cel, cel10)
    cel.save(os.path.join(args.out, "building_blended_%dx%d.png" % (CEL_W, CEL_H)))
    final, body, lo = letter(cel)
    final.save(os.path.join(args.out, "building_%dx%d.png" % (CEL_W, CEL_H)))
    print("plate sampled from the generated art: body #%02X%02X%02X edge #%02X%02X%02X"
          % (*body, *lo))

    px, tot, pct = coverage(CEL_X, CEL_Y, CEL_W, CEL_H, RECT)
    print("footprint: cel %dx%d at (%d,%d); rect %s = %d px; covered %d px = %.1f%%"
          % (CEL_W, CEL_H, CEL_X, CEL_Y, RECT, tot, px, pct))

    board = args.board or os.path.join(args.out, "pic11.png")
    if os.path.exists(board):
        b = Image.open(board).convert("RGB")
        b.paste(final, (CEL_X, CEL_Y))
        b.save(os.path.join(args.out, "board_with_new_employment.png"))
        print("wrote board_with_new_employment.png")


if __name__ == "__main__":
    main()
