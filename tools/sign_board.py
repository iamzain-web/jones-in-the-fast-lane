r"""A board sign built as an OBJECT, at the generation's own resolution.

WHY THIS EXISTS
---------------
The first six buildings stamped their name with `install_employment.text_mask` into the
already-downscaled cel, at 4x and at 12x. Zain's verdict on the result was two sentences:
the buildings look like AI-generated nonsense, and the signs look like words placed over an
image. The second half is literally true, and it is a direct consequence of the rule that
fixed garbled glyphs - generate with the band blank so the model never sees text, then
stamp. Looking at `COMMON THREADS` at 12x: flat vector type, one colour, no thickness, no
shadow, no fixing, no grain, and the only hard-edged thing in a soft photograph.

Three separate faults, and each has its own answer here:

  1. THE SIGN WAS NOT AN OBJECT. A sign is a painted board with sawn edges, a thickness, a
     shadow on the wall behind it and something holding it up. All of that is constructed,
     deterministically, by `build()`. None of it is asked of the model.

  2. THE SIGN WAS COMPOSITED AFTER THE DOWNSCALE, so its edges were resampled on a
     different schedule from the building's and it reads as a sticker. Everything here
     happens at the GENERATION's resolution - 16 px per cel pixel - and the downscale then
     antialiases sign and building together, once.

  3. THE SIGN'S MATERIAL WAS NEVER LIT BY THE SCENE. `marry()` runs a low-denoise img2img
     over the sign region only, which is the one thing a diffusion model is unambiguously
     good at: it lights and weathers what is already there. The layout is already correct
     before it runs, so the model is never asked to produce text - only to light text that
     exists. `drift()` then measures whether the letters survived, because an img2img that
     quietly reshapes a glyph is worse than no img2img at all.

THE FACADE'S PLANE IS THE IMAGE PLANE, AND THAT IS MEASURED, NOT ASSUMED
------------------------------------------------------------------------
Sierra drew every building on pic 11 as a flat elevation - no vanishing point, no receding
side wall, verticals exactly vertical in all fifteen cels. So "put the sign in the facade's
perspective plane" means NO rotation and NO foreshortening here; it means the board's
thickness reads as a bevel lit from the scene's own light, and its shadow falls on the wall
in the scene's own direction. Rotating the board on a frontal elevation would be the
invention, not the fidelity.

The light direction is measured off the generated facade by `light_from()` rather than
chosen, so the board's lit edge and its cast shadow agree with the photograph they sit on.
"""

import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import install_employment as ie


def light_from(face, rect=None, upper=0.55):
    """(lx, ly) unit vector pointing TOWARDS the light, measured off the facade.

    A heavy blur throws away texture and leaves the large-scale value structure; the
    gradient of that field points uphill, towards the light.

    TWO CORRECTIONS, AND THE FIRST ONE WAS FOUND BY THE SHADOW COMING OUT UPSIDE DOWN.

    1. ONLY THE HORIZONTAL COMPONENT IS MEASURED. A shopfront's brightest thing is its own
       LIT WINDOW, which is an emitter at the bottom of the frame, not the sun. Measured
       freely, the Pawn Shoppe's facade returned (-0.34, +0.94) - light from below - and
       the board threw its shadow upwards onto the wall. An exterior building in daylight
       is lit from above; the only open question is from which side, so `ly` is fixed
       negative and the measurement decides `lx`.
    2. ONLY THE UPPER PART OF THE FACADE IS SAMPLED, for the same reason: the glazing and
       the lamplight live in the lower third and they describe the shop's own lighting, not
       the sky's.
    """
    g = np.asarray(face.convert("L").filter(
        ImageFilter.GaussianBlur(max(face.width, face.height) / 8.0)), np.float32)
    g = g[:max(4, int(face.height * upper)), :].copy()
    if rect is not None:
        x0, y0, x1, y1 = rect
        y1 = min(y1, g.shape[0] - 1)
        if y1 >= y0:
            g[y0:y1 + 1, x0:x1 + 1] = np.nan      # the band tells us nothing about the wall
    gx = np.gradient(np.nan_to_num(g, nan=float(np.nanmean(g))))[1]
    vx = float(np.nanmean(gx))
    n = (vx * vx + 1.0) ** 0.5
    return (vx / n, -1.0 / n)                     # always from above; the side is measured


