"""Generates a PHOTO-REAL original cast for the twenty walker views, and screens it.

WHAT THIS IS
------------
tools/gen_walkers.py designed a cast and the result was rejected: deformed hands and feet,
figures that read as seated, white matte chips, no coherence as a set. The concept survived
that; the execution did not. This is the second attempt, and every difference from the
first is aimed at one of the five named failures.

  1. POSES ARE AUTHORED, NOT MEASURED.  tools/pose_author.py. The old skeleton was read out
     of a 39x95 alpha mask, which does not carry a pose - foot spread across the four cels
     of view 282 measures 15, 16, 17, 17 pixels, which is noise. Seated figures were the
     symptom. Nothing here reads a limb from the original.

  2. GENERATED AS LARGE AS THE CARD ALLOWS, WHICH IS NOT AS LARGE AS ASKED.  Measured on
     the RTX A500, 4GB, with --offload: 512x1024 runs without attention slicing at 2.83GB
     peak; 576x1152 and 640x1280 need slicing and peak at 3.17 and 3.46GB; 640x1280 without
     slicing, and 768x1536 either way, crash the driver outright (0xC000070A). So the base
     is 640x1280 and the brief's 768x1536 is not available on this hardware.
     THE HANDS AND FEET GET THEIR RESOLUTION BACK A DIFFERENT WAY: after the body is
     generated, the head, each hand and the feet are re-generated as 512x512 crops. 512 is
     SD1.5's native training resolution and a hand that fills a third of a 512 frame is a
     completely different problem from a hand that is sixty pixels tall in a 1280 one. This
     is the standard fix and it is where most of the quality in this file comes from.

  3. FAILURES ARE REJECTED.  `screen()` below. Geometric, not semantic - see its docstring
     for exactly what it can and cannot catch, which is stated honestly because a screen
     that is trusted for more than it does is worse than none.

  4. THE MATTE IS CUT FROM THE GENERATED FIGURE.  Not from the 1990 silhouette plus a band,
     which is what left white chips wherever the model painted something touching the
     figure. `cutout()` floods the background from the border and keeps the one connected
     component the person is made of. The original's outline is used for exactly one thing
     downstream - deciding where the figure lands in the cel - and for nothing about its
     shape.

  5. IDENTITY IS PINNED.  One seed per character, one prompt per character with only the
     outfit clause varying, and cels 1-3 img2img from the character's own cel 0. Measured
     by tools/fit_walkers.py --measure, whose `boil` is the number that matters.

THE CAST
--------
New people. Not reconstructions: at a 14x14-pixel head there is no facial identity in the
source to preserve, and the brief says so. What IS carried over is what the slot actually
shows, and that was measured rather than invented - the dominant colour of each figure's
hair, face, torso, legs and feet band in every one of the twenty views. So view 282 is a
white top over blue jeans with brown shoes because that is what view 282 measures; view 285
is a bright teal full-length dress because the torso and the legs of view 285 are both
(2,175,227); view 296 is a cream blouse over a tan skirt on an auburn-haired woman because
that is what is in the pixels. The wardrobe, the grade, the build and the broad colouring
come across. The faces are new.

USAGE
    python tools/gen_cast.py --poses <dir> --out <dir> [--views 282,286] [--dry]
    python tools/gen_cast.py --poses <dir> --gen <dir> --cels <dir> --factor 12
"""

import argparse
import ctypes
import json
import os
import subprocess
import sys
import time

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

import fit_walkers
import gen_walkers
import pose_author

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PNG1X = os.path.join(ROOT, "assets", "png")

# WINDOWS ERROR REPORTING MUST NOT OPEN A DIALOG. The resolution probe that established the
# 640x1280 ceiling crashed the CUDA driver twice, by design, and each crash raised a modal
# just-in-time debugger box on a machine that was being used for something else. An
# unattended run cannot block on a dialog. 0x8003 is SEM_FAILCRITICALERRORS |
# SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX.
if sys.platform == "win32":
    ctypes.windll.kernel32.SetErrorMode(0x8003)

# ---------------------------------------------------------------------------
# PROMPTS
#
# CLIP TAKES 77 TOKENS AND DROPS THE REST SILENTLY. gen_walkers.py learned this the hard
# way - the clause that fell off the end was the background, which is the one the cutout
# depends on. Counted here too, and checked at runtime below.
# ---------------------------------------------------------------------------

# The backdrop is SLATE BLUE-GREY and that is a cutout decision, not a taste one. It has to
# be far from every colour on a person so the flood key cannot walk from the wall into a
# face, and far from WHITE in particular, because four of the twenty views are a white shirt
# or a white dress and a white figure on a white wall has no edge to find. Slate is far from
# skin, far from white, and desaturated enough not to bounce a colour cast onto the figure
# the way a green screen does.
STYLE = ("full length studio photo, head to shoes, standing upright, both feet visible, "
         "plain green screen backdrop, even light, sharp focus, photorealistic, 1990")

CARTOON = ("full body flat cel shaded cartoon, bold clean outlines, flat colour, "
           "standing facing the viewer, plain bright green screen background, 1990")

# "seated, sitting, crouching, kneeling" are in here specifically because the rejected cast
# read as seated. The pose control is the real fix and this is the belt to its braces.
#
# "floor" IS DELIBERATELY ABSENT and was removed after it did measurable damage. The first
# run with it in came back with a man whose legs faded into the backdrop below the knee and
# who had no feet: asked for a full-length figure and simultaneously forbidden anything for
# it to stand on, SD1.5 resolves the contradiction by dissolving the legs. A floor is
# harmless downstream anyway - fit_walkers.flood_background clusters the border into four
# colours precisely so a two-tone wall-and-floor background keys in one pass, and its
# docstring records the white slab under every sprite that taught it that. "shadow" stays,
# because a cast shadow touching the shoes survives connectivity and becomes a dark blob
# attached to the sprite.
#
# THIS LIST IS SHORT BECAUSE IT HAS TO BE, AND FINDING THAT OUT COST MOST OF A DAY.
#
# CLIP truncates the NEGATIVE prompt at 77 tokens exactly as it truncates the positive one,
# and just as silently. gen_walkers.py documents the trap for the positive prompt and this
# file checked the positive prompt at runtime - and neither ever checked the negative. It
# had grown to 142 tokens, so a little over half of it was being discarded before it reached
# the model, on every frame generated in this session.
#
# What was being dropped, measured by decoding the tokens past the window:
#
#   vignette, faded, watch, wristwatch, bracelet, jewellery, floor length skirt, maxi skirt,
#   gown, evening dress, train, draped fabric, jacket off shoulder, open jacket, glitter,
#   sparkles, water droplets, dispersion, particle effect, film grain, light stand, tripod,
#   studio equipment, umbrella
#
# That is: EVERY term added in response to a defect during this session was in the discarded
# half and never applied at all. The wristwatch survived because nothing ever asked for it
# to go, and so did the light stand. The floor-length skirt was never prohibited either -
# what removed it was painting the init's leg colour as two narrow columns with backdrop
# between them. The glitter was cured by low-passing the init's noise, not by the word
# "glitter". Three independent confirmations of one lesson: on this pipeline the INIT is the
# strong lever and the prompt is the weak one, and a prompt clause is worth exactly nothing
# if it falls off the end of the window.
#
# So this is now short, and ordered by how much damage each failure does, because the end of
# the list is the part at risk. The real fix is the runtime check in generate(), which now
# covers both prompts.
NEGATIVE = ("illustration, painting, cartoon, 3d render, deformed hands, extra fingers, "
            "extra limbs, seated, crouching, cropped legs, missing feet, out of frame, "
            "two people, text, watermark, furniture, light stand, floor length skirt, "
            "gown, watch, jewellery, shadow, mutated")

CARTOON_NEG = ("photograph, deformed hands, extra fingers, extra limbs, mutated, seated, "
               "sitting, crouching, cropped, out of frame, two people, text, watermark, "
               "furniture, floor, shadow, realistic, 3d render")

# ---------------------------------------------------------------------------
# THE CAST. New people. The `wear` lines are what the corresponding view MEASURES - see the
# module docstring - and the `who` lines are invented within the build and broad colouring
# the sprite shows.
# ---------------------------------------------------------------------------
CAST = {
    # 280-283. Face band (224,144,135) - fair skin. Hair dark. Cel 43 wide: average build.
    "body0": {
        "seed": 704311, "style": STYLE, "neg": NEGATIVE,
        "who": "a man in his early thirties, fair skin, short dark brown hair, "
               "dark sunglasses, average build",
        "glasses": True,
        # View 280 carries a briefcase; the other three outfits do not.
        "prop": {"best": (24, 26, 38)},
        "neg_extra": "sweater, cardigan, rolled sleeves, trainers, slim",
        "wear": [
            # 280: torso and legs both (41,45,71) dark navy, feet dark.
            "wearing a mid grey blue two piece suit, pale blue shirt, dark tie, black shoes",
            # 281: torso (41,40,46) near black, legs (241,236,234) white, feet (73,42,41).
            "wearing a black blazer over cream trousers, brown loafers",
            # 282: torso (239,239,240) white 57%, legs (67,106,139) blue, feet (77,45,45).
            "wearing a white crew neck t-shirt with a red logo, blue jeans, brown shoes",
            # 283: torso and legs skin, feet (19,19,35) dark.
            "wearing only white underwear, bare chest, bare legs, embarrassed",
        ],
    },
    # 284-287. Face (240,169,169) fair. Skirt and dress outlines.
    "body1": {
        "seed": 118097, "style": STYLE, "neg": NEGATIVE,
        "who": "a woman in her late twenties, pale fair skin, dark auburn bob, "
               "sturdy build",
        "neg_extra": "leotard, swimsuit, athletic, muscular, slim",
        "prop": {"best": (38, 30, 32)},
        "bag": {"mid": (198, 168, 142)},
        "wear": [
            # 284: torso (192,170,176) grey-mauve, legs (13,16,33) navy.
            "wearing a pale mauve blouse and a navy skirt suit, dark shoes",
            # 285: torso AND legs (2,175,227) - one bright teal garment full length.
            "wearing a bright teal full length dress, pale shoes",
            # 286: torso (240,80,160) hot pink, legs (240,167,170) bare skin.
            "wearing a plain hot pink t-shirt and separate pale pink shorts, bare legs, "
            "pale trainers",
            # 287: torso (241,234,233) white, legs skin.
            "wrapped in only a white bath towel, bare shoulders and legs, embarrassed",
        ],
    },
    # 290-293. Face (200,131,99) olive/tanned, hair (48,33,34) very dark. Cels run wider.
    "body2": {
        "seed": 926455, "style": STYLE, "neg": NEGATIVE,
        "who": "a thickset barrel chested man in his late fifties, olive skin, "
               "thinning grey hair, grey moustache",
        "neg_extra": "sunglasses, slim fit suit, tailored, fashion model",
        "prop": {"best": (30, 32, 45)},
        "wear": [
            # 290: torso (41,45,71)/(45,64,67) dark, legs dark.
            "wearing a very dark charcoal navy suit, pale shirt, dark tie, black shoes",
            # 291: torso (136,108,108) dusty rose, legs (172,132,109) tan, feet dark brown.
            "wearing a dusty pink shirt, tan trousers, brown shoes",
            # 292: torso (239,231,240) white, legs (8,16,34) dark navy.
            "wearing a pale pink shirt, dark navy trousers, plain dark shoes",
            # 293: 61 cels wide with a big white mass at the feet - the barrel gag.
            "wearing only a wooden barrel on shoulder braces, bare arms and legs",
        ],
    },
    # 294-297. Hair (160,64,48)/(149,67,43) auburn. Face fair.
    "body3": {
        "seed": 553028, "style": STYLE, "neg": NEGATIVE,
        "who": "a full figured woman in her forties, fair skin, curly copper red hair",
        # "plain yellow shorts" was not enough - the print still bled across the conjunction.
        # This is a CATEGORY failure, not an adjective one: the model is choosing a matching
        # co-ord set instead of separates, and only the prompt can settle a category.
        "neg_extra": "matching set, printed shorts",
        "wear": [
            # 294: torso and legs (98,99,113) slate blue-grey.
            "wearing a slate blue grey skirt suit, dark low heeled shoes",
            # 295: torso and legs (240,235,235) white.
            "wearing a long cream white dress, pale shoes",
            # 296: torso (241,237,236) cream, legs (240,201,142) tan.
            "wearing a floral print blouse and plain yellow shorts, bare legs, pale flat shoes",
            # 297: torso skin, legs (241,236,237) white.
            "wrapped in only a white bath towel, bare shoulders and legs, embarrassed",
        ],
    },
    # 274-277. FLAT CARTOON ART AND IT SHOULD STAY THAT WAY. The legs of view 274 are
    # exactly RGB (0,0,208) and of 275 exactly (128,0,192) - pure palette entries, not
    # digitised photography like the other sixteen views. This is the attract-mode demo
    # walker, it never shares the screen with a player, and the joke is that it is drawn
    # rather than photographed. Kept here so the choice can be SHOWN rather than argued,
    # in the cartoon register, and the recommendation is to leave the original art alone.
    "jones": {
        "seed": 380142, "style": CARTOON, "neg": CARTOON_NEG,
        "who": "a cartoon man, long chin, big nose, swept back orange hair, wide grin",
        "wear": [
            "in a bright blue suit and a yellow bow tie",          # 274 legs (0,0,208)
            "in a purple jacket and purple trousers",              # 275 legs (128,0,192)
            "in a white shirt and cornflower blue trousers",       # 276 legs (80,112,240)
            "in only striped swimming trunks, skinny arms and legs",   # 277 all skin
        ],
    },
}

