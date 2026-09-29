"""
Interpret a Sierra SCI1 VGA pic resource properly and composite it to a PNG.

A pic is NOT a bitmap. It is a stream of drawing opcodes. The visible background of
an SCI1 VGA pic is built from one or more EMBEDDED VIEW cels, each stamped at its own
(x, y); the vector opcodes that remain from the EGA era paint the priority and control
planes, which are never shown. decode_pics.py only ever found the single largest cel,
so anything stamped on top of it (in Jones, every building's name sign) was lost.

Opcode stream, verified byte-for-byte against Jones in the Fast Lane (CD DOS 1.0):

    0xF0  set colour                1 byte
    0xF1  disable visual            -
    0xF2  set priority              1 byte
    0xF3  disable priority          -
    0xF4  short patterns            abs coord, then rel-1 coords
    0xF5  medium lines              abs coord, then rel-2 coords
    0xF6  long lines                abs coord, then abs coords
    0xF7  short lines               abs coord, then rel-1 coords
    0xF8  fill                      abs coords
    0xF9  set pattern               1 byte
    0xFA  absolute pattern          abs coords
    0xFB  set control               1 byte
    0xFC  disable control           -
    0xFD  medium patterns           abs coord, then rel-2 coords
    0xFE  extended opcode           1 sub-opcode byte, see below
    0xFF  end of stream

    0xFE 0x00  set palette entries   runs until the next opcode byte
    0xFE 0x01  embedded view         3-byte abs coord, u16 size, then a view cel
    0xFE 0x02  set palette           1284 bytes, same layout as a palette resource
    0xFE 0x03  priority table eqdist 4 bytes
    0xFE 0x04  priority table explct 14 bytes

An "abs coord" is three bytes: a prefix carrying the two high nibbles, then the low
byte of x and the low byte of y:
    x = data[1] | ((prefix & 0xF0) << 4)
    y = data[2] | ((prefix & 0x0F) << 8)

A byte below 0xF0 is not an opcode, which is how the variable-length coordinate lists
terminate.

Usage:
    python decode_pic_full.py <pic_file> <out_png> [--dump] [--cels <dir>] [--alpha]
    python decode_pic_full.py --all <raw_dir> <out_dir> [--alpha]

--alpha leaves pixels no cel covered transparent instead of palette index 0. The town
board's hollow centre is one of those: the game draws the interface window over it.

LIMITATION, stated plainly: line ops ARE rasterised, but visible FILL (0xF8) and brush
pattern ops are only parsed for length, never drawn — a fill needs SCI's exact flood
semantics and a wrong one would flood the screen. Pic 11 contains no visible fill, so
its output is complete; pics 0 to 5 do, and the tool prints a warning naming the count.
"""

import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from decode_views import decode_cel, write_png, parse_palette, PALETTE_SIZE

SCREEN_W, SCREEN_H = 320, 200

OP_FIRST = 0xF0
(OP_SET_COLOR, OP_DISABLE_VISUAL, OP_SET_PRIORITY, OP_DISABLE_PRIORITY,
 OP_SHORT_PATTERNS, OP_MEDIUM_LINES, OP_LONG_LINES, OP_SHORT_LINES,
 OP_FILL, OP_SET_PATTERN, OP_ABSOLUTE_PATTERN, OP_SET_CONTROL,
 OP_DISABLE_CONTROL, OP_MEDIUM_PATTERNS, OP_OPX, OP_END) = range(0xF0, 0x100)

OPX_SET_PALETTE_ENTRIES = 0x00
OPX_EMBEDDED_VIEW = 0x01
OPX_SET_PALETTE = 0x02
OPX_PRIORITY_TABLE_EQDIST = 0x03
OPX_PRIORITY_TABLE_EXPLICIT = 0x04

PATTERN_SIZE_FLAG = 0x20   # a pattern code with this bit carries an extra size byte


class PicError(Exception):
    pass


def abs_coord(data, pos):
    """Three-byte absolute coordinate: high-nibble prefix, then low x, low y."""
    prefix = data[pos]
    x = data[pos + 1] | ((prefix & 0xF0) << 4)
    y = data[pos + 2] | ((prefix & 0x0F) << 8)
    return x, y, pos + 3


