using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Jones.App;

/// <summary>
/// THE RENDER SCALE SWITCH.
///
/// The port computes every coordinate in the original's 320x200 space, exactly as the
/// scripts do, and that must never change — it is the whole basis of checking the port
/// against the source. What this class changes is the RESOLUTION those coordinates are
/// drawn at.
///
/// At <c>Factor</c> 1 the canvas is 320x200 and the art is the raw decode, nearest
/// neighbour: Sierra's pixels, exactly as before.
///
/// At <c>Factor</c> 4 the canvas is 1280x800, every coordinate and size is multiplied by
/// 4 on its way into the visual tree, and each cel is drawn from the smoothed 4x resample
/// in <c>assets/png4x</c> at its native resolution — one bitmap pixel per screen pixel,
/// no resampling at draw time.
///
/// The layout maths does not see any of this. The view models keep measuring the 1x
/// bitmaps (<c>PixelSize</c>) and keep producing 320x200 coordinates; the multiplication
/// and the bitmap substitution both happen in the view, through the converters below.
/// That is deliberate — it is the only arrangement in which the original's arithmetic,
/// including its off-by-ones and its integer division, stays bit-for-bit what it was.
///
/// At <c>Factor</c> 8 or 12 exactly the same thing happens with a bigger multiplier —
/// 12 is the 4K factor, because 320x200 at 12x is 3840x2400 and a 3840x2160 television
/// fits that to 3456x2160, which is the whole playfield at native panel resolution. The
/// layout arithmetic is untouched at every factor; only the canvas and the bitmaps grow.
///
/// TO GO BACK TO THE ORIGINAL LOOK: set <see cref="Requested"/> to 1 (or run with
/// <c>JONES_ART_SCALE=1</c> in the environment, which needs no rebuild). Nothing else
/// changes; the window stays the same size, because 320x200 drawn into 1280x800 is a
/// whole 4x4 block per pixel.
///
/// THE ART SET is a second, independent switch: <c>JONES_ART_SET=<i>name</i></c> puts
/// <c>assets/png{N}x-<i>name</i></c> in FRONT of <c>assets/png{N}x</c> on the search path,
/// so a set holding only the twenty walker views draws new figures over the ported board
/// and shops, and unsetting the variable restores the ported art with no other change.
/// See <see cref="ArtDirectories"/>.
/// </summary>
public static class RenderScale
{
    /// <summary>The original's own screen, and the space every ported coordinate is in.</summary>
    public const int ScreenWidth = 320;
    public const int ScreenHeight = 200;

    /// <summary>
    /// THE SWITCH. 12 draws from the 4K art; 1 restores the original pixels.
    /// Only scales for which an <c>assets/png{N}x</c> directory exists can take effect —
    /// anything else falls down the <see cref="Ladder"/> to the largest that does, and to
    /// 1 if none does, rather than drawing nothing. So this is safe to leave at 12 on a
    /// clone that has only built the 4x set: it draws the 4x set, exactly as before.
    /// </summary>
    public const int Requested = 12;

    /// <summary>
    /// The factors an <c>assets/png{N}x</c> directory is ever built at, largest first.
    /// Used as a LADDER: if the requested factor has no art on disk the next smaller one
    /// that does is taken, so asking for 12 on a clone that only has the 4x set draws the
    /// 4x set rather than dropping all the way to Sierra's 320x200 pixels.
    ///
    /// 12 is the 4K factor (3840x2400 fitted to a 2160-row panel); 8 is there because it
    /// is the largest whole factor that still fits a 2560x1600 desktop window one bitmap
    /// pixel per screen pixel.
    /// </summary>
    private static readonly int[] Ladder = [12, 8, 4];