OUTFIT_I = {"best": 0, "mid": 1, "cheap": 2, "undressed": 3}


# Writing any of these in a wardrobe line suppresses the automatic "patterned". The first
# six name a pattern the author has already described; "plain" is the explicit override for
# a garment the blunt detector over-reports - see measure_bands.
PATTERN_WORDS = ("pattern", "floral", "print", "logo", "striped", "checked", "plaid",
                 "plain")


def prompt_for(who, cel, bands):
    """The positive prompt, with "patterned" added when the slot's torso says so.

    This is the one thing the prompt can say that the init cannot. A patterned garment has
    no single colour - see measure_bands - so the init carries its BASE colour and the word
    carries the fact that there is a print on it. Skipped when the wardrobe line already
    names a pattern, so view 296's floral blouse does not come out "floral patterned,
    patterned"."""
    wear = who["wear"][OUTFIT_I[cel["outfit"]]]
    if bands and bands.get("torso_patterned") \
            and not any(w in wear for w in PATTERN_WORDS):
        wear += ", patterned"
    return f"{who['who']}, {wear}, {who['style']}"


# Colour words that appear in the wardrobe lines, and what they actually are. Only words
# that name a colour; "floral", "plain" and "denim" are garment or pattern words and are
# deliberately absent.
COLOUR_WORDS = {
    "white": (242, 242, 242), "cream": (238, 232, 210), "black": (26, 26, 28),
    "navy": (32, 38, 72), "charcoal": (54, 56, 60), "grey": (128, 128, 130),
    "slate": (102, 110, 124), "brown": (92, 58, 44), "tan": (198, 164, 118),
    "yellow": (232, 200, 110), "pink": (236, 150, 170), "hot pink": (240, 80, 160),
    "teal": (10, 168, 200), "blue": (70, 105, 150), "mauve": (188, 168, 178),
    "auburn": (140, 70, 48), "copper": (150, 72, 44), "olive": (120, 118, 72),
    "red": (190, 50, 45), "green": (70, 120, 70), "purple": (110, 60, 150),
    "beige": (214, 196, 168), "khaki": (188, 172, 120), "silver": (196, 198, 200),
}


def check_wardrobe(a):
    """Does each hand-written wardrobe line agree with the cel it describes?

    THIS EXISTS BECAUSE THE WARDROBE LINE IS THE MOST COMMON SOURCE OF WHOLESALE WRONGNESS
    IN THIS PIPELINE, and the reason is structural: every other channel is DERIVED from the
    1990 cel - the bands, the collar, the shoe, the pose box, the build - and this one is
    TYPED. Nobody measures it, so nothing catches it.

    Three separate frames were lost to it before the pattern was seen:

        view 286  "denim shorts"                     slot measures (240,167,171) - pink
        view 296  "a cream blouse and a tan skirt"    slot is a floral top over yellow shorts
        view 280  "a dark navy two piece suit"        jacket measures (78,77,100) - mid grey-blue

    All three would have been caught here before a single frame was generated.

    The output is a TABLE rather than a verdict, deliberately. A colour word is a fuzzy
    thing, the 1990 art is digitised and dithered, and a garment legitimately spans a range -
    so this reports each colour word against the nearest band it could plausibly be
    describing and lets a person judge the outliers. Anything past `--wardrobe-tol` is
    marked, but the mark is an invitation to look, not a failure."""
    print(f"{'slot':>6} {'word':<10} {'word is':>16} {'nearest band':>22} {'dist':>5}")
    flagged = 0
    for name, who in sorted(CAST.items()):
        for oi in range(4):
            view = pose_author.BODIES[name]["base"] + oi
            bands = measure_bands(view)
            if not bands:
                continue
            wear = who["wear"][oi].lower()
            words = [w for w in COLOUR_WORDS if w in wear]
            # "hot pink" subsumes "pink"; keep the longer match only.
            words = [w for w in words
                     if not any(w != o and w in o and o in wear for o in words)]
            for w in words:
                want = COLOUR_WORDS[w]
                # The bands, plus the two channels that carry colour the bands cannot:
                # the collar's own shirt colour (a shirt under a jacket is inside the torso
                # band, not a band of its own) and the torso palette (a small logo is a
                # palette swatch and never a dominant band).
                cand = {b: bands[b] for b in ("torso", "thigh", "shin", "feet", "hair")
                        if b in bands}
                col = collar_for(view)
                if col and col.get("shirt"):
                    cand["collar-shirt"] = col["shirt"]
                # The torso PALETTE is deliberately not a candidate. It masked two real
                # findings - "white shoes" on view 285 matched a torso swatch and stopped
                # flagging - because a word is not attached to a garment here, so any
                # channel can answer for any word. The cost is that a logo colour, which
                # lives only in the palette, always flags: view 282's "red" is expected.
                best, bd = None, 1e9
                for b, cv in cand.items():
                    d = max(abs(int(x) - int(y)) for x, y in zip(want, cv))
                    if d < bd:
                        best, bd = b, d
                bands_show = cand
                mark = "  <-- look" if bd > a.wardrobe_tol else ""
                if bd > a.wardrobe_tol:
                    flagged += 1
                print(f"{view:>6} {w:<10} {str(want):>16} "
                      f"{best + ' ' + str(bands_show[best]):>22} {bd:>5.0f}{mark}")
    print(f"\n{flagged} colour word(s) more than {a.wardrobe_tol:.0f} levels from any band "
          f"they could be describing. Look at each; a fuzzy word over dithered 1990 art is "
          f"not automatically wrong.")


def wear_of(who, cel):
    return who["wear"][OUTFIT_I[cel["outfit"]]]


def negative_for(who):
    """The shared negative plus this character's own.

    body2 needs "slim, slender, athletic, muscular, toned, young man, fashion model" and the
    others do not, because the thing being fought is specific to him: SD1.5's prior for an
    unqualified adult male is a lean young one, and a heavy fifty-year-old has to be asked
    for from both directions at once. The skeleton is the stronger of the two levers - see
    pose_author.BUILDS - but the prompt should not be pulling the other way while it works.

    CLIP TRUNCATES THE NEGATIVE AT 77 TOKENS TOO, silently, and what falls off the end of
    this one is whatever was added last. Checked at runtime in generate()."""
    extra = who.get("neg_extra")
    return f"{who['neg']}, {extra}" if extra else who["neg"]


# ---------------------------------------------------------------------------
# The init frame
# ---------------------------------------------------------------------------

# THIGH AND SHIN ARE SAMPLED SEPARATELY, and that is what makes shorts representable.
# One leg band from 0.55 to 0.88 cannot tell a tan skirt from tan shorts over bare legs: it
# returns the dominant colour of both, which is the garment, and the init then says "tan all
# the way down" - which is how view 296 came back in a floor-length tan skirt with no legs to
# animate. Split at the knee, view 296 reads tan over skin, which is the shorts it actually
# is, and view 282 reads denim over denim, which is the jeans it actually is.
BAND_F = [("hair", 0.00, 0.07), ("face", 0.08, 0.15), ("torso", 0.20, 0.45),
          ("thigh", 0.55, 0.70), ("shin", 0.73, 0.88), ("legs", 0.55, 0.88),
          ("feet", 0.90, 1.00)]


