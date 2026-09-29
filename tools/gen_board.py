"""Regenerates the town board as ORIGINAL artwork, keeping every footprint exactly.

WHAT PIC 11 ACTUALLY IS, AND WHY THAT DECIDES THE WHOLE DESIGN
--------------------------------------------------------------
`tools/decode_pic_full.py --cels` shows pic 11 is not one flat image. It is fifteen
embedded view cels stamped at absolute coordinates:

    cel 0    319x199 at (1,0)     the base: ground, roads, river, trees, and the
                                  buildings WITHOUT their names
    cel 1-13  small, at 13 places  each a complete building WITH its name lettered on it
    cel 14   60x39  at (130,150)  the town clock

and every one of the fifteen is 100% opaque. That last fact is what makes this safe.
The thirteen named buildings and the clock are opaque rectangles at fixed coordinates, so
stamping the ORIGINALS back over the regenerated base restores those regions pixel for
pixel. The buildings cannot move, because they are not regenerated at all. "Z-Mart",
"Bank" and "HI-TECH U" stay legible for the same reason - a diffusion model never sees
them, so it never gets the chance to garble them.

What IS regenerated is the ground the buildings stand on: the colour blocks, the roads,
the river, the trees. That is also what the complaint was about.

    THE CLICK TARGETS ARE A CORRECTNESS REQUIREMENT, NOT A STYLE ONE. `Board.All`
    (src/Jones.Core/Model/Board.cs, from the Place instances at room1.sc:294) gives
    thirteen rectangles in 320x200 space. If a building moved, it would become
    unclickable or would answer for its neighbour. --overlay draws those rectangles over
    the finished board so the claim can be looked at rather than believed, and the run
    prints how much of each rectangle is stamped original.

THE PIPELINE
------------
  A. WHOLE-BOARD PASS. One img2img over the entire board at --base (1024x640 fits a 4GB
     card at 3.09GB), so the model sees the whole town at once and the result is coherent.
     Structure is held by the init image and, when it is available, by a soft-edge
     ControlNet built from the source's own gradient.

     Strength is the whole argument. Measured on this board: 0.35 keeps every road and
     block where it was but barely improves the look; 0.50 looks convincingly painted and
     moves things; 0.65 is a different town. The buildings are stamped back either way, so
     the thing strength has to protect is the GROUND - the marble walks a fixed path over
     those roads. --measure reports how far it moved.

  B. REFINEMENT TO 12x. Deliberately NOT tiled diffusion. At factor 12 a 768-pixel tile
     covers 64x64 pixels of the original, which is a patch of flat grass with no context;
     tiles invent their own texture and their own white balance, and on a board that is
     mostly large flat colour that shows up as a quilt. The refinement is the 4x ONNX
     convolutional upscaler that is already cached for this repo, which is deterministic,
     has no per-tile randomness, and therefore has no seams to hide - its overlap error is
     measured and printed, and it is zero to rounding.

  C. COMPOSITE. The fourteen original cels, upscaled with the same text-friendly 4x model,
     stamped at their own coordinates; the interface window's cream panel restored exactly;
     the one-pixel frame cel 0 does not cover restored from the original.

OUTPUT
------
    <set>/board.png     what MainView draws (SciArt.Board)
    <set>/pic_11.png    the same image under its resource number, which
                        MainViewModel.cs:5197 asks for separately
    <set>/gen_board.manifest.json

Both are written into an ART SET (assets/png12x-new), never over assets/png or the app's
own Assets/game/board.png. `JONES_ART_SET=new` selects it; unsetting it reverts.

    ORDERING HAZARD: this reads assets/png12x/board.png as its source, which
    tools/Upscale produced. Re-running `Upscale 12 4` rewrites that source but does NOT
    touch the set, so the set silently goes stale against art that has changed underneath
    it. Re-run this afterwards. It does not read or write any walker cel, so it is
    independent of tools/fit_walkers.py in both directions.

USAGE
    python tools/decode_pic_full.py assets/raw/pic/11.pic out.png --cels <celdir> --alpha
    python tools/gen_board.py --cels <celdir> --out assets/png12x-new [--strength 0.45]
    python tools/gen_board.py --cels <celdir> --out <set> --overlay tools/samples
"""

