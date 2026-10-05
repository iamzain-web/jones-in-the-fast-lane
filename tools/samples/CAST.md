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

## 0. THE DELIVERABLE IS THE GAME. FINISH EVERY ROUND BY INSTALLING.

**`assets/png4x-cast0` and `assets/png12x-cast0` are the deliverable. `tools/samples` is a
comparison aid and nothing else.**

This is first because of how it was found. A whole day of work went into the walk — leg
occlusion, the arc fix, the sagittal arms, the shirt off the arm chain, the mid-swing source,
forced hand refinement — and **none of it reached the playable game.** The installed sprites
were dated 30 September 17:10 throughout. The player was looking at day-old art and reporting
that the walk was still wrong, and he was right, because the art he could see *was* the old
art. Every measurement in this file had improved and the game had not changed by one pixel.

The cause was a rule that was correct in its own terms and wrong in its reach: *do not replace
the build until the new one wins on every axis.* That was meant to govern which comparison
sheet to show. Applied to the game, it meant a build that lost on one measurement out of six
could not ship, so nothing shipped at all — and a walk that moves correctly with an imperfect
hand is enormously better than one that does not move correctly at all. **A quality bar that
gates the only artefact anyone can actually play will eventually stop everything.**

So:

- **Measure, fix, INSTALL, then report.** Every round.
- **Both scales, every time.** The ladder picks art by factor, so a half-installed set shows
  new art at one scale and 30 September at the other.
- **Verify on disk afterwards, by hash or timestamp, not by the copy step's exit code.** This
  project has twice had a build report success while shipping the previous artefact.
- **Say in the report what a player can now see that they could not see before** — and if a
  round produced no installable improvement, say *those words*. That sentence is the check.

### And the install must be ACCEPTED, not just verified. `tools/check_install.py`

The first install under the rule above shipped a matte failure to the player. It was verified
by hash and timestamp — **which proves the files moved and says nothing about what is in
them.** The cels carried opaque background outside the figure: view 282 at 19–22% near-black
against a clean baseline of 5%, view 281 at 48%. The player relaunched and saw blobs round the
man, and that was the first anyone knew.

**The cause was mine and it was one line of plumbing.** `fit_walkers.key()` does
`Image.open(p).convert("RGB")`, which drops alpha. `warp_walk` writes its transparent region as
`(0,0,0)`, so handing the warped cut-outs straight to the fit gave `silhouette_matte` a **black
backdrop** — and that matte decides the uncertain band of its trimap by colour. Against black
it cannot tell a navy suit, a black blazer or dark hair from the background, so it kept the
background. Every earlier fit run was clean at 5% because it was fed the *generator's* frames,
which have the green field the matte was built for. The chain measures 5.0% at the source, 5.0%
after the warp, and 19–22% after the fit: **the fit is where it enters.**

**And the obvious fix produced the opposite defect**, which is why the gate measures two
colours. Compositing the cut-outs back over the green field removes the black and leaves
**green** inside the sprite instead — 20% of it — because the trimap's *core* comes from the
1990 silhouette and the warped figure is narrower than that outline, so background inside the
core is forced opaque whatever colour it is. A gate that watched only black would have passed
that build.

> **Verifying that an artefact was delivered is not the same as accepting it.** Hash and
> timestamp answer "did it move". Only opening it answers "is it right". Every delivery step
> in this project now needs the second question, because the first one passed while the player
> was looking at the failure.

The near-miss that should have caught it came an hour earlier: a dark shape beside the figure
on a contact sheet turned out to be **the sheet's own grey panel behind a navy suit**. Judging
a matte by eye on a dark background misses real intrusions and invents false ones in the same
glance — **it fails in both directions at once, which is why it feels like it is working.**
The information was in hand an hour before the failure shipped, and the next matte was judged
the same way. **Magenta, not grey. A number, not a look.**

### The gate needed a second half, and it still has a limit

The first version measured only background intruding **into** the sprite. Two different
attempted fixes then ate the figure instead — one took a blazer, a shoulder and most of a
face; the other took the hair and one sunglass lens — and **both walked straight through the
gate**, because removing dark pixels makes the near-black percentage go *down*. View 281 went
from a broken 48% to an eaten 24% and read healthier than the clean build at 25%.

> **A check for too-much cannot see too-little.** Every threshold has a direction, and the
> failure that gets through is the one travelling the other way.

So the gate also measures how much of the 1990 outline the sprite still **fills** — clean
builds 0.587–0.713, the eaten 281 0.520–0.547 — and it is run against every known-bad set as
well as the known-good one each time it changes.

**And its limit, stated rather than discovered later: it did not catch the eaten hair.** A few
per cent of area, all of it dark, moves neither metric. That one was found by compositing the
head over magenta and looking at it, and nothing in this file would have found it otherwise.
The gate stops a catastrophe reaching the player; it does not replace §0a.

The gate is calibrated per view against a known-clean build, because one global threshold would
either pass 281's broken 48% or fail its clean 25% — its blazer really is that dark. And it was
checked the only way a check can be: **it fails on both known-bad sets and passes the known-good
one.**

## 0a. THE RULE ABOVE ALL THE OTHERS: DUMP THE THING AND LOOK AT IT

Put first because it is the one that keeps paying, on problems that have nothing to do with
each other. Every section below has an instance.

### A CHECK MUST FAIL WHEN IT MEASURED NOTHING

The purest instance of the whole family, and it sat in the acceptance gate itself.

`check_install` printed **"GATE PASSED. Safe to install."** over an **empty directory**. The
loop walks the cels it finds and appends a failure for each bad one; find no files, append no
failures, report success. Nothing to report read as nothing wrong.

It was found by accident — a build step silently produced zero cels and the gate blessed the
result — and it would have waved through *any* build where a step produced nothing, which is
the exact failure this project has hit over and over: a `-Filter` that matched no files while
`Copy-Item` reported success, a string replace that silently did not match, a sheet quietly
halved, an MSB3027 swallowed while the test passed against the previous build.

> **A pass has two preconditions, not one: that nothing was wrong, AND that something was
> examined.** Any check that aggregates over a collection must assert the collection is not
> empty. "No failures" and "no evidence" are the same value in almost every implementation
> and opposite conclusions in every interpretation.

`check` now counts what it measured and fails with "it measured nothing, which is not the
same as finding nothing wrong" when that count is zero.

**A third instance, found by going to look rather than by it breaking.** `gen_cast.gpu_busy`
asks `nvidia-smi` which processes hold a CUDA context. It returned a pid when the card was
busy and `None` otherwise — where *otherwise* silently covered **two different things**:
"asked the driver, nobody is there" and "could not ask the driver at all". The caller tests
`if other:`, so a missing, renamed, timed-out or erroring `nvidia-smi` read as **the card is
free**, and let through exactly the double-booking the guard exists to prevent — the one whose
own docstring records it costing several hours, a base frame going from 354s to 2014s, and a
CUDA failure that looks like dead hardware and is not.

> **"I asked and the answer was no" and "I could not ask" must not be the same return value.**
> Any probe of the outside world — a driver, a process table, a network, a file system — has
> three outcomes, and collapsing the last two into the falsy one makes the guard fail *open*
> at exactly the moment its subject is in an unusual state.

Now three outcomes: a pid, `None`, or `GPU_UNKNOWN`, with the unknown case warning loudly
instead of proceeding quietly. And note the shape of the fix, because it is its own trap:
**`GPU_UNKNOWN` is a non-empty string and therefore truthy**, so the caller must test for it
*before* the truthiness test or it reads as "busy, pid unknown" and refuses every run on a
machine without `nvidia-smi`. That is the same trap, one layer down, as a helper returning
`@('REFUSED: …', $false)` — a non-empty array — and being read as success.

### A CHECK THAT AVOIDS THE SUSPECT REGION IS A RUBBER STAMP. VERIFY IT CAN FAIL.

The sibling of the rule above, and the harder one to catch, because the check does measure
something — just never the thing it was written for. **Three instances now, in three
different tools, by three different authors**, which makes it a pattern rather than three
accidents:

- **The band check whose crops were cut on the seams.** The defect lives at the join; the
  sampler took its windows from the middle of each band and reported clean.
- **The contact sheet that was quietly halved.** A sheet assembled from resized tiles cannot
  show a one-pixel fault, and a round was lost to judging art off it.
- **A capture-guard self-test that passed `-Quiet`.** The guard leaked its diagnostic into
  its own return value and so returned `$true` on every failure (see below). The self-test
  written to catch exactly that passed `-Quiet`, which suppresses the diagnostic — the one
  code path that leaks — and duly passed against a module that had been **deliberately
  broken to make it fail**.

> **When you build a check, the first thing to verify is that it CAN fail, on the exact input
> it was written for.** Not on some input: on the one the check exists to reject. Break the
> thing on purpose, watch the check go red, put it back. A check that has never been seen to
> fail is a claim about the apparatus, and the three above all passed for as long as nobody
> asked them the question they were built to answer.

The corollary is about where you sample: **take the window from the place the fault would be**
— the seam, the edge, the margin, the error path — not from the comfortable middle. The
contact sheets in this project now re-read a tile back out of the finished sheet and assert it
is pixel-identical to the crop that went in; `verify_capture_guards.ps1` plants a probe file
and requires a non-zero exit before it will report a clean sweep.

### A FUNCTION THAT RETURNS A VALUE AND ALSO WRITES DIAGNOSTICS RETURNS BOTH

A PowerShell language rule rather than a mistake, which is exactly why it survives review: the
code reads correctly and behaves backwards.

`tools/SafeCapture.ps1` was written in response to a privacy incident — a screenshot that
captured the user's WhatsApp window. Its guard read:

```powershell
function _no($msg) { Write-Output "  capture guard: $msg"; return $false }
...
if (-not [SafeCaptureNative]::IsWindow($Handle)) { return (_no 'the handle is not a window') }
```

A function returns **everything it writes to the pipeline**, so `_no` returned the two-element
array `@('  capture guard: …', $false)` — and a non-empty array is `$true`. Every call of the
form `if (-not (Test-CaptureTarget …))` was therefore **false whenever the guard had something
to say, which is to say whenever it failed.** Ownership, minimised, and the re-check after the
copy were all inert. `Save-SafeWindowCapture` returned `@('REFUSED: …', $false)`, so
`if (-not (Save-SafeWindowCapture …)) { exit 1 }` could never exit 1 either.

> **The failure direction is the dangerous one: it returned SUCCESS exactly when it had a
> complaint.** A module written after an incident, that reads as careful, that was not wired
> up at all. Diagnostics from a function that returns a value must go to the host —
> `Write-Host`, `Write-Verbose`, `Write-Warning` — never `Write-Output`, and never a bare
> expression statement.

It cannot be ruled out that this contributed to the original incident: if the ownership check
had fired that day, it would have been ignored. `verify_capture_guards.ps1` now self-tests that
a failing guard returns a plain `$false`.

### A KNOB THAT CANNOT BE TURNED ANSWERS EVERY QUESTION WITH THE SAME PICTURE

A per-view override was added so a rebuild of 282 would not silently lose a setting:
`HAND_STRENGTH_VIEWS.get(cel["view"], args.hand_strength)`. It reads as a sensible default.
It is not: for view 282 the dict **always** wins, including over an explicit
`--hand-strength` on the command line.

The next thing done with that knob was a sweep — 0.56, 0.62, 0.68 — to find a setting between
two known-bad ones. All three returned **byte-identical images**, because all three were
rendered at 0.72. Had the three outputs merely *looked* similar rather than hashing the same,
the conclusion would have been "the strength does not matter in this range", which is false,
and it would have been supported by three pictures.

