"""
Decode Sierra SCI1 VGA pic resources (backgrounds) into PNGs.

A pic is an opcode stream rather than a plain bitmap. Verified against Jones in the
Fast Lane (CD DOS 1.0):

    offset 0    fe 02          PIC_OPX set palette
    offset 2    1284 bytes     palette, same layout as a palette resource
    ...         fe 03 ...      further opcodes (priority bands, vector ops)
    ...         fe 01          PIC_OPX embedded view, followed by a cel

The embedded cel uses the same header as a view cel:
    0..1 u16 width, 2..3 u16 height, 4 i8 dispX, 5 u8 dispY, 6 u8 clearKey,
    7 pad, 8+ RLE data.

Rather than interpret the whole vector opcode stream — which would also need the
priority/control planes the game does not display — this locates the embedded
full-screen cel directly. For Jones that is the entire visible background: the town
board in 11.pic decodes as 319x199, the full SCI screen.

Usage:
    python decode_pics.py <raw_dir> <out_dir>
"""

import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from decode_views import decode_cel, write_png, parse_palette, PALETTE_SIZE

PALETTE_AT = 2  # immediately after the `fe 02` opcode


def find_embedded_cels(data, start):
    """
    Finds every plausible embedded cel in the opcode stream, largest first.

    A pic is NOT a single image: the town board is drawn as a base painting with
    further cels composited over it, and the building name signs live in those
    overlays. Taking only the largest cel gives a board with no names on it.

    Cels are returned largest-first so the caller can lay the base down and paint
    the overlays on top. Overlapping candidates are skipped, since a false positive
    inside another cel's pixel data would paint noise over the image.
    """
    found = []

    for i in range(start, len(data) - 16):
        w, h = struct.unpack_from("<HH", data, i)
        if not (8 <= w <= 320 and 8 <= h <= 200):
            continue
        if len(data) - (i + 8) < (w * h) // 64:
            continue
        found.append((w * h, i, w, h))

    found.sort(key=lambda t: -t[0])

    # Keep candidates whose headers do not sit inside an already-accepted cel's data.
    kept = []
    for area, off, w, h in found:
        if any(o <= off < o + 8 + (ww * hh) for _, o, ww, hh in kept):
            continue
        kept.append((area, off, w, h))
        if len(kept) >= 8:
            break

    return kept


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1

    raw_dir, out_dir = sys.argv[1], sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)

    pic_dir = os.path.join(raw_dir, "pic")
    files = sorted(os.listdir(pic_dir), key=lambda f: int(f.split(".")[0]))

    written = 0
    for name in files:
        data = open(os.path.join(pic_dir, name), "rb").read()
        num = name.split(".")[0]

        if len(data) < PALETTE_AT + PALETTE_SIZE:
            print("  %-10s too small for a palette, skipped" % name)
            continue

        palette = parse_palette(data, PALETTE_AT)
        cel_at = find_embedded_cel(data, PALETTE_AT + PALETTE_SIZE)

        if cel_at is None:
            print("  %-10s no embedded bitmap found" % name)
            continue

        w, h, key, px, mask = decode_cel(data, cel_at)

        # A background fills the screen, so its skipped runs are not holes to see
        # through — they are simply undrawn. Passing no mask keeps it fully opaque.
        out = os.path.join(out_dir, "pic_%s.png" % num)
        if write_png(out, w, h, px, palette, key, None):
            written += 1
            print("  %-10s %3dx%-3d  -> %s" % (name, w, h, os.path.basename(out)))

    print("\nwrote %d backgrounds to %s" % (written, out_dir))
    return 0


if __name__ == "__main__":
    sys.exit(main())
