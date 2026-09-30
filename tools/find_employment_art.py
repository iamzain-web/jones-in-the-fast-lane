r"""Finds EVERY art surface carrying the Employment Office's name, from the resources.

WHY THIS EXISTS, AND WHY IT IS NOT A LIST
-----------------------------------------
The university rename was reported finished three times while a fourth surface still said
`HI-TECH U`, because the surfaces were enumerated from the list somebody happened to be
holding rather than from the resources. So this tool does not contain a list of files to
check. It takes the two painted forms of the name straight off Sierra's own art, as PIXEL
MASKS, and then searches every 1x view and every cel of pic 11 for them.

    FORM A - the interior title band. Gold lettering on the #205840 green field, measured
             at x 6..175, y 5..10 of `view_706_l0_c0.png` (183x112).
    FORM B - the board building's sign. #D0D8E0 pale lettering on the #C03838 red plate,
             cel 10 of pic 11 (47x34 at (75,151)), rows 2..11.

A mask, not a colour histogram: the green field and the red plate both appear on surfaces
that carry no words at all, so matching the FIELD would report those as hits and matching
the LETTERING is what actually answers the question.

    python tools/find_employment_art.py                  # search the ported 1x art
    python tools/find_employment_art.py --set cast0      # AND assert the set is clean

`--set` is the acceptance check, and it is deliberately built so that it CAN FAIL FOR A
REASON THAT HAS NOT ALREADY BEEN FIXED: it does not ask "did the four files I edited
change", it asks "does Sierra's lettering still appear anywhere in the art this set
resolves to". A fifth surface nobody knew about fails it. A check derived from the fix
would have confirmed the fix and found nothing.
"""

import argparse
import glob
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")
PNG1X = os.path.join(ASSETS, "png")

# --- Form A: the interior title band ------------------------------------------
A_SRC = "view_706_l0_c0.png"
A_X0, A_X1, A_Y0, A_Y1 = 6, 175, 5, 10
FIELD = (0x20, 0x58, 0x40)

# --- Form B: the board building's sign ----------------------------------------
B_CEL = "cel_10_47x34_at_75_151.png"
B_X0, B_X1, B_Y0, B_Y1 = 2, 44, 2, 11
PLATE = (0xC0, 0x38, 0x38)


def goldish(a):
    """Sierra's gold ramp: #D09058 #E0B868 #F8E898 #E0C898. Bright, warm, r >= b."""
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (r > 150) & (g > 120) & (b < 190) & (r >= b) & (a[..., 3] > 0)


def paleish(a):
    """The board sign's lettering: #D0D8E0 and #E0F0F8, pale and slightly blue."""
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (b > 190) & (g > 180) & (r > 170) & (b >= r) & (a[..., 3] > 0)


def load(p):
    return np.asarray(Image.open(p).convert("RGBA"))


def build_masks(celdir):
    a = load(os.path.join(PNG1X, A_SRC))
    ma = goldish(a)[A_Y0:A_Y1 + 1, A_X0:A_X1 + 1]

    mb = None
    cel = os.path.join(celdir, B_CEL) if celdir else None
    if cel and os.path.exists(cel):
        b = load(cel)
        mb = paleish(b)[B_Y0:B_Y1 + 1, B_X0:B_X1 + 1]
    return ma, mb


# How much of the mask has to be present before a surface counts as carrying the name.
# NOT 1.0, and that is the whole point: the first version of this tool demanded an EXACT
# pixel match and reported the art CLEAN while `view_0_l1_c0.png` - the picPatch, which
# repaints the building tops over the character panel and plainly shows the words - sat
# there saying `EMPLOYMENT OFFICE`. It missed it because the picPatch is a separately
# authored VIEW cel, anti-aliased against the trees behind it, so it says the same words
# in not-quite-the-same pixels.
#
# An exact matcher cannot fail for a reason it has not already been shown. A scored one
# can: anything that says the name in ANY hand scores high, and the score is printed for
# every file so a near miss is visible rather than silently absent.
THRESHOLD = 0.70


