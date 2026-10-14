r"""Fit a SUPPLIED image into a building's region, so "here is a picture, use it" is a
supported route rather than a special case.

    python tools/fit_source.py --who factory
    python tools/fit_source.py --who factory --dry

WHY THIS EXISTS
---------------
Zain supplied his own Factory after three of ours failed: *"I MADE IT because you just never
did a good job!"* He was right. Three passes asked for a sawtooth and got a brick wall, the
chimney was rendered in the same value as the wall behind it, and the building sat small in
its corner for days. His image has the monitor roof, the water tower, the rooftop plant, the
mullioned windows and the loading bay - everything the brief asked for and the pipeline never
produced.

So this is a first-class path, not a one-off. **Nothing downstream changes**: the fitted
image is written to the same `generated_WxH.png` slot a generation would occupy, so
`sign_building.py`, `install_building.py`, the matte, the `clear`, the marble clearance,
`margin_check` and `sierra_audit` all run exactly as they do for a generation.

HOW TO SUPPLY ONE - what helps
-------------------------------
  * **Any resolution at or above the region's own.** The region is `w x h` cel px and ships
    at 12, so 16x the cel is ideal and anything smaller gets upscaled. His was 980x680 for a
    60x44 region, which is 16.3 px per cel px - about right.
  * **Draw the BUILDING, not the scene.** Its pavement, kerb and road are discarded: the
    board already has those and they are Sierra's. `crop` says which part of the supplied
    image is the building.
  * **A sign is optional.** If the image has one it is kept, unless it is too small to read -
    which is measured, not judged: the port's 3x5 set has a 5 cel px cap height, and a sign
    below that cannot be read at the smallest rung. `cover` paints a band out with the
    image's own material so `sign_board` can stamp over it.
  * **Everything else still applies.** `no_text_guard` runs here, because a supplied image
    can carry lettering anywhere - a plaque by a loading bay, a street sign, a number on a
    door - and the band-by-band look at native 12x is the real acceptance either way.

THE FIT, STATED
---------------
  1. crop the supplied image to `crop`, which is the building without its ground
  2. scale so its width is exactly the region's width
  3. bottom-align so its base lands on `baseline`, a game row - the board supplies the
     ground below that
  4. anything above the scaled image inside the region is left as the init painted it
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_building as gb
from buildings import BUILDINGS

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def fit(b, F):
    """The supplied image, cropped, scaled and placed, at F px per cel px."""
    spec = b.source_image
    src = Image.open(spec["path"]).convert("RGB")
    cx0, cy0, cx1, cy1 = spec["crop"]
    crop = src.crop((cx0, cy0, cx1, cy1))
    _, ry, rw, rh = b.region[0], b.region[1], b.region[2], b.region[3]
    k = (rw * F) / crop.width
    sw, sh = rw * F, max(1, int(round(crop.height * k)))
    scaled = crop.resize((sw, sh), Image.LANCZOS)

    # the canvas starts as the init, so anything the image does not cover is authored
    base = Image.fromarray(gb.paint(b.init, b.palette, rw, rh), "RGB").resize(
        (rw * F, rh * F), Image.NEAREST)
    base_row = spec.get("baseline", b.region[1] + rh - 1)
    top = (base_row - b.region[1] + 1) * F - sh
    base.paste(scaled, (0, top))
    return base, k, top, sh, scaled


def cover(img, b, F, k, top):
    """Paint a band out with the image's OWN material, TILED from a plain patch.

    For a supplied sign too small to read. The first version copied one block from an
    offset and DUPLICATED A FEATURE - the Factory's monitor roof appeared twice, because
    eight rows above the sign is roof, not wall. So the source is a small patch of the
    plainest material in the image, found by variance, and it is tiled with alternate
    columns mirrored so the repeat does not read.

        (x0, y0, x1, y1, sx0, sy0, sx1, sy1)   destination, then the source patch

    `sign_board` then stamps over it. The same move `clear_copy` makes on the board, with
    the lesson that the source has to be plain or the patch is worse than the problem."""
    a = np.asarray(img, np.uint8).copy()
    for op in b.source_image.get("cover", []):
        x0, y0, x1, y1, sx0, sy0, sx1, sy1 = op
        D = a[y0 * F:(y1 + 1) * F, x0 * F:(x1 + 1) * F]
        src = a[sy0 * F:(sy1 + 1) * F, sx0 * F:(sx1 + 1) * F]
        if src.size == 0:
            raise SystemExit("cover source is empty: %s" % (op,))
        sh_, sw_ = src.shape[:2]
        for j in range(0, D.shape[0], sh_):
            for i in range(0, D.shape[1], sw_):
                t = src if ((i // sw_) % 2 == 0) else src[:, ::-1]
                hh = min(sh_, D.shape[0] - j)
                ww = min(sw_, D.shape[1] - i)
                D[j:j + hh, i:i + ww] = t[:hh, :ww]
        print("  covered cel x %d..%d y %d..%d, tiled from the plain patch at x %d..%d "
              "y %d..%d (alternate columns mirrored)"
              % (x0, x1, y0, y1, sx0, sx1, sy0, sy1))
    return Image.fromarray(a, "RGB")


def smooth(img, b, F):
    """Rebuild a band of the SUPPLIED image from its own surroundings, by diffusion.

    `cover` tiles a plain patch, and that is right for a flat wall. It is wrong for a
    surface with a gradient across it: the General Dealer's crown is an oval, dark at both
    ends and bright in the middle, and the plainest patch of it by variance measures
    #101F2F against a centre of #203451 - tiling that over the middle leaves a dark block.
    The first attempt sourced the patch from eight rows below the lettering, which there is
    the gold braid and the GD crest, and tiled THOSE across the whole crown.

    So the band is reconstructed instead, from the pixels either side of it, with
    `install_building.landscape_fill` - the same diffusion that paints out the regenerated
    board's buildings. With `sides="lr"` each row interpolates between its own left and
    right neighbours, so the crown's horizontal gradient and its gold piping both come back
    through the band rather than being averaged away.

        (x0, y0, x1, y1, sides, ring, feather)   region-local cel coords; the last three
                                                 are optional

    AND THE BANDS OVERLAP, with a small feather, for the reason the board's own patches do:
    a feathered mask leaves the first and last row of a band partly original, so butted
    bands keep a line of the thing being removed - here the tops of his capitals came back
    as a red ghost across the crown, twice, because the upper band's ring sampled the lower
    band's own un-opaque edge.
    """
    import install_building as ib
    for op in b.source_image.get("smooth", []):
        x0, y0, x1, y1 = op[:4]
        sides = op[4] if len(op) > 4 else "lr"
        ring = op[5] if len(op) > 5 else 2
        feather = op[6] if len(op) > 6 else 0.5
        img = ib.landscape_fill(img, (x0, y0, x1, y1), F, sides=sides, ring=ring,
                                feather=feather)
        print("  smoothed cel x %d..%d y %d..%d from its own %s neighbours "
              "(ring %d, feather %.2f)" % (x0, x1, y0, y1, sides, ring, feather))
    return img


def clip_matte(b, F, canvas_size):
    """His outline, AUTHORED, because nothing derived from his pixels can find it.

    Four methods were tried and all failed for one reason, which is the finding:

        HIS PICTURE IS A COMPOSITE OF THE BOARD, so its background IS the board - the same
        colours and the same texture. A colour flood cannot separate them: seeded from his
        own top corners the reference is (94,110,97) and the background above the roofline
        samples at (139,192,117), (130,151,137) and (88,96,87), which is 35 to 82 away
        against a tolerance of 34, and a tolerance wide enough eats his brick. A DETAIL test
        cannot either: his composite is a picture of a screen, so the "flat" board carries
        as much local variance as the building and the roofline came back as row zero at
        every threshold from 7 to 32.

        And no diff against her art can find it either, which is why it survived every check
        we had and had to be spotted by eye.

    So the outline is READ OFF HIS IMAGE AND WRITTEN DOWN, as a stepped profile of rects.
    It is deterministic, it cannot be defeated by his palette, and it is the honest answer:
    a supplied composite has no recoverable alpha, so somebody has to say where the building
    is. Everything outside it is restored from her board by `clear`.

    The real fix is upstream and is in BUILDINGS.md 38: ask for a plain flat background.
    """
    _, _, rw, rh = b.region
    m = np.zeros((canvas_size[1], canvas_size[0]), np.uint8)
    # THE COORDINATES MAY BE FRACTIONAL, and on a curved building they have to be. The
    # General Dealer's crown is an oval whose shoulders fall a whole cel row per column:
    # authored on the cel grid its outline staircases in 12-px steps at the shipping rung,
    # which is the same fault section 38b records for writing the matte at the cel grid
    # instead of the fitted resolution. A quarter-cel step is 5 px at scale 20.
    for (x0, y0, x1, y1) in b.source_image.get("clip", []):
        m[int(round(y0 * F)):int(round((y1 + 1) * F)),
          int(round(x0 * F)):int(round((x1 + 1) * F))] = 255
    print("  outlined by an AUTHORED stepped profile of %d rects: %.1f%% of the region"
          % (len(b.source_image.get("clip", [])), 100.0 * (m > 0).mean()))
    return Image.fromarray(m, "L")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--who", required=True, choices=sorted(BUILDINGS))
    ap.add_argument("--dry", action="store_true")
    ap.add_argument("--reviewed", default=None)
    a = ap.parse_args()

    b = BUILDINGS[a.who]
    if not getattr(b, "source_image", None):
        raise SystemExit("%s has no `source_image` in the table" % b.key)
    _, _, rw, rh = b.region
    F = b.scale
    out = os.path.join(ROOT, "tools", "samples", "%s-rename" % b.key)
    os.makedirs(out, exist_ok=True)

    img, k, top, sh, scaled = fit(b, F)
    print("%s <- %s" % (b.new_name, os.path.basename(b.source_image["path"])))
    print("  region %dx%d cel at (%d,%d); fitting at %d px per cel px"
          % (rw, rh, b.region[0], b.region[1], F))
    print("  crop %s -> scaled by %.4f -> %dx%d px = %d x %.1f cel px"
          % (b.source_image["crop"], k, rw * F, sh, rw, sh / F))
    print("  bottom-aligned: its base at game y %d, its top at game y %.1f"
          % (b.source_image.get("baseline", b.region[1] + rh - 1),
             b.region[1] + top / float(F)))
    img = cover(img, b, F, k, top)
    img = smooth(img, b, F)

    p = os.path.join(out, "generated_%dx%d.png" % (rw * F, rh * F))
    if a.dry:
        print("--dry: not written")
        return
    img.save(p)
    print("  wrote %s - the slot a generation would occupy, so nothing downstream changes"
          % os.path.basename(p))

    if b.source_image.get("clip"):
        mk = clip_matte(b, F, (rw * F, rh * F))
        mp = os.path.join(out, "matte_%dx%d.png" % (rw * F, rh * F))
        # AT THE FITTED RESOLUTION, not the cel grid. Written at 60x44 and thresholded,
        # the keyed edge stepped in whole cel pixels - twelve device px at the shipping
        # rung - and his roofline read as a staircase. The installer resizes it down.
        mk.save(mp)
        print("  wrote %s (%dx%d, the fitted resolution) - set `matte_file` to it"
              % (os.path.basename(mp), mk.width, mk.height))

    # THE GUARD RUNS ON A SUPPLIED IMAGE TOO. It can carry lettering anywhere.
    import no_text_guard
    ex = None
    if b.sign:
        sx0, sy0, sx1, sy1 = b.sign["rect"]
        ex = (sx0 * F, sy0 * F, (sx1 + 1) * F - 1, (sy1 + 1) * F - 1)
    hits = no_text_guard.check(img, F, ex)
    if hits and not a.reviewed:
        raise SystemExit("REFUSING: lettering outside the sign region. Look at it at native "
                         "resolution and re-run with --reviewed \"<what you saw>\".")
    if hits:
        print("  OVERRIDDEN BY REVIEW: %s" % a.reviewed)


if __name__ == "__main__":
    main()