> **A parameter with a concrete default cannot be distinguished from one that was never
> passed**, so any per-target override written on top of it must win, and then it wins over
> the explicit value too. Default the flag to `None` and resolve explicitly: flag, then
> per-target, then default. And when a sweep returns results that are *too* consistent, hash
> the outputs before believing them.

Caught only because the derived numbers were identical to two decimal places across three
runs — which is the same instinct as §0a's: a measurement that is suspiciously clean is a
claim about the apparatus, not the subject.

### THE SAME LESSON AS THE OCCLUDED SHOE, ONE LIMB UP: A HAND THE SOURCE DOES NOT CONTAIN

282's walk is warped from one photograph, and in that photograph **both hands are in the
pockets**. Below the wrist the base leaves **324 px of skin on the right hand against 1256 on
the left** — four times less. Everything downstream was then asked to produce a hand from that
quarter.

What each setting does, looked at over magenta at 3x and at the cel's own 95-row scale:

| refinement | right hand | left hand |
|---|---|---|
| 0.50 | in the pocket | a good hand, watch on |
| 0.56 | in the pocket | a good hand, watch on |
| 0.62 | in the pocket | at the pocket, watch gone pale |
| 0.72 | **splayed spindly claw** | smooth free hand, no watch |
| 0.72 then 0.50 | **the same claw** | smooth free hand |

The refiner at 0.72 did not *add* mass — 324 px in, 326 px out. It reshaped the little that was
there into fingers. And a second, gentler pass could not settle it, because **a settling pass
cannot settle what is not a hand**: it preserved the invention exactly.

> **This is `warp_walk`'s occluded shoe, one limb up.** There the rigid region was sized on a
> shoe the source photographed half-hidden, and no region rule could recover it. Here the
> refiner is asked to draw a hand the source photographed inside a pocket, and no strength can
> recover that either. When a defect survives every knob on the stage you are tuning, stop
> tuning and ask whether **the information exists in the input at all** — and if it does not,
> the only real fix is upstream, in what was generated.

The honest consequence for this character: there is **no setting where the right hand is both
out of the pocket and hand-shaped**. The choice is between a hand that is hidden and a hand
that is wrong, and a hidden hand cannot look like a bad hand.

### WHEN THE MODEL REFUSES AN INSTRUCTION, CHECK WHETHER THE INSTRUCTION IS ANATOMICALLY TRUE

The init beats the prompt (§0). It does not beat anatomy.

282's hands come out in the pockets, and the documented lever for position is `HAND_OUT`,
which pushes the init's hand blob outboard of the trouser column. The target was derived from
geometry rather than taste: the hand ellipse sits 48.7 px from the thigh capsule's axis, and
clearing it needs `HAND_R + thigh radius = 0.117 × fig = 120 px`, so **0.0695 of figure
height**. Three values either side were generated — 0.050, 0.070, 0.090.

**The init moved and the model did not.** Dumped and looked at, the hand blobs go from
touching the trouser at 0.0 to standing in open backdrop at 0.070. The generated base was
unmoved: skin below the wrist 324 → 334 → 335 → 332 px, and skin lying lateral to the thigh
**zero at every value**.

`walkref.mp4` says why, and it is the measurement that should have come first. Across 1434
samples in four sets, a real walker's hand-to-hip lateral gap is:

    min 0.0033    MEDIAN 0.0201    max 0.1155    10th-90th 0.0067 .. 0.0471

and the arm merges into the torso in projection on **46–93% of sampled rows**. A real hand
does not hang clear of the thigh — it hangs *against* it, about 0.02 of figure height out,
touching in the picture half the time. So 0.0695 is **three and a half times the real median
and past the 90th percentile**.

> **The init was asking for a posture humans do not adopt, and the model — whose prior is
> photographs of humans — declined. The prior was right and the target was wrong.** When a
> generator ignores an instruction it is capable of following, the next question is not "how
> do I push harder" but "is the thing I am asking for true of the subject?" A number derived
> correctly from the *apparatus* (capsule radii) can still be wrong about the *world*.

It also rules out the adjacent lever before it is spent: holding the hands further from the
hips in the authored pose asks for the same wrong thing one stage earlier. **The hand is
already where a real hand is.** The defect is not *where* it is but that the model draws it
inserted into the pocket rather than resting beside it — and lateral displacement cannot
express that difference.

### A RECORD THAT IS NOT TRACKED IS NOT A RECORD

`tools/install_walk.py --override` ships a build the gate rejects, and writes down who
decided and why. The log was first put in `tools/samples/` next to this file — and `git add`
refused it, because `/tools/samples/*` is in `.gitignore`.

> **An untracked record of a deliberate exception is indistinguishable from the exception
> never having been decided.** The waiver is the entire value of an override; if it lives
> only on one disk, with no history, then what shipped and what was knowingly accepted are
> unrecoverable the moment that disk is cleaned.

It lives at `tools/INSTALL_OVERRIDES.md`, which is tracked. **This file is not**, and it is
the accumulated reasoning of the whole project in exactly one place.

And the matching rule for the instrument itself:

> **Override the decision, never the instrument.** There is a real case for shipping a build
> the gate rejects — when the defect it catches is smaller than the defect the build fixes.
> The wrong way to take it is to move the threshold until the build passes, because a ceiling
> raised to admit one known defect silently admits every other instance of it. Raising
> `FOOT_FRAG_MAX` for 282's sheared sole would also have stopped reporting the fragment on
> 280, which nobody had decided to accept. That is how a gate dies: not switched off, but
> tuned until it only ever agrees.

**Five parameter sweeps on the shoe went nowhere; one picture of the mask answered it in
minutes.** Three sweeps of the shirt's colour tolerances moved nothing; one picture painting
each pixel by *which gate rejected it* showed that saturation was discarding 53% of the
garment. The forearm cylinder had passed its own check and a dump of the skeleton on the
photograph showed the segment lying on the shirt rather than on the arm.

**And twice now a defect has been found ONLY by a dump, with every number green:**

* a **green hole punched through the raised trainer** — the fragment count moved 21 px to 48
  and a hole is not a fragment, so nothing flagged it;
* a **green streak up the fly** where the two leg layers abut — an interior hole, narrower
  than the inter-leg gap surrounding it, so every silhouette measure read it as the gap.

The transferable form, which applies well outside this project:

> **A metric answers the question it was written for. A defect that is a different SHAPE of
> thing passes it in silence.** A count of fragments cannot see a hole; a measure of a
> silhouette's extent cannot see an interior gap; a check on an input cannot see what the
> transform did to it. Before trusting any number, ask what the picture would look like if
> something *else* were wrong — and if you cannot answer, dump it and look.

### A guard outlives the premise it was written under, and nothing fails when it does

§7e's hand refiner gives up when the crop holds under 8% flesh. That was correct when it was
written: the model placed the hands where it liked, so an empty crop meant there was no hand
there to improve, and refining one anyway hallucinated a hand-shaped blob onto a hip.

Then the premise changed — the hand is now *supposed* to be at the authored wrist, and a crop
full of trouser is the defect itself rather than evidence of its absence. **The gate went on
firing, and it was now protecting the defect.** Worse, it fired asymmetrically: `hand_r`
measured 0.082 and passed, `hand_l` measured 0.065 and skipped, so **the hand that was more
deeply in the pocket was precisely the one the refiner refused to touch.** The gate was doing
most harm exactly where the problem was worst.

Nothing announced this. The gate did not start failing, the log line it printed was accurate,
and the comment above it was still true as written. The only thing that had changed was a
belief held somewhere else in the pipeline.

> **A check encodes an assumption about the world. When the assumption is retired, the check
> does not retire with it — it carries on being correct about a world that no longer exists.**
> Nothing fails, nothing warns, and the check's own comment still reads as justification.
> Whenever a design decision is reversed, the thing to search for is not the code that
> implemented it but the code that *defended* it.

Same shape as §1's negative prompt, where `watch` sat in the file in the right variable with
the right syntax and had been silently truncated away for a session; and as §0d, where
`hands in pockets` was a sentence in a comment that *measured* the pockets and worked around
them. Three ways for a thing to look handled while doing nothing.

### The init has TWO blurs, and four defects were blamed on the wrong one

**`init_field` blurs the colour palette by `soft = 18` and the island MASK by `blur = 62`.**
The mask is a *composite* mask between the init and the flat backdrop. So the rule this file
carried for four rounds — *the init cannot express a feature smaller than its 62px blur* —
was **wrong**, and it was used to explain the hand, the wristwatch, the shoe and the hair. An
18px blur expresses a hand perfectly well.

What actually happens is that **the mask fades at the figure's extremities, and where the
mask fades the backdrop shows through whatever the palette painted.** Measured down view
282's centre line:

```
torso centre              0.98     the init is essentially all the model sees
nose                      0.69
hair band                 0.33 - 0.62
hand, past the wrist      0.40 - 0.60
```

At the hair and the hands, between a third and two thirds of what the model was shown was
**flat green field**. It painted the strongest nearby signal — on a green backdrop, a green
cap — and the chroma key then removed the cap and the skull with it.

> **A palette feature only survives where the island mask is still opaque. Any feature at the
> figure's margin needs the island extended to cover it.**

That is narrower than the rule it replaces and it is actionable: extend the island where the
feature lives. It costs nothing elsewhere, where lowering the blur would sharpen every view's
silhouette against the backdrop for no benefit.

**The second half of a feature is SEPARATION.** With the island extended, the head had room
for hair and the model stopped drawing the sunglasses — because the hair band ran to 0.010
above the nose and the lens bar sits 0.005–0.031 above it, so the two overlapped and the 18px
blur merged them into one dark mass from crown to cheekbone. Raising the hairline to leave a
skin-coloured forehead between them brings the glasses back. And a mass has to be big enough
that **its centre survives near the colour it was painted**: at 43px the lens bar's surviving
value was 73 — mid grey — and the model matched that mid grey with dark frames and *clear*
lenses. Spectacles, not sunglasses. At 57px and a darker paint it survives at 48.

### A solved problem that was never generalised is worse than an unsolved one

The fix was **already in this file**, for the feet, with the reasoning written out:

> the authored ankle is the JOINT; the shoe is another 4cm below it … and if the island stops
> at the joint then the only thing under the ankle is flat backdrop - which is what the model
> then paints there

Someone found exactly this mechanism, fixed it for shoes, and wrote down why. It was never
applied to the head or to the hands, and we paid for it twice in one day — a green cap that
cost a skull, and months of a man with his hands in his pockets.

**The file read as though the ground was covered.** An unsolved problem at least looks like
one; a solved one that was never generalised looks like safety. The three places the island
now has to reach past a joint are **the ankles, the head, and the wrists** — and the next
feature at a margin will need it too.

### Two corollaries, each earned twice

**If the probe is positioned by the thing you are varying, the experiment measures the probe.**
Sweeping the init's hand-capsule length put the probe at a fixed fraction *of that length*, so
a longer capsule always moved the sample point further from the body and always scored lower —
the sweep could not have produced any other answer whatever the truth was. The tell was that
`HAND_LEN 0` scored the highest number in the table. §7k is the first instance: the bones
blamed for the hip lobe were arm bones, and the experiment that "confirmed" it could only ever
have confirmed it. **Before running a sweep, ask whether the instrument moves with the
variable. If it does, fix the instrument first.**