def measure_bands(view, loop=0, cel=0):
    """The dominant colour of each of five horizontal bands of the 1990 figure.

    This is the ONLY colour the original contributes, and it is what the brief means by
    matching the slot's clothing grade and broad colouring. View 282 measures a white torso
    over blue legs over dark feet, so the new man wears a white shirt, blue jeans and dark
    shoes; view 285 measures the same bright teal in the torso band and the leg band, which
    is how we know it is one full-length garment and not a top and a skirt.

    THE MASK IS ERODED BY ONE PIXEL FIRST. The 1x decode leaves a cyan and cream matte
    fringe at full alpha around most of these figures - (152,232,208) and (224,200,152)
    turn up as "dominant" colours in half the views if you do not - and a fringe colour
    fed back in as a clothing hint would dress the entire cast in mint green."""
    p = os.path.join(PNG1X, f"view_{view}_l{loop}_c{cel}.png")
    if not os.path.exists(p):
        return None
    im = np.asarray(Image.open(p).convert("RGBA"))
    a = im[:, :, 3] > 128
    inner = fit_walkers.erode(a, 1)
    if inner.sum() < 40:
        inner = a
    rows = np.nonzero(a.any(axis=1))[0]
    top, span = int(rows[0]), int(rows[-1] - rows[0] + 1)

    out = {}
    for name, f0, f1 in BAND_F:
        y0 = int(round(top + f0 * (span - 1)))
        y1 = int(round(top + f1 * (span - 1)))
        sel = inner[y0:y1 + 1]
        px = im[y0:y1 + 1, :, :3][sel]
        if len(px) == 0:
            out[name] = (128, 128, 128)
            continue
        q = (px // 32).astype(int)
        keyq = q[:, 0] * 64 + q[:, 1] * 8 + q[:, 2]
        vals, cnt = np.unique(keyq, return_counts=True)
        order = vals[np.argsort(-cnt)]
        dom = px[keyq == order[0]].mean(axis=0)

        # The band's top colours, for painting a PATTERNED band as a mottle rather than a
        # flat mean. A floral print has no single colour and averaging one out is how view
        # 296's floral top became plain cream.
        # THE PALETTE CARRIES THE MOST SATURATED BUCKET AS WELL AS THE MOST COMMON ONES.
        # Taken by count alone, view 282's white t-shirt returned white, pale blue, pale pink
        # and pale salmon - the anti-aliased EDGES of its red logo, and no red at all - so
        # the mottle painted pale-on-pale, blurred to white, and the logo vanished. A small
        # graphic is never in the top four by count. Adding the most saturated bucket puts
        # the actual colour of the feature into the palette regardless of how few pixels
        # carry it, which is the same extreme-not-centre rule the collar and the band chroma
        # recovery use.
        pal = [tuple(int(v) for v in px[keyq == q].mean(axis=0).round()) for q in order[:4]]
        means = np.array([px[keyq == q].mean(axis=0) for q in order[:12]])
        if len(means):
            sat = means.max(axis=1) - means.min(axis=1)
            hot = tuple(int(v) for v in means[int(np.argmax(sat))].round())
            if hot not in pal and float(sat.max()) >= 25.0:
                pal.append(hot)
        out[name + "_palette"] = pal

        # THE DOMINANT BUCKET LOSES THE HUE OF A PALE GARMENT, and that is why view 292's
        # pink shirt came back pale blue-grey. Digitised in 1990 at 39x95, a pale pink shirt
        # is mostly highlight and dither: the most COMMON colour in the band is near-white
        # (239,231,240), and the pink lives in a minority of pixels. Feed the dominant bucket
        # back as the init's colour hint and the init says "near-white", and the init beats
        # the prompt every time - the prompt said "pale pink shirt" and lost.
        #
        # So the hue comes from the SATURATED end of the band and the lightness comes from
        # the dominant bucket. Take the mean of the top quintile by saturation to get the
        # garment's actual chroma, then rescale it to the dominant bucket's brightness so a
        # pale garment stays pale instead of turning into a saturated one.
        #
        # THE NEUTRAL GUARD IS LOAD-BEARING. On a genuinely white or grey garment the top
        # quintile by saturation is just noise, and following it would invent a hue and put
        # view 282's white t-shirt into a pink one. Where the band has no real chroma to
        # find, the dominant bucket is kept unchanged.
        sat = px.max(axis=1).astype(float) - px.min(axis=1).astype(float)
        p80 = float(np.percentile(sat, 80))
        hi = px[sat >= p80]
        sat_dom = float(dom.max() - dom.min())

        # PATTERNED, by the spread of saturation across the band. This is a blunt test and
        # it is kept blunt deliberately, because of what it is FOR: its main job is to stop
        # the chroma recovery below from following a small strong graphic and repainting the
        # whole garment. View 282's white t-shirt has a red logo, and without this flag the
        # recovery turns the shirt itself pink (255,222,224).
        #
        # Counting distinct colour buckets instead - which sounds more principled, and was
        # tried - is worse on exactly that case: the logo is too small a share of the band to
        # register as a "major" bucket, so the shirt went pink again. Saturation spread sees
        # it because it does not care how many pixels are involved.
        #
        # It over-reports: view 286's flat hot pink top trips it too, since a strongly
        # coloured garment anti-aliased against bare skin has a wide spread and no pattern.
        # That costs nothing on the colour, which is kept either way, and the spurious word
        # in the prompt is suppressed by writing "plain" in the wardrobe line - see
        # prompt_for.
        patterned = bool(float(sat.std()) >= 26.0 and p80 >= 30.0)
        out[name] = tuple(int(v) for v in dom.round())

        # THE RECOVERY ONLY FIRES WHERE THE DEFECT IS, and the three conditions are each
        # there because a broader rule broke something measurable:
        #
        #   sat_dom < 20   the dominant colour must be NEAR-NEUTRAL. This is the whole
        #                  failure: a pale garment whose commonest colour is near-white.
        #                  Applied to a band that already HAS a hue it flips it - view 282's
        #                  brown shoes, dominant (77,45,45), went to blue-grey (46,55,66),
        #                  because the saturated minority in that band is the jeans hem
        #                  above them, not the shoes.
        #   not patterned  a small strong graphic on a plain garment is not the garment's
        #                  colour. View 282's white t-shirt has a red logo, and following
        #                  its saturated pixels turned the whole shirt pink (255,222,224).
        #   p80 >= 18      there has to be some chroma present to recover at all, or this
        #                  invents a hue out of digitiser noise.
        if sat_dom < 20.0 and not patterned and p80 >= 18.0 and len(hi) >= 4:
            chroma = hi.mean(axis=0)
            lc = max(1.0, float(chroma.mean()))
            out[name] = tuple(int(v) for v in
                              np.clip(chroma * (float(dom.mean()) / lc), 0, 255).round())

        # HIGH SATURATION VARIANCE MEANS A PATTERN, NOT A COLOUR. View 296's top is a floral
        # print on cream: its band has a cream base and a scatter of strong hues, so the
        # spread of saturation is wide where a plain garment's is narrow. No single colour
        # describes it, and averaging one out is how the floral top became plain cream. The
        # colour hint stays the base; the word "patterned" goes in the prompt, which is the
        # one thing the prompt can say that the init cannot.
        out[name + "_sat"] = round(p80, 1)
        out[name + "_patterned"] = patterned

        # THE FACE BAND CAN COME BACK AS HAIR. On view 296 the hair is pinned up and wide
        # enough that the 0.08-0.15 band is mostly hair, and both bands measured (149,67,43)
        # - which would have painted her forearms auburn, because the forearms take the face
        # colour. When the two agree to within 30 levels the face band is not describing a
        # face, so the second most common colour in it is used instead.
        if name == "face" and "hair" in out:
            if max(abs(a - b) for a, b in zip(out["face"], out["hair"])) < 30 \
                    and len(order) > 1:
                out[name] = tuple(int(v)
                                  for v in px[keyq == order[1]].mean(axis=0).round())
    return out


# Slots whose wardrobe line names a single continuous garment. These are the only ones with
# no waist seam, so they are the only ones the init must not draw one on.
ONE_PIECE = ("dress", "towel", "barrel", "underwear", "swimming trunks")


def _measure_collar_cel(view, loop=0, cel=0):
    """The shirt-and-tie region, MEASURED off the 1990 cel rather than guessed.

    The collar was first drawn by eye at half-width 0.030 and 0.075 tall below the neck, and
    view 280 then came back with a burgundy V-neck sweater in two cels of four and a tie in
    the other two. Negating "sweater, cardigan" in the prompt changed nothing, because the
    cause was not the prompt: most of the chest was a flat navy polygon, so the model filled
    it from its own prior and filled it differently each time.

    Measured instead - brighter-than-torso pixels in the central 40% of the torso's width,
    averaged over four cels, as fractions of figure height:

        280 navy suit, white shirt, tie    top 0.161  bottom 0.418  halfw 0.048  tie 0.019
        290 charcoal suit, pale shirt      top 0.160  bottom 0.255  halfw 0.044  tie 0.008
        284 mauve blouse (control)         halfw 0.010  - i.e. nothing, correctly

    The real triangle is 3.4x taller and 1.6x wider than the guess. The central-40%
    restriction is what makes it honest: without it a lit lapel at the silhouette edge counts
    as shirt and view 280 measures a half-width of 0.114, wider than its own shoulders.

    Returns None where there is no shirt to find, which is the correct answer for view 284's
    blouse and for view 282's white t-shirt - on the latter the whole torso IS the bright
    thing, so nothing exceeds the torso median and the method declines to invent one."""
    p = os.path.join(PNG1X, f"view_{view}_l{loop}_c{cel}.png")
    if not os.path.exists(p):
        return None
    im = np.asarray(Image.open(p).convert("RGBA"))
    a = im[:, :, 3] > 128
    inner = fit_walkers.erode(a, 1)
    rows = np.nonzero(a.any(axis=1))[0]
    if len(rows) < 40:
        return None
    top, span = int(rows[0]), int(rows[-1] - rows[0] + 1)
    y0 = int(round(top + 0.16 * (span - 1)))
    y1 = int(round(top + 0.50 * (span - 1)))
    m = inner[y0:y1 + 1]
    if m.sum() < 10:
        return None
    cols = np.nonzero(m.any(axis=0))[0]
    cx = (cols.min() + cols.max()) / 2.0
    half = (cols.max() - cols.min() + 1) / 2.0
    keep = np.zeros_like(m)
    keep[:, max(0, int(round(cx - half * 0.40))):int(round(cx + half * 0.40)) + 1] = True
    m = m & keep
    lum = im[y0:y1 + 1, :, :3].astype(float).mean(axis=2)
    torso = float(np.median(lum[inner[y0:y1 + 1]]))
    shirt = (lum > torso + 38) & m
    if shirt.sum() < 6:
        return None
    px = im[y0:y1 + 1, :, :3][shirt]
    ys = np.nonzero(shirt)[0]
    widths = [int(np.nonzero(shirt[r])[0].max() - np.nonzero(shirt[r])[0].min() + 1)
              for r in range(shirt.shape[0]) if shirt[r].any()]
    # THE SHIRT COLOUR IS THE BRIGHTEST DECILE, NOT THE MEAN. Averaging gave (128,154,181)
    # for view 280 - a mid blue-grey where the garment is a white shirt - because at 39
    # pixels wide the shirt is two or three pixels between a navy jacket and a dark tie, so
    # most of what "brighter than the torso" collects is BLEND. Painting a mid blue-grey
    # column is an instruction to draw a dark open-necked shirt, and that is exactly what
    # cel 0 drew. The top decile is the part of the distribution that is actually shirt.
    # Same rule as the band chroma recovery in measure_bands: at this source width, take the
    # extreme of the distribution rather than its centre.
    lum_px = px.astype(float).mean(axis=1)
    bright = px[lum_px >= np.percentile(lum_px, 90)]
    out = {"top": (y0 + int(ys.min()) - top) / (span - 1.0),
           "bottom": (y0 + int(ys.max()) - top) / (span - 1.0),
           "halfw": float(np.percentile(widths, 80)) / 2.0 / (span - 1.0),
           "shirt": tuple(int(v) for v in
                          (bright if len(bright) else px).mean(axis=0).round()),
           "tie_halfw": 0.0, "tie": None}
    dark = (lum < torso - 25) & m
    if dark.sum() > 6:
        dw = [int(np.nonzero(dark[r])[0].max() - np.nonzero(dark[r])[0].min() + 1)
              for r in range(dark.shape[0]) if dark[r].any()]
        out["tie_halfw"] = float(np.median(dw)) / 2.0 / (span - 1.0)
        out["tie"] = tuple(int(v) for v in
                           im[y0:y1 + 1, :, :3][dark].mean(axis=0).round())
    return out


def _median_of_cels(fn, view, loop, keys):
    """Runs a per-cel measurement over all four cels of a view and takes the median.

    A garment is a property of the CHARACTER, not of the frame, so measuring it on one cel
    throws away three quarters of the evidence and inherits that cel's noise. It bit
    immediately: view 280's shoe measured top 0.932 averaged over four cels and a degenerate
    1.0 on cel 0 alone, because on that cel the shin and shoe colours happen not to separate
    until the very last row. The median over four is what was validated and is what is used.
    """
    got = [fn(view, loop, c) for c in range(4)]
    got = [g for g in got if g]
    if not got:
        return None
    out = {}
    for k in keys:
        vals = [g[k] for g in got if g.get(k) is not None]
        if not vals:
            out[k] = None
        elif isinstance(vals[0], tuple):
            out[k] = tuple(int(v) for v in np.mean(vals, axis=0).round())
        else:
            out[k] = float(np.median(vals))
    return out


def collar_for(view, loop=0):
    return _median_of_cels(_measure_collar_cel, view, loop,
                           ("top", "bottom", "halfw", "shirt", "tie_halfw", "tie"))


def shoe_for(view, loop=0):
    return _median_of_cels(_measure_shoe_cel, view, loop, ("top", "halfw", "colour"))


def _measure_shoe_cel(view, loop=0, cel=0):
    """Where the shoe starts, how wide it is and what colour, MEASURED off the 1990 cel.

    Same fault as the collar and found the same way. The init's shoe blob was drawn by eye
    at half-width 0.050 running from the authored ankle to a little below it - which, with
    the ankle at 0.955 of figure height, means it started at 0.943 and ran to 1.045. Both
    ends were wrong: the shoe actually begins at 0.90-0.93, well ABOVE where the blob
    started, and the figure ends at 1.0, so a third of the blob was painting empty space
    below the sole. View 282's trainers came back white in one cel and red in the next.

    Measured: the first row below 0.86 whose dominant colour departs from the shin's by more
    than 40 levels, then the extent and dominant colour of everything below it.

        280 black shoes      top 0.932  halfw 0.055  (39,37,38)
        282 brown shoes      top 0.927  halfw 0.074  (84,52,51)
        286 cream trainers   top 0.903  halfw 0.043  (210,161,160)
        287 bare feet        top 0.879  halfw 0.039  (216,140,135)  - a SKIN colour

    The last row is the control and it is informative rather than a failure: view 287's
    figure is barefoot under a towel, and the method returns her skin. It is reporting what
    is actually at the bottom of that figure, which is the right answer."""
    p = os.path.join(PNG1X, f"view_{view}_l{loop}_c{cel}.png")
    if not os.path.exists(p):
        return None
    im = np.asarray(Image.open(p).convert("RGBA"))
    a = im[:, :, 3] > 128
    inner = fit_walkers.erode(a, 1)
    rows = np.nonzero(a.any(axis=1))[0]
    if len(rows) < 40:
        return None
    top, bot = int(rows[0]), int(rows[-1])
    span = bot - top + 1

    def dom(px):
        if len(px) == 0:
            return None
        q = (px // 32).astype(int)
        kq = q[:, 0] * 64 + q[:, 1] * 8 + q[:, 2]
        v, c = np.unique(kq, return_counts=True)
        return tuple(int(x) for x in px[kq == v[int(np.argmax(c))]].mean(axis=0).round())

    sy0 = int(round(top + 0.73 * (span - 1)))
    sy1 = int(round(top + 0.86 * (span - 1)))
    shin = dom(im[sy0:sy1 + 1, :, :3][inner[sy0:sy1 + 1]])
    if shin is None:
        return None
    found = None
    for r in range(int(round(top + 0.86 * (span - 1))), bot + 1):
        if not inner[r].any():
            continue
        d = dom(im[r][inner[r]][:, :3])
        if d and max(abs(int(x) - int(y)) for x, y in zip(d, shin)) > 40:
            found = r
            break
    if found is None:
        return None
    seg = inner[found:bot + 1]

    # ONE SHOE, NOT BOTH. Measuring the row's full extent measures the pair plus the gap
    # between them: view 282 came out at half-width 0.073, and with the ankles 148 canvas
    # pixels apart on a contact frame two blobs that wide overlap into a single bar - which
    # is what the composited init showed. The widest contiguous RUN in the row is one foot.
    ws = []
    for r in range(seg.shape[0]):
        xs = np.nonzero(seg[r])[0]
        if not len(xs):
            continue
        best = run = 1
        for i in range(1, len(xs)):
            run = run + 1 if xs[i] == xs[i - 1] + 1 else 1
            best = max(best, run)
        ws.append(best)
    col = dom(im[found:bot + 1, :, :3][seg])
    if col is None or not ws:
        return None
    return {"top": (found - top) / (span - 1.0),
            "halfw": float(np.percentile(ws, 80)) / 2.0 / (span - 1.0),
            "colour": col}


def bands_of_rgba(path):
    """The same five band colours as `measure_bands`, but read off a GENERATED frame.

    The bands are taken from the figure's own alpha and its own bounding box, so the two are
    directly comparable: `measure_bands` says what the 1990 slot wears and this says what
    the generated cel actually came out wearing."""
    if not os.path.exists(path):
        return None
    im = np.asarray(Image.open(path).convert("RGBA"))
    a = im[:, :, 3] > 128
    rows = np.nonzero(a.any(axis=1))[0]
    if len(rows) < 40:
        return None
    top, span = int(rows[0]), int(rows[-1] - rows[0] + 1)
    out = {}
    for name, f0, f1 in BAND_F:
        y0 = int(round(top + f0 * (span - 1)))
        y1 = int(round(top + f1 * (span - 1)))
        sel = a[y0:y1 + 1]
        px = im[y0:y1 + 1, :, :3][sel]
        if len(px) == 0:
            out[name] = (128, 128, 128)
            continue
        q = (px // 32).astype(int)
        keyq = q[:, 0] * 64 + q[:, 1] * 8 + q[:, 2]
        vals, cnt = np.unique(keyq, return_counts=True)
        dom = vals[int(np.argmax(cnt))]
        out[name] = tuple(int(v) for v in px[keyq == dom].mean(axis=0).round())
    return out


def drift(a):
    """Flags a cel whose GARMENT COLOURS have wandered away from cel 0's.

    WHY THIS EXISTS, AND WHY boil DOES NOT COVER IT. fit_walkers' `boil` measures deviation
    only on pixels the 1990 source held still across all four cels - and that source is
    digitised photography carrying ~25 levels of per-pixel noise, so very few pixels qualify
    and the mask that survives is small and concentrated. It is a good detector of a figure
    being repainted wholesale (the rejected cast scored 25-31 on it) and a poor detector of
    one garment detail changing. View 280 scored a healthy 0.72 while visibly changing from
    a sweater to a striped tie to rolled sleeves across the loop, with the shoes going black,
    brown, black.

    Once img2img is out of the pipeline the four cels of a loop are independent samples of
    one seed and one prompt, so this is the failure mode to expect, and it has to be caught
    arithmetically rather than by looking at eleven loop sheets at two in the morning. A
    shoe going from black to brown is a band-colour change and is trivially measurable.

    Chebyshev distance per band against cel 0, in 0-255 levels. `--drift-tol` is the
    threshold; 40 catches a black-to-brown shoe and tolerates ordinary shading differences
    between poses."""
    import glob
    views = {}
    for p in sorted(glob.glob(os.path.join(a.drift, "cut_*.png"))):
        m = os.path.basename(p)[4:-4].split("_")
        v, l, c = int(m[0]), int(m[1][1:]), int(m[2][1:])
        views.setdefault((v, l), {})[c] = p

    worst = []
    for (v, l), cels in sorted(views.items()):
        if 0 not in cels:
            continue
        # THE REFERENCE IS THE LOOP'S MEDIAN, NOT CEL 0. Comparing against cel 0 assumes cel
        # 0 is right, and when it is the odd one out the table inverts: view 280's test loop
        # had a dark polo in cel 0 and a correct white-shirt-and-tie in cels 1 and 3, and the
        # checker dutifully flagged the two correct frames. A per-channel median over the
        # loop is the consensus garment, so the cel that actually disagrees is the one named
        # - which matters because the output is a list of cels to regenerate.
        got_all = {c: bands_of_rgba(p) for c, p in cels.items()}
        got_all = {c: g for c, g in got_all.items() if g}
        if len(got_all) < 3:
            continue
        ref = {}
        for name, _, _ in BAND_F:
            ref[name] = tuple(int(v) for v in
                              np.median([g[name] for g in got_all.values()], axis=0).round())
        for c in sorted(got_all):
            got = got_all[c]
            for name, _, _ in BAND_F:
                d = max(abs(int(x) - int(y)) for x, y in zip(got[name], ref[name]))
                if d <= a.drift_tol:
                    continue
                # THE HEAD BANDS ARE ADVISORY AND DO NOT GATE. "hair" and "face" are a fixed
                # 0.00-0.15 slice of FIGURE height, so on a figure whose extent shifts by a
                # few percent between cels the slice lands on a shoulder in one and on hair
                # in the next. View 282 reported face d=209 across two frames whose faces are
                # identical, while the feet flag on that same view was real. An unannotated
                # table gets acted on at two in the morning, so these are labelled and left
                # out of the count.
                head = name in ("hair", "face")
                tag = "note " if head else "DRIFT"
                extra = "   (advisory: head band is figure-relative)" if head else ""
                print(f"  {tag} view {v} loop {l} cel {c}  {name:<6} "
                      f"median={ref[name]} -> {got[name]}  d={d}{extra}")
                if not head:
                    worst.append((d, v, l, c, name, ref[name], got[name]))
    if not worst:
        print(f"no band drifted more than {a.drift_tol} levels from cel 0")
    else:
        worst.sort(reverse=True)
        print(f"{len(worst)} band(s) over tolerance; worst d={worst[0][0]} "
              f"on view {worst[0][1]} cel {worst[0][3]} {worst[0][4]}")
        print("regenerate a single offending cel with: --views <v> --only-cel <c> --force")
    return worst


def init_field(cel, size, bg, blur=62, grain=8, bands=None, soft=18, glasses=False,
               waist=True, collar=None, prop=None, shoe=None):
    """The frame the base is denoised from: a flat backdrop with a soft noise island where
    the figure is going to be.

    The island is drawn from the AUTHORED SKELETON, not from the original cel's alpha. That
    is the difference from gen_walkers.init_frame and it matters for the same reason the
    whole pose change does: the 1990 mask is not a statement about where a person's limbs
    are, and using it as one - even only as a blurred hint - reintroduces the silhouette's
    errors through the back door.

    Why an island at all: SD1.5 at a 1:2 aspect ratio does the well-known thing and draws
    the subject twice, stacked. ControlNet reduces that and does not eliminate it. A blurred
    anchor where the person goes does. The island is NOISE rather than flat grey because
    flat grey is structure and survives the denoise - the first version of this printed the
    island itself into the picture as a pair of grey wings."""
    W, H = size
    k = cel["keypoints"]
    fig = cel["fig_h"]

    m = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(m)
    for a, b in sprite_limbs():
        if k[a] is None or k[b] is None:
            continue
        d.line([tuple(k[a]), tuple(k[b])], fill=255, width=int(max(4, fig * 0.090)))

    # THE LEGS AND THE FEET GET A WIDER ISLAND THAN THE REST. A leg is the narrowest thing
    # on the figure, so after a heavy blur it is also the faintest part of the island, and
    # the flat field wins there - which is precisely where the first run's figure dissolved.
    # Widening the shins and putting an explicit blob under each ankle costs nothing
    # elsewhere and is what gives the model something to stand on.
    for kn, an in ((k[9], k[10]), (k[12], k[13])):
        d.line([tuple(kn), tuple(an)], fill=255, width=int(max(6, fig * 0.115)))
        # Down to +0.090 of the figure's height below the ankle, not +0.055. The authored
        # ankle is the JOINT; the shoe is another 4cm below it and half a shoe-length in
        # front, and if the island stops at the joint then the only thing under the ankle
        # is flat backdrop - which is what the model then paints there.
        d.ellipse([an[0] - fig * 0.080, an[1] - fig * 0.020,
                   an[0] + fig * 0.080, an[1] + fig * 0.090], fill=255)
    d.ellipse([k[0][0] - fig * 0.075, k[0][1] - fig * 0.085,
               k[0][0] + fig * 0.075, k[0][1] + fig * 0.060], fill=255)
    m = m.filter(ImageFilter.GaussianBlur(blur))

    # THE ISLAND'S NOISE IS LOW-PASSED, AND THAT IS NOT A DETAIL. Per-pixel Gaussian noise
    # is white noise, and white noise at an img2img strength of 0.88 does not all get
    # denoised away: the finest octave of it survives as a glittering speckle ALL OVER THE
    # FIGURE. The first frame through this path came back as a photograph of a man covered
    # in water droplets, dissolving into the backdrop from the waist down, and that speckle
    # is also what broke the matte - the key floods straight through a half-transparent leg.
    # Blurring the noise leaves it just as structureless (it biases no colour and preserves
    # no shape, which is the entire reason it is noise and not flat grey) while removing the
    # high frequencies that were surviving.
    rng = np.random.default_rng(20250929)
    island = Image.fromarray(
        np.clip(rng.normal(140, 42, (H, W, 3)), 0, 255).astype(np.uint8), "RGB")
    if grain:
        island = island.filter(ImageFilter.GaussianBlur(grain))

    # THE ISLAND IS TINTED WITH THE SLOT'S OWN MEASURED COLOURS, in five soft bands: hair,
    # face, torso, legs, feet. Two problems at once, and both had beaten the prompt alone.
    #
    # The prompt said "a plain white t-shirt, blue jeans, brown shoes" and the model
    # returned a bare-chested man in jeans. A garment clause sits in the middle of a 70
    # token prompt and CLIP does not weight it heavily; a torso-shaped patch of the right
    # COLOUR in the init is a far blunter instrument and the denoiser cannot ignore it.
    #
    # And it gives the legs and the feet something to be. Smooth neutral grey where a shoe
    # belongs is not a shoe, and the model kept resolving it as more backdrop; a dark blob
    # at the ankle resolves as a dark shoe.
    #
    # It is still not the original's SHAPE. The bands are painted onto the authored
    # skeleton, blurred to the point of being coloured fog, and the only thing taken from
    # 1990 is five RGB triples.
    s_, _ox, oy_ = cel["fit"]
    top_y = oy_ + cel["bbox"][0] * s_

    if bands:
        # THE BANDS ARE PAINTED ON THE SKELETON, NOT AS FULL-WIDTH STRIPES, and that is the
        # difference between a leg band meaning "legs" and meaning "anything at all down
        # there of roughly this colour".
        #
        # The first version filled the whole canvas width with the leg colour between hip
        # and ankle. For the men that worked, because their leg colour is a trouser colour
        # and a trouser-coloured slab reads as trousers. For the women it failed exactly as
        # you would expect once it is put that way: view 296's leg band measures (240,201,142)
        # tan, a full-width tan slab from hip to floor is a floor-length tan SKIRT, and that
        # is what came back - beautiful and unanimatable, with no legs to move and nothing
        # for the foot band to describe. View 286's leg band measures (240,167,170), which is
        # BARE SKIN, and a full-width skin-coloured slab is not bare legs either.
        #
        # Painted as two narrow columns down the authored femur and shin, with the space
        # between and beside them left as backdrop, the same colour can only be read one way.
        # A garment that covers the shins now has to be painted over backdrop-coloured init,
        # which is the one thing the denoiser is most reluctant to do.
        pal = Image.new("RGB", (W, H), tuple(bg))
        pd = ImageDraw.Draw(pal)

        def cap(p, q, wide, col):
            pd.line([tuple(p), tuple(q)], fill=col, width=int(max(3, wide)))
            r = max(2.0, wide / 2.0)
            for t in (p, q):
                pd.ellipse([t[0] - r, t[1] - r, t[0] + r, t[1] + r], fill=col)

        pd.polygon([tuple(k[2]), tuple(k[5]), tuple(k[11]), tuple(k[8])],
                   fill=bands["torso"])

        # A PATTERNED BAND IS PAINTED AS A MOTTLE, NOT A FLAT FILL. A print has no single
        # colour, and the flat mean of one is the reason view 296's floral top came back
        # plain cream and why "floral print blouse and plain yellow shorts" then leaked the
        # print onto the shorts - the prompt was carrying the whole burden because the init
        # said nothing about it. Scattered blobs of the band's own measured top colours say
        # "variegated here" in the one language the init speaks, and leave the flat thigh
        # column next to it saying "plain there".
        if bands.get("torso_patterned") and bands.get("torso_palette"):
            swatch = bands["torso_palette"]
            rng = np.random.default_rng(20250930)
            x0, x1 = k[2][0], k[5][0]
            y0, y1 = k[2][1], k[8][1]
            r = fig * 0.030
            for _ in range(70):
                cx = rng.uniform(x0 - fig * 0.02, x1 + fig * 0.02)
                cy = rng.uniform(y0, y1)
                col = swatch[int(rng.integers(0, len(swatch)))]
                pd.ellipse([cx - r, cy - r, cx + r, cy + r], fill=col)
        g = cel.get("build", {}).get("girth", 1.0)
        ch = cel.get("build", {}).get("chest", 1.0)

        # THE CHEST IS WIDENED SEPARATELY FROM THE WAIST. `girth` scales the whole figure's
        # limbs and torso sides together, so on its own it cannot distinguish a thickset man
        # from a fat one - turn it up and everything grows, turn it down and the midsection
        # is still the widest thing in the silhouette because the torso polygon tapers from
        # shoulder to hip. A stocky middle-aged man is thick through the CHEST and shoulders
        # as well, and his outline is closer to a column than a taper. This bar across the
        # upper torso is what makes that shape available to be painted into.
        ty = k[2][1] + (k[8][1] - k[2][1]) * 0.22
        half = (k[5][0] - k[2][0]) / 2.0 * ch
        mid = (k[2][0] + k[5][0]) / 2.0
        cap((mid - half, ty), (mid + half, ty), fig * 0.095 * g * ch, bands["torso"])

        cap(k[2], k[8], fig * 0.085 * g, bands["torso"])
        cap(k[5], k[11], fig * 0.085 * g, bands["torso"])
        cap(k[2], k[3], fig * 0.055 * g, bands["torso"])
        cap(k[5], k[6], fig * 0.055 * g, bands["torso"])
        # The FOREARM takes the face colour, i.e. skin. Most of this cast is in short
        # sleeves, and it is a better guess than sleeving every arm to the wrist.
        cap(k[3], k[4], fig * 0.042 * g, bands["face"])
        cap(k[6], k[7], fig * 0.042 * g, bands["face"])
        cap(k[8], k[9], fig * 0.075 * g, bands["thigh"])
        cap(k[11], k[12], fig * 0.075 * g, bands["thigh"])
        cap(k[9], k[10], fig * 0.058 * g, bands["shin"])
        cap(k[12], k[13], fig * 0.058 * g, bands["shin"])
        # A WAISTBAND, SO TWO GARMENTS ARE TWO SHAPES. View 286's top and shorts are both
        # pink - torso (240,80,160), thigh (240,167,171) - so the init painted one unbroken
        # pink column from shoulder to thigh, and the model read a single garment and drew
        # the likeliest one: a pink swimsuit. That is the narrow-column init working too
        # well, and it is the same failure as the full-width tan slab becoming a floor-length
        # skirt, in the opposite direction. A slab that should be two things needs a break in
        # it, exactly as a slab that should be one thing needed narrowing.
        #
        # Skipped for the genuinely one-piece slots, which is why it is a flag rather than
        # unconditional: a dress, a towel and a barrel have no waist seam and drawing one
        # would invent a garment boundary the slot does not have.
        # THE NECKLINE IS PINNED, because it is what flickered. With img2img gone the four
        # cels of a loop are independent samples, and view 280 came back with a V-neck
        # sweater in cel 0, a striped tie in cel 1 and rolled sleeves in cel 2 - the prompt
        # named "white shirt, dark tie" and the model filled the neckline differently each
        # time because the init's torso is one flat colour and says nothing there. A shirt
        # wedge and a tie stripe are three shapes and they remove the ambiguity.
        # A collar narrower than 0.02 of figure height is the measurement saying there is
        # no shirt-under-jacket to find - view 284's blouse returns 0.010 - and painting a
        # sliver that thin says nothing while risking a white stripe on a patterned top.
        if collar and collar.get("halfw", 0) >= 0.02:
            nx = k[1][0]
            ty = top_y + collar["top"] * fig
            by = top_y + collar["bottom"] * fig
            w = fig * collar["halfw"]
            # A tapering column, widest at the collar and narrowing to the jacket button,
            # spanning the measured top-to-bottom rather than a guessed sliver.
            pd.polygon([(nx - w, ty), (nx + w, ty),
                        (nx + w * 0.45, by), (nx - w * 0.45, by)], fill=collar["shirt"])
            if collar["tie"] and collar["tie_halfw"] > 0.004:
                tw = fig * max(0.010, collar["tie_halfw"])
                pd.polygon([(nx - tw, ty + fig * 0.020), (nx + tw, ty + fig * 0.020),
                            (nx + tw * 0.7, by), (nx - tw * 0.7, by)], fill=collar["tie"])

        # A CARRIED PROP IS A MASS IN THE INIT, NOT A WORD. View 280's original carries a
        # briefcase in all four cels and it is a large dark block at the end of one arm -
        # a real part of that character's silhouette at 39 pixels wide. Remember the ivy:
        # a mass too small to survive the blur was never in the init at all, so this is
        # drawn at the size the thing actually is.
        if prop:
            # WIDER AND CLEAR OF THE BODY. The first attempt was a 0.055-half-width block at
            # the wrist and it did not survive: against a dark navy suit at a 62px blur a
            # dark block adjacent to the figure merges into it, and the briefcase simply did
            # not appear in any of the four cels. The ivy lesson again - the mass has to be
            # big enough AND separated enough to still read after the blur.
            wx, wy = k[4]
            wx = wx - fig * 0.055 if wx < k[7][0] else wx + fig * 0.055
            pd.rectangle([wx - fig * 0.085, wy + fig * 0.020,
                          wx + fig * 0.085, wy + fig * 0.230], fill=prop)

        if waist:
            wy = (k[8][1] + k[11][1]) / 2.0
            hw = abs(k[11][0] - k[8][0]) * 0.60 + fig * 0.045
            dark = tuple(int(v * 0.55) for v in bands["torso"])
            pd.rectangle([(k[8][0] + k[11][0]) / 2.0 - hw, wy - fig * 0.009,
                          (k[8][0] + k[11][0]) / 2.0 + hw, wy + fig * 0.009], fill=dark)

        # THE SHOES, at their measured extent. Drawn from the shoe's measured top down to
        # the SOLE LINE and no further - the eyeballed blob started below the shoe's real
        # top and ran a third of its height past the bottom of the figure, painting the
        # backdrop. `shoe` is None where there is nothing to measure, and the flat feet band
        # is used instead.
        sole_y = top_y + fig
        # A measured half-width below 0.025 of figure height is the method finding a sliver
        # rather than a shoe - view 283, the undressed tier, returns 0.011 - and painting a
        # blob that thin says nothing. Fall back to the flat feet band there.
        if shoe and shoe.get("halfw", 0) < 0.025:
            shoe = None
        for an in (k[10], k[13]):
            if shoe:
                # Capped against the ankle separation so the two shoes cannot merge into one
                # bar on a contact frame, whatever the measurement says. The measured width
                # still includes some trouser hem, and on view 282 that gave a half-width of
                # 0.072 - wide enough that two blobs 148 canvas pixels apart overlapped, and
                # the composited init showed a single foot-wide slab.
                sep = abs(k[13][0] - k[10][0])
                sw = min(fig * shoe["halfw"], max(fig * 0.030, sep * 0.45))
                sy = min(an[1] - fig * 0.010, top_y + shoe["top"] * fig)
                pd.rounded_rectangle([an[0] - sw, sy, an[0] + sw, sole_y],
                                     radius=fig * 0.018, fill=shoe["colour"])
            else:
                pd.ellipse([an[0] - fig * 0.050, an[1] - fig * 0.012,
                            an[0] + fig * 0.050, sole_y], fill=bands["feet"])
        pd.ellipse([k[0][0] - fig * 0.055, k[0][1] - fig * 0.070,
                    k[0][0] + fig * 0.055, k[0][1] + fig * 0.055], fill=bands["face"])
        pd.ellipse([k[0][0] - fig * 0.058, k[0][1] - fig * 0.085,
                    k[0][0] + fig * 0.058, k[0][1] - fig * 0.010], fill=bands["hair"])

        # SUNGLASSES GO IN THE INIT, NOT ONLY IN THE PROMPT. View 282 wears them and they
        # are a strong silhouette feature at 39 pixels wide, but "dark sunglasses" is four
        # tokens in the middle of a 74-token prompt and the rule this file keeps rediscovering
        # is that the init wins. A dark bar across the eye line is unambiguous.
        if glasses:
            ey = (k[14][1] + k[15][1]) / 2.0
            pd.rectangle([k[16][0] - fig * 0.004, ey - fig * 0.013,
                          k[17][0] + fig * 0.004, ey + fig * 0.013], fill=(34, 34, 40))

        pal = pal.filter(ImageFilter.GaussianBlur(soft))
        island = Image.blend(island, pal, 0.82)

    return Image.composite(island, Image.new("RGB", (W, H), tuple(bg)), m)


def sprite_limbs():
    import sprite_pose
    return sprite_pose.LIMBS


# ---------------------------------------------------------------------------
# The cutout
# ---------------------------------------------------------------------------

def fill_holes(mask):
    """A mask with its interior holes closed: anything not reachable from the border."""
    h, w = mask.shape
    border = np.zeros((h, w), bool)
    border[0], border[-1], border[:, 0], border[:, -1] = True, True, True, True
    outside = fit_walkers.keep_connected(~mask, border)
    return ~outside


def cutout(rgb, tol=22.0, soft=20.0, mode="chroma"):
    """Alpha for a generated frame, CUT FROM THE GENERATED FIGURE.

    This is failure 4 in the brief. The previous pass matted against the 1990 silhouette
    dilated by a band, so anything the model painted that happened to touch the figure
    survived inside the band as a white chip, and anything the new character had that the
    1990 one did not - different hair, a hat, a wider coat - was shaved off at the band's
    edge. Nothing here consults the original's shape at all.

    Three steps, each of which exists because the one before it is not sufficient on its
    own:

      flood   from the border, over pixels near one of four clustered border colours. This
              is fit_walkers.flood_background and its docstring records why it is four
              colours and not one, and why it floods rather than region-grows.
      keep    only the largest connected foreground component. A studio backdrop sometimes
              comes back with a corner vignette the flood will not reach; the person is
              always the biggest thing that is left.
      close   fill interior holes, so a gap between an arm and the body that the flood could
              not reach from outside does not become a transparent bubble in the middle of
              a torso. Holes that ARE open to the background - the triangle between the legs
              - are reached by the flood and stay open, which is correct.

    Returns (alpha, frac), where `frac` is how much of the pre-connectivity foreground the
    largest component accounted for. That number is handed to the screen rather than
    recomputed there, because by the time this function returns there is only ONE component
    left and a connectivity check on its own output would be a tautology that always passes.

    THE BACKDROP IS GREEN AND THAT IS A MEASUREMENT, NOT A PREFERENCE. The first working
    figure was generated against the slate blue-grey backdrop that reads best, and the flood
    key then ate its legs. The numbers, sampled off that exact frame: the four background
    clusters the key derives are (48,57,83), (66,73,95), (75,84,109) and (95,107,135), and
    the man's jeans measure (20,38,64) at the thigh and (49,66,96) at the shin - 27.6 and
    13.0 away from the nearest background colour, against a keying threshold of 42. The
    shirt is 138 away and the face 148, so the top half cut perfectly and the legs
    disappeared. A blue figure on a blue wall has no colour edge to find, and no tolerance
    setting separates 13.0 from the background's own 11.4.

    Six of the twenty views are navy or charcoal below the waist, so this was not one bad
    frame; it was most of the cast. Green is the one hue absent from the entire measured
    palette of all twenty views - whites, creams, tans, browns, navies, charcoals, teal,
    hot pink, dusty rose, slate and skin - so `g - max(r, b)` is large and positive on the
    backdrop and at or below zero on every person in the set, and the key becomes a hue
    test that cannot collide with anything. fit_walkers.chroma_key does that test and
    de-spills afterwards.

    gen_walkers.py tried green once and recorded three failures: green crept over the
    figure, the island's outline printed as a board behind it, and the prompt stopped being
    obeyed. All three were consequences of the island being FLAT GREEN. This island is
    tinted with the slot's own measured garment colours, which is what put the white shirt
    and the blue jeans where they belong in the approved frame, so the interior is not green
    and there is nothing for it to creep out of.
    """
    if mode == "chroma":
        a, rgbf = fit_walkers.chroma_key(rgb, tol, soft)
    else:
        a, rgbf = fit_walkers.flood_background(rgb, tol, soft), rgb.astype(np.float32)
    solid = a > 0.5
    n = int(solid.sum())
    if n < 500:
        return a, rgbf, 0.0
    big = largest(solid)
    keep = fill_holes(big)
    return a * keep.astype(np.float32), rgbf, int(big.sum()) / float(n)


def largest(mask):
    """The biggest 4-connected True region, by run-length labelling with union-find.

    THE OBVIOUS IMPLEMENTATION HANGS AND IT COST AN HOUR OF WALL CLOCK. Flood-filling from
    each unvisited pixel in turn is O(components x pixels) with a Python flood, which is
    unnoticeable on a clean two-component matte and catastrophic on a speckled one: the run
    that produced the glittering frame left a matte with thousands of one-pixel islands, and
    this function sat on it for forty minutes still holding the GPU, which is also what
    starved the next run of the card. Scanning runs once and merging them through a
    disjoint-set is O(runs) and finishes in well under a second on any matte, good or bad -
    and it is the bad ones where it matters, because those are exactly the frames the screen
    is supposed to reject quickly.

    scipy.ndimage.label would be one line; scipy is not installed on this machine and
    fit_walkers records the same."""
    h, w = mask.shape
    parent = []

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    allruns = []
    prev = []
    for y in range(h):
        d = np.diff(np.concatenate(([0], mask[y].astype(np.int8), [0])))
        starts = np.nonzero(d == 1)[0].tolist()
        ends = np.nonzero(d == -1)[0].tolist()
        cur = []
        i = 0
        for s, e in zip(starts, ends):
            lbl = len(parent)
            parent.append(lbl)
            # Two-pointer against the previous row's runs. Both lists are sorted, so this
            # is linear; testing every previous run against every current one is what makes
            # the naive version quadratic again on a speckled row.
            while i < len(prev) and prev[i][1] <= s:
                i += 1
            j = i
            while j < len(prev) and prev[j][0] < e:
                union(prev[j][2], lbl)
                j += 1
            cur.append((s, e, lbl))
            allruns.append((y, s, e, lbl))
        prev = cur

    if not allruns:
        return mask
    size = {}
    for _, s, e, lbl in allruns:
        r = find(lbl)
        size[r] = size.get(r, 0) + (e - s)
    best = max(size, key=size.get)
    out = np.zeros_like(mask)
    for y, s, e, lbl in allruns:
        if find(lbl) == best:
            out[y, s:e] = True
    return out


# ---------------------------------------------------------------------------
# The screen
# ---------------------------------------------------------------------------

def screen(alpha, cel, size, one_frac):
    """Automatic rejection. Returns (ok, [(name, ok, detail), ...]).

    WHAT THIS CATCHES AND WHAT IT DOES NOT, stated plainly because a screen believed to do
    more than it does is worse than no screen.

    It catches: a figure that is not standing the way it was told to (the crown or the soles
    in the wrong place, which is exactly the seated failure), a second figure or a second
    head, a figure with no separable feet, a silhouette that is not one connected person,
    and a frame the key could not cut. These are geometric facts about the matte and the
    authored skeleton, and they are decided without a model and without a judgement call.

    It does NOT catch a well-placed six-fingered hand. Nothing available on this machine
    can: there is no pose detector and no hand model in the local model set, and neither
    was downloaded because neither could be justified as a one-line answer to "is this
    hand right". The extremity refinement is the mitigation for hands; this screen is the
    mitigation for everything structural. That division is deliberate and it is the honest
    description of what the automation is worth.
    """
    W, H = size
    k = cel["keypoints"]
    fig = cel["fig_h"]
    m = alpha > 0.5
    out = []

    def add(name, ok, detail):
        out.append((name, bool(ok), detail))

    n = int(m.sum())
    cov = n / float(W * H)
    add("coverage", 0.04 <= cov <= 0.55, f"{cov*100:.1f}% of frame")
    if n < 500:
        return False, out

    rows = np.nonzero(m.any(axis=1))[0]
    top, bot = int(rows[0]), int(rows[-1])

    # BOTH REFERENCES COME FROM THE ANKLES, NOT FROM THE NOSE. The nose moves: once the
    # authored cycle gained its vertical bob the head rises by 1.4% of figure height on the
    # pass frames, so a sole line derived from it rose too and the check failed cels 1 and 3
    # of a loop whose feet were exactly where they should be. The ankles are the one landmark
    # that does not move - a planted foot is planted - so the ground line is measured from
    # them, and the crown is measured back up from them by the figure's own height.
    sole = max(k[10][1], k[13][1]) + fig * (1.0 - pose_author.Y["ankle"])
    want_bot = sole
    want_top = sole - fig
    add("crown", abs(top - want_top) <= fig * 0.055,
        f"at {top}, authored {want_top:.0f}")
    # THE SEATED TEST. A figure that sat down, crouched or was drawn short ends well above
    # the authored sole line, and this is the check that would have rejected the last cast.
    add("soles", abs(bot - want_bot) <= fig * 0.045,
        f"at {bot}, authored {want_bot:.0f}")

    # Measured by `cutout` BEFORE it discarded the other components, because afterwards
    # there is only one and the question answers itself.
    add("one figure", one_frac >= 0.93,
        f"largest component was {one_frac*100:.1f}% of the keyed foreground")

    def runs_at(y, minw):
        y = int(round(min(H - 1, max(0, y))))
        r = [t for t in _runs(m[y]) if t[1] - t[0] >= minw]
        return r

    # One head, not two. SD1.5's stacked-subject failure at a 1:2 aspect shows here first.
    eye_y = k[14][1]
    add("one head", len(runs_at(eye_y, fig * 0.02)) == 1,
        f"{len(runs_at(eye_y, fig*0.02))} runs at the eye line")

    hr = runs_at(eye_y, fig * 0.02)
    hw = (hr[0][1] - hr[0][0]) / fig if hr else 0.0
    add("head width", 0.045 <= hw <= 0.135, f"{hw:.3f} of figure height")

    # Feet. One run when the ankles are together, two when they are apart; three means the
    # model grew something, zero means the key ate them.
    ank_y = max(k[10][1], k[13][1]) + fig * 0.028
    fr = runs_at(ank_y, fig * 0.012)
    add("feet", 1 <= len(fr) <= 2, f"{len(fr)} runs just above the sole line")

    sh_r = runs_at(k[1][1], fig * 0.02)
    sw = (max(t[1] for t in sh_r) - min(t[0] for t in sh_r)) / fig if sh_r else 0.0
    want_sw = 2 * cel["build"]["sh"]
    add("shoulders", 0.75 * want_sw <= sw <= 1.75 * want_sw,
        f"{sw:.3f} of figure height, authored {want_sw:.3f}")

    return all(o for _, o, _ in out), out


def _runs(row):
    out, s = [], None
    for i, v in enumerate(row):
        if v and s is None:
            s = i
        elif not v and s is not None:
            out.append((s, i))
            s = None
    if s is not None:
        out.append((s, len(row)))
    return out


# ---------------------------------------------------------------------------
# Generation
# ---------------------------------------------------------------------------

def crop_box(cx, cy, half, size):
    W, H = size
    x0 = int(round(min(max(0, cx - half), W - 2 * half)))
    y0 = int(round(min(max(0, cy - half), H - 2 * half)))
    x0 = max(0, x0)
    y0 = max(0, y0)
    x1 = min(W, x0 + 2 * int(half))
    y1 = min(H, y0 + 2 * int(half))
    return x0, y0, x1, y1


def feather(w, h, pad):
    """A soft-edged rectangle, so a refined crop is blended back rather than pasted with a
    visible seam."""
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).rectangle([pad, pad, w - pad - 1, h - pad - 1], fill=255)
    return m.filter(ImageFilter.GaussianBlur(pad * 0.55))


def refine(img_pipe, torch, base, cel, who, device, args):
    """Re-generates the head, each hand and the feet as 512x512 crops.

    THIS IS WHERE THE HANDS COME FROM. At 640x1280 a hand is about sixty pixels tall and
    SD1.5 is famously bad at that size; the rejected cast's deformed hands and feet are
    that failure. Cropped to a square around the authored wrist and resized to 512, the same
    hand fills a third of a frame at the resolution the model was actually trained on, and
    a short img2img pass at low strength redraws it in place. The strength is the whole
    trade: high enough to rebuild the anatomy, low enough that the sleeve, the skin tone and
    the lighting do not change - which would show up immediately as the frame-to-frame boil
    this whole arrangement exists to avoid.

    The control image is BLACK at conditioning scale 0, which makes the ControlNet pipeline
    behave as plain img2img. Building a second, ControlNet-free pipeline sharing the same
    modules would fight the CPU-offload hooks that gen_walkers.build installs, and those
    hooks are the difference between running and page-thrashing on a 4GB card.
    """
    k = cel["keypoints"]
    fig = cel["fig_h"]
    W, H = base.size
    out = base

    jobs = []
    if args.refine_head:
        jobs.append(("head", k[0][0], k[0][1] + fig * 0.005, fig * 0.115,
                     f"close up photograph of the face of {who['who']}, "
                     f"looking at the camera, sharp focus, photorealistic",
                     args.head_strength))
    if args.refine_hands:
        for nm, kp in (("hand_r", k[4]), ("hand_l", k[7])):
            jobs.append((nm, kp[0], kp[1] + fig * 0.012, fig * 0.085,
                         "close up photograph of one relaxed human hand hanging at the "
                         "side, five fingers, natural anatomy, sharp focus",
                         args.hand_strength))
    if args.refine_feet:
        fy = max(k[10][1], k[13][1])
        fx = (k[10][0] + k[13][0]) / 2.0
        half = max(fig * 0.095, abs(k[13][0] - k[10][0]) * 0.75 + fig * 0.05)
        jobs.append(("feet", fx, fy + fig * 0.022, half,
                     "close up photograph of a pair of shoes on a standing person's feet, "
                     "natural anatomy, sharp focus", args.foot_strength))

    # ATTENTION SLICING IS FOR THE 640x1280 BASE, NOT FOR THESE. Slicing is what makes the
    # base fit in 4GB at all, and it costs roughly a factor of two in time; a 512x512 crop
    # has never needed it - the unsliced measurement at 512x1024 peaked at 2.83GB and this
    # is a third of that. Off for the refinement passes and back on afterwards, which is
    # most of the difference between seventeen minutes a frame and eleven.
    if jobs and args.slicing:
        img_pipe.disable_attention_slicing()
    try:
        for nm, cx, cy, half, prompt, strength in jobs:
            half = int(round(half))
            if half < 24:
                continue
            x0, y0, x1, y1 = crop_box(cx, cy, half, (W, H))
            if x1 - x0 < 32 or y1 - y0 < 32:
                continue
            crop = out.crop((x0, y0, x1, y1)).resize((512, 512), Image.LANCZOS)
            blank = Image.new("RGB", (512, 512), (0, 0, 0))
            gen = torch.Generator(device=device).manual_seed(who["seed"] + 977)
            t = time.time()
            fixed = img_pipe(prompt=prompt + ", plain green screen background",
                             negative_prompt=negative_for(who), image=crop,
                             control_image=blank,
                             strength=strength,
                             num_inference_steps=max(args.steps,
                                                     int(args.steps / strength)),
                             guidance_scale=args.cfg, controlnet_conditioning_scale=0.0,
                             generator=gen).images[0]
            fixed = fixed.resize((x1 - x0, y1 - y0), Image.LANCZOS)
            out = out.copy()
            out.paste(fixed, (x0, y0), feather(x1 - x0, y1 - y0, max(6, (x1 - x0) // 9)))
            print(f"      refined {nm} {x1-x0}x{y1-y0} at ({x0},{y0})  "
                  f"{time.time()-t:.0f}s", flush=True)
    finally:
        if jobs and args.slicing:
            img_pipe.enable_attention_slicing("max")
    return out


LOCK = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
                    "jones-upscale-models", "gpu.pid")


def gpu_busy():
    """Any OTHER python process currently holding a CUDA context, per nvidia-smi.

    The pid-file lock alone cannot see another TOOL - see claim_gpu - so this asks the
    driver instead, which is the only authority that knows what is actually on the card."""
    try:
        out = subprocess.run(
            ["nvidia-smi", "--query-compute-apps=pid", "--format=csv,noheader"],
            capture_output=True, text=True, timeout=30).stdout
    except (OSError, subprocess.SubprocessError):
        return None
    mine = os.getpid()
    for line in out.splitlines():
        line = line.strip()
        if not line.isdigit() or int(line) == mine:
            continue
        try:
            name = subprocess.run(
                ["tasklist", "/FI", f"PID eq {line}", "/NH", "/FO", "CSV"],
                capture_output=True, text=True, timeout=30).stdout
        except (OSError, subprocess.SubprocessError):
            continue
        if "python" in name.lower():
            return int(line)
    return None


def claim_gpu(out):
    """Refuse to start while another run of this tool is alive, and leave a note behind
    saying which process holds it.

    TWO RUNS ON A 4GB CARD IS WORSE THAN ONE RUN TWICE. When a launcher is killed on Windows
    the python child is NOT reaped - it keeps the CUDA context, keeps about 3.5GB of the
    card and keeps its commit - and the next run then either contends with it (one base
    frame went from 354s to 2014s) or cannot initialise CUDA at all and dies with
    0xC000070A, which looks exactly like a hardware failure and is not one. Several hours
    of this session went into diagnosing symptoms of that. The pid file is cleared on
    normal exit and on SIGTERM; a stale one whose process is gone is ignored."""
    import atexit
    import signal

    # THE LOCK IS GLOBAL, AND IT USED NOT TO BE. It wrote its pid file into the RUN'S OWN
    # output directory, so two runs with different --out never saw each other, and two
    # different TOOLS never could. It protected gen_cast from a second gen_cast and nothing
    # else - while being cited, by me, as the reason the card could not be double-booked.
    # It was: a walker run and a building run sat on the same 4GB card at 3814MiB with both
    # crawling, and neither was refused.
    #
    # Same shape as the CLIP guard that checked the positive prompt and not the negative: a
    # guard that does not guard is worse than none, because it is why everybody stopped
    # watching. So the file is now at one fixed path shared by every tool, AND the driver is
    # asked directly, since nvidia-smi is the only authority on what is actually on the card.
    other = gpu_busy()
    if other:
        raise SystemExit(f"another python process (pid {other}) is holding the GPU; "
                         f"wait for it rather than crawling beside it")
    os.makedirs(os.path.dirname(LOCK), exist_ok=True)
    os.makedirs(out, exist_ok=True)
    p = LOCK
    if os.path.exists(p):
        try:
            with open(p) as f:
                old = int(f.read().strip())
        except (ValueError, OSError):
            old = None
        if old and old != os.getpid() and _alive(old):
            raise SystemExit(f"another gen_cast is already running as pid {old} "
                             f"({p}); stop it first - two runs will not fit on 4GB")
    with open(p, "w") as f:
        f.write(str(os.getpid()))

    def clean(*_):
        try:
            os.remove(p)
        except OSError:
            pass
    atexit.register(clean)
    for sig in (signal.SIGTERM, signal.SIGINT):
        try:
            signal.signal(sig, lambda *_: (clean(), sys.exit(1)))
        except (ValueError, OSError):
            pass


def _alive(pid):
    if sys.platform != "win32":
        return os.path.exists(f"/proc/{pid}")
    PROCESS_QUERY_LIMITED = 0x1000
    h = ctypes.windll.kernel32.OpenProcess(PROCESS_QUERY_LIMITED, False, pid)
    if not h:
        return False
    code = ctypes.c_ulong()
    ok = ctypes.windll.kernel32.GetExitCodeProcess(h, ctypes.byref(code))
    ctypes.windll.kernel32.CloseHandle(h)
    return bool(ok) and code.value == 259  # STILL_ACTIVE


def generate(a):
    claim_gpu(a.out)
    import torch
    device = "cuda" if torch.cuda.is_available() else "cpu"
    with open(os.path.join(a.poses, "poses.json")) as f:
        poses = json.load(f)
    W, H = poses["size"]
    os.makedirs(a.out, exist_ok=True)

    only = set(int(v) for v in a.views.split(",")) if a.views else None
    cels = [(k, c) for k, c in sorted(poses["cels"].items(),
                                      key=lambda kv: (kv[1]["view"], kv[1]["loop"],
                                                      kv[1]["cel"]))
            if (only is None or c["view"] in only)
            and (a.only_cel is None or c["cel"] == a.only_cel)]

    if a.dry:
        for k, c in cels:
            who = CAST[c["body"]]
            print(f"{k}  {c['body']}/{c['outfit']}  seed {who['seed']}  "
                  f"{c['phase']}  fig {c['fig_h']:.0f}px")
            print(f"   {who['who']}, {who['wear'][OUTFIT_I[c['outfit']]]}, {who['style']}")
        return

    print(f"device: {device} "
          f"{torch.cuda.get_device_name(0) if device=='cuda' else ''}  canvas {W}x{H}",
          flush=True)
    txt, img = gen_walkers.build(device, offload=a.offload, slicing=a.slicing)

    report = {}
    for k, cel in cels:
        who = CAST[cel["body"]]
        bands = measure_bands(cel['view'], cel['loop'], cel['cel'])
        prompt = prompt_for(who, cel, bands)
        neg = negative_for(who)
        for what, s in (("prompt", prompt), ("negative", neg)):
            n = len(txt.tokenizer(s).input_ids)
            if n > txt.tokenizer.model_max_length:
                raise SystemExit(f"{what} for {k} is {n} tokens, over CLIP's "
                                 f"{txt.tokenizer.model_max_length}:\n  {s}")

        v, l, c = cel["view"], cel["loop"], cel["cel"]
        dst = os.path.join(a.out, f"raw_{v}_l{l}_c{c}.png")
        if os.path.exists(dst) and not a.force:
            continue
        pose = Image.open(os.path.join(a.poses, f"pose_{v}_l{l}_c{c}.png")).convert("RGB")
        first = os.path.join(a.out, f"raw_{v}_l{l}_c0.png")

        best = None
        for attempt in range(a.tries):
            # ONE SEED PER CHARACTER, and a rejected frame is the only thing that moves it.
            # Identity across the twenty views is carried by the seed being bit-identical,
            # so the retry offset is small and is recorded in the manifest.
            seed = who["seed"] + attempt * 10007
            gen = torch.Generator(device=device).manual_seed(seed)
            t0 = time.time()
            if c == 0 or a.no_img2img or not os.path.exists(first):
                base = img(prompt=prompt, negative_prompt=neg,
                           image=init_field(cel, (W, H), a.bg, bands=bands,
                                            glasses=who.get("glasses", False),
                                            collar=(collar_for(v, l)
                                                    if "tie" in wear_of(who, cel) else None),
                                            shoe=shoe_for(v, l),
                                            prop=who.get("prop", {}).get(cel["outfit"]),
                                            waist=not any(
                                                w in who["wear"][OUTFIT_I[cel["outfit"]]]
                                                for w in ONE_PIECE)),
                           control_image=pose, strength=a.bg_strength,
                           num_inference_steps=max(a.steps, int(a.steps / a.bg_strength)),
                           guidance_scale=a.cfg,
                           controlnet_conditioning_scale=a.control,
                           generator=gen).images[0]
            else:
                base = img(prompt=prompt, negative_prompt=neg,
                           image=Image.open(first).convert("RGB"),
                           control_image=pose, strength=a.strength,
                           num_inference_steps=max(a.steps, int(a.steps / a.strength)),
                           guidance_scale=a.cfg,
                           controlnet_conditioning_scale=a.control,
                           generator=gen).images[0]
            print(f"  {k} base {time.time()-t0:.0f}s (try {attempt+1}, seed {seed})",
                  flush=True)
            base.save(os.path.join(a.out, f"base_{v}_l{l}_c{c}.png"))

            body = refine(img, torch, base, cel, who, device, a)
            al, despilled, one_frac = cutout(np.asarray(body.convert("RGB")),
                                             a.tol, a.soft, a.key)
            ok, checks = screen(al, cel, (W, H), one_frac)
            for nm, o, det in checks:
                print(f"      {'PASS' if o else 'FAIL'}  {nm:<12} {det}", flush=True)
            best = (body, al, despilled, ok, checks, seed)
            if ok:
                break
            print(f"      screen rejected, regenerating", flush=True)

        body, al, despilled, ok, checks, seed = best
        body.save(dst)
        # The DE-SPILLED colour, not the raw frame. A green field bounces green into every
        # edge pixel and into light hair; keep the raw colour and the sprite carries a lime
        # rim once it is over the board, which is the halo this whole exercise exists to be
        # rid of. fit_walkers.chroma_key clamps the green channel to the mean of the other
        # two wherever it exceeds them, which is a no-op on anything legitimately green.
        Image.fromarray(np.clip(np.dstack([despilled, al * 255.0]),
                                0, 255).astype(np.uint8), "RGBA") \
             .save(os.path.join(a.out, f"cut_{v}_l{l}_c{c}.png"))
        report[k] = {"seed": seed, "ok": ok, "prompt": prompt,
                     "checks": [{"check": nm, "ok": o, "detail": d}
                                for nm, o, d in checks]}
        if device == "cuda":
            torch.cuda.empty_cache()

    with open(os.path.join(a.out, "gen_cast.manifest.json"), "w") as f:
        json.dump({"size": [W, H], "steps": a.steps, "cfg": a.cfg, "control": a.control,
                   "strength": a.strength, "bg": a.bg, "cels": report}, f, indent=1)
    bad = [k for k, r in report.items() if not r["ok"]]
    print(f"{len(report)} frames, {len(bad)} still failing the screen: {bad}")


# ---------------------------------------------------------------------------
# Fitting into cels
# ---------------------------------------------------------------------------

def fit(a):
    """Generated frames -> RGBA cels at exactly `factor` x the 1x cel.

    The alpha is the one `cutout` produced, carried through rather than recomputed, so the
    thing that ships is the thing the screen passed. Placement still comes from the original
    cel's box - same top row, same ground line, same horizontal centre - because that is the
    functional geometry the game positions against, and it is the one part of the 1990 art
    this work is supposed to inherit."""
    with open(os.path.join(a.poses, "poses.json")) as f:
        poses = json.load(f)
    os.makedirs(a.cels, exist_ok=True)

    loops = {}
    for k, cel in sorted(poses["cels"].items()):
        loops.setdefault((cel["view"], cel["loop"]), []).append((k, cel))

    only = set(int(v) for v in a.views.split(",")) if a.views else None
    report = {}
    for (v, l), entries in sorted(loops.items()):
        if only and v not in only:
            continue
        have = []
        for k, cel in entries:
            p = os.path.join(a.gen, f"cut_{v}_l{l}_c{cel['cel']}.png")
            if os.path.exists(p):
                have.append((k, cel, p))
        if not have:
            continue
        keyed = []
        for _, _, p in have:
            im = np.asarray(Image.open(p).convert("RGBA")).astype(np.float32)
            keyed.append((im[:, :, 3] / 255.0, im[:, :, :3]))
        refit = fit_walkers.refit_for([al for al, _ in keyed], [c for _, c, _ in have])
        for (k, cel, p), (al, rgb) in zip(have, keyed):
            im, clipped = fit_walkers.fit_one(p, cel, a.factor, 0.0, a.tol, a.soft,
                                              refit=refit, alpha=al, src=rgb)
            name = f"view_{v}_l{l}_c{cel['cel']}.png"
            im.save(os.path.join(a.cels, name))
            cov = float((np.asarray(im)[:, :, 3] > 8).mean())
            report[k] = {"file": name, "size": list(im.size), "coverage": round(cov, 4)}
            print(f"  {name}  {im.size[0]}x{im.size[1]}  cover {cov*100:4.1f}%", flush=True)

    p = os.path.join(a.cels, "gen_cast.fit.json")
    old = {}
    if os.path.exists(p):
        with open(p) as f:
            old = json.load(f).get("cels", {})
    old.update(report)
    with open(p, "w") as f:
        json.dump({"factor": a.factor, "cels": old}, f, indent=1)
    print(f"{len(report)} cels -> {a.cels}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--poses", required=True)
    ap.add_argument("--out", help="where the generated frames go")
    ap.add_argument("--gen", help="generated frames, for --cels")
    ap.add_argument("--cels", help="fit the generated frames into game cels here")
    ap.add_argument("--views")
    ap.add_argument("--drift", help="report garment-colour drift across each loop in this "
                                    "generated directory and exit")
    ap.add_argument("--check-wardrobe", action="store_true",
                    help="check every hand-written wardrobe line against the bands measured "
                         "off its own cel, and exit. Costs no GPU and catches the class of "
                         "error that has cost the most frames")
    ap.add_argument("--wardrobe-tol", type=float, default=60.0)
    ap.add_argument("--drift-tol", type=float, default=40.0)
    ap.add_argument("--only-cel", type=int,
                    help="generate just this cel index - a single-frame turnaround while "
                         "a recipe is still being settled, instead of four frames of the "
                         "same fault")
    ap.add_argument("--steps", type=int, default=30)
    ap.add_argument("--cfg", type=float, default=8.0)
    ap.add_argument("--control", type=float, default=1.0)
    ap.add_argument("--strength", type=float, default=0.42)
    ap.add_argument("--bg-strength", type=float, default=0.88)
    ap.add_argument("--bg", type=int, nargs=3, default=[56, 132, 72],
                    help="the flat field the init puts outside the figure. Green, and the "
                         "argument for that is in cutout()'s docstring: it is the one hue "
                         "absent from all twenty views, so the key cannot collide with a "
                         "garment")
    ap.add_argument("--key", choices=("chroma", "flood"), default="chroma")
    ap.add_argument("--tol", type=float, default=22.0)
    ap.add_argument("--soft", type=float, default=20.0)
    ap.add_argument("--factor", type=int, default=12)
    ap.add_argument("--tries", type=int, default=2,
                    help="regenerate a frame this many times before shipping one that "
                         "fails the screen")
    ap.add_argument("--head-strength", type=float, default=0.42)
    ap.add_argument("--hand-strength", type=float, default=0.50)
    ap.add_argument("--foot-strength", type=float, default=0.45)
    ap.add_argument("--no-refine-head", dest="refine_head", action="store_false")
    ap.add_argument("--no-refine-hands", dest="refine_hands", action="store_false")
    ap.add_argument("--no-refine-feet", dest="refine_feet", action="store_false")
    ap.add_argument("--no-img2img", action="store_true")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--dry", action="store_true")
    ap.add_argument("--offload", action="store_true")
    ap.add_argument("--slicing", action="store_true")
    a = ap.parse_args()

    if a.check_wardrobe:
        return check_wardrobe(a)
    if a.drift:
        return drift(a)
    if a.cels:
        if not a.gen:
            ap.error("--cels needs --gen")
        return fit(a)
    if not a.out:
        ap.error("--out is required unless --cels is given")
    generate(a)


if __name__ == "__main__":
    main()




































