r"""The talking-head portraits (views 351, 353-362), one face in eleven mouth positions.

    python tools/gen_talker.py --view 355 --out <dir> --dry      # inits only, no GPU
    python tools/gen_talker.py --view 355 --out <dir>            # base face
    python tools/gen_talker.py --view 355 --out <dir> --mouths   # the eleven cels

Zain, on the Factory's shopkeeper: *"this looks ridiculous!!!, doesn't even look like a human
being!!!"* and *"Change the lady's face, same colour hair and similar characteristics, but
much more realistic face"*. Same woman, realistically rendered - not a different person.

WHY THIS IS NOT A BUILDING
--------------------------
**A talker has ELEVEN cels, not one.** `LipSync.cs` drives `_talkerCel` 0..10 from the game's
own sync tables at 60Hz while she speaks. One cel freezes her mouth; eleven independently
generated faces flicker, because nothing holds a diffusion model to the same cheekbones twice.

So the shape is the one that settled the signs: **construct, then light.** One face is
generated. The other ten cels are that same face with ONLY a mouth box replaced, and
everything outside that box is **copied byte for byte**. Flicker is not reduced, it is made
impossible - there is nothing outside the box that COULD differ.

AND WE DO NOT REPRODUCE HER JITTER
----------------------------------
Measured across her eleven cels, the per-pixel standard deviation is 4.9 and **64.8% of
pixels change somewhere**; a mouth-sized box holds only 41% of the strongest variation. That
is not animation. **Her eleven cels are eleven separate photographs of a real actor**, so her
head moves between frames. A still head with only the mouth moving is BETTER than the
original, and it is what makes the copy-outside-the-box approach legitimate rather than a
compromise.

THE MOUTH SHAPES ARE HERS, VERBATIM, AND THAT IS THE POINT
----------------------------------------------------------
The sync tables index these eleven positions by meaning - closed, open with teeth, the dark
"O" at cel 6, back to closed. **Smooth but wrong lip-sync is worse than what we have.** So
each cel's mouth is conditioned on the SOFT EDGES OF HER OWN MOUTH in that cel: the structure
is measured off her art and cannot be invented, while the rendering is ours. That is the same
division as a constructed sign - layout deterministic, lighting denoised.

WHAT IS RESTORED RATHER THAN GENERATED
--------------------------------------
Her field is a flat **(0,32,144)** blue and her frame is a 1px black border, both exact. They
are put back after every pass, so the model only ever has to get the PERSON right and the
portrait still keys and sits in the dialog exactly as before.
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageChops, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_building as gb

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PNG1X = os.path.join(ROOT, "assets", "png")

FIELD = (0, 32, 144)          # her flat blue, measured
FRAME = (0, 0, 0)             # her 1px border, measured
CELS = 11                     # LipSync.cs: every talker view has exactly eleven

# Per view: the prompt for the face. Only 355 is written so far; the rest are listed so the
# next person knows the set is eleven and which is which.
FACES = {
    355: dict(
        who="the Factory's shopkeeper",
        # af_nicole at 1.02 was cast against this face - "light and smiling, the cheer over
        # 'we will overwork you' is the joke". The face has to keep reading as that voice.
        prompt=("a photograph of a smiling woman in her thirties, voluminous shoulder length "
                "red auburn curly hair, broad warm open smile, dark top, plain blue "
                "background, studio portrait, three quarter view, soft even lighting, sharp "
                "focus, fine detail, photorealistic"),
        negative=("illustration, drawing, painting, cartoon, 3d render, cgi, anime, doll, "
                  "plastic skin, waxy, deformed face, distorted, extra eyes, asymmetric "
                  "eyes, blurry, low resolution, text, watermark, frame, border"),
        # The mouth box, in CEL coordinates, from the variation peak across her eleven cels:
        # the per-pixel standard deviation peaks in rows 30..34 with its median at row 30,
        # col 33. The box is drawn generously around that so its edges land in flat skin and
        # on the dark top, NOT across the lip line or the jaw contour.
        mouth=(20, 26, 48, 46),
        strength=0.55, control=0.55, cfg=7.5, steps=34, seed=7,
        # THE MOUTH PASS NEEDS ITS OWN PROMPT, and the first attempt proved it the hard way.
        # Handing the whole-portrait prompt to a 348x252 crop of a chin asks the model to fit
        # "studio portrait, plain blue background" into a close-up, and six of the eleven
        # cels came back as coloured swirls. Same failure as asking for "an apartment
        # building" in a square frame and getting a doorway: THE PROMPT MUST DESCRIBE THE
        # CROP, not the subject the crop was taken from.
        mouth_prompt=("a close photograph of a smiling mouth and chin, lips and teeth, "
                      "soft even lighting, sharp focus, fine detail, photorealistic"),
        mouth_strength=0.35, mouth_control=0.60, mouth_steps=28,
    ),
}
SCALE = 12                    # 68x55 -> 816x660 = 538,560 px, inside the measured 810k


def her_cels(view):
    out = []
    for c in range(CELS):
        p = os.path.join(PNG1X, "view_%d_l0_c%d.png" % (view, c))
        out.append(Image.open(p).convert("RGB"))
    return out


def field_mask(cel):
    """Her flat blue field, and the 1px frame. Both are restored, never generated."""
    a = np.asarray(cel).astype(int)
    d = np.abs(a - np.array(FIELD)).max(2)
    m = d <= 24
    h, w = m.shape
    frame = np.zeros((h, w), bool)
    frame[0, :] = frame[-1, :] = True
    frame[:, 0] = frame[:, -1] = True
    return m, frame


def restore(img, cel, S):
    """Put her field and her frame back, exactly, at S px per cel pixel."""
    m, frame = field_mask(cel)
    a = np.asarray(img).astype(np.uint8).copy()
    M = np.kron(m, np.ones((S, S), bool))
    F = np.kron(frame, np.ones((S, S), bool))
    a[M] = FIELD
    a[F] = FRAME
    return Image.fromarray(a, "RGB")


def pad8(im):
    """Grow to a multiple of 8 by REPLICATING the bottom and right edges.

    68x55 at 12x is 816x660, and 660 is not a multiple of 8, so the pipeline floors it to 656
    and hands back an image four rows short of the masks. Resizing the result back would
    resample the whole portrait to fix four rows. Padding the INPUT to 816x664 instead means
    the pass runs at its own native size and the pad is simply cropped off afterwards -
    nothing in the portrait is resampled at all. The replicated rows are her black frame, so
    the model is not shown an edge that is not already there."""
    w, h = im.size
    W, H = (w + 7) // 8 * 8, (h + 7) // 8 * 8
    if (W, H) == (w, h):
        return im
    a = np.asarray(im)
    a = np.pad(a, ((0, H - h), (0, W - w), (0, 0)), mode="edge")
    return Image.fromarray(a, "RGB")


def init_from(cel, S, blur):
    big = cel.resize((cel.width * S, cel.height * S), Image.LANCZOS)
    return pad8(big.filter(ImageFilter.GaussianBlur(blur)))


def align_to(her_c, her_0, box, search=5):
    """How far her head moved between cel 0 and cel c. MEASURED, AND THE ANSWER IS ZERO.

    Cels 1, 2 and 9 came back with swirls and kept coming back with swirls on a rerolled
    seed, so the failure is systematic. The hypothesis was her jitter arriving where it was
    not expected: the conditioning is taken from her cels, so in a cel where her head has
    moved it would describe a jaw in a position our base face does not have one, and the pass
    would be asked to put structure where there is none while keeping the face it was given.

    **That is not what is happening.** Run across all eleven cels, scored on everything
    except the mouth box, the best shift is (0,0) FOR EVERY CEL:

        cel  1  (+0,+0) residual 4.3      cel  6  (+0,+0) residual 3.7
        cel  2  (+0,+0) residual 3.4      cel  9  (+0,+0) residual 4.8

    Her jitter is sub-pixel and photographic - grain and shading, not translation - so there
    is nothing to align and the residuals do not separate the failing cels from the clean
    ones either (cel 2 fails at 3.4 while cel 6 is clean at 3.7).

    This is kept as a measurement rather than deleted, so the next person does not re-derive
    the same plausible theory. It is the second cause I was confident about on this portrait
    and measured away; the first was the dither. **The conditioning is not the problem.**
    """
    a0 = np.asarray(her_0.convert("L")).astype(np.float32)
    ac = np.asarray(her_c.convert("L")).astype(np.float32)
    h, w = a0.shape
    x0, y0, x1, y1 = box
    keep = np.ones((h, w), bool)
    keep[y0:y1 + 1, x0:x1 + 1] = False          # the mouth is SUPPOSED to differ
    best, bdx, bdy = None, 0, 0
    for dy in range(-search, search + 1):
        for dx in range(-search, search + 1):
            sh = np.roll(np.roll(ac, dy, 0), dx, 1)
            m = keep.copy()
            if dy > 0:
                m[:dy, :] = False
            elif dy < 0:
                m[dy:, :] = False
            if dx > 0:
                m[:, :dx] = False
            elif dx < 0:
                m[:, dx:] = False
            if not m.any():
                continue
            v = float(np.abs(sh[m] - a0[m]).mean())
            if best is None or v < best:
                best, bdx, bdy = v, dx, dy
    return bdx, bdy, best


def seat_patch(base, patch, box, S, ring=2, ramp=4):
    """Put the mouth patch back so the box CANNOT be seen, without touching anything outside.

    A hard paste shows. The first run proved it: real mouths, and a visible RECTANGLE across
    the lower face where the patch's tone and grain met the base's. The fix has to respect the
    invariant that makes this whole design work - every pixel outside the box stays byte
    identical - so BOTH halves of it live strictly INSIDE the box:

      1. LEVEL MATCH. The patch is shifted per channel so its mean over a ring just inside the
         border equals the base's mean over the same ring. That removes the tone step, which
         is what the eye actually catches.
      2. A RAMP, INSIDE THE BORDER. The patch fades to the base over `ramp` cel pixels at the
         box edge. At the border itself the result IS the base, so the seam is not hidden -
         there is nothing there to hide.

    This is not the feathering §17 forbids. That rule is about never blurring a join with
    SIERRA'S art to disguise it. Here both sides are ours and the edge is an artefact of our
    own tiling, which is the same thing `detail_tiles` corrects across its overlaps.
    """
    x0, y0, x1, y1 = box
    bx, by = x0 * S, y0 * S
    bw, bh = (x1 - x0 + 1) * S, (y1 - y0 + 1) * S
    b = np.asarray(base).astype(np.float32)
    p = np.asarray(patch).astype(np.float32)
    under = b[by:by + bh, bx:bx + bw]

    r = max(1, ring * S)
    m = np.zeros((bh, bw), bool)
    m[:r, :] = m[-r:, :] = True
    m[:, :r] = m[:, -r:] = True
    p = p + (under[m].mean(0) - p[m].mean(0))

    f = max(1, ramp * S)
    ys = np.minimum(np.arange(bh), bh - 1 - np.arange(bh))
    xs = np.minimum(np.arange(bw), bw - 1 - np.arange(bw))
    w = np.minimum(np.minimum(ys[:, None], xs[None, :]) / float(f), 1.0)
    w = (0.5 - 0.5 * np.cos(np.pi * np.clip(w, 0, 1)))[..., None]

    out = b.copy()
    out[by:by + bh, bx:bx + bw] = p * w + under * (1.0 - w)
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB")


def despeckle(path, box, S, radius=0.30, ramp=4):
    """Smooth the CHROMA inside the mouth box. Luma, and everything outside, untouched.

    The seated patch came back with coloured specks - yellow and green flecks on the cheek,
    a purple cast across the lips in some cels. At 1x they vanish entirely; at 12x, which is
    the rung the desktop ships, they are visible.

    They are a CHROMA artefact: the pass is re-rendering a soft upscale at low strength and
    the colour channels pick up high-frequency noise the luma does not. So the fix is the
    standard one and it costs no GPU - **blur the chroma, keep the luma**. Perceived sharpness
    lives almost entirely in luma, so the mouth stays as crisp as it was while the flecks go.

    The same cosine ramp as `seat_patch` is reused, so the correction fades to nothing at the
    box border and the byte-identical invariant outside the box still holds exactly.
    """
    im = Image.open(path).convert("RGB")
    x0, y0, x1, y1 = box
    bx, by = x0 * S, y0 * S
    bw, bh = (x1 - x0 + 1) * S, (y1 - y0 + 1) * S
    ycc = im.convert("YCbCr")
    y, cb, cr = ycc.split()
    r = max(1.0, S * radius)
    cb2 = cb.filter(ImageFilter.GaussianBlur(r))
    cr2 = cr.filter(ImageFilter.GaussianBlur(r))

    f = max(1, ramp * S)
    ys = np.minimum(np.arange(bh), bh - 1 - np.arange(bh))
    xs = np.minimum(np.arange(bw), bw - 1 - np.arange(bw))
    w = np.minimum(np.minimum(ys[:, None], xs[None, :]) / float(f), 1.0)
    w = 0.5 - 0.5 * np.cos(np.pi * np.clip(w, 0, 1))

    out = {}
    for name, orig, blurred in (("cb", cb, cb2), ("cr", cr, cr2)):
        a = np.asarray(orig).astype(np.float32)
        b = np.asarray(blurred).astype(np.float32)
        sub = a[by:by + bh, bx:bx + bw]
        a[by:by + bh, bx:bx + bw] = b[by:by + bh, bx:bx + bw] * w + sub * (1.0 - w)
        out[name] = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "L")
    # ONLY THE BOX IS WRITTEN BACK, and the first version of this did not do that.
    #
    # It converted the whole portrait to YCbCr and back, which is LOSSY: every pixel in the
    # image shifted slightly, including her flat blue field, even though the only values
    # deliberately changed were inside the box. The cels despeckled in one run then no longer
    # matched the cels generated in the next, and the installer refused with 415,882 px
    # differing outside the mouth box - 77% of the portrait - for an operation that was
    # supposed to touch 16% of it.
    #
    # The guard caught it. That is the second time on this portrait that the invariant check
    # has caught a defect introduced by HOUSEKEEPING rather than by the generation.
    fixed = Image.merge("YCbCr", (y, out["cb"], out["cr"])).convert("RGB")
    res = im.copy()
    res.paste(fixed.crop((bx, by, bx + bw, by + bh)), (bx, by))
    res.save(path)


def check_invariant(cels_gen, spec, S):
    """Outside the mouth box, every cel must be byte-identical. Not nearly - identical.

    This is the guarantee the whole two-stage design exists to provide, so it is MEASURED
    rather than assumed, and the first run failed it: 5,324 px differed, because each cel's
    blue field was being restored against ITS OWN original and her field jitters frame to
    frame. A design that makes a defect impossible still has to be checked, because the thing
    that breaks it is never the part you were thinking about."""
    x0, y0, x1, y1 = spec["mouth"]
    bx, by = x0 * S, y0 * S
    bw, bh = (x1 - x0 + 1) * S, (y1 - y0 + 1) * S
    base = np.asarray(cels_gen[0]).astype(int)
    mask = np.ones(base.shape[:2], bool)
    mask[by:by + bh, bx:bx + bw] = False
    worst = 0
    for c in cels_gen:
        d = (np.abs(np.asarray(c).astype(int) - base).max(2) > 0) & mask
        worst = max(worst, int(d.sum()))
    return worst


def install(a):
    """Write the eleven cels into assets/png{F}x-{set}, at every rung asked for."""
    spec = FACES[a.view]
    cels_her = her_cels(a.view)
    w, h = cels_her[0].size
    S = SCALE
    gen = []
    for c in range(CELS):
        p = os.path.join(a.out, "cel_%02d.png" % c)
        if not os.path.exists(p):
            raise SystemExit("missing %s - run --mouths first" % p)
        gen.append(Image.open(p).convert("RGB"))

    # THE INVARIANT IS ENFORCED HERE, NOT MERELY CHECKED. Every cel is rebuilt as the base
    # face with its own mouth box pasted in, so "identical outside the box" is true by
    # construction at install time and cannot be broken by anything that happened upstream -
    # a despeckle, a partial reroll, a hand edit. The check that follows then has something
    # real to confirm rather than being the only thing standing between a bug and the game.
    base_p = os.path.join(a.out, "base_%dx%d.png" % (w * S, h * S))
    if os.path.exists(base_p):
        bse = Image.open(base_p).convert("RGB")
        x0, y0, x1, y1 = spec["mouth"]
        bx, by = x0 * S, y0 * S
        bw, bh = (x1 - x0 + 1) * S, (y1 - y0 + 1) * S
        for c in range(CELS):
            out = bse.copy()
            out.paste(gen[c].crop((bx, by, bx + bw, by + bh)), (bx, by))
            out = restore(out, cels_her[0], S)
            out.save(os.path.join(a.out, "cel_%02d.png" % c))
            gen[c] = out
        print("  reseated every cel onto the base face: identical outside the box BY"
              " CONSTRUCTION")

    if a.despeckle:
        for c in range(CELS):
            despeckle(os.path.join(a.out, "cel_%02d.png" % c), spec["mouth"], S)
        gen = [Image.open(os.path.join(a.out, "cel_%02d.png" % c)).convert("RGB")
               for c in range(CELS)]
        print("  chroma smoothed inside the mouth box; luma and everything outside untouched")

    bad = check_invariant(gen, spec, S)
    print("  INVARIANT: outside the mouth box, %d px differ across the eleven cels" % bad)
    if bad:
        raise SystemExit("  REFUSING: the eleven cels are not identical outside the mouth "
                         "box, so the face will flicker while she speaks.")
    print("  (zero - so nothing outside the mouth CAN differ, by construction)")

    for F in [int(v) for v in a.factors.split(",")]:
        out = os.path.join(ROOT, "assets", "png%dx-%s" % (F, a.set))
        if not os.path.isdir(out):
            raise SystemExit("no such set: %s" % out)
        for c in range(CELS):
            im = gen[c]
            if (w * F, h * F) != im.size:
                im = im.resize((w * F, h * F), Image.BOX if F < S else Image.LANCZOS)
            im.save(os.path.join(out, "view_%d_l0_c%d.png" % (a.view, c)))
        print("  wrote %d cels at %dx (%dx%d) -> %s"
              % (CELS, F, w * F, h * F, os.path.basename(out)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--view", type=int, required=True, choices=sorted(FACES))
    ap.add_argument("--out", required=True)
    ap.add_argument("--dry", action="store_true")
    ap.add_argument("--mouths", action="store_true",
                    help="stage two: the eleven cels, from an existing base face")
    ap.add_argument("--install", action="store_true",
                    help="stage three: write the eleven cels into the shipping set")
    ap.add_argument("--redo", default=None,
                    help="regenerate only these cels, e.g. 1,2,9")
    ap.add_argument("--seed-shift", type=int, default=0,
                    help="offset the per-cel seed, to reroll a cel that came back damaged")
    ap.add_argument("--despeckle", action="store_true",
                    help="smooth chroma inside the mouth box before installing")
    ap.add_argument("--set", default="cast0")
    ap.add_argument("--factors", default="4,12")
    ap.add_argument("--strength", type=float, default=None)
    ap.add_argument("--control", type=float, default=None)
    ap.add_argument("--seed", type=int, default=None)
    a = ap.parse_args()

    spec = FACES[a.view]
    os.makedirs(a.out, exist_ok=True)
    if a.install:
        install(a)
        return
    cels = her_cels(a.view)
    w, h = cels[0].size
    S = SCALE
    print("view %d - %s" % (a.view, spec["who"]))
    print("  %d cels, each %dx%d cel px; generating at %dx%d = %d px"
          % (CELS, w, h, w * S, h * S, w * S * h * S))

    blur = max(1.0, S * 0.11)
    init = init_from(cels[0], S, blur)
    cond = gb.soft_edges(init_from(cels[0], S, blur * 0.5))
    init.save(os.path.join(a.out, "init_%dx%d.png" % (w * S, h * S)))
    cond.save(os.path.join(a.out, "cond_softedge.png"))
    m, _ = field_mask(cels[0])
    print("  init blur %.1f px; her field is %.1f%% of the portrait and is RESTORED, not"
          " generated" % (blur, 100.0 * m.mean()))

    if a.dry:
        print("--dry: inits written, no GPU used")
        return

    base_path = os.path.join(a.out, "base_%dx%d.png" % (w * S, h * S))
    if not a.mouths:
        print("  strength %.2f control %.2f cfg %.1f steps %d seed %d"
              % (a.strength or spec["strength"], a.control or spec["control"],
                 spec["cfg"], spec["steps"], a.seed or spec["seed"]))
        img = gb.run(spec["prompt"], init, cond,
                     a.strength or spec["strength"], spec["steps"], spec["cfg"],
                     a.control or spec["control"], a.seed or spec["seed"],
                     spec["negative"])
        img = img.crop((0, 0, w * S, h * S))        # drop the multiple-of-8 pad
        img = restore(img, cels[0], S)
        img.save(base_path)
        print("  wrote base_%dx%d.png - LOOK AT IT before running --mouths" % (w * S, h * S))
        return

    # ---- stage two: eleven cels, identical outside the mouth box -------------------
    if not os.path.exists(base_path):
        raise SystemExit("no base face yet: run without --mouths first, and look at it")
    base = Image.open(base_path).convert("RGB")
    x0, y0, x1, y1 = spec["mouth"]
    bx, by = x0 * S, y0 * S
    bw, bh = (x1 - x0 + 1) * S, (y1 - y0 + 1) * S
    print("  mouth box cel (%d,%d)-(%d,%d) = %dx%d cel px, %.1f%% of the portrait"
          % (x0, y0, x1, y1, x1 - x0 + 1, y1 - y0 + 1,
             100.0 * ((x1 - x0 + 1) * (y1 - y0 + 1)) / (w * h)))
    print("  everything outside it is COPIED, so nothing outside it can differ")

    todo = range(CELS) if not a.redo else [int(v) for v in a.redo.split(",")]
    for c in todo:
        # CEL 0 IS THE BASE FACE, UNTOUCHED, and that is not a shortcut.
        #
        # Her cel 0 is the resting closed smile, and the base face was generated FROM her
        # cel 0 - so the face we already have IS this mouth position. Running a pass over it
        # can only add artefacts to the frame the player sees most, which is exactly what it
        # did: cel 0 came back with swirls on the cheek while cels 3-8 were clean. The pass
        # that has nothing to change is the one most likely to damage something.
        if c == 0:
            base.save(os.path.join(a.out, "cel_00.png"))
            print("    cel  0 IS the base face - no pass, nothing to re-render")
            continue
        out = base.copy()
        # The structure is HER mouth in THIS cel; the rendering is ours.
        dx, dy, _ = align_to(cels[c], cels[0], spec["mouth"])
        shifted = ImageChops.offset(cels[c], dx, dy)
        if dx or dy:
            print("      her head moved (%+d,%+d) here; shifted back onto cel 0"
                  % (-dx, -dy))
        her_mouth = shifted.crop((x0, y0, x1 + 1, y1 + 1))
        hb = her_mouth.resize((bw, bh), Image.LANCZOS)
        # HER MOUTH IS DITHERED 16-COLOUR ART, so its soft edges at 12x are the DITHER, not
        # the lips. At a 0.7 px blur the conditioning map was texture noise and the pass
        # painted swirls over it at control 0.85. The blur has to be wide enough to destroy
        # the dither and leave only the gross shape - the lip line, the teeth block, the
        # opening - which is the only thing we actually want to copy from her.
        cnd = gb.soft_edges(hb.filter(ImageFilter.GaussianBlur(max(2.0, S * 0.40))))
        src = base.crop((bx, by, bx + bw, by + bh))
        # --strength/--control steer THIS pass when --mouths is in play. They used to feed
        # only the base face, so a command that looked like it was retuning the mouth pass
        # was silently just rerolling it - a flag that appears to do something and does not.
        got = gb.run(spec["mouth_prompt"], pad8(src), pad8(cnd),
                     a.strength or spec["mouth_strength"],
                     spec["mouth_steps"], spec["cfg"],
                     a.control or spec["mouth_control"],
                     (a.seed or spec["seed"]) + c + a.seed_shift * 101,
                     spec["negative"])
        out = seat_patch(base, got.crop((0, 0, bw, bh)), spec["mouth"], S)
        # THE FIELD MASK COMES FROM CEL 0, NOT FROM CEL c, AND THAT IS THE WHOLE POINT.
        # Restoring each cel against its OWN original put her jitter straight back in: her
        # blue field has a slightly different silhouette in every frame because her head
        # moves, so the restore wrote a different boundary per cel and 5,324 pixels differed
        # OUTSIDE the mouth box - the invariant this stage exists to guarantee, broken by the
        # one line that was supposed to be housekeeping. One mask, from one cel, for all
        # eleven.
        out = restore(out, cels[0], S)
        out.save(os.path.join(a.out, "cel_%02d.png" % c))
        print("    cel %2d written" % c)
    print("  eleven cels written")


if __name__ == "__main__":
    main()
