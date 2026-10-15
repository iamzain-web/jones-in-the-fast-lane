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
class Detect:
    """Where a building's painted name is, so `find_name_art.py` can look for it.

    THE RECTANGLE MUST BE DENSE WITH LETTERING, and that is not a style note - it is the
    rule that makes the acceptance check mean anything.

    QT Clothing's first mask spanned both of her bands, y 2..13, which swept in the dark gap
    between them. The box was then mostly background, so the score stopped describing the
    LETTERFORMS and started describing "some coloured letters on a dark band": it reported
    EIGHTEEN surfaces, including view 706's gold-on-green employers panel, and it flagged
    this port's own `COMMON THREADS` as Sierra's name still being present. **A check that
    cannot tell a rename from a non-rename is worse than no check.**

    BUT DENSITY HAS AN UPPER BOUND TOO, which Black's Market found the hard way. Her
    `market` band sits on a LIGHT ground, so a brightness predicate over it is 88-100% TRUE
    - and a mask that is nearly all TRUE is as useless as one that is nearly all FALSE: it
    stops encoding glyph shapes and starts matching any bright rectangle. Her `black's`
    band is light-on-dark and measures 33%, which discriminates.

    **Aim for roughly 25-55% of the rectangle being lettering.** `find_name_art.py` prints
    the density of every mask and warns outside that range, so this is checked rather than
    remembered. One good band beats two, and a small second line only dilutes.
    """
    label: str
    source: str         # "cel" to use the Building's own cel, or a view filename
    rect: tuple         # (x0, y0, x1, y1) in that image's coordinates
    test: str           # the name of a predicate in find_name_art.py


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
    # ONE RECT PER LINE, when the lines do not share a band. QT Clothing is the first of
    # five to need it: Sierra puts `Q.T` on a narrow gable and `CLOTHING` across the
    # full-width awning below, two different widths with different backgrounds. Setting
    # both lines in one box would centre them on a common width and throw away a hierarchy
    # she drew deliberately. Everything else about QT was already covered by the table.
    name_rects: list = None
    name_plate: str = None      # palette key to repaint under the name, or None
    # A PHYSICAL SIGN, built by tools/sign_board.py at the GENERATION's resolution and then
    # married to the facade with a masked low-denoise img2img. Set this and `name_rect`,
    # `name_rects` and `name_plate` are not used: the six that shipped before it stamped
    # flat type into the already-downscaled cel, and Zain's verdict on that was "the signs
    # look like words placed over an image". See sign_board.py for what each key does.
    # A rectangle of Sierra's ORIGINAL art to restore before the building is pasted, in
    # GAME coords. Sized to every earlier install's footprint, so nothing of theirs can
    # survive outside the new region. See install_building.py for why this is not `region`.
    # Palette keys in `init` that are SCENERY rather than building. With
    # `edge_feather` they are faded into her art at the region border, so a fully
    # generated region does not read as a rectangle. See install_building.py.
    # [(x0,y0,x1,y1,face,face_hi)] region-local: bands painted deterministically AFTER the
    # generation, for a strip the model keeps writing on. See install_building.py.
    trims: list = None
    scenery: tuple = ()
    edge_feather: int = 0
    clear: tuple = None
    # [(x0,y0,x1,y1,dx,dy)] in GAME coords: after `clear`, copy her art from
    # an offset into this rectangle. For painting out something her own
    # BACKGROUND contains - see install_building.py.
    clear_copy: list = None
    # [(x0,y0,x1,y1)] in GAME coords: rectangles of the board to REBUILD AS LANDSCAPE,
    # diffused from the board's own pixels just outside them. For the thing `clear` cannot
    # fix because `clear` is what puts it there - the REGENERATED board's own copy of one
    # of her buildings, standing outside the region where no install can reach it. See
    # install_building.landscape_fill.
    board_fill: list = None
    sign: dict = None
    # The ControlNet conditioning's blur, as a multiple of the init's own gen_w/60. 1.0 is
    # what the first six used - edges ten pixels wide, which is an instruction to paint
    # soft. Lower it to hand the model a crisp structure.
    cond_blur: float = 1.0
    # A NEGATIVE PROMPT OF THIS BUILDING'S OWN, when the shared one is fighting the brief.
    # NEGATIVE is shared on purpose - a per-building copy is how `flat` got deleted from
    # one of them - so this overrides rather than edits, and only where a building needs
    # something the shared list forbids. See the Casa El Cheapo entry for the one case.
    negative: str = None
    # DECAY, COPING AND SKY, CONSTRUCTED OVER THE INSTALLED REGION after the generation.
    # The model will not draw weathering from an init - a rectangle is an object, and at
    # this scale a dark rectangle on a wall is a window - so it is built instead, the way
    # `sign_board` builds signs and `trims` builds the Rentflix parapet. See tools/grime.py
    # for the ops.
    grime: list = None
    # A SUPPLIED IMAGE, which skips generation entirely. "Here is a picture, use it" is a
    # first-class route: Zain made the Factory himself after three of ours failed, and his
    # has the monitor roof, the water tower, the rooftop plant and the loading bay the
    # pipeline never produced. See tools/fit_source.py for how to supply one.
    #
    #   {"path": ..., "crop": (x0,y0,x1,y1), "baseline": <game row>,
    #    "cover": [(x0,y0,x1,y1,dx,dy)]}
    #
    # `crop` is the BUILDING in the supplied image - its pavement and road are discarded
    # because the board already has those and they are Sierra's. `baseline` is the game row
    # its base lands on. `cover` paints a band out with the image's own material, copied
    # from an offset, for a supplied sign too small to read at the smallest rung.
    source_image: dict = None
    # THIS BUILDING DELIBERATELY GENERATES ITS OWN MARGINS, so `sierra_audit`'s third
    # clause - inside the region but outside the silhouette the pixels must be HERS -
    # does not apply to it. The town centre is the only one: it fills a hole she left
    # blank, so there is no scenery of hers to leave there.
    margin_generated: bool = False
    # The INIT's own blur, as a multiple of the same gen_w/60. The formula was written
    # for 16x canvases, where it is about two thirds of a cel pixel. On the town centre's
    # 6x canvas it is THREE cel pixels - the init arrives as mush and the model paints
    # mush back, which is the LANCZOS-conditioning lesson in a second place.
    init_blur: float = 1.0
    device_rect: tuple = None   # region-local box for a decorative device, or None
    devices: list = field(default_factory=list)
    surfaces: list = field(default_factory=list)
    detect: list = field(default_factory=list)   # [Detect] - see the class for the rule
    # (top, bottom) cel rows to pad the generation canvas with, towards a squarer aspect;
    # cropped back off before anything downstream sees it. See gen_building.synthetic_init.
    pad: tuple = (0, 0)
    # An EXPLICIT matte, as a greyscale PNG in the samples directory, when the outline
    # is measured off Sierra's board rather than authored in `init`. The town centre is
    # the only one: its shape IS her blank panel, flood-filled, and no arrangement of
    # init rectangles would reproduce it honestly. See tools/town_centre_mask.py.
    matte_file: str = None
    # BANDS INSIDE `clear` THAT A NEIGHBOUR OWNS and this building must not erase. See
    # install_building for why: two regions can overlap, each eraser is a rectangle, and a
    # building that erases its neighbour then repaints only its own silhouette leaves a
    # wedge of Sierra's art along the join.
    clear_keep: tuple = ()
    # AN AUTHORED ROOFLINE, in cel units, as samples (col, top) interpolated between.
    #
    # `init` boxes are the silhouette for most buildings and they are exact IN CEL TERMS
    # ONLY: `building_matte` krons the box grid up to the rung, so an outline authored on
    # the cel grid staircases in whole-cel steps. On a flat roof nothing shows. On a CURVE
    # it is a staircase - Watt a Bargain's dome came out as eight steps with a 36 px riser
    # at 12x, which reads as a ziggurat rather than a roofline.
    #
    # This is the same fix the General Dealer's `clip` is: author the outline at the
    # GENERATION resolution, where a quarter of a cel is a real distance. Here it is a
    # profile rather than a list of rects, because a roofline is a function of x.
    # tools/crown_mask.py writes the matte; `matte_file` then points at it.
    crown: tuple = ()
    # STAGE B, the tiled detail pass - see tools/detail_tiles.py. `cols` and `rows` are
    # the cel-coordinate JOINS between tile cores, and they are a design decision rather
    # than a setting: two adjacent tiles are denoised independently, so a core boundary
    # is a join like any other and has to land on a boundary the scene already has.
    tiles: dict = None
    detail_scale: int = 0       # 0 = no detail pass; the rung stage B renders at
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
# `shallow depth of field, defocused background` WERE ADDED HERE AND HAVE BEEN TAKEN BACK
# OUT, because they were measured and they did nothing. They are recorded rather than
# silently dropped, so the next person does not spend a GPU round on them again.
#
# The defect they were aimed at: the six shipped buildings read as soft photographs dropped
# into a painted town. What is actually established, by measurement rather than by eye:
#
#   * it is NOT any resize. QT's generation measures an edge-width ratio (mean |d1| over
#     mean |d4|) of 0.281 at its OWN 640x576 and 0.288 after the downscale to the 12x rung.
#     Nothing is lost between the model and the board.
#   * it is NOT the ControlNet conditioning. That was the first suspect, because the
#     conditioning was computed from an init already blurred by gen_w/60, so every edge
#     handed to ControlNet was a ten-pixel ramp. At a QUARTER of that radius the Pawn
#     Shoppe measured 0.301 against QT's 0.281 - no change worth the round.
#   * it is NOT the focus vocabulary. `blurry, out of focus, bokeh` were already in this
#     negative; adding `deep focus, everything in focus` to the positive and `shallow depth
#     of field` here moved 0.301 to 0.300.
#
# So it is in the GENERATION, and what it looks like is a lens wide open - a sharp band
# across the top of the frame and the lower two thirds defocused. AND THE METRIC IS ITSELF
# SUSPECT: Black's Market scores 0.422 and Socket City 0.427 against QT's 0.281, and those
# two are concrete, rust and mullions while QT is awning, glass and mannequins. It may be
# measuring TEXTURE DENSITY rather than focus, which would make it the kind of metric
# CAST.md 7b warns about - derived from the mechanism it is being used to judge.


BUILDINGS = {}


# --- Pawn Shoppe -> Needful Things ------------------------------------------------
# CEL 4, 39x28 at (201,5), 100% opaque. Established FIRST, before anything was planned,
# because the Factory cost six rounds to that question: `decode_pic_full.py --cels` lists
# fifteen cels and cel 4 is the only one whose stamped rectangle lands inside the Pawn
# Shoppe's hotspot (191,10)-(250,43). `Board.cs` reads `..., "Pawn Shoppe", 14, 12, 191,
# 10, 250, 43` - the 12 is the PLACE index and the 14 the marble-path step, and neither is
# a cel. NOT in the picPatch either: that strip is 183x25 at (68,138) and this cel ends at
# y 32.
#
# THE REGION IS THE WHOLE CEL, and that is measured, not assumed. Her scene inside it is 50
# lawn pixels in the left and right margins of rows 3..10 and 23 sky pixels in the roof's
# top corners: the building is 93.3% of the 39x28, against the Factory's 35% before it was
# cropped and 94% after. There is nothing to crop.
#
# SIERRA'S ART SAYS `PAWN SHOP`, NOT `PAWN SHOPPE` - on the board cel, on view 712's plate,
# and in all eight greeting clips. Only `Board.cs`'s label has the archaic spelling.
BUILDINGS["pawnshop"] = Building(
    key="pawnshop",
    sierra="Pawn Shoppe",
    new_name="Needful Things",
    source="cel:04",
    region=(201, 5, 39, 28),
    hotspot=(191, 10, 250, 43),
    palette={
        # Every one of these is sampled off her cel 4 except the two marked otherwise. She
        # lit this shop in warm darks already - #682028 is 13% of it - so unlike Black's
        # Market the brief and her palette want the same thing.
        "SKY":      (0x38, 0x58, 0x70),
        "LAWN":     (0x78, 0xD0, 0xB0),
        "CORNICE":  (0x28, 0x18, 0x18),
        # THE MID VALUE THE FIRST INIT DID NOT HAVE. Her #907858 reads at luminance ~124
        # against the shopfront's ~52 and the glass's ~200, which is the alternation that
        # survives a box mean down to 39x28.
        "RENDER":   (0x90, 0x78, 0x58),
        "RENDER_LO": (0x78, 0x60, 0x58),
        "WOOD":     (0x40, 0x30, 0x30),   # the varnished timber
        "WOOD_LO":  (0x28, 0x18, 0x18),   # its deepest shadow
        "WOOD_HI":  (0x68, 0x50, 0x50),   # where the varnish catches light
        "PAINT":    (0x68, 0x20, 0x28),   # the aged painted band, 13.0% of the cel
        "BRASS":    (0xD0, 0x90, 0x58),   # her own three balls, 3.5%
        "BRASS_LO": (0x90, 0x80, 0x68),
        "GLASS":    (0x58, 0x40, 0x40),
        "GOODS":    (0x90, 0x48, 0x30),   # the things in her windows
        "GOODS2":   (0x78, 0x60, 0x58),
        "GOODS3":   (0x80, 0x60, 0x48),
        "STONE":    (0x68, 0x70, 0x78),   # her step and pavement
        # NOT HERS, and deliberately. A 1990 cel is lit by one flat daylight and has no warm
        # light anywhere, so an init built only from her colours is warm browns top to
        # bottom with nothing bright in it - which is the Factory's "an init with no light
        # in it returns an image with no light in it", and the fix there was the same:
        # invent the one value the source cannot supply.
        "GLOW":     (0xF0, 0xC8, 0x88),   # the lit interior behind the glass
        "SPILL":    (0xC0, 0xA0, 0x80),   # what reaches the pavement
    },
    init=[
        # THE MASSING IS THE POINT, AND THE FIRST VERSION OF THIS INIT PROVED IT. It gave
        # the upper half of the cel to one eleven-row band of dark varnish and the lower
        # half to two bright windows, and what came back was not a building at all - it was
        # a close-up of a shop window full of brass spheres, with the fascia a black void.
        # At 39x28 what survives is silhouette and value structure, so the facade is built
        # as four alternating values down its height: dark cornice, MID render wall, dark
        # painted band, dark shopfront with bright glass, light step. The sign is dark and
        # hangs on the mid wall, which is what makes it read at 1x.
        (0, 0, 38, 27, "SKY"),
        (0, 22, 38, 27, "LAWN"),
        (4, 0, 35, 2, "CORNICE"),         # the cornice, stepped in to meet her roofline
        # the render wall. This is the one value that had to change: it is a MID, so the
        # dark board reads against it and the facade is not one dark mass top to bottom.
        (3, 3, 36, 10, "RENDER"),
        (0, 11, 38, 12, "RENDER"),        # it steps out over the frontage, as hers does
        (3, 9, 36, 10, "RENDER_LO"),      # the wall in the board's own shadow
        # THE PAWNBROKER'S THREE BALLS ARE KEPT. She draws them at x 14..28, y 13..17 on a
        # bracket - the shop's own emblem, the same kind of thing as Socket City's plug.
        # They are in the INIT, not a `device_rect`: a device is generated apart and
        # composited, and at 4x3 these are far under the 13px at which that was already
        # marginal. In the init they are lit and reflected as part of one photograph.
        #
        # THE WARM COURSE THE BALLS HANG ON. Two variants of this were generated and
        # rejected, and both are recorded because each cost a round:
        #
        #   deleted entirely          no text, and the facade came back PALE AND COLD.
        #                             Colour lives in the init, so taking it out of the
        #                             init takes it out of the picture - CAST.md section 0,
        #                             straight down the middle.
        #   broken by pilasters       no text, still cold, and softer (p99 11 against 20).
        #
        # It is kept whole. It is a continuous dark band under a sign and the model WILL
        # write on it when it is given enough freedom to be sharp - that is measured, not
        # feared. What stops that shipping is tools/no_text_guard.py, which fails the build,
        # and the conservative strength/control below, under which it does not happen.
        (0, 13, 38, 16, "PAINT"),
        (12, 13, 29, 14, "WOOD_LO"),      # the bracket
        (14, 14, 17, 16, "BRASS"),
        (19, 14, 22, 16, "BRASS"),
        (24, 14, 27, 16, "BRASS"),
        # THE SHOPFRONT: dark varnished timber, two deep windows, a recessed pair of doors.
        (0, 17, 38, 25, "WOOD"),
        (0, 17, 2, 25, "WOOD_LO"),        # a pilaster at each end
        (36, 17, 38, 25, "WOOD_LO"),
        (3, 17, 14, 25, "WOOD_HI"),       # the left window's frame
        (4, 18, 13, 24, "GLOW"),          # lit interior behind the glass
        (8, 18, 9, 24, "WOOD"),           # the mullion between its two panes
        (25, 17, 35, 25, "WOOD_HI"),      # the right window
        (26, 18, 34, 24, "GLOW"),
        (30, 18, 31, 24, "WOOD"),
        (15, 17, 24, 25, "WOOD_LO"),      # the doors, recessed and darker than the windows
        (16, 18, 19, 25, "GLASS"),
        (20, 18, 23, 25, "GLASS"),
        # the window crowded with unrelated objects. Nothing here is under 2px, because a
        # mass thinner than the blur was never in the init at all.
        (4, 19, 6, 22, "GOODS"),
        (6, 20, 7, 23, "BRASS_LO"),
        (10, 18, 11, 21, "GOODS2"),
        (12, 20, 13, 23, "GOODS3"),
        (26, 19, 27, 22, "GOODS3"),
        (28, 18, 29, 21, "BRASS_LO"),
        (32, 20, 33, 23, "GOODS"),
        (33, 19, 34, 22, "GOODS2"),
        (0, 26, 38, 27, "STONE"),         # the step
        # The lamplight reaching the pavement - LOCAL, in front of the doors only. A soft
        # light band across the bottom of an init is what haze looks like and it painted
        # mist over the Factory's base for two rounds; `smoke, steam, fog` in the negative
        # never fixed it and removing the band did. Ten columns of thirty-nine is a pool.
        (15, 26, 24, 27, "SPILL"),
    ],
    # VARNISH, BRASS AND A CROWDED WINDOW. Black's Market is the other non-glazed-chain
    # building on this board and it is deliberately hard and cold - concrete, roller
    # shutter, rust, raking shadow, no display at all. This one has to be its opposite out
    # of the same kind of subject, so the vocabulary is depth, clutter and age in a
    # finish, and the light is interior rather than overcast. Not one colour word: the
    # palette above already says every colour in the picture.
    #
    # IT LEADS WITH THE BUILDING, and the first draft did not. That one opened on the
    # shopfront and spent its middle on the window - "a deep plate glass window crowded
    # with clocks instruments and bric-a-brac" - and the model obliged with a close-up of a
    # window. `the facade of a small old shop` first, the window as one clause among
    # several. Token budget is also why: the first draft was 79/77 and was caught by
    # measuring before the GPU rather than after. Nothing was taken from `sharp focus, fine
    # detail, architectural photography`, which predates this building - that is how `flat`
    # got deleted from a negative and returned a vector illustration.
    # `shop`, `shopfront` and `display` WERE REMOVED HERE AND PUT BACK, which is worth a
    # note because the reasoning for taking them out was sound. A shopfront is a thing with
    # writing on it and naming one three times is an instruction, so `premises`/`frontage`
    # was tried. It did stop the text - and it also lost the building: the facade came back
    # pale, cold and generic, with no pawnbroker in it at all. Category is the one thing
    # only the prompt can carry (CAST.md section 0), and `shop` was carrying it.
    #
    # So the three measures against the model writing on the building are now: this prompt
    # is NOT one of them, the negative prompt is NOT one of them (all nine of its
    # text-suppression terms were verified to reach the model intact at 68/77 and the model
    # wrote words anyway), and what is left is tools/no_text_guard.py plus a strength and
    # control conservative enough that it does not happen.
    prompt=("a photograph of the facade of a small old pawnbroker's shop, weathered "
            "painted render over a varnished timber shopfront, deep display windows with "
            "lamplit interiors and reflections, brass spheres on an iron bracket, a stone "
            "step, peeling paint, overcast daylight, sharp focus, fine detail, "
            "architectural photography"),
    # The conditioning was a ten-pixel ramp on every edge and the buildings came back soft.
    # 0.25 of that radius; measured on the result, not assumed. See gen_building.py.
    cond_blur=0.25,
    # Her own silhouette, measured row by row: x 5..34 across the cornice, x 3..36 down the
    # sign band with lawn in both margins, and the full width from y 11 where the shopfront
    # steps out. A bounding box here would paint a slab of timber over the lawn.
    # THE MATTE DRAWS THE OUTLINE. The keep rects were inset at the top corners, so the
    # installer wrote 67 px of HER sky INSIDE the building - 6.1% of the region. The matte
    # cuts the same corners instead, which leaves her sky on the board around the shop
    # rather than inside it.
    #
    # SKY IS THE ONLY SCENERY KEY. Five keys touch the border - CORNICE 50, WOOD_LO 50,
    # STONE 31, SKY 27, SPILL 10 - and four of them are the BUILDING reaching its own
    # edge. SPILL was the one worth looking at: it is the warm light the shop throws onto
    # the pavement, part of our own render, and cutting it put a notch in the base with her
    # pavement showing through the middle of the shopfront.
    surround={"mode": "rects", "keep": [(0, 0, 38, 27)]},
    scenery=("SKY",),
    # the eraser, sized to the old install's footprint
    clear=(201, 5, 239, 33),
    # `name_lines` and `name_rect` are kept only so the table stays uniform; with `sign`
    # set, `install_building` uses the constructed board and never stamps type.
    name_lines=["NEEDFUL THINGS"],
    name_rect=(3, 4, 36, 10),
    name_ink=(0xE0, 0xB8, 0x68),
    # A HAND-PAINTED BOARD, BOLTED TO THE RENDER. Built by tools/sign_board.py at 624x448 -
    # the generation's own resolution - so the downscale antialiases the sign and the
    # building together, once. The six that shipped before this stamped flat type into the
    # already-downscaled cel and every one of them reads as a decal.
    #
    # ITS SIZE IS A PROPORTION AND HERE IS THE PROPORTION: 34 x 7 on a 39 x 28 facade, so
    # 87% of the width and 25% of the height. The Industrial Revolution's board is about
    # half its building and overhangs into the grass; Bare Necessities' is a third of its
    # facade. A quarter of the height, running almost the full width, is a fascia band.
    #
    # ONE LINE, AND THAT IS WHAT THE PROPORTION COSTS. Two lines need eleven rows - 39% of
    # the facade - which is a hoarding, not a sign. `NEEDFUL THINGS` is fourteen characters
    # against Sierra's nine, so on a band this size it is legible at the 4x and 12x rungs
    # the game actually draws and is mush at 1x. So is `THE INDUSTRIAL REVOLUTION`. Stated
    # rather than hidden: a fourteen-character name on a 39px building cannot be both a
    # band and legible at 1x, and the band is the half Zain asked for.
    sign={
        # 36 x 7 on a 39 x 28 facade: 92% of the width, 25% of the height. It was 34 wide
        # until the facade came back SHARP, at which point the model started asserting its
        # own shopfront prior and painted garbled lettering right across the fascia - the
        # price of the strength and control that buy detail (see BUILDINGS.md). The board
        # overpaints it, and two extra columns are what guarantee nothing peeks out at the
        # ends. The garbled text is covered, not shipped; it is still in the facade file.
        "rect": (2, 4, 37, 10),
        "lines": ["NEEDFUL THINGS"],
        "face":    (0x2A, 0x1A, 0x14),    # the board's painted field, dark varnished
        "face_hi": (0x46, 0x2E, 0x20),
        "edge_hi": (0x8A, 0x6A, 0x46),    # its sawn edge where the light reaches it
        "edge_lo": (0x16, 0x0E, 0x0A),
        "ink":     (0xE0, 0xB8, 0x68),    # the gold of the rule across her own lettering
        "ink_hi":  (0xF8, 0xE4, 0xB8),
        "ink_lo":  (0x4A, 0x30, 0x16),
        "bolt":    (0x6A, 0x5A, 0x40),
        "bolt_hi": (0xC8, 0xB0, 0x80),
        "depth": 0.5, "shadow": 1.0, "grain": 0.11, "relief": 0.11, "fill": 0.86,
        # The marrying pass. Its POSITIVE asks for a lit painted board and its NEGATIVE is
        # NOT the shared one: that forbids `text, letters, words, writing, lettering`, and
        # running it over an image whose sign band is full of letters is an instruction to
        # delete them. The shared negative is right for the facade, where the model must
        # invent nothing, and exactly wrong here, where it must preserve something.
        "prompt": ("a close photograph of an old hand painted wooden sign board bolted to "
                   "a weathered wall, gilded lettering with chipped paint, aged varnish, "
                   "raking daylight, sharp focus, fine detail"),
        "negative": ("blurry, out of focus, bokeh, 3d render, cgi, vector, illustration, "
                     "flat, cartoon, pixelated, jpeg artifacts, people, distorted, frame, "
                     "vignette, smoke, steam, fog"),
        "strength": 0.22, "cfg": 7.0, "steps": 24, "control": 0.80, "feather": 7.0,
        # THE MARRYING PASS IS OFF ON THIS BUILDING, and that is a result rather than a
        # preference. Run at 0.22, 0.12 and 0.07, it removed the bolt heads and flattened
        # the bevel every time and added no material the construction did not have. The
        # measurement agrees: survival IoU 0.887, centroid moved 0.22 cel px - and side by
        # side the LETTERFORMS are identical, so what the number is reporting is the loss
        # of the signwriter's relief, not glyphs moving. The stage stays in the pipeline
        # because it may earn its place on a different sign; it has not earned it here.
        "marry": False,
    },
    # HIGH STRENGTH, LOW CONTROL, and both halves are load-bearing. Measured on the p99 of
    # the one-pixel luminance step over the whole 624x448 frame - a real photograph runs
    # 20-60, Sierra's upscaled 12x cels 11-20:
    #
    #     0.84 / 0.55   p99 11   the shipped recipe. No hard edge anywhere in the frame.
    #     0.86 / 0.50   p99 13   with the crisp NEAREST conditioning - barely moved
    #     0.74 / 0.18   p99  9   low control cannot rescue a low strength
    #     0.90 / 0.15   p99 28   sharp, but the massing drifted off the init entirely
    #     0.92 / 0.30   p99 26   sharp, right building - AND IT WROTE ON IT
    #
    # Holding the model to EITHER smooth input - the Gaussian-blurred init at low strength,
    # or the near-empty softedge map at high conditioning scale - is what costs the detail.
    # Both have to be released at once.
    #
    # AND RELEASING BOTH IS WHAT MAKES IT WRITE. 0.92/0.30 is the sharpest facade that
    # still looks like this building, and it came back with `PADEMOKIK BATEWEIS ILP` across
    # the course under the sign. That is not a coincidence to be tuned away: the detail and
    # the hallucinated signage are the same freedom. Three ways out were tried and the
    # two prompt-side ones are recorded above as failures.
    #
    # SO THIS SHIPS AT 0.84/0.55: softer than it should be, and the facade Zain was shown
    # and approved on massing and material. The sharp variant exists in the scratchpad and
    # cannot ship. This is a real unresolved trade and it is written down rather than hidden
    # behind a parameter nobody will question.
    strength=0.84,
    control=0.55,
    detect=[
        # Both measured before the scan, which is one cheap measurement against minutes of
        # searching 829 files for the wrong thing. 42.6% and 44.6%, both mid-window.
        Detect("A board sign", "cel", (3, 3, 36, 8), "cyanglyph"),
        Detect("B interior plate", "view_712_l0_c0.png", (67, 1, 181, 20), "midblue"),
    ],
    surfaces=[
        # view 712, NOT an 8xx. Every other shop's interior backdrop is in the 8xx block and
        # this one is not: `MainViewModel.ArtFor` reads `LocationId.PawnShop => (712, 0,
        # 362, 708)`. Checked rather than guessed.
        #
        # The plate is exactly x 67..181, y 1..20 and holds nothing but the name - rows 21
        # and below are the green field, and at y 14..16 the only unlit pixel is the plate's
        # own left edge, because her letters fill the rest. So repainting all of it covers
        # no second line, unlike Socket City's `APPLIANCE STORE`.
        Surfaces(file="view_712_l0_c0.png",
                 plate=(67, 1, 181, 20),
                 body=(0x20, 0x20, 0x20),      # her plate's own black, measured
                 ink=(0xE0, 0xB8, 0x68),       # the gold of the rule across her lettering
                 lines=["NEEDFUL THINGS"]),
    ],
    notes=("All eight greeting clips (70-77) say `Welcome to the Pawn Shop`, and two build "
           "jokes on the word - 76 `Where 'pawn' is just another word for nothing left to "
           "lose!` and 74 `where we pardon your beg!`. Left alone, as with Hi-Tech U and "
           "ACNE Employment. The purchase lines (78-85) and the refusal (86) name no shop."),
)


