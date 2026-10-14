"""Write a building's silhouette as a matte at the GENERATION resolution, so a curved
roofline can step at a fraction of a cel instead of a whole one.

    python tools/crown_mask.py --who socketcity
    python tools/crown_mask.py --who socketcity --write
    python tools/crown_mask.py --survey          # who else has the same fault

WHY THIS EXISTS

`install_building.building_matte` builds the silhouette from the `init` boxes: every box
whose palette key is not in `scenery` is building, and the cel-resolution result is then
`np.kron`-ed up to the shipping rung. That is exact, and it is exact IN CEL TERMS ONLY.
A flat roof loses nothing to it. A CURVE loses everything:

    Watt a Bargain's dome, authored as stepped init boxes, came out as EIGHT STEPS with a
    12-to-36 px tread and a 36 px riser at 12x. It reads as a ziggurat, not a roofline.

The General Dealer hit the same wall from the other direction and the answer there was to
author its outline at the generation resolution - BUILDINGS.md 44, "authored on the cel
grid its outline staircases in 12-px steps at the shipping rung". This is that answer
applied to a roofline: a profile, because a roofline is a function of x, sampled in cel
units in `buildings.py` and interpolated here at 1/scale of a cel.

WHAT IT IS NOT

It is not a smoothing filter and it does not invent a shape. The samples are MEASURED off
Sierra's own board, column by column, and written down - the same discipline as the
General Dealer's `clip`. Interpolating between her own measurements is the only thing this
file does that she did not.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from buildings import BUILDINGS                                   # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def init_silhouette(b):
    """The silhouette `building_matte` would build, as (top, bottom) per cel column."""
    _, _, w, h = b.region
    solid = np.zeros((h, w), bool)
    for (bx0, by0, bx1, by1, key) in b.init:
        solid[by0:by1 + 1, bx0:bx1 + 1] = key not in (b.scenery or ())
    out = []
    for c in range(w):
        ys = np.nonzero(solid[:, c])[0]
        out.append((int(ys.min()), int(ys.max())) if len(ys) else None)
    return out


def worst_riser(b):
    """The tallest single step in this building's authored roofline, in CEL rows.

    Only the TOP edge is measured. A vertical side wall is not a step - the fault this
    looks for is a curve approximated by whole-cel treads, and that only shows along an
    outline that is supposed to be sloping.
    """
    _, _, w, h = b.region
    prof = init_silhouette(b)
    tops = [p[0] if p else None for p in prof]
    # A SIDE WALL IS NOT A STEP, and the first version of this counted them: Watt a
    # Bargain's pavement runs two columns wider than its building, so the jump from
    # pavement-only (top 50) to building (top 9) read as a 41-row riser. Columns whose
    # only content is down at the baseline are not part of the roofline.
    tops = [t if (t is not None and t < h - 4) else None for t in tops]
    run = [i for i, t in enumerate(tops) if t is not None]
    if not run:
        return 0, 0, []
    steps = []
    for i in range(run[0], run[-1]):
        a, c = tops[i], tops[i + 1]
        if a is None or c is None:
            continue
        if a != c:
            steps.append((i + 1, abs(c - a), 1 if c > a else -1))

    # A SLOPE, NOT A CORNER. One big drop in one column is a vertical wall and buildings
    # have those; what reads as a staircase is a SLOPING edge cut into whole-cel treads.
    #
    # AND A CORNER STANDING NEXT TO A STAIRCASE IS STILL A CORNER, which the first version
    # of this got wrong and which over-reported two buildings by a factor of six. Bare
    # Necessities' outline steps 12 rows between columns 0 and 1 and then 2, 2, 2: the 12
    # is the vertical left wall of the shopfront and looks like a wall, and only the 2s are
    # treads. Needful Things is the same shape - an 8-row wall beside a 3-row tread.
    #
    # So: group the steps into runs going the same way, and inside each run count a step as
    # a TREAD only while it is no more than TWICE the smallest step in that run. The
    # worst tread is the number that predicts what the silhouette looks like.
    runs, cur = [], []
    for st in steps:
        if cur and (st[2] != cur[-1][2] or st[0] - cur[-1][0] > 4):
            runs.append(cur); cur = []
        cur.append(st)
    if cur:
        runs.append(cur)
    tread, corner = 0, 0
    for r in runs:
        if len(r) < 2:
            corner = max(corner, max(d for _, d, _ in r))
            continue
        lo = min(d for _, d, _ in r)
        for _, d, _ in r:
            if d <= 2 * lo:
                tread = max(tread, d)
            else:
                corner = max(corner, d)
    return (max((d for _, d, _ in steps), default=0), len(steps), steps, tread, corner)


def profile_at(b, gw):
    """Her roofline, interpolated across `gw` generated columns, in CEL rows."""
    _, _, w, h = b.region
    samples = sorted(b.crown)
    xs = np.array([c + 0.5 for c, _ in samples], float)
    ys = np.array([t for _, t in samples], float)
    # the centre of each generated column, in cel units
    gx = (np.arange(gw) + 0.5) * (w / float(gw))
    return np.interp(gx, xs, ys), samples


def build(b, scale=None):
    _, _, w, h = b.region
    s = scale or b.scale
    gw, gh = w * s, h * s
    prof = init_silhouette(b)
    curve, samples = profile_at(b, gw)

    m = np.zeros((gh, gw), np.uint8)
    lo, hi = min(c for c, _ in samples), max(c for c, _ in samples)
    for gxi in range(gw):
        cx = int((gxi + 0.5) * w / float(gw))          # which cel column this falls in
        p = prof[cx]
        if p is None:
            continue
        top_cel, bot_cel = p
        # INSIDE the authored span the curve governs; OUTSIDE it the init still does.
        # Watt a Bargain's pavement runs two columns wider than its building at the very
        # bottom, and a profile has nothing to say about that - it is not a roofline.
        top = curve[gxi] if lo <= cx <= hi else float(top_cel)
        y0 = int(round(top * s))
        y1 = int(round((bot_cel + 1) * s))
        m[max(0, y0):min(gh, y1), gxi] = 255
    return Image.fromarray(m, "L"), curve, samples


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--who")
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--survey", action="store_true")
    ap.add_argument("--suggest", action="store_true",
                    help="print `crown` samples that split this init's own treads")
    a = ap.parse_args()

    if a.suggest:
        # THE UNBIASED LINE THROUGH A STAIRCASE PASSES THROUGH ITS RISERS' MIDPOINTS.
        #
        # The first crowns written for Bare Necessities and Common Threads pinned the line
        # at the TOP of each tread instead, and the margin check caught it: Bare
        # Necessities' margin went DOWN, from 221 px of her board to 217. A line pinned at
        # the top of the treads rides above the staircase for most of its length, so the
        # building quietly got bigger - exactly the direction a silhouette fix must never
        # go. Splitting each riser is neutral by construction: half of every tread is
        # given back and half is taken.
        b = BUILDINGS[a.who]
        _, _, w, h = b.region
        tops = [p[0] if p and p[0] < h - 4 else None for p in init_silhouette(b)]
        pts = []
        for i in range(len(tops) - 1):
            p, q = tops[i], tops[i + 1]
            if p is None or q is None or p == q:
                continue
            pts.append((i + 1.0, (p + q) / 2.0))        # the riser's own x, both values' mean
        print("%s: riser midpoints, as (x, row)" % a.who)
        for x, t in pts:
            print("    x %5.1f  row %5.2f   -> sample (%g, %g)" % (x, t, x - 0.5, t))
        print("\n`crown` takes (col, row) and reads col as col+0.5, so subtract a half.")
        return 0

    if a.survey:
        print("WHOSE OUTLINE COMES FROM `init` BOXES, AND HOW BADLY IT STEPS")
        print("A riser is a jump in the roofline between two adjacent cel columns. At the")
        print("12x rung a riser of N cel rows is a 12N px vertical jog in the silhouette.\n")
        print("  %-13s %-22s %s" % ("building", "silhouette from", "worst TREAD in a staircase"))
        rows = []
        for k in sorted(BUILDINGS):
            b = BUILDINGS[k]
            if getattr(b, "matte_file", None):
                rows.append((k, "an authored matte", None, 0, 0, 0))
                continue
            if not getattr(b, "scenery", None):
                rows.append((k, "no matte - full rect", None, 0, 0, 0))
                continue
            d, n, steps, tread, corner = worst_riser(b)
            rows.append((k, "`init` boxes", d, n, tread, corner))
        rows.sort(key=lambda r: (-(r[4] or 0), -(r[2] or 0), r[0]))
        for row in rows:
            k, how, d, n = row[0], row[1], row[2], row[3]
            tread, corner = (row[4], row[5]) if len(row) > 5 else (0, 0)
            if d is None:
                print("  %-13s %-22s -" % (k, how))
            else:
                print("  %-13s %-22s %2d rows = %3d px   (%d steps; the worst plain CORNER, "
                      "which is not a fault, is %d rows)" % (k, how, tread, tread * 12, n, corner))
        return 0

    b = BUILDINGS[a.who]
    if not b.crown:
        d, n, steps, tread, corner = worst_riser(b)
        print("%s declares no `crown`. Its roofline steps %d times, worst tread %d cel rows."
              % (a.who, n, tread))
        return 2
    img, curve, samples = build(b)
    _, _, w, h = b.region
    print("%s -> %s" % (b.sierra, b.new_name))
    print("  region %dx%d, matte at %dx%d (%d px per cel px)"
          % (w, h, img.width, img.height, b.scale))
    print("  %d authored samples, cols %d..%d" % (len(samples), samples[0][0], samples[-1][0]))
    d, n, _, tread, _ = worst_riser(b)
    jumps = np.abs(np.diff(curve))
    print("  the init's roofline: %d steps, worst TREAD %d cel rows = %d px at 12x"
          % (n, tread, tread * 12))
    print("  this matte's:        worst %.3f cel rows = %.1f px at 12x, mean %.4f"
          % (jumps.max(), jumps.max() * 12, jumps.mean()))
    print("  coverage %.1f%% of the region" % (100.0 * (np.asarray(img) > 127).mean()))
    if a.write:
        out = os.path.join(ROOT, "tools", "samples", "%s-rename" % b.key)
        p = os.path.join(out, "matte_%dx%d.png" % (img.width, img.height))
        img.save(p)
        print("  wrote %s - set `matte_file` to matte_%dx%d.png"
              % (p, img.width, img.height))
    else:
        print("  --write to save it")
    return 0


if __name__ == "__main__":
    sys.exit(main())
