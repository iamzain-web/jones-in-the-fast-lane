"""Measures the twenty walker views and writes an OpenPose skeleton for every cel.

WHY THIS EXISTS
---------------
The new character art is generated with a pose-conditioned diffusion model, and the pose
has to come from the original cels or the game's timing, anchor points and hit areas stop
being valid. But a pose DETECTOR cannot be pointed at a 39x95 sprite: there is nothing
there for it to find. So the skeleton is MEASURED from the alpha mask and the cel's own
column profile instead, which is both more accurate at this size and completely
deterministic - the same cel always yields the same skeleton, which is half of what keeps
the four cels of a cycle consistent with each other.

WHAT IS TAKEN FROM THE ORIGINAL, AND WHAT IS NOT
------------------------------------------------
Taken: the bounding box, the height, the ground line, the horizontal centre of mass, and
the per-cel leg and arm positions - i.e. the FUNCTIONAL geometry. Everything the figure
is made of - face, build, clothing, colour - is invented downstream and owes the original
nothing. No pixel of the original reaches the generated image; only these numbers do.

THE MODEL
---------
BODY_25 is overkill for a 95-pixel frontal figure. This writes the COCO-18 layout that
ControlNet's openpose model was trained on:

    0 nose        1 neck        2 rsho   3 relb   4 rwri
    5 lsho        6 lelb        7 lwri   8 rhip   9 rkne
    10 rank       11 lhip       12 lkne  13 lank  14 reye
    15 leye       16 rear       17 lear

Proportions along the figure's own height come from the measured silhouette where the
silhouette states them (head top, ground, shoulder line, waist pinch) and from a fixed
canon where it does not (eye line, ear line) - a 95-pixel sprite simply does not resolve
an ear.

OUTPUT
------
    <out>/pose_<view>_l<loop>_c<cel>.png   the skeleton, at --size, on black
    <out>/poses.json                        every measurement, for the generator

Run:  python tools/sprite_pose.py --out <dir> [--size 512x1024]
"""

import argparse
import json
import math
import os

from PIL import Image, ImageDraw

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "png")

# The twenty walker views, grouped the way the scripts group them.
#
# room1.sc:1302-1312 and startTrn.sc:495-515 both choose a BASE from whichBody --
# 0 -> 280, 1 -> 284, 2 -> 290, 3 -> 294, and 274 when playing: is 29, which is the demo --
# then add (wearing - 34) for the three clothing levels, or +3 when weeksOfClothing: is 0,
# which is the undressed cel. So each body is four consecutive views and the fourth is the
# one the player is embarrassed by.
BODIES = {
    "jones": {"base": 274, "sex": "m", "note": "demo attract-mode walker, flat cartoon art"},
    "body0": {"base": 280, "sex": "m"},
    "body1": {"base": 284, "sex": "f"},
    "body2": {"base": 290, "sex": "m"},
    "body3": {"base": 294, "sex": "f"},
}

OUTFITS = ["best", "mid", "cheap", "undressed"]  # base+0, +1, +2, +3

LIMBS = [
    (1, 2), (2, 3), (3, 4), (1, 5), (5, 6), (6, 7), (1, 8), (8, 9), (9, 10),
    (1, 11), (11, 12), (12, 13), (1, 0), (0, 14), (14, 16), (0, 15), (15, 17),
]

COLOURS = [
    (255, 0, 0), (255, 85, 0), (255, 170, 0), (255, 255, 0), (170, 255, 0),
    (85, 255, 0), (0, 255, 0), (0, 255, 85), (0, 255, 170), (0, 255, 255),
    (0, 170, 255), (0, 85, 255), (0, 0, 255), (85, 0, 255), (170, 0, 255),
    (255, 0, 255), (255, 0, 170), (255, 0, 85),
]


def load(view, loop, cel):
    p = os.path.join(SRC, f"view_{view}_l{loop}_c{cel}.png")
    return Image.open(p).convert("RGBA") if os.path.exists(p) else None


def mask_of(im):
    """Solid pixels. The decode leaves a cyan/white matte fringe at alpha 255 around some
    figures, so 'solid' is alpha, not colour; the fringe is inside the silhouette the
    original actually occupied and belongs in the measurement."""
    a = im.getchannel("A").load()
    w, h = im.size
    return [[a[x, y] > 128 for x in range(w)] for y in range(h)], w, h


