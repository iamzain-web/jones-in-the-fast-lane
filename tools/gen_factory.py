"""Regenerates the Factory as photoreal architecture, then hangs a new sign on it.

HOW THIS DIFFERS FROM gen_employment.py, AND WHY IT MATTERS
-----------------------------------------------------------
The Employment Office is a CEL: one 47x34 rectangle stamped on the board, sign and all.
The Factory is not. Its building is painted into pic 11's BACKGROUND (cel 0) and only its
hanging sign is a cel - cel 11, 32x13 at (9,176). So the two halves of this job are
independent edits rather than one:

    the building   a region of the background, regenerated and blended back
    the name       a new sign cel, stamped over it afterwards

That split is a gift, because it makes the band-blank-then-stamp guarantee structural: the
model is handed a region with NO SIGN IN IT AT ALL - the init paints the building carrying
on behind where the board hangs - and the sign is composited afterwards. There is no band
to leave blank because there is no band.

    THE OLD BOARD IS IN THE BACKGROUND TOO. `#E0B868` covers region-local x 2..33,
    y 23..36 in cel 0, under the cel. If the init reproduced it, the model would paint a
    blank hoarding and the new sign would sit on top of an old one. It does not.

THE NUMBERS, ALL MEASURED OFF cel 0 (tools/samples/FACTORY.md has the census)
-----------------------------------------------------------------------------
Region: game (7,155), 61x38 - the Factory hotspot exactly. At 16x that is 976x608 =
593k px, inside the ~810k ceiling this 4GB card was measured at, and both sides divide
by 8. The masses:

    brick building   x 19..33, y  0..24   #584030
    dark stack       x 34..48, y  0..12   #202830
    furnace block    x 37..48, y 13..25   #785838
    river            x  3..18 upper, x 33..60 lower   #B8C8E0
    ground           lavender             #6070C8 / #8080C8

WHAT WAS LEARNED ON THE PREVIOUS TWO BUILDINGS AND IS APPLIED HERE WITHOUT REDISCOVERY
---------------------------------------------------------------------------------------
1. NO COLOUR WORD IN THE POSITIVE PROMPT. Colour, position and extent live in the init
   (CAST.md section 0) and the init above says all of it, measured. A colour word in the
   prompt spends budget on something already said and starves the only thing the prompt
   can contribute, which is MATERIAL. Hire Ground came back flat three times with
   "peach stucco ... green shrubs ... red sign board" in its prompt and photographic the
   round that phrase was replaced with brick, render, raking light and reflections.
2. `flat` STAYS IN THE NEGATIVE, and room for new terms is taken from synonyms, never from
   a term that predates the problem being solved. Deleting `flat` to fit the anti-render
   clause cost a round and returned a vector illustration.
3. THE BLUR IS THE LITERAL CANVAS FRACTION, `GEN_W / 60.0`, not a tidied-up re-expression
   in cel units. The model sees a canvas; re-deriving this in cel pixels made the init
   blurrier relative to its frame and produced an out-of-focus render.
4. THE SURROUND IS SIERRA'S. Only the building's own footprint is taken from the
   generation; the river, the ground and the board around it stay hers, so the new
   building sits in the scene instead of in a pale box.

USAGE
    python tools/gen_factory.py --cels <celdir> --out tools/samples/factory-rename --dry
    python tools/gen_factory.py --cels <celdir> --out tools/samples/factory-rename
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

# --- the region, in game coordinates --------------------------------------------
#
# THE REGION IS THE BUILDING'S BOUNDING BOX, NOT THE HOTSPOT, AND THAT WAS THE FIX.
#
# This started as the whole Factory hotspot, (7,155) 61x38, generated at 976x608. Six
# rounds came back as a close-up of a brick wall rather than as a factory, and the reason
# was arithmetic rather than prompting: THE BUILDING WAS ONLY 35% OF THOSE PIXELS. The
# model spent its detail budget on a yard, a river bank and a horizon, every one of which
# `blend_surround` then threw away, and the small irregular piece that was kept had almost
# no structure in it.
#
# The Employment Office never had this problem because its cel IS its building - the whole
# 752x544 canvas was facade, so all of the detail landed somewhere visible. Cropping to the
# bounding box restores that property here. 32x27 at 16x is 512x432, which is also close to
# SD1.5's native 512x512 and the size it behaves best at.
#
# The lesson generalises to the remaining buildings: GENERATE THE BUILDING, NOT ITS
# HOTSPOT. The hotspot is a click target and has nothing to do with where the art is.
REG_X, REG_Y = 26, 155                 # the brick mass's own bounding box, measured
REG_W, REG_H = 32, 27
RECT = (7, 155, 67, 192)               # Board.All's Factory rectangle, for the record
SCALE = 16
GEN_W, GEN_H = REG_W * SCALE, REG_H * SCALE      # 512 x 432 = 221k px
BLUR = GEN_W / 60.0

# The whole region is now the building, so the footprint is the region.
BUILDING = (0, 0, REG_W - 1, REG_H - 1)

# --- colours, every one sampled from cel 0 ---------------------------------------
BRICK = (0x58, 0x40, 0x30)
BRICK_LO = (0x58, 0x40, 0x20)
STACK = (0x20, 0x28, 0x30)
FURNACE = (0x78, 0x58, 0x38)
WIN = (0x38, 0x28, 0x18)
WATER = (0xB8, 0xC8, 0xE0)       # Sierra's river   - restored by the blend, not generated
GROUND = (0x60, 0x70, 0xC8)      # Sierra's lavender - likewise
GROUND_HI = (0x80, 0x80, 0xC8)
SKY = (0xC0, 0xD0, 0xE0)
# The yard the MODEL is shown, which is not Sierra's palette - see synthetic_init.
#
# MID CONCRETE, NOT DARK ASPHALT, AND THAT IS THE SECOND CORRECTION HERE. The first yard
# was #4A4642 and, with a brown building and a near-black stack, left the init with NO
# BRIGHT REFERENCE ANYWHERE. The model produced a beautifully textured DARK ALLEY - correct
# brick, no silhouette, no daylight. An init needs tonal range as much as it needs extent:
# if nothing in it is light, nothing in the output is either.
YARD = (0x8A, 0x85, 0x80)
YARD_HI = (0x9E, 0x99, 0x93)
YARD_LO = (0x6E, 0x69, 0x64)
DAYLIGHT = (0xD2, 0xD6, 0xDA)    # a sky band outside the footprint, purely as a reference

# `bright overcast daylight` is an EXPOSURE instruction, not a colour word, and it is here
# because the first two rounds came back underexposed - the second so dark it read as an
# alley at dusk. The university's prompt carries `overcast daylight` for the same reason.
#
# `soot staining` WAS HERE AND IS STRUCK. It was meant as a surface stain on brick; the
# model read `factory` plus `soot` as a working chimney and returned the building behind
# billowing STEAM that buried its whole base. The stain is not worth the smoke, and
# `weathered brickwork` already carries the surface.
PROMPT = ("a photograph of an old industrial brick factory, weathered brickwork, "
          "corrugated steel roof, rusted iron chimney stack, tall grimy multi pane "
          "windows, bright overcast daylight, even exposure, clear air, sharp focus, "
          "fine detail, architectural photography")

#
# `smoke, steam, fog` added after round 3 buried the building's base in steam. ROOM WAS
# TAKEN FROM SYNONYMS ONLY - `lowres` (covered by `pixelated`), `deformed` (by `distorted`),
# `border` (by `frame`) and `cars` (nothing in this scene suggests one). `flat` is NOT
# touched: deleting it to make room is what turned Hire Ground into a vector illustration,
# and the rule that came out of that is that a new term never evicts one that predates the
# problem it is solving.
NEGATIVE = ("text, letters, words, writing, lettering, signage, numbers, logo, watermark, "
            "blurry, out of focus, bokeh, 3d render, cgi, vector, illustration, flat, "
            "cartoon, pixelated, jpeg artifacts, noise, people, distorted, frame, "
            "vignette, smoke, steam, fog")


def synthetic_init():
    """A 61x38 block painting of the Factory - and NO SIGN BOARD.

    THE SURROUNDINGS ARE SCAFFOLDING, NOT OUTPUT, AND THEY ARE PAINTED AS A YARD RATHER
    THAN AS SIERRA'S COLOURS. This is the one place in the pipeline where copying her
    palette is actively wrong, and the first attempt did it and failed.

    Her ground is lavender (#6070C8) and her river is pale blue (#B8C8E0). Those are
    perfectly good 1990 map colours and they are not ground colours in any photograph: the
    model read a large soft blue mass above AND below the building as SKY, painted clouds
    across both, and returned a brick facade FLOATING IN MID AIR with no base.

    It costs nothing to fix, because `blend_surround` keeps only the building's own
    footprint and throws every one of these pixels away. They exist solely to tell the
    model what kind of scene this is, so they are painted as a yard: dark ground, a
    horizon, and no blue anywhere below the roofline. Sierra's river and lavern ground come
    back untouched in the blend.

    This is CAST.md section 0's third category - IDENTITY. The mass was there and the
    colour was there; the model simply made it the wrong object."""
    a = np.zeros((REG_H, REG_W, 3), np.uint8)

    def box(x0, y0, x1, y1, c):
        a[max(0, y0):y1 + 1, max(0, x0):x1 + 1] = c

    box(0, 0, REG_W - 1, REG_H - 1, YARD)
    # NO BRIGHT HORIZON BAND. One was drawn across y 26..29 and it is what produced the
    # MIST: a soft bright horizontal stripe low in the frame is exactly what haze looks
    # like, and the model painted it faithfully over the building's base two rounds
    # running, through `smoke, steam, fog` in the negative. The ground reads perfectly well
    # as ground without it.

    # --- EVERYTHING INTERESTING SITS ABOVE THE SIGN ---------------------------------
    # The chosen sign is 55x21 at game (9,171), i.e. region rows 16..36, so rows 0..15 are
    # the only part of this building the player ever sees. The masses below are arranged so
    # the chimney, the roofline and all three window courses fall inside those 16 rows;
    # what is under row 16 only has to be plausible, not interesting.
    SIGN_TOP = 16

    box(0, 0, 14, 26, BRICK)
    box(0, 0, 14, 2, BRICK_LO)                  # a darker course at the eaves
    box(15, 2, 25, 12, STACK)                   # the roof block
    box(26, 0, 30, 14, BRICK_LO)                # a chimney: 5px, well clear of the blur
    box(26, 0, 30, 1, STACK)                    # its rusted cap
    box(15, 13, 30, 26, FURNACE)                # the lower block

    # Windows, at rows 3, 8 and 13 - ALL THREE COURSES ABOVE THE SIGN. They were at 4, 11
    # and 18 and the last course was behind the board, which is resolution spent on
    # something nobody sees. 3px wide and 4 tall: at BLUR = 16.3px against a 16x scale that
    # is one cel pixel, and anything thinner than 2px is not in the init at all (CAST.md
    # section 0, mode 3 - the university's 1px ivy vanished exactly this way).
    WIN_ROWS = (3, 8, 12)
    for wx in (2, 7, 12):
        for wy in WIN_ROWS:
            box(wx, wy, wx + 2, wy + 3, WIN)
    for wx in (17, 22):
        box(wx, 5, wx + 3, 10, WIN)
    # The courses were at 3, 8 and 13 and this assertion rejected it: a window at row 13 is
    # 4 rows tall, so its last row IS row 16, the sign's first. Off by exactly one, and
    # caught before a GPU round rather than after looking at the result.
    assert max(WIN_ROWS) + 3 < SIGN_TOP, "a window course has fallen behind the sign"

    small = Image.fromarray(a, "RGB")
    big = small.resize((GEN_W, GEN_H), Image.LANCZOS)
    return small, big.filter(ImageFilter.GaussianBlur(BLUR))


def soft_edges(im):
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
    """Refuse while another python holds the card. Only a python counts - `dwm`, Teams and
    the Edge WebView are permanently on this GPU and an earlier version that refused on any
    compute process would have refused every run for ever."""
    import subprocess
    try:
        r = subprocess.run(["nvidia-smi", "--query-compute-apps=pid,process_name,used_memory",
                            "--format=csv,noheader"],
                           capture_output=True, text=True, timeout=30)
        rows = [ln.strip() for ln in r.stdout.splitlines() if ln.strip()]
    except (OSError, subprocess.SubprocessError):
        rows = []
    mine = os.getpid()
    others = []
    for ln in rows:
        parts = [p.strip() for p in ln.split(",")]
        if len(parts) >= 2 and parts[0].isdigit() and int(parts[0]) != mine \
                and "python" in os.path.basename(parts[1]).lower():
            others.append(ln)
    if others:
        raise SystemExit("another python is on the card - refusing a second job on 4GB:\n  "
                         + "\n  ".join(others))
    try:
        import gen_cast
        gen_cast.claim_gpu(out)
    except ImportError:
        pass


def generate(args, init_big, cond):
    import torch
    import gen_walkers as gw

    device = "cuda" if torch.cuda.is_available() else "cpu"
    txt, img = gw.build(device, offload=True, slicing=True,
                        controlnet=os.path.join(MODELS, "cn-softedge"))
    for what, s in (("positive", PROMPT), ("negative", NEGATIVE)):
        n = len(txt.tokenizer(s).input_ids)
        if n > txt.tokenizer.model_max_length:
            raise SystemExit("%s prompt is %d tokens, over CLIP's %d:\n  %s"
                             % (what, n, txt.tokenizer.model_max_length, s))
        print("  %s prompt: %d/%d tokens" % (what, n, txt.tokenizer.model_max_length))

    g = torch.Generator(device).manual_seed(args.seed)
    return img(prompt=PROMPT, negative_prompt=NEGATIVE, image=init_big, control_image=cond,
               strength=args.strength,
               num_inference_steps=max(args.steps, int(args.steps / args.strength)),
               guidance_scale=args.cfg, controlnet_conditioning_scale=args.control,
               generator=g).images[0]


def silhouette(sierra_region, scale=1):
    """The building's REAL outline, taken from Sierra's own pixels - not a bounding box.

    THE BOUNDING BOX WAS WRONG AND IT LOOKED IT. `blend_surround` originally kept a
    rectangle, which is fine for the Employment Office because that building IS very nearly
    its own bounding box. The Factory is not: it has a stepped roofline, a taller left
    block and a lower right annexe, and the lavender and water come INTO the box around all
    of them. Keeping the rectangle pasted a hard-edged slab of brick over the scene with no
    silhouette at all - the single worst-looking thing produced in this whole job.

    So the surroundings are identified by colour instead. Every one of Sierra's
    surroundings here is BLUE-DOMINANT and reasonably light - lavender #6070C8 and #8080C8,
    water #B8C8E0 and #C0D0E0, the darker blues #405890 and #585890 - and no part of the
    building is: the brickwork is #584030 and friends (red-dominant), the dark stack is
    #202830 (blue-dominant but far too dark to pass the threshold), and the old sign board
    is tan. So `blue and not dark` separates them cleanly, and the building is what is left.

    The old sign board falls on the building side of that test, which is correct: the new
    sign is larger than the old one and covers it completely, so what is underneath only
    has to not be a hole."""
    a = np.asarray(sierra_region.convert("RGB")).astype(int)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    surroundings = (b > r) & (b > g) & (b > 130)
    m = ~surroundings
    # Confine it to the building's own area; anything outside is scenery whatever colour
    # it is, and a stray tan pixel in the river is not a building.
    box = np.zeros_like(m)
    x0, y0, x1, y1 = BUILDING
    box[y0:y1 + 1, x0:x1 + 1] = True
    m &= box
    if scale != 1:
        m = np.kron(m, np.ones((scale, scale), bool))
    return m


def blend_surround(gen_region, sierra_region):
    a = np.asarray(gen_region.convert("RGB")).astype(np.uint8)
    s = np.asarray(sierra_region.convert("RGB")).astype(np.uint8)
    m = silhouette(sierra_region)
    print("  silhouette: %d of %d px in the footprint are building (%.0f%%)"
          % (int(m.sum()), REG_W * REG_H, 100.0 * m.sum() / (REG_W * REG_H)))
    return Image.fromarray(np.where(m[..., None], a, s).astype(np.uint8), "RGB")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cels", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--strength", type=float, default=0.88)
    ap.add_argument("--steps", type=int, default=32)
    ap.add_argument("--cfg", type=float, default=9.0)
    ap.add_argument("--control", type=float, default=0.35)
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--dry", action="store_true")
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)
    init_small, init_big = synthetic_init()
    init_small.save(os.path.join(args.out, "init_%dx%d.png" % (REG_W, REG_H)))
    init_big.save(os.path.join(args.out, "init_%dx%d.png" % (GEN_W, GEN_H)))
    cond = soft_edges(init_big)
    cond.save(os.path.join(args.out, "cond_softedge.png"))
    print("region %dx%d at game (%d,%d) -> %dx%d (%d px), blur %.1f px = %.2f%% of width"
          % (REG_W, REG_H, REG_X, REG_Y, GEN_W, GEN_H, GEN_W * GEN_H, BLUR,
             100.0 * BLUR / GEN_W))
    print("building footprint kept: x %d..%d, y %d..%d (region-local)"
          % (BUILDING[0], BUILDING[2], BUILDING[1], BUILDING[3]))

    if args.dry:
        print("--dry: init written, no GPU used")
        return

    claim_card(args.out)
    print("generating at %dx%d, strength %.2f, control %.2f, cfg %.1f, steps %d, seed %d"
          % (GEN_W, GEN_H, args.strength, args.control, args.cfg, args.steps, args.seed))
    big = generate(args, init_big, cond)
    big.save(os.path.join(args.out, "generated_%dx%d.png" % (GEN_W, GEN_H)))

    region = big.resize((REG_W, REG_H), Image.BOX)
    region.save(os.path.join(args.out, "region_unblended_%dx%d.png" % (REG_W, REG_H)))

    bg = Image.open(os.path.join(args.cels, "cel_00_319x199_at_1_0.png")).convert("RGB")
    sierra = bg.crop((REG_X - 1, REG_Y, REG_X - 1 + REG_W, REG_Y + REG_H))
    blended = blend_surround(region, sierra)
    blended.save(os.path.join(args.out, "region_%dx%d.png" % (REG_W, REG_H)))
    print("wrote region_%dx%d.png - the building only, in Sierra's surroundings"
          % (REG_W, REG_H))


if __name__ == "__main__":
    main()
