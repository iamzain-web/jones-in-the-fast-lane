# Board buildings: rename and photorealistic rebuild

Thirteen buildings sit on the town board (pic 11, cels 1-13). Zain wants them all done
eventually, **one at a time, asking him for each one** — the name is his to choose and the
cartoon-versus-photograph mismatch is his to judge as the board fills in.

Order is his. This file is the record of what is done, so nobody has to reconstruct it.

| # | Sierra's name | new name | building | state |
|---|---|---|---|---|
| 9 | Hi-Tech University | **Open Door University** | photoreal | **DONE** — verified on the town board |
| 7 | Employment Office | **Hire Ground** | photoreal | **DONE** — verified on the town board |
| 6 | Factory | **The Industrial Revolution** | photoreal | **rebuilt under the sign technique** — a board on posts, 166% → 125% of the building's width |
| 10 | Socket City | **Watt a Bargain** | photoreal | **DONE** — verified on the town board; device is a filament bulb |
| 1 | Low-Cost Housing | — | — | not started |
| 2 | Rent Office | **Rentflix** | photoreal | **DONE** — eighth of thirteen |
| 3 | Security Apartments | — | — | not started |
| 4 | Black's Market | **Bare Necessities** | photoreal | **DONE** — verified on the town board |
| 5 | Bank | — | — | not started |
| 11 | QT Clothing | **Common Threads** | photoreal | shipped — **sign rejected**, awaiting the rebuild |
| 12 | Monolith Burgers | **Bun Believable** | **his own pixel art** | **DONE** — see §47. The first non-photographic source; HIS lettering is kept because it measures 4.04 cel px of cap against our house face's 2.70. Door cels built, `CastDoors` (+1,+1). Its 21 spoken lines are the first rename to reach the VOICE — §48 |
| 13 | Pawn Shoppe | **Needful Things** | photoreal | **rebuilt under the sign technique** — first customer |
| 8 | Z-Mart | **General Dealer** | photoreal | **built under the sign technique** — see §41. Cap height 5.08/5.00 cel px, the first to be sized from a measured scan rather than from three candidates |

Rows 9, 7, 10, 4 and 11 all shipped under the stamped-type recipe and all carry the defect
described below. **Only the Factory has been rebuilt alongside the Pawn Shoppe**, because
it was the worst offender and gives a direct before and after. The other four wait on
Zain's judgement of the technique.

## What every one of these needs

Four things, and the count is found **from the resources**, never from a list someone wrote:

1. **The board cel** in pic 11 — architecture generated with the sign band blank, lettering
   stamped on afterwards so the model never sees text.
2. **`view_0_l1_c0`, the picPatch strip** — 183x25 at (68,138), repaints building TOPS over
   the character panel. Covers game x 68..250, so it holds the Employment Office and the
   University but NOT the Factory (x 9..40) or Socket City (x 255..298). This is what made
   two renames look broken for three rounds.
