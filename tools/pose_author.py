"""AUTHORS an OpenPose skeleton for every walker cel, from canon rather than measurement.

WHY THIS REPLACES THE MEASURING IN tools/sprite_pose.py
-------------------------------------------------------
sprite_pose.py reads the skeleton out of the 39x95 alpha mask. That was the single biggest
cause of the rejected cast: a silhouette that small does not carry a pose. The numbers say
so outright. Foot spread across the four cels of view 282, measured at 96% of the figure's
height, is 15, 16, 17, 17 pixels. View 286 is 11, 12, 12, 12. That is one or two pixels of
signal on a forty-pixel-wide figure, and it is the SAME order as the digitiser noise
tools/restore_walkers.py measured at ~25 levels per pixel. A knee position derived from it
is a coin toss, and a skeleton built out of coin tosses is what produced figures that read
as seated.

So nothing about the POSE is read from the original here. What is still read from it is
only the box - cel size, the figure's top and bottom row, and the horizontal centre - which
is the functional geometry the game's positioning, collision and anchor maths depend on,
and which a silhouette DOES carry reliably.

Everything else is canon: a 7.7-head standing adult, and a four-frame front-facing walk
cycle of the kind that is completely standard and completely well defined. Contact, pass,
contact, pass. Authored once, parameterised by the figure's height and sex, identical in
construction for every character, and therefore identical between the four cels of a loop
except in the ways a walk cycle is supposed to differ.

THE PROPORTIONS
---------------
Fractions of the figure's own height, crown at 0.0 and sole at 1.0. These are the ordinary
artist's canon for an adult at roughly 7.7 heads, which is what a photographic reference
of a standing person actually measures:

    crown 0.000   eyes 0.070   nose 0.088   chin 0.130   shoulder 0.178
    elbow 0.330   wrist 0.455  hip 0.505    knee 0.740   ankle 0.955   sole 1.000

The shoulder and hip half-widths are the only sexed numbers, because they are the only two
the silhouette of a 39-pixel sprite genuinely distinguishes: views 284-287 and 294-297 are
skirt and dress outlines with narrow shoulders, and a trouser figure's shoulder line does
not fit them.

THE WALK
--------
Four frames, front view, looping. Cel 0 and cel 2 are CONTACT - the feet at their widest
separation, one leg forward. Cel 1 and cel 3 are PASS - the swinging leg travelling through
the planted one, its foot lifted and its knee raised, the feet nearly together. The two
contacts are mirror images and so are the two passes, which is what makes the cycle close.

In a FRONT view the forward/back component of a stride is depth and cannot be drawn, so it
is expressed the way it actually reads from the front: horizontal separation, the swinging
foot rising and crossing towards the centre line, and the pelvis lifting at the pass. Arm
swing opposes the legs, as a real gait does, and is small because these arms hang.

`--still` writes the standing idle instead, which is the pose the STOP-AND-SHOW frames use.

OUTPUT
------
    <out>/pose_<view>_l<loop>_c<cel>.png   the skeleton on black, at --size
    <out>/poses.json                       the same schema tools/fit_walkers.py reads,
                                           plus `authored` keypoints in canvas pixels so
                                           the refiner in tools/gen_cast.py knows where the
                                           head, the hands and the feet are without having
                                           to find them.

Run:  python tools/pose_author.py --out <dir> [--size 640x1280] [--still]
"""

import argparse
import json
import os

import numpy as np
from PIL import Image

import sprite_pose

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "png")

BODIES = sprite_pose.BODIES
OUTFITS = sprite_pose.OUTFITS

# Fractions of the figure's height, crown = 0.
Y = {
    "crown": 0.000, "eye": 0.070, "ear": 0.076, "nose": 0.088,
    "shoulder": 0.178, "elbow": 0.330, "wrist": 0.455,
    "hip": 0.505, "knee": 0.740, "ankle": 0.955,
}

# Half-widths, also as fractions of the figure's height.
SH_HALF = {"m": 0.107, "f": 0.092}
HIP_HALF = {"m": 0.052, "f": 0.058}
EYE_DX, EAR_DX = 0.018, 0.032