def runs(row):
    """Contiguous True spans in a mask row, as (start, end_exclusive)."""
    out, s = [], None
    for i, v in enumerate(row):
        if v and s is None:
            s = i
        elif not v and s is not None:
            out.append((s, i))
            s = None
    if s is not None:
        out.append((s, len(row)))
    return out


def widest(row):
    r = runs(row)
    if not r:
        return None
    a, b = max(r, key=lambda t: t[1] - t[0])
    return (a + b) / 2.0, b - a


def measure(im):
    """Everything the generator needs about one cel, in the cel's own pixel space."""
    m, w, h = mask_of(im)
    rows = [i for i in range(h) if any(m[i])]
    if not rows:
        return None
    top, bottom = rows[0], rows[-1]
    span = bottom - top + 1

    prof = []
    for y in range(h):
        c = widest(m[y])
        prof.append(c if c else (None, 0))

    def at(frac):
        """Centre and width at a fraction of the way down the FIGURE (not the cel)."""
        y = min(bottom, max(top, int(round(top + frac * (span - 1)))))
        cx, cw = prof[y]
        return y, cx, cw

    # The head is everything above the NECK PINCH: the narrowest row between the crown and
    # the shoulder rise. Two things make this fiddly at 95 pixels and both bit once.
    #
    # The crown search must stop at 12% of the figure. Let it run to a third and it finds
    # the SHOULDERS, which are wider than any head here, and the pinch below them is the
    # waist - every figure came out with a 26-pixel head.
    #
    # And some figures have no pinch at all: view 295's hair runs straight into her
    # shoulders and the profile only ever widens. That is detectable - the minimum lands
    # immediately below the crown - and falls back to the canon, 16% of the figure, which
    # is where the chin is on the ones that DO resolve.
    def last_max(ys, key):
        best, arg = None, None
        for y in ys:
            v = key(y)
            if best is None or v >= best:
                best, arg = v, y
        return arg

    crown_hi = min(bottom, top + max(2, int(span * 0.12)))
    crown_y = last_max((y for y in range(top, crown_hi + 1) if prof[y][0] is not None),
                       lambda y: prof[y][1]) or top
    neck_lo = min(bottom, crown_y + 2)
    neck_hi = min(bottom, top + max(4, int(span * 0.35)))
    cand = [y for y in range(neck_lo, neck_hi + 1) if prof[y][0] is not None]
    if cand:
        lo = min(prof[y][1] for y in cand)
        pinch = last_max((y for y in cand if prof[y][1] == lo), lambda y: y)
    else:
        pinch = None
    neck_y = pinch if pinch is not None and pinch > crown_y + 3 \
        else min(bottom, top + max(2, int(round(span * 0.16))))
    head_cx = sum(prof[y][0] for y in range(top, neck_y + 1) if prof[y][0] is not None) / \
              max(1, sum(1 for y in range(top, neck_y + 1) if prof[y][0] is not None))

    # Shoulders sit a fixed 5% of the figure below the neck. NOT the widest row below the
    # neck: that is the elbows, a fifth of the way down the torso, and it dragged the
    # shoulder line down onto the waist.
    sh_y = min(bottom, neck_y + max(2, int(round(span * 0.05))))
    sh_cx, sh_w = prof[sh_y] if prof[sh_y][0] is not None else (prof[neck_y][0], prof[neck_y][1])

    hip_y, hip_cx, hip_w = at(0.52)

    # LEGS. Below the hip the mask separates into two runs on most cels; where it does not
    # (a skirt, a barrel, a newspaper) the two legs are taken as a fixed fraction of the
    # silhouette width about its centre, which is what the figure's own stance implies.
    def legs(frac):
        y, cx, cw = at(frac)
        # Only runs UNDER the hips are legs. Without this test the briefcase view 284
        # carries at knee height reads as the left leg and the skeleton splays sideways.
        # hip_w is the whole silhouette at the hip, ARMS INCLUDED, so it is a generous
        # reference; 0.42 of it still admits a leg swung well out and excludes the
        # briefcase view 284 carries at knee height, which is what this is for.
        reach = max(3.0, hip_w * 0.42)
        thin = max(2.0, hip_w * 0.18)
        r = [t for t in runs(m[y])
             if t[1] - t[0] >= thin and abs((t[0] + t[1]) / 2.0 - hip_cx) <= reach]
        if len(r) >= 2:
            r.sort(key=lambda t: t[1] - t[0], reverse=True)
            a, b = sorted(r[:2], key=lambda t: t[0])
            return y, (a[0] + a[1]) / 2.0, (b[0] + b[1]) / 2.0, True
        if r:
            a, b = r[0]
            c, wd = (a + b) / 2.0, b - a
            return y, c - wd * 0.24, c + wd * 0.24, False
        if cx is None:
            return y, None, None, False
        return y, cx - cw * 0.22, cx + cw * 0.22, False

    kn_y, kn_r, kn_l, kn_split = legs(0.76)
    an_y, an_r, an_l, an_split = legs(0.97)

    # ARMS. Hung straight down from the shoulder line, at the shoulder's own half-width.
    #
    # NOT taken from the silhouette edge, which is what the first version did and is wrong
    # here: every one of these figures is carrying something. View 280's briefcase, 284's
    # satchel and 287's newspaper are all the outermost pixel at hand height, so the arm
    # bones were drawn out to the props and the model painted a coloured wedge across the
    # waist where it thought the forearms were. These are frontal standing figures whose
    # arms hang; a canon is both more accurate and more stable between cels than a
    # measurement of the wrong thing.
    el_y, _, _ = at(0.38)
    wr_y, _, _ = at(0.50)
    arm = max(2.0, sh_w * 0.44)
    el_lo, el_hi = sh_cx - arm * 0.94, sh_cx + arm * 0.94
    wr_lo, wr_hi = sh_cx - arm * 1.00, sh_cx + arm * 1.00

    return {
        "top": top, "bottom": bottom, "span": span, "cel_w": w, "cel_h": h,
        "head_cx": head_cx, "neck_y": neck_y,
        "sh_y": sh_y, "sh_cx": sh_cx, "sh_w": sh_w,
        "hip_y": hip_y, "hip_cx": hip_cx, "hip_w": hip_w,
        "kn_y": kn_y, "kn_r": kn_r, "kn_l": kn_l, "kn_split": kn_split,
        "an_y": an_y, "an_r": an_r, "an_l": an_l, "an_split": an_split,
        "el_y": el_y, "el_r": el_lo, "el_l": el_hi,
        "wr_y": wr_y, "wr_r": wr_lo, "wr_l": wr_hi,
        "area": sum(sum(1 for v in r if v) for r in m),
    }


