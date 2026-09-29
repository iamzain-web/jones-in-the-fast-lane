"""Generates ORIGINAL high-resolution walker sprites for the twenty player views.

WHAT THIS IS, AND WHAT IT IS NOT
--------------------------------
The original figures are 39x95 digitised photographs of 1990 actors. Five upscalers have
been tried on them and every one either did nothing or made them worse, because there is
no detail left in a 39x95 photograph to recover. This tool does not restore them. It
DESIGNS NEW PEOPLE and draws them at a resolution the game can use on a 4K television.

Nothing of the original artwork reaches the output. What is reused is only what the GAME
depends on:

  * the bounding box, the height and the ground line of each cel, so the figure lands
    where the scripts put it and the click and collision geometry stays valid;
  * the per-cel leg, arm and shoulder positions measured by tools/sprite_pose.py, so the
    new character walks on the original's cadence and the animation timing is unchanged;
  * the clothing TIER each view stands for - the scripts choose view base+0, +1 or +2 from
    (wearing - 34) and base+3 when weeksOfClothing: is 0, so the three views have to read
    as three descending grades of dress and the fourth has to read as caught undressed;
  * the sex the silhouette implies, because views 284-287 and 294-297 are skirt and dress
    outlines and a trouser figure in that box would not fill it.

The four characters below were invented for this file. They are not descriptions of the
original actors and no prompt here names, describes or alludes to them - deliberately, at
every point: different ages, different builds, different colouring, different clothes
within the same tier. The one thing the model is shown of the original is a stick figure
of coloured line segments. It never sees a pixel of the photographs.

PIPELINE
--------
Stable Diffusion 1.5 + ControlNet openpose, fp16, on a 4GB card. The pose is the measured
skeleton, not a detector's guess: a detector has nothing to find in a 39-pixel-wide sprite.

CONSISTENCY, which is the hard part
-----------------------------------
Three levers, applied together:

 1. ONE SEED PER CHARACTER. Every cel of every outfit of one body denoises from the same
    initial noise. With SD1.5 this alone holds a face together across small pose changes,
    and the four cels of these cycles are a very small pose change - the figures barely
    move.
 2. ONE PROMPT PER CHARACTER, with the outfit as the only clause that varies, so the
    tokens that carry identity are bit-identical between cels.
 3. CEL 0 IS THE REFERENCE. Cels 1-3 are generated as img2img FROM cel 0 at low strength
    under their own pose. That is what stops a hand or a collar redesigning itself between
    frames; the pose control still moves the legs, because the ControlNet residual is
    applied at every step and the strength is high enough to let it.

`--measure` reports the frame-to-frame figure the way tools/smooth_walk.py does. Anything
that flickers should not be shipped; see the manifest it writes.

ALPHA
-----
Not solved here. Two attempts were made to generate a keyable background from this script
and both are recorded at the call site, because both failed in instructive ways and the
flags that select them are still present. The cutout is done by tools/fit_walkers.py
instead, which mattes against the original cel's own silhouette and so does not care what
colour the background came out. The original's cyan/white matte fringe, which Mitchell
spreads into a halo at 4x, has no equivalent in the result.

USAGE
-----
    python tools/gen_walkers.py --poses <dir> --out <dir> [--bodies body0] [--dry]
    python tools/gen_walkers.py --measure <dir>

Models are the local component files under %LOCALAPPDATA%\\jones-upscale-models\\gen.
"""

import argparse
import json
import os
import sys

MODELS = os.path.join(os.environ.get("LOCALAPPDATA", ""), "jones-upscale-models", "gen")
ASSETS_1X = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                         "assets", "png")

# CLIP TAKES 77 TOKENS AND SILENTLY DROPS THE REST. The first version of these ran to 93
# and the tail that fell off the end was "plain flat light grey background, no shadow" -
# the two clauses the whole cutout depends on. Everything here is counted: style ~20
# tokens, character ~22, outfit ~20, which leaves room and puts the background clause
# safely inside the window rather than at the end of it.
# GREEN SCREEN, AND IT IS NOT AN AESTHETIC CHOICE. These are cutout sprites and the
# background has to come off cleanly. Asked for white, SD1.5 tints the field towards
# whatever the clothes are and paints a floor under the feet; the first cast member came
# back on a brown gradient close enough to his own skin that keying it took his face off
# with it. Nothing on a person is saturated green, so a green field can be keyed on hue
# alone with no possible collision, and the spill it leaves is removable arithmetic.
STYLE = ("isolated on a plain green screen background, full body board game character "
         "illustration, clean digital painting, crisp edges, standing facing the viewer")

