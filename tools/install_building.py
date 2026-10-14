r"""One installer for every renamed board building. Differences live in tools/buildings.py.

    python tools/install_building.py --who socketcity --cels <dir> --set cast0
    python tools/install_building.py --who socketcity --cels <dir> --set cast0 --device bulb
    python tools/install_building.py --who socketcity --cels <dir> --set cast0 --verify

WHAT IT WRITES, AND WHERE
-------------------------
Into `assets/png{4,12}x-<set>` only, and it takes the SET's own files as its base so an
earlier building's work is not undone - rebuilding from the ported art is what silently
reverted the university's picPatch fix once.

    board.png, pic_11.png   the building, blended by silhouette, then the name
    <each surface>          any interior the detector found carrying the name

BOTH RUNGS CARRY EVERY FILE. `RenderScale` takes the largest factor at which the set
exists, so a 4x set silent about a file the 12x set replaces shows 1990 art whenever the
game settles on 4x.

LETTERING
---------
The 3x5 bitmap at 1x, where the plate is too small for anything else, and a REAL TYPEFACE
at 4x and 12x. At 12x a sign plate is hundreds of pixels across and stamping a block font
there spends that resolution reproducing 1990's staircase - which is what "I don't want it
to look pixelated, it's 2026 not 1986" was about. The model never sees text at any scale.
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_building as gb
import install_employment as ie          # the glyph set, text_mask and composite_text
from buildings import BUILDINGS

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")


def letter_region(img, lines, rect, ink, F, fill=0.88):
    """Write `lines` into `rect` (region-local, 1x) at factor F. Typeface if available,
    the 3x5 bitmap otherwise, so a machine without the fonts still produces correct art."""
    x0, y0, x1, y1 = [v * F for v in rect[:2]] + [rect[2] * F + F - 1, rect[3] * F + F - 1]
    bw, bh = x1 - x0 + 1, y1 - y0 + 1
    mask = ie.text_mask(lines, bw, bh, fill=fill)
    if mask is not None:
        return ie.composite_text(img, mask, (x0, y0), lambda r, h: ink), (x0, y0, bw, bh)
    sx = sy = max(1, min(bw // max(1, max(ie.tw(l) for l in lines)),
                         bh // (len(lines) * 6)))
    ty = y0 + (bh - (len(lines) * 6 - 1) * sy) // 2
    for i, ln in enumerate(lines):
        ie.stamp_scaled(img, ln, x0 + (bw - ie.tw(ln) * sx) // 2, ty + i * 6 * sy,
                        sx, sy, lambda ry, dy, s: ink)
    return img, (x0, y0, bw, bh)


def building_matte(b, region, F):
    """An alpha matte of the building, snapped to the edge the model actually drew.

    Three steps, and the middle one is the point:

      1. The AUTHORED silhouette. Every `init` box whose palette key is not in `scenery`
         is building. That is exact in cel terms but only in cel terms - a 12x upscale of
         it staircases in 12-pixel steps, and the real roofline is finer than that.
      2. SNAP. Inside a band of +/-1.5 cel px around that outline, decide per pixel by
         colour: nearer the background the init painted there, or nearer what the building
         rendered as. This is the only part that looks at the generated image, and it only
         ever moves the edge by a pixel or two - a colour matte cannot key a figure against
         a same-hue backdrop (CAST.md 4), but it can refine an edge that is already known.
      3. ONE PIXEL of antialiasing. Not a feather. The join is meant to be visible as a
         clean edge, the way a cel's is.
    """
    _, _, w, h = b.region
    # IN ORDER, AND SCENERY BOXES CUT. This loop used to only ever set True, so a PARK box
    # painted over the shed to make a sawtooth notch left the matte solid underneath and
    # the roofline came out as a flat rectangle. `paint()` composites the init in order and
    # so must this, or the matte is not the silhouette the init describes.
    solid = np.zeros((h, w), bool)
    for (bx0, by0, bx1, by1, key) in b.init:
        solid[by0:by1 + 1, bx0:bx1 + 1] = key not in b.scenery

    # NO COLOUR KEYING. It was tried: the init paints the surround in two known colours, so
    # keying everything far from them looked like the right move. It punched holes straight
    # through the brick, because mortar and stone sit near the background grey - which is
    # precisely the failure CAST.md 4 describes. The authored silhouette is exact, and the
    # right place to improve the outline is the init, not a matte that second-guesses it.
    M = np.kron(solid, np.ones((F, F), np.uint8)) * 255
    # ONE pixel of antialiasing. Not a feather.
    return Image.fromarray(M, "L").filter(ImageFilter.GaussianBlur(0.6))


def check_clear_covers_region(b):
    """THE ERASER MUST COVER ITS OWN REGION, and this has now been got wrong three times.

    `clear` is the rectangle restored from her ported board before anything is pasted. If it
    is smaller than the region in any direction, the rows it misses keep whatever the PREVIOUS
    install left there - and every check we had still passed, because the audit asked whose
    pixels were inside the silhouette and whether any had escaped the region, and a leftover
    INSIDE the region but OUTSIDE the matte is neither.

    The Factory's `clear` began at game y 151 while its region began at y 149, so a two-row
    band of a supplied image's background survived every reinstall, untouched, invisible to
    everything except the eye. A rule nobody can forget is better than a rule written down,
    so this refuses rather than warns.
    """
    x, y, w, h = b.region
    cx0, cy0, cx1, cy1 = b.clear
    if cx0 <= x and cy0 <= y and cx1 >= x + w - 1 and cy1 >= y + h - 1:
        return
    raise SystemExit(
        "%s: `clear` is %s but the region is x %d..%d, y %d..%d. The eraser must cover the\n"
        "whole region or the rows it misses keep the last install's pixels for ever."
        % (b.key, b.clear, x, x + w - 1, y, y + h - 1))


def pick_clear_source(ported, F, board, clear):
    """Which ported rung directory this set's board was actually built from.

    Compared on an 8-px frame just OUTSIDE the rectangle about to be cleared, because that
    is art neither this install nor any earlier one has touched. Returns the image, or
    raises - a mismatched patch is worse than no patch, and silently picking the first
    candidate is how the first two attempts both produced a visible rectangle."""
    x0, y0, x1, y1 = clear
    fx0, fy0 = max(0, x0 - 8), max(0, y0 - 8)
    fx1, fy1 = min(319, x1 + 8), min(199, y1 + 8)
    cur = np.asarray(board.convert("RGB")).astype(int)
    frame = np.zeros(cur.shape[:2], bool)
    frame[fy0 * F:(fy1 + 1) * F, fx0 * F:(fx1 + 1) * F] = True
    frame[y0 * F:(y1 + 1) * F, x0 * F:(x1 + 1) * F] = False
    # AND EVERY OTHER BUILDING'S OWN FOOTPRINT COMES OUT OF THE COMPARISON. The first
    # version did not do this, and the 8px frame around the Factory reached into Hire
    # Ground's cel at x 75 - which this programme has already replaced, so no ported set
    # could ever match it and the check refused everything. A control area has to be art
    # nobody has touched.
    for other in BUILDINGS.values():
        ox, oy, ow, oh = other.region
        frame[oy * F:(oy + oh) * F, ox * F:(ox + ow) * F] = False
        if getattr(other, "clear", None):
            kx0, ky0, kx1, ky1 = other.clear
            frame[ky0 * F:(ky1 + 1) * F, kx0 * F:(kx1 + 1) * F] = False
        # AND EVERY DECLARED LANDSCAPE PATCH, for the same reason. `board_fill` writes
        # OUTSIDE the region by design - it is how Sierra's own building gets removed where
        # ours does not cover it - so it is our work sitting in what this function treats as
        # untouched control area. Hyper Save found it: its fill reaches eight rows above its
        # region, and on the SECOND install the frame contained the FIRST install's fill, no
        # ported set matched (best 5.88 against 0.00) and the tool refused.
        #
        # The alternative was to widen that building's `clear` to cover the fill, and that
        # is worse: the rectangle needed would have reached three columns into Bare
        # Necessities and erased part of a neighbour every time the Bank was installed.
        # A patch we declare is a patch this check already knows is ours.
        for (px0, py0, px1, py1, *_rest) in getattr(other, "board_fill", None) or []:
            frame[py0 * F:(py1 + 1) * F, px0 * F:(px1 + 1) * F] = False
    if frame.sum() < 500:
        raise SystemExit("  not enough untouched board left to identify the clear source")

    root = os.path.dirname(ported)
    best, bestd, tried = None, None, []
    for name in ("png%dx-new" % F, "png%dx-uni" % F, "png%dx" % F):
        p = os.path.join(root, name, "pic_11.png")
        if not os.path.exists(p):
            continue
        im = Image.open(p).convert("RGB")
        if im.size != board.size:
            continue
        d = float(np.abs(np.asarray(im).astype(int) - cur)[frame].mean())
        tried.append((name, d))
        if bestd is None or d < bestd:
            best, bestd = im, d
    print("  clear source candidates: "
          + ", ".join("%s %.2f" % (n, d) for n, d in tried))
    if best is None or bestd > 0.5:
        raise SystemExit("  no ported set matches this board outside the clear rect "
                         "(best %.2f) - refusing to paste a mismatched patch"
                         % (bestd if bestd is not None else -1))
    return best


def landscape_fill(board, rect, F, sides="tlr", ring=2, feather=0.5):
    """Rebuild a rectangle of the board's own LANDSCAPE, from the landscape around it.

    WHY THIS IS NOT `clear_copy`. `clear_copy` copies a patch of her art from an offset,
    and it is the right tool when a patch of the same thing exists somewhere nearby. At the
    Factory there is none: the rectangle to be painted out is 42x17 cel px of hillside that
    runs from green at its left edge to grey yard at its right, and no rectangle of that
    size anywhere on the board carries that transition. Every candidate source either
    contains the thing being painted out, a neighbouring building, or the board's frame.

    So the patch is CONSTRUCTED rather than copied, exactly as `grime.skyfill` constructs a
    sky instead of borrowing one, and for the reason recorded there: a gradient matches the
    board and a fill does not. The interior is solved by diffusion from a ring of the
    board's own pixels just outside the rectangle - every colour in the result is one the
    board already has, interpolated - at CEL resolution, then resampled up. Smooth by
    construction, deterministic, and no model is involved.

    `sides` names which edges constrain it. The BOTTOM is excluded by default because at
    the moment this runs the board below the rectangle still carries the very thing being
    removed, and a ring that samples the defect would paint the defect back in faintly.
    """
    x0, y0, x1, y1 = rect
    w, h = x1 - x0 + 1, y1 - y0 + 1
    # work at cel resolution: the surround here is smooth, and solving at 12x buys nothing
    # but time. The upsample at the end is what makes it continuous.
    gx0, gy0 = max(0, x0 - ring), max(0, y0 - ring)
    gx1, gy1 = min(319, x1 + ring), min(199, y1 + ring)
    big = board.crop((gx0 * F, gy0 * F, (gx1 + 1) * F, (gy1 + 1) * F))
    small = np.asarray(big.resize((gx1 - gx0 + 1, gy1 - gy0 + 1), Image.BOX)).astype(np.float32)
    H, W = small.shape[:2]
    known = np.zeros((H, W), bool)
    if "t" in sides:
        known[: y0 - gy0, :] = True
    if "b" in sides:
        known[y1 - gy0 + 1:, :] = True
    if "l" in sides:
        known[:, : x0 - gx0] = True
    if "r" in sides:
        known[:, x1 - gx0 + 1:] = True
    if not known.any():
        raise SystemExit("landscape_fill: no side names a ring to solve from")
    a = small.copy()
    a[~known] = small[known].mean(axis=0)          # start from the ring's own mean
    for _ in range(600):                            # Jacobi; 600 is past convergence here
        nb = np.zeros_like(a)
        nb[1:] += a[:-1]
        nb[:-1] += a[1:]
        nb[:, 1:] += a[:, :-1]
        nb[:, :-1] += a[:, 1:]
        cnt = np.zeros((H, W, 1), np.float32)
        cnt[1:] += 1
        cnt[:-1] += 1
        cnt[:, 1:] += 1
        cnt[:, :-1] += 1
        a = np.where(known[..., None], small, nb / cnt)
    patch = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGB").resize(
        (big.width, big.height), Image.BICUBIC)
    # feathered, for the same reason `clear_copy` is: pasted hard, the four edges of a
    # rectangle are a tonal step, and an outline is what makes a rectangle a rectangle.
    m = Image.new("L", (big.width, big.height), 0)
    # THE FEATHER IS SMALL AND CLAMPED TO THE RECTANGLE, and both halves of that were paid
    # for. `clear_copy`'s 1.5 cel px is right for a patch twenty rows tall and ruinous for
    # one three rows tall: at 4x it inverted the inner rectangle and PIL raised, and at 12x
    # it did something worse than raising - it left the core of the band only partly
    # opaque, so the defect showed THROUGH the patch and the result looked like a smear of
    # the building rather than a replacement for it. A check that reads "wrote the patch"
    # while the patch is 40% transparent is not a check.
    fe = max(1, min(int(feather * F), (min(w, h) * F) // 4))
    ImageDraw.Draw(m).rectangle([(x0 - gx0) * F + fe, (y0 - gy0) * F + fe,
                                 (x1 - gx0 + 1) * F - 1 - fe, (y1 - gy0 + 1) * F - 1 - fe],
                                fill=255)
    m = m.filter(ImageFilter.GaussianBlur(fe * 0.6))
    out = board.copy()
    out.paste(patch, (gx0 * F, gy0 * F), m)
    return out


def build_region(b, F, out_dir, ported, celdir, samples, device):
    """The building at factor F: generation inside Sierra's silhouette, then the name."""
    x, y, w, h = b.region
    # A BUILDING WITH A `sign` CARRIES ITS NAME ALREADY, built as an object at the
    # generation's own resolution by tools/sign_building.py. Downscaling that image is what
    # makes the sign and the building share one resampling - the six that stamped type into
    # the already-downscaled cel all read as decals, which is what Zain rejected.
    gw_, gh_ = w * b.scale, h * b.scale
    pre = None
    if b.sign:
        # `marry: False` ships the CONSTRUCTION and skips the img2img result. On the Pawn
        # Shoppe the marrying pass was run at 0.22, 0.12 and 0.07 and at every strength it
        # took the bolt heads and the bevel off the board while adding no material the
        # construction did not already have. The glyph-drift numbers say the same thing
        # precisely: survival IoU 0.887 with the centroid moved 0.22 cel px, and the
        # letterforms side by side are identical - what moved was the signwriter's relief
        # and the board's hardware, not the letters. A stage that removes the thing it was
        # added to improve does not ship on that building.
        order = ("signed_%dx%d.png" % (gw_, gh_), "constructed_%dx%d.png" % (gw_, gh_))
        if b.sign.get("marry", True) is False:
            order = order[1:]
        for n in order:
            if os.path.exists(os.path.join(samples, n)):
                pre = n
                break
        if pre is None:
            raise SystemExit("%s declares a `sign` but neither signed_ nor constructed_ "
                             "%dx%d exists - run tools/sign_building.py first"
                             % (b.key, gw_, gh_))
    # STAGE B's OUTPUT WINS WHEN IT EXISTS. The town centre is generated at 6 and
    # detailed at 12, so `detailed_` is both a different file and a different resolution;
    # taking it here means the 4x rung is boxed down from the detailed art too, rather
    # than from the composition.
    if pre is None and getattr(b, "detail_scale", 0):
        dw, dh = w * b.detail_scale, h * b.detail_scale
        dn = "detailed_%dx%d.png" % (dw, dh)
        if os.path.exists(os.path.join(samples, dn)):
            pre = dn
            print("    detail: %s (stage B, %d px per game px)" % (dn, b.detail_scale))
    gen = Image.open(os.path.join(samples, pre or ("generated_%dx%d.png" % (gw_, gh_)))
                     ).convert("RGB")
    if pre:
        # A LOG THAT SAYS THE WRONG THING IS A LOG THAT GETS BELIEVED. This printed
        # "sign: detailed_2172x1320.png (built at 1086x660...)" for the town centre -
        # wrong noun and wrong size, because gw_/gh_ are stage A's.
        pw, ph = Image.open(os.path.join(samples, pre)).size
        print("    source: %s (%dx%d, downscaled with the region)" % (pre, pw, ph))
    new = gen.resize((w * F, h * F), Image.LANCZOS)
    sierra = gb.sierra_region(b, celdir).resize((w * F, h * F), Image.NEAREST)
    m = gb.silhouette(b, gb.sierra_region(b, celdir), F)
    img = Image.fromarray(
        np.where(m[..., None], np.asarray(new), np.asarray(sierra)).astype(np.uint8),
        "RGB").copy()

    # the device, if this building has one and one was chosen
    if device and b.device_rect:
        dp = os.path.join(samples, "device_%s_384x512.png" % device)
        if os.path.exists(dp):
            dx0, dy0, dx1, dy1 = b.device_rect
            dw, dh = (dx1 - dx0 + 1) * F, (dy1 - dy0 + 1) * F
            d = Image.open(dp).convert("RGB").resize((dw, dh), Image.LANCZOS)
            img.paste(cut_out(d), (dx0 * F, dy0 * F), cut_out(d))
        else:
            print("    device '%s' not generated yet (%s)" % (device, dp))

    # GRIME, COPING AND SKY - constructed, after the generation and before the matte, so
    # they are inside the building's own outline and share its downscale. The model will not
    # draw weathering from an init; see tools/grime.py.
    if getattr(b, "grime", None):
        import grime as gr
        her = Image.open(os.path.join(ROOT, "assets", "png%dx-new" % F, "board.png")) \
            if os.path.exists(os.path.join(ROOT, "assets", "png%dx-new" % F, "board.png")) \
            else Image.open(os.path.join(ported, "board.png"))
        img = gr.apply(img, b.grime, F,
                       sky_mask=gr.sky_mask_from_init(b, F),
                       her_board=her, region_xy=(b.region[0], b.region[1]), seed=b.seed)
        print("    grime: %d constructed ops over the region (no model involved)"
              % len(b.grime))

    if b.sign:                       # the name is already in the pixels; stamp nothing
        x0, y0, x1, y1 = b.sign["rect"]
        return img, (x0 * F, y0 * F, (x1 - x0 + 1) * F, (y1 - y0 + 1) * F)

    # A REGION WITH NO NAME. The town centre is ground, not a shop: no name, no sign, no
    # surfaces, nothing for `find_name_art` to find. Without this it would fall through to
    # `letter_region` with an empty line list and a zero-sized rectangle.
    if not b.name_lines:
        return img, (0, 0, 0, 0)

    if b.name_plate:
        a = np.asarray(img).astype(np.uint8).copy()
        nx0, ny0, nx1, ny1 = b.name_rect
        a[ny0 * F:(ny1 + 1) * F, nx0 * F:(nx1 + 1) * F] = b.palette[b.name_plate]
        img = Image.fromarray(a, "RGB").copy()

    # `name_rects` sets each line in its own box, for a sign whose lines do not share a
    # band - QT Clothing's gable and awning are different widths with different
    # backgrounds, and one shared box would centre them on a common width.
    if b.name_rects:
        rect = None
        for line, r in zip(b.name_lines, b.name_rects):
            img, rect = letter_region(img, [line], r, b.name_ink, F)
        return img, rect
    img, rect = letter_region(img, b.name_lines, b.name_rect, b.name_ink, F)
    return img, rect


