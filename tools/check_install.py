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

BLACK_CEILING = {280: 20.0, 281: 30.0, 282: 9.0}
BLACK_DEFAULT = 20.0
GREEN_CEILING = 2.0

def measure(path):
    im = np.asarray(Image.open(path).convert("RGBA")).astype(int)
    a = im[:, :, 3] > 200
    if a.sum() == 0:
        return None
    rgb = im[:, :, :3]
    black = 100.0 * (a & (rgb.max(axis=2) < 40)).sum() / a.sum()
    g = rgb[:, :, 1] - np.maximum(rgb[:, :, 0], rgb[:, :, 2])
    green = 100.0 * (a & (g > 25)).sum() / a.sum()
    return black, green

def check(directory, views):
    bad = []
    print("  %-26s %9s %8s  %s" % ("cel", "black%", "green%", "verdict"))
    for v in views:
        ceil = BLACK_CEILING.get(v, BLACK_DEFAULT)
        for p in sorted(glob.glob(os.path.join(directory, "view_%d_l0_c*.png" % v))):
            m = measure(p)
            if m is None:
                continue
            black, green = m
            why = []
            if black > ceil:
                why.append("black > %.0f%%" % ceil)
            if green > GREEN_CEILING:
                why.append("green > %.0f%%" % GREEN_CEILING)
            if why:
                bad.append((os.path.basename(p), black, green))
            print("  %-26s %8.1f%% %7.1f%%  %s"
                  % (os.path.basename(p)[:26], black, green,
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