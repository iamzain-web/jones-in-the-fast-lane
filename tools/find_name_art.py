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


def neonscript(a):
    """Le Security's neon script, which `redish` cannot see.

    HER SCRIPT IS MOSTLY PINK, NOT RED. The band's dominant colours are #E07080 (76 px),
    #E03838 (27) and #C03838 (18) over a dark ground, and `redish` requires g < 100, so it
    rejects the pink that carries most of the lettering: it found 83 pixels in the whole cel
    and a template only 11.5% dense.

    That is not a near miss, it is an INVERTED CHECK, and the measurement that proved it is
    worth keeping: scored against the set where her script is definitely PRESENT the test
    read 0.67-0.70, and against the set where it had been REMOVED it read 0.74. It scored
    HIGHER after her art was gone, because it was never matching her script at all - a
    sparse mask matches whatever red is nearest.

    Red-dominance by DIFFERENCE rather than by an absolute green ceiling catches the whole
    neon: 186 pixels, and a template 43.2% dense over y 8..13."""
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (r > 180) & (r - g > 80) & (r - b > 60) & (a[..., 3] > 0)


def brightgreen(a):
    """QT Clothing's sign green, #48A848 on the board and #38B038 inside: green-dominant
    and NOT dark, which is what separates it from the Factory's #305848 ink.

    These predicates are the one part of this work that has not become data. There are now
    five buildings and five hand-written colour tests, two of which (`darkgreen` here and
    this one) differ only in a brightness threshold. The right shape is a per-building
    colour test in `buildings.py`; it is not done yet, and it is the first thing to fix
    when a sixth building needs a predicate none of these five have."""
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (g > r + 25) & (g > b + 25) & (g > 120) & (a[..., 3] > 0)


def litglyph(a):
    """Anything appreciably lighter than a near-black ground.

    Black's Market's lettering is pale blue-white with blue shading - #E0F0F8 and #98E0F0
    at the highlights but #78A8D0, #507098 and #385880 through the glyph bodies - on a
    #202830 roof. `paleish` catches only the highlights, which made the mask 13% dense and
    reproduced the QT failure exactly: 64 surfaces, and this port's own `BARE NECESSITIES`
    flagged as Sierra's name still present.

    Summed brightness separates them cleanly: the darkest glyph colour sums to 272 and the
    roof to 120. The lesson from QT was about the RECTANGLE; this one is the same lesson
    about the PREDICATE, and the check is the same - a mask has to be dense."""
    s = a[..., 0].astype(int) + a[..., 1].astype(int) + a[..., 2].astype(int)
    return (s > 200) & (a[..., 3] > 0)


def cyanglyph(a):
    """The Pawn Shoppe's BOARD sign ink: #98E0F0 bodies with #E0F0F8 highlights, on her
    #682028 fascia. Pale and blue-dominant, but far too dark in the red channel for
    `paleish`, which catches only the eleven highlight pixels and measures 5.4% - the
    Black's Market failure exactly. `litglyph` goes the other way: her fascia carries
    #507098, #685050 and #786058 mid-tones that all sum past 200, so it measures 72% and
    describes "a band" rather than these glyphs.

    Requiring GREEN as well as BLUE is what separates the ink from the fascia: 42.6% over
    (3,3,36,8), which is mid-window."""
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (b > 150) & (g > 130) & (b >= r) & (a[..., 3] > 0)


def midblue(a):
    """The Pawn Shoppe's INTERIOR plate, view 712: the glyph core #7088E0 only.

    Her plate is black and the letters are fat, so ANY predicate that also takes the
    #98B8E0 highlight and the #507098 shade covers 73-80% of the plate and stops encoding
    letterforms. Excluding both leaves one colour and 44.6% over the plate, which does.

    That makes NINE hand-written colour predicates for SEVEN buildings, and this building
    needed two of them. See the note on `brightgreen`: this really does want to be a
    per-building colour test in `buildings.py`."""
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    return (b > 190) & (b - r > 80) & (g > r) & (a[..., 3] > 0)