def cut_out(d):
    """Key the device off its plain studio background so it sits ON the facade rather than
    in a box. The init paints that background a flat light grey on purpose."""
    a = np.asarray(d.convert("RGB")).astype(int)
    r, g, bl = a[..., 0], a[..., 1], a[..., 2]
    mx, mn = np.maximum(np.maximum(r, g), bl), np.minimum(np.minimum(r, g), bl)
    bg = (mn > 150) & ((mx - mn) < 42)
    out = np.dstack([a.astype(np.uint8), np.where(bg, 0, 255).astype(np.uint8)])
    return Image.fromarray(out, "RGBA")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--who", required=True, choices=sorted(BUILDINGS))
    ap.add_argument("--cels", required=True)
    ap.add_argument("--set", default="cast0")
    ap.add_argument("--device", default=None)
    ap.add_argument("--factors", default="4,12")
    ap.add_argument("--verify", action="store_true")
    a = ap.parse_args()

    b = BUILDINGS[a.who]
    samples = os.path.join(ROOT, "tools", "samples", "%s-rename" % b.key)
    names = ["board.png", "pic_11.png"] + [s.file for s in b.surfaces]
    print("%s -> %s   device=%s" % (b.sierra, b.new_name, a.device or "none"))

    for F in (int(s) for s in a.factors.split(",")):
        ported = os.path.join(ASSETS, "png%dx" % F)
        out = os.path.join(ASSETS, "png%dx-%s" % (F, a.set))
        print("\n=== %dx -> %s" % (F, out))
        if a.verify:
            for n in names:
                p = os.path.join(out, n)
                print("  %-22s %s" % (n, ("%dx%d in the SET" % Image.open(p).size)
                                      if os.path.exists(p) else "ABSENT"))
            continue
        os.makedirs(out, exist_ok=True)

        base = os.path.join(out, "board.png")
        if not os.path.exists(base):
            base = os.path.join(ported, "board.png")
        print("  board base: %s" % base)
        board = Image.open(base).convert("RGB")
        if board.size != (320 * F, 200 * F):
            raise SystemExit("board is %s, expected %s" % (board.size, (320 * F, 200 * F)))

        # `clear` RESTORES SIERRA'S ORIGINAL over a rectangle BEFORE the building is pasted.
        #
        # It exists because `region` was doing two different jobs and they pull opposite
        # ways. A region has to be big enough to cover whatever an EARLIER install wrote,
        # or that art survives outside the new bounds forever (a gold bar did exactly that).
        # But a region also decides what the MODEL is shown, and a big one spends the
        # detail budget on scenery the blend throws away - the Factory's own six-round
        # lesson, which fired again here at 39% fill.
        #
        # So: `region` is the building's own box, tight, and `clear` is the eraser, as large
        # as every previous install's footprint put together. Erasing costs no GPU.
        if getattr(b, "clear", None):
            check_clear_covers_region(b)
            cx0, cy0, cx1, cy1 = b.clear
            # THE SOURCE IS CHOSEN BY MATCHING, NOT ASSUMED, and both guesses before this
            # were wrong. A LANCZOS upscale of the 1x cel left a visible patchwork, because
            # the rest of this board was upscaled by a model and a plain resample does not
            # match its neighbours. `assets/png{F}x` then left a fainter one: measured over
            # an untouched corner of the board, `png12x` differs from the cast0 base by a
            # mean of 18.8 per channel while `png12x-new` is byte-identical. The set was
            # built from a different rung directory than the obvious one.
            #
            # So: try the candidates, compare each against the CURRENT board in a frame just
            # outside the rectangle being cleared, and take the one that matches. If none
            # does, stop rather than paste a mismatched patch.
            src = pick_clear_source(ported, F, board, b.clear)
            patch = src.crop((cx0 * F, cy0 * F, (cx1 + 1) * F, (cy1 + 1) * F))
            board.paste(patch, (cx0 * F, cy0 * F))
            print("  cleared x %d..%d y %d..%d back to Sierra's own art"
                  % (cx0, cx1, cy0, cy1))

            # AND THEN PATCH OUT WHAT HER ORIGINAL ITSELF CONTAINS. Restoring cel 0 brought
            # the Factory's BLANK GOLD BOARD straight back, because she painted the panel
            # into the background and only the lettering is the cel. With the name moved
            # onto the building there is no board, so its footprint has to be filled - and
            # filled with HER OWN GROUND rather than a flat colour, or it is just a
            # differently-coloured rectangle in the same place.
            #
            # `clear_copy` copies a patch of her art from an offset: plain ground of the
            # same kind, at the same scale, under the same light.
            for (px0, py0, px1, py1, sdx, sdy) in getattr(b, "clear_copy", None) or []:
                src2 = src.crop(((px0 + sdx) * F, (py0 + sdy) * F,
                                 (px1 + sdx + 1) * F, (py1 + sdy + 1) * F))
                # FEATHERED. Pasted hard, the patch reads as a framed picture lying on the
                # ground - its four edges are a tonal step, and an outline is exactly what
                # makes a rectangle a rectangle. A soft mask lets it melt into her ground.
                pw, ph = src2.size
                m = Image.new("L", (pw, ph), 0)
                fe = max(2, int(1.5 * F))
                ImageDraw.Draw(m).rectangle([fe, fe, pw - 1 - fe, ph - 1 - fe], fill=255)
                m = m.filter(ImageFilter.GaussianBlur(fe * 0.6))
                board.paste(src2, (px0 * F, py0 * F), m)
                print("  patched x %d..%d y %d..%d from her ground at offset (%+d,%+d)"
                      % (px0, px1, py0, py1, sdx, sdy))

        # AND PAINT OUT WHAT THE REGENERATED BOARD ITSELF CONTAINS, which is a different
        # problem from both of the above and was found the hard way. `clear` restores
        # `png12x-new`, and the board regeneration REDREW her buildings: at the Factory it
        # reproduced her mill as a render-and-slate block whose roofline stands ABOVE the
        # region, so no install has ever been able to touch it. Restoring her art there
        # restores a copy of her building. See `landscape_fill`.
        for rect in getattr(b, "board_fill", None) or []:
            # an optional fifth element is the RING's width, for a band whose neighbour two
            # rows up is somebody else's building
            board = landscape_fill(board, rect[:4], F, ring=(rect[4] if len(rect) > 4 else 2))
            print("  landscape fill over x %d..%d y %d..%d - constructed from the board's "
                  "own pixels around it, no model" % (rect[0], rect[2], rect[1], rect[3]))

        region, rect = build_region(b, F, out, ported, a.cels, samples, a.device)
        x, y, w, h = b.region
        # MATTE THE BUILDING TO ITS OWN SILHOUETTE. NOTHING IS BLURRED TO HIDE A SEAM.
        #
        # `edge_feather` is gone and should not come back. It faded the generated scenery
        # into hers to disguise the region's border, and traded a hard rectangle for a
        # blurry one: at native resolution the building sat in a smeared halo, which on a
        # screen reads as the picture being broken rather than the art being wrong.
        #
        # The cast pipeline already solves this and solves it properly - generate the
        # subject, cut it out with an alpha matte, composite it with a one-pixel
        # antialiased edge. This does the same. The authored init gives the silhouette
        # exactly; the band around that silhouette is then snapped to the edge the model
        # actually drew, by asking whether each pixel is nearer the background colour the
        # init painted or nearer the building. Then ONE pixel of antialiasing, and no more.
        # AN EXPLICIT MATTE, when the outline was MEASURED off her board rather than
        # authored in `init`. The town centre's shape is her blank panel, flood-filled,
        # with the picPatch's eleven pixels subtracted; no arrangement of init rectangles
        # would reproduce it honestly. tools/town_centre_mask.py writes it.
        mf = getattr(b, "matte_file", None)
        if mf:
            mp = os.path.join(samples, mf)
            if not os.path.exists(mp):
                raise SystemExit("%s names matte_file %s but it does not exist - run "
                                 "tools/town_centre_mask.py --write" % (b.key, mf))
            matte = Image.open(mp).convert("L").resize((w * F, h * F), Image.NEAREST)
            # ONE pixel of antialiasing at the shipping rung, as building_matte does. Not
            # a feather: the join is meant to read as a clean edge.
            matte = matte.filter(ImageFilter.GaussianBlur(0.6))
            print("  matte: %s (measured off her board, %.1f%% of the region)"
                  % (mf, 100.0 * (np.asarray(matte) > 127).mean()))
        else:
            matte = building_matte(b, region, F) if getattr(b, "scenery", None) else None
        if matte is not None:
            board.paste(region, (x * F, y * F), matte)
            print("  matted to the building's own silhouette, 1px antialiased edge")
        else:
            board.paste(region, (x * F, y * F))
        # CONSTRUCTED TRIM. A band the model is not allowed to decide.
        #
        # The Rent Office needed this because the model kept writing a shop name on it: at
        # seed 7 across the door head, at seed 23 in the fascia, and after the brick band
        # was replaced with plain stone, along the parapet's top edge. Each move was a
        # reroll, and a reroll is not a fix - the only band guaranteed free of pseudo-text
        # is one that is painted deterministically after the generation, the way the sign
        # is. Flat face, a lit top edge, a shadow under it; no texture to misread.
        for (tx0, ty0, tx1, ty1, face, face_hi) in getattr(b, "trims", None) or []:
            gx, gy = (x + tx0) * F, (y + ty0) * F
            tw, th = (tx1 - tx0 + 1) * F, (ty1 - ty0 + 1) * F
            # A CORNICE, NOT A SLAB. The first version was one flat colour with a hairline
            # under it and it read as a grey rectangle pasted on the sky, because the top
            # four rows of this cel ARE wider than the shopfront below - that overhang is
            # Sierra's own roof and it only looks like a mistake when nothing explains it.
            # So: a lit capping course, a stone face, and a deep soffit shadow where the
            # overhang throws one. Three tones is enough to read as depth; a texture would
            # be something for the model to misread, which is the whole point of the band.
            band = Image.new("RGB", (tw, th), tuple(face))
            td = ImageDraw.Draw(band)
            cap = max(1, F // 3)
            sof = max(1, F // 2)
            td.rectangle([0, 0, tw, cap], fill=tuple(face_hi))
            td.rectangle([0, cap, tw, cap + max(1, F // 6)],
                         fill=tuple(int(c * 0.86) for c in face_hi))
            td.rectangle([0, th - sof, tw, th],
                         fill=tuple(int(c * 0.45) for c in face))
            td.rectangle([0, th - sof, tw, th - sof + max(1, F // 6)],
                         fill=tuple(int(c * 0.34) for c in face))
            # THE TRIM IS CUT BY THE SAME MATTE AS THE BUILDING. Painted as a bare
            # rectangle it floated above the roofline with sky either side of it - the
            # hard rectangular silhouette this pipeline exists to avoid. The band is part
            # of the building, so it ends where the building ends and nowhere else.
            m = None if matte is None else matte.crop((tx0 * F, ty0 * F,
                                                       (tx1 + 1) * F, (ty1 + 1) * F))
            board.paste(band, (gx, gy), m)
            print("  trim: plain band over game x %d..%d y %d..%d (constructed, not "
                  "generated; cut by the building matte)"
                  % (x + tx0, x + tx1, y + ty0, y + ty1))

        for n in ("board.png", "pic_11.png"):
            board.save(os.path.join(out, n))
        print("  wrote board.png and pic_11.png %dx%d; name at (%d,%d) %dx%d"
              % (board.width, board.height, *rect))

        l, t, r, bm = b.hotspot
        if not (x >= l - 6 and x + w - 1 <= r + 6):
            print("  NOTE: the region sits outside the hotspot %s" % (b.hotspot,))

        for s in b.surfaces:
            src = os.path.join(out, s.file)
            if not os.path.exists(src):
                src = os.path.join(ported, s.file)
            im = Image.open(src).convert("RGBA")
            px0, py0, px1, py1 = s.plate
            arr = np.asarray(im).astype(np.uint8).copy()
            arr[py0 * F:(py1 + 1) * F, px0 * F:(px1 + 1) * F, :3] = s.body
            arr[py0 * F:(py1 + 1) * F, px0 * F:(px1 + 1) * F, 3] = 255
            im2 = Image.fromarray(arr, "RGBA").copy()
            rgb, r2 = letter_region(im2.convert("RGB"), s.lines, s.plate, s.ink, F,
                                    fill=0.90)
            outim = Image.new("RGBA", im2.size)
            outim.paste(rgb)
            outim.putalpha(im2.getchannel("A"))
            outim.save(os.path.join(out, s.file))
            print("  wrote %-22s %dx%d, plate (%d,%d) %dx%d"
                  % (s.file, outim.width, outim.height, *r2))

    if not a.verify:
        print("\nuntouched: assets/png, assets/png4x, assets/png12x (opened read-only)")
        print("verify: python tools/find_name_art.py --who %s --cels <dir> --set %s"
              % (b.key, a.set))


if __name__ == "__main__":
    main()

