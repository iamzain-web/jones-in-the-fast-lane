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
# (4, 18) and (7, 19) are the HANDS. COCO-18 has no fingertip, so this chain used to end at
# the wrist while the `projected` measure ran shoulder-to-HAND - a fixed segment of the arm
# was carried by the forearm's transform without ever being shortened by it. Solving the
# realised range back for the length actually foreshortening gave a flat L_eff of 0.313 at
# 30, 35, 40 and 45 degrees, between shoulder-to-wrist (0.277) and shoulder-to-fingertip
# (0.382) and not moving with the knob. See Â§7o.
#
# pose_author appends 18 and 19; every COCO-18 index keeps its meaning, and
# sprite_pose.draw_pose is pinned to the first eighteen so the ControlNet image - and
# therefore every generated frame - is untouched.
# How far ABOVE the ankle the shoe window reaches, as a fraction of figure height.
#
# The ankle keypoint sits in the middle of a shoe's height, so the window has to reach above
# it - but on a figure whose trousers are the same tone as the shoe, the colour test inside
# that window has nothing to stop it and the mask swallows the hem. Measured on view 280 at
# 0.07: the mask began 75 px ABOVE the ankle, which is the window's own limit, so the "rigid
# shoe" was a rigid shoe-and-trouser and it tore away from the leg above it.
SHOE_TOP = 0.030

BONES = [(1, 2), (2, 3), (3, 4), (4, 18), (1, 5), (5, 6), (6, 7), (7, 19),
         (1, 8), (8, 9), (9, 10), (1, 11), (11, 12), (12, 13), (1, 0)]


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
    # AND EACH FOOT'S WINDOW STOPS AT THE MIDLINE BETWEEN THE TWO ANKLES.
    #
    # The window is +-0.09 of figure height, which is 92 px here, and that was safe only
    # because every pose it had been run on had the feet far apart. A source generated at a
    # NEUTRAL stance (Â§0b) puts the ankles 62 px apart, so both windows contain BOTH shoes:
    # measured on the mid-swing frame, the two masks came out 150 and 132 px wide against 64
    # and 67 on the old source, overlapping almost completely. Each foot's rigid translation
    # was then applied to a region holding the other foot's shoe as well, and the pair shredded
    # into fragments - which looked like the foot machinery failing and was the mask.
    #
    # A foot cannot extend past the midline between the two ankles, so that is where the
    # window ends. Geometry that was always true and was never enforced because no pose had
    # tested it.
    # AND THE MIDLINE CLIP THAT FIXED IT WAS ITSELF WRONG, FOR THE SAME POSE CLASS.
    #
    # The sentence above - "a foot cannot extend past the midline between the two ankles" -
    # is true of two feet side by side and FALSE IN A FRONT VIEW the moment one of them
    # swings forward. The forward foot is nearer the camera and its projected outline
    # crosses the midline; that is what foreshortening looks like from the front. Clipping
    # the window at a vertical line therefore sliced the toe off square, and because the
    # cut follows a column rather than the shoe, what came out was rectangular blocks -
    # torn hems, a shoe reduced to a stump, fragments detached below the foot, on all three
    # characters. The damage is in the WARP, not the fit: the source shoes are clean and
    # the warped cels are already shredded.
    #
    # Both the original bug and that fix came from treating this as a one-dimensional
    # partition. It is not. Every shoe pixel belongs to the ankle it is NEAREST to, which
    # gives each pixel to exactly one foot (so the windows cannot overlap) without ever
    # cutting along a straight line (so a crossing toe keeps its shape). It is the same
    # rule `leg_masks` already uses to split the legs, applied one joint further down.
    masks = []
    ank = [(float(p[0]), float(p[1])) for p in src_ankles]
    for ai, (ax, ay) in enumerate(ank):
        x0, x1 = int(max(0, ax - 0.09 * fig)), int(min(op.shape[1], ax + 0.09 * fig))
        band = op[bot - int(0.020 * fig):bot - int(0.004 * fig), x0:x1]
        cols = rgb[bot - int(0.020 * fig):bot - int(0.004 * fig), x0:x1][band]
        if len(cols) < 30:
            masks.append(np.zeros(op.shape, bool))
            continue
        ref = np.median(cols, axis=0)
        y0, y1 = int(max(0, ay - SHOE_TOP * fig)), int(min(op.shape[0], ay + 0.10 * fig))
        sub = rgb[y0:y1, x0:x1]
        d = np.sqrt(((sub - ref) ** 2).sum(axis=2))
        one = np.zeros(op.shape, bool)
        keep = (d < tol) & op[y0:y1, x0:x1]
        # Nearest-ankle ownership, in two dimensions. Whichever ankle this pixel is closer
        # to owns it; the other foot's mask cannot claim it, and nothing is cut on a column.
        if len(ank) == 2:
            yy, xx = np.mgrid[y0:y1, x0:x1]
            mine = (xx - ax) ** 2 + (yy - ay) ** 2
            ox, oy = ank[1 - ai]
            keep &= mine <= (xx - ox) ** 2 + (yy - oy) ** 2
        one[y0:y1, x0:x1] = keep
        # A SHOE HAS NO HOLES IN IT EITHER. The colour test drops the laces, the logo and the
        # dark eyelets, leaving small islands enclosed by shoe. While the shoe was carried as
        # a pure translation a two-pixel hole was invisible; once `foot_flex` stretches the
        # region, the hole stretches with it AND the smear rule below sends it off-frame,
        # which punched a green blob through the middle of the raised trainer. Found by
        # dumping the warped shoe at 4x, not by any number - the fragment count moved from
        # 21 px to 48 and never looked like a hole.
        lab = _components(~one)
        bord = np.unique(np.concatenate((lab[0], lab[-1], lab[:, 0], lab[:, -1])))
        one |= ~one & ~np.isin(lab, bord)
        masks.append(one)
    if grow:
        masks = [np.asarray(Image.fromarray((mm * 255).astype(np.uint8))
                            .filter(ImageFilter.MaxFilter(grow))) > 127 for mm in masks]
    return masks


def _components(mask):
    """Connected components by run-length union-find; returns a label image."""
    lab = np.zeros(mask.shape, np.int32)
    par = {}
    nxt = 1

    def find(a):
        while par[a] != a:
            par[a] = par[par[a]]
            a = par[a]
        return a

    for y in range(mask.shape[0]):
        idx = np.nonzero(mask[y])[0]
        if not len(idx):
            continue
        brk = np.nonzero(np.diff(idx) > 1)[0]
        st = np.concatenate(([0], brk + 1))
        en = np.concatenate((brk, [len(idx) - 1]))
        for s, e in zip(st, en):
            x0, x1 = idx[s], idx[e]
            above = lab[y - 1, x0:x1 + 1] if y else np.zeros(0, np.int32)
            hit = np.unique(above[above > 0])
            if not len(hit):
                lab[y, x0:x1 + 1] = nxt
                par[nxt] = nxt
                nxt += 1
            else:
                lab[y, x0:x1 + 1] = hit[0]
                for h in hit[1:]:
                    a, b = find(hit[0]), find(h)
                    if a != b:
                        par[b] = a
    if nxt == 1:
        return lab
    flat = np.zeros(nxt, np.int32)
    for k in range(1, nxt):
        flat[k] = find(k)
    return flat[lab]


