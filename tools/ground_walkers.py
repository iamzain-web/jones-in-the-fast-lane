"""Clean the walk cels: cut the backdrop off them, and give the shoe something to stand against.

TWO FAULTS, BOTH REPORTED BY LOOKING AT HIM WALK.

THE COMPLAINT. Zain, watching his man cross the board: "his shoe is not looking right, it
blends into the background."

He is right, and it is measurable rather than a matter of taste. The trainers are white -
the shoe region's brightest pixels sit at luminance 225 - and the board's paving runs
between about 120 and 180. A near-white shoe on pale concrete has nothing to separate it,
so the leg appears to end at the ankle.

There is a second, quieter cause in the same place. 5.5% of the figure's pixels are
SEMI-TRANSPARENT, the fringe left by matting him off a light background, and every one of
them is paler than what it sits on. The cut-out carries its own halo, which is brightest
exactly where the silhouette is thinnest: the ankles and the shoe.

WHAT THIS DOES, AND WHAT IT DELIBERATELY DOES NOT.

A contact shadow would be the usual answer and it is not available: the figure occupies the
bottom row of its own cel - rows 5..379 of 380 - and the game positions him by
`146 - height`, so growing the canvas downward would lift him off the path instead of
planting him on it. There is nowhere below his feet to draw.

So the shade is drawn INTO him:

  THE RAMP      the lowest 18% of the figure is multiplied by a gradient running from 1.0
                down to 0.42 at the final row. That is ambient occlusion by another name -
                less light reaches what is nearest the ground - and it reads as contact.
  THE SHOE      within that band, pixels brighter than mid-grey are taken down a further
                22%. The ramp alone darkens the jeans as much as the trainers, which loses
                the leg without finding the shoe; this part is aimed at the white.
  THE FRINGE    every partially-transparent pixel is darkened by 30%, which turns a pale
                halo into an edge. This is what stops him glowing against dark buildings as
                well as vanishing against light paths.

Nothing here repaints his clothes or invents a sole. It is light, applied where the ground
is.

CHOSEN BY LOOKING, at the size the game draws him, over the actual paving he walks on -
four strengths composited onto the board's concrete plaza and compared. "Mild" left the
shoe pale; "strong" dimmed the jeans enough to notice. These numbers are the third one.

  python tools/ground_walkers.py                 # writes into a scratch dir, prints it
  python tools/ground_walkers.py --apply         # and installs via install_walk.py
"""
import argparse
import os
import subprocess
import sys

import numpy as np
from collections import deque
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")

# The character the replacement set covers, in its three clothed views.
VIEWS = (280, 281, 282)

# Measured, not guessed. See the module docstring.
FOOT_FRACTION = 0.18     # how much of the figure counts as "near the ground"
FLOOR = 0.42             # brightness multiplier at the very last row
SHOE_MULTIPLIER = 0.78   # extra darkening for bright pixels inside the foot band
BRIGHT_THRESHOLD = 170   # what counts as a white trainer rather than a blue jean
FRINGE_MULTIPLIER = 0.70 # the matting halo
MIN_LUMINANCE = 45       # below this a pixel is left alone; see the ramp


# The backdrop the figure was photographed against, as it survives in the cut-out.
BACKDROP_SATURATION = 9.0    # it is neutral; his shirt is bluer
BACKDROP_LUMINANCE = 212.0   # and dimmer than the shirt
BACKDROP_LUMINANCE_MIN = 150.0
# ^ AND BRIGHTER THAN CLOTHES. Without a floor this ate view 281: a grey suit is neutral
# AND dim, so an unbounded "neutral" test treats it as backdrop, and the gate reported the
# figure filling 0.29 of its 1990 outline - eaten, which is the failure this check exists
# for. Measured across all three views: the neutral pixels of 280 and 281 sit at median
# luminance 4.7 and 26.7 (dark cloth), 282's at 227.7 (the white shirt), and the wedge at
# 191. The band between 150 and 212 is the wedge and nothing else - it catches 3,517 px in
# 282 and TEN in 280, which is the whole point.
FOOT_BAND = 0.16             # matches check_install's own band: never cut into a shoe


def strip_backdrop(img: Image.Image) -> tuple[Image.Image, int]:
    """Flood in from outside the sprite through backdrop-coloured pixels only."""
    a = np.array(img).astype(np.float32)
    rgb, alpha = a[..., :3], a[..., 3]
    lum = rgb.mean(axis=2)
    sat = rgb.max(axis=2) - rgb.min(axis=2)

    candidate = ((alpha > 0) & (sat < BACKDROP_SATURATION)
                 & (lum < BACKDROP_LUMINANCE) & (lum > BACKDROP_LUMINANCE_MIN))

    # THE FEET ARE NOT NEGOTIABLE. A shoe's sole is neutral and dim - the same colour
    # profile as the backdrop - so an unrestricted flood eats into it and leaves the shoe
    # in pieces. The install gate caught exactly that: three detached fragments, all inside
    # the foot band it watches, which is the check that exists because a shoe once shattered
    # and shipped. The wedge being removed runs down his SIDE, nowhere near the ground, so
    # the band costs nothing to protect.
    rows = np.nonzero(alpha.any(axis=1))[0]
    if rows.size:
        top, bottom = int(rows[0]), int(rows[-1])
        foot_top = int(bottom - FOOT_BAND * (bottom - top + 1))
        candidate[foot_top:, :] = False

    h, w = alpha.shape
    reach = np.zeros((h, w), dtype=bool)
    q: deque = deque()

    def seed(y, x):
        if alpha[y, x] < 16 and not reach[y, x]:
            reach[y, x] = True
            q.append((y, x))

    for x in range(w):
        seed(0, x); seed(h - 1, x)
    for y in range(h):
        seed(y, 0); seed(y, w - 1)

    while q:
        y, x = q.popleft()
        for dy, dx in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and not reach[ny, nx]:
                if alpha[ny, nx] < 16 or candidate[ny, nx]:
                    reach[ny, nx] = True
                    q.append((ny, nx))

    strip = reach & (alpha > 0)
    a[strip, 3] = 0

    # The new edge is a hard cut through a photograph. One pixel of feather stops it
    # looking like a stencil without softening the silhouette enough to see.
    edge = Image.fromarray(a[..., 3].astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6))
    a[..., 3] = np.minimum(a[..., 3], np.array(edge).astype(np.float32) * 1.25)

    # SWEEP THE CRUMBS. Feathering a cut edge leaves one- and three-pixel islands behind,
    # and the install gate counts those as detached fragments - correctly, since the check
    # that catches a severed arm cannot tell one from a speck. Anything this small is not
    # part of a man: the body is twenty-four thousand pixels.
    a[..., 3] = _drop_specks(a[..., 3], minimum=24)

    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA"), int(strip.sum())


