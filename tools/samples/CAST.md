# Generating a photo-real cast — what was measured

> **Status: at the stop-and-show gate.** Five stills exist, one per character, in
> `assets/png12x-cast` with 1:1 comparisons beside them here. The remaining 75 cels have
> **not** been generated. `assets/png`, `assets/png12x` and `assets/png12x-new` are
> untouched.

This is the record for `tools/pose_author.py`, `tools/gen_cast.py` and
`tools/cast_samples.py`. The first attempt at this work (`tools/gen_walkers.py`) was
rejected outright — deformed hands and feet, figures reading as seated, ghost-white matte
fragments, no coherence as a set. Everything below is either a cause of that or a thing
found while fixing it. The findings are ordered by how much they cost to learn.

---

## 0. The rule to reach for first: the init beats the prompt

**Where a defect is about WHAT SOMETHING IS, change the init. The prompt is for things the
init cannot express.**

This is not a heuristic, it is a property of this pipeline, and it has now been confirmed
four separate times on four unrelated defects:

| Defect | Prompt said | Init said | What came out |
|---|---|---|---|
| Bare chest | "a plain white t-shirt" | neutral grey | bare chest |
| Floor-length skirt | (nothing — clause was truncated) | full-width tan slab | floor-length tan skirt |
| Four fashion models | "a heavy set man in his fifties" | 7.7-head, 0.107 shoulder | a slim young man |
| Blue-grey shirt | "a pale pink shirt" | (239,231,240) near-white | pale blue-grey shirt |
| No sunglasses | "dark sunglasses" | nothing at the eye line | no sunglasses |

The last one is the cleanest demonstration, because it was fixed by moving the *same*
instruction from one place to the other: "dark sunglasses" in the prompt did nothing, and a
dark bar painted across the eye line in the init produced them immediately.

**But the prompt is not powerless, and one failure was misdiagnosed as an init problem.**
View 292 came back with an overhanging belly through two rounds of narrowing the init's
columns — `girth` 1.30, then 1.15 — and `girth` was never the cause: it scales the torso
sides, arms and legs uniformly and cannot make a paunch. The paunch was in the words. The
prompt said *"an overweight man … thick waist"*, which is a request for exactly that
silhouette. Changing it to "a thickset barrel chested man" and adding a `chest` parameter so
the upper torso is wide — a column rather than a taper — fixed it in one round.

So the rule is about **what kind of thing** is being specified, not about the init always
winning. Colour, position, extent and body *frame* live in the init. **Semantic category** —
stocky versus fat, patterned versus plain, the era — is something only the prompt can say,
and when the prompt names the wrong category no init will argue it back.

### Extent, category and identity are three different things

The init reliably carries the first two and **not the third**, and all three have now been
confirmed separately:

| what is wrong | example | fix |
|---|---|---|
| **extent** — the mass was too small to survive the blur | the university building's ivy at 1px; view 280's first briefcase block | draw it bigger, and *look at the composited init* |
| **category** — the init cannot name a kind of thing | stocky vs fat; patterned vs plain; sunglasses | say it in the prompt |
| **identity** — the mass survived but became the wrong object | view 280's briefcase came back as a folded **newspaper with garbled text**, ignoring the dark colour painted into it | shape, not colour — and it may not be worth solving |

The third is the newest and the least tractable. The briefcase block *was* in the init, it
*did* survive the blur, and the model still painted paper: a dark rectangle hanging at a
wrist is as much a newspaper as a case, and the colour it was painted was simply overruled.
Extent got it into the picture; nothing available made it the right object.

### The third mode: the init did not actually say it either

There are therefore **three** ways a defect happens, and the third is the one that looks most
like a prompt failure while not being one:

1. the prompt said it and the init contradicted it — **move it to the init** (sunglasses);
2. the prompt named the wrong *category* and no init could fix it — **change the words**
   (the "overweight … thick waist" paunch);
3. the init was *asked* to say it, and what was drawn was **too small to survive the blur** —
   so nothing said it. The prompt then looks ignored when in fact nothing ever instructed
   the model at all.

Mode 3 came from the university-building work on the same pipeline and the same card, and it
is exactly transferable: ivy columns were thinned from 2px to 1px, the init's Gaussian erased
them entirely, and the building came back with no ivy however plainly the prompt asked for
it. Widening them back to 2px brought it back.

A worked example of getting mode 3 wrong and then measuring it properly. View 280's suit
came back with a burgundy V-neck sweater in two cels of four and a tie in the other two, and
putting "sweater, cardigan" in the negative changed nothing. The cause was the init's collar
wedge being far too small, so most of the chest was flat navy and the model filled it from
its own prior. The wedge was picked by eye at half-width 0.030 and 0.075 tall.

**Measured off the 1990 art instead** — brighter-than-torso pixels in the central 40% of the
torso's width, averaged over four cels, as fractions of figure height:

| view | shirt top | shirt bottom | shirt half-width | tie half-width |
|---|---|---|---|---|
| 280 navy suit, white shirt, tie | 0.161 | **0.418** | **0.048** | 0.019 |
| 290 charcoal suit, pale shirt | 0.160 | 0.255 | 0.044 | 0.008 |
| 284 mauve blouse *(control)* | 0.160 | 0.465 | **0.010** | 0.036 |

The real triangle is **3.4× taller and 1.6× wider** than the one that was guessed. The two
controls are what make the number trustworthy: view 284 has no white shirt and correctly
measures a 0.010 half-width, and view 282's white t-shirt yields no measurement at all,
because there the whole torso *is* the bright thing. A method that returns "nothing here"
on the two cases that have nothing is measuring the right thing.

The diagnostic is cheap and should be the first thing tried when an init instruction appears
to be ignored: **render the init and look at it.** Every init in this file is written out and
inspected before any GPU time is spent, for precisely this reason — a mass that is not
visible in the composited init at the blur radius in use is not an instruction, whatever the
code that drew it says.

That work also independently confirmed the general shape of §0 from the other direction: its
init was Sierra's cel upscaled 16x, which is an instruction to paint flat 1990 art at any
strength that still preserves layout. Lowering the ControlNet conditioning scale from 0.85 to
0.45 grew real windows and a real door but the result was still a painting; **replacing the
init with a synthetic block painting produced a photographic building in one round.** Same
conclusion as the cast: stop denoising *from* the original art, and let the init carry only
colour, position and extent.

In every case the init won and the prompt lost, including when the prompt was explicit and
correct. The init is denoised from directly; the prompt is a conditioning signal competing
with ControlNet and with the init's own colour. So the working order is:

1. Can the init say it? Colour, position, extent, body frame — say it there.
2. Only if it cannot — "patterned", "1990", "photorealistic", the *absence* of something —
   use the prompt.
3. Check the prompt actually fits in 77 tokens (§1), or it did not say it either.

---

## 1. CLIP truncates the NEGATIVE prompt too, and nothing was checking it

**The negative prompt had grown to 142 tokens against a limit of 77.** A little over half of
it was discarded before reaching the model, on every frame generated.

`gen_walkers.py` documents this trap for the *positive* prompt and `gen_cast.py` checked the
positive prompt at runtime. Neither ever checked the negative. Decoding the tokens past the
window shows exactly what never applied:

```
vignette, faded, watch, wristwatch, bracelet, jewellery, floor length skirt, maxi skirt,
gown, evening dress, train, draped fabric, jacket off shoulder, open jacket, glitter,
sparkles, water droplets, dispersion, particle effect, film grain, light stand, tripod,
studio equipment, umbrella
```

Every term added in response to a defect was in the discarded half. The wristwatch on view
282 survived because nothing ever asked for it to go; so did the studio light stand. This
also settles three arguments about what actually fixed things:

| Defect | Prompt clause added | What actually fixed it |
|---|---|---|
| Glitter over the whole figure | `glitter, sparkles, …` (dropped) | low-passing the init's noise by 8px |
| Floor-length skirt on view 296 | `floor length skirt, gown, …` (dropped) | painting the leg colour as two narrow columns |
| Studio light stand in frame | `light stand, tripod, …` (dropped) | nothing yet — it was never prohibited |

**The lesson, confirmed three independent ways: on this pipeline the init is the strong
lever and the prompt is the weak one** (§0). A prompt clause is worth nothing if it falls
off the end of the window. Both prompts are now checked at runtime in `generate()`.

### Staying under 77 is not the same as not losing anything

The documented failure is a term falling off the end of the window unnoticed. There is an
opposite one, and it reports green the whole time: **making room by deleting a term that was
already doing work.**

Cutting the negative from 142 tokens to 59 removed **twelve terms that had been reaching the
model**, and one of them was `shadow` — which an earlier round had deliberately kept, with a
written reason at the site: *a cast shadow touching the shoes survives connectivity and
becomes a dark blob attached to the sprite*. It was dropped anyway while trimming for length,
and every guard read 59/77 and passed.

The sister project hit the same thing from the other direction: they made room by deleting
`flat` from `cartoon, flat, distorted`, the count printed 76/77, and the building came back a
flat vector illustration.

**THE RULE: take the space from a synonym, never from a term that predates the problem you
are currently solving.**

A synonym costs nothing when it goes — `bodysuit` against `leotard`, `co-ord set` against
`matching set`, `dark glasses` against `sunglasses` are all the same instruction said twice,
and the model cannot tell the difference. A term that was added to fix an *earlier* defect is
load-bearing by definition: something went wrong once, that word was the answer, and nothing
about the current problem makes the old one stop existing. `shadow` cost a visible blemish on
the figure's hip in the frames that were in front of the user at the time.