def shirt_mask(rgb, alpha, kps, fig, tol=60.0, grow=5, arm_r=0.0, fill=True,
               val_tol=60.0, sat_tol=24.0, narrow=0.160):
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
    # WIDE ENOUGH TO CONTAIN THE WHOLE GARMENT, SLEEVES INCLUDED, and let COLOUR stop it at
    # the skin. Clipped to the ribcage's own width (0.115) the rigid region ended in the
    # middle of the fabric and the step to the blended field showed as a ragged vertical
    # seam. The boundary has to fall where the garment itself ends - the sleeve hem, where
    # cloth stops and skin begins - because the eye expects an edge there and does not
    # expect one down the middle of a continuous surface.
    x0 = int(max(0, cx - 0.30 * fig))
    x1 = int(min(op.shape[1], cx + 0.30 * fig))
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
    # SATURATION, NOT COLOUR DISTANCE. A distance test against the LIT chest excluded the
    # SHADED parts of the same garment - both side panels and both sleeves - and the mask's
    # ragged edge down the shirt's sides is exactly the tear that appeared in the warp. The
    # dump showed it in one look, as it did for the shoe.
    #
    # Shading changes a colour's VALUE greatly and its SATURATION barely, so saturation is
    # the property that actually separates these two materials: a white shirt is achromatic
    # in light or in shadow, and skin is orange in both. Same instrument as the shoe, aimed
    # at the right axis.
    # SATURATION ALONE IS NOT ENOUGH: LIT SKIN IS ALSO UNSATURATED.
    # Measured on this character, with the mask checked against the ARM by name rather than
    # only against the shirt:
    #
    #     chest (shirt)            sat   7   val 232
    #     right forearm (shaded)   sat  78   val 113    correctly excluded
    #     LEFT forearm (LIT)       sat  15   val 201    78% CAPTURED
    #
    # A brightly lit forearm washes out to saturation 15 against a threshold of 26, so the
    # arm was welded to the rigid ribcage and carried with it - a shirt and an arm moving as
    # one solid piece, which is what a player described. The previous check passed because it
    # only asked whether the mask covered the shirt. A mask that covers the garment
    # beautifully AND takes the arm passes a shirt-shaped test; the check needs both halves.
    #
    # The shirt is BOTH very unsaturated AND very bright. Lit skin is neither - it is less
    # bright and more saturated - so both tests together separate them where either alone
    # does not. Both thresholds come from the chest's own reference, so a dark or coloured
    # shirt on another character sets its own.
    #
    # AND BOTH TOLERANCES WERE SET AGAINST A SINGLE REFERENCE PIXEL BLOCK AND WERE FAR TOO
    # TIGHT. `ref_sat` on this figure is 6, so `ref_sat + 5` is a gate at saturation 11 -
    # tighter than the garment's own variation. Classifying every pixel of the torso box by
    # WHICH GATE REJECTED IT, which is one picture rather than another sweep:
    #
    #     kept                           40225 px   44.3%
    #     rejected, too SATURATED        48166 px   53.0%     <- the whole of one flank
    #     rejected, too DARK              1297 px    1.4%
    #     rejected, hem/narrow                0 px    0.0%
    #
    # The saturation gate was doing all of the work and throwing away over half the garment
    # with it; the value gate, which the comment above credits, rejects 1.4%. Row profiles
    # across the waist settle where the line belongs - sampling saturation every 16 px on
    # every row from the sleeve hem to the waistband, the cloth never exceeds 25 and the arms
    # never fall below 36, on any row. `ref_sat + 24` = 30 sits in that gap, and it is a
    # measured gap rather than a chosen number.
    #
    #     tolerance        mask px   chest   garment   R forearm   L forearm
    #     sat+5  val-20      44372   89.4%     49.9%        0%          0%
    #     sat+24 val-60      69326  100.0%     97.6%        0%       12.9%
    #
    # "garment" there is the second half the old check never had: the whole t-shirt, bounded
    # on each row by the first run of four saturated pixels outboard of the centre - the
    # arm's own edge. A mask can cover the chest perfectly while a comb is bitten out of its
    # sides, and chest coverage alone reports that as a pass.
    ref = np.median(ref_px, axis=0)
    ref_sat = float(ref.max() - ref.min())
    ref_val = float(ref.max())
    # THE SKIN REFERENCE IS THE FOREHEAD, and it is the one patch of skin that is skin on
    # every character: the hands end up in pockets or behind backs (7e), the forearms may be
    # inside a sleeve, and the throat is beard on 282. The block sits above the eye line, so
    # it clears both sunglasses and beard.
    nose = np.asarray(kps[0], np.float64)
    fy0, fy1 = int(nose[1] - 0.055 * fig), int(nose[1] - 0.025 * fig)
    fx0, fx1 = int(nose[0] - 0.025 * fig), int(nose[0] + 0.025 * fig)
    skin_ref = None
    if fy1 > fy0 >= 0 and fx1 > fx0 >= 0:
        sk_px = rgb[fy0:fy1, fx0:fx1][op[fy0:fy1, fx0:fx1]]
        if len(sk_px) >= 40:
            skin_ref = np.median(sk_px, axis=0)
            # and only if the two references are actually distinguishable - a character in a
            # skin-coloured garment has no separation to exploit and the test would carve the
            # shirt at random.
            if np.sqrt(((skin_ref - ref) ** 2).sum()) < 25.0:
                skin_ref = None
    sub = rgb[y0:y1, x0:x1]
    sat = sub.max(axis=2) - sub.min(axis=2)
    val = sub.max(axis=2)
    out[y0:y1, x0:x1] = ((sat < ref_sat + sat_tol) & (val > ref_val - val_tol)
                         & op[y0:y1, x0:x1])
    # A TOLERANCE AROUND ONE REFERENCE CANNOT WORK ACROSS THIS CAST, AND `--shirt-check`
    # SAID SO ON FIVE CHARACTERS. `ref_sat + 24` was measured in the gap between cloth and
    # skin on 282, where the shirt is white and `ref_sat` is 6. On 285 and 286 the garment is
    # the SATURATED material - cloth 156 and 203 against a forehead of 36 and 69 - so the
    # same rule puts the gate at 180 and 227 and admits everything. The dump showed 285 with
    # both bare forearms and both hands welded into the plate: the exact fault this section
    # was written to remove, reappearing on a different character because the instrument was
    # tuned on one figure and shipped to nine.
    #
    # NEAREST OF TWO REFERENCES instead, both taken from the figure's own pixels: the chest
    # block above, and the FOREHEAD, which is skin on every character and is never cloth. A
    # pixel is garment if it is closer to the chest colour than to the forehead colour. That
    # is symmetric, so it cannot inverts when the garment is the saturated one, and it has no
    # tolerance to tune. The sat/val gate is kept as well and the two are intersected: the
    # nearest test alone will claim the jeans on a figure whose trousers are nearer the shirt
    # than the face, and the sat/val gate already rejects those.
    if skin_ref is not None:
        d_cloth = np.sqrt(((rgb - ref) ** 2).sum(axis=2))
        d_skin = np.sqrt(((rgb - skin_ref) ** 2).sum(axis=2))
        out &= d_cloth < d_skin
    # AND THE SLEEVE HEM, MEASURED OFF THE PHOTOGRAPH RATHER THAN CHOSEN. Walking down each
    # upper arm from shoulder to elbow and finding where the low-saturation run ends puts the
    # hem at 54% and 62% of that span - 0.076 and 0.087 of figure height below the neck. Past
    # it there is no cloth, so nothing out there can be shirt whatever its colour, and the
    # lit forearm's specular highlights (which still slipped the sat/val test, 45.8% of it)
    # are excluded by geometry that was measured rather than guessed.
    #
    # THE HALF-WIDTH IS 0.160 AND WAS 0.115, WHICH IS THE RIBCAGE'S WIDTH AND NOT THE
    # GARMENT'S. A t-shirt at the waist is wider than the ribs. Measured here, walking outward
    # from the centre on every row from the sleeve hem to the waistband to the first run of
    # four saturated pixels, the garment's own edge sits at 0.063 to 0.118 of figure height
    # from the centre, so 0.115 clips it on the widest rows.
    #
    # Worth recording that this was SUSPECTED of causing the flank crease and did not: the
    # sweep from 0.115 to 0.300 moves the mask by 30 px in 69,326 and leaves the plate a
    # median 3 px short of the garment's edge either way, because colour is what binds. It is
    # widened anyway - it costs nothing and a geometric rule sized to the wrong body part is a
    # trap waiting for the next character - but the crease was the smear below, not this.
    hem = neck[1] + 0.090 * fig
    band = np.zeros(op.shape, bool)
    band[:, int(max(0, cx - narrow * fig)):int(min(op.shape[1], cx + narrow * fig))] = True
    below = np.zeros(op.shape, bool)
    below[int(hem):] = True
    out &= ~(below & ~band)
    # AND A FOREARM CYLINDER, WHICH IS NOW OFF BY DEFAULT AND SHOULD STAY OFF. It was added
    # on the belief that colour could not finish the separation, and on one sentence that is
    # simply false for this figure: "it cannot take shirt with it because no shirt lies along
    # a forearm". Drawing the segments on the photograph shows that the generator put each
    # arm slightly OUTBOARD of the pose it was given, so the elbow-wrist segment runs down the
    # arm's inner edge, which is shirt. A cylinder on it is a cylinder on the garment.
    #
    # It was also twice a forearm wide. 0.055 of figure height is 56 px; the forearm's own
    # half-width, measured at five heights between elbow and wrist, is 17 px = 0.0166. So it
    # reached 40 px into the cloth and took a vertical swath out of both flanks - 18.5% of the
    # garment and 8% of the chest - which is exactly the ragged white fringe that appeared
    # down the shirt's left side on the two cels where the arms swing furthest.
    #
    #     setting                      mask px   chest   garment   R forearm   L forearm
    #     r 0.055, sat+5,  val-20        44372   89.4%     49.9%       0%          0%
    #     r 0.025, sat+24, val-60        63934  100.0%     84.1%       0%          0%
    #     r 0,     sat+24, val-60        69326  100.0%     97.6%       0%        12.9%
    #
    # The 12.9% is the two-pixel dilation below touching the arm's antialiased edge, not the
    # arm; at grow 0 it is 8.4% and the mask gains a pale ragged sliver along the whole flank,
    # which is worse. Once the saturation gate is at the measured gap the cylinder costs 13.5%
    # of the garment to buy nothing.
    #
    # And the check that passed it was measuring nothing. Its forearm probe is a box centred
    # on the elbow-wrist SEGMENT, which lies on the cloth/skin boundary rather than on the
    # arm, so the box is half shirt; and ANY cylinder wider than the box reads 0% whatever it
    # does to the garment, which is why 0.055 scored perfectly. The probes used now are found
    # from the SILHOUETTE - the outermost 0.030 of figure height on each side at forearm
    # heights, measuring sat 77 / val 118 and sat 71 / val 104, unambiguously skin.
    #
    # Left in, and parameterised, because it is still the right fallback for a character whose
    # garment genuinely overlaps their skin in colour. It is not that character.
    for elb, wri in ((3, 4), (6, 7)) if arm_r > 0 else ():
        e = np.asarray(kps[elb], np.float64)
        w = np.asarray(kps[wri], np.float64)
        v = w - e
        L = float(np.hypot(*v))
        if L < 1e-6:
            continue
        yy, xx = np.mgrid[0:op.shape[0], 0:op.shape[1]]
        t_ = np.clip(((xx - e[0]) * v[0] + (yy - e[1]) * v[1]) / (L * L), 0.0, 1.0)
        d2 = (xx - (e[0] + t_ * v[0])) ** 2 + (yy - (e[1] + t_ * v[1])) ** 2
        out &= d2 > (arm_r * fig) ** 2
    # A RIBCAGE HAS NO HOLES IN IT. Where the colour gates drop the garment's own folds the
    # mask comes out with islands of non-rigid pixels sitting in the middle of a rigid plate,
    # and every one of those islands is a tear across a continuous surface - the one thing
    # this whole section exists to avoid.
    #
    # Filling them changes nothing about where the mask's OUTER boundary falls, so it cannot
    # take arm: a hole is by definition already enclosed by shirt on every side. Cheap
    # insurance rather than the main fix - with the gates at the measured gap it recovers only
    # 138 px here, because the ragged notches opened onto the edge and were never holes.
    if fill:
        lab = _components(~out)
        bord = np.unique(np.concatenate((lab[0], lab[-1], lab[:, 0], lab[:, -1])))
        holes = ~out & ~np.isin(lab, bord)
        out = out | holes
    if grow:
        out = np.asarray(Image.fromarray((out * 255).astype(np.uint8))
                         .filter(ImageFilter.MaxFilter(grow))) > 127
    return out