# ---------------------------------------------------------------------------
# BUILD, PER CHARACTER. This is AUTHORED, and it is the reason the first cast came back as
# four fashion models.
#
# The skeleton above is a canonical 7.7-head figure with a 0.107 shoulder and a 0.052 hip.
# Those are an athletic young adult's proportions, and ControlNet is handed them as a precise
# body frame. Asking the prompt for "a heavy set man in his fifties" on top of that is asking
# it to overrule the one input the model trusts most, and it loses every time. The figures
# were slim because they were TOLD to be slim, in the only language the pose control speaks.
#
# I tried to measure build off the silhouette instead, the way the box is measured, and it
# is not there. Median filled width per row, averaged over all four cels: view 282 (the
# young man) is 0.318 of span and view 292 (the heavy set man) is 0.291 - the heavier figure
# measures NARROWER. The full spread across seventeen views is 0.249 to 0.361 and most of it
# is props rather than people: 0.346 is the newspaper in 287, 0.361 the barrel in 293, 0.249
# the towel in 297. A 39x95 alpha mask does not carry build any more than it carries pose,
# and deriving it from there would be the same mistake in a new place.
#
# So these are design decisions, stated openly, taken from looking at the art:
#
#   heads   how many head-heights tall. 7.7 is a fashion plate. Real middle-aged people are
#           nearer 7, and a larger head relative to the body is most of what reads as
#           "ordinary person" rather than "model".
#   sh, hip half-widths as fractions of figure height.
#   girth   scales the limb columns in the init mannequin, so a heavy figure is given heavy
#           legs to be painted into rather than a thin frame with a fat prompt.
BUILDS = {
    "body0": {"heads": 7.6, "sh": 0.108, "hip": 0.058, "girth": 1.00},   # 280-283, average man
    # 284-287. SHORT AND STOCKY, with a bob. "Short" cannot be height here - every walker
    # cel is 95 rows and the figure is fitted to the same pixel height whatever it is - so
    # it has to be carried by PROPORTION, which is what `heads` is for. At 6.7 the head is a
    # larger fraction of the body and the figure reads short; at 7.0 it read as a tall slim
    # woman however the prompt was worded.
    # 284-287. THE SHORTEST AND STOCKIEST OF THE CAST, at 6.5 heads - below body2's 6.8.
    #
    # There is a real measurement behind this one, unlike the width figures above. Head-band
    # width as a fraction of figure height, averaged over four cels and cross-checked on two
    # independent outfits per character:
    #
    #     body2 0.136   body0 0.150   body1 0.176 / 0.181   body3 0.188 / 0.193
    #
    # The two women's head mass is 29% and 38% larger relative to their height than body2's,
    # and the two outfits of each body agree to within 0.005 - so this is signal, where the
    # silhouette WIDTH measurements were noise. A bigger head relative to the body reads
    # shorter and stockier, which is what the art shows.
    #
    # It is used DIRECTIONALLY and not converted into an exact value, because the band
    # includes hair and both women have volume where body2 is balding: a linear mapping puts
    # body2 at 8.4 heads, which the approved 292 frame flatly contradicts.
    "body1": {"heads": 6.5, "sh": 0.102, "hip": 0.090, "girth": 1.22, "chest": 1.12},
    # 290-293. STOCKY, NOT OBESE - and the third attempt at it, because the first two turned
    # the wrong dial. 1.30 and then 1.15 girth both came back with an overhanging belly, and
    # `girth` was never the cause: it scales the torso sides, arms and legs UNIFORMLY and
    # cannot make a paunch on its own.
    #
    # The paunch was in the PROMPT. It said "an overweight man ... thick waist", which is a
    # request for exactly that silhouette, and no amount of narrowing the init's columns was
    # going to argue with it. The wardrobe line now says thickset and barrel chested, and
    # `chest` widens the upper torso so the figure is a column rather than a taper - which is
    # what the 1990 art shows: a waist only modestly wider than the chest.
    "body2": {"heads": 6.8, "sh": 0.124, "hip": 0.084, "girth": 1.05, "chest": 1.22},
    # 294-297. A fuller woman in her forties. Same treatment as body2 and for the same
    # reason: her line said "average build" and she came back as a fashion model, because
    # "average" against a 7.2-head frame is what the model already wanted to draw.
    "body3": {"heads": 6.9, "sh": 0.102, "hip": 0.086, "girth": 1.18, "chest": 1.12},
    "jones": {"heads": 7.4, "sh": 0.100, "hip": 0.055, "girth": 1.00},   # 274-277, cartoon
}

