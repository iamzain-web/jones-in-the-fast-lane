"""Builds the STOP-AND-SHOW sheet: each new cel at 1:1, beside the sprite it replaces.

The brief for this work says to show rather than assert, so the only thing this does is put
the two next to each other at the size the game actually draws them and let them be looked
at. Nothing is rescaled to flatter anything:

    the LEFT figure is assets/png12x - the 1990 cel upscaled to the same 12x the renderer
    asks for, which is what the game shows today;
    the RIGHT figure is the new cel, at exactly the same pixel dimensions, because
    SciArt.HighRes rejects a twin that is not exactly Factor x and the two therefore cannot
    differ in size;
    the strip between them is the 1x cel at 1:1, a 39x95 thumbnail, which is the only
    honest reminder of how much of what is on the left was invented by an upscaler.

Drawn on the board's own midtone green, because these are cutout sprites: judging them on
white flatters the edge and judging them on black hides the fringe.

    python tools/cast_samples.py --new assets/png12x-cast --out tools/samples
"""

import argparse
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PNG1X = os.path.join(ROOT, "assets", "png")
BACK = (92, 132, 92)
LABEL = (236, 240, 236)

# The cheap tier - base+2 - which is both what room1.sc puts on the board at the start and
# what fit_walkers composites into the character-select screen, so it is the art that is
# seen most and the right thing to judge the cast on.
GATE = [(282, "body0"), (286, "body1"), (292, "body2"), (296, "body3"), (276, "jones")]


def on_back(im):
    bg = Image.new("RGBA", im.size, BACK + (255,))
    bg.alpha_composite(im.convert("RGBA"))
    return bg.convert("RGB")


def pair(view, old_dir, new_dir, cel=0):
    pn = os.path.join(new_dir, f"view_{view}_l0_c{cel}.png")
    if not os.path.exists(pn):
        return None
    new = Image.open(pn).convert("RGBA")
    po = os.path.join(old_dir, f"view_{view}_l0_c{cel}.png")
    old = Image.open(po).convert("RGBA") if os.path.exists(po) else None
    p1 = os.path.join(PNG1X, f"view_{view}_l0_c{cel}.png")
    one = Image.open(p1).convert("RGBA") if os.path.exists(p1) else None
    return old, one, new


def sheet(view, old, one, new, path, tag):
    gap, pad, cap = 28, 24, 34
    w = pad * 2 + gap * 2 + (old.size[0] if old else 0) + new.size[0] + \
        (one.size[0] if one else 0)
    h = pad * 2 + cap + max(new.size[1], old.size[1] if old else 0)
    im = Image.new("RGB", (w, h), BACK)
    d = ImageDraw.Draw(im)

    x = pad
    if old:
        im.paste(on_back(old), (x, pad + cap))
        d.text((x, pad + 8), f"assets/png12x  (1990 art, upscaled)  {old.size[0]}x"
                             f"{old.size[1]}", fill=LABEL)
        x += old.size[0] + gap
    if one:
        im.paste(on_back(one), (x, pad + cap))
        d.text((x, pad + 8), f"1x  {one.size[0]}x{one.size[1]}", fill=LABEL)
        x += one.size[0] + gap
    im.paste(on_back(new), (x, pad + cap))
    d.text((x, pad + 8), f"new  view {view}  {tag}  {new.size[0]}x{new.size[1]}",
           fill=LABEL)
    im.save(path)
    return im


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--new", required=True)
    ap.add_argument("--old", default=os.path.join(ROOT, "assets", "png12x"))
    ap.add_argument("--out", default=os.path.join(ROOT, "tools", "samples"))
    ap.add_argument("--cel", type=int, default=0)
    ap.add_argument("--contact-height", type=int, default=560,
                    help="the one-page sheet is scaled down to this; the per-character "
                         "files beside it are 1:1 and are what should actually be judged")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)

    made = []
    for view, body in GATE:
        got = pair(view, a.old, a.new, a.cel)
        if got is None:
            continue
        old, one, new = got
        p = os.path.join(a.out, f"cast_{view}_{body}.png")
        made.append((body, view, sheet(view, old, one, new, p, body)))
        print(f"  {p}  1:1")

    if made:
        k = a.contact_height / max(im.size[1] for _, _, im in made)
        tiles = [im.resize((max(1, int(im.size[0] * k)), max(1, int(im.size[1] * k))),
                           Image.LANCZOS) for _, _, im in made]
        w = sum(t.size[0] for t in tiles) + 20 * (len(tiles) + 1)
        h = max(t.size[1] for t in tiles) + 40
        con = Image.new("RGB", (w, h), BACK)
        x = 20
        for t in tiles:
            con.paste(t, (x, 20))
            x += t.size[0] + 20
        p = os.path.join(a.out, "cast_all.png")
        con.save(p)
        print(f"  {p}  contact sheet, scaled to {a.contact_height}px")
    print(f"{len(made)} character(s) -> {a.out}")


if __name__ == "__main__":
    main()