# --- Rent Office -> Rentflix -----------------------------------------------------
# CEL 2, 41x22 at (85,3), 99.8% opaque, and 92.7% of it is building. Established with
# `decode_pic_full.py --cels` before anything was planned: cel 2 is the only cel whose
# stamped rectangle lands in the Rent Office's hotspot (68,10)-(129,43). `Board.cs` reads
# `..., "Rent Office", 164, 1, 68, 10, 129, 43, ...` - the 1 is the PLACE index and 164 the
# marble-path step, neither is a cel.
#
# THE REGION IS FOUR ROWS TALLER THAN HER CEL, and that is the door's doing. Her door is
# view 751 loop 1 at (105,29), drawn at `(x - w/2, y + 1 - h)`, so the 7x11 cel covers game
# x 102..108, y 19..29 - five rows below a cel that ends at y 24. Rather than leave the
# threshold hanging over the path as the Pawn Shoppe's does, the facade is extended to meet
# it. It can be: the marble's swept footprint in this corner starts at y 29 for x 66..84 and
# y 30 everywhere else, so y 3..29 is free at the door's own x.
#
# THE NAME IS ZAIN'S; THE LOGO IS NOT A COPY OF ANYONE'S. He asked for "the same logo as
# Netflix" and that is deliberately NOT what this is - no arched wordmark, no ribbon mark,
# no imitation of their letterforms or construction. The whole point of this programme is
# getting someone else's intellectual property OFF this board, and swapping Sierra's for a
# live trademark would defeat it. What is built instead is the GENRE: a bold wordmark on a
# dark fascia, lit like a shopfront. Red-on-dark bold type is a style, not a design anyone
# owns.
BUILDINGS["rentoffice"] = Building(
    key="rentoffice",
    sierra="Rent Office",
    new_name="Rentflix",
    source="cel:02",
    region=(85, 3, 41, 22),
    hotspot=(68, 10, 129, 43),
    palette={
        # Every one sampled off her cel 2 except GLOW, which is marked.
        "SKY":     (0x70, 0x88, 0xE0),
        "ROOF":    (0x38, 0x28, 0x28),   # her dark roof, 11.6% of the cel
        "FASCIA":  (0x30, 0x20, 0x20),   # the sign ground - DARK, where hers is orange
        "FRAME":   (0x40, 0x30, 0x30),
        "GLASS":   (0x50, 0x70, 0x98),
        "CARD":    (0xE0, 0xC8, 0x98),   # her cream, for the property cards
        "DESK":    (0x80, 0x60, 0x48),
        "DOOR":    (0x58, 0x40, 0x30),
        "DOOR_LO": (0x30, 0x20, 0x20),
        "STONE":   (0x80, 0x88, 0x90),
        # MASONRY, and this is a change of BRIEF rather than of parameter. Four settings
        # hypotheses were measured and rejected for this building's softness; what the
        # masonry test did show is that brick and tiles come back sharp while glazing
        # stays soft. So the facade is rebalanced towards brick: plate glass for the
        # DISPLAY, not for the whole frontage.
        "PIER":    (0x90, 0x78, 0x58),
        "RISER":   (0x80, 0x60, 0x48),
        "LINTEL":  (0xB0, 0xB8, 0xC0),
        "INT":     (0x38, 0x30, 0x2C),   # a dim interior behind the glass
        # MASONRY, TRIED AND REVERTED - see the note on `pad`. Added after three hypotheses for this building's
        # softness were tested and rejected - pixel count, aspect, and earlier the
        # conditioning and the strength. What is left is the SUBJECT: the Factory
        # came back sharp and it is brick, while this facade was plate glass and
        # reflections, which are smooth by nature. Piers and a tiled stall riser
        # give the model something with real high-frequency texture in it.
        "PIER":    (0x90, 0x78, 0x58),
        "RISER":   (0x80, 0x60, 0x48),
        # NOT HERS. A 1990 cel has no warm interior light anywhere, and an init with none in
        # it returns an image with none - the Factory's lesson. This is what makes the
        # window read as lit rather than as a dark hole.
        "GLOW":    (0xF0, 0xC8, 0x88),
    },
    init=[
        (0, 0, 40, 21, "SKY"),
        # A PLAIN STONE PARAPET, NOT BRICK. This band was brick, and what came back was
        # irregular dark marks in groups of varying length on two rough lines - which I
        # read as brick headers and the reviewer read as writing. THAT DISAGREEMENT IS THE
        # ANSWER: if two careful readings differ, a player cannot tell either, and
        # ambiguous marks across the top of a building are not worth defending. Plain stone
        # has no texture to misread.
        (0, 0, 40, 3, "LINTEL"),
        (2, 4, 38, 12, "FASCIA"),         # THE SIGN BAND - plain; the model never sees text
        (1, 13, 39, 13, "LINTEL"),
        (2, 14, 38, 21, "FRAME"),
        # BRICK AS PIERS ONLY, NOT AS THE SURFACE. The masonry rebalance went too far: with
        # the openings cut to 8x5 cel px the model rendered them as small glowing LAMPS and
        # the building stopped reading as a letting agency at all. The windows are back at
        # the proportions that worked and the brick is reduced to what it is good for -
        # separating the openings and carrying the fascia.
        (2, 14, 3, 21, "PIER"), (15, 14, 16, 21, "PIER"),
        (24, 14, 25, 21, "PIER"), (37, 14, 38, 21, "PIER"),
        (4, 14, 14, 20, "GLASS"), (26, 14, 36, 20, "GLASS"),
        (5, 15, 13, 19, "GLOW"), (27, 15, 35, 19, "GLOW"),
        # CREAM property cards. These are what carry the meaning at this size - they are
        # the difference between "a letting agency" and "a lit storefront" - and they only
        # read when the window is big enough to hold them.
        (5, 16, 7, 18, "CARD"), (8, 16, 10, 18, "CARD"), (11, 16, 13, 18, "CARD"),
        (27, 16, 29, 18, "CARD"), (30, 16, 32, 18, "CARD"), (33, 16, 35, 18, "CARD"),
        (27, 19, 34, 20, "DESK"),         # a desk visible inside
        # THE DOORS, PAINTED SHUT, under the door cel's own footprint - view 751 loop 1 at
        # (105,29) covers game x 102..108, y 19..29 = local x 17..23. The sprite is only
        # drawn while the door animates, so a hole in the art is a hole on screen.
        (17, 14, 23, 21, "DOOR"),
        (20, 14, 21, 21, "DOOR_LO"),      # the meeting stile - ONE line; three read as bars
    ],
    # LETTING AGENCY, not boutique and not pawn shop. Common Threads leans on awning and
    # mannequins and Needful Things on varnish and brass; this one is plate glass, property
    # cards and a desk, which is a different shop at a glance. No colour word: the palette
    # above says every colour in the picture.
    prompt=("a photograph of a high street letting agency shopfront, large plate glass "
            "windows with property cards on display, a desk and chairs inside, lit "
            "interior, slim metal window frames, brick piers, a plain fascia board "
            "above, bright overcast daylight, sharp focus, fine detail, architectural "
            "photography"),
    cond_blur=0.25,
    strength=0.84,
    control=0.60,
    # SEED 23, not the table's default 7, AND THIS BUILDING'S CORRECTNESS DEPENDS ON THE
    # SIGN NOT MOVING.
    #
    #   *** IF YOU MOVE OR RESIZE THE RENTFLIX SIGN, PSEUDO-TEXT APPEARS FROM UNDER IT. ***
    #
    # At seed 7 the model wrote `Pandrizino` across the DOOR HEAD - pseudo-text on the
    # facade, outside the sign band, covered by nothing, and the no-text guard PASSED it
    # because on a soft facade the letters merge into blobs too large to be glyphs
    # (109x58, 135x77). A stone transom over the doorway did not stop it; it wrote on the
    # door below instead. Seed 23 leaves the door head clean and writes in the FASCIA
    # instead, where `sign["rect"]` (2,4,38,12) covers it completely.
    #
    # That is a reroll, not a fix. The text is still in the generation underneath.
    seed=23,
    # SCALE STAYS AT 16, AND THE SOFTNESS IS NOT A RESOLUTION PROBLEM. 24 was tried and
    # REVERTED - see BUILDINGS.md 22: a plate-glass frontage has almost no
    # high-frequency content to resolve, so more pixels bought nothing a player sees.
    # The reasoning that led to 24 is kept because the numbers in it are still true:
    # at 16 the
    # canvas is 656x352 = 230,912 px, BELOW SD1.5's native 262,144. The Factory shipped
    # sharp at 672x640 = 430,080; the Pawn Shoppe shipped soft at 624x448 = 279,552.
    # Compared at native resolution the Factory's brick and window frames are crisp where
    # this facade's card and glazing are a smear. 24 gives 984x528 = 519,552 px, still
    # under the measured 810k ceiling for this card, with the aspect unchanged at 1.86 so
    # the test moves one variable.
    #
    # AND THE p99 ONE-PIXEL-STEP METRIC SAYS THE OPPOSITE - 15.0 here against the Factory's
    # 12.0 - because this facade has high-contrast property cards in it and that number
    # tracks contrast, not focus. It has now misled twice. Look at the pixels.
    scale=16,
    # 1.86:1 is close to the 1:2 that CAST.md 6 says SD1.5 falls apart at. Six rows above
    # and eight below bring the canvas to 41x36 = 1.14:1, and they are cropped off again
    # before the matte ever sees them.
    pad=(6, 8),
    # Only the building is generated; her trees and ground are untouched, and the matte cuts
    # to this silhouette. Two columns of sky at each side so the matte is not the region.
    surround={"mode": "rects", "keep": [(0, 0, 40, 21)]},
    scenery=("SKY",),
    # THE PARAPET IS CONSTRUCTED, NOT GENERATED. The model wrote a shop name on this
    # building three times in three different places - the door head at seed 7, the fascia
    # at seed 23, and then the parapet's own top edge once the brick there was replaced
    # with plain stone. Rerolling the seed only moved it. This band is painted after the
    # generation, like the sign, so there is nothing there for the model to decide.
    # The face is the PIERS' own colour, sampled off the installed board at game y 18
    # (0x828C95): the cornice has to belong to this building, and a lighter stone
    # made it the brightest thing in the frame and read as a slab stuck on the sky.
    trims=[(0, 0, 40, 3, (0x82, 0x8C, 0x95), (0xAE, 0xB6, 0xBD))],
    # The eraser. The trim is cut by the matte, so anything an EARLIER install wrote
    # outside the silhouette would survive forever under it - a full-width grey slab did
    # exactly that on the first pass and floated above the roofline against her sky.
    # Erasing costs no GPU: restore Sierra over the whole footprint before pasting.
    clear=(85, 3, 125, 24),
    name_lines=["RENTFLIX"],
    name_rect=(2, 4, 38, 12),
    name_ink=(0xD0, 0x1F, 0x2A),
    # 37 x 9 on a 41 x 27 facade: 90% of its width, 33% of its height. One line of eight
    # characters, which is fewer than Sierra's own nine and so needs no argument about
    # legibility.
    sign={
        "rect": (2, 4, 38, 12),
        "lines": ["RENTFLIX"],
        "face":    (0x1A, 0x14, 0x16),    # a dark fascia panel
        "face_hi": (0x2E, 0x24, 0x26),
        "edge_hi": (0x50, 0x46, 0x48),
        "edge_lo": (0x0C, 0x08, 0x0A),
        "ink":     (0xD0, 0x1F, 0x2A),    # bold red on dark - the GENRE, not a brand
        "ink_hi":  (0xF0, 0x5A, 0x60),
        "ink_lo":  (0x56, 0x0C, 0x12),
        # NO BOLTS and almost no thickness. A bolted timber board is right for a pawnbroker
        # and wrong for a modern shopfront; this is a flush illuminated fascia panel.
        "bolts": False,
        "depth": 0.3, "shadow": 0.5, "grain": 0.04, "relief": 0.08, "fill": 0.86,
        "prompt": ("a close photograph of an illuminated shopfront fascia panel, flush "
                   "metal edge, even internal lighting, sharp focus, fine detail"),
        "negative": ("blurry, out of focus, bokeh, 3d render, cgi, vector, illustration, "
                     "flat, cartoon, pixelated, jpeg artifacts, people, distorted, frame, "
                     "vignette, smoke, steam, fog"),
        "strength": 0.22, "cfg": 7.0, "steps": 24, "control": 0.80, "feather": 7.0,
        "marry": False,                   # same finding as the Pawn Shoppe - see there
    },
    detect=[
        # All three measured before the scan. Her board ink is #305848, the same dark green
        # the Factory's sign uses, so `darkgreen` already exists for it.
        Detect("A board sign", "cel", (2, 6, 38, 11), "darkgreen"),        # 34.7%
        Detect("B interior plate", "view_701_l0_c0.png", (5, 16, 43, 25), "goldish"),  # 41.3%
        Detect("C interior bar", "view_701_l0_c1.png", (10, 4, 106, 11), "redish"),    # 35.4%
    ],
    surfaces=[
        Surfaces(file="view_701_l0_c0.png", plate=(2, 2, 46, 27),
                 body=(0x18, 0x14, 0x16), ink=(0xD0, 0x1F, 0x2A),
                 lines=["RENTFLIX"]),
        Surfaces(file="view_701_l0_c1.png", plate=(1, 1, 114, 14),
                 body=(0x18, 0x14, 0x16), ink=(0xD0, 0x1F, 0x2A),
                 lines=["RENTFLIX"]),
    ],
    notes=("Eight greeting clips (160-167) say `Welcome to the Rent Office` (160-162) or "
           "`the Rental Office` (163-167); 183 is the refusal and names no shop. Left "
           "alone, as with every other building. `ArtFor` gives the interior backdrop as "
           "view 701 LOOP 1 - loop 0 is the two title plates, which are the surfaces above."),
)


# --- Low-Cost Housing -> Casa El Cheapo --------------------------------------------
# Zain: "rename low cost housing to Casa El Cheapo and make it look like a cheap but badly
# maintained castle". The brief is a JOKE WITH A SHAPE, so the thing to build is the joke:
# not a castle and not a ruin, but a PRETENSION to a castle, built cheaply and let go. The
# humour is the gap between what it is pretending to be and what it is, so the pretension
# has to read first or nothing lands.
#
# IT IS A CEL, ESTABLISHED FROM THE DECODE RATHER THAN ASSUMED - the question that cost the
# Factory six rounds. `DoorFor` gives LowCostHousing as Place 1 at (144,30); the only cel in
# pic 11 whose box contains that point is CEL 3, 52x34 at (134,2), 100% opaque, and it is
# her brown tenement with LOW COST HOUSING in red across the top.
#
# THE REGION TOPS AT y 2, NOT y 0. Her cel starts at y 2 and the two rows above it measure
# (239,239,221) - the board's own cream MARGIN, not sky. Crenellations pushed up there would
# stand in the border. It runs down to y 39, four rows below her cel, for the gate and the
# ground.  52x38 at scale 18 = 936x684 = 640,224 px, inside the measured 810k ceiling.
BUILDINGS["lowcost"] = Building(
    key="lowcost", sierra="Low Cost Housing", new_name="Casa El Cheapo",
        # SOURCE IS THE BACKGROUND, NOT `cel:03`, and only so the region can run four rows
    # below her cel for the gate and the ground. `sierra_region` asserts a `cel:NN` region
    # EQUALS the cel - correctly, a facade may not grow to meet a door - and hers is
    # 52x34. It costs nothing: `surround` keeps the whole region, so those pixels are
    # never blended in and the MATTE decides the outline. IT IS STILL HER CEL 3; only the
    # fallback source changes, and the identification above stands.
    source="background", region=(134, 2, 52, 38), hotspot=(130, 6, 190, 44),
    palette={
        # her own surround, sampled just outside the region so the matte's edge is low
        # contrast: (136,135,167) left, (143,138,181) right, (183,175,213) below
        # SKY IS OURS AND IS *BUILDING*, NOT SCENERY, AND THAT IS NOT AN OVERSIGHT.
        # The crenellations need something to stand against, and the obvious move is to
        # cut their gaps out of the matte so HER sky shows between the merlons. It cannot
        # be done here: her art in those rows IS THE OLD NAME. `LOW COST HOUSING` is
        # painted in red across game x 146..174, y 3..19, so every gap would frame a piece
        # of the lettering we are replacing. Her sky is only clean outside x 146..174.
        #
        # So the top band is OUR sky, colour-matched to hers at the region's sides -
        # (136,135,167) left and (143,138,181) right, so (0x8C,0x89,0xAE) - and the matte
        # keeps it. The merlons stand against a painted sky instead of a cut one, her
        # lettering is covered outright, and the only joins are at the region's edges
        # where our sky meets hers at a near-identical value.
        "SKY":      (0x8C, 0x89, 0xAE), "GROUND": (0xB7, 0xAF, 0xD5),
        # cheap render over blockwork, and the stain running down it
        # GRUBBY, NOT CREAM. At (0xC6,0xBA,0xA2) with strength 0.88 the model returned a
        # clean new castle twice. This is the decay's real lever.
        "RENDER":   (0xAE, 0xA4, 0x8A), "RENDER_LO": (0x72, 0x6A, 0x56),
        # the mock stonework of the gatehouse - dressed to look like a castle, isn't
        "STONE":    (0xAA, 0xA4, 0x9A),
        "PORT":     (0x4A, 0x46, 0x42),   # the portcullis grille
        "DOOR":     (0x5A, 0x3E, 0x28),
        "WIN":      (0x3A, 0x3A, 0x42),
        "BOARD":    (0x9E, 0x82, 0x5A),   # the one boarded window
    },
    # THE MASSING IS THE JOKE. Crenellations that are too shallow and too regular - six
    # identical merlons on a four-pixel pitch - a "turret" that is plainly the stairwell,
    # and a mock portcullis over an ordinary door. The silhouette is what survives the
    # downscale, so the pretension is stated in the OUTLINE and the neglect is left to the
    # prompt, which is the only place a satellite dish or a wheelie bin can live at this
    # size.
    # A CRENELLATION NEEDS SKY ABOVE IT, NOT ONLY BETWEEN THE MERLONS. The first attempt
    # ran the merlons from local y 0 - the region's own top row - so the only sky was in the
    # gaps, and the model read an alternating light/dark band on a wall as exactly what it
    # has most training data for: A ROW OF WINDOWS. It came back as flat render with ten
    # blue openings along the top, which is also what tripped `no_text_guard`.
    #
    # The block now starts at local y 7 and the merlons at y 3, leaving three clear rows of
    # sky above them. The joke depends on the crenellation reading, so the silhouette has to
    # be unambiguous before the prompt is asked for anything.
    init=[
        (0, 0, 51, 37, "SKY"),
        (0, 34, 51, 37, "GROUND"),
        # the main block
        (2, 7, 51, 33, "RENDER"),
        (2, 31, 51, 33, "RENDER_LO"),     # the stained base
        # merlons - four wide, four apart, all identical, which is the point
        (2, 3, 5, 7, "RENDER"), (10, 3, 13, 7, "RENDER"), (18, 3, 21, 7, "RENDER"),
        (26, 3, 29, 7, "RENDER"), (34, 3, 37, 7, "RENDER"), (42, 3, 45, 7, "RENDER"),
        # the "turret", which is the stairwell, two rows taller with its own merlons
        (0, 5, 15, 33, "RENDER"),
        (0, 1, 3, 5, "RENDER"), (6, 1, 9, 5, "RENDER"), (12, 1, 15, 5, "RENDER"),
        # the mock portcullis over the DOOR CEL's own footprint. view 751 loop 0 at (144,30)
        # is 8x8 drawn bottom-centred, so it covers game x 140..147, y 23..30 = local
        # x 6..13, y 21..28. Painted SHUT - the sprite only draws while it animates.
        (3, 17, 16, 33, "STONE"),
        (4, 19, 15, 31, "PORT"),
        (6, 21, 13, 30, "DOOR"),
        # one row of windows below the sign, one of them boarded
        (18, 26, 23, 32, "WIN"), (26, 26, 31, 32, "BOARD"),
        (34, 26, 39, 32, "WIN"), (42, 26, 47, 32, "WIN"),
        # THE NEGLECT IS AUTHORED, NOT ASKED FOR. The first pass with the sky band gave a
        # clean, new-looking castle: crenellations, a gatehouse arch, gothic windows - the
        # PRETENSION landed and the DECAY did not, and the joke needs both. The prompt
        # already named flaking render, water stains, a dish, an air conditioner, washing
        # and a bin, and the model rendered none of them. CAST.md 0: the init beats the
        # prompt, so the decay goes in the init where it is a shape rather than a wish.
        #
        # Only the wall the SIGN does not cover is worth marking - it sits over local
        # x 14..45, y 9..24 - so the streaks are on the turret and the right-hand strip.
        # ONLY THE HORIZONTAL ONE SURVIVES, AND THE REASON IS A FINDING. Vertical
        # RENDER_LO bars were authored down the wall as water staining and came back as
        # SHUTTERED WINDOWS - the same failure as the merlons, which became window openings
        # for the same reason. AT THIS SCALE A DARK VERTICAL RECTANGLE ON A WALL IS A
        # WINDOW TO THIS MODEL, whatever the prompt calls it. A satellite dish and a wheelie
        # bin authored as small rects went the same way or vanished.
        #
        # The init's language is rectangles, and a rectangle is an OBJECT. It can state
        # massing and openings; it cannot state weathering, which is low-contrast,
        # soft-edged and irregular - the opposite of a rect. So the decay is carried by the
        # PALETTE instead, which section 29 established is the real lever when the prompt
        # may not name colours: a grubbier render and a lower strength, so the authored
        # values survive the generation.
        (18, 33, 47, 33, "RENDER_LO"),     # a damp course - horizontal, so not a window
        # THE SIGN BOARD, last so it sits in front, and BUILDING in the matte
        (14, 9, 45, 24, "RENDER"),
    ],
    # 76/77 tokens, counted with the local tokenizer BEFORE running rather than
    # after a failed generation. The first draft was 92 and the second 79; what
    # came out was wording, never a feature of the joke or `sharp focus, fine
    # detail`.
    prompt=("a photograph of a cheap apartment block imitating a castle, shallow "
            "regular crenellations, a stair tower as a turret, a mock portcullis "
            "over a plain door, render flaking off blockwork, water stains, a "
            "satellite dish and an air conditioner on the battlements, washing on "
            "a line, a wheelie bin, sharp focus, fine detail"),
    surround={"mode": "rects", "keep": [(0, 0, 51, 37)]},
    scenery=("GROUND",),
    clear=(134, 2, 185, 41),
    name_lines=["CASA EL", "CHEAPO"],
    # 32x16 CEL PX AT GAME (148,11), SIZED FROM 1x. Sierra's own 5-row cap height, the port's
    # 3x5 metric:
    #
    #   CASA EL CHEAPO  on one line   51 x  5
    #   CASA / EL CHEAPO              33 x 11
    #   CASA EL / CHEAPO              25 x 11
    #
    # The collision-free pocket here is the whole cel above the marble - x 134..185,
    # y 2..29, 52x28, because the marble only sweeps y 30..35 - so all three FIT. Fourteen
    # characters is a far kinder name than THE INDUSTRIAL REVOLUTION's twenty-five.
    #
    # CASA EL / CHEAPO is chosen because at 25 px it is the narrowest, which leaves the
    # turret its own columns instead of the board running over them. Interior at a 2-cel
    # bevel is 28x12 against the 25x11 it needs. The board ends at y 20 and the DOOR CEL
    # starts at y 23.
    name_rect=(14, 9, 45, 24),
    name_ink=(0xF0, 0xEC, 0xE0),
    sign={
        "rect": (14, 9, 45, 24),
        "lines": ["CASA EL", "CHEAPO"],
        # CHEAP MOULDED PLASTIC, which is the opposite end of the range from Needful Things'
        # varnished timber and Hire Ground's gilt moulding. Thin (depth 0.25 against 0.50
        # and 0.55), a white plastic edge instead of a bevel, galvanised screws instead of
        # brass, almost no grain because plastic is smooth, and relief 0.04 because the
        # lettering is PRINTED rather than signwritten. Her red is kept - the identity.
        #
        # It cannot be hung CROOKED, which the brief asks for: `sign_board` refuses to
        # rotate, because pic 11 is a flat elevation and a rotated board would be the
        # invention rather than the fidelity. The cheapness is carried by the material.
        "face": (0xC8, 0x3A, 0x2E), "face_hi": (0xE4, 0x58, 0x44),
        "edge_hi": (0xE8, 0xE4, 0xDC), "edge_lo": (0x6A, 0x18, 0x12),
        "ink": (0xF0, 0xEC, 0xE0), "ink_hi": (0xFF, 0xFF, 0xF8),
        "ink_lo": (0x70, 0x1A, 0x14),
        "bolt": (0x8A, 0x8A, 0x86), "bolt_hi": (0xC8, 0xC8, 0xC4),
        "depth": 0.25, "shadow": 0.5, "shadow_strength": 0.45,
        "grain": 0.03, "relief": 0.04, "fill": 0.86,
        "prompt": ("a close photograph of a cheap moulded plastic sign panel screwed to a "
                   "rendered wall, printed lettering, sun faded and grimy, a cracked "
                   "corner, overcast daylight, sharp focus, fine detail"),
        "negative": ("blurry, out of focus, bokeh, 3d render, cgi, vector, illustration, "
                     "flat, cartoon, pixelated, jpeg artifacts, people, distorted, frame, "
                     "vignette, smoke, steam, fog"),
        "strength": 0.22, "cfg": 7.0, "steps": 24, "control": 0.8, "feather": 7.0,
        "marry": False,
    },
    # ITS OWN NEGATIVE, BECAUSE THE SHARED ONE CONTAINS `noise` AND THIS BUILDING NEEDS
    # DIRT. `noise` is in NEGATIVE to suppress jpeg artefacts, and it suppresses grain,
    # grime and surface variation with them - which is the whole brief here. Three passes
    # returned a clean new castle while the prompt asked for flaking render and water
    # staining, and the authored stain bars came back as shuttered windows.
    #
    # So `noise` is dropped FOR THIS BUILDING ONLY and four terms are added that push the
    # other way. NEGATIVE itself is untouched: it is shared on purpose, and editing it
    # would quietly change ten buildings that are already accepted.
    negative=(
        "text, letters, words, writing, lettering, signage, numbers, logo, "
        "watermark, blurry, out of focus, bokeh, 3d render, cgi, vector, "
        "illustration, flat, cartoon, pixelated, jpeg artifacts, people, "
        "distorted, frame, vignette, smoke, steam, fog, clean, new, "
        "pristine, well maintained"),
    # CONSTRUCTED AFTER THE GENERATION, because the model would not draw any of it. Five
    # passes and four levers are in BUILDINGS.md 36; this is the fifth lever and the one
    # that cannot be ignored, because no model is involved. See tools/grime.py.
    #
    # THE FIRST SET WAS FAR TOO HEAVY and is recorded rather than deleted: the `sky` op
    # tiled her own board from x 134..145 on the theory that her lettering only ran
    # x 146..174, and those columns at those rows are HER TENEMENT'S DARK ROOF - her cel is
    # a tenement that fills its box and there is no sky in it to borrow. The merlon gaps
    # filled with pieces of her building. The patches pasted grey slabs and the coping
    # turned the merlons into boxes. Everything here is now a MULTIPLY or a blend, and the
    # sky is a gradient between two values sampled from the board beside the region.
    grime=[
        # 1. the sky, built rather than borrowed - (136,135,167) her left, (143,138,181) her
        #    right, lightened a little towards the top as a sky is
        ("skyfill", 0, 7, (0x96, 0x94, 0xB8), (0x84, 0x82, 0xA4)),
        # 2. the merlons get a section instead of an outline: a lit top course, its shadow,
        #    a dark underside, a lit left return
        ("coping", 2, 3, 5, 5), ("coping", 10, 3, 13, 5), ("coping", 18, 3, 21, 5),
        ("coping", 26, 3, 29, 5), ("coping", 34, 3, 37, 5), ("coping", 42, 3, 45, 5),
        ("coping", 0, 1, 3, 3), ("coping", 6, 1, 9, 3), ("coping", 12, 1, 15, 3),
        # 3. and the crenellation throws a shadow on the wall below it
        ("shadow", 2, 7, 51, 8, 0.26),
        ("shadow", 0, 5, 15, 6, 0.26),
        # 4. WATER RUNS OFF A PARAPET THROUGH THE GAPS, so that is where the streaks start.
        #    The sign covers local x 14..45, y 9..24, so only the ends show above it.
        ("streak", 7, 8, 24, 1.5, 0.22), ("streak", 47, 8, 24, 1.5, 0.22),
        ("streak", 7, 25, 33, 1.5, 0.16), ("streak", 23, 25, 33, 1.5, 0.16),
        ("streak", 31, 25, 33, 1.5, 0.16), ("streak", 39, 25, 33, 1.5, 0.16),
        ("streak", 47, 25, 33, 1.5, 0.16),
        # 5. staining below the damp course and in the sheltered corners
        ("stain", 18, 32, 47, 33, 0.22),
        ("stain", 0, 28, 16, 33, 0.20),
        ("stain", 2, 31, 51, 33, 0.18),
        # 6. blown render showing a darker substrate - small, blended at 45%, and kept well
        #    clear of the sign board
        ("patch", 47, 11, 50, 15, (0x82, 0x78, 0x66)),
        ("patch", 5, 13, 9, 17, (0x82, 0x78, 0x66)),
    ],
    cond_blur=0.25,
    init_blur=0.5,
    scale=18,
    # 0.80, NOT 0.88: the init's colours are the only colour instruction there is (section
    # 29), and at 0.88 the grubby render was normalised back to clean cream.
    strength=0.80,
    control=0.45,
    notes=("Fourteen spoken clips, ids 300-313, say `Welcome to Low Cost Housing` and are "
           "deliberately NOT renamed, as with every other building."),
)