# THE CYCLE. One row per cel: how far each ankle sits from the centre line, how far each
# knee does, how high the swinging foot and knee are lifted, and how much the pelvis rises.
# Positive x is the IMAGE's right, which is the figure's left - consistent everywhere, which
# is all the ControlNet needs.
#
# Contact frames put the feet at +-0.072 of the figure's height, which on a 1.75m person is
# a 25cm stride: a walk, not a march. Pass frames bring them to +-0.015, the swinging one
# lifted 0.048 and its knee 0.034, which is the foot clearing the ground by about 8cm.
#
# A WALK IS NOT A LEG MOTION WITH A STATIC BODY ON TOP, and the first version of this table
# was exactly that: it moved the ankles and the knees and left the head, shoulders, arms and
# torso identical in all four cels. Played back, the user's description was "it doesn't look
# like walking at all, it looks like he is just wriggling his legs", and that is precisely
# what the table produced.
#
# `step` could not see it. step is a whole-frame pixel difference and the legs dominate it,
# so a cycle that moves only the legs scores 17.92 against the original's 20.38 and looks
# healthy while being wrong. The acceptance criterion was measuring the one thing that was
# already working.
#
# MEASURED OFF THE 1990 ART, across the four cels of views 280, 282, 284, 290, 292 and 296,
# as fractions of the figure's own height:
#
#   vertical bob      the figure's HEIGHT varies by 1-2%, and it varies on cels 1 and 3
#   lateral sway      the shoulder centre moves 3.5-4.5px on a 40px cel - 8-10% of the
#                     cel's width, about 3.7% of the figure's height
#   arm swing         the silhouette's extent at the wrist line moves 3.2-8.6% of height
#   counter-rotation  shoulder centre against hip centre, +-1.5% of height
#
# The old table had zero of the first, second and fourth, and 1.6% of the third on only two
# of the four cels. The amplitudes below are those measurements, taken at the low end of
# each range because the measured spread includes the props these figures carry.
#
# THE CROWN IS NO LONGER PINNED. The old comment argued that a constant span keeps one scale
# and one offset valid for the whole loop - true, and right for the FIT, but it is the fit's
# job to absorb that (fit_walkers.refit_for already maps a loop by the union of its four
# extents for exactly this reason). Pinning it there bought tidiness in the wrong place and
# cost the animation its bob.
# THE FOUR MOTIONS ARE PHASED AGAINST EACH OTHER, NOT MERELY PRESENT. In the original they
# all come from one real stride, so they peak together; four correct amplitudes out of phase
# would read as a puppet rather than a walk. The phasing below is the gait, not a guess:
#
#   rise   peaks at the PASS, because that is when the supporting leg is vertical and the
#          body is at the top of its arc. Zero at contact, when the legs are apart and the
#          pelvis is at its lowest. The measurement agrees: the original's cels 1 and 3 are
#          1-2% SHORTER than 0 and 2, so its contacts are its short frames, as here.
#   sway   peaks at the PASS too, and in the direction of the SUPPORTING foot - the body is
#          balanced over one leg there. Zero at contact, when the weight is between both.
#   arm    peaks at CONTACT, opposed to the leading leg, and passes through neutral at the
#          pass exactly as the legs do.
#   twist  shoulders against hips, in phase with the arms, because it is the same rotation.
#
# So it is a quarter-cycle offset: legs and arms extreme at 0 and 2, body extreme at 1 and 3.
# That is what makes it a stride rather than four poses.
CONTACT, PASS = "contact", "pass"
CYCLE = [
    # phase,  lead, (ankle_r, ankle_l), (knee_r, knee_l), lift, rise,  sway,   arm,  twist
    # RISE is 0.038, not the 0.014 the art measures, because the WARP DAMPS IT. Measured on
    # the warped result: an authored 0.014 arrived as 0.0054 where the 1990 art has 0.0212 -
    # the figure was barely bobbing at all. The warp delivers roughly 38% of what is asked
    # vertically, so the authored value is the wanted amplitude divided by that. This is the
    # one place the authored cycle deliberately departs from the measurement, and it does so
    # to LAND on it.
    #
    # TWIST is 0.004, cut from 0.012. The 1990 art's shoulder-to-hip differential is only
    # 0.0078 of figure height across the whole cycle - the torso moves very nearly as a unit.
    # At 0.012 the realised differential was 0.0549, SEVEN TIMES the original, and the pelvis
    # swung through a wide arc out of phase with the shoulders. That is what "nobody's body
    # moves like that when they are walking" was describing.
    (CONTACT, "l", (-0.072, 0.072), (-0.048, 0.048), None, 0.000, 0.000, +0.042, 0.000),
    (PASS,    "r", (-0.014, 0.018), (-0.026, 0.020), "r",  0.027, +0.018, 0.000, 0.000),
    (CONTACT, "r", (-0.072, 0.072), (-0.048, 0.048), None, 0.000, 0.000, -0.042, 0.000),
    (PASS,    "l", (-0.018, 0.014), (-0.020, 0.026), "l",  0.027, -0.018, 0.000, 0.000),
]

