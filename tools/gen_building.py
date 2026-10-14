"""One generator for every board building. The differences are in tools/buildings.py.

    python tools/gen_building.py --who socketcity --cels <celdir> --out <dir> --dry
    python tools/gen_building.py --who socketcity --cels <celdir> --out <dir>
    python tools/gen_building.py --who socketcity --cels <celdir> --out <dir> --device bulb

WHAT THIS KEEPS FROM THE THREE HAND-WRITTEN GENERATORS IT REPLACES
--------------------------------------------------------------------
Every one of these cost at least one GPU round to learn, and each is now in one place
instead of three:

  * generate at exactly SCALE x the region, so the downscale is a clean box mean;
  * the init is a SYNTHETIC block painting, never Sierra's cel upscaled - denoising from
    1990 pixel art returns 1990 pixel art at every strength that preserves layout;
  * blur by the LITERAL canvas fraction, GEN_W/60. Re-deriving it in cel units made the
    init blurrier relative to its frame and produced an out-of-focus render;
  * no colour word in the positive prompt;
  * `flat` stays in the negative, and both prompts are token-checked at runtime - CLIP
    truncates the NEGATIVE as silently as the positive;
  * the init needs TONAL RANGE: one with no light in it anywhere returns an image with
    none;
  * NO BRIGHT HORIZONTAL BAND low in the frame - that is what haze looks like, and the
    model painted mist over a building's base for two rounds through `smoke, steam, fog`
    in the negative;
  * the blend keeps Sierra's surroundings by SILHOUETTE, never by bounding box;
  * nothing in an init is drawn thinner than 2px, because a mass thinner than the blur was
    never in the init at all.
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from buildings import BUILDINGS, NEGATIVE

MODELS = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
                      "jones-upscale-models", "gen")

# The devices are generated separately and composited at the lettering stage, so one
# facade serves every candidate. Each is a tiny init plus a material prompt of its own.
DEVICES = {
    "plug": {
        "prompt": ("a photograph of a red moulded electrical plug with brass pins and a "
                   "trailing flex, studio lighting, sharp focus, fine detail, product "
                   "photography on a plain background"),
        "init": [(0, 0, 23, 31, "BG"), (4, 8, 19, 27, "BODY"), (8, 2, 10, 8, "PIN"),
                 (13, 2, 15, 8, "PIN"), (9, 26, 14, 31, "FLEX")],
        "palette": {"BG": (0xE8, 0xE8, 0xE8), "BODY": (0xC0, 0x38, 0x38),
                    "PIN": (0xC8, 0xB0, 0x60), "FLEX": (0x90, 0x28, 0x28)},
    },
    "bulb": {
        "prompt": ("a photograph of a clear filament light bulb with a glowing tungsten "
                   "coil and a brass screw base, studio lighting, sharp focus, fine "
                   "detail, product photography on a plain background"),
        "init": [(0, 0, 23, 31, "BG"), (5, 2, 18, 19, "GLASS"), (9, 8, 14, 15, "COIL"),
                 (8, 20, 15, 27, "BRASS"), (9, 28, 14, 31, "TIP")],
        "palette": {"BG": (0xE8, 0xE8, 0xE8), "GLASS": (0xF0, 0xE8, 0xC8),
                    "COIL": (0xF8, 0xC8, 0x50), "BRASS": (0xC0, 0xA0, 0x58),
                    "TIP": (0x60, 0x58, 0x50)},
    },
}


def paint(boxes, palette, w, h):
    a = np.zeros((h, w, 3), np.uint8)
    for (x0, y0, x1, y1, name) in boxes:
        a[max(0, y0):y1 + 1, max(0, x0):x1 + 1] = palette[name]
    return a


def synthetic_init(b):
    _, _, w, h = b.region
    a = paint(b.init, b.palette, w, h)
    # PAD THE CANVAS TOWARDS SQUARE, THEN CROP BACK. CAST.md 6: SD1.5 is trained at 512x512
    # and degrades with distance from the subject centre at extreme aspects - at 1:2 a
    # figure came back flawless at the head and dissolved below the thigh.
    #
    # The Rent Office's cel is 41x22, so its canvas is 1.86:1 and it came back soft where
    # the Factory's 1.05:1 came back sharp. Raising the pixel count was tried first and
    # RULED OUT by measurement: at scale 24 the canvas is 519,552 px against 230,912 and
    # the edge-width ratio at the shipping rung is 0.315 against 0.320, which is nothing.
    #
    # `pad` adds rows above and below, filled by repeating the init's own top and bottom
    # rows so no new instruction is introduced, and the generation is cropped back to the
    # region before anything downstream sees it.
    pt, pb = getattr(b, "pad", (0, 0))
    if pt or pb:
        a = np.concatenate(
            ([np.repeat(a[:1], pt, axis=0)] if pt else []) + [a] +
            ([np.repeat(a[-1:], pb, axis=0)] if pb else []), axis=0)
        h = a.shape[0]
    small = Image.fromarray(a, "RGB")
    gen_w, gen_h = w * b.scale, h * b.scale
    big = small.resize((gen_w, gen_h), Image.LANCZOS)
    r = gen_w / 60.0
    # THE CONDITIONING IS BUILT FROM A **NEAREST** UPSCALE, AND THIS IS THE CAUSE OF THE
    # SOFTNESS ZAIN REJECTED. It was computed from the LANCZOS upscale of a 39x28 block
    # painting, after that image's gen_w/60 Gaussian. A 16x LANCZOS upscale has no hard
    # edge in it anywhere - every boundary is a sixteen-pixel ramp because there are sixteen
    # generated pixels per cel pixel - so the softedge map handed to ControlNet was telling
    # the model that EVERY structural boundary in the building is sixteen pixels wide. At a
    # conditioning scale of 0.35 it painted exactly that.
    #
    # Measured, on the p99 of the one-pixel luminance step over the whole frame (a real
    # photograph runs 20-60; Sierra's upscaled 12x cels run 11-20):
    #
    #     control 0.55, LANCZOS conditioning      p99 = 11   - no hard edge in the frame
    #     control 0.15, LANCZOS conditioning      p99 = 28   - sharp
    #
    # So the scale was the lever, not the blur radius: reducing `cond_blur` to a quarter
    # did nothing (0.301 against 0.281) because the softness is in the UPSCALE and not in
    # the Gaussian. But control 0.15 is not the answer either - with the structure that
    # weakly enforced the model asserted its own shopfront prior and painted GARBLED TEXT
    # across the fascia, which is the one thing this whole programme exists to avoid.
    #
    # NEAREST gives hard block boundaries, so the edge map is thin lines in the right
    # places instead of wide ramps everywhere, and the conditioning scale can stay high
    # enough to hold the massing. The init the model DENOISES from is untouched and still
    # LANCZOS-and-Gaussian: a hard-edged init produces hard-edged artefacts, which is why
    # it was smooth in the first place. Only the control image changes.
    cond = small.resize((gen_w, gen_h), Image.NEAREST).filter(
        ImageFilter.GaussianBlur(max(0.6, r * b.cond_blur)))
    return small, big.filter(
        ImageFilter.GaussianBlur(max(0.6, r * getattr(b, "init_blur", 1.0)))), cond


def soft_edges(im):
    g = np.asarray(im.convert("L"), np.float32)
    gx = np.zeros_like(g)
    gy = np.zeros_like(g)
    gx[:, 1:-1] = g[:, 2:] - g[:, :-2]
    gy[1:-1, :] = g[2:, :] - g[:-2, :]
    mag = np.hypot(gx, gy)
    mag = np.clip(mag / max(1.0, np.percentile(mag, 99.0)), 0.0, 1.0) ** 0.7
    e = Image.fromarray((mag * 255.0).astype(np.uint8), "L").filter(
        ImageFilter.GaussianBlur(0.8))
    return Image.merge("RGB", (e, e, e))


def silhouette(b, sierra, scale=1):
    """Which pixels of the region are BUILDING. `surround` says how to tell.

    A bounding box is not an option for an irregular building: one pasted a hard-edged slab
    of brick over the Factory, the worst-looking thing produced in this work."""
    a = np.asarray(sierra.convert("RGB")).astype(int)
    r, g, bl = a[..., 0], a[..., 1], a[..., 2]
    mode = b.surround["mode"]
    if mode == "none":
        m = np.ones(a.shape[:2], bool)
    elif mode == "blue_light":
        m = ~((bl > r) & (bl > g) & (bl > b.surround["b_min"]))
    elif mode == "green":
        s = b.surround
        m = ~((g - np.maximum(r, bl) > s["g_over"]) & (g < s["g_max"]))
    elif mode == "rects":
        m = np.zeros(a.shape[:2], bool)
        for (x0, y0, x1, y1) in b.surround["keep"]:
            m[y0:y1 + 1, x0:x1 + 1] = True
    else:
        raise SystemExit("unknown surround mode %r" % mode)
    # `force` is unioned in AFTER the test, for a region that holds two things. The Factory
    # is the only one so far: its building is background art and its sign is a separate cel
    # stamped over the yard, so a colour test over cel 0 calls the sign's own footprint
    # "scene" and would hand the old sign straight back. The sign's ground must be
    # GENERATED, or the new board has nothing of its own to cast a shadow onto.
    for (x0, y0, x1, y1) in b.surround.get("force", []):
        m[y0:y1 + 1, x0:x1 + 1] = True
    if scale != 1:
        m = np.kron(m, np.ones((scale, scale), bool))
    return m


def sierra_region(b, celdir):
    """Sierra's own pixels for the region, from the cel or from the background."""
    x, y, w, h = b.region
    if b.source.startswith("cel:"):
        pre = "cel_%s_" % b.source[4:]
        hit = [n for n in os.listdir(celdir) if n.startswith(pre)]
        if not hit:
            raise SystemExit("%s* not found in %s" % (pre, celdir))
        im = Image.open(os.path.join(celdir, hit[0])).convert("RGB")
        if im.size != (w, h):
            raise SystemExit("%s is %s, the table says %s" % (hit[0], im.size, (w, h)))
        return im
    bg = Image.open(os.path.join(celdir, "cel_00_319x199_at_1_0.png")).convert("RGB")
    return bg.crop((x - 1, y, x - 1 + w, y + h))      # cel 0 is stamped at (1,0)