def score_all(mask, test, img):
    """(best_score, (x, y), hits) for `mask` over `img`.

    THE SCORE IS min(recall, specificity), AND BOTH HALVES ARE LOad-BEARING.

    Recall alone - what fraction of the mask's lettering pixels are present - was the
    second version of this tool and it reported SEVENTY surfaces, because a solid pale
    backdrop contains every pixel of any pale mask you care to slide over it. Specificity
    alone would accept a blank plate. A surface is only saying the word if the lettering
    is there AND THE SPACE AROUND THE LETTERS IS NOT, which is what the pair measures:

        recall      = |lettering INSIDE  the mask| / |mask|
        specificity = |background OUTSIDE the mask| / |box - mask|

    So the first version was too strict (exact match, missed the picPatch) and the second
    too loose (recall only, matched every pale rectangle in the game). This one was
    checked against BOTH known answers before it was trusted: it must find the picPatch,
    and it must not find view 807."""
    if mask is None:
        return 0.0, None, []
    m = test(img)
    mh, mw = mask.shape
    H, W = m.shape
    if H < mh or W < mw:
        return 0.0, None, []
    need = int(mask.sum())
    out_of = mh * mw - need
    if need == 0 or out_of == 0:
        return 0.0, None, []

    # Summed-area table, so the total lettering in each window is O(1) and the only
    # per-offset work is the mask's own pixels.
    ii = np.zeros((H + 1, W + 1), np.int64)
    ii[1:, 1:] = np.cumsum(np.cumsum(m.astype(np.int64), 0), 1)

    best, best_at, hits = 0.0, None, []
    idx = np.argwhere(mask)
    for y in range(H - mh + 1):
        for x in range(W - mw + 1):
            inside = int(m[y + idx[:, 0], x + idx[:, 1]].sum())
            total = int(ii[y + mh, x + mw] - ii[y, x + mw] - ii[y + mh, x] + ii[y, x])
            recall = inside / need
            specificity = 1.0 - (total - inside) / out_of
            s = min(recall, specificity)
            if s > best:
                best, best_at = s, (x, y)
            if s >= THRESHOLD:
                hits.append((x, y, s))
    return best, best_at, hits


def find(mask, test, img, name=None):
    return score_all(mask, test, img)[2]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cels", default=None, help="decoded pic 11 cel dir, for form B")
    ap.add_argument("--set", default=None,
                    help="art set name; also search assets/png{4,12}x-<set> and FAIL on a hit")
    args = ap.parse_args()

    ma, mb = build_masks(args.cels)
    print("form A mask: %dx%d, %d lettering px  (the interior title band)"
          % (ma.shape[1], ma.shape[0], int(ma.sum())))
    if mb is not None:
        print("form B mask: %dx%d, %d lettering px  (the board building's sign)"
              % (mb.shape[1], mb.shape[0], int(mb.sum())))
    else:
        print("form B mask: SKIPPED - pass --cels <dir> from decode_pic_full.py --cels")

    # --- the resource-side sweep of the ported 1x art --------------------------
    print("\nsearching every view in assets/png (threshold %.2f) ..." % THRESHOLD)
    found, near = [], []
    files = sorted(glob.glob(os.path.join(PNG1X, "*.png")))
    for p in files:
        img = load(p)
        for form, mask, test in (("A", ma, goldish), ("B", mb, paleish)):
            best, at, hits = score_all(mask, test, img)
            if hits:
                x, y, s = max(hits, key=lambda h: h[2])
                found.append((os.path.basename(p), form, x, y, s))
                print("  HIT   %-24s form %s at (%3d,%3d)  score %.2f"
                      % (os.path.basename(p), form, x, y, s))
            elif best >= 0.45:
                near.append((os.path.basename(p), form, best))
    print("scanned %d files; %d surface(s) carry the name" % (len(files), len(found)))
    if near:
        # Printed, not hidden: a near miss is either a surface the threshold is wrong for
        # or a coincidence, and both are worth a human's eye.
        print("\nbelow threshold but worth a look:")
        for n, form, s in sorted(near, key=lambda t: -t[2]):
            print("  %-24s form %s  best %.2f" % (n, form, s))
    if not found:
        print("  (none - if you expected some, the mask or the predicate is wrong)")

    # --- the acceptance check --------------------------------------------------
    if not args.set:
        return
    bad = []
    for f in (4, 12):
        d = os.path.join(ASSETS, "png%dx-%s" % (f, args.set))
        if not os.path.isdir(d):
            continue
        # THE SET'S ART IS DOWNSCALED TO 1x AND THE MASK IS LEFT ALONE, rather than the
        # mask being scaled up to the art. Scaling a 170x6 mask by 12 gives a 2040x72
        # window slid over a 2196x1344 image - about 150 million mask lookups per file per
        # form, which does not finish. Downscaling the art is the same comparison at 1/144
        # of the cost, and BOX at an exact integer factor is a clean block mean, so
        # Sierra's lettering survives it intact if it is there at all.
        for p in sorted(glob.glob(os.path.join(d, "*.png"))):
            im = Image.open(p).convert("RGBA")
            if im.width % f or im.height % f:
                print("  skip %s: %dx%d is not a multiple of %d"
                      % (os.path.basename(p), im.width, im.height, f))
                continue
            img = np.asarray(im.resize((im.width // f, im.height // f), Image.BOX))
            for form, mask, test in (("A", ma, goldish), ("B", mb, paleish)):
                if mask is None:
                    continue
                best, at, hits = score_all(mask, test, img)
                if hits:
                    bad.append((p, form))
                    print("  STILL PRESENT  %s  form %s  score %.2f"
                          % (p, form, max(h[2] for h in hits)))
                elif best >= 0.45:
                    print("  near miss      %s  form %s  best %.2f"
                          % (os.path.basename(p), form, best))
    print("\nacceptance: art set '%s' -> %s"
          % (args.set, "CLEAN" if not bad else "%d surface(s) STILL SAY IT" % len(bad)))
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
