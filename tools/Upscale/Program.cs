using SkiaSharp;

// Upscales every extracted cel with a high-quality resampler.
//
// Writes to a PARALLEL directory (assets/png<N>x) and never touches assets/png, so the
// original decode stays the reference and this pass can be thrown away or redone with a
// different filter at any time.
//
// WHY SKIA AND NOT PILLOW: PyPI is unreachable from this machine, so the Python version of
// this tool (tools/upscale.py) cannot run here. Skia is already in the dependency tree
// under Avalonia, and it brings a real advantage: SKBitmap decodes PNG to PREMULTIPLIED
// alpha. The cels have hard-edged transparency — a skip run decodes to alpha 0 while the
// RGB underneath still holds whatever the palette put there — so a resampler working on
// straight alpha averages those invisible colours into the visible edge pixels and fringes
// every sprite with dark halos. Premultiplied alpha makes an invisible pixel contribute
// nothing, which is what "transparent" ought to mean.
//
// The filter is Mitchell (B=1/3, C=1/3), Skia's high-quality cubic. It is not literally
// Lanczos — Skia dropped its Lanczos kernel — but it is the same family of windowed cubic
// and is the better choice here anyway: Lanczos has stronger negative lobes and rings
// visibly at the hard edges these sprites are full of.
//
// Either way this SMOOTHS; it does not add detail. Nothing here can invent skin texture
// that was never in a 40x100 digitised photograph.
//
// ORDERING HAZARD: tools/restore_faces.py replaces the RGB of the digitised-photograph
// cels in assets/png4x with a neural super-resolution pass (the 121 shopkeeper talker
// cels, and the walker figures). This tool rewrites EVERY file in that directory, so
// running it afterwards silently reverts them to the Mitchell resample. Run this first
// and restore_faces.py second — which is also how you undo that pass.
//
//     Upscale  ->  restore_faces.py  ->  smooth_walk.py
//
// THE WHOLE ORDER, now that there is a 12x set and a generated-character set:
//
//     Upscale                          assets/png    -> assets/png4x
//     restore_faces.py                 rewrites the talker and walker RGB in png4x
//     smooth_walk.py                   adds the _sN in-between frames to png4x
//     Upscale 12 4                     assets/png4x  -> assets/png12x   (carries both)
//     sprite_pose.py                   measures the walkers -> pose skeletons
//     gen_walkers.py                   the new characters, as raw frames
//     fit_walkers.py --factor 12       -> assets/png12x-new   (cels only)
//     smooth_walk.py --dir assets/png12x-new --scale 12    -> its _sN frames
//
// THIS TOOL NEVER WRITES AN ART SET. It only ever writes assets/png{scale}x, so
// assets/png12x-new survives any re-run here - and that is exactly the trap, because the
// generated cels were fitted to the 1x boxes, not to png12x, so they stay correct while
// the art around them changes. What does NOT survive is the set's own _sN frames if the
// set's cels are rebuilt: rerun smooth_walk.py --dir against it, or the new character
// alternates with three ported in-between frames of the old one.
//
// tools/smooth_walk.py is the third link and it fails DIFFERENTLY. It writes the walker's
// in-between frames as view_V_lL_cC_sN.png, and those have no counterpart in assets/png,
// so the loop above never enumerates them and this tool does NOT overwrite or remove them.
// They therefore survive a re-run and go STALE: the cels revert to Mitchell while the
// frames between them still hold whatever they were built from, and the walk cycle
// alternates two different pictures. Running this tool means re-running smooth_walk.py
// afterwards. `python tools/smooth_walk.py --check` says whether that is outstanding, by
// comparing hashes of the cels it was built from; `--clean` removes them instead.