def skin_field(src_kps, dst_kps, size, alpha=2.0, step=2, reach=0.26, fig=None,
               across=1.0, foot_w=1.0, alpha_src=None, rgb_src=None, props=(),
               hard_foot=0.160, rigid_torso=True, torso_feather=0.050, foot_flex=1.0, arm_reach=0.0, arm_free_cloth=True):
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

    # A FOREARM HAS NO CLOTH ON IT, SO ITS BONE MUST NOT REACH ANY.
    #
    # Dumped rather than reasoned: painting every destination pixel of cel 2 by the bone with
    # the largest weight shows the LEFT FOREARM owning a rounded lobe that spreads across the
    # hip and the shirt's lower flank - 45% of the band below the plate and outboard of
    # centre, against 41% for the thigh and 14% for the pelvis. That lobe is the hem that
    # swings down over the jeans. The hips were the obvious suspect and the dump says they are
    # not it.
    #
    # Weight falls as 1/(d^2+1)^alpha, so a bone keeps a say over everything nearer to it than
    # any other bone - and a hanging forearm is simply closer to the hip than the thigh or the
    # pelvis is. Â§7h already measured the same thing one storey up, the arm bones reaching
    # into the ribcage with 23.6% against 12.6%, and the rigid plate was built to answer it
    # THERE. Below the plate nothing answered it at all.
    #
    # The sleeve hem is measured at 54-62% of shoulder-to-elbow (Â§7h), so below the elbow
    # there is no cloth on the arm whatever: a forearm bone that is influencing cloth is
    # influencing something that is not attached to it. Its reach is therefore capped at a
    # forearm's own half-width plus a margin - 0.050 of figure height against the 0.0166
    # measured half-width - with a smooth falloff so there is no edge where the cap bites.
    # The upper arms are left alone; they carry the sleeve and legitimately move it.
    fore_idx = {i for i, (ia, ib) in enumerate(BONES) if (ia, ib) in ((3, 4), (6, 7))}
    arm_r2 = (arm_reach * (fig if fig else H)) ** 2

    # THE GARMENT IS NOT ATTACHED TO THE ARM, so no arm bone may move a garment pixel.
    #
    # A player, after four rounds of this: "THE SHIRT IS NOT ATTACHED TO THE ARM!". He is
    # right by construction and Â§7h had already proved it without anyone noticing: the sleeve
    # hem is at 54-62% of shoulder-to-elbow, so below the elbow there is no cloth on the arm
    # IN ANY POSE. A garment pixel weighted to a forearm or a wrist is weighted to a bone it
    # is not attached to, including in the cels that happen to look fine.
    #
    # THE EARLIER TEST OF THIS WAS INVALID AND THE REASON IS WORTH MORE THAN THE FIX. Capping
    # only the FOREARM's reach changed nothing, and that was read as "dominance is not
    # responsibility - the bones that take over move the cloth the same way". The bones that
    # took over were the UPPER ARM and the clavicle, which are also arm bones and also swing
    # with the arm. The experiment could not have produced a different answer whatever the
    # truth was - Â§0a's rule applied to one's own test instead of to a metric.
    #
    # So this is an exclusion of the WHOLE CHAIN - clavicle, upper arm, forearm - not a reach
    # cap with a falloff. Garment pixels are driven by the torso and hip bones alone.
    #
    # Identifying the garment needs the field, and the field is what is being computed, so it
    # is done in two passes: accumulate once with every bone, ask which destination pixels
    # landed on shirt, then accumulate again with the arm bones switched off there. One extra
    # field evaluation, which is cheap beside the three the occlusion layers already cost.
    # THE HAND BONES BELONG IN HERE TOO. This set is what keeps the garment off the arm â€”
    # the shirt is the one result already fixed, and a new bone the cloth could be dragged by
    # is the single most likely way to undo it. Adding (4,18) and (7,19) to BONES without
    # adding them here would have given the hem two new neighbours at exactly hip height,
    # which is where the lobe was.
    ARM_CHAIN = {i for i, (ia, ib) in enumerate(BONES)
                 if (ia, ib) in ((1, 2), (2, 3), (3, 4), (4, 18),
                                 (1, 5), (5, 6), (6, 7), (7, 19))}

    def accumulate(block):
        """`block` is a destination-space WEIGHT in 0..1 multiplying the arm chain: 0 deep
        inside the garment, 1 off it, ramped across the boundary.

        THE RAMP IS NOT OPTIONAL and the first version proved it. Switching the arm chain off
        with a hard `~cloth` removed the hem lobe on all four cels - the thing it was built
        for - and tore every arm into a doubled, ghosted limb trailing a smear past the hip,
        because a sharp boundary between two fields that disagree is a tear wherever they
        disagree. That is the same lesson as the rigid plate's own edge (Â§7h), arrived at
        from the other side."""
        ax = np.zeros_like(VX)
        ay = np.zeros_like(VY)
        aw = np.zeros_like(VX)
        for bi, (s0, s1, t0, t1, bw, capped) in enumerate(bones):
            w, mx, my = bone_field(bi, s0, s1, t0, t1, bw, capped)
            if w is None:
                continue
            if block is not None and bi in ARM_CHAIN:
                w = w * block
            ax += w * mx
            ay += w * my
            aw += w
        r_ = (reach * (fig if fig else H)) ** 2
        wid = 1.0 / (r_ + 1.0) ** alpha
        return ax + wid * VX, ay + wid * VY, aw + wid

    def bone_field(bi, s0, s1, t0, t1, bw, capped):
        vt = t1 - t0
        vs = s1 - s0
        lt = float(np.hypot(*vt))
        ls = float(np.hypot(*vs))
        if lt < 1e-6 or ls < 1e-6:
            return None, None, None

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
        if arm_reach > 0 and bi in fore_idx:
            # OFF BY DEFAULT: this did not fix what it was built for. See the note above.
            w = w / (1.0 + (d2 / arm_r2) ** 3)
        if capped:
            # How far past the far endpoint this pixel lies, along the bone.
            past = np.maximum(0.0, (dx * vt[0] + dy * vt[1]) / lt - lt)
            w = w / (1.0 + (past / (0.30 * foot)) ** 2)
        return w, mx, my

    acc_x, acc_y, acc_w = accumulate(None)
    if arm_free_cloth and rgb_src is not None and alpha_src is not None:
        figc = fig if fig else H
        cloth = shirt_mask(rgb_src, alpha_src, src_kps, figc)
        if cloth.any():
            px = acc_x / acc_w
            py = acc_y / acc_w
            on_cloth = cloth[np.clip(py.astype(int), 0, cloth.shape[0] - 1),
                             np.clip(px.astype(int), 0, cloth.shape[1] - 1)]
            # Ramped over roughly a sleeve's width, by successive dilations averaged, which is
            # a distance ramp without scipy for the same reason `largest()` is hand-rolled.
            n = max(1, int(round(0.016 * figc / step)))
            keep = (~on_cloth).astype(np.float64)
            cur = ~on_cloth
            for _ in range(n):
                cur = np.asarray(Image.fromarray((cur * 255).astype(np.uint8))
                                 .filter(ImageFilter.MaxFilter(3))) > 127
                keep += cur
            acc_x, acc_y, acc_w = accumulate(np.clip(keep / (n + 1.0), 0.0, 1.0))

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
        # AND THE SHOE IS NO LONGER CARRIED PERFECTLY FLAT. `foot_flex` drives it from the
        # shin's own projected-length ratio, so there is no new constant: a shin that has not
        # foreshortened belongs to a planted foot and its shoe is left exactly as it was,
        # while the swing foot's shoe takes the flex in proportion.
        #
        # Measured on this cycle, shin projected length as a fraction of its source length:
        #     swing  0.610  (the Muybridge reading is 0.615)      in-plane rotation  6 deg
        #     planted 0.975 - 1.021                                                14 deg
        # so the weight below is 1 on the swing foot and 0 on the planted one, and the
        # in-plane term is weighted with it. Inheriting the shin's rotation UNWEIGHTED would
        # tilt the planted shoe by 14 degrees - a foot that is flat on the ground does not
        # rotate because the leg above it leans.
        figv = fig if fig else H

        # THE PRECONDITION, STATED AND TESTED RATHER THAN ASSUMED.
        #
        # This whole block exists to carry TWO SHOES as two rigid bodies, and it can only do
        # that while the two shoes are separable. Each one is found in a window +-0.09 of
        # figure height about its ankle, so once the ankles are closer together than a shoe
        # is wide, the two windows describe overlapping regions of the same pixels and every
        # downstream step - ownership, the "own translation onto own shoe" test, the smear
        # deletion - is reasoning about a partition that does not exist.
        #
        # That is now the NORMAL case, not an edge case: Â§0b made mid-swing the generation
        # rule, and a mid-swing stance is precisely one with the feet close together. The
        # source here measures 62 px between the ankles against a 184 px pair of windows.
        #
        # AND THE ANSWER IS NOT TO TURN THE BLOCK OFF, WHICH WAS TRIED AND SHIPPED AND WRONG.
        #
        # Disabling it hands the feet to the general skinning field, and that field deforms
        # what it moves - which is exactly the defect this block exists to prevent. The
        # shoes stopped tearing and started STRETCHING, and the player saw it within minutes
        # of the build going in: "why do his shoes stretch and warp as he walks".
        #
        # A rigid thing needs a rigid transform (Â§7ab). Falling back to the smooth field is
        # not a neutral default; it is the original bug. The overlap has to be SOLVED, and
        # the two pieces that solve it are nearest-ankle ownership in two dimensions (so the
        # partition exists and never cuts along a column) and keeping the smear rule away
        # from the feet themselves.
        sep = abs(float(src_kps[10][0]) - float(src_kps[13][0]))

    if hard_foot and rgb_src is not None:
        shoes = shoe_mask(rgb_src, alpha_src, [src_kps[10], src_kps[13]], figv)

        # WHAT THE GENERAL FIELD WOULD DRAW AS SHOE, kept so the rigid path can be checked
        # against it and abandoned if it cannot cover it - see the coverage test below.
        fx_gen, fy_gen = fx.copy(), fy.copy()
        anysh0 = shoes[0] | shoes[1]
        want = anysh0[np.clip(fy_gen.astype(int), 0, anysh0.shape[0] - 1),
                      np.clip(fx_gen.astype(int), 0, anysh0.shape[1] - 1)]

        m = np.zeros(VX.shape, bool)
        # THE CLAIM IS A REGION BY CONSTRUCTION. There is no membership test and nothing to
        # delete afterwards.
        #
        # It used to ask, of every destination pixel, "does this foot's own translation land
        # on this foot's own shoe in the SOURCE?" - and then the smear rule deleted whatever
        # said no. That test can only claim the part of the destination foot that has a
        # counterpart inside the source shoe MASK, and the two differ in extent, so it
        # claimed 49% of the foot on one cel and 70% on another and the remainder was
        # deleted. A partial claim is not a tuning error; it is what a membership test does
        # when the regions differ. The shoes came apart, on all three characters, twice.
        #
        # So the question is asked the other way round. Each foot owns a REGION of the
        # DESTINATION - the pixels nearest its own ankle, between the top of a shoe and the
        # ground - and that whole region is drawn by its foot's rigid transform. The region
        # is defined geometrically, so it cannot be partial, and because nothing is ever
        # unclaimed there is no deletion step to get wrong.
        #
        # It also stops asking the picture what a pixel IS. The mask-based test inherited
        # every colour failure underneath it: on view 280 a black shoe and black trousers do
        # not separate, so the mask ran 75 px up the leg and the "rigid shoe" was a rigid
        # shoe-and-trouser that tore away from the leg above it. Geometry does not ask.
        #
        # The handover at the top is FEATHERED, for the same reason the torso plate's is: a
        # region boundary is a straight line across a leg, and a hard switch between two
        # fields draws it as a seam. The shoe masks are still computed, but only to place
        # the region's top edge and to measure rigidity - not to decide ownership.
        top_f = np.full(VX.shape, 1e9)
        own_id = np.zeros(VX.shape, np.int32)
        for i, ankj in enumerate((10, 13)):
            t = np.asarray(dst_kps[ankj], np.float64)
            d2 = (VX - t[0]) ** 2 + (VY - t[1]) ** 2
            closer = d2 < top_f
            top_f = np.where(closer, d2, top_f)
            own_id = np.where(closer, i + 1, own_id)

        feather = max(1.0, 0.025 * figv)
        for idx, (ank, kne, sh) in enumerate(zip((10, 13), (9, 12), shoes)):
            t0f = np.asarray(dst_kps[ank], np.float64)
            s0f = np.asarray(src_kps[ank], np.float64)
            dx, dy = VX - t0f[0], VY - t0f[1]
            if foot_flex:
                sv = np.asarray(src_kps[ank], np.float64) - np.asarray(src_kps[kne],
                                                                       np.float64)
                dv = np.asarray(dst_kps[ank], np.float64) - np.asarray(dst_kps[kne],
                                                                       np.float64)
                sl, dl = float(np.hypot(*sv)), float(np.hypot(*dv))
                ratio = dl / sl if sl > 1e-6 else 1.0
                w = float(np.clip((1.0 - ratio) / 0.39, 0.0, 1.0))
                ky = 1.0 + foot_flex * w * (1.0 - ratio)
                th = foot_flex * w * (np.arctan2(dv[0], dv[1]) - np.arctan2(sv[0], sv[1]))
                # NO SCALE TERM. A RIGID TRANSFORM HAS NONE, AND THIS ONE DID.
                #
                # `ky` divided the shoe's own pixels, squashing it to 1/ky of its height, and
                # on this cycle ky reaches 1.383 - the swing shoe came out at 0.72 of its
                # height on cels 0 and 2 and 1.00 on cels 1 and 3. So the shoe changed SHAPE
                # from key to key by 28%, the in-betweens faithfully interpolated between a
                # tall shoe and a short one, and what the player saw was the feet warping as
                # he walked. The rotation term over the same cels is 2 degrees.
                #
                # It was meant to model foreshortening, and foreshortening of a real shoe is
                # a ROTATION about a horizontal axis - the projection shortens because the
                # object turns, and the object keeps its shape while it does. Compressing the
                # pixels instead gets the silhouette roughly right for one frame and destroys
                # rigidity across the cycle, which is the thing that actually reads in motion.
                #
                # `ky` is kept and reported because it is still the measure of how much
                # foreshortening the shin is asking for, and a later rotation-based model
                # will need it. It no longer scales anything.
                ca, sa = np.cos(-th), np.sin(-th)
                rx, ry = ca * dx - sa * dy, sa * dx + ca * dy
                gx = s0f[0] + rx
                gy = s0f[1] + ry
            else:
                gx = s0f[0] + dx
                gy = s0f[1] + dy
            # The region: nearest this ankle, and no higher than the top of a shoe. The top
            # is taken from where this foot's own shoe mask begins relative to its ankle,
            # so a boot and a trainer each get their own height rather than a shared guess,
            # and it falls back to SHOE_TOP when the mask found nothing.
            # THE REGION IS THE SHOE'S OWN MEASURED EXTENT, PLACED AT THE DESTINATION ANKLE.
            #
            # An unbounded nearest-ankle region claims background as well as foot, and the
            # rigid transform then paints shoe colour into it - which came out as a thin dark
            # spike trailing off the rear heel on half the cels. Bounding it by the shoe's own
            # bounding box, measured in the source and offset to the destination ankle, keeps
            # the region complete over the shoe (so there is nothing to delete) while giving
            # it nowhere to invent one. The box travels with the foot because the transform
            # is rigid; that is the whole point of it being rigid.
            ys_sh = np.nonzero(sh.any(axis=1))[0]
            xs_sh = np.nonzero(sh.any(axis=0))[0]
            if len(ys_sh) and len(xs_sh):
                sa = np.asarray(src_kps[ank], np.float64)
                ry0 = min(float(ys_sh[0]) - sa[1], -SHOE_TOP * figv)
                ry1 = float(ys_sh[-1]) - sa[1]
                rx0 = float(xs_sh[0]) - sa[0]
                rx1 = float(xs_sh[-1]) - sa[0]
            else:
                ry0, ry1 = -SHOE_TOP * figv, 0.10 * figv
                rx0, rx1 = -0.09 * figv, 0.09 * figv

            ex, ey_ = VX - t0f[0], VY - t0f[1]
            w_reg = np.minimum.reduce([
                np.clip((ey_ - ry0) / feather, 0.0, 1.0),
                np.clip((ry1 - ey_) / feather, 0.0, 1.0),
                np.clip((ex - rx0) / feather, 0.0, 1.0),
                np.clip((rx1 - ex) / feather, 0.0, 1.0),
            ])
            w_reg = np.where(own_id == idx + 1, w_reg, 0.0)

            # AND INSIDE THE BOX, ONLY WHAT IS ACTUALLY THIS SHOE.
            #
            # The box is a rectangle and a shoe is not. Where the two differ the box covers
            # trouser, backdrop and the OTHER foot's toe, and the rigid transform dutifully
            # painted this shoe's pixels into them - which is the pale curl that appeared
            # between the shoes on cels 1 and 3 and survived into the installed build.
            #
            # Testing the sample against this foot's own shoe mask is not the membership
            # test that failed before. That one used the test as the WHOLE claim and then
            # deleted everything outside it, measured against what the stretching general
            # field wanted to draw - so it deleted real shoe. Here the box already bounds the
            # claim and nothing is deleted at all; the mask only stops the box claiming what
            # is visibly not a shoe. Bound by geometry, filtered by the object's own shape.
            # NOT FILTERED BY THE SHOE MASK. Tried, and it re-broke the thing the region was
            # built to fix: filtering the claim by "does the sample land on the shoe" makes
            # the claim incomplete again - the mask has ragged edges and holes - and the
            # deletion below then eats the real shoe, which came out as magenta holes right
            # through view 282's trainers. The box IS the claim; what it over-claims is
            # trouser and backdrop, which the rigid transform draws as trouser and backdrop.
            take = w_reg > 0.0
            fx = np.where(take, w_reg * gx + (1.0 - w_reg) * fx, fx)
            fy = np.where(take, w_reg * gy + (1.0 - w_reg) * fy, fy)
            # The WHOLE region, feathered edge included. Using the core (> 0.5) left the
            # feather band counting as unclaimed, so the smear rule below deleted it and the
            # shoes grew fragments again - the same "incomplete claim feeding a deletion"
            # failure, one layer finer.
            m |= w_reg > 0.0

        # AND THE SMEAR RULE COMES BACK, BECAUSE IT WAS NEVER THE PROBLEM.
        #
        # It deletes destination pixels that sample a shoe but belong to no foot - the ghost
        # the general field drags off a heel when it stretches shoe colour across the gap
        # between the legs. Removing the rule along with the membership test duly brought the
        # ghost back: a thin dark spike trailing below the rear foot, on cels 1 and 3 of all
        # three views.
        #
        # What was wrong was never this rule. It was that "unclaimed" used to mean "the
        # membership test did not reach it", which included half of a real shoe - so the rule
        # deleted the shoe along with the ghost. Now the claim is a REGION and is complete by
        # construction, "unclaimed" means what it says, and subtracting it is safe. A correct
        # rule resting on a broken premise looks exactly like a broken rule, and the fix was
        # upstream of it both times.
        # AND IT IS BACK ON, BECAUSE TURNING IT OFF PUT A SECOND SHOE ON EVERY CEL.
        #
        # I removed it when the region became complete, reasoning that a bounded region can
        # no longer paint a shoe into background so there was nothing left to delete. That
        # was half the story and the wrong half. The region stops the RIGID transform drawing
        # a shoe where there is none; it does nothing about the GENERAL field, which goes on
        # mapping the same source shoe to wherever the smooth deformation sends it. So each
        # foot was drawn twice - once rigidly by its region and once smeared by the field -
        # and because the two overlap, are the same colour and are both attached to the
        # figure, every instrument we had read it as clean. The player found it by looking:
        # "there are 2 feet on his right leg". Shoe-coloured area in the foot band came to
        # roughly double the source's on the swing cels.
        #
        # The thing the rule removes is not a "smear artefact" at the edges. It is the
        # general field's entire copy of a shoe that the rigid layer has already drawn
        # properly. That is why it has to delete everything outside the claim, and why the
        # claim had to be made complete FIRST - which it now is.
        anysh = shoes[0] | shoes[1]

        # THE DELETION USES ONLY THE PART OF THE MASK THAT IS GENUINELY SHOE.
        #
        # On a figure whose trousers match the shoe, the mask runs up the leg - 10% of it on
        # view 280 even with SHOE_TOP tightened - and the rule then deletes the general
        # field's copy of the TROUSER too, which is a hole in the leg rather than a removed
        # duplicate. Measured: 280's shoe-coloured area fell to 7352 against a source of
        # 12188, i.e. 40% UNDER, where 281 and 282 landed within a few per cent of theirs.
        # A shoe is below the ankle, so that is where the rule is allowed to look.
        ank_top = min(float(src_kps[10][1]), float(src_kps[13][1])) - 0.02 * figv
        shoe_only = anysh.copy()
        shoe_only[:max(0, int(ank_top))] = False

        smear = shoe_only[np.clip(fy.astype(int), 0, anysh.shape[0] - 1),
                          np.clip(fx.astype(int), 0, anysh.shape[1] - 1)] & ~m
        fx = np.where(smear, -1e4, fx)
        fy = np.where(smear, -1e4, fy)

        # Rigidity is measured rather than assumed. A rigid transform preserves area, so the
        # source shoe's own pixel count is the yardstick a later stage checks the warped shoe
        # against: the correct value is 1.00 and any drift is deformation by definition.
        # The rigid region itself, so the caller can sample the ORIGINAL source inside it and
        # the de-shoed source everywhere else. That split is what lets the general field be
        # given a shoeless picture without the rigid layer losing its shoe too.
        # Resized to the OUTPUT grid, because skin_field works on a decimated one (`step`)
        # and the caller samples at full resolution. Nearest, because it is a mask.
        globals()["LAST_FOOT_REGION"] = np.asarray(
            Image.fromarray((m * 255).astype(np.uint8)).resize(size, Image.NEAREST)) > 127
        globals()["LAST_SRC_SHOE_PX"] = int(anysh.sum())
        # The DRAWN shoe: pixels inside the rigid region whose sample lands on the source
        # shoe. That is the warped shoe's own area, found through the region rather than by
        # asking the warped picture what colour things are - which is what made the two
        # previous attempts at this useless. Under a rigid transform it equals the source
        # shoe's area, so the ratio has a known correct value of 1.00.
        globals()["LAST_REGION_PX"] = int(
            (anysh[np.clip(fy.astype(int), 0, anysh.shape[0] - 1),
                   np.clip(fx.astype(int), 0, anysh.shape[1] - 1)] & m).sum())
        cover = m.sum() / max(1, want.sum())
        if cover < 0.90:
            print(f"      foot: region claim {cover*100:.0f}% of what the field draws as "
                  f"shoe", flush=True)
    # ===================================================================================
    # THE RIBCAGE IS ONE RIGID BODY
    # ===================================================================================
    # A player: "the whole right side shirt skews when he walks, thats not how clothing
    # works". They were right, and the weight map said why. The ribcage was spanned by TWO
    # bones - neck to RIGHT hip and neck to LEFT hip - and those two hips move independently
    # in every cel. Blending two transforms that disagree IS shear; no parameter avoids it.
    # Measured field shear over the torso band: mean 0.096, p95 0.441, where a rigid body
    # reads 0.
    #
    # The asymmetry they reported was in the same dump. Dominant bone over the ribcage:
    #     neck-Rhip 27.3%   neck-Lhip 21.2%   RIGHT arm 23.6%   left arm 12.6%
    # The right arm's bones reach into the shirt with nearly twice the left's influence, so
    # one side skews and the other does not.
    #
    # THE TRANSFORM IS DERIVED FROM SOMETHING THAT CANNOT DISAGREE WITH ITSELF: the neck and
    # the hip MIDPOINT. Rotation and translation only - the scale is forced to 1, so the
    # shirt cannot stretch or parallelogram. Below the waist the hips diverge as they must,
    # and the blend zone sits at the waist, where a real body actually articulates.
    #
    # WHICH PIXELS ARE THE SHIRT IS DECIDED BY COLOUR, not by which bone is nearest, for the
    # same reason the shoe was: geometry cannot tell a shirt from an arm. Third time colour
    # has settled a separation that geometry could not.
    if rigid_torso and rgb_src is not None and alpha_src is not None:
        figv = fig if fig else H
        shirt = shirt_mask(rgb_src, alpha_src, src_kps, figv)
        if shirt.any():
            s_neck = np.asarray(src_kps[1], np.float64)
            d_neck = np.asarray(dst_kps[1], np.float64)
            s_hip = (np.asarray(src_kps[8], np.float64)
                     + np.asarray(src_kps[11], np.float64)) / 2.0
            d_hip = (np.asarray(dst_kps[8], np.float64)
                     + np.asarray(dst_kps[11], np.float64)) / 2.0
            vs, vd = s_hip - s_neck, d_hip - d_neck
            if np.hypot(*vs) > 1e-6 and np.hypot(*vd) > 1e-6:
                ang = np.arctan2(vs[1], vs[0]) - np.arctan2(vd[1], vd[0])
                ca, sa = np.cos(ang), np.sin(ang)   # NO scale term: rigid by construction
                dxt, dyt = VX - d_neck[0], VY - d_neck[1]
                gx = s_neck[0] + ca * dxt - sa * dyt
                gy = s_neck[1] + sa * dxt + ca * dyt
                own = shirt[np.clip(gy.astype(int), 0, shirt.shape[0] - 1),
                            np.clip(gx.astype(int), 0, shirt.shape[1] - 1)]
                # AND HAND OVER TO THE SKINNED FIELD GRADUALLY, OUTSIDE THE GARMENT.
                #
                # Switching hard between two fields that disagree tears wherever they
                # disagree. Measured at the plate's edge, as the distance between where the
                # rigid map and the skinned field each send the same pixel:
                #
                #     cel 0   median  5.3   p90  8.6   p99 11.9 px
                #     cel 1   median 12.6   p90 47.2   p99 60.5
                #     cel 2   median 17.0   p90 58.3   p99 80.4
                #     cel 3   median  9.9   p90 31.3   p99 39.7
                #
                # which is the player's report as a number: cels 1 and 2 are the two they
                # called ragged, and the ranking is exact. The shoe gets away with a hard
                # boundary (7ab) because its transform is a pure TRANSLATION and the step at
                # the ankle is a real edge. The ribcage's transform is a rotation about the
                # neck, so its disagreement with the arm bones grows with distance and is
                # largest at the flank, which is the one place it abuts a swinging limb.
                #
                # THE RAMP GOES INWARD, AND THE OUTWARD VERSION WAS TRIED AND IS WORSE.
                # Ramping outward keeps every pixel of the garment perfectly rigid and spends
                # the blend on the arm beside it, which sounds like the right trade and is
                # not: the band outside the plate is the ARM, so part of the arm's dark pixels
                # get the torso's transform and are dragged into the cloth. It produced a hard
                # dark staircase down the flank, plainly worse than the light crease it was
                # meant to cure. Dumped at 3x and discarded.
                #
                # Ramping inward spends the blend on cloth, so the cost is a slight loss of
                # rigidity in the outer band. 0.010 of figure height is too narrow to absorb a
                # 58 px disagreement and creases; 0.050 is wide enough that the handover reads
                # as drapery.
                #
                # Successive erosions averaged together are a distance ramp without scipy, for
                # the same reason `largest()` is hand-rolled.
                #
                # Rejected, with the picture to show for it: the shoe's SMEAR rule, sending
                # anything outside the plate that reads shirt pixels off-frame. For a shoe the
                # vacated ground really is background; for a flank it is not - the arm hangs
                # against the cloth - so it bit a jagged chunk out of the shirt's side and
                # showed green between the shirt and the arm. The right instrument for one
                # part is not the right instrument for its neighbour.
                if torso_feather > 0:
                    n = max(1, int(round(torso_feather * figv / step)))
                    w = own.astype(np.float64)
                    cur = own
                    for _ in range(n):
                        cur = np.asarray(
                            Image.fromarray((cur * 255).astype(np.uint8))
                            .filter(ImageFilter.MinFilter(3))) > 127
                        w += cur
                    w /= (n + 1.0)
                else:
                    w = own.astype(np.float64)
                fx = fx * (1.0 - w) + gx * w
                fy = fy * (1.0 - w) + gy * w
                # THE SHOE'S SMEAR RULE WAS TRIED HERE TWICE AND IS WRONG BOTH TIMES. The
                # second attempt fixed the predicate the first one got wrong - `w < 0.5` is
                # the outer half of the FEATHER BAND, which is cloth inside the garment, so
                # it carved the shirt; `~own` is strictly outside the plate and is the honest
                # test. It still carved the shirt, on all four cels including the clean one,
                # and took the plate's area to 0.94 of the source where 1.00 is rigid.
                #
                # THE SHOE'S SMEAR RULE IS CLOSED FOR THE SHIRT. THREE ATTEMPTS, THREE CARVED
                # GARMENTS, and the third had no excuse left:
                #
                #   `w < 0.5`          tested the outer half of the FEATHER BAND, which is
                #                      cloth inside the garment. Carved.
                #   `~own`             strictly outside the plate. Still carved - the plate's
                #                      edge stops inside the antialiased silhouette.
                #   `~dilate(own, 9)`  only cloth well clear of the plate. STILL carved, a
                #                      ragged green gash down the flank on three cels.
                #
                # The rule works for a shoe because the region it protects is small, compact
                # and surrounded by background. A garment's edge is long, soft and adjacent to
                # an arm, so any test of the form "outside the plate and shirt-coloured" keeps
                # finding the garment's own antialiased rim. Most of the lobe was the ARM bone
                # stretching anyway (`clamp_limbs`); what is left is accepted.
                pass

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
         method="skin", fig=None, across=1.0, foot_w=1.0, props=(), rigid_torso=True,
         hard_foot=0.160, torso_feather=0.050, foot_flex=1.0, occlude=True,
         arm_reach=0.0, arm_free_cloth=True):
    """One deformed frame. Alpha is warped with the colour, so the silhouette moves too."""
    if method == "skin":
        dst_kps = clamp_limbs(src_kps, dst_kps)
    if method == "skin" and occlude:
        return warp_occluded(src_rgba, src_kps, dst_kps, size, fig,
                             alpha=alpha, step=step, across=across, foot_w=foot_w,
                             props=props, hard_foot=hard_foot, rigid_torso=rigid_torso,
                             torso_feather=torso_feather, foot_flex=foot_flex, arm_reach=arm_reach,
                            arm_free_cloth=arm_free_cloth)
    if method == "skin":
        mx, my = skin_field(src_kps, dst_kps, size, alpha=alpha, step=step, fig=fig,
                            across=across, foot_w=foot_w,
                            alpha_src=src_rgba[:, :, 3], props=props,
                            rgb_src=src_rgba[:, :, :3], hard_foot=hard_foot,
                            rigid_torso=rigid_torso, torso_feather=torso_feather,
                            foot_flex=foot_flex, arm_reach=arm_reach,
                            arm_free_cloth=arm_free_cloth)
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


