r"""The talking-head portraits (views 351, 353-362), one face in eleven mouth positions.

    python tools/gen_talker.py --view 355 --out <dir> --dry      # inits only, no GPU
    python tools/gen_talker.py --view 355 --out <dir>            # base face
    python tools/gen_talker.py --view 355 --out <dir> --mouths   # the eleven cels

Zain, on the Factory's shopkeeper: *"this looks ridiculous!!!, doesn't even look like a human
being!!!"* and *"Change the lady's face, same colour hair and similar characteristics, but
much more realistic face"*. Same woman, realistically rendered - not a different person.

WHY THIS IS NOT A BUILDING
--------------------------
**A talker has ELEVEN cels, not one.** `LipSync.cs` drives `_talkerCel` 0..10 from the game's
own sync tables at 60Hz while she speaks. One cel freezes her mouth; eleven independently
generated faces flicker, because nothing holds a diffusion model to the same cheekbones twice.

So the shape is the one that settled the signs: **construct, then light.** One face is
generated. The other ten cels are that same face with ONLY a mouth box replaced, and
everything outside that box is **copied byte for byte**. Flicker is not reduced, it is made
impossible - there is nothing outside the box that COULD differ.

AND WE DO NOT REPRODUCE HER JITTER
----------------------------------
Measured across her eleven cels, the per-pixel standard deviation is 4.9 and **64.8% of
pixels change somewhere**; a mouth-sized box holds only 41% of the strongest variation. That
is not animation. **Her eleven cels are eleven separate photographs of a real actor**, so her
head moves between frames. A still head with only the mouth moving is BETTER than the
original, and it is what makes the copy-outside-the-box approach legitimate rather than a
compromise.

THE MOUTH SHAPES ARE HERS, VERBATIM, AND THAT IS THE POINT
----------------------------------------------------------
The sync tables index these eleven positions by meaning - closed, open with teeth, the dark
"O" at cel 6, back to closed. **Smooth but wrong lip-sync is worse than what we have.** So
each cel's mouth is conditioned on the SOFT EDGES OF HER OWN MOUTH in that cel: the structure
is measured off her art and cannot be invented, while the rendering is ours. That is the same
division as a constructed sign - layout deterministic, lighting denoised.

WHAT IS RESTORED RATHER THAN GENERATED
--------------------------------------
Her field is a flat **(0,32,144)** blue and her frame is a 1px black border, both exact. They
are put back after every pass, so the model only ever has to get the PERSON right and the
portrait still keys and sits in the dialog exactly as before.
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageChops, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_building as gb

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PNG1X = os.path.join(ROOT, "assets", "png")

FIELD = (0, 32, 144)          # her flat blue, measured
FRAME = (0, 0, 0)             # her 1px border, measured
CELS = 11                     # LipSync.cs: every talker view has exactly eleven

# Per view: the prompt for the face. Only 355 is written so far; the rest are listed so the
# next person knows the set is eleven and which is which.
FACES = {
    355: dict(
        who="the Factory's shopkeeper",
        # af_nicole at 1.02 was cast against this face - "light and smiling, the cheer over
        # 'we will overwork you' is the joke". The face has to keep reading as that voice.
        prompt=("a photograph of a smiling woman in her thirties, voluminous shoulder length "
                "red auburn curly hair, broad warm open smile, dark top, plain blue "
                "background, studio portrait, three quarter view, soft even lighting, sharp "
                "focus, fine detail, photorealistic"),
        negative=("illustration, drawing, painting, cartoon, 3d render, cgi, anime, doll, "
                  "plastic skin, waxy, deformed face, distorted, extra eyes, asymmetric "
                  "eyes, blurry, low resolution, text, watermark, frame, border"),
        # The mouth box, in CEL coordinates, from the variation peak across her eleven cels:
        # the per-pixel standard deviation peaks in rows 30..34 with its median at row 30,
        # col 33. The box is drawn generously around that so its edges land in flat skin and
        # on the dark top, NOT across the lip line or the jaw contour.
        mouth=(20, 26, 48, 46),
        strength=0.55, control=0.55, cfg=7.5, steps=34, seed=7,
        # THE MOUTH PASS NEEDS ITS OWN PROMPT, and the first attempt proved it the hard way.
        # Handing the whole-portrait prompt to a 348x252 crop of a chin asks the model to fit
        # "studio portrait, plain blue background" into a close-up, and six of the eleven
        # cels came back as coloured swirls. Same failure as asking for "an apartment
        # building" in a square frame and getting a doorway: THE PROMPT MUST DESCRIBE THE
        # CROP, not the subject the crop was taken from.
        mouth_prompt=("a close photograph of a smiling mouth and chin, lips and teeth, "
                      "soft even lighting, sharp focus, fine detail, photorealistic"),
        mouth_strength=0.35, mouth_control=0.60, mouth_steps=28,
    ),
    357: dict(
        who="the university's shopkeeper",
        # ZAIN SUPPLIED THE MAN. "replace the university man with this", and when told a
        # reconstruction would drift: "Of course the likeness is important!" So the headshot
        # is the subject and the likeness is the REQUIREMENT - this cel is a DOWNSCALE of a
        # 1254x1254 photograph into 816x660, never an upscale and never a re-imagining. No
        # pass is run over the resting cel at all.
        #
        # NO MORTARBOARD. Her man wears academic dress; this one does not. Zain was asked,
        # said "add the mortarboard", then "no forget the mortar board!!!" - so nothing was
        # ever added, and nothing in this cel is outside his photograph.
        source_face=dict(
            path="universityman_face.png",
            # Head-and-shoulders at the portrait's own 68:55. A 760-tall crop puts his head
            # at 53% of the width, which is about where hers sits; taller crops drop it to
            # 40% and he stops filling the frame.
            # 870, SOLVED RATHER THAN CHOSEN. A square source and a landscape cel fight each
            # other: filling the width overshoots the height, so headroom and shoulders
            # compete. Zain: "fix the crop at the top".
            #
            #   crop_h  headroom  head   shoulders   (all cel px, of 55)
            #      760     6.0    43.8      5.2      head clears, but he is a floating head
            #      870     6.0    38.2     10.8      <- both
            #     1014     5.2    32.8     17.0      shoulders win, headroom falls short
            #
            # 870 is the tallest crop that still reaches a full 6 cel px with top at 0, and it
            # hands back 10.8 cel px of collar and shoulder. 1076x870 into 816x660 is a 0.76
            # DOWNSCALE, which the likeness requires; past 1014 the width exceeds the source
            # and it would become an upscale.
            crop_h=870,
            # Her head clears the frame by 6 cel px and his must too.
            head_clear=6,
            # HIS BACKGROUND IS GREY AND HERS IS A FLAT BLUE FIELD, and HER mask cannot be
            # reused - it is the silhouette of HER man, who is bearded, capped and a
            # different shape. So the field is keyed from HIS OWN image: a flood from the top
            # edge only, so skin that happens to match a grey cannot be keyed out from the
            # inside. Measured: tol 30 leaves an unkeyed grey island down the left where the
            # gradient darkens, tol 60 eats into his hair and temple, tol 45 is clean at
            # 56.2% keyed with the suit and collar intact.
            tol=45,
        ),
        source_mouths=dict(
            path="universityman_mouths.jpg",
            # THE GRID IS MEASURED, NOT TAKEN FROM THE BRIEF. It was described as 64 frames
            # of 115x64 in a 4x16 grid; the strongest horizontal changes in the sheet fall at
            # y = 0, 85, 170, 256, 341, 427, 512, 597, 683, 768, 854, 939, a spacing of 85.3.
            # So it is 4 x 12 = 48 frames of 115x85. The printed labels are unreliable too
            # (34 appears twice), so frames are indexed by GRID POSITION, never by label.
            cols=4, rows=12, tile_w=115, tile_h=85,
            # His mouth within a tile. Found by variance across the 48 frames AFTER masking
            # the top-left corner: the printed LABEL changes between frames and is the single
            # most-varying thing in the picture, so unmasked it keys the number, not the man.
            mbox=(44, 46, 78, 72),
            # HER ORDERING CARRIES THE LIP-SYNC, so his frames are chosen to follow it rather
            # than taken in sequence. Hers runs closed, closed, parted, open-dark, closed,
            # widest, open-dark, closed, open-teeth, open-teeth, closed.
            #   cel 0 is the headshot itself - closed, a slight smile, no pass
            picks=[None, 45, 17, 4, 21, 28, 12, 22, 10, 31, 47],
            # KEEP ONLY THE STRONGEST 12% OF THE EDGE MAP, and this is what makes a supplied
            # VIDEO frame usable at all.
            #
            # His tiles are 35x27 px of JPEG-compressed, soft, dark-room video. Scaled into a
            # 324x192 box that is a 9x upscale, and every soft gradient becomes a ring of
            # contours: 70% of the map reads above 64 against 50% for her pixel art, and the
            # amplitude is far higher. The first run followed that noise faithfully and
            # returned ten cels of grotesque abstract texture - the model was not failing, it
            # was doing exactly what a full-field edge map told it to.
            #
            # Thresholding to the top 12% leaves the lip line, the mouth opening and the chin
            # arc and drops the ringing. At 25% clutter survives; at 6% the mouth itself
            # starts to go.
            cond_keep=12.0,
            # His frame supplies the PIXELS of the mouth, not just its edges. See the note at
            # the paste site: conditioning alone left every cel closed, and the strength that
            # opened the mouth destroyed the likeness.
            paste_init=True,
            # Mouth landmarks, read off a grid rather than detected - an automatic reading
            # latched onto the jaw shadow in the headshot and the nose shadow in the video.
            # Landmarks read off matched grids - his whole tile at 9x, the base at 2x with
            # rows labelled in cel px. The eye line and the nose tip register; the lip line
            # is recorded for the record but deliberately NOT used, because it moves.
            register=dict(
                vid_eye_y=31.5, vid_nose_y=48.5, vid_cx=59.0,     # TILE px
                head_eye_y=25.5, head_nose_y=34.0, head_cx=31.5,  # CEL px
            )
        ),
        # THE BOX REPAINTED HIS NOSE. Zain: "that looks nothing like a human being speaking!,
        # It's a moving mouth on his nose!".
        #
        # Measured on his installed cel 0, down the box's own columns:
        #     nose tip (luminance peak)      cel y 32.5
        #     lip line (local trough)        cel y 38
        #     collar shadow begins           cel y ~47
        # and the region that actually changed between cels was cel y 30.1 .. 45.9. The box
        # began ABOVE HIS NOSE TIP, so all eleven cels repainted the nose with soft upscaled
        # video - a nose that changes shape eleven times, which is what he saw.
        #
        # It was NOT the 8 cel px context expansion leaking into the output: the changed
        # region is 15.8 cel px, which is the authored box (16.0), not the box plus expansion
        # (32.0). The ring is context-only and always was. The fault was the authored extent.
        #
        # AND THE REAL CAUSE IS THAT I NEVER RE-DERIVED THE BOX AFTER THE RECROP. It was
        # placed by eye against the crop_h=760 composition; changing to 870 moved every
        # feature up the frame and the box was carried over unchanged. A measurement taken
        # against one geometry does not survive a change to that geometry.
        #
        # Now: top at 35 clears the nostrils, bottom at 44 clears the collar by 3 px, centre
        # 39.5 sits just below the closed lip line at 38 - correct, because a mouth opens
        # downward.
        # x ENDS WHERE HIS FACE ENDS IN THE TILE. With the registration solved, his face
        # spans tile x 30..78, which maps to absolute device x = 24 + 30*6 = 204 and
        # 24 + 78*6 = 492 - cel x 17.0 to 41.0. A box reaching to cel 43 therefore pulled his
        # VIDEO BACKGROUND in at the right edge, which showed at 12x as a white smear on his
        # jaw. 40 stops inside his face with a margin, and keeps the box centred on his mouth
        # (centre 31 against his mouth centre 31.5).
        mouth=(22, 35, 40, 44),
        # The ramp is 4 cel px by default, which on a 10-row box would reach full strength
        # only at the centre row and dilute the mouth away. 2 keeps rows 37..42 at full.
        seat_ramp=2,
        mouth_prompt=("a close photograph of a mouth and chin, lips and teeth, soft even "
                      "lighting, sharp focus, fine detail, photorealistic"),
        negative=("illustration, drawing, painting, cartoon, 3d render, cgi, plastic skin, "
                  "waxy, deformed, distorted, blurry, low resolution, text, watermark"),
        context=8,
        # See the note at the no_pass branch: at a 6x upscale the model has too little signal
        # in the box and invents rather than resolves.
        no_pass=True,
        mouth_strength=0.35, mouth_control=0.60, mouth_steps=28,
        cfg=7.5, steps=0, strength=0.0, control=0.0, seed=7,
    ),
}
SCALE = 12                    # 68x55 -> 816x660 = 538,560 px, inside the measured 810k


def her_cels(view):
    out = []
    for c in range(CELS):
        p = os.path.join(PNG1X, "view_%d_l0_c%d.png" % (view, c))
        out.append(Image.open(p).convert("RGB"))
    return out


def field_mask(cel):
    """Her flat blue field, and the 1px frame. Both are restored, never generated."""
    a = np.asarray(cel).astype(int)
    d = np.abs(a - np.array(FIELD)).max(2)
    m = d <= 24
    h, w = m.shape
    frame = np.zeros((h, w), bool)
    frame[0, :] = frame[-1, :] = True
    frame[:, 0] = frame[:, -1] = True
    return m, frame


def restore(img, cel, S):
    """Put her field and her frame back, exactly, at S px per cel pixel."""
    m, frame = field_mask(cel)
    a = np.asarray(img).astype(np.uint8).copy()
    M = np.kron(m, np.ones((S, S), bool))
    F = np.kron(frame, np.ones((S, S), bool))
    a[M] = FIELD
    a[F] = FRAME
    return Image.fromarray(a, "RGB")


def pad8(im):
    """Grow to a multiple of 8 by REPLICATING the bottom and right edges.

    68x55 at 12x is 816x660, and 660 is not a multiple of 8, so the pipeline floors it to 656
    and hands back an image four rows short of the masks. Resizing the result back would
    resample the whole portrait to fix four rows. Padding the INPUT to 816x664 instead means
    the pass runs at its own native size and the pad is simply cropped off afterwards -
    nothing in the portrait is resampled at all. The replicated rows are her black frame, so
    the model is not shown an edge that is not already there."""
    w, h = im.size
    W, H = (w + 7) // 8 * 8, (h + 7) // 8 * 8
    if (W, H) == (w, h):
        return im
    a = np.asarray(im)
    a = np.pad(a, ((0, H - h), (0, W - w), (0, 0)), mode="edge")
    return Image.fromarray(a, "RGB")


def restore_mask(img, mask):
    """Lay her field and frame back using a PRECOMPUTED mask.

    `restore` derives the mask from her own cel, which is right when the subject is hers. For
    a supplied headshot it is wrong: her mask is the silhouette of HER man - bearded, capped,
    a different shape - so using it would paint blue across his face where her background was
    and leave his grey where her man stood. A supplied-face view keys its own field once and
    reuses that one mask for all eleven cels, which is also what keeps the field identical
    frame to frame.
    """
    a = np.asarray(img).astype(np.uint8).copy()
    a[mask] = FIELD
    h, w = mask.shape
    S = max(1, w // 68)
    fr = np.zeros((h, w), bool)
    fr[:S, :] = fr[-S:, :] = True
    fr[:, :S] = fr[:, -S:] = True
    a[fr] = FRAME
    return Image.fromarray(a, "RGB")


def init_from(cel, S, blur):
    big = cel.resize((cel.width * S, cel.height * S), Image.LANCZOS)
    return pad8(big.filter(ImageFilter.GaussianBlur(blur)))


def key_field(img, tol):
    """His background, keyed from HIS OWN image by a flood from the top edge.

    A flood rather than a colour test: a colour test anywhere in the frame will key skin or
    shirt that happens to sit near a grey, and there is no way to tell that from background
    once it has. Seeded only from the top edge, an interior patch that matches is never
    reached, because it is not connected to the outside.
    """
    a = np.asarray(img).astype(int)
    h, w, _ = a.shape
    ref = np.concatenate([a[0:40, 0:40].reshape(-1, 3),
                          a[0:40, -40:].reshape(-1, 3)]).mean(0)
    near = np.abs(a - ref).max(2) <= tol
    from collections import deque
    seen = np.zeros((h, w), bool)
    q = deque()
    for x in range(w):
        if near[0, x]:
            seen[0, x] = True
            q.append((0, x))
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            yy, xx = y + dy, x + dx
            if 0 <= yy < h and 0 <= xx < w and near[yy, xx] and not seen[yy, xx]:
                seen[yy, xx] = True
                q.append((yy, xx))
    return seen


def base_from_source(spec, w, h, S, samples):
    """The base cel from a SUPPLIED headshot. No model, no prompt, no pass.

    Crop to the portrait's own aspect on his face, downscale, key his background to her flat
    field, and lay her 1px frame on top. Everything in the result is his photograph except
    the field and the frame, which are hers and are restored rather than generated.
    """
    src = spec["source_face"]
    f = Image.open(os.path.join(samples, src["path"])).convert("RGB")
    a = np.asarray(f).astype(int)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    skin = (r > 95) & (g > 50) & (b > 40) & ((r - g) > 12) & (r > b)
    col = skin[:int(f.height * 0.56)].sum(0)
    on = np.flatnonzero(col > 30)
    cx = (int(on.min()) + int(on.max())) // 2
    hgt = src["crop_h"]

    # THE TOP OF HIS HEAD IS HAIR, NOT SKIN, and measuring it as skin is what cropped him
    # into the frame. Zain: "fix the crop at the top".
    #
    # The first version set the crop from the first SKIN row less a fixed 70 px. His hair
    # sits about 60 px above his forehead, so that put the skull hard against the border:
    # keyed on the field, his figure began at device row 0 where hers begins at 72 - six cel
    # pixels down. The headroom has to be measured from the FIGURE, which means keying the
    # background on the source and taking the first row that is not background.
    figure = ~key_field(f, src["tol"])
    rows = np.flatnonzero(figure.sum(1) > 0.02 * f.width)
    head_top = int(rows[0]) if len(rows) else 0
    clear = src.get("head_clear", 6)
    # 1 cel px of the finished cel is hgt/h source px, so N cel px of headroom is N*hgt/h.
    pad = int(round(clear * hgt / float(h)))
    top = max(0, head_top - pad)
    print("  his figure starts at source row %d; %d cel px of headroom needs %d source px"
          % (head_top, clear, pad))
    wid = int(round(hgt * (w / float(h))))
    x0 = max(0, min(f.width - wid, cx - wid // 2))
    y0 = max(0, min(f.height - hgt, top))
    if y0 > top:
        print("  WARNING: wanted top %d but the crop would run off the bottom; using %d"
              % (top, y0))
    print("  his face spans x %d..%d; crop %dx%d at (%d,%d) -> %dx%d"
          % (on.min(), on.max(), wid, hgt, x0, y0, w * S, h * S))
    crop = f.crop((x0, y0, x0 + wid, y0 + hgt)).resize((w * S, h * S), Image.LANCZOS)

    field = key_field(crop, src["tol"])
    out = np.asarray(crop).astype(np.uint8).copy()
    out[field] = FIELD
    frame = np.zeros(field.shape, bool)
    frame[:S, :] = frame[-S:, :] = True
    frame[:, :S] = frame[:, -S:] = True
    out[frame] = FRAME
    print("  her field keyed from HIS background at tol %d: %.1f%% of the portrait"
          % (src["tol"], 100.0 * field.mean()))
    return Image.fromarray(out, "RGB"), (field | frame)


def align_to(her_c, her_0, box, search=5):
    """How far her head moved between cel 0 and cel c. MEASURED, AND THE ANSWER IS ZERO.

    Cels 1, 2 and 9 came back with swirls and kept coming back with swirls on a rerolled
    seed, so the failure is systematic. The hypothesis was her jitter arriving where it was
    not expected: the conditioning is taken from her cels, so in a cel where her head has
    moved it would describe a jaw in a position our base face does not have one, and the pass
    would be asked to put structure where there is none while keeping the face it was given.

    **That is not what is happening.** Run across all eleven cels, scored on everything
    except the mouth box, the best shift is (0,0) FOR EVERY CEL:

        cel  1  (+0,+0) residual 4.3      cel  6  (+0,+0) residual 3.7
        cel  2  (+0,+0) residual 3.4      cel  9  (+0,+0) residual 4.8

    Her jitter is sub-pixel and photographic - grain and shading, not translation - so there
    is nothing to align and the residuals do not separate the failing cels from the clean
    ones either (cel 2 fails at 3.4 while cel 6 is clean at 3.7).

    This is kept as a measurement rather than deleted, so the next person does not re-derive
    the same plausible theory. It is the second cause I was confident about on this portrait
    and measured away; the first was the dither. **The conditioning is not the problem.**
    """
    a0 = np.asarray(her_0.convert("L")).astype(np.float32)
    ac = np.asarray(her_c.convert("L")).astype(np.float32)
    h, w = a0.shape
    x0, y0, x1, y1 = box
    keep = np.ones((h, w), bool)
    keep[y0:y1 + 1, x0:x1 + 1] = False          # the mouth is SUPPOSED to differ
    best, bdx, bdy = None, 0, 0
    for dy in range(-search, search + 1):
        for dx in range(-search, search + 1):
            sh = np.roll(np.roll(ac, dy, 0), dx, 1)
            m = keep.copy()
            if dy > 0:
                m[:dy, :] = False
            elif dy < 0:
                m[dy:, :] = False
            if dx > 0:
                m[:, :dx] = False
            elif dx < 0:
                m[:, dx:] = False
            if not m.any():
                continue
            v = float(np.abs(sh[m] - a0[m]).mean())
            if best is None or v < best:
                best, bdx, bdy = v, dx, dy
    return bdx, bdy, best


def seat_patch(base, patch, box, S, ring=2, ramp=4):
    """Put the mouth patch back so the box CANNOT be seen, without touching anything outside.

    A hard paste shows. The first run proved it: real mouths, and a visible RECTANGLE across
    the lower face where the patch's tone and grain met the base's. The fix has to respect the
    invariant that makes this whole design work - every pixel outside the box stays byte
    identical - so BOTH halves of it live strictly INSIDE the box:

      1. LEVEL MATCH. The patch is shifted per channel so its mean over a ring just inside the
         border equals the base's mean over the same ring. That removes the tone step, which
         is what the eye actually catches.
      2. A RAMP, INSIDE THE BORDER. The patch fades to the base over `ramp` cel pixels at the
         box edge. At the border itself the result IS the base, so the seam is not hidden -
         there is nothing there to hide.

    This is not the feathering §17 forbids. That rule is about never blurring a join with
    SIERRA'S art to disguise it. Here both sides are ours and the edge is an artefact of our
    own tiling, which is the same thing `detail_tiles` corrects across its overlaps.
    """
    x0, y0, x1, y1 = box
    bx, by = x0 * S, y0 * S
    bw, bh = (x1 - x0 + 1) * S, (y1 - y0 + 1) * S
    b = np.asarray(base).astype(np.float32)
    p = np.asarray(patch).astype(np.float32)
    under = b[by:by + bh, bx:bx + bw]

    r = max(1, ring * S)
    m = np.zeros((bh, bw), bool)
    m[:r, :] = m[-r:, :] = True
    m[:, :r] = m[:, -r:] = True
    p = p + (under[m].mean(0) - p[m].mean(0))

    f = max(1, ramp * S)
    ys = np.minimum(np.arange(bh), bh - 1 - np.arange(bh))
    xs = np.minimum(np.arange(bw), bw - 1 - np.arange(bw))
    w = np.minimum(np.minimum(ys[:, None], xs[None, :]) / float(f), 1.0)
    w = (0.5 - 0.5 * np.cos(np.pi * np.clip(w, 0, 1)))[..., None]

    out = b.copy()
    out[by:by + bh, bx:bx + bw] = p * w + under * (1.0 - w)
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGB")


def despeckle(path, box, S, radius=0.30, ramp=4):
    """Smooth the CHROMA inside the mouth box. Luma, and everything outside, untouched.

    The seated patch came back with coloured specks - yellow and green flecks on the cheek,
    a purple cast across the lips in some cels. At 1x they vanish entirely; at 12x, which is
    the rung the desktop ships, they are visible.

    They are a CHROMA artefact: the pass is re-rendering a soft upscale at low strength and
    the colour channels pick up high-frequency noise the luma does not. So the fix is the
    standard one and it costs no GPU - **blur the chroma, keep the luma**. Perceived sharpness
    lives almost entirely in luma, so the mouth stays as crisp as it was while the flecks go.

    The same cosine ramp as `seat_patch` is reused, so the correction fades to nothing at the
    box border and the byte-identical invariant outside the box still holds exactly.
    """
    im = Image.open(path).convert("RGB")
    x0, y0, x1, y1 = box
    bx, by = x0 * S, y0 * S
    bw, bh = (x1 - x0 + 1) * S, (y1 - y0 + 1) * S
    ycc = im.convert("YCbCr")
    y, cb, cr = ycc.split()
    r = max(1.0, S * radius)
    cb2 = cb.filter(ImageFilter.GaussianBlur(r))
    cr2 = cr.filter(ImageFilter.GaussianBlur(r))

    f = max(1, ramp * S)
    ys = np.minimum(np.arange(bh), bh - 1 - np.arange(bh))
    xs = np.minimum(np.arange(bw), bw - 1 - np.arange(bw))
    w = np.minimum(np.minimum(ys[:, None], xs[None, :]) / float(f), 1.0)
    w = 0.5 - 0.5 * np.cos(np.pi * np.clip(w, 0, 1))

    out = {}
    for name, orig, blurred in (("cb", cb, cb2), ("cr", cr, cr2)):
        a = np.asarray(orig).astype(np.float32)
        b = np.asarray(blurred).astype(np.float32)
        sub = a[by:by + bh, bx:bx + bw]
        a[by:by + bh, bx:bx + bw] = b[by:by + bh, bx:bx + bw] * w + sub * (1.0 - w)
        out[name] = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "L")
    # ONLY THE BOX IS WRITTEN BACK, and the first version of this did not do that.
    #
    # It converted the whole portrait to YCbCr and back, which is LOSSY: every pixel in the
    # image shifted slightly, including her flat blue field, even though the only values
    # deliberately changed were inside the box. The cels despeckled in one run then no longer
    # matched the cels generated in the next, and the installer refused with 415,882 px
    # differing outside the mouth box - 77% of the portrait - for an operation that was
    # supposed to touch 16% of it.
    #
    # The guard caught it. That is the second time on this portrait that the invariant check
    # has caught a defect introduced by HOUSEKEEPING rather than by the generation.
    fixed = Image.merge("YCbCr", (y, out["cb"], out["cr"])).convert("RGB")
    res = im.copy()
    res.paste(fixed.crop((bx, by, bx + bw, by + bh)), (bx, by))
    res.save(path)


def check_invariant(cels_gen, spec, S):
    """Outside the mouth box, every cel must be byte-identical. Not nearly - identical.

    This is the guarantee the whole two-stage design exists to provide, so it is MEASURED
    rather than assumed, and the first run failed it: 5,324 px differed, because each cel's
    blue field was being restored against ITS OWN original and her field jitters frame to
    frame. A design that makes a defect impossible still has to be checked, because the thing
    that breaks it is never the part you were thinking about."""
    x0, y0, x1, y1 = spec["mouth"]
    bx, by = x0 * S, y0 * S
    bw, bh = (x1 - x0 + 1) * S, (y1 - y0 + 1) * S
    base = np.asarray(cels_gen[0]).astype(int)
    mask = np.ones(base.shape[:2], bool)
    mask[by:by + bh, bx:bx + bw] = False
    worst = 0
    for c in cels_gen:
        d = (np.abs(np.asarray(c).astype(int) - base).max(2) > 0) & mask
        worst = max(worst, int(d.sum()))
    return worst


def install(a):
    """Write the eleven cels into assets/png{F}x-{set}, at every rung asked for."""
    spec = FACES[a.view]
    cels_her = her_cels(a.view)
    w, h = cels_her[0].size
    S = SCALE
    gen = []
    for c in range(CELS):
        p = os.path.join(a.out, "cel_%02d.png" % c)
        if not os.path.exists(p):
            raise SystemExit("missing %s - run --mouths first" % p)
        gen.append(Image.open(p).convert("RGB"))

    # THE INVARIANT IS ENFORCED HERE, NOT MERELY CHECKED. Every cel is rebuilt as the base
    # face with its own mouth box pasted in, so "identical outside the box" is true by
    # construction at install time and cannot be broken by anything that happened upstream -
    # a despeckle, a partial reroll, a hand edit. The check that follows then has something
    # real to confirm rather than being the only thing standing between a bug and the game.
    base_p = os.path.join(a.out, "base_%dx%d.png" % (w * S, h * S))
    if os.path.exists(base_p):
        bse = Image.open(base_p).convert("RGB")
        x0, y0, x1, y1 = spec["mouth"]
        bx, by = x0 * S, y0 * S
        bw, bh = (x1 - x0 + 1) * S, (y1 - y0 + 1) * S
        for c in range(CELS):
            out = bse.copy()
            out.paste(gen[c].crop((bx, by, bx + bw, by + bh)), (bx, by))
            mp = os.path.join(a.out, "field_mask.npy")
            if os.path.exists(mp):
                out = restore_mask(out, np.load(mp))
            else:
                out = restore(out, cels_her[0], S)
            out.save(os.path.join(a.out, "cel_%02d.png" % c))
            gen[c] = out
        print("  reseated every cel onto the base face: identical outside the box BY"
              " CONSTRUCTION")

    if a.despeckle:
        for c in range(CELS):
            despeckle(os.path.join(a.out, "cel_%02d.png" % c), spec["mouth"], S)
        gen = [Image.open(os.path.join(a.out, "cel_%02d.png" % c)).convert("RGB")
               for c in range(CELS)]
        print("  chroma smoothed inside the mouth box; luma and everything outside untouched")

    bad = check_invariant(gen, spec, S)
    print("  INVARIANT: outside the mouth box, %d px differ across the eleven cels" % bad)
    if bad:
        raise SystemExit("  REFUSING: the eleven cels are not identical outside the mouth "
                         "box, so the face will flicker while she speaks.")
    print("  (zero - so nothing outside the mouth CAN differ, by construction)")

    for F in [int(v) for v in a.factors.split(",")]:
        out = os.path.join(ROOT, "assets", "png%dx-%s" % (F, a.set))
        if not os.path.isdir(out):
            raise SystemExit("no such set: %s" % out)
        for c in range(CELS):
            im = gen[c]
            if (w * F, h * F) != im.size:
                im = im.resize((w * F, h * F), Image.BOX if F < S else Image.LANCZOS)
            im.save(os.path.join(out, "view_%d_l0_c%d.png" % (a.view, c)))
        print("  wrote %d cels at %dx (%dx%d) -> %s"
              % (CELS, F, w * F, h * F, os.path.basename(out)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--view", type=int, required=True, choices=sorted(FACES))
    ap.add_argument("--out", required=True)
    ap.add_argument("--dry", action="store_true")
    ap.add_argument("--mouths", action="store_true",
                    help="stage two: the eleven cels, from an existing base face")
    ap.add_argument("--install", action="store_true",
                    help="stage three: write the eleven cels into the shipping set")
    ap.add_argument("--redo", default=None,
                    help="regenerate only these cels, e.g. 1,2,9")
    ap.add_argument("--seed-shift", type=int, default=0,
                    help="offset the per-cel seed, to reroll a cel that came back damaged")
    ap.add_argument("--despeckle", action="store_true",
                    help="smooth chroma inside the mouth box before installing")
    ap.add_argument("--set", default="cast0")
    ap.add_argument("--factors", default="4,12")
    ap.add_argument("--strength", type=float, default=None)
    ap.add_argument("--control", type=float, default=None)
    ap.add_argument("--seed", type=int, default=None)
    a = ap.parse_args()

    spec = FACES[a.view]
    os.makedirs(a.out, exist_ok=True)
    if a.install:
        install(a)
        return
    cels = her_cels(a.view)
    w, h = cels[0].size
    S = SCALE
    print("view %d - %s" % (a.view, spec["who"]))
    print("  %d cels, each %dx%d cel px; generating at %dx%d = %d px"
          % (CELS, w, h, w * S, h * S, w * S * h * S))

    SAMPLES = os.path.join(ROOT, "tools", "samples")
    src_mask = None
    if spec.get("source_face"):
        base_img, src_mask = base_from_source(spec, w, h, S, SAMPLES)
        np.save(os.path.join(a.out, "field_mask.npy"), src_mask)
        if not a.mouths and not a.install:
            base_img.save(os.path.join(a.out, "base_%dx%d.png" % (w * S, h * S)))
            print("  wrote base_%dx%d.png from HIS PHOTOGRAPH - no model, no pass."
                  % (w * S, h * S))
            print("  LOOK AT IT before running --mouths")
            return

    blur = max(1.0, S * 0.11)
    init = init_from(cels[0], S, blur)
    cond = gb.soft_edges(init_from(cels[0], S, blur * 0.5))
    # DO NOT WRITE PASS SCAFFOLDING FOR A VIEW THAT HAS NO BASE PASS. These two are the init
    # and conditioning built from HER cel 0, and for a supplied-headshot view nothing ever
    # consumes them - the base is a downscale of his photograph and cel 0 is that file saved
    # verbatim. Writing them anyway put `init_816x660.png` and `cond_softedge.png` on disk
    # next to a cel that was never generated, which reads from outside as evidence that a
    # pass ran over the resting frame. It had not, and that was provable - cel_00 is
    # bit-identical to a freshly recomputed crop - but a file that implies work nobody did is
    # a bad thing to leave lying next to the work.
    if not spec.get("source_face"):
        init.save(os.path.join(a.out, "init_%dx%d.png" % (w * S, h * S)))
        cond.save(os.path.join(a.out, "cond_softedge.png"))
    m, _ = field_mask(cels[0])
    print("  init blur %.1f px; her field is %.1f%% of the portrait and is RESTORED, not"
          " generated" % (blur, 100.0 * m.mean()))

    if a.dry:
        print("--dry: inits written, no GPU used")
        return

    base_path = os.path.join(a.out, "base_%dx%d.png" % (w * S, h * S))
    if not a.mouths:
        print("  strength %.2f control %.2f cfg %.1f steps %d seed %d"
              % (a.strength or spec["strength"], a.control or spec["control"],
                 spec["cfg"], spec["steps"], a.seed or spec["seed"]))
        img = gb.run(spec["prompt"], init, cond,
                     a.strength or spec["strength"], spec["steps"], spec["cfg"],
                     a.control or spec["control"], a.seed or spec["seed"],
                     spec["negative"])
        img = img.crop((0, 0, w * S, h * S))        # drop the multiple-of-8 pad
        img = restore(img, cels[0], S)
        img.save(base_path)
        print("  wrote base_%dx%d.png - LOOK AT IT before running --mouths" % (w * S, h * S))
        return

    # ---- stage two: eleven cels, identical outside the mouth box -------------------
    if not os.path.exists(base_path):
        raise SystemExit("no base face yet: run without --mouths first, and look at it")
    base = Image.open(base_path).convert("RGB")
    x0, y0, x1, y1 = spec["mouth"]
    bx, by = x0 * S, y0 * S
    bw, bh = (x1 - x0 + 1) * S, (y1 - y0 + 1) * S
    print("  mouth box cel (%d,%d)-(%d,%d) = %dx%d cel px, %.1f%% of the portrait"
          % (x0, y0, x1, y1, x1 - x0 + 1, y1 - y0 + 1,
             100.0 * ((x1 - x0 + 1) * (y1 - y0 + 1)) / (w * h)))
    print("  everything outside it is COPIED, so nothing outside it can differ")

    todo = range(CELS) if not a.redo else [int(v) for v in a.redo.split(",")]
    for c in todo:
        # CEL 0 IS THE BASE FACE, UNTOUCHED, and that is not a shortcut.
        #
        # Her cel 0 is the resting closed smile, and the base face was generated FROM her
        # cel 0 - so the face we already have IS this mouth position. Running a pass over it
        # can only add artefacts to the frame the player sees most, which is exactly what it
        # did: cel 0 came back with swirls on the cheek while cels 3-8 were clean. The pass
        # that has nothing to change is the one most likely to damage something.
        if c == 0:
            base.save(os.path.join(a.out, "cel_00.png"))
            print("    cel  0 IS the base face - no pass, nothing to re-render")
            continue
        out = base.copy()
        # The structure is HER mouth in THIS cel; the rendering is ours.
        sm = spec.get("source_mouths")
        if sm:
            # HIS OWN FRAME SUPPLIES THE MOUTH STRUCTURE. The sheet is indexed by grid
            # position, never by its printed label, and the chosen frame's mouth box is
            # scaled up into our box. It is a coarse upscale - a 35x27 px crop into 324x192 -
            # which is why it is used as CONDITIONING and never as pixels.
            sheet = Image.open(os.path.join(SAMPLES, sm["path"])).convert("RGB")
            thf = sheet.height / float(sm["rows"])
            f = sm["picks"][c]
            rr, cc = divmod(f, sm["cols"])
            ty = int(round(rr * thf))
            tile = sheet.crop((cc * sm["tile_w"], ty,
                               (cc + 1) * sm["tile_w"], ty + sm["tile_h"]))
            mx0, my0, mx1, my1 = sm["mbox"]
            her_mouth = tile.crop((mx0, my0, mx1 + 1, my1 + 1))
            print("      mouth structure from HIS frame %d (grid r%d c%d)" % (f, rr, cc))
            keep = sm.get("cond_keep")
        else:
            her_mouth, keep = None, None
        # HER cels are only consulted when the structure comes from her. For a supplied-mouth
        # view the alignment is meaningless - and printing "her head moved" while conditioning
        # on HIS video would describe something that is not happening.
        if her_mouth is None:
            dx, dy, _ = align_to(cels[c], cels[0], spec["mouth"])
            shifted = ImageChops.offset(cels[c], dx, dy)
            if dx or dy:
                print("      her head moved (%+d,%+d) here; shifted back onto cel 0"
                      % (-dx, -dy))
        if her_mouth is None:
            her_mouth = shifted.crop((x0, y0, x1 + 1, y1 + 1))
        hb = her_mouth.resize((bw, bh), Image.LANCZOS)
        # HER MOUTH IS DITHERED 16-COLOUR ART, so its soft edges at 12x are the DITHER, not
        # the lips. At a 0.7 px blur the conditioning map was texture noise and the pass
        # painted swirls over it at control 0.85. The blur has to be wide enough to destroy
        # the dither and leave only the gross shape - the lip line, the teeth block, the
        # opening - which is the only thing we actually want to copy from her.
        cnd = gb.soft_edges(hb.filter(ImageFilter.GaussianBlur(max(2.0, S * 0.40))))
        if keep:
            e = np.asarray(cnd.convert("L")).astype(float)
            thr = np.percentile(e, 100.0 - keep)
            e = np.where(e >= thr, e, 0.0)
            cnd = Image.fromarray(np.clip(e, 0, 255).astype(np.uint8), "L").convert("RGB")
            print("      conditioning sparsified to its strongest %.0f%%" % keep)
        # THE MODEL IS GIVEN A BIGGER CANVAS THAN THE BOX IT MAY CHANGE.
        #
        # This view's box is 27x16 cel px = 324x192 px, and SD1.5 falls apart below about
        # 256 px a side - it returns hallucinated texture, which is exactly what ten cels of
        # this portrait came back as from a clean init and a clean conditioning map.
        # Narrowing the conditioning first, to its strongest 12%, was the obvious fix and
        # changed nothing; that is what pointed at the SIZE rather than the content.
        #
        # So the pass runs over the box expanded by `context` cel pixels - 43x32 cel px, or
        # 516x384 - and only the box itself is seated back. The ring is real face, so the
        # model has something coherent to work from, and nothing outside the box can reach
        # the output however it renders the ring.
        ctx = spec.get("context", 0)
        cx0, cy0 = max(0, x0 - ctx), max(0, y0 - ctx)
        cx1, cy1 = min(w - 1, x1 + ctx), min(h - 1, y1 + ctx)
        gx, gy = cx0 * S, cy0 * S
        gw, gh = (cx1 - cx0 + 1) * S, (cy1 - cy0 + 1) * S
        src = base.crop((gx, gy, gx + gw, gy + gh))
        if sm and sm.get("paste_init"):
            # HIS OWN MOUTH IS THE INIT, NOT A HINT. This is the construct-then-light shape
            # that settled the signs, with his video frame as the construction.
            #
            # Conditioning alone could not do it and the measurements say why. His base is a
            # SHARP photograph of a specific man with a confidently closed mouth, so at
            # strength 0.35 the pass reproduced the init and every cel came back closed - the
            # lip-sync did not move at all. Raising it until the mouth opened (0.55, then
            # 0.70, control 0.75/0.85) opened it and destroyed him: orange and pink smears,
            # invented teeth, a cartoon. There is no strength that does one without the other,
            # because the model is being asked to INVENT an open mouth for a face it has
            # never seen open.
            #
            # It does not have to invent one. His 48 frames ARE that face with the mouth open.
            # So his mouth is scaled into the box and level-matched to the surrounding skin,
            # and the pass runs at a LOW strength whose only job is to resolve the softness of
            # a 9x upscale. The structure is his, the sharpening is ours, and nothing is
            # imagined - the same division as a constructed sign, lit rather than drawn.
            # REGISTERED ON HIS OWN FACE. Zain: "I actually GAVE you reference frames which
            # tells you exactly where it should be and you just messed it all up."
            #
            # He is right, and the earlier versions of this were both wrong. The frames are
            # not mouth texture, they are a REGISTRATION REFERENCE: every one shows the same
            # man's eyes, nose, mouth and chin with the true distances between them, which
            # fixes the geometry completely and leaves nothing to choose.
            #
            # The registration uses the EYE LINE and the NOSE TIP, because those are present
            # in both pictures and do not move. The lip line cannot be used - it moves by
            # definition, which is the whole point of the sequence.
            #
            #                       his tile      the headshot
            #     eye line            31.5          25.5 cel
            #     nose tip            48.5          34.0 cel
            #     lip line            55.5          38.0 cel   (not used to register)
            #     chin                70.5          45.5 cel
            #
            # so k = (34.0 - 25.5) * 12 / (48.5 - 31.5) = 6.0 device px per tile px.
            #
            # THE PREVIOUS SCALE WAS 8.06, TAKEN FROM MATCHING MOUTH WIDTHS - 34% too large,
            # which is why his mouth sat oversized and spilling however the box was drawn.
            # Matching one feature's width is not a registration; matching two fixed
            # landmarks is.
            #
            # And mapping his mouth through this registration lands it at cel y 35.3..43.4,
            # which is independent confirmation of the box measured off the installed cel's
            # own luminance - two methods, same answer.
            reg = sm["register"]
            k = ((reg["head_nose_y"] - reg["head_eye_y"]) * S
                 / float(reg["vid_nose_y"] - reg["vid_eye_y"]))
            # The WHOLE tile is scaled and registered, not a hand-cut mouth crop, so his
            # anatomy decides what lands where.
            nw = int(round(tile.width * k))
            nh = int(round(tile.height * k))
            grown = tile.resize((nw, nh), Image.LANCZOS)
            ox = int(round(reg["head_cx"] * S - reg["vid_cx"] * k)) - bx
            oy = int(round(reg["head_nose_y"] * S - reg["vid_nose_y"] * k)) - by
            mb = Image.new("RGB", (bw, bh))
            mb.paste(base.crop((bx, by, bx + bw, by + bh)), (0, 0))
            mb.paste(grown, (ox, oy))
            print("      REGISTERED on eyes+nose: scale x%.2f device px per tile px,"
                  " offset (%+d,%+d)" % (k, ox, oy))
            # Level-matched to his own skin on a ring just inside the box, so his video's
            # exposure does not step against the headshot's.
            a_m = np.asarray(mb).astype(np.float32)
            inner = np.asarray(base.crop((bx, by, bx + bw, by + bh))).astype(np.float32)
            r = max(1, 2 * S)
            rg = np.zeros((bh, bw), bool)
            rg[:r, :] = rg[-r:, :] = True
            rg[:, :r] = rg[:, -r:] = True
            a_m = a_m + (inner[rg].mean(0) - a_m[rg].mean(0))
            glued = src.copy()
            glued.paste(Image.fromarray(np.clip(a_m, 0, 255).astype(np.uint8), "RGB"),
                        (bx - gx, by - gy))
            src = glued
            print("      registered frame seated as the init, level-matched to his own skin")
        if ctx:
            # The ring's conditioning is the BASE's own edges - "this already exists, leave
            # it" - and only inside the box does the model see his mouth structure. A black
            # ring would read as "nothing here" and invite exactly the invention we are
            # trying to stop.
            ring = gb.soft_edges(src)
            ring.paste(cnd, (bx - gx, by - gy))
            cnd = ring
            print("      pass covers %dx%d px of context; only the %dx%d box is seated"
                  % (gw, gh, bw, bh))
        if spec.get("no_pass"):
            # NO GENERATIVE PASS AT ALL FOR THIS VIEW. Construct rather than generate - the
            # answer every other time something had to be a specific thing at this scale.
            #
            # With the registration correct and his own mouth seated as the init, a low
            # strength pass STILL invented: cel 2 came back with a second mouth, complete with
            # lips and teeth, on his right cheek, inside the box. The reason is in the numbers
            # - the box is 228x120 device px and is fed by 38x20 = 760 pixels of his video, a
            # 6.0x linear upscale, so there is almost no real signal in there for the model to
            # hold on to and it fills the gap with invention. Raising strength makes it worse
            # (0.50 smears, 0.65 degrades) and lowering it does nothing.
            #
            # So the mouth is HIS PIXELS, registered and level-matched, and nothing else. It
            # is soft, because a 6x upscale of video is soft. It is not invented, and it is
            # him. Softness is a defect of resolution that better source fixes; invention is a
            # defect of method that nothing fixes.
            got_box = src.crop((bx - gx, by - gy, bx - gx + bw, by - gy + bh))
            out = seat_patch(base, got_box, spec["mouth"], S,
                             ramp=spec.get("seat_ramp", 4))
            out = restore_mask(out, src_mask) if src_mask is not None \
                else restore(out, cels[0], S)
            out.save(os.path.join(a.out, "cel_%02d.png" % c))
            print("    cel %2d written - HIS PIXELS, no generative pass" % c)
            continue

        # --strength/--control steer THIS pass when --mouths is in play. They used to feed
        # only the base face, so a command that looked like it was retuning the mouth pass
        # was silently just rerolling it - a flag that appears to do something and does not.
        got = gb.run(spec["mouth_prompt"], pad8(src), pad8(cnd),
                     a.strength or spec["mouth_strength"],
                     spec["mouth_steps"], spec["cfg"],
                     a.control or spec["mouth_control"],
                     (a.seed or spec["seed"]) + c + a.seed_shift * 101,
                     spec["negative"])
        out = seat_patch(base, got.crop((bx - gx, by - gy,
                                         bx - gx + bw, by - gy + bh)), spec["mouth"], S,
                         ramp=spec.get("seat_ramp", 4))
        # THE FIELD MASK COMES FROM CEL 0, NOT FROM CEL c, AND THAT IS THE WHOLE POINT.
        # Restoring each cel against its OWN original put her jitter straight back in: her
        # blue field has a slightly different silhouette in every frame because her head
        # moves, so the restore wrote a different boundary per cel and 5,324 pixels differed
        # OUTSIDE the mouth box - the invariant this stage exists to guarantee, broken by the
        # one line that was supposed to be housekeeping. One mask, from one cel, for all
        # eleven.
        out = restore_mask(out, src_mask) if src_mask is not None \
            else restore(out, cels[0], S)
        out.save(os.path.join(a.out, "cel_%02d.png" % c))
        print("    cel %2d written" % c)
    print("  eleven cels written")


if __name__ == "__main__":
    main()
