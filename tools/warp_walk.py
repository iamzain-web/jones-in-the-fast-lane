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
from PIL import Image, ImageFilter

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


def rigid_span(alpha_src, joint, size, fig, reach=0.10, floor=0.045):
    """Where the rigid mass hanging off `joint` actually LIES, from the source alpha.

    A shoe and a briefcase have the same problem: they are solid objects attached to the end
    of a limb, they cannot deform, and no keypoint marks them. A bone guessed as "straight
    down from the joint" misses the part that sticks out - a shoe's toe, a case's far corner -
    and that part is then left to the limb's own transform, which stretches it. The residue
    is the horn.

    So the bone is aimed at the mass instead of guessed: take the ink within `reach` of the
    joint and on the far side of it, and return the vector from the joint to that ink's
    furthest extent. The caller builds a bone along it whose source and target lengths are
    EQUAL, so the object is carried and never deformed.
    """
    W, H = size
    r = reach * fig
    x0, y0 = int(max(0, joint[0] - r)), int(max(0, joint[1] - r))
    x1, y1 = int(min(W, joint[0] + r)), int(min(H, joint[1] + r + r))
    if x1 <= x0 or y1 <= y0:
        return np.array([0.0, floor * fig])
    win = alpha_src[y0:y1, x0:x1] > 100
    ys, xs = np.nonzero(win)
    if len(ys) < 20:
        return np.array([0.0, floor * fig])
    d = np.stack([xs + x0 - joint[0], ys + y0 - joint[1]], axis=1)
    keep = d[:, 1] > 0                       # below the joint only
    if keep.sum() < 20:
        return np.array([0.0, floor * fig])
    d = d[keep]
    # The far extent, not the centroid: the bone has to REACH the toe, not stop at the
    # middle of the shoe, or the toe is exactly what gets left behind.
    far = d[np.argmax((d ** 2).sum(axis=1))].astype(np.float64)
    n = float(np.hypot(*far))
    if n < 1e-6:
        return np.array([0.0, floor * fig])
    # THE DIRECTION IS READ, THE LENGTH IS FIXED. Aiming the bone at the toe is what was
    # wanted; letting it REACH the toe is not. Measured twice, on two different views: a
    # foot bone of 0.045 of figure height scores 2.20 px of seam deviation, 0.075 scores
    # 4.31, and on view 282 an unclamped 0.077 took one cel from 4.09 to 7.53. A long rigid
    # bone reaches back up into the shin and fights it, and that costs more than the horn it
    # was meant to remove. So: use the measured DIRECTION, clamp the LENGTH.
    return far / n * (floor * fig)

def shoe_mask(rgb, alpha, src_ankles, fig, tol=52.0, grow=7):
    """The SHOE pixels in the source, per foot, found by colour rather than by shape.

    The sole is sampled a little above the figure's lowest row and beside each ankle, which
    is shoe on every character in the cast. Everything within `tol` of that colour, opaque,
    and inside a generous box around the ankle is shoe; the trouser above is a different
    tone and falls outside, which is the whole point.
    """
    op = alpha > 100
    ys = np.nonzero(op.any(axis=1))[0]
    if not len(ys):
        return [np.zeros(op.shape, bool) for _ in src_ankles]
    bot = int(ys[-1])
    masks = []
    for ax, ay in src_ankles:
        x0, x1 = int(max(0, ax - 0.09 * fig)), int(min(op.shape[1], ax + 0.09 * fig))
        band = op[bot - int(0.020 * fig):bot - int(0.004 * fig), x0:x1]
        cols = rgb[bot - int(0.020 * fig):bot - int(0.004 * fig), x0:x1][band]
        if len(cols) < 30:
            masks.append(np.zeros(op.shape, bool))
            continue
        ref = np.median(cols, axis=0)
        y0, y1 = int(max(0, ay - 0.07 * fig)), int(min(op.shape[0], ay + 0.10 * fig))
        sub = rgb[y0:y1, x0:x1]
        d = np.sqrt(((sub - ref) ** 2).sum(axis=2))
        one = np.zeros(op.shape, bool)
        one[y0:y1, x0:x1] = (d < tol) & op[y0:y1, x0:x1]
        masks.append(one)
    if grow:
        masks = [np.asarray(Image.fromarray((mm * 255).astype(np.uint8))
                            .filter(ImageFilter.MaxFilter(grow))) > 127 for mm in masks]
    return masks