**If the numbers did not move, check the HASH before concluding the change did not land.**
Reordering the init's draw calls produced a frame whose screen checks were identical in every
field — coverage 22.0%, crown 57, head width 0.087, shoulders 0.265, the same single failure.
On "byte-identical output means a change that did not land" that fix was one step from being
reverted as ineffective. **It had landed**; the thigh capsule was painting over the hand, and
the file hashes differed. The screen's numbers are coarse and a real change can leave all of
them unmoved. This is the same family as the stale-`testhost` trap, where identical bytes meant
a build that never happened — in both cases the cheap check is the hash, not the metric.

Two corollaries that have each cost a round here:

1. **Look at it at 100% or magnified, never scaled to fit a sheet.** Four rounds went past
   with a visible defect because 468-pixel figures were being reviewed at 28%.
2. **Check the reference can physically SHOW the quantity** before measuring it. §7i spent a
   round on a foot-clearance number read off a clip in which the feet are below the frame, the
   mask's bottom is cast shadow, and the swing foot is occluded for exactly the phase it is
   highest. The segmentation worked perfectly; the information was never in the picture.

## 0b. GENERATION RULE: a warp source is shot at the MIDDLE of every motion

Binding on everything generated from here, and it governs anything warped from a stills
source rather than this cycle in particular.

> **A source frame for a warp-based cycle is generated at the MIDDLE of every motion it will
> be warped through, never at an extreme.** An extreme halves the usable range for free: one
> end of the cycle costs nothing and the other costs double.

It is free at generation time and unrecoverable afterwards without re-running the GPU, which
makes it the cheapest decision available. **All 28 ungenerated cels — views 283, 287, 293–297,
the whole of body3 — are generated this way.**

Why it matters, measured (§7i): there is a limit of about **55 px of forearm travel, 0.054 of
figure height**, beyond which a warp of a photograph of a man standing still has no information
about how the cloth falls beside the limb, and the shirt tears into a lobe over the hip. View
282's source was shot at a swing EXTREME, so its four cels need 26.9 / 61.7 / 69.1 / 44.4 px
and two of them are over the limit. The same cycle against a mid-swing source needs
**37.4 / 32.3 / 20.9 / 47.8 — maximum 47.8, all four inside it.**

### And it does not cost identity, which was the thing to check first

If a different source pose produced a different-looking person, adopting this for the 28 while
282's cast stays as shot would put two different men on the board for one character — a far
worse defect than one hem. **It does not.** Identity is pinned by one seed per character
(`CAST[...]["seed"]`, 704311 for body0) with no IP-adapter and no face reference, and the
evidence was already on disk: `cast-final` holds four generations of view 282 at the same seed,
same prompt and **four different authored poses**. Measured across them, as median band colour:

```
hair     6 levels spread      face   11        <- the same man
torso   10                    legs   30
feet   164                                     <- the trainers, already flagged in §7a
height  1.2%                  median width 7.6%
```

At 1:1 the four faces are one person — same beard, same hairline, same sunglasses. **What does
not survive a pose change is GARMENT DETAIL**, which is §7d's finding and the entire reason the
warp exists; and since only one frame per view is generated now, garment detail only has to be
self-consistent *within* a view, which it is by construction.

body3 has nothing generated at all, so its four views at mid-swing are self-consistent with
each other. The remaining ungenerated views are the `+3` undressed tiers, which §9 already
decided keep the 1990 art.

## 0c. A reference drawn from the population you are testing cannot see a defect it shares

Both halves of this were observed inside a single experiment, which is why it is stated as a
rule rather than a caveat.

`--shirt-check` compares each warped cel's rigid plate against a reference. With **cel 0** as
the reference it went green the moment the defect moved *onto* cel 0 — winding the arm swing to
zero took cel 0's forearm travel from 43.8 px to 73.6 and put the lobe there, and the other
three were then scored against a broken yardstick and read 0.98–0.99. §7a had already recorded
exactly this for the drift checker. Switching to the **loop median** then went green when zero
swing spread a milder lobe across all four, because a median cannot see what the whole
population shares.

The fix is not a better statistic. It is a reference from **outside the population**: the
source photograph's own plate, which is what "rigid" is defined against in the first place.
`--shirt-check --src <source cel>` does that.

**And it was not a wash.** With the correct reference, cel 1 passes at 1.03 — it had been
failing a bad yardstick for three rounds, and two of those were spent chasing it.

## 0c-bis. A CALIBRATION IS A PROPERTY OF THE SUBJECT, NOT A CONSTANT

Same shape as 0c, from the other end: there the yardstick was drawn from the population,
here it was drawn from a DIFFERENT population and reused after the subject changed.

The board's missing middle has no colour words in its prompt - the house rule - so the
init's colours are the only colour instruction there is, and the question was how much of
an authored palette survives a generation. It was measured: init saturation 0.283 ->
output 0.309, a transfer of **x1.09**. The model *added* chroma.

The roofs were then varied - slate, lead and flat asphalt over about a quarter of the
region, where one saturated maroon had been - and the next lift was computed with that
same x1.09. **It missed by a mile, and in the wrong direction.** Re-measured on the new
init the transfer is **x0.864**: with greys in it the model *removes* chroma.

Nothing was wrong with the first measurement. It was correct for the frame it was taken
on, and that frame no longer existed.

**The rule: a measured transfer, ratio or correction factor is valid for the content it
was measured on. Change the subject and it is stale. Re-measure it in the same pass that
changes the subject, or the prediction will be confidently wrong.**

And the corollary that saved the round: **when a prediction misses, scan the lever before
turning it again.** Sweeping the saturation multiplier across 1.00-1.70 and reading off
the predicted output showed the target was unreachable at *any* setting - 0.317 against a
0.340 target at k=1.70 - which turned four more generations into one measurement and a
decision. The scan could not make the last call: k=1.50 reached 81% of her ring and put
the pantile at 0xEF500B, traffic cone rather than roof tile. **A scan narrows the choice;
an eye makes it.**

## 0c-ter. A CORRECTION FITTED ACROSS AN OVERLAP MUST FIT INSIDE THAT OVERLAP

A region too big for the card is generated in tiles that overlap, and each tile is pulled
back onto its neighbours by a correction estimated from a neighbourhood - a blur, a local
mean, a rolling window, a fitted gradient. **Any such estimate is unreliable within its own
radius of a boundary**, because that is where the data it needs stops existing and the
implementation clamps.

So if the correction's radius is wider than the overlap, **it is least accurate exactly at
the join, which is the only place it existed to help.** The town centre's low-frequency
match used sigma = min(tile)/8, about 100 px, against an overlap of 72. It changed the
measured seams by nothing at all - 8.08/10.83/10.52 before, the same after - and read as a
working correction because it printed a non-zero number per tile.

Narrow the kernel to fit inside the overlap, or widen the overlap to contain the kernel.
Both, here: sigma to 2 cel px and the overlap from 6 to 10, which halved the worst seam and
cost 117k pixels on the largest tile against a hard 810k ceiling.

**And the fix that actually closed it, which is upstream of all of the above:** tiles that
overlap must denoise from the SAME randomness in the overlap. Sample one latent noise
tensor for the whole image and give each tile an aligned crop of it, and neighbours solve
the same problem from the same start - they agree where they meet without anything being
blended, feathered or corrected. Two of three seams became indistinguishable from their
neighbours and the third fell inside its own stripe's variation.

A latent pixel is eight image pixels, so **every tile's origin AND size must be a multiple
of 8** or the crop is not aligned and the field is not shared. Pad the canvas to a multiple
of 8 and crop back at the end. In diffusers 0.31 the hook is `randn_tensor` in the
pipeline's own module, called once by `prepare_latents`; match on the tensor's trailing
dimensions and fall through to the original for everything else.

**And the corollary, which is the one that cost the GPU time:** before fixing a seam, check
whether it scales with the thing you think causes it. Halving the denoise strength moved
these seams by a tenth, which said the texture was not the cause - and that one measurement
would have been cheaper than the two full passes spent assuming it was.

## 0d. A term you believe is active may be a COMMENT. Twice in one day.

**Grepping the file is not evidence that the model was asked. Decode the tokeniser's output
and look for the word.**

Today's source frame came back with the man's hands in his pockets, and the obvious question
was why the negative prompt had not stopped it. It had not stopped it because **there was no
such term**. `gen_cast.py` contained the string "in a pocket" exactly once, in the comment
above the hand refiner's skip test:

> the crop is taken at the AUTHORED wrist, but the model puts the hands where it likes — in a
> pocket, behind the back, tucked into a jacket — so the crop frequently lands on plain cloth

That comment *measures* the pockets (four of six hand crops under 8% skin), *works around*
them, and never once objects to them. Anyone searching the file for "pocket" finds a careful,
numerate paragraph on the subject and concludes it is handled. Nothing was ever asked.

**The workaround is what hid it.** The refiner's skip test made the symptom tolerable — no
hallucinated hand-blob on a jacket — so the underlying behaviour stopped being annoying enough
to fix, and the comment explaining the workaround read, at a glance, like a fix.

This is the **second** time in one day a term believed active turned out to be prose. The
first was §1: twelve terms sat in the source file, in the correct syntax, in the right
variable, and were being discarded by CLIP's 77-token window before they reached the model —
`watch` among them, which is why that same source frame also came back wearing two wristwatches.

Three different ways for a term to be inert, all of which look identical when you read the
source:

| How it fails | Looks like | Found by |
|---|---|---|
| It is in a comment | a handled concern | decoding, then searching the decode |
| It is past token 77 | a correct prompt string | decoding, then searching the decode |
| It is in the string but does not fire | a correct prompt string | the picture |

The third is real too, and it paid for this round's tokens: `jewellery` was inside the window
and reaching the model while the figure wore two wristwatches. All three are invisible to
`grep` and the first two are invisible to a token *count*, which is why `check_guards` now
decodes and searches for named terms (`PINNED`) rather than only counting.

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
| Brown shoes | "white trainers" | (84,52,51) dark brown | **BLACK** trainers, white sole |

The sunglasses row is the cleanest demonstration, because it was fixed by moving the *same*
instruction from one place to the other: "dark sunglasses" in the prompt did nothing, and a
dark bar painted across the eye line in the init produced them immediately.

**The shoe row is the strictest test this rule has had, and it is the reason to trust it.**
Every previous row can be waved away as a term that never reached the model — §1 showed that
half of them genuinely did not. This one cannot. "white trainers" was verified *in the decode*,
at token 71 of 74, inside the window, before the frame was generated. The model was definitely
asked. The init was still painting the measured brown, and what came back was a **black** shoe
with a white sole: the init set the value, and the prompt was left decorating the trim.

That is the useful form of the rule. The prompt did not lose outright — it moved the sole. It
lost **on what the object is**, which is the half the init owns.

### A prompt term that reached the model and did nothing is not evidence against the term

Three of the four terms pinned for this run were verified in the decode and three failed in
the pixels: the shoes above, the hands still in the pockets, and a watch-shaped blob still on
the left wrist. **None of those is a reason to delete the term**, and the distinction matters
because the obvious next move — "it didn't work, take it out and try different words" — is the
move that wastes the day. The term is now doing the only job a prompt term can do: it is no
longer *fighting* the init while the init is corrected. `watch` sat in the file for a whole
session without reaching the model at all (§1), so this is the first frame on which it has
actually been asked for, and the first on which its failure means anything.

**What to do with each one is decided by which half the defect lives in:**