def clamp_limbs(src_kps, dst_kps):
    """No projected limb can be LONGER than the limb itself.

    A player, twice: the shirt looks like part of his arm, and the arm is a long thin strip
    with no hand on the end. Both are one authoring fault, found by measuring every bone's
    destination length against its source length:

        bone        cel 0   cel 1   cel 2   cel 3
        R forearm   1.01x   1.15x   1.32x   1.06x
        L forearm   1.02x   1.18x   1.36x   1.08x
        upper arms  1.01x   1.06x   1.04x   1.05x
        legs        0.61x   1.01x   1.02x   0.99x

    The forearms are stretched by up to 36%, and the four cels rank 0 < 3 < 1 < 2 - exactly
    the order a human ranked them by eye, cel 0 clean and cel 2 worst.

    `across` is 1.0 so a limb keeps its WIDTH while its length is free, which is what makes a
    foreshortened shin read as a bent knee in a front view (Â§7aa). Applied to a bone that gets
    LONGER it does the opposite and draws the limb out into a ribbon at constant width, with
    the hand at the end smeared off it and the cloth beside it dragged into a bell over the
    hip. The shirt was never the problem; it was downstream of the arm.

    AND A RATIO ABOVE 1.0 IS NOT A TUNING QUESTION, IT IS IMPOSSIBLE. The source photograph
    has the arms hanging straight down, perpendicular to the view, so each bone is already at
    its true length there - and a projection of a rigid segment can only ever be SHORTER. Any
    destination bone longer than its source one is an authored pose that no camera could see.

    The cycle produces it because the wrist is placed by an x and a y amplitude independently
    rather than on an arc of fixed radius about the elbow. The honest fix is in
    `pose_author.CYCLE`; this clamp is in the renderer because the renderer should not draw an
    impossible limb whatever it is handed, and because it can be verified against the cels
    that exist. Direction is kept, length is capped, and the chain is walked outward so a
    clamped elbow carries its wrist with it.
    """
    out = np.array(dst_kps, np.float64, copy=True)
    # THE HANDS ARE ON THE END OF THE ARM CHAINS. This table is the skeleton written out a
    # second time, and when the hand bone was added to BONES it did not update itself: the
    # fingertip sat in no chain, so a clamped wrist moved and left the hand behind, and the
    # hand bone's two ends stopped agreeing. The hand was then dragged rather than
    # foreshortened, the predicted projected range did not arrive, and the measured effective
    # arm length stayed at 0.311 - the exact number that said the hand was not in the chain in
    # the first place. Â§0a: the code that DEFENDS a design decision is the part that does not
    # notice when the decision changes.
    for chain in ((2, 3, 4, 18), (5, 6, 7, 19), (8, 9, 10), (11, 12, 13)):
        for a_, b_ in zip(chain[:-1], chain[1:]):
            sv = np.asarray(src_kps[b_], np.float64) - np.asarray(src_kps[a_], np.float64)
            sl = float(np.hypot(*sv))
            dv = out[b_] - out[a_]
            dl = float(np.hypot(*dv))
            if dl > sl > 1e-6:
                shift = dv * (sl / dl - 1.0)
                for j in chain[chain.index(b_):]:
                    out[j] = out[j] + shift
    return out


