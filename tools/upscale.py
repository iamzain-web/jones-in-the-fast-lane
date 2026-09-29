"""
Upscale every extracted cel with Lanczos resampling.

Writes to a PARALLEL directory (assets/png<N>x) and never touches assets/png, so the
original decode stays the reference and this pass can be thrown away or redone with a
different filter at any time.

Why premultiply
---------------
The cels are RGBA with hard-edged transparency: a skip run decodes to alpha 0, but the
RGB under it still holds whatever the palette put there. Resampling RGBA directly makes
the filter average those invisible colours into the visible edge pixels, which fringes
every sprite with dark halos — most obvious on the walker figures against the pale
board.

So: premultiply RGB by alpha, resample, then unpremultiply. Invisible pixels then
contribute nothing to their visible neighbours, which is what "transparent" should mean.

Lanczos is a sharpening filter with negative lobes, so it can push a channel outside
0..255 and ring slightly at hard edges. Values are clamped; the ringing is inherent to
the filter and is one reason this pass smooths rather than adds detail.

Usage:
    python upscale.py [scale] [--src DIR] [--out DIR]

    scale   integer factor, default 4
"""

import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def upscale(img: Image.Image, scale: int) -> Image.Image:
    """Lanczos-resample an RGBA image without letting invisible colours bleed."""
    img = img.convert("RGBA")
    w, h = img.size
    size = (w * scale, h * scale)

    r, g, b, a = img.split()

    # Premultiply: every colour channel is scaled by its own alpha, so a fully
    # transparent pixel becomes 0 and carries no weight through the filter.
    pm = [
        Image.frombytes(
            "L", img.size,
            bytes((c * al + 127) // 255 for c, al in zip(ch.tobytes(), a.tobytes())),
        )
        for ch in (r, g, b)
    ]

    pm = [c.resize(size, Image.LANCZOS) for c in pm]
    a2 = a.resize(size, Image.LANCZOS)

    # Unpremultiply, clamping both the division and Lanczos's overshoot.
    out = []
    ab = a2.tobytes()
    for ch in pm:
        cb = ch.tobytes()
        out.append(
            Image.frombytes(
                "L", size,
                bytes(
                    0 if al == 0 else min(255, (c * 255 + al // 2) // al)
                    for c, al in zip(cb, ab)
                ),
            )
        )

    return Image.merge("RGBA", (out[0], out[1], out[2], a2))


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    scale = int(args[0]) if args else 4

    src = os.path.join(ROOT, "assets", "png")
    out = os.path.join(ROOT, "assets", "png%dx" % scale)

    if "--src" in sys.argv:
        src = sys.argv[sys.argv.index("--src") + 1]
    if "--out" in sys.argv:
        out = sys.argv[sys.argv.index("--out") + 1]

    if not os.path.isdir(src):
        print("no such directory: %s" % src)
        return 1

    os.makedirs(out, exist_ok=True)

    names = sorted(n for n in os.listdir(src) if n.lower().endswith(".png"))
    biggest = (0, "")
    for i, name in enumerate(names, 1):
        img = Image.open(os.path.join(src, name))
        big = upscale(img, scale)
        big.save(os.path.join(out, name))

        px = big.size[0] * big.size[1]
        if px > biggest[0]:
            biggest = (px, "%s %dx%d" % (name, big.size[0], big.size[1]))

        if i % 100 == 0 or i == len(names):
            print("  %d/%d" % (i, len(names)))

    print("\n%d cels upscaled x%d -> %s" % (len(names), scale, out))
    print("largest: %s" % biggest[1])
    return 0


if __name__ == "__main__":
    sys.exit(main())
