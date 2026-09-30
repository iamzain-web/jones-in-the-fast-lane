r"""
RESTORE the 1990 walker cels. Not new characters - the originals, cleaned up.

    THE HEADLINE, SO IT IS NOT BURIED. Two methods are implemented here. The diffusion one
    (--method img2img), which is what this file was written to try, DOES NOT BEAT MITCHELL
    and should not be used. The cheap deterministic one (--method bilat, the default) does
    beat it, on every view and on both fidelity metrics, and costs no GPU at all. The
    diffusion path is kept because the measurements that condemn it are worth being able to
    reproduce, not because it is an option.

WHAT THE SOURCE ACTUALLY IS, WHICH DECIDES EVERYTHING ELSE
----------------------------------------------------------
The player walkers are 39x95 digitised photographs quantised to a 256-colour palette.
Looking at one at 12x, the suit is full of speckled blue and purple pixels. Three
hypotheses were on the table for what that speckle is, and it is worth knowing which,
because the right treatment is different for each.

  1. VGA DITHER - 1990 hardware faking extra shades with a checkerboard. FALSIFIED.
     Measured three independent ways over the visible pixels of 280/284/290/294/274 c0-c1:

         test                                    walkers        talkers (control)
         checkerboard share of HF energy         0.09 - 0.13    0.09 - 0.10
         fraction of px with that share > 0.55   0.000          0.000 - 0.001
         3x3 windows holding exactly 2 colours   0.000          0.000
         distinct colours / visible pixels       147 / 2334     121 / 3740

     Not one 3x3 checkerboard of two colours exists anywhere in any cel, and the
     checkerboard share of the high-frequency energy is the same as on the talker
     portraits, which nobody has ever called dithered. 147 colours over 2334 pixels is
     continuous tone, not a two-colour pattern. There is no dither to resolve.

  2. DETAIL - fabric, folds, shading, worth recovering. FALSIFIED.
  3. NOISE - digitiser grain and palette quantisation, worth suppressing. CONFIRMED.

     The test that separates 2 from 3 is the lag-1 spatial autocorrelation of the
     high-frequency residual. Real detail spans pixels, so it correlates with its
     neighbour; independent noise does not. Over the trouser regions, which are the
     flattest part of each figure and therefore the cleanest read:

         view 280 legs   r1 = -0.330      view 351 (talker)  r1 = +0.661
         view 284 legs   r1 = -0.285      view 355 (talker)  r1 = +0.132
         view 290 legs   r1 = -0.421      view 359 (talker)  r1 = +0.385
         view 294 legs   r1 = -0.119      view 357 (talker)  r1 = +0.297
         view 274 legs   r1 = +0.075      synthetic white noise  r1 = -0.172
                                          synthetic smooth ramp  r1 = +0.213

     The player walkers sit AT OR BELOW the white-noise reference. Their high-frequency
     content is per-pixel independent: it is noise, with a standard deviation of about 25
     levels out of 255. The talker portraits - the cels tools/restore_faces.py demonstrably
     improved with GPEN - sit far above it. That one table explains the whole history of
     this problem:

       * Mitchell looks soft because it is low-passing 25 levels of noise away. The blur
         everyone objects to is doing a job.
       * Every sharpener tried on these sprites - RealESRGAN, 4x-UltraSharp,
         4x-ClearRealityV1 (restore_faces.py's table) and now img2img at 0.2-0.5 with and
         without pinning - re-exposes or elaborates that noise, because at 39x95 the noise
         IS the high-frequency content. There is nothing else up there to find.
       * GPEN worked on the talkers because their high frequencies are real AND because a
         face fills 68x55 there. On a walker the whole head is about 14x14 and the face
         inside it is a pink blob with two dark bars for sunglasses.

THE METHOD THAT FOLLOWS FROM THAT (--method bilat, the default)
--------------------------------------------------------------
If the problem is noise, the operation is an EDGE-PRESERVING DENOISE AT 1x, before any
upscale: it removes the noise the way a blur does, but keeps the few genuine edges the blur
also destroys, and afterwards there is something left that can be sharpened without
amplifying grain.

    1. a small bilateral filter over the 1x cel, visible pixels only
    2. Lanczos to exactly `factor` x
    3. a mild unsharp mask

Nothing is invented; it is deterministic, it is arithmetic on Sierra's own pixels, it runs
on the CPU in under a second a cel, and having no per-frame randomness it cannot introduce
flicker. --bilat sweeps the two parameters that matter: the bilateral's range sigma (how
different two pixels may be and still be averaged together) and the unsharp amount.

Averaged over views 280/284/290/294/274, all four cels each:

    variant        structure  roundtrip    step    boil
    mitchell          0.9756       7.74   29.34   3.488
    bilat 14/0.6      0.9922       5.91   34.67   2.971
    bilat 18/0.9      0.9912       6.00   35.06   2.964   <- chosen
    bilat 22/0.9      0.9888       6.57   34.53   2.877
    bilat 22/1.2      0.9895       6.30   35.44   2.979
    bilat 30/0.9      0.9832       7.56   33.59   2.775

EVERY variant beats Mitchell on structure, on roundtrip AND on boil. The boil result is the
one worth pausing on, because it is the opposite of what every previous attempt on these
sprites did: removing the noise LOWERS the frame-to-frame change on pixels the source held
still, from 3.49 to about 2.9, because a good part of what Mitchell was faithfully carrying
from cel to cel was the noise differing between cels. `step` rises (29.3 -> 35.1) simply
because a sharper picture has more contrast everywhere, including across the real motion.

    THE METRICS CANNOT PICK THE WINNER WITHIN THIS TABLE AND IT WOULD BE DISHONEST TO LET
    THEM. structure and roundtrip both measure fidelity to a source that is 25 levels of
    noise, so they reward the variant that denoises LEAST - which is why 14/0.6 tops both
    columns and 30/0.9 sits at the bottom. They are the right test for "has this repainted
    the person", which is what the diffusion attempt failed, and the wrong test for "how
    much noise should come out". 18/0.9 is an eye's choice from the 1:1 sheets: 14/0.6
    still carries speckle on the trousers, 30/0.9 has begun flattening the lapel fold and
    the cardigan texture into slabs. A human should confirm or overrule it.

WHAT THE DIFFUSION PATH DID, AND WHY IT IS NOT THE DEFAULT (--method img2img)
----------------------------------------------------------------------------
The idea was sound and is worth stating properly, because it is not the same mistake
tools/gen_walkers.py made. There the model never saw Sierra's pixels: it measured a
skeleton, threw the cel away, and invented a person to stand in that pose. Here THE
ORIGINAL CEL IS THE INIT IMAGE - at strength s, (1-s) of the latent that survives into the
result is the 1990 cel - with a soft-edge ControlNet built from the cel's own gradient on
top. That is exactly what tools/gen_board.py did to the town's ground, and it worked there
(0.9543 gradient correlation at strength 0.62).

It fails here anyway, and the noise measurement says why: the model has nothing to work
from but noise, so it interprets the noise as texture and elaborates it.

    probe A, cfg 6.0, no pinning     the flat charcoal suit came back as LEOPARD PRINT,
                                     worse with strength. The prompt had asked for
                                     "detailed fabric texture", and the only way to obey
                                     that over a region with no detail is to invent one.
    probe C, + pin_to_mitchell       the speckle is gone - the structure mask refuses the
                                     model's output wherever the 1x source is flat - but
                                     what survives is a regular diagonal WEAVE printed
                                     across the face, which is the same invention arriving
                                     through the one door left open.

The strength sweep, view 280 cel 0, against the Mitchell baseline of 0.9797 / 7.35:

    strength   probe A structure/roundtrip   probe C (+pin) structure/roundtrip
    0.20            0.9734   8.65                 0.9804   7.34
    0.30            0.9646  10.01                 0.9802   7.41
    0.40            0.9505  11.91                 0.9798   7.53
    0.50            0.9382  13.42                 0.9790   7.66
    --------------------------------------------------------------------------
    bilat 18/0.9    0.9923   5.63   <- the CPU method, same cel, same statistics

Unpinned, every strength is worse than Mitchell and monotonically worse as strength rises -
there is no sweet spot to find, the trend has no minimum inside the range. Pinned, every
strength is indistinguishable from Mitchell to three decimal places, which is what pinning
the low frequencies guarantees; it buys the cross-hatch and nothing else. Both cost 41
seconds a cel of GPU against under a second for the bilateral. See
tools/samples/restore_char_280_strengths_probeA_cfg6.png and ..._probeC_pin.png.

WHY THIS IS A DIFFERENT THING FROM tools/gen_walkers.py
------------------------------------------------------
gen_walkers.py never showed the model Sierra's pixels. It measured a skeleton, threw the
cel away, and asked SD1.5 to invent a person who happened to stand in that pose. What came
back was a different cast with deformed hands, and it was rejected.

Here the ORIGINAL CEL IS THE INIT IMAGE. img2img at strength s re-noises the init to s of
the way back to pure noise and denoises from there, so at s=0.3 seventy per cent of the
latent that survives into the result is the 1990 cel. Identity, pose, clothing, colour and
silhouette are not decisions the model gets to make; they arrive with the init. A soft-edge
ControlNet built from the cel's own gradient holds the layout on top of that.

This is exactly what tools/gen_board.py did to the town's ground (strength 0.62, control
0.90, 0.9543 gradient correlation with the original) - and the board is a 320x200 map. A
39x95 figure has a twentieth of the pixels, so the strength has to be much lower, and how
much lower is the only real question. --strengths sweeps it.

THE THREE THINGS THAT CANNOT MOVE
---------------------------------
  1. THE SILHOUETTE. Alpha is copied verbatim from assets/png12x - the Mitchell twin that
     ships today - so the cutout is byte-identical to the art on screen now and nothing can
     grow a white matte fringe. Only RGB is replaced. (The rejected set's white fragments
     were a matte the generator had to invent; here there is nothing to invent.)
  2. THE SIZE. SciArt/RenderScale reject a twin that is not EXACTLY `Factor x` the 1x cel
     and fall back to the 1x bitmap, so a 39x95 cel must come out 468x1140. Asserted on
     every write.
  3. THE WALK CYCLE. Four cels play at 183ms. Every cel of a view is denoised from the SAME
     SEED, so the model's noise is identical frame to frame and the only thing that differs
     between them is the init - i.e. the legs actually moving. --measure reports the boil.

THE CANVAS
----------
The cel is Lanczos-resized to a canvas 1088 tall (multiple of 8, aspect preserved to the
nearest 8 px) and the result is Lanczos-resized to exactly `factor` x. 1088 is chosen to be
slightly UNDER the 12x target of 1140 so the diffusion pass is the detail-bearing stage and
the final resize is a mild downsample rather than a blow-up of a smaller canvas. The stretch
into the canvas is undone exactly by the inverse resize, the same round trip
tools/restore_faces.py makes through GPEN's 512x512.

ALPHA UNDERPAINT
----------------
The RGB hiding under transparent pixels is flooded outward from the nearest visible colour
before the model sees it, as in tools/restore_faces.py: the SCI decode leaves palette
garbage there and a convolution has no alpha channel to consult.

USAGE
    # the method that wins; CPU only, seconds
    python tools/restore_walkers.py --views 280,284,290,294,274 --cels 0,1,2,3 \
        --bilat 14/0.6,22/0.9,30/1.2 --out <scratch> --samples tools/samples

    # the method that does not; needs the GPU, 41s a cel
    python tools/restore_walkers.py --method img2img --strengths 0.2,0.3,0.4,0.5 \
        --pin --out <scratch> --samples tools/samples --tag probeC_pin
"""