CARTOON = ("isolated on a plain green screen background, full body flat cel shaded cartoon, "
           "bold clean outlines, flat colour, standing facing the viewer")

# The background clauses are load-bearing: the cutout is a flood fill from the border and
# it needs one flat connected field to fill. The first run came back on a maroon gradient
# with a floor line under the feet - SD1.5 tints the background towards whatever the
# clothes are - so "coloured background", "gradient background" and "floor" are all named
# here, not just implied by "plain solid white" in the prompt.
# THE FURNITURE CLAUSES ARE NOT PADDING. Asked only for "no scenery", SD1.5 put an
# armchair, a stack of books and a curtain in beside the figure, close enough to the
# silhouette that the matte could not tell them from a coat. A sprite has to be alone in
# its frame, so every piece of set dressing it reached for is named.
NEGATIVE = ("photograph, text, watermark, signature, two people, cropped, out of frame, "
            "extra limbs, deformed hands, blurry, gradient background, scenery, floor, "
            "ground, horizon, shadow, reflection, vignette, border, frame, furniture, "
            "chair, armchair, sofa, table, books, curtain, plant, interior, room, wall")

# ---------------------------------------------------------------------------
# THE CAST. Invented for this port. Any resemblance to the 1990 cast is not
# intended and is actively avoided: each entry differs from the figure it
# replaces in age, build, colouring and garment.
# ---------------------------------------------------------------------------
CAST = {
    "body0": {
        "seed": 704311,
        "who": "a tall slim man in his early thirties, deep brown skin, short afro fade, "
               "neat beard",
        "style": STYLE,
        "wear": [
            "in a charcoal three piece suit, burgundy tie, black shoes",
            "in an olive bomber jacket over a white shirt, dark chinos, brown boots",
            "in a grey hooded sweatshirt, faded blue jeans, white trainers",
            "in only a white vest and blue boxer shorts, barefoot, embarrassed",
        ],
    },
    "body1": {
        "seed": 118097,
        "who": "a woman in her late twenties, warm brown skin, straight black hair to the "
               "shoulders",
        "style": STYLE,
        "wear": [
            "in a burgundy skirt suit over a cream blouse, low heels, black satchel",
            "in a teal wrap dress to the knee, flat tan shoes",
            "in a denim pinafore dress over a navy striped top, canvas shoes",
            "wrapped in only a folded broadsheet newspaper, bare shoulders and legs",
        ],
    },
    "body2": {
        "seed": 926455,
        "who": "a broad shouldered man in his fifties, pale skin, thinning grey hair, round "
               "tortoiseshell glasses",
        "style": STYLE,
        "wear": [
            "in a brown pinstripe suit, wide tie, scuffed brogues",
            "in a mustard cardigan over a checked shirt, brown corduroy trousers",
            "in a red plaid flannel shirt, work trousers, heavy boots",
            "in only a wooden barrel on shoulder braces, bare arms and legs, striped socks",
        ],
    },
    "body3": {
        "seed": 553028,
        "who": "a woman in her forties, olive skin, dark curly hair pinned up",
        "style": STYLE,
        "wear": [
            "in a long camel wool coat over a grey pencil skirt, carrying a document case",
            "in a sage green floral sundress with a full skirt, sandals",
            "in a white linen blouse and wide tan culottes, flat shoes",
            "wrapped in only a white bath towel, bare shoulders, legs and feet",
        ],
    },
    # The demo walker. The original is a caricature of the game's title character; this is
    # a different caricature - different hair, different face, different palette - drawn in
    # the same flat cartoon register so it still reads as "not one of the players".
    "jones": {
        "seed": 380142,
        "who": "a cartoon man with a long chin, a big nose, swept back teal hair, round "
               "spectacles, smug grin",
        "style": CARTOON,
        "wear": [
            "in a shiny electric blue double breasted suit with a huge yellow bow tie",
            "in a loud purple hawaiian shirt and tan slacks",
            "in a green patterned shirt, white trousers, deck shoes",
            "in only red and white striped swimming trunks, skinny arms and legs",
        ],
    },
}


# ---------------------------------------------------------------------------


