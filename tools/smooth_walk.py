r"""
Generate IN-BETWEEN frames for the board walker's four-cel walk cycle, and (optionally)
re-sharpen the walker cels with a masked super-resolution pass.

WHAT THIS IS NOT
----------------
Nothing here recovers anything. The resource file holds four pictures of the actor and
that is all it will ever hold. The in-between frames are MADE UP: optical flow guesses
where each pixel was travelling and slides it part of the way, so a frame that never
existed is manufactured out of the two that did. Where the guess is wrong it smears.
The super-resolution option is worse in kind - it INVENTS texture from a training set,
as tools/restore_faces.py says at length about the faces.

That is a deliberate trade and it is confined to the 4x art. assets/png (the 1x reference
decode) is never touched, the game's 320x200 coordinate space is untouched, no game timing
changes, and deleting every file this writes puts the animation back to the original's own
four frames with no code change.

    ORDERING HAZARD: tools/Upscale rewrites every ordinary file in assets/png4x with the
    Mitchell resample, and tools/restore_faces.py then replaces the RGB of the talker
    cels. This script reads the walker cels OUT of assets/png4x and interpolates between
    them, so its output is only as good as what was there when it ran.

        Upscale  ->  restore_faces.py  ->  smooth_walk.py

    Re-running Upscale does NOT delete this script's output - the in-between files have
    no 1x counterpart, so Upscale never enumerates them - which means they silently go
    STALE: the base cels revert to Mitchell while the frames between them still carry
    whatever they were built from, and the cycle alternates two different pictures.
    `--check` detects exactly that, and `--clean` removes them. Run it after any Upscale.

THE BUG THAT WAS ACTUALLY MAKING THE WALKER LOOK BAD
----------------------------------------------------
Before any of this: the port was cycling the walker's four cels once per marble step,
i.e. every 25ms, so the walk cycle played ten times a second. Sixty to seventy per cent
of the figure's pixels differ between adjacent cels, so at that rate a photographed actor
does not walk, he vibrates.

The original does not tie the cel rate to the marble at all. `theWalker` declares
`ticksToDo 10` (room1.sc:1060); `Cycle::nextCel` (Motion.sc:26-44) gates cel changes on
that against `GetTime`, which counts 60ths of a second, and the comparison is strict - so
a cel holds 11 ticks, about 183ms, and the cycle takes 0.73s. The Game Speed gauge
rewrites only the MARBLE's `ticksToDo`/`moveSpeed` (Menu.sc:307-310); nothing anywhere
writes the walker's.

That is fixed in `MainViewModel.WalkerFrame`, it needed no new art, and it is the larger
part of the improvement. What this script adds is the frames BETWEEN those four, because
183ms of the same picture at a 16ms redraw is a slideshow.

HOW THE EXTRA FRAMES REACH THE RENDERER
---------------------------------------
`SciArt.HighRes` rejects any high-resolution twin that is not exactly `RenderScale.Factor`
times the 1x bitmap, and an in-between frame has no 1x counterpart at all. So it does not
get one. `SciArt.SubCel(view, loop, cel, sub)` hands back ANOTHER INSTANCE of the ordinary
1x cel - which the view models measure exactly as before - and registers the in-between
file as that instance's twin. The twin table is keyed by reference, which is what lets one
cel stand for several 4x frames, and the 4x size guard still applies literally because an
in-between frame is generated at the size of the pair it sits between.

    assets/png4x/view_282_l0_c1.png       the cel          (Upscale / restore_faces)
    assets/png4x/view_282_l0_c1_s1.png    1/4 of the way to cel 2   <- this script
    assets/png4x/view_282_l0_c1_s2.png    2/4
    assets/png4x/view_282_l0_c1_s3.png    3/4

All four cels of every walker view are the same size (checked), so nothing on screen can
move as a result: the sprite's position is `160 - width/2`, and the width is the same
whichever frame is showing.

THE CYCLE IS A LOOP, so cel 3 interpolates round to cel 0, not just 0->1->2->3.

WHAT THE INTERPOLATION ACTUALLY DOES
------------------------------------
Pyramidal Lucas-Kanade optical flow between the two 4x cels, computed on the
alpha-premultiplied luma so the silhouette drives it, then the standard backward-warp
blend at each intermediate time:

    I_t(y) = (1-t) * A(y - t*F(y))  +  t * B(y + (1-t)*F(y))

...except that the two terms are not blended blind. Where the forward and backward warps
disagree the flow has failed, and averaging them draws the limb TWICE; there the weight is
pushed to the nearer real cel instead. See `tween`: on view 296, whose feet swing past each
other, that is the difference between a clean step and four feet.

RGB and ALPHA are warped together, premultiplied, so the outline moves with the figure
instead of the figure sliding inside a fixed hole, and the alpha stays soft to match the
Mitchell cels it sits between - see ALPHA_FLOOR for why binarising it was wrong.

It is deterministic - fixed number of pyramid levels and iterations, no random seed
anywhere - so the same cels always give the same in-betweens, which is half of temporal
consistency for free. The other half is that no texture is invented: the warp only moves
pixels that were already in one of the two real cels, so it cannot "boil" the way a
generative model does. The numbers below confirm it.

    --method dissolve is the fallback: a plain cross-fade, no flow. It cannot smear,
    because it does not move anything; it double-exposes instead. It is worse on the legs
    and better on nothing, and is kept only so the comparison can be re-made.

    It also BEATS THE FLOW ON step_after - 6.35 against 8.48 on view 282 - and that is
    not a recommendation, it is arithmetic. A cross-fade is linear in pixel space by
    construction, so its adjacent-frame difference is exactly 1/SUBS of the cel
    difference and nothing can score better. What it buys that number with is a limb
    fading out while another fades in, which is the one thing the exercise was trying to
    avoid. Third entry in this file's running argument against believing the table.

MEASUREMENTS
------------
Printed per view, all in 0-255 levels:

    step_before   mean abs change between ADJACENT DISPLAYED FRAMES over the four cels
                  of the loop - how big a jump the eye is asked to absorb.
    step_after    the same over the SUBS x 4 frames actually displayed now. The point of
                  the exercise: it should fall by roughly the factor SUBS.
    boil          mean abs deviation from the per-pixel mean, over the whole loop, on
                  pixels the 1x SOURCE held still (within 2 levels) in all four. The source
                  says nothing happens there, so anything above about 1.0 is the tool
                  inventing movement. Flow interpolation scores ~0 because it only moves
                  pixels; a generative model does not.
    endpoint      mean abs error of the interpolator evaluated at t=0 and t=1 against the
                  real cels it is meant to reproduce. A sanity check on the flow's sign
                  and the warp's sampling; anything but ~0 is a bug in this file.

WHAT WAS TRIED AND REJECTED FOR THE SPRITE ITSELF (--sharpen)
-------------------------------------------------------------
tools/restore_faces.py had already run RealESRGAN over these cels and reverted it. This
re-ran the question with the two models it had not tried, and with a blend that keeps
Mitchell's colour and takes only the model's high frequencies, masked by local structure
in the 1x source. Measured on views 282/292/296/274, all four cels:

    variant                 roundtrip   mad_stat   what it looks like
    mitchell (current)         5.3-9.6     0.00     soft; the logo is legible
    RealESRGAN_x4              9.4-13.9    1.2-1.7  logo -> red blob, face erased
    4x-UltraSharp              5.7-9.5     0.5-0.8  BEST NUMBERS, WORST PICTURE: orange
                                                    halos down both arms, invented grid
                                                    seams all over the denim, face gone
    blend(4x-UltraSharp)       5.1-9.4     0.00     BETTER THAN MITCHELL ON EVERY NUMBER
                                                    and still carries UltraSharp's orange
                                                    arms - the mask reads the artefact as
                                                    detail and faithfully preserves it
    4x-ClearRealityV1          5.6-10.4    0.3-1.0  raw, the only one that does no damage
    blend(4x-ClearRealityV1)      -           -     what --sharpen actually runs, and it
                                                    is WORSE than Mitchell to look at: the
                                                    T-shirt logo smears into a blob, the
                                                    arms mottle orange, and view 296's
                                                    printed blouse turns to muddy streaks
                                                    with invented veins down the shorts

That table is the whole argument for not trusting the metric here. `blend(4x-UltraSharp)`
beats Mitchell on roundtrip AND scores a perfect 0.00 for flicker and is plainly the worst
picture of the five; `4x-ClearRealityV1` is the only model that survives being looked at
raw, and putting it through the same blend spoils it.

So --sharpen is OFF by default and the walker ships on Mitchell, which is where
restore_faces.py left it for different reasons. It is reachable so the judgement can be
re-checked rather than repeated blind.

The honest summary is that 39x95 pixels of dithered 1990 photograph is all there is. What
made the walker look bad was never the resampler; it was the cadence, above.

MODELS (--sharpen only)
-----------------------
Cached in %LOCALAPPDATA%\jones-upscale-models, downloaded on first use:
    4x-ClearRealityV1.onnx  huggingface.co/skbhadra/ClearRealityV1          2MB
    4x-UltraSharp.onnx      huggingface.co/uwg/upscaler                    68MB
    RealESRGAN_x4.onnx      huggingface.co/yuvraj108c/ComfyUI-Upscaler-Onnx 68MB
CPU inference only (onnxruntime, no CUDA).

Usage:
    python smooth_walk.py                      write the in-between frames
    python smooth_walk.py --dry-run            measure and write samples, write nothing
    python smooth_walk.py --check              report stale or missing in-between frames
    python smooth_walk.py --clean              delete every in-between frame
    python smooth_walk.py --method dissolve    cross-fade instead of optical flow
    python smooth_walk.py --sharpen MODEL      also rewrite the cels (see above; default off)
    python smooth_walk.py --views 282,296      restrict to these views
    python smooth_walk.py --subs 4             frames per cel including the cel itself
    python smooth_walk.py --no-samples         skip the animated comparisons

Dependencies and how to get them onto this machine: see the bottom of
tools/restore_faces.py. pip's resolver hangs here; fetch wheels and install offline.
"""

