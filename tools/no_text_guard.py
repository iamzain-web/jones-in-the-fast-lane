r"""Fail the build when the model has written on the building.

    python tools/no_text_guard.py --who pawnshop
    python tools/no_text_guard.py --image <png> --scale 16 [--exclude x0,y0,x1,y1]

WHY THIS EXISTS, AND WHY A BIGGER MASK IS NOT THE ANSWER
--------------------------------------------------------
The programme's rule was "generate with the sign band blank so the model never sees text".
It held for six buildings. Then the Pawn Shoppe's facade came back sharp - and the moment
the model had enough freedom to render detail, it wrote **PADEMOKIK BATEWEIS ILP** across
the band BELOW the sign, where nothing was masked.

So the rule was never "the model will not generate text". It was "the model will not
generate text WHERE WE TOLD IT NOT TO" - a guard that covers the place we were looking,
which is the shape of every guard failure in this project.

AND THE NEGATIVE PROMPT IS NOT THE ANSWER EITHER, which is measured rather than assumed.
`NEGATIVE` already opens with `text, letters, words, writing, lettering, signage, numbers,
logo, watermark`; decoding the token ids back shows all nine arrive intact at 68/77, well
inside CLIP's window. They were present, they were not truncated, and the model wrote words
anyway. Adding a tenth synonym is not a fix.

What is left is a detector that looks at the whole frame and fails the build. Unlike a
mask, it fails on the thing it is named for.

HOW IT DECIDES, AND WHAT IT DELIBERATELY WILL NOT DO
-----------------------------------------------------
No OCR, and no attempt to read anything. A row of text has a structure that a building
does not:

  * many small high-contrast blobs against a locally uniform ground;
  * sitting on a COMMON BASELINE;
  * of similar height;
  * but of VARYING width and spacing.

The last one is what separates lettering from the things that would otherwise fire it -
balusters, railings, window mullions, roof tiles, a row of panes. Those are small, aligned
and similar height too, and they are also all the SAME WIDTH and evenly spaced. Letters are
not. The width-variation test is the whole difference between this and a guard that cries
wolf on every balcony.

Calibrated on four real frames from this project, which is the only reason the thresholds
mean anything - two that must fire and two that must not:

    0.92/0.30 facade, `PADEMOKIK BATEWEIS ILP`   FAIL  17 blobs, 2.1 cel px pitch
    0.90/0.15 facade, garbled fascia             FAIL  22 blobs, 1.6 cel px pitch
    0.84/0.55 facade, clean                      pass
    0.74/0.18 facade, clean                      pass

The first version of the pitch rule did not exist and the guard fired on the window rows of
BOTH clean facades. A guard that fails on clean work gets switched off, so it would have
been worse than nothing.

RUN IT AT THE GENERATION'S OWN RESOLUTION. The same gibberish frame at half size passes:
the blobs fall under the minimum glyph height and vanish. This is a real limitation, it is
stated rather than papered over, and it is why `gen_building.py` calls it on the full
`generated_WxH.png` and never on a downscale.
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def _label(m):
    """8-connected component labelling. Hand-written because scipy is not installed here
    and one import is not worth a dependency on a 4GB box that already fights for RAM."""
    h, w = m.shape
    lab = np.zeros((h, w), np.int32)
    cur = 0
    stack = []
    for sy in range(h):
        row = m[sy]
        for sx in range(w):
            if not row[sx] or lab[sy, sx]:
                continue
            cur += 1
            stack.append((sy, sx))
            lab[sy, sx] = cur
            while stack:
                y, x = stack.pop()
                y0, y1 = max(0, y - 1), min(h, y + 2)
                x0, x1 = max(0, x - 1), min(w, x + 2)
                for ny in range(y0, y1):
                    for nx in range(x0, x1):
                        if m[ny, nx] and not lab[ny, nx]:
                            lab[ny, nx] = cur
                            stack.append((ny, nx))
    return lab, cur


def ink(img, S):
    """High-contrast small features against a locally uniform ground."""
    g = np.asarray(img.convert("L"), np.float32)
    bg = np.asarray(img.convert("L").filter(ImageFilter.GaussianBlur(S * 1.2)), np.float32)
    return np.abs(g - bg) > 26.0


def cx_of(boxes, grp):
    """Blob centre x for a candidate row."""
    return np.array([(boxes[j][0] + boxes[j][2]) / 2.0 for j in grp], float)


def rows_of_glyphs(img, S, exclude=None, verbose=True):
    """Every horizontal run of letter-like blobs on a common baseline."""
    m = ink(img, S)
    if exclude is not None:
        x0, y0, x1, y1 = exclude
        m[y0:y1 + 1, x0:x1 + 1] = False
    lab, n = _label(m)
    boxes = []
    for i in range(1, n + 1):
        ys, xs = np.nonzero(lab == i)
        if len(ys) < 8:
            continue
        bh, bw = ys.max() - ys.min() + 1, xs.max() - xs.min() + 1
        # a glyph at this scale: between about half and three cel pixels tall, and taller
        # than it is wide or nearly so. A long horizontal smear is a moulding, not a letter.
        if not (0.55 * S <= bh <= 3.2 * S):
            continue
        if bw > 1.7 * bh or bw < 0.12 * bh:
            continue
        if len(ys) < 0.12 * bh * bw:
            continue
        boxes.append((xs.min(), ys.min(), xs.max(), ys.max()))

    hits = []
    used = set()
    for i, (ax0, ay0, ax1, ay1) in enumerate(boxes):
        if i in used:
            continue
        base = ay1
        grp = [j for j, (_, _, _, by1) in enumerate(boxes)
               if abs(by1 - base) <= 0.28 * S and j not in used]
        if len(grp) < 5:
            continue
        hs = np.array([boxes[j][3] - boxes[j][1] + 1 for j in grp], float)
        ws = np.array([boxes[j][2] - boxes[j][0] + 1 for j in grp], float)
        xs0 = np.sort(np.array([boxes[j][0] for j in grp], float))
        span = xs0[-1] - xs0[0]
        # SIMILAR HEIGHTS, VARYING WIDTHS. The second half is what keeps a balcony railing
        # and a row of window panes out of this: they are as aligned as letters and as
        # uniform in height, and they are all the same width.
        if hs.std() > 0.38 * hs.mean():
            continue
        if ws.std() < 0.16 * ws.mean():
            continue
        if span < 5.0 * S:
            continue
        # PITCH IS WHAT ACTUALLY SEPARATES LETTERING FROM ARCHITECTURE, and it was found by
        # calibrating rather than by reasoning. The first version required only 5 aligned
        # blobs of similar height and varying width, and it fired on the window rows of two
        # perfectly clean facades. Measured on the frames to hand:
        #
        #   PADEMOKIK BATEWEIS ILP   17 blobs over 35.9 cel px -> 2.1 cel px pitch
        #   a second garbled row     22 blobs over 35.7 cel px -> 1.6 cel px pitch
        #   a row of window panes      5 blobs over 29.4 cel px -> 5.9 cel px pitch
        #   another row of panes       5 blobs over 28.4 cel px -> 5.7 cel px pitch
        #
        # Letters are PACKED and windows are not. 3.3 cel px of pitch sits between the two
        # clusters with room on both sides.
        if len(grp) < 6 or span / (len(grp) * float(S)) > 3.3:
            continue
        # A CAP LINE AS WELL AS A BASELINE. Letters sit between two roughly parallel lines;
        # the blobs share a bottom AND a top. A row of standing seams on a slate roof
        # shares neither - it is a set of parallel ridges running at an angle, and it got
        # through every test above (9 blobs, 3.2 cel px pitch, width variation 0.23) on the
        # Factory. Requiring the tops to line up as tightly as the bottoms rejects it.
        tops = np.array([boxes[j][1] for j in grp], float)
        if tops.std() > 0.45 * S:
            continue
        # REGULAR SPACING. A line of text is a run of closely and fairly evenly spaced
        # glyphs; its biggest gap is a word space, two or three times the median. The
        # Factory's slate roof produced nine blobs whose centres ran
        # 12, 40, 81, 117, 153, 174, 249, 433, 486 - gaps with a median of 36 and a maximum
        # of 184, a factor of five. Scattered architectural features are not a sentence.
        gaps = np.diff(np.sort(cx_of(boxes, grp)))
        if len(gaps) and np.median(gaps) > 0 and gaps.max() > 4.0 * np.median(gaps):
            continue
        for j in grp:
            used.add(j)
        cx = np.sort(np.array([(boxes[j][0] + boxes[j][2]) / 2.0 for j in grp], float))
        hits.append({"n": len(grp), "baseline": base, "span": span,
                     "x0": int(xs0[0]), "x1": int(xs0[-1]), "cx": cx,
                     "h_mean": float(hs.mean()), "w_cv": float(ws.std() / ws.mean())})

    # A GRID IS NOT A SENTENCE. The Factory's three courses of multi-pane windows fired
    # every test above - ten or eleven small blobs, aligned, similar height, 2.7 cel px of
    # pitch - because a mullioned window IS a row of packed rectangles. What it is not is
    # UNIQUE: the panes repeat vertically in the SAME COLUMNS, course after course, and a
    # line of writing does not. Two rows whose blob centres line up in x are a window grid.
    #
    # Two lines of a name are safe from this because their letters do not share columns:
    # `THE INDUSTRIAL` over `REVOLUTION` are different words of different widths.
    def columns_agree(a, bb):
        if len(a) == 0 or len(bb) == 0:
            return 0.0
        return float(np.mean([np.min(np.abs(bb - c)) <= 0.35 * S for c in a]))

    grid = set()
    for i in range(len(hits)):
        for j in range(i + 1, len(hits)):
            if abs(hits[i]["baseline"] - hits[j]["baseline"]) < 0.5 * S:
                continue
            if (columns_agree(hits[i]["cx"], hits[j]["cx"]) >= 0.6
                    and columns_agree(hits[j]["cx"], hits[i]["cx"]) >= 0.6):
                grid.add(i)
                grid.add(j)
    if grid and verbose:
        print("    (%d row(s) dropped as a repeating grid - panes, not letters)" % len(grid))
    hits = [h for i, h in enumerate(hits) if i not in grid]
    if verbose:
        for h in hits:
            print("    glyph row: %2d blobs on baseline y=%d (cel y %.1f), span %.0f px "
                  "(%.1f cel px), mean height %.1f px, width variation %.2f"
                  % (h["n"], h["baseline"], h["baseline"] / S, h["span"], h["span"] / S,
                     h["h_mean"], h["w_cv"]))
    return hits


def check(img, S, exclude=None, label=""):
    print("  no-text guard%s: scanning %dx%d at %d px per cel px"
          % (" (%s)" % label if label else "", img.width, img.height, S))
    hits = rows_of_glyphs(img, S, exclude)
    if hits:
        print("    FAIL: %d row(s) of letter-like structure outside the sign region"
              % len(hits))
    else:
        print("    pass: no letter-like row found outside the sign region")
    return hits


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--who")
    ap.add_argument("--image")
    ap.add_argument("--scale", type=int, default=16)
    ap.add_argument("--exclude", default=None, help="x0,y0,x1,y1 in CEL coords")
    a = ap.parse_args()

    if a.who:
        from buildings import BUILDINGS
        b = BUILDINGS[a.who]
        _, _, w, h = b.region
        p = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                         "tools", "samples", "%s-rename" % b.key,
                         "generated_%dx%d.png" % (w * b.scale, h * b.scale))
        S = b.scale
        ex = None
        if b.sign:
            x0, y0, x1, y1 = b.sign["rect"]
            ex = (x0 * S, y0 * S, (x1 + 1) * S - 1, (y1 + 1) * S - 1)
    else:
        p, S = a.image, a.scale
        ex = None
        if a.exclude:
            x0, y0, x1, y1 = [int(v) for v in a.exclude.split(",")]
            ex = (x0 * S, y0 * S, (x1 + 1) * S - 1, (y1 + 1) * S - 1)

    hits = check(Image.open(p).convert("RGB"), S, ex, os.path.basename(p))
    sys.exit(1 if hits else 0)


if __name__ == "__main__":
    main()