def leg_masks(alpha, kps, fig):
    """The two legs' own source pixels, below the hips, split by nearest leg chain.

    Colour cannot do this one - both trouser legs are the same cloth, which is the opposite
    of the shoe and the shirt - but geometry can, because unlike a shirt and an arm the two
    legs ARE separated by their own bones. Each pixel goes to whichever hip-knee-ankle chain
    it is nearer. The split through the crotch is arbitrary and does not matter: both sides
    of it belong to the figure and the layers are composited back together."""
    op = alpha > 100
    H, W = op.shape
    yy, xx = np.mgrid[0:H, 0:W]
    hipy = (float(kps[8][1]) + float(kps[11][1])) / 2.0
    d = []
    for chain in ((8, 9, 10), (11, 12, 13)):
        best = None
        for a_, b_ in zip(chain[:-1], chain[1:]):
            A = np.asarray(kps[a_], np.float64)
            B = np.asarray(kps[b_], np.float64)
            v = B - A
            L2 = max(float(v[0] * v[0] + v[1] * v[1]), 1e-6)
            t = np.clip(((xx - A[0]) * v[0] + (yy - A[1]) * v[1]) / L2, 0.0, 1.0)
            dd = (xx - (A[0] + t * v[0])) ** 2 + (yy - (A[1] + t * v[1])) ** 2
            best = dd if best is None else np.minimum(best, dd)
        d.append(best)
    below = yy >= hipy
    masks = [op & below & (d[0] <= d[1]), op & below & (d[1] < d[0])]
    # AND THE TWO MASKS OVERLAP SLIGHTLY, because a hard split is a seam. Each layer is
    # warped by its own field, so along the crotch - where the split runs and the two fields
    # diverge most - a gap opens and the background shows through the jeans as a thin green
    # streak up the fly. Found by cropping the torso at 4x; no silhouette measure sees it,
    # because the hole is interior and narrower than the inter-leg gap that surrounds it.
    #
    # Dilating each mask by a few pixels makes the layers share a strip instead of abutting.
    # The near leg is drawn last, so the shared strip simply comes from the near leg, which is
    # what it should show anyway.
    g = max(3, int(round(0.006 * fig)) | 1)
    return tuple(np.asarray(Image.fromarray((m * 255).astype(np.uint8))
                            .filter(ImageFilter.MaxFilter(g))) > 127 for m in masks)


