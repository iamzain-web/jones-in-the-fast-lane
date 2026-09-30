# Board buildings: rename and photorealistic rebuild

Thirteen buildings sit on the town board (pic 11, cels 1-13). Zain wants them all done
eventually, **one at a time, asking him for each one** — the name is his to choose and the
cartoon-versus-photograph mismatch is his to judge as the board fills in.

Order is his. This file is the record of what is done, so nobody has to reconstruct it.

| # | Sierra's name | new name | building | state |
|---|---|---|---|---|
| 9 | Hi-Tech University | **Open Door University** | photoreal | **DONE** — verified on the town board |
| 7 | Employment Office | **Hire Ground** | photoreal | **DONE** — verified on the town board |
| 6 | Factory | **The Industrial Revolution** | photoreal | **DONE** — verified on the town board |
| 10 | Socket City | **Watt a Bargain** | photoreal | in progress — name measured (fits two lines), plug to be remade realistic |
| 1 | Low-Cost Housing | — | — | not started |
| 2 | Rent Office | — | — | not started |
| 3 | Security Apartments | — | — | not started |
| 4 | Black's Market | — | — | not started |
| 5 | Bank | — | — | not started |
| 11 | QT Clothing | — | — | not started |
| 12 | Monolith Burgers | — | — | not started |
| 13 | Pawn Shoppe | — | — | not started |

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
