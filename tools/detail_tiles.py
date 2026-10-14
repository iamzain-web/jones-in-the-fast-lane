r"""STAGE B: the detail pass, tiled, for a region too big to generate at the shipping rung.

    python tools/detail_tiles.py --who towncentre --dry        # plan the grid, no GPU
    python tools/detail_tiles.py --who towncentre              # run it
    python tools/detail_tiles.py --who towncentre --strength 0.30

Reads  tools/samples/<key>-rename/generated_<w*scale>x<h*scale>.png   (stage A)
Writes tools/samples/<key>-rename/detailed_<w*DF>x<h*DF>.png          (stage B)

`install_building.py` prefers `detailed_` when `Building.detail_scale` is set.

WHY THERE ARE TWO STAGES
------------------------
The town centre is 181x110 game px. At the shipping rung that is 2,172x1,320 = 2.87M px,
three and a half times this 4GB card's measured 810k ceiling, so it cannot be generated at
12 in one pass. Stage A therefore runs at 6 - the only scale at which ONE generation sees
the whole centre, which is what makes the layout, the light and the palette coherent.

Stage B adds SURFACE, not composition. Every tile starts from the same stage-A image,
upscaled, and is denoised at a strength low enough that it can only sharpen what is there:
tiles onto a roof, a body onto a car, a canopy onto a tree. It cannot move a street.

WHERE THE JOINS GO, AND WHY THAT IS A DESIGN DECISION RATHER THAN A SETTING
---------------------------------------------------------------------------
Two adjacent tiles are denoised independently, so a core boundary between them is a join
like any other in this pipeline - and the rule is that every join lands on a boundary the
scene already has, and nothing is blurred to hide one. So the TOWN WAS DESIGNED AROUND ITS
TILE GRID: the init's plaza was widened from 59 px to 63 so that x 60 and x 120 fall inside
it, and the streets were later laid so that all three joins - x 60, x 120, y 54 - sit on a
street's own centre marking. A seam in a painted line on tarmac is a seam in a uniform
surface, which is the one place it cannot be seen.

Each tile is cropped with an OVERLAP for context and writes back only its CORE, so the
model always sees past the join it is working next to.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_building as gb
from buildings import BUILDINGS

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def grid(b, W=None, H=None):
    """[(name, core_px, read_px)] in PIXELS on the PADDED canvas.

    Everything here is a multiple of 8 and that is not decoration: stage B samples ONE
    latent noise tensor for the whole image and hands each tile its own crop of it, so that
    neighbours denoise from the SAME randomness where they overlap. A latent pixel is 8
    image pixels, so a tile whose origin or size is not a multiple of 8 cannot take an
    aligned crop, and the shared field stops being shared.

    The cores and the overlap are multiples of 8 already (x 720/1440, y 648, overlap 120).
    Only the region's own width is not - 2172 is 271.5 latent pixels - so the canvas is
    padded by four columns on the right and cropped back at the end.
    """
    _, _, w, h = b.region
    DF = b.detail_scale
    t = b.tiles
    W = W if W is not None else w * DF
    H = H if H is not None else h * DF
    ov = t.get("overlap", 6) * DF
    xs = [0] + [c * DF for c in t["cols"]] + [w * DF]
    ys = [0] + [r * DF for r in t["rows"]] + [h * DF]

    def span(c0, c1, N):
        a = max(0, c0 - ov) // 8 * 8
        b_ = min(N, -(-(c1 + ov) // 8) * 8)
        return a, b_

    out = []
    for r in range(len(ys) - 1):
        for c in range(len(xs) - 1):
            cx0, cx1 = xs[c], xs[c + 1]
            cy0, cy1 = ys[r], ys[r + 1]
            rx0, rx1 = span(cx0, cx1, W)
            ry0, ry1 = span(cy0, cy1, H)
            out.append(("r%dc%d" % (r, c), (cx0, cy0, cx1, cy1), (rx0, ry0, rx1, ry1)))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--who", required=True, choices=sorted(BUILDINGS))
    ap.add_argument("--dry", action="store_true")
    ap.add_argument("--strength", type=float, default=None)
    ap.add_argument("--control", type=float, default=None)
    ap.add_argument("--steps", type=int, default=None)
    ap.add_argument("--cfg", type=float, default=None)
    ap.add_argument("--seed", type=int, default=None)
    a = ap.parse_args()

    b = BUILDINGS[a.who]
    if not getattr(b, "tiles", None):
        raise SystemExit("%s has no `tiles` in the table" % b.key)
    _, _, w, h = b.region
    DF = b.detail_scale
    S = b.scale
    out = os.path.join(ROOT, "tools", "samples", "%s-rename" % b.key)
    src = os.path.join(out, "generated_%dx%d.png" % (w * S, h * S))
    if not os.path.exists(src):
        raise SystemExit("no stage A yet: %s" % src)

    t = b.tiles
    strength = a.strength if a.strength is not None else t.get("strength", 0.35)
    control = a.control if a.control is not None else t.get("control", 0.70)
    steps = a.steps if a.steps is not None else t.get("steps", 28)
    cfg = a.cfg if a.cfg is not None else t.get("cfg", 7.0)
    seed = a.seed if a.seed is not None else b.seed

    # THE CANVAS IS PADDED TO A MULTIPLE OF 8 and cropped back at the end, so that every
    # tile can take an ALIGNED crop of one shared latent noise field. Four columns on the
    # right, replicated; they are outside the region and never reach the board.
    PW = -(-(w * DF) // 8) * 8
    PH = -(-(h * DF) // 8) * 8
    stageA = Image.open(src).convert("RGB").resize((w * DF, h * DF), Image.LANCZOS)
    base = Image.new("RGB", (PW, PH))
    base.paste(stageA, (0, 0))
    for x in range(w * DF, PW):
        base.paste(stageA.crop((w * DF - 1, 0, w * DF, h * DF)), (x, 0))
    for y in range(h * DF, PH):
        base.paste(base.crop((0, h * DF - 1, PW, h * DF)), (0, y))

    cells = grid(b, PW, PH)
    print("%s: stage A %s -> %dx%d (padded to %dx%d), then %d tiles at strength %.2f"
          % (b.key, Image.open(src).size, w * DF, h * DF, PW, PH, len(cells), strength))
    print("  joins at cel x %s and y %s, overlap %d cel px"
          % (list(t["cols"]), list(t["rows"]), t.get("overlap", 6)))
    worst = 0
    for name, core, read in cells:
        rx0, ry0, rx1, ry1 = read
        px = (rx1 - rx0) * (ry1 - ry0)
        worst = max(worst, px)
        align = "" if (rx0 % 8 or ry0 % 8 or (rx1 - rx0) % 8 or (ry1 - ry0) % 8) else " aligned"
        print("  %-5s core px x %4d..%-4d y %4d..%-4d   read %4dx%-4d = %7d px%s"
              % (name, core[0], core[2], core[1], core[3],
                 rx1 - rx0, ry1 - ry0, px, align))
        if px > 810000:
            raise SystemExit("tile %s is over the 810k ceiling" % name)
    print("  largest tile %d px against the measured 810k ceiling" % worst)
    if a.dry:
        print("--dry: no GPU used")
        return

    gb.claim_card(out)
    import torch
    import gen_walkers as gw

    # ONE NOISE FIELD FOR THE WHOLE IMAGE. Six tiles denoised from six independently
    # sampled latents disagree in the overlap, and the disagreement shows as a drawn line
    # at every core boundary - measured at 8.08/10.83/10.52 against a neighbourhood of 2,
    # and the three seams were then the three strongest edges in the region. Siting them on
    # a uniform surface made it worse rather than better, and the denoise strength barely
    # moved it, so the cause is the randomness itself. Sample it once, globally, and let
    # each tile crop its own: where two tiles overlap they are now solving the same problem
    # from the same start, so they agree there without anything being blended.
    import diffusers.pipelines.controlnet.pipeline_controlnet_img2img as _cn
    _ORIG = _cn.randn_tensor
    _HELD = {"crop": None}

    def _shared(shape, generator=None, device=None, dtype=None, layout=None):
        c = _HELD["crop"]
        if c is not None and tuple(c.shape[-2:]) == tuple(shape[-2:]):
            v = c if c.shape[0] == shape[0] else c.expand(shape[0], -1, -1, -1)
            return v.to(device=device, dtype=dtype)
        return _ORIG(shape, generator=generator, device=device, dtype=dtype, layout=layout)

    _cn.randn_tensor = _shared
    device = "cuda" if torch.cuda.is_available() else "cpu"
    txt, img = gw.build(device, offload=True, slicing=True,
                        controlnet=os.path.join(gb.MODELS, "cn-softedge"))
    for what, p_ in (("positive", b.prompt), ("negative", gb.NEGATIVE)):
        n = len(txt.tokenizer(p_).input_ids)
        print("  %s prompt: %d/%d tokens" % (what, n, txt.tokenizer.model_max_length))
        if n > txt.tokenizer.model_max_length:
            raise SystemExit("  over CLIP's window")

    gcpu = torch.Generator("cpu").manual_seed(seed)
    FIELD = torch.randn((1, 4, PH // 8, PW // 8), generator=gcpu, dtype=torch.float32)
    print("  shared noise field %s, one sample at seed %d" % (tuple(FIELD.shape), seed))

    result = base.copy()
    for name, core, read in cells:
        rx0, ry0, rx1, ry1 = read
        tile = base.crop((rx0, ry0, rx1, ry1))
        cond = gb.soft_edges(tile)
        _HELD["crop"] = FIELD[:, :, ry0 // 8:ry1 // 8, rx0 // 8:rx1 // 8].contiguous()
        g = torch.Generator(device).manual_seed(seed)
        res = img(prompt=b.prompt, negative_prompt=gb.NEGATIVE, image=tile,
                  control_image=cond, strength=strength,
                  num_inference_steps=max(steps, int(steps / strength)),
                  guidance_scale=cfg, controlnet_conditioning_scale=control,
                  generator=g).images[0]
        if res.size != tile.size:
            res = res.resize(tile.size, Image.LANCZOS)

        # LEVEL IS STAGE A'S; ONLY SURFACE IS STAGE B'S - and the kernel must fit INSIDE
        # the overlap or it is edge-clamped exactly where the cores meet, which is the one
        # place it exists to help. 2 cel px against a 120 px margin. See CAST.md 0c-ter.
        sigma = 2.0 * DF
        lo_a = np.asarray(tile.filter(ImageFilter.GaussianBlur(sigma)), np.float32)
        lo_b = np.asarray(res.filter(ImageFilter.GaussianBlur(sigma)), np.float32)
        drift = float(np.abs(lo_a - lo_b).mean())
        res = Image.fromarray(
            np.clip(np.asarray(res, np.float32) + (lo_a - lo_b), 0, 255).astype(np.uint8),
            "RGB")

        cx0, cy0, cx1, cy1 = core
        result.paste(res.crop((cx0 - rx0, cy0 - ry0, cx1 - rx0, cy1 - ry0)), (cx0, cy0))
        print("  %-5s done   level drift corrected: %.2f per channel" % (name, drift))

    _cn.randn_tensor = _ORIG
    result = result.crop((0, 0, w * DF, h * DF))
    p = os.path.join(out, "detailed_%dx%d.png" % (w * DF, h * DF))
    result.save(p)
    print("  wrote %s" % os.path.basename(p))

    import no_text_guard
    no_text_guard.check(result, DF, None)


if __name__ == "__main__":
    main()
