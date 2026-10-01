"""THE DATA for every renamed board building. One table, no per-building code.

WHY THIS FILE EXISTS
--------------------
`gen_university.py`, `gen_employment.py` and `gen_factory.py` were three copies of one
pipeline that had already begun to drift. Nine buildings remain, and a copy per building is
nine chances for a fix made in one to be missing from the others - which is exactly how the
picPatch bug survived two renames.

Three real buildings were enough to see the shape. EVERY difference between them turned out
to be a parameter; none needed its own code path:

    the University      a CEL, sign band inside it, lettering overpainted
    the Employment Office  a CEL, sign band inside it, surroundings kept by rectangle
    the Factory         BACKGROUND art with a SEPARATE sign cel, surroundings by colour
    Socket City         a CEL, name painted across the glass, surroundings by colour

So the engine (`gen_building.py`, `install_building.py`) reads this and nothing else.

WHAT EVERY ENTRY HAS TO GET RIGHT, AND WHY
-------------------------------------------
`region`   GENERATE THE BUILDING, NOT ITS HOTSPOT. The Factory cost six rounds to learn
           this: its hotspot is 61x38 and the building is 35% of it, so the model spent its
           detail on scenery the blend discards. Cropping to the building took it to 94%
           and fixed it in one round. The hotspot is a CLICK TARGET.
`init`     Flat colour boxes, all colours SAMPLED FROM SIERRA, no texture. Nothing thinner
           than 2px survives the blur, so nothing here is drawn thinner.
`prompt`   MATERIAL ONLY. No colour word may appear: colour lives in the init, and a colour
           word in the prompt spends budget on something already said while starving the
           one thing the prompt can contribute. This is the change that turned Hire Ground
           from a cartoon into a photograph.
`surround` How to tell building from scene, so the blend keeps Sierra's surroundings. A
           bounding box is NOT good enough for an irregular building - it pastes a slab.
"""

from dataclasses import dataclass, field


@dataclass
class Surfaces:
    """A painted name somewhere other than the board. Found by find_name_art.py, never
    from a list someone wrote: that search is what turned up view 706 cel 1, which was on
    nobody's."""
    file: str
    plate: tuple          # (x0, y0, x1, y1) in the view's own coordinates
    body: tuple           # colour to repaint the plate
    ink: tuple            # lettering colour, or the first stop of a ramp
    lines: list           # what to write
    ramp: list = field(default_factory=list)


@dataclass
class Building:
    key: str
    sierra: str
    new_name: str
    source: str                 # "cel:NN" or "background"
    region: tuple               # (x, y, w, h) in game coords - WHAT TO GENERATE
    hotspot: tuple              # (l, t, r, b) from Board.All, for the footprint assertion
    palette: dict               # NAME -> (r,g,b), every one sampled off Sierra's art
    init: list                  # [(x0,y0,x1,y1,"NAME")] region-local, painted in order
    prompt: str                 # material vocabulary, NO COLOUR WORDS
    surround: dict              # {"mode": ...} - how the blend keeps Sierra's scene
    name_lines: list            # the new name, already broken into lines
    name_rect: tuple            # region-local box the lettering goes in
    name_ink: tuple
    name_plate: str = None      # palette key to repaint under the name, or None
    device_rect: tuple = None   # region-local box for a decorative device, or None
    devices: list = field(default_factory=list)
    surfaces: list = field(default_factory=list)
    scale: int = 16
    strength: float = 0.88
    control: float = 0.35
    cfg: float = 9.0
    steps: int = 32
    seed: int = 7
    notes: str = ""


# The negative prompt is SHARED because nothing in it is building-specific, and because a
# per-building copy is how `flat` got deleted from one of them. `flat` stays; new terms take
# space from synonyms, never from a term that predates the problem being solved.
NEGATIVE = ("text, letters, words, writing, lettering, signage, numbers, logo, watermark, "
            "blurry, out of focus, bokeh, 3d render, cgi, vector, illustration, flat, "
            "cartoon, pixelated, jpeg artifacts, noise, people, distorted, frame, "
            "vignette, smoke, steam, fog")