def build(device, offload=False, slicing=False, controlnet=None):
    """Loads SD1.5 and the openpose ControlNet from the local component tree.

    `tools/arrange_models.ps1` lays the downloaded files out the way diffusers expects
    (sd15/unet, sd15/vae, ...) and everything here is an ordinary `from_pretrained`. An
    earlier version built each config by hand from the flat downloads and was fragile for
    no benefit - `CLIPTextConfig(**json)` in particular swallows unknown keys and produces
    a model whose weights load with strict=False and silently do nothing."""
    import torch
    from diffusers import (AutoencoderKL, ControlNetModel,
                           StableDiffusionControlNetImg2ImgPipeline,
                           StableDiffusionControlNetPipeline, UNet2DConditionModel,
                           UniPCMultistepScheduler)
    from transformers import CLIPTextModel, CLIPTokenizer

    sd = os.path.join(MODELS, "sd15")
    dt = torch.float16
    kw = dict(torch_dtype=dt, local_files_only=True)

    unet = UNet2DConditionModel.from_pretrained(sd, subfolder="unet", **kw).to(device)
    vae = AutoencoderKL.from_pretrained(sd, subfolder="vae", **kw).to(device)
    te = CLIPTextModel.from_pretrained(sd, subfolder="text_encoder",
                                       torch_dtype=dt, local_files_only=True).to(device)
    # `controlnet` lets a caller swap the conditioner without duplicating this function -
    # tools/gen_board.py wants soft edges, not a skeleton. The pipeline classes are the
    # same either way; only the weights differ.
    cn = ControlNetModel.from_pretrained(
        controlnet or os.path.join(MODELS, "cn-openpose"), **kw).to(device)
    tok = CLIPTokenizer.from_pretrained(sd, subfolder="tokenizer", local_files_only=True)
    sched = UniPCMultistepScheduler.from_pretrained(sd, subfolder="scheduler",
                                                    local_files_only=True)

    common = dict(vae=vae, text_encoder=te, tokenizer=tok, unet=unet, controlnet=cn,
                  scheduler=sched, safety_checker=None, feature_extractor=None,
                  requires_safety_checker=False)

    txt = StableDiffusionControlNetPipeline(**common)
    img = StableDiffusionControlNetImg2ImgPipeline(**common)

    for p in (txt, img):
        p.set_progress_bar_config(disable=True)
        p.enable_vae_slicing()
        p.enable_vae_tiling()

        # ATTENTION SLICING IS OFF BY DEFAULT AND THAT IS A MEASUREMENT, NOT A GUESS.
        # torch 2.6 routes attention through scaled_dot_product_attention, which is already
        # memory-efficient; enable_attention_slicing("max") then serialises it one head at
        # a time on top of that and cost 3m22s a cel against 1m03s without, for memory this
        # card did not need once the text encoder and VAE were off it. Kept as a flag for
        # the case where it does.
        if slicing:
            p.enable_attention_slicing("max")

    if offload:
        # The text encoder and the VAE live on the CPU and are walked over only at the ends
        # of the run, leaving the UNet and the ControlNet the whole card for the denoising
        # loop. WITHOUT this all four sit on the card, the last few hundred MB spill into
        # the WDDM system-memory fallback, and the GPU sits at 0% utilisation moving pages
        # over PCIe - six minutes without finishing one frame. Applied to the txt2img
        # pipeline; the img2img one shares every module, so the hooks fire for it too.
        txt.enable_model_cpu_offload()

    return txt, img