def shirt_mask(rgb, alpha, kps, fig, tol=60.0, grow=5):
    """The SHIRT's own pixels in the source, by colour, restricted to the ribcage's width.

    Same instrument as shoe_mask and for the same reason: geometry cannot tell a shirt from
    an arm, and the nearest-bone test hands 23.6% of the ribcage to the RIGHT arm's bones
    against 12.6% to the left - which is exactly the asymmetric skew a player reported.

    The horizontal limit matters as much as the colour. A short sleeve is the same cloth as
    the body of the shirt, but it sits on an arm that legitimately swings, so the mask is
    clipped to the ribcage's own width and the sleeves are left to the arm bones.
    """
    op = alpha > 100
    neck = np.asarray(kps[1], np.float64)
    hipm = (np.asarray(kps[8], np.float64) + np.asarray(kps[11], np.float64)) / 2.0
    y0 = int(max(0, neck[1] - 0.02 * fig))
    y1 = int(min(op.shape[0], hipm[1] + 0.02 * fig))
    cx = 0.5 * (neck[0] + hipm[0])
    x0 = int(max(0, cx - 0.115 * fig))
    x1 = int(min(op.shape[1], cx + 0.115 * fig))
    out = np.zeros(op.shape, bool)
    if y1 - y0 < 8 or x1 - x0 < 8:
        return out
    # Reference colour from the chest: central, just below the shoulder line.
    ry0 = int(neck[1] + 0.05 * fig)
    ry1 = int(neck[1] + 0.14 * fig)
    rx0, rx1 = int(cx - 0.04 * fig), int(cx + 0.04 * fig)
    ref_px = rgb[ry0:ry1, rx0:rx1][op[ry0:ry1, rx0:rx1]]
    if len(ref_px) < 50:
        return out
    ref = np.median(ref_px, axis=0)
    sub = rgb[y0:y1, x0:x1]
    d = np.sqrt(((sub - ref) ** 2).sum(axis=2))
    out[y0:y1, x0:x1] = (d < tol) & op[y0:y1, x0:x1]
    if grow:
        out = np.asarray(Image.fromarray((out * 255).astype(np.uint8))
                         .filter(ImageFilter.MaxFilter(grow))) > 127
    return out