    /// <summary>
    /// How big the window opens, as a multiple of the playfield. Deliberately SEPARATE
    /// from <see cref="Factor"/>: the art can be 12x while the window is 4x, and the
    /// Viewbox in MainView fits one to the other — that is just a high-resolution image
    /// shown small, which is exactly what you want on a laptop. On the television the
    /// window is maximised and the Viewbox scales the same canvas up instead.
    ///
    /// <c>JONES_WINDOW_SCALE</c> overrides it without a rebuild. The default stays 4, so
    /// the window geometry is unchanged from before this switch existed.
    /// </summary>
    public static int WindowFactor
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("JONES_WINDOW_SCALE");
            return int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out var e) && e > 0
                ? e
                : 4;
        }
    }

    /// <summary>
    /// The window is sized in whole multiples of the playfield either way, so switching
    /// the scale changes only how the art is sampled, never the geometry. 1280x800 is 4x.
    /// </summary>
    public static double WindowWidth => ScreenWidth * WindowFactor;
    public static double WindowHeight => ScreenHeight * WindowFactor;

    private static int? _factor;
    private static string[]? _artDirectories;
    private static string? _explicitAssetRoot;

    /// <summary>
    /// The scale asked for: <see cref="Requested"/>, or the <c>JONES_ART_SCALE</c>
    /// environment variable, which overrides it without a rebuild so the two can be
    /// compared side by side.
    /// </summary>
    private static int Wanted
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("JONES_ART_SCALE");
            return int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out var e) && e > 0
                ? e
                : Requested;
        }
    }

    /// <summary>
    /// The art set name from <c>JONES_ART_SET</c>, or null for the ported originals.
    /// </summary>
    private static string? ArtSet
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("JONES_ART_SET");
            return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
        }
    }

    /// <summary>
    /// The scale actually in force: <see cref="Wanted"/> if its art is on disk, else the
    /// largest factor on the <see cref="Ladder"/> that is, else 1.
    /// </summary>
    public static int Factor
    {
        get
        {
            if (_factor is { } f) return f;
            Resolve();
            return _factor!.Value;
        }
    }

    /// <summary>Canvas width in the scaled space. Bound from the XAML.</summary>
    public static double CanvasWidth => ScreenWidth * Factor;
    public static double CanvasHeight => ScreenHeight * Factor;

    /// <summary>
    /// How the art is sampled. At 4x the bitmap is already at screen resolution, so this
    /// only matters when the window is not an exact multiple — and there a smooth
    /// resample of an already-smooth image is what you want. At 1x it must stay
    /// <c>None</c>, or the 4x blow-up of a 320x200 image turns to mush.
    ///
    /// The FONTS are never drawn this way: see <see cref="SciFont"/>.
    /// </summary>
    public static BitmapInterpolationMode ArtInterpolation =>
        Factor > 1 ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.None;

    /// <summary>
    /// Where the upscaled cels live, or null if they are not there.
    ///
    /// 23MB of PNG is too much to embed the way the 1x decode is embedded (Assets\** goes
    /// into the assembly as Avalonia resources), so it is loaded from disk — the same
    /// arrangement the 21MB of speech already uses. This walks up from the executable
    /// looking for <c>assets\png{N}x</c>, exactly as <c>Program.FindAssetRoot</c> walks up
    /// looking for <c>assets\audio\speech</c>, so a build run from anywhere under the repo
    /// finds it.
    /// </summary>
    public static string? ArtDirectory
    {
        get
        {
            var dirs = ArtDirectories;
            return dirs.Length > 0 ? dirs[0] : null;
        }
    }

    /// <summary>
    /// Every directory a high-resolution twin may be found in, IN ORDER. With no art set
    /// this is just <c>assets/png{Factor}x</c>; with <c>JONES_ART_SET=new</c> it is
    /// <c>assets/png{Factor}x-new</c> followed by <c>assets/png{Factor}x</c>, and the
    /// first file that exists wins.
    ///
    /// That ordering is the whole switch. A set need only contain the files it replaces —
    /// the twenty walker views, say — and everything it is silent about still comes from
    /// the ported art, at the same factor, with no code path of its own. Clearing the
    /// variable puts the ported art back exactly; nothing in <c>assets/png</c> or
    /// <c>assets/png{N}x</c> is ever written by a set.
    /// </summary>
    public static string[] ArtDirectories
    {
        get
        {
            if (_artDirectories is { } d) return d;
            Resolve();
            return _artDirectories!;
        }
    }

    /// <summary>
    /// Settles <see cref="Factor"/> and <see cref="ArtDirectories"/> together, because the
    /// factor depends on which art is actually on disk and the search path depends on the
    /// factor. Runs once; both are read on every draw.
    /// </summary>
    private static void Resolve()
    {
        if (_factor is not null && _artDirectories is not null) return;

        var want = Wanted;
        if (want <= 1)
        {
            _factor = 1;
            _artDirectories = [];
            return;
        }

        // The requested factor first, then down the ladder. A factor not on the ladder
        // (someone's JONES_ART_SCALE=6) is still honoured if its directory exists.
        var order = new List<int> { want };
        foreach (var n in Ladder)
            if (n < want && !order.Contains(n))
                order.Add(n);

        // TWO PASSES when an art set is named, and the order matters. The set is what the
        // person asked for, so the largest factor at which the SET exists beats a larger
        // factor at which only the ported art does — otherwise building the set at 12x
        // while 8x art happened to be lying about would quietly show the ported figures.
        // Only if the set exists at no factor at all does the second pass draw the ported
        // art, and it says so on the way past rather than dropping to 320x200, which is
        // what the first version of this did and is a far worse surprise.
        if (ArtSet is not null)
        {
            foreach (var n in order)
            {
                var dirs = DirectoriesFor(n, requireSet: true);
                if (dirs.Length == 0) continue;
                _factor = n;
                _artDirectories = dirs;
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"JONES_ART_SET={ArtSet}: no assets/png{{N}}x-{ArtSet} found at any scale; " +
                "drawing the ported art.");
        }

        foreach (var n in order)
        {
            var dirs = DirectoriesFor(n, requireSet: false);
            if (dirs.Length == 0) continue;
            _factor = n;
            _artDirectories = dirs;
            return;
        }

        // No upscaled art on disk at any factor (the Browser and Android heads have none,
        // and a build run from a stray directory may not find it) — draw the original
        // pixels rather than nothing.
        _factor = 1;
        _artDirectories = [];
    }

    /// <summary>
    /// The search path for one factor: the art set's directory if it is there, then the
    /// plain one. Empty when neither exists, which is what sends <see cref="Resolve"/>
    /// one rung further down the ladder.
    /// </summary>
    private static string[] DirectoriesFor(int factor, bool requireSet)
    {
        // A set with no ported art behind it is a half-drawn screen, not a feature: every
        // cel the set is silent about — eight hundred of them — would fall back to 320x200
        // pixels beside 12x ones. So the ported directory is always required.
        if (Locate($"png{factor}x") is not { } ported) return [];

        var set = ArtSet is { } s ? Locate($"png{factor}x-{s}") : null;
        if (requireSet && set is null) return [];

        return set is null ? [ported] : [set, ported];
    }

    /// <summary>
    /// Finds <c>assets/<paramref name="name"/></c> from the explicit root a head handed
    /// over, else by walking up from the executable exactly as <c>Program.FindAssetRoot</c>
    /// walks up looking for <c>assets\audio\speech</c>, so a build run from anywhere under
    /// the repo finds it.
    /// </summary>
    /// <remarks>
    /// EVERY STEP IS GUARDED, because on Android there is no directory tree to walk: an APK's
    /// <c>AppContext.BaseDirectory</c> is not a repository checkout and may not be a usable
    /// path at all, and <c>Directory.GetParent</c> throws on an empty one rather than
    /// returning null. A head with no upscaled art must fall to <see cref="Factor"/> 1 and
    /// draw Sierra's own pixels — which is the intended Android look — and it must get there
    /// by finding nothing, never by throwing on the way.
    /// </remarks>
    private static string? Locate(string name)
    {
        try
        {
            if (_explicitAssetRoot is { Length: > 0 } root)
            {
                var direct = System.IO.Path.Combine(root, name);
                if (System.IO.Directory.Exists(direct)) return direct;
            }

            var dir = AppContext.BaseDirectory;
            for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var candidate = System.IO.Path.Combine(dir, "assets", name);
                if (System.IO.Directory.Exists(candidate)) return candidate;
                dir = System.IO.Directory.GetParent(dir)?.FullName;
            }
        }
        catch (Exception e)
        {
            StartupLog.Blame($"RenderScale.Locate({name}) (falling back to the 1x art)", e);
        }

        return null;
    }

    /// <summary>
    /// Points the loader at an already-located asset root, for heads that have found it
    /// themselves (the desktop head locates the same directory for the speech). Must be
    /// called before the first cel is drawn.
    /// </summary>
    public static void UseAssetRoot(string assetsRoot)
    {
        if (_artDirectories is not null) return;
        _explicitAssetRoot = assetsRoot;
    }

    /// <summary>
    /// Multiplies a 320x200-space coordinate or size into the scaled canvas. Every
    /// <c>Canvas.Left</c>, <c>Canvas.Top</c>, <c>Width</c> and <c>Height</c> in MainView
    /// goes through this, which is what keeps the view models in the original's space.
    /// </summary>
    public static readonly IValueConverter Scale = new ScaleConverter();

    /// <summary>
    /// Swaps a bitmap the view models measured for the high-resolution twin that is
    /// actually drawn. Identity at 1x, and identity for any bitmap with no twin.
    /// </summary>
    public static readonly IValueConverter HighRes = new HighResConverter();

    private sealed class ScaleConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var f = Factor;
            return value switch
            {
                double d => d * f,
                int i => (double)(i * f),
                _ => value,
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is double d ? d / Factor : value;
    }

    private sealed class HighResConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is Bitmap b ? SciArt.HighRes(b) : value;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value;
    }
}
