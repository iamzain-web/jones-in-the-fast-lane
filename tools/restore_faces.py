r"""
Super-resolve the digitised-photograph art with a neural model, replacing the Mitchell
resample for those cels only.

WHAT THIS IS NOT
----------------
These models INVENT plausible detail. They do not recover what the actor actually
looked like in 1990. A 68x55 cel holds about 3,700 pixels; the 272x220 result holds
59,840. The extra 56,000 came out of the model's training set, not out of Sierra's
resource file. Every eyelash, pore and hair strand below is a guess.

That is a deliberate trade, and it is confined to the art: assets/png (the 1x reference
decode) is never touched, the game's 320x200 coordinate space is untouched, and the
whole pass can be undone by re-running tools/Upscale.

    ORDERING HAZARD: tools/Upscale rewrites EVERY file in assets/png4x with the
    Mitchell resample. Running it after this script silently reverts the faces.
    Run Upscale first, then this. (That is also how you undo this pass.)

    The full chain is now  Upscale -> restore_faces.py -> smooth_walk.py, because
    tools/smooth_walk.py interpolates the walker's in-between frames OUT OF what is
    in assets/png4x at the time. This script does not touch the walkers by default,
    so it does not usually invalidate them - but running it with the `walkers` group
    does, and `python tools/smooth_walk.py --check` will say so.

WHAT IT DOES
------------
The shopkeeper talker views (351 and 353-362) are digitised photographs of live
actors: one loop of 11 cels at 68x55, the mouth positions used for lip sync. That is
121 images, and they are the most face-like art in the game.

    faces    views 351, 353-362        GPEN-BFR-512   68x55  -> 272x220   SHIPPED
    walkers  274-277, 282/286/292/296  RealESRGAN x4  49x95  -> 196x380   REGRESSES
    select   views 499, 500            RealESRGAN x4  183x112 -> 732x448  REGRESSES

Only `faces` is applied by default, and it is the only group that survived measurement.

GPEN-BFR-512 is a blind face restoration model: it takes a 512x512 face and returns a
512x512 face. So the cel is Lanczos-resized up into the square, restored, and Lanczos-
resized back down to exactly 4x its original size. The stretch into a square is undone
exactly by the inverse resize.

Padding the cel to give the head a margin first was tried and is WORSE: these models
expect an FFHQ-style alignment where the face fills the frame, and padding shrinks the
face away from it — eyes come back damaged. Feeding GPEN the RealESRGAN result instead
of the raw cel was also tried and is worse on both fidelity and stability. The plain
route wins; see the numbers this script prints.

The walker figures are whole bodies, not faces, so a face prior is wrong for them and
the `walkers` group uses a general 4x super-resolution net (RealESRGAN) instead. It is a
plain feed-forward convolutional net at exactly 4x, so no resize round trip happens at
all. It still made things worse — see below.

WHY NOT THE OTHER MODELS
------------------------
Measured on views 351/355/359/357, all 11 cels each (roundtrip = mean abs error, 0-255,
after box-downsampling the result back to 68x55 and comparing with the untouched source;
mad_stat = mean abs change between adjacent cels over pixels the SOURCE left exactly
unchanged, i.e. how much the model repaints from frame to frame):

    mitchell (current)   roundtrip 4.06   mad_stat 0.00   blurred, no invention
    RealESRGAN x4                  6.87             0.36   great hair, blank eyes
    GFPGAN v1.4                    4.54             0.62   blotches on cheeks
    GPEN-BFR-512                   5.32             0.40   <- chosen
    CodeFormer w=0.7          (not measured)         1.20   worst boiling of the set
    blends of ESRGAN+GPEN          5.40             0.30   dilutes GPEN's eyes

CodeFormer's discrete codebook makes it pick different entries for near-identical
frames, which is exactly the flicker to avoid. GPEN's 0.40 is negligible: under half a
level out of 255, on pixels the source held still.

EXACT 4x IS MANDATORY
---------------------
SciArt.HighRes rejects any twin that is not exactly 4x the 1x bitmap and falls back to
the 1x image, so a 68x55 cel MUST come out 272x220. Every write here is asserted.

ALPHA
-----
The alpha channel is taken verbatim from the Mitchell file already in assets/png4x, so
the silhouette stays byte-identical to the art that ships today and nothing can move on
screen. Only RGB is replaced.

Before the model sees the cel, the RGB hiding UNDER transparent pixels is replaced by an
outward flood of the nearest visible colour. The SCI decode leaves whatever the palette
happened to hold there, and a convolution has no alpha channel to consult — without the
flood it drags that noise into the visible edge. (This is the same hazard the premultiply
in tools/Upscale exists to avoid; 11 of the 121 talker cels have transparency, and the
walkers are 40-60% transparent.)

WHAT WAS TRIED AND REVERTED
---------------------------
The walkers and the character-select screen were both run, measured, looked at, and put
back. They are still reachable as `walkers` and `select` if you want to judge for
yourself, but they are not applied by default and the numbers say why:

    group    roundtrip   mad_stat    what goes wrong
    faces      4.5-6.1   0.20-0.49   nothing; shipped
    walkers    29-33     1.5-2.5     views 274-277 are flat-shaded CARTOON art, not
                                     photographs. ESRGAN is trained on photos: it
                                     breaks the hard black outlines, smears the brown
                                     shoes into gold, and recolours the spectacle rims
                                     from grey to blue.
    walkers    10-14     1.2-nan     views 282/286/292/296 ARE digitised photographs and
                                     do get sharper, but the script logo on the T-shirt
                                     is destroyed into an orange smear, skin goes
                                     oversaturated, and the denim gets a different
                                     invented wrinkle pattern in each of the 4 walk
                                     frames - which is flicker on a cycling sprite.
    select     9-18      n/a         RealESRGAN sharpens the "SELECT YOUR CHARACTER"
                                     title nicely, but the four figures' faces are only
                                     ~15px tall and two of them flatten into blanks.

Mitchell scores roundtrip ~4, so a roundtrip of 30 means the pass has moved the picture
a long way from the photograph that is actually in the resource file. For the faces that
trade buys eyes and skin; for the walkers it buys nothing and costs the logo.

MODELS
------
Downloaded on first run to %LOCALAPPDATA%\jones-upscale-models (roughly 340MB total) and
cached. Sources:
    GPEN-BFR-512.onnx   huggingface.co/netrunner-exe/Face-Upscalers-onnx     271MB
    RealESRGAN_x4.onnx  huggingface.co/yuvraj108c/ComfyUI-Upscaler-Onnx       68MB
CPU inference only (onnxruntime, no CUDA). About 5s per face cel, 2s per walker cel.

Usage:
    python restore_faces.py [faces|walkers|select] [options]     default: faces

    --dry-run           measure and write samples, write nothing into assets/png4x
    --views 351,355     restrict to these view numbers
    --samples DIR       where the before/after sheets go (default tools/samples)
    --no-samples        skip the sheets
"""