import hashlib
import json
import os
import sys
import time
import urllib.request

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PNG1X = os.path.join(ROOT, "assets", "png")
PNG4X = os.path.join(ROOT, "assets", "png4x")
CACHE = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
                     "jones-upscale-models")
MANIFEST = os.path.join(PNG4X, "smooth_walk.manifest.json")
SCALE = 4

# Frames per cel INCLUDING the cel itself. Must match MainViewModel.WalkerSubFrames.
SUBS = 4

# The board walker: four character bodies, each with four clothing states, `+3` being the
# undressed one. `MainViewModel.BuildBoard` picks base + (Wearing - 34), or base + 3.
BODY_BASES = [280, 284, 290, 294]
PLAYER_VIEWS = [b + i for b in BODY_BASES for i in range(4)]
# The cartoon Jones figures. Flat-shaded art, 25 visible colours against the photographed
# players' 120-155 - which is why restore_faces.py's neural pass broke their hard black
# outlines, and why flow interpolation, which only moves pixels that are already there, is
# safe on them where a super-resolution model is not.
#
# NOTE: nothing in the port draws these today. `MainViewModel.BuildBoard` picks the view
# from `_whichBody`, which is 0-3, so the board walker is always one of the four players
# even in demo mode; Jones's own walker is a separate parity gap and not this script's
# business. The frames are generated anyway because they cost 48 files and are correct
# whenever that gets wired up.
JONES_VIEWS = [274, 275, 276, 277]
WALKER_VIEWS = PLAYER_VIEWS + JONES_VIEWS