BUILDINGS = {}


# --- Socket City -> Watt a Bargain ------------------------------------------------
# Cel 8, 44x52 at (255,132), 100% OPAQUE - so its alpha is no silhouette and the foliage in
# its corners has to be kept by colour. Measured masses: blue glass 61%, dark glazing 12%,
# red script 11.5% at x 1..42, y 15..35.
#
# The best subject of the four so far: a curved glazed frontage with display windows, which
# is real glass with real reflections rather than a flat wall.
BUILDINGS["socketcity"] = Building(
    key="socketcity",
    sierra="Socket City",
    new_name="Watt a Bargain",
    source="cel:08",
    region=(255, 132, 44, 52),
    hotspot=(251, 133, 311, 192),
    palette={
        "GLASS":   (0x78, 0xA8, 0xD0),   # the curved frontage, 11.5% of the cel
        "GLASS_LO": (0x60, 0x98, 0xC8),
        "GLASS_HI": (0xA0, 0xC0, 0xE0),
        "MULLION": (0x50, 0x70, 0x98),
        "DARK":    (0x20, 0x20, 0x20),   # inside the display windows
        "GOODS":   (0x70, 0x80, 0x90),   # appliance shapes behind the glass
        "FOLIAGE": (0x20, 0x78, 0x60),   # the trees in the corners - KEPT, not generated
        "BASE":    (0x90, 0x48, 0x30),   # the plinth under the shopfront
        "SKY":     (0x58, 0x90, 0xE0),
    },
    init=[
        # Order matters: later boxes paint over earlier ones.
        #
        # The first draft of this was a flat rectangle and it threw away the two things
        # that make the building what it is - Sierra's DOMED CROWN, and any structure at
        # all across the 60% of the canvas that is glass. A featureless slab gives the
        # model nothing to grip and comes back as a blank wall.
        (0, 0, 43, 51, "SKY"),
        # the crown, stepped so it reads as a curve once blurred - 2px steps, never 1
        (8, 1, 35, 3, "GLASS_LO"),
        (5, 3, 38, 6, "GLASS_LO"),
        (3, 6, 40, 9, "GLASS"),
        (2, 9, 41, 37, "GLASS"),
        # Glazing bars in the UPPER glass only. Sierra's script sits on open glass and the
        # new name goes in the same place, so bars there would fight the lettering.
        (3, 11, 40, 12, "MULLION"),
        (3, 14, 40, 15, "MULLION"),
        (12, 7, 13, 15, "MULLION"),
        (22, 7, 23, 15, "MULLION"),
        (31, 7, 32, 15, "MULLION"),
        (4, 7, 11, 10, "GLASS_HI"),       # a highlight raking across the crown
        (14, 7, 21, 10, "GLASS_HI"),
        (2, 34, 41, 37, "MULLION"),       # the transom above the shopfront
        (3, 38, 40, 47, "DARK"),          # the display windows
        (5, 40, 12, 46, "GOODS"),         # appliances behind the glass - 8x7, survives blur
        (18, 40, 25, 46, "GOODS"),
        (31, 40, 38, 46, "GOODS"),
        (14, 38, 16, 47, "MULLION"),      # the two dividers, 3px so they survive
        (27, 38, 29, 47, "MULLION"),
        (0, 48, 43, 51, "BASE"),          # the plinth
    ],
    # Glass, reflection and depth behind it - the vocabulary the Factory proved. Not one
    # colour word: GLASS, DARK and FOLIAGE above already say every colour in the picture.
    prompt=("a photograph of a modern glass fronted appliance showroom, curved glazed "
            "facade, polished aluminium mullions, reflections and depth behind the glass, "
            "lit interior with appliances on display, brushed steel, bright overcast "
            "daylight, sharp focus, fine detail, architectural photography"),
    # The corners carry Sierra's trees. Green is the one thing in this cel that is not the
    # building, and nothing in the building is green.
    surround={"mode": "green", "g_over": 20, "g_max": 170},
    name_lines=["WATT A", "BARGAIN"],
    # Measured: her script occupies x 1..42, y 15..35. The device takes the left of that
    # and the name the rest - 13 + 2 + 25 = 40 of the 42 available.
    name_rect=(16, 16, 42, 33),
    name_ink=(0xC0, 0x38, 0x38),          # her own red, so the sign stays her sign
    device_rect=(1, 16, 13, 33),
    devices=["plug", "bulb"],
    surfaces=[
        Surfaces(file="view_808_l0_c0.png",
                 plate=(70, 2, 180, 22),
                 body=(0x58, 0x90, 0xE0),
                 ink=(0xC0, 0x38, 0x38),
                 lines=["WATT A BARGAIN"]),
    ],
    notes=("The letter S of `Socket` is drawn as an electrical PLUG - a device and a letter "
           "at once. It is 36% of the red pixels with a 10x13 body, so the name takes most "
           "of the area and a standalone device fits beside it. `Watt a Bargain` puns on "
           "the watt rather than the socket, so a filament bulb is the more apt device; "
           "both are generated and the user picks from the picture."),
)