def _over(dst, src):
    """src composited OVER dst, both premultiplied RGBA in 0-255 floats."""
    a = src[:, :, 3:4] / 255.0
    return src + dst * (1.0 - a)


def shoe_area_ratio(src_rgba, out_rgba, src_kps, fig, band=0.17, tol=52.0):
    """Shoe-coloured area drawn, over shoe-coloured area in the source.

    Same colour test both sides, so the ratio is dimensionless and its correct value is 1.00:
    the destination has the same two shoes the source does. A duplicate shoe shows as ~1.5; a
    deleted one as well under 1. Neither moves a fragment count, a near-black percentage or a
    runs-per-row, which is why a duplicate shoe shipped with every gate green."""
    def band_area(im, ref=None):
        al = im[:, :, 3] > 128
        ys = np.nonzero(al.any(axis=1))[0]
        if not len(ys):
            return 0, None
        y0, y1 = int(ys[0]), int(ys[-1])
        H = y1 - y0 + 1
        px = im[:, :, :3].astype(np.float64)
        if ref is None:
            sel = al.copy()
            sel[:int(y1 - 0.06 * H)] = False
            if not sel.any():
                return 0, None
            ref = np.median(px[sel], axis=0)
        sel = al.copy()
        sel[:int(y1 - band * H)] = False
        d = np.sqrt(((px - ref) ** 2).sum(axis=2))
        return int((sel & (d < tol)).sum()), ref

    a_src, ref = band_area(src_rgba)
    if not a_src or ref is None:
        return 1.0
    a_out, _ = band_area(out_rgba, ref)
    return a_out / float(a_src)

def deshoe(src_rgba, shoes, fig, src_kps_y=None, grow=3):
    """The source with the shoes REMOVED and the trouser continued down over them.

    The general skinning field cannot draw a second shoe out of pixels it was never given.
    Every earlier attempt at the duplicate was downstream - a membership test, a smear rule,
    a shape filter, a bounding box, an exemption - and all five let the field draw a shoe and
    then argued about which copy to keep, using a colour mask that fails differently for a
    black brogue, a brown loafer and a white trainer. This removes the question instead.

    It is the body layer's own trick, one level down: the body does not draw the legs and
    then erase them, its source has no legs in it. Here each layer's source keeps its leg and
    loses its shoe, and the rigid foot layer supplies the only shoe in the frame.

    The hole is filled by continuing the trouser downward - for every column the mask covers,
    the colour a few pixels above the topmost shoe pixel is extended to the bottom. The field
    then draws leg where the shoe was, and the rigid layer covers that area with the real
    shoe. Alpha is untouched, so the silhouette does not change.

    BIASED TO OVER-ERASE, deliberately, because the two failure modes are not symmetric.
    Erasing a little trouser costs nothing - the rigid layer draws over that area anyway -
    while leaving a sliver of shoe behind puts a second shoe back in the picture, which is
    the whole defect. Hence `grow`, which dilates the mask before the fill.
    """
    # THE ERASE IS GEOMETRIC, NOT COLOUR-DERIVED, and that is the whole point of it.
    #
    # Using `shoes` - the colour mask - left white patches of trainer behind on every cel:
    # laces, tongue and toe highlights that the mask never covered, which the field then
    # drew as the second shoe we were trying to remove. That mask has now failed on a black
    # brogue, a brown loafer and a white trainer, in three different ways.
    #
    # A shoe is BELOW THE ANKLE. That is true of every character without asking the picture
    # anything, and erasing a little shin above it costs nothing because the rigid foot layer
    # draws over that area anyway. This is the over-erase bias stated as geometry.
    out = src_rgba.copy()
    op0 = out[:, :, 3] > 100
    yy = np.mgrid[0:out.shape[0], 0:out.shape[1]][0]
    ank = min(float(src_kps_y[0]), float(src_kps_y[1])) if src_kps_y else None
    if ank is None:
        m = shoes[0] | shoes[1]
    else:
        # 0.022, which must stay BELOW the region's own reach of SHOE_TOP = 0.030.
        #
        # At 0.035 the erase went higher up the leg than the rigid region comes, so a thin
        # band was removed from the source and covered by nothing - a magenta line straight
        # across the ankle on most cels. Now that the erase makes pixels transparent rather
        # than recolouring them, anything erased above the region's top edge is simply a hole.
        # The two numbers are a pair and have to be read together.
        m = op0 & (yy >= ank - 0.022 * (fig if fig else out.shape[0]))

        # Bounding the erase sideways to the region's own width was tried and is NOT here,
        # because it changed nothing: identical ratios on all twelve cels and the same three
        # tween frames failing. The remaining fragments are not an erase-versus-region
        # mismatch - the keys are clean and only smooth_walk's in-betweens carry them, so
        # they come from the flow interpolating between two correct key positions.
    if not m.any():
        return src_rgba

    rgb = out[:, :, :3]
    op = out[:, :, 3] > 100
    lift = max(2, int(round(0.010 * (fig if fig else src_rgba.shape[0]))))

    # FILLED FROM A TROUSER REFERENCE, NOT FROM THE PIXEL ABOVE.
    #
    # Copying each column down from whatever sat above it looked obvious and was wrong: the
    # shoe mask is colour-derived and misses pixels, so where it missed a bright patch of
    # trainer the fill copied THAT down the column and painted a white streak the length of
    # the shoe. The de-shoed source came out with the shoe half-erased and half-smeared, and
    # the field drew exactly that.
    #
    # A median over the trouser band above the feet cannot do it: one missed pixel cannot
    # move a median, and the colour is the right one by construction.
    ys_m = np.nonzero(m.any(axis=1))[0]
    top = int(ys_m[0])
    band = np.zeros_like(m)
    band[max(0, top - 6 * lift):max(1, top - lift)] = True
    band &= op & ~m
    if not band.any():
        return src_rgba
    rgb[m] = np.median(rgb[band], axis=0)

    # AND THE ERASED AREA IS MADE TRANSPARENT, NOT JUST RECOLOURED.
    #
    # Filling it with trouser removed the second SHOE and replaced it with a second shoe-
    # SHAPED patch of denim, because the general field goes on warping that region to
    # wherever the smooth deformation sends it. Drawn over the cel, the region boxes sit
    # correctly on both real shoes and the denim blob sits OUTSIDE them both - it is the fill
    # arriving where the field would have put the shoe, which on view 282's raised foot is
    # 100 px from where the rigid layer correctly draws it. On 280 and 281 the two places
    # nearly coincide, which is why only 282 showed a stump.
    #
    # The ratio said 0.93 and the picture said stump: area right, placement wrong. A number
    # that is in range while the thing is in the wrong place is the failure mode this gate
    # cannot see, and it was caught by cropping the feet and looking - twice.
    #
    # So the field is given nothing there at all. The rigid layer is the only thing that
    # draws a foot, which is what "the field never receives shoe pixels" should have meant.
    out[:, :, 3][m] = 0
    return out


def warp_occluded(src_rgba, src_kps, dst_kps, size, fig, **kw):
    """One deformed frame with the near leg drawn OVER the far one.

    WHY THIS IS NEEDED, measured the same way the reference was. Across a real stride, 18% of
    frames show the two shins as a SINGLE ink run and in a further 40% one leg is a 4-5 px
    sliver beside the other's 8-21 px - about 70% of the far leg hidden, alternating every
    half-stride because the leading leg is the nearer one. Measured on our own shipped cels at
    three heights: **two runs of near-equal width in 16 frames out of 16, at every height.**
    Our silhouette is wrong for well over half the cycle and no pose work reaches it, because
    a single backward field maps every destination pixel exactly once and therefore cannot
    express one thing being in front of another.

    So the frame is built in three layers, each with its OWN field:

        body      every joint moving, both legs erased from the source
        far leg   the near leg's joints frozen at their source positions
        near leg  the far leg's joints frozen

    Freezing the other leg's joints is what stops a leg being dragged by the limb it is
    supposed to be independent of; without it both layers inherit the same scissor and the
    occlusion is between two copies of the same motion.

    WHICH LEG IS NEAR IS A CYCLE FACT, NOT A PIXEL FACT. It is read from the destination
    ankles' heights: Â§7ac established that with the camera above the ground plane a nearer
    foot appears LOWER, and the Â±0.010 depth offset in `pose_author.CYCLE` is precisely what
    puts it there. So the ordering comes from the authored phase, exactly as the foot flex's
    weight comes from the authored shin.
    """
    figv = fig if fig else size[1]
    legs = leg_masks(src_rgba[:, :, 3], src_kps, figv)
    near = 0 if float(dst_kps[10][1]) > float(dst_kps[13][1]) else 1

    a = src_rgba[:, :, 3:4] / 255.0
    pre = np.dstack([src_rgba[:, :, :3] * a, src_rgba[:, :, 3:4]]).astype(np.float64)

    def masked(m):
        out = pre.copy()
        out *= m[:, :, None]
        return out

    # THE FIELD IS GIVEN A SHOELESS PICTURE. Inside the rigid foot region the ORIGINAL is
    # sampled, so the shoe is drawn exactly once, by the layer that carries it rigidly.
    # Outside it the de-shoed source is sampled, so the smooth field has no shoe pixels to
    # stretch into a second one. Nothing is deleted and nothing has to be classified.
    shoes_src = shoe_mask(src_rgba[:, :, :3].astype(np.float64), src_rgba[:, :, 3],
                          [src_kps[10], src_kps[13]], figv)
    nos = deshoe(src_rgba, shoes_src, figv,
                 src_kps_y=(float(src_kps[10][1]), float(src_kps[13][1])))
    a_n = nos[:, :, 3:4] / 255.0
    pre_n = np.dstack([nos[:, :, :3] * a_n, nos[:, :, 3:4]]).astype(np.float64)

    def split(layer_full, layer_nos, fx, fy):
        out = sample(layer_nos, fx, fy)
        reg = globals().get("LAST_FOOT_REGION")
        if reg is None:
            return out
        return np.where(reg[:, :, None], sample(layer_full, fx, fy), out)

    keep = (~(legs[0] | legs[1]))[:, :, None]
    body, body_n = pre.copy() * keep, pre_n.copy() * keep
    mx, my = skin_field(src_kps, dst_kps, size, fig=fig,
                        alpha_src=src_rgba[:, :, 3], rgb_src=src_rgba[:, :, :3], **kw)
    acc = split(body, body_n, mx, my)

    for which in (1 - near, near):
        frozen = np.array(dst_kps, np.float64, copy=True)
        for j in ((8, 9, 10) if (1 - which) == 0 else (11, 12, 13)):
            frozen[j] = src_kps[j]
        lx, ly = skin_field(src_kps, frozen, size, fig=fig,
                            alpha_src=src_rgba[:, :, 3], rgb_src=src_rgba[:, :, :3], **kw)
        mk = legs[which][:, :, None]
        acc = _over(acc, split(pre * mk, pre_n * mk, lx, ly))

    al = np.clip(acc[:, :, 3:4], 0, 255)
    rgb = np.where(al > 1e-3, acc[:, :, :3] / np.maximum(al / 255.0, 1e-3), 0.0)
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