# --- Black's Market -> Bare Necessities -------------------------------------------
# Cel 13, 43x37 at (25,69), 100% opaque. NOT in the picPatch: that strip covers game
# x 68..250 and this cel ends at x 67, missing it by one pixel.
#
# THIS IS THE SHOP WHERE THE MUGGING HAPPENS - `MainViewModel` carries
# `LocationId.BlacksMarket => 50, // Random(0,50) == 0` - and the soundtrack already leans
# on a semitone above the tonic for it. So this is the FIRST of six that is not a glazed
# shop, and its prompt goes somewhere the others never did: concrete, roller shutter, worn
# paint, hard shadow. No reflection, no depth, no lit display.
#
# THE DARK MASS AT THE BOTTOM LEFT IS NOT A FIGURE. It was checked before anything was
# masked, because deleting a lurking figure outside the mugging shop would have been a
# worse version of nearly erasing `APPLIANCE STORE`. Enlarged, it has a brown trunk-like
# vertical above it and an irregular organic mass below, with no head-and-shoulders
# articulation: it is a tree base in deep shadow. So the shadow does the work instead, and
# the init keeps a dark mass at that corner rather than inventing a person there.
BUILDINGS["blacksmarket"] = Building(
    key="blacksmarket",
    sierra="Black's Market",
    new_name="Bare Necessities",
    source="cel:13",
    region=(25, 69, 43, 37),
    hotspot=(24, 69, 67, 114),
    palette={
        "ROOF":    (0x20, 0x28, 0x30),   # the dark pitched roof the name sits on, 9.4%
        "ROOF_LO": (0x00, 0x00, 0x00),
        "WALL":    (0x50, 0x70, 0x98),   # the frontage, 6.7%
        "SHUTTER": (0x40, 0x48, 0x58),
        "GLASS":   (0xA0, 0xB0, 0xC8),   # what little there is
        "GLASS_LO": (0x78, 0xA8, 0xD0),
        "GOODS":   (0xE0, 0x38, 0x38),   # the red items in the windows
        "SHADOW":  (0x38, 0x28, 0x18),   # the tree base at the left - see above
        "TRUNK":   (0x78, 0x60, 0x58),
        "KERB":    (0xE0, 0x70, 0x80),
        "GROUND":  (0x88, 0x90, 0xA0),
    },
    init=[
        (0, 0, 42, 36, "GROUND"),
        # the pitched roof, stepped. The name goes on this, so it stays plain.
        (6, 0, 36, 2, "ROOF"),
        (3, 2, 39, 4, "ROOF"),
        (1, 4, 41, 19, "ROOF"),
        (1, 18, 41, 19, "ROOF_LO"),      # a shadow line under the eaves
        # the frontage: mostly solid, with shutters rather than display windows
        (2, 20, 40, 34, "WALL"),
        (3, 22, 15, 33, "SHUTTER"),
        (28, 22, 39, 33, "SHUTTER"),
        (17, 21, 26, 33, "GLASS"),       # the one window
        (18, 23, 25, 30, "GLASS_LO"),
        (19, 25, 24, 29, "GOODS"),       # something red behind it
        # the tree at the left, and the deep shadow at its base
        (0, 16, 5, 26, "TRUNK"),
        (0, 24, 7, 36, "SHADOW"),
        (0, 34, 42, 36, "KERB"),
    ],
    # CONCRETE AND SHUTTER, NOT GLASS AND REFLECTION. The three glazed shops all wanted
    # depth behind the window; this one wants the opposite, and the vocabulary has to say
    # so or it will come back looking like the others.
    prompt=("a photograph of a shabby corner shop, bare concrete and worn painted render, "
            "a steel roller shutter, one small barred window, peeling paint and rust "
            "streaks, hard raking shadow, overcast light, sharp focus, fine detail, "
            "architectural photography"),
    # The kerb pink and the ground grey are scene; the building is the roof and frontage.
    # THE KEEP RECTS WERE THE SECOND WORST FRINGE ON THE BOARD. Inset from a 43x37 cel they
    # left 13.5% of her art inside the region, 98% of it in the outer two pixels and 80% of
    # the outer one-pixel border - a frame of 1990 art around a photograph, which is the
    # blended rectangle the Factory taught us to stop drawing (BUILDINGS.md 3, 17). The
    # generation now reaches the cel's edge everywhere and the MATTE decides the outline,
    # so every join lands on a boundary the scene already has: the roofline, the shopfront,
    # the kerb, the tree. Install-only - the facade that exists was generated from this
    # same init and needs no GPU.
    surround={"mode": "rects", "keep": [(0, 0, 42, 36)]},
    # Scene, not building - and the list is TWO keys, not four. GROUND is the base layer,
    # so without it the matte is the whole rectangle and nothing has been fixed; KERB is her
    # pavement, which runs the width of the board, so cutting there lands the join on a line
    # the scene already has.
    #
    # TRUNK AND SHADOW STAY AS BUILDING, and that was decided by looking. They model the
    # dark mass at the bottom-left corner - a tree base in deep shadow (see above), which
    # stands IN FRONT of the shop. Cutting them showed HER 1990 blob, upscaled, against our
    # photograph: four cel pixels of mush where the eye goes first. Kept as building, the
    # model's own dark mass fills her composition instead. The boundary is the same; what
    # differs is which side of it is 2026.
    scenery=("GROUND", "KERB"),
    # The eraser, and it is REQUIRED the moment a matte is introduced: earlier installs
    # of this building wrote generated ground, kerb and tree across the whole region, and
    # the matte no longer covers them, so they would survive outside the silhouette
    # forever. Erasing costs no GPU (BUILDINGS.md 24).
    # HER DIAGONAL SHOULDERS, SMOOTHED. The `init` draws them as treads - tops 4, 4, 2, 2,
    # 2, 0 on the left and the mirror on the right - and `building_matte` renders that at
    # cel resolution, so the bevel is four 24 px jogs instead of a line. These samples are
    # the SAME shape at the same endpoints, interpolated: nothing about the design changes,
    # only the resolution it is drawn at.
    #
    # Her own board agrees about the shape and not about the extent. Measured as the first
    # non-foliage row per column (a test that has to see foliage IN SHADOW, or it reads her
    # dark leaves as the sign board):
    #
    #     col  3  4  5  6  7  8 ... 36 37 38 39 | 40 41 42
    #     row 11  7  5  5  4  4 ...  4  4  3  3 | 19 18 20
    #
    # so her shoulder is a steeper diagonal starting a column further right, and her right
    # edge is a CLIFF at column 40 where ours bevels. Following her there would narrow a
    # shipped building, which is a design change and not this fix; it is written down here
    # instead. COLUMN 0 IS NOT IN THE SPAN: its top is row 16, the vertical left wall of
    # the shopfront, and a wall is not a staircase.
    crown=((1, 4.0), (2.5, 3.0), (5.5, 1.0), (7, 0.0),
           (35, 0.0), (36.5, 1.0), (39.5, 3.0), (41, 4.0)),
    matte_file="matte_688x592.png",
    clear=(25, 69, 67, 105),
    name_lines=["BARE", "NECESSITIES"],
    # Her own two bands, short over long, exactly as BARE over NECESSITIES. `BARE` is 15px
    # against 28 and `NECESSITIES` is 39px against 41 - tight, and it fits. They are now
    # ONE constructed board carrying both lines; see `sign` below. `name_rect` is kept
    # because it records where her lettering was.
    name_rect=(1, 4, 41, 19),
    name_ink=(0xC0, 0xE0, 0xE0),          # her own pale blue-white
    # THE SIGN IS AN OBJECT. Zain on the old one: "Bare necessities doesn't look like a
    # sign, it looks like text over a building, quite dumb." It was flat type stamped into
    # the already-downscaled cel - the decal that `sign_board.py` exists to replace.
    #
    # IT IS NOT NEEDFUL THINGS' PLAQUE, AND THE VOCABULARY SAYS SO. That one is varnished
    # timber, gilded relief and brass bolt heads, because a pawnbroker's is. This is the
    # mugging shop: bare concrete, a roller shutter, worn paint. So the board is a PAINTED
    # STEEL PANEL - thin (depth 0.40 against the plaque's 0.50), a galvanised edge rather
    # than a warm bevel, a hard shadow (0.70 against 0.55) to match the raking light the
    # facade was generated under, almost no grain, and relief 0.06 because enamel on steel
    # is painted flat, not carved and gilded.
    #
    # The ink is HERS, unchanged - a sign's colour is a fact about the building's identity
    # rather than about the material the model chose (the Hire Ground finding).
    #
    # 41x13 cel px on a 43x37 cel = 95% of the facade's width and 35% of its height, and
    # that width is forced rather than picked: NECESSITIES measures 39px in Sierra's own
    # 3px-cap metric against the 41 her band offers.
    sign={
        # y 6, NOT 5. At y 5 the board's top-left corner took ONE swept pixel from the
        # marble at path step 144 (24,74) - the cel is 9x8 bottom-centred, so it covers
        # x 20..28 y 67..74 there and just clips the corner. The rule is absolute: no
        # sign may overlap the marble's swept footprint (BUILDINGS.md 6).
        "rect": (1, 6, 41, 18),
        "lines": ["BARE", "NECESSITIES"],
        "face": (0x3C, 0x46, 0x52), "face_hi": (0x56, 0x62, 0x70),
        "edge_hi": (0x96, 0xA4, 0xB2), "edge_lo": (0x10, 0x14, 0x1A),
        "ink": (0xC0, 0xE0, 0xE0), "ink_hi": (0xEC, 0xFA, 0xFA),
        "ink_lo": (0x2A, 0x38, 0x42),
        "bolt": (0x5E, 0x68, 0x72), "bolt_hi": (0xAE, 0xBC, 0xC8),
        "depth": 0.40, "shadow": 1.0, "shadow_strength": 0.70,
        "grain": 0.05, "relief": 0.06, "fill": 0.86,
        "prompt": ("a close photograph of a painted steel sign panel bolted to a concrete "
                   "wall, flat enamel paint, chipped edges, rust streaks, hard raking "
                   "daylight, sharp focus, fine detail"),
        "negative": ("blurry, out of focus, bokeh, 3d render, cgi, vector, illustration, "
                     "flat, cartoon, pixelated, jpeg artifacts, people, distorted, frame, "
                     "vignette, smoke, steam, fog"),
        "strength": 0.22, "cfg": 7.0, "steps": 24, "control": 0.8, "feather": 7.0,
        # Same verdict as the Pawn Shoppe: the marrying pass took the bolt heads and the
        # bevel off and added no material the construction did not already have.
        "marry": False,
    },
    detect=[
        # Her `black's` band, NOT `market`, and measured rather than assumed. `market` sits
        # on a light ground, so a brightness predicate over it is 88-100% TRUE and matches
        # any bright rectangle; `paleish` over it is 5.7% and matches any band. `black's` is
        # light-on-dark at 33%, which is the range that actually discriminates.
        Detect("A board sign", "cel", (9, 5, 35, 11), "litglyph"),
        Detect("B interior sign", "view_803_l0_c0.png", (3, 2, 112, 21), "redish"),
    ],
    surfaces=[
        Surfaces(file="view_803_l0_c0.png",
                 plate=(2, 1, 115, 23),
                 body=(0x40, 0x48, 0x58),  # the slate panel, measured
                 ink=(0xC0, 0x38, 0x38),   # her red, measured
                 lines=["BARE NECESSITIES"]),
    ],
    notes=("Fourteen spoken clips (530-543) say `Welcome to Black's Market`; left alone. "
           "The mugging roll lives at `LocationId.BlacksMarket => 50`."),
)



