"""
Decode Sierra SCI1 VGA view resources into PNGs.

Format verified empirically against Jones in the Fast Lane (CD DOS 1.0) and
cross-checked with ScummVM's GfxView::initData:

  view header
    0      u8   loop count
    2..3   u16  mirror bits
    6..7   u16  palette offset (in-view palette, if any)
    8+     u16  loop offset, one per loop

  loop header (at its offset)
    0..1   u16  cel count
    2..3   u16  unused
    4+     u16  cel offset, one per cel

  cel header (at its offset)
    0..1   u16  width
    2..3   u16  height
    4      i8   displace x
    5      u8   displace y
    6      u8   clear (transparent) colour key
    7      u8   padding
    8+          RLE pixel data

  RLE control byte: top two bits select the operation, low six give the run.
    0x00 / 0x40  literal - copy `run` bytes verbatim
    0x80         fill    - repeat the next byte `run` times
    0xC0         skip    - `run` transparent pixels

  palette resource 999 (1284 bytes)
    0..255      index mapping
    256..259    header
    260+        256 entries of 4 bytes: used flag, R, G, B

Writes PNGs with no external dependencies (zlib + struct only), so it runs on a
bare Python install.

Usage:
    python decode_views.py <raw_dir> <out_dir> [--limit N]
"""

import os
import struct
import sys
import zlib

CEL_HEADER_SIZE = 8


PALETTE_SIZE = 1284
PALETTE_ENTRIES = 260  # 256-byte index mapping, then a 4-byte header


def parse_palette(data, base=0):
    """
    Reads a 1284-byte SCI palette starting at `base`. Layout is the same whether it
    is a standalone palette resource or embedded in a view at its palette offset:
    256 bytes of index mapping, a 4-byte header, then 256 entries of
    (used flag, R, G, B) with full 8-bit components.
    """
    pal = []
    start = base + PALETTE_ENTRIES
    for i in range(256):
        off = start + i * 4
        if off + 3 < len(data):
            pal.append((data[off + 1], data[off + 2], data[off + 3]))
        else:
            pal.append((0, 0, 0))
    return pal


def load_palette(path):
    return parse_palette(open(path, "rb").read())


def view_palette(data, fallback):
    """
    Each view carries its OWN palette at the offset in bytes 6-7. Using the global
    palette for every view produces structurally correct but garishly miscoloured
    sprites, so prefer the embedded one wherever it is present.
    """
    if len(data) < 8:
        return fallback
    pal_off = struct.unpack_from("<H", data, 6)[0]
    if pal_off and pal_off + PALETTE_SIZE <= len(data):
        return parse_palette(data, pal_off)
    return fallback


def decode_cel(data, offset):
    """
    Returns (width, height, clear_key, pixels, transparent_mask) for one cel.

    TRANSPARENCY comes from the SKIP opcode alone, never from a pixel's value.

    This matters: much of the art is dithered between two palette indices, and one of
    them is frequently the same index as clearKey. Treating every pixel whose value
    equals clearKey as transparent punches out every other pixel and leaves the whole
    image looking like a checkerboard. Only runs the encoder explicitly skipped are
    holes; literal and fill pixels are opaque whatever their index.
    """
    width, height = struct.unpack_from("<HH", data, offset)
    clear_key = data[offset + 6]

    total = width * height
    pixels = bytearray()
    mask = bytearray()          # 1 = transparent
    pos = offset + CEL_HEADER_SIZE

    while len(pixels) < total and pos < len(data):
        control = data[pos]
        pos += 1
        op = control & 0xC0
        run = control & 0x3F

        if op == 0x80:                     # fill with the following byte
            if pos >= len(data):
                break
            pixels.extend(bytes([data[pos]]) * run)
            mask.extend(b"\x00" * run)
            pos += 1
        elif op == 0xC0:                   # skip - these pixels are the holes
            pixels.extend(bytes([clear_key]) * run)
            mask.extend(b"\x01" * run)
        else:                              # literal (0x00 and 0x40)
            chunk = data[pos:pos + run]
            pixels.extend(chunk)
            mask.extend(b"\x00" * len(chunk))
            pos += run

    # Pad so the image is well formed even if a cel is truncated; padding is a hole.
    if len(pixels) < total:
        short = total - len(pixels)
        pixels.extend(bytes([clear_key]) * short)
        mask.extend(b"\x01" * short)

    return width, height, clear_key, pixels[:total], mask[:total]