def shirt_probes(rgb, alpha, kps, fig):
    """The regions a shirt mask must COVER and the regions it must NOT, found from the
    picture rather than from the skeleton.

    This exists because every mask check in this project has been written to confirm the
    thing it was built for, and has then passed while swallowing something it was never
    asked about. The shoe's check asked "does it cover both shoes"; the shirt's asked "does
    it cover the chest" and passed at 49.9% garment coverage with a comb bitten out of both
    flanks; and the forearm probe was a box centred on the elbow-wrist SEGMENT, which lies
    on the cloth/skin boundary, so it was half cloth and could not answer either way.

    So the probes here are all found from the silhouette and from the figure's own two
    materials, and `must_be_out` is as long as `must_be_in`.

    SKIN IS IDENTIFIED BY BEING NEARER THE FOREHEAD THAN THE CHEST in RGB, not by being more
    saturated. On views 285 and 286 the garment is hot pink and MORE saturated than skin
    (cloth 156 and 203 against a forehead of 36 and 69), so a "skin is the saturated one"
    test is backwards on two of nine characters. Nearest-of-two-references is symmetric and
    cannot invert.

    THE HANDS ARE GATED ON ACTUALLY CONTAINING FLESH, for the reason 7e records: the model
    puts hands in pockets, behind backs and under jackets, so a block at the authored wrist
    frequently holds no skin at all. Measured here, the 75th-percentile saturation in that
    block is 25 on view 280 and 4.5 on view 290 - both of which are jacket. Reporting a hand
    coverage figure off those blocks would be measuring cloth and calling it a hand, so a
    block under the flesh floor is reported as 'no flesh' rather than as a pass.
    """
    op = alpha > 100
    H, W = op.shape
    neck = np.asarray(kps[1], np.float64)
    hipm = (np.asarray(kps[8], np.float64) + np.asarray(kps[11], np.float64)) / 2.0
    cx = 0.5 * (neck[0] + hipm[0])
    sat = rgb.max(axis=2) - rgb.min(axis=2)

    def block(cy, cx_, ry, rx):
        m = np.zeros((H, W), bool)
        m[int(max(0, cy - ry)):int(cy + ry), int(max(0, cx_ - rx)):int(cx_ + rx)] = True
        return m & op

    chest = block(neck[1] + 0.100 * fig, cx, 0.045 * fig, 0.045 * fig)
    nose = np.asarray(kps[0], np.float64)
    fore = block(nose[1] - 0.040 * fig, nose[0], 0.015 * fig, 0.025 * fig)
    if chest.sum() < 50 or fore.sum() < 20:
        return None
    c_ref = np.median(rgb[chest], axis=0)
    s_ref = np.median(rgb[fore], axis=0)
    dc = np.sqrt(((rgb - c_ref) ** 2).sum(axis=2))
    ds = np.sqrt(((rgb - s_ref) ** 2).sum(axis=2))
    skin = op & (ds < dc)

    hem = neck[1] + 0.090 * fig
    garment = np.zeros((H, W), bool)
    for y in range(int(hem), int(hipm[1]) - int(0.02 * fig)):
        xs = np.nonzero(op[y])[0]
        if len(xs) < 20:
            continue
        sk = skin[y]
        lo = hi = int(cx)
        while lo > xs.min() and not sk[lo - 4:lo].all():
            lo -= 1
        while hi < xs.max() and not sk[hi:hi + 4].all():
            hi += 1
        garment[y, lo:hi] = True
    garment &= op

    sleeve = np.zeros((H, W), bool)
    for sho, elb in ((2, 3), (5, 6)):
        a_, b_ = np.asarray(kps[sho], np.float64), np.asarray(kps[elb], np.float64)
        for t in np.linspace(0.15, 0.70, 24):
            px = a_ + (b_ - a_) * t
            sleeve |= block(px[1], px[0], 2, 0.018 * fig)
    sleeve &= op

    # THE FOREARM PROBE IS SKIN, NOT A BAND AT FOREARM HEIGHT. The first version of this
    # check took the outermost 0.030 of figure height between elbow and wrist and called it
    # the arm, and it reported the mask "swallowing the forearm" on views 280, 285, 286, 290
    # and 292 - every character in a LONG SLEEVE, where that band is jacket and the mask is
    # right to cover it. A check that fails on five of nine characters for being correct is
    # the same fault as a check that passes while swallowing an arm, pointed the other way.
    #
    # Intersecting with skin asks the question that was meant: is there BARE ARM inside the
    # rigid plate. On a long sleeve the probe correctly becomes empty and prints '-'.
    arms = []
    for side in (0, 1):
        m = np.zeros((H, W), bool)
        for y in range(int(kps[3][1]) + 10, int(kps[4][1]) - 5, 3):
            xs = np.nonzero(op[y])[0]
            if len(xs) < 20:
                continue
            w = int(0.030 * fig)
            if side:
                m[y, xs.max() - w:xs.max() - 2] = True
            else:
                m[y, xs.min() + 2:xs.min() + w] = True
        m &= skin
        arms.append(m if m.sum() >= 120 else None)

    hands = []
    for wr in (4, 7):
        b = block(np.asarray(kps[wr], np.float64)[1] + 0.020 * fig,
                  np.asarray(kps[wr], np.float64)[0], 0.040 * fig, 0.050 * fig)
        flesh = b & skin
        hands.append(flesh if flesh.sum() >= 0.08 * max(1, b.sum()) else None)

    return {"in": [("chest", chest), ("GARMENT", garment), ("sleeve", sleeve)],
            "out": [("R forearm", arms[0]), ("L forearm", arms[1]),
                    ("R hand", hands[0]), ("L hand", hands[1])],
            "skin": skin}


def plate_stats(rgb, alpha, kps, fig):
    """The rigid plate's own properties, measured on a WARPED cel rather than on the mask.

    THIS EXISTS BECAUSE `--shirt-check` READ GREEN OVER A BROKEN SHIRT, and the reason is the
    fourth instance of one shape in this project. The mask test runs `shirt_mask` on the
    SOURCE photograph and never opens a warped cel: it asks whether the mask covers the
    garment at the point of DEFINITION and can say nothing about what the warp then does to
    those pixels at the point of USE. Cel 0 is the one cel that is essentially the source, so
    a defect that is a function of POSE is invisible to it by construction. Â§1's CLIP guard
    covered one of two identical inputs; Â§8's GPU lock guarded one tool against itself; Â§7h's
    forearm probe was positioned by the same wrong assumption as the fix. Same shape each
    time: the check was not aimed at the thing that varies.

    What varies here is the pose, so the test has to be on the output, and the property to
    test is the one the section claims: THE PLATE IS RIGID. A rigid motion preserves area,
    and it does not thin a limb.

        area    shirt-coloured pixels, over the source's. A rigid plate reads 1.00 whatever
                its rotation or translation. Measured on the cels that shipped: 1.02, 1.06,
                1.11, 1.04 - the plate grows 11% on cel 2, which is the bell-shaped lobe
                hanging over the hip.
        arm     the narrowest width of the forearm band. Source 38 and 36 px; the cels read
                33/34, 11/33, 11/37, 15/33 - the right forearm collapses to under a third on
                exactly the three cels that are wrong.

    Both rank the four cels in the order a human ranked them by eye, and both pass cel 0.
    """
    op = alpha > 100
    H, W = op.shape
    neck = np.asarray(kps[1], np.float64)
    hipm = (np.asarray(kps[8], np.float64) + np.asarray(kps[11], np.float64)) / 2.0
    cx = 0.5 * (neck[0] + hipm[0])
    nose = np.asarray(kps[0], np.float64)

    def med(y0, y1, x0, x1):
        m = np.zeros((H, W), bool)
        m[int(max(0, y0)):int(y1), int(max(0, x0)):int(x1)] = True
        m &= op
        return np.median(rgb[m], axis=0) if m.sum() > 40 else None

    c = med(neck[1] + 0.05 * fig, neck[1] + 0.14 * fig, cx - 0.04 * fig, cx + 0.04 * fig)
    s = med(nose[1] - 0.055 * fig, nose[1] - 0.025 * fig,
            nose[0] - 0.025 * fig, nose[0] + 0.025 * fig)
    if c is None or s is None:
        return None
    dc = np.sqrt(((rgb - c) ** 2).sum(axis=2))
    ds = np.sqrt(((rgb - s) ** 2).sum(axis=2))
    shirt = op & (dc < ds) & (dc < 70)
    skin = op & (ds < dc)
    arms = []
    for elb, wri in ((3, 4), (6, 7)):
        e = np.asarray(kps[elb], np.float64)
        w_ = np.asarray(kps[wri], np.float64)
        widths = []
        for t in np.linspace(0.2, 0.8, 12):
            px = e + (w_ - e) * t
            y = int(px[1])
            if not (0 <= y < H) or not skin[y].any():
                continue
            band = np.nonzero(skin[y])[0]
            near = band[np.abs(band - px[0]) < 0.07 * fig]
            if len(near) >= 3:
                widths.append(int(near.max() - near.min() + 1))
        # THE MEDIAN, NOT THE MINIMUM. The narrowest single sample along the band is wherever
        # the shirt happens to cross the arm, so it reads a sliver on frames whose arms are
        # fine - it flagged a cel at 39% that a human sees as a normal arm with a hand on it.
        # "Is this limb a ribbon" is a question about the limb, not about its thinnest row.
        arms.append(int(np.median(widths)) if widths else 0)
    return {"area": int(shirt.sum()), "arms": arms}