# --- THE BOARD'S MISSING MIDDLE -> a town centre -----------------------------------
# NOT A BUILDING. Zain: "I don't want all that white space there, it looks silly, can't you
# shrink that slightly and generate the missing part of the town?"
#
# Sierra left the centre of pic 11 blank and sized the shop dialog to fill it. Flood-filled
# from (160,100) the pale field is 19,330 px = 30.2% of the whole board, and `clear`-sized
# rectangles do not describe it - see tools/town_centre_mask.py, which measures the shape,
# subtracts the picPatch and writes the matte.
#
# THE REGION IS x 69..249, y 45..154 = 181x110, AND THE MATTE FILLS 97.0% OF IT.
#
# THREE MEASUREMENTS THAT DECIDED THE WORK, none of them assumed:
#
#  1. THE EDGE IS HERS AND IT IS THE BEST ON THE BOARD. She drew a ONE-PIXEL BLACK FRAME
#     round the panel - (0,0,0) at y 44 above, x 68 at the left. Over all 640 boundary
#     pixels the step across the outline in her own art is 204.0 per channel with 0.3%
#     below 10. Rentflix's best side is 98.9; the Factory scores "nothing to land on" on
#     all four. Every join here lands on a hard line she drew, and NOTHING is feathered.
#  2. THE MARBLE NEVER ENTERS. 0 of 170 `MarblePath` anchors are inside and 0 swept pixels
#     fall in it; the nearest is 2.0 px outside the edge. The ring road passes around the
#     centre, so nothing generated here is walked on and nothing here can collide with a
#     sign. This was expected to be the hard constraint and it is not a constraint at all.
#  3. THE picPATCH TOUCHES IT BY ELEVEN PIXELS. Of its 2071 opaque pixels, 11 land inside -
#     antialiasing fringe along the bottom edge. They are SUBTRACTED from the matte. That
#     is the bug that bit the University and the Employment Office, closed by subtraction
#     rather than by hoping it would not matter.
#
# WHAT IT CONTAINS, AND WHY. The thirteen buildings face INWARD, so the middle must read as
# what they face onto. Four blocks of low shops with pitched roofs in the corners, a paved
# plaza running north-south and east-west between them, a lawn and a pond at the crossing,
# street trees and parked cars along the kerbs. The plaza arms run to all four edges, so
# the streets read as continuing under her frame rather than stopping at it.
#
# AND THE PLAZA IS ALSO THE TILE GRID. 181x110 at 12x is 2,172x1,320 = 2.87M px, three and
# a half times this card's measured 810k ceiling, so the detail pass has to be tiled. The
# tile joins are placed at x 60, x 120 and y 54 - all three INSIDE the plaza, which is a
# uniform paved surface. A tile join, like every other join in this pipeline, lands on a
# line the scene already has.
BUILDINGS["towncentre"] = Building(
    key="towncentre",
    sierra="(the blank centre)",
    new_name="",
    source="background",
    region=(69, 45, 181, 110),
    hotspot=(69, 45, 249, 154),
    matte_file="matte_181x110.png",
    margin_generated=True,
    # THE PALETTE IS LIFTED TOWARDS HER RING, BY A MEASURED AMOUNT, NOT BY EYE.
    # Measured over her board outside the hole against the installed centre:
    #
    #     her ring   mean HSV saturation 0.370   mean value 0.514
    #     the centre                     0.309                0.377
    #
    # The centre was at 83% of her saturation and 73% of her brightness, and the larger
    # gap was BRIGHTNESS, which is not what it looked like. Half of each gap is closed -
    # target 0.340 and 0.445 - and not more, because matching her outright would make the
    # middle the loudest thing on the board. Since the house rule forbids colour words in
    # the prompt, the only place to apply it is HERE: every key scaled in HLS by
    # saturation x1.316 and lightness x1.157, which lands the INIT at 0.279/0.483 and is
    # predicted to land the OUTPUT at the target. The achieved figure is in BUILDINGS.md.
    palette={
        "ROAD":     (0x55, 0x58, 0x5E),
        "ROAD_LO":  (0x42, 0x45, 0x4B),
        "MARK":     (0xE2, 0xDF, 0xD1),
        "PAVE":     (0xB4, 0xB0, 0xA7),
        "PLAZA":    (0x9F, 0x9B, 0x92),   # darker than it looks right: see the init
        "GRASS":    (0x5A, 0xC8, 0x2D),
        "TREE":     (0x1F, 0x7A, 0x1F),
        "WATER":    (0x34, 0x8B, 0xD6),
        "WALL":     (0xD2, 0xC9, 0xBA),
        "WALL_LO":  (0xA4, 0x9B, 0x8C),
        # EIGHT RANGES, FIVE MATERIALS. One maroon across all four blocks read as a
        # repeated pattern rather than a town, and it is not something a detail pass can
        # fix - tiling detail onto four identical slabs spends the pass on the wrong thing.
        # Slate and lead are cold, pantile is warm, asphalt is the flat service roof; the
        # blocks are paired so that no two adjacent ones share a front range.
        "SLATE":    (0x51, 0x64, 0x76),
        "SLATE_LO": (0x33, 0x41, 0x4E),
        "LEAD":     (0x8B, 0x90, 0x92),
        "LEAD_LO":  (0x62, 0x69, 0x6C),
        "PANTILE":  (0xC9, 0x5F, 0x31),
        "PANTILE_LO": (0x8C, 0x3D, 0x1F),
        "PANTILE_D": (0x9D, 0x47, 0x2F),
        "ASPHALT":  (0x45, 0x45, 0x48),
        "ASPHALT_LO": (0x2E, 0x2E, 0x31),
        "CAR_A":    (0xFA, 0x00, 0x00),
        "CAR_B":    (0x05, 0x46, 0xCF),
        "CAR_C":    (0xE7, 0xE7, 0xE7),
    },
    init=[
        # THE STREETS ARE THE TILE GRID, and the parcels between them are BUILT. Pass two
        # put a 63px plaza down the middle and the model painted it pale: on the board the
        # centre still read as the white space this job exists to remove. Four blocks of
        # shops, a green and a square now fill the parcels, and the three tile joins sit on
        # the streets' own centre lines - x 60, x 120 and y 54 are inside `MARK`.
        (0, 0, 180, 109, "PAVE"),
        # EACH BLOCK IS TWO RANGES WITH A SERVICE YARD BETWEEN THEM, NOT A ROOF OVER A
        # YARD. Pass four gave each block one roof and a `WALL` band below it, and at
        # native 12x that band rendered as a large blank cream forecourt - pale emptiness,
        # which is the exact thing this job exists to remove. Built edge to edge instead:
        # a front range, a three-row yard, a rear range, and an alley through both.
        # NW: slate over a lead service wing
        (2, 2, 52, 46, "PAVE"),
        (2, 2, 52, 22, "SLATE"), (2, 21, 52, 23, "SLATE_LO"),
        (2, 26, 52, 44, "LEAD"), (2, 43, 52, 45, "LEAD_LO"),
        (24, 2, 27, 46, "ROAD_LO"),
        # NE: pantile over flat asphalt
        (128, 2, 178, 46, "PAVE"),
        (128, 2, 178, 22, "PANTILE"), (128, 21, 178, 23, "PANTILE_LO"),
        (128, 26, 178, 44, "ASPHALT"), (128, 43, 178, 45, "ASPHALT_LO"),
        (152, 2, 155, 46, "ROAD_LO"),
        # SW: the cold block - lead in front, slate behind
        (2, 62, 52, 107, "PAVE"),
        (2, 62, 52, 82, "LEAD"), (2, 81, 52, 83, "LEAD_LO"),
        (2, 86, 52, 105, "SLATE"), (2, 104, 52, 106, "SLATE_LO"),
        (26, 62, 29, 107, "ROAD_LO"),
        # SE: the warm block - two pantile ranges at different ages
        (128, 62, 178, 107, "PAVE"),
        (128, 62, 178, 82, "PANTILE_D"), (128, 81, 178, 83, "PANTILE_LO"),
        (128, 86, 178, 105, "PANTILE"), (128, 104, 178, 106, "PANTILE_LO"),
        (150, 62, 153, 107, "ROAD_LO"),
        # the north green, with a gravel walk across it
        (66, 2, 114, 46, "GRASS"),
        (66, 20, 114, 24, "PLAZA"),
        (78, 8, 81, 11, "TREE"), (96, 8, 99, 11, "TREE"), (72, 30, 75, 33, "TREE"),
        (104, 30, 107, 33, "TREE"), (86, 38, 89, 41, "TREE"),
        # the south square, and a pond stepped rather than square so the edge map is not
        # handed a rectangle - pass two authored a square inside a square and got a target
        (66, 62, 114, 107, "PLAZA"),
        (76, 72, 104, 96, "GRASS"),
        (84, 78, 96, 90, "WATER"), (82, 80, 98, 88, "WATER"), (86, 76, 94, 92, "WATER"),
        (70, 66, 73, 69, "TREE"), (107, 66, 110, 69, "TREE"),
        (70, 100, 73, 103, "TREE"), (107, 100, 110, 103, "TREE"),
        # the streets, cut through everything, reaching the frame on all four sides
        (55, 0, 65, 109, "ROAD"), (59, 0, 61, 109, "MARK"),
        (115, 0, 125, 109, "ROAD"), (119, 0, 121, 109, "MARK"),
        (0, 49, 180, 59, "ROAD"), (0, 53, 180, 55, "MARK"),
        # parked cars at the kerbs, nose to tail
        (56, 6, 59, 14, "CAR_A"), (56, 18, 59, 26, "CAR_C"), (56, 30, 59, 38, "CAR_B"),
        (62, 66, 65, 74, "CAR_B"), (62, 78, 65, 86, "CAR_A"), (62, 90, 65, 98, "CAR_C"),
        (116, 6, 119, 14, "CAR_C"), (116, 18, 119, 26, "CAR_B"),
        (121, 66, 124, 74, "CAR_A"), (121, 78, 124, 86, "CAR_C"),
        (121, 90, 124, 98, "CAR_B"),
        (8, 50, 16, 53, "CAR_B"), (20, 50, 28, 53, "CAR_A"), (32, 50, 40, 53, "CAR_C"),
        (140, 56, 148, 59, "CAR_A"), (152, 56, 160, 59, "CAR_B"),
        (164, 56, 172, 59, "CAR_C"),
    ],
    # NO COLOUR WORDS, as everywhere else in this table.
    # 76/77 TOKENS, AND IT WENT OVER ONCE. Naming three roof materials cost four
    # tokens and the run died at 79. What came back out was synonym and filler -
    # "small", "stone" before setts, "casting" - never `sharp focus, fine detail`,
    # never `overcast daylight`, and never a material. Count before running: the
    # tokenizer is local, at MODELS/gen/sd15/tokenizer.
    prompt=("a photograph looking straight down at a town centre, slate and clay "
            "pantile roofs with ridges and chimneys, lead flats over the service "
            "wings, a square of setts, a mown lawn and pond, tarmac streets with "
            "parked cars at the kerbs, gravel paths, clipped hedges, trees and "
            "shadows, overcast daylight, sharp focus, fine detail, aerial "
            "photography"),
    # The matte comes from `matte_file`, not from the init, so every init key is building.
    surround={"mode": "rects", "keep": [(0, 0, 180, 109)]},
    name_lines=[],              # it has no name: it is ground, not a shop
    name_rect=(0, 0, 0, 0),
    name_ink=(0, 0, 0),
    # 181x110 at 6 px per game px = 1,086x660 = 716,760 px, inside the card's measured
    # 810k ceiling, and the ONLY scale at which one generation can see the whole centre.
    # The detail pass tiles at 12. See BUILDINGS.md.
    scale=6,
    # STRENGTH 0.80, NOT 0.92, AND THE REASON IS THE PALETTE. There is nothing of hers to
    # preserve inside the frame, so the first instinct was to turn the model loose - but
    # the house rule is NO COLOUR WORDS IN THE PROMPT, which means the INIT'S COLOURS ARE
    # THE ONLY COLOUR CONTROL THERE IS. At 0.92 they barely survive and the centre came
    # back washed out and pink against her saturated ring; at 0.80 the init's greens,
    # tarmac and pantile carry through and the block plan still renders as photograph.
    strength=0.80,
    control=0.55,
    # The street layout is the whole point of stage A, and the TILE JOINS depend on the
    # plaza landing where the init puts it. At the default 1.0 the conditioning's edges are
    # 18 px wide on this canvas, which is an instruction to paint soft; 0.30 gives 5.4 px -
    # thin lines on the block boundaries, which is a structure to follow.
    cond_blur=0.30,
    # The init's own blur, for the same reason: at 6x the default is three cel px.
    init_blur=0.35,
    # STAGE B. Six tiles at 12 px per game px, the shipping rung, each denoised just
    # enough to resolve what stage A settled. The joins are x 60, x 120 and y 54, and
    # all three sit on a street's own centre marking - the plaza was widened from 59 px
    # to 63 to put them there before a single pixel was generated.
    detail_scale=12,
    tiles={"cols": (60, 120), "rows": (54,), "overlap": 10,
           "strength": 0.35, "control": 0.70, "steps": 28, "cfg": 7.0},
    seed=7,
    notes=("Not a shop and not in `Board.All`: the hotspot is the region itself. No name, "
           "no sign, no surfaces, no spoken clips. `find_name_art` has nothing to find."),
)

# --- QT Clothing -> Common Threads ------------------------------------------------
# Cel 7, 40x36 at (257,80), 100% opaque. NOT "cel 9 at (277,113)": the 9 in Board.cs is the
# PLACE index and (277,113) is `clothingP`, the shopkeeper's animation anchor. Checked
# rather than trusted, because the Factory taught that the hotspot and the art are
# different things.
#
# Her sign is TWO BANDS - `Q.T` in green on a dark gable at y 2..6, and `CLOTHING` in the
# same green across a PINK STRIPED AWNING at y 9..13. The pink is awning, not lettering.
# There is NO device: the full stop in `Q.T` is punctuation, and the red and green shapes
# at the sides are garments on rails, which is shopfront content. Socket City's plug was a
# letter doing double duty; this shop has no equivalent and none is invented.
BUILDINGS["qtclothing"] = Building(
    key="qtclothing",
    sierra="QT Clothing",
    new_name="Common Threads",
    source="cel:07",
    region=(257, 80, 40, 36),
    hotspot=(251, 82, 311, 118),
    palette={
        "GABLE":   (0x28, 0x28, 0x38),   # the dark roof the first line sits on
        "AWN":     (0xF8, 0xB0, 0xA0),   # the awning, 7.2% of the cel
        "AWN_LO":  (0xE0, 0x70, 0x80),   # its darker stripe
        "CANOPY":  (0x60, 0x70, 0xC8),   # the entrance canopy
        "CANOPY_HI": (0x58, 0x90, 0xE0),
        "GLASS":   (0x48, 0x50, 0x60),   # the shop window
        "GLASS_LO": (0x40, 0x48, 0x58),
        "GARMENT": (0xE0, 0x70, 0x80),   # clothes on a rail, left
        "GARMENT2": (0x48, 0xA8, 0x48),  # and right
        "PALE":    (0xF8, 0xC0, 0xA0),   # the door columns
        "GROUND":  (0xD0, 0x90, 0x58),
    },
    init=[
        (0, 0, 39, 35, "GROUND"),
        # the gable, stepped so it reads as a pitched roof rather than a box
        (13, 0, 26, 2, "GABLE"),
        (10, 2, 29, 4, "GABLE"),
        (8, 4, 31, 6, "GABLE"),
        (6, 6, 33, 8, "GABLE"),
        # THE LETTERING BAND IS A DARK FASCIA, NOT THE AWNING, and the first draft of this
        # init had it wrong. Re-reading her row map: `CLOTHING` sits at y 9..13 in green on
        # #282838 DARK, and the pink stripes only begin at y 14. Putting the awning at y 9
        # produced green lettering on pink-and-white stripes, which at true size breaks up
        # and is barely readable - the same contrast failure as Socket City's red plug on
        # red text, found the same way, by looking at 1x.
        (1, 9, 38, 13, "GABLE"),
        # the striped awning below it. 2px stripes: 1px is thinner than the blur and would
        # not be in the init at all.
        (1, 14, 38, 19, "AWN"),
        (1, 15, 38, 16, "AWN_LO"),
        (1, 18, 38, 19, "AWN_LO"),
        # the shopfront below it
        (2, 20, 37, 31, "GLASS"),
        (2, 20, 12, 31, "GLASS_LO"),
        (27, 20, 37, 31, "GLASS_LO"),
        (3, 22, 10, 30, "GARMENT"),      # garments behind the left window
        (29, 22, 36, 30, "GARMENT2"),    # and the right
        (13, 20, 26, 26, "CANOPY"),      # the entrance canopy
        (15, 21, 24, 24, "CANOPY_HI"),
        (14, 27, 16, 31, "PALE"),        # the two columns either side of the door
        (23, 27, 25, 31, "PALE"),
        (17, 27, 22, 31, "GLASS_LO"),    # the doorway itself
        (0, 32, 39, 35, "GROUND"),
    ],
    # Glass, reflection and depth behind it - the vocabulary that made Socket City the best
    # of the five. A clothes shop has the same asset: a lit window with things behind it.
    prompt=("a photograph of a small clothing boutique shopfront, striped canvas awning, "
            "plate glass window with reflections, mannequins and hanging garments behind "
            "the glass, draped fabric, polished timber frame, bright overcast daylight, "
            "sharp focus, fine detail, architectural photography"),
    # The gable is a triangle and the body a rectangle, so the silhouette follows the slope
    # rather than boxing it - a bounding box here would paint building over the sky either
    # side of the roof.
    # THE MATTE DRAWS THE OUTLINE, NOT INSET RECTANGLES. The keep rects followed the
    # stepped gable and were inset from the cel, so the installer wrote HER pixels
    # everywhere outside them - 366 px, 25.4% of the region, the worst on the board and
    # the reason Zain's rule was written. `np.where(m, new, sierra)` puts her art INSIDE
    # the building; the matte instead decides what is pasted AT ALL, so her scenery stays
    # on the board around the building and none of it is inside it.
    #
    # GROUND IS THE ONLY SCENERY KEY, decided by looking rather than by which keys touch
    # the border. GABLE touches it too - 14 px - and is the dark stepped roof, which is
    # the building. Cutting it would notch her sky through the roofline, which is the
    # Bare Necessities mistake in a different coat.
    surround={"mode": "rects", "keep": [(0, 0, 39, 35)]},
    scenery=("GROUND",),
    # the eraser, sized to the old install's footprint
    # HER ARCHED SHOULDERS, SMOOTHED. The `init` steps 9, 9, 9, 9, 9, 6, 6, 4, 4, 2, 2, 2, 0
    # and mirrors it - EIGHT treads, the most of any building on the board, which is why
    # this one looked worst at 12x despite its risers being the smallest. Same endpoints,
    # interpolated.
    #
    # Her own board, first non-foliage row per column, agrees closely: the shoulder runs
    # from about (4, 9) to (10, 2) and then flattens at 2 to 3, against the init's (5, 9) to
    # (13, 0). The difference is a column of start and three rows of height on the flat -
    # the second of those is the building's own design and is left alone.
    crown=((3.9, 9.0), (5.5, 7.5), (7.5, 5.0), (9.5, 3.0), (12.5, 1.0), (14, 0.0),
           (25, 0.0), (26.5, 1.0), (29.5, 3.0), (31.5, 5.0), (33.5, 7.5), (35.1, 9.0)),
    matte_file="matte_640x576.png",
    clear=(257, 80, 296, 116),
    name_lines=["COMMON", "THREADS"],
    # COMMON is 23px and the gable offers about 24; THREADS is 27px and the awning offers
    # 38. Her own two bands take the new name with no resizing - the second of five that
    # needed no change to the sign's geometry.
    name_rect=(1, 2, 38, 13),
    # SHIFTED OFF THE MARBLE PATH. The Factory's sign was the one Zain saw the player walk
    # through, but it was not the only collision: checking every installed sign against the
    # marble's swept footprint found this one too. The marble comes down the right side of
    # the board and its bottom-left corner clipped the top-right of `COMMON` by 10 px at
    # path step 36, and `THREADS` by 1 px.
    #
    # `COMMON` is narrowed from 24 to 19 px rather than shifted, and `THREADS` moved one
    # column left at full width, so the two stay very nearly concentric (centres at local
    # 17.0 and 18.5). Both now contain ZERO swept pixels.
    #
    # NOTE: this building still carries the REJECTED flat stamped type. Only its position
    # is fixed here; the sign-as-object rebuild waits with the other four.
    name_rects=[(8, 2, 26, 7), (0, 9, 37, 14)],
    name_ink=(0x48, 0xA8, 0x48),          # her own green
    # THE SIGN IS AN OBJECT, NOT TYPE OVER A BUILDING - the Bare Necessities lesson, applied
    # to the second of the two facades still carrying a flat stamp. What was there read as
    # badly as anything on the board: two lines of flat vector green in TWO DIFFERENT SIZES,
    # floating on her dark gable with nothing under them. The sizes differed because the
    # gable is STEPPED and the old `name_rects` fitted each line to the step it landed on.
    #
    # So the board is a rectangle INSCRIBED IN THE STEP, x 8..31 by y 4..13, which is the
    # largest that fits inside her own gable profile:
    #     rows 4..6  x  8..31      <- the board's width is set by this, the narrowest row
    #     rows 6..8  x  6..33
    #     rows 9..13 x  1..38
    # Her dark gable stays visible all round it, which is what makes it read as MOUNTED ON
    # something rather than painted over everything.
    #
    # CAP HEIGHT IS 4 CEL PIXELS AGAINST SIERRA'S OWN 5, and that is the cost of two lines
    # in ten rows. `THREADS` is the long line: 7 characters at 2.85 cel px each is 20 cel px
    # inside a 22 cel px board. It is clean at 12x and MARGINAL AT 1x - a reader at 1x will
    # see two words of the right shape and colour rather than read them letter by letter.
    # One line at cap 5 does not fit: `COMMON THREADS` is 14 characters, 50 cel px, in a
    # region 40 wide.
    #
    # The ink stays her bright green (0x48,0xA8,0x48), which is a deliberate choice about the
    # SHOP, not about the check. I first moved `detect`'s rect to this new board on the
    # reasoning that it kept a colour test alive - THAT WAS WRONG AND IT IS WORTH THE SPACE.
    # The rect is not a search window, it is HER GLYPH TEMPLATE: find_name_art scores a dense
    # mask of her own letterforms against the colour-keyed pixels. Pointing it at our board
    # aims a detector of her art at our art, which can only ever report her name as still
    # present. It did, immediately - ten surfaces. The rect stays on her `CLOTHING` band at
    # y 9..13, and find_name_art's own note at that site explains why it is that band.
    #
    # No bolts: a boutique fascia is a painted timber board with a moulded edge, not a steel
    # panel bolted to concrete. That is the one line that separates this sign from Bare
    # Necessities' - the object has to suit the shop.
    sign={
        "rect": (8, 4, 31, 13),
        "lines": ["COMMON", "THREADS"],
        "face": (226, 214, 190),          # cream enamel on timber
        "face_hi": (242, 232, 212),
        "edge_hi": (252, 246, 232),
        "edge_lo": (120, 104, 82),
        "ink": (0x48, 0xA8, 0x48),       # hers, kept
        "ink_hi": (128, 210, 128),
        "ink_lo": (28, 72, 36),
        "bolts": False,
        "depth": 0.35,
        "shadow": 0.9,
        "shadow_strength": 0.6,
        "relief": 0.08,                   # painted letters, barely proud
        "grain": 0.04,                    # smooth paint, not weathered steel
        "fill": 0.86,
        "feather": 7.0,
        "prompt": "a close photograph of a painted timber shop fascia board, cream enamel "
                  "paint on moulded timber, fine brush texture, slight wear along the "
                  "mouldings, soft overcast daylight, sharp focus, fine detail",
        "negative": "blurry, out of focus, bokeh, 3d render, cgi, vector, illustration, "
                    "flat, cartoon, pixelated, jpeg artifacts, people, distorted, frame, "
                    "vignette, smoke, steam, fog",
        "strength": 0.22,
        "steps": 24,
        "cfg": 7.0,
        "control": 0.8,
    },
    surfaces=[
        Surfaces(file="view_809_l0_c0.png",
                 plate=(2, 19, 114, 36),
                 body=(0x28, 0x18, 0x18),
                 ink=(0x38, 0xB0, 0x38),
                 lines=["COMMON THREADS"]),
    ],
    # Her `CLOTHING` band ONLY. Spanning both bands made the mask mostly background and it
    # matched eighteen surfaces - see the Detect docstring.
    detect=[
        Detect("A board sign", "cel", (1, 9, 38, 13), "brightgreen"),
        Detect("B interior sign", "view_809_l0_c0.png", (2, 21, 110, 33), "brightgreen"),
    ],
    notes=("Nine spoken clips (10-18) all say `Welcome to QT Clothing`, and clip 16 builds "
           "its joke on it - `Wear our clothes and you'll be a QT, too!`. Left alone, as "
           "with ACNE Employment."),
)


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
        "BASE":    (0x90, 0x48, 0x30),   # the stall riser under the display windows
        "PAVE":    (0x88, 0x84, 0x80),   # the two rows her sprite's feet stand on
        "SKY":     (0x58, 0x90, 0xE0),
    },
    init=[
        # Order matters: later boxes paint over earlier ones.
        #
        # The first draft of this was a flat rectangle and it threw away the two things
        # that make the building what it is - Sierra's DOMED CROWN, and any structure at
        # all across the 60% of the canvas that is glass. A featureless slab gives the
        # model nothing to grip and comes back as a blank wall.
        # THE DOORWAY'S ROWS WERE CHOSEN BEFORE ANYTHING ELSE IN THIS INIT, which is the
        # one thing the other twelve buildings did backwards. Sierra's loop 8 sprite is
        # 14x14 drawn at (277,183), so `AddCastView` lands it on game x 270..283 y 170..183
        # - and inside that, her LEAF BODY is rows 0..11 (y 170..181) with only the swinging
        # feet in rows 12..13 (y 182..183). Against this region's origin (255,132) that is
        #
        #     the doorway   cel cols 15..28, cel rows 38..49
        #     her feet      cel rows 50..51, which have to be pavement and not shopfront
        #
        # The previous facade put its ground floor in the last three rows with a display
        # window above, so her leaf had nothing to seat on; `CastDoors` can move a sprite
        # but it cannot invent a doorway for it to land in. Every box below is placed around
        # those two numbers.
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
        (2, 34, 41, 37, "MULLION"),       # the fascia above the shopfront
        # --- THE GROUND FLOOR, built around the doorway ------------------------------
        # What says ENTRANCE rather than WINDOW at this size is not detail, it is one
        # relation: the middle bay runs all the way to the pavement while the two beside it
        # stop on a stall riser. Three bays, the centre one open to the floor.
        (2, 38, 41, 49, "DARK"),          # the recessed shopfront
        (3, 40, 12, 46, "GOODS"),         # appliances behind the left window - 10x7
        (31, 40, 40, 46, "GOODS"),        # and the right
        (2, 47, 12, 49, "BASE"),          # the stall riser the left window stands on
        (31, 47, 41, 49, "BASE"),         # and the right
        (13, 38, 14, 49, "MULLION"),      # the entrance's left jamb, 2px so it survives
        (29, 38, 30, 49, "MULLION"),      # and its right
        (15, 38, 28, 39, "MULLION"),      # its head rail - `head_rows` 2 keeps this
        (15, 40, 28, 49, "GLASS_LO"),     # THE ENTRANCE, glazed, down to the floor
        (21, 40, 22, 49, "MULLION"),      # the stile where her two leaves meet
        (0, 50, 43, 51, "PAVE"),          # the pavement her feet stand on
    ],
    # Glass, reflection and depth behind it - the vocabulary the Factory proved. Not one
    # colour word: GLASS, DARK and FOLIAGE above already say every colour in the picture.
    # THE ENTRANCE IS NAMED IN THE PROMPT because the init alone did not get one last time.
    # Still not one colour word: GLASS, DARK, BASE, PAVE and FOLIAGE above already say
    # every colour in the picture, and `aluminium`, `steel` and `glazed` are materials.
    prompt=("a photograph of a modern glass fronted appliance showroom, curved glazed "
            "facade, a tall glazed entrance with double doors at street level in the "
            "centre of the shopfront, polished aluminium mullions, reflections and depth "
            "behind the glass, lit interior with appliances on display, brushed steel, "
            "bright overcast daylight, sharp focus, fine detail, architectural photography"),
    # The corners carry Sierra's trees. Green is the one thing in this cel that is not the
    # building, and nothing in the building is green.
    # THE MATTE DRAWS THE OUTLINE, NOT A COLOUR TEST. `mode: green` kept whatever of HER
    # cel was green - the foliage in the top corners - and the installer then wrote those
    # 150 px, 6.6% of the region, INSIDE the building. A colour test cannot tell her
    # foliage from ours, and CAST.md 4 says why: it is the same failure as keying a figure
    # against a same-hue backdrop.
    #
    # SKY IS THE ONLY SCENERY KEY, decided by looking. `BASE` touches the border too - 50
    # px - and is the shop's own plinth, not her ground; cutting it would open the bottom
    # of the building. That is the Bare Necessities call again: touching the border and
    # being scenery are different things.
    surround={"mode": "rects", "keep": [(0, 0, 43, 51)]},
    scenery=("SKY",),
    # HER DOME, MEASURED COLUMN BY COLUMN OFF HER OWN BOARD, and the reason this field
    # exists at all. The init's crown is stepped boxes, `building_matte` krons them up at
    # cel resolution, and the result was EIGHT treads with a 36 px riser at 12x - a
    # ziggurat where she drew an arc. tools/crown_mask.py interpolates these samples at
    # 16 px per cel px instead, so the outline steps at 1/16 of a cel.
    #
    # The samples are the topmost row of her cel 8 that is neither her sky nor her foliage,
    # read off `png12x-new` at cel resolution:
    #
    #   col  4  5  6  7  8  9 10 | 11..22 | 23..32 | 33..35 | 36 37 38 39 40 41
    #   row 11 10  8  7  6  4  3 |   2    |   1    |   2    |  4  7  9 10 12 14
    #
    # COLUMNS 0..3 ARE NOT HERS AND ARE NOT USED. The same test returns 4, 2, 2, 3 there,
    # which is not a building: it is the dark branch of her tree at the top-left corner,
    # and a colour test cannot tell that from glazing (CAST.md 4, again). Columns 2 and 3
    # continue her own shoulder's slope instead - 13 and 12 - and are marked as the only
    # two samples here that were not read off the board.
    #
    # Her shoulders sit far lower than the init's: at column 8 the init says row 1 and she
    # says row 6. Following her costs the upper corners of the generated glazing and gives
    # back the arc, which is the whole point.
    crown=((2, 13.0), (3, 12.0),                       # extrapolated, not measured
           (4, 11.0), (5, 10.0), (6, 8.0), (7, 7.0), (8, 6.0), (9, 4.0), (10, 3.0),
           (11, 2.0), (22, 2.0), (23, 1.0), (32, 1.0), (33, 2.0), (35, 2.0),
           (36, 4.0), (37, 7.0), (38, 9.0), (39, 10.0), (40, 12.0), (41, 14.0)),
    matte_file="matte_704x832.png",
    # the eraser, sized to the old install's footprint
    clear=(255, 132, 298, 184),
    name_lines=["WATT A", "BARGAIN"],
    # Measured: her script occupies x 1..42, y 15..35. The device takes the left of that
    # and the name the rest - 13 + 2 + 25 = 40 of the 42 available.
    name_rect=(16, 16, 42, 33),
    name_ink=(0xC0, 0x38, 0x38),          # her own red, so the sign stays her sign
    # THE LAST FACADE ON THE BOARD CARRYING A FLAT STAMP, and it was the worst of them: red
    # vector type floating ON THE PLATE GLASS, off-centre to the right and running to the
    # region's edge, with no object under it at all.
    #
    # The object suits the shop. A discount appliance showroom signs itself with an
    # ILLUMINATED ACRYLIC BOX SIGN in a brushed aluminium frame - flat lit face, applied
    # letters, no relief to speak of. That is the opposite of Bare Necessities' bolted steel
    # and Needful Things' gilded timber, and deliberately so: the material says what kind of
    # place it is, and only the typeface is shared across the town.
    #
    # x 14..41 keeps it inside her glazing (x 2..41) and clear of `device_rect` (x 1..13),
    # where the plug and the bulb live. 28 x 20 cel px is the largest board that does both.
    sign={
        "rect": (14, 15, 41, 34),
        "lines": ["WATT A", "BARGAIN"],
        "face": (236, 238, 240),          # a lit white acrylic face
        "face_hi": (252, 252, 252),
        "edge_hi": (200, 206, 212),       # brushed aluminium
        "edge_lo": (70, 78, 86),
        "ink": (0xC0, 0x38, 0x38),        # hers, kept
        "ink_hi": (0xE8, 0x60, 0x60),
        "ink_lo": (0x60, 0x14, 0x18),
        "bolts": False,                   # a modern box sign hides its fixings
        "depth": 0.5,                     # it stands off the glass
        "shadow": 1.1,
        "shadow_strength": 0.65,
        "relief": 0.06,                   # applied letters, nearly flush
        "grain": 0.02,                    # smooth acrylic, not weathered
        "fill": 0.86,
        "feather": 7.0,
        "prompt": "a close photograph of an illuminated acrylic box sign in a brushed "
                  "aluminium frame, flat white lit face, crisp applied lettering, modern "
                  "shopfront signage, soft overcast daylight, sharp focus, fine detail",
        "negative": "blurry, out of focus, bokeh, 3d render, cgi, vector, illustration, "
                    "flat, cartoon, pixelated, jpeg artifacts, people, distorted, frame, "
                    "vignette, smoke, steam, fog",
        "strength": 0.22,
        "steps": 24,
        "cfg": 7.0,
        "control": 0.8,
    },
    device_rect=(1, 16, 13, 33),
    devices=["plug", "bulb"],
    surfaces=[
        # MEASURED, and the first draft was wrong: the red script runs y 2..17 and
        # `APPLIANCE STORE` sits at y 20..23 directly beneath it. A plate to y 22 would
        # have erased the top of a line that is NOT the brand name and was never asked to
        # change - the same class of mistake as masking the plug out with the lettering.
        Surfaces(file="view_808_l0_c0.png",
                 plate=(72, 1, 180, 18),
                 body=(0x70, 0x88, 0xE0),      # the panel's own field, measured
                 ink=(0xC0, 0x38, 0x38),
                 lines=["WATT A BARGAIN"]),
    ],
    notes=("REBUILT around its own doorway - see BUILDINGS.md 50. The first facade in the "
           "programme whose entrance rows were chosen from `DoorFor` BEFORE the init was "
           "written rather than measured afterwards, and the only one whose door came out "
           "within one row. The previous generation is kept at tools/samples/"
           "socketcity-rename-OLD. "
           "The letter S of `Socket` is drawn as an electrical PLUG - a device and a letter "
           "at once. It is 36% of the red pixels with a 10x13 body, so the name takes most "
           "of the area and a standalone device fits beside it. `Watt a Bargain` puns on "
           "the watt rather than the socket, so a filament bulb is the more apt device; "
           "both are generated and the user picks from the picture."),
)