def init_frame(cel, size, bg, inner=None, dilate_frac=0.14, blur=90):
    """The frame cel 0 is denoised from: a green field with a soft neutral-grey island
    where the figure is going to be.

    A UNIFORMLY green init does not work - it was tried, and at strength 0.82 the green
    came through the figure as well, producing a man in a green suit on a green wall. The
    background's colour has to be fixed WITHOUT fixing the subject's, so the island is cut
    from the original cel's own alpha mask, mapped into the canvas by the same (scale,
    offset) tools/sprite_pose.py used for the skeleton, dilated by a fifth of the figure's
    width and blurred very hard.

    THE BLUR IS THE PARAMETER THAT MATTERS and it took three goes. At 25 pixels the island
    still had an edge, and the model drew that edge: the figure came back standing in front
    of a brown cut-out board in exactly the island's shape. At 90 the transition is spread
    over two hundred pixels and there is no boundary left to draw, while the green outside
    it is still flat enough to fix the background.

    This is the mask being used as a HINT, not a stencil. It says "the person is roughly
    here"; the pose control says what they are doing; nothing about the original's pixels
    survives into the image.
    """
    import numpy as np
    from PIL import Image, ImageFilter

    W, H = size
    p1 = os.path.join(ASSETS_1X,
                      f"view_{cel['view']}_l{cel['loop']}_c{cel['cel']}.png")
    m = Image.new("L", (W, H), 0)
    if os.path.exists(p1):
        a = np.asarray(Image.open(p1).convert("RGBA"))[:, :, 3] > 128
        s, ox, oy = cel["fit"]
        # Dilated in the CEL's own pixels, where the figure is forty across, so the margin
        # stays a margin. Doing it after the 9.6x scale-up, or with a fifth of the width
        # rather than a tenth, produced an island covering nine tenths of the canvas and
        # the green never reached the model at all.
        cw = max(1, int(a.any(axis=0).sum()))
        pad = max(1, int(round(cw * dilate_frac)))
        src = Image.fromarray((a * 255).astype(np.uint8), "L")
        src = src.filter(ImageFilter.MaxFilter(min(21, pad * 2 + 1)))
        src = src.resize((max(1, int(round(src.width * s))),
                          max(1, int(round(src.height * s)))), Image.BILINEAR)
        m.paste(src, (int(round(ox)), int(round(oy))))
    m = m.filter(ImageFilter.GaussianBlur(blur))

    # THE ISLAND IS NOISE, NOT FLAT GREY. Flat grey is structure, and at a strength low
    # enough to hold the background it survives - the first attempt printed the island
    # itself into the picture as a pair of enormous grey wings behind the figure. Noise
    # has nothing to survive: it biases no colour and preserves no shape, so the region
    # under it is as free as pure txt2img while the flat green outside it still fixes the
    # background. Seeded, so the init is the same every run.
    #
    # `inner` replaces the noise with cel 0, which is how cels 1-3 are built. Feeding them
    # cel 0 WHOLE, background and all, was the obvious thing and it is wrong: img2img
    # re-noises the whole frame, so the flat field came back mottled and streaked and would
    # not key - the figure was clean and the background was not. Pasting only the ISLAND of
    # cel 0 over a fresh flat field gives the next cel the identity it needs and the same
    # untouched background every time.
    if inner is None:
        rng = np.random.default_rng(20250928)
        island = Image.fromarray(
            np.clip(rng.normal(140, 42, (H, W, 3)), 0, 255).astype(np.uint8), "RGB")
    else:
        island = inner.convert("RGB")

    field = Image.new("RGB", (W, H), tuple(bg))
    return Image.composite(island, field, m)


