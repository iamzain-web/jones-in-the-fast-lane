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
    /// THE ART SET THE GAME IS ACTUALLY BUILT ON. <c>cast0</c> by default — the photoreal
    /// cast, the rebuilt buildings, the filled town centre and every rename.
    ///
    /// <para>
    /// IT USED TO DEFAULT TO NULL, which is Sierra's 1990 art, and NOTHING in the repository
    /// set it: not a script, not a launch profile, not a config file. Every rebuilt asset was
    /// visible only to someone who happened to type <c>$env:JONES_ART_SET = 'cast0'</c> in the
    /// launching shell first. Start the game from Explorer, a shortcut, the IDE or a fresh
    /// terminal and you got the 1990 game, with nothing anywhere saying why — while
    /// `PARITY.md` described the renames as "on, no switch", which was true of the content and
    /// false of the selector.
    /// </para>
    ///
    /// <para>
    /// <b><c>JONES_ART_SET=none</c> selects the ported originals</b> and is the A/B path, which
    /// has to stay: comparing the rebuilt art against Sierra's is how any of it was judged.
    /// The word is <c>none</c> rather than an empty string because Windows DELETES an
    /// environment variable set to empty, so "" and "unset" cannot be told apart — and unset
    /// now means the opposite. <c>original</c> and <c>sierra</c> are accepted as synonyms
    /// because they are what someone will type.
    /// </para>
    /// </summary>
    public static string? ArtSet
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("JONES_ART_SET");
            if (string.IsNullOrWhiteSpace(env)) return DefaultArtSet;

            var name = env.Trim();
            return name.Equals("none", StringComparison.OrdinalIgnoreCase)
                || name.Equals("original", StringComparison.OrdinalIgnoreCase)
                || name.Equals("sierra", StringComparison.OrdinalIgnoreCase)
                ? null
                : name;
        }
    }

    /// <summary>
    /// The set a launch with no environment variable at all uses. This is the one line that
    /// decides which game somebody sees.
    /// </summary>
    public const string DefaultArtSet = "cast0";

    /// <summary>
    /// Set by <see cref="Resolve"/> when a named art set could not be found at any scale, so
    /// the window can SAY SO. Silence here is how view 283 stayed Sierra's for a week: the
    /// resolver falls through to the ported art cel by cel, with no error, no log and nothing
    /// on screen to distinguish "the set replaced this" from "the set does not have this".
    /// </summary>
    public static string? MissingArtSet { get; private set; }

    /// <summary>
    /// What to put after the window's title: nothing at all in the normal case, and an
    /// unmissable complaint when the art is not what the launcher asked for.
    /// </summary>
    public static string TitleSuffix
    {
        get
        {
            _ = Factor;   // force Resolve, which is what sets MissingArtSet
            if (MissingArtSet is { } missing)
                return $"  —  ART SET '{missing}' NOT FOUND: showing Sierra's 1990 art";
            return ArtSet is null ? "  —  Sierra's 1990 art (JONES_ART_SET=none)" : string.Empty;
        }
    }

    /// <summary>
    /// The desktop window's title. It is the only channel a WinExe has for telling somebody
    /// that the art on screen is not the art they asked for, so it carries
    /// <see cref="TitleSuffix"/> and is otherwise the plain name.
    /// </summary>
    public static string WindowTitle => "Jones.App" + TitleSuffix;

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

    // ------------------------------------------------------------------
    // THE WIDE BOARD — a FEASIBILITY PROBE, off by default, with no art behind it.
    // ------------------------------------------------------------------

    /// <summary>
    /// Columns of decorative scenery either side of the world, or 0 — the default — for the
    /// board exactly as it has always been.
    ///
    /// <para>
    /// WHAT IT IS FOR. The world is 320x200, which is 1.6:1, and a modern handset in
    /// landscape is about 2.2:1, so the playfield is pillarboxed on black. The proposal is to
    /// let the PAINTED SCENERY continue past the world either side while the world itself —
    /// every coordinate, every <c>Place</c> rectangle, every <c>MarblePath</c> point, every
    /// dialog origin — stays exactly 320 wide and completely unaware of it.
    /// </para>
    ///
    /// <para>
    /// HOW MANY, measured rather than guessed: a canvas of <c>320 + 2M</c> by 200 has aspect
    /// <c>(320+2M)/200</c>, so M = 100A − 160. That is <b>18</b> columns per side for 16:9,
    /// <b>40</b> for 18:9, <b>57</b> for 19.5:9, <b>63</b> for 20:9 and <b>74</b> for 21:9.
    /// Anything NARROWER than 1.6 — every tablet at 4:3 or 3:2, and every phone in portrait —
    /// does not want columns at all; it letterboxes top and bottom, and widening makes that
    /// worse rather than better. See <c>WideBoardTests</c>.
    /// </para>
    ///
    /// <para>
    /// NOTHING IS DRAWN OUT THERE YET. This exists so the mechanism can be tested before any
    /// art is commissioned: set <c>JONES_WIDE_BOARD=63</c> and the canvas widens, the world
    /// is centred in it, and the margin is filled with a flat marker colour so that it can be
    /// seen and probed. It is not a feature and it is not a deviation until there is scenery
    /// and a decision to ship it.
    /// </para>
    /// </summary>
    public static int MarginColumns
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("JONES_WIDE_BOARD");
            return int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out var e)
                   && e > 0 && e <= 200
                ? e
                : 0;
        }
    }

    /// <summary>
    /// The OUTER canvas: the world plus its margins. Identical to <see cref="CanvasWidth"/>
    /// while <see cref="MarginColumns"/> is 0, which is why the default render is unchanged
    /// down to the pixel.
    /// </summary>
    public static double OuterCanvasWidth => (ScreenWidth + 2 * MarginColumns) * Factor;

    /// <summary>
    /// Where the world's own canvas sits inside the outer one. THE WORLD IS A CLIPPED CHILD,
    /// which is the whole point of the arrangement: nothing inside it has to learn a new
    /// coordinate, and anything that strayed outside 0..319 would be clipped away rather than
    /// drawn into the margin — so the margin cannot become a place where things happen by
    /// accident. It can only be used deliberately, by adding something to the OUTER canvas.
    /// </summary>
    public static double WorldLeft => MarginColumns * Factor;

    /// <summary>
    /// The margin's fill while the probe is on: a flat colour so the extra space can be seen
    /// and pointed at. Black — i.e. invisible, i.e. today's bars — when it is off.
    /// </summary>
    public static string MarginFill => MarginColumns > 0 ? "#201838" : "#000000";

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

            // LOUD, because this is the case where the player is looking at a different game
            // from the one they asked for. A Debug.WriteLine on its own was not: a WinExe has
            // no console, the desktop head leaves StartupLog's delegates null, and the result
            // was a silent substitution of Sierra's art for the whole build.
            MissingArtSet = ArtSet;
            var complaint =
                $"ART SET '{ArtSet}' NOT FOUND: no assets/png{{N}}x-{ArtSet} at any scale. " +
                "Drawing Sierra's 1990 art instead. Set JONES_ART_SET=none to ask for that " +
                "deliberately, or generate the set.";
            System.Diagnostics.Debug.WriteLine(complaint);
            StartupLog.Say(complaint);
            SciArt.Trace(complaint);
            // And the window title says it too - see TitleSuffix. That is the only channel
            // this head actually has.
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
