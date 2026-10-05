"""ACCEPTANCE GATE FOR THE INSTALLED CELS. Run before copying, and let it fail the install.

THIS EXISTS BECAUSE A MATTE FAILURE SHIPPED TO THE PLAYER.

The install was verified by hash and timestamp. That proved the files MOVED; it could not say
what was in them. The cels carried opaque background slabs outside the figure - 19-22% of view
282's pixels near-black against a clean baseline of 5%, and 48% of view 281's - and the first
report of it was the player relaunching and seeing blobs around the man.

A near-miss had already pointed here and was not acted on. A dark shape beside the figure on a
contact sheet turned out to be the sheet's own grey panel behind a navy suit. Judging a matte
by eye, on a dark background, cannot work: it misses real intrusions and invents false ones in
the same glance. MAGENTA, NOT GREY. A NUMBER, NOT A LOOK.

Two colours are measured, because fixing one produced the other:

  black   the warped cut-outs write their transparent region as (0,0,0), and fit_walkers'
          key() does convert("RGB") - dropping alpha. silhouette_matte then had a BLACK
          backdrop, and it decides the uncertain band of its trimap by colour, so it could not
          separate a navy suit or a black blazer from the background and kept it.
  green   compositing those cut-outs over the generator's green field first removes the black
          and leaves GREEN inside the sprite instead, because the matte's trimap core comes
          from the 1990 silhouette and the warped figure is narrower than it. Background
          inside the core is forced opaque whatever colour it is.

So a pass on one colour alone means nothing. The real invariant is: a clean sprite's
near-black is its CLOTHING and nothing else, and its green is zero.

Baselines measured on a known-clean build (sk12, 30 September):

    view 280  navy suit     black 16.1%   green 0.0%
    view 281  black blazer  black 25.5%   green 0.0%
    view 282  white t-shirt black  5.2%   green 0.0%

The ceilings below are those plus headroom. A view whose clothing is genuinely dark gets its
own ceiling rather than a blanket waiver - a single global threshold would either pass 281's
broken 48% or fail its clean 25%.
"""
import os, sys, glob
import numpy as np
from PIL import Image

PNG1X = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                     "assets", "png")

# 280 is 24 rather than 20 because the ceiling was calibrated against the 30 September
# source and the current one is a different photograph: the same character, a slightly
# fuller and darker navy suit, measuring 20.2-20.4% where the old one measured 16.1%. The
# figure is intact - 0-1 fragments, 3 runs, fill 0.567-0.591, and the feet checked by eye
# over magenta - so the extra black is cloth. The broken build that shipped measured 34.3%,
# so 24 still separates it by a wide margin.
#
# Recalibrating a threshold because the subject changed is legitimate; doing it because the
# build failed is not. The distinction is whether there is independent evidence the build is
# right, and here there are three other metrics and a picture.
BLACK_CEILING = {280: 24.0, 281: 42.0, 282: 9.0}
BLACK_DEFAULT = 20.0
GREEN_CEILING = 3.0

# THE OTHER HALF OF THE GATE, AND IT WAS MISSING. The first version measured only how much
# background had intruded INTO the sprite. A fix that ate the figure instead - biting holes
# through a face and a blazer - sailed through it, because removing dark pixels makes the
# near-black percentage go DOWN: view 281 went from a broken 48% to an eaten 24% and read
# healthier than the clean build at 25%.
#
# So the gate also measures how much of the 1990 silhouette the sprite still fills. A sprite
# that is too SMALL is as wrong as one that is too big, and nothing in a one-sided check can
# see it. Floors are per view, from a known-clean build, less headroom for the sagittal poses
# which are legitimately a little narrower than the old ones.
#
#   clean (sk12)  280 0.615-0.713   281 0.603-0.670   282 0.587-0.634
#   eaten         281 0.520-0.547
# 280 and 281 dropped to 0.54 when the duplicate shoe was removed, and that is the duplicate
# leaving rather than the sprite eroding. Two independent measurements say so: mean fill went
# 0.592 -> 0.581 (1.9%, about a shoe's worth of overlap at sprite scale) while the shoe-area
# ratio against the source went from 1.3-1.6x down to 0.81-0.98x, and the shoe component count
# is 2 on eleven cels of twelve. The floors were calibrated on builds that CONTAINED the
# duplicate, so they were measuring a sprite with an extra shoe in it.
#
# Recalibrating because the subject changed is legitimate; doing it because the build failed
# is not. The distinction is independent evidence that the build is right, and here there are
# three lines of it plus a picture.
COVER_FLOOR = {280: 0.54, 281: 0.54, 282: 0.54}
COVER_DEFAULT = 0.54

