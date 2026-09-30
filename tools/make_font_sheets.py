"""Font comparison sheets for `tools/samples/`.

SAMPLES ONLY. Nothing here is imported by the game, and nothing here writes into
`src/` or `assets/`. It reads the game's own font resources and the port's own
layout tables and draws PNG sheets so a human can choose a face.

What it shows, per sheet:

  * the CURRENT rendering  - `assets/raw/font/10.font` (or 14), pixel-replicated,
    which is exactly what `SciFont.Rasterise` does;
  * each candidate NAIVE   - the port's existing padded strings (spaces, leader
    dots and 1px `|` shims) poured into the new face unchanged;
  * each candidate MEASURED- the same data, laid out by measuring the new face
    and placing each column explicitly at the stop the original holds.

Two scales:

  * 12x  - `RenderScale.Requested`. 320x200 -> 3840x2400, which a 3840x2160 panel
    fits to 3456x2160, i.e. 10.8 device pixels per game pixel.
  * 3.375x - the Android head. `MainView`'s Viewbox is Stretch="Uniform" with
    BitmapInterpolationMode="None", and the Android head ships no `assets/pngNx`
    so `RenderScale.Factor` falls to 1. On a 1080-wide portrait screen the 8:5
    playfield is width-limited: 1080/320 = 3.375, giving a 1080x675 playfield.

The bitmap panels are drawn at 1x and then nearest-neighbour enlarged, because
that is the pipeline (1x raster -> Viewbox with interpolation None). The TTF
panels are drawn at the target resolution, because that is what a TTF would
actually give you.
"""

import os
import re

from PIL import Image, ImageDraw, ImageFont

ROOT = r"C:\AI-Accelerator\jones"
FONT_RES = os.path.join(ROOT, "assets", "raw", "font")
TTF_DIR = os.path.join(ROOT, "tools", "fonts")
OUT_DIR = os.path.join(ROOT, "tools", "samples")

PHONE_SCALE = 1080.0 / 320.0          # 3.375
FOURK_SCALE = 12.0                    # RenderScale.Requested


# --------------------------------------------------------------------------
# The game's palette, parsed out of SciPalette.cs so no colour is invented.
# --------------------------------------------------------------------------

def load_palette():
    src = open(os.path.join(ROOT, "src", "Jones.App", "Jones.App", "SciPalette.cs"),
               encoding="utf-8", errors="replace").read()
    start = src.index("private static readonly uint[] Rgb =")
    body = src[start:src.index("];", start)]
    vals = [int(m, 16) for m in re.findall(r"0x([0-9A-Fa-f]{6})", body)]
    return [((v >> 16) & 255, (v >> 8) & 255, v & 255) for v in vals]


PAL = load_palette()


def pal(i):
    return PAL[i]


# --------------------------------------------------------------------------
# SciFont, ported from src/Jones.App/Jones.App/SciFont.cs
#   u16 unused, u16 charCount, u16 height, u16 offset[charCount]
#   glyph: u8 width, u8 height, ceil(w/8)*h bytes, MSB leftmost
# --------------------------------------------------------------------------