import argparse
import json
import os
import sys
import time

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PNG1X = os.path.join(ROOT, "assets", "png")
CACHE = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
                     "jones-upscale-models")
MODELS = os.path.join(CACHE, "gen")

LOOP = 0
STILL_TOL = 2          # tools/smooth_walk.py's definition of a pixel the source held still
CANVAS_H = 1088        # diffusion canvas height; multiple of 8, just under 95*12

# The four player bodies at their best-dressed grade, plus the demo walker.
PLAYER_BASES = [280, 284, 290, 294]
JONES_VIEWS = [274, 275, 276, 277]

# THE PROMPT NAMES NO PERSON. Everything that identifies the figure - who they are, what
# they are wearing, what colour any of it is - comes from the init image. The prompt only
# says what KIND of picture this is.
#
# "DETAILED FABRIC TEXTURE" WAS IN HERE AND IT HAD TO COME OUT. It is the obvious thing to
# ask a restoration for and it is exactly wrong: the first probe (280 cel 0, strength 0.30,
# cfg 6.0) came back with the flat charcoal suit turned into leopard print, because the only
# way to obey "detailed fabric texture" over a region that has no detail is to invent one.
# The negative prompt now names that failure in six ways. The lesson generalises - anything
# here that asks for detail will be honoured in the flat areas first, since that is where
# there is the most room to add it.
PROMPT = "a photograph of a person standing, sharp focus, clean edges, even daylight"