CELS = 4          # every walker loop is four cels
LOOP = 0

MODELS = {
    "4x-ClearRealityV1.onnx":
        "https://huggingface.co/skbhadra/ClearRealityV1/resolve/main/4x-ClearRealityV1.onnx",
    "4x-UltraSharp.onnx":
        "https://huggingface.co/uwg/upscaler/resolve/main/ESRGAN/4x-UltraSharp.onnx",
    "RealESRGAN_x4.onnx":
        "https://huggingface.co/yuvraj108c/ComfyUI-Upscaler-Onnx/resolve/main/RealESRGAN_x4.onnx",
}

# Below this the pixel is treated as carrying no colour at all, so the un-premultiply
# cannot divide by something near zero and blow a near-transparent edge pixel up to a
# saturated colour.
#
# The alpha is NOT re-binarised. The 1x decode's transparency is hard - 2 distinct alpha
# values, no partials - but the Mitchell 4x twin's is deliberately SOFT: 166 distinct
# values and 5,586 partial pixels on view 282 cel 0, which is the whole point of
# resampling in premultiplied alpha. An in-between frame with a hard edge sitting between
# two cels with soft ones would pop the silhouette four times a cel. Interpolating the
# soft alpha is what makes the endpoint check below come out at zero.
ALPHA_FLOOR = 1.0 / 255.0

# How far a 1x source pixel may wander across the four cels and still count as "held
# still" for the boil measurement. See sequence_stats.
STILL_TOL = 2

# Premultiplied disagreement (0-1) between the forward and backward warps below which the
# flow is trusted and the two are blended, and above which the nearer cel is preferred
# outright. See `tween`. Tuned by looking at view 296's feet, which is the worst case in
# the set: at 0.02/0.08 the doubled foot is gone and nothing else visibly changed.
OCCLUSION_LO = 0.02
OCCLUSION_HI = 0.08


# ----------------------------------------------------------------------------------
# small image maths, all numpy, all deterministic
# ----------------------------------------------------------------------------------

def gauss(a, sigma):
    """Separable Gaussian over the first two axes, edges repeated."""
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


def box(a, r):
    """Mean over a (2r+1)^2 window, edges repeated."""
    p = np.pad(a, r, mode="edge")
    n = 2 * r + 1
    o = np.zeros_like(a)
    for dy in range(n):
        for dx in range(n):
            o += p[dy:dy + a.shape[0], dx:dx + a.shape[1]]
    return o / (n * n)