And before trimming, **diff the old prompt against the new one and read what left.** That diff
is two lines of code and it is the only thing that sees this class of loss, because every
count-based guard reports green throughout.

### Why it went unnoticed for so long

`gen_walkers.py` documents this trap at length — for the **positive** prompt only. The
runtime guard in `gen_cast.py` was then written to match that documentation rather than to
match the risk, and the negative prompt is the same input through the same tokenizer with
the same limit.

**A check that guards one of two identical inputs is worse than no check at all**, because
it creates the belief that the class of bug is handled. Every time a term was added to the
negative during this session, the existing guard was there, visibly passing, on the other
prompt. The right question when writing a validator is not "does this cover the case I just
hit" but "what else is exactly this shape".

## 2. Build is not in the silhouette either, and neither is pose

The rejected cast's poses were read from a 39×95 alpha mask. Foot spread at 96% of figure
height across the four cels of view 282 measures **15, 16, 17, 17 pixels**; view 286 is 11,
12, 12, 12. That is one or two pixels of signal, the same order as the ~25 levels of
per-pixel digitiser noise `restore_walkers.py` established. Figures came out seated because
the skeleton was built from coin tosses.

When the first cast came back as four fashion models, the obvious next move was to measure
*build* from the silhouette the same way the bounding box is measured. **It is not there.**
Median filled width per row, averaged over all four cels:

| view | who | med width / span |
|---|---|---|
| 282 | the young man | 0.318 |
| 292 | the heavy-set man | **0.291** |
| 287 | (newspaper prop) | 0.346 |
| 293 | (barrel prop) | 0.361 |
| 297 | (towel prop) | 0.249 |

The heavier figure measures **narrower** than the lighter one, and most of the spread across
all seventeen views is props rather than people. Build is therefore an **authored** parameter
per character (`pose_author.BUILDS`), stated openly as a design decision rather than dressed
up as a measurement.

What the silhouette *does* carry reliably, and all that is taken from it: cel width, cel
height, the figure's first and last row, and the horizontal centre. Those are averages over
many pixels and they are the functional geometry the game positions against.

## 3. The skeleton outvotes the prompt

The canonical figure was 7.7 heads tall with a 0.107 shoulder and a 0.052 hip — an athletic
young adult. ControlNet receives that as a precise body frame. Asking the prompt for "a
heavy set man in his fifties" on top of it is asking the model to overrule the one input it
trusts most, and it loses every time. **The figures were slim because they were told to be
slim, in the only language the pose control speaks.**

`pose_author.proportions()` now adjusts the whole landmark table for how many heads tall a
character is — the head grows and everything below the chin compresses, with the ground line
fixed — because a 6.8-head figure is not a 7.6-head figure scaled down.

## 4. A colour-distance matte cannot key a figure against a same-hue backdrop

Measured on the first figure that otherwise looked right, against the slate blue-grey
backdrop that reads best:

| sample | RGB | distance to nearest background cluster |
|---|---|---|
| shirt | (233,231,237) | 137.8 |
| face | (243,184,164) | 147.8 |
| **jeans, thigh** | (20,38,64) | **27.6** |
| **jeans, shin** | (49,66,96) | **13.0** |
| background's own internal spread | — | 11.4 |

Keying threshold was 42, so the top half cut perfectly and the legs vanished. **No tolerance
separates 13.0 from 11.4** — they overlap. Six of the twenty views are navy or charcoal below
the waist, so this would have cost a third of the cast its legs.

The backdrop is now green and the key is a hue test (`g - max(r, b)`) with de-spill. Green is
the one hue absent from the entire measured palette of all twenty views — whites, creams,
tans, browns, navies, charcoals, teal, hot pink, dusty rose, slate and skin — so it cannot
collide with a garment, and unlike a threshold it cannot be eroded by a colour not yet seen.

`gen_walkers.py` tried green once and recorded three failures: green creeping onto the
figure, the island printing as a board behind it, and the prompt being ignored. All three
came from the island being **flat green**. This island is tinted with each slot's own
measured garment colours, so there is no green inside the figure to creep out of.

## 5. Colour bands must follow the limbs, not span the canvas

The init is a coloured mannequin painted on the authored skeleton, tinted with colours
measured from the slot (`measure_bands`). It is what puts the right clothes on the right
character — the prompt alone produced a bare-chested man when it asked for a white t-shirt.

### Take the extreme of the distribution, not its centre

**At a 39-pixel source width, averaging over a small bright feature gives you the blend, not
the feature.** This is the single most transferable sampling rule in this work and it caught
me three times before it was named.

| feature | method used | what it returned | what it is |
|---|---|---|---|
| view 292's pale pink shirt | dominant bucket | (239,231,240) near-white | pale pink |
| view 280's white shirt under a suit | mean of "brighter than torso" | (128,154,181) mid blue-grey | white |
| view 282's red chest logo | top four buckets **by count** | white, pale blue, pale pink, pale salmon — **no red at all** | a red graphic |

All three are the same fault. A feature two or three pixels across, sitting between a navy
jacket and a dark tie, is mostly *blend* pixels; average them and you land halfway to the
surroundings. A small graphic is never in the top four buckets by count, so the four you get
are its anti-aliased edges rather than the thing itself.

**The rule: the dominant bucket is right for a garment that FILLS its band and wrong for
anything that does not.** The size of the feature relative to its sampling band is what
decides which statistic to use:

* fills the band → dominant bucket, or the mean
* small and bright → the **brightest decile**
* small and coloured → the **most saturated bucket**, included alongside the common ones
* and always with a neutral guard, so a genuinely white or grey thing is not given an
  invented hue (see §5's three conditions)

This also explains why the 292 fix did not generalise on its own: that shirt *filled* its
band, so it presented as a colour problem rather than a sampling-window problem, and the
lesson stayed local to the band colours instead of reaching the collar and the palette.

**The boundary on the rule: the extreme is only the feature if the feature is what makes it
extreme.** View 280's suit came back near-black and the obvious move was to take the torso
band's brightest decile — which returns **(148,177,209)**, a pale blue. That is not the
suit, it is the *shirt*: the torso band holds shirt, tie and jacket together, and its
brightest tenth is the shirt every time. Painting the whole torso pale blue would have been
a far worse error than the one being fixed.

Split the band the way the collar splits it, but inverted — central 40% is shirt and tie,
outboard is jacket:

| view | central | **jacket only** | what the init painted |
|---|---|---|---|
| 280 | (66,81,101) | **(78,77,100)** | (77,78,100) |
| 290 | (52,55,64) | **(41,47,71)** | (41,45,72) |

**The init was already painting the measured jacket colour to within a level.** The fault
was in the wardrobe line: it said *"a dark navy two piece suit"* where the jacket measures a
mid grey-blue, and the model obliged. Corrected to "mid grey blue"; view 290 corrected the
other way, to "very dark charcoal navy", because its jacket measures (41,47,71) and
"charcoal grey" had produced mid-grey. Two views drifting in opposite directions from one
cause.

**So before reaching for a different statistic, check whether the sampling window contains
more than one garment.** If it does, split the window first — the statistic was not the
problem.

### A slab that should be two things needs a break in it

The mirror of the problem below, and it appeared as soon as the columns were narrowed.
View 286's top and shorts are both pink — torso (240,80,160), thigh (240,167,171) — so the
init painted one unbroken pink column from shoulder to thigh, the model read a single
garment, and it drew the likeliest one: **a pink swimsuit**. Two garments had become one
shape.

The fix is a thin darker waistband in the init at the hip line, skipped for the genuinely
one-piece slots (`ONE_PIECE` — dress, towel, barrel, underwear, trunks) because drawing a
seam on a full-length dress would invent a boundary the slot does not have. Said in the
prompt as well, because "a t-shirt and shorts" versus "a swimsuit" is a *category* and only
the prompt can settle a category (§0).

### Adjectives bleed across a conjunction, and the pattern flag is not to blame

View 296 came back in a matching floral top **and** floral shorts, against a floral top over
plain yellow shorts in the original. The obvious suspect was the automatic "patterned" word
being applied to the whole outfit rather than per band. **It was not**: the word is appended
only from `torso_patterned`, and for 296 it is suppressed entirely because the wardrobe line
already contains "print". Measured — nothing was appended.

The cause is CLIP, which does not reliably scope an adjective to its noun across a
conjunction: "floral print blouse and yellow shorts" leaks the print onto the shorts.

**The general rule: when two nouns share a clause, the second one needs its own adjective to
defend itself.** "plain yellow shorts", not "yellow shorts" trusting the parser. This applies
to every wardrobe line in `CAST` — a slot whose two garments differ in a property one of them
is described by must say so on both, or the first description wins both nouns.

Worth noting that a per-band pattern word would have made this *worse*, not better: 296's
thigh band measures `patterned = True` as well, because a yellow short over bare skin has a
wide saturation spread. Applying the word per band would have asked for patterned shorts
explicitly, which is what we were trying to stop.

### Why the columns are narrow

Painting the bands as **full-width stripes** failed on the women, and the reason is obvious
once stated: view 296's leg band measures (240,201,142) tan, and a full-width tan slab from
hip to floor *is* a floor-length tan skirt. That is what came back — beautiful, unanimatable,
no legs to move and nothing for the foot band to describe. Painted as two narrow columns down
the femur and shin with backdrop between them, the same colour can only be read one way.

Two supporting details:

* **Thigh and shin are sampled separately.** One band from 0.55 to 0.88 returns the dominant
  colour of both, which is the garment, so it cannot tell a tan skirt from tan shorts over
  bare legs. Split at the knee, view 296 reads tan over skin — the shorts it actually is.
* **The mask is eroded by one pixel before sampling.** The 1x decode leaves a cyan and cream
  matte fringe at full alpha — (152,232,208) and (224,200,152) turn up as "dominant" in half
  the views otherwise, and a fringe colour fed back as a clothing hint would dress the whole
  cast in mint green.

## 6. Aspect ratio, not memory, was eating the legs

SD1.5 is trained at 512×512 and degrades with distance from the subject centre at extreme
aspects. At 1:2 the head came back flawless and the figure dissolved below the thigh, four
runs running, through every prompt and margin change tried. The canvas is **704×1152**
(1:1.64, near the 1:1.5 of the 512×768 the model is routinely used at) and the figure holds
together end to end.

Two related fixes from the same symptom:

* `floor` was removed from the negative prompt. Asked for a full-length figure and
  simultaneously forbidden anything to stand on, SD1.5 dissolves the legs.
* The ankle block in the init extends to 0.090 of figure height *below* the joint. The
  authored ankle is the joint; a shoe is several centimetres below it, and if the island
  stops at the joint the only thing under it is backdrop — which is what gets painted.

## 6a. Why absolute canvas pixels are safe here, and when they stop being

`init_field`'s blur (62), grain (8) and soft (18) are **absolute canvas pixels**, not fractions
of anything. That is only safe because **every one of the twenty views has an identical
`fig_h` of 1025.3** — 89% of a fixed 704x1152 canvas — so the blur is 6.05% of the figure
everywhere, including view 293's 61-wide barrel cel, because figure *height* binds the scale
in every case.

**It stops being safe the moment any view gets its own canvas.** Widening the canvas for 293
was under consideration earlier and would have made this a bug on the same day: the same 62
would be a smaller fraction of a wider frame, and an init blurred differently relative to its
frame is a different instruction. The sister project hit precisely this by "tidying" a blur
from a canvas fraction into cel units — the model sees a canvas, not cels, so the tidier
formula blurred 2.09% of one frame where it had blurred 1.67% of another, and the result read
as out of focus.

If a per-view canvas is ever introduced, these three constants must become fractions of the
canvas in the same commit.

## 7. What a 4GB RTX A500 actually does

With `--offload`:

| canvas | attention slicing | result |
|---|---|---|
| 512×1024 | off | 2.83 GB peak, ~145 s for 28 steps |
| 576×1152 | on | 3.17 GB peak, ~167 s |
| 704×1152 | on | works; ~130–190 s for a base |
| 640×1280 | on | 3.46 GB peak, ~273 s |
| 640×1280 | off | **driver crash, 0xC000070A** |
| 768×1536 | either | **driver crash** |

**768×1536 is not available on this hardware.** The ceiling is around 810k pixels. Hands and
feet get their resolution back instead by re-generating the head, each hand and the feet as
**512×512 crops** — SD1.5's native resolution — at about 25–40 s each. Slicing is disabled
for those crops and re-enabled afterwards, which is most of the difference between seventeen
minutes a frame and about four and a half.

A full 80-cel run is therefore roughly **six hours**.

## 7a. The drift checker, and the two things it cannot see

`gen_cast.py --drift <dir>` measures the five band colours on each generated cel's own alpha
and compares them to the **per-channel median of the loop**. It exists because `boil` is
blind to a single garment detail changing (§7b), and it caught view 282's trainers going
white-to-red on its first live use.

**The reference is the loop median and not cel 0, and that was a real bug.** Comparing to
cel 0 assumes cel 0 is correct, and on a loop of independent `--no-img2img` samples there is
no reason it should be. View 280's test loop had a dark polo in cel 0 and the correct
white-shirt-and-tie in cels 1 and 3, and the cel-0 reference flagged the two *correct*
frames. Since the output is a list of cels to regenerate, that version of the tool would
have replaced the good frames and kept the bad one — making the cast worse while reporting
progress.

**Two blind spots, both of which must be stated wherever the table is read:**

1. **It cannot see a garment change that preserves the dominant colour.** The torso band
   takes the *dominant* colour, and on a navy suit that stays navy whether the neckline
   holds a shirt, a tie, a polo or a burgundy sweater — the sweater is too small an area to
   move it. View 280 scores **zero flags** on a loop where cel 0 wears a polo and cel 2
   wears a sweater with rolled sleeves. So it is a good instrument for the shoe class of
   fault and no instrument at all for the garment class. **A clean table is not a clean
   cast.** The two tie views, 280 and 290, need an eye.
2. **The head bands are advisory.** "hair" and "face" are a fixed 0.00–0.15 slice of figure
   height, so on a figure whose extent shifts a few percent the slice lands on a shoulder in
   one cel and on hair in the next. View 282 reported `face d=209` across two frames whose
   faces are identical while the `feet` flag on that same view was real. They are printed as
   `note` and excluded from the count.

## 7aa. We were not measuring the wrong quantity. We were measuring the wrong MOTION.

Five rounds of the walk were rejected, each time after tuning an amplitude. Bob, sway, arm
swing, shoulder-to-hip twist, foot lift, handle spacing, grid step, warp method: every one
was measured off the 1990 art, argued about, adjusted, and re-shown. Every one of them was a
number about something that was not the fault.

The fault was that **the swinging leg's knee never bent**. Projected shin length across the
four cels of the cycle, as a fraction of figure height:

```
0.215   0.207   0.215   0.215        foreshortening 3.7%
```

A knee at about 60 degrees foreshortens the shin roughly **50%** in a front view. The leg was
a rigid straight limb pivoting at the hip, which is the single most recognisable "not
walking" signal there is - it reads as a mannequin sliding. No amplitude anywhere else can
rescue it, and no amount of correct bob will make a straight swinging leg look like a step.

Section 7b below is the same failure one level down: a metric that measured the mechanism
being fixed. This one is a level up. **7b is "the number was about the wrong quantity"; this
is "every number we had was about the wrong motion."** When a thing has been rejected
repeatedly and each fix is a different value of the same kind of parameter, stop adjusting
and ask what class of motion is absent altogether.

Two structural faults came out with it, both invisible to every metric in use:

- **The cycle had three distinct leg poses of four, and effectively two.** Cels 0 and 2 were
  bit-identical in the legs and cel 3 mirrored cel 1. It was a two-state oscillation wearing
  four frames.
- **The cels were sampled at the two phases with no knee bend in them.** The classical four
  keys are CONTACT, DOWN, PASSING, UP (Richard Williams, *The Animator's Survival Kit*, Faber
  & Faber 2001, the walk chapter; the same four are in Preston Blair). Peak knee flexion is
  in EARLY SWING, between contact and passing - so authoring contact and passing sampled the
  gait at exactly the two moments the knee is straightest.

Four cels covering a full cycle is two samples per step, so two of the four keys, not four.
The pairing now authored is **DOWN and UP** - the body's lowest and highest, the swing knee
at its most folded against its most extended. Contact-and-passing is the widest stride, and
it is also the pairing that cannot show a bent knee.

### In a front view, knee bend IS foreshortening

There is no angle to draw. Thigh and shin both still project as near-vertical segments and
the only thing that changes is that they get shorter. That has a consequence for the warp
which had to land in the same change, not after it: `warp_walk.skin_field` applied a
**similarity** - one uniform scale per bone - so a target shin 0.6 as long sampled the source
1/0.6 as far in both directions and came back 0.6 as long AND 0.6 as WIDE. A withered leg,
not a bent one, and visibly worse than no flexion at all. Had the two shipped separately the
honest conclusion from the first would have been "knee flexion does not work".

Measured, same cycle, same source photograph, only the transform changed - width of the
SWING leg's shin across the four cels:

```
anisotropic (scale along the bone, 1.0 across)    38  49  45  41   ratio 0.776
uniform similarity (the old transform)            39  59  30   0   ratio 0.000
```

The `0` is not a rounding artefact: in that cel the leg is no longer present at the column
its own foot is in. Note also that measuring "the widest run in the row" gives **0.935 for
both** and proves nothing - the widest run is the SUPPORTING leg, which is not foreshortened
at all. That is 7b again, caught this time before it was believed.

### What the 1990 art cannot supply, and why this is closed

Before authoring anything, the obvious alternative was tried: recover the target skeletons
from the original cels directly. It is not available. Counting separate ink runs row by row
from hip to sole:

```
view 282   one run at every height, in all four cels
view 292   one run, except two at 0.56 - which is the ARMS
view 286   one run, except one split at 0.80 in cel 2
```

**The two legs never separate.** Per-leg ankle and knee positions are not recoverable from a
39x95 alpha at any tolerance, so a target skeleton cannot be derived from the source. This is
closed, not deferred. (An earlier note in this project quoted 96/196/73/161 as view 280's
"ankle separation"; those are the TOTAL silhouette extent at ankle height, not two identified
feet, and 280's briefcase reads as a second leg besides.)

### A rigid object needs a rigid bone

Foreshortening the shin turned every raised shoe into a black horn. The cause is that
COCO-18 has no toe joint, so the lowest bone is the shin and the shoe hangs below its far
endpoint, where distance-to-segment clamps and hands the whole shoe to the shin's transform -
which is being compressed 1.6:1 along exactly the axis the shoe lies on. It had been blamed
once on the foot lift being too large, and the lift was reduced from 0.048 to 0.030 as a
result. The lift was never the cause.

Two fixes that did **not** work, both measured rather than guessed:

```
longer foot bone   0.045 -> 0.075 -> 0.110 of figure height   seam RMS 2.20 -> 4.31 -> 4.31
heavier foot bone  weight 1, 3, 8, 20                          horn identical in all four
capping the shin's influence past its own endpoint             horn identical
```

What worked was giving the foot a short rigid bone of its own, ankle to sole, with equal
source and target length so its transform is a pure translation - the shoe is carried, not
deformed. The horn is much reduced but not gone. **The same class of bug is still open for
the briefcase in view 280**, which is a rigid object driven by the arm bones and visibly
changes shape between frames. Anything in the photograph that cannot deform needs a bone
that cannot deform.
## 7ab. Separating a shoe from a trouser leg: what is closed, and what worked

Once the cycle foreshortened the shin, every raised shoe came out with a spike on it. Six
attempts failed before one worked, and the failures are worth more than the fix because
two of them close doors permanently.

### Closed, and not to be retried

**Geometry cannot do it.** A disc, a box, three different top edges and an asymmetric window
all failed. Dumping the mask as an image explained why in one look: **the ankle keypoint sits
in the MIDDLE of the shoe's height, not at its top.** A region starting at the ankle misses
most of the shoe; one starting above it takes trouser and cuts a notch out of the jean. No
offset does both. It is also the entire explanation for a result that had looked like
evidence against the whole approach - radius 0.105, 0.16 and 0.24 giving a pixel-for-pixel
identical spike. Growing a region downward and sideways into background never reaches up
over the shoe, so all three contained exactly the same nothing.

**Connectivity cannot do it either.** The obvious next idea is that a shoe is a distinct
island of ink, so take the component the ankle sits in. It is not. The source figure is
**ONE connected component of 177,993 pixels spanning rows 56 to 1118** - the shoe is
continuous with the trouser, the leg and the torso. No component analysis will ever separate
them. This is closed in the same way the 1990 art's legs-never-separate result is closed.

### What worked: colour, per foot, with each foot's own translation

A shoe is a distinct TONE from the trouser above it on every character in the cast - white
trainers on 282, black brogues on 280. `shoe_mask` samples the sole colour beside each ankle
and keeps what matches. Dumped and checked against the art: it covers both shoes, stops
exactly at the jean hem, and excludes the trouser.

That alone was not enough, and the reason is the sharpest measurement of the whole exercise.
A combined "does this pixel land on A shoe" test left a detached shoe-coloured fragment
beside the raised foot. The coordinate dump named the mechanism outright - the fragment's
own pixels, and where each sampled from:

```
cel 0   fragment at cols 214-269   samples cols 261-317   offset (+47,+1)
        planted foot's translation (+49, 0)      <- this one
        raised  foot's translation (-33,+87)
cel 2   fragment at cols 447-498   samples cols 399-449   offset (-48,+1)
        planted foot's translation (-49, 0)      <- again
```

With one ankle lifted 87 px, **ground-level pixels beneath it are NEARER the planted ankle**,
so nearest-ankle assignment handed them the planted foot's transform; that landed on the
source's *other* shoe and drew a shoe into the space the raised foot had vacated. Asking
"does foot F's own translation land on foot F's own shoe" catches it exactly: those pixels
fail both feet's tests and correctly stay background. Fragment went from 1797 px to 7.

### The metric fooled us a third time

With the shoe finally correct, the seam numbers on the raised-foot cels JUMPED to 4.58 and
6.20 against a tolerance of 3.0, while the picture was plainly better than anything before
it. The seam metric reads the silhouette between 0.62 and 0.93 of figure height, and a
raised shoe sits at 0.83-0.93 - so it was measuring a shoe's profile as though it were a
trouser seam. Narrowing the band to 0.62-0.80 to exclude the shoe:

```
              0.62-0.93 (default)    0.62-0.80 (shoe excluded)
   cel 0            4.58                     0.58
   cel 1            1.56                     0.52
   cel 2            6.20                     0.60
   cel 3            1.48                     0.55
```

0.52 to 0.60 px across all four, and the trousers are straighter than at any point in this
work. **Three times now a number has argued against a change that was right** - `step` in
7b, "widest run in the row" in 7aa, and the seam band here. The pattern is always the same:
the metric was defined for one situation and is being read in another, and the tell is
always that it disagrees with the picture. Look at the picture.
## 7ac. Measuring a real walk off Muybridge, and why five clever passes lost to one grid

The cycle's numbers came from animation canon (Williams) because the 1990 art cannot supply
them - its legs never separate. Canon is a book ABOUT strides; a photograph is a stride. So
the cycle was checked against **Eadweard Muybridge, *Animal Locomotion*, Plate 5 (1887)**,
Boston Public Library scan via Wikimedia Commons, Creative Commons Public Domain Mark 1.0.

Plate 5 is the right plate because it carries **one stride in two views**: eight lateral
frames and eight frames of the man walking straight toward the camera with his arms hanging.
Plate 7 was rejected (bent arms, fast tempo, and its third row is a REAR view); Plate 6 was
rejected (correct gait, lateral only). The scans stay out of the repo - what transfers is
measurements of how a human walks, which are nobody's property, not the plates themselves.

### The one relationship that makes the lateral row usable

**In a lateral view, the shin's VERTICAL EXTENT is its FRONT-VIEW projected length.** The
front projection discards the fore-aft component and a shin has almost no lateral one, so
what the side camera shows as knee-to-ankle height is exactly the number the CYCLE table
needs. That is why having both views of the same stride matters: a knee angle is directly
visible from the side and only inferable from foreshortening from the front.

### What was wrong, and what was already right

```
                        MEASURED (Plate 5)     AUTHORED      verdict
  foot separation       0.045 - 0.064          0.064 - 0.084  TOO WIDE by ~0.02
    range of that       0.019                  0.020          matches
  shin projection       0.615  (+-0.12)        0.632          confirmed
  bob                   0.0485 (+-0.014)       0.0300         see below
```

**The stance error was a constant OFFSET, not a wrong amplitude, and that distinction is the
entire correction.** The range the feet travel through matches the photographs almost
exactly; only the baseline was wrong. So both extremes slide inward by 0.010 each and the
amplitude is untouched. Compressing the range is the intuitive fix and would have made it
worse.

A constant offset is a **wide stance**, and a permanently wide stance is a gait abnormality
in its own right - it reads as a swagger or a brace rather than a walk, and it is wrong in
every frame rather than only at the extremes. That matches "nobody's body moves like that
when they are walking" better than over-wide swinging does.

**The knee is confirmed**, which is worth as much as a correction: 0.615 measured against
0.632 authored, well inside the reading error. The canon was right about the one motion that
actually carries a front-view walk.

**The bob was expected to be half ours and is not.** An earlier unconfirmed candidate said
0.015 and the 1990 art measures 0.011-0.021, so two sources seemed to agree that 0.030 was
double life. The grid read says **0.0485 +- 0.014**, and the physiological figure for an
adult's vertical displacement in walking is 4-5 cm on 1.75 m, i.e. 0.025-0.030. Ours sits on
the physiological value and inside the plate's error bar. **The 0.015 candidate is withdrawn
and the bob is left alone.** The 1990 art's 0.011-0.021 is a 1-2 pixel measurement on a
95-pixel sprite and should never have been treated as a second independent source.

### FIVE automated segmentation passes failed. Record this before someone repeats it.

```
brightness threshold            defeated by the grid backdrop and the pale ground
percentile threshold            clipped the crown on 4 of 8 frames; gave a bob of 0.20
median-background differencing  defeated by per-frame exposure variation
  + per-frame normalisation
  + largest connected component coverage fell to 0.1-2.3%: it found fragments, not a walker
dark-run detection, ground strip returned foot separations of 0.31-0.55 of figure height
```

One consistent reason: **an 1887 collotype of a cluttered outdoor set has no clean
figure-ground separation anywhere in it.** Planks, a fence, shadows and flowers behave
exactly like limbs under every test that can be defined on brightness or on difference.

What worked was rendering the frames at 3x with a **labelled grid every 10 source pixels and
reading the positions by eye**, then stating the reading uncertainty (+-8 px, so +-0.013 of
figure height). For a one-off reference measurement a reliable manual read beats five clever
automatic ones, and refusing to claim more precision than the grid gives is part of the
method rather than a hedge on it.
## 7b. A metric derived from the mechanism you are fixing will confirm the mechanism

The cast passed its animation check and then a player said **"it doesn't look like walking at
all, it looks like he is just wriggling his legs."**

`step` — mean absolute pixel change between adjacent cels — read **17.92 against the
original's 20.38**, which is 88% of the real animation's amplitude and looks like a pass. It
was tautological. `step` is a whole-frame difference **dominated by the legs**, the legs were
the only thing the authored cycle moved, and so the metric could only ever have confirmed
that the legs moved. It had no way to notice that the head, shoulders, arms and torso were
*identical in all four cels*.

Measured off the 1990 art, across six views, as fractions of figure height:

| motion | 1990 art | the cycle that passed |
|---|---|---|
| vertical bob (figure height varies) | 0.011 – 0.021 | **0** |
| lateral sway (shoulder centre) | ~0.037 | **0** |
| arm swing (extent at the wrist) | 0.032 – 0.086 | 0.016, on two cels of four |
| shoulder-to-hip counter-twist | ±0.015 | **0** |

**A walk is not a leg motion with a static body on top.** Four front-facing frames *can*
carry a convincing walk — the original is four frames — but the original is a photograph of
someone actually walking, so every one of those motions is present *and correlated*, all
peaking together because they came from one real stride. A synthetic skeleton that moves only
the legs will read as wriggling at any frame count.

This is the same shape as the drift checker's blind spot in §7a: an instrument that cannot
see the class of fault it is being trusted for. Before relying on a metric, ask what it would
read if the thing you have **not** implemented were missing entirely — if the answer is "the
same", it is not a check.

**The pinned crown is the specific error, and the reasoning that produced it was sound in the
wrong place.** `pose_author` held the crown fixed so every cel had an identical span, because
a constant span keeps one scale and one offset valid for the whole loop. True — and it is the
*fit's* job to absorb a varying span, which `fit_walkers.refit_for` already does by mapping a
loop from the union of its four extents. The pin bought tidiness where it was not needed and
cost the animation its bob.

## 7c. The hand-written wardrobe line is the likeliest thing to be wrong

Of every input to this pipeline, one is **typed** and all the others are **derived from the
cel** — the bands, the collar, the shoe, the pose box, the build. So the typed one is where
wholesale wrongness lives, and it cost three separate regenerations before the pattern was
visible:

| slot | the line said | the cel measures |
|---|---|---|
| 286 | "denim shorts" | (240,167,171) — pink |
| 296 | "a cream blouse and a tan skirt" | a floral top over yellow shorts |
| 280 | "a dark navy two piece suit" | (78,77,100) — mid grey-blue |

`gen_cast.py --check-wardrobe` compares every colour word in every wardrobe line against the
bands measured off its own cel, and prints the nearest channel and the distance. It costs no
GPU, runs in seconds, and would have caught all three before a frame was generated. It found
three more on its first run: 280's shirt is pale blue rather than white, and 285's and 286's
shoes are not white either.

Its own limit is worth knowing: a colour word is not attached to a garment, so **any** channel
can answer for **any** word. Including the torso palette as a candidate *masked* two real
findings — "white shoes" matched a torso swatch and stopped flagging — so the palette is
excluded, and the cost is that a logo colour, which lives only in the palette, always flags.
View 282's "red" is expected.

## 7d. Four independent photoreal samples cannot animate

A player's verdict on the generated four-cel loop: **"the legs are all over the place like he
is dancing."** Read as *clothing* rather than as poses, view 280's four cels were: jacket
buttoned with both arms down; jacket open, waistcoat showing, sleeves rolled, one hand
pocketed; jacket open, **both** hands pocketed, a different tie; jacket buttoned with a pale
object under the arm. Four photographs of four slightly different men who share a face.

**And "dancing" was not about ORDER.** Measured, the generated set's stride pattern is
correct — wide, narrow, wide, narrow, 195/66/214/52. What is wrong is **repeatability**: cels
0 and 2 are authored *identically* and came out 195 and 214, and every garment differs. The
right stride performed by four different men. Photorealism is what makes it fatal — at 468
pixels wide every re-invented fold and button is glaring, where 39 dithered pixels hid it.

**The fix is to stop generating four frames.** Generate one, and produce the others by
deforming *its pixels* along the skeleton delta (`tools/warp_walk.py`). Consistency stops
being hoped for and becomes structural: one photograph means one jacket. The stride becomes
exactly what the skeleton says, because the generator gets no vote. Cels 0 and 2 now measure
**201 and 201**.

This is **not** the img2img path §0 killed — that fed cel 0 through the sampler, which
preserved identity by freezing everything including the legs. No sampler runs here at all.

Four things learned building it, each of which nearly killed the method:

1. **Derive the matrix, do not transcribe it.** A negated `y` component in the MLS
   per-handle matrix mirrors every displacement about the centroid and folds the field over
   itself. It presents as heads torn into butterflies and duplicated limbs — which reads
   exactly like *"warping cannot carry this"*. **A transcription error in a formula does not
   announce itself as a bug; it announces itself as evidence against the approach.**
2. **Handles at the quarter, half and three-quarter points of every bone**, not just at
   joints. With joints and midpoints only, a quarter of a thigh has nothing near it and the
   similarity term bends it freely — both trouser legs carried visible S-curves.
3. **The anchor ring must sit OUTSIDE the frame.** On the frame edge it was 63 pixels below
   the soles, and the nearest handle to a foot being lifted was a fixed point resisting it:
   49 pixels of lift asked, **1 to 5 delivered**. Pushed outside, 24 to 27. Removing anchors
   instead is worse — at two per edge the warp blew the figure off the canvas.
4. **Overshooting the target is not a substitute.** Pre-scaling the leg delta did nothing
   (145 → 146 at ×1.25) and past ×1.28 the ankles cross and the legs splay, inverting the
   pattern. The damping had a cause; it was worth finding rather than compensating for.

**What a 2D warp cannot do**, and these are honest limits rather than bugs: it cannot rotate
a rigid shoe, so a lifted foot points rather than staying shoe-shaped (mitigated by dropping
the lift from 0.048 to 0.030 of figure height); and it has no depth, so it cannot hide the far
foot behind the near one — the 1990 art shows **one** merged foot blob in every cel and a
warp always shows two.

**And the source frame's flaws propagate to all four cels.** That is the deal, and it is a
good one: they propagate *consistently*, which is what the eye forgives.

## 7e. The pass meant to fix hands was creating them

A player: **"and his hand!, wtf is going on with his hands??"**

Two guards had gone missing in the token trim of §1 — `missing fingers` and `fused fingers`
— and mangled hands are the most predictable failure of this model family without them. But
neither was the real cause.

**The hand refinement crops at the AUTHORED wrist**, and the model puts the hands where it
likes: in a pocket, behind the back, tucked into a jacket. Skin-toned fraction inside the
crop the refiner actually used, over three source frames:

| view | hand_r | hand_l |
|---|---|---|
| 280 | **6.5%** | **0.1%** |
| 281 | 11.5% | 31.9% |
| 282 | **7.2%** | **2.2%** |

**Four of six crops contained essentially no flesh** — and each was handed to the sampler with
the instruction *"close up photograph of one relaxed human hand, five fingers, natural
anatomy"*. It did what it was told. The hand-shaped blob on the figure's hip is not a badly
drawn hand; it is **a hand hallucinated onto a jacket by the pass meant to improve hands.**

Gated now on a skin-fraction test: under 8%, the crop is skipped and the reason logged.
**Refining nothing is strictly better than inventing something.**

Note how the warp changed the stakes. Before, a hallucinated hand was one frame in four;
once the cycle is built by deforming one photograph it is identically wrong in every frame.
**The source frame's quality bar goes up when you stop generating each frame, not down.**

## 7f. A walk is a torso that moves as a unit

A player, on a cycle whose amplitudes had all been measured: **"nobody's body moves like that
when they are walking!!!"**

Torso-only measurement — the centre of the *widest run*, because at hip height the silhouette
is arm/torso/arm and full-extent measures the swinging hands instead of the pelvis:

| | bob | shoulder sway | hip sway | **shoulder − hip** |
|---|---|---|---|---|
| 1990 art | 0.0212 | 0.0354 | 0.0299 | **0.0078** |
| what was rejected | 0.0054 | 0.0343 | 0.0529 | **0.0549** |

**The 1990 torso moves very nearly as a unit** — shoulders sway slightly *more* than the hips,
and the differential across the whole cycle is 0.0078 of figure height. Ours was **seven times
that**, with the pelvis swinging out of phase with the shoulders. Authored `twist` was ±0.012
against a measured ±0.004; it is now zero, and the residual comes from MLS bleeding the leg
scissor upward into the pelvis.

And the bob was **four times too small**, not too large: 0.0054 against 0.0212, because the
warp damps vertical motion to about 70% of what is asked. The authored rise is therefore 0.027
where the art measures 0.014 — the one place the cycle deliberately departs from the
measurement in order to land on it.

**Two lessons.** First, *every amplitude can be individually correct and the motion still
wrong*, if the relationship between two of them is wrong — a differential is a measurement in
its own right and has to be checked as one. Second, measure the thing and not its neighbours:
the first sway numbers were contaminated by the arms and pointed at the wrong culprit.

## 7g. Judge the frames the game actually draws, at the size it draws them

Two compounding mistakes, and both are about *looking* rather than about the pipeline.

**The game plays sixteen frames, not four.** `smooth_walk` generates three interpolated
sub-frames between every cel, so three quarters of what a player sees had never been reviewed
by anyone. Every GIF, sheet and verdict up to this point was built from the four cels.

**And the four cels were being judged in thumbnails.** Comparison sheets were rendered at
26-30% scale, where a three-pixel wave along a trouser seam is invisible. The user, playing at
full size, saw it instantly and called the legs **"squiggly"**. It had been in the four cels
all along.

So: **render what the engine renders, at the resolution it renders it, before calling anything
finished.** `legs16_280.png` — all sixteen frames, legs cropped and magnified — is what should
have existed from the first warp.

The fault itself is worth recording too, because it is a trade rather than a bug. The
squiggle peaks at the *pass* cels and decays away from them, and its period is 40-60 pixels —
**exactly the handle spacing along the leg**. MLS with densely packed point handles whose
motions differ slightly does not interpolate smoothly between them; it ripples. The quarter-
and three-quarter-point handles that cured the rubber-hose bend (§7d) are what caused it.
Fewer handles bends, more handles corrugates: **two ends of one dial, and neither end is good
enough at 468 pixels.** The displacement grid was ruled out first — rendered at step 1 instead
of step 4 the result is identical.

The escape is to stop using point handles at all: per-bone linear blend skinning gives each
*bone* one rigid transform and weights pixels by distance to the *segment*, so a straight seam
stays straight by construction. Built, and it works:

| seam deviation (RMS px) | cel 0 | cel 1 | cel 2 | cel 3 |
|---|---|---|---|---|
| MLS point handles | 4.48 | 3.70 | 4.65 | 3.34 |
| **per-bone skinning** | **1.32** | **1.88** | **1.27** | **1.77** |

All sixteen shipped frames, interpolated ones included: worst **2.46** against a tolerance of
3.0. Skinning also removes the anchor ring — a virtual "stay where you are" bone holds the
background, so nothing pins a foot that is trying to lift, which was a separate fault MLS
needed a margin to work around.

### And the metric had to be fixed before it was worth anything

The first straightness check fitted a line to each trouser edge and reported the residual.
**The 1990 art scored 27-45 px on it** — far "worse" than any warp — because it was measuring
leg *geometry*: the original's legs are splayed and a two-leg silhouette is not a vertical
line. A ripple is HIGH frequency and leg splay is LOW frequency, so the reference is now the
edge's own moving average over about a limb's width.

Same family as §7b: **a metric has to separate the thing you are looking for from the thing
that is supposed to be there**, and if it cannot, it will confidently rate the reference worse
than the defect.

## 7h. A rigid thing needs a rigid transform, and the seam belongs at the garment's edge

A player: **"the whole right side shirt skews when he walks, thats not how clothing works."**

A ribcage does not shear. The skinned field spanned it with two bones — neck-to-RIGHT-hip and
neck-to-LEFT-hip — and those two hips move independently in every cel, so blending their
transforms **is** shear and no parameter avoids it. Measured field shear over the torso band,
as `||JᵀJ − I||` on the backward field's Jacobian, which reads 0 for any rigid motion:

| | core of the torso | outer band |
|---|---|---|
| skinned field, no plate | 0.049 (p50), 0.268 (p95) | 0.196, **5.148** |
| one rigid plate | **0.000** | **0.000** |

The asymmetry they reported was in the same dump. Dominant bone over the ribcage: neck-Rhip
27.3%, neck-Lhip 21.2%, **RIGHT arm 23.6%, left arm 12.6%** — the right arm's bones reach into
the shirt with nearly twice the left's influence, so one side skews and the other does not.

The transform is derived from the neck and the hip **midpoint**, which cannot disagree with
itself, and the scale is forced to 1 so the shirt cannot stretch or parallelogram.

### Colour identifies the rigid thing — and the skeleton is NOT always the fallback

Which pixels are the shirt is a colour question, for the same reason the shoe was: geometry
cannot tell a shirt from an arm. The rule that came out of the shoe generalised correctly.
What did not generalise was the **fallback**.

When saturation alone let a brightly lit forearm through, a cylindrical exclusion of radius
0.055 of figure height around each elbow-wrist segment was added, on the stated reasoning that
*"no shirt lies along a forearm"*. It passed its check first try and produced the next defect
a player saw: **a ragged white fringe down the shirt's left side on cels 1 and 2.**

Drawing the segments on the photograph answers it in one look, which is the whole method:
**the generator put each arm slightly OUTBOARD of the pose it was given, so the elbow-wrist
segment runs down the arm's inner edge, which is shirt.** The sentence the cylinder rested on
is false for this figure. It was also twice a forearm wide — the forearm's own half-width,
measured at five heights between elbow and wrist, is 17 px = 0.0166 against a radius of 56 px —
so it reached 40 px into the cloth and took a vertical swath out of both flanks.

**And the check that passed it was measuring nothing.** Its forearm probe was a box centred on
the elbow-wrist *segment* — which is the cloth/skin boundary, so the box is half shirt — and
any cylinder wider than the box reads 0% whatever it does to the garment. §7a's lesson about
instruments that cannot see the fault they are trusted for, one level down: here the
instrument could not see the fault because it was **positioned** by the same wrong assumption
as the fix.

Probes are now found from the **silhouette** — the outermost 0.030 of figure height on each
side at forearm heights, measuring sat 77 / val 118 and sat 71 / val 104, unambiguously skin.

| | mask px | chest | **garment** | R forearm | L forearm |
|---|---|---|---|---|---|
| cylinder 0.055 | 44372 | 89.4% | **49.9%** | 0% | 0% |
| cylinder 0.025 | 63934 | 100.0% | 84.1% | 0% | 0% |
| **no cylinder** | 69326 | 100.0% | **97.6%** | 0% | 12.9% |

The 12.9% is the mask's own two-pixel dilation touching the arm's antialiased edge; at no
dilation it is 8.4% and the mask gains a pale ragged sliver along the whole flank, which is
worse. **The cylinder was costing 13.5% of the garment to buy nothing.**

### "Garment" is the half of the check that did not exist

Every number above except that column existed before. A mask can cover the chest perfectly
while a comb is bitten out of its sides, and chest coverage reports that as a pass — which is
exactly what happened, at 89.4% chest and 49.9% garment. The garment probe is the whole
t-shirt, bounded on each row by the first run of four saturated pixels outboard of the
centre, i.e. by the arm's own edge. §7ab said a mask check needs the thing that should be in
and the thing that should be out; **it also needs the whole of the thing that should be in.**

### Classify by WHICH GATE REJECTS, not by sweeping the gates

Five parameter sweeps went nowhere on the shoe and one picture of the mask answered it. The
same here, one level further: instead of sweeping the two colour tolerances, paint every pixel
of the torso box by the gate that rejected it.

```
kept                        40225 px   44.3%
rejected, too SATURATED     48166 px   53.0%     <- a whole flank, and both arms
rejected, too DARK           1297 px    1.4%
rejected, hem/narrow             0 px    0.0%
```

The saturation gate was doing all of the work and throwing away over half the garment with it;
the value gate, which the comment at the site credited, rejects 1.4%; and the geometric rule
rejects literally nothing. `ref_sat` is 6 on this shirt, so `ref_sat + 5` was a gate at
saturation 11 — tighter than the garment's own variation.

**Where the line belongs is a measurement, not a choice.** Sampling saturation every 16 px on
every row from the sleeve hem to the waistband: the cloth never exceeds 25 and the arms never
fall below 36, on any row. `ref_sat + 24` sits in that gap. Garment coverage 49.9% → 97.6%,
arms unchanged at 0%.

### …and then the same mistake, one character along

**`ref_sat + 24` was measured on ONE figure and shipped to nine, and it is wrong on five of
them.** It is a tolerance around a single reference, so what it means in absolute terms
depends entirely on what the garment is. On 282 the shirt is white, `ref_sat` is 6, and the
gate lands at 30 — in the measured gap. On 285 and 286 **the garment is the SATURATED
material**: cloth 156 and 203 against a forehead of 36 and 69. The same rule puts the gate at
180 and 227 and admits everything, and the dump shows 285 with both bare forearms and both
hands welded into the rigid plate. That is the exact fault this section exists to remove,
reappearing on a different character for the same reason as every other time — *an instrument
tuned on the case in front of me.*

The replacement has no tolerance to tune: **classify each pixel by which of two references it
is NEARER in RGB** — the chest block the mask already samples, and the **forehead**, which is
skin on every character (the hands are in pockets, the forearms may be in a sleeve, and the
throat is beard on 282). Nearest-of-two is symmetric, so it cannot invert when the garment is
the saturated one, and the sat/val gate is kept and intersected with it because the nearest
test alone will claim trousers that happen to sit nearer the shirt than the face.

| bare forearm swallowed | `ref_sat + 24` | nearest-of-two |
|---|---|---|
| 285 right / left | 100% / 95.6% | **8.1% / 9.3%** |
| 292 left | 56.4% | **7.3%** |
| 284 left | 97.5% | **6.6%** |
| 282 left *(the control)* | 5.9% | **5.3%** |

**The lesson is not "use nearest-of-two".** It is that `sat_tol` had exactly the shape of the
failures above it in this section — a number set from the one figure that was being looked at,
with nothing in the process asking what the other eight would do with it. The guard against
that is §7h's last subsection, which is a tool rather than a resolution.

### The check is in the tool now, and it was wrong the first time too

`warp_walk.py --shirt-check <dir> --poses <dir>` runs the both-halves test over every view in
one go and prints what the mask must COVER (chest, whole garment, sleeves) beside what it must
NOT (both forearms, both hands), with `--out` dumping the mask over each figure at 1:1. It
lives in the tool and not in a scratch script for the reason §8 gives about the GPU lock: a
guard nobody runs is why everybody stopped watching.

Its first version reported the mask "swallowing the forearm" on 280, 285, 286, 290 and 292 —
**every character in a long sleeve, where the band between elbow and wrist is jacket and the
mask is right to cover it.** A check that fails five of nine characters for being correct is
the same fault as a check that passes while swallowing an arm, pointed the other way. The
probe is now intersected with skin, so on a long sleeve it correctly becomes empty and prints
`-` rather than a pass.

And the hands are gated on actually containing flesh, for the reason §7e records: the
75th-percentile saturation in a block at the authored wrist is **25 on view 280 and 4.5 on view
290**, and both of those are jacket. A hand coverage figure off those blocks would be measuring
cloth and calling it a hand, so the gate prints `-` and says so.

Standing results, for whoever picks this up: **282 is clean** (garment 100%, chest 100%, both
forearms ≤5.3%, right hand 0%). **284, 286, 291 and 292 are not** and must not be rebuilt until
they are — 291's garment reads 54.9%, and 292's right forearm 73.9%.

A geometric rule that rejects nothing is still a trap: the half-width was 0.115 of figure
height, which is the **ribcage's** width and not the garment's — the t-shirt's own edge
measures 0.063 to 0.118 from the centre, so it clipped the widest rows. Widened to 0.160. It
was *suspected* of causing the flank crease below and did not: sweeping it from 0.115 to 0.300
moves the mask by 30 px in 69,326, because colour is what binds.

### Where the seam goes, and which way the handover ramps

**Put the seam where the garment's own boundary already is.** A tear at a real edge is
invisible; a tear across a continuous surface is not. That is why the shoe's boundary at the
ankle works, and it is why the sleeve hem — measured at 54% and 62% of shoulder-to-elbow —
is the right place for the mask to stop.

But a hard switch between two fields that disagree tears wherever they disagree, and the
disagreement is not small. Measured at the plate's edge, as the distance between where the
rigid map and the skinned field send the same pixel:

```
cel 0   median  5.3   p90  8.6   p99 11.9 px
cel 1   median 12.6   p90 47.2   p99 60.5
cel 2   median 17.0   p90 58.3   p99 80.4
cel 3   median  9.9   p90 31.3   p99 39.7
```

**That is the player's report as a number, and the ranking is exact** — cels 1 and 2 are the
two they called ragged. The shoe gets away with a hard boundary (§7ab) because its transform
is a pure *translation*; the ribcage's is a rotation about the neck, so its disagreement with
the arm bones grows with distance and is largest at the flank, the one place it abuts a
swinging limb.

Three handovers were built and dumped at 3–4x:

* **Hard switch** — a staircase of duplicated and dropped pixels down the flank.
* **Ramp outward** (dilate the plate, spend the blend on the arm) — sounds like the right
  trade and is not. The band outside the plate *is* the arm, so its dark pixels get the
  torso's transform and are dragged into the cloth: a hard dark staircase, plainly worse.
* **Ramp inward** (erode, spend the blend on cloth) — 0.010 of figure height is too narrow to
  absorb a 58 px disagreement and creases; **0.050 is wide enough that the handover reads as
  drapery**, and the plate's core stays exactly rigid.

Also rejected, with a picture to show for it: **the shoe's SMEAR rule.** For a shoe the
vacated ground really is background, so sending anything outside the region that reads shoe
pixels off-frame is right. For a flank it is not — the arm hangs against the cloth — and it
bit a jagged chunk out of the shirt's side and showed green between the shirt and the arm.

**So the refined rule is three clauses, not one.** *A rigid thing needs a rigid transform.
Colour is what identifies it. And when colour alone is not enough, the fallback is whatever
instrument actually fits the picture — not the skeleton by default, because the skeleton is
only as true as the generator's obedience to it.* Connectivity failed on the shoe because the
figure is one component and failed on the shirt for the opposite reason, because shading
breaks the shirt into several; colour solved the shoe; and on the shirt the answer was colour
again, aimed at the right axis and set from a measured gap rather than from a reference pixel.

Everything is behind `rigid_torso=True`; `False` restores the pre-torso behaviour for
comparison, and `--torso-feather 0` restores the hard switch.

## 7i. The swing foot's lift is NOT too large, and the clip cannot measure it

> **This section was first written with the opposite conclusion and it was wrong.** It is
> left corrected rather than rewritten, because the way the wrong answer was reached is the
> transferable part: a clip was used to measure a quantity it physically cannot show, and the
> number it produced was confident, plausible, and about the wrong phase of the stride.

`pose_author.CYCLE` lifts the swing foot `ank_dy = -0.095` of figure height at both DOWN keys,
and the planted foot carries a `+0.010` depth offset, so the two soles are **0.105 apart**.
That is **18 cm on a 1.75 m person**, and §7ab deliberately carries the shoe FLAT and RIGID.
A flat shoe held level 18 cm off the ground is a **march**, not a walk, and it is in the four
REAL cels, so no smoothing question touches it.

The number has never been set from a measurement. It was 0.048; it was cut to 0.030 to hide a
shoe spike; the spike turned out to be the missing toe joint (§7ab) and it went back up.

### Measured off the user's own reference clip

`walkref.mp4` is a man walking **toward a fixed camera** on a boardwalk, barefoot, in shorts —
a front view of exactly the motion being authored, and barefoot removes the shoe from the
ground line. Measurements only; the clip is the user's and is not copied into the repo.

**Every automatic sole measurement on this clip fails, and the reason is worth more than the
attempt.** Column-wise, connected-component and bounding-box methods all report the two feet
"merged" in every frame of every set. Dumping the masks at 4x with a labelled source-pixel
grid shows why in one look: **the silhouette's lowest rows are the man's CAST SHADOW on the
boardwalk**, not his sole. Anything that takes the bottom of the mask as the ground is
measuring shadow. (The same reason §1 kept `shadow` in the negative prompt — a cast shadow
touching the feet survives every silhouette test there is.)

So this was read the way Muybridge was read (§7ac): 4x, a grid every 10 source pixels, read
by eye. Across one 16-frame set it gave **5–8 px on a 295 px figure = 0.017–0.027 of figure
height against an authored 0.105**, which looked like a factor of four to six and was reported
as one.

### THE CLIP CANNOT MEASURE THIS, for three independent reasons

**1. The feet are below the frame.** Of 279 extracted frames, **249 have the silhouette
touching row 359 of a 640×360 video** — he walks toward a fixed camera and his feet leave the
bottom of the frame long before he is big enough to read.

**2. Where they are in frame, the bottom of the mask is the SHADOW** (above).

**3. The swing foot is occluded by the stance leg.** This is the one that matters and it is
inherent to the view: in a front view of someone walking toward the camera the swing leg
passes *behind* the stance leg, and it is hidden for exactly the part of the cycle where it is
highest. Testing it directly — he is barefoot, so his feet are skin and the shadow is not —
**only 8 frames of 279 have two separable bare-foot runs at all**, and in all 8 the runs are
clamped at the frame edge, so even those are unusable.

So the 0.017–0.027 was a survey of **stance and double-support**, which is where both feet are
near the ground by definition, reported as if it covered the stride. It is not a measurement
of swing clearance. It is a measurement of the frames in which there is no swing clearance to
see.

**The general rule, and it is the expensive one here: before trusting a measurement off a
reference, ask whether the reference can physically show the quantity.** §7ac's five failed
Muybridge passes were all about segmentation being defeated; this is a level above that — the
segmentation worked and the information was never in the picture.

### What the geometry says, which needs no clip at all

The ankle's projected position in a front view is

```
hip  +  thigh * cos(thigh angle)  +  shin * cos(shin angle)
```

and §7aa/§7ac established, and confirmed off Muybridge Plate 5, that the swing shin projects
to **0.61** of its length at the flexed key. That is `cos(shin angle) = 0.61`. Even with the
thigh perfectly vertical, which is the most generous case there is:

```
thigh  0 deg forward   ankle projects 0.871   =  0.084 of figure height ABOVE canonical
thigh 15 deg forward   ankle projects 0.863   =  0.092
thigh 30 deg forward   ankle projects 0.840   =  0.115
```

**The authored 0.095 sits inside that range.** Lift and flexion are not the same number
because of how `CYCLE` is written — they are the same number because of **anatomy**. A
0.61-foreshortened shin *requires* the foot to be roughly 0.084–0.115 off the ground. You
cannot have the knee bend and keep the foot down, and the lift was never the free parameter it
looked like.

### And a knee depth offset cannot rescue it — worked, not assumed

The proposal was to give the knee its own depth offset, since a knee swung forward is nearer
the camera and §7ac established that nearer appears lower. The projection, calibrated on the
one depth number already measured (the feet's 0.0209, at ground level where the effect is
largest):

```
R = ((h - Y) / Hf) * (d / Z)        knee at Y = 0.260 of figure height above the ground

camera at 0.8-1.2 figure heights, knee travel 0.3-0.7 of the foot's
    knee depth offset  =  0.004 - 0.011 of figure height
```

A few thousandths. Against it, the **thigh's own foreshortening moves the knee the other way
and three to seven times further** — 0.008 at 15°, 0.031 at 30°, 0.069 at 45° — so a knee
swung forward appears **HIGHER**, not lower. The perspective term is the right instrument for
the FEET, where `Y = 0` makes it largest and nothing competes with it; at the knee it is
swamped by the limb it is attached to.

And a hard bound settles it without any of that: **the knee can never project further from the
hip than the thigh is long**, so it cannot go below `hip + thigh = 0.740`. The reconciliation
needed 0.812. No offset of any size places it there.

### So what IS wrong with the raised foot

Not its height. **Its orientation.** §7ab gave the shoe a bone of its own whose transform is a
pure *translation*, deliberately, because that is what killed the black horn — and §7d records
the cost in the same breath: *"it cannot rotate a rigid shoe, so a lifted foot points rather
than staying shoe-shaped"*. A real foot at peak swing is plantar-flexed with the toe down. A
**flat shoe held level 0.095 of figure height off the ground** is what reads as a march, and it
reads that way at the correct ankle height.

That also explains the other half of the complaint without needing the lift to be wrong: the
silhouette's extent at ankle height swings 49→122 px across our four cels against Sierra's
215→228, because the flat raised shoe clears ankle height entirely instead of trailing through
it. Rotating the shoe about the ankle is the open question; the lift is not.

**Nothing was changed, twice.** First because a measurement that argued for cutting the lift
disagreed with the knee; then because the measurement itself turned out to be of the wrong
phase. §7b, §7aa and §7ab are the same shape, and the tell has been identical in all four: the
number disagreed with something else that was also measured.

### And the shoe now flexes, because the conditions that made it fail had changed

§7ab closed rotation as a failure. **It was closed under conditions that no longer hold, and
re-testing it took one run.** When rotation produced the black horn the shoe was not an
identified object at all — it was whatever pixels hung past the shin bone's far endpoint,
where distance-to-segment clamps, compressed 1.6:1 along the axis they lay on. Rotation was
failing on a region that was never the shoe. Since then it has a dedicated rigid foot bone and
a per-foot colour mask that isolates it, verified by dumping it.

Re-tested with both in place: **it does not horn.** The shoe stays shoe-shaped at every
setting tried, and reads as a trainer seen at a steeper angle rather than a flat slab hanging
level in the air.

**The transform is derived from the shin and introduces no new constant.** A foot is rigidly
attached to its shin, so the flex is driven by that shin's own projected-length ratio:

```
swing shin   0.610 of its source length      in-plane rotation   6 deg
planted      0.975 - 1.021                                      14 deg
```

so the weight `(1 - ratio) / 0.39` is 1 on the swing foot and 0 on a planted one, and both the
stretch and the in-plane term carry it. **Inheriting the shin's rotation unweighted would tilt
the planted shoe by 14 degrees**, which is wrong for the obvious reason: a foot flat on the
ground does not rotate because the leg above it leans. That is the whole reason the weight
exists, and it is the same shape as every other failure in this file — an instrument that is
right for one case applied to its neighbour.

**One thing broke, and only the picture found it.** A stretched shoe showed a GREEN HOLE
punched through the middle of the trainer. The shoe mask's colour test drops the laces, the
logo and the dark eyelets, leaving small islands enclosed by shoe; while the shoe was carried
as a pure translation a two-pixel hole was invisible, but stretched — and then caught by the
smear rule, which sends anything outside the rigid region that reads shoe pixels off-frame —
it became a hole in the silhouette. Filled, the same way the ribcage's holes are filled.

The fragment count moved from 21 px to 48 across four cels and **never looked like a hole**.
The 1797 px fragment §7ab fixed does not return; what is left is thin edge slivers. Seam
deviation in the shoe-excluded band is 0.66 px, unchanged.

**What it does NOT fix, stated plainly because it was the reason for building it.** The
ankle-height silhouette extent — ours swinging 47→118 px against Sierra's 215→228 — **does not
move**: 71 px of swing before, 72 after. The arithmetic says why. The raised shoe's bottom sits
at about row 1022 with the ankle lifted 97 px, and 0.95 of the figure is row 1068; even a 1.6
gain only reaches 1075, which is why the extent moved by one pixel. To close that gap the shoe
would have to stretch into a clown shoe.

**That metric is not about the shoe. It is about occlusion.** Sierra's two feet merge into a
single blob in every cel because the near one covers the far one; ours never overlap because
the warp has no depth ordering (§7d's honest limit, and the leg-occlusion work is still open).
The flex is a real improvement in how the raised shoe reads and it is worth having; it is not
what closes the distance to 215-228.

## 8. Two bugs that looked like hardware faults

**`largest()` was O(components × pixels)** with a Python flood fill. Fine on a clean matte;
on a speckled one it sat on a single frame for forty minutes *while still holding the GPU*.
Replaced with run-length labelling and union-find: **0.06 s** on a deliberately speckled
704×1152 mask, verified against the flood-fill answer. scipy would be one line; scipy is not
installed here and `fit_walkers.py` records the same.

**The GPU lock did not lock anything, and it was cited as the reason not to worry.** It wrote
its pid file into **the run's own output directory**, so two runs with different `--out` never
saw each other and two different *tools* never could. It protected `gen_cast` from a second
`gen_cast` and nothing else — while a walker run and a building run sat on the same 4GB card
at 3814 MiB with both crawling and neither refused.

Same shape as §1's CLIP guard covering one of two identical inputs: **a guard that does not
guard is worse than none, because it is why everyone stopped watching.** The lock is now at
one fixed path shared by every tool, and it also asks `nvidia-smi` directly for any other
python process holding a CUDA context — the driver being the only authority on what is
actually on the card.

**A killed launcher on Windows does not reap the python child.** It keeps the CUDA context
and ~3.5 GB. When that happened a base frame went from 354 s to 2014 s; otherwise the next
run fails at CUDA init with 0xC000070A, which looks exactly like a hardware fault and is not
one. `claim_gpu()` writes a pid file, refuses to start while another instance is alive, and
clears it on normal exit and on SIGTERM. Verified: a retry loop raced it and was refused
three times naming the holding pid.

Separately, an unrelated **0x8007000e (E_OUTOFMEMORY) from the WMI query inside torch's DLL
loader** turned out to be genuine commit exhaustion — ~24 GB held by already-dead python
processes from previous days. Worth logging free memory at launch.

## 8a. A likeness error found by measuring a suspicion

body2 came back wearing dark sunglasses. Sunglasses are **body0's** identifying feature, and
at 39 pixels wide eyewear is one of the very few things that distinguishes two middle-aged
men from each other — so this mattered more than its size suggests.

Rather than trust anyone's eye on a 39x95 sprite, the eye band's luminance was measured
against the **cheek band of the same face**, so the number is a contrast within one figure
rather than an absolute that skin tone would dominate. Central 60% of the head's width,
averaged over four cels:

| | eye / cheek | reading |
|---|---|---|
| **280, 281, 282** (body0) | **0.570, 0.620, 0.534** | eye band ~45% darker — sunglasses |
| **290, 291, 292** (body2) | **1.064, 1.053, 1.075** | eye band *brighter* — no sunglasses |
| 284 (body1), 294 (body3) | 0.923, 1.145 | no sunglasses |

Unambiguous, and consistent across all three of each man's outfits. body0 wears them, nobody
else does.

The init was not the culprit here — `glasses` is set only on body0, so no bar was ever
painted on body2 — the model simply added them. That makes it a **category** fault, and the
fix is the prompt: `sunglasses, dark glasses` in body2's negative.

## 9. Decisions taken

* **Views 274–277, the demo walker: leave the original art.** It is flat cartoon in pure
  palette entries — view 274's legs are exactly RGB (0,0,208), 275's exactly (128,0,192) —
  not digitised photography like the other sixteen. It appears only in attract mode, never
  beside a player, and the joke is that it is drawn rather than photographed. A generated
  cartoon version exists in the samples and does not resemble the original; it should not
  ship.
* **The four `+3` undressed views (283, 287, 293, 297): leave the original art** unless asked
  otherwise. 293 is the barrel gag, 61 cels wide with a 68%-white mass at the foot band, and
  props are exactly what failed last time. Prompts are written; no frames generated.
* **View 293 loop 1** (four 13×13 cels) is props, not people, and is skipped by the pose
  author. 80 human cels + 4 prop cels = the 84 in the set.

## 9a. Every cel a player can reach, and which still show the 1990 figure

A walker that turns into a different person when his clothes wear out is a bug, not a
caveat. The rule is **no trace of the old graphic anywhere a player can get to**, so this is
the full audit rather than a spot check. Against the installed `cast0` set:

| character | cels still falling back |
|---|---|
| body0 (280–283) | **4** — view 283, the undressed tier |
| body1 (284–287) | **16** — all four views |
| body2 (290–293) | **20** — all four views, plus view 293's 4 prop cels |
| body3 (294–297) | **16** — *nothing has ever been generated for this character* |
| view 500 | the character-select screen still shows the 1990 photographs |

**body3 was omitted from the generation run entirely.** The view list was chosen as "the
three dressed tiers of three bodies" and nobody noticed it left out a whole playable
character — a player picking the fourth would have got the 1990 woman throughout. Enumerate
the reachable set from the *resource* side, not from the list you happen to be working on.

68 walker cels exist in `assets/png` for the four bodies.

## 9b. A known exception: view 293 loop 1, four 13x13 prop cels

View 293's second loop holds **four 13x13 and 14x14 cels** which are the barrel gag's props —
objects, with no person in them. `pose_author` skips anything under 40 rows and is right to:
an authored human skeleton scaled into a 13-pixel box produces a mangled figure where an
object belongs.

So generation is the wrong instrument here and these **four cels** will fall back to the 1990
art whatever the walker run does. Upscaling is the honest answer for them. It is the only
place in the set where "no trace of the old graphic" cannot be met by this pipeline, and it
is four cels of a prop rather than any part of a character.

## 10. Still open

* **Consistency is unmeasured.** `boil` needs all four cels of a loop and only cel 0 of five
  views exists. `fit_walkers.py --measure` and
  `smooth_walk.py --dir assets/png12x-cast --scale 12` both still need running.
* **View 500**, the character-select screen, needs `fit_walkers.py --portraits` or the select
  screen shows one cast and the board another.
* Known cosmetic misses on the current stills: heels on 296 where the prompt says flat shoes;
  an oversized shirt on 292; no sunglasses on 282, which the original wears and which are a
  strong silhouette feature at 39 pixels wide.
