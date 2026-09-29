"""Builds the before/after samples for the generated walker art.

Two kinds, because a still cannot show what is actually wrong with a sprite:

    walkers_<view>.gif    the ported figure and the new one walking side by side, at the
                          rate the game plays them (a cel holds 11 ticks, about 183ms;
                          with three in-between frames per cel that is ~46ms a frame - see
                          tools/smooth_walk.py for where those numbers come from)
    walkers_<body>.png    a still sheet, ported above, new below, all four cels of all
                          four clothing views, on the board's own background colour

Both are drawn on an opaque backdrop, because these are cutout sprites: judging them on
white flatters the edges and judging them on black hides the fringe.

    python tools/walker_samples.py --new <dir> --old assets/png12x --out tools/samples
"""

import argparse
import os

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PNG1X = os.path.join(ROOT, "assets", "png")

BODIES = {"jones": 274, "body0": 280, "body1": 284, "body2": 290, "body3": 294}

# Sampled from the board's midtone green so the sprites are judged against something
# close to what they sit on in the game.
BACK = (92, 132, 92)


def frames(d, view, loop, subs):
    """The displayed sequence: cel, then its in-between frames, for all four cels."""
    out = []
    for c in range(4):
        base = os.path.join(d, f"view_{view}_l{loop}_c{c}.png")
        if not os.path.exists(base):
            return None
        out.append(base)
        for s in range(1, subs):
            p = os.path.join(d, f"view_{view}_l{loop}_c{c}_s{s}.png")
            if os.path.exists(p):
                out.append(p)
    return out


def on_back(path, height):
    im = Image.open(path).convert("RGBA")
    k = height / im.size[1]
    im = im.resize((max(1, int(round(im.size[0] * k))), height), Image.LANCZOS)
    bg = Image.new("RGBA", im.size, BACK + (255,))
    bg.alpha_composite(im)
    return bg.convert("RGB")


def make_gif(old, new, view, loop, out, height, subs):
    a = frames(old, view, loop, subs)
    b = frames(new, view, loop, subs)
    if not a or not b:
        return False
    n = min(len(a), len(b))
    pages = []
    for i in range(n):
        l = on_back(a[i], height)
        r = on_back(b[i], height)
        w = max(l.size[0], r.size[0])
        p = Image.new("RGB", (w * 2 + 24, height), BACK)
        p.paste(l, ((w - l.size[0]) // 2, 0))
        p.paste(r, (w + 24 + (w - r.size[0]) // 2, 0))
        pages.append(p.convert("P", palette=Image.ADAPTIVE, colors=255))
    ms = int(round(183.0 / max(1, n // 4) / 10.0)) * 10
    pages[0].save(out, save_all=True, append_images=pages[1:], duration=max(20, ms),
                  loop=0, disposal=2, optimize=False)
    return True


def make_sheet(old, new, body, base, out, height):
    cols = []
    for v in range(base, base + 4):
        for c in range(4):
            po = os.path.join(old, f"view_{v}_l0_c{c}.png")
            pn = os.path.join(new, f"view_{v}_l0_c{c}.png")
            if not os.path.exists(pn):
                continue
            cols.append((on_back(po, height) if os.path.exists(po) else None,
                         on_back(pn, height)))
    if not cols:
        return False
    w = max(max(x.size[0] for x in pair if x) for pair in cols) + 10
    sheet = Image.new("RGB", (w * len(cols) + 10, height * 2 + 30), BACK)
    for i, (o, n) in enumerate(cols):
        x = 10 + i * w
        if o:
            sheet.paste(o, (x + (w - 10 - o.size[0]) // 2, 10))
        sheet.paste(n, (x + (w - 10 - n.size[0]) // 2, height + 20))
    sheet.save(out)
    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--new", required=True)
    ap.add_argument("--old", default=os.path.join(ROOT, "assets", "png12x"))
    ap.add_argument("--out", default=os.path.join(ROOT, "tools", "samples"))
    ap.add_argument("--height", type=int, default=760,
                    help="figure height in the sample; 1140 is what a 95px sprite occupies "
                         "on a 4K screen at the 12x render scale")
    ap.add_argument("--subs", type=int, default=4)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)

    made = 0
    for body, base in BODIES.items():
        p = os.path.join(a.out, f"walkers_{body}.png")
        if make_sheet(a.old, a.new, body, base, p, a.height):
            print(f"  {p}")
            made += 1
        for v in range(base, base + 4):
            g = os.path.join(a.out, f"walkers_{v}.gif")
            if make_gif(a.old, a.new, v, 0, g, a.height, a.subs):
                print(f"  {g}")
                made += 1
    print(f"{made} sample(s) -> {a.out}")


if __name__ == "__main__":
    main()