def half(a):
    """Downsample by 2 after a light blur, cropping an odd last row/column."""
    b = gauss(a, 1.0)
    return b[:b.shape[0] // 2 * 2:2, :b.shape[1] // 2 * 2:2]


def resize_to(a, shape):
    """Nearest-ish bilinear resize of a 2D field to `shape`."""
    h, w = shape
    yi = np.linspace(0, a.shape[0] - 1, h)
    xi = np.linspace(0, a.shape[1] - 1, w)
    y0 = np.floor(yi).astype(int); y1 = np.minimum(y0 + 1, a.shape[0] - 1)
    x0 = np.floor(xi).astype(int); x1 = np.minimum(x0 + 1, a.shape[1] - 1)
    fy = (yi - y0)[:, None]; fx = (xi - x0)[None, :]
    return ((a[np.ix_(y0, x0)] * (1 - fy) + a[np.ix_(y1, x0)] * fy) * (1 - fx)
            + (a[np.ix_(y0, x1)] * (1 - fy) + a[np.ix_(y1, x1)] * fy) * fx)


def warp(a, dx, dy):
    """Bilinear sample of `a` at (x + dx, y + dy). `a` is HxW or HxWxC, float."""
    single = a.ndim == 2
    if single:
        a = a[..., None]
    h, w = a.shape[:2]
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    sx = np.clip(xs + dx, 0, w - 1)
    sy = np.clip(ys + dy, 0, h - 1)
    x0 = np.floor(sx).astype(int); x1 = np.minimum(x0 + 1, w - 1)
    y0 = np.floor(sy).astype(int); y1 = np.minimum(y0 + 1, h - 1)
    fx = (sx - x0)[..., None]; fy = (sy - y0)[..., None]
    o = ((a[y0, x0] * (1 - fx) + a[y0, x1] * fx) * (1 - fy)
         + (a[y1, x0] * (1 - fx) + a[y1, x1] * fx) * fy)
    return o[..., 0] if single else o


def flow(a, b, levels=4, iters=6, win=6, smooth=3):
    """
    Pyramidal Lucas-Kanade. Returns (fx, fy) mapping A to B: the pixel at (x, y) in A is
    at (x + fx, y + fy) in B.

    Deterministic: the level count, the iteration count and the window are fixed, there is
    no randomness and no early exit, so identical inputs give identical flow.
    """
    pa, pb = [a], [b]
    for _ in range(levels - 1):
        if min(pa[-1].shape) < 16:
            break
        pa.append(half(pa[-1])); pb.append(half(pb[-1]))

    u = np.zeros(pa[-1].shape, np.float32)
    v = np.zeros(pa[-1].shape, np.float32)

    for lvl in range(len(pa) - 1, -1, -1):
        A, B = pa[lvl], pb[lvl]
        if u.shape != A.shape:
            sy = A.shape[0] / u.shape[0]
            sx = A.shape[1] / u.shape[1]
            u = resize_to(u, A.shape) * sx
            v = resize_to(v, A.shape) * sy
        for _ in range(iters):
            # Pull B back onto A with the flow so far; the residual drives the update.
            Bw = warp(B, u, v)
            It = Bw - A
            Iy, Ix = np.gradient(0.5 * (A + Bw))
            Sxx = box(Ix * Ix, win); Syy = box(Iy * Iy, win); Sxy = box(Ix * Iy, win)
            Sxt = box(Ix * It, win); Syt = box(Iy * It, win)
            det = Sxx * Syy - Sxy * Sxy + 1e-4
            u -= (Syy * Sxt - Sxy * Syt) / det
            v -= (Sxx * Syt - Sxy * Sxt) / det
            u = box(u, smooth); v = box(v, smooth)
    return u, v


# ----------------------------------------------------------------------------------
# interpolation
# ----------------------------------------------------------------------------------

def premul(rgba):
    """float RGBA 0-1 -> premultiplied RGB and alpha, so warping cannot drag the RGB
    hiding under transparent pixels into the visible edge."""
    a = rgba[..., 3:4]
    return rgba[..., :3] * a, a[..., 0]


def tween(fa, fb, t, method, cached_flow=None):
    """
    One in-between frame, `t` of the way from cel A to cel B. fa/fb are float RGBA 0-1.
    Returns float RGBA 0-1, alpha soft (see ALPHA_FLOOR).
    """
    ca, aa = premul(fa)
    cb, ab = premul(fb)

    if method == "dissolve":
        c = ca * (1 - t) + cb * t
        al = aa * (1 - t) + ab * t
    else:
        fx, fy = cached_flow
        # I_t(y) = (1-t) A(y - t F) + t B(y + (1-t) F)   -- see the header.
        sa = warp(np.dstack([ca, aa]), -t * fx, -t * fy)
        sb = warp(np.dstack([cb, ab]), (1 - t) * fx, (1 - t) * fy)

        # OCCLUSION. Where the two warps land on the same thing, the flow was right and a
        # straight blend is correct. Where they disagree the flow has FAILED - a foot
        # swinging past the other leg is the case that matters here - and blending them
        # then draws BOTH of them. That double-exposed foot scored a boil of 0.03, i.e.
        # perfectly stable, and still looked wrong, which is the whole reason this file
        # ships animations and not just a table.
        #
        # So the weight is pushed towards the NEARER of the two real cels in proportion to
        # how badly the two warps disagree. One foot in the wrong place beats two feet, and
        # the error is confined to the frames either side of the midpoint.
        conf = 1.0 - smoothstep(OCCLUSION_LO, OCCLUSION_HI, np.abs(sa - sb).mean(axis=2))
        conf = gauss(conf, 1.5)
        w = (conf * t + (1.0 - conf) * (0.0 if t < 0.5 else 1.0))[..., None]
        m = sa * (1.0 - w) + sb * w
        c, al = m[..., :3], m[..., 3]

    keep = al > ALPHA_FLOOR
    out = np.zeros(fa.shape, np.float32)
    out[..., :3][keep] = np.clip(c[keep] / al[keep, None], 0.0, 1.0)
    out[..., 3] = np.clip(al, 0.0, 1.0)
    return out


def flow_for(fa, fb):
    """Flow on the premultiplied luma: the silhouette is the strongest signal these
    39x95-at-4x sprites have, and it is the thing that must land in the right place."""
    def luma(f):
        g = f[..., :3] @ np.array([0.299, 0.587, 0.114], np.float32)
        return gauss(g * f[..., 3], 1.0).astype(np.float32)
    return flow(luma(fa), luma(fb))


# ----------------------------------------------------------------------------------
# optional sharpening pass (OFF by default - see the header)
# ----------------------------------------------------------------------------------

_SESSIONS = {}


def ensure_model(name):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, name)
    if os.path.exists(path) and os.path.getsize(path) > 1_000_000:
        return path
    print("  downloading %s ..." % name)
    tmp = path + ".part"
    with urllib.request.urlopen(MODELS[name], timeout=900) as r, open(tmp, "wb") as f:
        while True:
            chunk = r.read(1 << 20)
            if not chunk:
                break
            f.write(chunk)
    os.replace(tmp, path)
    return path


def session(name):
    import onnxruntime as ort
    ort.set_default_logger_severity(3)
    if name not in _SESSIONS:
        o = ort.SessionOptions()
        o.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
        _SESSIONS[name] = ort.InferenceSession(ensure_model(name), o,
                                               providers=["CPUExecutionProvider"])
    return _SESSIONS[name]


