r"""Rebuild a building's animated door (view 751) into the cast0 overlay.

    python tools/door_cels.py --who factory --set cast0
    python tools/door_cels.py --who factory --set cast0 --dry

WHY THIS EXISTS
---------------
Zain: *"Can the factory door look like an actual factory door!? why is it just a space!?"*

Two separate faults, and the first one is not what it looked like.

1. **There was no doorway in the facade.** The building art was generated with a flat wall
    where the door cel lands, so with the door not animating there is nothing there at all.
    Fixed in `buildings.py` by building an entrance bay around the cel's own footprint.

2. **The door cel itself was still Sierra's 1990 art**, upscaled. `view_751_l5_c*` exists in
    `assets/png` and `assets/png12x` but NOT in `png12x-cast0`, and `RenderScale` takes the
    largest factor at which a file exists - so a cartoon door was being drawn over
    photoreal brick. This script fills that gap.

THE GEOMETRY IS HERS AND ONLY THE MATERIAL IS NEW
--------------------------------------------------
`AddCastView` draws at `(x - w/2, y + 1 - h)` - **bottom-centre**, not top-left
(`MainViewModel.Animation.cs:299-301`). Her four cels are a pair of doors swinging
outward, and the leaf width per cel measured off her own pixels is:

    cel 0   5 + 5   closed, meeting in the middle
    cel 1   4 + 4   parting, and the leaf feet appear in rows 9-10 as they swing out
    cel 2   3 + 3
    cel 3   1 + 1   fully open

**Her alpha is reused verbatim** (upscaled NEAREST), so the sprite's footprint on the board
is exactly the original's and nothing new can overhang. Her leaf/interior partition is
reused too, cleaned to the leftmost and rightmost contiguous runs so her dithering does not
punch holes in the new timber. Only the colours inside those masks are ours.

The script coordinates are NOT touched. They are script data and the scripts are the truth.
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "assets")
PNG1X = os.path.join(ASSETS, "png")

# (loop, x, y) straight out of MainViewModel.Animation.cs DoorFor, which cites room1.sc.
DOORS = {"factory": (5, 50, 181), "rentoffice": (1, 105, 29), "pawnshop": (12, 221, 34), "employment": (6, 97, 183),
         "security": (2, 29, 44), "zmart": (11, 282, 34), "monolith": (10, 288, 75),
         "university": (7, 220, 179), "socketcity": (8, 277, 183),
         "qtclothing": (9, 277, 113), "blacksmarket": (3, 46, 105)}

# Materials, keyed by building. Sampled to sit with that facade's own palette and light.
MATERIAL = {
    # RE-SAMPLED OFF THE INSTALLED FACADE, which is Zain's own picture and not the facade
    # this material was first written for. Measured on `png12x-cast0` at 1x:
    #
    #     his door leaf        #201D18      his stone lintel     #393025
    #     brick beside it      #27211A      the roller shutter   #262521
    #
    # The old values - a #4A3322 varnished leaf over a #8C4C28 warm interior - were authored
    # against the generated brick that his picture replaced, and they are two to three times
    # lighter than anything in the frame they are now drawn over. A door that glows against
    # its own wall is the same defect as a sign that reads as a sticker.
    #
    # PANELLED, NOT BOARDED. His is a flush painted personnel door beside a roller shutter,
    # with no planking and no iron straps on it; straps would make a barn door of it.
    # THE FACTORY, REBUILT AT THE CORRECTED FOOTPRINT. `CastDoors.cs` moves this door
    # (0,+11) for the `cast0` set, which puts the sprite on Zain's own personnel door for
    # the first time - so `from_facade` is finally the right tool here, exactly as it is for
    # the General Dealer, and the `panel` style below is no longer reached. It is kept
    # because it is the only flush panelled door in the table and the next supplied building
    # with one will want it.
    #
    # `head_rows` 2 keeps his STONE LINTEL, which sits at y 183..184 inside a footprint of
    # y 182..192: without it the opening swallows the lintel and the doorway loses its head.
    #
    # The opening is HER OWN loop 5 interior, measured off her cel 3 and in her own values:
    # #281818 over rows 0..1, #483838 over 2..3, #604848 over 4..5, #682028 over 6..7,
    # #984028 at row 8. Nothing is invented here - unlike the General Dealer's shop, which
    # Zain sanctioned, this door has her art to follow and so it follows it.
    "factory": {
        "from_facade": True,
        "head_rows": 2,
        "bands": [(2, 3, "in_mid"), (4, 5, "in_low"), (6, 7, "in_warm"), (8, 8, "in_floor")],
        "in_mid":   (0x48, 0x38, 0x38),   # hers
        "in_low":   (0x60, 0x48, 0x48),   # hers
        "in_warm":  (0x68, 0x20, 0x28),   # hers
        "in_floor": (0x98, 0x40, 0x28),   # hers
        "style": "panel",
        # the opening: a dark works interior with a little light reaching the floor. Nothing
        # bright - his building is in shade and the brightest thing at this door is the
        # #393025 lintel.
        "in_top":   (0x0A, 0x09, 0x08),
        "in_mid":   (0x16, 0x14, 0x12),
        "in_low":   (0x26, 0x20, 0x1A),
        "in_floor": (0x3A, 0x32, 0x2A),
        # the leaves: his own leaf value, lifted so it separates from the opening behind it.
        # The FIRST set of these was his #201D18 almost unchanged, and composited over the
        # board the shut cel read as a flat dark slab with one line in it - a hole, not a
        # door. The panel has to differ from the frame by enough to survive the 16x->12x
        # box mean, which here is about twenty levels.
        "leaf":     (0x2E, 0x2A, 0x24),
        "leaf_hi":  (0x5A, 0x50, 0x46),
        "leaf_lo":  (0x0E, 0x0C, 0x0A),
        "board":    (0x1A, 0x17, 0x13),
        "iron":     (0x15, 0x14, 0x12),
        "iron_hi":  (0x6A, 0x62, 0x56),
    },
    # Villa Locke & Keyes: a dark painted double door with brass furniture, opening onto a
    # warm lit lobby with a pale stone floor. NOT glazed - the facade's door is panelled,
    # and a glazed leaf here would contradict the art it is drawn over.
    "security": {
        "glazed": False,
        "leaf": (46, 50, 58), "leaf_hi": (96, 102, 112), "leaf_lo": (20, 22, 28),
        "board": (30, 32, 38),
        "iron": (28, 28, 30), "iron_hi": (170, 150, 90),      # brass furniture
        "pane": (70, 74, 82), "pane_hi": (142, 146, 152),
        "in_top": (28, 24, 22), "in_mid": (74, 64, 54),
        "in_low": (120, 102, 78), "in_floor": (186, 170, 140),
    },
    # THE GENERAL DEALER: a pair of SLIDING glass doors under the cap's visor, which is a
    # third thing again - not the Factory's planking, not Villa's panelled portal, not the
    # Rent Office's stile-and-rail shop door. Every value is sampled off the installed
    # facade at 12x, and they are all dark because the visor shadows the whole entrance:
    #
    #     the glass, upper (reflecting)  #454B40, p90 #6F7770
    #     the glass, lower (interior)    #2F3B34
    #     the centre mullion             #343A3C
    #     a door frame rail              #242F35
    #     the threshold strip            #21362B
    #
    # WHAT THE OPEN STATE SHOWS, decided rather than defaulted. A glazed door is not a hole
    # that appears: when the leaves slide apart the REFLECTIONS go with them, so the opening
    # is the interior seen directly - darker and flatter than the glass that was there, with
    # a pale floor strip at the threshold. Her own cels agree, which is the check: the
    # opening columns in her art drop from luminance ~200 to 63.
    "zmart": {
        "style": "slide",
        # THE LEAVES ARE THE FACADE'S OWN PIXELS, and that is the finding on this door.
        #
        # Painted as synthetic glazing they composited as a flat dark slab, twice, and the
        # reason is not the shading: the sprite is 25 cel px wide while his entrance is 18,
        # so it covers four columns of his FIXED glazing at each side - and that glazing
        # carries bright reflections of the car park and a white van. Any uniform glass
        # painted across the whole footprint is darker than what it covers at the edges and
        # lighter in the middle, so the cel reads as a patch the moment the door animates.
        #
        # So the leaf is taken from the installed board at the cel's own footprint. His
        # painted doors ARE the shut state - they were authored to this footprint - which
        # makes cel 0 identical to what is already there, so the animation starts and ends
        # with nothing changing. Only the OPENING is ours.
        "from_facade": True,
        "head_rows": 1,
        # THE OPENING IS WARM AND LIT, AND THAT IS HER CALL RATHER THAN MINE. The first set
        # of these was a cold grey-green - "the interior seen directly, with the reflections
        # slid away" - and composited it read as a hole punched in the building. Her own
        # loop 11 cel 3 says otherwise, and it is the only evidence available about what is
        # inside this shop: #281818 at the head, warming down through #483838 and #604848
        # to #682028, with a #984028 floor at the threshold. She drew a lit interior.
        "in_top":   (0x16, 0x12, 0x12),   # the head, deepest under the visor
        "in_mid":   (0x2C, 0x24, 0x22),
        "in_low":   (0x4C, 0x3C, 0x34),
        "in_floor": (0x8A, 0x72, 0x56),   # the lit floor inside
        # THE THRESHOLD, AND IT IS WHY THE OPENING SPILLED ONTO THE GRASS. Her door cel
        # covers y 24..34 and her Z-Mart's cel ends at y 31, so THREE ROWS OF THE DOOR ARE
        # BELOW THE BUILDING - hers, by design, exactly as the Pawn Shoppe's two rows are
        # (section 12). Our fit already puts the base on her base row, so this is not the
        # Factory's defect; what was wrong is that those three rows were painted as interior
        # floor, and on the regenerated board the ground there is LAWN, so the shop's floor
        # appeared to run out onto the grass.
        #
        # Her own cel 3 does not do that: its bottom rows carry #D0D8E0 and #687078 at the
        # right of the opening - PAVEMENT, not floor. His photograph agrees and says what
        # it is made of: paving at #64696E with a dark entrance mat at #404A55 laid on it.
        #
        # AND THEN THE GROUND CHANGED UNDER IT, WHICH IS WHY `threshold_rows` IS NOW 0.
        # The paragraph above is still true about WHERE those three rows are; what it got
        # wrong is that it is our business to paint them. It reasoned from what happened to
        # be on the board at y 32..34 that week - lawn - and painted paving over it. Bun
        # Believable then went in at (264,32) and the top of its giant B now occupies
        # x 283..294 of exactly those rows, so the General Dealer's invented pavement
        # composited as a grey slab floating over the burger shop's sign.
        #
        # `foot_rows` 3 is the honest rule and it cannot go stale: below the General Dealer
        # there is no General Dealer, so those rows stay facade and show whatever the board
        # has there - lawn today, a neighbour's sign tomorrow. Her cels are fully opaque
        # over all 11 rows in all four cels, so this is the only thing that stops the
        # opening reaching them. The pave/mat values are kept because they are measured off
        # his photograph and the next building with a real step will want them.
        "foot_rows": 3,
        "threshold_rows": 0,
        "pave":     (0x64, 0x69, 0x6E),   # his, measured
        "mat":      (0x40, 0x4A, 0x55),   # his entrance mat
        "pave_hi":  (0x8C, 0x90, 0x94),
        # HER BANDS, WITH HER EDGES. The opening was a smooth ramp between her end values
        # and at 12x that is a flat wash - "a god damn block" is his phrase for this shape.
        # Her own cel 3 is not a ramp: it is hard-edged bands, #281818 over rows 1..3,
        # #483838 over 4..5, #604848 over 6..7, and the warm step below. Three steps of
        # value ARE the structure at 25 x 11, and they are the only structure either source
        # can justify - HIS photograph has no interior in it to copy, because his doors
        # reflect the car park almost entirely (measured: behind the glass at the lower
        # centre is #353834 and at the upper centre #121F25, both reflections). Shelving, a
        # ceiling line or depth would be invented, so they are not drawn.
        "bands": [(1, 3, "in_top"), (4, 5, "in_mid"), (6, 7, "in_low")],
        # A LIT SHOP INTERIOR, INVENTED, AND SANCTIONED BY THE OWNER.
        #
        # Everything else in this programme is read off a source. This is not: neither his
        # photograph nor her cel contains an interior - his doors reflect the car park
        # (#353834 behind the glass at the lower centre, #121F25 at the upper) and her four
        # values are a ramp. It was put to Zain as a trade and he took it: *"yes give it a
        # lit shop interior."* That ruling is the whole provenance, and it covers the inside
        # of this opening ONLY - the facade, the threshold, the mat, her head rail and her
        # alpha are untouched, and there is no lettering anywhere in it (CLAUDE.md rule 1 is
        # not what was lifted).
        #
        # STRUCTURE AT 11 COLUMNS IS VALUE, NOT DETAIL - this building's own finding, two
        # rounds ago, and it governs here too. Four separated values arranged so the eye
        # reads depth: a dark ceiling, SHELVING either side (the vertical structure is what
        # makes it a shop rather than a hallway), a lit back wall between them for the eye
        # to land on through a seven-column gap, and a floor that lightens towards the
        # daylight at the door. Nothing is drawn that would need more than one cel pixel to
        # be legible.
        #
        # The rectangles are in CEL coordinates on the 25x11 cel, painted in order, and
        # they only cover x 7..17 because that is the widest her leaves ever open.
        "in_ceil":    (0x14, 0x10, 0x0E),
        "in_back":    (0x7A, 0x62, 0x48),   # the lit back wall - the brightest thing inside
        "in_shelf":   (0x50, 0x3E, 0x2E),   # shelf fronts catching that light
        "in_dark":    (0x20, 0x18, 0x14),   # the shadow between the shelves
        # The floor is COOLER and a little darker than the back wall, deliberately: painted
        # at the same warmth and value they merged into one tan mass and the wall stopped
        # being the thing the eye lands on.
        "in_fl_back": (0x4A, 0x42, 0x38),
        "in_fl_front": (0x6C, 0x62, 0x54),  # daylight spilling in at the threshold
        "in_back_hi": (0x96, 0x7C, 0x5C),   # the back wall where the ceiling lights hit it
        # Cel coordinates, fractional, painted in order. Only x 7..17 is ever revealed.
        "interior": [
            (7, 1, 17, 1.6, "in_ceil"),         # the ceiling, darkest
            (7, 1.6, 17, 5.6, "in_dark"),       # the shop, in shadow to start with
            (10, 1.8, 14, 4.8, "in_back"),      # the lit back wall down the aisle
            (10, 1.8, 14, 3.1, "in_back_hi"),   # brighter where the ceiling lights reach it
            (10, 4.8, 14, 5.6, "in_fl_back"),   # the floor at the back of the aisle
            (7, 5.6, 17, 7, "in_fl_front"),     # and at the door, where the daylight reaches
        ],
        # SHELVING, as a run of bands rather than three bars. Drawn as (x0, x1, y0, y1,
        # pitch, duty) per side: the first version was one band per cel row and it read as
        # three horizontal bars; at 0.7 of a cel px pitch it reads as a rack of shelves at
        # 12x, and at 1x it averages to a mid value, which is the right failure.
        "shelves": [(7, 9, 1.9, 5.6, 0.7, 0.5), (15, 17, 1.9, 5.6, 0.7, 0.5)],
        "leaf":     (0x3E, 0x45, 0x40),   # the glazing itself
        "leaf_hi":  (0x6E, 0x75, 0x6E),   # what it reflects, at its top
        "leaf_lo":  (0x1C, 0x22, 0x22),
        "board":    (0x2E, 0x38, 0x3C),   # the frame
        "iron":     (0x24, 0x2F, 0x35),
        "iron_hi":  (0x5A, 0x60, 0x58),
    },
    # BUN BELIEVABLE: a glazed aluminium double door in a shopfront, which is the THIRD
    # glazed door in the table and the first one Sierra also drew as glass. Her own loop 10
    # cel 0 is #D0D8E0 / #C0D0E0 panes with an #A0B0C8 mullion down the middle - she drew
    # the same thing his picture draws, so for once her sprite and our facade agree about
    # what the door is made of and the only question is where it sits.
    #
    # EVERY NUMBER BELOW IS OFF THE INSTALLED BOARD AT 12x, read as the median of each 12x
    # block so a dithered pixel cannot carry the sample. Rows at the door's own columns:
    #
    #     y 65   FDD34E  the cheese fringe above the entrance - not the door
    #     y 66   777880  the transom's top edge
    #     y 67   CAC8CB  the lit head rail
    #     y 68   71767E  the head
    #     y 69..74       the glazing, 82A6C4 / A7C4D8, with the pull handles at 4E5254
    #     y 75+  FCF4EA  HER cream path - the building has ended
    #
    # and across, at y 71: 78787F is his left jamb at x 284, the panes run x 285..292, and
    # 8D989E is the right jamb at x 293. The two handles are at x 288 and x 290 with the
    # aluminium meeting stile between them at x 289, so his door's centre line is x 289.
    "monolith": {
        "style": "slide",
        "from_facade": True,
        # TWO ROWS, which is his transom plus his head rail - y 67 and y 68 once the
        # offset below has moved the footprint. An opening that eats them takes the lit
        # #CAC8CB rail with it and the doorway stops reading as a doorway.
        "head_rows": 2,
        # AND TWO AT THE BOTTOM, which is her sprite's feet. Her cel 0 is transparent over
        # rows 8..9 and cels 1..3 put 11, 7 and 3 pixels there; on this facade those rows
        # are y 75..76, her own cream path, below the building entirely. The first build
        # painted them as interior and cel 1 ran a column of lit shop down onto the
        # pavement. `foot_rows` is `head_rows`' mirror and was added for this.
        "foot_rows": 2,
        # A LIT FAST-FOOD INTERIOR, INVENTED, ON THE GENERAL DEALER'S PRECEDENT AND NOT ON
        # A NEW SANCTION. The same two sources are empty for the same reasons: his picture
        # is a photograph of a shopfront whose glass reflects the street (measured behind
        # the panes: 82A6C4 at the centre, A7C4D8 at the top - sky, not a room), and her
        # cel 3 is the generic four-value ramp she gives every door loop in view 751 -
        # 281818, 483838, 604848, 682028, 984028, byte for byte the same ladder as loop 11.
        # So there is nothing to copy, and a flat slab is the known failure. What is lifted
        # is the inside of this opening ONLY. No lettering of any kind goes in it: a burger
        # shop's interior wants a menu board and that is exactly the thing CLAUDE.md rule 1
        # forbids, so the wall behind the counter is left as light and colour.
        #
        # STRUCTURE AT 8 COLUMNS BY 6 ROWS IS VALUE, NOT DETAIL. Three things, each one
        # cel pixel thick or more: a dark soffit, a warm lit back wall, and a COUNTER -
        # one pale horizontal band across the full width at two thirds height. The counter
        # is what makes it a food shop rather than the General Dealer's aisle; it is also
        # the only piece of furniture that survives being one pixel tall.
        "in_top":   (0x14, 0x10, 0x10),
        "in_mid":   (0x26, 0x1E, 0x1C),
        "in_low":   (0x44, 0x34, 0x2E),
        "in_floor": (0x6A, 0x5C, 0x50),
        "in_ceil":  (0x12, 0x0E, 0x0E),
        "in_dark":  (0x26, 0x1E, 0x1C),
        "in_back":  (0x74, 0x44, 0x34),   # the back wall, warm rather than brand-red
        "in_back_hi": (0x9C, 0x62, 0x46),  # where the ceiling lights reach it
        "in_shelf": (0x8E, 0x76, 0x58),   # the counter front, and the rack behind it
        "in_shelf_hi": (0xBE, 0xAC, 0x8C),  # the counter's lit top edge
        "in_fl_back": (0x3A, 0x30, 0x2C),
        "in_fl_front": (0x56, 0x4A, 0x42),  # daylight on the floor at the threshold
        # Her bands first, hard-edged, as her own cel 3 has them - a ramp between the end
        # values reads as a flat wash at 12x, which is the fault this whole door table
        # exists to avoid.
        "bands": [(2, 3, "in_top"), (4, 5, "in_mid"), (6, 7, "in_low")],
        # Cel coordinates on the 14x10 cel, painted in order. A rect runs from x0 to x1+1,
        # so the last column written is x1 and a fractional x1 ends part way into the NEXT
        # column - the same convention the General Dealer's uses, and getting it wrong is
        # what put the first version's floor two rows below the opening.
        #
        # Only cols 3..10 and rows 2..7 are ever revealed.
        "interior": [
            (3, 2, 10, 1.6, "in_ceil"),          # the soffit, 0.6 of a row
            (3, 2.6, 10, 7, "in_dark"),          # the room's own walls, in shadow
            # The lit wall is inset 0.45 of a column at EACH side, which is the dark return
            # that makes the opening read as a room rather than a painted panel. It has to
            # be written as x1 = 9.55 for an inset of 0.45, because the rect ends at x1+1 -
            # at x1 = 9.1 the left return was 0.4 wide and the right one 0.9, and that
            # lopsidedness is visible at 12x.
            (3.45, 2.7, 9.55, 4.3, "in_back"),     # the lit wall behind the counter
            (3.45, 2.7, 9.55, 2.6, "in_back_hi"),  # brighter under the ceiling lights
            (3, 5.3, 10, 4.65, "in_shelf_hi"),   # THE COUNTER's lit top edge
            (3, 5.65, 10, 5.5, "in_shelf"),      # and its front
            (3, 6.5, 10, 7, "in_fl_front"),      # the floor, towards the daylight
        ],
        # A RHYTHM ON THE BACK WALL, not a drawing on it. The first version left that wall
        # a flat rectangle and at 12x it read as a slab of colour - the same fault the
        # General Dealer's opening had before its shelving went in. This is the warming
        # rack behind the counter, as alternating value at a 0.6 cel px pitch; at 1x it
        # averages back to the wall's own tone, which is the right way for it to fail.
        # It is deliberately NOT a menu board: lettering is what CLAUDE.md rule 1 forbids.
        #
        # TWICE CORRECTED. Drawn in the counter's own pale tan across the whole wall it
        # read as a garage shutter - a hard light-dark-light-dark stack filling the
        # opening. It now sits in `in_rack`, which is one step off the wall's lit tone
        # rather than four, runs over the wall's upper half only, and is a quarter duty
        # instead of nearly half. The wall below it stays a wall.
        "in_rack": (0x8A, 0x5A, 0x42),
        "shelves": [(3.6, 8.9, 3.5, 4.6, 0.55, 0.3)],
        "board":    (0x78, 0x78, 0x7F),   # his jamb, which is what the leading stile is
        "leaf":     (0x8E, 0xAE, 0xC6),   # the glazing - unused while from_facade is on
        "leaf_hi":  (0xC4, 0xD6, 0xE4),
        "leaf_lo":  (0x60, 0x76, 0x88),
        "pane":     (0x82, 0xA6, 0xC4), "pane_hi": (0xA7, 0xC4, 0xD8),
        "iron":     (0x4E, 0x52, 0x54), "iron_hi": (0xA2, 0xA1, 0xA3),
    },
    # BARE NECESSITIES. Measured: her door's bottom row is 105 and the building's base row
    # is 105 - FLUSH, so this one needs no offset at all and has no entry in CastDoors.cs.
    # The earlier survey called it a placement defect on a dark/wall ratio of 1.68; looked
    # at, that ratio was reading a PALE entrance bay between two dark roller shutters as
    # "no opening". The sprite is on the shop's own entrance.
    #
    # The interior is hers and it is the same five values she uses behind every door on the
    # board: #281818, #483838, #604848, #682028, #984028 down to the floor.
    "blacksmarket": {
        "from_facade": True,
        "head_rows": 1,                  # her own head, #507098 on cel row 0
        "bands": [(1, 4, "in_top"), (5, 6, "in_mid"), (7, 8, "in_low"),
                  (9, 10, "in_warm"), (11, 11, "in_floor")],
        "in_top":   (0x28, 0x18, 0x18), "in_mid": (0x48, 0x38, 0x38),
        "in_low":   (0x60, 0x48, 0x48), "in_warm": (0x68, 0x20, 0x28),
        "in_floor": (0x98, 0x40, 0x28),
    },
    # WATT A BARGAIN, and this one does NOT come out right - see the note in BUILDINGS.md.
    # Her door's bottom row is 183 and the building's base row is 183, so again no offset:
    # what is wrong is a HEIGHT mismatch no offset can fix. Her door is 14 cel px tall and
    # our generated showroom puts its ground floor in the last THREE rows, y 181..183; the
    # eleven rows above it are a rounded display window. `head_rows` 11 confines the opening
    # to the entrance band so the window survives, which is the best this facade can do.
    # AND HER INTERIOR VALUES ARE WRONG FOR THIS ONE, which is a finding rather than a
    # setting. Painted with the #682028 / #984028 she uses behind every other door, the
    # opening came back as a MAROON BLOCK in a showroom whose own lit band measures
    # #8E7458: her palette is a 1990 interior and this facade is a photoreal one. The
    # values here are the showroom's own, taken a little darker for the recess - which
    # makes the opening correct and very nearly invisible, and that is the honest state of
    # this door. See BUILDINGS.md: the real fix is a facade with an entrance in it.
    "socketcity": {
        "from_facade": True,
        "head_rows": 11,
        "bands": [(11, 11, "in_warm"), (12, 13, "in_floor")],
        "in_warm":  (0x5E, 0x4C, 0x38),   # the showroom's #8E7458, in shadow
        "in_floor": (0x74, 0x5E, 0x46),
    },
    "rentoffice": {
        # A GLAZED SHOP DOOR, not a boarded one. The Factory's treatment would put vertical
        # planking and iron straps on a letting agency's entrance.
        "glazed": True,
        "in_top":   (0x18, 0x16, 0x1A),
        "in_mid":   (0x3C, 0x38, 0x3E),
        "in_low":   (0x6A, 0x56, 0x3E),
        "in_floor": (0xC8, 0xA0, 0x68),
        "leaf":     (0x2A, 0x24, 0x26),   # the dark frame
        "leaf_hi":  (0x6A, 0x62, 0x62),
        "leaf_lo":  (0x12, 0x10, 0x12),
        "board":    (0x20, 0x1C, 0x1E),
        "iron":     (0x1E, 0x1C, 0x1C),
        "iron_hi":  (0x9A, 0x96, 0x92),
        "pane":     (0x46, 0x4A, 0x52),   # what shows through the leaf's own glass
        "pane_hi":  (0x8E, 0x92, 0x98),
    },
}

SUPER = 4          # build at 4x the target rung, then box down: clean antialiasing

# WHICH LocationId EACH KEY IS, for reading the art set's own door offsets.
LOCATION = {"factory": "Factory", "rentoffice": "RentOffice", "pawnshop": "PawnShop",
            "employment": "EmploymentOffice", "security": "SecurityApartments",
            "zmart": "ZMart", "university": "HiTechU", "socketcity": "SocketCity",
            "qtclothing": "QtClothing", "blacksmarket": "BlacksMarket",
            "monolith": "MonolithBurgers"}


def cast_offset(set_name, who):
    """The offset THIS ART SET declares for this building's door, read out of CastDoors.cs.

    ONE SOURCE OF TRUTH, DELIBERATELY. The offset decides where these cels are generated -
    `from_facade` crops the shut state from the board at the footprint, so a Python copy of
    the number that drifted from the C# one would paste the wrong part of the building in
    as the door and nothing downstream could tell. So it is parsed from the file the game
    reads, and a building with no entry gets (0,0), which is what the game does too.
    """
    import re
    p = os.path.join(ROOT, "src", "Jones.App", "Jones.App", "CastDoors.cs")
    if not os.path.exists(p):
        print("  no CastDoors.cs - every offset is zero, as the game's would be")
        return 0, 0
    src = open(p, encoding="utf-8-sig").read()
    m = re.search(r'"%s"\s*=>\s*id switch\s*\{(.*?)\n\s*\},' % re.escape(set_name), src, re.S)
    if not m:
        return 0, 0
    loc = LOCATION.get(who)
    hit = re.search(r"LocationId\.%s\s*=>\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)" % loc,
                    m.group(1)) if loc else None
    return (int(hit.group(1)), int(hit.group(2))) if hit else (0, 0)


def her_cels(loop):
    out = []
    for c in range(4):
        p = os.path.join(PNG1X, "view_751_l%d_c%d.png" % (loop, c))
        out.append(np.asarray(Image.open(p).convert("RGBA")).astype(int))
    return out


# Leaf width per cel, in cel pixels, MEASURED off her own art at mid height and then
# regularised. Her raw per-pixel mask is heavily dithered - reading it literally gives
# ragged fragments rather than doors, which is exactly what the first attempt produced.
# The measurement is the useful part of her art; the dithering is not.
#
# AND THE HINGING DIFFERS PER BUILDING. The Factory's loop 5 is a pair meeting in the
# middle; the Rent Office's loop 1 is a SINGLE leaf hinged on the left with the interior
# revealed to its right. Assuming the Factory's arrangement everywhere would have drawn
# the Rent Office a mirrored second door that does not exist in her animation.
#
#     factory     loop 5   5+5  4+4  3+3  1+1   two leaves, meeting
#     rentoffice  loop 1   7    4    3    1     one leaf, hinged left
LEAF = {
    # `opening_cols` is HIS doorway inside her footprint: the cel covers game x 45..54 and
    # his personnel door measures x 47..51 off the board at 12x, so cel columns 2..6.
    "factory":   {"hinge": "both", "w": [5, 4, 3, 1], "opening_cols": (2, 6)},
    # BUN BELIEVABLE, and this one did not need regularising at all: her loop 10 cels are
    # fully opaque over the leaf body, so the alpha says nothing, but the LUMINANCE says
    # everything. Column means over rows 0..7, her own art:
    #
    #     cel 0   171 193 222 214 204 201 197 176 206 201 201 203 211 159   no dark at all
    #     cel 1   ...                     63  63                            cols 6..7
    #     cel 2   ...         63 63 63 63 63  63 143                        cols 4..9
    #     cel 3   ...  63 63 63 63 63 63 63  63  63  63                     cols 2..11
    #
    # 63 is her interior. So the leaf width per cel is 7, 6, 4, 2 counted from each side,
    # read rather than chosen - a pair of glazed leaves parting from the centre.
    "monolith":  {"hinge": "both", "w": [7, 6, 4, 2], "opening_cols": (3, 10)},
    "rentoffice": {"hinge": "left", "w": [7, 4, 3, 1]},
    # MEASURED, NOT ASSUMED, exactly as the note above requires. Her loop 2 cels are 12x12
    # and fully opaque, so the leaves cannot be read from the alpha at all - the arrangement
    # is in the LUMINANCE. The revealed interior is dark and it grows from the centre:
    #
    #     cel 0   no dark columns at all        closed, a pair meeting in the middle
    #     cel 1   dark x 4..7                   leaves 4 + 4
    #     cel 2   dark x 3..8                   leaves 3 + 3
    #     cel 3   dark x 2..9                   leaves 2 + 2
    #
    # So: hinged both, and never fully open - two cel pixels of leaf remain at each jamb.
    "security":  {"hinge": "both", "w": [6, 4, 3, 2]},
    # THE GENERAL DEALER, measured the same way and it is a SLIDING pair. Her loop 11 cels
    # are 25x11 and fully opaque, so the arrangement is in the luminance again, and the
    # opening grows symmetrically about column 12:
    #
    #     cel 0   no column below luminance 70      shut
    #     cel 1   column 12                         leaves 12 + 12
    #     cel 2   columns  9..15                    leaves  9 +  9
    #     cel 3   columns  7..17                    leaves  7 +  7
    #
    # A sliding leaf does not narrow in life, it slides behind the fixed glazing beside it;
    # at 25 cel px the two are the same picture, and this is HER picture.
    "zmart":     {"hinge": "both", "w": [13, 12, 9, 7]},
    # Both measured off her own cels the same way - the dark columns of each cel, which is
    # where the leaves are not:
    #     loop 3  cel 0 none, cel 1 x 5..8, cel 2 x 4..9, cel 3 x 3..10   -> 7 / 5 / 4 / 3
    #     loop 8  cel 0 x 5,   cel 1 x 4..7, cel 2 x 4..9, cel 3 x 2..11  -> 7 / 4 / 4 / 2
    "blacksmarket": {"hinge": "both", "w": [7, 5, 4, 3]},
    "socketcity":   {"hinge": "both", "w": [7, 4, 4, 2]},
}


def partition(a, cel, who):
    """(alpha, leaf). Her ALPHA verbatim; the leaf geometry parametric, from LEAF."""
    al = a[..., 3]
    h, w = al.shape
    alpha = al > 0
    spec = LEAF[who]
    k = spec["w"][cel]
    x = np.arange(w)[None, :]
    if spec["hinge"] == "both":
        leaf = ((x < k) | (x >= w - k)) & alpha
    else:
        leaf = (x < k) & alpha
    # AN OPENING MAY NOT BE WIDER THAN THE DOORWAY THE FACADE PAINTS. Her sprite is sized
    # to HER building: the Factory's is 10 cel px wide and Zain's personnel door is 6, so
    # her 1+1 at cel 3 opens a hole two columns wider than the door on each side and eats
    # his brick jambs. Her leaf widths still drive the timing - how much opens, and when -
    # and this only stops the opening crossing the reveal. Same class as `head_rows`
    # keeping his lintel.
    cols = spec.get("opening_cols")
    if cols:
        lo, hi = cols
        leaf = leaf | (((x < lo) | (x > hi)) & alpha)
    return alpha, leaf


def build_cel(a, mat, S, cel, who):
    """One cel at S px per cel pixel, from her alpha and the measured leaf geometry."""
    alpha, leaf = partition(a, cel, who)
    h, w = alpha.shape
    W, H = w * S, h * S
    A = np.kron(alpha, np.ones((S, S), bool))
    L = np.kron(leaf, np.ones((S, S), bool))

    img = Image.new("RGB", (W, H), mat.get("in_mid", mat.get("in_warm", (0, 0, 0))))
    d = ImageDraw.Draw(img)
    # HER OWN BANDS, WHERE A BUILDING DECLARES THEM - hard-edged, in cel rows, as she drew
    # them. A ramp between the same end values reads as a flat wash at 12x.
    if mat.get("bands"):
        for (r0, r1, key) in mat["bands"]:
            d.rectangle([0, r0 * S, W, (r1 + 1) * S - 1], fill=mat[key])
    else:
        # THE OPENING: four bands down the height, so the eye reads depth rather than a hole.
        for y0, y1, c0, c1 in ((0.00, 0.30, "in_top", "in_mid"),
                               (0.30, 0.68, "in_mid", "in_low"),
                               (0.68, 0.88, "in_low", "in_floor"),
                               (0.88, 1.00, "in_floor", "in_floor")):
            a0, a1 = np.array(mat[c0], float), np.array(mat[c1], float)
            for yy in range(int(y0 * H), max(int(y0 * H) + 1, int(y1 * H))):
                t = (yy - y0 * H) / max(1.0, (y1 - y0) * H)
                d.line([(0, yy), (W, yy)], fill=tuple((a0 * (1 - t) + a1 * t).astype(int)))

    # THE INTERIOR, where a building has one. Painted over the bands, so her ramp is still
    # underneath it and still shows at the edges of the opening - the interior sits INSIDE
    # her depth rather than replacing it. It is ONE fixed scene: the leaves are composited
    # over it afterwards, so cels 1, 2 and 3 reveal more of the same shop rather than being
    # three drawings of it.
    for (rx0, ry0, rx1, ry1, key) in mat.get("interior", []):
        d.rectangle([int(rx0 * S), int(ry0 * S), int((rx1 + 1) * S) - 1,
                     int((ry1 + 1) * S) - 1], fill=mat[key])
    for (sx0, sx1, sy0, sy1, pitch, duty) in mat.get("shelves", []):
        y = sy0
        while y < sy1:
            # `in_rack` where a building separates the rhythm from the shelf fronts. The
            # General Dealer's run IS its shelf fronts and wants one colour for both; Bun
            # Believable's run is a warming rack standing against a lit wall, and painted
            # in the counter's pale tan it came out as a row of louvres across the whole
            # back of the shop. A rhythm reads as texture only while it stays near the
            # value it is drawn on.
            d.rectangle([int(sx0 * S), int(y * S), int((sx1 + 1) * S) - 1,
                         int((y + pitch * duty) * S) - 1],
                        fill=mat.get("in_rack", mat["in_shelf"]))
            y += pitch

    # THE THRESHOLD, where the cel hangs below the building. Painted over the opening's own
    # gradient, because what is down there is not the shop's floor: it is the paving outside
    # the door with its mat on it. See the note in MATERIAL.
    tr = int(mat.get("threshold_rows", 0))
    if tr:
        t0 = H - tr * S
        d.rectangle([0, t0, W, H], fill=mat["pave"])
        d.rectangle([0, t0, W, t0 + max(1, S // 3)], fill=mat["pave_hi"])   # its lit edge
        d.rectangle([int(W * 0.18), t0 + int(tr * S * 0.42), int(W * 0.82), H - max(1, S // 3)],
                    fill=mat["mat"])

    # THE LEAVES, painted over the opening and then masked back to her leaf shape.
    #
    # NOT DRAWN AT ALL when the building takes its leaves `from_facade`: the composite at
    # the rung replaces every one of these pixels with the board's own, so painting them
    # here is work thrown away - and it is work that needs a `leaf` colour, which a facade
    # material has no reason to carry. Three buildings hit this as a KeyError.
    if mat.get("from_facade"):
        rgba = np.dstack([np.asarray(img).astype(np.uint8),
                          np.where(A, 255, 0).astype(np.uint8)])
        return Image.fromarray(rgba, "RGBA")

    leaves = Image.new("RGB", (W, H), mat["leaf"])
    ld = ImageDraw.Draw(leaves)
    cols = np.nonzero(leaf.any(0))[0]
    if len(cols) and mat.get("glazed"):
        # A STILE-AND-RAIL DOOR, DRAWN PER LEAF. The first version drew one pane across the
        # whole canvas and then masked it to the leaf, which for a SINGLE leaf hinged left
        # left a flat slice of grey with no frame, no glass and no handle in it - "a block"
        # rather than a door. The geometry has to be relative to each leaf run.
        runs, c0 = [], None
        present = leaf.any(0)
        for cx in range(leaf.shape[1] + 1):
            on = cx < leaf.shape[1] and present[cx]
            if on and c0 is None:
                c0 = cx
            elif not on and c0 is not None:
                runs.append((c0 * S, (cx * S) - 1))
                c0 = None
        for (rx0, rx1) in runs:
            rw = rx1 - rx0 + 1
            stile = max(2, int(rw * 0.20))
            top, bot = int(0.13 * H), int(0.70 * H)
            # the glazed panel, with the shop interior showing through it
            for yy in range(top, bot):
                t = (yy - top) / max(1.0, bot - top)
                c = tuple(int(a * (1 - t) + b * t) for a, b in
                          zip(mat["in_top"], mat["in_low"]))
                ld.line([(rx0 + stile, yy), (rx1 - stile, yy)], fill=c)
            # a reflection across the top of the glass, so it reads as glass not a hole
            ld.rectangle([rx0 + stile, top, rx1 - stile, top + max(2, S // 3)],
                         fill=mat["pane_hi"])
            # frame: stiles either side, a head rail and a mid rail under the glass
            ld.rectangle([rx0, 0, rx0 + stile, H], fill=mat["leaf_hi"])
            ld.rectangle([rx1 - stile, 0, rx1, H], fill=mat["leaf_lo"])
            ld.rectangle([rx0, 0, rx1, top], fill=mat["leaf"])
            ld.rectangle([rx0, bot, rx1, bot + max(3, int(0.10 * H))], fill=mat["leaf"])
            # and a handle on the opening edge, which is the cheapest thing that says DOOR
            hx = rx1 - stile - max(2, S // 2)
            ld.rectangle([hx, int(0.42 * H), hx + max(2, S // 4), int(0.56 * H)],
                         fill=mat["iron_hi"])
    elif len(cols) and mat.get("style") == "slide":
        # A SLIDING GLAZED LEAF: a slim frame all round, glass filling it, and nothing else.
        # No handle and no mid rail - an automatic sliding door has neither, and at 25 cel
        # px either would be a smear. The glass carries a vertical fall-off because that is
        # what the facade's own glazing does: it reflects the sky and the car park at the
        # top and shows the interior lower down, #454B40 against #2F3B34 measured.
        #
        # AND IT IS DARKEST AT THE HEAD, because the cap's visor shadows the entrance in
        # his photograph. A leaf lit evenly would fight that shadow, and the shadow is part
        # of why the building reads as a cap.
        runs, c0 = [], None
        present = leaf.any(0)
        for cx in range(leaf.shape[1] + 1):
            on = cx < leaf.shape[1] and present[cx]
            if on and c0 is None:
                c0 = cx
            elif not on and c0 is not None:
                runs.append((c0 * S, (cx * S) - 1))
                c0 = None
        hi = np.array(mat["leaf_hi"], float)
        lo = np.array(mat["leaf_lo"], float)
        glass = np.array(mat["leaf"], float)
        for (rx0, rx1) in runs:
            rw = rx1 - rx0 + 1
            if rw < 2:
                continue
            # THE REFLECTION IS THE BRIGHT PART AND THE FIRST VERSION KILLED IT. A 0.62
            # multiply over the top third - meant as the visor's shade - took the glass to
            # about #2B2E29, which is the opening's own value, so the whole cel composited
            # as one dark slab and the leaves could not be told from the gap. The facade's
            # glass measures #454B40 with a p90 of #6F7770 and it is the LIGHTEST thing at
            # this entrance; the shade belongs on the head alone.
            for yy in range(H):
                t = yy / max(1.0, H - 1.0)
                if t < 0.34:                       # sky and car park, reflected
                    k = 1.0 - t / 0.34
                    c = glass * (1 - 0.30 * k) + hi * (0.30 * k)
                else:                              # the interior, through the glass
                    u = (t - 0.34) / 0.66
                    c = glass * (1 - u) + lo * u
                ld.line([(rx0, yy), (rx1, yy)], fill=tuple(np.clip(c, 0, 255).astype(int)))
            fr = max(1, int(round(S * 0.10)))          # the frame, about a tenth of a cel
            ld.rectangle([rx0, 0, rx0 + fr, H], fill=mat["board"])
            ld.rectangle([rx1 - fr, 0, rx1, H], fill=mat["board"])
            ld.rectangle([rx0, 0, rx1, fr], fill=mat["iron"])
            ld.rectangle([rx0, H - max(2, int(S * 0.22)), rx1, H], fill=mat["board"])
            # THE VISOR'S SHADE, and only on the head. The cap throws it across the top of
            # the entrance in his photograph and it is part of why the building reads as a
            # cap, so the door must not be lit through it - but it is two rows deep, not a
            # third of the leaf.
            sh_h = max(2, int(S * 0.18))
            for yy in range(sh_h):
                k = 0.72 + 0.28 * (yy / float(sh_h))
                row = [tuple(int(v * k) for v in leaves.getpixel((xx, yy)))
                       for xx in range(rx0, min(rx1 + 1, W))]
                for i, cpx in enumerate(row):
                    ld.point((rx0 + i, yy), fill=cpx)
            # one specular streak across the upper glass, so it reads as glass and not a
            # painted panel
            ld.rectangle([rx0 + fr, int(0.16 * H), rx1 - fr, int(0.16 * H) + max(1, S // 6)],
                         fill=tuple(int(v) for v in (glass * 0.45 + hi * 0.55)))
    elif len(cols) and mat.get("style") == "panel":
        # A FLUSH PANELLED DOOR, drawn per leaf run like the glazed one. Two sunk panels in
        # a stile-and-rail frame, a lit hanging edge and a plain lever handle - which is what
        # a painted personnel door in a brick wall is, and what Zain's own picture shows at
        # this building. No planking, no straps.
        runs, c0 = [], None
        present = leaf.any(0)
        for cx in range(leaf.shape[1] + 1):
            on = cx < leaf.shape[1] and present[cx]
            if on and c0 is None:
                c0 = cx
            elif not on and c0 is not None:
                runs.append((c0 * S, (cx * S) - 1))
                c0 = None
        for (rx0, rx1) in runs:
            rw = rx1 - rx0 + 1
            if rw < 3:
                continue
            stile = max(1, int(rw * 0.22))
            for (py0, py1) in ((0.10, 0.44), (0.52, 0.88)):
                ld.rectangle([rx0 + stile, int(py0 * H), rx1 - stile, int(py1 * H)],
                             fill=mat["board"])
                # the panel's own lit top and left arris, one device pixel of relief
                ld.rectangle([rx0 + stile, int(py0 * H), rx1 - stile, int(py0 * H) + max(1, S // 6)],
                             fill=mat["leaf_lo"])
                ld.rectangle([rx0 + stile, int(py1 * H) - max(1, S // 6), rx1 - stile, int(py1 * H)],
                             fill=mat["leaf_hi"])
            ld.rectangle([rx0, 0, rx0 + max(1, S // 4), H], fill=mat["leaf_hi"])
            ld.rectangle([rx1 - max(1, S // 4), 0, rx1, H], fill=mat["leaf_lo"])
            hx = rx1 - stile - max(2, S // 2)
            ld.rectangle([hx, int(0.46 * H), hx + max(2, S // 5), int(0.54 * H)],
                         fill=mat["iron_hi"])
    elif len(cols):
        # vertical boarding, two boards per cel pixel of leaf width, plus a lit left edge
        step = max(2, S // 2)
        for x in range(0, W, step):
            ld.line([(x, 0), (x, H)], fill=mat["board"], width=max(1, S // 8))
            ld.line([(x + 1, 0), (x + 1, H)], fill=mat["leaf_hi"], width=max(1, S // 12))
        # iron straps across each leaf
        for fy in (0.18, 0.62):
            y = int(fy * H)
            ld.rectangle([0, y, W, y + max(2, S // 3)], fill=mat["iron"])
            ld.rectangle([0, y, W, y + max(1, S // 8)], fill=mat["iron_hi"])
    leaves = leaves.filter(ImageFilter.GaussianBlur(S * 0.12))

    out = np.asarray(img).astype(np.uint8).copy()
    out[L] = np.asarray(leaves).astype(np.uint8)[L]

    # the reveal: the opening is darker right where a leaf shadows it
    sh = np.zeros((H, W), np.float32)
    sh[~L & A] = 1.0
    shimg = Image.fromarray((sh * 255).astype(np.uint8), "L").filter(
        ImageFilter.GaussianBlur(S * 0.5))
    k = 1.0 - (np.asarray(shimg, np.float32) / 255.0) * 0.0
    out = np.clip(out.astype(np.float32) * k[..., None], 0, 255).astype(np.uint8)

    rgba = np.dstack([out, np.where(A, 255, 0).astype(np.uint8)])
    return Image.fromarray(rgba, "RGBA")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--who", required=True, choices=sorted(DOORS))
    ap.add_argument("--set", default="cast0")
    ap.add_argument("--factors", default="4,12")
    ap.add_argument("--dry", action="store_true")
    a = ap.parse_args()

    loop, dx, dy = DOORS[a.who]
    mat = MATERIAL.get(a.who)
    if mat is None:
        raise SystemExit("%s has no door material yet - add one to MATERIAL" % a.who)
    cels = her_cels(loop)
    h, w = cels[0].shape[:2]
    print("%s: view 751 loop %d at (%d,%d), cel %dx%d" % (a.who, loop, dx, dy, w, h))
    ox, oy = cast_offset(a.set, a.who)
    if ox or oy:
        print("  the '%s' art set moves this door by (%+d,%+d) - CastDoors.cs, which is "
              "where the GAME reads it too" % (a.set, ox, oy))
        dx, dy = dx + ox, dy + oy
    x0, y0 = dx - w // 2, dy + 1 - h
    print("  AddCastView draws at (x - w/2, y + 1 - h), so it covers game x %d..%d y %d..%d"
          % (x0, x0 + w - 1, y0, y0 + h - 1))
    for c, im in enumerate(cels):
        _, lf = partition(im, c, a.who)
        print("  cel %d: leaf %d px, %d+%d wide at mid height"
              % (c, int(lf.sum()), int(lf[5, :w // 2].sum()), int(lf[5, w // 2:].sum())))

    for F in (int(s) for s in a.factors.split(",")):
        out = os.path.join(ASSETS, "png%dx-%s" % (F, a.set))
        os.makedirs(out, exist_ok=True)
        facade = None
        if mat.get("from_facade"):
            bp = os.path.join(out, "board.png")
            if not os.path.exists(bp):
                raise SystemExit("%s takes its leaves from the facade, but %s does not "
                                 "exist - install the building first" % (a.who, bp))
            facade = Image.open(bp).convert("RGB").crop(
                (x0 * F, y0 * F, (x0 + w) * F, (y0 + h) * F))
            print("  leaves from the installed facade at %dx, %dx%d px - no resampling"
                  % (F, facade.width, facade.height))
        for c, im in enumerate(cels):
            big = build_cel(im, mat, F * SUPER, c, a.who)
            small = big.resize((w * F, h * F), Image.BOX)
            if facade is not None:
                # AT THE RUNG, NOT AT THE SUPERSAMPLE. Pasting the facade into the 4x
                # canvas and boxing it down would soften it, and a shut door one blur
                # softer than the wall it sits on is exactly the "sticker" fault this
                # project keeps finding. Here it is pixel for pixel.
                _, lf = partition(im, c, a.who)
                # HER HEAD RAIL IS KEPT. Her own cels open from row 1 down and leave row 0
                # as the door's head across the full width - which on this facade is his
                # transom and head rail, and an opening that swallows them reads as a hole
                # rather than as a doorway.
                hr = int(mat.get("head_rows", 0))
                fr = int(mat.get("foot_rows", 0))
                if hr or fr:
                    lf = lf.copy()
                    if hr:
                        lf[:hr, :] = True
                    # HER FEET ARE NOT AN OPENING. `head_rows`' mirror, and Bun Believable
                    # is the case that needed it: her loop 10 sprite is ten rows and the
                    # bottom two hold only the swinging feet - empty in cel 0, a handful of
                    # pixels in 1..3 - which on this facade fall on her cream PATH, below
                    # the building entirely. Painted as interior they ran the lit shop out
                    # of the doorway and down onto the pavement in cel 1. Kept as facade
                    # they are her own ground, which is what is actually down there.
                    if fr:
                        lf[-fr:, :] = True
                lm = Image.fromarray(
                    (np.kron(lf, np.ones((F, F), np.uint8)) * 255).astype(np.uint8), "L")
                small = Image.composite(facade, small, lm)
                # the leading stile, so the edge of a sliding leaf reads as an edge
                d2 = ImageDraw.Draw(small)
                runs, c0 = [], None
                pres = lf.any(0)
                for cx in range(w + 1):
                    on = cx < w and pres[cx]
                    if on and c0 is None:
                        c0 = cx
                    elif not on and c0 is not None:
                        runs.append((c0, cx - 1))
                        c0 = None
                # THE STILE ONLY EXISTS WHERE THE DOOR OPENS. Drawn over the full height it
                # ran a vertical line across Watt a Bargain's display WINDOW, eleven rows
                # above the opening - `head_rows` keeps those rows as facade and the stile
                # has to respect the same bound.
                sy = int(mat.get("head_rows", 0)) * F
                ey = (h - int(mat.get("foot_rows", 0))) * F
                for (a0, a1) in runs:
                    if a0 > 0:
                        d2.rectangle([a0 * F, sy, a0 * F + max(1, F // 8), ey],
                                     fill=mat.get("board", mat.get("in_warm", (0, 0, 0))))
                    if a1 < w - 1:
                        d2.rectangle([(a1 + 1) * F - max(1, F // 8), sy, (a1 + 1) * F - 1,
                                      ey],
                                     fill=mat.get("board", mat.get("in_warm", (0, 0, 0))))
            # alpha stays a HARD mask at the rung, so the sprite footprint is exactly hers
            hard = Image.fromarray(
                np.kron(np.asarray(Image.fromarray(
                    (im[..., 3] > 0).astype(np.uint8) * 255, "L")), np.ones((F, F), np.uint8)),
                "L")
            small.putalpha(hard)
            p = os.path.join(out, "view_751_l%d_c%d.png" % (loop, c))
            if not a.dry:
                small.save(p)
            print("  %s %s %s" % ("would write" if a.dry else "wrote",
                                  os.path.basename(p), small.size))


if __name__ == "__main__":
    main()