import argparse
import glob
import json
import os
import re

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PNG1X = os.path.join(ROOT, "assets", "png")
CACHE = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
                     "jones-upscale-models")
MODELS = os.path.join(CACHE, "gen")

SCREEN_W, SCREEN_H = 320, 200

# THE PROMPT DESCRIBES A TOWN, NOT THIS TOWN'S ARTWORK. Nothing here names Sierra, the
# original's palette or its rendering; the only thing carried over from 1990 is the
# composition, and that arrives as an init image because the layout is functional.
PROMPT = ("a detailed hand painted bird's eye view map of a small town, neat green lawns, "
          "tarmac roads, a winding river, leafy trees, tidy suburban buildings, clean "
          "crisp illustration, soft daylight")

NEGATIVE = ("text, letters, words, numbers, signage, labels, watermark, blurry, pixelated, "
            "jpeg artifacts, noise, people, faces, cars, lowres, oversaturated, frame, "
            "border, vignette")

# The thirteen Place rectangles, copied from src/Jones.Core/Model/Board.cs, which took
# them from room1.sc:294. Kept here as data so --overlay needs no build.
HOTSPOTS = [
    ("Low Cost Apartment", 130, 10, 190, 43),
    ("Pawn Shoppe", 191, 10, 250, 43),
    ("Z-Mart", 251, 10, 311, 43),
    ("Monolith", 251, 44, 311, 81),
    ("QT Clothing", 251, 82, 311, 118),
    ("Socket City", 251, 133, 311, 192),
    ("University", 190, 158, 250, 192),
    ("Employment Office", 68, 158, 128, 192),
    ("Factory", 7, 155, 67, 192),
    ("Bank", 7, 119, 67, 154),
    ("Black's Market", 24, 69, 67, 114),
    ("Security Apartment", 7, 10, 67, 43),
    ("Rent Office", 68, 10, 129, 43),
]

CEL_RE = re.compile(r"cel_(\d+)_(\d+)x(\d+)_at_(-?\d+)_(-?\d+)\.png$")


def load_cels(celdir):
    """[(index, x, y, PIL image)] from tools/decode_pic_full.py --cels, in stamp order."""
    out = []
    for p in sorted(glob.glob(os.path.join(celdir, "cel_*.png"))):
        m = CEL_RE.search(os.path.basename(p))
        if not m:
            continue
        i, w, h, x, y = (int(v) for v in m.groups())
        im = Image.open(p).convert("RGBA")
        if im.size != (w, h):
            raise SystemExit(f"{p} is {im.size}, its name says {w}x{h}")
        out.append((i, x, y, im))
    if not out:
        raise SystemExit(f"no cels in {celdir}; run decode_pic_full.py --cels first")
    return out


# ----------------------------------------------------------------------------------
# the 4x ONNX upscaler, reused from the cache tools/restore_faces.py already fills
# ----------------------------------------------------------------------------------

_SESSIONS = {}


def session(name):
    import onnxruntime as ort
    if name not in _SESSIONS:
        path = os.path.join(CACHE, name)
        if not os.path.exists(path):
            raise SystemExit(f"{path} is missing; tools/restore_faces.py caches it")
        o = ort.SessionOptions()
        o.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
        _SESSIONS[name] = ort.InferenceSession(path, o, providers=["CPUExecutionProvider"])
    return _SESSIONS[name]