# --- the three already shipped, recorded so the table is the whole record ----------
# These are INSTALLED AND VERIFIED and are not re-run. They are here because a table that
# describes only the next building is not a record, and because the next person needs to see
# that `source` and `surround` genuinely differ between them.
BUILDINGS["factory"] = Building(
    key="factory", sierra="Factory", new_name="The Industrial Revolution",
    source="background", region=(26, 155, 32, 27), hotspot=(7, 155, 67, 192),
    palette={"BRICK": (0x58, 0x40, 0x30), "STACK": (0x20, 0x28, 0x30),
             "FURNACE": (0x78, 0x58, 0x38), "WIN": (0x38, 0x28, 0x18),
             "YARD": (0x8A, 0x85, 0x80)},
    init=[], prompt="(see gen_factory.py - shipped before the extraction)",
    surround={"mode": "blue_light", "b_min": 130},
    name_lines=["THE INDUSTRIAL", "REVOLUTION"], name_rect=None,
    name_ink=(0x30, 0x58, 0x48),
    notes="Sign is a SEPARATE CEL (11) on a hanging board, not part of the building.",
)
BUILDINGS["employment"] = Building(
    key="employment", sierra="Employment Office", new_name="Hire Ground",
    source="cel:10", region=(75, 151, 47, 34), hotspot=(68, 158, 128, 192),
    palette={"WALL": (0xF8, 0xA0, 0x68), "PLATE": (0xC0, 0x38, 0x38)},
    init=[], prompt="(see gen_employment.py - shipped before the extraction)",
    surround={"mode": "rects", "keep": [(1, 1, 45, 13), (2, 14, 44, 31)]},
    name_lines=["HIRE", "GROUND"], name_rect=(1, 1, 45, 13),
    name_ink=(0xD0, 0xD8, 0xE0),
    notes="Sign band INSIDE the building cel. picPatch carries a second copy of it.",
)
BUILDINGS["university"] = Building(
    key="university", sierra="Hi-Tech University", new_name="Open Door University",
    source="cel:09", region=(191, 141, 59, 41), hotspot=(190, 158, 250, 192),
    palette={"WALL": (0x9C, 0x8A, 0x6E)},
    init=[], prompt="(see gen_university.py - shipped before the extraction)",
    surround={"mode": "rects", "keep": [(0, 0, 58, 40)]},
    name_lines=["OPEN DOOR", "UNIVERSITY"], name_rect=(3, 11, 55, 24),
    name_ink=(0xE0, 0x80, 0x40),
    notes="picPatch carries a second copy of its sign.",
)