def _drop_specks(alpha: np.ndarray, minimum: int) -> np.ndarray:
    solid = alpha > 40
    h, w = solid.shape
    seen = np.zeros_like(solid)

    for y in range(h):
        for x in range(w):
            if not solid[y, x] or seen[y, x]:
                continue
            q = deque([(y, x)])
            seen[y, x] = True
            points = []
            while q:
                cy, cx = q.popleft()
                points.append((cy, cx))
                for dy, dx in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)):
                    ny, nx = cy + dy, cx + dx
                    if 0 <= ny < h and 0 <= nx < w and solid[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        q.append((ny, nx))
            if len(points) < minimum:
                for cy, cx in points:
                    alpha[cy, cx] = 0

    return alpha


def ground(img: Image.Image) -> Image.Image:
    a = np.array(img).astype(np.float32)
    alpha = a[..., 3]

    rows = np.where(alpha.max(axis=1) > 16)[0]
    if rows.size == 0:
        return img                      # a fully transparent cel has no feet to ground

    top, bottom = int(rows.min()), int(rows.max())
    height = bottom - top + 1
    start = bottom - int(height * FOOT_FRACTION)

    for y in range(start, bottom + 1):
        t = (y - start) / max(1, bottom - start)
        ramp = 1.0 - t * (1.0 - FLOOR)

        # NEVER CRUSH WHAT IS ALREADY DARK. The problem is a WHITE trainer on pale paving;
        # a flat multiply treats the dress-clothes view the same way and takes near-black
        # cloth to black. view_281_l0_c1_s4 was already 46.9% black before this ran, and
        # the install gate - correctly - refused the build when the ramp pushed it further.
        # So the multiplier is floored per pixel: a pixel at or below MIN_LUMINANCE keeps
        # what it has, and the ramp applies in full only to the bright pixels it is for.
        row = a[y, :, :3]
        lum = np.maximum(row.mean(axis=1), 1.0)
        safe = np.maximum(ramp, np.minimum(1.0, MIN_LUMINANCE / lum))
        a[y, :, :3] = row * safe[:, None]

    band = slice(start, bottom + 1)
    segment = a[band, :, :3]
    bright = (segment.mean(axis=2) > BRIGHT_THRESHOLD) & (alpha[band] > 128)
    seg_lum = np.maximum(segment.mean(axis=2), 1.0)
    shoe_safe = np.maximum(SHOE_MULTIPLIER, np.minimum(1.0, MIN_LUMINANCE / seg_lum))
    segment[bright] *= shoe_safe[bright][:, None]
    a[band, :, :3] = segment

    partial = (alpha > 16) & (alpha < 220)
    fringe_lum = np.maximum(a[partial, :3].mean(axis=1), 1.0)
    fringe_safe = np.maximum(FRINGE_MULTIPLIER, np.minimum(1.0, MIN_LUMINANCE / fringe_lum))
    a[partial, :3] *= fringe_safe[:, None]

    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def run(out_root: str) -> tuple[str, str]:
    made = {}
    for scale, src_set in (("4", "png4x-cast0"), ("12", "png12x-cast0")):
        src = os.path.join(ASSETS, src_set)
        dst = os.path.join(out_root, f"ground{scale}")
        os.makedirs(dst, exist_ok=True)
        made[scale] = dst

        count = 0
        stripped = 0
        for name in sorted(os.listdir(src)):
            if not name.endswith(".png"):
                continue
            if not any(name.startswith(f"view_{v}_") for v in VIEWS):
                continue
            cel = Image.open(os.path.join(src, name)).convert("RGBA")
            cel, removed = strip_backdrop(cel)          # the wedge first
            ground(cel).save(os.path.join(dst, name))   # then the contact shade
            stripped += removed
            count += 1
        print(f"{src_set}: {count} cels, {stripped} backdrop px removed -> {dst}")

    return made["4"], made["12"]


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(ROOT, ".ground"))
    ap.add_argument("--apply", action="store_true",
                    help="install through tools/install_walk.py, which gates and backs up")
    args = ap.parse_args()

    src4, src12 = run(args.out)

    if args.apply:
        cmd = [sys.executable, os.path.join(ROOT, "tools", "install_walk.py"),
               "--src4", src4, "--src12", src12,
               "--views", ",".join(str(v) for v in VIEWS)]
        print("\n" + " ".join(cmd))
        sys.exit(subprocess.call(cmd))
