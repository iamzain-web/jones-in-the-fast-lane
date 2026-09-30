"""Builds a walk cycle by DEFORMING one generated photograph, instead of generating four.

WHY THIS EXISTS
---------------
Four independent photoreal samples cannot animate. Generated cel by cel from one seed and
one prompt, the four frames of view 280's loop came back as: jacket buttoned with both arms
down; jacket open, waistcoat showing, sleeves rolled, one hand in a pocket; jacket open,
BOTH hands in pockets, a different tie; jacket buttoned with a pale object under the arm.
That is four photographs of four slightly different men who share a face, and at 468 pixels
wide every re-invented fold and button is glaring in a way 39 dithered pixels hid completely.

The legs were worse, and a player put it best: "the legs are all over the place like he is
dancing". Measured, that is not a figure of speech. View 280's authored ankle separations are

    cel 0   148        cel 1    33        cel 2   148        cel 3    33

- a clean alternating stride. What came back was

    cel 0   126        cel 1    70        cel 2   172        cel 3   (feet not separable)

Cels 0 and 2 are authored IDENTICALLY and arrived 46 pixels apart. The pose channel was
being read as a suggestion and the model was choosing a plausible stance per frame. Four
plausible stances in sequence are not a cycle; the generator was not animating, it was
posing, four times.

WHAT THIS DOES INSTEAD
----------------------
One frame is generated - the rest pose, as good as it can be made, fully refined. The other
three are produced by moving THAT frame's pixels along the skeleton delta. Every fold,
every shadow, every button and the whole face therefore comes from one photograph, so
consistency is structural rather than hoped for, and the stride is exactly what the skeleton
says because the generator gets no vote.

THIS IS NOT THE img2img PATH, which was tried and killed. That fed cel 0 through the sampler
and the sampler preserved identity by freezing everything, legs included - 0.60 and 0.75
strength both produced a cel 1 that was 99% of cel 0's foot spread, because img2img skips
the early denoising steps where gross layout is decided. Here no sampler runs at all.

MOVING LEAST SQUARES, NOT A THIN-PLATE SPLINE
---------------------------------------------
The choice of deformation is what decides whether this works. A thin-plate spline through
the joints has no notion of a rigid limb and bends a forearm like a hose; the result reads
as rubber and would have been mistaken for "warping cannot carry this". MLS similarity
deformation (Schaefer, McPhail and Warren, 2006) derives a locally rigid-plus-uniform-scale
transform per pixel from the handles, so a limb moves like a limb. The uniform-scale term is
wanted here rather than tolerated: the vertical bob makes the figure genuinely taller and
shorter through the cycle.

Handles are the 18 authored keypoints plus the MIDPOINT OF EVERY BONE - a joint-only handle
set lets the middle of a thigh drift, because nothing is holding it - plus fixed anchors
around the frame edge so distant background is not dragged along.

USAGE
    python tools/warp_walk.py --poses <dir> --src <cut_V_l0_c0.png> --view 280 --out <dir>
"""

import argparse
import json
import os

import numpy as np
from PIL import Image

# The bones, as index pairs into the COCO-18 layout. Same list sprite_pose draws.
BONES = [(1, 2), (2, 3), (3, 4), (1, 5), (5, 6), (6, 7), (1, 8), (8, 9), (9, 10),
         (1, 11), (11, 12), (12, 13), (1, 0)]