| Defect | Whose half | Lever |
|---|---|---|
| V-neck | shape at the neckline | init already pins the neckline — fixed, came out crew |
| Shoe colour | what the object is | `SHOE_INIT`, an init override |
| Hands in pockets | where the limb ENDS | the init's forearm capsule stops at the wrist |
| Wristwatch | a small detail on skin | the only one the prompt can plausibly own |

> **THE WATCH REQUIREMENT HAS SINCE BEEN WITHDRAWN BY THE PERSON WHO SET IT.** After seeing
> the build that removed it, Zain said: *"I don't care about the watch, The hand just needs to
> look like an actual hand!"* The term stays in the negative — it is reaching the model and
> removing it is a change nobody asked for — but it is **not a constraint to satisfy any
> more**, and in particular it is not a reason to raise the hand refinement strength. The
> withdrawal was given *after* seeing what beating the watch cost, which is what makes it
> binding rather than a preference. A pin recorded in a file outlives the reason it was set;
> check who still wants it before paying for it again.

> **AND `hands in pockets` IS WITHDRAWN FOR 282 TOO, WHICH IS THE SHARPER CASE** — because it
> was never the player's pin. It was added after he said the figure read as standing still
> with identical arms, and *that* complaint was about **both** hands buried and no arm motion
> at all. The installed build keeps one arm free and properly formed and tucks only the other,
> so the thing the pin existed to prevent does not come back.
>
> **The reason a pin was added matters more than the pin.** Read as a bare rule, "hands in
> pockets" forbids the only build on this character with no malformed hand in it. Read as the
> complaint it came from, it is already satisfied. Both of this outfit's pins outlived their
> reasons inside a day — which is the whole argument for writing the reason beside every rule
> rather than the rule alone. A rule without its reason cannot be retired, only obeyed.

The hands row is worth stating plainly, because it is not a model whim: the init paints the
forearm as a skin capsule from elbow **to the wrist keypoint and no further**, and at mid-swing
the wrist sits against the hip. So the init draws skin that stops dead at the trouser column —
which is a picture of a hand going into a pocket. **The init was asking for the very thing the
negative prompt was asking against.** Until the capsule runs on past the wrist by a hand's
length, the prompt is arguing with the picture and the picture wins.

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

### And there is a THIRD place to stand, for a detail below the init's reach

The wristwatch was finally beaten, and not by the prompt or the init.

The state it was beaten from: 282's source had to be regenerated with the feet further apart
(§0b-bis), and **every** regenerated stance — 0.107, 0.123 and 0.137 of figure height, all at
the one pinned seed — put a black watch on the wrist. `watch` was verified in the decoded
negative, present for the base pass *and* the hand-refinement pass, and drawn anyway. Three
poses at one seed is not an unlucky draw: a man standing with his arms hanging straight down
against a plain backdrop **is a stock product photograph**, and that prior brings a watch with
it.

The init could not argue. It already paints that wrist as **bare skin** — which is the
strongest statement it can make, and the watch is drawn *on top of* it. `SHOE_INIT` wins a
shoe because a shoe is a large uniform mass the init dictates; a 40-pixel dark band on a limb
the init is already colouring correctly is below the scale at which the init says anything at
all.

**The refinement pass is the third lever, and it is the one that operates at that scale.** The
watch sat inside the hand crop the refiner was already working on, and it survived — because
at the default `--hand-strength 0.50` the refiner *preserves* what it is handed. Raising it to
**0.72** removed the watch from both wrists and took both hands out of the pockets with it, in
one run.

> **A prompt names what the picture should contain; an init dictates masses it can resolve; a
> refinement pass is the only one of the three that can argue about a detail smaller than the
> init's own brush — and only if it is given enough strength to redraw rather than preserve.**
> When a term is verified present and the defect is small, do not conclude the term is
> hopeless: ask which stage is capable of seeing something that size.

The price is the one §7e already named and accepted: at 0.72 the fingers come out soft and
indistinct. **A hand with vague fingers and no watch beats a crisp hand wearing the thing the
player explicitly excluded** — the defect that was named is the defect that matters.

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
282 survived because nothing ever asked for it to go; so did the studio light stand.

**§0d is the sequel to this one** — a term can also be inert because it is sitting in a
*comment*, which `grep` cannot distinguish from a prompt string and a token *count* cannot
see at all. Both failures happened in one day. The check now decodes and searches for named
terms (`PINNED` in `gen_cast.py`), because counting tokens never proved a word arrived.

This also settles three arguments about what actually fixed things:

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

## 7j. The near leg now covers the far one, and it fixed a second fault nobody had attributed

A single backward field maps every destination pixel exactly once, so it **cannot express one
thing being in front of another**. That is not a tuning problem and no pose work reaches it.

Measured the same way the reference stride was. A real walk, front view: **18% of frames show
the two shins as a single ink run with no gap, and in a further 40% one leg is a 4–5 px sliver
beside the other's 8–21 px** — about 70% of the far leg hidden, alternating every half-stride
because the leading leg is the nearer one. Our shipped cels, at three heights:

```
                       one run     two runs    one a sliver
  0.72 of the figure        0%         100%             0%
  0.80                      0%         100%             0%
  0.88                      0%         100%             0%
```

**Two runs of near-equal width in sixteen frames of sixteen, at every height.** Our silhouette
is wrong for well over half the cycle.

### Three layers, each with its own field

```
body      every joint moving, both legs erased from the source
far leg   the NEAR leg's joints frozen at their source positions
near leg  the FAR leg's joints frozen
```

Freezing the other leg's joints is what makes each leg independent of the limb it is supposed
to be independent of; without it both layers inherit the same scissor and the occlusion is
between two copies of one motion.

**Which leg is near is a cycle fact, not a pixel fact**, and it is read as one: the nearer
foot is the one whose destination ankle is LOWER, because §7ac established that with the
camera above the ground plane nearer appears lower, and the ±0.010 depth offset in `CYCLE` is
exactly what puts it there. Same discipline as the foot flex taking its weight from the
authored shin rather than from the image.

**The two legs split by geometry, not colour** — the opposite of the shoe and the shirt, and
for a stated reason: both trouser legs are the same cloth, so colour has nothing to separate,
while unlike a shirt and an arm the two legs genuinely *are* separated by their own bones.
Nearest hip-knee-ankle chain. The split through the crotch is arbitrary and harmless, because
both sides of it belong to the figure and the layers are composited back together.

### The hole that was expected did not appear

The scoped risk was the hole behind the near leg — pixels that were never photographed. There
is none, and the reason is worth recording so nobody budgets for it again: **in the source
photograph both legs are fully visible and separate**, so each leg's layer is warped from
complete pixels of its own, and the far leg is merely *covered* rather than needing to be
invented. The mirroring that was planned as the fallback was not needed.

### And it fixed the leg separation, which had been measured and not explained

Before occlusion, the rendered ankle separation against what the skeleton asks:

```
cel 0   skeleton 48 px   rendered 60   1.25x
cel 1            70              70    1.00x
cel 2            48              67    1.40x
cel 3            70              70    1.01x
```

Faithful on the UP cels, **25–40% too wide on the two DOWN cels** — which are exactly the cels
where the skeleton asks for the legs to overlap (48 px of separation against legs 50–57 px
wide). The warp was pushing them apart precisely where they should have touched, so there was
nothing to occlude and the defect looked like a cycle problem.

The cause was the blend between the two legs' bones: each leg was being dragged outward by the
other's scissor. Giving each leg its own field removes it. Cels 0 and 2 now read as **one run**
at 0.88 (103 px and 119 px) where they were two with a 10 px gap.

**Two faults, one cause, and the measurement that separated them was comparing the render
against the skeleton rather than against taste.** A number that says "the legs are too far
apart" is a cycle complaint; a number that says "the render is 1.4x what the skeleton asked"
is a warp bug, and only the second one is actionable.

### Checked at 4x before anything was believed

The green hole of §7i was found by a dump and by nothing else, so the overlap was dumped
before any metric was trusted. Where the near leg crosses the far one there is a clean dark
silhouette edge — a real edge, which §7h's principle says is invisible — with no seam, no
sliver of background between legs that should touch, and no hole at the hip where the three
layers meet.

Cost: three fields per cel instead of one, so a four-cel view takes about seven minutes
instead of two and a half. `--no-occlude` restores the single-field frame.

## 7k. A projected limb cannot be longer than the limb — and the guard that missed it

A player, twice: **"The mans shirt seems to be part of his arm!!!!, looks really stupid."**
It came back. On three cels of four the shirt sagged into a bell over the hip, the hem traced
an S-curve, the forearm beside it was a thin ribbon, and **the hand was gone**. Cel 0 was
clean — which is the whole clue, because the wardrobe line and the source photograph are
identical across all four, so whatever is wrong is a function of the POSE.

### Why `--shirt-check` read green over it

**Because it runs `shirt_mask` on the SOURCE photograph and never opens a warped cel.** It
asks whether the mask covers the garment at the point of DEFINITION, and the mask is the same
for every cel; cel 0 is the one cel that is essentially the source. A defect that varies with
pose is invisible to it by construction.

That is the fourth instance of one shape in this file, and the shape is worth naming once:
**§1**'s CLIP guard checked one of two identical inputs; **§8**'s GPU lock guarded one tool
against itself; **§7h**'s forearm probe was positioned by the same wrong assumption as the fix
it was validating; and this one tested the input instead of the output. Every time, the check
was not aimed at the thing that varies.

### The check now tests the plate at the point of use

A rigid motion preserves area and does not thin a limb, so on each **warped** cel:

```
             plate area / source     forearm, narrowest band
  cel 0            1.02                     33, 34
  cel 1            1.06                     11, 33
  cel 2            1.11                     11, 37
  cel 3            1.04                     15, 33
  (source)         1.00                     38, 36
```

Both rank the cels 0 < 3 < 1 < 2 — exactly the order a human ranked them by eye — and both
pass cel 0. `--shirt-check` now fails on cels 1–3 and says why.

**The probe had to be fixed twice more before it was trustworthy**, and both are the same
lesson one level down. It placed the forearm band from the AUTHORED pose while the renderer
draws from a clamped one, so it looked where the arm is no longer drawn; and it took the
MINIMUM width along the band, which is wherever the shirt happens to cross the arm, and so
cried wolf at 39% on a frame with a normal arm and a hand on it. Point of use means the pose
at the point of use, and "is this a ribbon" is a question about the limb, not its thinnest row.

### The cause: the cycle authors an impossible arm

Every bone's destination length against its source length:

```
bone        cel 0   cel 1   cel 2   cel 3
R forearm   1.01x   1.15x   1.32x   1.06x
L forearm   1.02x   1.18x   1.36x   1.08x
upper arms  1.01x   1.06x   1.04x   1.05x
legs        0.61x   1.01x   1.02x   0.99x
```

**The forearms are stretched by up to 36%.** `across` is 1.0, so a limb keeps its WIDTH while
its length is free — which is exactly what makes a foreshortened shin read as a bent knee
(§7aa). Applied to a bone that gets LONGER it does the reverse: the limb is drawn out into a
ribbon at constant width, the hand on the end is smeared off it, and the cloth beside it is
dragged into the bell. **The shirt was never the fault; it was downstream of the arm.**

And a ratio above 1.0 is not a tuning question, it is **impossible**. The source photograph
has the arms hanging straight down, perpendicular to the view, so each bone is already at its
true length there, and the projection of a rigid segment can only ever be SHORTER. The cycle
produces it because the wrist is placed by an x and a y amplitude independently rather than on
an arc of fixed radius about the elbow.