3. **Any interior backdrop** carrying the name (the university's was view 807).
4. **Any job-list backdrop** — view 706 carried the Employment Office's name on BOTH cels and
   was on nobody's list. `tools/find_name_art.py` finds these by lifting the lettering off
   Sierra's own cels as a pixel mask and searching all 829 views.

## Standing decisions

- **Spoken clips are NOT renamed.** The university's 22 say "Hi-Tech U"; the employment
  office's seven say *"Welcome to ACNE Employment"* and four build jokes on it. A subtitle
  disagreeing with its own audio is worse than the old name.
  **One building has now gone the other way, and it does not overturn this.** Bun
  Believable's 22 lines were REWRITTEN in `subtitles.json` and then regenerated in
  `speech-kokoro`, so the audio and the text agree again (§48). The rule is about
  *disagreement*; rewriting both ends removes the disagreement rather than tolerating it.
  It costs a rewrite per line, which is why it is not the default — "Monny the Burger
  Clown" and "it must be a Monolith" are jokes built on the old name and a find-and-replace
  would have left nonsense.
- **Generate at exactly 16x the cel**, downscale by a clean box mean.
- **No colour words in the positive prompt.** Colour lives in the init, measured off Sierra's
  cel; colour words displace the only thing the prompt can contribute, which is material.
  This was the fix that turned a cartoon into a photograph.
- **Lettering**: the 3x5 bitmap at 1x, a real typeface at the high rungs.
- Full pipeline reasoning is in `tools/samples/CAST.md`; the generators are
  `tools/gen_university.py`, `tools/gen_employment.py` and their installers.

## What the Factory changed about the recipe

Three findings, all of which cost GPU rounds and none of which are specific to that
building. They belong here rather than in one generator.

### 1. Not every building is a cel

The University and the Employment Office are cels: one rectangle, sign and all. **The
Factory is not.** Its building is painted into pic 11's BACKGROUND (cel 0) and only its
hanging sign is a cel — cel 11, 32x13 at (9,176). So renaming and rebuilding were two
independent edits, and the "generate with the sign band blank" step was unnecessary: the
model was handed a region with no sign in it at all.

**Check which kind each building is before planning the work.** `decode_pic_full.py --cels`
lists the fifteen cels with their sizes and stamp positions; anything in the hotspot that
is not covered by a cel is background.

### 2. Generate the BUILDING's bounding box, not the hotspot

This cost six rounds. The Factory hotspot is 61x38 and the building occupies **35%** of it
— the rest is river, lavender ground and a horizon, all of which the blend throws away. The
model spent its detail budget on scenery and the small irregular piece that was kept had
almost no structure in it: six rounds of prompt and parameter changes produced variations
on "a close-up of a brick wall".

Cropping to the building's own bounding box, 32x27, took the building from 35% to **94%** of
the canvas and fixed it in one round. The Employment Office never had the problem because
its cel IS its building.

    The hotspot is a CLICK TARGET. It has nothing to do with where the art is.

512x432 is also close to SD1.5's native 512x512, which is where it behaves best.

### 3. The blend mask is a SILHOUETTE, not a bounding box

`blend_surround` originally kept a rectangle. That is fine for a building that is very
nearly its own bounding box, and wrong for one with a stepped roofline: it pasted a
hard-edged slab of brick over the scene, the worst-looking thing produced in this work.

The outline is taken from Sierra's own pixels instead. Here every one of her surroundings
is blue-dominant and light — lavender #6070C8, water #B8C8E0, the darker blues #405890 —
and no part of the building is, so `blue and not dark` separates them and the building is
what remains. The same trick should work elsewhere; the *test* is per-building and belongs
in the data.

## Corrections to the four surfaces above

- **The job-list backdrop is the Employment Office's own, not a shared one.** `showingJobs`
  in `MainViewModel` requires `here.Id == LocationId.EmploymentOffice`, so view 706 cel 1 is
  only ever drawn there. Other shops do NOT have an equivalent, and Socket City does not.
- **The Factory has two surfaces**: the board sign and view 705's IC chip. **Socket City has
  two**: the board script and view 808. Neither is in the picPatch — it covers game x
  68..250 and they sit at x 9..40 and x 255..298.

## More standing decisions

- **Spoken clips still not renamed, now on eight more.** The Factory's 8 greetings all say
  *"Welcome to the Factory"*; Socket City's 16 all say *"Welcome to Socket City"*. Same
  judgement as Hi-Tech U.
- **No bright horizontal band low in the init.** A soft light stripe near the bottom is what
  haze looks like, and the model painted mist over the building's base for two rounds
  through `smoke, steam, fog` in the negative. Removing the band fixed it; the negative
  never could.
- **The init needs tonal range, not just extent.** An init with no light in it anywhere
  returns an image with no light in it: a yard at #4A4642 under a brown building produced a
  beautifully textured dark alley. Mid concrete, #8A8580, fixed it.
- **Never let a new negative term evict an old one.** `flat` was deleted to make room and
  the result was a vector illustration. Take the space from synonyms.

## The generator is now DATA, not code

`tools/buildings.py` holds one entry per building and `tools/gen_building.py` /
`tools/install_building.py` read it. The three hand-written generators are kept for the
three that shipped before the extraction, and are not the way to add a building.

Socket City was the first customer and **nothing turned out to need its own code path.**
Everything that differs between the four is a field:

| field | why it differs | values seen so far |
|---|---|---|
| `source` | some buildings are cels, some are painted into the background | `cel:08`, `cel:09`, `cel:10`, `background` |
| `region` | **the BUILDING's box, never the hotspot** | 32x27 to 59x41 |
| `surround` | how to tell building from scene | `green`, `blue_light`, `rects` |
| `init` | flat colour boxes, colours sampled from Sierra | per building |
| `prompt` | material vocabulary only, no colour words | per building |
| `name_rect`, `name_ink`, `name_plate` | where the name goes and whether it needs a plate | per building |
| `surfaces` | interiors the detector found | 0, 1 or 2 |
| `device_rect`, `devices` | a decorative device beside the name | only Socket City |

The one thing that resisted is worth stating: **`find_name_art.py`'s colour predicates are
still code.** `darkgreen`, `redish`, `greyish` are per building, and `greyish` already
produces a false positive at exactly the 0.70 threshold. That wants to become a per-building
colour test in the table, and it is the first thing to fix when a fifth building needs a
predicate none of the four have.

## What Socket City added

- **A device can be a letter.** Sierra drew the S of `Socket` as an electrical plug - a
  letter and the shop's joke at once. Masking the name by colour would have deleted it.
  Measured before touching anything: the plug is 36% of the red pixels, body 10x13, so the
  name takes most of the area and a standalone device fits beside it.
- **At 13 px wide a device must earn its space, and colour decides it.** Both candidates
  were generated and composited. The PLUG does not read: it is the same red as the
  lettering, so at true size it merges with the name into one red mass. The BULB reads,
  because amber separates it from both the red name and the blue glass. **The deciding
  factor was contrast with the lettering, not how good the object looked on its own** - the
  plug is the better photograph of the two at full size.
- **Check what a plate covers before painting it.** The first `view_808` plate ran to y 22
  and would have erased the top of `APPLIANCE STORE` at y 20..23 - a line that is not the
  brand and was never asked to change.

## What QT Clothing added

**A name split across two bands** — the only thing in five buildings to need a new field.
Sierra puts `Q.T` on a narrow dark gable and `CLOTHING` across the fascia below it, two
different widths. Setting both lines in one box centres them on a common width and throws
away a hierarchy she drew on purpose, so `Building.name_rects` takes one rectangle per
line. Everything else about QT was already in the table.

**Measure the band the lettering sits ON, not just the lettering.** The first init put the
striped awning at y 9, where her lettering band is. Her stripes actually start at y 14 and
`CLOTHING` sits on a DARK FASCIA above them. Generating it the wrong way round gave green
letters on pink-and-white stripes, which at true size breaks up and is barely readable —
the same contrast failure as Socket City's red plug on red text, found the same way, by
looking at 1x rather than at 12x.

**A detector mask must be dense enough to encode the LETTERFORMS.** The first QT mask
spanned both bands, y 2..13, sweeping in the dark gap between them. That made it mostly
background, so the score described "some green letters on a dark band" rather than these
particular shapes: it reported EIGHTEEN surfaces, including view 706's gold-on-green
employers panel, and then flagged this port's own `COMMON THREADS` as Sierra's name still
being present. Narrowed to her `CLOTHING` band alone, where the glyphs fill the box, it
reports two and passes. **A mask over a band is not a mask of a name.**

**No device, checked rather than assumed.** The full stop in `Q.T` is punctuation and the
red and green shapes at the sides are garments on rails. Socket City's plug was a letter
doing double duty; this shop has no equivalent and none was invented.

## `Board.All`'s number is a PLACE INDEX, not a cel index

The table in `Board.cs` reads `new(LocationId.QtClothing, "QT Clothing", 47, 9, 251, 82,
311, 118, ...)`. **The 9 is the Place index and 47 is the marble-path step. Neither is a
cel.** QT Clothing is cel 7. The coordinates that look like they belong to it — (277,113) —
are `clothingP` in `MainViewModel.Animation.cs`, the SHOPKEEPER'S animation anchor.

This is the hotspot-versus-art confusion in a third form and it has now caught two people.
**Find the cel by overlap**: decode pic 11 with `--cels`, and the cel whose stamped
rectangle lands inside the Place's hotspot is the building. `Board.All`'s rectangle is a
click target and says nothing about where the art is or how big it is.

## Mask density: BOTH bounds, and both have now been hit

`find_name_art.py` prints the density of every mask and warns outside **25-55%**. Aim there.

| | density | what it matched |
|---|---|---|
| QT, both bands + the gap | 13% | 18 surfaces, incl. our own COMMON THREADS |
| Black's Market, `paleish` over `market` | 5.7% | **64 surfaces**, incl. our own BARE NECESSITIES |
| Black's Market, `litglyph` over `market` | 88-100% | any bright rectangle |
| Black's Market, `litglyph` over `black's` | **33.3%** | exactly 2, zero false positives |

**Too sparse and the score describes "letters on a band" rather than these letters. Too
dense and it describes "a bright rectangle".** The upper bound is the newer half and it is
not obvious: her `market` band sits on a LIGHT ground, so a brightness predicate over it is
nearly all TRUE and stops encoding glyph shapes entirely. Her `black's` band is
light-on-dark and discriminates.

The right mask gave the cleanest scan of any building so far — two hits, no near misses.
**Check the density before running the scan**; it is one cheap measurement against several
minutes of searching 829 files for the wrong thing.

## What Black's Market added

- **The first building that is not a glazed shop.** Three of the first five leaned on glass,
  reflection and depth behind the window. This one is concrete, roller shutter, worn paint
  and hard shadow, with no display at all — the vocabulary has to move or it comes back
  looking like the others. It is the shop where the game rolls a 1-in-50 mugging
  (`LocationId.BlacksMarket => 50`), and the soundtrack already leans a semitone above the
  tonic for it, so the building is meant to be the least comfortable on the board.
- **The dark mass at its bottom-left is NOT a figure.** Checked before masking, because
  deleting a lurking figure outside the mugging shop would have been a worse version of
  nearly erasing `APPLIANCE STORE`. Enlarged, it has a trunk-like vertical above an
  irregular organic mass with no head-and-shoulders articulation: a tree base in deep
  shadow. The shadow is kept and does the work; no person was invented.
- **`name_rects` took its second customer immediately**, which is the evidence it is a real
  field rather than a special case. Her short-over-long bands take BARE over NECESSITIES
  exactly as they took `black's` over `market`.

---

# Zain rejected the first six, and the recipe changed

Two sentences: the buildings look like AI-generated nonsense, and the signs look like words
placed over an image. Both are true and both were then confirmed on the real pixels. Four
defects, each measured, each with its own answer.

## 1. The sign was never an object

`COMMON THREADS` at 12x is flat vector type on a black fascia: one colour, no thickness, no
drop shadow, no fixing, no grain, no reaction to the scene's light — and the only
hard-edged thing in a soft photograph. That is not a figure of speech, it is literally what
`install_building.letter_region` does, and it is the direct consequence of the rule that
solved garbled glyphs: generate with the band blank so the model never sees text, then
stamp. **The rule fixed one defect and created another.**

**The replacement is two stages, and neither of them asks a model for text.**

*Stage one — `tools/sign_board.py`, geometry and material by construction.* A painted
board with a sawn edge and a bevelled thickness, a cast shadow thrown onto the façade, bolt
heads through its corners, a constructed timber grain, and signwritten letters built as
three passes — a shade thrown away from the light, a highlight towards it, then the body.
Optionally posts, for a sign that stands rather than hangs on a wall. All deterministic, so
none of it can garble.

*Stage two — `tools/sign_building.py`, married with a masked low-denoise img2img.* The
model does the one thing it is unambiguously good at — lighting, wear, the way paint sits
on timber — while the layout is already correct and only has to survive. The ControlNet
conditioning is the **soft-edge map of the constructed image at conditioning scale 0.80**,
which is what pins the glyph outlines through the pass: the model is handed the letterforms
as structure rather than asked to imagine them.

**Both stages happen at the GENERATION's resolution**, 16 px per cel pixel, so the
downscale antialiases sign and building together, once. A sign composited after the
downscale is resampled on a different schedule from the building and will always read as a
sticker. `install_building` now prefers `signed_*.png`, then `constructed_*.png`, and
stamps type only for a building with no `sign` in the table.

### The marrying pass needs its OWN negative, and this is not a detail

The shared `NEGATIVE` forbids `text, letters, words, writing, lettering, signage`. Running
it over an image whose sign band is full of letters is an instruction to **delete them**.
It is right for the façade, where the model must invent nothing, and exactly wrong here,
where it must preserve something. `sign["negative"]` is the shared list minus every
text-suppressing term.

### And the drift is measured, not hoped for

`sign_building.py --who X` prints the authored-versus-recovered glyph IoU and the centroid
shift, before and after the img2img, in high-rung pixels and in cel pixels. If the letters
move, the answer is a lower strength, not a shrug.

## 2. The sign was the wrong size, and the inflation was ours

Measured on the board, and **the first reading of this was wrong and had to be corrected** —
the plate in `assets/png/pic_11.png` is Sierra's, not ours, because `assets/png` is
untouched (verified byte-identical to cel 11 at its stamp point).

| | plate | vs the building 32x27 | vs the hotspot 61x38 |
|---|---|---|---|
| Sierra's own board | 30 x 12 | 94% wide, 44% tall | 49% wide, 32% tall |
| **what we shipped** | **53 x 15** | **166% wide, 56% tall** | **87% wide, 39% tall** |
| the rebuild | 32 x 12 | 100% wide, 44% tall | 52% wide, 32% tall |

`gen_factory.py` says it outright — *"the chosen sign is 55x21 at game (9,171)"*. The board
really was inflated 1.8x wider and 1.3x taller than hers. **State the proportion in the
table entry**, as `pawnshop` and `factory` now both do.

### What the proportion costs, said plainly rather than hidden

`NEEDFUL THINGS` is fourteen characters against Sierra's nine. On a band of 34 x 7 cel px —
87% of the façade's width and 25% of its height — it is legible at the 4x and 12x rungs the
game actually draws and is **mush at 1x**. Two lines would read at 1x and need eleven rows,
39% of the façade, which is a hoarding and not a sign. A fourteen-character name on a 39px
building cannot be both a band and legible at 1x. The band is the half that was asked for.
`THE INDUSTRIAL REVOLUTION` is twenty-five characters and has the same problem today.

## 3. A ring of Sierra's own pixels survives around four of the six

**Every cel in pic 11 is 100% opaque** — there is no alpha silhouette anywhere, which was
checked before anything was blamed on one. The fringe is our own `surround` rects, which
are inset from the cel and therefore leave her art in a border all the way round.

| building | surround | kept as Sierra | of that, in the outer 2px | outer 1px border |
|---|---|---|---|---|
| Open Door University | rects | 0.0% | — | **0% Sierra** |
| **Needful Things** (rebuilt) | rects | 6.1% | — | 22% Sierra |
| **The Industrial Revolution** (rebuilt) | blue_light | 22.5% | — | 28% Sierra |
| The Industrial Revolution (as shipped) | blue_light | 5.8% | 96% | 25% Sierra |
| Watt a Bargain | green | 6.6% | 46% | 23% Sierra |
| Bare Necessities | rects | 13.5% | 98% | **80% Sierra** |
| Hire Ground | rects | 15.0% | 100% | **100% Sierra** |
| **Common Threads** | rects | **25.4%** | 58% | **91% Sierra** |

Common Threads is a quarter 1990 art. Hire Ground has a complete one-pixel frame of it.
Only the University is clean, because its keep rect is the whole cel. For the two colour
tests the kept pixels are mostly genuine scenery, which is correct — but their borders are
still a quarter hers.

**The rule: the generated art has to reach the cel's edge wherever the BUILDING does.** The
Pawn Shoppe's rects run the full width from y 11 down, so its building edge is entirely
generated and its 67 kept pixels are all sky and lawn in the roof corners — 22% of its
outer border is Sierra's, and all of it is her sky and her lawn.

The Factory's figure went UP, from 5.8% to 22.5%, and that is correct rather than a
regression: its region grew from the building alone to the union of the building and the
sign's yard, so it now contains much more of her river and lavender ground, which the blend
is supposed to keep. **It is also where the rebuild's remaining visible defect is** — those
kept blocks read as flat pasted shapes now that the building beside them is sharp.

The four with the real fringe are not re-run in this round; they wait with the rest.

### The Factory's pale blue block: extending the region is NOT the fix

Measured, pixel by pixel. The 419 kept pixels sit in exactly two blocks:

| | where | note |
|---|---|---|
| the river | x 9..24, y 155..174 | Sierra's water, left of the building |
| **the pale blue block** | **x 52..67, y 184..190** | the one visible bottom-right |

**Both are already inside the region** (x 9..67, y 151..190), so there is nothing to extend
— the `blue_light` blend is *keeping* them, because they are her blue-dominant road and
water. One `force` rect would generate them instead.

**But 223 of the 419 are the road the marble walks on**, and that road continues outside the
region in her art, so forcing it puts a hard seam at x 68 where generated ground meets her
lavender. The options, costed:

- **Leave it.** No seam, her road stays blocky next to our brick.
- **Extend right to x 74 and down to y 192** — 66x42, which is 1056x672 = 709k px, still
  under the 810k ceiling — so the seam lands where Hire Ground's own cel starts at x 75
  rather than in open ground. One generation round. **This is the one I would take.**
- Regenerate the whole bottom edge of the board as one piece. Correct, and much larger.

## 4. The softness was the ControlNet conditioning, and it is named

**The conditioning image was built from a LANCZOS upscale of the 39x28 init.** A 16x
LANCZOS upscale has no hard edge in it anywhere — every boundary is a sixteen-pixel ramp,
because there are sixteen generated pixels per cel pixel. So the softedge map handed to
ControlNet was telling the model that *every structural boundary in the building is sixteen
pixels wide*, and at a conditioning scale of 0.35 it painted exactly that, over the whole
frame.

### The test that found it, and the two metrics — one of which was useless

The right test was the cheap one: **crop the generation at 100% and look for a single hard
edge.** There was not one, anywhere. That also kills the depth-of-field theory on sight —
a defocused photograph still has a focal plane, and this had none.

The number that matches that test is the **p99 of the one-pixel luminance step** over the
whole frame. A real photograph with detail runs 20–60; Sierra's upscaled 12x cels run
11–20.

| | p99 one-px step |
|---|---|
| the shipped recipe, control 0.55, LANCZOS conditioning | **11** |
| the same, control 0.15 | **28** |

**The first metric tried was worthless and nearly sent the whole investigation the wrong
way.** It was an edge-width ratio, mean |d1| over mean |d4|, and it put Black's Market at
0.42 against QT's 0.28 — those two being concrete-and-mullions against awning-and-glass.
It was measuring *texture density*, not focus, exactly the trap CAST.md §7b describes. On
that number the three ablations below all read as "no change" when one of them was in fact
decisive on the right metric. **Do not reuse it.**

### What was tried and did NOT work, so nobody spends the rounds again

| tried | result |
|---|---|
| conditioning blur at 1/4 the radius (`cond_blur`) | nothing — the softness is in the UPSCALE, not in the Gaussian |
| `deep focus, everything in focus` + `shallow depth of field` in the prompts | nothing. Both terms were then taken back out; the negative had reached 77/77, the exact cliff CAST.md §1 is about |
| strength 0.62 / control 0.65 instead of 0.84 / 0.55 | visibly flatter and worse |
| any resize | exonerated by measurement: QT is 0.281 at its own 640x576 and 0.288 at 12x |

### It is the SCALE, not the edge width — and a crisp conditioning did not rescue it

The obvious follow-up was to make the conditioning crisp so the scale could stay high.
`synthetic_init` now builds the control image from a **NEAREST** upscale instead of a
LANCZOS one, giving thin hard lines in the right places rather than wide ramps everywhere.
**It barely moved the number.**

| conditioning | strength / control | p99 one-px step |
|---|---|---|
| LANCZOS (as shipped) | 0.84 / 0.55 | 11 |
| LANCZOS | 0.90 / **0.15** | **28** |
| NEAREST | 0.86 / 0.50 | 13 |
| NEAREST | 0.74 / 0.18 | 13 |

So the mechanism is not edge width. **A softedge map derived from a flat block painting
says "there is no edge here" across almost the whole frame, and at a conditioning scale of
0.35 and above the model obeys that and renders no detail there.** The crisp version says
the same thing, just with tidier lines.

And the fourth row is the other half of it: at strength 0.74 the Gaussian-blurred init
itself dominates and the result is soft even at control 0.18. **Both smooth inputs have to
be released at once** — the model only paints detail when it is held by neither.

### Which makes it a straight trade, and it has to be stated as one

| | sharp | right massing | fascia stays blank |
|---|---|---|---|
| high control | no | yes | yes |
| low control, high strength | **yes** | no — it drifted to a boarded-up clapboard shop | no — it **painted garbled text across the fascia** |

Garbled lettering on the band is the one failure this whole programme exists to avoid, and
it is what turns up the moment the model is given enough freedom to be sharp. The
mitigation available to us is that the band is covered by a constructed board afterwards,
so text there is overpainted rather than shipped — but the massing drift is not mitigated
by anything.

    the init is for colour, position and extent, and it is smooth on purpose.
    the conditioning is for STRUCTURE, and it is nearly empty by construction.
    holding the model to either of them is what costs the detail.

That is the finding. The right answer is probably a conditioning with real structure in it
rather than twenty box boundaries — a depth or normal map, or a canny map taken from
something that actually has detail — and that has not been built.

## What did move, and it was the massing

The deeper half of the complaint is not focus at all. At 39x28 what survives a box mean is
silhouette and value structure, and a busy photograph has neither. The first Pawn Shoppe
init gave the upper half of the cel to one eleven-row band of dark varnish and the lower
half to two bright windows, and **what came back was not a building — it was a close-up of
a shop window full of brass spheres, with the fascia a black void.**

The fix was arithmetic, not prompting: four alternating values down the façade's height —
dark cornice, **mid render wall**, dark painted band, dark shopfront with bright glass,
light step. The mid wall is the value that was missing, and it is also what lets a dark sign
board read against it. The prompt was re-ordered to lead with `the facade of a small old
pawnbroker's shop` rather than with the window, because the first one spent its middle on
`a deep plate glass window crowded with clocks instruments and bric-a-brac` and the model
obliged.

## Measuring the light, and the shadow that came out upside down

`sign_board.light_from` blurs the façade hard and reads the gradient of what is left. Freely
measured, the Pawn Shoppe returned **(-0.34, +0.94) — light from below** — because a
shopfront's brightest object is its own lit window, which is an emitter at the bottom of the
frame and not the sun. The board duly threw its shadow upwards onto the wall.

So only the horizontal component is measured, over the upper 55% of the façade, and `ly` is
fixed negative: an exterior building in daylight is lit from above and the only open
question is from which side.

## 5. The model writes on the building, and the blank band never stopped it

The Pawn Shoppe's facade came back sharp and carrying **`PADEMOKIK BATEWEIS ILP`** across
the course below the sign — model-hallucinated words on a board whose whole complaint is
that it looks AI-generated.

**The rule was never "the model will not generate text". It was "the model will not
generate text where we told it not to."** Same shape as every guard failure here: it
covered the place we were looking.

### Three things were tried. Only one of them works.

| | verdict |
|---|---|
| more negative-prompt terms | **no.** Decoding the token ids back shows all nine of `text, letters, words, writing, lettering, signage, numbers, logo, watermark` arrive intact at 68/77, well inside CLIP's window. They were present, they were not truncated, and the model wrote words anyway |
| a prompt that does not imply writing (`premises`/`frontage` for `shop`/`shopfront`/`display`) | **it worked and it cost too much.** No text — and the facade came back pale, cold and generic with no pawnbroker in it. Category is the one thing only the prompt can carry; `shop` was carrying it |
| removing the dark band under the sign | **it worked and it cost too much.** Same pale cold result: colour lives in the init, so taking the maroon out of the init takes it out of the picture. Breaking the band into panels with pilasters kept the text away and was still cold, and softer |

### So the answer is a detector that fails the build: `tools/no_text_guard.py`

No OCR and no reading. A row of text has a structure a building does not: many small
high-contrast blobs, on a common baseline, of similar height, but of **varying width** and
**packed tightly**, and **not repeating vertically in the same columns**.

The last two are the whole difference between a guard and a nuisance, and both were found
by calibrating rather than by reasoning:

- **Pitch.** The first version fired on the window rows of two perfectly clean facades.
  Letters sit at 1.6–2.1 cel px of pitch; rows of window panes sit at 5.7–5.9. The bar is
  3.3, with room on both sides.
- **Grids.** The Factory then failed on its own three courses of multi-pane windows — a
  mullioned window *is* a row of packed rectangles. What it is not is unique: the panes
  repeat vertically in the same columns and a sentence does not.

Calibrated on five real frames, two that must fire and three that must not, and it now gets
all five right. **Run it at the generation's own resolution** — the same gibberish frame at
half size passes, because the blobs fall under the minimum glyph height.

## What shipped, and the trade that is still open

**The sharpness fix and the no-text fix are the same lever pointing in opposite
directions.** The detail and the hallucinated signage are both bought with the same freedom:

| | p99 sharpness | the building | the fascia |
|---|---|---|---|
| 0.84 / 0.55 | 11 — soft | right, warm | clean |
| 0.92 / 0.30 | 26 — sharp | right, warm | **`PADEMOKIK BATEWEIS ILP`** |

**Needful Things ships at 0.84/0.55** — the facade Zain was shown and approved on massing
and material, still softer than it should be. The sharp variant cannot ship. This is not
resolved and is written down rather than buried in a parameter.

## The marrying img2img did NOT earn its place, on either building

Run at 0.22, 0.12 and 0.07, it took the bolt heads off the board and flattened the bevel
every time, and added no material the construction did not already have. `marry: False` on
both, and the stage stays in the pipeline in case a different sign wants it.

The drift numbers say the same thing once read properly: survival IoU 0.887 with the
centroid moved 0.22 cel px — **and side by side the letterforms are identical.** What the
number reports is the loss of the signwriter's relief, not glyphs moving. The metric's own
identity check, `drift(built, built)`, returns exactly 1.000, so the metric is sound; its
first version was not, because it scored each image against the AUTHORED mask and the
construction draws every glyph three times, giving IoU 0.516 on an image that reproduces
the mask exactly.

## The Industrial Revolution: what got better and what got worse

| | before | after |
|---|---|---|
| sign | flat mustard rectangle, 1px border, no thickness, no shadow, nothing holding it | gold board with a bevelled frame, bolts, **two posts into the yard** and a cast shadow |
| size | 53 x 15 — 166% of the building's width | 40 x 13 — 125% |
| building | soft brown mush | sharp brick with three window courses |
| **legibility** | **readable** | **worse — and that is a real regression** |

`THE INDUSTRIAL REVOLUTION` is twenty-five characters. At Sierra's own 32x12 proportion it
is unreadable; 40x13 is the compromise that is still a quarter narrower than what shipped.
**A 32x27 building cannot carry this name at Sierra's sign proportions** — that is a fact
about the name, and the name is Zain's.

One more defect visible in the after and not fixed: the river and lavender ground that
`blue_light` keeps now read as flat pasted blocks against a sharper building. Sierra's
scenery did not get worse; the thing beside it got better.

## 5a. JUDGE AT 12x. The 1x cel is a by-product, not the design target.

**This supersedes everything above that reasons from the 1x cel, and two conclusions in
this file were wrong because of it.**

Zain: *"increase the resolution because it doesn't need to be low res... we should not be
constrained by resolution, it's 2026"*. He is right and the constraint was invented here.
The board ships at **3840x2400** and `RenderScale` takes the largest factor at which a file
exists, so **12x is what the player sees**.

**Withdrawn claim 1: "the lettering is mush".** It is not. Cropped out of
`assets/png12x-cast0/pic_11.png` at native resolution, `THE INDUSTRIAL REVOLUTION` is clean
dark green type on a bevelled brass plaque with corner bolts and a cast shadow, and it is
the sharpest thing in the frame. The "mush" came from rendering the 1x cel at 7x
nearest-neighbour — **an image nobody ever sees**. It was reported twice and both times it
was the instrument, not the art. Same class of error as judging a matte by eye on a dark
background: the measurement invented the defect.

**Withdrawn claim 2: "the massing has to survive a downscale to a few dozen pixels, so
design for the target resolution".** The principle is right and the resolution named was
wrong. Designing inits for a 39x28 thumbnail threw away detail the 12x rung carries
easily. **Draw inits with the detail 12x can hold** — brick courses, window reveals, roof
structure, signage hardware. A window is 60x72 device pixels at 12x and can have real
glazing bars; it does not have to be a 4x3 hole.

**And the p99 one-pixel-step metric does not predict what 12x looks like.** The Factory
measured p99 9 — "soft" — and its 12x crop is sharp. Use it to compare two renders of the
same subject if at all; never to decide whether something is good enough.

**What survives from the old rule:** the blurry, blocky, pasted-looking material around
these buildings is **Sierra's own 1x art upscaled**, not ours. That is a real defect and it
is the fringe problem in §3, now visible for what it is.

## 6. NO SIGN MAY OVERLAP THE MARBLE'S SWEPT FOOTPRINT

Zain sent a screenshot of the player walking **through** The Industrial Revolution's sign.
He was right, and it was never going to be caught by looking at the board: the marble is
only drawn where it happens to be.

**Compute it from `MarblePath`, never from the picture.** The marble is view 0 loop 2, 9x8,
drawn at `(mx - w/2, my - h)` — bottom-centre, confirmed in `MainViewModel.cs:2193`, not
assumed. Swept over all 170 points it covers **9392 px, 14.7% of the 320x200 board**.

Checking every installed sign against that sweep found **two** collisions, not one:

| building | sign, in game coords | swept px |
|---|---|---|
| **The Industrial Revolution** | x 11..50, y 175..187 | **379** — path steps 107–114 run straight through it |
| **Common Threads** | `COMMON` x 265..288, y 82..87 | **10** — the marble's bottom-left corner clips it at step 36 |
| | `THREADS` x 258..295, y 89..94 | 1 |
| Needful Things, Bare Necessities, Hire Ground, Watt a Bargain, Open Door University | | 0 — clear |

QT's is a one-step graze rather than a walk-through, which is why nobody saw it. It is fixed
by narrowing `COMMON` from 24 to 19 px and moving `THREADS` one column left, keeping the two
very nearly concentric. **That building still carries the rejected flat stamped type** — only
its position is corrected here.

## 7. What the Factory needed, and what it cost

- **The sign came off the ground and onto the wall.** A hanging board on posts was Sierra's
  own sign type and her own position, and the path goes through it. It is now a painted
  wall sign at game x 29..64, y 166..176 — **0 swept pixels** — which is 36x11 on a 42x33
  façade, **86% of its width and 33% of its height**. The posts went with it: a post under
  a wall sign holds up nothing.
- **The building grew to carry it**, which was Zain's suggestion: 32x27 → **42x33 at (26,151)**,
  a third wider and a fifth taller. The largest rectangle in this hotspot the marble never
  touches is x 28..67, y 151..176; a building may overhang that downwards because the marble
  walks *in front of* a building — it is the sign that may not be walked through.
- **It reads as a factory by its silhouette**: a chimney breaking the roofline, a sawtooth
  north-light roof drawn as a stepped top edge rather than as texture, a band of tall
  windows, a loading bay. The previous version was a box with three courses of holes and
  read as a brick house, which is exactly what Zain said.

### The init had no sky in it, and that is why the first two rounds had no chimney

The background was YARD across the whole frame, so the roof ran to the top edge and the
chimney had nothing to stand against. **Both** 0.92/0.30 and 0.84/0.55 came back as a
close-up of a brick mill wall — no chimney, no sawtooth, no loading bay. Painting SKY first
and the ground only in the lower third produced the chimney and the roofline in one round.

    A silhouette needs something to be a silhouette AGAINST.

### A region is only as big as what the LAST install wrote

A gold bar survived at **y 189..190, x 10..62** after the rebuild. The region ran to y 188;
the *original* install's board reached y 190. The pipeline only repaints `region`, so
anything an earlier install wrote outside it stays on the board forever. The region is now
59x40. **When replacing an install, size the region to the OLD footprint, not the new one.**

### The chimney, fixed — and what it took

The first one was 6x13 butted against the shed's left edge and was **invisible** in the
installed art: the model rendered it in the same brick value as the wall behind it, so it
had no silhouette, which was the entire point of it. Two changes fixed it and both were
needed — it now stands apart with **a column of sky between it and the shed**, and it is
painted a dark sooty value nothing else in the frame carries. Six rows of it are against
open sky. It reads at 12x.

### The sawtooth roof does NOT come out, at any of the three settings

The init paints three teeth as a stepped silhouette and all three renders returned a
continuous slate roof with **dormer windows** instead. Dormers are a plausible north-light
substitute and they look right, but it is not what the init asked for and it did not
improve with more or less control. Unresolved; recorded rather than claimed as intended.

### Judged at 12x, the three settings reverse the old conclusion

| | material at 12x | composition |
|---|---|---|
| 0.92 / 0.30 | **best by a distance** — individual brick courses, crisp slate, a stone lintel, a downpipe | **lost** — one window, no roof structure, no loading bay |
| 0.88 / 0.45 | close behind | **holds** — chimney, roof, two courses of windows, pilasters |
| 0.84 / 0.55 | softest | holds |

**0.88/0.45 ships.** The earlier verdict that 0.92/0.30 "loses the massing" was made against
the withdrawn 1x criterion; judged at 12x it is genuinely the better *rendering* and still
the wrong *building*. Sheet: `tools/samples/factory_settings_at_12x.png`.

### The sharper setting was tried again, as asked, and it still does not work

With `no_text_guard` armed the text risk is handled, so 0.92/0.30 was retried. The guard
passed — **and the composition was lost anyway**: no chimney, no sawtooth, no loading bay,
just a wall. The trade did not go away, it moved: high strength now costs the MASSING
rather than inviting text, and at 42x33 the massing is the whole building.

| | p99 one-px step | massing |
|---|---|---|
| 0.92 / 0.30 | 14 | **lost** |
| 0.84 / 0.55 | 8 | lost (no sky in the init) |
| 0.88 / 0.45, sky in the init | 9 | **right** |

Shipped at 0.88/0.45. Still soft, and said so.

## What it would take to fix the Sierra-pixel fringe

Not done in this pass. Common Threads is **25.4%** her art and Hire Ground has a complete
one-pixel border of it. Both use `surround={"mode": "rects"}` with keep rectangles inset
from the cel, so her art survives in a frame all the way round.

The fix is cheap for Hire Ground and harder for Common Threads:

- **Hire Ground** keeps `[(1,1,45,13),(2,14,44,31)]` on a 47x34 cel. Its building IS the
  whole cel, so widening those to `(0,0,46,13)` and `(0,14,46,33)` removes the frame
  outright. **No regeneration needed** — the generated façade already covers it; it is an
  install-only change, about ten minutes.
- **Common Threads** is the real one. Its keep rects follow a stepped gable, so the fringe
  is partly genuine sky either side of the roof and partly her building's own edge. It
  needs the per-pixel scene test the Pawn Shoppe uses (green + sky, measured) rather than
  hand-drawn rectangles, and then a reinstall. Half a day, no GPU.

Neither should be done before Zain has judged the sign technique, because both buildings
are due a full rebuild anyway and the fringe would be fixed as part of it.

## 8. THE DOORS. Every building has one, and it is a separate animated cel.

Zain: *"Can the factory door look like an actual factory door!? why is it just a space!?"*

**The anchor convention was the first thing to get right, and the obvious reading is wrong.**
`AddCastView` draws at **`(x - w/2, y + 1 - h)`** — `MainViewModel.Animation.cs:299-301` —
so a door is **bottom-centre** anchored, not top-left. The Factory's door is view 751 loop 5
at (50,181) with a 10x11 cel, so it covers **game x 45..54, y 171..181**, not x 50..60,
y 181..192. Read as top-left it looks as though it hangs eight rows below the building;
read correctly it is comfortably inside the façade.

### All thirteen, checked against the rebuilt façades

| | door covers | verdict |
|---|---|---|
| Factory | x 45..54, y 171..181 | inside — **but it overlapped its own sign**, now moved |
| **Pawn Shoppe** | x 216..225, **y 23..34** | **OUTSIDE — 2 of 12 rows hang below a façade that ends at y 32** |
| Black's Market, Hire Ground, Open Door U, Socket City, QT Clothing | | inside |
| the six not yet rebuilt | | n/a |

So it had happened twice. The Pawn Shoppe's fix is to extend its region from 28 to 30 rows
and regenerate — one round, not done here.

### Two faults, not one, and the first is the one that reads as "a space"

1. **The façade had no door in it.** `AddCastView(DoorView, …)` only runs **while the door
   is animating**, so on the static board no door sprite is drawn at all. If the art has a
   hole there, the player sees a hole. The façade must therefore paint the doors **shut**,
   and the four cels open them. A dark void was tried first and the model rendered it as
   another window — *a dark rectangle in a brick wall IS a window*. A light stone lintel
   and jambs around a warm timber panel is what makes it read as a doorway.
2. **The door cel was still Sierra's 1990 art.** `view_751_l5_c*` exists in `assets/png` and
   `assets/png12x` but **not in `png12x-cast0`**, and `RenderScale` takes the largest factor
   present — so a cartoon door was drawn over photoreal brick. `tools/door_cels.py` now
   builds all four cels into the overlay at both rungs.

### Her geometry, our material

`door_cels.py` reuses **her alpha verbatim**, so the sprite footprint is exactly the
original's and nothing new can overhang. The leaf geometry is **parametric from her measured
widths** — 5+5, 4+4, 3+3, 1+1 as the doors swing out — because reading her per-pixel mask
literally gives ragged brown fragments: her art is heavily dithered, and the measurement is
the useful part of it, not the dithering. **The script coordinates are never moved.**

## 9. The guard caught a third false-positive class, and I shipped past it once

The slate roof's **standing seams** fired it: nine blobs, 3.2 cel px pitch, width variation
0.23 — through every test. Two more discriminators, both principled rather than fitted:

- **A cap line as well as a baseline.** Letters sit between two parallel lines; roof ridges
  running at an angle share neither.
- **Regular spacing.** A text line's biggest gap is a word space, two or three times the
  median. The roof's blob centres ran 12, 40, 81, 117, 153, 174, 249, 433, 486 — median gap
  36, maximum 184, a factor of five.

Six calibration frames now, two that must fire and four that must not, all correct. **The
two must-fire frames are kept as the anchor every time the guard is touched**, because
tuning a guard against the frames it got wrong is how you end up with one that never fires.

**And once, the guard failed and the art shipped anyway** — `gen_building.py` raised, but
the shell ran `install_building.py` on the next line regardless. A gate that the caller can
step over is not a gate. Installs are now run only on `$LASTEXITCODE -eq 0`.

## 10. "A weird rectangle" — and it was not the river, it was the REGION BOUNDARY

Zain spotted it unprompted. The diagnosis everyone had, mine included, was the pale slabs of
her river and road kept inside the region. Cropping the corner at 12x shows something more
basic: **the whole generated patch reads as a rectangle**, with four hard edges in open
ground exactly where the region happens to stop.

`blue_light` keeps what is blue and light. Her **grass** above the roofline and her ground to
the sides are not blue, so they counted as *building*, got generated, and our sky and our
yard were painted over her scene. The slabs inside it are the second thing you see.

**The fix is `surround={"mode": "rects"}` with the building's own silhouette** — stepped at
the roof so the sawtooth notches show HER sky, not ours. Now only the building is generated,
her scene runs continuously into the rest of the board, and there is no inset at all.

## 11. `region` was doing two jobs that pull opposite ways

A region must be **big** enough to cover whatever an earlier install wrote, or that art
survives outside the new bounds forever (§7's gold bar). It must be **tight**, or the model
spends its detail on scenery the blend discards — the Factory's own six-round lesson, which
fired again here at **39% fill**.

So they are separate now:

- **`region`** is the building's box. Back to 42x33, **81% fill**, 354,816 px.
- **`clear`** is an eraser rectangle, as large as every earlier install's footprint put
  together (9,151)-(74,194). Erasing costs no GPU.
- **`clear_copy`** paints out what her *own background* contains — here the blank gold
  board, which is in cel 0 under cel 11's lettering, so restoring her original brought the
  sign straight back.

### The clear source has to be IDENTIFIED, not assumed — two wrong guesses

| source tried | result |
|---|---|
| LANCZOS upscale of the 1x cel | a visible patchwork — the board was upscaled by a model, a plain resample does not match its neighbours |
| `assets/png{F}x` | a fainter one. Measured over untouched board: **`png12x` differs from the cast0 base by a mean of 20.5 per channel** |
| **`assets/png12x-new` / `png4x-uni`** | **byte-identical, 0.00** |

`install_building.pick_clear_source` now tries the candidates and compares each against the
current board on a frame just outside the rectangle, refusing if none matches. **And the
control area has to exclude every other building's footprint** — the first version's frame
reached into Hire Ground's cel, which this programme has already replaced, so nothing could
ever match and it refused everything.

The patch offset is measured too: at (+32,0) the source still clips her own factory and
carries 13.1% warm pixels, which showed as a brown bar; (+32,+3) is 3.3%.

## 12. The Pawn Shoppe's door overhang is HERS — a correction

I reported it as a defect to fix. It is not. Her pawn shop cel is 39x28 at (201,5), so
y 5..32, and her door cel covers y 23..34 — **two rows below her own cel, onto the lawn**,
which at y 33..34 is green in her art. The door's threshold sits on the grass by design.
Extending our building two rows would make it taller than hers: a fidelity change, not a fix.
**Not done, and that is the right answer.**

## 13. Hire Ground's fringe: fixed, install-only

Keep rects widened from `[(1,1,45,13),(2,14,44,31)]` to the whole 47x34 cel. **15.0% kept
with a 100% one-pixel border → 0.0%.** No regeneration, no GPU. Common Threads still waits
for its full rebuild.

## 14. A DELIVERABLE THAT DISAGREED WITH THE BOARD, and the cause was mine

The sheet showed a clean integration. The installed `pic_11.png` had four visible defects.
That disagreement mattered more than the defects.

**The sheet was re-read from disk — and then I halved it.** Every panel went through
`im.resize((w//2, h//2), LANCZOS)` to fit the sheet, and the caption said "at 12x". It was
6x. **The downscale smoothed her blocky 1x art into something that looked like soft
scenery**, which is the whole defect, removed by the act of rendering it.

This is §5a's withdrawn-1x-rule failure happening again, inside the instrument built to
check for it.

    RENDER EVERY DELIVERABLE BY RE-READING THE INSTALLED FILE, AT NATIVE RESOLUTION,
    WITH NO RESIZE ANYWHERE IN THE PATH. Say so in the report.

## 15. What the four defects actually were

Measured off the installed board, not the sheet.

1 & 2. **The garbled brown chequering at the roofline and the green/lilac blocks were
SIERRA'S OWN ART** — her factory's brickwork at y 151..160 and her park beside it, showing
*above and around* our building because her factory is bigger than ours. At 12x her 1x art
is blocky, so next to a photograph it reads as corruption rather than as scenery.

**So "keep her scenery, generate only the building" was wrong in the other direction.** The
rule that works is narrower:

    Nothing of HERS may remain INSIDE the box, and the box must not read as a box.

Both halves are now arranged for: the region is sized so her factory is entirely inside it
and the whole region is generated, and the region's edge is handled by colour and by a
feather instead of by keeping her pixels.

3. **The pale rectangle had gained a brown outline** — the `clear_copy` patch pasted hard.
Its four edges were a tonal step, and an outline is exactly what makes a rectangle a
rectangle. It is feathered now.

4. **The hard rectangular silhouette** was the building running out to the region's own
borders, so there was nothing at the edges to fade. `scenery` + `edge_feather` paste the
building solid and fade only the generated scenery into her art — and the building is now
drawn two columns inside the region on every side so there is scenery to fade.

## 16. The no-text guard has reached the limit of geometry, and I stopped tuning it

Four false-positive classes now: window panes, mullion grids, slate roof seams, and
**courses of brick**. The first three had principled discriminators. Brick does not: *a
course of bricks is a row of similar, evenly spaced, aligned blobs*, which is the
definition of a line of text by every measure available.

The obvious next test — "masonry tiles vertically and a sentence does not" — **was measured
and failed**: the brick frame yields ONE candidate row, not many.

So the choice was to tune a fifth threshold until that particular frame passed, which is
how a guard stops guarding, or to make the override explicit. `gen_building.py --reviewed
"<what you saw>"` is the second: it demands a sentence, prints it beside the finding, and
leaves the finding on the record. **The guard was not retuned.** The Factory's install
carries this note:

> cropped generated_672x640.png at native 12x: the flagged row at cel y5.3 is the
> chimney's brick courses — seven bricks in one course. Masonry, not lettering.

## 17. THE BUILDING NEEDS A MATTE, NOT A FADE. Nothing is blurred to hide a seam.

Zain: *"It's blurring at the edges, how can you call that fixed?"* `edge_feather` traded a
hard rectangle for a **blurry** one — a smeared halo down the left, a wash under the base,
a fade off the right. The edge was still there, just out of focus, which on a screen reads
as the picture being broken rather than the art being wrong.

**The cast pipeline already solves this properly: generate, matte, composite with a
one-pixel antialiased edge.** The buildings pipeline was blending rectangles and blurring
the seam. `edge_feather` is gone and must not come back.

    WE ARE REPLACING HER ART, NOT BLENDING INTO IT.
    Every join lands on an EDGE the scene already has - a place where her own
    pixels already step - and the building's own silhouette IS such an edge.
    A UNIFORM SURFACE IS NOT A BOUNDARY. It is the worst place for a join,
    because a discontinuity there has nothing to hide in.
    If a seam cannot be put on a real edge, the REGION is the wrong shape.
    Nothing in this pipeline may be blurred to hide a seam.

The second and third lines were added after the wording above misled this agent into
siting three tile joins down the middle of a painted road marking - a uniform surface,
chosen BECAUSE it was uniform. It satisfied "a boundary the scene already has" as written
and inverted what it is for. Section 30 has the measurement: the three joins became the
three strongest edges in the region, against a strongest real feature of 4.01.

### A colour matte was tried and failed exactly as CAST.md §4 says it would

The init paints the surround in two known colours, so keying everything far from them
looked right. **It punched holes straight through the brick** — mortar and stone sit near
the background grey. The authored silhouette is exact and the right place to improve the
outline is the init, not a matte that second-guesses it.

### And the matte had a real bug: scenery boxes did not cut

The mask loop only ever set True, so a `PARK` box painted over the shed to make a sawtooth
notch left the matte solid underneath. `paint()` composites the init **in order** and so
must the matte, or it is not the silhouette the init describes.

### Two authoring faults the crisp edge then exposed

A crisp matte shows what a blurry one hid:

- **A two-column gap between chimney and shed** read as a pale vertical strip of her art.
  The shed now abuts the chimney.
- **Sawtooth notches cut into a roof the model never drew.** Three rounds asked for teeth
  and three returned a dormered slate roof. Authoring notches the model ignores is worse
  than not asking: the matte cut them out of a continuous roof and her green showed through
  the roofline as damage. One roof band now; the dormers are what is there.

### And a feathered patch must be bigger than the thing it covers

`clear_copy`'s outer 1.5 cel px are partly transparent, so a rect stopping exactly at the
old gold board left **14 px of it showing at x 10..33, y 189..190**. The rect now runs to
y 192 so the feathered ring lands on plain road. Residue: **none**.

## 18. Option 1 or option 2 for this corner — measured, and it is NOT what we expected

The question was whether to join her art at a **real boundary in the scene**. Measured on
her own pixels, scanning across each candidate join line:

| edge | scan | is there a real boundary? |
|---|---|---|
| **left** | water fraction x 2..14 | **YES** — 0.00 at x5, **0.97 at x6**. A hard edge. |
| **bottom** | water fraction y 188..196 | **YES** — 0.64 to y192, 0.00 at y195. The art/margin edge. |
| right | water fraction x 68..80 | **NO** — 0.92 → 0.19 as a smooth gradient. Hire Ground's cel edge at x75 sits mid-slope at 0.39: an *art* boundary, not a *scene* one. |
| top | green fraction y 144..156 | **NO** — 0.69 → 0.48 as a gradient. The only hard line is y154/155, and that is **her factory's own top edge**, which we are replacing. |

**So option 2 is not available for this corner: two of its four edges have no boundary to
land on.** The expectation was that 2 would be the answer here; the measurement says
otherwise, and inventing a seam across open grass is exactly what the rule forbids.

**Option 1 is what shipped** — crisp matted building, her scenery untouched. The join is the
building's own silhouette, which is a real boundary. The style difference at that join is
honest: it reads as a half-finished board, which is what it is.

## 19. Rentflix — and a name that is his, with a logo that is nobody's

Zain asked for "the same logo as Netflix". **That is deliberately not what was built**, and
the reason is the programme's own purpose: this whole exercise exists to get someone else's
intellectual property off this board. Replacing Sierra's artwork with a live trademark
would defeat it on a board he may want to show people.

So: **the genre, not the brand.** A bold wordmark on a dark fascia, lit like a shopfront.
No arched perspective wordmark, no ribbon mark, no imitation of anyone's letterforms,
proportions or sign construction. Red-on-dark bold type is a style and nobody owns it. The
test applied was whether it reads as *a video shop*, not as *that company's logo with
different letters*. **The name Rentflix is his and is fine.**

### The building

It is the lettings office in the game's fiction, so it is built as a high-street letting
agency: plate glass frontage, **property cards in both windows**, a desk visible inside,
warm interior light behind a dark fascia. The cards are what separate it from any other lit
shopfront at a glance.

| | |
|---|---|
| cel | **2**, 41x22 at (85,3), 99.8% opaque, **92.7% building** |
| region | the cel exactly — 41x22, 656x352 = 230,912 px at 16x, **100% fill** |
| sign | 37x9 — **90% of the façade's width, 41% of its height**, one line of eight characters |
| marble | sign at game x 87..123, y 7..15; the sweep in this corner starts at y 29. **0 swept px** |
| masks | board `darkgreen` **34.7%**, view 701 c0 `goldish` **41.3%**, c1 `redish` **35.4%** — all in window, no new predicates |

### The region cannot exceed the cel, and the door proves the Pawn Shoppe rule twice

Her door is view 751 loop 1 at (105,29) — bottom-centre, so the 7x11 cel covers game
x 102..108, **y 19..29**, while her cel ends at y 24. The façade was extended to meet it and
that failed two ways: `sierra_region` requires a cel source's region to **be** the cel, and
the Pawn Shoppe had already settled the principle. Checked on her pixels: at x 100..110,
y 25..29 her background continues the doorway and puts a step at y 28. **The threshold sits
below the building by design.** Making ours taller than hers is a fidelity change dressed as
a fix. The painted doors run to the cel's bottom edge so the sprite has building behind it
for every row they share.

### STANDING CHECK: composite every door over the FINISHED building at 12x

**Do this for every remaining building, before calling it done.** The static crop looked
fine twice — at the Factory and here — and both times the door was still Sierra's 1990 art
drawn over a photoreal façade. A door is only visible while it animates, so it is the one
part of a building that a still of the board cannot show you.

It also catches the second fault: the Rent Office's door first came back as a flat grey
slab with no frame, no glass and no handle, and that was only obvious composited at native
12x over the shopfront it has to sit in.

### Its door was still 1990 art, and the hinging is NOT the same for every building

Caught by compositing the door over the finished shopfront at native 12x: `view_751_l1_c*`
was missing from `cast0`, so Sierra's blocky door was being drawn over a photoreal
frontage — the same fault as the Factory's, in a building that had just been rebuilt.

**And `door_cels.py` could not simply be pointed at it.** Measured off her own cels, the
Factory's loop 5 is a **pair of leaves meeting in the middle** (5+5, 4+4, 3+3, 1+1) while
the Rent Office's loop 1 is a **single leaf hinged on the left** (7, 4, 3, 1) with the
interior revealed to its right. Assuming the Factory's arrangement would have drawn a
mirrored second door that does not exist in her animation. `LEAF` is now per building, with
a `hinge` of `both` or `left`.

The leaf style is per building too: a letting agency's entrance is a **glazed** stile-and-rail
door, not the Factory's planking and iron straps.

### The softness: FOUR hypotheses tested, all rejected, and it is not fixed

Compared at native resolution the Factory's brick and window frames are crisp where this
façade's card and glazing are a smear. Measured as the edge-width ratio at the **12x rung
that actually ships**, which is the only comparison that means anything:

| tried | canvas | d1/d4 at 12x |
|---|---|---|
| as shipped | 656x352, 230,912 px, 1.86:1 | 0.320 |
| **more pixels** — scale 24 | 984x528, 519,552 px, 1.86:1 | **0.315** |
| **squarer canvas** — `pad=(6,8)`, cropped back | 656x576, 1.14:1 | **0.326** |
| **masonry in the subject** — brick piers, tiled stall riser | 656x576 | **0.314** |

**None of them moved it.** Pixel count, aspect ratio and subject texture are all ruled out
here, as the conditioning blur, the denoise strength and the prompt's focus vocabulary were
ruled out earlier on other buildings.

**What the masonry attempt did show** is the likeliest explanation, and it is not a
parameter: in that render the **brick and the roof tiles came back sharp while the glazing
stayed soft**. Plate glass and its reflections have no high-frequency content — a
photograph of a shop window *is* soft. This building is mostly window. That is an
observation, not a proof, and it is recorded as such.

**`pad` is kept** even though it did not help here: padding the canvas towards square and
cropping back is the right tool for a cel with an extreme aspect, and this is the only
place that records it was measured and found not to be this building's problem.

### A PROPERTY OF THE SUBJECT, not of the settings — and it generalises

    PLATE GLASS CANNOT BE MADE SHARP. A photograph of a shop window genuinely is soft:
    glass and its reflections have almost no high-frequency content. A building that is
    mostly window will read soft at any setting, and the lever is the BRIEF, not a
    parameter. The next glazed shop will hit this.

Acted on here: the façade was rebalanced towards masonry — brick above the fascia, brick
piers between the openings, a tiled stall riser, plate glass for the **display** rather
than for the whole frontage. The brick comes back with visible mortar courses where the
glazing never did.

See §21: the sharpness number said this was *worse*, and it was consulted for the last time.

## 20a. THE NO-TEXT GUARD HAS A FALSE NEGATIVE, AND IT IS THE DANGEROUS DIRECTION

The guard's four false *positives* are documented in §16. This is worse: **on a soft façade
it misses real text.**

Seed 7 wrote **`Pandrizino`** across the Rent Office's door head — pseudo-text on the
building, outside the sign band and so covered by nothing. **The guard passed it.** The
reason, measured: the façade is blurry, so the letters merged into single blobs of
109x58, 135x77 and 181x89 px, which fail both the glyph shape test and the "six aligned
blobs" test. The guard counts discrete letter-shaped components, and blurred text has none.

    The guard works on sharp output and fails on soft output - which is precisely the
    output most likely to need it, because a soft facade is also one the eye skims.

Not fixed. Until it is, **a soft building needs a human look at native 12x over every band
the sign does not cover**, which is how this was caught.

### What did fix it, and what did not

- **A stone transom over the doorway: NO.** It simply wrote on the door below instead.
- **A different seed: YES, here.** Seed 23 leaves the door head clean and what it writes
  instead lands **inside the fascia**, which the constructed sign paints over completely.
  `seed=23` is pinned in the table with that reason.

A seed is not a fix, it is a reroll, and it is recorded as one.

### The two instructions pull against each other at 41x22, and this is the residual

"More masonry, less glass" and "cream property cards visible in the window" cannot both be
had on a 41x22 cel. With the openings reduced to display windows they are 8x5 cel px, and
the model renders an opening that size as a **small lit window** no matter what is painted
behind it — tried twice, once by removing the warm GLOW and once by replacing it with a dim
interior. The cards are no longer bright yellow boards, but they are not cream paper
either. Bigger windows would bring them back and undo the masonry rebalance.

### The model wrote on the property cards, and that is allowed

The cards carry faint print-like marks. The no-text guard passed them and at native 12x
they are **not legible** — they read as small print on a card, which is what a property card
has. This is the one place in the programme where model-made marks are kept, and it is kept
because they are texture rather than text. If they ever resolve into words the guard will
catch it.

## 21. THE SHARPNESS METRICS ARE NOT FIT FOR PURPOSE. Do not use them.

**Both of them. Delete them from your toolkit, not from your attention.** Two numbers were
used to judge focus on this programme and **neither has agreed with the picture once**:

| what it was asked | what it said | what was true |
|---|---|---|
| are the shipped buildings soft? (edge-width ratio, mean \|d1\| / mean \|d4\|) | Black's Market 0.42 vs QT 0.28 — QT is the soft one | it was ranking **concrete-and-mullions against awning-and-glass**: texture density, not focus |
| is the Factory soft? (p99 one-pixel step) | 12.0, softer than Rentflix's 15.0 | the Factory's 12x crop is **sharp**; Rentflix's is a smear. The number was reading Rentflix's high-contrast **cards** |
| did the masonry rebalance help? (d1/d4) | 0.320 → **0.309**, worse | the brick came back with visible mortar courses where glazing never did |
| did the masonry rebalance help? *(the picture)* | — | it read as **two glowing lamps** and lost the building |

**A number that has been wrong every time it was consulted should not be consulted again.**
Leaving it in this file with a caveat beside it invites the next person to trust it; so it
is written here as a dead end rather than as a tool.

Both are **contrast** measures. Focus is about how far a luminance step is spread, and both
of these are dominated by how much contrast the subject happens to contain — so a facade
with bright cards or rough concrete in it scores "sharp" regardless, and one made of glass
scores "soft" regardless.

    JUDGE SHARPNESS BY CROPPING THE INSTALLED FILE AT NATIVE 12x AND LOOKING FOR A
    SINGLE HARD EDGE. That test has been right every time. It is also the test that
    found the gibberish, the 1990 doors, the blurry halo and the region rectangle.

The four softness hypotheses in §19 were *measured against these numbers* and are therefore
weaker evidence than they look. What survives on the picture alone is the subject finding
below, which was confirmed by eye: brick renders with mortar courses, plate glass does not.

## 22. Plate glass must be ALLOWED to be soft

Settled, after the brief was changed once in each direction:

    Plate glass cannot be made sharp - a photograph of a shop window genuinely is soft.
    AND a shopfront that needs glass to be LEGIBLE must therefore be allowed to be soft.
    The brief is the lever only where the brief has slack. Here it does not.

Rentflix was rebalanced towards masonry to chase sharpness and **lost the building**: at
8x5 cel px the openings rendered as small glowing lamps and it read as a generic lit
storefront rather than a letting agency. The windows are back at the proportions that work,
with brick kept as **piers between the openings and above the fascia** rather than as the
dominant surface.

**A soft building that reads as a letting agency beats a sharper one that reads as two
lamps.** The property cards are what carry the meaning at this size, and they only read
when the window is big enough to hold them.

## 23. AMBIGUOUS MARKS ARE NOT WORTH DEFENDING, and a reroll is not a fix

I read the band above the Rentflix fascia as masonry. Zain read it as text. His ruling, and
it is now the rule for every building:

> If two careful readings disagree about whether a band says something, a player cannot
> tell either, and ambiguous marks across the top of a building are not worth defending.

So the band does not get argued about; it gets replaced. And it does not get rerolled,
because **rerolling only moves the problem**. The sequence on this one building:

| seed | where the model wrote a shop name | what happened |
|---|---|---|
| 7  | across the DOOR HEAD | the no-text guard MISSED it (§20a) |
| 23 | in the FASCIA | harmless — the constructed sign covers it |
| 23 | along the PARAPET's top edge | appeared only after the brick there was replaced with plain stone |

Three places, two seeds, one model. The text is still in the generation at seed 23; it is
underneath the sign. **Moving or resizing the Rentflix sign uncovers it**, and that warning
is pinned in capitals at the site in `tools/buildings.py`, not only here.

## 24. `Building.trims` — a band the model is not allowed to decide

The only band guaranteed free of pseudo-text is one that is **painted deterministically
after the generation**, exactly as the sign is. That is what `trims` does:

```python
trims=[(x0, y0, x1, y1, face, face_hi)]      # cel coords, painted in install_building
```

Three tones — a lit capping course, the stone face, a deep soffit shadow — and no texture,
because texture is something to misread and avoiding that is the whole point of the band.

**Three things went wrong building it, and all three are the same shape of mistake.**

1. **The paste was AFTER the only `board.save()`.** The trim went into the in-memory board
   and the files on disk never had it — while the tool printed `trim: plain band over game
   x 85..125 y 3..6` on every run. *A print is not evidence. The installed file is.* I
   found it by cropping the installed board, not by reading the log.
2. **It was painted as a bare rectangle** and floated above the roofline with sky either
   side — the hard rectangular silhouette this pipeline exists to avoid (§17). It is now
   cut by the same matte as the building: the band is part of the building, so it ends
   where the building ends.
3. **It needed `clear`.** The trim is matte-cut, so anything an earlier install wrote
   *outside* the silhouette survives underneath it forever. A full-width grey slab did
   exactly that. `clear=(85, 3, 125, 24)` restores Sierra over the whole footprint first;
   erasing costs no GPU.

**And the overhang is real.** The matte is 41 columns wide at cel rows 0..3 and 37 from row
4 down. That is not an authoring slip — Sierra's Rent Office has a roof wider than its
shopfront, and the cornice is full-cel-width because hers is. The fix for "it looks like a
slab" was not to narrow it; it was to give it a lit edge, a soffit shadow, and **the piers'
own colour, sampled off the installed board at game y 18 (`0x828C95`)**. A lighter stone
made it the brightest thing in the frame, and brightest-thing-in-the-frame is what reads as
pasted on.

## 25. The band-by-band eye inspection, and why the guard's pass means little here

`no_text_guard` passes on the Rentflix **generation** — an image in which the parapet
pseudo-text is plainly visible to me at 16 px per cel px. That is the §20a false negative
firing a third time, and it means **the guard's pass on this building is not evidence**.
What is evidence is looking at every uncovered band of the installed file at native 12x.
Do that, and say what you concluded for each; see the handback for this build's six.

## 26. Bare Necessities: the sign made an object, and the fringe fixed with no GPU

Zain: *"Bare necessities doesn't look like a sign, it looks like text over a building,
quite dumb."* It was one of the five that predate `sign_board.py` — flat type stamped into
the already-downscaled cel.

**It is not Needful Things' plaque, and the table says why.** The pawnbroker's is varnished
timber, gilded relief and brass bolts; this is the mugging shop, so it is a **painted steel
panel**: depth 0.40 against 0.50, a galvanised edge instead of a warm bevel, shadow 0.70
against 0.55 to match the raking light the façade was generated under, grain 0.05 and
**relief 0.06 — enamel on steel is painted flat, not carved**. The ink is hers, unchanged.

41x13 cel px on a 43x37 cel: **95% of the façade's width, 35% of its height**. The width is
forced, not chosen — `NECESSITIES` measures 39px in Sierra's own 3px-cap metric against the
41 her band offers.

**The marble took one pixel of it.** At `rect=(1,5,...)` the board's top-left corner
collected exactly **1 swept pixel**, from path step 144 at (24,74): the 9x8 cel drawn
bottom-centre covers x 20..28, y 67..74 there and clips the corner. Dropped to y 6. Zero.

### The fringe: an install-only fix, and TWO keys of scenery rather than four

The §3 audit had this building at **13.5% Sierra inside the region, 98% of it in the outer
two pixels, 80% of the outer one-pixel border** — a frame of 1990 art round a photograph.
The keep rects were inset from the cel. They are now the whole cel, and the **matte** draws
the outline instead. **No regeneration**: the façade that exists came from this same init.

`scenery=("GROUND", "KERB")` and **not** `("GROUND", "KERB", "TRUNK", "SHADOW")`, decided by
looking at both. TRUNK and SHADOW model the dark mass at the bottom-left — a tree base in
deep shadow, standing in front of the shop. Cut from the matte, **her 1990 blob showed
through, upscaled**, four cel pixels of mush where the eye goes first. Kept as building, the
model's own dark mass fills her composition. Same boundary; the difference is which side of
it is 2026.

And it needed `clear` for the same reason Rentflix did (§24): a matte that no longer covers
what an earlier install wrote leaves that art outside the silhouette forever.

### The edge scan, and what it can and cannot tell you

For every outline pixel, the step across the join in **her** 1x board, against the step one
pixel further out:

| side | px | step across the join | local texture | reading |
|---|---|---|---|---|
| left | 34 | 16.0 | 20.0 | nothing to land on |
| right | 34 | 8.4 | 63.4 | nothing to land on |
| top | 42 | 10.0 | 12.8 | nothing to land on |
| bottom | 42 | 44.3 | 46.6 | the kerb — a real line the ratio test misses |

**Calibrate it before believing it.** Rentflix's top scores 98.9 against 0.5 — a roofline
against flat sky, which is what a real boundary looks like. The Factory, already accepted,
scores "nothing to land on" on all four sides. So this is a **description, not a gate**: it
detects a boundary reliably only against a flat surround, and it under-reads narrow bands
like the kerb, where one pixel further out is still crossing the same band.

Bare Necessities' bottom lands on the kerb. Its left edge is the cel's own left edge, and
**Sierra's art has no stronger step there than its surroundings (17.5 against 22.9 one
column out)**. That is reported, not feathered.

## 27. THE BOARD'S MISSING MIDDLE — the plan, stated before any GPU is spent

Zain: *"I don't want all that white space there, it looks silly, can't you shrink that
slightly and generate the missing part of the town?"*

`tools/town_centre_mask.py` measures it and writes the matte. **Nothing is under there.**

### Region and fill

| | |
|---|---|
| region | **x 69..249, y 45..154 = 181 x 110 game px** |
| the hole itself | 19,330 px flood-filled from (160,100), minus 11 for the picPatch = **19,319 px** |
| | **30.2% of the whole board** |
| fill fraction | the matte covers **97.0%** of the region's rectangle |
| what the other 3% is | her own art reaching into the bounding box's corners — Hi-Tech U's roof, the park — left alone |

### Three measurements that decided the work, and two of them overturn an expectation

1. **The edge is hers, and it is the best boundary on the board.** She drew a **one-pixel
   black frame** round the blank panel: `(0,0,0)` at y 44 above, x 68 at the left. Over all
   **640 boundary pixels the step across the outline in her own art is 204.0 per channel,
   with 0.3% below 10.** For scale, Rentflix's best side is 98.9 and the Factory scores
   "nothing to land on" on all four (§26). Every join this region makes lands on a hard
   line she drew. **Nothing here needs feathering, on any side.**
2. **The marble never enters.** `0 of 170 MarblePath anchors` are inside and **0 swept
   pixels** fall in it; the nearest is **2.0 px outside** the edge. The ring road passes
   around the centre. So nothing generated here is walked on, and nothing generated here
   can collide with a sign. This was expected to be the hard constraint; it is not a
   constraint at all.
3. **The picPatch touches it by eleven pixels.** Of its 2,071 opaque pixels, **11** land
   inside — antialiasing fringe along the bottom edge, at (248,146)…(173,153). They are
   **subtracted from the matte**, so the patch can never contradict the board. That is the
   bug that bit the University and the Employment Office, closed by subtraction rather than
   by hoping it would not matter.

### What it contains, and why

The thirteen face **inward**, so the middle has to read as what they face onto. Four blocks
of low shops in the corners, a paved plaza running north–south and east–west between them,
a lawn with a pond at the crossing, street trees and parked cars along the kerbs. The
plaza's arms run to all four edges, so the streets read as continuing under her frame
rather than stopping at it. 48 authored `init` rectangles; see `BUILDINGS["towncentre"]`.

### How it gets generated, and why it must be two stages

**181 x 110 at 12x is 2,172 x 1,320 = 2.87M px — three and a half times this card's
measured 810k ceiling.** One pass at the shipping rung is impossible.

- **Stage A, composition.** The whole region at **scale 6 → 1,086 x 660 = 716,760 px**,
  inside the ceiling. This is the only scale at which ONE generation sees the entire centre,
  so the layout, the light and the palette are coherent by construction rather than by
  luck.
- **Stage B, detail.** Stage A upscaled to 12x, then **six tiles** at low denoise
  (~0.35) conditioned on the upscaled image's own soft edges — each about 61 x 55 cel px,
  73 x 67 with overlap = 876 x 804 = 704k px. Because every tile starts from the same
  stage-A image and only sharpens, it cannot diverge in colour or layout.
- **The tile joins are the plaza.** They sit at **x 60, x 120 and y 54**, all three inside
  the plaza's uniform paving. A tile join, like every other join in this pipeline, lands on
  a line the scene already has — it is not blurred away.

### What this costs to check

The no-text guard is unreliable on soft output (§20a), and this region is **19,319 px — ten
times the biggest building so far**. The band-by-band look at native 12x is therefore the
acceptance here too, over a much larger area: budget a full pass over the four blocks, the
two plaza arms, the lawn and the four kerb runs.

## 28. Stage A — five passes, and what each one was for

Installed into both rungs; **no detail pass has been run**. Stage A is 6 px per game px
against the shipping rung's 12, so it is half the final resolution by construction.

| pass | what changed | what it showed |
|---|---|---|
| 1 | the plan as authored | four untextured slabs. The init was blurred by `gen_w/60` = **18.1 px — three cel pixels at 6x**, so the init arrived as mush and the model painted mush back |
| 2 | `init_blur` 0.35, materials named in the prompt | roofs grew ridges and hips. But the 63px plaza down the middle came back **pale**: on the board the centre still read as white space |
| 3 | streets became the tile grid; the parcels were built | structure right, **palette washed out and pink** against her ring |
| 4 | **strength 0.92 → 0.80** | her greens and tarmac survived. The blocks' `WALL` band then rendered as a **large blank cream forecourt** — the same pale emptiness, moved |
| 5 | each block became **two ranges with a service yard**, built edge to edge | the composition as it stands |

### Three things this cost that are worth keeping

**`init_blur` is a new field.** The blur formula was written for 16x canvases, where
`gen_w/60` is about two thirds of a cel pixel. At 6x it is three. The LANCZOS-conditioning
lesson, in a second place: *a blurred input is an instruction to paint soft*, and the
instruction has to be scaled to the canvas, not to the pixel count.

**The init's colours are the only colour control there is.** The house rule is no colour
words in the prompt. So `strength` is not just a detail dial here — it decides how much of
the authored palette survives, and that is why 0.80 and not 0.92.
`tools/samples/town_centre_stage_a_strength.png` shows it as a **one-variable test**: the
same init, seed, conditioning and crop, only `strength` differing. The first version of
that sheet compared pass 3 against pass 5 and so differed in TWO things, and its caption
claimed otherwise; it was re-run rather than re-captioned. At 0.92 the model turns the
authored lawn into another roofed building and the square goes pale; at 0.80 the lawn and
the pond survive.

**Pale emptiness moves if you only move it.** Passes 2, 3 and 4 each shrank the blank area
and each time it reappeared somewhere else — the plaza, then the square, then the blocks'
forecourts. It went away when every parcel was given something to be, not when the blank
one was made smaller.

### The bands, looked at by eye at native 12x

Seven bands, each cut into three chunks on the tile lines so no chunk boundary falls in the
middle of anything. Re-read from `assets/png12x-cast0/board.png`, no resize.

| band | cel y | what it is | conclusion |
|---|---|---|---|
| A | 0..21 | the blocks' front ranges, the north green | hips and eaves read; **no glyph-like marks** |
| B | 22..30 | the service yards and ridge lines | plain; **no marks** |
| C | 31..46 | the rear ranges, the green's lower half | **no marks** |
| D | 47..60 | the east-west street | tarmac, a continuous centre line, kerbs, vehicles, hedges. **No marks** |
| E | 61..83 | the south blocks and the square's lawn and pond | the pond reads as water. **No marks** |
| F | 84..92 | the south yards, the pond's lower half | **no marks** |
| G | 93..109 | the south rear ranges, and **her art below the matte** | the only text in the frame is **Sierra's own** — the Employment Office's HIRE and Hi-Tech U's roofs, outside the matte and untouched |

The guard passes on the generation (6 px/cel) and on the installed region (12 px/cel). As
§20a and §25 say, that is worth little on soft output; the seven bands are the acceptance.

### What stage A cannot fix, and what stage B will not

**Stage B adds surface, not composition.** It will put tiles on those roofs, bodies on
those cars and canopies on those trees. It will **not** change the plan view, the block
layout, or how the centre's palette sits against her ring. Those are stage A decisions and
they are what is being judged now.

**The projection is the open question.** Her thirteen buildings are drawn in elevation and
every one of them faces INWARD, so an oblique middle would agree with one side of the ring
and contradict the other three. Her ground — the roads, the river, the grass — is already
drawn in plan. A plan view is therefore the only projection that contradicts no side, and
it is what stage A generates. The cost is that its rooftops are seen from above while hers
are seen from the front. That trade is the thing to rule on.

## 29. Roof variety and the colour lift - one target hit, one measurably missed

### "It looks washed out" was a BRIGHTNESS problem, not a colour problem

Measured over her board outside the hole, against the installed centre:

| | saturation | value |
|---|---|---|
| her ring | **0.370** | **0.514** |
| the centre, before | 0.309 (83%) | 0.377 (**73%**) |

It had been read as a colour problem all evening. **The larger gap was value.** Half of
each gap was the target - match *towards* her, not to her - so 0.340 and 0.445.

### What landed

| | target | achieved | of her ring |
|---|---|---|---|
| value | 0.445 | **0.448** | 87%, up from 73% |
| saturation | 0.340 | **0.269** | 73%, **down** from 83% |

**The value target is hit. The saturation target is not, and the roof variety is why.**

### Two findings behind the miss

**The init-to-output transfer is a property of the CONTENT, not a constant.** The first
lift was computed from pass 5, where one maroon covered every block: init 0.283 -> output
0.309, a transfer of **x1.09**. With slate, lead and asphalt over about a quarter of the
region the same measurement gives **x0.864** - the model now *desaturates*. Predicting the
second lift with the first transfer is what made it miss. Re-measure the transfer whenever
the init's content changes.

**The target was then out of reach, and that was established by a scan rather than by
trying.** Scaling every chromatic key (saturation above 0.15, so a slate roof stays grey):

| k | init sat | predicted output | of her ring |
|---|---|---|---|
| 1.00 | 0.279 | 0.241 | 65% |
| 1.25 | 0.316 | 0.273 | 74% |
| 1.50 | 0.347 | 0.300 | 81% |
| 1.70 | 0.367 | 0.317 | 86% |

**Even at 1.70 the 0.340 target is unreachable**, and 1.50 was tried and rejected by eye:
it put the pantile at `0xEF500B` and the two warm blocks became the loudest thing on the
board - traffic cone, not roof tile. The three pantile keys were pulled back to terracotta
and everything else left at 1.50.

**So this is a trade, not a shortfall waiting to be fixed.** The brief asked for slate,
lead, pantile and flat asphalt across four blocks; grey roofs are grey, and they now cover
a quarter of the region where one saturated maroon used to. The brightness gap - the real
cause of "washed out" - is closed. The chroma is ten points of her ring lower than before,
in exchange for four blocks that read as different buildings. **If that trade is wrong the
lever is `k` in the palette, and the cost is lurid grass.**

### And a file was truncated writing this up

`io.open(path, "w", newline="\\n")` with a backslash that PowerShell had already escaped
raised `ValueError: illegal newline value` - **after** opening the file, which had already
truncated it. BUILDINGS.md went to zero bytes. It was restored from the branch, which is
the only reason nothing was lost. **Write a document edit to a temporary file and move it
into place; never open the real one for writing until the new content exists.**

## 30. Stage B, and the tile seams - what fixed them and what did not

Six tiles at strength 0.35, 12 px per game px, installed at both rungs. The board matches
`detailed_2172x1320.png` at **0.00 per channel**, checked rather than assumed.

### The seams were real, and a measurement found all three where an eye found one

`seam_check.py` asks the right question. Not *is the step small* - on a kerb it is large
and correct - but **is the step at the join unlike the steps one to eight pixels away**.

| join | first run | after | the scene's strongest REAL edge |
|---|---|---|---|
| x 60 | 8.08 (z +5.6) | **6.57 (z +3.8)** | |
| x 120 | 10.83 (z +10.8) | **5.59 (z +5.0)** | **4.01**, at cel x 154 |
| y 54 | 10.52 (z +5.5) | **6.27 (z +3.2)** | |

I saw x 120 by eye in the deliverable. The scan found the other two.

### THE FINDING: a correction fitted across an overlap must fit INSIDE that overlap

Stage B's contract is *add surface, do not change level*, so each tile's low frequencies
are forced back to the ones stage A settled and its high frequencies are kept. That is not
blurring a seam - nothing is softened, and the correction is applied to the whole tile
rather than to a band at its edge.

It did nothing. The numbers were identical before and after.

**The kernel was wider than the overlap it was being fitted across.** Sigma was
`min(tile)/8` - about 100 px - against an overlap of 72. Both blurred fields were
edge-clamped exactly where the cores meet, so **the correction was least accurate
precisely at the joins, which is the only place it existed to help.**

Generally: *any* correction estimated from a neighbourhood - a blur, a local mean, a
rolling window, a fitted gradient - is unreliable within its own radius of a boundary. If
you are fitting it across an overlap, the radius has to be inside the overlap, or the
estimate is clamped garbage at the one place you care about.

**Both ends were moved**: the kernel down to **2 cel px** (a constant cast is captured by a
blur of any radius, so it only has to clear the edge) and the overlap up from 6 to **10 cel
px**, putting 3 sigma at 72 px inside a 120 px margin. **It cost pixels**: the largest tile
went from 643k to **760k**, still inside the card's measured 810k ceiling, with nothing
else to give if it had not been.

### Two things that were tried and did NOT work, which is why the above is the finding

**Strength is not the lever.** If the seam were the added texture it would scale with the
denoise. Measured: 0.35 gives 8.08/10.83/10.52 and **0.20 gives 7.05/10.02/9.60** - nearly
halving the strength moved it about a tenth, and bought a visibly weaker detail pass.

**Siting cannot hide it either, and the edge profile says so flatly.** The rule is that
every join lands on a boundary the scene already has - but **a boundary is an EDGE, not a
uniform area**, and putting the joins down the middle of a painted centre line satisfied
the words and inverted the meaning: a uniform surface is where a discontinuity is *most*
visible. The obvious correction is to move them onto the kerbs. Ranking every column and
row of the region by its step says not to bother: **the three strongest edges in the whole
region WERE the three seams**, and the strongest real feature is 4.01. There was nothing
to hide behind.

### Where it stands, honestly

The hard colour split is gone - the first run put a pink-cast tile against a neutral one
and the join read as a drawn line. **A faint step remains**: the worst join measures 5.59
against a scene whose strongest real edge is 4.01, so it is still the sharpest line in the
frame, just no longer by a factor of three.

**The proper fix is a shared noise field** - one latent noise tensor sampled for the whole
image with each tile taking its own crop, so neighbours denoise from the same randomness
where they overlap. That needs custom latents passed into the img2img pipeline and tile
rectangles that are multiples of 8 px in both origin and size, which the present grid is
not. It is real work and it is the right work; it is not done.

### And a log line that named the wrong thing

The installer printed `sign: detailed_2172x1320.png (built at 1086x660...)` - wrong noun
and wrong size, because the dimensions came from stage A's variables. It now prints the
file's real size, read off the file. A log that says the wrong thing is a log that gets
believed.

## 31. The shared noise field - the seams, closed

Six tiles denoised from six independently sampled latents disagree where they overlap, and
the disagreement draws a line at every core boundary. **Sample the noise ONCE for the whole
image and let each tile crop its own**, and neighbours are solving the same problem from
the same start: they agree in the overlap without anything being blended.

| join | first run | kernel fixed | **shared noise** |
|---|---|---|---|
| x 60 | 8.08 (z +5.6) | 6.57 (z +3.8) | **4.11 (z +2.1)** - not distinguishable |
| x 120 | 10.83 (z +10.8) | 5.59 (z +5.0) | **4.81 (z +4.3)** |
| y 54 | 10.52 (z +5.5) | 6.27 (z +3.2) | **3.20 (z +0.4)** - not distinguishable |

**x 120 is the last one with a number on it, and it is a residual rather than a seam.**
Printing the step at every column across both road stripes - the one with a join and the
one without - the join is the local maximum in each, but its neighbours reach 3.66 against
its 4.80. At native 12x, magnified three times, the strip shows a continuous road with no
vertical discontinuity anywhere; the hard colour split that used to be the first thing the
eye found is gone. The metric is a description and the eye decides (section 21).

### What it took, and what it cost

A latent pixel is eight image pixels, so **a tile whose origin or size is not a multiple of
8 cannot take an aligned crop** and the field stops being shared. The cores (x 720, x 1440,
y 648) and the overlap (120 px) were multiples of 8 already. **Only the region's own width
was not** - 2172 is 271.5 latent pixels - so the canvas is padded by four replicated
columns on the right and cropped back at the end.

**Alignment cost nothing.** The six tiles are 645,120 / 737,280 / 657,408 / 665,280 /
**760,320** / 677,952 px - the largest unchanged, still inside the measured 810k ceiling.

The mechanism is a three-line patch of `randn_tensor` in
`diffusers.pipelines.controlnet.pipeline_controlnet_img2img`, which `prepare_latents` calls
exactly once. The patch matches on the tensor's last two dimensions and falls through to
the original for anything else, so it cannot quietly replace some other sampling.

### The rule that misled me is corrected where it is stated

Section 17's block now says **EDGE** rather than "boundary", and adds the line that was
missing: *a uniform surface is not a boundary - it is the worst place for a join, because a
discontinuity there has nothing to hide in.* The old wording was satisfied, to the letter,
by siting three joins down the middle of a painted road marking **because** it was uniform.

## 32. The band check - all seven, and a flaw in my own method

Native 12x, re-read from `assets/png12x-cast0/board.png`, no resize in the path. Seven
bands tiling cel y 0..109, each cut into three chunks on the tile lines.

| band | cel y | what it is | conclusion |
|---|---|---|---|
| A | 0..23 | the north blocks' front ranges, the green's top | slate on the left pair, terracotta pantile on the right; hips and eaves read, the alley is a clean dark slot, the green has mown bands and two tree canopies. The faint disc at each slate roof's centre is a rooflight. **No glyph-like marks.** |
| B | 24..27 | the service yards between the two ranges | a narrow strip of pavement and ridge. Plain. **No marks.** |
| C | 28..48 | the rear ranges, the green's lower half | lead and asphalt behind, three more canopies, a parked car at the kerb. **No marks.** |
| D | 49..59 | the east-west street | tarmac, an unbroken pale centre line, kerbs, cars nose to tail, hedges. **No marks.** |
| E | 60..83 | the south blocks' front ranges, the square, the pond | the pond reads as water with a defined shore; the lawn has mown bands and clipped bushes at its corners. **No marks.** |
| F | 84..87 | the south service yards, the pond's lower lip | plain. **No marks.** |
| G | 88..109 | the south rear ranges, and HER art below the matte | the Employment Office's HIRE and Open Door University. **The only text in the frame is Sierra's**, outside the matte and untouched. |

### The flaw: cutting the bands ON the tile lines hides the tile joins

The chunks are cut at x 60 and x 120 so that no chunk boundary falls in the middle of
anything. The consequence is that **every tile join lands on a crop EDGE and is never
shown** - seven bands, twenty-one chunks, and the three things most likely to be wrong
were in none of them. A check whose sampling avoids the suspect area is the shape of
every guard failure on this project.

So three more crops, each **centred** on a join and covering it along its whole length:
x 60 and x 120 as twelve-cel-wide strips down all 110 rows, y 54 as a twelve-cel-tall
strip across all 181 columns.

**No discontinuity on any of the three.** The roads run through, the centre lines are
unbroken, the kerbs do not step. That is the eye agreeing with the numbers in section 31
rather than being asked to take them on trust.

## 33. The Factory, grown - and the sign sized from 1x rather than from what fitted

Zain: *"the factory sign is too small, won't be visible on a small screen, I asked for
the building size to be increased to make it look more like a factory."* He had asked
for the building to grow **in order to** accommodate the sign, and it settled at 42x40
because the no-collision constraint was satisfied. **Satisfying a constraint is not the
same as doing what was asked.**

| | was | now |
|---|---|---|
| building | 42x40 | **66x44** - 73% more area |
| sign | 26x8 | **41x20** - 3.9x the area |

### TWO ENVELOPES, and conflating them is what kept it small

**The building may overhang the marble** - the marble walks *in front of* a building - so
its bound is the NEIGHBOURS: Hire Ground's region starts at x 75, and the town centre's
matte reaches y 154 at x 69..75. **The sign may not.** Its bound is the swept footprint.

Re-derived rather than quoted: the left run sweeps x 7..27 from y 148 to 187, the bottom
run y 178..187 from x 9 to 94, the top run y 140..150 across x 19..66. **The largest
swept-free rectangle left of Hire Ground is x 28..74, y 151..177 = 47x27**, found by
search rather than by eye - and the proposed pocket turned out to BE that maximum.

### The sign is sized from LEGIBILITY AT 1x, and that forces three lines

Android renders at 1x, so the sign's size on a phone is its size in **cel** pixels.
Targeting **Sierra's own 5-row cap height**, the port's own 3x5 metric says:

| | needs |
|---|---|
| `THE INDUSTRIAL REVOLUTION` on one line | 89 x 5 |
| `THE INDUSTRIAL` / `REVOLUTION` | 49 x 11 |
| `THE` / `INDUSTRIAL` / `REVOLUTION` | **37 x 17** |

The widest collision-free pocket here is **47** cel px, so **two lines misses by two cel
pixels whatever the building does**. Three lines fits in the 41x20 board with a column
and a row to spare, so the name did not have to be shortened.

**And the 26x8 that shipped could not hold the name at 1x in any arrangement** - 49 px of
lettering in a 26 px plate. It was already illegible there and nobody had done the
arithmetic. The lesson is not about this sign: **size type from the smallest rung it has
to survive, and do the arithmetic before generating, not after shipping.**

### The first init had no silhouette, and the conditioning proved it

Pass one filled the region with one flat brown slab from the top edge down. The chimney
ran to the same height as the mill, so it was a dark stripe INSIDE the block rather than
a break in its outline. **The soft-edge conditioning came back almost black** - a handful
of thin lines - and the model returned a handsome brick wall with no chimney at all.

What survives a downscale is the OUTLINE, so the outline is what the init has to state.
Three steps now: chimney at local y 0, a taller bay at y 3, the main roof at y 6. **Dump
the conditioning and look at it** - if it is black, the init is a slab.

### No sawtooth, a fourth time was not spent

Three rounds have asked for sawtooth teeth and all three came back as a dormered slate
roof. A monitor lantern is authored instead, as a raised band on a bay's ridge.

### A patch removed by making it unnecessary

`clear_copy` covered her blank gold board with her own ground copied from 32 right and 2
down. Measured on the installed board, **its right edge at x 41 stepped 14.27 per channel
against a neighbourhood of 1.1** - a drawn line in open ground, which is what §17
forbids. The building's base now reaches y 190 and a boundary wall takes it out to x 9,
so the gold board is under the building and **`clear_copy` is deleted**. The same edge
now measures **2.11 against 0.88..2.14** - indistinguishable.

*Prefer making a defect impossible to measuring whether it happened.*

### The bands, by eye, native 12x

| band | game y | conclusion |
|---|---|---|
| A | 147..155 | the chimney's cap and the bay's ridge against her park; the sign board's top edge. **No glyph-like marks.** |
| B | 156..170 | the sign band and the roof behind it. The board reads as an object - bevel, bolts, cast shadow at the measured light. |
| C | 171..181 | the loading bay and the window band. **The guard's one finding was here**: 7 blobs on a baseline at cel y 33.8, inspected at native 16 px per cel px in three magnified thirds and found to be **the bottom row of window panes on the stone cill** - the guard's first documented false-positive class. Overridden with the reason on the record; the guard was not retuned. |
| D | 182..194 | brick to the base, then a clean horizontal cut onto her road. The old patch edge is gone. |

Checks: marble **0 swept pixels** under the sign and board-wide clear; acceptance **CLEAN**
(3 surfaces); the guard passes on the final generation.

## 33a. The two defects that were OUTSIDE the region - and a tool so the next one is not missed

Zain: *"this now looks absolutely ridiculous!, the factory is too close and it has other
remnants on the left of the factory"*. **Both were outside the generated region**, which
is why nothing in the pipeline saw them. A check scoped to the region cannot see them -
the same shape as the band check whose chunks were cut on the tile lines.

### The "remnants" were two different things, and the measurement split them

| | | |
|---|---|---|
| game x 0..8 | **byte-identical to `png12x-new`, diff 0.0** | **HERS.** Her river bank, always was. Not a remnant, and nothing of ours was ever there. |
| game x 9..12 | diff 38.8..41.2 | **OURS**, and it was the defect |

The stack started at x 13 and the boundary wall only ran from y 176, so between the
region's own left edge and the stack there were **twenty-four rows of her bank showing
through inside our own region**, with two isolated green pixels at y 156. A sliver of her
scenery framed by our building reads as a leftover - which is exactly what it looked like.

**Establish whose pixels they are before changing anything.** Diffing the installed board
against the ported Sierra art answers it in one run and costs nothing; guessing would
have had me repainting her own river bank.

### The gap, and why six columns is the maximum

The region came back from 66 to **60 wide**, so the building ends at x 68 and Hire Ground
starts at x 75: **a six-column gap, byte-identical to `png12x-new`** - the old wing is
fully erased and what is there is her own scenery.

**Six is the most it can be, and that is a trade rather than a choice.** The sign needs
39 cel px to hold three lines at Sierra's cap height at 1x, and the marble fixes its left
bound at x 28, so the sign cannot end left of x 66 and the building cannot stop left of
the sign. **More gap means a sign that cannot be read on a phone.**

### The eraser has to be sized to the PREVIOUS region, not the new one

Narrowing a region strands whatever the old one wrote outside the new bounds. `clear` is
now **(9, 155, 74, 194)** - still reaching x 74 - and it starts at y 155 rather than 151
because `clear` RESTORES Sierra and the town centre's matte owns x 69..75 down to y 154.
An eraser one rung too generous would punch a hole in the neighbour.

### And the fix cost the chimney, once

Taking the left mass out to the region's edge at FULL width - twelve columns with a
six-column dark core - read as a **glazed stair tower**, and the one thing that says
"factory" at this size disappeared. It now **tapers**: a tower base from local y 4 with a
narrow flue rising the full height, and the openings cut from a curtain wall to two small
punched windows. Four columns of her sky remain at the top-left corner, which is a corner
rather than a framed sliver, and the top edge is mostly hers by design anyway.

### `tools/margin_check.py`

New, and it is the standing answer to "the defect was outside the region". For any
building it reports **every pixel outside the region that differs from the ported Sierra
art** (so a stranded install or a neighbour's work is named by rectangle), **every pixel
inside the region that is still hers**, broken down per edge so an inset reads as a frame,
and a native-resolution crop with the region's box drawn on it so the margin can be looked
at rather than only measured.

## 34. Hire Ground, rebuilt from nothing

Zain: *"what is going on with this building!? it looks ridiculous, almost like it's not
a new building, but the old one with text pasted on top. It needs to be a new building,
original realistic art"*.

**There was no ghost of her lettering** - what reads as one at board scale is the
fascia's lower edge - but the substance was right. The sign was flat type on a red
rectangle: no board, no thickness, no fixings, no cast shadow. This was the **second**
building ever done. It predates `sign_board.py`, it predates the extraction, and it had
**no `init`, no palette, no `sign` and no `surfaces` in the table at all** - all of it
lived in `tools/gen_employment.py`.

### What it is now

Authored into the table and generated by the common path. Region grown from 47x34 to
**47x40** so the building has a base below the door; **scale 20**, 940x800 = 752,000 px,
inside the measured ceiling.

**The sign is an object**: 43x16 cel px - **91% of the façade's width, 40% of its
height** - in her own red, as a **painted timber board with a gilt moulding and brass
bolts**, thickness, and a cast shadow at the measured light.

**And deliberately not Rentflix**, which is the nearest neighbour in type and was
checked side by side at native 12x. Rentflix is a flush dark metal fascia over plate
glass with framed lightbox cards; this is warm render, **stone pilasters and a moulded
cornice**, a painted timber fascia, small-pane windows and a timber part-glazed door.
Different sign construction, different materials, different weight of palette.

### The source had to change, and only so the region could grow

`sierra_region` asserts that a `cel:NN` region EQUALS the cel - correctly, because a
façade may not grow to meet a door. Her cel is 47x34 and the new region is 47x40, so it
fires. The source is `background` instead. **It costs nothing**: `surround` keeps the
whole region, so the silhouette is all-generated and `sierra_region`'s pixels are never
blended in - the matte decides the outline against the board.

### The picPatch, which is the trap this building has and no other does

`view 0 loop 1`, 183x25 at (68,138), covers **y 152..162 across Hire Ground's whole
width - 509 opaque pixels** - and carries its own copy of the sign. An art set that
replaces the board and says nothing about the patch shows the new building with the OLD
sign stamped back over it.

It is **not redrawn**. `install_employment.build_picpatch` copies **this set's own board**
into the strip and leaves her alpha alone, so the patch agrees with whatever built the
board rather than with any tool's idea of it. A new `--skip-board` flag lets that run
after the data-driven path has written the board. **Measured on the installed files:
78,652 opaque pixels over this region, mean |patch − board| = 0.00, max 0.** It cannot
contradict the board.

### The margins, which is where the last two defects were

`margin_check` plus a per-column diff: **everything west of x 75 and east of x 121 is
byte-identical to `png12x-new`** (0.00 at every column). The dashed vertical I thought I
saw at the crop's right edge is **her own hedge**, not ours. Nothing is stranded on
either side.

### The bands, by eye, native 12x

| band | game y | conclusion |
|---|---|---|
| A | 149..158 | the board's top edge, gilt moulding and corner bolts against the town centre above. **No glyph-like marks.** |
| B | 159..168 | the board's lower half and its cast shadow on the fascia. **No marks.** |
| C | 169..176 | the cornice, window heads and door lintel. **No marks.** |
| D | 177..185 | the windows, the cards, the timber door. **No marks.** |
| E | 186..193 | the stall riser, the planters, then a clean horizontal cut onto her path. **No marks.** |

Marble **0 swept pixels** under the sign; the guard passes; acceptance clean.

### Two things that are honestly short of the brief

The windows read as **white blinds rather than rows of pinned job cards** - the init
authored card bands and the model made them blinds. And the façade is **clean to the
point of looking like a model**: flat light, no weathering. Both are fixable with the
prompt and another pass; neither is the thing Zain objected to, which was a flat sign on
a generic box.

## 35. THE SIERRA AUDIT, re-run against what is installed - `tools/sierra_audit.py`

Zain: *"None of Sierras stuff is supposed to exist with what we change"*.

### The rule needs two numbers, and only one of them is the rule

It cannot mean zero inside the region's **bounding box**, because a building that fills
its box to all four edges is exactly the hard rectangle §17 forbids. Her sky above a
roofline and her road below a base are the matte **working**. So:

> **Zero of her pixels inside the building's own SILHOUETTE, and zero stranded outside
> the region. Her scenery at the margins, outside the silhouette, is correct and stays.**

Two masks decide it and they are different things. **`silhouette`**, from `surround`,
says which pixels inside the region come from the generation - where it is False the
installer writes HER pixels, `np.where(m, new, sierra)`. **`matte`**, from `scenery`,
says which pixels are pasted onto the board at all. So the number that matters is
`matte & ~silhouette`: **her art that we pasted as part of the building**.

### The table, measured on the installed set

| building | in-silhouette (the rule) | margin (correct) | stranded |
|---|---|---|---|
| **Common Threads** | **25.4%** (366 px) | 0.0% | 0 px |
| **Watt a Bargain** | **6.6%** (150 px) | 0.0% | 0 px |
| **Needful Things** | **6.1%** (67 px) | 0.0% | 0 px |
| Bare Necessities | **0.0%** | 13.9% | 0 px |
| Hire Ground | **0.0%** | 12.5% | 0 px |
| The Industrial Revolution | **0.0%** | 9.2% | 0 px |
| Rentflix | **0.0%** | 7.8% | 0 px |
| the town centre | **0.0%** | 3.0% | 0 px |
| Open Door University | *no init - cannot be computed* | - | 0 px |

**Nothing is stranded outside any region.** Five of eight are clean inside the
silhouette; the old §3 table listed seven as carrying her art, and most of that was the
margin figure read as if it were the rule.

### A bug in the audit itself, found by a number that matched too well

The first run keyed `silhouette` on **the init** rather than on her cel. For
`mode: rects` that makes no difference - the test ignores the image - but `green` and
`blue_light` are **colour tests**, and running them over the init asks whether OUR
palette is green rather than whether HERS is. Watt a Bargain came back 0.0% and is
really **6.6%**. The fix is one line; what caught it was the corrected figure landing
exactly on the old table's 6.6%, which a wrong measurement does not do.

### What each would cost to reach zero

**The three with a real figure all have the same cheap fix, and it is the one that took
Bare Necessities and Hire Ground to 0.0% with NO GPU**: widen `surround`'s keep to the
whole region, name the init's surround keys in `scenery` so the **matte** draws the
outline instead of inset rectangles, and add a `clear` sized to the old footprint.

The init already describes the surround in every case - these are the keys that touch
the region's border, which is what `scenery` names:

| | keys on the border | the call to make by eye |
|---|---|---|
| Common Threads | `GROUND` (134 px), `GABLE` (14) | GROUND is scenery, GABLE is building |
| Watt a Bargain | `SKY` (138), `BASE` (50) | SKY is scenery; **BASE needs looking at** |
| Needful Things | `CORNICE` (50), `WOOD_LO` (50), `STONE` (31), `SKY` (27), `SPILL` (10) | only SKY and SPILL are scenery; the rest are the building reaching its own edge |

**Each needs a LOOK, not a rule** - that is the Bare Necessities lesson, where cutting
four keys showed her 1990 blob through and cutting two was right. Budget about fifteen
minutes each and **no GPU**.

**Open Door University is the only one that costs a generation.** It has no `init`, no
palette and no `sign` in the table at all - like Hire Ground, it predates the extraction
and lives in `tools/gen_university.py`. It cannot be audited or matted from the table,
so its figure above is not zero, it is *unknown*. The cost is the authoring job Hire
Ground just had plus **one** generation.

**So the ordered plan is: three install-only fixes, then one rebuild.**

## 35a. The three install-only fixes - done, and the board is clean

| building | in-silhouette before | after | GPU |
|---|---|---|---|
| Common Threads | 25.4% (366 px) | **0.0%** | none |
| Watt a Bargain | 6.6% (150 px) | **0.0%** | none |
| Needful Things | 6.1% (67 px) | **0.0%** | none |

**Every measurable building on the board is now 0.0% inside its silhouette, and nothing
is stranded outside any region.** Open Door University remains *unknown* - it has no
init and cannot be measured from the table.

The change is the same three lines each: `surround`'s keep becomes the whole region, so
`silhouette` is all-True and the installer never writes her pixels as part of the
building; `scenery` names the init's surround keys so the **matte** decides what is
pasted; and a `clear` sized to the old footprint stops the previous install surviving
outside the new outline.

### The scenery key was a different call on each, and only looking separated them

| | keys touching the border | chosen | why the rest stayed building |
|---|---|---|---|
| Common Threads | `GROUND` 134, `GABLE` 14 | **GROUND** | GABLE is the dark stepped roof; cutting it would notch her sky through the roofline |
| Watt a Bargain | `SKY` 138, `BASE` 50 | **SKY** | BASE is the shop's own plinth, not her ground; cutting it opens the bottom of the building |
| Needful Things | `CORNICE` 50, `WOOD_LO` 50, `STONE` 31, `SKY` 27, `SPILL` 10 | **SKY** | four of the five are the building reaching its own edge. **`SPILL` was the one worth looking at** - it is the warm light the shop throws on the pavement, part of our render, and cutting it put a notch in the base with her pavement showing through the middle of the shopfront |

**Touching the border and being scenery are different things**, and a rule that conflated
them would have damaged all three. The candidate matte was rendered beside her cel, the
init and the current silhouette for each, and read before anything was changed.

### Watt a Bargain's was also a colour test, which is why it was worst-hidden

`mode: green` kept whatever of HER cel was green - the foliage in the top corners - and
then wrote it inside the building. **A colour test cannot tell her foliage from ours**;
it is the same failure CAST.md §4 describes for keying a figure against a same-hue
backdrop. It is now `rects` over the whole region with the matte doing the work.

### Still outstanding on these three, and NOT part of this fix

**Common Threads and Watt a Bargain still carry flat stamped type** - `COMMON` /
`THREADS` straddling the gable and the awning, `WATT A` / `BARGAIN` on the glass. Both
predate `sign_board.py`. This pass was about her pixels, not about the signs, and the
signs are a separate rebuild each.

## 35b. THE THIRD CLAUSE - the margin is supposed to be HER scenery

The rule had two clauses and a slab of a supplied image's background still sat above the
Factory's roofline for a whole install with every check passing. It was not her art inside
the silhouette, and it was not our art stranded outside the region. It was **ours, inside the
region, outside the silhouette** - the margin, which we had agreed was correct territory
*because the margin is supposed to be her scenery*. Correct for her sky above a roofline;
wrong for a slab of screenshot. The audit could not tell them apart because **it only ever
asked whose pixels are where, never what they are.**

So the rule is now three clauses:

1. **Zero of her art inside the silhouette.**
2. **Zero of ours stranded outside the region.**
3. **Inside the region but outside the silhouette, the pixels must be hers** - unless the
   building deliberately generates scenery there, which it then has to *declare*.

Three things the implementation had to get right, each of which first got it wrong.

**It is measured at the shipping rung, not at 1x.** The first version compared 1x downsamples
and found 93 pixels where the eye found a large rectangle. A supplied image composited onto a
screenshot of the board carries board-*coloured* pixels, so the per-pixel difference is small
and a downsample averages it away entirely. It now reads `png12x-<set>` against `png12x-new`.

**A rim is not a slab.** Every matte edge carries a thin line of our pixels: the paste is
resampled to 12x and the join lands between cel pixels, so the first cel pixel outside the
matte is part ours *by construction*. That is inherent and one cel pixel wide. A bare count
cannot tell it from the thing the clause was written to catch, so the count is split by
distance from the matte edge and **only the far part is judged**. Without the split every
building on the board reads as an offender.

**But the split can hide things too.** Hire Ground's missing eraser row sat right against the
matte and was filed under "rim" - 7265 px of it. It was real. The split is a filter, and like
every filter it has a side it cannot see; §38c is the guard that catches that class properly.

**The exemption is per-building, not a blanket rule.** `margin_generated=True` on the town
centre, because it fills a hole she left blank and there is no scenery of hers to leave
there. It is the only one, and it is declared in the table where anyone can read it.

Run across the board after the fixes in §38c:

| building | ours in margin @12x | rim | SLAB | verdict |
|---|---|---|---|---|
| blacksmarket | 1188 | 1188 | 0 | clean, a join rim only |
| employment | 1061 | 1061 | 0 | clean (was 7265 / one unerased row) |
| factory | 1437 | 1437 | 0 | clean (was 16920, of which **12383 slab**) |
| lowcost | 617 | 617 | 0 | clean |
| pawnshop | 315 | 315 | 0 | clean |
| qtclothing | 1463 | 1463 | 0 | clean |
| rentoffice | 485 | 485 | 0 | clean |
| socketcity | 1534 | 1534 | 0 | clean |
| towncentre | 22278 | 22278 | 0 | **declared** - generates its own margins |
| university | - | - | - | no init: *unknown*, not zero |

All nine measurable buildings pass all three clauses. **A first run that showed every row at
0.0% was wrong**: it had been given `--set new`, so it compared the reference board against
itself. A check whose two inputs are the same file reads green for ever.

## 36. Casa El Cheapo - the pretension lands, the neglect will not

Zain: *"rename low cost housing to Casa El Cheapo and make it look like a cheap but
badly maintained castle"*. A joke with a shape: not a castle and not a ruin, but a
**pretension** to a castle, built cheaply and let go.

### It is a cel, established from the decode

`DoorFor` gives LowCostHousing as Place 1 at (144,30). The only cel in pic 11 whose box
contains that point is **cel 3, 52x34 at (134,2)**, 100% opaque - her brown tenement
with LOW COST HOUSING in red across the top. Cheap to answer, expensive to assume.

### The sign, sized from 1x

| | needs |
|---|---|
| `CASA EL CHEAPO` on one line | 51 x 5 |
| `CASA` / `EL CHEAPO` | 33 x 11 |
| `CASA EL` / `CHEAPO` | **25 x 11** |

The pocket is the whole cel above the marble - **x 134..185, y 2..29, 52x28**, because
the marble only sweeps y 30..35 - so all three fit. Fourteen characters is a far kinder
name than THE INDUSTRIAL REVOLUTION's twenty-five. **`CASA EL` / `CHEAPO` is chosen for
being narrowest**, which leaves the turret its own columns. 32x16 board, interior 28x12
at a 2-cel bevel. **0 swept pixels**, and the door cel at x 140..147 never meets it.

The board is **cheap moulded plastic** - depth 0.25 against Needful Things' 0.50 and
Hire Ground's 0.55, a white plastic edge rather than a bevel, galvanised screws rather
than brass, relief 0.04 because the lettering is printed. It **cannot be hung crooked**
as the brief asks: `sign_board` refuses to rotate on a flat elevation, so the cheapness
is carried by the material.

### Three ways the init can be misread, all the same way

**A rectangle is an OBJECT, and at this scale a dark rectangle on a wall is a WINDOW.**

| authored | came back as |
|---|---|
| merlons running to the region's top row | **a row of ten blue window openings** - the only sky was in the gaps, so an alternating band on a wall read as glazing. It also tripped `no_text_guard`. |
| vertical `RENDER_LO` bars as water staining | **shuttered windows** |
| a satellite dish and a wheelie bin as small rects | nothing |

The merlons were fixed structurally - **a crenellation needs sky ABOVE it, not only
between the merlons** - by dropping the block to local y 7 and leaving three clear rows.
The other two could not be.

### And the neglect still does not land, after five passes and four levers

| lever | result |
|---|---|
| the prompt naming flaking render, water stains, a dish, an air conditioner, washing, a bin | ignored |
| authored stains, dish, bin | became windows, or vanished |
| a grubbier render palette at strength 0.80 | cream became drab, not derelict |
| **dropping `noise` from the negative** and adding *clean, new, pristine, well maintained* | marginal |

**`noise` is in the shared `NEGATIVE` to suppress jpeg artefacts, and it suppresses
grain, grime and surface variation with it.** That is a real finding and `Building.negative`
exists now so one building can override without editing the shared list - which is shared
on purpose, since a per-building copy is how `flat` got deleted from one of them.

**Stopping at five, by the sawtooth rule.** A sixth pass of the same kind is not warranted.

### What would work, and it is the technique this pipeline already trusts

The decay is a **texture**, and the one proven way to put a specific thing on a building
here is to **construct it deterministically after the generation** - which is what
`sign_board.py` does for signs and `trims` does for the Rentflix parapet. A `grime` pass
multiplying authored streaks over the installed region would be unignorable by
construction. Not built, and named as the right work.

### Checks

**0.0% of her art inside the silhouette, 0 stranded, 0 swept under the sign.** Five bands
looked at by eye at native 12x - the merlons against our sky, the turret, the sign and
the wall, the gate and openings, the damp course onto her ground - **no glyph-like marks
in any.** Her red LOW COST HOUSING is covered outright.

**No spoken clip names this building.** The first draft of its note claimed fourteen at
ids 300-313; that was invented, and it was also wrong - 300-309 are the BANK's. Measured
against `subtitles.json`'s 533 lines, every other building has a `Welcome to ...` band
and this one has none, because the player LIVES here. The only two lines naming it are
**192** and **197**, and neither is renamed.

## 37. `tools/grime.py` - decay, coping and sky, constructed

The coordinator, at native 12x: *"it reads as a cream shed with a blue zigzag on top"*,
and the two reasons were right. **The painted sky was a flat saturated rectangle** with
the merlons notched out of it - a graphic, not a sky, because it did not match the
board. **The crenellations were flat-topped and evenly spaced** with no thickness, no
shadow under the overhang and no course lines - a pattern, not masonry.

All three of those - sky, coping, grime - are **construction**, and none needs the
model's cooperation. That is the move `sign_board.py` makes for signs and `trims` for
the Rentflix parapet.

### THE RULE THIS BUILDING ESTABLISHED, with three instances

> **A rectangle is an object, and at this scale a dark rectangle on a wall is a WINDOW.**

| authored in the init | came back as |
|---|---|
| merlons running to the region's top row | a row of ten blue window openings |
| vertical bars as water staining | shuttered windows |
| a satellite dish and a wheelie bin as small rects | nothing |

The init's language is rectangles, and the model resolves a rectangle into the nearest
*thing*. It can state **massing and openings**; it cannot state **weathering**, which is
low-contrast, soft-edged and irregular. The Factory's dark void reading as another window
is the same finding from the other direction.

**A crenellation also needs sky ABOVE it, not only between the merlons** - with the only
sky in the gaps, an alternating band on a wall is glazing.

### And the first grime pass was far too heavy, which is recorded rather than deleted

**The `sky` op copied her own board** from game x 134..145, on the reasoning that her
lettering only runs x 146..174 - the same move `pick_clear_source` makes for `clear`.
**Those columns at those rows are her TENEMENT'S DARK ROOF.** Her cel is a tenement that
fills its box; there is no sky in it to borrow, and the merlon gaps filled with pieces of
her building.

It is now `skyfill`: a **vertical gradient between two values sampled from the board
immediately beside the region**, with a little smooth variation so it is not a flat
field. A gradient matches the board; a fill does not. The patches pasted grey slabs and
are now blended at 45% behind a tighter threshold; the coping turned the merlons into
boxes and is about half its first strength.

**Everything multiplies or blends. Only `patch` writes a colour, because blown render is
a hole and should be.**

### Where it stands

The sky matches the board, the merlons have a section, and there is staining, streaking
and a damp course. **The decay is present but subtle** - deliberately, because the first
attempt proved that heavy reads as pasted and subtle is recoverable without a
regeneration. Every strength is a number in the table and costs **no GPU** to change.

Checks unchanged: **0.0% of her art inside the silhouette, 0 stranded, 0 swept under the
sign**, and the guard passes on the installed region at 12 px per cel px.

## 38. `source_image` - "here is a picture, use it"

Zain supplied his own Factory: *"you keep messing up with the graphics, use this, look
how much nicer it looks!!!"*, then *"I MADE IT because you just never did a good job!"*
**He is right.** Three passes asked for a sawtooth and got a brick wall, the chimney
shipped in the same value as the wall behind it, and the building sat small in its corner
for days. His image has the monitor roof, the water tower, the rooftop plant, the
mullioned windows and the loading bay - everything the brief asked for and the pipeline
never produced.

**So this is a first-class route, not a special case.** `tools/fit_source.py` writes the
fitted image into the same `generated_WxH.png` slot a generation would occupy, so
`sign_building`, `install_building`, the matte, the `clear`, the marble clearance,
`margin_check` and `sierra_audit` all run unchanged.

### HOW TO SUPPLY ONE

| | |
|---|---|
| **resolution** | at or above the region's own. 16 px per cel px is ideal; his was 980x680 for a 60x44 region, which is 16.3 |
| **what to draw** | the BUILDING. Its pavement, kerb and road are discarded - the board has those and they are Sierra's |
| **a sign** | optional. If it is there it is kept unless it is too small to read, which is **measured** |
| **the guard** | still runs. A supplied image can carry lettering anywhere - a plaque, a street sign, a number on a door |

The fit, stated: **crop** to the building, **scale** so its width is exactly the region's,
**bottom-align** so its base lands on a named game row. His: crop (95,190)-(1075,870),
scaled 0.98, 960x666 px = 60 x 41.6 cel px in a 60x44 region, base at game y 190.

### His sign is covered and ours is stamped, and that is arithmetic rather than taste

Fitted, his lettering lands at game x 19.6..56.8, y 167.3..170.5:

| | his | needed |
|---|---|---|
| cap height | **4.24 cel px** | 5, Sierra's own 3x5 set |
| per character | **1.49 cel px** | 3.56 |
| the name on one line | **37.2 cel px** | 89 |

It clears the marble - **0 swept pixels** - and at 12x its cap is 51 device px and it is
handsomer than ours. But it is **2.4x too small by our own measure**, and the three-line
board exists because 25 characters will not fit on one line at a readable size in this
corner. `cover` tiles his sign band out with his own brick and `sign_board` stamps ours.

**And the cost of that is worth stating plainly: our board covers his monitor roof and
his chimney.** There is no placement that avoids it - the board must sit inside the
collision-free pocket (y 151..177), above the door cel (y 171), and needs 17 rows for
three lines, which leaves exactly the band his roof occupies. **His roof or a legible
name; the two cannot both have that space.**

### Four things the fit got wrong first, each recorded

**`cover` duplicated his monitor roof.** It copied one block from eight rows above, and
eight rows above a sign is roof, not wall. It now **tiles the plainest patch in the
image**, found by variance - cel (47..52, 24..27) at variance 10 - with alternate columns
mirrored.

**The matte was ours, not his.** His picture is a composite of the board, so above the
roofline it carries his own screenshot of it, and our init's matte kept a **seven-row
strip of smeared board** above the building. `key` flood-fills his background instead.

**Written at the cel grid, the keyed edge stepped in whole cel pixels** - twelve device px
at the shipping rung - and his roofline read as a staircase. The matte is now written at
the fitted resolution.

**Seeding the flood from the whole top edge was worse than from the corners.** His strip
of the town centre along the top moved the mean reference towards a mid tone, the flood
walked through his dark roof glazing and **punched holes through the sign**. A key needs
a reference taken from the thing being keyed. Adding a local-continuity test was worse
still - it crawled down a gradient into the brick and took the whole building, leaving
0.2%. **A flood needs one fixed reference or it is a crawl, not a key.** The one island
the corner flood cannot reach is **named** in `key.exclude` rather than left.

**And the constructed sign is always forced into the matte**, because both the key and
the island overlapped it and the matte punched a hole through our own lettering.

### Checks

**0.0% of her art inside the silhouette** - the whole board is now 0.0% - **0 stranded**,
**0 swept** under the sign. The guard flagged one row at cel y 28.9 and it is **the
glazing-bar row of his mullioned windows**, inspected at native 16 px per cel px in three
magnified thirds: the window-pane class. His plaque by the loading bay is at cel x 55..56,
y 34..37 - one to two cel pixels, no recoverable lettering at any rung.

## 38a. HIS SIGN IS KEPT, AND IT FAILS THE 1x TEST ON PURPOSE

**The first time this programme has knowingly shipped a sign that fails the 1x test.**
Do not "fix" it without reading this.

| | his | the minimum |
|---|---|---|
| cap height | **4.24 cel px** | 5, Sierra's own 3x5 set |
| per character | **1.49 cel px** | 3.56 |
| the name on one line | 37.2 cel px | 89 |

By that arithmetic ours should be stamped over his, and for one install it was. **The
cost of stamping it was the reason the image was worth using**: our board is 41x20 and
must sit inside the collision-free pocket (y 151..177) and above the door cel (y 171),
which leaves exactly the band his **monitor roof, water tower and rooftop plant** occupy
- the things three of our own generations never produced. There is no placement that
avoids them.

**Zain chose the picture.** Two things support it: at 12x his cap is **51 device px** and
reads perfectly, and the 1x rung currently carries HER art anyway. And **the board is
likely to become 640x288**, which changes the arithmetic entirely - a sign measured
against a 320-wide world may be fine against a 640-wide one. **Re-measure it if that
lands** rather than carrying today's verdict forward.

### The eraser bit for the third time

Removing our sign left **a cream block at the top right: our own previous board,
stranded.** It ran y 151..170, the eraser started at y 155, and his roofline is lower
there so the new matte no longer covers those rows. `clear` is now **(9, 151, 68, 194)**
- and its right bound came IN to 68 at the same moment, because `clear` restores Sierra
and the town centre's matte owns x 69..75 down to y 154.

**The eraser is sized to every PREVIOUS install, not to this one.** Three times now.

### And the guard does not see his sign at all

With his sign no longer excluded from the scan, `no_text_guard` flags **only the glazing
bars of his mullioned windows** at cel y 28.9. It does **not** flag his own board - 25
characters of condensed type at cel y 16..21, the most legible lettering in the frame.

That is harmless here because the text is deliberate, but it is a **false negative on
exactly the thing a supplied image is most likely to carry by accident**. The §20a class
again: the guard finds evenly-spaced window panes and misses real words. **For a supplied
image the band-by-band look is not a supplement to the guard - it is the only check that
works.**

## 38b. WHY A SUPPLIED IMAGE HAS NO RECOVERABLE OUTLINE, in one sentence

> **Compositing onto a screenshot of the board puts board-coloured pixels in the image that
> no key can reliably distinguish from the board itself.**

That is the whole finding, and it is worth the five methods it cost. Each was tried against
his Factory image, each was measured, and each failed for the same underlying reason.

| # | method | what it did | measurement |
|---|--------|-------------|-------------|
| 1 | colour flood from the canvas corners | seeded on the init's own paint, not his image | keyed the wrong picture entirely |
| 2 | colour flood from **his** corners | reference `(94,110,97)`; the background above his roofline samples `(139,192,117)`, `(130,151,137)`, `(88,96,87)` | **35 to 82 away against a tolerance of 34**, and a tolerance wide enough ate his brick |
| 3 | flood from the whole top edge | walked through the dark glazing of his roof | holed the sign |
| 4 | flood with a local-continuity clause | crawled into the brick | **0.2% of the region survived** |
| 5 | detail test - his building is detailed, the board is flat | his composite is a photograph of a screen, so the "flat" board carries as much local variance as the building | roofline came back as **row 0 at every threshold from 7 to 32** |

A sixth was tried later, after the audit's third clause was written, on the reasoning that
his background **is** a screenshot of her board and so a diff against her own art should
find it. **It inverts.** His crop does not realign to her board after cropping and scaling:

    his brick wall      diff   6.8   <- definitely his building, the LOWEST reading
    his sky, col 20     diff  83.8   <- definitely his background
    his sky, col 40     diff  34.1

So the outline is **authored** - `source_image["clip"]`, a stepped profile of column bands
read off his image on a cel grid at 24 screen pixels per cel pixel. It is deterministic, it
cannot be defeated by his palette, and it is the honest answer: *a supplied composite has no
recoverable alpha, so somebody has to say where the building is.*

**A stepped profile has one inherent cost, and it is visible.** A band covering a thin
vertical object has to rise to that object's top across its whole width, so background
survives beside it. The first reading was too coarse and showed this plainly: the chimney
band claimed columns 47..52 from row 0 when his chimney is columns 49..52 from row 1, which
left **two columns of his background standing ten rows tall as a pale box** next to it. A
second, finer reading in two halves took the bands from 9 to 10 and fixed it. Roughly **20
cel pixels** of halo remain, beside the arch and the two ball vents - thin objects, and the
limit of rectangles.

**Neither audit clause can see a halo like that**, because it is *inside* the clip: it is not
her art inside the silhouette, and it is not our art in the margin. Only the eye at native
12x finds it. The real fix is upstream and costs nothing: **ask for the building on a plain
flat background.**

## 38c. THE ERASER MUST COVER ITS OWN REGION - got wrong three times, now guarded

`clear` is the rectangle restored from her ported board before anything is pasted. If it is
smaller than `region` in any direction, **the rows it misses keep whatever the previous
install left there, for ever**, and no amount of reinstalling touches them.

The Factory's `clear` began at game y **151** while its region begins at y **149**. A
two-cel-row band of his screenshot background, the full 60-cel width, survived every
reinstall. Hire Ground's began at y **152** against a region at y **151** - one row, same
cause. Both were invisible to every check we had:

- **not** her art inside the silhouette, so clause 1 passed;
- **not** our art outside the region, so clause 2 passed;
- and clause 3 did not exist yet.

`install_building.check_clear_covers_region` now **refuses to run** rather than warning. A
rule nobody can forget beats a rule written down, and this one had been written down twice.

## 39. The last two flat stamps - Common Threads and Watt a Bargain

Both were the complaint Zain made of Bare Necessities, and Watt a Bargain was the worst on
the board: red vector type floating **on the plate glass**, off-centre, running to the
region's edge, with no object under it. Common Threads was two lines of flat green in **two
different sizes**, because her gable is stepped and the old `name_rects` fitted each line to
whichever step it landed on.

Both now carry a `sign` dict and go through `sign_building.py`. The material differs on
purpose - **painted timber fascia** on the boutique, **illuminated acrylic box sign in a
brushed aluminium frame** on the appliance showroom - because the material is what tells you
what kind of place it is.

Common Threads' board is a rectangle **inscribed in her step profile**, x 8..31 by y 4..13:
rows 4..6 are only x 8..31 and that is what sets the width. Her dark gable stays visible all
round it, which is what makes it read as *mounted on* something.

Glyph drift through the marrying pass, which is the check that the letters were lit and not
re-invented: Common Threads **IoU 0.948, centroid 0.075 cel px**; Watt a Bargain **IoU 0.979,
centroid 0.016 cel px**. Both acceptances CLEAN, all three audit clauses clean.

### A detector's rect is a TEMPLATE, not a search window

Writing Common Threads' sign I moved `detect`'s rect from her `CLOTHING` band onto the new
board, reasoning that since the ink stays her green, a colour test could be kept alive rather
than relaxed to `litglyph`. **That was wrong, and it is the same family as every other guard
in this document.** `find_name_art` scores a **dense mask of her own letterforms** against the
colour-keyed pixels. Pointing that mask at our board aims a detector of *her* art at *our*
art, and it can only ever report her name as still present. It did, immediately - **ten
surfaces "STILL SAY IT"** on a building whose old lettering was gone.

With the rect back on her band the same build reads **CLEAN**, and that is the positive
result worth keeping: our green `COMMON THREADS`, in her colour, overlapping her position,
scores **below threshold because the letterforms differ**. The density of the template is
doing exactly the job `find_name_art`'s own note at that site says it was built to do.

## 39a. THE TYPEFACE IS ALREADY THE SAME ON EVERY SIGN - measured, because it was asked

| building | sign | cap height, CEL px | ink | material |
|---|---|---|---|---|
| employment | Hire Ground | **6.15** | (208,216,224) | |
| rentoffice | Rentflix | **5.12** | (208,31,42) | |
| lowcost | Casa El Cheapo | **5.00** | (240,236,224) | moulded plastic |
| blacksmarket | Bare Necessities | **4.19** | (192,224,224) | painted steel, bolted |
| socketcity | Watt a Bargain | **4.06** | (192,56,56) | illuminated acrylic |
| qtclothing | Common Threads | **3.44** | (72,168,72) | painted timber |
| pawnshop | Needful Things | **2.75** | (224,184,104) | gilded timber |

**Face, weight, case, tracking and leading are identical across all seven.** Every sign goes
through `install_employment.text_mask`, which resolves one face from `SIGN_FONTS` and offers
no tracking control at all: the lines are centred, `fill=0.86`, `leading=0.16`, all caps. No
building overrides either number.

The face on this machine is **Bahnschrift Regular** (`C:\Windows\Fonts\bahnschrift.ttf`).

So if the signs look like different design studios, **it is not the face.** Two things
actually vary, and the first is large:

**Cap height spans 2.75 to 6.15 cel px - a factor of 2.2.** Needful Things' letters are less
than half the size of Hire Ground's. Against Sierra's own metric of a 5 cel px cap, three
signs are under it and **Needful Things and Common Threads are below the ~4 where a reader at
1x stops reading letters and starts reading word shapes.** Nothing in the pipeline normalises
this: `fill=0.86` is a fraction *of each board*, and the boards were cut to fit each facade.

**Ink colour varies across the whole range** - pale blue-white, pale grey, cream, gold, green
and two reds - because each was matched to its own building, several of them to Sierra's own
ink for that shop.

### And the face is machine-dependent, which is a reproducibility fault

`SIGN_FONTS` is `["bahnschrift.ttf", "seguisb.ttf", "segoeuib.ttf", "arialbd.ttf",
"trebucbd.ttf", "verdanab.ttf"]`, first match wins. **Bahnschrift is Regular weight. Every
other entry in that list is a bold face.** On this machine every sign is set in a regular
condensed grotesque; on a machine without Bahnschrift installed, every sign would come out
**bold Segoe UI** instead. The art is not reproducible across machines, and the list's own
ordering suggests bold was the intent.

**FIXED.** `SIGN_FONTS` is gone. `install_employment` now pins one face - `SIGN_FACE =
"bahnschrift.ttf"` - searched for in `assets/fonts` first (so it CAN be shipped if a licence
ever allows; Bahnschrift ships with Windows and is not redistributable, so it is not in the
repo), then the system font directories. `JONES_SIGN_FONT` overrides it with a full path, for
a machine that wants a different face **on purpose**. If none is found, `_font` raises and
names the face, the directories searched and the override - it no longer returns `None`.

That last point closes the second half of the defect, which was quieter than the first: when
no face resolved, `text_mask` returned `None` and `install_building.letter_region` silently
stamped the **3x5 bitmap at the high rungs**. That is exactly the lettering Zain rejected -
*"I don't want it to look pixelated, it's 2026 not 1986"* - reappearing as a fallback that
would never show up in a log. The 1x cel still uses the bitmap, because at 1x nothing else
fits; that is a choice, not a fallback.

Proved both ways: with the face present the seven cap heights are unchanged, and with
`JONES_SIGN_FONT` pointed at a path that does not exist the run stops with the reason.

## 40. Villa Locke & Keyes - the sign that decided the building

Cel 01, 43x43 at board (7,1), **100% opaque**, door at (29,44) one row below its base.
Her original is "Le Security": neon script over three mansard peaks, pink awning, conifers.

### The arithmetic came first and it dictated everything after

"VILLA LOCKE & KEYES" is 19 characters, and **every arrangement is width-bound**:

| arrangement | plate | cap height |
|---|---|---|
| one line | 41 x 14 | **2.50** - impossible |
| `VILLA LOCKE` / `& KEYES` | 41 x 22, the whole facade | **4.19** - cannot reach 5 at any size |
| `VILLA` / `LOCKE &` / `KEYES` | 32 x 22 | 4.94 - just short |
| **three lines** | **34 x 22** | **5.19 - clears Sierra's 5** |
| three lines | 36+ wide | 5.38, saturates |

Three lines is not a preference, it is **the only arrangement that clears her cap at all**.
`leading` was then made a property of the sign rather than the engine's fixed 0.16 - an
engraved plate sets its lines close - and at 0.05 the same three lines clear 5 in **19 rows
instead of 22**, handing four rows of facade back for nothing.

**A plate that clears 5 is 79% of the facade's width and 44% of its height, and the first
init showed what that really means**: a bronze slab over two thirds of the frontage with
four squashed windows underneath. So the building was redesigned *around* the plate as a
formal frontispiece - pilasters, cornice, a raised bay, a proper portal, two narrow glazed
bays. That lands the joke better than flats did: **a block pretending to be a practice
should look like a practice.**

### Three things that cost a pass each, and one rule out of them

**THE INIT IS A CONDITIONING IMAGE, NOT A SPECIFICATION.** Small changes to it move the
whole result, and twice a change meant to fix the doorway made the building worse:

- widening the portal to x 12..31 → the glazed bays became flat studded panels and **the
  railings disappeared**;
- painting the opening 14 wide so it would survive the model narrowing it → same loss, and
  it **failed `no_text_guard` on its own dentil mouldings**.

Both were reverted. The residual defect is reported rather than chased: **the CLOSED door
cel sits about a cel pixel proud of its painted reveal**, because the model drew the portal's
inner opening narrower than the init asked.

**The first pass also got the framing wrong** - "an apartment building" in a near-square
43x43 frame came back as a **doorway close-up**, no panel band and no cornice. A square crop
reads as a detail shot unless the whole elevation is named, so the prompt names it. And
**strength came down to 0.80 with control up to 0.65** against the board's usual 0.88/0.35:
those suit a facade whose composition the model can invent, and this one's composition *is*
the design.

### The door is hers, measured

view 751 **loop 2**, 12x12 at (29,44). Her cels are **fully opaque**, so the leaves cannot be
read from the alpha at all - the arrangement is in the **luminance**, where the revealed dark
interior grows from the centre: cel 0 no dark columns, cel 1 dark x 4..7, cel 2 x 3..8, cel 3
x 2..9. So **hinged both, leaves 6 / 4 / 3 / 2**, never fully open. Material is a dark
painted double door with brass furniture over a warm lobby - not glazed, because the facade's
door is panelled.

### The acceptance detector was INVERTED, and the measurement that caught it

The first `detect` was `redish` over (2,4,40,13). It reported her script **still present at
0.74** on six surfaces after the building was replaced. The check that settled it was to run
it against **her own set, where her script certainly is present** - and it scored only
**0.67-0.70 there**. *It scored higher after her art was gone than while it was there.*

Her script is **mostly pink**: #E07080 carries 76 px of the band against #E03838's 27 and
#C03838's 18, and `redish` requires `g < 100`, so it rejected most of the lettering and left
a template **11.5% dense** that matched whatever red was nearest. `neonscript` keys on
red-*dominance* by difference instead - 186 px, a template **43.2% dense** - and now reads
**0.80 on her set and 0.65 on ours**. Clean, and discriminating in the right direction.

**A detector must be validated against the set where the thing IS present.** Running it only
against the set you have just cleaned cannot distinguish "it worked" from "it never looked".

### A refused generation is now quarantined

`no_text_guard`'s refusal used to leave the art at `generated_WxH.png` - the exact name every
downstream tool opens - while printing *"do not install them"*. I then chained the install
after the generation with `;`, which in PowerShell runs regardless of the exit code, and
**installed a facade the guard had just refused**. The guard worked; the handover did not.

`gen_building` now renames it to `REFUSED_generated_WxH.png`. A file that must not be
consumed should not sit under the name that means "consume me" - the refusal is structural
now rather than advisory.

### Checks

Acceptance **CLEAN**. All three clauses clean: 0.0% of her art inside the silhouette, 0 px
stranded, 369 px in the margin and **all of it join rim**. Glyph drift IoU 0.971, centroid
0.053 cel px. Fill 97%, 860x860 = 739,600 px inside the 810k ceiling.

**Not delivered:** the entryphone, the camera on its bracket and the clipped box hedges did
not render. At 43 cel px wide an entryphone is about one cel pixel, so this is close to the
floor of what the region can carry; it is reported rather than retried.

**Four spoken clips name the old building and none is renamed** - and they are not the same
kind of line. **166** and **169** are the *Rental Office's* sales patter about the product;
**193** and **196** are this building's own. A future rename decision would treat them
differently. There is no greeting band.

## What the art would cost in the APK, measured

`Jones.App.Android.csproj` does not ship the upscaled art and `JonesRuntime.cs` never calls
`RenderScale.UseAssetRoot`, so **Android renders at 1x today** — genuinely low resolution,
exactly as Zain says. The numbers, measured on disk (PNG is already deflate-compressed, so
a zip/APK adds essentially nothing — file size is the APK cost):

| set | files | size |
|---|---|---|
| `assets/png` (1x, what ships now) | 829 | **0.84 MB** |
| `assets/png4x` | 1070 | 45.20 MB |
| `assets/png4x-cast0` (the overlay) | 59 | 4.47 MB |
| `assets/png12x` | 1070 | 229.19 MB |
| `assets/png12x-cast0` (the overlay) | 59 | 30.19 MB |

**The board art alone**, which is what this programme touches:

| | board.png | pic_11.png | both |
|---|---|---|---|
| 4x | 0.91 MB | 0.91 MB | 1.82 MB |
| 12x | 6.94 MB | 6.94 MB | 13.89 MB |

**Three things that matter for the decision:**

1. **`board.png` and `pic_11.png` are byte-identical twins.** Shipping one halves the board
   cost outright: **0.91 MB at 4x, 6.94 MB at 12x**.
2. **The `cast0` sets are overlays, not standalone.** `RenderScale` takes the largest factor
   at which a file exists, so shipping at a rung means shipping the ported set *plus* the
   overlay: roughly **50 MB at 4x** and **259 MB at 12x**.
3. **12x for the whole art set is not shippable as a plain APK** — Google Play's limit is
   200 MB for the base APK. 4x at ~50 MB is comfortable. A middle path exists and is worth
   putting to Zain: **ship 4x everywhere and 12x for the board only** (~50 MB + 7 MB), since
   the board is the screen he is looking at.

## Three notes for whoever is next

- **`ndarray.ptp()` was removed in NumPy 2.0** and this machine runs 2.5.3. Use `np.ptp(a)`.
  It took out the grain on the first sign ever drawn.
- **The generated asset sets are not in git.** `assets/png{4,12}x-cast0` is untracked, so a
  "before" cannot be recovered from history — copy it before installing over it.
- **`sign_building.py` prints the board as a percentage of the REGION, not of the building.**
  The Factory's region is 59 wide and its building 42, so the tool says 61% where the
  meaningful figure is 86%. Read the proportion against the façade.

## 47. MONOLITH BURGERS → BUN BELIEVABLE, and the clip that was a convex hull

His picture (`tools/samples/Bun Believable.png`, 1456x1456) is the first NON-PHOTOGRAPHIC
source the pipeline has taken: hard-outlined pixel art at roughly 26 source pixels to the
cel pixel. Four things were measured before anything was built, because three of them could
have ruled the approach out.

**1. His lettering is kept, and that is a measurement rather than a preference.** The
standing instruction on every sign WE set is that it matches the house face. His name is
already drawn into the picture on two lines. At the chosen fit, where 1 cel px is 26.0
source px:

| | cap height, cel px |
|---|---|
| his `Bun` | 4.08 |
| his `Believable` | 4.04 |
| OUR house face, two lines in his 25x11 red panel | 2.70 |
| OUR house face, given a 35-wide box that does not fit the panel | 3.75 |
| OUR house face, one line across the whole 42 px façade | 3.20 |

His lettering is the most legible version of this name available at this size, by a third.
Setting it in the house face would make the name **harder** to read, which is the opposite
of what that instruction is for. Reversing this is a `cover` over the panel plus a `sign`
dict and it costs 1.3 cel px of cap.

**2. The pole sign stays.** Her own cel 6 has a red pole sign with a blue M at the left, a
giant M on the roof, a red fascia and a glazed shopfront with a central door. His picture
mirrors it element for element. Dropping it would have been the one change that made the
new building less like the old one than it had to be.

**3. A standard downscale does not mush pixel art.** The worry was that 26:1 BOX averaging
would soften outlines that are one art-pixel wide. Measured as the p99 one-pixel luminance
step at the shipping rung: BOX 22.0, LANCZOS 21.0, NEAREST 24.0 (means 3.5 / 3.6 / 3.6)
against his own source at 12.0. The downscale makes the edges *harder*, not softer, because
it is packing more contrast into fewer pixels. The photographic path was used unchanged.

**4. The crop.** 1050 x 1075 = 0.9767 against the cel's 42/43 = 0.9767, so nothing is
distorted and nothing is cut, scaled by 0.8000 to 840x860 at 20 px per cel px.

### THE DEFECT: A PER-COLUMN EXTENT IS NOT AN OUTLINE

The first clip was the Z-Mart recipe — one rect per cel column, first built row to last.
That is a **convex hull per column**, and it is only an outline for a single convex object.
His picture holds **two** objects standing apart, a pole sign at the left and the burger at
the right, so each column's full extent swallowed the plaza lying between them and a block
of his pink pavement rode onto the board at the left and the bottom-left.

The clip is now authored as separate shapes: the pole sign's board, its mast and its
splayed foot as three rects, and the burger as its own stepped profile. 27 rects, 75.4% of
the region, against the old version's 42 columns.

**The crop's bottom edge was wrong by 34 source rows, and the clip is what found it.**
Taking the base as "the last row more than half built across its width" gave y 1157; more
than half of that row is his PLAZA, not his building. His shop's own base line is at
y 1113. The crop now ends on the building, so the clip's bottom edge and the cel's bottom
row are the same line. Both axes shrank by the same 4.09%, a pure zoom about the crop's
top-left corner, so every coordinate measured on the old fit carried over multiplied by
1.0409 rather than being re-read.

### WHAT THE AUDITS SAY, AND WHAT THEY COULD NOT HAVE SAID

| clause | monolith |
|---|---|
| 1 — her art inside the silhouette | 0 px, 0.0% |
| 2 — ours stranded outside the region | 0 px |
| 3 — SLAB in the margin | 0 (5031 px of join rim, which is inherent) |
| margin still hers | 451 of 1806 px, 25.0% |

**None of those three clauses could have caught the pink-plaza defect.** It was inside the
region and inside the declared matte, so every pixel of it was legitimately "ours". This is
the same shape of hole as section 38: an audit that asks *whose pixels are where* cannot ask
*is this the right picture*. It was found by looking at the matte over magenta at 20x.

### AND THE NO-TEXT GUARD IS BLIND ON THIS BUILDING — WITH A REPRODUCTION

`no_text_guard` passes on the installed façade. That pass is worth nothing, and here is the
proof, run three ways on the same region at 12x:

| input | result |
|---|---|
| HER original 42x43 region, which says `Monolith` | **FAIL** — 8 blobs on baseline cel y 26.0, span 14.1 cel px |
| OUR installed 42x43 region, which says `Bun Believable` in larger letters | pass |
| OUR same pixels cropped to 28x10 around the words | **FAIL** — 7 blobs on baseline cel y 7.8, span 17.7 cel px |

So the guard is not broken and it is not blind to his face. It is defeated by the amount of
surrounding texture: the bun's sesame seeds and the burger's layers put enough blobs on
enough baselines that the row test stops discriminating. Scanning a whole region of busy
art, it returns a false negative on lettering it detects perfectly well in a tight crop.
**A guard that only works on a quiet background is not a guard on a busy one**, and until
it is fixed its pass on any supplied composite should be read as "no information".

### THE DOOR, loop 10 at (288,75), AND IT ONLY MOVED BY ONE

Bun Believable is the first supplied building whose art already had a glazed double door in
roughly the right place, so `CastDoors.cs` gets a nudge rather than the Factory's rescue.
Measured on the installed façade at 12x as the median of each 12x block:

```
y 65  FDD34E  the cheese fringe above the entrance - not the door
y 66  777880  the transom's top edge
y 67  CAC8CB  the lit head rail
y 68  71767E  the head
y 69..74      the glazing, 82A6C4 / A7C4D8, pull handles at 4E5254
y 75+ FCF4EA  HER cream path - the building has ended

across, at y 71:  78787F jamb at x 284 | panes x 285..292 | 8D989E jamb at x 293
                  handles at x 288 and x 290, meeting stile at x 289
```

Her footprint is 14x10 at (288,75) — x 281..294, y 66..75, leaf body rows 0..7.

| | |
|---|---|
| dx 0 | leaf centred on x 287.5; his door's centre is x 289 |
| **dx +1** | centred on x 288.5, and the two meeting stiles line up |
| dy 0 | leaf body y 66..73 — one row of his glazing shows under the leaf and the opening stops a row short of the floor |
| **dy +1** | leaf body y 67..74, **bottom flush** with his glazing; `head_rows` 2 lands exactly on his transom and rail |

`LocationId.MonolithBurgers => (1, 1)`. The leaf widths did not have to be chosen either:
her loop 10 cels are fully opaque over the body so the alpha says nothing, but the column
means over rows 0..7 say everything — cel 0 has no dark column at all, cel 1 is dark at
cols 6..7, cel 2 at 4..9, cel 3 at 2..11. That is 7, 6, 4, 2 from each side, read off her
own art. `opening_cols: (3,10)` then keeps the hole inside his jambs.

`from_facade` makes cel 0 **pixel-identical** to the board under its own alpha: max 0.000,
mean 0.000.

### `foot_rows`, WHICH IS `head_rows` MIRRORED, AND THE CEL THAT NEEDED IT

Her sprite is ten rows and the bottom two hold only the swinging feet — empty in cel 0, and
11, 7 and 3 pixels in cels 1..3. On this façade those rows are y 75..76, her own cream path,
below the building entirely. Painted as interior, cel 1 ran a column of lit shop out of the
doorway and down onto the pavement. `foot_rows: 2` keeps them as façade, which is what is
actually down there, and the leading stile now stops at the same bound.

### AND IT IMMEDIATELY FIXED A BUILDING I WAS NOT WORKING ON

**Installing this building broke the General Dealer's door, three rows above it.** The
General Dealer's sprite is 25x11 at (270,24) and its building's base row is 31, so its
bottom three rows hang at y 32..34 — which is inside *this* building's region, and the top
of Bun Believable's giant B now occupies x 283..294 of exactly those rows. Section 45 had
painted those rows as invented paving, reasoning from what was on the board at the time:
lawn. The result was a grey slab floating over the burger shop's sign.

`threshold_rows` is now 0 for the General Dealer and `foot_rows` is 3. **The rule that
cannot go stale is the one that does not guess:** below the General Dealer there is no
General Dealer, so those rows stay façade and show whatever the board has there — lawn
yesterday, a neighbour's sign today. The measured `pave`/`mat` values stay in the table
because they are off his photograph and the next building with a real step will want them.

This is also the first time one of our buildings has been close enough to another to matter.
`margin_check` reports 783 px differing outside this region at y 18..29 and names them
correctly as a neighbour's work; they are entirely inside the General Dealer's own region.

All five `from_facade` doors were re-verified after the rebuild — General Dealer, Bun
Believable, The Industrial Revolution, Bare Necessities, Watt a Bargain — and every shut
cel is max 0.000, mean 0.000 against the board under its own alpha.

### THE INTERIOR, INVENTED ON THE GENERAL DEALER'S PRECEDENT AND CORRECTED TWICE

Both sources are empty for the same reasons they were at the General Dealer: his glass
reflects the street (82A6C4 at the centre of the panes, A7C4D8 at the top — sky, not a
room) and her cel 3 is the generic ladder she gives every loop in view 751 — 281818,
483838, 604848, 682028, 984028, byte for byte the same as loop 11's. So it is lifted for the
inside of this opening only, and there is **no lettering of any kind** in it: a burger shop
wants a menu board and a menu board is exactly what rule 1 forbids.

Three readable things at 8 columns by 6 rows — a dark soffit, a warm lit wall, and a
**counter**, which is what makes it a food shop rather than the General Dealer's aisle.
Two corrections, both from looking at it at 12x:

- the back wall was a flat rectangle, so it got the warming-rack rhythm. Drawn in the
  counter's own pale tan across the whole wall it read as a **garage shutter**; it now sits
  in `in_rack`, one step off the wall's lit tone instead of four, over the upper half only,
  at a quarter duty.
- the lit wall was inset 0.4 of a column on the left and 0.9 on the right, which is visible
  at 12x. An `interior` rect runs from `x0` to `x1 + 1`, so a 0.45 inset at each side is
  written `x1 = 9.55`. The same off-by-one put the first floor two rows below the opening.

Deliverables: `tools/samples/monolith_before_after_{12,4}x.png` and
`tools/samples/monolith_door_{12,4}x.png`, all at native resolution.

### THE `Monolith` STRINGS, REPORTED AND NOT CHANGED BY THIS WORK

| where | string |
|---|---|
| `text 700[81]` | `Monolith` — the board's own Place label |
| `script 206 @0x111C` | `Monolith Burgers` — the Employment Office's employer menu |
| `script 217 @0x04D7` | `Monolith's Jobs Available:` — that shop's job-list header |
| `text 210[0..19]`, `[47]` | the twenty greetings and one purchase line |

`CastNames.cs:69` already maps `LocationId.MonolithBurgers` to `Bun Believable` for the
board. The 21 spoken lines in text 210 **were** rewritten and re-recorded — see section 48 —
but the three resource strings above are still Sierra's bytes and are the coordinator's to
decide on.

## 49. THE LAST SIX DOORS, AND THE LEAF WIDTHS WERE DERIVED RATHER THAN READ

Hire Ground, Rentflix, Casa El Cheapo, Open Door University, Needful Things and Common
Threads. With these, **all eleven rebuilt buildings now take their door leaves
`from_facade`**, and every shut cel is pixel-identical to the board beneath it: max 0.000,
mean 0.000 under its own alpha, on all eleven.

### A METHOD FOR `w`, BECAUSE THE OBVIOUS ONE FAILS

Her leaf widths had been hand-read off luminance, building by building. That does not scale
and it is not reliable: loop 7's opening is **five levels** darker than its own leaf, and a
per-column *drop* against cel 0 is defeated by dithering on loops 10 and 11, which move
columns by 20 either way. Both methods disagreed with the three hand-measured tables.

What works is a colour test:

1. take the colours cel 3 **gained** over cel 0 across the leaf body (≥ 3 px). That is
   Sierra's interior ladder for that loop — `281818 / 483838 / 604848 / 682028 / 984028` on
   most of them, `682028` alone on the two she drew darkest.
2. per column, the fraction of the body painted in any of them.
3. a column is open above 0.6; `w` is the narrower of the two leaves.

It reproduces the Factory's `[5,4,3,1]`, Bare Necessities' `[7,5,4,3]` and Bun Believable's
`[7,6,4,2]` **without being told them**, which is the check that makes its six new answers
worth trusting.

| | `w` | derived |
|---|---|---|
| Hire Ground | 5, 4, 2, 1 | |
| Rentflix | 7, 6, 3, 1 | hinge LEFT — a single leaf off the left jamb |
| Casa El Cheapo | 4, 3, 2, 1 | |
| Open Door University | 5, 4, 3, 2 | the one luminance could not read at all |
| Needful Things | 5, 4, 2, 1 | |
| Common Threads | 5, 3, 2, 1 | |

**Two disagreements with what was already in the table, and they are handled differently.**
Rentflix's cel 1 was `4` and measures `6` — at 4 the door jumped from shut to two-thirds
open in one frame, and since this door was being rebuilt anyway the measurement was taken.
Watt a Bargain's cel 1 is `4` in the table and measures `6` too, but that building is
shipped, approved and not in this job, so **the 4 stands and this is the note** — if its
cel 1 ever looks like it opens too far too fast, that is where to look.

### THREE MOVED, THREE DID NOT, AND ZERO IS ALSO A MEASUREMENT

Every offset was read off the installed façade at 12x as the median of each block.

| | his painted doorway | her footprint | offset |
|---|---|---|---|
| Hire Ground | x 92..101 y 172..182 | x 92..101, body y 171..181 | **(0, +1)** |
| Rentflix | x 102..108 y 17..24 | x 102..108, body y 19..27 | **(0, −3)** |
| Casa El Cheapo | x 139..146 y 23..33 | x 140..147, body y 23..28 | **(−1, +5)** |
| Open Door University | x 218..221 y 168..177 | x 215..224, body y 168..177 | (0, 0) |
| Needful Things | x 216..225 y 23..32 | x 216..225, body y 23..32 | (0, 0) |
| Common Threads | x 274..279 y 106..111 | x 273..281, body y 103..111 | (0, 0) |

Three of the six match **horizontally to the column** — the first exact matches in the
programme — and three needed nothing at all. A building that does not need an offset must
not get one; its absence in `CastDoors` is what keeps Sierra's own coordinate authoritative
for it.

**Rentflix is the only NEGATIVE offset so far.** A building may overhang the marble's path,
and it may also sit *higher* than she drew it; the rule in `CastDoors`' own remarks holds
either way, because what has to be under the sprite is the doorway.

**And the `dark/wall` ratio has now been wrong three more times in the same direction.** It
cleared Hire Ground at 0.40 and Rentflix at 0.54 — both of which needed an offset — and had
flagged Bare Necessities at 1.68 and Monolith at 1.24, neither of which did. It stays as a
first-pass hint and nothing rests on it.

### CASA EL CHEAPO DOES NOT FIT, AND THE CHOICE IS WHICH HALF OPENS

His timber door is **eleven rows**; her loop 0 sprite is 8x8 with a **six-row** body. No
offset makes a six-row leaf cover an eleven-row door.

- `dy 0` — body y 23..28, his door's top six rows. The opening is a hole in the upper half
  of a door whose lower half is still painted shut, and the marble walks into the part that
  did not move.
- `dy +5` — body y 28..33, bottom flush. His top five rows stay façade and read as the
  door's head; **the opening reaches the floor**, which is the thing Zain asked for by name.

+5, and it is the Factory's rule applied unchanged: seat her *leaf's* bottom on his door's
bottom, never her cel's.

### `opening_cols` EARNED ITS KEEP TWICE

- **Open Door University** — her sprite is 10 wide and his pointed arch is **four**
  (x 218..221, measured at 4..14 against the stone jambs' 91..114). `(3,6)`. `head_rows` 1
  as well, because the arch is pointed and its top row is only two columns wide.
- **Common Threads** — his opening is six columns and the **mannequins** flanking it
  measure 187..232 against the doorway's 37..66. `(1,6)`; without it cel 3 opens a hole
  straight through both of them. `head_rows` 3, because rows 0..2 of her footprint are his
  pale shopfront rail, not the door.

### ONE RECIPE FOR SIX INTERIORS, AND IT WAS TOO LIGHT FIRST TIME

Each opening gets three things and no more: **her own ladder** as hard-edged bands, used
verbatim because it is the only evidence about any of these interiors; a **structure** of a
dark soffit, a lit back wall inset from both jambs, and a floor; and **furniture only where
the opening is nine columns or wider** — a counter at Hire Ground, a rack of pledges at
Needful Things. At four to eight columns, structure is value, and that finding is not
re-litigated per building.

The first build used values pitched like Bun Believable's, whose leaves are **glass**.
These leaves are dark painted doors — Hire Ground's medians at `3D2717`, Open Door
University's portal at `0C0A06` — so a mid-tone interior came out *lighter* than the door it
replaced and all six read as pale slabs. The rule that was missing: **an opening is darker
than the leaf that was covering it**, whatever the leaf is made of. Re-pitched against each
door's own measured p10, they read as doorways.

### WHAT THE SWEEP FOUND THAT WAS NOT PART OF THE JOB

- **A DUPLICATE KEY IN `MATERIAL`.** Rentflix had a second entry *later* in the same dict
  literal — the synthetic glazed leaf its first door was built from. Python takes the last
  one silently, so the `from_facade` entry would have been thrown away with no error and no
  warning. Removed; the file is now checked for duplicates with `ast`.
- **WATT A BARGAIN PAINTS ITS FEET.** A sweep of all eleven doors for interior painted below
  the leaf body — the fault `foot_rows` exists for — comes back zero everywhere except
  Socket City's loop 8, which puts 1438 px of `in_floor` in its feet rows at y 182..183.
  Those rows are inside its own region, so this is not the General Dealer's grey slab, and
  its opening is only three rows tall as it is; `foot_rows` there would cut it to one. The
  real fix is the one its own note already states — a façade with an entrance in it.
  **Reported, not changed.**

Deliverables: `tools/samples/six_doors_{12,4}x.png`, all four cels of each, at native
resolution, composited on the board exactly as the game draws them.

## 48. THE RENAME REACHED THE VOICE, AND ONE LINE IS TOO LONG

This is the first rename where the spoken clips did **not** keep the old name. The standing
decision in sections 2 and 11 — *a subtitle disagreeing with its own audio is worse than a
stale name* — turns on the audio and the text disagreeing; rewriting both removes the
disagreement rather than overruling the rule. `gen_voice.py`'s `RENAMED` table is a report
of clips whose audio still contradicts their text, so Monolith is not in it: there is
nothing left to report.

22 ids, from `subtitles.json` against `subtitles.sierra-monolith.json`:

- **610..629**, the twenty greetings. All existed in `speech-kokoro`, so these are a
  regeneration against new words.
- **657**, `fastFood.sc`'s purchase line. `Remember, if it's a 6-lb. Beefburger, it must be
  a Monolith!` — the rename is the joke, so it could not be left alone.
- **545**, `market.sc`'s first purchase line, which is **Bare Necessities'**: her clerk says
  he used to work at the burger shop.

545 and 657 are NEW coverage — neither was in the kokoro set. Both sit outside their
location's GREETING band, which is all `VOICES` held, so `gen_voice.py` grew an `extra`
field rather than a wider band: widening `MonolithBurgers` to its purchase range would have
generated 35 clips nobody asked for. Each takes its own location's existing voice and speed
— `am_fenrir` 1.02 for Bare Necessities, `am_puck` 1.12 for the burger shop. No new voice
was invented.

**The regeneration is deterministic and that was checked rather than assumed.** All 135 ids
were regenerated and hashed against the previous set: 113 byte-identical, exactly the 20
greetings changed, 2 new. Nothing else in the set moved.

### `1987` IS NOT A NUMBER

espeak reads it as `nineteen HUNDRED eighty seven` — eight syllables for a four-syllable
word, and wrong English as well as long. `tts_input()` grew a year rule, which is a
pronunciation fix of exactly the kind that function already does and changes nothing the
player reads. Clip 610 went from 5.70 s to 5.38 s. The rule stays; every other year in the
game gets it.

**And then the LINE was cut, which is the better fix and was not mine to make.** The
coordinator took the measurement below and rewrote 610 rather than shaving its
pronunciation: *"...has held the title for years."* The joke is that nobody else has ever
won it, and "for years" says that in two syllables instead of five. **4.56 s**, inside her
envelope and no longer the longest of the twenty. The sequence is worth keeping as a
pattern: measure, fix what the tool can fix, report what is left, and let the person who
owns the words decide whether the words are the problem.

The first version of the rule matched 1000..2099 and caught the Bank's `fixed-rate CDs spin
at 1500 RPM`, turning *one thousand five hundred* into *fifteen hundred*. That may be better
English, but 305 is a clip that was generated and installed, and a pronunciation rule
written for one line is not a licence to redub another. The rule is `19xx` and `20xx` only.

`--only` also used to **truncate** `MANIFEST.json` to the ids it regenerated. It now carries
the untouched entries over.

### THE SYNC DRIFT, MEASURED, NOT FIXED

`assets/audio/sync` is Sierra's and nothing regenerates it. `MouthTick` already tolerates a
clip outliving its table (MainViewModel.cs:1641-1671): the mouth shuts, the balloon stays up
until playback ends. An **under**-run is self-correcting — when the clip ends the position
goes null, every remaining cue fires at once and the mouth closes on the last word. Only an
over-run shows, as a still mouth under a voice still talking.

**15 of the 22 over-run.** Mean +0.35 s, worst **+1.49 s (clip 627)**. Over the whole set the
rename made it slightly better, not worse: 48 of 135 clips now outlive their table, against
49 of 133 before, and the worst in the set is still clip 50 at +2.91 s.

> An earlier draft of this section said 13 and it was a miscount of my own table — the
> positives were always 545, 610, 611, 613, 615, 616, 617, 618, 619, 623, 624, 625, 627,
> 628 and 657. Recorded rather than quietly corrected, per rule 6.

**Every one of the twenty now sits inside Sierra's own envelope**, which runs 2.63 s to
5.02 s for this shop: 3.02 s (629) to 4.61 s (627). Before the line was cut, 610 was the
only exception at 5.38 s — 0.77 s longer than the next longest and 2.10 s past its own
38-cue table — and it mattered because the greeting blocks input on entry. At 4.56 s it is
now the second longest and inside the range.

The two purchase lines are not greetings and were not measured against that envelope: 657
is 3.93 s against a 3.42 s table, 545 is 5.19 s against a 4.43 s one.

No line is too long for the **balloon**: wrapped at 100 px in font 1 the new lines run 4 to
6 lines against her 4 to 6, and the game already draws 12 elsewhere.

## 46. `CastDoors.cs` - THE ART SET SAYS WHERE ITS OWN DOORS ARE, and the number is +11

The coordinator's mechanism, not mine: a new `src/Jones.App/Jones.App/CastDoors.cs`, called
once after `DoorFor`. Sierra's table is untouched; with no art set in force every offset is
zero and the doors land exactly where `room1.sc` puts them. It is the fourth option against
the three in section 43, and it costs none of what they cost.

### The offset was +9 and it is +11, and the difference is HER ALPHA

The base-row difference IS nine - his 190 against her 181 - and that is what an offset
derived from the BUILDING gives. But what has to land on his door is her LEAF:

| | |
|---|---|
| her loop 5 cels | 81.8% opaque: **rows 0..8 are the leaf body**, rows 9..10 are empty in cel 0 and hold only the swinging feet in cels 1..3 |
| his lintel | y 183..184, read off the board at 12x |
| **his door leaf** | **x 47..51, y 184..190** |
| his step | y 190..191, pavement from 191 |

| dy | her leaf body covers | |
|---|---|---|
| +9 | y 180..188 | a row high - a row of his own door shows below the sprite |
| +10 | y 181..189 | still one row of door showing at y 190 |
| **+11** | **y 182..190** | **bottom flush with his door's bottom**, feet on the pavement |

Her leaf body is nine rows and his door is seven, so the two spare rows land on his lintel -
which `head_rows: 2` then keeps as façade. **A building-derived offset is not a door-derived
one, and the door is what has to line up.**

### Two more bounds the cels needed, both measured

- **`opening_cols: (2,6)`** - her sprite is 10 cel px wide and his door is 5. At cel 3 her
  leaves are 1+1, so the opening ran two columns past each jamb and ate his brickwork. Her
  leaf widths still drive the timing; this only stops the opening crossing the reveal.
- **`head_rows: 2`** - without it the opening swallows his stone lintel and the doorway
  loses its head.

### And `from_facade` is finally right here, which is the point of the whole exercise

With the sprite on his own personnel door at last, the shut cel can be the façade pixel for
pixel. Measured on the installed cels: **mean |d| 0.000 at 4x and 12x**, her alpha exact, and
the opening each pair of cels shares is **identical, 0.000** - one fixed scene the leaves
slide across. The interior is HER OWN loop 5 ramp (#281818, #483838, #604848, #682028,
#984028), so nothing here is invented.

**Watched at native 12x with the marble at its stop: the marble arrives at his personnel
door and that door opens.** `tools/samples/factory_door_cycle_12x.png` and `.gif`.

### THE OTHER TWO DO NOT NEED AN OFFSET, and the measurement says why

| building | her door's bottom row | the building's base row | offset |
|---|---|---|---|
| Bare Necessities | 105 | 105 | **flush - none** |
| Watt a Bargain | 183 | 183 | **flush - none** |

Both were on the earlier list as "placement defects" on a `dark/wall` ratio. That ratio was
wrong about both: at **Bare Necessities it read a PALE entrance bay between two dark roller
shutters as "no opening"** - the sprite is on the shop's own entrance and always was, and it
now opens there with the shut cel identical to the façade.

**Watt a Bargain is the one that does not come out right, and no offset can fix it.** Her
door is 14 cel px tall; our generated showroom puts its ground floor in the last THREE rows,
y 181..183, with a rounded display window above. `head_rows: 11` confines the opening to the
entrance band so the window survives, and that is the best this façade can do: the door
opens as a shallow recess in the shopfront. **Her interior values are wrong for it too** -
painted with the #682028 she uses behind every other door, the opening came back as a maroon
block in a showroom measuring #8E7458, so this one door uses its own building's values. **The
real fix is a façade with an entrance in it, and that is a generation, not a door job.**

### One source of truth for the offsets

`tools/door_cels.py` does not keep a copy of the numbers: it **parses `CastDoors.cs`**, the
file the game itself reads. `from_facade` crops the shut state from the board at the
footprint, so a Python copy that drifted from the C# one would paste the wrong part of the
building in as the door and nothing downstream could tell.

Three faults found while doing this, all recorded in the tool: a facade material has no
`leaf` colour, so `from_facade` must skip the synthetic leaves entirely rather than painting
them and throwing them away; the leading stile was drawn over the full cel height and ran a
line across Watt a Bargain's display window; and the "one fixed scene" check must mask by
alpha, or her differing feet rows report a difference the player cannot see.

## 45. THE GENERAL DEALER'S DOOR: TAKE THE LEAVES FROM THE FACADE

Loop 11, 25x11 at (282,34), four cels into `cast0` at both rungs. The footprint covers game
x 270..294, y 24..34 and lands on his glazed entrance (dark/wall **0.67**).

### THE DOOR HANGS THREE ROWS BELOW THE BUILDING, AND IT IS HERS

**A correction to my own first report, which said this building "does not carry the
Factory's placement defect" on the strength of `dark/wall 0.67`. That number is a
HORIZONTAL test and cannot see the bottom edge** - the exact shape of guard failure this
file is full of.

| | |
|---|---|
| her door cel | y 24..**34** |
| her Z-Mart cel, 49x30 at (258,2) | y 2..**31** |
| so | **three rows of the door are below the building** |

Measured on HER board, which is the question the Factory's case made standard: her building
ends at y 31 and her door covers to y 34, so **her own door hangs three rows below her own
building** - the Pawn Shoppe's finding (section 12) with three rows instead of two. Our fit
puts his threshold on y 31, her base row, so **nothing of ours moved it**. That is the
difference from the Factory, where his base sat nine rows below hers because we put it
there, and it is why **re-fitting is the wrong answer here**: raising our base to meet the
door's feet would make our building taller than hers, which section 12 settled as a
fidelity change dressed as a fix.

**What was actually wrong was the paint, not the placement.** Those three rows were painted
as the shop's FLOOR, and on the regenerated board the ground under the door is LAWN - her
own board has a pale forecourt at y 32..33 (#C8E0F8, #F0F8F8) and her lawn only from y 35,
and the regeneration replaced that forecourt with grass. A shop floor running onto grass is
what "spilling onto the grass" was. It is now a paved threshold with an entrance mat.

### Her animation, measured off her own luminance

Her cels are fully opaque, so the arrangement is in the luminance, as Villa's was. The
opening grows symmetrically about column 12:

| cel | columns below luminance 70 | leaves |
|---|---|---|
| 0 | none | shut |
| 1 | 12 | 12 + 12 |
| 2 | 9..15 | 9 + 9 |
| 3 | 7..17 | 7 + 7 |

A **sliding pair**, not a swing - which is also what his facade has. A sliding leaf does not
narrow in life, it slides behind the fixed glazing beside it; at 25 cel px those are the
same picture, and this is hers.

### THE FINDING: paint the opening, not the leaves

Synthetic glazing was built first - a slim frame, a reflection gradient, a specular streak,
all sampled off the facade - and composited it read as **a flat dark slab**, twice. The
shading was not the cause. **The sprite is 25 cel px wide and his entrance is 18**, so it
covers four columns of his FIXED glazing at each side, and that glazing carries bright
reflections of the car park and a white van. Any uniform glass painted across the footprint
is darker than what it covers at the edges and lighter in the middle, so the cel reads as a
patch the moment the door animates.

So `MATERIAL["zmart"]["from_facade"]` takes the leaves from **the installed board at the
cel's own footprint**. His painted doors ARE the shut state, so:

| | |
|---|---|
| cel 0 against the facade, inside the leaf mask | **mean \|d\| 0.000 at 4x and at 12x** |
| her alpha reproduced exactly | **yes, both rungs** |
| the opening, cels 0..3 | **0 → 10 → 70 → 110 cel px** |

The shut door is pixel-identical to what is already there, so **the animation starts and
ends with nothing changing** - the class of fault this project keeps finding, made
impossible rather than measured. The paste is done at the RUNG, not at the 4x supersample:
boxing a pasted facade down softens it, and a shut door one blur softer than its own wall
is the sticker fault again.

### What the open state shows, decided rather than defaulted

A glazed door is not a hole that appears. When the leaves slide apart the REFLECTIONS go
with them, so the first version painted the opening as the interior seen directly - a cold
grey-green - and composited it read as a hole punched in the building.

**Her own cel 3 is the only evidence available about what is inside this shop, and it says
the interior is lit and warm**: #281818 at the head, warming down through #483838 and
#604848 to #682028, with a #984028 floor at the threshold. The opening follows her, in his
palette. **Her head rail is kept too** - her cels open from row 1 down and leave row 0 as
the door's head, which on this facade is his transom.

### AND HER BANDS HAVE EDGES - a ramp between her end values is a flat wash

The first version interpolated smoothly between those values and at native 12x it read as
**a uniform brown rectangle**, which is the shape Zain calls a block. Her cel 3 is not a
ramp: it is hard-edged bands, #281818 over rows 1..3, #483838 over 4..5, #604848 over 6..7,
and the warm step below. **Three steps of value ARE the structure at 25 x 11**, and
`MATERIAL["zmart"]["bands"]` now draws them as she drew them.

What his photograph DOES supply is the threshold: paving at #64696E with a dark entrance mat
at #404A55, which is what the three rows below the building carry.

### THE INTERIOR IS INVENTED, AND ZAIN SANCTIONED IT - which is the whole provenance

His photograph has **no interior in it to copy**: his doors reflect the car park almost
entirely, measuring #353834 behind the glass at the lower centre and #121F25 at the upper,
both reflections. Her cel is four values. So shelving, a counter, depth and light could only
be invented, and this file's whole purpose is that invented things are findable later.

It was put to him as a trade and he took it: **"yes give it a lit shop interior."** That
ruling covers **the inside of this opening only**. The facade, the threshold, the mat, her
head rail and her alpha are untouched; cel 0 is still the facade pixel for pixel; and
**CLAUDE.md rule 1 is not what was lifted** - there is no lettering anywhere inside that
shop, and `no_text_guard` passes on the region with the door open.

**STRUCTURE AT ELEVEN COLUMNS IS VALUE, NOT DETAIL**, which is this building's own finding
from two rounds earlier and governs what was drawn:

| | |
|---|---|
| a dark ceiling | #14100E, the top 0.6 of a cel row |
| **a lit back wall** down the aisle | #7A6248, with #967C5C where the ceiling lights reach it - the brightest thing inside, so the eye lands on it through a seven-column gap |
| **shelving** either side | racks at a 0.7-cel pitch, #503E2E fronts against #201814 shadow - the vertical structure is what makes it a shop rather than a hallway |
| the floor | #4A4238 at the back to #6C6254 at the door, COOLER than the wall: painted at the same warmth they merged into one tan mass and the wall stopped being the thing the eye finds |

Two things were tried and corrected: the shelving was one band per cel row and read as three
horizontal bars, so it is a run at 0.7 pitch; and the floor matched the wall's warmth.

**And it is ONE FIXED SCENE that the leaves slide across, which is checked rather than
assumed**: over every pair of cels, the opening they share is **identical, mean |d| 0.000**
at both rungs - 11,088 px between cels 2 and 3 at 12x. The player sees more of the same
shop, not three drawings of it.

**At the small rungs it degrades the right way.** At 4x the lit wall and the striped racks
both still read; at 1x the shelving averages to a mid-dark mass with the lit wall surviving
as a pale centre - it goes quiet rather than turning to noise, which is the failure mode to
want. Sheet: `tools/samples/zmart_door_rungs.png`.

### The visor

The cap throws a shadow across the entrance in his photograph and it is part of why the
building reads as a cap. The leaf shade sits **on the head alone**, two rows deep: the
first attempt shaded the top third by 0.62, which took the glass to about #2B2E29 - the
opening's own value - and that is half of why the early cels read as one slab.

### Checks

Audits unchanged by this work, because door cels are view files and do not touch the board:
**0.0% of her art inside the silhouette, 0 px stranded, 0 SLAB**, and the 6 px outside the
region are the town centre's. Her `assets/png` and `assets/png12x` are untouched.

`tools/samples/zmart_door_cycle_12x.png` and `.gif` are the whole cycle at native 12x with
the marble at its stop.

### What this changes about the other nine doors

`from_facade` is the right default **wherever the facade's painted door already sits on the
sprite's footprint**, and it makes those cheaper and safer: no material to match, no
flicker possible, about fifteen minutes each. That is Hire Ground (dark/wall 0.40),
Rentflix (0.54), Casa El Cheapo (0.66), Open Door University (0.90), Needful Things (0.99)
and Common Threads (1.07).

**It is exactly wrong for the three with the placement defect.** At the Factory it would
paste his WINDOW as the door leaf; at Bare Necessities a shutter panel; at Watt a Bargain a
window. For those it makes the defect vivid rather than fixing it, so they stay blocked on
the placement decision in section 43.

## 44. THE GENERAL DEALER IS A PEAKED CAP, AND THE FIRST ONE WAS BUILT FROM A DESCRIPTION

Zain: *"that looks absolutely nothing like the picture I shared!!!, it's now just a blue
building with a HUGE sign, look at my building!, it's a nice looking hat shaped roof with
glass, you gave me a god damn block!!!"*

The generated facade in section 41 is withdrawn. **The instruction that produced it was to
treat his picture as a MOOD reference rather than as the building**, and that is the whole
error: his picture is a giant navy peaked cap - an oval crown with gold piping, a gold band
with braid and a laurel GD crest, a glossy visor projecting over a curved glass curtain
wall. The silhouette IS the identity. Same route as his Factory: `source_image`.

### The fit, and one number that made it easy

His building spans image x 60..1175, y 62..745 - crown tip to the doors' threshold - which
is **1115 x 683 = 1.633:1 against the region's 49/30 = 1.633:1**. It fits the cel with no
distortion, nothing cropped off the sides, and `baseline` 31 puts his threshold on HER
building's own base row.

### What survives at 49x30, which is the question that was asked

| | at 1x | at 4x and 12x |
|---|---|---|
| the oval crown | **reads** - the arc, the overhang and the dark mass are unmistakable | reads |
| the gold band | **reads** as a band | reads, with the braid as a gold texture on it |
| the visor | **reads** - a dark ellipse under the band | reads, with its gloss |
| the glass box | **reads** - mullions resolve into a grid | reads |
| the braid | a gold texture | **reads as braid at 12x only** |
| **the laurel GD crest** | a gold blob, 9 x 4 cel px | a badge at 4x; the wreath and the letters only at 12x |

**The cap survives. The crest does not, at the rung that matters most.** It was measured
before it was promised: 9.5 x 4 cel px, against Socket City's device which was already
marginal at 13 px wide.

### The sign: Sierra's cap of 5 is NOT available on this building

The crown is an oval, so a wide box starts low on the arc and a narrow one starts high, and
the two trade. Measured against the real `text_mask`, and against his own pixels per column:

| box | cap | note |
|---|---|---|
| x 14..34, y 0..11 | 2.95 | the only one that fits above his gold piping, where HIS lettering is |
| x 10..38, y 2..11 | 4.10 | |
| **x 9..39, y 0..10** | **4.35** | **shipped** - ink x 11.8..37.5, y 1.1..9.9 |
| a 39 x 13 plate | 5.35 | 13 rows, which the crown does not have - this is the rejected look |

Two lines at cap 5 need 13 rows. The crown's navy runs **11 to 12 rows** between its arc and
the gold band at the width the name needs, and only **8** above his gold piping - which is
why his own type is small. 4.35 clears the ~4 at which a reader stops reading letters, and
it is 1.4x the 3.10 a single line gets on the same building.

**And the bottom bound is his CREST, not the band.** Its laurel starts at cel y 9.0 across
x 28.4..37.9, so a box that merely clears the band puts DEALER's last letters on the wreath,
which the first render did.

### `plate: False` - a sign with no board

Painting the plate in the crown's own navy was tried first and **it still read as a
rectangle**, because the crown is a cylinder: it measures #101F2F at its ends against
#203451 in the middle, and no single face colour sits on both. `sign_board` now takes
`plate: False`, which skips the shadow, the board, the bevel and the bolts and signwrites
the letters straight onto the facade - his design, in the house face, still constructed at
the generation's own resolution so it is not a decal and cannot garble.

### Three things the supplied-image route needed, and each was a measured failure first

1. **`cover` could not take his lettering off.** It tiles one plain patch, which is right
   for a flat wall and wrong for a surface with a gradient: the plainest patch of his crown
   by variance measures #101F2F against a centre of #203451, and the patch I first chose was
   eight rows below his sub-line, which there is **the gold braid and the crest** - so it
   tiled a row of GD crests across the cap. `fit_source.smooth` reconstructs the band by
   diffusion from the crown either side of it instead, so the gradient and the gold piping
   come back through it. **Two overlapping bands with a small feather**, for the same reason
   the Factory's landscape patch needed them: butted bands leave the thing being removed as
   a line at every junction, and his capitals came back twice as a red ghost.
2. **The clip staircased.** Authored in bands of two to four whole cel rows, the cap's
   shoulders - which fall a whole row per column - kept blocks of his sky, and they showed
   on the board as pale rectangles. The clip is now **one rect per column at quarter-cel
   precision**, which `fit_source` rounds to pixels: 5 px at scale 20 against 20.
3. **It had to be measured on the RAW fit.** `smooth` fills rows 0..4 with crown colour, so
   an outline measured after it reads as a straight line and squares off the oval.

### Checks

**0.0% of her art inside the silhouette, 0 px stranded, 0 SLAB**, 250 px (17.0%) of the
region left to her board around the cap's outline - which is the matte working. The guard
passes on the fitted image. `find_name_art` finds her `Mart` script at (272,7) on her own
board at **score 1.00** and **nothing at that position on ours**; the two hits it still
reports board-wide are the town centre's terracotta roofs at (220,129) and (219,109), at
the same scores as before any of this work.

**And the door now lands on a door.** The sprite's footprint - loop 11, x 270..294, y 24..34
- sits squarely on his glazed entrance, where on the old facade it sat on painted shut doors
and on the Factory it sits on a window (section 43). The cels there are still Sierra's.

Two tools were corrected while measuring this: `margin_check` read a building's INIT for its
matte when a `matte_file` existed, which for this building reports 97% where the truth is
83.2%; and `sierra_audit` counted a declared `board_fill` as stranded.

## 43. THE FACTORY'S DOOR ANIMATES TEN ROWS ABOVE ITS DOORWAY, AND THAT IS THE DEFECT

Zain: *"The whole point is that the marble enters into the door with the door functioning
on entry, this looks wrong on the factory."*

**It is not the art style. The animated door and the painted doorway are not the same
opening**, and the numbers say so before any judgement is involved.

| | game coords |
|---|---|
| `DoorFor(Factory)` = loop 5 at (50,181); `AddCastView` draws at `(x - w/2, y + 1 - h)` | the 10x11 cel covers **x 45..54, y 171..181** |
| what is at that footprint in the installed facade | his **mullioned window** (x 47..53, y 175..182) and the bottom three rows of **his sign board** |
| his painted personnel door | **x 47..52, y 183..190**, stone lintel at y 183 |
| the marble's stop, path step 107 | (49,186), its 9x8 cel covering x 45..53, **y 179..186** |

So the door opens **nine rows above the door**, over a window and the word REVOLUTION,
while the marble stands at the real door below it and that door never moves.

### The cause is ours and it is in this file already

**On Sierra's own board the cel's footprint is exactly right**: her factory's wall ends at
y 181, the door cel's bottom row IS her building's base, and the marble arrives on the road
in front of it. Checked on `png12x`, not assumed.

**His building's base is at y 190.** Section 33 grew the region to y 192 and `source_image`
fits his picture with `baseline: 190`, on the reasoning that *a building may overhang
downwards because the marble walks in front of it*. That is true of the building and false
of the door: **the door's anchor is script data and it is pinned to HER base**, so moving
the base down nine rows moved the doorway out from under the animation.

    A BUILDING MAY OVERHANG THE MARBLE'S PATH. ITS DOORWAY MAY NOT.
    `DoorFor` is a constant, so every facade has to put its opening where the cel lands -
    which for the Factory means the building's base belongs at y 181, as hers is.

### What was built, because it is needed under every option

`tools/door_cels.py --who factory` now writes the four cels into both rungs, re-sampled
off the facade that is actually installed rather than the generated one it was first
written for: his leaf measures **#201D18**, his lintel #393025, his brick #27211A, and the
old material was a #4A3322 varnished leaf over a #8C4C28 warm interior - two to three times
lighter than anything in the frame it is drawn over. A new `style: "panel"` draws a flush
panelled personnel door with a lever handle instead of planking and iron straps, because
that is what his picture has beside its roller shutter. **The first attempt kept his leaf
value almost unchanged and the shut cel read as a flat dark slab**; the panel has to differ
from the frame by about twenty levels to survive the box mean to 12x.

`tools/samples/factory_door_cycle_12x.png` is the whole cycle - 0→3 opening, 3→0 closing,
with the marble at its stop - at native 12x, and it is where the mismatch is obvious in
motion rather than in a still.

### Every door on the board, measured

`dark/wall` is the footprint's mean luminance over the wall either side of it: a painted
doorway is darker than its own wall, so a figure near or above 1.00 means **there is no
opening where the door animates.** It cannot tell a doorway from a window, so each was
looked at as well.

| building | loop, footprint | cels in cast0 | dark/wall | what is actually there |
|---|---|---|---|---|
| **Factory** | 5, x 45..54 y 171..181 | **yes, new** | **1.01** | **his window and sign - the defect above**; now (0,+11) |
| **Bare Necessities** | 3, x 39..52 y 92..105 | **yes, new** | **1.68** | a pale shutter panel with a red boss. Flush; no offset. |
| Watt a Bargain | 8, x 270..283 y 170..183 | yes | 1.03 | **a window**, the middle one of three. No offset; see §49 |
| Common Threads | 9, x 273..281 y 103..113 | **yes, new** | 1.07 | the central opening between the mannequins. No offset; `opening_cols` (1,6) |
| Needful Things | 12, x 216..225 y 23..34 | **yes, new** | 0.99 | the recessed centre of the shopfront. No offset |
| General Dealer | 11, x 270..294 y 24..34 | **yes, new** | 0.98 | its painted shut doors, authored to this footprint |
| Open Door University | 7, x 215..224 y 168..179 | **yes, new** | 0.90 | its central portal, four columns wide. `opening_cols` (3,6) |
| Casa El Cheapo | 0, x 139..146 y 28..35 | **yes, new** | 0.66 | an ELEVEN row timber door against a six row leaf; (−1,+5) |
| Hire Ground | 6, x 92..101 y 172..184 | **yes, new** | **0.40** | its timber double door; (0,+1) |
| Rentflix | 1, x 102..108 y 16..26 | **yes, rebuilt** | 0.54 | its glazed shop door, three rows HIGHER than her sprite; (0,−3) |
| Villa Locke & Keyes | 2, x 23..34 y 33..44 | yes | 0.89 | its panelled portal |
| **Bun Believable** | 10, x 282..295 y 67..76 | **yes, new** | — | **his glazed double door, after a (+1,+1) offset - section 47** |
| the Bank | 4 | no - 1990 | 0.81 | not renamed; 1990 door over 1990 art is consistent |

The Monolith row of this table read `10 ... 0.81 / 1.24 ... not renamed` and is now the Bun
Believable row above it: the building was rebuilt from Zain's picture and its door with it.
Its old 1.24 is another count against the ratio — it was reading HER own glazed door, which
is pale, as "no opening". That is the third time this column has been wrong (Bare
Necessities at 1.68, Watt a Bargain at 1.03, Monolith at 1.24) and the second time it was
wrong in the direction that invents work.

**Three buildings have cast0 door cels and ten do not**, so on ten of them a 1990 sprite is
drawn over whatever is behind it the moment the door animates.

## 42. SIERRA'S FACTORY WAS STILL STANDING BEHIND HIS, AND `clear` IS WHAT PUT IT THERE

Zain saw it. The diagnosis matters more than the fix, because it is a class and not an
accident: **the board regeneration REDREW her buildings**, and `clear` restores
`png12x-new`. So every install of the Factory faithfully restored a render-and-slate copy
of her mill behind Zain's, and no check could see it.

### Where it was, measured on the installed board

| | |
|---|---|
| her regenerated mill, visible extent | game x 20..57, y 145..158 |
| the Factory's region | x 9..68, **y 149**..192 |
| his own matte's top edge | y 153 at x 11..18, y 156..159 everywhere else |

**Four rows of it were above the region, and the other ten were inside the region and above
his roofline** — the margin, the one place all three audit clauses agree is supposed to be
HER art. The rule "the margin must be hers" was written for her sky above a roofline and it
cannot tell that from a whole building of hers. That is the shape of every other guard
failure in this file.

### `tools/install_building.landscape_fill` and `Building.board_fill`

`clear_copy` is the obvious tool and it does not work here: the rectangle to paint out runs
from green hill at its left edge to grey yard at its right, and **no rectangle of that size
anywhere on the board carries that transition** — every candidate source holds the thing
being removed, a neighbour, or the frame. So the landscape is **constructed**, the way
`grime.skyfill` constructs a sky rather than borrowing one: the interior is solved by
diffusion from a ring of the board's OWN pixels just outside it, at cel resolution, then
resampled up. Every colour in the result is one the board already has. **No GPU.**

**The region was NOT grown to cover it.** Its width and the six-column gap to Hire Ground
are a measured trade against the marble path and against *"the factory is too close"*.

### Three attempts, and each failure was the ring

| attempt | what it looked like | why |
|---|---|---|
| one box, x 19..60, y 144..160 | a pale wash where the hill had been | the box painted out 8 cel px of bright green left of the roof and 14 right of it, and the ring then averaged that green against the grey yard. **Painting out more landscape than the defect occupies is itself a defect.** |
| three boxes fitted tightly to the measurements | a dark smear in the shape of the building | the roof's right slope runs to x 52 at y 152 where the measurement said 46, so **the ring sat ON the block** and the diffusion interpolated between two dark boundaries |
| three boxes, 2-6 px of margin, rings checked column by column first | the hill, continuous | |

> **A reconstruction is only as good as its ring. Check every side against the board
> BEFORE the run — not the result afterwards.**

And two more faults that the rungs disagreed about:

- **The feather ate the patch.** `clear_copy`'s 1.5 cel px is right for a patch twenty rows
  tall and ruinous for one three rows tall: at 4x it inverted the inner rectangle and PIL
  raised; at 12x it did something worse than raising — it left the core only partly opaque,
  so **the defect showed through the patch** and the result looked like a smear of the
  building rather than a replacement for it.
- **Butted bands leave a line of the original at every junction.** The bands overlap by two
  rows now, so every row of the defect lands inside some band's opaque core.

### What it measures, after

| | before | after |
|---|---|---|
| pixels in rows 144..156 that are neither his building nor landscape | **153 of 550 (27.8%)** | **0 (0.0%)** |
| the patch's left boundary, step against its neighbourhood | | 5.28 vs 6.53 ± 6.21, **z −0.2** |
| the patch's right boundary | | 35.44 vs 17.53 ± 24.76, **z +0.7** |
| the patch's top boundary | | 8.76 vs 8.43 ± 6.89, **z +0.0** |
| `sierra_audit` clause 1 / stranded / SLAB | | **0.0% / 0 px / 0** |

All three joins are indistinguishable from their own neighbourhood, which is the test
section 33 used on `clear_copy`'s edge and the one it failed at 14.27 against 1.1.

**And the declaration is by RECTANGLE, not by building.** The patch is ours, in the margin,
on purpose — so `sierra_audit` and `margin_check` now subtract a declared `board_fill` and
judge the rest of the margin as before. `margin_generated` stays the blunt instrument and
the town centre is still its only customer.

**What it cost, said plainly:** the hill behind his building is now a smooth gradient where
her art had a bright green mass sweeping through it. At board scale it reads as hillside in
shadow; it is flatter than its neighbours.

### The same question, asked of every other installed building

Her building's own footprint is its CEL. Against that, what an install pastes is its matte
at its region. **For all eight cel buildings the region IS the cel, exactly**, and for the
two that declare `background` to let the region grow (Hire Ground, Casa El Cheapo) the
region strictly contains the cel. Measured, then looked at at native 12x:

| building | cel px the install never covers | what they are |
|---|---|---|
| Common Threads | 390 of 1440 (27%) | her lawn, path and trees round the shop |
| Bare Necessities | 221 of 1591 (14%) | her lawn above, her pavement below |
| Watt a Bargain | 300 of 2288 (13%) | her foliage in the top corners, her pavement |
| Rentflix | 70 of 902 (8%) | her bushes and path below |
| Needful Things | 61 of 1092 (6%) | her lawn and sky in the roof corners |
| Villa Locke & Keyes | 78 of 1849 (4%) | her sky and the board's margin |
| General Dealer | 40 of 1470 (3%) | the board's cream margin at the hipped corners |
| Bun Believable | 451 of 1806 (25%) | her grass behind the pole sign, her path at the foot |
| Open Door University | **0** | — |
| Hire Ground, Casa El Cheapo | region ⊇ cel | nothing of hers uncovered |
| the town centre | no cel | it fills a panel she left blank |

**Not one of them has a redrawn copy of its own building left on the board.** The Factory
was the only one, and the reason is structural rather than bad luck: **it is the only
building whose art is painted into the BACKGROUND rather than being a cel**, so it is the
only one where "the region" and "her building" were never the same rectangle.

Two things seen while looking, neither a defect: the orange mass above Open Door University
is the **town centre's own pantile roofs**, ours; and the blurred buildings beside Common
Threads and Bare Necessities were **Monolith Burgers and the Bank**, neither renamed at the
time. Monolith has since become Bun Believable (section 47); the Bank is still hers. Her **cel 14**, 60x39 at (130,150), is the town CLOCK — no name on it, nothing to
rename.

## 41. Z-Mart → General Dealer, and the guard that could not fail here

Cel **5**, 49x30 at (258,2), 100% opaque. TWO cels overlap this hotspot — cel 5 at y 2..31
and cel 6 at y 32..74 — and `DoorFor` settles it: `ZMart => (11, 282, 34)` puts her 25x11
door at game x 270..294, y 24..34, which is cel 5's own shopfront with the threshold three
rows below her cel. Cel 6 is Monolith Burgers, whose door is (288,75) against a cel ending
at y 74. Not in the picPatch (x 68..250; this starts at x 258).

### The sign: one line was never available, and that is arithmetic

Measured by rendering the real `install_employment.text_mask` through `sign_board`'s own
padding, in cel px, against Sierra's 5-row cap:

| arrangement | board | cap |
|---|---|---|
| `GENERAL DEALER` one line | 41 wide | 3.00 |
| one line | 45 wide | 3.30 |
| one line | **49 wide — the whole façade** | **3.55** |
| `GENERAL` / `DEALER` | 37 x 14 | 5.05 |
| **`GENERAL` / `DEALER`** | **39 x 14, leading 0.05** | **5.25 authored, 5.08/5.00 installed** |
| `GENERAL` / `DEALER` | 39 x 13 | 4.80 |

**A single line is WIDTH-bound, so height buys it nothing**: at every board height from 9
to 15 rows it measures the same 3.00–3.55. Even given the entire façade it lands at 3.55 —
under the ~4 at which a reader stops reading letters and starts reading word shapes. Two
lines is not a preference, it is the only arrangement that reaches 5 at all, and 14 rows is
the shortest board that does.

**The sub-line was measured before it was dropped.** `FINE GOODS & PROVISIONS` as a third
line in the same box takes the whole block to cap **2.10–2.20** — the engine sets every line
at one size, so it drags `GENERAL DEALER` down with it — and in a band of its own across the
full façade it reaches **2.40**. Both are half the floor. Zain ruled it out independently;
the numbers agree.

### The guard PASSES here and the pass is worth nothing — proved, not asserted

§20a says `no_text_guard` misses real text on a soft façade. This is the first time that has
been **demonstrated on the building being shipped** rather than inferred:

| probe | result |
|---|---|
| the generation at 20 px/cel px | pass |
| the installed region at 12 px/cel px | pass |
| our own `GENERAL DEALER`, 5 cel px caps, not excluded | **pass** — the pitch test rejects type this large |
| `PADEMOKIK BATEWEIS ILP` planted at 2 cel px on the installed region | **pass** |
| the same row planted on the generation at 20 px/cel px | **pass** |
| the same row on a FLAT grey ground | **FAIL, 20 blobs** — so the tool itself works |

The mechanism, measured: on this façade `ink()` marks **30.5% of the frame**, the planted
row's 22 glyphs survive as **4 blobs**, and the rest are rejected as too small, too tall or
the wrong aspect. A soft render floods the ink mask and the glyphs stop being discrete
components. **Acceptance here is the band-by-band look at native 12x, and nothing else.**

### What the audits actually measure on a building shaped like this

`sierra_audit` reads **0.0% inside the silhouette, 0 px stranded, 0 SLAB**, and two of those
three are structural rather than earned:

- **clause 1 is zero by construction** — `surround` keeps the whole region, so the installer
  never writes one of her pixels as part of the building;
- **clause 3's SLAB cannot fire at all.** The margin here is the hipped corners, 40 cel px in
  a 1–3 px border, and the rim/slab split classifies everything within 3 cel px of the matte
  as rim. Pasting the building over her cream corners on purpose moves `ours` from **180 px
  to 5612** and leaves **SLAB at 0**. On a building whose margin is thinner than the split's
  own radius, `ours` is the number that discriminates and SLAB is inert.

`margin_check`: 6 px outside the region differ from `png12x-new`, all at **x 244..249, y 45**
— inside the town centre's own region, which the tool names as a neighbour's work. Inside,
40 px (2.7%) are hers: top edge 14 of 49, left 3 of 30, right 3 of 30, bottom 0 of 49.

### `find_name_art` exits 1, and the reason is older than this building

The detector was validated where her art IS present and it is exact: **2 hits at score 1.00**
in `assets/png`, at `pic_11.png` (272,7) and `view_811_l0_c0.png` (6,4), with no false
positives in 829 files. After the install **the (272,7) hit is gone**. What remains are two
hits at **(220,129) score 0.70** and **(219,109) score 0.73** — and the same two positions
with the same scores are present on the cast0 board **as it was before this install**. They
are the town centre's terracotta pantiles under a red-script mask, a pre-existing false
positive at exactly the 0.70 threshold. The instrument was not retuned.

### Three more things, recorded rather than fixed

- **Her door cel is still 1990 art.** `view_751_l11_c*` is not in `cast0`, so while the door
  animates a 25x11 block of her blocky art is drawn over the new shopfront — the Factory's
  and Rentflix's fault, third instance. Composited at native 12x to confirm it.
- **The façade is soft**, and six generations did not fix it: 0.80/0.65 returned the init
  with no material in it at all, 0.92/0.30 lost the massing, and the shipped 0.88/0.45 with
  `cond_blur` 0.25 renders slate, stone and joinery but reads defocused below the roof. The
  sharpest thing in the frame is the constructed sign, which is the half that cannot blur.
- **At 7x5 cel px a display window renders as a small lit window whatever is painted behind
  it** — Rentflix's property-card finding, unchanged. Her own door sprite fixes the doorway
  at 25 of the 49 columns, so the windows cannot be any bigger.