def generate(args):
    from PIL import Image

    # torch is a 2.4GB install; --dry exists so the cast and the prompts can be reviewed
    # before any of it is needed.
    torch = None
    device = "cpu"
    if not args.dry:
        import torch
        device = "cuda" if torch.cuda.is_available() else "cpu"
        print(f"device: {device}  "
              f"{torch.cuda.get_device_name(0) if device == 'cuda' else ''}", flush=True)

    with open(os.path.join(args.poses, "poses.json")) as f:
        poses = json.load(f)
    W, H = poses["size"]

    os.makedirs(args.out, exist_ok=True)
    txt, img = (None, None) if args.dry else build(device, args.offload, args.slicing)

    want = set(args.bodies.split(",")) if args.bodies else set(CAST)
    made = {}

    for key, cel in sorted(poses["cels"].items(),
                           key=lambda kv: (kv[1]["view"], kv[1]["loop"], kv[1]["cel"])):
        body = cel["body"]
        if body not in want:
            continue
        who = CAST[body]
        oi = ["best", "mid", "cheap", "undressed"].index(cel["outfit"])
        prompt = f"{who['who']}, {who['wear'][oi]}, {who['style']}"

        # CLIP truncates at 77 and says so in a warning nobody reads. Check it here, where
        # it is fatal, because what falls off the end is the background clause and the
        # symptom is a sprite that cannot be cut out.
        if txt is not None:
            n = len(txt.tokenizer(prompt).input_ids)
            if n > txt.tokenizer.model_max_length:
                raise SystemExit(
                    f"prompt for {body}/{cel['outfit']} is {n} tokens, over CLIP's "
                    f"{txt.tokenizer.model_max_length}; shorten CAST or STYLE:\n  {prompt}")

        v, l, c = cel["view"], cel["loop"], cel["cel"]
        dst = os.path.join(args.out, f"raw_{v}_l{l}_c{c}.png")
        if os.path.exists(dst) and not args.force:
            made[key] = dst
            continue

        pose = Image.open(os.path.join(args.poses, f"pose_{v}_l{l}_c{c}.png")).convert("RGB")
        if args.dry:
            print(f"{key}  {body}/{cel['outfit']}  seed {who['seed']}")
            print(f"    {prompt[:150]}...")
            continue

        gen = torch.Generator(device=device).manual_seed(who["seed"])
        first = os.path.join(args.out, f"raw_{v}_l{l}_c0.png")

        if c == 0 or args.no_img2img or not os.path.exists(first):
            # CEL 0 IS DENOISED FROM A FLAT FIELD WITH A NOISE ISLAND, AND THE FIELD IS
            # NEUTRAL GREY. Both halves of that were arrived at by getting them wrong.
            #
            # The island earns its place on COMPOSITION, which was not why it was added.
            # SD1.5 at 512x1024 does the well-known thing at non-square aspect ratios and
            # draws the subject twice, stacked; ControlNet reduces it but does not fix it,
            # and --init none came back with a smeared, incoherent figure. A blurred island
            # where the person goes is enough of an anchor to stop that.
            #
            # The field was GREEN for a while, to make the background keyable. That was a
            # mistake in three directions at once: green crept over the figure, the
            # island's own outline printed as a coloured board behind it, and the prompt
            # stopped being obeyed - a charcoal three-piece suit with a burgundy tie came
            # back beige. A neutral field costs none of that, and the cutout does not need
            # it: tools/fit_walkers.py mattes against the original cel's silhouette, which
            # does not care what colour the background is.
            if args.init != "none":
                out = img(prompt=prompt, negative_prompt=NEGATIVE,
                          image=init_frame(cel, (W, H), args.bg),
                          control_image=pose, strength=args.bg_strength,
                          num_inference_steps=max(args.steps,
                                                  int(args.steps / args.bg_strength)),
                          guidance_scale=args.cfg,
                          controlnet_conditioning_scale=args.control,
                          generator=gen).images[0]
            else:
                out = txt(prompt=prompt, negative_prompt=NEGATIVE, image=pose,
                          width=W, height=H, num_inference_steps=args.steps,
                          guidance_scale=args.cfg,
                          controlnet_conditioning_scale=args.control,
                          generator=gen).images[0]
        else:
            # Cels 1-3 rebuild the SAME person under a new pose. Strength is the whole
            # trade: too low and the legs never move, too high and the face is redrawn.
            # The WHOLE of cel 0 goes in, background included, which also keeps the four
            # cels' backgrounds alike - and the matte downstream wants that.
            base = Image.open(first).convert("RGB")
            out = img(prompt=prompt, negative_prompt=NEGATIVE, image=base,
                      control_image=pose, strength=args.strength,
                      num_inference_steps=max(args.steps, int(args.steps / args.strength)),
                      guidance_scale=args.cfg,
                      controlnet_conditioning_scale=args.control,
                      generator=gen).images[0]

        out.save(dst)
        made[key] = dst
        print(f"  {key}  {body}/{cel['outfit']}", flush=True)
        if device == "cuda":
            torch.cuda.empty_cache()

    if not args.dry:
        with open(os.path.join(args.out, "generated.json"), "w") as f:
            json.dump({"cast": {k: {kk: vv for kk, vv in v.items() if kk != "style"}
                                for k, v in CAST.items()},
                       "size": [W, H], "steps": args.steps, "cfg": args.cfg,
                       "control": args.control, "strength": args.strength,
                       "made": sorted(made)}, f, indent=1)
    print(f"{len(made)} frames")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--poses")
    ap.add_argument("--out")
    ap.add_argument("--bodies", help="comma separated: body0,body1,body2,body3,jones")
    ap.add_argument("--steps", type=int, default=28)
    ap.add_argument("--cfg", type=float, default=7.0)
    ap.add_argument("--control", type=float, default=1.0)
    ap.add_argument("--strength", type=float, default=0.42,
                    help="img2img strength for cels 1-3 against cel 0; low holds identity, "
                         "high lets the pose move the legs")
    ap.add_argument("--init", choices=("none", "island"), default="island",
                    help="'none' denoises cel 0 from pure noise; at this aspect ratio that "
                         "composes badly - see the comment at the call site")
    ap.add_argument("--bg", type=int, nargs=3, default=[196, 196, 198],
                    help="the flat field outside the island")
    ap.add_argument("--bg-strength", type=float, default=0.86,
                    help="how far cel 0 departs from that flat field; 1.0 is pure txt2img "
                         "and loses the background entirely")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--dry", action="store_true")
    ap.add_argument("--offload", action="store_true",
                    help="keep the text encoder and VAE on the CPU; ~0.4GB less VRAM, and "
                         "on a 4GB card the difference between running and page-thrashing")
    ap.add_argument("--slicing", action="store_true",
                    help="slice the attention as well; three times slower, only needed if "
                         "--offload alone still will not fit")
    ap.add_argument("--no-img2img", action="store_true",
                    help="generate every cel from noise instead of from cel 0 - use to "
                         "measure what the img2img chain is actually buying")
    a = ap.parse_args()
    if not a.poses or not a.out:
        ap.error("--poses and --out are required")
    generate(a)


if __name__ == "__main__":
    main()