def skin_field(src_kps, dst_kps, size, alpha=2.0, step=2, reach=0.26, fig=None,
               across=1.0, foot_w=1.0, alpha_src=None, rgb_src=None, props=(), hard_foot=0.160):
    """A backward displacement field by LINEAR BLEND SKINNING - one transform per BONE.

    WHY THIS REPLACED MOVING LEAST SQUARES. MLS drives the image from scattered point
    handles, and that has two failure modes at opposite ends of one dial:

        too few handles  a thigh has nothing holding its middle and bends like a hose
        too many         the surface RIPPLES between them, with a period equal to the
                         handle spacing - 40 to 60 pixels along the leg, which is exactly
                         the "squiggly" trouser seams a player saw in the game

    Tuning moves between them; it does not escape them. Skinning has no point handles at
    all. Each BONE gets one similarity transform derived from its own two endpoints, every
    pixel is weighted by its distance to the bone SEGMENT, and the transforms are blended.
    A straight trouser seam then stays straight by construction, because every pixel along
    it is driven by the same transform rather than by five competing handles.

    THE IDENTITY TERM IS WHAT HOLDS THE BACKGROUND STILL, and it replaces the ring of frame
    anchors MLS needed. It is a virtual bone whose transform is "stay where you are", with a
    fixed weight set by `reach`: within a quarter of the figure's height the real bones
    dominate, and far outside it the identity does, so distant pixels map to themselves and
    nothing is dragged. No anchors, so nothing pins a foot that is trying to lift.

    JOINT BLENDING is deliberate rather than incidental. Near a knee, two segments are both
    close and their transforms blend - which is where skinning has its own classic artifact,
    a collapse of volume. `alpha` sets how sharply influence falls off and therefore how wide
    that blend zone is: higher is more rigid with a harder seam at the joint, lower is
    smoother with more collapse. 2.0 keeps the blend to roughly a knee's width on a
    front-facing walk with modest bend, which is the friendly case.

    THE BONE TRANSFORM IS ANISOTROPIC, AND THAT IS NOT A REFINEMENT - IT IS WHAT MAKES KNEE
    FLEXION POSSIBLE AT ALL. In a FRONT view a bent knee cannot be drawn as an angle: both
    thigh and shin still project as near-vertical segments, and the only thing that changes
    is that they get SHORTER. Knee bend in this projection IS foreshortening, and nothing
    else. So the cycle asks for a target shin roughly 0.6 of the source shin's length.

    Under a SIMILARITY - one uniform scale, which is what this function used to apply - a
    target bone 0.6 as long samples the source 1/0.6 as far in BOTH directions, so the
    trouser leg comes back 0.6 as long AND 0.6 as WIDE. A real foreshortened leg keeps its
    width; only its length collapses. The similarity version therefore produces a thin,
    shrunken limb - which reads as a withered leg, not a bent one, and is visibly worse than
    no flexion at all. Had the two landed in separate rounds, the honest conclusion from the
    first would have been "knee flexion does not work", and the fault would have been here.

    `across` is the cross-bone scale, held at 1.0 so width is preserved while length is free.
    Set it to None to get the old uniform similarity back, which is worth having only to
    reproduce the failure deliberately.
    """
    W, H = size
    gx = np.arange(0, W + step, step, dtype=np.float64)
    gy = np.arange(0, H + step, step, dtype=np.float64)
    VX, VY = np.meshgrid(gx, gy)

    acc_x = np.zeros_like(VX)
    acc_y = np.zeros_like(VY)
    acc_w = np.zeros_like(VX)

    # THE SHOE NEEDS A BONE OF ITS OWN, and not having one is what turned every raised foot
    # into a black dagger. COCO-18 has no toe joint, so the lowest bone is the shin and the
    # shoe hangs BELOW its far endpoint - where the segment-distance term clamps at the end
    # of the bone and hands the whole shoe to the shin's transform. The moment the cycle
    # foreshortens that shin (which is what a bent knee IS in this view) the shoe is scaled
    # along the same axis and drawn out into a spike. That was blamed once on the foot lift
    # being too large and the lift was reduced; the lift was never the cause.
    #
    # So each leg gets a synthetic foot bone from the ankle to the sole. Its source and
    # target lengths are EQUAL, which makes its transform a pure translation - the shoe is
    # carried wherever the ankle goes and is not deformed at all. That is right for a front
    # view: a shoe swinging through a walk changes position far more than it changes shape.
    foot = 0.045 * (fig if fig else H)
    # The foot bone is SHORT and HEAVY rather than long. Lengthening it to cover the shoe
    # was tried first and made things worse - measured, seam RMS 2.20 -> 4.31 px - because a
    # long rigid foot reaches back up into the shin and fights it. A weight multiplier
    # instead lets the shoe win where the shoe is, without the bone extending anywhere near
    # the knee.
    # THE SHIN IS CAPPED AT THE ANKLE. Weighting by distance to the SEGMENT means a bone
    # keeps full influence just past its far endpoint, so the shin still owned the shoe even
    # with a foot bone present - and a shin being foreshortened 1.6:1 drags the far tip of
    # that shoe into a curved horn. Raising the foot bone's weight does not help (measured:
    # at 1, 3, 8 and 20 the horn is identical), because the shin's weight there is not small,
    # it is the largest in the neighbourhood. `cap` fades a bone out beyond its own end, so
    # the shin stops at the ankle and the shoe belongs to the foot.
    bones = [(np.asarray(src_kps[a], np.float64), np.asarray(src_kps[b], np.float64),
              np.asarray(dst_kps[a], np.float64), np.asarray(dst_kps[b], np.float64),
              1.0, False)
             for a, b in BONES]
    for kn, ank in ((9, 10), (12, 13)):
        for i, (bs0, bs1, bt0, bt1, bw, _) in enumerate(bones):
            if np.allclose(bt0, dst_kps[kn]) and np.allclose(bt1, dst_kps[ank]):
                bones[i] = (bs0, bs1, bt0, bt1, bw, True)
    # AIMED AT THE SHOE, NOT GUESSED AS STRAIGHT DOWN. A bone from the ankle straight down
    # misses the toe, which sticks out sideways, and the toe is then still owned by the shin
    # and still drawn into a horn. Measured on view 282, whose white trainers make the
    # residue obvious, the horn survived a foot bone, a weight of 20 and a capped shin -
    # because none of those put the BONE where the toe is. rigid_span reads the source alpha
    # and aims it at the mass.
    for ank in (10, 13):
        sa = np.asarray(src_kps[ank], np.float64)
        da = np.asarray(dst_kps[ank], np.float64)
        if alpha_src is not None:
            v = rigid_span(alpha_src, sa, size, fig if fig else H)
        else:
            v = np.array([0.0, foot])
        bones.append((sa, sa + v, da, da + v, foot_w, False))

    # PROPS. A briefcase, a shoulder bag or a case is a rigid object carried by a limb: the
    # same class as the shoe, and it was visibly changing shape between frames because the
    # arm bones were deforming it. Each named joint gets a bone aimed at whatever hangs off
    # it, again with equal source and target length, so the object translates and nothing
    # else. `props` is the joints to do this for - wrists, for a carried case.
    for j in props:
        sj = np.asarray(src_kps[j], np.float64)
        dj = np.asarray(dst_kps[j], np.float64)
        if alpha_src is None:
            continue
        v = rigid_span(alpha_src, sj, size, fig if fig else H, reach=0.16, floor=0.05)
        bones.append((sj, sj + v, dj, dj + v, foot_w, False))

    for s0, s1, t0, t1, bw, capped in bones:
        vt = t1 - t0
        vs = s1 - s0
        lt = float(np.hypot(*vt))
        ls = float(np.hypot(*vs))
        if lt < 1e-6 or ls < 1e-6:
            continue

        # The map taking the TARGET bone onto the SOURCE bone, resolved in the bone's own
        # frame so length and width can be scaled independently.
        #
        #   ut, pt   unit along the TARGET bone, and its perpendicular
        #   us, ps   the same for the SOURCE bone
        #
        # A pixel at offset d from the target bone's origin is decomposed as
        #   along  = d . ut        across = d . pt
        # and re-composed in source space as
        #   s0 + (along * s_along) * us + (across * s_across) * ps
        #
        # With s_along = s_across = ls/lt this is exactly the old similarity. With
        # s_across = 1 it is a rotation plus a pure stretch ALONG the bone, which is what a
        # limb rotating out of the picture plane actually does to its own pixels.
        ut = vt / lt
        pt = np.array([-ut[1], ut[0]])
        us = vs / ls
        ps = np.array([-us[1], us[0]])
        s_along = ls / lt
        s_across = s_along if across is None else float(across)

        dx = VX - t0[0]
        dy = VY - t0[1]
        along = (dx * ut[0] + dy * ut[1]) * s_along
        acrs = (dx * pt[0] + dy * pt[1]) * s_across
        mx = s0[0] + along * us[0] + acrs * ps[0]
        my = s0[1] + along * us[1] + acrs * ps[1]

        # Distance to the target SEGMENT, not to its endpoints - that is what makes a limb
        # a limb rather than two points with a gap between them.
        tproj = np.clip((dx * vt[0] + dy * vt[1]) / (lt * lt), 0.0, 1.0)
        cx = t0[0] + tproj * vt[0]
        cy = t0[1] + tproj * vt[1]
        d2 = (VX - cx) ** 2 + (VY - cy) ** 2

        w = bw / (d2 + 1.0) ** alpha
        if capped:
            # How far past the far endpoint this pixel lies, along the bone.
            past = np.maximum(0.0, (dx * vt[0] + dy * vt[1]) / lt - lt)
            w = w / (1.0 + (past / (0.30 * foot)) ** 2)
        acc_x += w * mx
        acc_y += w * my
        acc_w += w

    r = (reach * (fig if fig else H)) ** 2
    w_id = 1.0 / (r + 1.0) ** alpha
    acc_x += w_id * VX
    acc_y += w_id * VY
    acc_w += w_id

    fx = acc_x / acc_w
    fy = acc_y / acc_w

    # ===================================================================================
    # THE SHOE IS A PURE TRANSLATION, WITH A HARD BOUNDARY AT THE ANKLE
    # ===================================================================================
    # Every earlier attempt tried to make the shoe DEFORM correctly - a foot bone, then a
    # heavier one, then a longer one, then one aimed at the toe, then a soft cap on the
    # shin, then an opening on the alpha afterwards. All of them left a spike, and the
    # reason is that they were all answers to the wrong question. A 2D warp cannot rotate a
    # rigid object. So the shoe must not be asked to rotate: lift it, move it, put it down.
    #
    # A blend cannot express that. Linear blend skinning mixes the foot's transform with the
    # shin's, and the shin is being compressed 1.6:1 along the very axis the shoe lies on,
    # so ANY non-zero shin weight over the shoe shears it - and near the ankle the shin's
    # weight is not small, it is the largest in the neighbourhood. The spike is the blend
    # itself, which is why raising the foot bone's weight to 20 changed nothing.
    #
    # So the ankle gets a HARD boundary instead of a blend. A knee needs a soft one because
    # it is a joint that articulates; this ankle does not, because we are deliberately not
    # articulating it. Below the line, every pixel is the source shoe moved bodily - no
    # scale, no rotation, no shear.
    #
    # The cut runs slightly ABOVE the ankle so it lands inside the trouser hem rather than
    # at the shoe's top edge: the trouser is near-uniform there and the discontinuity does
    # not read. Pixels are assigned to the NEARER ankle, so a raised foot's region cannot
    # capture the planted foot's shoe when the two overlap vertically.
    #
    # Cost, accepted deliberately: a shoe that should be angled lands flat. The 1990 art has
    # one merged foot blob and no ankle articulation at all, so a flat carried shoe is
    # CLOSER to the original than a spike is.
    if hard_foot and rgb_src is not None:
        # THE RIGID REGION IS THE SHOE'S OWN PIXELS, FOUND BY COLOUR, ONE FOOT AT A TIME.
        #
        # Geometry was tried in five shapes - a disc, a box, three top edges and an
        # asymmetric window - and every one failed for the same reason: the thing that has to
        # be separated is a shoe from a trouser leg, and geometry cannot tell them apart.
        # Dumping the mask settled it. The ankle keypoint sits in the MIDDLE of the shoe's
        # height, not at its top, so a region starting at the ankle misses most of the shoe
        # while one starting above it takes trouser and cuts a notch out of the jean. No
        # offset does both, which is also why radius 0.105, 0.16 and 0.24 gave a
        # pixel-for-pixel identical spike: growing a region downward and sideways into
        # background never reaches up over the shoe.
        #
        # CONNECTIVITY CANNOT DO IT EITHER, and that is recorded here so it is not reached
        # for again: the source figure is ONE connected island of 177,993 pixels spanning
        # rows 56 to 1118. The shoe is continuous with the trouser, the leg and the torso.
        #
        # COLOUR separates them, because a shoe is a distinct tone from the trouser above it
        # on every character - white trainers on 282, black brogues on 280.
        #
        # AND EACH FOOT MUST BE TESTED AGAINST ITS OWN SHOE WITH ITS OWN TRANSLATION. A
        # combined "does this land on A shoe" test is not enough, and the coordinate dump
        # showed exactly why. On cel 0 the detached fragment sat at cols 214-269 and sampled
        # cols 261-317 - an offset of (+47,+1), which is the PLANTED foot's translation of
        # (+49,0), not the raised foot's (-33,+87). With one ankle lifted 87 px, ground-level
        # pixels beneath it are NEARER the planted ankle, so nearest-ankle assignment handed
        # them the planted foot's transform; that landed on the source's right shoe and drew
        # a shoe into the space the raised foot had vacated. Cel 2 mirrored it exactly:
        # offset (-48,+1) against the planted right foot's (-49,0).
        #
        # Asking "does foot F's own translation land on foot F's own shoe" catches it: the
        # vacated pixels fail both feet's tests and correctly stay background.
        #
        # Cost, accepted deliberately: a shoe that should be angled lands flat. The 1990 art
        # has one merged foot blob and no ankle articulation at all, so a flat carried shoe
        # is CLOSER to the original than a spike is.
        figv = fig if fig else H
        shoes = shoe_mask(rgb_src, alpha_src, [src_kps[10], src_kps[13]], figv)
        m = np.zeros(VX.shape, bool)
        for ank, sh in zip((10, 13), shoes):
            t0f = np.asarray(dst_kps[ank], np.float64)
            s0f = np.asarray(src_kps[ank], np.float64)
            gx = s0f[0] + (VX - t0f[0])
            gy = s0f[1] + (VY - t0f[1])
            own = sh[np.clip(gy.astype(int), 0, sh.shape[0] - 1),
                     np.clip(gx.astype(int), 0, sh.shape[1] - 1)]
            take = own & ~m
            fx = np.where(take, gx, fx)
            fy = np.where(take, gy, fy)
            m |= own

        # Anything OUTSIDE the rigid region that still reads shoe pixels is a smear of the
        # shoe rather than the shoe. Send it off-frame; `sample` resolves that to background
        # rather than to an edge-clamped colour.
        anysh = shoes[0] | shoes[1]
        smear = anysh[np.clip(fy.astype(int), 0, anysh.shape[0] - 1),
                      np.clip(fx.astype(int), 0, anysh.shape[1] - 1)] & ~m
        fx = np.where(smear, -1e4, fx)
        fy = np.where(smear, -1e4, fy)

    yy = np.arange(H, dtype=np.float64) / step
    xx = np.arange(W, dtype=np.float64) / step
    y0 = np.clip(yy.astype(int), 0, fx.shape[0] - 2)
    x0 = np.clip(xx.astype(int), 0, fx.shape[1] - 2)
    ty = (yy - y0)[:, None]
    tx = (xx - x0)[None, :]

    def up(F):
        p = F[y0][:, x0]
        q = F[y0][:, x0 + 1]
        r2 = F[y0 + 1][:, x0]
        s2 = F[y0 + 1][:, x0 + 1]
        return (p * (1 - tx) + q * tx) * (1 - ty) + (r2 * (1 - tx) + s2 * tx) * ty

    return up(fx), up(fy)


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


