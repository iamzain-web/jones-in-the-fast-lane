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

BLACK_CEILING = {280: 20.0, 281: 42.0, 282: 9.0}
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
COVER_FLOOR = {280: 0.55, 281: 0.56, 282: 0.54}
COVER_DEFAULT = 0.54

def measure(path):
    im = np.asarray(Image.open(path).convert("RGBA")).astype(int)
    a = im[:, :, 3] > 200
    if a.sum() == 0:
        return None
    rgb = im[:, :, :3]
    black = 100.0 * (a & (rgb.max(axis=2) < 40)).sum() / a.sum()
    g = rgb[:, :, 1] - np.maximum(rgb[:, :, 0], rgb[:, :, 2])
    green = 100.0 * (a & (g > 25)).sum() / a.sum()
    return black, green, coverage(path, im.shape[0], a)


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
    print("  %-26s %9s %8s %7s  %s" % ("cel", "black%", "green%", "fill", "verdict"))
    for v in views:
        ceil = BLACK_CEILING.get(v, BLACK_DEFAULT)
        for p in sorted(glob.glob(os.path.join(directory, "view_%d_l0_c*.png" % v))):
            m = measure(p)
            if m is None:
                continue
            black, green, fill = m
            floor = COVER_FLOOR.get(v, COVER_DEFAULT)
            why = []
            if black > ceil:
                why.append("black > %.0f%%" % ceil)
            if green > GREEN_CEILING:
                why.append("green > %.0f%%" % GREEN_CEILING)
            if fill is not None and fill < floor:
                why.append("EATEN: fills %.2f < %.2f of the 1990 outline" % (fill, floor))
            if why:
                bad.append((os.path.basename(p), black, green))
            print("  %-26s %8.1f%% %7.1f%% %7s  %s"
                  % (os.path.basename(p)[:26], black, green,
                     "-" if fill is None else "%.3f" % fill,
                     "ok" if not why else "FAIL: " + ", ".join(why)))
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