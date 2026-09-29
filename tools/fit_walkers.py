"""Turns the generated character frames into game cels: keyed, cut out, and fitted back
into the original's box at the render scale.

THE CONTRACT THIS HAS TO MEET
-----------------------------
`SciArt.HighRes` rejects any high-resolution twin that is not EXACTLY `RenderScale.Factor`
times the 1x cel it stands in for, and falls back to the 320x200 bitmap when it does. So
every file written here is (cel_w * F) x (cel_h * F) to the pixel, and the figure inside
it occupies the same rows and columns the original figure occupied: same top, same ground
line, same horizontal centre. That is not cosmetic. The sprite's position is computed from
the 1x width, the walker's feet define where it stands on the board, and the four cels of
a cycle must agree or the figure jitters.

tools/sprite_pose.py recorded the mapping when it drew the skeleton - (scale, ox, oy) per
cel, fitting the figure's own height into the canvas with a fixed margin. This inverts it.

THE ALPHA
---------
`silhouette_matte` is what runs and its docstring is the argument for it. In short: colour
alone cannot cut these out, because SD1.5 tints the background towards the clothes and a
brown field behind a brown suit is the same colour as the man's face. The matte is a
trimap built from the ORIGINAL cel's silhouette - solid inside an eroded copy, clear
outside a dilated one, colour deciding only the band between - which is deterministic,
cannot eat a face, and puts no constraint on the generator at all.

`chroma_key` and `flood_background` are the two earlier attempts. They are kept because
each still has a case (a genuine green screen; a frame with no matching 1x cel) and
because the comments on them are the record of what was tried.

MEASURING IT
------------
`--measure <dir>` reports, per view, the two numbers tools/smooth_walk.py reports, on the
same definitions and in the same 0-255 levels:

    step   mean absolute change between ADJACENT cels of the loop - how big a jump the eye
           is asked to absorb every 183ms.
    boil   mean absolute deviation from the per-pixel mean over the loop, measured ONLY on
           pixels the 1x source held still (within 2 levels) in all four cels. The source
           says nothing happens there. Anything the new art does there is the generator
           repainting the character between frames, which is the failure this whole
           arrangement - one seed, one prompt, cel 0 as the img2img reference - exists to
           prevent. Flow interpolation scores ~0 on this because it only moves pixels that
           already exist; a diffusion model has to be made to.

It prints the ported cels' figures beside the new ones, because `step` is a property of
the ANIMATION, not of the art: the original's own cels differ a great deal from each other
and a new set that matches that is behaving correctly. `boil` is the one to judge.

USAGE
    python tools/fit_walkers.py --poses <dir> --gen <dir> --out <dir> --factor 12
    python tools/fit_walkers.py --measure <dir> [--against assets/png12x]
"""

import argparse
import json
import os

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PNG1X = os.path.join(ROOT, "assets", "png")


def chroma_key(rgb, tol, soft):
    """Alpha in [0,1] for a GREEN SCREEN frame, plus the de-spilled colour.

    tools/gen_walkers.py generates against a green field precisely so this can be a hue
    test rather than a colour-distance test: `g - max(r, b)` is large and positive only
    where the pixel is saturated green, and nothing on a clothed person is. Skin, hair,
    denim and a brown suit all score at or below zero, so unlike the flood key this cannot
    take a face off with the wall behind it - which is exactly what the flood key did.

    DE-SPILL. A green field bounces green into every edge pixel and into light-coloured
    hair; left alone it survives as a lime rim once the sprite is over the board. Clamping
    the green channel to the average of the other two wherever it exceeds them removes it,
    and is a no-op on anything that was legitimately green - it is a shirt that is green in
    HUE, i.e. green well above red AND blue, and those pixels are keyed out anyway.

    Connectivity is not needed and is deliberately not used: a hue test has no seeds to
    flood from, so a green-lit gap between an arm and a body is removed as correctly as
    the field around it. That is the right answer for a sprite.

    Returns (alpha, rgb_despilled)."""
    im = rgb.astype(np.float32)
    r, g, b = im[:, :, 0], im[:, :, 1], im[:, :, 2]
    green = g - np.maximum(r, b)

    # 1 where green is below `tol` (the figure), 0 above `tol + soft` (the field).
    a = np.clip((tol + soft - green) / max(1e-3, soft), 0.0, 1.0)

    out = im.copy()
    cap = (r + b) / 2.0
    over = g > cap
    out[:, :, 1] = np.where(over, cap, g)
    return a, out


