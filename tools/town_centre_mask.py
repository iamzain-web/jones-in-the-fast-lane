r"""The board's missing middle: measure the hole, and write its matte.

    python tools/town_centre_mask.py            # measure and report
    python tools/town_centre_mask.py --write    # also write the matte PNG

WHAT THE HOLE IS
----------------
Sierra left the centre of pic 11 blank and sized the shop dialog to fill it. Nothing is
under there: flood-filled from (160,100), the pale field is 19,330 px = 30.2% of the whole
board. Its bounding box is x 69..249, y 45..154 - 181x110 - and the field fills 97.1% of
that box, the rest being her own art reaching in at the corners.

THE EDGE IS HERS, AND IT IS THE BEST BOUNDARY ON THE BOARD
----------------------------------------------------------
She drew a ONE-PIXEL BLACK FRAME around the blank panel: (0,0,0) at y 44 above, x 68 at the
left, and so on round. Measured over all 652 boundary pixels, the step across the hole's
outline in her own art is 204.6 per channel with NOT ONE pixel below 10. Compare Rentflix's
best side at 98.9 and the Factory, which scores "nothing to land on" on all four. So every
join this region makes lands on a hard line she drew, and nothing needs feathering.

TWO THINGS THAT WERE EXPECTED TO CONSTRAIN IT AND DO NOT
--------------------------------------------------------
  marble    0 of 170 `MarblePath` anchors are inside the hole and 0 swept pixels fall in
            it. The ring road passes AROUND the centre; the nearest swept pixel is 2.0 px
            outside the edge. Nothing generated here can be walked on, and nothing
            generated here can collide with a sign.
  picPatch  view 0 loop 1, 183x25 at (68,138), 2071 opaque px, of which exactly 11 land
            inside the hole - all antialiasing fringe along the bottom edge. They are CUT
            from the matte, so the patch can never contradict what is generated. This is
            the bug that bit the University and the Employment Office and it is closed by
            subtraction rather than by hoping.
"""
import argparse
import os
from collections import deque

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REGION = (69, 45, 181, 110)          # x, y, w, h - the hole's bounding box


def hole_mask():
    """(mask over the whole board, stats). Pale field flood-filled from the centre, minus
    every pixel the picPatch repaints."""
    a = np.asarray(Image.open(os.path.join(ROOT, "assets", "png", "pic_11.png"))
                   .convert("RGB")).astype(int)
    pale = (a.min(2) >= 200) & ((a.max(2) - a.min(2)) <= 32)
    seen = np.zeros((200, 320), bool)
    q = deque([(100, 160)])
    seen[100, 160] = True
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < 200 and 0 <= nx < 320 and pale[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = True
                q.append((ny, nx))
    raw = seen.sum()

    pp = Image.open(os.path.join(ROOT, "assets", "png", "view_0_l1_c0.png")).convert("RGBA")
    cov = np.zeros((200, 320), bool)
    cov[138:138 + pp.height, 68:68 + pp.width] = np.array(pp)[:, :, 3] > 0
    clipped = int((seen & cov).sum())
    seen &= ~cov

    steps = []
    for y in range(200):
        for x in range(320):
            if not seen[y, x]:
                continue
            for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                ny, nx = y + dy, x + dx
                if 0 <= ny < 200 and 0 <= nx < 320 and not seen[ny, nx]:
                    steps.append(float(np.abs(a[y, x] - a[ny, nx]).mean()))
    steps = np.array(steps)
    x, y, w, h = REGION
    return seen, {"raw": int(raw), "picPatch_clipped": clipped, "px": int(seen.sum()),
                  "fill": 100.0 * seen[y:y + h, x:x + w].sum() / (w * h),
                  "outline": len(steps), "step": float(steps.mean()),
                  "under10": 100.0 * float((steps < 10).mean())}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    a = ap.parse_args()
    m, s = hole_mask()
    x, y, w, h = REGION
    print("the hole: %d px raw, %d clipped by the picPatch, %d kept = %.1f%% of the board"
          % (s["raw"], s["picPatch_clipped"], s["px"], 100.0 * s["px"] / 64000))
    print("region x %d..%d y %d..%d = %dx%d   the matte fills %.1f%% of it"
          % (x, x + w - 1, y, y + h - 1, w, h, s["fill"]))
    print("outline %d px   mean step in HER art %.1f per channel   below 10: %.1f%%"
          % (s["outline"], s["step"], s["under10"]))
    if a.write:
        out = os.path.join(ROOT, "tools", "samples", "towncentre-rename")
        os.makedirs(out, exist_ok=True)
        p = os.path.join(out, "matte_%dx%d.png" % (w, h))
        Image.fromarray((m[y:y + h, x:x + w] * 255).astype(np.uint8), "L").save(p)
        print("wrote %s" % p)


if __name__ == "__main__":
    main()