// USAGE
//
//     Upscale                 4x from assets/png  -> assets/png4x   (the original job)
//     Upscale 8               8x from assets/png  -> assets/png8x
//     Upscale 12 4            3x from assets/png4x -> assets/png12x
//
// THE SECOND ARGUMENT IS THE ONE THAT MATTERS ABOVE 4x. Resampling straight from
// assets/png at 12 throws away everything the later passes did: restore_faces.py's neural
// super-resolution of the 121 talker cels and the walker figures lives only in png4x, and
// so do smooth_walk.py's in-between frames, which have no 1x counterpart at all and would
// simply be absent. Going 4x -> 12x is a 3x resample of art that already holds both, and
// it enumerates the _sN frames because they are files in the source directory like any
// other. The rule is: build 4x from 1x, run the two Python passes, then build every
// larger factor FROM 4x.
//
// A 3x resample of a restored 4x cel is not as good as a restoration done at 12x would be.
// It is better than the alternative on offer, which is a 12x Mitchell blur of a 39x95
// photograph with the restoration discarded.

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

var scale = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 4;
var from = args.Length > 1 && int.TryParse(args[1], out var f) ? f : 1;

if (from < 1 || scale <= from || scale % from != 0)
{
    Console.Error.WriteLine($"cannot go from {from}x to {scale}x: the target must be a whole multiple of the source");
    return 1;
}

var ratio = scale / from;
var src = from == 1 ? Path.Combine(root, "assets", "png") : Path.Combine(root, "assets", $"png{from}x");
var dst = Path.Combine(root, "assets", $"png{scale}x");

if (!Directory.Exists(src))
{
    Console.Error.WriteLine($"no such directory: {src}");
    return 1;
}

Console.WriteLine($"{src}  x{ratio}  ->  {dst}");

Directory.CreateDirectory(dst);

var files = Directory.GetFiles(src, "*.png").ToList();
files.Sort();

// A couple of the app's bitmaps are not part of the resource decode and so are not in
// assets/png — board.png, the town board backdrop, is one and is the most visible image
// in the game. They live only in the app's own Assets/game, and they need upscaling on
// exactly the same terms, into the same output directory, or the board is the one screen
// still drawn from 320x200 pixels.
// Only when reading the 1x decode. From png4x onwards board.png is already in the source
// directory, because this ran once and put it there.
var appAssets = Path.Combine(root, "src", "Jones.App", "Jones.App", "Assets", "game");
if (from == 1 && Directory.Exists(appAssets))
{
    var decoded = new HashSet<string>(files.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
    var extra = Directory.GetFiles(appAssets, "*.png")
                         .Where(f => !decoded.Contains(Path.GetFileName(f)))
                         .OrderBy(f => f)
                         .ToList();
    if (extra.Count > 0)
        Console.WriteLine($"  plus {extra.Count} app-only bitmap(s): " +
                          string.Join(", ", extra.Select(Path.GetFileName)));
    files.AddRange(extra);
}

// SKFilterQuality.High is Skia 2.88's high-quality cubic — Mitchell (B=1/3, C=1/3).
// (SkiaSharp 3.x replaces this with SKSamplingOptions/SKCubicResampler; this project pins
// 2.88 because that is what the rest of the tree resolves.)
const SKFilterQuality sampling = SKFilterQuality.High;
var biggest = (Pixels: 0L, Name: "");
var done = 0;

foreach (var file in files)
{
    using var input = SKBitmap.Decode(file);
    if (input is null)
    {
        Console.Error.WriteLine($"  could not decode {Path.GetFileName(file)}");
        continue;
    }

    var info = new SKImageInfo(input.Width * ratio, input.Height * ratio,
                               SKColorType.Rgba8888, SKAlphaType.Premul);

    using var output = new SKBitmap(info);
    if (!input.ScalePixels(output, sampling))
    {
        Console.Error.WriteLine($"  could not scale {Path.GetFileName(file)}");
        continue;
    }

    using var image = SKImage.FromBitmap(output);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.OpenWrite(Path.Combine(dst, Path.GetFileName(file)));
    data.SaveTo(stream);

    long px = (long)output.Width * output.Height;
    if (px > biggest.Pixels) biggest = (px, $"{Path.GetFileName(file)} {output.Width}x{output.Height}");

    if (++done % 200 == 0) Console.WriteLine($"  {done}/{files.Count}");
}

Console.WriteLine($"\n{done} cels upscaled x{scale} -> {dst}");
Console.WriteLine($"largest: {biggest.Name}");
return 0;