def keypoints(d):
    """COCO-18 from the measurement. Right/left are the IMAGE's, which is the figure's
    left/right - consistent across every cel, which is all the model needs."""
    top, span = d["top"], d["span"]
    hcx, ncy = d["head_cx"], d["neck_y"]
    head_h = ncy - top + 1
    nose_y = top + head_h * 0.62
    eye_y = top + head_h * 0.48
    ear_y = top + head_h * 0.52
    eye_dx = max(1.2, head_h * 0.16)
    ear_dx = max(1.8, head_h * 0.30)

    sh_half = max(1.5, d["sh_w"] * 0.40)
    hip_half = max(1.2, d["hip_w"] * 0.26)

    k = [None] * 18
    k[0] = (hcx, nose_y)
    k[1] = (d["sh_cx"], d["sh_y"])
    k[2] = (d["sh_cx"] - sh_half, d["sh_y"])
    k[5] = (d["sh_cx"] + sh_half, d["sh_y"])
    k[3] = (d["el_r"], d["el_y"]) if d["el_r"] is not None else None
    k[6] = (d["el_l"], d["el_y"]) if d["el_l"] is not None else None
    k[4] = (d["wr_r"], d["wr_y"]) if d["wr_r"] is not None else None
    k[7] = (d["wr_l"], d["wr_y"]) if d["wr_l"] is not None else None
    k[8] = (d["hip_cx"] - hip_half, d["hip_y"])
    k[11] = (d["hip_cx"] + hip_half, d["hip_y"])
    if d["kn_r"] is not None:
        k[9] = (d["kn_r"], d["kn_y"])
        k[12] = (d["kn_l"], d["kn_y"])
    if d["an_r"] is not None:
        k[10] = (d["an_r"], d["an_y"])
        k[13] = (d["an_l"], d["an_y"])
    k[14] = (hcx - eye_dx, eye_y)
    k[15] = (hcx + eye_dx, eye_y)
    k[16] = (hcx - ear_dx, ear_y)
    k[17] = (hcx + ear_dx, ear_y)

    return k