def warp(src_rgba, src_kps, dst_kps, size, alpha=1.6, step=4, anchors=5, margin=0.0,
         method="skin", fig=None, across=1.0, foot_w=1.0, props=(),
         hard_foot=0.160):
    """One deformed frame. Alpha is warped with the colour, so the silhouette moves too."""
    if method == "skin":
        mx, my = skin_field(src_kps, dst_kps, size, alpha=alpha, step=step, fig=fig,
                            across=across, foot_w=foot_w,
                            alpha_src=src_rgba[:, :, 3], props=props,
                            rgb_src=src_rgba[:, :, :3], hard_foot=hard_foot)
    else:
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


def seam_deviation(path, f0=0.62, f1=0.93):
    """How far the trouser seams bend away from straight, in pixels. The squiggle, as a
    number.

    A player found the rippled trouser seams by playing the game; nobody found them in four
    rounds of review because the comparison sheets were rendered at 26-30% scale, where a
    three-pixel wave is invisible. This turns "does it ripple" into something that does not
    need eyes at all, in the same spirit as the drift checker and the wardrobe check.

    The outer edge of each leg over the shin band is very nearly a straight line on a real
    photograph and on the 1990 art. Fit one, and report the RMS departure from it. A clean
    warp scores a pixel or two; a rippled one scores several times that."""
    a = np.asarray(Image.open(path).convert("RGBA"))[:, :, 3] > 128
    rows = np.nonzero(a.any(axis=1))[0]
    if len(rows) < 40:
        return None
    top, bot = int(rows[0]), int(rows[-1])
    span = bot - top + 1
    y0 = int(round(top + f0 * (span - 1)))
    y1 = int(round(top + f1 * (span - 1)))

    ys, left, right = [], [], []
    for y in range(y0, y1 + 1):
        xs = np.nonzero(a[y])[0]
        if not len(xs):
            continue
        ys.append(y)
        left.append(float(xs.min()))
        right.append(float(xs.max()))
    if len(ys) < 20:
        return None
    ys = np.asarray(ys, np.float64)

    # AGAINST A SMOOTHED EDGE, NOT AGAINST A STRAIGHT LINE. The first version fitted a line
    # and reported the residual, which measures LEG GEOMETRY rather than ripple: the 1990
    # art scored 27-45 px - far "worse" than any warp - simply because its legs are splayed
    # and its two-leg silhouette is not a straight vertical. The metric has to separate the
    # thing being looked for from the thing that is supposed to be there.
    #
    # A ripple is HIGH frequency and leg geometry is LOW frequency, so the reference is the
    # edge's own moving average over roughly a limb's width. What is left is the wiggle.
    win = max(5, int(round(len(ys) * 0.12)) | 1)
    k = np.ones(win) / win
    out = []
    for edge in (np.asarray(left), np.asarray(right)):
        pad = np.pad(edge, win // 2, mode="edge")
        smooth = np.convolve(pad, k, mode="valid")[:len(edge)]
        out.append(float(np.sqrt(np.mean((edge - smooth) ** 2))))
    return max(out)


def report_seams(a):
    import glob
    print(f"{'frame':<26} {'seam RMS (px)':>14}")
    worst = 0.0
    for p in sorted(glob.glob(os.path.join(a.seams, "*.png"))):
        d = seam_deviation(p)
        if d is None:
            continue
        worst = max(worst, d)
        flag = "   <-- ripples" if d > a.seam_tol else ""
        print(f"{os.path.basename(p):<26} {d:>14.2f}{flag}")
    print(f"\nworst {worst:.2f} px against a tolerance of {a.seam_tol:.1f}. "
          f"Judge character art at 100% or magnified, never scaled to fit a sheet.")


# VIEWS THAT CARRY A RIGID PROP, and the wrist it hangs from. COCO-18 wrists are 4 (right)
# and 7 (left). These are objects that cannot deform - a briefcase, a case, a shoulder bag -
# and without a bone of their own the arm's transform reshapes them every frame. Read off the
# art: 280 and 290 are men with briefcases, 284 and 285 are women with a case and a bag.
PROP_JOINTS = {280: (4,), 281: (4,), 282: (), 284: (7,), 285: (7,), 290: (4,), 291: (4,)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seams", help="report trouser-seam straightness for every png in "
                                    "this directory and exit")
    ap.add_argument("--seam-tol", type=float, default=3.0)
    ap.add_argument("--poses", help="pose set the TARGET cels come from")
    ap.add_argument("--src-poses",
                    help="pose set the SOURCE frame was generated against, when it differs "
                         "from --poses. The source skeleton must match the photograph or "
                         "every warp inherits a constant offset")
    ap.add_argument("--src", help="the one generated cel, RGBA and cut out")
    ap.add_argument("--view", type=int)
    ap.add_argument("--loop", type=int, default=0)
    ap.add_argument("--src-cel", type=int, default=0,
                    help="which cel of the cycle the source frame was generated against")
    ap.add_argument("--out")
    ap.add_argument("--method", choices=("skin", "mls"), default="skin",
                    help="skin = one rigid transform per bone (no point handles, so it "
                         "cannot ripple between them); mls = the old point-handle warp")
    ap.add_argument("--alpha", type=float, default=2.0,
                    help="MLS falloff; higher makes each handle's influence more local")
    ap.add_argument("--step", type=int, default=2)
    ap.add_argument("--across", type=float, default=1.0,
                    help="cross-bone scale. 1.0 (the default) preserves limb WIDTH while "
                         "the cycle foreshortens its LENGTH, which is the only way a bent "
                         "knee reads in a front view. -1 restores the old uniform "
                         "similarity, which narrows a foreshortened leg as it shortens it")
    ap.add_argument("--foot-w", type=float, default=1.0, dest="foot_w",
                    help="weight multiplier on the synthetic foot bone. Above 1 the shoe "
                         "wins over the shin in its own neighbourhood, which is what stops "
                         "a raised toe being drawn out into a horn")
    ap.add_argument("--hard-foot", type=float, default=0.160, dest="hard_foot",
                    help="radius, as a fraction of figure height, within which the shoe is "
                         "moved as a PURE TRANSLATION with a hard boundary at the ankle "
                         "instead of being blended with the shin. 0 restores the blend, "
                         "which puts the spike back")
    ap.add_argument("--props", help="override the rigid-prop joints for this view: a "
                                    "comma-separated list of COCO-18 indices, or 'none'")
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

    if a.seams:
        return report_seams(a)
    with open(os.path.join(a.poses, "poses.json")) as f:
        poses = json.load(f)
    W, H = poses["size"]
    src = np.asarray(Image.open(a.src).convert("RGBA")).astype(np.float64)
    if src.shape[:2] != (H, W):
        raise SystemExit(f"{a.src} is {src.shape[1]}x{src.shape[0]}, expected {W}x{H}")

    key = f"{a.view}/{a.loop}/%d"
    if a.src_poses:
        with open(os.path.join(a.src_poses, "poses.json")) as f:
            spos = json.load(f)
    else:
        spos = poses
    src_k = np.asarray(spos["cels"][key % a.src_cel]["keypoints"], np.float64)
    props = PROP_JOINTS.get(a.view, ())
    if a.props:
        props = () if a.props == "none" else tuple(int(v) for v in a.props.split(","))
    if props:
        print(f"  rigid prop bone at joint(s) {props}", flush=True)
    os.makedirs(a.out, exist_ok=True)

    for c in range(4):
        cel = poses["cels"].get(key % c)
        if cel is None:
            continue
        dst_k = np.asarray(cel["keypoints"], np.float64)
        # PASS THE SOURCE THROUGH ONLY IF ITS OWN CEL STILL WANTS THE POSE IT WAS SHOT IN.
        # `c == src_cel` is not that test: with --src-poses pointing at an older set, cel 0's
        # target has moved and handing back the untouched photograph puts one frame of the
        # loop in a stance the other three no longer belong to. Compare the skeletons.
        if float(np.abs(dst_k - src_k).max()) < 0.5:
            out = src.astype(np.uint8)
            note = "source, unchanged"
        else:
            tgt = (exaggerate(src_k, dst_k, a.exaggerate)
                   if a.exaggerate != 1.0 else dst_k)
            out = warp(src, src_k, tgt, (W, H), a.alpha, a.step, a.anchors, a.margin,
                       a.method, cel.get("fig_h"),
                       across=(None if a.across < 0 else a.across), foot_w=a.foot_w,
                       props=props, hard_foot=a.hard_foot)
            note = "warped"
        p = os.path.join(a.out, f"cut_{a.view}_l{a.loop}_c{c}.png")
        Image.fromarray(out, "RGBA").save(p)
        print(f"  {os.path.basename(p)}  {note}", flush=True)
    print(f"4 cels -> {a.out}")


if __name__ == "__main__":
    main()