def _grain(w, h, seed, amp):
    """A deterministic timber grain. Long horizontal streaks, because that is how a sawn
    board runs, and it is CONSTRUCTED rather than generated so it cannot garble."""
    rng = np.random.default_rng(seed)
    n = rng.normal(0.0, 1.0, (h, max(8, w // 24)))
    # np.ptp(n), NOT n.ptp(): the ndarray method was removed in NumPy 2.0 and this machine
    # runs 2.5.3, so the method form raises AttributeError at the first grain ever drawn.
    n = np.asarray(Image.fromarray(((n - n.min()) / max(1e-6, float(np.ptp(n))) * 255).astype(
        np.uint8), "L").resize((w, h), Image.BICUBIC), np.float32) / 255.0 - 0.5
    n += (np.asarray(Image.fromarray(rng.integers(0, 255, (h // 3 + 1, w // 3 + 1),
                                                  dtype=np.uint8), "L").resize(
        (w, h), Image.BILINEAR), np.float32) / 255.0 - 0.5) * 0.35
    return n * amp


def _disc(d, r, cx, cy, col, hi):
    """A bolt head: a disc, a highlight off-centre towards the light, a seated shadow."""
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=col)
    d.ellipse([cx - r * 0.45, cy - r * 0.45, cx + r * 0.15, cy + r * 0.15], fill=hi)


def build(face, spec, S, seed=7):
    """Construct the sign onto a copy of `face` (the generated facade at S px per cel px).

    Returns (image, glyph_mask, board_mask, info). `glyph_mask` is the authored letterform
    coverage at this resolution - the thing `drift()` checks survived the img2img.
    """
    x0, y0, x1, y1 = spec["rect"]
    X0, Y0 = x0 * S, y0 * S
    X1, Y1 = (x1 + 1) * S - 1, (y1 + 1) * S - 1
    W, H = X1 - X0 + 1, Y1 - Y0 + 1
    lx, ly = spec.get("light") or light_from(face, (X0, Y0, X1, Y1))
    d = max(2.0, spec.get("depth", 0.45) * S)          # the board's sawn thickness
    sh = max(2.0, spec.get("shadow", 0.9) * S)         # how far its shadow is thrown

    img = face.convert("RGB").copy()

    # A SIGN WITH NO BOARD. The General Dealer's name is painted on the CROWN of a cap, as
    # his picture has it, and a board there is wrong twice over: he rejected the previous
    # facade for being "a blue building with a HUGE sign", and a flat plate cannot sit on a
    # surface with a gradient across it - painted in the crown's own navy it still read as a
    # rectangle, because the crown runs #101F2F at its ends and #203451 in the middle.
    #
    # So `plate: False` skips the shadow, the board, the bevel and the bolts and signwrites
    # the lettering straight onto the facade. Everything else is unchanged: the glyphs are
    # still constructed at the generation's own resolution, in the same face, with the same
    # three passes, so they are not a decal and they cannot garble.
    plate = spec.get("plate", True)

    # 0. THE POSTS, if this sign stands rather than hangs on a wall. Sierra drew three dark
    #    hooks along the top edge of the Factory's board in the BACKGROUND art, so her board
    #    is a hanging one and the fixings are hers, not an invention. They go in first,
    #    behind the board, each with its own shadow on the ground.
    posts = spec.get("posts")
    if posts:
        pw = max(2, int(round(posts.get("width", 0.8) * S)))
        drop = int(round(posts.get("drop", 5) * S))
        rise = int(round(posts.get("rise", 0.6) * S))
        n = posts.get("count", 2)
        xs = [int(X0 + (W - pw) * (i / (n - 1.0))) if n > 1 else X0 + (W - pw) // 2
              for i in range(n)]
        pd = ImageDraw.Draw(img)
        for px in xs:                                   # the shadow each post throws
            pd.rectangle([px - lx * sh, Y0 - rise - ly * sh,
                          px + pw - lx * sh, Y1 + drop - ly * sh],
                         fill=tuple(int(c * 0.45) for c in posts.get("colour", (70, 56, 40))))
        for px in xs:
            pd.rectangle([px, Y0 - rise, px + pw, Y1 + drop],
                         fill=posts.get("colour", (70, 56, 40)))
            pd.rectangle([px, Y0 - rise, px + max(1, pw // 3), Y1 + drop],
                         fill=posts.get("colour_hi", (110, 90, 64)) if lx < 0
                         else posts.get("colour_lo", (40, 30, 20)))

    # 1. THE CAST SHADOW, before anything is drawn on top of it. It falls AWAY from the
    #    light, and its penumbra widens with the throw, which is what makes a board read as
    #    standing off a wall rather than being painted on one.
    if plate:
        sm = Image.new("L", img.size, 0)
        ImageDraw.Draw(sm).rectangle(
            [X0 - lx * sh, Y0 - ly * sh, X1 - lx * sh, Y1 - ly * sh], fill=255)
        sm = sm.filter(ImageFilter.GaussianBlur(sh * 0.45))
        a = np.asarray(img, np.float32)
        k = (np.asarray(sm, np.float32) / 255.0)[..., None] * spec.get("shadow_strength", 0.55)
        img = Image.fromarray(np.clip(a * (1.0 - k), 0, 255).astype(np.uint8), "RGB")

    # 2. THE BOARD. A painted field with a vertical fall-off, plus grain, plus the wear the
    #    brief asks for - all constructed, so none of it can garble.
    face_c = np.array(spec["face"], np.float32)
    face_hi = np.array(spec.get("face_hi", spec["face"]), np.float32)
    ramp = np.linspace(0.0, 1.0, H, dtype=np.float32)[:, None, None]
    if ly > 0:                                          # light from below: flip the fall-off
        ramp = ramp[::-1]
    board = face_hi * (1.0 - ramp) + face_c * ramp
    board = board * (1.0 + _grain(W, H, seed, spec.get("grain", 0.10))[..., None])
    bd = int(round(d))

    # 3. THE THICKNESS, as a bevel. Two edges catch the light and two are in shadow; which
    #    two is decided by the measured direction, never by a constant.
    hi_c = np.array(spec.get("edge_hi", (255, 255, 255)), np.float32)
    lo_c = np.array(spec.get("edge_lo", (0, 0, 0)), np.float32)
    board[:bd, :] = hi_c if ly < 0 else lo_c
    board[-bd:, :] = lo_c if ly < 0 else hi_c
    board[:, :bd] = hi_c if lx < 0 else lo_c
    board[:, -bd:] = lo_c if lx < 0 else hi_c
    bi = Image.fromarray(np.clip(board, 0, 255).astype(np.uint8), "RGB")
    if plate:
        img.paste(bi, (X0, Y0))

    # 4. THE FIXINGS. A painted fascia board is bolted through; the bolt heads are the
    #    cheapest honest signal that the thing is screwed to a wall.
    dr = ImageDraw.Draw(img)
    if plate and spec.get("bolts", True):
        r = max(1.5, S * 0.22)
        inset = bd + r * 1.6
        for cx in (X0 + inset, X1 - inset):
            for cy in (Y0 + inset, Y1 - inset):
                _disc(dr, r, cx, cy, spec.get("bolt", (90, 78, 60)),
                      spec.get("bolt_hi", (200, 180, 130)))

    # 5. THE LETTERING, signwritten: a shade thrown away from the light, a highlight towards
    #    it, then the body. This is how a gilded letter is actually made, and it is the step
    #    that stops the type reading as a decal.
    pad = int(round(bd * 1.8))
    bw, bh = W - 2 * pad, H - 2 * pad
    # LEADING IS PART OF THE SIGN, NOT A CONSTANT. It was fixed at the engine default of
    # 0.16 and that is generous for an engraved plate, where the lines sit close. On a
    # three-line name it is also the difference between fitting and not: VILLA / LOCKE & /
    # KEYES clears Sierra's 5 cel px cap in a plate 22 rows tall at 0.16, but in 18 rows at
    # 0.02 - four rows of facade handed back to the building for nothing.
    mask = ie.text_mask(spec["lines"], bw, bh, fill=spec.get("fill", 0.88),
                        leading=spec.get("leading", 0.16))
    if mask is None:
        raise SystemExit("no typeface available for the sign")
    off = max(1, int(round(S * spec.get("relief", 0.10))))
    out = img
    for dx, dy, col in ((int(-lx * off), int(-ly * off), spec.get("ink_lo")),
                        (int(lx * off), int(ly * off), spec.get("ink_hi")),
                        (0, 0, spec["ink"])):
        if col is None:
            continue
        out = ie.composite_text(out, mask, (X0 + pad + dx, Y0 + pad + dy),
                                lambda r, h: col)

    gm = np.zeros((face.height, face.width), np.float32)
    gm[Y0 + pad:Y0 + pad + bh, X0 + pad:X0 + pad + bw] = np.asarray(mask, np.float32) / 255.0
    bm = np.zeros((face.height, face.width), bool)
    bm[Y0:Y1 + 1, X0:X1 + 1] = True
    info = {"light": (lx, ly), "board_px": (W, H), "bevel_px": bd, "throw_px": sh,
            "pad_px": pad, "glyph_px": int((gm > 0.5).sum())}
    return out, gm, bm, info


def marry_region(constructed, generated_full, board_mask, feather):
    """Keep the img2img result inside the board (and its shadow), the construction outside.

    The low-denoise pass is run over the WHOLE facade so the model sees the building as
    context and the aspect stays near the one it was trained at; only the sign comes back.
    """
    m = Image.fromarray((board_mask * 255).astype(np.uint8), "L").filter(
        ImageFilter.GaussianBlur(feather))
    k = (np.asarray(m, np.float32) / 255.0)[..., None]
    a = np.asarray(generated_full.convert("RGB"), np.float32)
    b = np.asarray(constructed.convert("RGB"), np.float32)
    return Image.fromarray(np.clip(b * (1 - k) + a * k, 0, 255).astype(np.uint8), "RGB")


def drift(before, after, glyph_mask, board_mask, ink, face):
    """Did the letters survive the img2img, or did it rewrite them?

    Re-derives a glyph mask from each image by asking, per pixel inside the board, whether
    it is nearer the ink or nearer the painted field, and compares that to the AUTHORED
    mask. Reports IoU and the centroid shift. A high strength shows up here as IoU falling
    and the centroid wandering, and the answer to that is a lower strength, not a shrug.
    """
    ink = np.array(ink, np.float32)
    face = np.array(face, np.float32)

    def derive(im):
        a = np.asarray(im.convert("RGB"), np.float32)
        di = ((a - ink) ** 2).sum(2)
        df = ((a - face) ** 2).sum(2)
        return (di < df) & board_mask

    auth = (glyph_mask > 0.5) & board_mask
    mb, ma = derive(before), derive(after)

    def iou(p, q):
        return float((p & q).sum()) / max(1, int((p | q).sum()))

    def cen(m):
        ys, xs = np.nonzero(m)
        return (float(xs.mean()), float(ys.mean())) if len(xs) else (0.0, 0.0)

    # THE NUMBER THAT MATTERS IS AFTER-vs-BEFORE, and the first version of this reported
    # each image against the AUTHORED mask instead, which made its own baseline wrong. The
    # construction draws every glyph three times - a shade thrown away from the light, a
    # highlight towards it, then the body - so the recovered ink region is about twice the
    # authored one and sits off-centre by the relief offset. The constructed image scored
    # IoU 0.516 against a mask it reproduces exactly. Against itself it scores 1.000, and
    # the question being asked is whether the img2img moved the letters, not whether the
    # signwriter's highlight counts as ink.
    bx, by = cen(mb)
    ax_, ay_ = cen(ma)
    return {
        "survive_iou": iou(ma, mb),            # after vs before - the real measurement
        "shift": ((ax_ - bx) ** 2 + (ay_ - by) ** 2) ** 0.5,
        "px_before": int(mb.sum()), "px_after": int(ma.sum()),
        "auth_iou_before": iou(mb, auth), "auth_iou_after": iou(ma, auth),
    }