class SciFont:
    scale = 1

    def __init__(self, number):
        self.data = open(os.path.join(FONT_RES, "%d.font" % number), "rb").read()
        self.count = int.from_bytes(self.data[2:4], "little")
        self.height = int.from_bytes(self.data[4:6], "little")

    def glyph(self, ch):
        i = ord(ch)
        if i >= self.count:
            i = ord("?")
        off = int.from_bytes(self.data[6 + i * 2:8 + i * 2], "little")
        return self.data[off], self.data[off + 1], off + 2

    def measure(self, text):
        return sum(self.glyph(c)[0] for c in text)

    def blit(self, draw, text, x, y, rgb):
        pen = int(round(x))
        y = int(round(y))
        for c in text:
            gw, gh, off = self.glyph(c)
            per = (gw + 7) // 8
            for row in range(min(gh, self.height)):
                for col in range(gw):
                    b = self.data[off + row * per + col // 8]
                    if (b >> (7 - (col % 8))) & 1:
                        draw.point((pen + col, y + row), rgb)
            pen += gw


F10 = SciFont(10)           # THE interface font: height 6, space 5px, `|` 1px blank
F14 = SciFont(14)           # the calculator readout


# --------------------------------------------------------------------------
# The candidates.  All five are SIL Open Font License 1.1; each face's own
# OFL.txt was fetched alongside it into tools/fonts/.
# --------------------------------------------------------------------------

CANDIDATES = [
    ("Nunito Sans", "NunitoSans.ttf", "Regular"),
    ("Quicksand", "Quicksand.ttf", "Medium"),
    ("Inter", "Inter.ttf", "Regular"),
    ("IBM Plex Sans", "IBMPlexSans.ttf", "Regular"),
    ("Silkscreen", "Silkscreen-Regular.ttf", None),
]

CAP_TARGET = 5.0            # font 10's capital height, in game pixels


def load_ttf(fname, instance, px):
    f = ImageFont.truetype(os.path.join(TTF_DIR, fname), px)
    if instance:
        f.set_variation_by_name(instance)
    return f


def cap_em(fname, instance):
    """Em size, in game pixels, that puts the cap height at CAP_TARGET."""
    probe = 480.0
    f = load_ttf(fname, instance, probe)
    box = f.getbbox("H")
    em = probe * CAP_TARGET / (box[3] - box[1])
    if fname.startswith("Silkscreen"):
        em = round(em)          # a pixel font must stay on whole pixels
    return em


EM = {name: cap_em(fname, inst) for name, fname, inst in CANDIDATES}


class Ttf:
    """A candidate face at one output scale."""

    def __init__(self, name, fname, instance, scale):
        self.name = name
        self.scale = scale
        self.em = EM[name]
        self.font = load_ttf(fname, instance, self.em * scale)
        # Put the cap top where font 10 puts it: on the row the label's y names.
        self.cap_top = self.font.getbbox("H")[1]

    def measure(self, text):
        return self.font.getlength(text) / self.scale

    def blit(self, draw, text, x, y, rgb):
        draw.text((x * self.scale, y * self.scale - self.cap_top), text,
                  font=self.font, fill=rgb)


# --------------------------------------------------------------------------
# Row model.
#
# `pad` is the port's verbatim string from ItemText / StoreLayout - trailing
# spaces, leader dots and 1px `|` shims and all.  `value` is what the game's
# doFormat appends.  Every column stop is MEASURED off the game font from those
# two, so the "measured" variant targets the stop the shipped game holds.
# --------------------------------------------------------------------------

class Row:
    def __init__(self, left, top, pad, value, font=F10, mode="column", band=None):
        self.left, self.top = left, top
        self.pad, self.value = pad, value
        self.font = font
        self.mode = mode                       # "column" | "centre"
        self.band = band                       # dsBACKGROUND palette index
        self.full = pad + value
        self.width = font.measure(self.full)
        self.stop = left + self.width

        # The bare name: everything before the run of padding.
        self.name = pad.rstrip(" .|")
        self.leader = "." in pad[len(self.name):]

        # Right edge of each whitespace-separated token of `value`, measured in
        # the game font from the string the game itself builds.
        self.tokens = []
        i = len(pad)
        for tok in value.split():
            j = self.full.index(tok, i)
            self.tokens.append((tok, left + font.measure(self.full[:j + len(tok)])))
            i = j + len(tok)

        self.centre = left + self.width / 2.0


def row_boxes(r, renderer, mode):
    """(text, x) pairs this row would draw, in game coordinates."""
    if mode in ("asis", "naive"):
        return [(r.full, r.left)]

    if r.mode == "centre":
        return [(r.name, r.centre - renderer.measure(r.name) / 2.0)]

    out = []
    xs = [(tok, right - renderer.measure(tok)) for tok, right in r.tokens]
    name_w = renderer.measure(r.name)
    if r.leader and xs:
        gap = xs[0][1] - (r.left + name_w)
        dot = renderer.measure(".")
        n = max(0, int((gap - dot) / dot)) if dot > 0 else 0
        out.append((r.name + "." * n, r.left))
    else:
        out.append((r.name, r.left))
    out.extend(xs)
    return out


def draw_row(draw, r, renderer, mode, text_rgb, shadow_rgb):
    sc = renderer.scale
    for s, x in row_boxes(r, renderer, mode):
        if r.band is not None and s:
            w = renderer.measure(s)
            draw.rectangle([round(x * sc), round(r.top * sc),
                            round((x + w) * sc) - 1, round((r.top + r.font.height) * sc) - 1],
                           fill=pal(r.band))
        if shadow_rgb is not None:
            renderer.blit(draw, s, x + 1, r.top + 1, shadow_rgb)
        renderer.blit(draw, s, x, r.top, text_rgb)


# --------------------------------------------------------------------------
# Panels
# --------------------------------------------------------------------------

PANEL_BG = pal(7)           # #D8D8D8 - sample chrome, not a game colour choice
GUIDE = (208, 48, 48)


class Sheet:
    def __init__(self, key, title, blurb, origin, size, rows, colours,
                 guides=(), panel_bg=PANEL_BG):
        self.key, self.title, self.blurb = key, title, blurb
        self.ox, self.oy = origin
        self.pw, self.ph = size
        self.rows = rows
        self.text, self.shadow = colours
        self.guides = guides
        self.panel_bg = panel_bg


class Shifted:
    """A renderer with the panel origin subtracted."""

    def __init__(self, inner, ox, oy):
        self.inner, self.ox, self.oy = inner, ox, oy
        self.scale = inner.scale

    def measure(self, s):
        return self.inner.measure(s)

    def blit(self, dd, s, x, y, rgb):
        self.inner.blit(dd, s, x - self.ox, y - self.oy, rgb)


def render_panel(sheet, renderer, mode, scale):
    w = int(round(sheet.pw * scale))
    h = int(round(sheet.ph * scale))
    bitmap = isinstance(renderer, SciFont)

    img = Image.new("RGB", (sheet.pw, sheet.ph) if bitmap else (w, h), sheet.panel_bg)
    d = ImageDraw.Draw(img)

    for r in sheet.rows:
        inner = r.font if bitmap else renderer
        draw_row(d, r, Shifted(inner, sheet.ox, sheet.oy), mode, sheet.text, sheet.shadow)

    if bitmap:
        img = img.resize((w, h), Image.NEAREST)

    # The column stops the original holds, drawn AFTER scaling so the hairline
    # is the same weight in every panel and a collapse is impossible to miss.
    d = ImageDraw.Draw(img)
    lw = max(2, int(scale / 3))
    for gx in sheet.guides:
        x = int(round((gx - sheet.ox) * scale))
        d.line([(x, 0), (x, h - 1)], fill=GUIDE, width=lw)
    return img


# --------------------------------------------------------------------------
# Sheet chrome
# --------------------------------------------------------------------------

UI = os.path.join(TTF_DIR, "Inter.ttf")


def ui(px, weight="Regular"):
    f = ImageFont.truetype(UI, px)
    f.set_variation_by_name(weight)
    return f


INK = (24, 24, 28)
MUTED = (104, 106, 114)
WARN = (168, 44, 44)
RULE = (196, 198, 204)
SHEET_BG = (250, 250, 251)


def compose(sheet):
    sections = [
        ("4K  -  12x   (RenderScale.Requested: 3840x2400 canvas, fitted to 3456x2160 "
         "on a 3840x2160 panel = 10.8 device px per game px)", FOURK_SCALE),
        ("PHONE  -  3.375x   (Android head: RenderScale.Factor 1, Viewbox Uniform / "
         "interpolation None, 1080-wide portrait -> a 1080x675 playfield)", PHONE_SCALE),
    ]

    blocks = []
    for slabel, scale in sections:
        rows = [[("CURRENT  -  the game's bitmap font, pixel-replicated "
                  "(this is what SciFont.Rasterise draws today)",
                  render_panel(sheet, F10, "asis", scale)), None]]
        for name, fname, inst in CANDIDATES:
            r = Ttf(name, fname, inst, scale)
            rows.append([
                ("%s  -  NAIVE   (the existing padded strings, unchanged)" % name,
                 render_panel(sheet, r, "naive", scale)),
                ("%s  -  MEASURED   (columns placed by measuring the face)" % name,
                 render_panel(sheet, r, "measured", scale)),
            ])
        blocks.append((slabel, rows, scale))

    pad, gut = 44, 34
    cap_h, sec_h = 46, 86
    cell_w = blocks[0][1][0][0][1].width
    total_w = pad * 2 + cell_w * 2 + gut

    head = 300
    y = head
    for slabel, rows, scale in blocks:
        y += sec_h
        ch = rows[0][0][1].height
        y += len(rows) * (ch + cap_h + gut)
    total_h = y + pad

    img = Image.new("RGB", (int(total_w), int(total_h)), SHEET_BG)
    d = ImageDraw.Draw(img)

    d.text((pad, 40), sheet.title, font=ui(60, "Bold"), fill=INK)
    ty = 124
    for line in sheet.blurb:
        d.text((pad, ty), line, font=ui(26), fill=MUTED)
        ty += 36
    d.text((pad, ty + 4),
           "The red hairline is the column stop the shipped game holds, measured off the "
           "game font. NAIVE lines that miss it are the collapse.",
           font=ui(26, "SemiBold"), fill=WARN)

    y = head
    for slabel, rows, scale in blocks:
        d.line([(pad, y), (total_w - pad, y)], fill=RULE, width=4)
        d.text((pad, y + 18), slabel, font=ui(34, "SemiBold"), fill=INK)
        y += sec_h
        ch = rows[0][0][1].height
        for ri, cells in enumerate(rows):
            yy = y + ri * (ch + cap_h + gut)
            for ci, cell in enumerate(cells):
                if cell is None:
                    continue
                cap, im = cell
                x = pad + ci * (cell_w + gut)
                bold = "MEASURED" in cap or "CURRENT" in cap
                d.text((x, yy + 6), cap, font=ui(27, "SemiBold" if bold else "Regular"),
                       fill=INK if bold else WARN)
                img.paste(im, (int(x), int(yy + cap_h)))
                d.rectangle([x - 1, yy + cap_h - 1, x + im.width, yy + cap_h + im.height],
                            outline=RULE)
        y += len(rows) * (ch + cap_h + gut)

    out = os.path.join(OUT_DIR, "font_%s.png" % sheet.key)
    img.save(out)
    return out, img.size


# --------------------------------------------------------------------------
# The data, from the port's own tables.
# --------------------------------------------------------------------------

def with_price(price):
    """ItemText.WithPrice - CostDItem's format (WButton.sc:185)."""
    return " $%d" % price if price < 100 else "$%d" % price


def with_wage(wage):
    """ItemText.WithWage - text resource 206 index 0 (under $10) or 1."""
    return "  $%d Hr." % wage if wage < 10 else " $%d Hr." % wage


# Monolith Burgers: StoreLayout.MonolithBurgers x ItemText.Items x Catalogue
# (prices at goods index 100), in the order fastFood.sc declares them.
MONOLITH = [
    ("Hamburgers.........",       10, 33,  79),
    ("Cheeseburger.......|",      10, 45,  89),
    ("Astro Chicken.......|",     75, 63, 124),
    ("Fries....................", 75, 74,  65),
    ("Shakes.................|",  75, 86, 102),
    ("Colas...................|", 75, 97,  69),
]

# The Factory's nine jobs: ItemText.Jobs + Jobs.cs base wages, declaration order.
FACTORY = [
    ("Janitor            |||",     7),
    ("Assembly Worker    ||||",    8),
    ("Secretary          ||",      9),
    ("Machinist's Helper   |",    10),
    ("Executive Secretary ||",    18),
    ("Machinist           ",      19),
    ("Department Manager ",       22),
    ("Engineer            ",      23),
    ("General Manager    ||",     25),
]

EMPLOYERS = [
    ("Z-Mart Discount",       23, 37),
    ("Monolith Burgers",      22, 45),
    ("QT Clothing",           35, 53),
    ("Socket City Appliance", 12, 61),
    ("Open Door University",  12, 69),   # renamed; see StoreLayout.Employment / PARITY.md
    ("Factory",               41, 77),
    ("Bank",                  48, 85),
    ("Black's Market",        26, 93),
    ("Rent Office",           35, 101),
]


def build_shop():
    rows = [Row(l, t, pad, with_price(p)) for pad, l, t, p in MONOLITH]
    return Sheet(
        "shop_list",
        "Monolith Burgers  -  shop list and price column",
        ["Strings verbatim from ItemText.Items, prices from Catalogue at goods index 100, "
         "positions from StoreLayout.MonolithBurgers (nsLeft 10 / 75, nsTop 33..97).",
         "Font 10 measures the labels at 79px in the left column and 81px in the right; "
         "with CostDItem's format appended, every price's right edge lands at 110 on the "
         "left and 177 on the right.",
         "Text colour 27, shadow 116 - StoreLayout.ColoursFor(MonolithBurgers). The flat "
         "panel fill is sample chrome, not a game colour."],
        (0, 26), (196, 86), rows, (pal(27), pal(116)),
        guides=sorted({r.stop for r in rows}))


def build_jobs():
    hdr, hl, ht, first, spacing = "Factory Jobs Available:", 40, 19, 30, 8
    rows = [Row(hl, ht, hdr, "")]
    rows += [Row(25, first + i * spacing, pad, with_wage(w))
             for i, (pad, w) in enumerate(FACTORY)]
    return Sheet(
        "job_list",
        "The Factory  -  nine jobs and the wage column",
        ["Labels verbatim from ItemText.Jobs, wages from Jobs.cs, geometry from "
         "StoreLayout.JobList(Factory): header at (40,19), first row 30, 8px spacing, "
         "nsLeft 25.",
         "All nine padded labels measure to the same width in font 10, so '$N' ends at 141 "
         "and 'Hr.' at 159 on every row - two columns, held by spaces and 1px '|' shims.",
         "The wage uses TWO format strings (res 206[0] '%s  $%d Hr.' under $10, 206[1] "
         "'%s $%d Hr.' at or above), which is what keeps $7 and $25 in one column.",
         "8px spacing on a 6px font is the tightest list in the game - the row that "
         "punishes a taller face hardest."],
        (0, 14), (196, 90), rows, (pal(0), pal(107)),
        guides=sorted({r.stop for r in rows[1:]}))


def build_employers():
    rows = [Row(l, t, name, "", mode="centre") for name, l, t in EMPLOYERS]
    return Sheet(
        "employers",
        "The Employment Office  -  nine employers",
        ["Labels and nsLeft/nsTop verbatim from StoreLayout.Employment; colour 0, shadow 80 "
         "(employment.sc:387 and its eight siblings).",
         "No price column here, so the invariant is different: every nsLeft was chosen so "
         "that the label CENTRES on x=58.5 in the dialog at font 10's widths (23+72/2, "
         "22+74/2, 12+92/2 ... all land within half a pixel of each other).",
         "NAIVE keeps each nsLeft and lets the wider face run right off centre; MEASURED "
         "re-centres each label on the centre the original holds."],
        (0, 30), (196, 84), rows, (pal(0), pal(80)),
        guides=[58.5])


def build_captions():
    rows = []
    # `Week #%2d` - MainViewModel.cs:2173: font 10, colour 0, dsBACKGROUND 86.
    for i, wk in enumerate([1, 7, 9, 10, 12]):
        rows.append(Row(6, 8 + i * 12, "Week #", "%2d" % wk, band=86))
    # The calculator readout - room1.sc:1437-1455: text 1[3] "%6s ", FONT 14,
    # colour 0, dsBACKGROUND 101, drawn LEFT-aligned from (CalcLeft+22, CalcTop+6).
    for i, cash in enumerate([1, 47, 300, 1250, 25000]):
        rows.append(Row(74, 8 + i * 12, "", "%6s " % cash, font=F14, band=101))
    return Sheet(
        "captions",
        "Board captions  -  'Week #%2d' and the calculator readout",
        ["'Week #%2d' is font 10, colour 0, on an opaque dsBACKGROUND 86 band "
         "(MainViewModel.cs:2173). The calculator readout is text 1[3] '%6s ' in FONT 14, "
         "colour 0, on a dsBACKGROUND 101 band (room1.sc:1437-1455).",
         "Both pad with SPACES to hold a column. Font 14's space is 4px and its digits are "
         "5px, so the readout's right edge already creeps 99 -> 103 as the figure grows "
         "from $1 to $25000. That slip is in the shipped game and is deliberately ported.",
         "The opaque band is the width of the string, so it grows with it - NAIVE in a "
         "proportional face makes both the band and the figure wander."],
        (0, 0), (150, 68), rows, (pal(0), None),
        panel_bg=(198, 200, 204),
        guides=[45, 103])


# --------------------------------------------------------------------------

def report(sheet):
    print("\n== %s" % sheet.title)
    for r in sheet.rows:
        print("   %-26s left %5.1f  width %3dpx  stop %5.1f   %s"
              % ("'" + r.name + "'", r.left, r.width, r.stop,
                 "  ".join("%s right=%d" % t for t in r.tokens)))

    groups = {}
    for r in sheet.rows:
        if r.tokens:
            groups.setdefault(round(r.tokens[-1][1]), []).append(r)
    if not groups:
        return
    print("   -- naive column spread (right edge of the last value token) --")
    for name, fname, inst in CANDIDATES:
        t = Ttf(name, fname, inst, 1.0)
        parts = []
        for stop, rs in sorted(groups.items()):
            edges = [r.left + t.measure(r.full) for r in rs]
            parts.append("stop %d: %.1f..%.1f  spread %.1fpx (%.0f phone px)"
                         % (stop, min(edges), max(edges), max(edges) - min(edges),
                            (max(edges) - min(edges)) * PHONE_SCALE))
        print("      %-14s %s" % (name, " | ".join(parts)))


if __name__ == "__main__":
    os.makedirs(OUT_DIR, exist_ok=True)
    print("font 10: height %d, space %dpx, '|' %dpx, '.' %dpx"
          % (F10.height, F10.measure(" "), F10.measure("|"), F10.measure(".")))
    print("font 14: height %d, space %dpx, '0' %dpx"
          % (F14.height, F14.measure(" "), F14.measure("0")))
    print("\nem size for a 5px cap height, in game pixels:")
    for k, v in EM.items():
        print("   %-14s %.2f px   (%.0f px at 12x, %.0f px at 3.375x)"
              % (k, v, v * 12, v * PHONE_SCALE))

    sheets = [build_shop(), build_jobs(), build_employers(), build_captions()]
    for s in sheets:
        report(s)
    print()
    for s in sheets:
        out, size = compose(s)
        print("wrote %s  %dx%d" % (out, size[0], size[1]))