STILL = (None, None, (-0.030, 0.030), (-0.036, 0.036), None, 0.000, 0.0, 0.0, 0.0)


def proportions(build):
    """The Y table adjusted for how many heads tall this character is.

    A 6.8-head figure is not a 7.7-head figure scaled down - the head grows and everything
    below the chin is compressed to make room, which is what makes a stocky person stocky
    rather than merely short. The chin sits at 1/heads of the figure, and the landmarks
    below it are shifted by the difference, with the ground line fixed because that is what
    the game positions against."""
    y = dict(Y)
    ref = 7.7
    chin_ref = 1.0 / ref
    chin = 1.0 / build["heads"]
    d = chin - chin_ref
    for k in ("eye", "ear", "nose"):
        y[k] = Y[k] * chin / chin_ref
    # The neck and shoulders take the full offset; by the hip it is half absorbed and by the
    # knee it is gone, so the legs stay the right length for the stride the cycle authors.
    y["shoulder"] = Y["shoulder"] + d
    y["elbow"] = Y["elbow"] + d * 0.75
    y["wrist"] = Y["wrist"] + d * 0.55
    y["hip"] = Y["hip"] + d * 0.45
    y["knee"] = Y["knee"] + d * 0.12
    return y


def skeleton(build, frame):
    """COCO-18 keypoints in figure-relative units: x about a centre line at 0, y from the
    crown at 0 to the sole at 1, both scaled by the figure's height.

    Returns a list of 18 (x, y) pairs, none of them None - an authored skeleton has no
    missing joints, which is itself part of the point. Every dropped joint in the measured
    version was a place the model was left to invent a limb."""
    phase, lead, (an_r, an_l), (kn_r, kn_l), lift, rise, sway, arm, twist = frame
    Y = proportions(build)
    sh = build["sh"]
    hip = build["hip"]
    eye_dx = EYE_DX * (7.7 / build["heads"])
    ear_dx = EAR_DX * (7.7 / build["heads"])

    # THE WHOLE BODY RISES AT THE PASS, CROWN INCLUDED. The previous version pinned the crown
    # so every cel had an identical span, and that is what made the figure wriggle its legs
    # under a motionless head. The ankles stay on the ground line - the planted foot cannot
    # move - so the rise lifts the pelvis, the shoulders and the head together, which is what
    # a stride does and what the 1990 art measures at 1-2% of height.
    y_hip = Y["hip"] - rise
    y_sh = Y["shoulder"] - rise
    y_head = -rise

    # The swinging leg's foot leaves the ground and its knee comes up with it.
    # FOOT LIFT, REDUCED FOR THE WARP. 0.048 of figure height is the right lift for a real
    # stride and is what a generated frame would draw. A warp has to DEFORM a photographed
    # shoe that far, and at 49 canvas pixels the shoe stretched into a dark spike rather
    # than rising as a shoe. 0.030 still reads as a foot leaving the ground - it is more
    # lift than the 1-5 pixels the first warp managed - and the shoe survives it.
    lift_r = 0.030 if lift == "r" else 0.0
    lift_k_r = 0.022 if lift == "r" else 0.0
    lift_l = 0.030 if lift == "l" else 0.0
    lift_k_l = 0.022 if lift == "l" else 0.0

    # ARMS OPPOSE THE LEGS. Front-on, a forward-swung arm reads as the hand drifting in
    # towards the hip and rising slightly; the trailing one drifts out and drops. The
    # amplitude is deliberately small - every one of these figures is walking, not marching,
    # and half of them are carrying something.
    # `arm` is the measured swing amplitude for this cel, signed so the RIGHT arm goes with
    # it. Front-on, a forward-swung arm reads as the hand drifting in towards the hip and
    # rising; the trailing one drifts out and drops. Measured at 3.2-8.6% of figure height at
    # the wrist line, against the 1.6% the first version used.
    arm_r = +arm
    arm_l = -arm

    # SWAY moves the whole body over the supporting foot; the ANKLES DO NOT GO WITH IT,
    # because a planted foot stays planted. So the sway is added to everything from the knees
    # up, tapering to zero at the ground, which is also what puts the lean into the legs.
    def s(frac):
        return sway * frac

    k = [None] * 18
    k[0] = (s(1.00), Y["nose"] + y_head)
    k[1] = (s(1.00), y_sh)
    k[2] = (-sh + s(1.00) + twist, y_sh)
    k[5] = (+sh + s(1.00) + twist, y_sh)
    k[3] = (-(sh * 0.90 + arm_r * 0.5) + s(0.95), Y["elbow"] + y_head - abs(arm_r) * 0.25)
    k[6] = (+(sh * 0.90 + arm_l * 0.5) + s(0.95), Y["elbow"] + y_head - abs(arm_l) * 0.25)
    k[4] = (-(sh * 0.97 + arm_r) + s(0.90), Y["wrist"] + y_head - arm_r * 0.55)
    k[7] = (+(sh * 0.97 + arm_l) + s(0.90), Y["wrist"] + y_head + arm_l * 0.55)
    # The hips take the OPPOSITE twist to the shoulders - that counter-rotation is the
    # measured shoulder-to-hip offset of +-1.5% of height, and it is the same rotation the
    # arms are expressing, so it shares their phase.
    k[8] = (-hip + s(0.85) - twist, y_hip)
    k[11] = (+hip + s(0.85) - twist, y_hip)
    k[9] = (kn_r + s(0.45), Y["knee"] - lift_k_r)
    k[12] = (kn_l + s(0.45), Y["knee"] - lift_k_l)
    k[10] = (an_r, Y["ankle"] - lift_r)
    k[13] = (an_l, Y["ankle"] - lift_l)
    k[14] = (-eye_dx + s(1.00), Y["eye"] + y_head)
    k[15] = (+eye_dx + s(1.00), Y["eye"] + y_head)
    k[16] = (-ear_dx + s(1.00), Y["ear"] + y_head)
    k[17] = (+ear_dx + s(1.00), Y["ear"] + y_head)
    return k