def report_shirt(a):
    """`--shirt-check <dir>`: run the both-halves mask test over every view in a cel
    directory and dump the mask over each figure at 1:1."""
    import glob
    with open(os.path.join(a.poses, "poses.json")) as f:
        poses = json.load(f)
    print("%-6s %8s | %6s %8s %7s | %6s %6s %6s %6s"
          % ("view", "mask px", "chest", "GARMENT", "sleeve",
             "Rarm", "Larm", "Rhand", "Lhand"))
    bad = []
    for p in sorted(glob.glob(os.path.join(a.shirt_check, "cut_*_l0_c0.png"))):
        v = int(os.path.basename(p).split("_")[1])
        key = "%d/0/0" % v
        if key not in poses["cels"]:
            continue
        im = np.asarray(Image.open(p).convert("RGBA"))
        rgb, alp = im[:, :, :3].astype(np.float64), im[:, :, 3]
        kps = np.asarray(poses["cels"][key]["keypoints"], np.float64)
        fig = poses["cels"][key].get("fig_h") or im.shape[0]
        pr = shirt_probes(rgb, alp, kps, fig)
        if pr is None:
            print("%-6d  probes unavailable" % v)
            continue
        m = shirt_mask(rgb, alp, kps, fig)
        cells = []
        for _, r in pr["in"]:
            cells.append("%5.1f%%" % (100 * m[r].mean()) if r.sum() else "    -")
        for _, r in pr["out"]:
            cells.append("    -" if r is None
                         else "%5.1f%%" % (100 * m[r].mean()) if r.sum() else "    -")
        print("%-6d %8d | %6s %8s %7s | %6s %6s %6s %6s"
              % (v, m.sum(), cells[0], cells[1], cells[2],
                 cells[3], cells[4], cells[5], cells[6]))
        for nm, r in pr["in"]:
            if r.sum() and m[r].mean() < 0.90:
                bad.append("%d %s only %.1f%% covered" % (v, nm, 100 * m[r].mean()))
        for nm, r in pr["out"]:
            if r is not None and r.sum() and m[r].mean() > 0.15:
                bad.append("%d %s %.1f%% SWALLOWED" % (v, nm, 100 * m[r].mean()))
        if a.out:
            os.makedirs(a.out, exist_ok=True)
            ov = im.copy()
            ov[m] = (0.45 * ov[m] + 0.55 * np.array([255, 40, 40, 255])).astype(np.uint8)
            for _, r in pr["out"]:
                if r is not None:
                    ov[r] = [60, 160, 255, 255]
            ys, xs = np.nonzero(alp > 100)
            o = Image.fromarray(ov, "RGBA").crop((int(xs.min()) - 8, int(ys.min()) - 8,
                                                  int(xs.max()) + 8, int(ys.max()) + 8))
            bg = Image.new("RGBA", o.size, (92, 132, 92, 255))
            Image.alpha_composite(bg, o).convert("RGB").save(
                os.path.join(a.out, "shirtcheck_%d.png" % v))
    # ---------------------------------------------------------------- THE POINT OF USE
    # Everything above tests the MASK on the source. Everything below opens the WARPED cels,
    # because the defect this was extended for is a function of the POSE and the mask is the
    # same for every cel.
    plates = sorted(glob.glob(os.path.join(a.shirt_check, "cut_*_l0_c[1-9].png")))
    if not plates:
        print()
        print("no warped cels here (cut_*_c1..c3), so ONLY the mask was tested - which is")
        print("the point of definition and cannot see a defect that depends on the pose.")
    else:
        per_cel = []
        for p in sorted(glob.glob(os.path.join(a.shirt_check, "cut_*_l0_c*.png"))):
            base = os.path.basename(p)
            v = int(base.split("_")[1])
            c = int(base.split("_c")[1].split(".")[0])
            key, k0 = "%d/0/%d" % (v, c), "%d/0/0" % v
            if key not in poses["cels"] or k0 not in poses["cels"]:
                continue
            im = np.asarray(Image.open(p).convert("RGBA"))
            fig = poses["cels"][k0].get("fig_h") or im.shape[0]
            # THE SAME KEYPOINTS THE RENDERER USED, not the authored ones. The renderer
            # clamps impossible limb lengths, so a probe placed from the raw pose looks where
            # the arm is no longer drawn and reports a collapsed forearm on a correct frame.
            # Testing at the point of use means using the pose at the point of use too.
            kp = clamp_limbs(np.asarray(poses["cels"][k0]["keypoints"], np.float64),
                             np.asarray(poses["cels"][key]["keypoints"], np.float64))
            st = plate_stats(im[:, :, :3].astype(np.float64), im[:, :, 3], kp, fig)
            if st is not None:
                per_cel.append((v, c, st))

        print()
        print("THE PLATE AT THE POINT OF USE - warped cels, not the mask")
        print("%-6s %-5s %9s %10s %14s %s"
              % ("view", "cel", "area", "area/med", "arm narrowest", "verdict"))
        # THE REFERENCE IS THE LOOP'S MEDIAN, NOT CEL 0, and Â§7a records why in almost these
        # words for the drift checker: comparing to cel 0 assumes cel 0 is correct, and on a
        # loop there is no reason it should be. It bit again here, on the very experiment it
        # was needed for. Winding the arm swing to zero moved the defect ONTO cel 0 - its
        # forearm travel went from 43.8 px to 73.6 - and the other three were then scored
        # against a broken reference and read 0.98-0.99, green, while the picture showed the
        # lobe sitting on cel 0. Second time in this file; the shape is "the reference is
        # whichever one I happened to trust".
        # AND THE SOURCE PHOTOGRAPH IS THE REFERENCE WHEN IT IS AVAILABLE (`--src`), because
        # the loop median has the mirror-image blind spot: it cannot see a defect the WHOLE
        # loop shares. Both failures were observed within one experiment. Cel 0 as reference
        # went green when the defect moved onto cel 0; the median went green when winding the
        # swing to zero spread a milder lobe across all four. "Rigid" is defined against the
        # thing the plate was cut from, so that is what to compare with.
        src_ref = None
        if a.src and os.path.exists(a.src):
            im0 = np.asarray(Image.open(a.src).convert("RGBA"))
            k00 = "%d/0/0" % per_cel[0][0]
            src_ref = plate_stats(im0[:, :, :3].astype(np.float64), im0[:, :, 3],
                                  np.asarray(poses["cels"][k00]["keypoints"], np.float64),
                                  poses["cels"][k00].get("fig_h") or im0.shape[0])
            if src_ref:
                print("reference: the SOURCE photograph, %d px" % src_ref["area"])
        if src_ref is None:
            print("reference: the loop median (pass --src for the source photograph, which "
                  "is the only one that can see a defect the whole loop shares)")
        for v, c, st in per_cel:
            same = [s for vv, _, s in per_cel if vv == v]
            ref_area = (src_ref["area"] if src_ref
                        else float(np.median([s["area"] for s in same])))
            ref_arms = (src_ref["arms"] if src_ref
                        else [float(np.median([s["arms"][i] for s in same]))
                              for i in (0, 1)])
            ratio = st["area"] / max(1.0, ref_area)
            thin = min(st["arms"][i] / max(1.0, ref_arms[i]) for i in (0, 1))
            why = []
            if abs(ratio - 1.0) > 0.04:
                why.append("plate area %+.0f%% off the loop median - A RIGID PLATE "
                           "PRESERVES AREA" % (100 * (ratio - 1)))
            if thin < 0.60:
                why.append("forearm down to %.0f%% of the loop median" % (100 * thin))
            print("%-6d %-5d %9d %9.2fx %14s %s"
                  % (v, c, st["area"], ratio, "%d,%d" % tuple(st["arms"]),
                     "ok" if not why else "FAIL"))
            for w in why:
                bad.append("%d cel %d: %s" % (v, c, w))

    print()
    if bad:
        print("%d problem(s):" % len(bad))
        for b in bad:
            print("   " + b)
    else:
        print("every view: garment covered, arms and hands excluded, plate rigid")
    print("\nA hand printed as '-' means the block at that wrist holds no flesh (7e), not "
          "that it passed.")
    return 1 if bad else 0


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
    ap.add_argument("--shirt-check", dest="shirt_check",
                    help="run the both-halves shirt-mask test over every cut_*_c0.png in "
                         "this directory and exit. Needs --poses; --out dumps the mask over "
                         "each figure at 1:1. Reports what the mask must COVER (chest, the "
                         "whole garment, the sleeves) and what it must NOT (both forearms, "
                         "both hands) - a mask that covers the chest perfectly while "
                         "swallowing an arm passes a shirt-shaped test")
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
    ap.add_argument("--torso-feather", type=float, default=0.050, dest="torso_feather",
                    help="width, as a fraction of figure height, over which the rigid "
                         "ribcage hands back to the skinned field at its own edge. 0 "
                         "restores the hard switch, which tears down the flank where the "
                         "cloth meets the swinging arm")
    ap.add_argument("--foot-flex", type=float, default=1.0, dest="foot_flex",
                    help="let the SWING foot's shoe stretch and rotate about its ankle "
                         "instead of being carried perfectly flat, driven by that shin's "
                         "own foreshortening so a planted foot is untouched. 0 is off and "
                         "reproduces the flat carried shoe exactly")
    ap.add_argument("--no-occlude", dest="occlude", action="store_false", default=True,
                    help="stop drawing the NEAR leg over the far one. Occlusion builds the "
                         "frame in three "
                         "layers, each with its own field, instead of one - a single "
                         "backward field maps every destination pixel once and so cannot "
                         "express one limb being in front of another")
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
    if a.shirt_check:
        return report_shirt(a)
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

    rigid = {}
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
                       props=props, hard_foot=a.hard_foot,
                       torso_feather=a.torso_feather, foot_flex=a.foot_flex,
                       occlude=a.occlude)
            note = "warped"
        p = os.path.join(a.out, f"cut_{a.view}_l{a.loop}_c{c}.png")
        Image.fromarray(out, "RGBA").save(p)

        # RIGIDITY, MEASURED THROUGH THE REGION MASK AND CARRIED FORWARD.
        #
        # The shoe's area is a known invariant: a rigid transform preserves it, so the
        # correct value is 1.00 and any drift is deformation by definition. Two earlier
        # attempts at this measured the wrong thing - foot-band area across the cycle, which
        # tracks the POSE and scored the clean build worst; and shoe area found by COLOUR in
        # the warped cel, which counts trouser on a character whose shoes and trousers are
        # both black and scored the fixed build identical to the broken one.
        #
        # The region mask is neither. It is the exact set of pixels the rigid transform was
        # applied to, known here and nowhere downstream, so it is recorded here for the gate
        # to use rather than re-derived from pixels that cannot carry the answer.
        # HOW MANY SHOES DID WE DRAW? The known correct answer is two - the same two the
        # source has - so the ratio of shoe-coloured area out to shoe-coloured area in is
        # 1.00 and anything near 1.5 is a second shoe.
        #
        # This is the fourth attempt at a rigidity number and the first that works, and the
        # three failures are worth keeping because each measured something adjacent to the
        # question: foot-band area across the cycle tracked the POSE (the clean build scored
        # worst); shoe area by colour in the warped cel alone had no yardstick; area through
        # the REGION measured the claim rather than the drawing and read 0.20-0.27. The
        # quantity that has a right answer is out-vs-in, same test both sides, and the source
        # is in hand here and nowhere downstream.
        #
        # It is measured here rather than in check_install because the gate sees only the
        # installed cel, and without the source there is no yardstick. Measured on known
        # builds: the clean 30 September cels 0.82-1.05, the build that shipped with a
        # duplicate shoe 0.97-1.57.
        sr = shoe_area_ratio(src, out, src_k, cel.get("fig_h"))
        rigid[str(c)] = {"src_shoe_px": globals().get("LAST_SRC_SHOE_PX", 0),
                         "shoe_area_ratio": round(sr, 3)}
        if sr > 1.25:
            print(f"      SHOE AREA {sr:.2f}x the source - that is a SECOND SHOE, not a "
                  f"wider one", flush=True)
        print(f"  {os.path.basename(p)}  {note}", flush=True)

    with open(os.path.join(a.out, f"rigid_{a.view}_l{a.loop}.json"), "w") as f:
        json.dump({"view": a.view, "loop": a.loop, "cels": rigid}, f, indent=1)
    print(f"4 cels -> {a.out}")


if __name__ == "__main__":
    main()