def is_data(data, pos):
    return pos < len(data) and data[pos] < OP_FIRST


def bresenham(p0, p1, color, put):
    """
    Plain Bresenham. SCI's own line routine rounds slightly differently, but every
    visible line in Jones' pics is axis-aligned or a single pixel, where the two agree
    exactly — so this is not an approximation for this game's data.
    """
    x0, y0 = p0
    x1, y1 = p1
    dx, dy = abs(x1 - x0), -abs(y1 - y0)
    sx = 1 if x0 < x1 else -1
    sy = 1 if y0 < y1 else -1
    err = dx + dy
    while True:
        put(x0, y0, color)
        if x0 == x1 and y0 == y1:
            return
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x0 += sx
        if e2 <= dx:
            err += dx
            y0 += sy


class Pic:
    def __init__(self, data):
        self.data = data
        self.palette = None
        self.cels = []          # (x, y, width, height, pixels, mask, dispX, dispY)
        self.log = []
        # Every visible draw, in stream order, as ("cel", …) or ("line", colour, pts).
        # Order matters and is not the same in every pic: pic 11 stamps its cels and
        # then rules lines over them, while pic 0 draws its vectors BEFORE its cel.
        self.draws = []
        # A vector op only reaches the screen while the visual plane is enabled.
        # 0xF1 disables it and 0xF0 (set colour) turns it back on, so pic 11's single
        # 0xF1 does NOT mean the rest of its stream is invisible - 50 ops there are
        # visible, and an earlier reading that said otherwise was wrong.
        self.visual_on = True
        self.color = 0
        self.visible_vectors = 0
        self.unrendered_vectors = 0   # visible fills/patterns, which are not drawn

    # -- opcode stream ----------------------------------------------------

    def emit_line(self, is_line, pts, unrendered=0):
        """
        Records a polyline if the visual plane is on. Pattern and fill ops go through
        here too, with is_line false: they are counted as unrendered rather than
        approximated, because a fill needs SCI's exact flood semantics and a wrong
        one would flood the whole screen.
        """
        if not self.visual_on:
            return
        if is_line and pts and len(pts) > 1:
            self.visible_vectors += 1
            self.draws.append(("line", self.color, pts))
        else:
            self.unrendered_vectors += unrendered or (len(pts) if pts else 0)

    def parse(self):
        data = self.data
        pos = 0
        pattern_code = 0

        while pos < len(data):
            op = data[pos]
            if op < OP_FIRST:
                # Stray data byte outside any operand list. The stream is desynced;
                # stopping is safer than guessing, since a wrong resync would stamp
                # garbage cels over the image.
                raise PicError("desynced at 0x%X: expected an opcode, saw 0x%02X"
                               % (pos, op))
            start = pos
            pos += 1

            if op == OP_END:
                self.log.append((start, "END", ""))
                break

            elif op == OP_SET_COLOR:
                self.color = data[pos]
                self.visual_on = True
                pos += 1

            elif op in (OP_SET_PRIORITY, OP_SET_CONTROL):
                pos += 1

            elif op == OP_SET_PATTERN:
                pattern_code = data[pos]
                pos += 1

            elif op == OP_DISABLE_VISUAL:
                self.visual_on = False

            elif op in (OP_DISABLE_PRIORITY, OP_DISABLE_CONTROL):
                pass

            elif op in (OP_SHORT_LINES, OP_SHORT_PATTERNS):
                sized = op == OP_SHORT_PATTERNS and (pattern_code & PATTERN_SIZE_FLAG)
                if sized:
                    pos += 1
                x, y, pos = abs_coord(data, pos)
                pts = [(x, y)]
                while is_data(data, pos):
                    if sized:
                        pos += 1
                    # One packed byte: high nibble dx, low nibble dy, bit 3 = sign.
                    b = data[pos]
                    pos += 1
                    dx, dy = (b & 0xF0) >> 4, b & 0x0F
                    if dx & 0x8:
                        dx = -(dx & 0x7)
                    if dy & 0x8:
                        dy = -(dy & 0x7)
                    x += dx
                    y += dy
                    pts.append((x, y))
                self.emit_line(op == OP_SHORT_LINES, pts)

            elif op in (OP_MEDIUM_LINES, OP_MEDIUM_PATTERNS):
                sized = op == OP_MEDIUM_PATTERNS and (pattern_code & PATTERN_SIZE_FLAG)
                if sized:
                    pos += 1
                x, y, pos = abs_coord(data, pos)
                pts = [(x, y)]
                while is_data(data, pos):
                    if sized:
                        pos += 1
                    # dy first as sign-magnitude, then dx as a plain signed byte.
                    b = data[pos]
                    pos += 1
                    y += -(b & 0x7F) if (b & 0x80) else b
                    x += struct.unpack_from("<b", data, pos)[0]
                    pos += 1
                    pts.append((x, y))
                self.emit_line(op == OP_MEDIUM_LINES, pts)

            elif op == OP_LONG_LINES:
                x, y, pos = abs_coord(data, pos)
                pts = [(x, y)]
                while is_data(data, pos):
                    x, y, pos = abs_coord(data, pos)
                    pts.append((x, y))
                self.emit_line(True, pts)

            elif op == OP_FILL:
                n = 0
                while is_data(data, pos):
                    _, _, pos = abs_coord(data, pos)
                    n += 1
                self.emit_line(False, None, unrendered=n)

            elif op == OP_ABSOLUTE_PATTERN:
                n = 0
                while is_data(data, pos):
                    if pattern_code & PATTERN_SIZE_FLAG:
                        pos += 1
                    _, _, pos = abs_coord(data, pos)
                    n += 1
                self.emit_line(False, None, unrendered=n)

            elif op == OP_OPX:
                sub = data[pos]
                pos += 1
                pos = self.opx(sub, pos, start)

            else:
                raise PicError("unknown opcode 0x%02X at 0x%X" % (op, start))

            if op != OP_OPX:
                self.log.append((start, "OP_%02X" % op,
                                 "VISIBLE" if self.visual_on else ""))

        return self

    def opx(self, sub, pos, start):
        data = self.data

        if sub == OPX_SET_PALETTE:
            pal = parse_palette(data, pos)
            # A pic may set the palette more than once; the first is the one the
            # background is drawn under, so keep it and note any others.
            if self.palette is None:
                self.palette = pal
            self.log.append((start, "SET_PALETTE", "1284 bytes"))
            return pos + PALETTE_SIZE

        if sub == OPX_SET_PALETTE_ENTRIES:
            end = pos
            while is_data(data, end):
                end += 1
            self.log.append((start, "SET_PALETTE_ENTRIES", "%d bytes" % (end - pos)))
            return end

        if sub == OPX_PRIORITY_TABLE_EQDIST:
            self.log.append((start, "PRI_TABLE_EQDIST", ""))
            return pos + 4

        if sub == OPX_PRIORITY_TABLE_EXPLICIT:
            self.log.append((start, "PRI_TABLE_EXPLICIT", ""))
            return pos + 14

        if sub == OPX_EMBEDDED_VIEW:
            x, y, pos = abs_coord(data, pos)
            size = struct.unpack_from("<H", data, pos)[0]
            pos += 2
            cel_at = pos
            w, h = struct.unpack_from("<HH", data, cel_at)
            disp_x = struct.unpack_from("<b", data, cel_at + 4)[0]
            disp_y = data[cel_at + 5]
            key = data[cel_at + 6]
            _, _, _, px, mask = decode_cel(data, cel_at)
            self.cels.append((x, y, w, h, px, mask, disp_x, disp_y, key))
            self.draws.append(("cel", x, y, w, h, px, mask))
            self.log.append((start, "EMBEDDED_VIEW",
                             "at %d,%d  %dx%d  size=%d  disp=%d,%d  key=%d"
                             % (x, y, w, h, size, disp_x, disp_y, key)))
            return pos + size

        raise PicError("unknown extended opcode 0xFE 0x%02X at 0x%X" % (sub, start))

    # -- compositing ------------------------------------------------------

    def composite(self):
        """
        Replays every visible draw in stream order, later ones over earlier ones. Only
        pixels the encoder explicitly SKIPPED are holes — a pixel that merely happens
        to equal clearKey is opaque, exactly as in decode_views. Getting that wrong
        shreds the dithered artwork into a checkerboard.
        """
        canvas = bytearray(SCREEN_W * SCREEN_H)
        drawn = bytearray(SCREEN_W * SCREEN_H)   # 1 = something covered this pixel

        def put(x, y, color):
            if 0 <= x < SCREEN_W and 0 <= y < SCREEN_H:
                canvas[y * SCREEN_W + x] = color
                drawn[y * SCREEN_W + x] = 1

        for draw in self.draws:
            if draw[0] == "cel":
                _, x0, y0, w, h, px, mask = draw
                for row in range(h):
                    ty = y0 + row
                    if not (0 <= ty < SCREEN_H):
                        continue
                    src = row * w
                    for col in range(w):
                        if not mask[src + col]:
                            put(x0 + col, ty, px[src + col])
            else:
                _, color, pts = draw
                for i in range(len(pts) - 1):
                    bresenham(pts[i], pts[i + 1], color, put)
                if len(pts) == 1:
                    put(pts[0][0], pts[0][1], color)

        return canvas, drawn