def box_of(view, loop, cel):
    """The functional geometry, and ONLY that: cel size, the figure's first and last row,
    and the columns it occupies. No limb is read from here."""
    p = os.path.join(SRC, f"view_{view}_l{loop}_c{cel}.png")
    if not os.path.exists(p):
        return None
    a = np.asarray(Image.open(p).convert("RGBA"))[:, :, 3] > 128
    rows = np.nonzero(a.any(axis=1))[0]
    cols = np.nonzero(a.any(axis=0))[0]
    if not len(rows) or not len(cols):
        return None
    return {"cel_w": a.shape[1], "cel_h": a.shape[0],
            "top": int(rows[0]), "bottom": int(rows[-1]),
            "span": int(rows[-1] - rows[0] + 1),
            "col0": int(cols[0]), "col1": int(cols[-1]) + 1}


def place(box, size, margin, side):
    """(s, ox, oy, fig_h, cx, top_px): how the cel's pixel space maps onto the canvas.

    Fitted by HEIGHT, then clamped so the cel's full WIDTH still lands inside the canvas -
    because fit_walkers crops a window `cel_w * factor` wide out of the generated frame, and
    view 293 is 61 cels wide. Without the clamp that window runs off the side of a 640-pixel
    canvas and the figure loses an arm on the way back down."""
    W, H = size
    s = min(H * (1 - 2 * margin) / box["span"], W * (1 - 2 * side) / box["cel_w"])
    fig_h = box["span"] * s
    top_px = (H - fig_h) / 2.0
    oy = top_px - box["top"] * s
    ox = W / 2.0 - (box["cel_w"] / 2.0) * s
    return s, ox, oy, fig_h, W / 2.0, top_px


