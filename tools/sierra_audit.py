r"""How much of SIERRA'S art survives in each rebuilt building, on BOTH measures.

    python tools/sierra_audit.py [--set cast0] [--factor 12]

THE RULE, AND WHY IT NEEDS TWO NUMBERS
---------------------------------------
Zain: *"None of Sierras stuff is supposed to exist with what we change"*.

That cannot mean zero inside the region's BOUNDING BOX, because a building that fills its
box to all four edges is exactly the hard rectangle section 17 forbids. Her sky above a
roofline and her road below a base are the matte WORKING. So:

    zero of her pixels inside the BUILDING'S OWN SILHOUETTE,
    zero of ours stranded outside the region, and
    INSIDE the region but outside the silhouette, THE PIXELS MUST BE HERS - unless the
    building deliberately generates scenery there, which it then has to declare.

THE THIRD CLAUSE, AND WHY IT WAS MISSING
-----------------------------------------
A slab of a supplied image's own background sat above the Factory's roofline for a whole
install and every check passed. It is not her art inside the silhouette and it is not ours
stranded outside the region: it is OURS, INSIDE THE REGION, OUTSIDE THE SILHOUETTE - the
margin, which we had agreed was correct territory BECAUSE THE MARGIN IS SUPPOSED TO BE HER
SCENERY. Correct for her sky above a roofline; wrong for a slab of screenshot. The audit
could not tell them apart because it only ever asked WHOSE pixels are where, never WHAT
they are.

AND IT IS MEASURED AT THE SHIPPING RUNG, NOT AT 1x. The first version of this check
compared 1x downsamples and found 93 px where the eye found a large rectangle: a supplied
image composited onto a screenshot of the board carries board-COLOURED pixels, so the
difference is small per pixel and a downsample averages it away entirely.

TWO MASKS DECIDE IT, and they are different things:

    `silhouette`  from `surround` - which pixels INSIDE the region are drawn from the
                  generation. Where it is False the installer writes HER pixels:
                  `np.where(m, new, sierra)`. That is the real defect.
    `matte`       from `scenery` (or `matte_file`) - which pixels of the region are pasted
                  onto the board at all. Where it is False her board shows through, which
                  is the intended cel-style cut.

So the number that matters is **her pixels that we PASTED as part of the building**:
`matte & ~silhouette`, as a fraction of the building. The margin figure is reported beside
it so the two are never confused again - the audit this replaces quoted the margin number
and it read as alarming when most of it was sky.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_building as gb
import install_building as ib
from buildings import BUILDINGS

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def _dilate(m, n):
    """Grow a mask by n cel pixels, 4-connected."""
    out = m.copy()
    for _ in range(n):
        p = np.zeros_like(out)
        p[1:] |= out[:-1]
        p[:-1] |= out[1:]
        p[:, 1:] |= out[:, :-1]
        p[:, :-1] |= out[:, 1:]
        out |= p
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--set", default="cast0")
    ap.add_argument("--factor", type=int, default=12)
    ap.add_argument("--cels", default=None)
    a = ap.parse_args()
    F = a.factor

    now = np.asarray(Image.open(os.path.join(ROOT, "assets", "png%dx-%s" % (F, a.set),
                                             "board.png")).convert("RGB")
                     .resize((320, 200), Image.BOX)).astype(int)
    her = np.asarray(Image.open(os.path.join(ROOT, "assets", "png%dx-new" % F, "board.png")
                                ).convert("RGB").resize((320, 200), Image.BOX)).astype(int)
    diff = np.abs(now - her).mean(2)

    # every region, so "stranded" can tell a neighbour's work from an unerased install
    owned = np.zeros((200, 320), bool)
    for b in BUILDINGS.values():
        x, y, w, h = b.region
        owned[y:y + h, x:x + w] = True

    print("%-14s %-13s %9s %9s   %s" % ("building", "region", "IN-SILHO", "margin", "stranded"))
    print("%-14s %-13s %9s %9s   %s" % ("", "", "(the rule)", "(correct)", "outside the region"))
    print("-" * 78)
    rows = []
    for key in sorted(BUILDINGS):
        b = BUILDINGS[key]
        x, y, w, h = b.region
        if not b.init:
            print("%-14s %-13s %9s %9s   %s"
                  % (key, "%dx%d" % (w, h), "no init", "-", "cannot compute"))
            continue
        init = Image.fromarray(gb.paint(b.init, b.palette, w, h), "RGB")
        # THE SILHOUETTE IS KEYED ON HER PIXELS, NOT ON THE INIT, and the first version of
        # this tool got it wrong. For `mode: rects` it makes no difference - the test
        # ignores the image - but `green` and `blue_light` are COLOUR tests, and running
        # them over the init asks whether OUR palette is green rather than whether HERS is.
        # The installer calls `silhouette(b, sierra_region(...), F)`; so does this.
        if a.cels:
            sierra = gb.sierra_region(b, a.cels)
        else:
            raise SystemExit("--cels is required: the silhouette is a test on HER pixels")
        sil = gb.silhouette(b, sierra, 1)
        mf = getattr(b, "matte_file", None)
        if mf:
            p = os.path.join(ROOT, "tools", "samples", "%s-rename" % b.key, mf)
            # A matte_file may be at the FITTED resolution rather than the cel grid -
            # fit_source writes it that way so a keyed roofline does not step in whole
            # cel pixels - so it is brought to the region's own size here.
            mi = Image.open(p).convert("L")
            if mi.size != (w, h):
                mi = mi.resize((w, h), Image.BOX)
            matte = np.asarray(mi) > 127
        elif getattr(b, "scenery", None):
            matte = np.asarray(ib.building_matte(b, init, 1)) > 127
        else:
            matte = np.ones((h, w), bool)      # no matte: the whole region is pasted

        pasted = matte.sum()
        hers_in = int((matte & ~sil).sum())
        hers_margin = int((~matte).sum())
        # stranded: differs from her, outside EVERY region
        band = np.zeros((200, 320), bool)
        band[max(0, y - 14):min(200, y + h + 14), max(0, x - 14):min(320, x + w + 14)] = True
        # A DECLARED `board_fill` MAY REACH OUTSIDE THE REGION and the Factory's does, by
        # four rows, because the thing it paints out stands above the region. Declared is
        # not stranded.
        # AND A NEIGHBOUR'S DECLARED PATCH IS STILL DECLARED. This loop used to read only
        # `b.board_fill`, so Hyper Save's patch - which sits just left of Bare Necessities'
        # region - was invisible to Hyper Save's own row and charged to Bare Necessities
        # as 106 stranded pixels, on a building nobody had touched. A declaration is a
        # statement about the BOARD, not about the building that made it.
        for other in BUILDINGS.values():
            for fr in (getattr(other, "board_fill", None) or []):
                fx0, fy0, fx1, fy1 = fr[:4]
                band[fy0:fy1 + 1, fx0:fx1 + 1] = False
        stranded = int((band & ~owned & (diff > 3)).sum())
        # THE THIRD CLAUSE: in the margin, whose pixels are they? Measured at the SHIPPING
        # RUNG, because a 1x downsample averages away exactly the difference that matters.
        marg = ~matte
        big_now = np.asarray(Image.open(os.path.join(
            ROOT, "assets", "png%dx-%s" % (F, a.set), "board.png")).convert("RGB")).astype(int)
        big_her = np.asarray(Image.open(os.path.join(
            ROOT, "assets", "png%dx-new" % F, "board.png")).convert("RGB")).astype(int)
        bd = np.abs(big_now[y * F:(y + h) * F, x * F:(x + w) * F]
                    - big_her[y * F:(y + h) * F, x * F:(x + w) * F]).mean(2)
        bm = np.kron(marg, np.ones((F, F), bool))
        ours = (bd > 2.0) & bm
        ours_margin = int(ours.sum())
        margin_px = int(bm.sum())
        # A RIM IS NOT A SLAB, and a count cannot tell them apart. Every matte edge carries
        # a thin line of our pixels: the paste is resampled to 12x and the join lands between
        # cel pixels, so the first cel pixel outside the matte is part ours by construction.
        # That is inherent and it is one cel pixel wide. What the clause was written to catch
        # is a SLAB - a supplied image's background standing out in her scenery - so the
        # count is split by distance from the matte edge and only the far part is judged.
        near = _dilate(matte, 3) & marg
        slab = ours & ~np.kron(near, np.ones((F, F), bool))
        # A DECLARED `board_fill` IS OURS ON PURPOSE, and it is declared by RECTANGLE rather
        # than by exempting the whole building. The Factory paints out the regenerated
        # board's own copy of her mill, which stands in its margin where no install could
        # reach it; those rectangles are ours by design and the rest of the margin is still
        # judged. `margin_generated` remains the blunt instrument, and the town centre is
        # still its only customer.
        patch = np.zeros((h, w), bool)
        for fr in (getattr(b, "board_fill", None) or []):
            fx0, fy0, fx1, fy1 = fr[:4]
            patch[max(0, fy0 - y):max(0, fy1 - y + 1), max(0, fx0 - x):max(0, fx1 - x + 1)] = True
        if patch.any():
            slab = slab & ~np.kron(patch, np.ones((F, F), bool))
            print("   %-14s %d cel px of the margin are a DECLARED landscape patch"
                  % (key, int((patch & marg).sum())))
        slab_px = int(slab.sum())
        rows.append((key, hers_in, pasted, hers_margin, w * h, stranded,
                     ours_margin, margin_px,
                     "all" if bool(getattr(b, "margin_generated", False))
                     else ("patch" if patch.any() else False), slab_px))
        print("%-14s %-13s %6d %4.1f%% %6d %4.1f%%   %d px"
              % (key, "%dx%d at %d,%d" % (w, h, x, y),
                 hers_in, 100.0 * hers_in / max(1, pasted),
                 hers_margin, 100.0 * hers_margin / (w * h), stranded))

    print("-" * 78)
    worst = sorted(rows, key=lambda r: -r[1] / max(1, r[2]))
    print("\nCLAUSE 1 - her art INSIDE the silhouette. Worst first:")
    for key, hi, pa, hm, tot, st, om, mp, dec, sl in worst:
        pct = 100.0 * hi / max(1, pa)
        if pct == 0:
            verdict = "CLEAN"
        elif pct < 2:
            verdict = "a fringe - widen `surround`'s keep rects, install-only, no GPU"
        else:
            verdict = "REAL - her art is inside the building"
        print("   %-14s %5.1f%%  %s" % (key, pct, verdict))

    print("\nCLAUSE 3 - OUR pixels in the margin, where HERS should be (at %dx)."
          "\n   `rim` is the join at the matte edge and is inherent; `SLAB` is the"
          " offence.\n" % F)
    print("   %-14s %8s %8s %8s" % ("", "ours", "rim", "SLAB"))
    for key, hi, pa, hm, tot, st, om, mp, dec, sl in sorted(
            rows, key=lambda r: -r[9] / max(1, r[7])):
        pct = 100.0 * sl / max(1, mp)
        if dec == "all":
            verdict = "declared - this building generates its own margins"
        elif dec == "patch" and sl == 0:
            verdict = "CLEAN - a join rim plus a DECLARED landscape patch"
        elif sl == 0:
            verdict = "CLEAN - a join rim only"
        elif pct < 0.5:
            verdict = "specks - look, then decide"
        else:
            verdict = "SLAB: %.1f%% of the margin is ours and should be hers" % pct
        print("   %-14s %8d %8d %8d  %s" % (key, om, om - sl, sl, verdict))


if __name__ == "__main__":
    main()