# Views 274-277 are FLAT CEL-SHADED CARTOON art, not digitised photographs (see the table in
# tools/restore_faces.py). A photographic prior on them broke the hard black outlines and
# turned the brown shoes gold. They get a cartoon prompt instead.
PROMPT_CARTOON = ("a clean flat cel shaded cartoon of a person standing, bold crisp black "
                  "outlines, flat solid colour")

NEGATIVE = ("mottled, speckled, blotchy, dappled, patterned fabric, camouflage, film grain, "
            "texture overlay, blurry, out of focus, pixelated, jpeg artifacts, noise, "
            "lowres, text, watermark, deformed hands, extra limbs, extra fingers, "
            "two people, cropped, sitting, oversaturated, frame, border, vignette")


# ----------------------------------------------------------------------------------
# image plumbing (fill_transparent and soft_edges are lifted from restore_faces.py and
# gen_board.py respectively so this script has no import-time dependency on either)
# ----------------------------------------------------------------------------------

def fill_transparent(rgb, alpha, iters=64):
    """Flood the RGB under transparent pixels outward from the nearest visible colour."""
    out = rgb.astype(np.float32).copy()
    known = (alpha > 0).astype(np.float32)
    out[known == 0] = 0.0
    for _ in range(iters):
        if known.all():
            break
        acc = np.zeros_like(out)
        cnt = np.zeros_like(known)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            acc += np.roll(np.roll(out, dy, 0), dx, 1) * np.roll(np.roll(known, dy, 0), dx, 1)[..., None]
            cnt += np.roll(np.roll(known, dy, 0), dx, 1)
        fresh = (cnt > 0) & (known == 0)
        out[fresh] = (acc / np.maximum(cnt, 1)[..., None])[fresh]
        known[fresh] = 1.0
    return np.clip(out, 0, 255).astype(np.uint8)