def claim_card(out):
    """Refuse while another python holds the card. Only a python counts: dwm, Teams and the
    Edge WebView are permanently on this GPU."""
    import subprocess
    try:
        r = subprocess.run(["nvidia-smi", "--query-compute-apps=pid,process_name",
                            "--format=csv,noheader"],
                           capture_output=True, text=True, timeout=30)
        rows = [l.strip() for l in r.stdout.splitlines() if l.strip()]
    except (OSError, subprocess.SubprocessError):
        rows = []
    mine = os.getpid()
    busy = [l for l in rows
            if (lambda p: len(p) >= 2 and p[0].isdigit() and int(p[0]) != mine
                and "python" in os.path.basename(p[1]).lower())
            ([q.strip() for q in l.split(",")])]
    if busy:
        raise SystemExit("another python is on the card - refusing:\n  " + "\n  ".join(busy))
    try:
        import gen_cast
        gen_cast.claim_gpu(out)
    except ImportError:
        pass


def run(prompt, init_big, cond, strength, steps, cfg, control, seed,
        negative=None):
    import torch
    import gen_walkers as gw
    device = "cuda" if torch.cuda.is_available() else "cpu"
    txt, img = gw.build(device, offload=True, slicing=True,
                        controlnet=os.path.join(MODELS, "cn-softedge"))
    neg = negative or NEGATIVE
    for what, s in (("positive", prompt), ("negative", neg)):
        n = len(txt.tokenizer(s).input_ids)
        if n > txt.tokenizer.model_max_length:
            raise SystemExit("%s prompt is %d tokens, over CLIP's %d"
                             % (what, n, txt.tokenizer.model_max_length))
        print("  %s prompt: %d/%d tokens" % (what, n, txt.tokenizer.model_max_length))
    g = torch.Generator(device).manual_seed(seed)
    return img(prompt=prompt, negative_prompt=neg, image=init_big, control_image=cond,
               strength=strength, num_inference_steps=max(steps, int(steps / strength)),
               guidance_scale=cfg, controlnet_conditioning_scale=control,
               generator=g).images[0]