def handles(kps, size, anchors=5, margin=0.0):
    """Control points for one skeleton: the joints, every bone's midpoint, and a ring of
    fixed anchors on the frame edge.

    THE MIDPOINTS ARE NOT PADDING. With handles only at the joints, MLS has nothing to say
    about the middle of a thigh and the weighting lets it drift between its two ends, which
    is the rubber-hose look this is chosen to avoid. A midpoint per bone roughly halves the
    unconstrained span and costs nothing.

    THE ANCHORS ARE WHAT KEEPS THE BACKGROUND STILL. Without them the deformation has no
    far-field constraint and every pixel in the frame is dragged by the nearest limb."""
    pts = [tuple(p) for p in kps]
    # QUARTER POINTS, NOT JUST MIDPOINTS. With a handle only at each end and one in the
    # middle, MLS still has a quarter of a thigh with nothing near it, and the similarity
    # term is free to bend it - the first result put visible S-curves through both trouser
    # legs on every warped cel. Three interior handles per bone shortens the unconstrained
    # span to an eighth of a limb and is what makes a leg move like a leg.
    for a, b in BONES:
        for t in (0.25, 0.5, 0.75):
            pts.append((kps[a][0] + (kps[b][0] - kps[a][0]) * t,
                        kps[a][1] + (kps[b][1] - kps[a][1]) * t))
    # THE ANCHORS SIT OUTSIDE THE FRAME, AND THAT IS NOT A DETAIL. On the frame edge they
    # are 63 pixels below the figure's soles, and MLS weights fall off with distance - so the
    # nearest thing to a foot being lifted was a fixed point that did not want it to move.
    # Measured: the cycle asks the swinging foot to rise 49 canvas pixels and the warp
    # delivered 1 to 5. Pushing the ring out by a margin keeps the far-field constraint that
    # stops the background being dragged, while putting it far enough away that a limb near
    # the edge of the frame is free.
    #
    # Removing anchors instead was tried and is worse: at two per edge the deformation had
    # no far-field constraint at all and the warp blew the figure off the canvas entirely.
    W, H = size
    m = margin
    for i in range(anchors):
        f = i / float(anchors - 1)
        x = -m + f * (W - 1 + 2 * m)
        y = -m + f * (H - 1 + 2 * m)
        pts += [(x, -m), (x, H - 1 + m), (-m, y), (W - 1 + m, y)]
    return np.asarray(pts, np.float64)


def mls_similarity(dst_pts, src_pts, size, alpha=1.6, step=4):
    """A backward displacement field: for each pixel of the OUTPUT, where it comes from in
    the source image.

    Backward is the only sane direction for image warping - a forward map leaves holes
    wherever the deformation expands. So the handles are passed the other way round: the
    TARGET skeleton is `p` and the SOURCE skeleton is `q`, and evaluating the deformation at
    an output pixel yields its source coordinate directly.

    Computed on a grid every `step` pixels and bilinearly upsampled. The field is smooth by
    construction, a quarter-resolution grid is indistinguishable from a full one, and it is
    sixteen times less arithmetic on an 811k-pixel frame.
    """
    W, H = size
    gx = np.arange(0, W + step, step, dtype=np.float64)
    gy = np.arange(0, H + step, step, dtype=np.float64)
    VX, VY = np.meshgrid(gx, gy)

    p = dst_pts[:, None, None, :]                     # (n,1,1,2)
    q = src_pts[:, None, None, :]
    vx, vy = VX[None], VY[None]

    dx = p[..., 0] - vx
    dy = p[..., 1] - vy
    w = 1.0 / (dx * dx + dy * dy + 1e-8) ** alpha     # (n,gh,gw)
    sw = w.sum(axis=0)

    pstar_x = (w * p[..., 0]).sum(axis=0) / sw
    pstar_y = (w * p[..., 1]).sum(axis=0) / sw
    qstar_x = (w * q[..., 0]).sum(axis=0) / sw
    qstar_y = (w * q[..., 1]).sum(axis=0) / sw

    phx = p[..., 0] - pstar_x
    phy = p[..., 1] - pstar_y
    qhx = q[..., 0] - qstar_x
    qhy = q[..., 1] - qstar_y

    mu = (w * (phx * phx + phy * phy)).sum(axis=0)
    mu = np.where(np.abs(mu) < 1e-8, 1e-8, mu)

    ddx = VX - pstar_x
    ddy = VY - pstar_y

    # THE 2x2 PER HANDLE, WORKED OUT RATHER THAN TRANSCRIBED. Schaefer's A_i is
    #
    #     A_i = w_i * [ ph ; -perp(ph) ] * [ d ; -perp(d) ]^T
    #
    # with perp(a) = (-a_y, a_x). Multiplying those two out collapses to
    #
    #     A_i = w_i * [[ ph.d,  cross ],
    #                  [ -cross, ph.d ]]      where cross = ph_x*d_y - ph_y*d_x
    #
    # so with qh as a row vector, qh * A_i is
    #
    #     x:  qh_x*(ph.d) - qh_y*cross
    #     y:  qh_x*cross  + qh_y*(ph.d)
    #
    # The first version had the y row as -(qh_x*cross + qh_y*(ph.d)) - the whole component
    # negated - which mirrors every displacement about q*_y and folds the field over itself.
    # It did not look like a sign error, it looked like the method failing: heads torn into
    # butterflies and limbs duplicated, exactly what "warping cannot carry this" would look
    # like. Derive the matrix, do not copy it.
    ph_d = phx * ddx + phy * ddy
    cross = phx * ddy - phy * ddx

    fx = (w * (qhx * ph_d - qhy * cross)).sum(axis=0) / mu + qstar_x
    fy = (w * (qhx * cross + qhy * ph_d)).sum(axis=0) / mu + qstar_y

    # Upsample the coarse field to every pixel.
    yy = np.arange(H, dtype=np.float64) / step
    xx = np.arange(W, dtype=np.float64) / step
    y0 = np.clip(yy.astype(int), 0, fx.shape[0] - 2)
    x0 = np.clip(xx.astype(int), 0, fx.shape[1] - 2)
    ty = (yy - y0)[:, None]
    tx = (xx - x0)[None, :]

    def up(F):
        a = F[y0][:, x0]
        b = F[y0][:, x0 + 1]
        c = F[y0 + 1][:, x0]
        d = F[y0 + 1][:, x0 + 1]
        return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty

    return up(fx), up(fy)