import os
import sys
import time
import urllib.request

import numpy as np
from PIL import Image, ImageDraw

try:
    import onnxruntime as ort
except ImportError:
    sys.exit("onnxruntime is not installed. See the note at the bottom of this file.")

ort.set_default_logger_severity(3)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PNG1X = os.path.join(ROOT, "assets", "png")
PNG4X = os.path.join(ROOT, "assets", "png4x")
CACHE = os.path.join(os.environ.get("LOCALAPPDATA", os.path.expanduser("~")),
                     "jones-upscale-models")
SCALE = 4

MODELS = {
    "GPEN-BFR-512.onnx":
        "https://huggingface.co/netrunner-exe/Face-Upscalers-onnx/resolve/main/GPEN-BFR-512.onnx",
    "RealESRGAN_x4.onnx":
        "https://huggingface.co/yuvraj108c/ComfyUI-Upscaler-Onnx/resolve/main/RealESRGAN_x4.onnx",
}

# The shopkeeper talkers. 352 does not exist.
TALKERS = [351] + list(range(353, 363))
# Walker figures: the four generic ones and the per-character bases.
WALKERS = [274, 275, 276, 277, 282, 286, 292, 296]
SELECT = [499, 500]

GROUPS = {
    "faces": (TALKERS, "gpen"),
    "walkers": (WALKERS, "esrgan"),
    "select": (SELECT, "esrgan"),
}

# Groups that were run, measured, looked at and put back. See the header for the
# numbers. They stay reachable so the judgement can be re-checked, not repeated blind.
REGRESSES = {
    "walkers": "broken outlines on the cartoon figures, destroyed T-shirt logo and "
               "per-frame denim flicker on the photographic ones",
    "select": "the four character faces are ~15px tall and two flatten into blanks",
}


# ----------------------------------------------------------------------------------
# model plumbing
# ----------------------------------------------------------------------------------