`clamp_limbs` caps every bone at its source length, keeping direction and walking the chain
outward so a clamped elbow carries its wrist. The honest fix is in `pose_author.CYCLE`; the
clamp is in the renderer because the renderer should not draw an impossible limb whatever it
is handed. **Result: the ribbon and the missing hand are gone on all four cels**, and the
plate's area drops from 1.11 to 1.07 on the worst cel.

### Closed for good: the shoe's smear rule does not work on a garment

Three attempts, three carved shirts:

```
w < 0.5             the outer half of the FEATHER BAND - cloth inside the garment
~own                strictly outside the plate
~dilate(own, 9)     only cloth well clear of the plate
```

The third had no excuse left and still cut a ragged green gash down the flank on three cels.
The rule works for a shoe because the region it protects is small, compact and surrounded by
background; a garment's edge is long, soft and adjacent to an arm, so any test of the form
"outside the plate and shirt-coloured" keeps finding the garment's own antialiased rim. The
residual +7% on cel 2 is accepted rather than chased.

### The real fix: the arm swings on an arc

`clamp_limbs` stops the renderer drawing an impossible limb, which is worth having on its own
and will be worth having again the next time a cycle is authored. But the pose should not be
impossible in the first place, and `pose_author.skeleton` was placing each elbow and wrist by
**independent x and y amplitudes**. The directions were always right; nothing held the length.
Specifically the elbow's vertical term carried `abs(arm)` while the wrist's carried the signed
`arm`, so on the half of the cycle where the sign is negative the elbow rose while the wrist
dropped and the forearm grew 0.027 of figure height — 22% — before the horizontal terms were
even counted.

The joints are now placed exactly as before and then pulled back onto arcs of the canonical
radius about their parent, with the chain carried so a corrected elbow moves its wrist. The
radii come from the same expressions with the swing set to zero, so they are functions of the
build and the proportion table rather than numbers typed in.

```
                 before (dst / src)              after
R forearm   1.01x 1.15x 1.32x 1.06x      constant in every cel
L forearm   1.02x 1.18x 1.36x 1.08x      constant in every cel
```

**Scope, checked rather than asserted**: across all 80 authored cels, the only joints that
moved are the elbows (8.9 px) and the wrists (23 px). Nose, neck, shoulders, hips, knees and
ankles are bit-identical, so every leg, bob, sway and depth decision in this file still stands.

### And the hip lobe was the forearm too — but its WEIGHT, not its length

The arc fix made the arm bones constant length in every cel and **the hip lobe did not move**:
cel 2's plate area stayed at +7%. So the arm's length was never what caused it, and the
obvious next suspect was the hip bones, which diverge below the plate as the cycle requires.

**Dumped before theorising, and the dump replaced the hypothesis.** Painting every destination
pixel of cel 2 by the bone with the largest weight in the skinned field — the instrument that
found the 23.6%/12.6% ribcage asymmetry in §7f — shows the **LEFT FOREARM** owning a rounded
lobe spreading across the hip and the shirt's lower flank:

```
band below the plate and outboard of centre, cel 2
   L forearm   45.2%
   L thigh     41.0%
   L pelvis    13.7%
```

That lobe *is* the hem swinging down over the jeans. The hips are 14%.

The mechanism is the same one §7h solved one storey up. Weight falls as `1/(d²+1)^alpha`, so a
bone keeps a say over everything nearer to it than any other bone — and a hanging forearm is
simply closer to the hip than the thigh or the pelvis is. The rigid plate answered it for the
ribcage; **below the plate nothing answered it at all.**

And the forearm is not only the nearest bone there, it is by far the largest *motion*. How far
each candidate's own endpoints travel between source and destination:

```
            cel 0   cel 1   cel 2   cel 3
L forearm    43.8    60.6    67.3    26.8 px
hips         21.1    17.0    21.1    16.9
```

Three times the hips on cel 2. So capping its reach — at 0.050 of figure height, three times
the forearm's own measured half-width of 0.0166 and still far short of the hip, with a smooth
falloff — looked like the answer twice over.

### IT WAS NOT. The cap changed nothing, and that is the finding

Built, run, measured: **cel 2's plate area stayed at 1.07 and the 1:1 crop is unchanged.** The
hem still swings down over the hip.

So **dominance is not responsibility.** The dominant-bone dump correctly says the forearm owns
45% of that band, and the displacement table correctly says it moves three times as far as
anything else there — and removing its influence entirely moves the result by nothing, because
the bones that take over move that cloth the same way. Both numbers were true and neither was
the cause. It is §0a's rule turned on a diagnostic instead of a defect: *a metric answers the
question it was written for*, and "which bone is nearest" and "which bone moves furthest" are
both different questions from "which bone put the cloth there".

`arm_reach` is left in and **defaults to 0**, because a mechanism that was built, measured and
found not to work is worth more as a closed door than as a deleted one.

**Three mechanisms have now failed on this lobe** — the arc fix (correct on its own terms, no
effect here), the smear rule (three variants, three carved shirts) and the forearm reach cap.

### What the two tables say when they are put side by side

```
             forearm travel      plate area
  cel 0          43.8 px            1.00   clean
  cel 3          26.8               1.02   clean
  cel 1          60.6               1.03   FAIL
  cel 2          67.3               1.07   FAIL
```

**The clean cels are the ones under ~45 px and the failing ones are over ~60.** That is the
only reading that *separates* the cels rather than ranking them, and it says what the three
failed mechanisms were all missing: there is an amount of relative movement beyond which a
warp of a photograph of a man standing still simply has no information about how the cloth
falls beside the limb. Below it, it has enough. No downstream rule can supply what the
photograph does not contain.

### And the lever is the SOURCE POSE, not the swing amplitude

The obvious response is to wind the swing down. **It does not work, and why it does not is the
transferable part.** Travel is measured against the pose the PHOTOGRAPH was shot in, and that
pose — `poses-walk5` cel 0 — is a DOWN key with its arms at a swing EXTREME. So every cel at
the opposite extreme is *two* amplitudes away, and reducing the swing just drags the other end
of the cycle away from the source instead:

```
  arm scale   per-cel forearm travel          max
     1.00     44.4  61.7  69.1  26.9         69.1
     0.50     58.6  56.6  57.3  31.9         58.6
     0.00     73.6  51.3  47.2  36.9         73.6
```

The floor is about 58 px at any amplitude. But the same cycle measured against a source shot
at the MIDDLE of the swing:

```
  as shot (arm +0.034)    44.4  61.7  69.1  26.9    max 69.1
  if shot at arm 0        37.4  32.3  20.9  47.8    max 47.8
```

**Three of the four cels drop inside the clean band and the maximum falls from 69 to 48.**

> **A source frame for a warp-based cycle must be generated at the MIDDLE of every motion it
> will be warped through, never at an extreme.** An extreme halves the usable range for free:
> one end of the cycle costs nothing and the other costs double. This is the single cheapest
> thing to get right about generating a cast for this pipeline, it costs nothing at generation
> time, and it cannot be recovered afterwards without re-running the GPU.

### Where it actually breaks, measured by walking the swing down

Three builds, twelve cels, plate area against the SOURCE photograph's own plate:

```
  travel (px)   26.9  31.9  36.9  44.4  47.2  51.3 | 56.6  57.3  58.6  61.7  69.1  73.6
  plate area    1.02  1.02  1.03  1.00  1.03  1.02 | 1.03  1.05  1.02  1.03  1.07  1.04
```

Everything at or under **51 px** is 1.00–1.03 and clean in the 1:1 crop. The two worst areas,
1.05 and 1.07, sit at 57.3 and 69.1, and the lobe is plainly visible on both. **The limit is
about 55 px of forearm travel — 0.054 of figure height.**

The prediction that established the mechanism is worth stating separately, because it
*distinguishes* this explanation from the alternatives rather than merely fitting it: wound to
zero swing, cel 0's travel rises from 43.8 px to 73.6 and cels 1–3 fall to 36–51. **The lobe
moved onto cel 0 and off the others.** It follows the travel, not the cel.

### And why no amplitude fixes it

The achievable floor is **58.6 px**, at half swing — and 58.6 is on the wrong side of a limit
of ~55, by a few pixels. So there is no arm-swing amplitude that clears it with this source
frame, and winding it down costs something real for nothing: §7b measured the 1990 art's arm
swing at 0.032–0.086 of figure height and ours is authored at 0.034, already the bottom of
that range, so half swing is 0.017 — less than the original's own arm movement, on an
animation a player has twice rejected for not reading as a walk.

**So the cycle is left as authored.** `pose_author --arm-scale` stays as a knob with the walk
recorded above, because the next person authoring against a still photograph will need to do
the same walk with different numbers.

The lever is the source pose, and it is unreachable without the GPU: regenerating 282's
photograph at mid-swing is hours plus a new figure to re-verify. **For the 28 cels never
generated — views 283, 287, 293–297, all of body3 — it costs nothing and must be done.**

### And one defect the occlusion work introduced

A thin green streak up the fly on cels 1 and 3: the two leg masks abut along the crotch and
each layer is warped by its own field, so where the fields diverge the background shows
through. Found by cropping the torso at 4x — no silhouette measure sees it, because the hole
is interior and narrower than the inter-leg gap surrounding it. The masks now overlap by
0.006 of figure height so the layers share a strip instead of abutting, and the near leg is
drawn last so the strip shows what it should.

## 7l. Arms swing FORE AND AFT, and this cycle swung them sideways

A player, after four rounds of us chasing the shirt: **"THE SHIRT IS NOT ATTACHED TO THE ARM!,
and arms don't sway left and right when a human walks, how can you not know how a human
walks!???"**

He is right, and it explains the entire thread. Human arm swing is **sagittal** — forward and
backward. `pose_author.skeleton` placed the elbow and wrist by x and y amplitudes in the image
plane, which is a **lateral** swing. That is not a tuning error, it is the wrong motion, and it
accounts for every symptom chased since: 67 px of lateral travel, cloth dragged sideways into a
lobe, a hand ending up where no walking hand goes, and the "wriggling" of §7b.

In a front view a sagittal swing is almost entirely DEPTH, so it shows as the limb
**foreshortening** and as the arm separating from or tucking behind the hip. It does not show
as the hand moving sideways.

### Measured off the reference footage

`walkref.mp4`, a man walking toward a fixed camera — the same view we author. Over one stride,
as fractions of figure height (`walkref/armmeas.py`; measurements only, nothing copied in):

```
                                        REFERENCE          OURS (authored)
hand's LATERAL half-width at the wrist   0.0139 - 0.0160    0.068 peak-to-peak
PROJECTED arm length, shoulder to hand   0.0588 - 0.0660    0.000
```

**Almost exactly inverted.** Four times too much of the motion that barely happens, and none
at all of the motion that carries the swing.

What the clip cannot show, stated rather than estimated (§0a): the **elbow** is not a landmark
in a silhouette and this subject is shirtless, so no colour boundary marks it and the FOREARM's
projected length is not recoverable. Shoulder-to-hand is reported instead, which is the
quantity the chain actually needs.

### And the arc fix had cemented it

§7k pulled each joint onto an arc of the canonical radius about its parent, to stop the forearm
stretching. It worked — 128.0 px in every cel — and that was the error. **A limb swinging
toward or away from the camera MUST foreshorten**; its projected length is the true length
times the cosine of the swing angle, equal to the true length only at mid-swing. Holding the
projection constant removes the one degree of freedom a front-facing sagittal swing uses and
leaves the arm nowhere to go but sideways.