# --- the three already shipped, recorded so the table is the whole record ----------
# These are INSTALLED AND VERIFIED and are not re-run. They are here because a table that
# describes only the next building is not a record, and because the next person needs to see
# that `source` and `surround` genuinely differ between them.
# --- Factory -> The Industrial Revolution (REBUILT under the sign technique) -------
# This shipped, and Zain rejected it: the sign is a mustard rectangle with flat green type
# floating in front of the building, with no thickness, no shadow and nothing holding it up.
#
# MEASURED, because the first reading of this was wrong and had to be corrected. Sierra's
# own board is 30x12 at (10,179) - 94% of the building's width and 44% of its height. What
# we installed is 53x15 at (10,176): 166% of the building's width, 56% of its height and
# 87% of the whole hotspot's width. `gen_factory.py` says so outright - "the chosen sign is
# 55x21 at game (9,171)" - so the board really was inflated by this pipeline, 1.8x wider
# and 1.3x taller than hers. The new board is 32x12, which is HER proportion back again.
#
# THE REGION NOW HOLDS BOTH HALVES. The building is background art at (26,155) 32x27 and
# the sign is cel 11 at (9,176) 32x13, and they were two independent edits. Generating
# their union, (9,155) 49x34, means the board is constructed INTO the same image as the
# yard it stands in, so its shadow falls on generated ground and the downscale resamples
# board, posts and building together, once.
#
# HER BOARD HANGS, AND THE FIXINGS ARE HERS. Cel 0 carries the blank gold panel with THREE
# dark hooks along its top edge, under cel 11. So posts and a hanging board are reading her
# art, not inventing a sign type.
BUILDINGS["factory"] = Building(
    key="factory", sierra="Factory", new_name="The Industrial Revolution",
    # THE REGION IS THE WHOLE CORNER, 59x38 at (9,151), and it has to be: the pipeline only
    # repaints `region`, so anything the PREVIOUS install wrote outside it stays on the
    # board. The old hanging board ran to x 50 and y 187, so a region drawn tightly round
    # the new building would have left a strip of the old sign behind it.
    # 38 ROWS WAS NOT ENOUGH AND THE EVIDENCE WAS A GOLD BAR LEFT ON THE BOARD. The
    # ORIGINAL install (before any of this) put its board at 53x15 reaching y 190, two rows
    # below where a 38-row region ends, so a strip of the old sign survived at y 189..190,
    # x 10..62 - measured on the installed set, not guessed. 40 rows covers it.
    source="background", region=(9, 149, 60, 44), hotspot=(7, 155, 67, 192),
    # 66x44 AT (9,149), AGAINST 42x40 - 73% more area, and the sign grows with it.
    # Zain: "the factory sign is too small, won't be visible on a small screen, I asked
    # for the building size to be increased to make it look more like a factory." The
    # no-collision constraint was satisfied at 42x40 and that is not the same thing as
    # doing what was asked.
    #
    # TWO DIFFERENT ENVELOPES, and conflating them is what kept it small:
    #   THE BUILDING may overhang the marble - the marble walks IN FRONT of a building -
    #   so its bound is the NEIGHBOURS: Hire Ground's region starts at x 75, and the
    #   town centre's matte reaches y 154 at x 69..75.
    #   THE SIGN may not. Its bound is the swept footprint.
    #
    # Re-derived from `MarblePath` rather than quoted: the left run sweeps x 7..27 from
    # y 148 down to 187, the bottom run y 178..187 from x 9 to 94, the top run y 140..150
    # across x 19..66. The LARGEST swept-free rectangle left of Hire Ground is
    # x 28..74, y 151..177 = 47x27, found by search rather than by eye - and the sign
    # sits inside it.
    # 960x704 = 675,840 px, inside the card's measured 810k ceiling.
    #
    # 60 WIDE, NOT 66: THE RIGHT EDGE CAME BACK IN TO x 68 TO RESTORE THE GAP. At 66 the
    # building ran to x 74 and Hire Ground starts at x 75, so the two abutted where Sierra
    # had scenery between them and the corner read as one terrace. Zain: "the factory is
    # too close".
    #
    # SIX COLUMNS IS THE MOST THE GAP CAN BE, and that is a measured trade rather than a
    # choice. The sign needs 39 cel px to hold THE / INDUSTRIAL / REVOLUTION at Sierra's
    # own cap height at 1x, and the marble fixes its left bound at x 28, so the sign ends
    # at x 66 at the earliest and the building cannot stop left of that. Taking more gap
    # means a sign that cannot be read on a phone.
    palette={
        "BRICK":   (0x58, 0x40, 0x30), "BRICK_LO": (0x58, 0x40, 0x20),
        "STACK":   (0x20, 0x28, 0x30), "FURNACE":  (0x78, 0x58, 0x38),
        "WIN":     (0x38, 0x28, 0x18),
        "SKY":     (0xC0, 0xD0, 0xE0),    # gen_factory.py's own, for the sawtooth glazing
        # Invented, like YARD below and for the same reason: the silhouette is what
        # survives a downscale to 42x33, and a sawtooth roof has to separate in VALUE from
        # both the brick under it and the sky behind it or the teeth vanish.
        "ROOF":    (0x3A, 0x3E, 0x42),
        # The yard the MODEL is shown, and not Sierra's lavender - a yard at #4A4642 under
        # a brown building left the init with no light in it anywhere and returned a
        # beautifully textured dark alley. Mid concrete fixed it, and it stays.
        "YARD":    (0x8A, 0x85, 0x80), "YARD_HI": (0x9E, 0x99, 0x93),
        "YARD_LO": (0x6E, 0x69, 0x64),
        # The doors IN THE FACADE. Same values as the door cel's timber in
        # tools/door_cels.py, so the painted shut door and the animated one agree.
        "DOOR":    (0x35, 0x26, 0x1C), "DOOR_LO": (0x1A, 0x12, 0x0C),
        "STONE":   (0x8A, 0x86, 0x82),
        # Sampled just outside the region, so its edge does not read as a cut.
        "PARK":    (0x71, 0x95, 0x7C), "GROUND_OUT": (0x65, 0x5D, 0x63),
    },
    # THE BUILDING IS NOW 42x33 AT GAME (26,151), against 32x27 - a third wider and a fifth
    # taller, which is Zain's own suggestion and the room the sign needs. The bound is
    # computed from `MarblePath`, not from the picture: inside this hotspot the largest
    # rectangle the marble never touches is x 28..67, y 151..176, and the building may
    # overhang that downwards because the marble walks IN FRONT of a building - it is the
    # SIGN that may not be walked through.
    #
    # IT READS AS A FACTORY BY ITS SILHOUETTE, which is the only thing that survives at
    # 42x33: a tall chimney breaking the roofline, a SAWTOOTH north-light roof drawn as a
    # stepped top edge rather than as texture, a long band of tall windows, and a loading
    # bay. The previous version was a plain box with three courses of holes in it and read
    # as a brick house, which is what Zain said.
    # THE MASSING IS THE POINT. A mill, not a shed: a chimney running the full height at
    # the left, a long three-storey block with a raised glazed MONITOR lantern on its ridge,
    # a lower wing stepping down to the right, and the loading bay in the middle. The
    # silhouette is what survives the downscale, so every one of those is a step in the
    # outline rather than a texture.
    #
    # NO SAWTOOTH. Three rounds asked for sawtooth teeth and all three came back as a
    # dormered slate roof; a fourth was not spent. A monitor lantern is a different thing -
    # a raised band along the ridge with glazing in its sides - and it is authored here as
    # a raised band so the model only has to light it.
    #
    # THE STEP AT THE TOP RIGHT IS NOT STYLE. The town centre's matte reaches y 154 at
    # x 69..75, so nothing of this building may be painted above game y 155 beyond x 68.
    # The main block therefore stops at local x 59 and the wing starts at local y 6.
    init=[
        # her own surrounding colours, so the region's edge is low contrast
        (0, 0, 59, 43, "PARK"),
        (0, 39, 59, 43, "GROUND_OUT"),
        # THE ROOFLINE IS THREE STEPS, and the first attempt had none. That init filled the
        # region with one flat brown slab from the top edge down: the chimney ran to the
        # same height as the mill, so it was a dark stripe INSIDE the block rather than a
        # break in its outline, and the conditioning came back almost black. The model had
        # no silhouette to render and returned a brick wall. What survives a downscale is
        # the OUTLINE, so the outline is what the init has to state.
        #
        # main block, roof at local y 6 = game 155
        (12, 6, 59, 41, "BRICK"),
        (12, 6, 59, 8, "ROOF"),
        # a taller bay between the chimney and the sign, with a glazed lantern on its ridge.
        # It sits LEFT of the sign because the sign covers the main ridge, and left of x 60
        # because the town centre's matte owns x 69..75 down to y 154.
        (12, 3, 18, 41, "BRICK"),
        (12, 3, 18, 4, "ROOF"),
        (13, 5, 17, 5, "SKY"),
        # THE LEFT MASS REACHES THE REGION'S OWN EDGE, and that is the fix for what Zain
        # saw as "remnants on the left of the factory". MEASURED RATHER THAN GUESSED, and
        # the measurement split it in two: game x 0..8 is BYTE-IDENTICAL to png12x-new, so
        # it is HER river bank and always was - not a remnant and not ours. x 9..12 WAS
        # ours, and it was a four-column sliver: the stack started at x 13 and the
        # boundary wall only ran from y 176, so between the region's edge and the stack
        # there were twenty-four rows of her bank showing through INSIDE our own region,
        # with two isolated green pixels at y 156. A sliver of her scenery framed by our
        # building reads as a leftover, which is exactly what it looked like.
        #
        # So the stack is a TOWER standing on the region's edge - a stair tower with the
        # flue rising from it, which is a real mill form - owning local x 0..11 for the
        # full height. It also carries her blank gold board's left end, which is why the
        # separate boundary wall is gone: one mass instead of a wall beside a stack.
        # AND IT TAPERS. Taking the mass out to the edge at FULL WIDTH cost the chimney:
        # twelve columns of stack with a six-column dark core read as a glazed stair
        # tower, and the one thing that says 'factory' at this size went away. So the
        # tower is the BASE, from local y 4 down, and a narrow flue rises out of it for
        # the full height. Above y 4 only four columns of her sky are left at the corner,
        # which is a corner and not a framed sliver - the top edge is already mostly hers
        # by design, because that is where her park belongs.
        (0, 4, 11, 41, "STACK"),
        (4, 0, 9, 41, "STACK"),
        (3, 0, 10, 1, "BRICK_LO"),
        # a few openings, NOT a curtain wall - the previous core was six columns wide and
        # the model glazed the whole face
        (5, 14, 8, 20, "WIN"),
        (5, 26, 8, 32, "WIN"),
        # THE SIGN BOARD STANDS IN FRONT OF THE FACADE, as Sierra's own hanging board does,
        # and it has to be BUILDING in the init or the matte cuts its top four rows away
        # where they rise above the roofline. local y 2..21 at x 19..59 - the model sees a
        # plain band and never sees text.
        (19, 2, 59, 21, "BRICK"),
        # THE ENTRANCE, around the DOOR CEL's own footprint. view 751 loop 5 at (50,181) is
        # 10x11 drawn bottom-centred, so it covers game x 45..54, y 171..181 = local
        # x 36..45, y 22..32. The facade paints the doors SHUT.
        (34, 22, 47, 23, "STONE"),
        (35, 24, 35, 36, "STONE"), (46, 24, 46, 36, "STONE"),
        (36, 24, 45, 36, "DOOR"),
        (40, 24, 41, 36, "DOOR_LO"),      # the meeting stile - ONE line; three read as bars
        (35, 35, 46, 36, "STONE"),
        # tall multi-pane windows either side of the bay
        (16, 24, 32, 33, "WIN"),
        (49, 24, 58, 33, "WIN"),
    ],
    prompt=("a photograph of a large old brick mill, a tall chimney stack, a raised "
            "glazed monitor lantern along the ridge, rows of tall multi pane iron "
            "framed windows, a loading bay with timber doors, weathered brickwork, "
            "bright overcast daylight, even exposure, clear air, sharp focus, fine "
            "detail, architectural photography"),
    # Every earlier install's footprint put together: the original board reached y 190
    # and x 62, and the 59x40 and 66x44 regions reached x 74. Erasing costs no GPU, so
    # this is generous on purpose.
    scenery=("PARK", "GROUND_OUT"),
    # `edge_feather` REMOVED. Section 17: nothing in this pipeline may be blurred to
    # hide a seam, and this was the last building still carrying it.
    # x 68, NOT 74: the town centre's matte occupies x 69..75 down to y 154, and
    # `clear` RESTORES Sierra, so an eraser reaching those columns would punch a
    # hole in the centre. The old install's footprint ends at x 67, so 68 covers it.
    # THE ERASER STILL HAS TO REACH x 74, because the PREVIOUS install's region did. That
    # one ran to x 74 and its wing was pasted at x 69..74 from y 155 down; the new region
    # stops at x 68 and would leave that strip on the board forever.
    #
    # AND IT MUST START AT y 155, not 151: `clear` RESTORES Sierra, and the town centre's
    # matte owns x 69..75 down to y 154. An eraser reaching those rows would punch a hole
    # in the centre. The old install wrote nothing above y 155 out there - its init had
    # PARK in those cells and the matte cut them - so y 155 is both sufficient and safe.
    # y 151, NOT 155, AND x 68, NOT 74. Both bounds moved when his sign replaced ours and
    # a cream block appeared at the top right: OUR OLD BOARD, stranded. It ran y 151..170,
    # the eraser started at y 155, and his roofline is lower there so the matte no longer
    # covers those rows - four rows of our own previous install survived on the board.
    #
    # The right bound comes IN to 68 at the same time, because `clear` RESTORES Sierra and
    # the town centre's matte owns x 69..75 down to y 154: an eraser reaching those columns
    # at y 151 would punch a hole in the centre. x 69..74 was cleared by an earlier install
    # and measures byte-identical to png12x-new, and the region no longer reaches it.
    #
    # THE ERASER IS SIZED TO EVERY PREVIOUS INSTALL, NOT TO THIS ONE. That is the third
    # time this has bitten.
    # THE ERASER COVERS THE WHOLE REGION. It used to start at y 151 while the region
    # starts at y 149, so rows 149-150 kept the previous install's pixels through every
    # reinstall - a slab of his screenshot background that no check could see.
    #
    # AND IT NOW COVERS `board_fill` TOO, for the same reason a fourth time: the landscape
    # patch writes ABOVE the region, at y 145..148, so without this those rows would keep
    # whatever the last install's patch left there and a changed patch could never be
    # retried. The eraser must cover everything an install WRITES, not just the region.
    clear=(9, 144, 68, 194),
    # `clear_copy` REMOVED. It covered her blank gold board with her own ground copied
    # from an offset, and its right edge measured 14.27 per channel against a 1.1
    # neighbourhood - visible. The building now covers the board outright, so the patch
    # has nothing to do. Prefer making a defect impossible to measuring whether it
    # happened.
    #
    # SIERRA'S FACTORY WAS STILL STANDING BEHIND HIS, AND `clear` IS WHAT PUT IT THERE.
    #
    # Zain saw it. The board REGENERATION redrew her mill faithfully - a render-and-slate
    # gabled block in the same place - and `clear` restores `png12x-new`, so every install
    # of this building restored a copy of her building behind it. Measured on the installed
    # board at native 12x:
    #
    #   her regenerated roof apex           game y 145-146
    #   the block's visible extent          x 20..57, y 145..158
    #   the region's top edge               y 149        - four rows of it are ABOVE the
    #                                                      region, where no install reaches
    #   his own matte's top edge            y 153 at x 11..18, y 156..159 everywhere else
    #
    # So most of it was not outside the region at all: it was INSIDE the region and ABOVE
    # HIS ROOFLINE, which is the margin - the one place all three audit clauses agree is
    # supposed to be HER art. The rule "the margin must be hers" was written for her sky
    # above a roofline; it cannot tell that from a whole building of hers, and that is the
    # same shape as every other guard failure here.
    #
    # THE REGION IS NOT GROWN TO COVER IT. Its width and the six-column gap to Hire Ground
    # are a measured trade against the marble path and against "the factory is too close",
    # and reopening that to hide a different defect would be paying twice.
    #
    # THREE RECTANGLES, EACH AS WIDE AS THE BLOCK IS AT THOSE ROWS, and the first attempt
    # was ONE rectangle of x 19..60, y 144..160 which was a measurable mistake. The block is
    # a gable: 15 cel px wide at its apex and 32 at its base. A single box round it painted
    # out the bright green hill either side of the roof - 8 cel px of it at the top left and
    # 14 at the top right - and the diffusion then averaged that green against the grey yard
    # on its ring, so the patch came back as a pale wash where the hill used to be. Painting
    # out more landscape than the defect occupies IS a defect.
    #
    # Measured on the board at 1x, row by row (the block, in game x):
    #     y 146   x 28..41        y 149   x 26..44        y 153..155   x 25..53
    #     y 147   x 27..40        y 150   x 26..46        y 156..158   x 26..54
    #     y 148   x 27..40        y 151   x 25..46
    # and y 143..145 is clean green across the whole width, which is where the top ring sits.
    #
    # Applied in order, so each one's top ring samples the one above it and the three agree
    # where they meet.
    # AND THE SECOND ATTEMPT FAILED THE OTHER WAY, which is the finding worth keeping:
    # rectangles fitted TIGHTLY to those measurements put the diffusion's own ring ON the
    # block - the roof's right slope runs out to x 52 at y 152 while the measurement said
    # x 46 - so the fill interpolated between two dark boundaries and painted a dark smear
    # where the building had been. **A reconstruction is only as good as its ring: the ring
    # must be outside the defect on every side, and that is worth checking column by column
    # before the run rather than looking at the result afterwards.**
    #
    # These have a 2-6 px margin on each side and every ring was checked against the board
    # first: R1's at y 144..145 and x 22..23 / 46..47 is clean green, R2's sides are green,
    # R3's are her dark hillside and her grey yard. R1 starts at y 146 and not y 145
    # because the two rows above carry the BANK's warm brown, and a ring that samples a
    # neighbour's building paints that neighbour's colour into the hill.
    # AND THE BANDS OVERLAP, which is the third thing this patch had to be taught. Butted
    # edge to edge with a feathered mask, every junction keeps a one-pixel line of the
    # original - and a row of her roof left between two patches reads as a drawn line, which
    # is worse than the block itself. Each band now runs two rows into the next, so every
    # row of the defect lands inside some band's OPAQUE core rather than in a feather.
    #
    # The fifth number is the ring's width. R1 takes 1 rather than 2 because two rows above
    # it is the BANK's warm brown, and a ring that reaches a neighbour's building diffuses
    # that neighbour's colour into this hillside.
    board_fill=[
        (24, 145, 45, 151, 1),   # the roof's apex, between green and green
        (22, 149, 52, 155),      # the gable, between green and green
        (20, 153, 60, 159),      # the base, green on the left to her grey yard on the right
    ],
    # ZAIN'S OWN IMAGE, AND THE PIPELINE SKIPS GENERATION FOR THIS BUILDING.
    # "you keep messing up with the graphics, use this, look how much nicer it looks!!!",
    # and then "I MADE IT because you just never did a good job!". He is right: three
    # passes asked for a sawtooth and got a brick wall, the chimney shipped in the same
    # value as the wall behind it, and the building sat small in its corner for days.
    #
    # THE FIT, MEASURED. His picture is a composite of the board with the mill in place, so
    # the mill occupies image x 95..1075, y 190..870 with its pavement and road below that.
    # Cropping to the building and scaling to the region's width gives 720x500 px at 12x =
    # 60 x 41.6 cel px, against a 60x44 region - so it fills the width exactly and leaves
    # two rows of her ground at the foot. Bottom-aligned with its base at game y 190.
    #
    # HIS SIGN IS COVERED AND OURS IS STAMPED, and that is a measurement rather than a
    # preference. Fitted, his lettering lands at game x 19.6..56.8, y 167.3..170.5:
    #
    #     cap height            4.24 cel px   against Sierra's own 3x5 set at 5
    #     per character         1.49 cel px   against the 3.56 that set needs
    #     the name on one line  37.2 cel px   against the 89 our own metric requires
    #
    # It clears the marble - 0 swept pixels - and it is handsome at 12x, where its cap is 51
    # device px. But it is 2.4x too small by our own measure, and the three-line board exists
    # because 25 characters will not fit on one line at a readable size in this corner. So
    # `cover` replaces his sign band with his own brick from six rows above, and `sign_board`
    # stamps ours over it. If the 1x rung is ever generated this is the difference between a
    # readable name and a smudge; at 12x alone his would have been the better picture.
    # HIS OWN OUTLINE, written by fit_source --who factory from the `key` flood fill.
    matte_file="matte_960x704.png",
    source_image={
        "path": (r"C:\Users\ZAIN~1.CAS\AppData\Local\Temp\claude"
                 r"\C--AI-Accelerator\1709fd27-425c-4815-a187-5d85a4ff583f\images\35.webp"),
        "crop": (95, 190, 1075, 870),
        "baseline": 190,
        # HIS PICTURE IS A COMPOSITE OF THE BOARD, so above the roofline it carries his
        # own screenshot of it - and our init's matte kept that, leaving a seven-row strip
        # of smeared board above the building. `key` flood-fills his background from the
        # TOP CORNERS, which a colour test could not do: a colour test cannot tell his sky
        # from a grey roof (CAST.md 4), but a flood cannot reach what the building encloses.
        # HIS OUTLINE, AUTHORED - read off his image on a cel grid and written down, because
        # nothing derived from his pixels can find it. See fit_source.clip_matte for the four
        # methods that failed and why. Region-local cel coordinates; local y 0 is game y 149.
        # Read off his image on a cel grid, column band by column band, at 24 screen pixels
        # per cel pixel. The FIRST reading was too coarse and it showed: the chimney band
        # claimed columns 47-52 from row 0, but his chimney is columns 49-52 from row 1, so
        # two columns of his background stood ten rows tall beside it as a pale box. Neither
        # audit clause could see it - it is INSIDE the clip, so it is not her art inside the
        # silhouette and not our art in the margin. Only the eye at native 12x finds this.
        "clip": [
        (0, 12.2, 0, 55.0), (1, 11.4, 1, 55.0), (2, 10.3, 2, 55.0), (3, 9.3, 3, 55.0),
        (4, 7.6, 4, 55.0), (5, 7, 5, 55.0), (6, 6.35, 6, 55.0), (7, 5.85, 7, 55.0),
        (8, 5.45, 8, 55.0), (9, 4.75, 9, 55.0), (10, 4.45, 10, 55.0), (11, 3.95, 11, 55.0),
        (12, 3.45, 12, 55.0), (13, 3.05, 13, 55.0), (14, 2.35, 14, 55.0), (15, 1.85, 15, 55.0),
        (16, 1.4, 16, 55.0), (17, 0.8, 17, 55.0), (18, 0.25, 18, 55.0), (19, 0.15, 19, 55.0),
        (20, 0, 20, 55.0), (21, 0, 21, 55.0), (22, 0.1, 22, 55.0), (23, 0.75, 23, 55.0),
        (24, 1.3, 24, 55.0), (25, 1.5, 25, 55.0), (26, 1.9, 26, 55.0), (27, 2.45, 27, 55.0),
        (28, 3.1, 28, 55.0), (29, 3.6, 29, 55.0), (30, 4.05, 30, 55.0), (31, 4.55, 31, 55.0),
        (32, 5.1, 32, 55.0), (33, 5.7, 33, 55.0), (34, 6.35, 34, 55.0), (35, 6.75, 35, 55.0),
        (36, 7.05, 36, 55.0), (37, 7.6, 37, 55.0), (38, 7.55, 38, 55.0), (39, 7.55, 39, 55.0),
        (40, 7.5, 40, 55.0),
        ],
    },
    cond_blur=0.25,
    # Judged AT 12x, side by side, which is the rung the game renders. 0.92/0.30 gives
    # visibly the best material - individual brick courses, a crisp slate roof, a stone
    # lintel - and loses the composition: one window, no roof structure, no loading bay.
    # 0.84/0.55 holds the composition and is the softest. 0.88/0.45 holds the composition
    # with the material close behind 0.92, and is what ships.
    strength=0.88,
    control=0.45,
    # KEEP HER SCENERY, GENERATE ONLY THE BUILDING - and this is the fix for the thing Zain
    # called "a weird rectangle". It was not the river: IT WAS THE REGION BOUNDARY.
    #
    # `blue_light` keeps what is blue and light. Her grass above the roofline and her ground
    # to the sides are not blue, so they counted as building, got generated, and painted our
    # sky and our yard over her scene - leaving four hard edges in open ground exactly where
    # the region happens to stop. The pale slabs inside it are the second thing you see, not
    # the first.
    #
    # `rects` keeps everything the building does not occupy, so her scene runs continuously
    # into the rest of the board and there is no inset at all. The rectangles are the
    # building's own silhouette, stepped at the roof so the sawtooth notches show HER sky
    # rather than ours. The old gold board's footprint is covered because the blend writes
    # her ORIGINAL pixels back wherever the building is not - which erases any earlier
    # install inside the region without needing a `force` list at all.
    # THE WHOLE REGION IS GENERATED, and the region is sized so HER FACTORY IS ENTIRELY
    # INSIDE IT. Keeping her scenery around a stepped silhouette sounded right and was
    # worse: her own brickwork at y 151..160 and y 184..187 sits OUTSIDE our building
    # and showed above and below it as a brown chequered patch, with her park blocks
    # beside it - at 12x her 1x art is blocky, so next to a photograph it reads as
    # corruption rather than as scenery. Nothing of hers may remain inside the box.
    #
    # The region edge is handled by COLOUR instead: the init paints her own
    # surrounding values, so the join is low contrast rather than a cut.
    surround={"mode": "rects", "keep": [(0, 0, 59, 43)]},                    # the shed
    name_lines=[],
    name_rect=(19, 2, 59, 21),
    name_ink=(0x30, 0x58, 0x48),
    # THE SIGN IS NOW ON THE BUILDING, AND THAT IS THE DEFECT FIX.
    #
    # It was a board on posts standing in the yard - Sierra's own sign type, and her own
    # position - and the player walked straight through it. Measured against `MarblePath`
    # rather than judged from the picture: the marble is view 0 loop 2, 9x8, drawn at
    # (mx - w/2, my - h), so its swept footprint over 170 path points covers 14.7% of the
    # board. The old board at game x 11..50, y 175..187 took 379 of those pixels and path
    # steps 107 to 114 ran through it.
    #
    # THE RULE, FOR EVERY BUILDING FROM NOW ON: no sign may overlap the marble's swept
    # footprint. It is computed from the path, never eyeballed.
    #
    # This band is game x 29..64, y 166..176, which contains exactly ZERO swept pixels -
    # it sits above the bottom run of the path and right of the left run. As a proportion
    # of the 42x33 facade it is 36 x 11: 86% of its width and 33% of its height, against
    # the 166% of the building's width that originally shipped.
    #
    # It is a PAINTED WALL SIGN now rather than a hanging board, because that is what fits
    # on a factory wall and it is also what removes the posts that had nothing to stand on
    # once the sign left the ground. Her gold panel and dark green lettering are kept.
    # HIS SIGN IS KEPT AND OURS IS NOT STAMPED - AND THIS IS THE FIRST TIME THIS PROGRAMME
    # HAS KNOWINGLY SHIPPED A SIGN THAT FAILS THE 1x TEST. Do not "fix" it without reading
    # this.
    #
    # Measured on the fitted image, his lettering gives a CAP HEIGHT OF 4.24 CEL PX against
    # the 5 that Sierra's own 3x5 set needs, 1.49 cel px per character against 3.56, and
    # 37.2 cel px for a name our own metric puts at 89 on one line. By that arithmetic ours
    # should be stamped over his, and for one install it was.
    #
    # THE COST OF STAMPING IT WAS THE REASON THE IMAGE WAS WORTH USING. Our board is 41x20
    # and has to sit inside the collision-free pocket (y 151..177) and above the door cel
    # (y 171), which leaves exactly the band his MONITOR ROOF, WATER TOWER and ROOFTOP PLANT
    # occupy - the things three of our own generations never produced. There is no placement
    # that avoids them. Zain chose the picture, which is his call.
    #
    # TWO THINGS SUPPORT IT. At 12x his cap is 51 device px and reads perfectly, and the 1x
    # rung currently carries HER art anyway. And the board is likely to become 640x288, which
    # changes the arithmetic entirely: a sign measured against a 320-wide world may be fine
    # against a 640-wide one. RE-MEASURE IT IF THAT LANDS rather than carrying today's
    # verdict forward.
    #
    # The sign spec that was removed is in the history of this file; `sign_building --who
    # factory` and a `cover` op put it back.
    sign=None,
    notes=("The building is BACKGROUND art and the sign is cel 11; the region is now their "
           "union so they are generated and downscaled as one image. Eight greeting clips "
           "(50-57) all say `Welcome to the Factory`; left alone."),
)
BUILDINGS["employment"] = Building(
    key="employment", sierra="Employment Office", new_name="Hire Ground",
    # REBUILT FROM NOTHING. Zain: "what is going on with this building!? it looks ridiculous,
    # almost like it's not a new building, but the old one with text pasted on top. It needs
    # to be a new building, original realistic art".
    #
    # He was right about the substance. There was no ghost of her EMPLOYMENT OFFICE
    # lettering - what reads as one at board scale is the fascia's lower edge - but the sign
    # WAS flat type on a red rectangle with no board, no thickness, no fixings and no cast
    # shadow, and the building was a beige box with two dark windows. This was the second
    # building ever done; it predates `sign_board.py`, it predates the extraction, and it
    # had never been revisited. It had NO init, NO palette, NO sign and NO surfaces in this
    # table at all - `tools/gen_employment.py` held all of that.
    #
    # THE REGION GROWS DOWN TO y 190, from y 184, so the building has a base below the door
    # and covers her plinth. 47x40 at scale 20 = 940x800 = 752,000 px, inside the measured
    # 810k ceiling.
    # SOURCE IS THE BACKGROUND, NOT `cel:10`, AND ONLY SO THE REGION CAN GROW.
    # `sierra_region` asserts that a `cel:NN` region EQUALS the cel - correctly: a facade
    # may not grow to meet a door. Her cel is 47x34 and the new region is 47x40, so the
    # assertion fires. It costs nothing to take cel 0 instead: `surround` keeps the whole
    # region, so the silhouette is all-generated and `sierra_region`'s pixels are never
    # blended in - the MATTE decides the outline against the board. The Factory already
    # works this way. HER CEL IS STILL THE SUBJECT; only the fallback source changes.
    source="background", region=(75, 151, 47, 40), hotspot=(68, 158, 128, 192),
    palette={
        # Her own colours, which are the building's identity: apricot render, pale blue-white
        # lettering, green planters, and the red sign ground.
        "WALL":     (0xD8, 0xA8, 0x78), "WALL_LO": (0xA6, 0x7E, 0x56),
        "STONE":    (0x9A, 0x94, 0x8C), "STONE_HI": (0xC2, 0xBC, 0xB2),
        "GLASS":    (0x3A, 0x44, 0x50), "GLASS_HI": (0x7C, 0x94, 0xAA),
        "CARD":     (0xE8, 0xE4, 0xD4),
        "DOOR":     (0x5A, 0x3A, 0x24), "DOOR_LO": (0x2E, 0x1C, 0x10),
        "PLANT":    (0x3E, 0x6A, 0x38),
        # sampled just outside the region so its edge is low contrast
        "PARK":     (0x74, 0x93, 0x7E), "GROUND_OUT": (0x8C, 0x96, 0xA8),
    },
    # A HIGH-STREET AGENCY, AND DELIBERATELY NOT RENTFLIX. Rentflix is the nearest
    # neighbour in type and the two must not read alike: that one is a flush dark metal
    # fascia over plate glass with framed lightbox cards. This is warm render, STONE
    # PILASTERS and a moulded cornice, a PAINTED TIMBER fascia, and small-pane windows with
    # rows of PINNED JOB CARDS behind them - paper on a board, not cards in a lightbox.
    init=[
        (0, 0, 46, 39, "PARK"),
        (0, 36, 46, 39, "GROUND_OUT"),
        # the terrace front. Local y 0 stays scenery: game y 151 is the town centre's own
        # bottom row at x 76..121 and must not be painted over.
        (0, 1, 46, 35, "WALL"),
        # the fascia the sign sits on, local y 1..16, kept plain - the model never sees text
        (0, 1, 46, 17, "WALL_LO"),
        (0, 17, 46, 18, "STONE"),          # the moulded cornice under it
        (0, 19, 3, 35, "STONE"), (43, 19, 46, 35, "STONE"),      # pilasters
        (4, 31, 42, 34, "STONE"),          # the stall riser, before the openings
        # the windows, with rows of pinned cards behind the glass
        (5, 20, 15, 30, "GLASS"), (6, 21, 14, 23, "CARD"), (6, 25, 14, 27, "CARD"),
        (28, 20, 41, 30, "GLASS"), (29, 21, 40, 23, "CARD"), (29, 25, 40, 27, "CARD"),
        (5, 20, 15, 20, "GLASS_HI"), (28, 20, 41, 20, "GLASS_HI"),
        (4, 33, 15, 35, "PLANT"), (28, 33, 42, 35, "PLANT"),     # her planters
        # THE ENTRANCE, around the DOOR CEL's own footprint. view 751 loop 6 at (97,183) is
        # 10x13 drawn bottom-centred, so it covers game x 92..101, y 171..183 = local
        # x 17..26, y 20..32. Painted SHUT, because the sprite only draws while it animates.
        (16, 19, 27, 20, "STONE"),
        (16, 21, 16, 33, "STONE"), (27, 21, 27, 33, "STONE"),
        (17, 21, 26, 33, "DOOR"),
        (21, 21, 22, 33, "DOOR_LO"),
        (16, 32, 27, 33, "STONE"),
    ],
    prompt=("a photograph of a high street employment agency shopfront, a painted timber "
            "fascia board, stone pilasters and a moulded cornice, windows with rows of job "
            "cards pinned behind the glass, a timber part glazed door, a stone stall riser, "
            "planters, overcast daylight, sharp focus, fine detail, architectural "
            "photography"),
    surround={"mode": "rects", "keep": [(0, 0, 46, 39)]},
    scenery=("PARK", "GROUND_OUT"),
    # The old install's footprint was x 75..121, y 151..184. It starts at y 152, not 151,
    # because `clear` RESTORES Sierra and the town centre's matte owns game y 151 from x 76.
    # The eraser covers the whole region: it began at y 152 while the region begins at
    # y 151, leaving one row that no reinstall could reach. See install_building's
    # check_clear_covers_region.
    clear=(75, 151, 121, 192),
    name_lines=["HIRE", "GROUND"],
    # Her own band is x 76..120, y 152..164. The board is a little taller so the two lines
    # have room: 43x16 at game (77,152), interior 39x12 at a 2-cel bevel against the 23x11
    # the port's 3x5 metric needs for HIRE over GROUND. 0 swept pixels - the marble only
    # reaches y 176 here.
    name_rect=(2, 1, 44, 16),
    name_ink=(0xD0, 0xD8, 0xE0),
    sign={
        "rect": (2, 1, 44, 16),
        "lines": ["HIRE", "GROUND"],
        # HER RED, and a PAINTED TIMBER board with a gilt moulding - the opposite end of the
        # material range from Rentflix's flush dark metal panel.
        "face": (0x9E, 0x2A, 0x24), "face_hi": (0xC4, 0x3C, 0x32),
        "edge_hi": (0xE2, 0xBA, 0x6C), "edge_lo": (0x46, 0x10, 0x0E),
        "ink": (0xD0, 0xD8, 0xE0), "ink_hi": (0xF6, 0xFA, 0xFE),
        "ink_lo": (0x4A, 0x14, 0x12),
        "bolt": (0xB2, 0x8C, 0x42), "bolt_hi": (0xEA, 0xD0, 0x8C),
        "depth": 0.55, "shadow": 0.9, "shadow_strength": 0.60,
        "grain": 0.09, "relief": 0.10, "fill": 0.86,
        "prompt": ("a close photograph of a painted timber shop fascia board with a moulded "
                   "gilt frame, signwritten lettering, brass fixings, aged enamel paint, "
                   "raking daylight, sharp focus, fine detail"),
        "negative": ("blurry, out of focus, bokeh, 3d render, cgi, vector, illustration, "
                     "flat, cartoon, pixelated, jpeg artifacts, people, distorted, frame, "
                     "vignette, smoke, steam, fog"),
        "strength": 0.22, "cfg": 7.0, "steps": 24, "control": 0.8, "feather": 7.0,
        "marry": False,
    },
    cond_blur=0.25,
    init_blur=0.5,
    scale=20,
    strength=0.88,
    control=0.45,
    notes=("Four surfaces carry the name - the board cel, the picPatch, and BOTH cels of "
           "view 706 loop 0. The board is built here; `tools/install_employment.py "
           "--skip-board` refreshes the other three, and it rebuilds the picPatch by "
           "copying this set's own board into the strip and leaving HER alpha alone, so "
           "the patch agrees with the board by construction. Seven greeting clips 426-432 "
           "say `Welcome to ACNE Employment` and are left alone."),
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


# --- Monolith Burgers -> Bun Believable --------------------------------------------
# Zain: "replace the Burger joint with with the rename to Bun Believable, graphic is here".
# His picture is 1213x1297 and it is PIXEL ART rather than a photograph - the third kind of
# source this pipeline has taken, after a generation and a photographic composite.
#
# IT IS CEL 6, 42x43 at (264,32), established from `DoorFor` as every building now is:
# MonolithBurgers is (10, 288, 75) and cel 6 is the only cel whose box holds that anchor
# with its base one row above it, which is her standing arrangement.
#
# THE ASPECT PROBLEM DISSOLVES ONCE THE SLOT IS READ CORRECTLY. The hotspot is 61x38
# (1.605, wide) and his picture is 0.935 (tall), which looks irreconcilable - but the
# hotspot is a CLICK TARGET and the thing he has to fill is the CEL, which is **42x43 =
# 0.977**, very nearly square. His picture is within 4% of it.
#
# THE POLE SIGN COMES, AND HER OWN CEL IS WHY. She drew this building with a red pole sign
# carrying a blue M at its left, a giant M monument on the roof, a red fascia with
# `Monolith` lettered across it and a glazed shopfront with a central door. His picture has
# a red pole sign with a yellow B at its left, a giant B on the bun, a red panel with
# `Bun Believable` and a glazed shopfront with a central double door. It is her composition,
# element for element, and leaving his pole out would be the deviation.
BUILDINGS["monolith"] = Building(
    key="monolith", sierra="Monolith", new_name="Bun Believable",
    source="cel:06", region=(264, 32, 42, 43), hotspot=(251, 44, 311, 81),
    palette={"GROUND": (0xC8, 0x90, 0x70)},
    init=[(0, 0, 41, 42, "GROUND")],
    prompt="(supplied art - no generation; see source_image)",
    surround={"mode": "rects", "keep": [(0, 0, 41, 42)]},
    clear=(264, 32, 305, 74),
    # HIS LETTERING IS KEPT AND NOTHING IS STAMPED, which is a measured decision and not a
    # style one. `Bun Believable` is drawn into his picture on two lines in its own face,
    # and the standing instruction on every sign WE set is that it matches the house face.
    # Measured at the fit below, where 1 cel px is 26.0 source px:
    #
    #     his 'Bun'              cap 4.08 cel px
    #     his 'Believable'       cap 4.04
    #     OUR type, two lines in his red panel (25 x 11 cel px)     cap 2.70
    #     OUR type, given a 35-wide box that does not fit the panel cap 3.75
    #     OUR type, one line across the whole 42-px facade          cap 3.20
    #
    # **His lettering is the most legible version of this name available at this size**, by
    # a third. Setting it in the house face would make the name HARDER to read, which is the
    # opposite of what that instruction is for. Put to the coordinator as a trade; reversing
    # it is a `cover` over the panel plus a `sign` dict, and it costs 1.3 cel px of cap.
    name_lines=[], name_rect=(0, 0, 0, 0), name_ink=(0, 0, 0),
    source_image={
        "path": r"C:\AI-Accelerator\jones\tools\samples\Bun Believable.png",
        # THE CROP IS SIZED SO THE BUILDING FILLS THE CEL'S HEIGHT AND HIS SIGN CLEARS THE
        # LEGIBILITY FLOOR, and those two pull against each other: the fit scales by WIDTH,
        # so a wider crop is a smaller building and smaller lettering. Measured:
        #
        #     crop 850 wide (the building alone)   the building is 63 cel rows - 20 too tall
        #     crop 1030 (building + pole)          47 rows - 4 too tall
        #     crop 1093 (+ his ground at the edges) 43.0 rows, cap 4.04   <- chosen
        #     crop 1155                            41 rows, cap 3.82 - under the floor
        #
        # 1050 x 1075 is 0.9767 against the cel's 0.9767, so nothing is distorted and
        # nothing is cut.
        #
        # THE BOTTOM EDGE WAS WRONG BY 34 SOURCE ROWS on the first fit and that is worth
        # recording, because the test that found it was the clip and not the fit. Taking
        # the base as "the last row more than half built across its width" gave y 1157,
        # but more than half of that row is his PLAZA, not his building: the shop's own
        # base line sits at y 1113 and the 44 rows below it are pavement. The crop now
        # ends on the building, so the clip's bottom edge and the cel's bottom row are the
        # same line and `baseline` 74 puts that line on HER base row. Both axes shrank by
        # the same 4.09%, which is a pure zoom about the crop's top-left corner, so every
        # coordinate measured on the old fit carries over multiplied by 1.0409.
        "crop": (120, 38, 1170, 1113),
        "baseline": 74,
        # HIS OUTLINE, measured off the fitted image and written down.
        #
        # THE FIRST VERSION OF THIS CLIP WAS ONE RECT PER COLUMN, FIRST BUILT ROW TO LAST,
        # and that is a convex hull per column, not an outline. His picture has TWO objects
        # standing apart - a pole sign at the left and the burger at the right - so taking
        # each column's full extent filled the gap between them with the plaza that lies
        # between them, and a block of his pink pavement rode onto the board at the left
        # and the bottom-left. The lesson generalises: a per-column extent is only an
        # outline for a SINGLE convex object. Where the supplied art holds more than one
        # thing, each thing needs its own rects.
        #
        # So the pole sign is three shapes (board, mast, splayed foot) and the burger is
        # its own stepped profile, and the plaza between them is now background. Fractional
        # coordinates are cel units at the fitted resolution: a rect covers pixels
        # [x0*F, (x1+1)*F) x [y0*F, (y1+1)*F), so x1 is the LAST column, not the edge.
        "clip": [
            # --- the pole sign -----------------------------------------------------------
            (2.25, 0.90, 11.45, 12.75),     # the dark header bar and the red board
            (5.90, 13.30, 7.15, 35.85),     # the mast
            (4.45, 36.30, 8.30, 39.30),     # the splayed foot
            # --- the burger's left shoulder, which overhangs its own shopfront -----------
            (9.20, 20.75, 9.00, 32.25),
            (10.00, 19.25, 10.00, 32.25),
            (10.75, 32.25, 10.75, 42.00),   # the shopfront's left edge, below the overhang
            # --- the bun's left slope, column by column off the fitted arc ---------------
            (11, 14.90, 11, 42), (12, 11.20, 12, 42), (13, 9.85, 13, 42),
            (14, 8.65, 14, 42), (15, 7.60, 15, 42), (16, 6.85, 16, 42),
            (17, 6.25, 17, 42), (18, 6.25, 18, 42),
            # --- the big B, which reaches the top of the cel -----------------------------
            (18.90, 0.00, 30.20, 42.00),
            # --- the bun's right slope ---------------------------------------------------
            (31, 5.70, 31, 42), (32, 5.80, 32, 42), (33, 6.00, 33, 42),
            (34, 6.30, 34, 42), (35, 6.70, 35, 42), (36, 7.35, 36, 42),
            (37, 8.25, 37, 42), (38, 9.40, 38, 42),
            # the right side overhangs its shopfront too, so it stops at the patty's edge
            (39.00, 10.85, 39.00, 32.25),
            (40.00, 13.00, 40.00, 32.25),
            (41.00, 15.60, 40.60, 32.25),   # the cheese wing's last 12 px
            (39.00, 32.25, 38.30, 42.00),   # the shopfront's right edge, below it
        ],
    },
    matte_file="matte_840x860.png",
    scale=20,
    notes=("TWO user-visible strings carry the old name and NEITHER is changed here: text "
           "resource 700[81] `Monolith`, which is the Place label on the board and in the "
           "`works at` line, and script 206 @0x111C `Monolith Burgers`, which is the "
           "Employment Office's employer-menu label. Both are her resource bytes, so "
           "renaming them is a CLAUDE.md rule 1 decision and not an art one."),
)


BUILDINGS["security"] = Building(
    key="security", sierra="Security Apartments", new_name="Villa Locke & Keyes",
    # THE JOKE IS THAT A BLOCK OF FLATS IS NAMED LIKE A FIRM OF SOLICITORS. "Locke & Keyes"
    # reads as a partnership and puns on locks and keys, so the building has to be smug:
    # the security IS the selling point and the name is pretending to be a practice.
    #
    # BUILT AS THE DELIBERATE OPPOSITE OF CASA EL CHEAPO, because the two are the game's
    # housing options and will be compared side by side. Casa pretends to be a castle and
    # fails; this one pretends to be a practice and COMMITS. So: nothing decayed, and
    # `grime` stays off here - that contrast is the point, and a `grime` block on this entry
    # would quietly undo it.
    #
    # Her original is "Le Security": red neon script across three mansard peaks, a pink
    # awning over the entrance, two conifers. Nothing of that composition is preserved
    # beyond the cel's extent.
    source="cel:01", region=(7, 1, 43, 43), hotspot=(7, 10, 67, 43),
    palette={
        # Sober and expensive. Warm brick rather than render, pale stone bands, bronze.
        "SKY":      (0xB8, 0xC8, 0xD8),
        "BRICK":    (0x8A, 0x6A, 0x5C), "BRICK_HI": (0xA0, 0x80, 0x70),
        "STONE":    (0xC6, 0xC0, 0xB4), "STONE_LO": (0xA0, 0x9A, 0x8E),
        "PLATE":    (0x7A, 0x60, 0x36),          # the sign band, left plain for stage two
        "GLASS":    (0x60, 0x70, 0x80), "GLASS_HI": (0x8C, 0x9E, 0xAE),
        "BLIND":    (0xD6, 0xD6, 0xCE),          # blinds, not curtains - controlled
        "DOOR":     (0x30, 0x38, 0x40),
        "RAIL":     (0x28, 0x2C, 0x32),          # iron railings
        "HEDGE":    (0x38, 0x5C, 0x40),          # clipped box in planters
        "PAVE":     (0xA8, 0xA8, 0xA4),
    },
    # THE FACADE IS A FORMAL FRONTISPIECE, AND THAT IS A CONSEQUENCE OF THE ARITHMETIC
    # RATHER THAN A STYLE CHOICE. The first layout put the plate on the upper field of an
    # ordinary block of flats and the init showed what that really is: a bronze slab over
    # two thirds of the frontage with four squashed windows underneath. 34 x 19 cel px of
    # plate in a 43 x 43 region IS the building, so the building has to be designed around
    # it - stone pilasters either side, a cornice and a raised bay above, a proper portal
    # below, and only two narrow glazed bays to say that people live here.
    #
    # It also lands the joke better than flats did. A block pretending to be a practice
    # should LOOK like a practice: this is a formal entrance front with a dedication panel,
    # which is what the name is pretending to be.
    init=[
        (0, 0, 42, 42, "SKY"),
        (10, 0, 32, 4, "STONE"),                 # the raised bay
        (1, 3, 41, 42, "BRICK"),                 # the main block
        (1, 3, 41, 5, "STONE"),                  # the cornice
        (0, 6, 3, 30, "STONE"), (39, 6, 42, 30, "STONE"),      # pilasters framing the panel
        (5, 6, 37, 31, "BRICK_HI"),              # the field the plate is screwed to
        (4, 7, 37, 25, "PLATE"),                 # THE SIGN BAND, blank by design, 34 x 19
        (4, 27, 10, 36, "GLASS"), (32, 27, 38, 36, "GLASS"),   # the only flats showing
        (5, 28, 9, 30, "BLIND"), (33, 28, 37, 30, "BLIND"),    # blinds, not curtains
        # THE PORTAL IS BUILT AROUND HER DOOR CEL'S FOOTPRINT, NOT TO TASTE. view 751 loop 2
        # is 12x12 and `AddCastView` draws it bottom-centre at (29,44), so it occupies board
        # x 23..34, y 33..44 - region-local x 16..27. The first layout painted the opening at
        # local x 17..25, two columns narrower than the cel, and at 12x the door slab sat
        # PROUD OF ITS OWN STONE SURROUND. The opening is now exactly the cel's width.
        (13, 26, 29, 42, "STONE"),               # the portal surround
        # THE OPENING IS THE CEL'S OWN WIDTH AND NO WIDER, and two attempts to improve on
        # that made the building worse. Widening the portal to 12..31, and then painting
        # the opening 14 wide to survive the model narrowing it, each produced a facade
        # that LOST THE RAILINGS AND THE GLAZED BAYS and came back as flat studded panels;
        # the second also failed `no_text_guard` on its own dentil mouldings. The init is a
        # conditioning image, not a specification, and a small change to it moves the whole
        # result - so this is left where it generates well, and the residual defect (the
        # CLOSED door cel sits about a cel pixel proud of its painted reveal) is reported
        # rather than chased.
        (16, 30, 27, 42, "DOOR"),                # the opening, to the cel's own footprint
        (11, 40, 31, 42, "STONE_LO"),            # the steps
        (0, 37, 10, 42, "RAIL"), (32, 37, 42, 42, "RAIL"),     # iron railings
        (4, 38, 9, 41, "HEDGE"), (33, 38, 38, 41, "HEDGE"),    # clipped box in planters
        (0, 41, 42, 42, "PAVE"),
    ],
    # THE FRAMING IS IN THE PROMPT BECAUSE THE REGION IS SQUARE. The first pass asked for
    # "an apartment building" in a 43x43 frame and got a DOORWAY CLOSE-UP - stone wall, two
    # doors, railings, no panel band and no cornice at all. A near-square crop reads as a
    # detail shot unless the whole elevation is named, so it is named.
    prompt=("the whole front elevation of an apartment building seen straight on, "
            "symmetrical, stone pilasters, deep stone cornice, a large blank bronze "
            "panel across the upper facade, two glazed bays with pale blinds, a "
            "central stone portal with a dark door, an entryphone and a camera, "
            "iron railings, clipped box hedges, overcast daylight, sharp focus, "
            "fine detail"),
    # THE SILHOUETTE IS NOT A RECTANGLE. Her sky stays hers in the top corners and either
    # side of the raised bay, which is what keeps this off the hard-rectangle list that
    # Bare Necessities was on.
    # THE KEEP RECTS MUST COVER EVERY COLUMN THE INIT PAINTS. They kept x 1..41 while the
    # init paints pilasters, railings and pavement from x 0 to x 42, so two columns at each
    # edge were pasted but not claimed as building - 62 px of HER art left inside the
    # silhouette, which showed at 12x as vertical strips of her red script down both sides
    # and failed acceptance at 0.74.
    surround={"keep": [(0, 3, 42, 42), (10, 0, 32, 4)], "mode": "rects"},
    scenery=("SKY",),
    # FIRST INSTALL, so the eraser is exactly the region - and `check_clear_covers_region`
    # proves it covers it rather than leaving a band for the next install to inherit.
    clear=(7, 1, 49, 43),
    # THE PLATE IS 34 x 22 CEL PX AND THAT NUMBER IS THE WHOLE DESIGN.
    #
    # "VILLA LOCKE & KEYES" is 19 characters, and every arrangement is WIDTH-bound:
    #     one line                         cap 2.50   impossible
    #     VILLA LOCKE / & KEYES  41 wide   cap 4.19   cannot reach 5 at ANY size
    #     VILLA / LOCKE & / KEYES 32 wide  cap 4.94   just short
    #     VILLA / LOCKE & / KEYES 34 wide  cap 5.19   CLEARS SIERRA'S 5
    #     ...                    36+ wide  cap 5.38   saturates, wider buys nothing
    #
    # The HEIGHT then came down from 22 rows to 19 by making `leading` a property of the
    # sign instead of the engine's fixed 0.16 - an engraved plate sets its lines close, and
    # at 0.05 the same three lines clear 5 in 19 rows (cap 5.10) instead of 22. Four rows of
    # facade handed back for nothing. It does NOT rescue the composition on its own, which
    # is why the facade is a frontispiece; see the `init` note.
    # So three lines at 34 x 22 is not a preference, it is the only arrangement that clears
    # her own cap height at all. At 5.19 it is already above 5, so it survives the pending
    # decision to normalise the other signs upward without needing to be redone.
    #
    # The cost is stated rather than hidden: a plate that clears 5 is 79% of the facade's
    # width and 51% of its height. That is NOT a discreet brass plate beside a door, it is a
    # large engraved bronze panel - a solicitor's nameplate blown up to the size of the
    # building, which is the pretension made literal and the better joke.
    #
    # Board x 11..44, y 7..28. The marble sweeps this region from board row 34 down, so the
    # building overhangs the path and THE SIGN CLEARS IT BY SIX ROWS - measured, 0 swept
    # pixels inside the plate.
    sign={
        "rect": (4, 7, 37, 25),
        "lines": ["VILLA", "LOCKE &", "KEYES"],
        "leading": 0.05,                  # an engraved plate sets its lines close

        "face": (0x7A, 0x60, 0x36),       # solid bronze
        "face_hi": (0xA8, 0x8A, 0x54),
        "edge_hi": (0xD6, 0xBA, 0x78),
        "edge_lo": (0x30, 0x24, 0x12),
        # ENGRAVED, NOT RAISED, AND THAT IS WHY THESE TWO LOOK SWAPPED. `sign_board` draws
        # `ink_lo` on the side AWAY from the light and `ink_hi` towards it, which models a
        # letter standing proud. An engraved letter is a trough: the lit face is the one
        # turned away from the light, so the bright colour goes in `ink_lo`. Inverting the
        # pair is the whole difference between a plaque and a sticker.
        "ink": (0x36, 0x2A, 0x14),
        "ink_lo": (0xC6, 0xAA, 0x6E),     # the lit wall of the trough
        "ink_hi": (0x22, 0x1A, 0x0C),     # the shaded wall
        "bolts": True,                    # screwed to brickwork, and the screws show
        "bolt": (0x96, 0x7E, 0x4E),
        "bolt_hi": (0xDE, 0xC8, 0x96),
        "depth": 0.3,                     # a plate is thin
        "shadow": 0.7,
        "shadow_strength": 0.55,
        "relief": 0.07,
        "grain": 0.03,                    # polished, not weathered
        "fill": 0.86,
        "feather": 7.0,
        "prompt": "a close photograph of an engraved solid bronze nameplate screwed to "
                  "brickwork, polished metal with a soft specular sheen, bevelled edge, "
                  "crisp engraved lettering, soft daylight, sharp focus, fine detail",
        "negative": "blurry, out of focus, bokeh, 3d render, cgi, vector, illustration, "
                    "flat, cartoon, pixelated, jpeg artifacts, people, distorted, frame, "
                    "vignette, smoke, steam, fog, rust, dirt, peeling, damaged",
        "strength": 0.22,
        "steps": 24,
        "cfg": 7.0,
        "control": 0.8,
    },
    name_lines=["VILLA", "LOCKE &", "KEYES"],
    name_rect=(4, 7, 37, 25),
    name_ink=(0x36, 0x2A, 0x14),
    # Her neon script, which is what has to be GONE - and the rect and the test are both
    # the SECOND attempt. The first was `redish` over (2,4,40,13), a box 11.5% dense that
    # scored 0.74 against the set where her script had been removed and only 0.67-0.70
    # against the set where it is still there. A check that reads HIGHER after the thing it
    # looks for is gone is not a weak check, it is an inverted one. See `neonscript`.
    detect=[
        Detect("A board script", "cel", (2, 8, 40, 13), "neonscript"),
    ],
    # STRENGTH DOWN AND CONTROL UP, against the board's usual 0.88/0.35. Those numbers suit
    # a facade whose composition the model can be trusted to invent; this one's composition
    # IS the design - a bronze panel of an exact size in an exact place - and at 0.35 the
    # softedge map did not hold it. 0.80/0.65 keeps the layout and still re-renders the
    # surfaces.
    scale=20, strength=0.80, control=0.65, cfg=9.0, steps=32, seed=7,
    notes=("FOUR spoken clips name the old building and NONE is renamed, but they are not "
           "the same kind of line and a future rename decision would treat them "
           "differently. 166 (`Welcome to the Rental Office. Our security apartments "
           "feature 24-hour eunuch guards!`) and 169 (`You'll love it! 24-hour security, "
           "spa, exercise room and free parking for BMWs.`) are the RENTAL OFFICE'S sales "
           "patter ABOUT the product. 193 (`Your %d weeks of prepaid rent on your Security "
           "Apartment are non-refundable.`) and 196 (`You already live at the security "
           "apartments.`) are THIS BUILDING'S OWN. There is no greeting band. "
           "43x43 at scale 20 = 860x860 = 739,600 px, inside the measured 810k ceiling."),
)


# --- Z-Mart -> General Dealer ------------------------------------------------------
# THE KEY IS `zmart`, AND THAT IS THE CONVENTION RATHER THAN A PREFERENCE. Seven keys here
# match both Sierra's name and the location's script (`pawnshop`/pawnShop.sc,
# `lowcost`/lowcost.sc, `security`/security.sc, ...). The three that DISAMBIGUATE the two
# rules all follow her NAME: `blacksmarket` for market.sc, `qtclothing` for clothing.sc,
# `socketcity` for appliance.sc. Z-Mart's script is discount.sc, so the convention says
# `zmart`, not `discount`.
#
# IT IS CEL 5, 49x30 at (258,2), 100% opaque - established from the decode before anything
# was planned, because that question cost the Factory six rounds and has since caught two
# people. `decode_pic_full.py --cels` lists fifteen cels, and TWO of them overlap this
# hotspot (251,10)-(311,43): cel 5 at y 2..31 and cel 6 at y 32..74. `DoorFor` settles it -
# `LocationId.ZMart => (11, 282, 34)` (discountP, room1.sc:531) and `AddCastView` draws
# bottom-centre, so her 25x11 door covers game x 270..294, y 24..34, which is cel 5's own
# shopfront with the threshold three rows below her cel. Cel 6 belongs to Monolith Burgers,
# whose door is (288,75) against a cel ending at y 74. Checked rather than assumed.
#
# THE HOTSPOT IS 61x34 AND THE BUILDING IS 49x30 - 79% of its width, 88% of its height. The
# region is the CEL and nothing more: `sierra_region` requires a `cel:NN` region to BE the
# cel, and the Pawn Shoppe and Rentflix both settled the reason - HER THRESHOLD SITS BELOW
# HER BUILDING BY DESIGN, and growing ours to meet the door makes it taller than hers, which
# is a fidelity change dressed up as a fix.
#
# NOT IN THE picPATCH. That strip is 183x25 at (68,138) and covers game x 68..250; this cel
# starts at x 258. Measured, not assumed - it is what made two renames look broken for
# three rounds.
#
# THE MARBLE, re-derived from `MarblePath` rather than quoted: over all 170 anchors the 9x8
# marble (view 0 loop 2, drawn bottom-centre) sweeps 8,589 px of the board, of which 42 fall
# inside this cel - game y 29 at x 258..262, y 30 at x 258..268, y 31 at x 258..286. THE
# LARGEST SWEPT-FREE RECTANGLE INSIDE THE CEL IS THE FULL WIDTH BY ROWS 0..26, found by
# search, so the sign has the whole top of the building and the constraint never binds. The
# building may overhang the path - the marble walks in FRONT of a building - and the sign
# may not; this one clears it by eight rows.
BUILDINGS["zmart"] = Building(
    key="zmart", sierra="Z-Mart", new_name="General Dealer",
    source="cel:05", region=(258, 2, 49, 30), hotspot=(251, 10, 311, 43),
    palette={
        # HER OWN NAVY IS THE GIFT HERE. The reference Zain sent is a navy-and-gold
        # shopfront, and #405890 and #385880 - her mansard roof, 21% of the cel between
        # them - are already that navy. So the identity is sampled rather than invented,
        # and the gold lives where no colour word can reach it: in the `sign` dict.
        "MARGIN":   (0xF8, 0xF8, 0xF0),   # THE BOARD'S OWN CREAM MARGIN - hers, see `scenery`
        "ROOF":     (0x40, 0x48, 0x58),   # her darkest roof value, 15 px of row 0
        "ROOF_HI":  (0x68, 0x70, 0x78),   # her lit course at the ridge
        "STONE":    (0xB0, 0xB8, 0xC0),   # her fascia's light value, row 12
        "STONE_LO": (0x98, 0x98, 0xA8),   # her fascia mid, 5.7% of the cel
        "STONE_SH": (0x78, 0x78, 0x80),   # her fascia in shadow, 3.3%
        "PLATE":    (0x28, 0x50, 0x68),   # THE SIGN BAND, left plain for stage two
        "NAVY":     (0x40, 0x58, 0x90),   # hers, 11.2% - the shopfront joinery
        "NAVY_LO":  (0x38, 0x58, 0x80),   # hers, 9.9% - the doors
        "NAVY_HI":  (0x58, 0x90, 0xC0),   # hers - a lit moulding
        "JOIN":     (0x28, 0x28, 0x38),   # her deepest value, row 4 - the recessed doorway
        "GLASS":    (0xC8, 0xE0, 0xF8),   # her glazing, 7.2%
        # DELIBERATELY NOT BRIGHT: a light horizontal band low in the frame is what haze
        # looks like, and the model painted mist over a building's base for two rounds
        # because of one.
        "RISER":    (0x50, 0x70, 0x98),   # her shopfront base course
        # NOT HERS, AND DELIBERATELY, exactly as the Pawn Shoppe's GLOW is not. A 1990 cel
        # is lit by one flat daylight and has no warm light anywhere, so an init built only
        # from her colours is blue and grey top to bottom with nothing warm in it - and a
        # general dealer's whole character is lit goods behind glass.
        "GLOW":     (0xF0, 0xC8, 0x88),
        "GOODS":    (0x90, 0x78, 0x58),   # sacks and crates behind the glass
        "GOODS2":   (0xA8, 0x90, 0x68),
    },
    # THE DOOR IS A QUARTER OF THE FACADE AND IT IS NOT NEGOTIABLE. Her view 751 loop 11 is
    # 25x11 at (282,34), bottom-centre, so it covers local x 12..36, y 22..29 - half the
    # width of a 49-wide building. The shopfront is therefore a wide central pair of doors
    # with one narrow display window either side, because that is the only arrangement her
    # own door sprite allows. The doors are painted SHUT: `AddCastView(DoorView, ...)` only
    # runs WHILE the door animates, so on the static board no door sprite is drawn at all
    # and a hole in the art is a hole on the screen.
    #
    # The order matters - later boxes paint over earlier ones, and `building_matte` walks
    # the same list in the same order, so a `scenery` box CUTS what was painted under it.
    init=[
        (0, 0, 48, 29, "STONE"),
        # the roof, four rows, lit along the ridge
        (0, 0, 48, 3, "ROOF"),
        (0, 0, 48, 1, "ROOF_HI"),
        # HER HIPPED CORNERS, CUT BACK TO THE BOARD'S OWN MARGIN. Measured off her cel: the
        # cream #F8F8F0 margin runs x 0..6 and x 42..48 on rows 0..1 and x 0..5 and x 43..48
        # on row 2, and her roof's hips are what expose it. Reproducing the cut gives the
        # roof a silhouette to be a silhouette AGAINST - the only thing above this building
        # is the board's border - and it puts the matte's join on a line she already drew.
        (0, 0, 6, 1, "MARGIN"), (42, 0, 48, 1, "MARGIN"),
        (0, 2, 5, 2, "MARGIN"), (43, 2, 48, 2, "MARGIN"),
        # the fascia, and the pilasters that frame it
        (0, 4, 48, 19, "STONE"),
        (0, 4, 3, 29, "STONE_SH"), (45, 4, 48, 29, "STONE_SH"),
        # A MOULDED CORNICE UNDER THE EAVES, AND CAPITALS ON THE PILASTERS. Not decoration:
        # Villa Locke & Keyes' generation at 20 px per cel px comes back with crisp dentils
        # and mouldings off an init full of 2-3 px features, while this one's first three
        # rounds returned smeared flat panels off an init that is four big bands. Fine
        # structure in the init is what the model has to render; where there is none it
        # paints a defocused wall.
        (0, 4, 48, 5, "STONE_LO"),
        (0, 4, 3, 5, "STONE"), (45, 4, 48, 5, "STONE"),
        # THE SIGN BAND, BLANK BY DESIGN. 39x14 at local (5,6): stage two builds the board
        # into these pixels at the generation's own resolution, so sign and building are
        # downscaled together, once. See the `sign` note for why it is this size and no
        # other.
        (5, 6, 43, 19, "PLATE"),
        # THE SHOPFRONT HEAD IS A LIGHT STONE LINTEL, AND ROUND TWO IS WHY. It was a navy
        # band, two rows, between a navy-blue plate above and a navy shopfront below - three
        # mid-dark blues in a row - and the model merged all of them into ONE BLUE BARN WALL
        # with the doorway gone altogether. The Pawn Shoppe's own finding, in a second
        # place: at this size what survives is the VALUE LADDER, so the facade alternates -
        # dark roof, light fascia, dark plate, LIGHT LINTEL, dark shopfront with bright
        # glass, mid threshold.
        #
        # It is also broken by the two piers so it is not one continuous dark band under a
        # sign, which is precisely where the model wrote `PADEMOKIK BATEWEIS ILP` across the
        # Pawn Shoppe.
        (4, 20, 44, 21, "STONE"),
        (4, 21, 44, 21, "STONE_LO"),
        (11, 20, 13, 29, "STONE_LO"), (35, 20, 37, 29, "STONE_LO"),
        # the shopfront
        (4, 22, 44, 29, "NAVY_LO"),
        # the left display window: glass, a lit interior behind it, goods against the light
        (4, 22, 10, 26, "GLASS"),
        (5, 23, 9, 26, "GLOW"),
        (5, 24, 7, 26, "GOODS"), (8, 24, 9, 26, "GOODS2"),
        # the right display window
        (38, 22, 44, 26, "GLASS"),
        (39, 23, 43, 26, "GLOW"),
        (41, 24, 43, 26, "GOODS"), (39, 24, 40, 26, "GOODS2"),
        # THE DOORS, SHUT, over her own door cel's footprint (local x 12..36, y 22..29).
        # The piers above take x 11..13 and x 35..37, so every row the sprite will ever
        # cover has building painted behind it. The reveal is the DARKEST value in the
        # frame and the panels the brightest, because at 49 cel px wide a doorway that is
        # merely a different blue is not a doorway at all - round two proved that.
        (14, 22, 34, 29, "JOIN"),
        (15, 22, 33, 23, "GLASS"),         # the transom light over the doors
        (15, 25, 23, 27, "GLASS"), (26, 25, 33, 27, "GLASS"),   # the glazed upper panels
        (24, 24, 25, 29, "NAVY_LO"),       # the meeting stile, 2px - nothing thinner survives
        # the stall riser under each display window. THE DOORWAY HAS NO THRESHOLD BOX: the
        # sprite covers local y 22..29 and every row it will ever cover has to have building
        # painted behind it, so nothing is allowed to interrupt the doors at their base.
        (4, 27, 10, 29, "RISER"), (38, 27, 44, 29, "RISER"),
    ],
    # MATERIAL ONLY. Not one colour word: the navy, the stone and the warm interior are all
    # in the palette above, and a colour word here would spend the budget saying something
    # the init already says. 63/77 tokens, counted with the local tokenizer BEFORE the first
    # generation rather than after a failed one.
    #
    # AND THE CATEGORY WORD EARNED ITS OWN ROUND. The first five generations led with "the
    # front of an old general store" and came back as a BARN - a wide blue boarded gable
    # with a shed roof - because that is what the massing suggests and nothing in the prompt
    # argued. Leading with "an old village general store SHOPFRONT" and naming shop windows
    # rather than display windows gave a pitched slate roof with a hip, a downpipe, ashlar
    # quoins and shelving behind the glass, with the same init, seed and settings. Category
    # is the one thing only the prompt can carry (CAST.md 0), and this is that finding in a
    # second place.
    prompt=("a photograph of an old village general store shopfront, stone piers, a deep "
            "painted timber fascia, two shop windows full of sacks crates and jars on "
            "shelves, a wide pair of part glazed timber doors, a tiled stall riser, a "
            "slate roof, overcast daylight, sharp focus, fine detail"),
    # THE WHOLE REGION IS GENERATED, so `silhouette` is all-True and the installer never
    # writes one of her pixels as part of the building. The MATTE draws the outline instead
    # - which is the fix that took Common Threads, Watt a Bargain and Needful Things from
    # 25.4%, 6.6% and 6.1% to zero with no GPU at all.
    surround={"mode": "rects", "keep": [(0, 0, 48, 29)]},
    # MARGIN IS THE ONLY SCENERY KEY, AND IT IS A LOOK RATHER THAN A RULE. The other keys
    # that touch the region's border - STONE_SH at the pilasters, ROOF along the top, STEP
    # and RISER at the base - are all the BUILDING reaching its own edge, exactly as the
    # Pawn Shoppe's CORNICE and STONE do. Her own scenery inside this cel is 23 px of green
    # in slivers 1-3 px wide at the left and right edges of rows 6..11; cutting those out
    # would show her 1990 foliage through a notch in our wall, which is the Bare Necessities
    # finding ("her blob showed through, upscaled") for four pixels of gain. They are
    # generated.
    scenery=("MARGIN",),
    # FIRST INSTALL, so the eraser is exactly the region - and `check_clear_covers_region`
    # proves it covers it rather than leaving a band for the next install to inherit.
    clear=(258, 2, 306, 31),
    name_lines=["GENERAL", "DEALER"],
    name_rect=(9, 1, 39, 12),
    name_ink=(0xD8, 0xB0, 0x60),
    # THE ARITHMETIC CAME FIRST AND IT DECIDED THE BUILDING, as it did at Villa Locke &
    # Keyes. Measured by rendering the real `install_employment.text_mask` through
    # `sign_board`'s own padding, in CEL pixels, against Sierra's own 5-row cap:
    #
    #     GENERAL DEALER on ONE line, board 41 wide    cap 3.00
    #                                 board 45 wide    cap 3.30
    #                                 board 49 wide    cap 3.55   <- the WHOLE facade
    #     GENERAL / DEALER            37 x 14          cap 5.05
    #                                 39 x 14          cap 5.25
    #                                 39 x 13          cap 4.80
    #
    # ONE LINE IS WIDTH-BOUND, SO HEIGHT BUYS IT NOTHING: at every board height from 9 to 15
    # rows a single line measures the same 3.00-3.55, because the width runs out first. Even
    # given the entire 49-px facade it lands at 3.55 - below the ~4 at which a reader stops
    # reading letters and starts reading word shapes (see the cap-height table in
    # BUILDINGS.md 39a). Two lines is not a preference here, it is the only arrangement that
    # reaches Sierra's 5 at all, and 14 rows is the shortest board that does.
    #
    # THE SUB-LINE IS GONE, and it was measured before it was dropped. `FINE GOODS &
    # PROVISIONS` is 23 characters: as a third line in this same box the engine sets every
    # line at one size and the whole block collapses to cap 2.10-2.20, taking GENERAL DEALER
    # down with it; in a band of its own across the full 49-px facade it reaches 2.40. Both
    # are half the legibility floor. Zain ruled it out independently; the numbers agree.
    #
    # 39 x 14 is 80% of the facade's width and 47% of its height, between Casa El Cheapo's
    # 42% and Villa Locke & Keyes' 51%. Local (5,6)-(43,19): seven rows clear of the marble's
    # highest sweep in this cel (local row 27) and two clear of her door cel's head at
    # local row 22.
    #
    # NO DEVICE, CHECKED RATHER THAN ASSUMED. The reference's laurel `GD` crest would have to
    # live beside the board, and the board leaves 5 cel px at each side - Socket City's plug
    # was already marginal at 13, and the deciding test there was contrast at TRUE size, not
    # how good the object looked on its own. There is no room for one and none is invented.
    # THE SIGN IS NOW PAINTED ON HIS CROWN, NOT BOLTED TO A WALL, and every number below
    # was measured rather than chosen.
    #
    # WHAT HIS CROWN CAN CARRY. The crown is an oval, so a wide box starts low on the arc
    # and a narrow one starts high: width and height trade against each other. Measured on
    # his own pixels, per cel column, and against the real `text_mask`:
    #
    #     box x 14..34, y 0..11   cap 2.95   fits above his gold piping
    #     box x 12..36, y 1..11   cap 3.55
    #     box x 10..38, y 2..11   cap 4.10
    #     box x  9..39, y 1..12   cap 4.35   <- chosen; ink x 11.8..37.5, y 2.0..10.9
    #     a 39 x 13 box                cap 5.35   does not fit the crown at all
    #
    # **SIERRA'S CAP OF 5 IS NOT AVAILABLE ON THIS BUILDING.** Two lines at 5 need 13 rows;
    # the crown's navy runs 11 to 12 rows between its arc and the gold band at the width the
    # name needs, and only 8 rows above his gold piping - which is where HIS lettering sits,
    # and it is why his own type is small. 4.35 is the largest that fits inside the crown,
    # it clears the ~4 at which a reader stops reading letters, and it is 1.4x the 3.10 a
    # single line would give on the same building. The alternative is the thing he rejected:
    # a 39x13 plate over the cap at cap 5.35.
    #
    # NO PLATE. `depth` 0.10 with the face, the bevel and the grain all set to his crown's
    # own navy makes the board invisible and leaves only the signwritten letters - a shade
    # away from the light, a highlight towards it, then the body - so the name is PAINTED
    # ON THE CAP, as his picture has it, and still constructed at the generation's own
    # resolution rather than stamped into the downscaled cel. The pad shrinks with the
    # bevel, which is worth 0.6 of a cel px on its own: at depth 0.50 the same box measures
    # cap 3.65.
    sign={
        # (9,0)-(39,10), and the bottom bound is HIS CREST rather than the gold band. Its
        # laurel starts at cel y 9.0 (x 28.4..37.9, measured on the fitted image), so a box
        # that merely clears the band puts DEALER's last letters on the wreath - which is
        # what the first render did. Measured alternatives:
        #
        #     9,1-39,12   cap 4.35   ink y 2.5..11.4   over the crest by 2.4
        #     9,0-39,11   cap 4.35   ink y 1.6..10.4   over it by 1.4
        #     9,0-39,10   cap 4.35   ink y 1.1.. 9.9   over it by 0.9   <- chosen
        #     10,0-38,10  cap 4.10   ink y 1.4.. 9.6   over it by 0.6
        #     9,0-39, 9   cap 4.10   ink y 0.8.. 9.1   clears it - and the capitals then
        #                                              cross the crown's own top edge at
        #                                              x 12..13, where the matte cuts them
        "rect": (9, 0, 39, 10),
        "lines": ["GENERAL", "DEALER"],
        # NO BOARD AT ALL. Painting the plate in the crown's own navy was tried first and it
        # still read as a flat rectangle on the cap, because the crown is a cylinder: it
        # measures #101F2F at its ends against #203451 in the middle, and no single face
        # colour sits on both. `plate: False` signwrites the letters straight onto his
        # crown and leaves its gradient, its gold piping and its crest showing between
        # them - which is what his picture has, in red script where ours is gilt.
        "plate": False,
        "face": (0x22, 0x33, 0x58), "face_hi": (0x2A, 0x3D, 0x66),
        "edge_hi": (0x2A, 0x3D, 0x66), "edge_lo": (0x22, 0x33, 0x58),
        "ink": (0xD8, 0xB0, 0x60), "ink_hi": (0xF4, 0xE0, 0xA8),
        "ink_lo": (0x5A, 0x3E, 0x14),
        "bolts": False,                   # nothing is bolted to a cap
        "depth": 0.10,                    # no plate, so no thickness and a small pad
        "shadow": 0.0, "shadow_strength": 0.0,
        "relief": 0.10,                   # signwritten and raised, so three passes read
        "grain": 0.0,                     # the crown's own texture is already there
        "leading": 0.05,
        "fill": 0.86, "feather": 7.0,
        # Unused while `marry` is False, and kept so the stage can be run if anyone wants
        # to light the lettering. It describes the CROP a marrying pass would see - gilt
        # letters on navy - not a timber board, which is what it used to say.
        "prompt": ("a close photograph of gilded signwritten lettering on a navy painted "
                   "curved surface, fine shaded outline, soft overcast daylight, sharp "
                   "focus, fine detail"),
        # THE SHARED NEGATIVE MINUS EVERY TEXT-SUPPRESSING TERM. Running `text, letters,
        # words, writing, lettering, signage` over an image whose band is full of letters is
        # an instruction to DELETE them.
        "negative": ("blurry, out of focus, bokeh, 3d render, cgi, vector, illustration, "
                     "flat, cartoon, pixelated, jpeg artifacts, people, distorted, frame, "
                     "vignette, smoke, steam, fog"),
        "strength": 0.22, "steps": 24, "cfg": 7.0, "control": 0.8,
        # RUN AND MEASURED, THEN TURNED OFF - the Pawn Shoppe's finding for the third time.
        # The marrying img2img survived its own drift check easily (IoU 0.982, ink area
        # -0.4%, centroid 0.020 cel px) and added NO material the construction did not
        # already have: side by side at native 20 px per cel px the bolt, the gilt edge and
        # the bevel are the same and the board's face is very slightly smoother. On a facade
        # that is already softer than it should be, the crisp construction is the half worth
        # keeping.
        "marry": False,
    },
    detect=[
        # HER `Mart` SCRIPT, 28.2% dense - inside the 25-55% window the density rule asks
        # for, measured before the scan rather than after it. The box is the script alone:
        # taking in her blue `Z` as well (x 5..13) drops it to 23.8%, which is the QT failure
        # - a mask that stops encoding letterforms and starts describing "some red on a
        # band".
        Detect("A board script", "cel", (14, 5, 44, 12), "redish"),
        # THE INTERIOR PLATE, view 811 loop 0 - her logo again and INVERTED, a red `Z-` with
        # a blue `Mart` where the board has them the other way round. `redish` over her `Z-`
        # measures 36.3%. Her blue script needs a predicate none of the ten existing ones
        # catch, and one good mask per surface is the rule, so it is not added.
        Detect("B interior plate", "view_811_l0_c0.png", (6, 4, 56, 21), "redish"),
    ],
    surfaces=[
        # view 811 loop 0 is this shop's interior backdrop (`ArtFor`: 811, talker 361, items
        # 711) and carries the whole logo on a white plate, measured at x 1..114, y 1..24
        # inside her own black rule - the repaint stops at the rule and leaves it hers.
        #
        # HER `DISCOUNT STORE` SUB-LINE GOES WITH IT, and that is the opposite call from
        # Socket City's `APPLIANCE STORE` for a measured reason: that line sat OUTSIDE her
        # plate and was not part of the brand, while this one is inside the same white plate
        # in the same script, part of one logo lockup. Leaving it would be leaving Z-Mart on
        # the wall.
        Surfaces(file="view_811_l0_c0.png", plate=(1, 1, 114, 24),
                 body=(0xF0, 0xF8, 0xF8),            # her plate's own white, measured
                 ink=(0x1E, 0x34, 0x5C),             # the board's navy; gold on white
                                                     # would not read
                 lines=["GENERAL DEALER"]),
    ],
    # ZAIN'S OWN PICTURE, AND THE GENERATED FACADE IS WITHDRAWN.
    #
    # *"that looks absolutely nothing like the picture I shared!!!, it's now just a blue
    # building with a HUGE sign, look at my building!, it's a nice looking hat shaped roof
    # with glass, you gave me a god damn block!!!"*
    #
    # He is right, and the instruction that produced it - treat the reference as a MOOD
    # rather than as the building - is withdrawn. His picture is a peaked cap: a navy oval
    # crown with gold piping, a gold band with braid and a laurel GD crest, a glossy near
    # black visor projecting over the entrance, and a curved glass curtain wall under it.
    # The silhouette IS the identity, and a rectangle with a sign on it is what he rejected.
    # Same route as his Factory, which beat three generated attempts: `source_image`.
    #
    # THE FIT, MEASURED. His building spans image x 60..1175, y 62..745 - crown tip to the
    # doors' threshold - which is 1115 x 683 = **1.633:1 against the region's 49/30 =
    # 1.633:1**, so it fits the cel with no distortion and nothing cropped off the sides.
    # `baseline` 31 puts his threshold on HER building's own base row.
    source_image={
        "path": r"C:\AI-Accelerator\jones\tools\samples\generaldealer.jpg",
        "crop": (60, 62, 1175, 745),
        "baseline": 31,
        # HIS LETTERING COMES OFF AND OURS GOES ON, and the arithmetic is in the `sign`
        # note below. NOT with `cover`: his crown is an oval with a light gradient across
        # it, the plainest patch of it measures #101F2F against a centre of #203451, and
        # tiling that over the middle leaves a dark block. The first attempt sourced the
        # patch from under his sub-line, which there is the gold braid and the GD crest,
        # and tiled a row of crests across the cap.
        #
        # `smooth` reconstructs the band from the crown either side of it instead, so the
        # gradient and the gold piping come back through it. Two bands because the ring has
        # to be crown and not sky: the lower one works from left and right, and the upper
        # one from BELOW, off the band just rebuilt - at x 9 the crown's arc only starts at
        # y 1.6, so a ring above it would be sky.
        # The upper band runs to row 0 and not row 1, because it did at first and the tops
        # of his capitals were left as a red ghost across the crown - his script starts at
        # cel y 1.05, so a band starting at y 1 cuts through it.
        "smooth": [(9, 2, 41, 9, "lr", 1, 0.2), (9, 0, 41, 4, "b", 1, 0.2)],
        # HIS OUTLINE, AUTHORED, because a supplied composite has no recoverable alpha
        # (see fit_source.clip_matte). Read off his image on the cel grid, column band by
        # column band: the crown's arc across the top, and the lower bound dropping to the
        # glass box only where the glass actually is - at the far left and right the crown
        # overhangs open sky and HER board has to show under it.
        # ONE BAND PER COLUMN WHERE THE ARC MOVES, and the first version was authored in
        # bands of two to four. The cap's shoulders fall a whole cel row per column there,
        # so the coarse steps kept a block of HIS SKY either side of the crown and it
        # showed on the installed board as a pale rectangle. Measured off the fitted image
        # per column - the first row that is more than 60% cap - and rounded UP so no sky
        # is ever kept.
        #
        # The bottoms are measured too, and they are not all 29: at x 1..3 only the crown's
        # left TIP is there and his trees are under it, and at x 45..47 only the gold
        # band's right end, with his planting below. x 0 and x 48 are his sky and his
        # trees top to bottom and are excluded outright.
        # ONE RECT PER COLUMN AT QUARTER-CEL PRECISION. Authored in bands of two to four
        # whole cel rows first, and the cap's shoulders fall a whole row per column there,
        # so the steps kept blocks of HIS SKY either side of the crown and they showed on
        # the installed board as pale rectangles. A quarter cel is 5 px at scale 20.
        #
        # Measured on the RAW fit, before `smooth` runs: the smoothing fills rows 0..4
        # across x 9..41 with crown colour, so an outline measured after it reads as a
        # straight line and squares off the oval.
        #
        # The bottoms are measured too and they are not all 29: at x 1..3 only the crown's
        # left TIP is there with his trees under it, at x 45..47 only the gold band's right
        # end, and x 0 and x 48 are his sky and his trees top to bottom.
        "clip": [
            (1, 8.75, 1, 16), (2, 6.75, 2, 16), (3, 5.75, 3, 16),
            (4, 4.75, 4, 29), (5, 4, 5, 29), (6, 3.5, 6, 29), (7, 2.75, 7, 29),
            (8, 2.25, 8, 29), (9, 2, 9, 29), (10, 1.5, 10, 29), (11, 1.25, 11, 29),
            (12, 0.75, 12, 29), (13, 0.5, 13, 29), (14, 0.25, 14, 29),
            (15, 0, 35, 29),                                   # the crown's flat top
            (36, 0.25, 36, 29), (37, 0.5, 37, 29), (38, 1, 38, 29), (39, 1.25, 39, 29),
            (40, 1.75, 40, 29), (41, 2.5, 41, 29), (42, 3.25, 42, 29), (43, 4.75, 43, 29),
            (44, 13, 44, 29),                                  # band and glass, no crown
            (45, 13, 46, 17), (47, 13.25, 47, 17),             # the band's right end only
        ],
    },
    matte_file="matte_980x600.png",
    # 0.88/0.45 WITH A CRISP CONDITIONING, AND ROUND ONE IS WHY. Villa Locke & Keyes'
    # 0.80/0.65 was tried first, on the same reasoning that justified it there - this
    # facade's composition IS the design, a board of an exact size over a doorway whose
    # width her own door sprite fixes. It came back as THE INIT: cropped at native 20 px per
    # cel px there was no material anywhere in the frame, no timber, no masonry, no glass,
    # just the blurred block painting with stepped ramps where its boxes meet.
    #
    # That is BUILDINGS.md's "both smooth inputs have to be released at once" from the other
    # side: at strength 0.80 the Gaussian-blurred init dominates, and at control 0.65 a
    # conditioning map built from a 16-px blur pins it there. Villa got away with it because
    # its init carries pilasters, a cornice, railings, planters and four openings; this one
    # is four flat bands and a plate across half the frame, so there was nothing in either
    # input for the model to work from.
    #
    # So: the Factory's shipped compromise, 0.88/0.45 - enough freedom to render surfaces,
    # enough control to hold the massing - and `cond_blur` down to a quarter so the
    # structure that IS there arrives as thin hard lines rather than 16-px ramps.
    cond_blur=0.25,
    init_blur=0.5,
    scale=20, strength=0.88, control=0.45, cfg=9.0, steps=32, seed=7,
    notes=("TWELVE spoken clips, ids 330-341, say `Z-Mart` - eleven `Welcome to Z-Mart ...` "
           "and 341 `Thank you for shopping at Z-Mart.` NONE is renamed, as with every "
           "other building: a subtitle disagreeing with its own audio is worse than the old "
           "name. `Board.All`'s label `Z-Mart` is text resource 700 index 82 - verified in "
           "the raw bytes, Place 11 + 71 - and is NOT "
           "changed either - no rename on this board has touched that table. "
           "49x30 at scale 20 = 980x600 = 588,000 px, inside the measured 810k ceiling."),
)

# --- the Bank -> Hyper Save (the THIRTEENTH and last board building) ----------------
#
# THE DOORWAY WAS PLACED FIRST, which is now the rule (gen_building.py's docstring), and on
# this building it decided every other number. `DoorFor` gives the Bank (loop 4, x 20,
# y 147) and her loop 4 cel is 9x12, so `AddCastView` lands it on game x 16..24 y 136..147
# with the LEAF BODY in rows 0..9 - y 136..145 - and only the swinging feet in y 146..147.
#
# His door is 200 x 205 source px. Her body is 10 rows tall, so the scale is not a choice:
#
#     205 source px / 10 cel rows = 20.5 source px per cel px
#
# and everything else follows from it. His building is 850 x 1155 source px, which at that
# scale is 41.5 x 56.3 cel px; his door's centre sits 49.4% across it, so placing that
# centre on x 20 puts his left edge at x -0.5 and his right at x 41. Half a pixel of his
# left wall falls off the board and nothing else is cut.
#
# THE POLE SIGN IS DROPPED, and unlike Bun Believable that is forced rather than chosen.
# The door's x is Sierra's and the board's left edge is x 0; between them the composition
# has 20 columns to the left of the door and his building needs 20.5 of them. There is no
# room for a sign that stands further left again. Nothing of hers is lost either way - her
# Bank is a plain round tower with no pole sign - and his building still carries HS twice,
# on the pediment roundel and on the fascia.
BUILDINGS["bank"] = Building(
    key="bank", sierra="Bank", new_name="Hyper Save",
    # `background`, NOT `cel:12`. Her Bank IS cel 12 - 43x54 at (11,92) - but this region
    # is neither that box nor inside it: it is x 0..41 y 97..153, set by where her door
    # anchor and the board's left edge put his building. `cel:12` would assert the two
    # match and they do not; `background` crops cel 0, which is what "Sierra's pixels for
    # this rectangle" actually means here.
    source="background", region=(7, 97, 42, 57), hotspot=(7, 119, 67, 154),
    palette={"GROUND": (0xC8, 0x90, 0x70)},
    init=[(0, 0, 41, 56, "GROUND")],
    prompt="(supplied art - no generation; see source_image)",
    surround={"mode": "rects", "keep": [(0, 0, 41, 56)]},
    # THE ERASER IS THE REGION AND NOTHING MORE, and that took two goes. `board_fill`
    # writes eight rows ABOVE the region, so on the second install `pick_clear_source`
    # found the first install's own fill in the control frame it compares against the
    # ported sets, matched nothing (5.88 against 0.00) and refused. Widening the eraser to
    # y 89 fixed that and broke something worse: at x 39..41 it reached into BARE
    # NECESSITIES and erased three columns of a neighbour's building every time the Bank
    # was installed, which showed up as that building's own door cel no longer matching
    # its facade (max 183.0, mean 18.5). The fix belongs in the check, not here - a
    # declared `board_fill` is our work and `pick_clear_source` now excludes it the same
    # way it already excludes every region and every clear rect.
    # THE ERASER REACHES BACK TO x 0, WHICH IS THE BOARD'S FRAME, and that is deliberate.
    # The region is x 7..48 and the eraser only has to cover that - but this building was
    # first installed at x 0..41 and then moved right, and the seven columns it vacated kept
    # the old paint: `pick_clear_source` found them in its control frame and refused (24.71
    # against 0.00). Restoring x 0..6 from her board every time costs nothing - they are the
    # frame, and her frame is what belongs there - and it makes the move idempotent instead
    # of leaving a one-off repair for somebody to discover.
    clear=(0, 97, 48, 154),
    # BARE NECESSITIES OWNS x 25..48 y 97..105, so the Bank does not erase it. The two
    # regions contest 216 cel px - Hyper Save's x 7..48 y 97..153 against Bare Necessities'
    # x 25..67 y 69..105 - and the Bank is NEARER the viewer, so it installs last and
    # paints over its neighbour wherever its own roofline is opaque. What it must not do is
    # erase the rest: before `clear_keep` existed that left 99 cel px of Sierra's art as a
    # wedge along the join, across Bare Necessities' door.
    #
    # INSTALL `blacksmarket` BEFORE `bank`.
    # The second band is THE INDUSTRIAL REVOLUTION's, whose region starts at y 149 while
    # the Bank's base lands at y 151. Same rule: the Bank paints its planters over its
    # neighbour's upper margin because it stands in front, and it does not erase what it
    # does not paint.
    clear_keep=((25, 97, 48, 105), (9, 149, 48, 154)),
    name_lines=[], name_rect=(0, 0, 0, 0), name_ink=(0, 0, 0),
    source_image={
        "path": r"C:\AI-Accelerator\jones\tools\samples\hypersave.png",
        # Derived, not chosen: x so his door's centre (source x 720) lands on game x 20,
        # y so his door's top (source y 895) lands on game y 136, and the size so the
        # aspect matches the region's 42/57 = 0.7368 (861/1168 = 0.7372).
        "crop": (300, 96, 1161, 1264),
        "baseline": 153,
        # HIS OUTLINE, one rect per cel column, MEASURED off the fitted image at 20 px per
        # cel px and written down. Columns 3..41 are his roofline read directly; columns
        # 0..2 are EXTRAPOLATED along that same line - his pole sign's board runs down the
        # left of the crop and a colour test reads its pink as building, which would square
        # off the roof's left slope. The slope through columns 3..19 is 0.497 cel rows per
        # column, so columns 2, 1 and 0 are 8.60, 9.10 and 9.60. They are the only three
        # samples here that were not read off the picture.
        #
        # The pediment is the whole reason this is authored at generation resolution rather
        # than on the cel grid: a pediment is a function of x, and at the cel grid it would
        # come out as the staircase that sections 51 and 52 are about. Here the roofline
        # steps a twentieth of a cel.
        "clip": [
        # THREE TRIMS, each one measured against his own crop arithmetic rather than
        # eyeballed off the composite:
        #
        #   COLUMN 41 IS GONE. His building's right edge is source x 1150, which is cel
        #   (1150-310)/20.5 = 40.98 - the very end of column 40. Column 41 was therefore
        #   100% his plaza, and it installed as an orange stripe down the right of the
        #   building. 41 rects, not 42.
        #
        #   THE BASE COMES IN TO 55.0, which ends the rect at cel 56.0. His own base is
        #   source y 1250 = cel 56.29, so 55.3 looked right and left three tenths of a row
        #   of his plaza under the building; the row below the base is a shadow we do not
        #   need. The first version ran to 56.85 and took three quarters of a row.
        #
        #   COLUMNS 0..3 DROP. The extrapolation along the roof's measured slope put them
        #   at 9.6, 9.1, 8.6, 8.1 and a wedge of his grass showed above the roof's left
        #   corner. His eave is at source (300, 470) = cel (-0.49, 18.2), so the true
        #   left slope is steeper than the one the colour test returned - it was reading
        #   the pink fascia behind the roof, not the roof. 12.2, 11.4, 10.3, 9.3.
        (0, 10.2, 0, 54), (1, 9.7, 1, 54), (2, 9.2, 2, 54), (3, 8.73, 3, 54), (4, 8.25, 4, 54),
        (5, 7.75, 5, 54), (6, 7.6, 6, 54), (7, 7, 7, 54), (8, 6.4, 8, 54), (9, 5.85, 9, 54),
        (10, 5.3, 10, 54), (11, 4.8, 11, 54), (12, 4.25, 12, 54), (13, 3.75, 13, 54),
        (14, 3.2, 14, 54), (15, 2.7, 15, 54), (16, 2.05, 16, 54), (17, 1.95, 17, 54),
        (18, 1.35, 18, 54), (19, 0.75, 19, 54), (20, 0.25, 20, 54), (21, 0.15, 21, 54),
        (22, 0.8, 22, 54), (23, 1.15, 23, 54), (24, 1.5, 24, 54), (25, 2.05, 25, 54),
        (26, 2.55, 26, 54), (27, 3.1, 27, 54), (28, 3.7, 28, 54), (29, 4.1, 29, 54),
        (30, 4.55, 30, 54), (31, 5, 31, 54), (32, 5.7, 32, 54), (33, 6.05, 33, 54),
        (34, 6.5, 34, 54), (35, 7.05, 35, 54), (36, 7.7, 36, 54), (37, 8.3, 37, 54),
        (38, 8.95, 38, 54), (39, 8.95, 39, 54), (40, 18.9, 40, 54),
        ],
    },
    matte_file="matte_840x1140.png",
    # HER TOWER STICKS OUT ABOVE AND BESIDE HIS PEDIMENT, and that is arithmetic rather
    # than carelessness. Her Bank is a round tower 18 cel px wide and 54 tall running
    # y 92..148; his building is 41.5 wide and 56 tall, and the door fixes where both of
    # them go. His roof is a TRIANGLE, so at its apex it is four columns wide and at
    # y 100 it covers only x 16.6..23.4 - her shaft is x 11..27 and shows on both sides
    # until the pediment reaches full width at about y 104. Above y 97 her dome is not
    # covered at all.
    #
    # Nothing can be done about that by moving or scaling: the scale is set by her leaf
    # body and the position by `DoorFor`. So it is the Factory's answer (section 42) -
    # `board_fill` diffuses the board's OWN pixels over the strip, no GPU and no invented
    # landscape. The ring sits on clean board above and beside the tower.
    # STOPPING AT x 24 IS NOT A ROUNDING, IT IS A NEIGHBOUR. The fill first ran to x 30
    # and the audit charged 106 stranded pixels to BARE NECESSITIES, because its region
    # starts at x 25 and our patch was writing inside it. It does not need to: her tower
    # runs x 11..27 and Bare Necessities' own building already covers x 25..30 down to
    # y 105, so the three columns we were fighting for are behind somebody else's wall.
    board_fill=[(10, 90, 24, 98, 2), (10, 96, 24, 105, 2)],
    scale=20,
    notes=("THE THIRTEENTH AND LAST board building. The doorway was placed FIRST and every "
           "other number follows from it - see BUILDINGS.md 53. HIS lettering is kept, as "
           "at Bun Believable: `Hyper Save` is drawn into his picture on its own fascia "
           "board and no sign of ours is stamped over it. The pole sign in his picture is "
           "dropped for want of room, not for taste. "
           "INSTALL `blacksmarket` BEFORE `bank`: the two regions overlap at "
           "x 25..41 y 97..105, each building's eraser is a rectangle covering its "
           "own region, and whichever goes last wins there. The Bank is nearer the "
           "viewer, so it goes last."),
)
