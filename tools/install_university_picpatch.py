r"""Rebuilds `picPatch` (view 0 loop 1 cel 0) for the university art set.

THE BUG THIS FIXES
------------------
`MainViewModel.BuildBoard` draws, in order: the board, then the character body cel
(view 0 loop 0, 181x110 at (69,45)) which covers the board down to y=154, then
**`picPatch` - view 0 loop 1 cel 0, 183x25 stamped at (68,138)** - which repaints the
BUILDING TOPS back over the body cel so they stand in front of the panel, exactly as
`MainViewModel.cs` says ("Hi-Tech U stick up above the panel's bottom edge").

That strip contains Sierra's own artwork for the Employment Office sign, the trees, and
**the top of the university with HI-TECH U lettered on it**. It is a VIEW cel, not part of
pic 11, so replacing `board.png` and `pic_11.png` in an art set does not touch it: the set
was silent about `view_0_l1_c0.png`, so it fell through to the ported art and Sierra's
university top was stamped over the new building on every board screen.

It only shows on the TOWN BOARD. The five setup screens call `TitleBackdrop` instead of
`BuildBoard`, so they never stamp this strip - which is why the fault looked screen-
dependent and why a setup-screen capture looked clean.

    picPatch covers game x 68..250, y 138..162.
    The university cel is at (191,141) 59x41, so its top 22 rows fall inside the strip,
    at strip-local x 123..181, y 3..24.

WHAT THIS WRITES
----------------
`assets/png{4,12}x-uni/view_0_l1_c0.png`: Sierra's strip with ONLY the university region
replaced by the same pixels the set's own `board.png` carries there, at full opacity -
because the new building is a solid cel and fills that rectangle. Everything else in the
strip (the Employment Office, the trees) is left exactly as Sierra drew it, alpha included.

No original is written. `assets/png`, `assets/png4x` and `assets/png12x` are read only.
"""

import os

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")

PATCH = "view_0_l1_c0.png"
PATCH_X, PATCH_Y, PATCH_W, PATCH_H = 68, 138, 183, 25     # the strip, in 320x200 space
CEL_X, CEL_Y, CEL_W, CEL_H = 191, 141, 59, 41             # the university cel

# The part of the university that falls inside the strip, in strip-local game coordinates.
LX0 = CEL_X - PATCH_X                      # 123
LY0 = CEL_Y - PATCH_Y                      # 3
LX1 = min(CEL_X + CEL_W, PATCH_X + PATCH_W) - PATCH_X      # 182
LY1 = PATCH_H                                              # 25


def rebuild(factor):
    ported = os.path.join(ASSETS, "png%dx" % factor)
    uni = os.path.join(ASSETS, "png%dx-uni" % factor)
    src = os.path.join(ported, PATCH)
    board = os.path.join(uni, "board.png")
    if not (os.path.exists(src) and os.path.exists(board)):
        print("  skip %dx: missing %s or %s" % (factor, src, board))
        return

    patch = Image.open(src).convert("RGBA")
    if patch.size != (PATCH_W * factor, PATCH_H * factor):
        raise SystemExit("%s is %s, expected %s"
                         % (src, patch.size, (PATCH_W * factor, PATCH_H * factor)))

    b = Image.open(board).convert("RGB")
    F = factor
    region = b.crop((CEL_X * F, CEL_Y * F, (PATCH_X + LX1) * F, (PATCH_Y + LY1) * F))

    a = np.asarray(patch).astype(np.uint8).copy()
    r = np.asarray(region.convert("RGB")).astype(np.uint8)
    y0, y1 = LY0 * F, LY1 * F
    x0, x1 = LX0 * F, LX1 * F
    before_opaque = int((a[y0:y1, x0:x1, 3] > 0).sum())
    a[y0:y1, x0:x1, :3] = r
    a[y0:y1, x0:x1, 3] = 255
    out = os.path.join(uni, PATCH)
    Image.fromarray(a, "RGBA").save(out)
    print("  %dx: wrote %s  (region x %d..%d, y %d..%d; opaque px %d -> %d)"
          % (factor, out, x0, x1 - 1, y0, y1 - 1, before_opaque, (x1 - x0) * (y1 - y0)))


print("picPatch = view 0 loop 1 cel 0, %dx%d at (%d,%d) - game rows %d..%d"
      % (PATCH_W, PATCH_H, PATCH_X, PATCH_Y, PATCH_Y, PATCH_Y + PATCH_H - 1))
print("university inside the strip: x %d..%d, y %d..%d (strip-local)"
      % (LX0, LX1 - 1, LY0, LY1 - 1))
for f in (4, 12):
    rebuild(f)