_SESSIONS = {}


def ensure_model(name):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, name)
    if os.path.exists(path) and os.path.getsize(path) > 1_000_000:
        return path
    print("  downloading %s ..." % name)
    tmp = path + ".part"
    with urllib.request.urlopen(MODELS[name], timeout=900) as r, open(tmp, "wb") as f:
        while True:
            chunk = r.read(1 << 20)
            if not chunk:
                break
            f.write(chunk)
    os.replace(tmp, path)
    print("  got %s (%.0f MB)" % (name, os.path.getsize(path) / 1048576.0))
    return path


def session(name):
    if name not in _SESSIONS:
        opts = ort.SessionOptions()
        opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
        _SESSIONS[name] = ort.InferenceSession(ensure_model(name), opts,
                                               providers=["CPUExecutionProvider"])
    return _SESSIONS[name]


# ----------------------------------------------------------------------------------
# image plumbing
# ----------------------------------------------------------------------------------

def fill_transparent(rgb, alpha, iters=64):
    """Flood the RGB under transparent pixels outward from the nearest visible colour.

    The decode leaves palette garbage there and the models have no alpha channel, so
    without this the garbage is convolved into the visible edge.
    """
    out = rgb.astype(np.float32).copy()
    known = (alpha > 0).astype(np.float32)
    out[known == 0] = 0.0
    for _ in range(iters):
        if known.all():
            break
        acc = np.zeros_like(out)
        cnt = np.zeros_like(known)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            acc += np.roll(np.roll(out, dy, 0), dx, 1) * np.roll(np.roll(known, dy, 0), dx, 1)[..., None]
            cnt += np.roll(np.roll(known, dy, 0), dx, 1)
        fresh = (cnt > 0) & (known == 0)
        out[fresh] = (acc / np.maximum(cnt, 1)[..., None])[fresh]
        known[fresh] = 1.0
    return np.clip(out, 0, 255).astype(np.uint8)


def run_gpen(rgb, out_w, out_h):
    """68x55 -> 512x512 -> restore -> out_w x out_h, all Lanczos. Returns float [0,1]."""
    s = session("GPEN-BFR-512.onnx")
    big = np.asarray(Image.fromarray(rgb).resize((512, 512), Image.LANCZOS),
                     dtype=np.float32) / 255.0
    x = ((big - 0.5) / 0.5).transpose(2, 0, 1)[None].astype(np.float32)
    y = s.run(None, {s.get_inputs()[0].name: x})[0]
    y = np.clip(y[0].transpose(1, 2, 0) * 0.5 + 0.5, 0.0, 1.0)
    small = Image.fromarray((y * 255.0 + 0.5).astype(np.uint8)).resize((out_w, out_h), Image.LANCZOS)
    return np.asarray(small, dtype=np.float32) / 255.0


def run_esrgan(rgb, out_w, out_h):
    """Native 4x convolution, no resampling anywhere. Returns float [0,1]."""
    s = session("RealESRGAN_x4.onnx")
    x = rgb.astype(np.float32).transpose(2, 0, 1)[None] / 255.0
    y = s.run(None, {s.get_inputs()[0].name: x})[0]
    out = np.clip(y[0].transpose(1, 2, 0), 0.0, 1.0)
    assert out.shape[:2] == (out_h, out_w), "esrgan gave %s, wanted %dx%d" % (out.shape, out_w, out_h)
    return out


def cels_of(view):
    names = [n for n in os.listdir(PNG1X)
             if n.startswith("view_%d_l" % view) and n.endswith(".png")]

    def key(n):
        loop = int(n.split("_l")[1].split("_")[0])
        cel = int(n.rsplit("_c", 1)[1].split(".")[0])
        return loop, cel
    return sorted(names, key=key)


def erode(mask, r):
    for _ in range(r):
        mask = (mask & np.roll(mask, 1, 0) & np.roll(mask, -1, 0)
                & np.roll(mask, 1, 1) & np.roll(mask, -1, 1))
    return mask