def flood_background(rgb, tol, soft):
    """Alpha in [0,1]: 0 on the background, 1 on the figure. THE FALLBACK KEY, for frames
    that did not come back on a green field.

    SEVERAL SEED COLOURS, AND CONNECTIVITY. Two earlier versions failed in opposite ways
    and both are worth recording.

    A SINGLE seed colour - the median of the border - keys the wall and leaves the floor.
    SD1.5 tints the background towards whatever the clothes are and draws a lighter strip
    under the feet however loudly the negative prompt says "floor", so the background is
    routinely two-toned and one seed only removes one of them. The symptom is a white slab
    under every sprite.

    PURE REGION GROWING - neighbour to neighbour, each step under a small tolerance -
    crosses from wall to floor happily, and then walks straight through the figure's
    anti-aliased outline, which is a staircase of small steps and not the hard wall the
    idea depended on. It swallowed 97% of the frame.

    So: cluster the border into a few colours, accept a pixel within `tol` of ANY of them,
    and flood from the border over that mask. The tolerance is global, so the outline stops
    it; the clusters are plural, so wall and floor both go. A white sock is not keyed even
    though a white floor is, because it is not connected to the border.

    `soft` widens the band over which the boundary fades, so hair and a coat hem keep an
    edge instead of a staircase."""
    h, w, _ = rgb.shape
    im = rgb.astype(np.float32)

    # Border colours, clustered. Plain Lloyd's algorithm on 3072 samples - k-means from
    # scipy is not installed here and this converges in a dozen passes.
    border = np.concatenate([im[0], im[-1], im[:, 0], im[:, -1]])
    k = 4
    cent = np.array([np.percentile(border, p, axis=0) for p in (10, 37, 63, 90)])
    for _ in range(24):
        d = np.abs(border[:, None, :] - cent[None, :, :]).max(axis=2)
        lab = d.argmin(axis=1)
        new = np.array([border[lab == i].mean(axis=0) if (lab == i).any() else cent[i]
                        for i in range(k)])
        if np.abs(new - cent).max() < 0.5:
            cent = new
            break
        cent = new

    dist = np.abs(im[:, :, None, :] - cent[None, None, :, :]).max(axis=3).min(axis=2)
    near = dist <= tol + soft

    # Scanline flood from the border over `near`.
    seen = np.zeros((h, w), bool)
    stack = [(0, x) for x in range(w) if near[0, x]]
    stack += [(h - 1, x) for x in range(w) if near[h - 1, x]]
    stack += [(y, 0) for y in range(h) if near[y, 0]]
    stack += [(y, w - 1) for y in range(h) if near[y, w - 1]]

    while stack:
        y, x = stack.pop()
        if seen[y, x] or not near[y, x]:
            continue
        x0 = x
        while x0 > 0 and near[y, x0 - 1] and not seen[y, x0 - 1]:
            x0 -= 1
        x1 = x
        while x1 < w - 1 and near[y, x1 + 1] and not seen[y, x1 + 1]:
            x1 += 1
        seen[y, x0:x1 + 1] = True
        for ny in (y - 1, y + 1):
            if 0 <= ny < h:
                for i in np.nonzero(near[ny, x0:x1 + 1] & ~seen[ny, x0:x1 + 1])[0]:
                    stack.append((ny, x0 + int(i)))

    # Inside the flooded region the alpha ramps with distance from the nearest background
    # colour, which is what gives the boundary a soft edge instead of a staircase.
    a = np.ones((h, w), np.float32)
    a[seen] = np.clip((dist[seen] - tol) / max(1e-3, soft), 0.0, 1.0)

    return a


def dilate(mask, r, ry=None):
    """Dilation by r horizontally and ry (default r) vertically, as repeated 1-pixel
    shifts. No scipy on this machine.

    The two radii are separate because the two directions want different answers. A new
    character's hat or hair legitimately rises above the original's crown, so the matte has
    to reach UP generously or every head comes back flat-topped. Sideways, the thing just
    outside the silhouette is not a coat, it is the armchair the generator put there, so
    the reach has to be tight."""
    ry = r if ry is None else ry
    m = mask.copy()
    for _ in range(int(max(0, r))):
        n = m.copy()
        n[:, 1:] |= m[:, :-1]
        n[:, :-1] |= m[:, 1:]
        m = n
    for _ in range(int(max(0, ry))):
        n = m.copy()
        n[1:, :] |= m[:-1, :]
        n[:-1, :] |= m[1:, :]
        m = n
    return m