def draw_pose(k, size, box, thickness):
    """The canonical openpose rendering: additive limb ellipses then joint dots, on black.

    `box` maps the cel's pixel space onto the canvas: (sx, sy, ox, oy)."""
    sx, sy, ox, oy = box
    W, H = size
    img = Image.new("RGB", (W, H), (0, 0, 0))
    lay = Image.new("RGB", (W, H), (0, 0, 0))
    d = ImageDraw.Draw(lay)

    def pt(p):
        return None if p is None else (p[0] * sx + ox, p[1] * sy + oy)

    P = [pt(p) for p in k]

    for i, (a, b) in enumerate(LIMBS):
        if P[a] is None or P[b] is None:
            continue
        x0, y0 = P[a]
        x1, y1 = P[b]
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        ln = math.hypot(x1 - x0, y1 - y0)
        ang = math.degrees(math.atan2(y1 - y0, x1 - x0))
        e = Image.new("RGB", (max(2, int(ln)), max(2, thickness * 2)), (0, 0, 0))
        ed = ImageDraw.Draw(e)
        ed.ellipse([0, 0, e.size[0] - 1, e.size[1] - 1], fill=COLOURS[i])
        e = e.rotate(-ang, expand=True, resample=Image.BICUBIC)
        lay.paste(e, (int(cx - e.size[0] / 2), int(cy - e.size[1] / 2)), e.convert("L").point(lambda v: 255 if v > 8 else 0))

    img = Image.blend(img, lay, 0.6)
    d = ImageDraw.Draw(img)
    for i, p in enumerate(P):
        if p is None:
            continue
        r = thickness
        d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=COLOURS[i % len(COLOURS)])
    return img


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--size", default="512x1024")
    ap.add_argument("--margin", type=float, default=0.06,
                    help="blank fraction above the head and below the feet on the canvas")
    a = ap.parse_args()
    W, H = (int(v) for v in a.size.lower().split("x"))
    os.makedirs(a.out, exist_ok=True)

    manifest = {"size": [W, H], "margin": a.margin, "bodies": {}, "cels": {}}

    for name, body in BODIES.items():
        base = body["base"]
        manifest["bodies"][name] = dict(body, views=[base + i for i in range(4)],
                                        outfits=OUTFITS)

    for name, body in BODIES.items():
        for oi, outfit in enumerate(OUTFITS):
            view = body["base"] + oi
            for loop in range(2):
                for cel in range(8):
                    im = load(view, loop, cel)
                    if im is None:
                        continue
                    d = measure(im)
                    if d is None:
                        continue
                    k = keypoints(d)

                    # The figure is fitted to the canvas by its own HEIGHT, with the same
                    # margin on every cel of every view, so one body's four outfits and one
                    # outfit's four cels all come out at the same scale and the generated
                    # figures are directly comparable - and directly compositable back into
                    # the cel's box.
                    usable = H * (1 - 2 * a.margin)
                    s = usable / d["span"]
                    ox = W / 2 - d["hip_cx"] * s
                    oy = H * a.margin - d["top"] * s

                    img = draw_pose(k, (W, H), (s, s, ox, oy),
                                    max(3, int(round(H / 150))))
                    img.save(os.path.join(a.out, f"pose_{view}_l{loop}_c{cel}.png"))

                    manifest["cels"][f"{view}/{loop}/{cel}"] = {
                        "body": name, "outfit": outfit, "view": view,
                        "loop": loop, "cel": cel,
                        "cel_size": [d["cel_w"], d["cel_h"]],
                        "bbox": [d["top"], d["bottom"], d["span"]],
                        "fit": [s, ox, oy],
                        "keypoints": [None if p is None else [round(p[0], 2), round(p[1], 2)] for p in k],
                        "measure": {kk: (round(vv, 3) if isinstance(vv, float) else vv)
                                    for kk, vv in d.items()},
                    }

    with open(os.path.join(a.out, "poses.json"), "w") as f:
        json.dump(manifest, f, indent=1)
    print(f"{len(manifest['cels'])} cels -> {a.out}")


if __name__ == "__main__":
    main()