def write_png(path, width, height, pixels, palette, clear_key, mask=None):
    """
    Writes an RGBA PNG. Only pixels flagged in `mask` are transparent — see
    decode_cel for why a pixel's value must not decide its transparency.
    """
    if width == 0 or height == 0:
        return False

    raw = bytearray()
    for y in range(height):
        raw.append(0)  # filter: none
        base = y * width
        for x in range(width):
            idx = pixels[base + x]
            r, g, b = palette[idx]
            hole = mask[base + x] if mask is not None else 0
            raw.extend((r, g, b, 0 if hole else 255))

    def chunk(tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")

    with open(path, "wb") as f:
        f.write(png)
    return True


def decode_view(path, out_dir, global_palette, report):
    data = open(path, "rb").read()
    name = os.path.splitext(os.path.basename(path))[0]

    if len(data) < 10:
        return

    palette = view_palette(data, global_palette)
    if palette is not global_palette:
        report["own_palette"] += 1

    loop_count = data[0]
    if loop_count == 0 or loop_count > 64:
        report["skipped"] += 1
        return

    written = 0
    for loop_no in range(loop_count):
        off_pos = 8 + loop_no * 2
        if off_pos + 1 >= len(data):
            break
        loop_off = struct.unpack_from("<H", data, off_pos)[0]
        if loop_off == 0 or loop_off + 4 > len(data):
            continue

        cel_count = struct.unpack_from("<H", data, loop_off)[0]
        if cel_count == 0 or cel_count > 128:
            continue

        for cel_no in range(cel_count):
            cel_ptr = loop_off + 4 + cel_no * 2
            if cel_ptr + 1 >= len(data):
                break
            cel_off = struct.unpack_from("<H", data, cel_ptr)[0]
            if cel_off + CEL_HEADER_SIZE > len(data):
                continue

            w, h, key, px, mask = decode_cel(data, cel_off)
            if w == 0 or h == 0 or w > 640 or h > 480:
                continue

            out = os.path.join(out_dir, "view_%s_l%d_c%d.png" % (name, loop_no, cel_no))
            if write_png(out, w, h, px, palette, key, mask):
                written += 1
                report["max_w"] = max(report["max_w"], w)
                report["max_h"] = max(report["max_h"], h)

    report["cels"] += written
    if written:
        report["views"] += 1


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1

    raw_dir, out_dir = sys.argv[1], sys.argv[2]
    limit = None
    if "--limit" in sys.argv:
        limit = int(sys.argv[sys.argv.index("--limit") + 1])

    os.makedirs(out_dir, exist_ok=True)
    palette = load_palette(os.path.join(raw_dir, "palette", "999.palette"))

    view_dir = os.path.join(raw_dir, "view")
    files = sorted(os.listdir(view_dir), key=lambda f: int(f.split(".")[0]))
    if limit:
        files = files[:limit]

    report = {"views": 0, "cels": 0, "skipped": 0, "max_w": 0, "max_h": 0,
              "own_palette": 0}
    for f in files:
        try:
            decode_view(os.path.join(view_dir, f), out_dir, palette, report)
        except Exception as e:                      # keep going; report at the end
            report["skipped"] += 1
            print("  ! %s: %s" % (f, e))

    print("views decoded : %d" % report["views"])
    print("cels written  : %d" % report["cels"])
    print("skipped       : %d" % report["skipped"])
    print("own palette   : %d of %d views" % (report["own_palette"], len(files)))
    print("largest cel   : %dx%d" % (report["max_w"], report["max_h"]))
    print("output        : %s" % out_dir)
    return 0


if __name__ == "__main__":
    sys.exit(main())