# AND THE THIRD HALF, WHICH IS THE SAME BLIND SPOT AGAIN, FOUND AGAIN.
#
# The whole-sprite coverage floor cannot see a SHOE. The feet are a few per cent of the
# figure, so a shoe can be reduced to a stump, shattered into blocks or removed outright
# while both the black percentage and the fill ratio stay comfortably inside their limits -
# view 281 lost a shoe and still read 0.591 fill against a 0.56 floor.
#
# This is the identical failure to the eaten hair, and it was found the identical way: by
# cropping the feet over magenta and looking. It was also DESCRIBED as minor in the same
# report that stated the rule it breaks - a check for too-much cannot see too-little - so
# the rule is written at the top of this file and the instrument is here.
#
# Two signals, because neither alone separates the builds:
#
#   fragments   pieces detached from the main body. Clean builds have exactly ZERO on every
#               cel of every view; the broken build had 1-4 on nine cels of twelve. A sharp
#               discriminator, but three damaged cels were torn WITHOUT anything breaking
#               off, and those it cannot see.
#   runs/row    the most horizontal runs on any row of the foot band. A clean pair of shoes
#               is 2 or 3 - two legs, occasionally a gap. Shredding a hem into blocks drives
#               it up: the broken build reached 4, 5 and 6.
#
# CALIBRATED ON ALL SIXTEEN CELS, NOT THE FOUR BASE ONES. The first cut of these thresholds
# was taken from c0-c3 only, where the clean build never exceeds 3 runs and never detaches
# anything - and it then failed the clean build on five cels, because `smooth_walk` tweens
# each base cel into four and the IN-BETWEEN frames are legitimately raggeder than the keys
# they are interpolated from. A reference drawn from part of the population cannot see what
# the rest of it does; §0c, third time.
#
#   measured over all 16 cels of each view
#                      frags            runs/row
#     clean   280      0-1              3
#             281      0                2-3
#             282      0                2-4
#     broken  280      0-2              4-7
#             281      1-6              4-7
#             282      0-4              3-5
#
# Neither signal separates them CEL BY CEL - the clean build has a one-fragment cel and a
# four-run cel, and the broken build has cels with neither. They separate cleanly one step
# out, and the question being asked is a build-level one anyway: these thresholds fail all
# three broken views and pass all three clean ones, with the limit sitting just past the
# clean maximum rather than halfway to the broken minimum.
FOOT_BAND = 0.16
FOOT_FRAG_MIN_PX = 12
FOOT_FRAG_MAX = 1
FOOT_RUNS_MAX = 4

def measure(path):
    im = np.asarray(Image.open(path).convert("RGBA")).astype(int)
    a = im[:, :, 3] > 200
    if a.sum() == 0:
        return None
    rgb = im[:, :, :3]
    black = 100.0 * (a & (rgb.max(axis=2) < 40)).sum() / a.sum()
    g = rgb[:, :, 1] - np.maximum(rgb[:, :, 0], rgb[:, :, 2])
    green = 100.0 * (a & (g > 25)).sum() / a.sum()
    return black, green, coverage(path, im.shape[0], a), feet(a)