def soft_edges(im):
    """A soft-edge conditioning map from the image's own gradient (gen_board.py)."""
    g = np.asarray(im.convert("L"), np.float32)
    gx = np.zeros_like(g)
    gy = np.zeros_like(g)
    gx[:, 1:-1] = g[:, 2:] - g[:, :-2]
    gy[1:-1, :] = g[2:, :] - g[:-2, :]
    mag = np.hypot(gx, gy)
    hi = np.percentile(mag, 99.0)
    mag = np.clip(mag / max(1.0, hi), 0.0, 1.0) ** 0.7
    e = Image.fromarray((mag * 255.0).astype(np.uint8), "L").filter(
        ImageFilter.GaussianBlur(0.8))
    return Image.merge("RGB", (e, e, e))


def canvas_for(w, h, ch=CANVAS_H):
    """Diffusion canvas: `ch` tall, aspect preserved to the nearest multiple of 8."""
    cw = int(round(w / float(h) * ch / 8.0)) * 8
    return max(64, cw), ch


def box_down(a, s):
    """Exact s-fold box downsample. a is HxWxC float."""
    h, w, c = a.shape
    assert h % s == 0 and w % s == 0, (a.shape, s)
    return a.reshape(h // s, s, w // s, s, c).mean(axis=(1, 3))


def erode(mask, r):
    for _ in range(r):
        mask = (mask & np.roll(mask, 1, 0) & np.roll(mask, -1, 0)
                & np.roll(mask, 1, 1) & np.roll(mask, -1, 1))
    return mask


def grad(v):
    gx = np.zeros_like(v)
    gy = np.zeros_like(v)
    gx[:, 1:-1] = v[:, 2:] - v[:, :-2]
    gy[1:-1, :] = v[2:, :] - v[:-2, :]
    return np.hypot(gx, gy)


def luma(rgb):
    return rgb.astype(np.float32) @ np.array([0.299, 0.587, 0.114], np.float32)


def gauss(a, sigma):
    """Separable Gaussian over the first two axes, edges repeated (smooth_walk.py)."""
    r = max(1, int(sigma * 3.0))
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2)
    k /= k.sum()
    single = a.ndim == 2
    if single:
        a = a[..., None]
    p = np.pad(a, ((r, r), (0, 0), (0, 0)), mode="edge")
    o = sum(k[i] * p[i:i + a.shape[0]] for i in range(2 * r + 1))
    p = np.pad(o, ((0, 0), (r, r), (0, 0)), mode="edge")
    o = sum(k[i] * p[:, i:i + a.shape[1]] for i in range(2 * r + 1))
    return o[..., 0] if single else o


def local_std(g, r=1):
    p = np.pad(g, r, mode="edge")
    n = (2 * r + 1) ** 2
    s = np.zeros_like(g)
    s2 = np.zeros_like(g)
    for dy in range(2 * r + 1):
        for dx in range(2 * r + 1):
            w = p[dy:dy + g.shape[0], dx:dx + g.shape[1]]
            s += w
            s2 += w * w
    m = s / n
    return np.sqrt(np.maximum(s2 / n - m * m, 0.0))


