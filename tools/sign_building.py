r"""Stage two of a board building: construct the sign, then marry it to the facade.

    python tools/sign_building.py --who pawnshop --dry        # construct only, no GPU
    python tools/sign_building.py --who pawnshop              # construct, then img2img
    python tools/sign_building.py --who pawnshop --strength 0.30

Reads  tools/samples/<key>-rename/generated_WxH.png   (the facade, sign band blank)
Writes tools/samples/<key>-rename/constructed_WxH.png (the physical sign, built)
       tools/samples/<key>-rename/signed_WxH.png      (after the marrying img2img)

`install_building.py` prefers `signed_*.png`, falls back to `constructed_*.png`, and only
then to `generated_*.png` with the old stamped type.

WHY THERE ARE TWO STAGES AND NOT ONE
------------------------------------
Asking a diffusion model for a sign gets garbled glyphs - that is why the whole programme
generates with the band blank. Stamping the glyphs afterwards gets a decal, which is what
Zain rejected. Splitting it gives each half the job it is actually good at: the LAYOUT is
constructed, deterministically, and can never garble; the LIGHTING is denoised, at a
strength low enough that the letters are only lit and not re-invented.

The ControlNet conditioning for the marrying pass is the soft-edge map of the CONSTRUCTED
image, at a high conditioning scale. That is what pins the glyph outlines through the pass:
the model is handed the letterforms as structure, not asked to imagine them.

AND THE DRIFT IS MEASURED, not hoped for. `--report` prints the authored-versus-recovered
glyph IoU and the centroid shift before and after. If the letters move, the answer is a
lower strength.
"""

import argparse
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_building as gb
import sign_board as sbd
from buildings import BUILDINGS

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


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
    if not b.sign:
        raise SystemExit("%s has no `sign` in the table - it still stamps type" % b.key)
    s = dict(b.sign)
    for k in ("strength", "control", "steps", "cfg"):
        if getattr(a, k) is not None:
            s[k] = getattr(a, k)
    seed = a.seed if a.seed is not None else b.seed

    out = os.path.join(ROOT, "tools", "samples", "%s-rename" % b.key)
    _, _, w, h = b.region
    S = b.scale
    gen_w, gen_h = w * S, h * S
    src = os.path.join(out, "generated_%dx%d.png" % (gen_w, gen_h))
    if not os.path.exists(src):
        raise SystemExit("no facade yet: %s" % src)
    face = Image.open(src).convert("RGB")

    x0, y0, x1, y1 = s["rect"]
    bw, bh = x1 - x0 + 1, y1 - y0 + 1
    print("%s -> %s" % (b.sierra, b.new_name))
    print("  facade %dx%d at %d px per cel px" % (gen_w, gen_h, S))
    print("  board  %dx%d cel px = %.0f%% of the facade's width, %.0f%% of its height"
          % (bw, bh, 100.0 * bw / w, 100.0 * bh / h))

    built, gmask, bmask, info = sbd.build(face, s, S, seed=seed)
    built.save(os.path.join(out, "constructed_%dx%d.png" % (gen_w, gen_h)))
    lx, ly = info["light"]
    print("  light measured off the facade: (%+.2f,%+.2f) - %s%s"
          % (lx, ly, "left" if lx < 0 else "right", " and above" if ly < 0 else " and below"))
    print("  bevel %d px, shadow thrown %.0f px, %d glyph px authored"
          % (info["bevel_px"], info["throw_px"], info["glyph_px"]))
    print("  wrote constructed_%dx%d.png" % (gen_w, gen_h))

    if a.dry:
        print("--dry: no GPU used")
        return

    gb.claim_card(out)
    cond = gb.soft_edges(built)          # SHARP, off the construction - this pins the glyphs
    cond.save(os.path.join(out, "cond_sign.png"))
    print("  marrying: strength %.2f control %.2f cfg %.1f steps %d seed %d"
          % (s["strength"], s["control"], s["cfg"], s["steps"], seed))

    import torch
    import gen_walkers as gw
    device = "cuda" if torch.cuda.is_available() else "cpu"
    txt, img = gw.build(device, offload=True, slicing=True,
                        controlnet=os.path.join(gb.MODELS, "cn-softedge"))
    for what, p in (("positive", s["prompt"]), ("negative", s["negative"])):
        n = len(txt.tokenizer(p).input_ids)
        print("  sign %s prompt: %d/%d tokens" % (what, n, txt.tokenizer.model_max_length))
        if n > txt.tokenizer.model_max_length:
            raise SystemExit("  over CLIP's window")
    g = torch.Generator(device).manual_seed(seed)
    res = img(prompt=s["prompt"], negative_prompt=s["negative"], image=built,
              control_image=cond, strength=s["strength"],
              num_inference_steps=max(s["steps"], int(s["steps"] / s["strength"])),
              guidance_scale=s["cfg"], controlnet_conditioning_scale=s["control"],
              generator=g).images[0]

    # Only the board comes back. The facade was already right and must not be re-denoised.
    keep = np.zeros(bmask.shape, np.float32)
    keep[bmask] = 1.0
    married = sbd.marry_region(built, res, keep, s.get("feather", 7.0))
    married.save(os.path.join(out, "signed_%dx%d.png" % (gen_w, gen_h)))
    print("  wrote signed_%dx%d.png" % (gen_w, gen_h))

    d = sbd.drift(built, married, gmask, bmask, s["ink"], s["face"])
    print("\n  GLYPH DRIFT - did the img2img move the letters?")
    print("    survival IoU (after vs before)  %.3f   (1.000 = untouched)"
          % d["survive_iou"])
    print("    ink area   %6d px -> %6d px   (%+.1f%%)"
          % (d["px_before"], d["px_after"],
             100.0 * (d["px_after"] - d["px_before"]) / max(1, d["px_before"])))
    print("    centroid shifted %.2f high-rung px = %.3f cel px"
          % (d["shift"], d["shift"] / S))
    print("    (for reference, each against the authored mask: %.3f -> %.3f; the "
          "construction\n     draws shade, highlight and body, so neither reaches 1.0)"
          % (d["auth_iou_before"], d["auth_iou_after"]))
    area = abs(d["px_after"] - d["px_before"]) / max(1, d["px_before"])
    if d["survive_iou"] < 0.90 or d["shift"] / S > 0.20 or area > 0.10:
        print("    WARNING: the letters moved. Lower --strength rather than accept this.")


if __name__ == "__main__":
    main()
