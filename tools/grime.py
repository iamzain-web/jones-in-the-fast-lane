r"""Decay, coping and sky - CONSTRUCTED over the installed region, after the generation.

WHY THIS EXISTS
---------------
Casa El Cheapo asked for "a cheap but badly maintained castle" and five generations produced
a clean one. Four levers were tried and recorded in BUILDINGS.md 36: the prompt naming
flaking render and water staining (ignored), the decay authored as init rectangles (they
came back as SHUTTERED WINDOWS), a grubbier palette (drab, not derelict), and dropping
`noise` from the negative prompt (marginal).

The reason the init cannot state it is a rule in its own right:

    A RECTANGLE IS AN OBJECT, AND AT THIS SCALE A DARK RECTANGLE ON A WALL IS A WINDOW.

Weathering is low-contrast, soft-edged and irregular - the opposite of a rect. So it is
built the way this pipeline builds everything the model will not cooperate on: constructed
deterministically, after the generation, where it cannot be ignored or reinterpreted. That
is `sign_board.py` for signs and `Building.trims` for the Rentflix parapet, and this is the
same move for surface.

THE SKY IS HERE FOR A DIFFERENT REASON, AND IT IS NOT DECAY
------------------------------------------------------------
Casa's top rows had to be painted over because HER ART IN THEM IS THE OLD NAME - `LOW COST
HOUSING` in red across game x 146..174, y 3..19 - so cutting the merlon gaps out of the
matte would have framed pieces of the name we are replacing. The first answer was to paint
our own sky in the init, and the model rendered it as a flat saturated blue panel with
castellation notched out of it: it did not match the board, so it read as a graphic rather
than as sky.

`sky` copies HER OWN SKY instead, tiled from clean columns at the same rows - the same
move `pick_clear_source` makes for `clear`, and for the same reason. The name does not
survive; the cover looks like the board, because it IS the board.

OPS, all in region-local CEL coordinates, scaled by the install factor:

    ("skyfill", y0, y1, top, bot)       fill SKY-keyed pixels in rows y0..y1 with a
                                        vertical gradient between two sampled colours
    ("coping", x0, y0, x1, y1)          a merlon's cap: a lit top course and a dark
                                        underside, so it has thickness instead of being a
                                        notch in a zigzag
    ("shadow", x0, y0, x1, y1, k)       a hard horizontal shadow, for under an overhang
    ("streak", x,  y0, y1, w, k)        a vertical grime streak, strongest at the top and
                                        fading down, as water actually runs
    ("stain",  x0, y0, x1, y1, k)       a soft darkening, for a sheltered corner
    ("patch",  x0, y0, x1, y1, rgb)     blown render showing a darker substrate

Everything multiplies or blends; nothing is pasted as a flat colour except `patch`, which
is a hole in the render and should be.
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter


def _rand(w, h, seed, sigma):
    """A smooth irregular field in [0,1], deterministic for a given seed."""
    r = np.random.RandomState(seed)
    a = r.rand(h, w).astype(np.float32)
    im = Image.fromarray((a * 255).astype(np.uint8), "L").filter(
        ImageFilter.GaussianBlur(sigma))
    a = np.asarray(im, np.float32) / 255.0
    lo, hi = a.min(), a.max()
    return (a - lo) / max(1e-6, hi - lo)


def apply(region, ops, F, sky_mask=None, her_board=None, region_xy=None, seed=7):
    """`region` is the generated art at F px per cel px. Returns a new image."""
    a = np.asarray(region.convert("RGB"), np.float32).copy()
    H, W, _ = a.shape

    for op in ops or []:
        kind = op[0]

        if kind == "skyfill":
            # A VERTICAL GRADIENT BETWEEN TWO SAMPLED VALUES, not a copy of her sky, because
            # SHE HAS NO SKY HERE. The first attempt tiled her own board from game x 134..145
            # on the theory that her lettering only ran x 146..174 - but those columns at
            # these rows are her TENEMENT'S DARK ROOF, not sky, and the merlon gaps filled
            # with pieces of her building. Her cel is a tenement that fills its box; there is
            # no sky in it to borrow.
            #
            # So the sky is built: a soft gradient between two values sampled from the board
            # immediately beside the region, which is what made the flat saturated panel read
            # as a graphic rather than as sky. A gradient matches; a fill does not.
            _, y0, y1, top, bot = op
            if sky_mask is None:
                raise SystemExit("the `skyfill` op needs the init's SKY mask")
            Y0, Y1 = y0 * F, min(H, (y1 + 1) * F)
            t = np.array(top, np.float32)
            b_ = np.array(bot, np.float32)
            n = _rand(W, Y1 - Y0, seed + 991, F * 1.6)
            for i, y in enumerate(range(Y0, Y1)):
                f = i / max(1, (Y1 - Y0 - 1))
                row = t * (1 - f) + b_ * f
                m = sky_mask[y]
                if not m.any():
                    continue
                # a little smooth variation so it is not a flat field
                a[y][m] = np.clip(row * (0.97 + 0.06 * n[i][m])[:, None], 0, 255)

        elif kind == "coping":
            _, x0, y0, x1, y1 = op
            X0, Y0, X1, Y1 = x0 * F, y0 * F, (x1 + 1) * F - 1, (y1 + 1) * F - 1
            t = max(1, F // 3)
            a[Y0:Y0 + t, X0:X1 + 1] *= 1.10                      # the lit top course
            a[Y0 + t:Y0 + 2 * t, X0:X1 + 1] *= 0.90              # its shadow on the merlon
            a[Y1 - t + 1:Y1 + 1, X0:X1 + 1] *= 0.80              # the dark underside
            a[Y0:Y1 + 1, X0:X0 + max(1, F // 4)] *= 1.06         # a lit left return
            a[Y0:Y1 + 1, X1 - max(1, F // 4) + 1:X1 + 1] *= 0.92

        elif kind == "shadow":
            _, x0, y0, x1, y1, k = op
            m = np.zeros((H, W), np.float32)
            m[y0 * F:(y1 + 1) * F, x0 * F:(x1 + 1) * F] = 1.0
            m = np.asarray(Image.fromarray((m * 255).astype(np.uint8), "L").filter(
                ImageFilter.GaussianBlur(F * 0.18)), np.float32) / 255.0
            a *= (1.0 - k * m)[..., None]

        elif kind == "streak":
            _, x, y0, y1, w, k = op
            X0, Y0, Y1 = x * F, y0 * F, min(H, (y1 + 1) * F)
            ww = max(1, int(w * F))
            n = _rand(ww, Y1 - Y0, seed + x * 31 + y0, F * 0.35)
            fade = np.linspace(1.0, 0.0, Y1 - Y0, dtype=np.float32)[:, None] ** 1.4
            m = np.clip(n * 0.6 + 0.4, 0, 1) * fade
            sl = a[Y0:Y1, X0:min(W, X0 + ww)]
            m = m[:, :sl.shape[1]]
            a[Y0:Y1, X0:min(W, X0 + ww)] = sl * (1.0 - k * m)[..., None]

        elif kind == "stain":
            _, x0, y0, x1, y1, k = op
            X0, Y0, X1, Y1 = x0 * F, y0 * F, (x1 + 1) * F, (y1 + 1) * F
            n = _rand(X1 - X0, Y1 - Y0, seed + x0 * 17 + y1, F * 0.9)
            a[Y0:Y1, X0:X1] *= (1.0 - k * n)[..., None]

        elif kind == "patch":
            _, x0, y0, x1, y1, rgb = op
            X0, Y0, X1, Y1 = x0 * F, y0 * F, (x1 + 1) * F, (y1 + 1) * F
            n = _rand(X1 - X0, Y1 - Y0, seed + x1 * 13 + y0, F * 0.5)
            m = (n > 0.62).astype(np.float32)
            m = np.asarray(Image.fromarray((m * 255).astype(np.uint8), "L").filter(
                ImageFilter.GaussianBlur(F * 0.12)), np.float32) / 255.0
            col = np.array(rgb, np.float32)
            # 45% at most, so it reads as render blown off rather than a pasted slab
            m = m * 0.45
            a[Y0:Y1, X0:X1] = a[Y0:Y1, X0:X1] * (1 - m)[..., None] + col * m[..., None]

        else:
            raise SystemExit("unknown grime op %r" % (kind,))

    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGB")


def sky_mask_from_init(b, F, key="SKY"):
    """Which pixels of the region the init painted with `key`, at F px per cel px.

    Taken from the init IN ORDER, so a later box covering the sky removes it - the same
    rule `building_matte` learned when a scenery box stopped cutting."""
    _, _, w, h = b.region
    m = np.zeros((h, w), bool)
    for (x0, y0, x1, y1, name) in b.init:
        m[y0:y1 + 1, x0:x1 + 1] = (name == key)
    return np.kron(m, np.ones((F, F), bool))