`warp_walk.clamp_limbs` stays, because a projection can never *exceed* the true length. The
fixed-radius arc goes, because the pose must now actively shorten it.

### What is authored now

`arm` in `CYCLE` is a PHASE in [-1, 1], not a displacement, driving three measured constants:

```
ARM_SWING_DEG  35.0    cos(theta) = 0.329/0.388 and 0.294/0.360 in the footage -> 32 and 35
ARM_LATERAL    0.008   half the measured peak-to-peak lateral range of 0.016
ARM_DEPTH      0.003   the feet's depth cue (§7ac) scaled by the hand's height off the ground
```

Realised against the reference:

```
                      before    after    reference
lateral range         0.0666    0.0130   0.0160
projected arm range   0.0038    0.0444   0.0660
```

The foreshortening is still short of the footage because our four cels sample phases ±1.0 and
∓0.30, so the "mid" key is at 10.5° rather than at true mid-swing; the relative range realised
is 0.16 against the footage's 0.152–0.183, inside the measured band.

### And the garment comes off the arm entirely

Zain's first clause is a rig statement, and §7h had already proved it without anyone noticing:
**the sleeve hem is at 54–62% of shoulder-to-elbow, so below the elbow there is no cloth on the
arm in any pose.** A garment pixel weighted to a forearm or a wrist is weighted to a bone it is
not attached to, including in the cels that look fine.

The whole chain — clavicle, upper arm, forearm — is now excluded from garment pixels, which are
driven by the torso and hip bones alone. Identifying the garment needs the field and the field
is what is being computed, so it is done in two passes: accumulate with every bone, ask which
destination pixels landed on shirt, accumulate again with the arm chain switched off there.

**The earlier test of this was invalid and the reason matters more than the fix.** §7k capped
only the FOREARM, measured no change, and concluded *dominance is not responsibility*. The
bones that took over were the upper arm and the clavicle — also arm bones, also swinging with
the arm. **The experiment could not have produced a different answer whatever the truth was.**
That is §0a's rule applied to one's own test rather than to a metric: ask what the result would
have looked like if the arm *were* responsible, and it would have looked exactly like that.

**And the exclusion must be ramped — required, not merely nicer.** Switched hard with `~cloth`
it removed the hem lobe on all four cels — the thing it was built for — and tore every arm into
a doubled, ghosted limb trailing a smear past the hip. A sharp boundary between two fields that
disagree is a tear wherever they disagree; same lesson as the rigid plate's own edge, reached
from the other side. Ramped over a sleeve's width, 0.016 of figure height.

### And a sagittal cycle CANNOT be rendered from an extreme-pose source

This settles the order of work, and it is why the tear that survives the ramp is not something
to carve away downstream. The warp deforms the SOURCE, so what each cel asks for is its
projected arm length **over the source's**:

```
source at a swing EXTREME (282 as shot)   1.000  1.200  1.000  1.200   max STRETCH 20%
source at MID-SWING                       0.819  0.983  0.819  0.983   every cel a squeeze
```

A ratio above 1.000 is a stretch the renderer must refuse, because a projection cannot exceed
the true length (`clamp_limbs`). The arm then lands short of where the pose asks and tears
against the cloth beside it. **Two of four cels currently demand a 20% stretch, so the
remaining tear is the refusal showing** — the renderer is behaving correctly and the fault is
upstream in the source pose.

From a mid-swing source every cel is a squeeze, which the warp handles natively. **§0b's
generation rule is therefore not an optimisation for a sagittal cycle; it is a precondition.**

### The current build STAYS. The new one is not better on every axis.

| | plate area | sleeve | hands swallowed | lateral | projected |
|---|---|---|---|---|---|
| **in `tools/samples` now** | 0.97–0.98 | 68.2% | L only, 17.6% | 0.0257 | **0.0660 — in band** |
| regenerated (`warp_mid4`) | 0.99–1.02 | 65.6% | both, 21.6/24.3 | 0.0227 | 0.0535 — below |
| + init hand, draw order (`warp_mid6`) | 0.99–1.02 | 65.1% | both, 20.8/22.5 | **0.0208** | 0.0526 — below |
| + forced hand refinement (`warp_mid7`) | 0.99–1.02 | 64.8% | L only, 26.0% | 0.0320 | 0.0513 — below |

Better on the shirt plate and better on lateral sway. **Worse on the sleeve, worse on the
hands, and the projected arm drops out of the reference band** where the current build sits
inside it. So the deliverables are not rebuilt and the current build stands — not as a
judgement call, but because the standing rule is that the replacement has to win on every axis
and this one loses on three.

The regenerated source is still the better *source* — same man, crew neck, single-tone white
trainers — and it should go in as soon as the hands are solved, because that is the one defect
standing between it and a clean sweep.

### Re-measured after the wardrobe regeneration, not assumed to carry

The source changed underneath both results, so both were measured again rather than inherited.
`warp_mid4` is the regenerated source (seed 704311, crew neck, white trainers) warped through
the same sagittal cycle.

| | plate area, 4 cels | sleeve | hands swallowed |
|---|---|---|---|
| previous mid-swing source | 0.98 / 0.98 / 0.97 / 0.98 | 68.2% | L only, 17.6% |
| **regenerated source** | **1.00 / 0.99 / 1.02 / 0.99** | 65.6% | **both**, 21.6% / 24.3% |

**The shirt result carried and improved** — every cel within 2% of the source photograph's own
plate, against 2–3% before. The V-neck is gone; the neckline came out crew.

| | lateral range | projected range |
|---|---|---|
| before (lateral swing) | 0.0550 | 0.1352 |
| sagittal, old source | 0.0354 | 0.0360 |
| sagittal, mid-swing source | 0.0257 | 0.0660 |
| **sagittal, regenerated source** | **0.0227** | **0.0535** |
| REFERENCE (`walkref.mp4`) | 0.0139–0.0160 | 0.0588–0.0660 |

**The lateral improved and the projected fell out of the reference band.** 0.0535 is below the
footage's 0.0588 floor, where the previous source sat inside it at 0.0660. That is a
regression and it is the one number in this round that went the wrong way.

**The likely cause is the hands, and two independent measurements point the same way.** The
projected instrument finds the hand's lowest row; `--shirt-check` independently reports both
hands swallowed now against one before, and deeper (17.6% → 24.3% on the left). Hands buried
further into the pockets give a higher hand-bottom and a shorter projected arm on every cel.
Stated as the supported reading, **not** as a demonstrated cause — it has not been dumped and
looked at, and §7k is the standing reminder of what happens when an experiment is allowed to
confirm the only answer it could have produced.

