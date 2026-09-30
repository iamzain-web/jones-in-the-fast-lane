r"""Finds EVERY art surface carrying a given location's painted name, from the resources.

Generalised from `find_employment_art.py`, which was written for one building and found a
fourth surface nobody had listed. The method is unchanged and is the reason it works:

  * the name is taken as a PIXEL MASK off Sierra's own art, never as a list of filenames;
  * a surface matches on min(recall, specificity) - the lettering present AND the space
    around the letters absent. Recall alone matched every pale rectangle in the game (70
    false hits); exact match missed the picPatch, which says the same words in not-quite
    the same pixels because it is a separately authored cel.

Each location declares its forms below. A form is (source image, rectangle, predicate) and
the rectangle is measured off the art, not guessed.

    python tools/find_name_art.py --who factory --cels <celdir>
    python tools/find_name_art.py --who socketcity --cels <celdir> --set cast0

`--set` is the acceptance check and is built to FAIL FOR A REASON NOT ALREADY FIXED: it
asks "does Sierra's lettering still appear anywhere this set resolves to", not "did the
files I edited change". The set's art is downscaled to 1x rather than the mask scaled up -
same comparison, 1/144 of the cost, and BOX at an exact integer factor is a block mean so
the lettering survives it if it is there.
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

THRESHOLD = 0.70


def goldish(a):
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (r > 150) & (g > 120) & (b < 190) & (r >= b) & (a[..., 3] > 0)


def paleish(a):
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (b > 190) & (g > 180) & (r > 170) & (b >= r) & (a[..., 3] > 0)


def darkgreen(a):
    """Sierra's Factory sign ink, #305848: dark, green-dominant."""
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (g > r) & (g > b) & (g < 130) & (r < 110) & (a[..., 3] > 0)


def redish(a):
    """Socket City's script, #C03838 / #E03838: strongly red-dominant."""
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (r > 140) & (g < 100) & (b < 100) & (a[..., 3] > 0)


def greyish(a):
    """The `THE FACTORY` lettering on view 705's chip: pale neutral on a dark body."""
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    mx, mn = np.maximum(np.maximum(r, g), b), np.minimum(np.minimum(r, g), b)
    return (mn > 120) & ((mx - mn) < 40) & (a[..., 3] > 0)


# Each entry: label, source ("cel:<prefix>" or a png in assets/png), rect, predicate.
WHO = {
    "employment": [
        ("A interior title", "view_706_l0_c0.png", (6, 5, 175, 10), goldish),
        ("B board sign", "cel:cel_10_", (2, 2, 44, 11), paleish),
    ],
    "factory": [
        ("A board sign", "cel:cel_11_", (2, 6, 30, 11), darkgreen),
        ("B interior chip", "view_705_l0_c0.png", (26, 12, 143, 20), greyish),
    ],
    "socketcity": [
        ("A board script", "cel:cel_08_", (1, 15, 42, 35), redish),
        ("B interior script", "view_808_l0_c0.png", (70, 2, 180, 20), redish),
    ],
}


def load(p):
    return np.asarray(Image.open(p).convert("RGBA"))


def find_cel(celdir, prefix):
    for n in os.listdir(celdir):
        if n.startswith(prefix):
            return os.path.join(celdir, n)
    return None


def build(who, celdir):
    out = []
    for label, src, (x0, y0, x1, y1), pred in WHO[who]:
        if src.startswith("cel:"):
            if not celdir:
                print("  %-20s SKIPPED - pass --cels" % label)
                continue
            p = find_cel(celdir, src[4:])
            if p is None:
                print("  %-20s SKIPPED - %s* not in %s" % (label, src[4:], celdir))
                continue
        else:
            p = os.path.join(PNG1X, src)
        m = pred(load(p))[y0:y1 + 1, x0:x1 + 1]
        print("  %-20s %dx%d from %-24s %4d lettering px"
              % (label, m.shape[1], m.shape[0], os.path.basename(p), int(m.sum())))
        out.append((label, m, pred))
    return out


def score_all(mask, test, img):
    """(best, at, hits) on min(recall, specificity). See the module docstring."""
    m = test(img)
    mh, mw = mask.shape
    H, W = m.shape
    need = int(mask.sum())
    out_of = mh * mw - need
    if H < mh or W < mw or need == 0 or out_of == 0:
        return 0.0, None, []
    ii = np.zeros((H + 1, W + 1), np.int64)
    ii[1:, 1:] = np.cumsum(np.cumsum(m.astype(np.int64), 0), 1)
    best, at, hits = 0.0, None, []
    idx = np.argwhere(mask)
    for y in range(H - mh + 1):
        for x in range(W - mw + 1):
            inside = int(m[y + idx[:, 0], x + idx[:, 1]].sum())
            total = int(ii[y + mh, x + mw] - ii[y, x + mw] - ii[y + mh, x] + ii[y, x])
            s = min(inside / need, 1.0 - (total - inside) / out_of)
            if s > best:
                best, at = s, (x, y)
            if s >= THRESHOLD:
                hits.append((x, y, s))
    return best, at, hits


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--who", required=True, choices=sorted(WHO))
    ap.add_argument("--cels", default=None)
    ap.add_argument("--set", default=None)
    a = ap.parse_args()

    print("masks for '%s':" % a.who)
    forms = build(a.who, a.cels)
    if not forms:
        raise SystemExit("no masks built")

    print("\nsearching every view in assets/png (threshold %.2f) ..." % THRESHOLD)
    found, near = [], []
    files = sorted(glob.glob(os.path.join(PNG1X, "*.png")))
    for p in files:
        img = load(p)
        for label, mask, pred in forms:
            best, at, hits = score_all(mask, pred, img)
            if hits:
                x, y, s = max(hits, key=lambda h: h[2])
                found.append(os.path.basename(p))
                print("  HIT   %-24s %-18s at (%3d,%3d)  score %.2f"
                      % (os.path.basename(p), label, x, y, s))
            elif best >= 0.45:
                near.append((os.path.basename(p), label, best))
    print("scanned %d files; %d surface(s) carry the name" % (len(files), len(found)))
    if near:
        print("\nbelow threshold, worth an eye:")
        for n, l, s in sorted(near, key=lambda t: -t[2])[:12]:
            print("  %-24s %-18s best %.2f" % (n, l, s))

    if not a.set:
        return
    bad = []
    for f in (4, 12):
        d = os.path.join(ASSETS, "png%dx-%s" % (f, a.set))
        if not os.path.isdir(d):
            continue
        for p in sorted(glob.glob(os.path.join(d, "*.png"))):
            im = Image.open(p).convert("RGBA")
            if im.width % f or im.height % f:
                continue
            img = np.asarray(im.resize((im.width // f, im.height // f), Image.BOX))
            for label, mask, pred in forms:
                best, at, hits = score_all(mask, pred, img)
                if hits:
                    bad.append(p)
                    print("  STILL PRESENT  %dx %-22s %-18s score %.2f"
                          % (f, os.path.basename(p), label, max(h[2] for h in hits)))
    print("\nacceptance: set '%s' -> %s"
          % (a.set, "CLEAN" if not bad else "%d surface(s) STILL SAY IT" % len(bad)))
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