def to_canvas(k, cx, top_px, fig_h):
    return [(cx + x * fig_h, top_px + y * fig_h) for (x, y) in k]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    # 704x1152 rather than 640x1280, and the reason is the ASPECT RATIO, not the memory.
    # Both are about 810k pixels and both fit in 4GB with slicing on. But SD1.5 was trained
    # at 512x512 and degrades with distance from the subject's centre at extreme aspects: at
    # 1:2 the head came back flawless and the figure dissolved below the thigh, twice,
    # whatever was done to the prompt, the margins or the init. 704x1152 is 1:1.64, which is
    # near the 1:1.5 of the 512x768 the model is routinely used at, and the figure holds
    # together end to end. The cost is that the figure is 1025px instead of 1139px and the
    # fit scales it up by 1.11 to reach the 1140 a 95-row sprite occupies at 12x - which is
    # a far smaller price than a sprite with no feet.
    ap.add_argument("--size", default="704x1152",
                    help="4GB with attention slicing tops out near 810k pixels; 768x1536 "
                         "crashes the driver either way")
    # 0.055 top and bottom leaves the figure 1139px tall on a 1280 canvas, which is within
    # a pixel of the 1140 a 95-row sprite occupies at 12x - so the base generation is
    # already at final resolution and the fit downsamples by nothing.
    #
    # IT IS NOT MERELY A SCALE CHOICE. At the 0.030 it started on, the soles sat 90% of the
    # way down the canvas and the first run came back with the legs dissolving into the
    # backdrop below the knee and no feet at all. SD1.5 fades or crops whatever it is asked
    # to put against the frame edge. Seventy pixels of footroom is what stops that.
    ap.add_argument("--margin", type=float, default=0.055)
    ap.add_argument("--side", type=float, default=0.025)
    ap.add_argument("--still", action="store_true",
                    help="write the standing idle for every cel instead of the walk cycle "
                         "- this is what the stop-and-show frames are generated from")
    ap.add_argument("--views", help="comma separated, e.g. 282,286")
    a = ap.parse_args()

    W, H = (int(v) for v in a.size.lower().split("x"))
    os.makedirs(a.out, exist_ok=True)
    only = set(int(v) for v in a.views.split(",")) if a.views else None

    man = {"size": [W, H], "margin": a.margin, "still": bool(a.still),
           "authored": True, "bodies": {}, "cels": {}}
    for name, body in BODIES.items():
        man["bodies"][name] = dict(body, views=[body["base"] + i for i in range(4)],
                                   outfits=OUTFITS)

    n = 0
    for name, body in BODIES.items():
        sex = body["sex"]
        for oi, outfit in enumerate(OUTFITS):
            view = body["base"] + oi
            if only and view not in only:
                continue
            for loop in range(2):
                for cel in range(8):
                    box = box_of(view, loop, cel)
                    if box is None:
                        continue

                    # The 13x13 second loop of view 293 is a prop, not a person. An authored
                    # human skeleton scaled into a 13-pixel box is nonsense, and generating
                    # against it would produce a tiny mangled figure where an object belongs.
                    if box["span"] < 40:
                        continue

                    s, ox, oy, fig_h, cx, top_px = place(box, (W, H), a.margin, a.side)
                    frame = STILL if a.still else CYCLE[cel % 4]
                    build = BUILDS[name]
                    k = to_canvas(skeleton(build, frame), cx, top_px, fig_h)

                    img = sprite_pose.draw_pose(k, (W, H), (1.0, 1.0, 0.0, 0.0),
                                                max(3, int(round(H / 150))))
                    img.save(os.path.join(a.out, f"pose_{view}_l{loop}_c{cel}.png"))

                    man["cels"][f"{view}/{loop}/{cel}"] = {
                        "body": name, "outfit": outfit, "view": view,
                        "loop": loop, "cel": cel, "sex": sex,
                        "phase": "still" if a.still else frame[0],
                        "build": build,
                        "cel_size": [box["cel_w"], box["cel_h"]],
                        "bbox": [box["top"], box["bottom"], box["span"]],
                        "fit": [s, ox, oy],
                        "fig_h": fig_h,
                        "keypoints": [[round(p[0], 2), round(p[1], 2)] for p in k],
                        "measure": {"top": box["top"], "bottom": box["bottom"],
                                    "span": box["span"], "cel_w": box["cel_w"],
                                    "cel_h": box["cel_h"]},
                    }
                    n += 1

    with open(os.path.join(a.out, "poses.json"), "w") as f:
        json.dump(man, f, indent=1)
    print(f"{n} authored poses -> {a.out}")


if __name__ == "__main__":
    main()