def smoothstep(lo, hi, x):
    t = np.clip((x - lo) / max(hi - lo, 1e-6), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def pin_to_mitchell(res_rgb, mit_rgb, src_rgba, factor):
    """Mitchell's COLOUR, the diffusion's HIGH FREQUENCIES, masked by structure in the 1x
    source. tools/smooth_walk.py's `sharpen` with a diffusion result in place of ESRGAN's.

    WHY IT IS WORTH RETRYING HERE WHEN IT WAS NOT ENOUGH THERE. smooth_walk's note is that
    the mask "cannot tell the model's own artefacts from detail, because at 4x they look
    like detail" - ESRGAN's damage is ringing ON the edges, which is precisely where the
    mask is wide open. Diffusion fails the opposite way round: its damage is invented
    texture in the FLAT regions, which is precisely where the mask is shut. So the same
    mask that could not save ESRGAN is aimed correctly at this failure.

    Both bands are measured against the 1x source's own local contrast: below ~3 levels
    there is nothing to sharpen, so anything added is invention; above ~26 it is a hard
    palette edge where these models ring. Scaled from smooth_walk's 4x to this factor.

    All arrays float 0-1; src_rgba uint8 at 1x.
    """
    k = factor / 4.0
    hf = (res_rgb - gauss(res_rgb, 1.6 * k)) - (mit_rgb - gauss(mit_rgb, 1.6 * k))
    lv = local_std(luma(src_rgba[..., :3]))
    m = smoothstep(3.0, 9.0, lv) * (1.0 - smoothstep(26.0, 44.0, lv))
    m = gauss(np.repeat(np.repeat(m, factor, 0), factor, 1), 1.2 * k)
    return np.clip(mit_rgb + m[..., None] * hf, 0.0, 1.0)


def bilateral_1x(rgb, alpha, r=1, sr=22.0, ss=1.2):
    """A small bilateral filter over the 1x cel. THE VISIBILITY TEST IS NOT OPTIONAL: a
    transparent neighbour carries whatever the palette happened to hold there, and letting
    it vote drags that garbage into the edge - the same hazard tools/restore_faces.py's
    flood exists to avoid, arriving from the other direction.

    `sr` is the whole parameter. It is how different two pixels may be, in luma levels, and
    still be averaged together. The noise is about 25 levels, so below ~14 the filter
    refuses to average the noise away and the result is nearly the source; far above ~30 it
    starts averaging across real edges and the figure goes plastic. --bilat sweeps it."""
    a = rgb.astype(np.float32)
    vis = (alpha > 0).astype(np.float32)
    g = luma(a)
    acc = np.zeros_like(a)
    wsum = np.zeros(a.shape[:2], np.float32)
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            sh = np.roll(np.roll(a, dy, 0), dx, 1)
            shg = np.roll(np.roll(g, dy, 0), dx, 1)
            shv = np.roll(np.roll(vis, dy, 0), dx, 1)
            w = np.exp(-0.5 * ((dy * dy + dx * dx) / (ss * ss)
                               + ((shg - g) / sr) ** 2)) * shv
            acc += sh * w[..., None]
            wsum += w
    out = acc / np.maximum(wsum, 1e-6)[..., None]
    return np.where(vis[..., None] > 0, out, a)


def restore_cel_bilat(view, cel, sr, us, args):
    """1x bilateral -> Lanczos to exactly factor x -> mild unsharp. -> rgba uint8.

    THE UNSHARP RUNS AT factor x, NOT AT 1x, and the order matters. Sharpening the 39x95
    cel first would put the halo in the source pixels, where Lanczos then spreads it over
    twelve screen pixels and it reads as an outline. Sharpening after the resize puts the
    halo at the scale of the resize kernel, where it is what makes the edge look crisp
    rather than what makes it look traced."""
    p1, pm = cel_paths(view, cel, args.factor)
    src = np.asarray(Image.open(p1).convert("RGBA"))
    h, w = src.shape[:2]
    mit = np.asarray(Image.open(pm).convert("RGBA"))
    if mit.shape[:2] != (h * args.factor, w * args.factor):
        raise SystemExit("%s is %dx%d, expected %dx%d"
                         % (pm, mit.shape[1], mit.shape[0], w * args.factor, h * args.factor))

    clean = bilateral_1x(src[..., :3], src[..., 3], sr=sr)
    im = Image.fromarray(np.clip(clean, 0, 255).astype(np.uint8), "RGB")
    big = np.asarray(im.resize((w * args.factor, h * args.factor), Image.LANCZOS),
                     np.float32) / 255.0
    if us > 0:
        big = np.clip(big + us * (big - gauss(big, 1.4 * args.factor / 4.0)), 0.0, 1.0)

    rgba = np.dstack([(big * 255.0 + 0.5).astype(np.uint8), mit[..., 3:4]])
    assert rgba.shape[:2] == (h * args.factor, w * args.factor), rgba.shape
    return rgba.astype(np.uint8)


# ----------------------------------------------------------------------------------
# the pass
# ----------------------------------------------------------------------------------

def cel_paths(view, cel, factor):
    return (os.path.join(PNG1X, "view_%d_l%d_c%d.png" % (view, LOOP, cel)),
            os.path.join(ROOT, "assets", "png%dx" % factor,
                         "view_%d_l%d_c%d.png" % (view, LOOP, cel)))


def restore_cel(img_pipe, view, cel, strength, args, torch):
    """-> (rgba uint8 at exactly factor x, the raw canvas result)"""
    p1, pm = cel_paths(view, cel, args.factor)
    src = np.asarray(Image.open(p1).convert("RGBA"))
    h, w = src.shape[:2]
    mit = np.asarray(Image.open(pm).convert("RGBA"))
    if mit.shape[:2] != (h * args.factor, w * args.factor):
        raise SystemExit("%s is %dx%d, expected %dx%d"
                         % (pm, mit.shape[1], mit.shape[0], w * args.factor, h * args.factor))

    filled = fill_transparent(src[..., :3], src[..., 3])
    cw, ch = canvas_for(w, h)
    init = Image.fromarray(filled, "RGB").resize((cw, ch), Image.LANCZOS)
    cond = soft_edges(init) if args.control > 0 else Image.new("RGB", (cw, ch), (0, 0, 0))

    prompt = PROMPT_CARTOON if view in JONES_VIEWS else PROMPT
    # ONE SEED PER VIEW, not per cel. The four cels of a walk cycle differ only in where the
    # legs are; giving them different noise makes the model repaint the torso differently in
    # each, which is flicker at 183ms. Identical noise means identical treatment of anything
    # the source held still, and --measure reports what is left.
    g = torch.Generator("cuda" if torch.cuda.is_available() else "cpu").manual_seed(args.seed + view)
    out = img_pipe(prompt=prompt, negative_prompt=NEGATIVE, image=init, control_image=cond,
                   strength=strength,
                   num_inference_steps=max(args.steps, int(args.steps / max(strength, 0.05))),
                   guidance_scale=args.cfg,
                   controlnet_conditioning_scale=args.control,
                   generator=g).images[0]

    small = out.resize((w * args.factor, h * args.factor), Image.LANCZOS)
    rgb = np.asarray(small.convert("RGB"), np.float32) / 255.0
    if args.pin:
        rgb = pin_to_mitchell(rgb, mit[..., :3].astype(np.float32) / 255.0, src, args.factor)
    rgba = np.dstack([(rgb * 255.0 + 0.5).astype(np.uint8), mit[..., 3:4]])
    assert rgba.shape[:2] == (h * args.factor, w * args.factor), rgba.shape
    return rgba.astype(np.uint8), out


# ----------------------------------------------------------------------------------
# measurement
# ----------------------------------------------------------------------------------

def still_mask(srcs, factor):
    """The pixels the SOURCE holds still across the whole loop, at factor-x.

    THE STRICT MASK IS EMPTY ON THESE SPRITES AND THAT IS A MEASUREMENT, NOT A BUG.
    tools/smooth_walk.py already records that view 296 has no byte-still pixel, because the
    walkers are dithered digitised photographs. Counted here at 1x, over the four cels of
    each loop, the `<= 2 levels` mask survives erosion by one pixel in NONE of views 280,
    284, 290 or 294:

        view   visible px   still@tol2   ...after a 1px erosion
        280        1918          12               0
        284        1966          65               0
        290        2083          22               0
        294        2273         105               0
        274        1430         130              24    (flat cartoon art, so it has some)

    So the tolerance is walked up until something survives, and the tolerance that was
    needed is returned and printed. A boil measured at tol 32 is a much weaker statement
    than one measured at tol 2, and pretending otherwise by quoting the number alone would
    be exactly the kind of flattering figure CLAUDE.md rule 6 is about. `step` below is the
    metric that is always defined, and it is the one to read.
    """
    if len(srcs) < 2 or not all(s.shape == srcs[0].shape for s in srcs):
        return None, 0, None
    stack = np.stack([s[..., :3].astype(np.int16) for s in srcs])
    rng = (stack.max(axis=0) - stack.min(axis=0)).max(axis=2)
    vis = np.stack([s[..., 3] for s in srcs]).min(axis=0) > 0
    for tol in (STILL_TOL, 4, 8, 16, 32, 48):
        m1 = erode((rng <= tol) & vis, 1)
        if m1.sum() >= 8:
            m = erode(np.repeat(np.repeat(m1, factor, 0), factor, 1), factor // 2)
            if m.sum() >= 50:
                return m, int(m.sum()), tol
    return None, 0, None


def measure_view(srcs, bigs, factor, mask_info=None):
    """srcs: list of 1x RGBA uint8. bigs: list of factor-x RGBA uint8, same order.

    Returns dict with:

      structure   correlation of gradient magnitude between the result box-downsampled to
                  1x and the untouched 1x source, over the VISIBLE INTERIOR only. The
                  silhouette edge is excluded because alpha is copied verbatim, so including
                  it would score the one thing that cannot differ and flatter every variant
                  equally. tools/gen_board.py's board number (0.9543) is this statistic.
      roundtrip   mean abs error 0-255 after box-downsampling the result back to 1x
                  (tools/restore_faces.py). Low means detail was ADDED without repainting.
      step        mean abs change between ADJACENT cels over every visible pixel. This is
                  what the eye actually sees change at 183ms, and unlike `boil` it is always
                  defined. Mitchell's step is the walk cycle's REAL motion; a variant whose
                  step is much higher than Mitchell's has added that difference as flicker.
      boil        the strict version: the same change restricted to pixels the source held
                  still. See still_mask - on four of these five views it needs a tolerance
                  so loose that the mask is no longer really "still".
    """
    rts, corrs = [], []
    for src, big in zip(srcs, bigs):
        back = box_down(big[..., :3].astype(np.float32), factor)
        vis = src[..., 3] > 0
        inner = erode(vis, 1)
        if inner.sum() < 30:
            inner = vis
        rts.append(np.abs(back - src[..., :3].astype(np.float32)).mean(axis=2)[vis].mean())
        ga, gb = grad(luma(back))[inner], grad(luma(src[..., :3]))[inner]
        if ga.std() > 1e-6 and gb.std() > 1e-6:
            corrs.append(float(np.corrcoef(ga, gb)[0, 1]))

    step, boil, npx, tol = float("nan"), float("nan"), 0, None
    if len(bigs) > 1 and all(b.shape == bigs[0].shape for b in bigs):
        m, npx, tol = mask_info if mask_info is not None else still_mask(srcs, factor)
        anyvis = np.stack([b[..., 3] for b in bigs]).min(axis=0) > 0
        ds, db = [], []
        for i in range(len(bigs)):
            j = (i + 1) % len(bigs)
            d = np.abs(bigs[i][..., :3].astype(np.float32)
                       - bigs[j][..., :3].astype(np.float32)).mean(axis=2)
            ds.append(d[anyvis].mean())
            if m is not None:
                db.append(d[m].mean())
        step = float(np.mean(ds))
        if db:
            boil = float(np.mean(db))

    return dict(structure=float(np.mean(corrs)) if corrs else float("nan"),
                roundtrip=float(np.mean(rts)), step=step, boil=boil,
                still_px=npx, still_tol=tol)


# ----------------------------------------------------------------------------------
# samples
# ----------------------------------------------------------------------------------

def on_shade(rgba, shade=107):
    a = rgba[..., 3:4].astype(np.float32) / 255.0
    bg = np.full(rgba.shape[:2] + (3,), float(shade), np.float32)
    return np.clip(rgba[..., :3].astype(np.float32) * a + bg * (1 - a), 0, 255).astype(np.uint8)


def sheet(view, src1x, mit, variants, factor, path, label):
    """One column per treatment, AT 1:1 - a 12x cel is 1140 tall and thumbnailing it hides
    exactly the detail the sheet exists to show.

    Columns: the 1x original (nearest-neighbour to the same height, so its pixels are
    visible as pixels), the Mitchell twin that ships today, then one per strength."""
    cols = [("1x original (nearest)",
             np.asarray(Image.fromarray(on_shade(src1x)).resize(
                 (src1x.shape[1] * factor, src1x.shape[0] * factor), Image.NEAREST))),
            ("Mitchell (ships today)", on_shade(mit))]
    for name, big in variants:
        cols.append((name, on_shade(big)))

    hdr = 30
    w = sum(c.shape[1] for _, c in cols) + 12 * (len(cols) + 1)
    h = max(c.shape[0] for _, c in cols) + hdr + 24
    im = Image.new("RGB", (w, h), (24, 24, 24))
    d = ImageDraw.Draw(im)
    d.text((10, 8), "view %d cel 0   %s   all panels 1:1 at %dx"
           % (view, label, factor), fill=(255, 255, 160))
    x = 12
    for name, c in cols:
        im.paste(Image.fromarray(c), (x, hdr + 18))
        d.text((x + 2, hdr), name, fill=(200, 220, 255))
        x += c.shape[1] + 12
    im.save(path)
    return path


def gif(path, frames, delay, caption, half=True):
    ims = []
    for f in frames:
        i = Image.fromarray(on_shade(f))
        if half:
            i = i.resize((max(1, i.width // 2), max(1, i.height // 2)), Image.LANCZOS)
        lab = Image.new("RGB", (max(i.width, 250), i.height + 16), (18, 18, 18))
        lab.paste(i, (0, 16))
        ImageDraw.Draw(lab).text((3, 3), caption, fill=(255, 255, 140))
        ims.append(lab)
    ims[0].save(path, save_all=True, append_images=ims[1:], duration=delay, loop=0,
                disposal=2, optimize=False)
    return path


# ----------------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--views", default="280,284,290,294,274")
    ap.add_argument("--cels", default="0,1,2,3")
    ap.add_argument("--method", choices=("bilat", "img2img"), default="bilat",
                    help="bilat (default) is the one that beats Mitchell and needs no GPU; "
                         "img2img is kept so the measurements that condemn it can be redone")
    ap.add_argument("--bilat", default="14/0.6,22/0.9,30/1.2",
                    help="bilateral sweep, as rangeSigma/unsharpAmount pairs")
    ap.add_argument("--strengths", default="0.2,0.3,0.4,0.5",
                    help="img2img sweep")
    ap.add_argument("--out", required=True, help="scratch root; one subdir per variant")
    ap.add_argument("--samples", default=None)
    ap.add_argument("--factor", type=int, default=12)
    ap.add_argument("--steps", type=int, default=20)
    ap.add_argument("--cfg", type=float, default=6.0)
    ap.add_argument("--pin", action="store_true",
                    help="keep Mitchell's low frequencies and take only the diffusion's "
                         "high ones, masked by structure in the 1x source (pin_to_mitchell)")
    ap.add_argument("--tag", default=None,
                    help="suffix for the sample filenames, so probes do not overwrite "
                         "each other")
    ap.add_argument("--control", type=float, default=0.9)
    ap.add_argument("--controlnet", default=os.path.join(MODELS, "cn-softedge"))
    ap.add_argument("--seed", type=int, default=90210)
    ap.add_argument("--measure-only", action="store_true",
                    help="re-measure what is already in --out, run no diffusion")
    a = ap.parse_args()

    views = [int(v) for v in a.views.split(",") if v.strip()]
    cels = [int(c) for c in a.cels.split(",") if c.strip()]
    os.makedirs(a.out, exist_ok=True)

    # A job is (column label, output subdirectory, a function making one cel). Building them
    # up front means the two methods share the whole loop below, so a number printed for one
    # is the same statistic computed the same way as the number printed for the other -
    # which is the only reason they can be compared at all.
    #
    # "none" runs the Mitchell baseline alone; that is how the metric code is smoke-tested
    # without spending a GPU minute.
    jobs = []
    img = torch = None
    if a.method == "bilat":
        for spec in a.bilat.split(","):
            if not spec.strip() or spec.strip().lower() == "none":
                continue
            sr, us = (float(x) for x in spec.split("/"))
            jobs.append(("bilat %g/%.1f" % (sr, us), "b%02d_u%02d" % (int(sr), int(us * 10)),
                         lambda v, c, sr=sr, us=us: restore_cel_bilat(v, c, sr, us, a)))
    else:
        strengths = [float(s) for s in a.strengths.split(",")
                     if s.strip() and s.strip().lower() != "none"]
        if strengths and not a.measure_only:
            import torch as _torch
            torch = _torch
            sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
            import gen_walkers as gw
            print("loading SD1.5 + soft-edge ControlNet ...", flush=True)
            t0 = time.time()
            _txt, img = gw.build("cuda" if torch.cuda.is_available() else "cpu",
                                 offload=True, controlnet=a.controlnet)
            print("  %.0fs" % (time.time() - t0), flush=True)
        for s in strengths:
            jobs.append(("img2img %.2f" % s, "s%.2f" % s,
                         lambda v, c, s=s: restore_cel(img, v, c, s, a, torch)[0]))

    rows = []
    for view in views:
        srcs, mits = [], []
        for c in cels:
            p1, pm = cel_paths(view, c, a.factor)
            srcs.append(np.asarray(Image.open(p1).convert("RGBA")))
            mits.append(np.asarray(Image.open(pm).convert("RGBA")))

        # The still mask is a property of the SOURCE, so it is computed once and handed to
        # every treatment. Recomputing it per variant would be the same answer; passing it
        # makes it impossible for a variant to be scored against a different mask.
        mask_info = still_mask(srcs, a.factor)

        base = measure_view(srcs, mits, a.factor, mask_info)
        rows.append(dict(view=view, treatment="mitchell", strength=None, **base))
        print("view %-4d %-13s structure %.4f  roundtrip %6.2f  step %6.3f  boil %6.3f"
              "  (%d still px @tol %s)"
              % (view, "mitchell", base["structure"], base["roundtrip"], base["step"],
                 base["boil"], base["still_px"], base["still_tol"]), flush=True)

        variants = []
        for label, slug, make in jobs:
            sd = os.path.join(a.out, slug)
            os.makedirs(sd, exist_ok=True)
            bigs = []
            for c in cels:
                dst = os.path.join(sd, "view_%d_l%d_c%d.png" % (view, LOOP, c))
                if a.measure_only or os.path.exists(dst):
                    bigs.append(np.asarray(Image.open(dst).convert("RGBA")))
                    continue
                t0 = time.time()
                rgba = make(view, c)
                Image.fromarray(rgba, "RGBA").save(dst)
                bigs.append(rgba)
                print("    %d c%d %s  %.1fs" % (view, c, slug, time.time() - t0), flush=True)
            st = measure_view(srcs, bigs, a.factor, mask_info)
            rows.append(dict(view=view, treatment=a.method, variant=label, **st))
            print("view %-4d %-13s structure %.4f  roundtrip %6.2f  step %6.3f  boil %6.3f"
                  % (view, label, st["structure"], st["roundtrip"], st["step"], st["boil"]),
                  flush=True)
            variants.append((label, slug, bigs))

        if a.samples:
            os.makedirs(a.samples, exist_ok=True)
            kind = "FLAT CARTOON art" if view in JONES_VIEWS else "digitised photograph"
            tag = ("_" + a.tag) if a.tag else ""
            sheet(view, srcs[0], mits[0], [(l, b[0]) for l, _, b in variants], a.factor,
                  os.path.join(a.samples, "restore_char_%d_strengths%s.png" % (view, tag)),
                  kind + ("   [%s]" % a.tag if a.tag else ""))
            # THE GIFS ARE NOT DECORATION. A still cannot show flicker, and flicker is the
            # failure mode that killed every previous attempt on these sprites. 183ms is the
            # game's own cadence for a walker cel (ticksToDo 10, per tools/smooth_walk.py).
            if len(cels) == 4:
                gif(os.path.join(a.samples, "restore_walk_%d_mitchell%s.gif" % (view, tag)),
                    mits, 180, "view %d  MITCHELL (ships today)  183ms" % view)
                for label, slug, fr in variants:
                    gif(os.path.join(a.samples,
                                     "restore_walk_%d_%s%s.gif" % (view, slug, tag)),
                        fr, 180, "view %d  %s  183ms" % (view, label))

    with open(os.path.join(a.out, "restore_walkers.metrics.json"), "w") as f:
        json.dump(rows, f, indent=1)

    print()
    print("structure  gradient correlation with the 1x original, visible interior only.")
    print("           Mitchell is the ceiling here by construction; how far a variant falls")
    print("           below it is how far it repainted the person.")
    print("roundtrip  mean abs error 0-255 after box-downsampling back to 1x. Mitchell ~4")
    print("           on the talkers; higher means the picture moved, not that it sharpened.")
    print("step       mean abs change between adjacent cels over every visible pixel. Read")
    print("           it AGAINST MITCHELL'S step, which is the walk cycle's real motion:")
    print("           the excess is what the treatment added as flicker.")
    print("boil       the same on pixels the source held still. On 280/284/290/294 the")
    print("           strict mask is EMPTY (see still_mask) and the tolerance printed above")
    print("           says how far it had to be loosened; treat it as weak evidence there.")
    print()
    print("None of these can tell you whether the result LOOKS better. Look at the sheets.")


if __name__ == "__main__":
    sys.exit(main())