def decode_pic(path):
    pic = Pic(open(path, "rb").read()).parse()
    if pic.palette is None:
        raise PicError("no palette in %s" % path)
    return pic


def render(pic, out_png, opaque=True):
    canvas, drawn = pic.composite()
    mask = None if opaque else bytearray(1 - b for b in drawn)
    return write_png(out_png, SCREEN_W, SCREEN_H, canvas, pic.palette, 0, mask)


def warn_vectors(pic, name):
    """
    Says out loud when something visible is still missing. Lines ARE rasterised, so
    the only gap left is visible fills and brush patterns; pic 11 has none of those,
    pics 0 to 5 do. Silently dropping them would be a lie by omission.
    """
    if pic.unrendered_vectors:
        print("  ! %s: %d visible fill/pattern op(s) NOT rendered (lines are)"
              % (name, pic.unrendered_vectors))


def main():
    args = [a for a in sys.argv[1:]]
    if not args:
        print(__doc__)
        return 1

    if args[0] == "--all":
        raw_dir, out_dir = args[1], args[2]
        os.makedirs(out_dir, exist_ok=True)
        pic_dir = os.path.join(raw_dir, "pic")
        for name in sorted(os.listdir(pic_dir), key=lambda f: int(f.split(".")[0])):
            num = name.split(".")[0]
            try:
                pic = decode_pic(os.path.join(pic_dir, name))
                out = os.path.join(out_dir, "pic_%s.png" % num)
                render(pic, out, opaque="--alpha" not in args)
                print("  %-8s %2d cel(s) -> %s" % (name, len(pic.cels),
                                                   os.path.basename(out)))
                warn_vectors(pic, name)
            except Exception as e:
                print("  %-8s FAILED: %s" % (name, e))
        return 0

    src, out_png = args[0], args[1]
    dump = "--dump" in args
    cels_dir = None
    if "--cels" in args:
        cels_dir = args[args.index("--cels") + 1]
        os.makedirs(cels_dir, exist_ok=True)

    pic = decode_pic(src)

    if dump:
        for off, name, detail in pic.log:
            print("0x%06X  %-20s %s" % (off, name, detail))
        print()

    print("embedded cels: %d" % len(pic.cels))
    for i, (x, y, w, h, _, _, dx, dy, key) in enumerate(pic.cels):
        print("  [%2d] %3dx%-3d at (%3d,%3d)  disp=(%d,%d) key=%d"
              % (i, w, h, x, y, dx, dy, key))

    if cels_dir:
        for i, (x, y, w, h, px, mask, _, _, key) in enumerate(pic.cels):
            write_png(os.path.join(cels_dir, "cel_%02d_%dx%d_at_%d_%d.png"
                                   % (i, w, h, x, y)),
                      w, h, px, pic.palette, key, mask)

    warn_vectors(pic, os.path.basename(src))
    render(pic, out_png, opaque="--alpha" not in args)
    print("wrote %s" % out_png)
    return 0


if __name__ == "__main__":
    sys.exit(main())