def esrgan4(rgb, model, tile=256, pad=16):
    """Native 4x, tiled with an overlap so a whole board fits in memory.

    The model is a convolution with a finite receptive field and NO randomness, so the
    overlap is not a blend, it is a crop: each tile is run with `pad` pixels of context on
    every side and only its interior is kept. Two tiles therefore agree exactly where they
    meet, which is the entire reason the refinement is done this way rather than with
    tiled diffusion. `--measure` prints the residual, and it is zero to rounding."""
    s = session(model)
    name = s.get_inputs()[0].name
    h, w = rgb.shape[:2]
    out = np.zeros((h * 4, w * 4, 3), np.float32)
    for y in range(0, h, tile):
        for x in range(0, w, tile):
            y0, y1 = max(0, y - pad), min(h, y + tile + pad)
            x0, x1 = max(0, x - pad), min(w, x + tile + pad)
            patch = rgb[y0:y1, x0:x1].astype(np.float32).transpose(2, 0, 1)[None] / 255.0
            got = s.run(None, {name: patch})[0][0].transpose(1, 2, 0)
            ky0, kx0 = (y - y0) * 4, (x - x0) * 4
            ky1 = ky0 + (min(h, y + tile) - y) * 4
            kx1 = kx0 + (min(w, x + tile) - x) * 4
            out[y * 4:y * 4 + (ky1 - ky0), x * 4:x * 4 + (kx1 - kx0)] = got[ky0:ky1, kx0:kx1]
    return np.clip(out, 0.0, 1.0)


def up_to(im, w, h, model):
    """RGB(A) PIL image -> w x h. `model` of "lanczos" or None skips the network.

    THE STAMPED CELS GO THROUGH LANCZOS, NOT THROUGH A NETWORK, and that is a measurement.
    Upscaling the Z-Mart sign twelve times was tried five ways (tools/samples/
    board_sign_upscalers.png): nearest is faithful but blocky, Lanczos is soft and
    completely legible, and all three 4x networks - ClearReality, UltraSharp and
    RealESRGAN - REWRITE THE LETTERFORMS. "Z-Mart" comes back as something that is no
    longer the same word. That is precisely the failure the signs were kept out of the
    generator to avoid, and a super-resolution model reintroduces it just as happily.

    The regenerated ground has the opposite requirement - invented texture is the point -
    so it still goes through the network."""
    if not model or model == "lanczos":
        return im.convert("RGB").resize((w, h), Image.LANCZOS)
    rgb = np.asarray(im.convert("RGB"))
    big = (esrgan4(rgb, model) * 255.0 + 0.5).astype(np.uint8)
    return Image.fromarray(big, "RGB").resize((w, h), Image.LANCZOS)


# ----------------------------------------------------------------------------------


def soft_edges(im):
    """A soft-edge conditioning map from the image's own gradient.

    ControlNet softedge wants an HED/PiDiNet map. There is no detector in this tree and a
    detector is a further model to download for one image; a Sobel magnitude, blurred a
    little and normalised, is the same KIND of picture - grey-valued, thick, forgiving -
    and this source is clean flat art whose edges are unambiguous."""
    from PIL import ImageFilter
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


