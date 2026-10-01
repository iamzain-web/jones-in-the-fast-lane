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
    small = Image.fromarray(a, "RGB")
    gen_w, gen_h = w * b.scale, h * b.scale
    big = small.resize((gen_w, gen_h), Image.LANCZOS)
    return small, big.filter(ImageFilter.GaussianBlur(gen_w / 60.0))


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


def run(prompt, init_big, cond, strength, steps, cfg, control, seed):
    import torch
    import gen_walkers as gw
    device = "cuda" if torch.cuda.is_available() else "cpu"
    txt, img = gw.build(device, offload=True, slicing=True,
                        controlnet=os.path.join(MODELS, "cn-softedge"))
    for what, s in (("positive", prompt), ("negative", NEGATIVE)):
        n = len(txt.tokenizer(s).input_ids)
        if n > txt.tokenizer.model_max_length:
            raise SystemExit("%s prompt is %d tokens, over CLIP's %d"
                             % (what, n, txt.tokenizer.model_max_length))
        print("  %s prompt: %d/%d tokens" % (what, n, txt.tokenizer.model_max_length))
    g = torch.Generator(device).manual_seed(seed)
    return img(prompt=prompt, negative_prompt=NEGATIVE, image=init_big, control_image=cond,
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
    gen_w, gen_h = w * b.scale, h * b.scale
    print("%s -> %s" % (b.sierra, b.new_name))
    print("  region %dx%d at game (%d,%d), source %s" % (w, h, x, y, b.source))
    print("  generate %dx%d = %d px, blur %.1f = %.2f%% of width"
          % (gen_w, gen_h, gen_w * gen_h, gen_w / 60.0, 100.0 / 60.0))
    if gen_w * gen_h > 810000:
        raise SystemExit("  %d px is over the measured 810k ceiling for this 4GB card"
                         % (gen_w * gen_h))

    init_small, init_big = synthetic_init(b)
    init_small.save(os.path.join(args.out, "init_%dx%d.png" % (w, h)))
    init_big.save(os.path.join(args.out, "init_%dx%d.png" % (gen_w, gen_h)))
    cond = soft_edges(init_big)
    cond.save(os.path.join(args.out, "cond_softedge.png"))

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
              args.control, args.seed)
    big.save(os.path.join(args.out, "generated_%dx%d.png" % (gen_w, gen_h)))

    region = big.resize((w, h), Image.BOX)
    region.save(os.path.join(args.out, "region_unblended_%dx%d.png" % (w, h)))
    blended = Image.fromarray(
        np.where(m[..., None], np.asarray(region), np.asarray(sierra)).astype(np.uint8),
        "RGB")
    blended.save(os.path.join(args.out, "region_%dx%d.png" % (w, h)))
    print("  wrote region_%dx%d.png - the building in Sierra's surroundings" % (w, h))


if __name__ == "__main__":
    main()