def greyish(a):
    """The `THE FACTORY` lettering on view 705's chip: pale neutral on a dark body."""
    r, g, b = a[..., 0].astype(int), a[..., 1].astype(int), a[..., 2].astype(int)
    mx, mn = np.maximum(np.maximum(r, g), b), np.minimum(np.minimum(r, g), b)
    return (mn > 120) & ((mx - mn) < 40) & (a[..., 3] > 0)


# THE RECTANGLES AND THE CHOICE OF PREDICATE LIVE IN tools/buildings.py, not here. Only
# the predicate IMPLEMENTATIONS are code, selected by name from the table, so sizing a mask
# for a new building is a data edit next to everything else about it - and so the rule that
# a mask must be DENSE WITH LETTERING is written down beside the rectangles it governs
# (see `buildings.Detect`). Entries below are the ones that predate the table.
PREDICATES = {}


def _from_table():
    """Build the search spec for any building whose table entry declares `detect`."""
    try:
        from buildings import BUILDINGS
    except ImportError:
        return {}
    out = {}
    for key, b in BUILDINGS.items():
        if not getattr(b, "detect", None):
            continue
        forms = []
        for d in b.detect:
            src = ("cel:cel_%s_" % b.source[4:]) if d.source == "cel" else d.source
            forms.append((d.label, src, d.rect, PREDICATES[d.test]))
        out[key] = forms
    return out


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
    # THE MASK IS THE `CLOTHING` BAND ONLY, and that is not an arbitrary choice.
    #
    # The first version spanned both of her bands, y 2..13, which swept in the dark gap
    # between them. That made the mask mostly background, so the score stopped describing
    # the LETTERFORMS and started describing "some green letters on a dark band" - it
    # reported EIGHTEEN surfaces, including view 706's gold-on-green employers panel, and
    # then flagged this port's own `COMMON THREADS` as Sierra's name still being present,
    # because that is also green lettering in the same place.
    #
    # A mask has to be dense enough to encode the shapes. y 9..13 is her `CLOTHING` band,
    # where the glyphs fill the box, so a different word in the same colour and position
    # scores low. `Q.T` is too small to add anything and only dilutes it.
}

# The predicate implementations stay as code; WHICH one a building uses, and over WHICH
# rectangle, is data. Anything declaring `detect` in tools/buildings.py overrides the
# entries above, and new buildings go there rather than here - so the rule that a mask must
# be dense with lettering sits beside the rectangles it governs.
PREDICATES.update({"goldish": goldish, "paleish": paleish, "darkgreen": darkgreen,
                   "redish": redish, "greyish": greyish, "brightgreen": brightgreen,
                   "neonscript": neonscript,
                   "litglyph": litglyph, "cyanglyph": cyanglyph, "midblue": midblue})
WHO.update(_from_table())


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
        dens = 100.0 * m.sum() / max(1, m.size)
        print("  %-20s %dx%d from %-24s %4d px, %4.1f%% dense"
              % (label, m.shape[1], m.shape[0], os.path.basename(p), int(m.sum()), dens))
        # A MASK OUTSIDE 25-55% IS NOT DESCRIBING LETTERFORMS, and both failure modes have
        # now happened. Too sparse (QT at 13%, Black's Market at 5.7%) and the score
        # describes "letters on a band", matching dozens of surfaces and the port's own
        # replacement text. Too dense (Black's Market's other band at 88-100%) and it
        # matches any bright rectangle. Warned rather than enforced, because a genuinely
        # unusual sign might sit outside it - but it has to be a decision, not an oversight.
        if dens < 25.0 or dens > 55.0:
            print("    WARNING: %.1f%% is outside 25-55%%. %s"
                  % (dens, "Too sparse - this will match any band with letters on it."
                     if dens < 25.0 else
                     "Too dense - this will match any bright rectangle."))
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