def gen_device(name, out, args):
    """A small separate generation per candidate device, so one facade serves them all."""
    spec = DEVICES[name]
    W, H, S = 24, 32, 16
    a = paint(spec["init"], spec["palette"], W, H)
    small = Image.fromarray(a, "RGB")
    big = small.resize((W * S, H * S), Image.LANCZOS).filter(
        ImageFilter.GaussianBlur(W * S / 60.0))
    big.save(os.path.join(out, "device_%s_init.png" % name))
    if args.dry:
        return
    img = run(spec["prompt"], big, soft_edges(big), args.strength, args.steps,
              args.cfg, args.control, args.seed)
    img.save(os.path.join(out, "device_%s_%dx%d.png" % (name, W * S, H * S)))
    print("  device '%s': %dx%d" % (name, W * S, H * S))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--who", required=True, choices=sorted(BUILDINGS))
    ap.add_argument("--cels", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--dry", action="store_true")
    ap.add_argument("--devices-only", action="store_true")
    ap.add_argument("--strength", type=float, default=None)
    ap.add_argument("--control", type=float, default=None)
    ap.add_argument("--cfg", type=float, default=None)
    ap.add_argument("--steps", type=int, default=None)
    ap.add_argument("--seed", type=int, default=None)
    ap.add_argument("--reviewed", default=None,
                    help="proceed despite a no-text-guard FAIL, stating what was seen at "
                         "native resolution; the finding is still printed and recorded")
    args = ap.parse_args()

    b = BUILDINGS[args.who]
    for k in ("strength", "control", "cfg", "steps", "seed"):
        if getattr(args, k) is None:
            setattr(args, k, getattr(b, k))
    if not b.init:
        raise SystemExit("%s has no init in the table - it shipped before the extraction "
                         "and its generator is still tools/gen_%s.py" % (b.key, b.key))

    os.makedirs(args.out, exist_ok=True)
    x, y, w, h = b.region
    _pt, _pb = getattr(b, "pad", (0, 0))
    gen_w, gen_h = w * b.scale, (h + _pt + _pb) * b.scale
    print("%s -> %s" % (b.sierra, b.new_name))
    print("  region %dx%d at game (%d,%d), source %s" % (w, h, x, y, b.source))
    print("  generate %dx%d = %d px, blur %.1f = %.2f%% of width"
          % (gen_w, gen_h, gen_w * gen_h, gen_w / 60.0, 100.0 / 60.0))
    if gen_w * gen_h > 810000:
        raise SystemExit("  %d px is over the measured 810k ceiling for this 4GB card"
                         % (gen_w * gen_h))

    init_small, init_big, cond_src = synthetic_init(b)
    init_small.save(os.path.join(args.out, "init_%dx%d.png" % (w, h)))
    init_big.save(os.path.join(args.out, "init_%dx%d.png" % (gen_w, gen_h)))
    cond = soft_edges(cond_src)
    cond.save(os.path.join(args.out, "cond_softedge.png"))
    print("  cond blur %.1f px (%.2f x the init's %.1f)"
          % (max(0.6, gen_w / 60.0 * b.cond_blur), b.cond_blur, gen_w / 60.0))

    sierra = sierra_region(b, args.cels)
    m = silhouette(b, sierra)
    print("  silhouette (%s): %d of %d px are building (%.0f%%)"
          % (b.surround["mode"], int(m.sum()), w * h, 100.0 * m.sum() / (w * h)))
    if m.sum() < 0.5 * w * h:
        print("  NOTE: under half the region is building. Generating the HOTSPOT rather "
              "than the BUILDING is what cost the Factory six rounds - check `region`.")

    for d in b.devices:
        gen_device(d, args.out, args)

    if args.dry:
        print("--dry: inits written, no GPU used")
        return
    if args.devices_only:
        return

    claim_card(args.out)
    print("  strength %.2f control %.2f cfg %.1f steps %d seed %d"
          % (args.strength, args.control, args.cfg, args.steps, args.seed))
    big = run(b.prompt, init_big, cond, args.strength, args.steps, args.cfg,
              args.control, args.seed, getattr(b, "negative", None))
    # CROP THE PADDING AWAY. Everything downstream - the matte, the sign, the installer -
    # expects `generated_{w*scale}x{h*scale}.png` to be exactly the region.
    pt, pb = getattr(b, "pad", (0, 0))
    if pt or pb:
        big = big.crop((0, pt * b.scale, gen_w, big.height - pb * b.scale))
        print("  cropped %d/%d padded cel rows back off" % (pt, pb))
    gen_h = h * b.scale
    big.save(os.path.join(args.out, "generated_%dx%d.png" % (gen_w, gen_h)))

    # THE MODEL WRITES ON THE BUILDING WHEN IT IS FREE ENOUGH TO BE SHARP, and the blank
    # band does not stop it - it only stops it in the band. The Pawn Shoppe came back with
    # `PADEMOKIK BATEWEIS ILP` across the course below the sign. This fails the build on it,
    # at the generation's own resolution, where the glyphs are big enough to detect.
    import no_text_guard
    ex = None
    if b.sign:
        sx0, sy0, sx1, sy1 = b.sign["rect"]
        ex = (sx0 * b.scale, sy0 * b.scale, (sx1 + 1) * b.scale - 1, (sy1 + 1) * b.scale - 1)
    hits = no_text_guard.check(big, b.scale, ex)

    region = big.resize((w, h), Image.BOX)
    region.save(os.path.join(args.out, "region_unblended_%dx%d.png" % (w, h)))
    blended = Image.fromarray(
        np.where(m[..., None], np.asarray(region), np.asarray(sierra)).astype(np.uint8),
        "RGB")
    blended.save(os.path.join(args.out, "region_%dx%d.png" % (w, h)))
    print("  wrote region_%dx%d.png - the building in Sierra's surroundings" % (w, h))
    if hits:
        # AN OVERRIDE THAT HAS TO SAY WHAT IT SAW, because the alternative was worse.
        #
        # The guard has now had FOUR false-positive classes: window panes, mullion grids,
        # slate roof seams and - here - COURSES OF BRICK. Each of the first three had a
        # principled discriminator. Brick does not: a course of bricks is a row of
        # similar, evenly spaced, aligned blobs, which is the definition of a line of text
        # by every geometric measure available. The obvious next test, "masonry tiles
        # vertically and a sentence does not", was measured and FAILED - the brick frame
        # yields one candidate row, not many.
        #
        # So the choice was to tune a fifth threshold until this particular frame passed,
        # which is how a guard stops guarding, or to make the override explicit and
        # auditable. This is the second. `--reviewed` demands a sentence naming what was
        # looked at and what it was, prints it beside the finding, and is the only way past.
        if not args.reviewed:
            # AND THE REFUSED FILE IS RENAMED, because "do not install them" is an
            # instruction and instructions get skipped.
            #
            # This refusal used to leave the art at `generated_WxH.png` - the exact name
            # every downstream tool opens - while printing a line asking the operator not to
            # use it. I then did precisely that: chained the install after the generation
            # with `;`, which in PowerShell runs the next command whatever the last one
            # returned, and installed a facade the guard had just refused. The guard worked;
            # the handover did not.
            #
            # A file that must not be consumed should not be sitting under the name that
            # means "consume me". Renaming it makes the refusal structural rather than
            # advisory - downstream opens the usual name, finds nothing, and stops.
            bad = os.path.join(args.out, "generated_%dx%d.png" % (gen_w, gen_h))
            quarantine = os.path.join(args.out,
                                      "REFUSED_generated_%dx%d.png" % (gen_w, gen_h))
            if os.path.exists(bad):
                if os.path.exists(quarantine):
                    os.remove(quarantine)
                os.replace(bad, quarantine)
            raise SystemExit("  REFUSING: the model has written on the building.\n"
                             "  The art is quarantined as REFUSED_generated_%dx%d.png so "
                             "nothing downstream can pick it up;\n"
                             "  region_*.png are left beside it for inspection.\n"
                             "  If this is a false positive, look at it AT NATIVE "
                             "RESOLUTION and re-run with --reviewed \"<what you saw>\"."
                             % (gen_w, gen_h))
        print("  OVERRIDDEN BY REVIEW: %s" % args.reviewed)
        print("  (the guard's finding above stands on the record; it was not retuned)")


if __name__ == "__main__":
    main()