If that reading is right, then **`hands in pockets` is no longer cosmetic**: it is costing the
arm swing the thing §7l was built to deliver, and the init-side fix named in §0 (run the
forearm capsule past the wrist by a hand's length) is the next move rather than a tidy-up.

## 7n. The hands: cause found, two fixes applied, defect NOT fixed. And it bounds §0.

The cause was right and the remedy did not work, which is worth more written down than a win
would be.

**The cause, confirmed:** the init painted the forearm as a skin capsule that stopped at the
wrist keypoint. At mid-swing the wrist sits against the hip, so the init drew skin ending dead
at the trouser column — a picture of a hand going into a pocket. The negative prompt said
`hands in pockets`; the init drew them; the init won.

**Fix 1 — the capsule runs past the wrist by a hand's length (0.105 of figure height).**
Anthropometric, not tuned: the authored wrist is at 0.455 and the hip at 0.505, and wrist
to fingertip is about 10.5% of stature, so the hand runs 0.455 → 0.560 and ends *below* the
hip exactly as a real relaxed hand does. **The frame came back with the hands still in the
pockets.**

**Fix 2 — the arms are drawn AFTER the legs.** The first attempt looked like a change that had
not landed, and nearly got treated as one. It had landed; the *thigh capsule was painted over
it*. The thigh is 0.075 of figure height in radius against the forearm's 0.042, and at the
hand's position it spanned x 221–375 against the hand's 196–282 — twenty-five pixels of an
eighty-six pixel hand survived. The init was drawing the hand and then painting the trouser on
top, which is a *better* picture of a hand in a pocket than the one it replaced. Reordering is
also simply correct: in a front view the arms hang in front of the thighs. **Measured +21
levels at the hand. The frame came back with the hands still in the pockets.**

### Why it cannot be pushed further from here, measured

Contrast above the background colour, which is what the model actually has to see through a
**62 px** blur:

```
forearm (a limb the init demonstrably gets across)        118
the wrist itself                                          100
hand, after both fixes                                     60
hand, radius 0.042 -> 0.070                            60 -> 67
hand, nudged outboard 0.030 / 0.055                    35 / 20
```

**The blur radius (62) is larger than the hand radius (43), so a hand is below the size this
init can express at all.** Widening it buys 7 levels and starts drawing a cartoon hand.

**And nudging it outboard made it worse, 60 → 20**, which disproved the hypothesis I had
carried over from the briefcase ("big enough AND separated enough"). That lesson does not
transfer: the briefcase was a dark mass that needed separating from a dark suit, whereas
moving the hand off the body surrounds it with backdrop and washes it out. **A rule learned on
one defect is a hypothesis on the next one.**

One sweep had to be thrown away: varying the capsule length moved the probe point with it, so
longer always measured lower and the experiment could not have produced any other answer. Same
error as §7k, caught by noticing that `HAND_LEN 0` scored 100 — the highest in the table.

### Abduction, measured off the footage — and it is not enough. Do not author it.

The next lever was to abduct the arms so the hands clear the hips. That was measured off
`walkref.mp4` rather than chosen, because whether it changes the silhouette *correctly* is a
fact about human walking and not a matter of judgement.

**The raw mask could not be run-counted, and the first version of this measurement was wrong.**
Background subtraction leaves the figure riddled with interior holes wherever the subject
matches the backdrop — the hip band came back as **7–12 runs on a single row**. The first run
of the instrument duly reported a gap of `0.0034` on nearly every frame, which is **one pixel**:
it was measuring the width of a noise speck. A genuine arm-to-torso gap is background that
reaches the border and an interior hole is not, so the fix is to flood the inverse from the
border and fill whatever it does not reach. (That flood was then itself wrong in the first
cut — padding the ring with the *figure* value sent it through the figure and filled the whole
frame, one run spanning all 640 columns. Both errors were caught by dumping the mask.)

With holes filled the silhouette through the hand band is **arm | gap | torso | gap | arm**,
read directly. What the clip can and cannot show:

| | |
|---|---|
| does a gap exist | **yes** — three runs or one, a fact about the silhouette |
| the gap's width | **yes**, where it exists |
| the gap where the arm crosses the hip | **no** — arm and torso merge into one run and the clearance is behind the body. Reported as merged, never as zero |
| the true 3-D abduction angle | **no** — this is the projected gap in the image plane |

```
set    frames   rows merged        LEFT gap (median)   RIGHT gap (median)
cyc_     16      93%                   0.0337              0.0035
md_      34      58%                   0.0202              0.0168
lg_      34      46%                   0.0426              0.0164
sv_      31      87%                   0.0202              0.0135

ALL, both sides, 1434 samples:  min 0.0033   MEDIAN 0.0201   max 0.1155
                                10th-90th percentile: 0.0067 .. 0.0471
```

**The merged fraction is the headline, not a footnote.** Between 46% and 93% of sampled rows
have the arm and torso as one run — for most of the stride, in a front view, a real walker's
hand is *not* laterally clear of his hip. The gap is real but intermittent and small.

**And it is nowhere near enough.** To free the hand from the init's trouser column the hand
must move **0.0587 of figure height** (hand centre 238, thigh outer edge 221, hand radius
0.042 → 60 px of overlap on a 1025 px figure). Against that:

```
required to clear the thigh column      0.0587
footage, median                         0.0201     2.9x too small
footage, 90th percentile                0.0471     still short
```

Only a single outlier row in the whole clip (0.1155) exceeds the requirement.

**So the measured abduction is not applied.** It is about a third of what is needed, and the
init sweep already showed that moving the hand outboard *reduces* its contrast (60 → 35 at an
offset of 0.030), so authoring the honest number would be strictly harmful: too small to clear
the column, and in the direction that weakens the hint. Inventing a larger angle would be
inventing a walk the reference does not contain. **The footage closed this lever rather than
opening it, which is still worth the measurement — the alternative was authoring it, waiting
for a generation, and finding out afterwards.**

## 7p. The hand bone landed, and the prediction it was built on was WRONG

The hand was added to the chain — `BONES` gained `(4,18)` and `(7,19)`, `pose_author` appends
fingertips at 18 and 19, `clamp_limbs`' chain table gained them too. The prediction was that
35° would then give **0.0627**, inside the reference band, with no tuning.

**It gave 0.0510.** Essentially unchanged from 0.0513 without the bone, and the implied
effective arm length stayed at 0.311 — the very number that was supposed to move to 0.382.

### Why: the hand bone describes a hand that was never drawn

Probing down the bone in the source photograph, asking at each step whether there is any hand
there:

```
  0% along the bone (the wrist)   skin 12%
 10%                              skin 31%   <- hand
 20%                              skin  2%
 30% .. 100%                      skin  0%
```

**The bone is 108 px long and holds hand pixels over about 10% of it.** The generated hand is
roughly a third of anthropometric length — it hangs just below the wrist and stops. A bone
cannot foreshorten length that was never drawn.

So `L_eff = 0.313` was never `0.277 + a hand that is not foreshortening`. It was
**`0.277 + a hand that is only 0.036 long`** — the small drawn hand, already being carried and
already foreshortening. The two readings predict the same flat 0.313 at every angle, which is
why the sweep could not distinguish them, and I presented the wrong one as a cause rather than
as the more interesting of two fits. **Flatness across the sweep ruled out an angle-dependent
explanation; it never ruled out a short hand.**

This traces straight back to §7n's bound: the init could not express a hand through a 62 px
blur, so the model drew its own at its own scale, and the rig then described a hand that is
not in the picture. **One defect, showing up a third time in a third place.**

### What was kept, and what it costs

The rig change is correct in itself — a chain that stops at the wrist while the measure runs to
the hand is wrong whatever the pixels do — and it passed every condition: all 18 COCO-18 joints
bit-identical across all 80 cels, all 80 ControlNet images byte-identical, the garment's arm
chain still 0.00%. But measured against the build without it:

```
                          lateral            projected
no hand bone              0.0320             0.0513
hand bone                 0.0451             0.0510
reference              0.0139-0.0160      0.0588-0.0660
```

**It buys nothing on projected and makes lateral worse**, because the bone drags trouser
through the 90% of its length that holds no hand. Lateral sway is the defect the player named
in the first place. **So the bone is in the code and the build shipped to the game is the one
WITHOUT it**, until the source hand is the right size.

`clamp_limbs` gaining `(2,3,4,18)` is a separate correctness fix and is inert here: in a
sagittal swing every arm bone gets *shorter*, so the `dl > sl` clamp never fires on the arm at
all. It was verified inert by hash, not by the numbers — which were identical, and under the
old reasoning would have been read as a change that did not land (§0a).

## 7o. Both arm knobs, walked until they broke. The answer on each is: do not move it.

`ARM_SWING_DEG` and `ARM_LATERAL` were treated as two independent knobs with two separate
reference bands, not as a trade. Each was swept alone with the source held constant
(`gen_mid7`), and each turned out to be the wrong lever — for a different reason.

### The sampling loss is real, is 9%, and is not the problem

The four keys sample arm phase `+1.00, −0.30, −1.00, +0.30`. **`cos` is even**, so those four
phases produce only **two** distinct foreshortening values, and neither is the maximum: no key
sits at `ph = 0`, true mid-swing, where the arm is longest in projection.

```
            cos at the four keys      realised span   ideal span   realised/ideal
30 deg      0.866 0.988 0.866 0.988      0.1217        0.1340          91%
35 deg      0.819 0.983 0.819 0.983      0.1641        0.1808          91%
40 deg      0.766 0.978 0.766 0.978      0.2121        0.2340          91%
45 deg      0.707 0.972 0.707 0.972      0.2653        0.2929          91%
```

**It is a flat 91% at every angle.** So it is a genuine sampling loss and worth knowing, but it
is a constant 9% and cannot explain a ~20% shortfall. It was checked before the angle was
touched, which was the right order — but it was not the answer.

### The swing sweep: the band is only reachable OUTSIDE the footage

```
swing      lateral    projected    verdict
30 deg     0.0318      0.0381      below
35 deg     0.0320      0.0513      below
40 deg     0.0312      0.0648      IN BAND
45 deg     0.0330      0.0806      ABOVE
REFERENCE  0.0139-0.0160   0.0588-0.0660
```

**40° hits the band and the footage measures 32–35°.** So the band is not reachable inside the
reference, and the standing rule is that exceeding it buys the number by authoring a walk the
clip does not contain. **`ARM_SWING_DEG` stays at 35.** Setting 40 would have passed the check
and made the walk wrong.

### And the sweep says exactly what IS wrong: the hand does not foreshorten

Solving each measured range back for the effective length being foreshortened:

```
          shoulder->wrist = 0.2770      shoulder->fingertip = 0.3820
30 deg  ->  L_eff 0.3130
35 deg  ->  L_eff 0.3129
40 deg  ->  L_eff 0.3057
45 deg  ->  L_eff 0.3040
```

**L_eff is ~0.313 and flat across the whole sweep** — far nearer the wrist than the fingertip.
The hand contributes only about a third of its length to the foreshortening.

The cause is structural and is visible in one line: `BONES` runs `(1,2), (2,3), (3,4)` — neck,
shoulder, elbow, **wrist**. COCO-18 has no fingertip keypoint, so **there is no hand bone**.
The hand is carried along by the forearm's transform but is not shortened by it, and the
`projected` measure runs shoulder-to-*hand*.

**If the hand foreshortened properly, 35° would give `0.382 × 0.1641 = 0.0627` — inside the
band, at the footage's own angle, with no tuning at all.** That is the fix: put the hand in the
chain. Not a bigger swing.

### Lateral: the cycle contributes 0.6% of it, so tuning the cycle cannot work

Separated by setting the cycle's own amplitude to zero and re-measuring, source held constant:

```
ARM_LATERAL 0.008, hands OUT      0.0320
ARM_LATERAL 0.000, hands OUT      0.0317     <- survives with the knob at ZERO
ARM_LATERAL 0.008, hands pocketed 0.0208

attributable to the CYCLE                     0.0002
attributable to hand extent + probe sampling  0.0317
reference band                           0.0139 - 0.0160
```

**Turning the knob off changes the measured lateral range by 0.0002 out of 0.0320.** The cycle
is responsible for **0.6%** of the number it was about to be blamed for. Tuning `ARM_LATERAL`
to bring 0.0320 down to 0.0150 would have required an absurd value and would have produced a
cycle that was wrong the moment the hands changed again — which they just did, twice.

**`ARM_LATERAL` stays at 0.008.** The overshoot is the rendered hand's own width, plus the probe
row at 0.455 of figure height sampling a hand on one cel and a forearm on another. Both are
downstream of the same missing hand bone.

> Two knobs, two bands, two sweeps, and **neither knob was the lever**. Had these been moved
> together on the assumption of a trade, 40° and a reduced lateral would have put both numbers
> inside their bands with the walk further from the reference than when it started.

### What this bounds about §0

§0 says the init beats the prompt, and the shoe row proves it at its strongest. **This is the
other edge of the same rule.** The init is a 62 px-blurred colour field: it is the strong lever
for *what a large mass is* — a garment, a shoe, a briefcase, a dark bar across the eye line —
and it has **no authority over anything smaller than its own blur**. A hand is smaller than its
blur. So is a wristwatch, which is why that one has not moved either.

The honest statement of the rule is therefore: **the init beats the prompt on masses that
survive the blur, and below that size neither lever has been shown to work.** Where the defect
is smaller than 62 px, the levers left are the pose (place the limb where the ambiguity does
not arise), the blur itself, or a refinement pass — not a better adjective.

### The refinement pass DOES get the hands out — and the gate was right all along

The init has no authority below its blur, so the hand has to be resolved at a stage that works
at the hand's own scale. That stage already exists: §7e's hand refinement, a 512×512 masked
pass at the wrist. **It was being skipped.** Its gate gives up when the crop holds under 8%
flesh, and on this source `hand_r` measured 0.082 (passes) and `hand_l` **0.065 (skips)** — so
the one hand most buried in a pocket was precisely the one the refiner refused to touch.

The gate's premise had quietly expired. It was written when the model's hand placement was
accepted, so an empty crop meant no hand to improve; now the hand is *supposed* to be at the
authored wrist and a crop full of trouser is the defect itself. `--force-hands` defeats it,
off by default.

**It works, and it fails, and both halves matter.**

```
                             hands out of pockets   fingers       watches
init alone (7n above)               no              n/a           both present
--force-hands, strength 0.72        YES             FUSED         both gone
```

Both hands come out and hang at the sides, and the two wristwatches went with them — the first
thing all session to move that defect, and consistent with the §0 bound, because the refiner
works at a scale where a watch exists. But **both hands came back as fused mittens**: a thumb
and a single undivided mass, no separate fingers. That is exactly the failure §7e was built to
prevent, reproduced on demand. **The gate is vindicated, not obsolete** — it should stay on by
default, and this flag should only ever be used on a frame someone is about to look at.

**At which rung does the mangling matter?** The shadow lesson applies again. Downsampled so the
figure is the 1990 cel's own 94 rows, the hand is **2–3 pixels**: the fused fingers are below
resolution, and what *is* visible is a skin-coloured blob where a hand belongs — present in the
refined version, absent in the pocket version. So at the rung the game draws, this is a gain.
At the 1:1 deliverable rung, where these frames are actually inspected, the hand is visibly
wrong. **Those two facts point opposite ways and the choice between them is not a measurement.**

### The test that was asked for: run at last, and it REFUTES the hypothesis

With the hands finally out of the pockets the precondition exists, so the question could be
asked properly — does the projected arm range return to the reference band?

```
                                 lateral range      projected range
sagittal, mid-swing source           0.0257             0.0660   in band
regenerated source                   0.0227             0.0535
  + hand in init, draw order         0.0208             0.0526
  + forced hand refinement           0.0320             0.0513   STILL BELOW
REFERENCE (walkref.mp4)          0.0139-0.0160      0.0588-0.0660
```

**It did not return. 0.0513 against a floor of 0.0588.** My reading was that buried hands were
shortening the measured arm; the hands are no longer buried and the number did not move
(0.0526 → 0.0513). **The hypothesis is refuted, and there is a second problem.** It is not the
source: three different sources with three different hand treatments all land at 0.051–0.054,
which points upstream at the authored cycle's swing amplitude rather than at anything the
generator produces.

**And the two numbers moved in opposite directions, which is why they are reported apart.**
Getting the hands out made the *offset* right and made the *amplitude* worse — lateral went
0.0208 → 0.0320, now **twice** the reference's 0.0139–0.0160, because hands that stick out
contribute lateral extent that hands in pockets did not. A single combined "arm fidelity"
figure would have averaged a win against a loss and reported neither. That is the metric that
cannot fail, and this is what it would have hidden.

The question was whether the projected arm range returns to the reference band once the hands
come out of the pockets. **The hands did not come out, so the test has no precondition and is
neither confirmed nor refuted.** What can be said is that the init changes alone did not move
it: 0.0535 → 0.0526 against a band of 0.0588–0.0660, which is noise, and `--shirt-check`
independently still reports both hands swallowed (20.8% / 22.5%). Those two agree, which is
consistent with the reading but is not a test of it.

## 7m. The shoe mask classifies ONE tone, and the shoe had two. Open, deliberately.

**Recorded now, not fixed now.** The wardrobe has been changed to route around it so the
mid-swing build can be judged; the mask itself is untouched.

View 282's wardrobe line said `brown shoes`, measured off the 1990 cel's foot band (77,45,45).
The model rendered that as a **two-tone boat shoe**: a near-black sole, a mid-brown upper, and
a pale welt between them. `shoe_mask` classifies against a single reference colour, so it took
the upper and left the sole behind. The sole then stayed in the body layer, the upper rotated
with the foot, and the shoe came apart as it turned.

**Why it is not being patched in this run.** The honest fix is a second reference colour —
`shirt_mask` already had to go this way, from "a tolerance around one reference" to
"nearest-of-two", when `sat_tol = 24` was measured on one figure and welded view 285's bare
forearms into the torso plate. A tolerance wide enough to take a black sole *and* a mid-brown
upper is also wide enough to take the jeans directly above them. That is a real piece of work
with its own measurement, and doing it under time pressure while the shirt and arm results are
waiting to be judged is how the one-reference classifier got shipped to nine characters in the
first place.

**The route around it**, which is a wardrobe change and nothing more: 282 is generated in
**white trainers**. This is the only deliberate deviation from a measured 1990 band in the
whole `wear` table and it is commented at the line. When the mask handles two tones, the line
goes back to brown shoes.

**And the wardrobe word alone did not deliver it** — see §0's shoe row. With the init still
painting the measured (84,52,51), "white trainers" produced a **black** trainer with a white
sole: a *higher*-contrast two-tone shoe than the brown one it replaced, i.e. the change made
the thing it was meant to fix worse. Only after overriding the init colour (`SHOE_INIT`) did
a plain white shoe appear, and the measured row profile is the proof:

```
source                      rows through the shoe              verdict
gen_mid   brown boat shoe   (50,22,11) ... (98,76,68) then (176,175,175)   two tones
gen_mid2  black trainer     (31,33,36) ... (60,58,58) then (178,174,170)   two tones, worse
gen_mid4  white trainer     (194,201,209) ... (170,180,191) throughout     ONE tone
```

### I predicted `--check-wardrobe` would flag it. It does not, and that is the real finding

The expectation written down was that scoring "white" against the (77,45,45) foot band would
flag the deviation and keep it honest. Measured: **it does not flag, at any tolerance.**
`check_wardrobe` scores each colour word against the **nearest band of any kind**, and "white"
lands on the torso's (239,239,240) at a distance of **3**. The foot band is never consulted.

So it is not that the checker is lenient here — it is that **a colour word is satisfied by any
band anywhere on the figure**. "White trainers" would pass just as quietly on a figure wearing
a white hat and black shoes. The guard reads green on a deviation that was made on purpose and
would read green on one made by accident, which is the same shape as §0c and as the `±0.09`
window: a check that cannot fail on the case you care about.

**Nothing silences this deviation except the comment at the `wear` line.** That is a weak place
to keep it, and it is where it is kept, deliberately and with this paragraph pointing at it.

### The attached shadow: judged at cel resolution, and it is not there

The regenerated source fails one screen check — `soles 1136 vs authored 1089` — and the cause
is **not** the shoe. The shoe ends cleanly around row 1119. What reaches 1136 is a near-black
cast shadow under one trainer: rows 1125–1133, mean `(0,0,1)`, 26–33 px wide. `shadow` is in
the negative prompt and was reaching the model; it fired anyway.

The question is whether that matters, and **the rung you measure it at decides the answer.**
At the 1065-row generation rung it is an obvious black blob. The 1990 cel is **39×95 with a
94-row figure**, so the same blob is about **one cel pixel tall and three wide** — and a single
dark pixel directly under a shoe is what ground contact looks like, not what a defect looks
like.

**It turned out to be neither, because it never gets that far.** Traced through the real fit
step rather than a simulated downsample:

```
                           near-black px in the bottom 2% of the figure
warped input (1058 rows)              214 of 338
fitted 12x   (1079 rows)                5 of 1092
```

The fitted cel's bottom rows are white shoe throughout (161–182). **`silhouette_matte` already
removes the shadow**, and for a reason that is worth keeping: the shadow is `(0,0,1)` and the
backdrop outside the figure is `(0,0,0)`. The matte cannot tell them apart, so the shadow keys
out with the background.

So: **no alpha clip, and the sole check stays exactly as it is.** The check is honest — it is
measuring a stage at which the shadow is genuinely present. It is just measuring a stage the
shadow does not survive. Loosening it to make the number go away would have removed a working
instrument to hide an artefact that was already being handled one step later.

**This is luck, not design, and nothing should be built on it.** The shadow is removed because
it happens to be `(0,0,1)` against a `(0,0,0)` backdrop — indistinguishable, so it keys out.
**A shadow one shade lighter would survive the matte and land on the sprite**, and it would do
so silently, because the only check that currently notices a shadow at all is the sole check,
which fires at the generation rung for a different reason. Nothing downstream is looking for
this. If a cast shadow ever does reach a finished cel, this paragraph is where to start.

The general point, which is the same one as §0c and the `±0.09` window from the other side:
**a defect's size is meaningless until you say which rung you measured it at.** This one is
fatal at 1065 rows, invisible at 95, and already deleted by the time it reaches either.

### The window bug underneath it survived only because the feet were always apart

Separately, and already fixed: `shoe_mask` cut each shoe out of a window `±0.09 × fig_h` either
side of the ankle. At a 62 px ankle separation **both windows contained both shoes** — they
measured 150 px and 132 px wide against actual shoes of 64 px and 67 px — so each foot's mask
dragged the other foot along with it. Fixed with a midline clip between the two ankles.

The arithmetic was never safe: `2 × 0.09 × fig_h` exceeds the ankle separation whenever the
feet are closer than about a fifth of figure height, which is **most of a real walk cycle**.
It had never fired because **every pose it had ever run on had the feet far apart** — the old
cycle's extreme keys, and the still poses before that. The constant was not loosened by the
mid-swing change and no code moved under it; the mid-swing pose was simply the first input
that ever put the feet near each other.

So this is not a regression, it is a **latent fault that was being masked by an unrepresentative
test set** — the same shape as §0c, where a reference drawn from the population could not see a
defect the population shared. A constant that is only correct for the inputs you happen to have
tried reads as correct forever.

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

**COUNTED IN KEYS, WHICH IS A QUARTER OF WHAT THE PLAYER SEES.** The table below was written
as four cels per view. Every walker view holds **sixteen** — four keys and twelve
in-betweens, `_s1` to `_s3` — and `SciArt.SubCel` resolves the in-betweens down the same
search path, so they fall back to the 1990 art exactly as the keys do. Re-counted against the
directories on disk:

| character | views in `cast0` | cels still falling back |
|---|---|---|
| body0 (280–283) | 280, 281, 282 | **16** — view 283, the undressed tier |
| body1 (284–287) | none | **64** — all four views |
| body2 (290–293) | none | **64** + view 293's 4 prop cels |
| body3 (294–297) | none | **64** — *nothing has ever been generated for this character* |
| view 500 | none | the character-select screen still shows the 1990 photographs |

A smoothed 1990 figure is *harder* to spot, not easier: view 283 gets its 22fps in-betweens
from Sierra's art and so reads as deliberate rather than missing.

`assets/png12x-new` is the LEGACY CARTOON SET and is not a source for any of this. It does
contain `view_283`–`view_287`, which makes it look like the gap is already filled; it is not.
That art is a different art direction entirely — flat illustration, not photography — and its
283 is additionally torn, with blocky holes through the torso, arms and legs. `png12x-cast`
(no `0`) is a third stale set, 16 files. Neither is on any search path unless someone sets
`JONES_ART_SET` to its name.

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

* **`shoe_mask` classifies one tone — see §7m.** Needs the `shirt_mask` treatment
  (nearest-of-N rather than a tolerance around one reference), measured, not guessed. Until
  it lands, view 282 is generated in white trainers instead of its measured brown, and
  `--check-wardrobe` correctly flags that line. **Fixing the mask is what retires the
  deviation; putting brown back without fixing the mask just re-breaks the shoe.**
* **Views 284, 286, 291, 292 fail `--shirt-check`** and are not to be rebuilt in this pass.
* **28 cels have never been generated** (283, 287, 293–297, and all of body3). All of them
  must be shot mid-swing per §0b — that is now a precondition, not a preference (§7l).
* **THE HAND NEEDS A BONE — §7o, and it is now the top of this list.** `BONES` ends at the
  wrist, so the hand is carried by the forearm without being foreshortened by it. Measured
  effective length is 0.313 against a true shoulder-to-fingertip of 0.382, flat across a
  30–45° sweep. **One missing bone explains both open arm numbers**: the projected range is
  short because a third of the arm does not foreshorten, and the lateral range is 2× high
  because the hand's own width dominates it (the cycle contributes 0.0002 of 0.0320). With
  the hand in the chain, 35° — the footage's own angle — predicts 0.0627, inside the band,
  with no tuning. **Neither `ARM_SWING_DEG` nor `ARM_LATERAL` should be moved; both were
  swept and both are the wrong lever.**
* **Hand refinement produces fused fingers when forced past its gate (§7n).** `--force-hands`
  places the hands correctly and mangles them; it is ON for 282 by decision (`FORCE_HAND_VIEWS`)
  because at 94 rows a fused finger is below resolution and a missing hand is not — see
  `hand_both_rungs_282.png`. One attempt at fixing it properly (768px, 0.115 crop,
  finger-specific prompt) came back **worse**: a hallucinated dark object held in the hand.
  Needs a better hand pass, not a looser gate and not a bigger crop.
* **`boil 12.12` on the mid-swing smoothing run** was observed and not investigated.
* **`generate()`'s `best` variable is not the best, it is the LAST.** It is reassigned every
  attempt and only the `break` on a clean screen stops the loop, so when no attempt passes,
  the frame that gets written is whichever one happened to run last — even if an earlier try
  failed one check and the last failed six. Harmless so far because a passing try breaks out,
  and genuinely useful with `--tries 1` (it is how a rejected frame can be put on disk and
  looked at). But the name says a judgement it does not make.
* **Consistency is unmeasured.** `boil` needs all four cels of a loop and only cel 0 of five
  views exists. `fit_walkers.py --measure` and
  `smooth_walk.py --dir assets/png12x-cast --scale 12` both still need running.
* **View 500**, the character-select screen, needs `fit_walkers.py --portraits` or the select
  screen shows one cast and the board another.
* Known cosmetic misses on the current stills: heels on 296 where the prompt says flat shoes;
  an oversized shirt on 292; no sunglasses on 282, which the original wears and which are a
  strong silhouette feature at 39 pixels wide.