def fill_transparent(rgb, alpha, iters=64):
    """Flood the RGB under transparent pixels outward. Same reasoning as restore_faces."""
    out = rgb.astype(np.float32).copy()
    known = (alpha > 0).astype(np.float32)
    out[known == 0] = 0.0
    for _ in range(iters):
        if known.all():
            break
        acc = np.zeros_like(out); cnt = np.zeros_like(known)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            acc += (np.roll(np.roll(out, dy, 0), dx, 1)
                    * np.roll(np.roll(known, dy, 0), dx, 1)[..., None])
            cnt += np.roll(np.roll(known, dy, 0), dx, 1)
        fresh = (cnt > 0) & (known == 0)
        out[fresh] = (acc / np.maximum(cnt, 1)[..., None])[fresh]
        known[fresh] = 1.0
    return np.clip(out, 0, 255).astype(np.uint8)


def local_std(g, r=1):
    p = np.pad(g, r, mode="edge")
    n = (2 * r + 1) ** 2
    s = np.zeros_like(g); s2 = np.zeros_like(g)
    for dy in range(2 * r + 1):
        for dx in range(2 * r + 1):
            w = p[dy:dy + g.shape[0], dx:dx + g.shape[1]]
            s += w; s2 += w * w
    m = s / n
    return np.sqrt(np.maximum(s2 / n - m * m, 0.0))