def box_down(a, s=SCALE):
    h, w, c = a.shape
    return a.reshape(h // s, s, w // s, s, c).mean(axis=(1, 3))


# ----------------------------------------------------------------------------------
# the pass
# ----------------------------------------------------------------------------------

def process_view(view, how, dry_run):
    """Returns (records, stats) where records are (name, mitchell_rgba, new_rgb, alpha)."""
    names = cels_of(view)
    if not names:
        print("  view %d: no cels in assets/png" % view)
        return [], None

    recs = []
    srcs = []
    for name in names:
        src = np.asarray(Image.open(os.path.join(PNG1X, name)).convert("RGBA"))
        h, w = src.shape[:2]

        mpath = os.path.join(PNG4X, name)
        if not os.path.exists(mpath):
            raise SystemExit(
                "assets/png4x/%s is missing. Run tools/Upscale (or tools/upscale.py) "
                "first - this script takes the alpha channel from it." % name)
        mit = np.asarray(Image.open(mpath).convert("RGBA"))
        if mit.shape[:2] != (h * SCALE, w * SCALE):
            raise SystemExit("assets/png4x/%s is %dx%d, expected %dx%d"
                             % (name, mit.shape[1], mit.shape[0], w * SCALE, h * SCALE))

        filled = fill_transparent(src[..., :3], src[..., 3])
        run = run_gpen if how == "gpen" else run_esrgan
        new = run(filled, w * SCALE, h * SCALE)
        assert new.shape == (h * SCALE, w * SCALE, 3), new.shape

        recs.append((name, mit, new))
        srcs.append(src)

    # --- stability and fidelity, on the sequence as it will actually play -----------
    # Cels within one view are NOT all the same size - view 499 mixes 45x97, 43x97 and
    # 13x12 in a single loop - so every mask is taken from the cel it belongs to, and a
    # pair of different-sized cels is simply not a frame-to-frame comparison.
    stat, rt = [], []
    for i, (_, _, new) in enumerate(recs):
        back = box_down(new) * 255.0
        vis1 = srcs[i][..., 3] > 0
        rt.append(np.abs(back - srcs[i][..., :3].astype(np.float32)).mean(axis=2)[vis1].mean())
    for i in range(len(recs) - 1):
        if srcs[i].shape != srcs[i + 1].shape:
            continue
        same = (srcs[i][..., :3] == srcs[i + 1][..., :3]).all(axis=2) & (srcs[i][..., 3] > 0)
        m = (erode(np.repeat(np.repeat(same, SCALE, 0), SCALE, 1), 2 * SCALE)
             & (recs[i][1][..., 3] > 200))
        if m.sum() < 50:
            continue
        d = np.abs(recs[i][2] - recs[i + 1][2]).mean(axis=2) * 255.0
        stat.append(d[m].mean())

    stats = (np.mean(rt), np.mean(stat) if stat else float("nan"), len(recs))

    if not dry_run:
        for name, mit, new in recs:
            rgba = np.dstack([(np.clip(new, 0, 1) * 255.0 + 0.5).astype(np.uint8),
                              mit[..., 3:4]])
            img = Image.fromarray(rgba, "RGBA")
            h, w = np.asarray(Image.open(os.path.join(PNG1X, name))).shape[:2]
            assert img.size == (w * SCALE, h * SCALE), \
                "%s would be %s, must be %dx%d" % (name, img.size, w * SCALE, h * SCALE)
            img.save(os.path.join(PNG4X, name))

    return recs, stats


def write_sample(view, recs, out_dir):
    """A before/after sheet: Mitchell on top, the model underneath, both 2x."""
    os.makedirs(out_dir, exist_ok=True)
    n = min(len(recs), 6)
    shown = recs[:n]
    # cels in one view can differ in size (view 499), so pad every tile to the largest
    ph = max(m.shape[0] for _, m, _ in shown)
    pw = max(m.shape[1] for _, m, _ in shown)

    def tile(rgb, a):
        bg = np.full(rgb.shape[:2] + (3,), 0.5, np.float32)
        t = rgb * a + bg * (1 - a)
        return np.pad(t, ((0, ph - t.shape[0]), (0, pw - t.shape[1]), (0, 0)),
                      constant_values=0.5)

    top, bot = [], []
    for name, mit, new in shown:
        a = mit[..., 3:4].astype(np.float32) / 255.0
        top.append(tile(mit[..., :3].astype(np.float32) / 255.0, a))
        bot.append(tile(np.clip(new, 0, 1), a))
    grid = np.concatenate([np.concatenate(top, 1), np.concatenate(bot, 1)], 0)
    u8 = (grid * 255 + 0.5).astype(np.uint8)
    img = Image.fromarray(u8).resize((u8.shape[1] * 2, u8.shape[0] * 2), Image.NEAREST)
    lab = Image.new("RGB", (img.width, img.height + 22), (0, 0, 0))
    lab.paste(img, (0, 22))
    ImageDraw.Draw(lab).text(
        (6, 6), "view %d   TOP: Mitchell (current)   BOTTOM: model   %dx%d each"
        % (view, recs[0][1].shape[1], recs[0][1].shape[0]), fill=(255, 255, 255))
    p = os.path.join(out_dir, "view_%d_before_after.png" % view)
    lab.save(p)
    return p


def main():
    argv = sys.argv[1:]
    dry = "--dry-run" in argv
    samples = "--no-samples" not in argv
    out_dir = os.path.join(ROOT, "tools", "samples")
    if "--samples" in argv:
        out_dir = argv[argv.index("--samples") + 1]

    wanted = [a for a in argv if not a.startswith("--") and a in GROUPS]
    if not wanted:
        wanted = ["faces"]

    only = None
    if "--views" in argv:
        only = {int(v) for v in argv[argv.index("--views") + 1].split(",")}

    if not os.path.isdir(PNG4X):
        raise SystemExit("assets/png4x does not exist. Run tools/Upscale first.")

    print("restore_faces: %s%s" % (", ".join(wanted), "  (DRY RUN)" if dry else ""))
    print("These models INVENT detail. Nothing below is recovered; it is plausible guesswork.")
    for g in wanted:
        if g in REGRESSES and not dry:
            print()
            print("  !! '%s' was measured and REVERTED: %s" % (g, REGRESSES[g]))
            print("  !! It is not applied by default. Undo with tools/Upscale, then")
            print("  !! re-run this script with no arguments to put the faces back.")
    print()
    print("%-7s %-8s %6s  %9s  %8s   %s"
          % ("view", "model", "cels", "roundtrip", "mad_stat", "size"))
    print("-" * 70)

    total = 0
    worst = 0.0
    t0 = time.time()
    for group in wanted:
        views, how = GROUPS[group]
        for view in views:
            if only and view not in only:
                continue
            recs, stats = process_view(view, how, dry)
            if not recs:
                continue
            rt, st, n = stats
            total += n
            if st == st:
                worst = max(worst, st)
            h, w = recs[0][1].shape[:2]
            print("%-7d %-8s %6d  %9.2f  %8.2f   %dx%d"
                  % (view, how, n, rt, st, w, h))
            if samples:
                write_sample(view, recs, out_dir)

    print("-" * 70)
    print("%d cels in %.0fs%s" % (total, time.time() - t0, "  (nothing written)" if dry else ""))
    print()
    print("roundtrip: mean abs error (0-255) after box-downsampling the result back to 1x")
    print("           and comparing with the untouched source. Mitchell scores about 4.")
    print("mad_stat:  mean abs change between ADJACENT cels over pixels the source left")
    print("           EXACTLY unchanged - the model repainting frame to frame. Above 1.5")
    print("           is visible boiling in the mouth animation.")
    print("worst mad_stat this run: %.2f" % worst)
    if samples:
        print("before/after sheets: %s" % out_dir)
    print()
    print("A human still has to LOOK at those sheets. Nothing here can tell you whether")
    print("the invented face is a face you want.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

# ----------------------------------------------------------------------------------
# Getting the dependencies onto this machine
# ----------------------------------------------------------------------------------
# Python is at %LOCALAPPDATA%\Programs\Python\Python312\python.exe. The python.exe on
# PATH is a Microsoft Store stub and will not do.
#
# `pip install onnxruntime numpy pillow` hangs here - not the network (urllib reaches
# huggingface.co and pypi.org in about 6s), but pip's own resolver. Fetch the wheels
# with Invoke-WebRequest and install them offline instead:
#
#   $ProgressPreference='SilentlyContinue'
#   foreach ($p in 'numpy','pillow','onnxruntime','coloredlogs','humanfriendly',
#                  'pyreadline3','flatbuffers','packaging','protobuf','sympy','mpmath') {
#     $j = Invoke-RestMethod "https://pypi.org/pypi/$p/json"
#     $u = $j.releases.($j.info.version) | Where-Object {
#            $_.filename -like '*.whl' -and
#            ($_.filename -like '*cp312*win_amd64*' -or $_.filename -like '*none-any*') } |
#          Select-Object -First 1
#     Invoke-WebRequest $u.url -OutFile "wheels\$($u.filename)"
#   }
#   & $py -m pip install --no-index --no-deps (Get-ChildItem wheels\*.whl).FullName
