r"""Look at the MARGIN round a rebuilt building, not just the region that was generated.

    python tools/margin_check.py --who factory [--margin 14] [--set cast0]

WHY THIS EXISTS
---------------
Two defects shipped in one round and BOTH were outside the generated region: a strip of
the previous install left at x 69..74 when the region narrowed, and a four-column sliver of
Sierra's own bank framed inside our own left edge. A check scoped to the region cannot see
either - it is the same shape as the band check whose chunks were cut on the tile lines, and
the same shape as every guard failure on this project: the sampling avoided the suspect area.

WHAT IT REPORTS
---------------
  1. Every pixel OUTSIDE the region that differs from the ported Sierra art. Outside the
     region nothing of ours should exist, so any difference is either an earlier install we
     failed to erase or a neighbour's work - and it says which by naming the rectangle.
  2. Every pixel INSIDE the region that is still HERS - i.e. inside the region's box but
     outside the building's matte. That is the number the "zero of her pixels" rule is
     about, and it is reported per side so an inset edge shows up as a frame.
  3. A native-resolution crop with the region's box drawn on it, so the margin can be
     LOOKED at rather than only measured.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_building as gb
import install_building as ib
from buildings import BUILDINGS

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--who", required=True, choices=sorted(BUILDINGS))
    ap.add_argument("--set", default="cast0")
    ap.add_argument("--margin", type=int, default=14)
    ap.add_argument("--factor", type=int, default=12)
    ap.add_argument("--out", default=None)
    a = ap.parse_args()

    b = BUILDINGS[a.who]
    rx, ry, w, h = b.region
    F = a.factor
    now_im = Image.open(os.path.join(ROOT, "assets", "png%dx-%s" % (F, a.set), "board.png")
                        ).convert("RGB")
    now = np.asarray(now_im.resize((320, 200), Image.BOX)).astype(int)
    her = np.asarray(Image.open(os.path.join(ROOT, "assets", "png%dx-new" % F, "board.png")
                                ).convert("RGB").resize((320, 200), Image.BOX)).astype(int)

    print("%s: region x %d..%d y %d..%d" % (b.key, rx, rx + w - 1, ry, ry + h - 1))

    # 1. OUTSIDE the region
    inside = np.zeros((200, 320), bool)
    inside[ry:ry + h, rx:rx + w] = True
    d = np.abs(now - her).mean(2)
    mx0, my0 = max(0, rx - a.margin), max(0, ry - a.margin)
    mx1, my1 = min(320, rx + w + a.margin), min(200, ry + h + a.margin)
    band = np.zeros((200, 320), bool)
    band[my0:my1, mx0:mx1] = True
    outside = band & ~inside
    # A DECLARED `board_fill` may reach above the region on purpose - the Factory's does,
    # because the thing it paints out stands above the region - so it is named rather than
    # reported as "an install not erased".
    declared = np.zeros((200, 320), bool)
    for fr in (getattr(b, "board_fill", None) or []):
        fx0, fy0, fx1, fy1 = fr[:4]
        declared[fy0:fy1 + 1, fx0:fx1 + 1] = True
    if (declared & outside).any():
        print("  declared landscape patch outside the region: %d px (board_fill)"
              % int((declared & outside & (d > 3)).sum()))
        outside = outside & ~declared
    hits = outside & (d > 3)
    print("  OUTSIDE the region, within %d px: %d of %d px differ from png%dx-new"
          % (a.margin, hits.sum(), outside.sum(), F))
    if hits.sum():
        ys, xs = np.where(hits)
        print("     at x %d..%d  y %d..%d  (a neighbour's work, or an install not erased)"
              % (xs.min(), xs.max(), ys.min(), ys.max()))
        for yy in sorted(set(ys))[:12]:
            r = xs[ys == yy]
            print("       y %3d  x %3d..%-3d  (%d px)" % (yy, r.min(), r.max(), len(r)))

    # 2. INSIDE the region but outside the matte - HER pixels, which the rule says is zero
    #
    # A BUILDING WITH A `matte_file` HAS ITS OUTLINE IN THAT FILE, not in its init, and
    # reading the init for it reports a matte nobody uses: the General Dealer's authored
    # clip keeps 83.2% of its region and its init would have said 97%. `sierra_audit`
    # already prefers the file; this did not.
    mf = getattr(b, "matte_file", None)
    if mf:
        p = os.path.join(ROOT, "tools", "samples", "%s-rename" % b.key, mf)
        mi = Image.open(p).convert("L")
        if mi.size != (w, h):
            mi = mi.resize((w, h), Image.BOX)
        m = np.asarray(mi) > 127
        hers = (~m).sum()
        print("  INSIDE the region: %d of %d px are outside the matte = %.1f%% still HERS"
              " (from %s)" % (hers, w * h, 100.0 * hers / (w * h), mf))
        for name, sl in (("top", (slice(0, 1), slice(None))),
                         ("bottom", (slice(h - 1, h), slice(None))),
                         ("left", (slice(None), slice(0, 1))),
                         ("right", (slice(None), slice(w - 1, w)))):
            edge = ~m[sl]
            print("     %-7s edge: %d of %d px are hers" % (name, edge.sum(), edge.size))
    elif b.init:
        m = np.asarray(ib.building_matte(
            b, Image.fromarray(gb.paint(b.init, b.palette, w, h), "RGB"), 1)) > 127
        hers = (~m).sum()
        print("  INSIDE the region: %d of %d px are outside the matte = %.1f%% still HERS"
              % (hers, w * h, 100.0 * hers / (w * h)))
        for name, sl in (("top", (slice(0, 1), slice(None))),
                         ("bottom", (slice(h - 1, h), slice(None))),
                         ("left", (slice(None), slice(0, 1))),
                         ("right", (slice(None), slice(w - 1, w)))):
            edge = ~m[sl]
            print("     %-7s edge: %d of %d px are hers" % (name, edge.sum(), edge.size))
    else:
        print("  INSIDE the region: no init in the table - this building predates the "
              "extraction and its matte cannot be computed here")

    # 3. look at it
    crop = now_im.crop((mx0 * F, my0 * F, mx1 * F, my1 * F))
    dr = ImageDraw.Draw(crop)
    dr.rectangle([(rx - mx0) * F, (ry - my0) * F,
                  (rx - mx0 + w) * F - 1, (ry - my0 + h) * F - 1],
                 outline=(255, 0, 0), width=2)
    p = a.out or os.path.join(ROOT, "tools", "samples", "%s_margin_%dx.png" % (b.key, F))
    crop.save(p)
    print("  wrote %s - NATIVE %dx, the red box is the region" % (os.path.basename(p), F))


if __name__ == "__main__":
    main()