def sample(img, mx, my):
    """Bilinear fetch of `img` (H,W,C float) at the float coordinates (mx,my)."""
    H, W = img.shape[:2]
    x0 = np.floor(mx).astype(np.int64)
    y0 = np.floor(my).astype(np.int64)
    tx = (mx - x0)[..., None]
    ty = (my - y0)[..., None]
    x0c = np.clip(x0, 0, W - 1)
    x1c = np.clip(x0 + 1, 0, W - 1)
    y0c = np.clip(y0, 0, H - 1)
    y1c = np.clip(y0 + 1, 0, H - 1)
    out = (img[y0c, x0c] * (1 - tx) + img[y0c, x1c] * tx) * (1 - ty) + \
          (img[y1c, x0c] * (1 - tx) + img[y1c, x1c] * tx) * ty
    # Anything that came from outside the frame is background, not edge-clamped colour.
    inside = (mx >= 0) & (mx <= W - 1) & (my >= 0) & (my <= H - 1)
    return out * inside[..., None]


# The leg joints: knees and ankles. These are the handles whose motion MLS damps hardest,
# because moving a lower limb a long way laterally is the largest local deformation asked of
# it anywhere in the cycle.
LEGS = (9, 10, 12, 13)


def exaggerate(src_kps, dst_kps, k, joints=LEGS):
    """Pushes the target leg joints further from the source so that a DAMPED warp lands on
    the amplitude actually wanted.

    MLS resists large local deformation: asked to bring the ankles from 148 canvas pixels
    apart to 33, the warp delivered a silhouette narrowing of 28% where the authored delta
    is 78% and the 1990 art achieves 63%. The stride pattern was right and repeatable, just
    shallow. Rather than fight the solver - lowering the anchor count to free it up made the
    warp blow up entirely - the target is overshot by `k` and the damping brings it back.

    This is a correction for a known, measured property of the deformation, not a fudge of
    the authored cycle: `pose_author` still states the true stride, and the amplitudes in it
    are still the ones measured off the 1990 art. Only what is handed to the warp is scaled.
    """
    out = np.array(dst_kps, np.float64)
    for j in joints:
        out[j] = src_kps[j] + (dst_kps[j] - src_kps[j]) * k
    return out