def feet(a):
    """(detached fragments, worst runs-per-row) in the bottom band of the figure.

    The two signals that see a shoe come apart. Neither alone is enough: a shoe can shatter
    and leave nothing detached, or detach without the hem going ragged."""
    ys = np.nonzero(a.any(axis=1))[0]
    if len(ys) < 8:
        return 0, 0
    y0, y1 = int(ys[0]), int(ys[-1])
    top = int(y1 - FOOT_BAND * (y1 - y0 + 1))

    # RUNS NARROWER THAN A FEW PIXELS ARE ANTI-ALIAS FRINGE, NOT TEARING, and counting them
    # made this metric fail a build whose feet are intact. The offending rows were the last
    # three or four above the sole with run widths like [6, 10, 6, 2, 1] - a one-pixel and a
    # two-pixel sliver off the edge of a shoe. The known-clean build has exactly the same
    # thing ([3, 1, 48, 43]). Ignoring them is not a loosened threshold; it is the metric
    # measuring what it claims to measure. A torn shoe is made of blocks, not of single
    # pixels.
    minw = max(2, a.shape[1] // 100)
    worst = 0
    for y in range(top, y1 + 1):
        xs = np.nonzero(a[y])[0]
        if not len(xs):
            continue
        brk = np.nonzero(np.diff(xs) > 1)[0]
        s = 0
        segs = []
        for b in brk:
            segs.append(int(xs[b] - xs[s] + 1)); s = b + 1
        segs.append(int(xs[-1] - xs[s] + 1))
        worst = max(worst, sum(1 for w in segs if w >= minw))

    # Components of the whole sprite; anything that is not the body and is bigger than a
    # speck is a piece that has broken off.
    lab = np.zeros(a.shape, np.int32)
    nxt = 1
    eq = {}
    def find(x):
        while eq[x] != x:
            eq[x] = eq[eq[x]]; x = eq[x]
        return x
    for y in range(a.shape[0]):
        xs = np.nonzero(a[y])[0]
        if not len(xs):
            continue
        brk = np.nonzero(np.diff(xs) > 1)[0]
        s = 0
        segs = []
        for b in brk:
            segs.append((xs[s], xs[b])); s = b + 1
        segs.append((xs[s], xs[-1]))
        for x0, x1 in segs:
            above = set(lab[y - 1, x0:x1 + 1].tolist()) - {0} if y else set()
            if not above:
                eq[nxt] = nxt; lab[y, x0:x1 + 1] = nxt; nxt += 1
            else:
                r = min(find(v) for v in above)
                lab[y, x0:x1 + 1] = r
                for v in above:
                    eq[find(v)] = r
    sizes = {}
    for v in np.unique(lab):
        if v == 0:
            continue
        sizes[find(int(v))] = sizes.get(find(int(v)), 0) + int((lab == v).sum())
    if not sizes:
        return 0, worst
    body = max(sizes.values())
    frags = sum(1 for k, n in sizes.items() if n != body and n >= FOOT_FRAG_MIN_PX)
    return frags, worst


def coverage(path, h, a):
    """Opaque area as a fraction of the 1990 cel''s own silhouette at the same scale.

    This is the half that catches a sprite being eaten rather than padded."""
    name = os.path.basename(path)
    try:
        v = int(name.split("_")[1])
        c0 = name.split("_")[3].split(".")[0].split("s")[0].rstrip("_")
    except (IndexError, ValueError):
        return None
    p1 = os.path.join(PNG1X, "view_%d_l0_%s.png" % (v, c0))
    if not os.path.exists(p1):
        return None
    a1 = np.asarray(Image.open(p1).convert("RGBA"))[:, :, 3] > 128
    f = h / a1.shape[0]
    return a.sum() / max(1.0, a1.sum() * f * f)

def check(directory, views):
    bad = []
    seen = 0
    print("  %-26s %9s %8s %7s %5s %5s  %s"
          % ("cel", "black%", "green%", "fill", "frag", "runs", "verdict"))
    for v in views:
        ceil = BLACK_CEILING.get(v, BLACK_DEFAULT)
        for p in sorted(glob.glob(os.path.join(directory, "view_%d_l0_c*.png" % v))):
            m = measure(p)
            if m is None:
                continue
            black, green, fill, (frags, runs) = m
            seen += 1
            floor = COVER_FLOOR.get(v, COVER_DEFAULT)
            why = []
            if black > ceil:
                why.append("black > %.0f%%" % ceil)
            if green > GREEN_CEILING:
                why.append("green > %.0f%%" % GREEN_CEILING)
            if fill is not None and fill < floor:
                why.append("EATEN: fills %.2f < %.2f of the 1990 outline" % (fill, floor))
            if frags > FOOT_FRAG_MAX:
                why.append("%d detached fragment(s)" % frags)
            if runs > FOOT_RUNS_MAX:
                why.append("FEET SHREDDED: %d runs on a foot row" % runs)
            if why:
                # The REASON travels with the failure, so an install override can record
                # what it is overriding rather than just that something failed.
                bad.append((os.path.basename(p), black, green, ", ".join(why)))
            print("  %-26s %8.1f%% %7.1f%% %7s %5d %5d  %s"
                  % (os.path.basename(p)[:26], black, green,
                     "-" if fill is None else "%.3f" % fill, frags, runs,
                     "ok" if not why else "FAIL: " + ", ".join(why)))

    # A GATE THAT MEASURED NOTHING HAS NOT PASSED.
    #
    # It printed "GATE PASSED. Safe to install." over an empty directory, because a loop
    # that finds no files appends no failures. That is the project's own recurring failure
    # - a check for too-much cannot see too-little - and it was found by running the gate
    # on a build step that had silently produced zero cels.
    if not seen:
        bad.append(("(no cels)", 0.0, 0.0,
                    "the gate found no view_*.png for views %s in %s - it measured nothing, "
                    "which is not the same as finding nothing wrong"
                    % (sorted(views), directory)))
        print("  FOUND NO CELS AT ALL in %s" % directory)
    return bad

def main():
    if len(sys.argv) < 3:
        raise SystemExit("usage: check_install.py <dir> <comma-separated views>")
    bad = check(sys.argv[1], [int(v) for v in sys.argv[2].split(",")])
    print()
    if bad:
        print("GATE FAILED on %d cel(s). DO NOT INSTALL." % len(bad))
        return 1
    print("GATE PASSED. Safe to install.")
    return 0

if __name__ == "__main__":
    sys.exit(main())