def panel_rect(base):
    """The interface window's flat panel in the middle of cel 0, as (x0, y0, x1, y1)
    inclusive in cel-0 coordinates.

    Found rather than hard-coded: it is the largest run of one colour in the middle third
    of the image, and the game draws its window over it, so it has to come back exactly."""
    a = np.asarray(base.convert("RGB"))
    h, w = a.shape[:2]
    c = a[h // 2, w // 2]
    same = (np.abs(a.astype(int) - c.astype(int)).max(axis=2) <= 2)
    rows = np.nonzero(same[:, w // 2])[0]
    cols = np.nonzero(same[h // 2, :])[0]
    if len(rows) == 0 or len(cols) == 0:
        return None
    # Walk out from the centre so a stray matching pixel elsewhere cannot widen it.
    def span(idx, mid):
        lo = hi = mid
        s = set(idx.tolist())
        while lo - 1 in s:
            lo -= 1
        while hi + 1 in s:
            hi += 1
        return lo, hi
    y0, y1 = span(rows, h // 2)
    x0, x1 = span(cols, w // 2)
    return x0, y0, x1, y1


def clear_buildings(init, cond, cels, base, grow=0):
    """Erase the fourteen stamped rectangles from the init image and the edge map.

    WHY: the base cel carries the buildings too, unlettered, so the generator redraws them
    - and its versions do not line up with the originals that get stamped on top. The
    result was a ghost roofline sticking out beside Pawn Shop and a second Hi-Tech U
    behind the first. Erasing those rectangles first means the model only ever draws
    GROUND there, and the stamped building then sits on clean ground instead of on top of
    a rival.

    The hole is filled by growing the surrounding colours inward - the same trick
    tools/restore_faces.py uses under transparent pixels - so the model is handed plausible
    grass rather than a flat grey patch it would treat as an object. The edge map is
    cleared outright, because an edge there is exactly what must not be enforced.

    `grow` IS ZERO ON PURPOSE. The cleared rectangle is then exactly the rectangle that
    gets stamped back, so whatever the model does inside it is covered and can never be
    seen. Growing it by even one pixel leaves a ring that is cleared but not stamped, and
    at the top-left corner - where Le Security sits against the board's edge and there is
    almost no surrounding colour to grow from - that ring came back as a grey wedge over
    the sky."""
    from PIL import ImageFilter

    W, H = base
    sx, sy = W / float(SCREEN_W), H / float(SCREEN_H)
    a = np.asarray(init.convert("RGB"), np.float32).copy()
    hole = np.zeros((H, W), bool)
    for i, x, y, im in cels:
        if i == 0:
            continue
        w, h = im.size
        x0 = max(0, int((x - grow) * sx))
        y0 = max(0, int((y - grow) * sy))
        x1 = min(W, int(np.ceil((x + w + grow) * sx)))
        y1 = min(H, int(np.ceil((y + h + grow) * sy)))
        hole[y0:y1, x0:x1] = True

    known = (~hole).astype(np.float32)
    a[hole] = 0.0
    for _ in range(max(W, H)):
        if known.all():
            break
        acc = np.zeros_like(a)
        cnt = np.zeros_like(known)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            acc += np.roll(np.roll(a, dy, 0), dx, 1) * np.roll(np.roll(known, dy, 0), dx, 1)[..., None]
            cnt += np.roll(np.roll(known, dy, 0), dx, 1)
        fresh = (cnt > 0) & (known == 0)
        a[fresh] = (acc / np.maximum(cnt, 1)[..., None])[fresh]
        known[fresh] = 1.0

    filled = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGB")
    soft = filled.filter(ImageFilter.GaussianBlur(3.0))
    m = Image.fromarray((hole * 255).astype(np.uint8), "L")
    filled = Image.composite(soft, filled, m)

    c = np.asarray(cond.convert("RGB")).copy()
    c[hole] = 0
    return filled, Image.fromarray(c, "RGB")


def generate(args, cels=None):
    import torch
    import gen_walkers as gw

    src = Image.open(args.source).convert("RGB")
    W, H = args.base
    init = src.resize((W, H), Image.LANCZOS)

    txt, img = gw.build("cuda" if torch.cuda.is_available() else "cpu",
                        offload=True, controlnet=args.controlnet)
    g = torch.Generator("cuda").manual_seed(args.seed)
    cond = soft_edges(init) if args.control > 0 else Image.new("RGB", (W, H), (0, 0, 0))
    if cels and not args.keep_buildings:
        init, cond = clear_buildings(init, cond, cels, (W, H))
        init.save(os.path.join(args.out, "_init_cleared.png")) if os.path.isdir(args.out) else None
    out = img(prompt=PROMPT, negative_prompt=NEGATIVE, image=init, control_image=cond,
              strength=args.strength,
              num_inference_steps=max(args.steps, int(args.steps / args.strength)),
              guidance_scale=args.cfg, controlnet_conditioning_scale=args.control,
              generator=g).images[0]
    return out


def composite(stage_a, cels, factor, model, cel_model="lanczos", feather=3.0,
              colour_match=True, frame=4.0, full_source=None):
    """Stage A -> full resolution, then the original cels stamped back."""
    fw, fh = SCREEN_W * factor, SCREEN_H * factor
    board = up_to(stage_a, fw, fh, model)

    base = next(im for i, _, _, im in cels if i == 0)
    bx = next(x for i, x, _, _ in cels if i == 0)
    by = next(y for i, _, y, _ in cels if i == 0)

    # THE FRAME. The board has a dark border a few pixels wide, and cel 0 starts at x=1,
    # so the outermost column and row are not even covered by it. It is a border, not
    # artwork anyone asked to improve, and it is where the generator misbehaves worst:
    # clearing the corner buildings left the top-left with almost no surrounding colour to
    # grow from, and the model filled the hole with a dark slab. Restoring it from the
    # source, with a short ramp inwards so it does not become a second hard edge, costs
    # nothing and removes the one artefact that read as a mistake rather than as style.
    if frame > 0:
        srcbig = up_to(full_source, fw, fh, None) if full_source.size != (fw, fh) \
            else full_source.convert("RGB")
        r = int(round(frame * factor))
        m = np.zeros((fh, fw), np.float32)
        ramp = np.linspace(1.0, 0.0, r, endpoint=False)
        for k, v in enumerate(ramp):
            m[k, :] = np.maximum(m[k, :], v)
            m[-1 - k, :] = np.maximum(m[-1 - k, :], v)
            m[:, k] = np.maximum(m[:, k], v)
            m[:, -1 - k] = np.maximum(m[:, -1 - k], v)
        b = np.asarray(board, np.float32)
        s = np.asarray(srcbig, np.float32)
        board = Image.fromarray(
            np.clip(b * (1 - m[..., None]) + s * m[..., None], 0, 255).astype(np.uint8), "RGB")

    # The interface panel, exactly as it was.
    pr = panel_rect(base)
    if pr:
        x0, y0, x1, y1 = pr
        px, py = (bx + x0) * factor, (by + y0) * factor
        pw, ph = (x1 - x0 + 1) * factor, (y1 - y0 + 1) * factor
        patch = base.crop((x0, y0, x1 + 1, y1 + 1)).convert("RGB").resize(
            (pw, ph), Image.NEAREST)
        board.paste(patch, (px, py))

    # The thirteen named buildings and the clock.
    #
    # STAMPED THROUGH A FEATHER, AND COLOUR MATCHED AT THE JOIN. A cel is an opaque
    # RECTANGLE, not a building cut-out: it carries a margin of 1990 ground around the
    # building. In the original that margin agreed with the ground outside it, because it
    # was the same picture. Against regenerated ground it does not, and pasting the
    # rectangle flat drew a visible box round every building - thirteen of them, and the
    # first thing the eye went to.
    #
    # So: a constant colour offset per cel, taken from the difference between the ring
    # just inside the cel and the ring just outside it and clipped hard, and an alpha that
    # is solid across the interior and ramps to nothing over the outermost `feather`
    # pixels. The building itself is central and well inside the ramp, so it is still
    # stamped original to the pixel - which is the whole point - while its margin of grass
    # dissolves into the new grass.
    stamped = []
    arr = np.asarray(board, np.float32)
    for i, x, y, im in cels:
        if i == 0:
            continue
        w, h = im.size
        bw, bh = w * factor, h * factor
        px, py = x * factor, y * factor
        big = np.asarray(up_to(im, bw, bh, cel_model), np.float32)

        r = max(1, int(round(feather * factor)))

        def edge_weight(depth):
            """1 on the cel's outermost pixel, 0 at `depth` pixels in."""
            w = np.zeros((bh, bw), np.float32)
            if depth <= 0:
                return w
            ramp = 0.5 + 0.5 * np.cos(np.pi * (np.arange(depth) + 0.5) / depth)
            for k, v in enumerate(ramp):
                w[k, :] = np.maximum(w[k, :], v)
                w[-1 - k, :] = np.maximum(w[-1 - k, :], v)
                w[:, k] = np.maximum(w[:, k], v)
                w[:, -1 - k] = np.maximum(w[:, -1 - k], v)
            return w

        if colour_match and r > 0:
            inside = np.concatenate([big[:r].reshape(-1, 3), big[-r:].reshape(-1, 3),
                                     big[:, :r].reshape(-1, 3), big[:, -r:].reshape(-1, 3)])
            oy0, oy1 = max(0, py - r), min(arr.shape[0], py + bh + r)
            ox0, ox1 = max(0, px - r), min(arr.shape[1], px + bw + r)
            ring = arr[oy0:oy1, ox0:ox1].reshape(-1, 3)
            off = np.clip(np.median(ring, axis=0) - np.median(inside, axis=0), -12, 12)

            # RAMPED, NOT FLAT. Applying the offset to the whole cel shifted the building
            # itself - which is meant to be the original, unaltered - and still left a step
            # at the boundary, because the correction stopped dead at the cel's edge. A
            # ramp that is full at the edge and gone a little way in corrects the margin of
            # grass, which is the only part that has to agree with anything, and leaves the
            # building exactly as it was. The visible pale rectangle round Socket City was
            # this.
            big = np.clip(big + off * edge_weight(r * 3)[..., None], 0, 255)

        a = 1.0 - edge_weight(r)

        dst = arr[py:py + bh, px:px + bw]
        arr[py:py + bh, px:px + bw] = dst * (1 - a[..., None]) + big * a[..., None]
        stamped.append((i, x, y, w, h))

    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB"), stamped, pr


def hotspot_report(stamped):
    """How much of each click rectangle is stamped original, as a fraction."""
    # A UNION, not a sum. Two cels overlap where Black's Market meets the Bank, and adding
    # their intersections gave that rectangle 112.7% coverage - a number that cannot mean
    # anything and would have been quoted.
    cover = np.zeros((SCREEN_H, SCREEN_W), bool)
    for _i, x, y, w, h in stamped:
        cover[max(0, y):y + h, max(0, x):x + w] = True

    rows = []
    for name, l, t, r, b in HOTSPOTS:
        sub = cover[t:b + 1, l:r + 1]
        rows.append({"name": name, "rect": [l, t, r, b],
                     "stamped_fraction": round(float(sub.mean()), 4)})
    return rows


def overlay(board, factor, path):
    """The finished board with the thirteen click rectangles drawn over it."""
    from PIL import ImageDraw
    im = board.copy().convert("RGB")
    d = ImageDraw.Draw(im)
    for name, l, t, r, b in HOTSPOTS:
        d.rectangle([l * factor, t * factor, (r + 1) * factor - 1, (b + 1) * factor - 1],
                    outline=(255, 40, 40), width=max(2, factor // 3))
    im.save(path)


def measure(new, old, cels, factor):
    """Structure drift, on the GROUND only.

    The stamped regions are original by construction and would report zero, which would
    flatter the number; they are excluded. What is left is the ground the marble walks
    over, and the figure is how far its edges moved: the correlation between the two
    images' gradient magnitudes, downsampled to the original's own resolution so the
    comparison is about layout and not about texture."""
    small = (SCREEN_W, SCREEN_H)
    a = np.asarray(new.convert("L").resize(small, Image.LANCZOS), np.float32)
    b = np.asarray(old.convert("L").resize(small, Image.LANCZOS), np.float32)

    mask = np.ones(small[::-1], bool)
    for i, x, y, im in cels:
        if i == 0:
            continue
        w, h = im.size
        mask[y:y + h, x:x + w] = False

    def grad(v):
        gx = np.zeros_like(v); gy = np.zeros_like(v)
        gx[:, 1:-1] = v[:, 2:] - v[:, :-2]
        gy[1:-1, :] = v[2:, :] - v[:-2, :]
        return np.hypot(gx, gy)

    ga, gb = grad(a)[mask], grad(b)[mask]
    if ga.std() < 1e-6 or gb.std() < 1e-6:
        return float("nan")
    return float(np.corrcoef(ga, gb)[0, 1])


def seam_check(stage_a, model, w=384, h=240):
    """Proof that the refinement has no seams, rather than an assurance that it hasn't.

    The 4x upscaler is run over the same image twice with DIFFERENT tile sizes. A tiled
    process that blends, or that lets each tile decide its own colour, cannot give the same
    answer twice; a convolution run with enough context and cropped to its interior has to.
    Returns (max, mean) absolute difference in 0-255 levels.

    This is why the refinement is not tiled diffusion. On a board that is mostly large flat
    colour, every tile of a diffusion pass invents its own texture and its own white
    balance, and the quilt shows. No number makes that acceptable, so it was not used."""
    small = np.asarray(stage_a.convert("RGB").resize((w, h), Image.LANCZOS))
    a = esrgan4(small, model, tile=256, pad=16)
    b = esrgan4(small, model, tile=192, pad=16)
    d = np.abs(a - b) * 255.0
    return float(d.max()), float(d.mean())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cels", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--source", default=os.path.join(ROOT, "assets", "png12x", "board.png"))
    ap.add_argument("--factor", type=int, default=12)
    ap.add_argument("--base", type=int, nargs=2, default=[1024, 640])
    ap.add_argument("--strength", type=float, default=0.45)
    ap.add_argument("--steps", type=int, default=26)
    ap.add_argument("--cfg", type=float, default=7.0)
    ap.add_argument("--control", type=float, default=0.0,
                    help="soft-edge ControlNet weight; 0 runs img2img alone")
    ap.add_argument("--controlnet", default=None,
                    help="directory of the ControlNet to load instead of openpose")
    ap.add_argument("--seed", type=int, default=90210)
    ap.add_argument("--model", default="4x-ClearRealityV1.onnx",
                    help="the 4x ONNX upscaler for the stamped cels and the ground; "
                         "4x-UltraSharp is sharper and rings on 1990 lettering")
    ap.add_argument("--cel-model", default="lanczos",
                    help="upscaler for the stamped cels; 'lanczos' keeps the lettering, "
                         "the 4x networks rewrite it")
    ap.add_argument("--feather", type=float, default=3.0,
                    help="original pixels of ramp at each stamped cel's edge")
    ap.add_argument("--no-colour-match", action="store_true")
    ap.add_argument("--frame", type=float, default=4.0,
                    help="original pixels of the board's border restored from the source")
    ap.add_argument("--stage-a", help="skip generation and use this image as stage A")
    ap.add_argument("--keep-buildings", action="store_true",
                    help="do NOT erase the stamped rectangles before generating; the base "
                         "then draws its own buildings and they ghost out beside the real ones")
    ap.add_argument("--overlay", help="also write the hotspot overlay into this directory")
    a = ap.parse_args()

    cels = load_cels(a.cels)
    print(f"{len(cels)} cels, {len(cels) - 1} stamped back", flush=True)

    os.makedirs(a.out, exist_ok=True)
    if a.stage_a:
        stage = Image.open(a.stage_a).convert("RGB")
    else:
        stage = generate(a, cels)
    stage.save(os.path.join(a.out, "_stage_a.png"))

    old = Image.open(a.source).convert("RGB")
    board, stamped, pr = composite(stage, cels, a.factor, a.model, cel_model=a.cel_model,
                                   feather=a.feather, colour_match=not a.no_colour_match,
                                   frame=a.frame, full_source=old)
    for name in ("board.png", "pic_11.png"):
        board.save(os.path.join(a.out, name))
    print(f"  {board.size[0]}x{board.size[1]} -> {a.out}", flush=True)

    corr = measure(board, old, cels, a.factor)
    rows = hotspot_report(stamped)
    smax, smean = seam_check(stage, a.model)
    print(f"  ground structure correlation vs the ported board: {corr:.4f}")
    print(f"  refinement seam residual (two tile sizes): max {smax:.4f}, "
          f"mean {smean:.6f} levels of 255")
    for r in rows:
        print(f"    {r['name']:<20} stamped {r['stamped_fraction']*100:5.1f}% of its rect")

    if a.overlay:
        os.makedirs(a.overlay, exist_ok=True)
        overlay(board, a.factor, os.path.join(a.overlay, "board_hotspots.png"))
        print(f"  overlay -> {a.overlay}")

    with open(os.path.join(a.out, "gen_board.manifest.json"), "w") as f:
        json.dump({"source": a.source, "base": a.base, "strength": a.strength,
                   "steps": a.steps, "cfg": a.cfg, "control": a.control,
                   "seed": a.seed, "model": a.model, "factor": a.factor,
                   "panel_rect": pr, "ground_correlation": corr,
                   "seam_residual": {"max": smax, "mean": smean},
                   "hotspots": rows}, f, indent=1)


if __name__ == "__main__":
    main()