def keep_connected(mask, seed):
    """The parts of `mask` reachable from `seed`. Scanline flood; no scipy."""
    h, w = mask.shape
    seen = np.zeros_like(mask)
    ys, xs = np.nonzero(seed & mask)
    stack = list(zip(ys.tolist(), xs.tolist()))
    while stack:
        y, x = stack.pop()
        if seen[y, x] or not mask[y, x]:
            continue
        x0 = x
        while x0 > 0 and mask[y, x0 - 1] and not seen[y, x0 - 1]:
            x0 -= 1
        x1 = x
        while x1 < w - 1 and mask[y, x1 + 1] and not seen[y, x1 + 1]:
            x1 += 1
        seen[y, x0:x1 + 1] = True
        for ny in (y - 1, y + 1):
            if 0 <= ny < h:
                for i in np.nonzero(mask[ny, x0:x1 + 1] & ~seen[ny, x0:x1 + 1])[0]:
                    stack.append((ny, x0 + int(i)))
    return seen


def erode(mask, r):
    """Dilation of the complement, which is erosion."""
    return ~dilate(~mask, r)


def silhouette_matte(rgb, cel, tol, soft, core=0.06, reach=0.055, reach_up=0.30,
                     open_frac=0.013):
    """THE MATTE THAT SHIPS. Alpha from a trimap built out of the ORIGINAL cel's own
    silhouette, with colour deciding only the band in between.

    Everything else tried here keyed on colour alone, and colour alone cannot do this job.
    SD1.5 tints the background towards the clothes, so the field behind a brown suit is
    brown, and a key that removes it removes the man's face with it - which is what
    happened. Trying to force a keyable background from the generator (see
    tools/gen_walkers.py --init green) cost more than it bought: green spill over the
    figure, the init's own outline printed as a board behind it, and the prompt stopped
    being obeyed.

    The silhouette is already in hand and the brief for this work says so: the original's
    outline is FUNCTIONAL - it is what keeps the figure's footprint, anchor and hit area
    valid - and it is the one thing about the 1990 art that the new art is supposed to
    inherit. So:

        inside  erode(silhouette, core)    alpha 1, whatever colour it is
        outside dilate(silhouette, reach)  alpha 0, whatever colour it is
        between                            colour decides, against background samples
                                           taken from JUST OUTSIDE this cel's own figure

    The band is a sixth of the figure's width wide, which is where a new coat hem or a
    different hairline actually falls, so the generated figure's own edge is what gets cut
    - the silhouette only says where to look for it. Sampling the background from the ring
    around the figure rather than from the frame's border is what makes it robust to the
    floor strip and the gradient: the ring contains whatever is actually adjacent.

    Returns (alpha, rgb as float32)."""
    im = rgb.astype(np.float32)
    h, w = im.shape[:2]

    p1 = os.path.join(PNG1X, f"view_{cel['view']}_l{cel['loop']}_c{cel['cel']}.png")
    if not os.path.exists(p1):
        return flood_background(rgb, 24.0, 26.0), im

    a1 = np.asarray(Image.open(p1).convert("RGBA"))[:, :, 3] > 128
    s, ox, oy = cel["fit"]
    big = np.asarray(Image.fromarray((a1 * 255).astype(np.uint8), "L")
                     .resize((max(1, int(round(a1.shape[1] * s))),
                              max(1, int(round(a1.shape[0] * s)))), Image.BILINEAR)) > 128

    sil = np.zeros((h, w), bool)
    x0, y0 = int(round(ox)), int(round(oy))
    sx0, sy0 = max(0, x0), max(0, y0)
    sx1, sy1 = min(w, x0 + big.shape[1]), min(h, y0 + big.shape[0])
    if sx1 > sx0 and sy1 > sy0:
        sil[sy0:sy1, sx0:sx1] = big[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0]
    if not sil.any():
        return flood_background(rgb, 24.0, 26.0), im

    width = max(1, int(sil.any(axis=0).sum()))
    inner = erode(sil, max(1, int(round(width * core))))
    outer = dilate(sil, max(2, int(round(width * reach))),
                   max(2, int(round(width * reach_up))))

    # Background samples: the ring just outside the figure, which is adjacent to the edge
    # being cut and therefore holds the colours that edge is actually against.
    ring = dilate(outer, max(2, int(round(width * 0.10)))) & ~outer
    samples = im[ring] if ring.any() else im[~outer]
    if len(samples) == 0:
        # A silhouette that, dilated, covers the whole frame leaves nowhere to sample
        # from. Rare - it needs a wide cel and a tight crop - but np.percentile on an
        # empty array raises, and the whole run dies on one view.
        samples = np.concatenate([im[0], im[-1], im[:, 0], im[:, -1]])
    if len(samples) > 6000:
        samples = samples[:: max(1, len(samples) // 6000)]

    k = 4
    cent = np.array([np.percentile(samples, p, axis=0) for p in (10, 37, 63, 90)])
    for _ in range(24):
        lab = np.abs(samples[:, None, :] - cent[None, :, :]).max(axis=2).argmin(axis=1)
        new = np.array([samples[lab == i].mean(axis=0) if (lab == i).any() else cent[i]
                        for i in range(k)])
        if np.abs(new - cent).max() < 0.5:
            cent = new
            break
        cent = new

    band = outer & ~inner
    a = np.zeros((h, w), np.float32)
    a[inner] = 1.0
    if band.any():
        d = np.abs(im[band][:, None, :] - cent[None, :, :]).max(axis=2).min(axis=1)
        a[band] = np.clip((d - tol) / max(1e-3, soft), 0.0, 1.0)

    # AND IT MUST BE ATTACHED TO THE FIGURE. A pixel in the band that looks nothing like
    # the background is not necessarily part of the person - it is just as likely a corner
    # of the armchair the generator drew beside them. Keeping only what is connected to
    # the solid core removes those and cannot remove a sleeve, because a sleeve is joined
    # to the body.
    a = a * keep_connected(a > 0.15, inner).astype(np.float32)

    # AND IT MUST BE MORE THAN A WISP. The band picks up hairline tendrils where the
    # generator's brushwork runs off the figure, and a sliver of the furniture beside it
    # survives connectivity by touching an elbow. A morphological opening - erode, then
    # dilate back - deletes anything narrower than a fortieth of the figure and leaves
    # everything thicker untouched, which is every part of a person at this size. The
    # core is put back afterwards so the opening can never bite into the body itself.
    if open_frac > 0:
        r = max(1, int(round(width * open_frac)))
        solid = a > 0.15
        solid = dilate(erode(solid, r), r) | inner
        a = a * solid.astype(np.float32)
    return a, im


def key(path, tol, soft, cel=None):
    """(alpha, rgb) for a generated frame, chroma if it is on a green field and the flood
    key if it is not. Decided by looking: a green screen leaves a third of the frame
    strongly green, and nothing else does."""
    rgb = np.asarray(Image.open(path).convert("RGB"))
    im = rgb.astype(np.float32)

    # THE SILHOUETTE MATTE WINS WHENEVER THERE IS A SILHOUETTE. This used to sniff for a
    # green background first and hand those frames to the chroma key, which was right while
    # the generator was aiming for a green screen and became a trap when it stopped: the
    # backgrounds still come out green-ish often enough to trip a 15% test, and on a
    # SPECKLED green field the chroma key keeps nine tenths of the frame. Views 275, 276,
    # 295 and 297 all came back as a figure in a full box of texture, and their boil went
    # to 16-18 against a baseline of 0.7. The chroma key is still here, but only on request.
    if cel is not None:
        return silhouette_matte(rgb, cel, tol, soft)
    greenish = ((im[:, :, 1] - np.maximum(im[:, :, 0], im[:, :, 2])) > 30).mean()
    if greenish > 0.15:
        return chroma_key(rgb, tol, soft)
    return flood_background(rgb, tol, soft), im


def refit_for(mattes, cels):
    """ONE mapping for a whole four-cel loop, from the UNION of the four mattes.

    This is per-VIEW and not per-cel, and getting that wrong is measurable. The pose tells
    the model how tall to draw the figure and the model approximates it, so each cel comes
    out a percent or two off - and fitting each one to its own extent then makes the whole
    figure jump a percent or two between frames. The consistency number for that is the
    `boil` in --measure, and it went from 0.9 on the ported art to 25-31 on the generated
    art: every pixel moving every frame, which is exactly what it is meant to catch.

    The union of the four extents is the choice that cannot clip: every cel is mapped by
    the same scale and offset, the walk's own motion survives inside it, and the figure as
    a whole sits still. The original's four cels are one size and one alignment for the
    same reason.

    Returns (s, ox, oy) in the same sense as poses.json's `fit`: canvas = cel * s + o."""
    ys = [np.nonzero(a > 0.35) for a in mattes]
    ys = [(y, x) for y, x in ys if len(y) > 32]
    if not ys:
        return None
    gy0 = min(float(y.min()) for y, _ in ys)
    gy1 = max(float(y.max()) for y, _ in ys)
    gx0 = min(float(x.min()) for _, x in ys)
    gx1 = max(float(x.max()) for _, x in ys)

    cel = cels[0]
    top1, _bot1, span1 = cel["bbox"]
    s = max(1e-6, (gy1 - gy0) / max(1.0, span1 - 1.0))
    oy = gy0 - top1 * s

    # Horizontally, centre on the original figure's own centre, taken over the same four
    # cels so a swinging arm does not shift the whole body.
    lo, hi = [], []
    for c in cels:
        p1 = os.path.join(PNG1X, f"view_{c['view']}_l{c['loop']}_c{c['cel']}.png")
        if not os.path.exists(p1):
            continue
        m1 = np.asarray(Image.open(p1).convert("RGBA"))[:, :, 3] > 128
        col = np.nonzero(m1.any(axis=0))[0]
        if len(col):
            lo.append(float(col.min()))
            hi.append(float(col.max()) + 1.0)
    mid1 = (min(lo) + max(hi)) / 2.0 if lo else cel["cel_size"][0] / 2.0
    ox = (gx0 + gx1) / 2.0 - mid1 * s
    return s, ox, oy


def fit_one(gen_path, cel, factor, clip_frac, tol, soft, refit=None, alpha=None, src=None):
    """One generated frame -> one RGBA cel at `factor` x the original."""
    if alpha is None:
        alpha, src = key(gen_path, tol, soft, cel)

    cw, ch = cel["cel_size"]
    s, ox, oy = cel["fit"]

    if refit is not None:
        s, ox, oy = refit

    # The pose was drawn with  canvas = cel * s + o.  Going back and out to `factor`:
    #   out = (canvas - o) * (factor / s)
    # so the output is the generated frame scaled by factor/s and cropped to the window
    # the cel's own box maps to.
    k = factor / s
    out_w, out_h = cw * factor, ch * factor

    # PREMULTIPLY BEFORE RESAMPLING. Lanczos on straight alpha averages the flat grey of
    # the background into every edge pixel and hands the sprite back with the exact halo
    # this whole exercise was supposed to be rid of - the same trap tools/Upscale
    # documents about the 1x decode's skip runs, arriving by a different route.
    a0 = alpha[:, :, None]
    pre = np.dstack([src * a0, alpha * 255.0])
    nh, nw = (max(1, int(round(v * k))) for v in src.shape[:2])
    big = Image.fromarray(np.clip(pre, 0, 255).astype(np.uint8), "RGBA") \
               .resize((nw, nh), Image.LANCZOS)
    b = np.asarray(big).astype(np.float32)
    aa = b[:, :, 3:4] / 255.0
    b[:, :, :3] = np.where(aa > 1e-3, b[:, :, :3] / np.maximum(aa, 1e-3), 0.0)
    big = Image.fromarray(np.clip(b, 0, 255).astype(np.uint8), "RGBA")

    # out = (canvas - o) * k  and  big = canvas * k,  so the output window [0, out_w) sits
    # at big pixel [ox*k, ox*k + out_w). ox is negative whenever the figure's hip is right
    # of the canvas centre, and a cel wider than the generated frame runs off both ends,
    # so crop through a pad rather than clamping - PIL's crop pads with zeros only if the
    # box is inside, and silently returns garbage otherwise.
    left = int(round(ox * k))
    top = int(round(oy * k))
    pad = Image.new("RGBA", (big.width + 2 * out_w, big.height + 2 * out_h), (0, 0, 0, 0))
    pad.alpha_composite(big, (out_w, out_h))
    canvas = pad.crop((left + out_w, top + out_h,
                       left + out_w + out_w, top + out_h + out_h))

    arr = np.asarray(canvas).astype(np.float32)

    # The clip. The original cel's own alpha, generously dilated.
    clipped = 0.0
    if clip_frac > 0:
        p1 = os.path.join(PNG1X, f"view_{cel['view']}_l{cel['loop']}_c{cel['cel']}.png")
        o = np.asarray(Image.open(p1).convert("RGBA"))[:, :, 3] > 128
        o = np.asarray(Image.fromarray((o * 255).astype(np.uint8))
                       .resize((out_w, out_h), Image.NEAREST)) > 128
        width = max(1, int(o.any(axis=0).sum()))
        o = dilate(o, max(2, int(round(width * clip_frac))))
        before = arr[:, :, 3].sum()
        arr[:, :, 3] *= o
        after = arr[:, :, 3].sum()
        clipped = 0.0 if before <= 0 else (before - after) / before

    # Premultiplied-safe: kill the colour under fully transparent pixels so no resampler
    # downstream can average an invisible colour into a visible edge. This is the same
    # trap tools/Upscale documents about the 1x decode's skip runs.
    a = arr[:, :, 3:4] / 255.0
    arr[:, :, :3] *= (a > 0)

    return Image.fromarray(arr.astype(np.uint8), "RGBA"), clipped


# ---------------------------------------------------------------------------
# The character-select screen.
#
# `MainViewModel.BuildSelect2` (script 235): view 500 is the background and THE FOUR
# CHARACTERS ARE PAINTED INTO IT, 45x97 each at nsLeft 1/47/93/139, nsTop 14. View 499
# loop 0 is only the highlight over the chosen one and loop 1 the player-number badge -
# neither contains a person.
#
# So this screen has to be done or the game shows the 1990 photographs on the select
# screen and the new characters on the board, which is worse than either alone. The four
# panels are flat single colours, which makes the old figure liftable and the new one
# compositable without touching the title lettering or the marble icons.
#
# The portraits are the CHEAP outfit - base+2, the same views room1.sc:1302-1312 puts on
# the board - so they are generated already and no extra diffusion run is needed.
# ---------------------------------------------------------------------------

PORTRAIT_X = [1, 47, 93, 139]
PORTRAIT_Y, PORTRAIT_W, PORTRAIT_H = 14, 45, 97
PORTRAIT_VIEWS = [282, 286, 292, 296]


def largest_component(mask):
    """The biggest connected True region, so the figure is taken and the marble icon in
    the corner of each panel is left alone."""
    h, w = mask.shape
    seen = np.zeros_like(mask)
    best = None
    for sy in range(h):
        for sx in range(w):
            if not mask[sy, sx] or seen[sy, sx]:
                continue
            comp = np.zeros_like(mask)
            stack = [(sy, sx)]
            while stack:
                y, x = stack.pop()
                if y < 0 or y >= h or x < 0 or x >= w or seen[y, x] or not mask[y, x]:
                    continue
                seen[y, x] = True
                comp[y, x] = True
                stack += [(y - 1, x), (y + 1, x), (y, x - 1), (y, x + 1)]
            if best is None or comp.sum() > best.sum():
                best = comp
    return best if best is not None else mask


def portraits(a):
    """Rebuilds view 500 with the new cast in the four panels."""
    with open(os.path.join(a.poses, "poses.json")) as f:
        poses = json.load(f)
    src1 = np.asarray(Image.open(os.path.join(PNG1X, "view_500_l0_c0.png"))
                      .convert("RGBA"))
    big = os.path.join(a.against, "view_500_l0_c0.png")
    if not os.path.exists(big):
        raise SystemExit(f"{big} is missing - run tools/Upscale {a.factor} 4 first")
    out = Image.open(big).convert("RGBA")
    F = a.factor
    if out.size != (src1.shape[1] * F, src1.shape[0] * F):
        raise SystemExit(f"{big} is {out.size}, expected "
                         f"{(src1.shape[1] * F, src1.shape[0] * F)}")

    for i, x in enumerate(PORTRAIT_X):
        # The last panel starts at 139 and is 45 wide, which runs one pixel off the right
        # edge of a 183-wide pic. Clamp, or the 12x slice is 528 columns where the mask is
        # 540 and numpy refuses the assignment.
        pw = min(PORTRAIT_W, src1.shape[1] - x)
        ph = min(PORTRAIT_H, src1.shape[0] - PORTRAIT_Y)
        panel = src1[PORTRAIT_Y:PORTRAIT_Y + ph, x:x + pw, :3]
        back = panel[0, 0].astype(np.int16)
        off = (np.abs(panel.astype(np.int16) - back).max(axis=2) > 12)
        fig = largest_component(off)
        rows = np.nonzero(fig.any(axis=1))[0]
        cols = np.nonzero(fig.any(axis=0))[0]
        if len(rows) == 0:
            continue
        top, bot = int(rows[0]), int(rows[-1])
        cx = (int(cols[0]) + int(cols[-1]) + 1) / 2.0

        # Erase the old figure by painting the panel colour back over it, dilated by a
        # whole 1x pixel so the Mitchell resample's soft edge goes with it.
        big_fig = np.asarray(Image.fromarray((dilate(fig, 1) * 255).astype(np.uint8))
                             .resize((pw * F, ph * F), Image.NEAREST)) > 128
        arr = np.asarray(out).copy()
        y0, x0 = PORTRAIT_Y * F, x * F
        sub = arr[y0:y0 + ph * F, x0:x0 + pw * F]
        sub[:, :, :3][big_fig] = back.astype(np.uint8)
        out = Image.fromarray(arr, "RGBA")

        # The new figure, at the height the old one occupied, on the same centre and the
        # same ground line - the panels are a fixed layout and the highlight rectangle
        # view 499 draws over them does not move.
        gen = os.path.join(a.gen, f"raw_{PORTRAIT_VIEWS[i]}_l0_c0.png")
        if not os.path.exists(gen):
            print(f"  panel {i}: no generated frame for view {PORTRAIT_VIEWS[i]}, left alone")
            continue
        al, rgb = key(gen, a.tol, a.soft,
                      poses["cels"].get(f"{PORTRAIT_VIEWS[i]}/0/0"))
        fg = Image.fromarray(np.clip(np.dstack([rgb, al * 255.0]), 0, 255).astype(np.uint8),
                             "RGBA")
        bb = fg.getbbox()
        if bb is None:
            continue
        fg = fg.crop(bb)
        want_h = (bot - top + 1) * F
        k = want_h / fg.size[1]
        fg = fg.resize((max(1, int(round(fg.size[0] * k))), want_h), Image.LANCZOS)
        px = int(round(x0 + cx * F - fg.size[0] / 2))
        py = int(round(y0 + top * F))
        out.alpha_composite(fg, (max(0, px), max(0, py)))
        print(f"  panel {i}: view {PORTRAIT_VIEWS[i]} -> {fg.size[0]}x{fg.size[1]} "
              f"at ({px},{py})")

    os.makedirs(a.out, exist_ok=True)
    p = os.path.join(a.out, "view_500_l0_c0.png")
    out.save(p)
    print(f"  {p}  {out.size[0]}x{out.size[1]}")


def loop_stats(paths, view, loop):
    """(step, boil) over one four-cel loop, in 0-255 levels, or None."""
    imgs = []
    for c in range(4):
        p = paths(view, loop, c)
        if p is None or not os.path.exists(p):
            return None
        imgs.append(np.asarray(Image.open(p).convert("RGBA")).astype(np.float32))
    if len({i.shape for i in imgs}) != 1:
        return None
    h, w = imgs[0].shape[:2]

    # Composite over mid grey so a change in ALPHA counts as a change - a limb that
    # appears and disappears is exactly the flicker being looked for, and comparing RGB
    # alone would miss it entirely.
    def flat(a):
        al = a[:, :, 3:4] / 255.0
        return a[:, :, :3] * al + 128.0 * (1.0 - al)

    f = [flat(i) for i in imgs]

    step = float(np.mean([np.abs(f[(c + 1) % 4] - f[c]).mean() for c in range(4)]))

    # The still mask, from the 1x decode, resized to whatever these cels are.
    ones = []
    for c in range(4):
        p1 = os.path.join(PNG1X, f"view_{view}_l{loop}_c{c}.png")
        if not os.path.exists(p1):
            return step, float("nan")
        a = np.asarray(Image.open(p1).convert("RGBA")).astype(np.float32)
        al = a[:, :, 3:4] / 255.0
        ones.append(a[:, :, :3] * al + 128.0 * (1.0 - al))
    spread = np.max(ones, axis=0) - np.min(ones, axis=0)
    still = (spread.max(axis=2) <= 2.0)
    still = np.asarray(Image.fromarray((still * 255).astype(np.uint8))
                       .resize((w, h), Image.NEAREST)) > 128
    if still.sum() == 0:
        return step, float("nan")

    mean = np.mean(f, axis=0)
    dev = np.mean([np.abs(x - mean).mean(axis=2) for x in f], axis=0)
    return step, float(dev[still].mean())


def measure(a):
    with open(os.path.join(a.poses, "poses.json")) as f:
        poses = json.load(f)

    seen = set()
    print(f"{'view':>5} {'loop':>4} | {'step new':>9} {'step old':>9} | "
          f"{'boil new':>9} {'boil old':>9}")
    rows = []
    for key, cel in sorted(poses["cels"].items()):
        vl = (cel["view"], cel["loop"])
        if vl in seen:
            continue
        seen.add(vl)
        new = loop_stats(lambda v, l, c: os.path.join(a.measure, f"view_{v}_l{l}_c{c}.png"),
                         *vl)
        old = loop_stats(lambda v, l, c: os.path.join(a.against, f"view_{v}_l{l}_c{c}.png"),
                         *vl) if a.against else None
        if new is None:
            continue
        o = old or (float("nan"), float("nan"))
        print(f"{vl[0]:>5} {vl[1]:>4} | {new[0]:>9.2f} {o[0]:>9.2f} | "
              f"{new[1]:>9.2f} {o[1]:>9.2f}")
        rows.append({"view": vl[0], "loop": vl[1],
                     "step_new": new[0], "step_old": o[0],
                     "boil_new": new[1], "boil_old": o[1]})
    if rows:
        print(f"{'mean':>10} | {np.nanmean([r['step_new'] for r in rows]):>9.2f} "
              f"{np.nanmean([r['step_old'] for r in rows]):>9.2f} | "
              f"{np.nanmean([r['boil_new'] for r in rows]):>9.2f} "
              f"{np.nanmean([r['boil_old'] for r in rows]):>9.2f}")
    with open(os.path.join(a.measure, "consistency.json"), "w") as f:
        json.dump(rows, f, indent=1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--poses", required=True)
    ap.add_argument("--gen")
    ap.add_argument("--out")
    ap.add_argument("--measure", help="report on a finished cel directory instead of building one")
    ap.add_argument("--portraits", action="store_true",
                    help="rebuild view 500, the character-select screen, with the new cast")
    ap.add_argument("--against", default=os.path.join(ROOT, "assets", "png12x"))
    ap.add_argument("--factor", type=int, default=12)
    ap.add_argument("--clip", type=float, default=0.0,
                    help="dilate the original silhouette by this fraction of the figure "
                         "width and clip the matte to it. Off by default: the silhouette "
                         "matte already bounds itself, and this only has a job when the "
                         "chroma or flood key is what ran")
    ap.add_argument("--tol", type=float, default=18.0,
                    help="colour distance from the background samples below which a pixel "
                         "in the unknown band counts as background")
    ap.add_argument("--soft", type=float, default=22.0,
                    help="width of the band over which alpha rises from 0 to 1")
    a = ap.parse_args()

    if a.measure:
        return measure(a)
    if not a.gen or not a.out:
        ap.error("--gen and --out are required unless --measure is given")
    if a.portraits:
        return portraits(a)

    with open(os.path.join(a.poses, "poses.json")) as f:
        poses = json.load(f)
    os.makedirs(a.out, exist_ok=True)

    # Grouped by view and loop, because the mapping is per-loop: see refit_for.
    loops = {}
    for k, cel in sorted(poses["cels"].items()):
        loops.setdefault((cel["view"], cel["loop"]), []).append((k, cel))

    report = {}
    for (v, l), entries in sorted(loops.items()):
        paths = [os.path.join(a.gen, f"raw_{v}_l{l}_c{c['cel']}.png") for _, c in entries]
        have = [(k, c, p) for (k, c), p in zip(entries, paths) if os.path.exists(p)]
        if not have:
            continue

        keyed = [key(p, a.tol, a.soft, c) for _, c, p in have]
        refit = refit_for([al for al, _ in keyed], [c for _, c, _ in have])

        for (k, cel, p), (al, rgb) in zip(have, keyed):
            im, clipped = fit_one(p, cel, a.factor, a.clip, a.tol, a.soft,
                                  refit=refit, alpha=al, src=rgb)
            name = f"view_{v}_l{l}_c{cel['cel']}.png"
            im.save(os.path.join(a.out, name))
            cov = float((np.asarray(im)[:, :, 3] > 8).mean())
            report[k] = {"file": name, "size": list(im.size),
                         "clipped": round(float(clipped), 4),
                         "coverage": round(float(cov), 4)}
            print(f"  {name}  {im.size[0]}x{im.size[1]}  clipped {clipped*100:4.1f}%  "
                  f"cover {cov*100:4.1f}%", flush=True)

    with open(os.path.join(a.out, "fit_walkers.manifest.json"), "w") as f:
        json.dump({"factor": a.factor, "clip": a.clip, "tol": a.tol, "soft": a.soft,
                   "cels": report}, f, indent=1)
    print(f"{len(report)} cels -> {a.out}")


if __name__ == "__main__":
    main()