def warp(src_rgba, src_kps, dst_kps, size, alpha=1.6, step=4, anchors=5, margin=0.0):
    """One deformed frame. Alpha is warped with the colour, so the silhouette moves too."""
    P = handles(dst_kps, size, anchors, margin)
    Q = handles(src_kps, size, anchors, margin)
    mx, my = mls_similarity(P, Q, size, alpha=alpha, step=step)
    # Premultiply before resampling, for the reason fit_walkers documents at length: straight
    # alpha averages the background into every edge pixel and hands back a halo.
    a = src_rgba[:, :, 3:4] / 255.0
    pre = np.dstack([src_rgba[:, :, :3] * a, src_rgba[:, :, 3:4]])
    got = sample(pre.astype(np.float64), mx, my)
    al = np.clip(got[:, :, 3:4], 0, 255)
    rgb = np.where(al > 1e-3, got[:, :, :3] / np.maximum(al / 255.0, 1e-3), 0.0)
    return np.clip(np.dstack([rgb, al]), 0, 255).astype(np.uint8)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--poses", required=True)
    ap.add_argument("--src", required=True, help="the one generated cel, RGBA and cut out")
    ap.add_argument("--view", type=int, required=True)
    ap.add_argument("--loop", type=int, default=0)
    ap.add_argument("--src-cel", type=int, default=0,
                    help="which cel of the cycle the source frame was generated against")
    ap.add_argument("--out", required=True)
    ap.add_argument("--alpha", type=float, default=1.6,
                    help="MLS falloff; higher makes each handle's influence more local")
    ap.add_argument("--step", type=int, default=4)
    ap.add_argument("--exaggerate", type=float, default=1.0,
                    help="overshoot the target LEG joints by this factor, to land on the "
                         "wanted amplitude after MLS damps it. 1.0 is off")
    ap.add_argument("--margin", type=float, default=0.0,
                    help="push the anchor ring this far OUTSIDE the frame, so a limb near "
                         "the edge is not pinned by it")
    ap.add_argument("--anchors", type=int, default=5,
                    help="fixed handles per frame edge. They stop the background being "
                         "dragged, but they also RESIST the deformation: too many, too "
                         "close, and the legs move a fraction of what the skeleton asks")
    a = ap.parse_args()

    with open(os.path.join(a.poses, "poses.json")) as f:
        poses = json.load(f)
    W, H = poses["size"]
    src = np.asarray(Image.open(a.src).convert("RGBA")).astype(np.float64)
    if src.shape[:2] != (H, W):
        raise SystemExit(f"{a.src} is {src.shape[1]}x{src.shape[0]}, expected {W}x{H}")

    key = f"{a.view}/{a.loop}/%d"
    src_k = np.asarray(poses["cels"][key % a.src_cel]["keypoints"], np.float64)
    os.makedirs(a.out, exist_ok=True)

    for c in range(4):
        cel = poses["cels"].get(key % c)
        if cel is None:
            continue
        dst_k = np.asarray(cel["keypoints"], np.float64)
        if c == a.src_cel:
            out = src.astype(np.uint8)
            note = "source, unchanged"
        else:
            tgt = (exaggerate(src_k, dst_k, a.exaggerate)
                   if a.exaggerate != 1.0 else dst_k)
            out = warp(src, src_k, tgt, (W, H), a.alpha, a.step, a.anchors, a.margin)
            note = "warped"
        p = os.path.join(a.out, f"cut_{a.view}_l{a.loop}_c{c}.png")
        Image.fromarray(out, "RGBA").save(p)
        print(f"  {os.path.basename(p)}  {note}", flush=True)
    print(f"4 cels -> {a.out}")


if __name__ == "__main__":
    main()