def smoothstep(lo, hi, x):
    t = np.clip((x - lo) / max(hi - lo, 1e-6), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def sharpen(mit_rgb, src_rgba, model):
    """
    Mitchell's COLOUR, the model's HIGH FREQUENCIES, masked by structure in the 1x source.

    Pinning the low frequencies to Mitchell is what stops the colour damage every model in
    the table above does - oversaturated skin, brown shoes turned gold, grey spectacle
    rims turned blue - because a low-pass of the result is a low-pass of Mitchell by
    construction. The mask then refuses the model's detail where the 1x source is flat
    (nothing to sharpen, so anything added is invention) and where it is a hard palette
    edge (where these models ring and smear, which is what destroyed the T-shirt logo).

    It is still not good enough to ship: see the header. The mask cannot tell the model's
    own artefacts from detail, because at 4x they look like detail.
    """
    s = session(model)
    filled = fill_transparent(src_rgba[..., :3], src_rgba[..., 3])
    x = filled.astype(np.float32).transpose(2, 0, 1)[None] / 255.0
    y = s.run(None, {s.get_inputs()[0].name: x})[0]
    out = np.clip(y[0].transpose(1, 2, 0), 0.0, 1.0)

    hf = (out - gauss(out, 1.6)) - (mit_rgb - gauss(mit_rgb, 1.6))
    g = src_rgba[..., :3].astype(np.float32) @ np.array([0.299, 0.587, 0.114], np.float32)
    lv = local_std(g)
    m = smoothstep(3.0, 9.0, lv) * (1.0 - smoothstep(26.0, 44.0, lv))
    m = gauss(np.repeat(np.repeat(m, SCALE, 0), SCALE, 1), 1.2)
    return np.clip(mit_rgb + m[..., None] * hf, 0.0, 1.0)


# ----------------------------------------------------------------------------------
# file plumbing
# ----------------------------------------------------------------------------------

def base_name(view, cel):
    return "view_%d_l%d_c%d.png" % (view, LOOP, cel)


def sub_name(view, cel, sub):
    return "view_%d_l%d_c%d_s%d.png" % (view, LOOP, cel, sub)


def load_rgba(path):
    return np.asarray(Image.open(path).convert("RGBA"), dtype=np.float32) / 255.0


def save_rgba(path, f):
    Image.fromarray((np.clip(f, 0, 1) * 255.0 + 0.5).astype(np.uint8), "RGBA").save(path)


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()[:16]


def read_manifest():
    try:
        with open(MANIFEST, "r", encoding="utf-8") as fh:
            return json.load(fh)
    except Exception:
        return None


def existing_subs():
    if not os.path.isdir(PNG4X):
        return []
    return sorted(n for n in os.listdir(PNG4X)
                  if n.startswith("view_") and "_s" in n and n.endswith(".png"))


# ----------------------------------------------------------------------------------
# measurement
# ----------------------------------------------------------------------------------

def sequence_stats(srcs, base4x, frames):
    """
    srcs     the four 1x RGBA cels, uint8
    base4x   the four 4x cels, float RGBA 0-1, as displayed before this pass
    frames   the SUBS*4 frames as displayed after it
    """
    def step(seq):
        d = []
        for i in range(len(seq)):
            j = (i + 1) % len(seq)
            a, b = seq[i], seq[j]
            # Compare what is actually SEEN: premultiplied, so a pixel appearing or
            # vanishing counts as the change it is.
            d.append(np.abs(a[..., :3] * a[..., 3:4] - b[..., :3] * b[..., 3:4]).mean() * 255.0)
        return float(np.mean(d))

    # Pixels the SOURCE holds still across the WHOLE loop, eroded so the 4x neighbourhood
    # of a moving 1x pixel does not count as still.
    #
    # "Still" is a range of at most STILL_TOL levels rather than exact equality. These are
    # dithered digitised photographs and on some views - 296 is one - literally no visible
    # pixel is byte-identical in all four cels, so the exact test that restore_faces.py
    # could use on the talkers returns an empty mask and no number at all here.
    stack1 = np.stack([s[..., :3].astype(np.int16) for s in srcs])
    still = (stack1.max(axis=0) - stack1.min(axis=0)).max(axis=2) <= STILL_TOL
    still &= np.stack([s[..., 3] for s in srcs]).min(axis=0) > 0
    m = np.repeat(np.repeat(still, SCALE, 0), SCALE, 1)
    for _ in range(SCALE):
        m = m & np.roll(m, 1, 0) & np.roll(m, -1, 0) & np.roll(m, 1, 1) & np.roll(m, -1, 1)

    if m.sum() >= 50:
        stack = np.stack([f[..., :3] * f[..., 3:4] for f in frames])
        boil = float(np.abs(stack - stack.mean(axis=0)).mean(axis=(0, 3))[m].mean() * 255.0)
    else:
        # Not a failure: on some views there IS no still region. View 296 has 24 such
        # pixels out of 3,500 at 1x and none survive the 4x erosion, because she is
        # photographed in shorts and every pixel of her moves. The mask size is returned
        # so a blank boil can be told from a measured zero.
        boil = float("nan")

    return step(base4x), step(frames), boil, int(m.sum())


# ----------------------------------------------------------------------------------
# samples
# ----------------------------------------------------------------------------------

def composite(f, shade=0.42):
    a = f[..., 3:4]
    bg = np.full(f.shape[:2] + (3,), shade, np.float32)
    return (np.clip(f[..., :3] * a + bg * (1 - a), 0, 1) * 255.0 + 0.5).astype(np.uint8)


def write_gifs(view, base4x, frames, out_dir, half_size=True):
    """
    Three animations, because there are three things to judge and they are not the same
    thing. GIF delays are quantised to 10ms, so each caption says what it is approximating.
    """
    os.makedirs(out_dir, exist_ok=True)
    written = []

    def pack(seq, delay, caption, name):
        ims = []
        for f in seq:
            u8 = composite(f)
            im = Image.fromarray(u8)
            if half_size:
                im = im.resize((im.width // 2, im.height // 2), Image.LANCZOS)
            lab = Image.new("RGB", (max(im.width, 210), im.height + 16), (18, 18, 18))
            lab.paste(im, (0, 16))
            ImageDraw.Draw(lab).text((3, 3), caption, fill=(255, 255, 140))
            ims.append(lab)
        p = os.path.join(out_dir, name)
        ims[0].save(p, save_all=True, append_images=ims[1:], duration=delay, loop=0,
                    disposal=2, optimize=False)
        written.append(p)

    # 1. What it used to do: a cel per 25ms marble step. GIF cannot go below 10ms units
    #    and browsers clamp very short delays, so 30ms is as close as this format gets.
    pack(base4x, 30, "WAS: 4 cels @ 25ms (~30 here)", "walk_%d_1_was_25ms.gif" % view)
    # 2. The cadence fixed from the script, with the original four cels and no new art.
    pack(base4x, 180, "NOW: 4 cels @ 183ms (ticksToDo 10)", "walk_%d_2_fixed_4cel.gif" % view)
    # 3. Plus the in-between frames.
    pack(frames, int(round(183.0 / SUBS / 10.0)) * 10,
         "NOW + tweens: %d frames @ 46ms" % len(frames),
         "walk_%d_3_smooth_%dframe.gif" % (view, len(frames)))
    return written


def write_strip(view, base4x, frames, out_dir):
    """Every displayed frame, in order, at 2x - so a smeared in-between is visible as a
    still rather than having to be caught in motion."""
    os.makedirs(out_dir, exist_ok=True)
    top = np.concatenate([composite(f) for f in base4x], 1)
    bot = np.concatenate([composite(f) for f in frames], 1)
    w = max(top.shape[1], bot.shape[1])
    top = np.pad(top, ((0, 0), (0, w - top.shape[1]), (0, 0)), constant_values=18)
    bot = np.pad(bot, ((0, 0), (0, w - bot.shape[1]), (0, 0)), constant_values=18)
    g = np.concatenate([top, bot], 0)
    im = Image.fromarray(g)
    lab = Image.new("RGB", (im.width, im.height + 40), (0, 0, 0))
    lab.paste(im, (0, 24))
    d = ImageDraw.Draw(lab)
    d.text((4, 4), "view %d   TOP: the 4 cels in the resource file   "
                   "BOTTOM: the %d frames displayed (sub 0 of each is the cel itself)"
           % (view, len(frames)), fill=(255, 255, 255))
    d.text((4, im.height + 28), "every in-between is MADE UP - flow moves real pixels, "
                                "it does not recover a frame that was never shot",
           fill=(255, 180, 180))
    p = os.path.join(out_dir, "walk_%d_strip.png" % view)
    lab.save(p)
    return p


# ----------------------------------------------------------------------------------
# the pass
# ----------------------------------------------------------------------------------

def process_view(view, method, subs, sharpen_model, dry_run):
    srcs, base4x = [], []
    for c in range(CELS):
        p1 = os.path.join(PNG1X, base_name(view, c))
        p4 = os.path.join(PNG4X, base_name(view, c))
        if not os.path.exists(p1):
            return None
        if not os.path.exists(p4):
            raise SystemExit("%s is missing. Run tools/Upscale first."
                             % os.path.join(PNG4X, base_name(view, c)))
        s = np.asarray(Image.open(p1).convert("RGBA"))
        f = load_rgba(p4)
        if f.shape[:2] != (s.shape[0] * SCALE, s.shape[1] * SCALE):
            raise SystemExit("%s is %dx%d, expected %dx%d"
                             % (os.path.join(PNG4X, base_name(view, c)),
                                f.shape[1], f.shape[0],
                                s.shape[1] * SCALE, s.shape[0] * SCALE))
        srcs.append(s)
        base4x.append(f)

    if len({f.shape for f in base4x}) != 1:
        # Would move the sprite mid-cycle; every walker view checked is uniform, so this
        # is a guard against a future view rather than a case that happens today.
        print("  view %d: cels differ in size, skipped" % view)
        return None

    if sharpen_model:
        base4x = [np.dstack([sharpen(f[..., :3], s, sharpen_model), f[..., 3]])
                  for f, s in zip(base4x, srcs)]

    # --- the in-betweens, round the loop: 0->1, 1->2, 2->3 and 3->0 ------------------
    frames, endpoint = [], []
    for c in range(CELS):
        a, b = base4x[c], base4x[(c + 1) % CELS]
        fl = flow_for(a, b) if method == "flow" else None
        frames.append(a)                       # sub 0 IS the cel, byte for byte
        for k in range(1, subs):
            frames.append(tween(a, b, k / float(subs), method, fl))
        # The interpolator at t=0 and t=1 must reproduce the cels it sits between.
        for t, ref in ((0.0, a), (1.0, b)):
            g = tween(a, b, t, method, fl)
            endpoint.append(np.abs(g[..., :3] * g[..., 3:4]
                                   - ref[..., :3] * ref[..., 3:4]).mean() * 255.0)

    stats = sequence_stats(srcs, base4x, frames) + (float(np.mean(endpoint)),)

    written = []
    if not dry_run:
        for c in range(CELS):
            for k in range(1, subs):
                f = frames[c * subs + k]
                assert f.shape == base4x[c].shape, (f.shape, base4x[c].shape)
                h, w = srcs[c].shape[:2]
                assert f.shape[:2] == (h * SCALE, w * SCALE), \
                    "%s would be %dx%d, must be %dx%d" % (sub_name(view, c, k),
                                                          f.shape[1], f.shape[0],
                                                          w * SCALE, h * SCALE)
                p = os.path.join(PNG4X, sub_name(view, c, k))
                save_rgba(p, f)
                written.append(p)
        if sharpen_model:
            for c in range(CELS):
                save_rgba(os.path.join(PNG4X, base_name(view, c)), base4x[c])

    return base4x, frames, stats, written


def do_check(subs):
    """Are the in-between frames present, and were they built from the cels that are
    there NOW? This is the tools/Upscale ordering hazard, made detectable."""
    man = read_manifest()
    if man is None:
        print("no manifest: smooth_walk has not been run, or its output was removed.")
        print("%d in-between file(s) present in %s" % (len(existing_subs()), PNG4X))
        return 1
    if man.get("subs") != subs:
        print("manifest was written with --subs %s, asked about %d" % (man.get("subs"), subs))
    missing, stale = [], []
    for name, want in sorted(man.get("bases", {}).items()):
        p = os.path.join(PNG4X, name)
        if not os.path.exists(p):
            missing.append(name)
        elif sha(p) != want:
            stale.append(name)
    for name in man.get("subs_written", []):
        if not os.path.exists(os.path.join(PNG4X, name)):
            missing.append(name)
    print("manifest: %s, method %s, subs %d, %d base cels, %d in-between files"
          % (man.get("written"), man.get("method"), man.get("subs"),
             len(man.get("bases", {})), len(man.get("subs_written", []))))
    if stale:
        print()
        print("STALE: %d base cel(s) have changed since the in-between frames were built."
              % len(stale))
        print("Something rewrote assets/png4x - tools/Upscale does exactly this - so the")
        print("cycle now alternates cels from one pass and in-betweens from another.")
        print("Re-run: python tools/smooth_walk.py")
        for n in stale[:8]:
            print("    %s" % n)
        if len(stale) > 8:
            print("    ... and %d more" % (len(stale) - 8))
    if missing:
        print()
        print("MISSING: %d file(s) the manifest lists are not on disk." % len(missing))
        for n in missing[:8]:
            print("    %s" % n)
    if not stale and not missing:
        print()
        print("up to date.")
        return 0
    return 1


def do_clean():
    subs = existing_subs()
    for n in subs:
        os.remove(os.path.join(PNG4X, n))
    if os.path.exists(MANIFEST):
        os.remove(MANIFEST)
    print("removed %d in-between file(s) and the manifest." % len(subs))
    print("The walker is back to the original's four cels; no code change is needed.")
    return 0


def main():
    argv = sys.argv[1:]

    def opt(name, default=None):
        return argv[argv.index(name) + 1] if name in argv else default

    # --dir / --scale point the whole script at a DIFFERENT high-resolution set. That is
    # how the generated character art (tools/gen_walkers.py, assets/png12x-<set>) gets its
    # in-between frames: without them the renderer finds no _sN file in the set directory,
    # falls through to the ported one in assets/png12x, and the cycle alternates a new
    # character with three frames of the old one. Everything else here is unchanged - the
    # cels are read from the directory given and the 1x decode is still only ever read,
    # never written.
    global PNG4X, SCALE, MANIFEST
    if "--dir" in argv:
        PNG4X = os.path.abspath(opt("--dir"))
        MANIFEST = os.path.join(PNG4X, "smooth_walk.manifest.json")
    if "--scale" in argv:
        SCALE = int(opt("--scale"))

    dry = "--dry-run" in argv
    samples = "--no-samples" not in argv
    method = opt("--method", "flow")
    subs = int(opt("--subs", SUBS))
    sharpen_model = opt("--sharpen")
    out_dir = opt("--samples", os.path.join(ROOT, "tools", "samples"))

    if method not in ("flow", "dissolve"):
        raise SystemExit("--method must be flow or dissolve")
    if not os.path.isdir(PNG4X):
        raise SystemExit("%s does not exist. Run tools/Upscale first." % PNG4X)
    if "--clean" in argv:
        return do_clean()
    if "--check" in argv:
        return do_check(subs)
    if sharpen_model and not sharpen_model.endswith(".onnx"):
        sharpen_model += ".onnx"
    if sharpen_model and sharpen_model not in MODELS:
        raise SystemExit("--sharpen must be one of: %s" % ", ".join(MODELS))

    views = WALKER_VIEWS
    if "--views" in argv:
        want = {int(v) for v in opt("--views").split(",")}
        views = [v for v in views if v in want]

    gif_views = {int(v) for v in opt("--gif-views", "282,296,274").split(",")}

    print("smooth_walk: %s, %d frames per cel%s" % (method, subs, "  (DRY RUN)" if dry else ""))
    print("The in-between frames are INVENTED. Flow moves pixels that exist; it does not")
    print("recover a frame Sierra never shot. Look at the animations before believing them.")
    if sharpen_model:
        print()
        print("  !! --sharpen %s was measured, looked at and NOT adopted." % sharpen_model)
        print("  !! It rewrites the cels themselves. Undo with tools/Upscale, then")
        print("  !! tools/restore_faces.py, then this script with no --sharpen.")
    print()
    print("%-6s %6s  %11s %10s %7s %8s %9s   %s"
          % ("view", "frames", "step_before", "step_after", "boil", "still_px",
             "endpoint", "size"))
    print("-" * 88)

    t0 = time.time()

    # MERGE, never replace. A run restricted with --views must not drop the other views
    # out of the manifest: --check reads it to detect the tools/Upscale ordering hazard,
    # and a manifest that only lists what was regenerated last would quietly stop watching
    # the 228 files it did not touch, which is the exact failure the manifest exists for.
    manifest = read_manifest() or {}
    manifest.update({"written": time.strftime("%Y-%m-%d %H:%M:%S"), "method": method,
                     "subs": subs, "sharpen": sharpen_model})
    manifest.setdefault("bases", {})
    written_set = set(manifest.setdefault("subs_written", []))
    worst_boil = 0.0
    made = 0
    for view in views:
        r = process_view(view, method, subs, sharpen_model, dry)
        if r is None:
            continue
        base4x, frames, (sb, sa, boil, still, ep), written = r
        made += len(written)
        h, w = base4x[0].shape[:2]
        print("%-6d %6d  %11.2f %10.2f %7.2f %8d %9.3f   %dx%d"
              % (view, len(frames), sb, sa, boil, still, ep, w, h))
        if boil == boil:
            worst_boil = max(worst_boil, boil)
        if not dry:
            for c in range(CELS):
                p = os.path.join(PNG4X, base_name(view, c))
                manifest["bases"][base_name(view, c)] = sha(p)
            written_set.update(os.path.basename(p) for p in written)
        if samples:
            write_strip(view, base4x, frames, out_dir)
            if view in gif_views:
                write_gifs(view, base4x, frames, out_dir)

    if not dry:
        manifest["subs_written"] = sorted(written_set)
        with open(MANIFEST, "w", encoding="utf-8") as fh:
            json.dump(manifest, fh, indent=1)

    print("-" * 88)
    print("%d in-between file(s) in %.0fs%s"
          % (made, time.time() - t0, "  (nothing written)" if dry else ""))
    print()
    print("step_before: mean abs change between adjacent DISPLAYED frames, 0-255, over the")
    print("             four cels. step_after: the same over the %d frames now displayed."
          % (subs * CELS))
    print("boil:        movement on pixels the 1x SOURCE held still (within 2 levels). The")
    print("             source says nothing happens there. Above ~1.0 is invented motion.")
    print("still_px:    how many 4x pixels boil was measured over, after erosion. 0 means")
    print("             the view has no still region at all and boil is blank, not zero.")
    print("endpoint:    the interpolator evaluated at t=0 and t=1 against the real cels.")
    print("             Anything but ~0 is a bug in smooth_walk.py, not a judgement call.")
    print("worst boil this run: %.2f" % worst_boil)
    if samples:
        print()
        print("animations and strips: %s" % out_dir)
        print("  walk_V_1_was_25ms.gif        what it looked like before the cadence fix")
        print("  walk_V_2_fixed_4cel.gif      the script's own rate, original art only")
        print("  walk_V_3_smooth_Nframe.gif   the same rate with these in-between frames")
        print("  walk_V_strip.png             every displayed frame as a still")
    print()
    print("A human has to WATCH those. No number here can tell you whether the man walks.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
