using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Jones.App;

/// <summary>
/// Loads the original game art, decoded out of the CD copy's resource volume into
/// view/loop/cel PNGs.
///
/// Every asset is addressed the way the original scripts address it — by view number,
/// loop and cel — so a line like `view 250 loop 8` in the source maps straight onto a
/// call here. That is deliberate: it keeps the port checkable against the scripts.
///
/// ASSET SWAP: everything resolves through <see cref="PathFor"/>. Replacing the art
/// later means dropping different PNGs into Assets/game with the same names; no code
/// changes anywhere.
///
/// UPSCALED ART: <see cref="Cel"/> and <see cref="Pic"/> always return the 1x decode,
/// because the view models measure what they get back (<c>PixelSize</c>) and every
/// coordinate in the port is in the original's 320x200 space. The smoothed 4x resample
/// is a SEPARATE bitmap, fetched by the view through <see cref="HighRes"/> at draw time.
/// Returning the 4x bitmap from here instead would quadruple every width the layout
/// maths reads and silently move half the screen — see <see cref="RenderScale"/>.
/// </summary>
public static class SciArt
{
    private const string Root = "avares://Jones.App/Assets/game/";

    private static readonly Dictionary<string, Bitmap?> Cache = [];

    private static string PathFor(int view, int loop, int cel) =>
        $"{Root}view_{view}_l{loop}_c{cel}.png";

    /// <summary>A view cel, or null if that combination was not in the resource file.</summary>
    public static Bitmap? Cel(int view, int loop, int cel)
    {
        var key = $"{view}/{loop}/{cel}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var file = $"view_{view}_l{loop}_c{cel}.png";
        var bmp = Load($"{Root}{file}");
        if (bmp is not null) PairWithFile(bmp, file);

        // How to make this again, for network play — see Net.ArtRecipes.
        Net.ArtRecipes.Note(bmp, Jones.Net.ArtRef.Cel(view, loop, cel));

        Cache[key] = bmp;
        return bmp;
    }

    /// <summary>
    /// An IN-BETWEEN frame of a cel animation: the same cel, drawn from interpolated
    /// high-resolution art that sits <paramref name="sub"/>/N of the way towards the next
    /// cel. <c>sub</c> 0 is the cel itself and is exactly <see cref="Cel"/>.
    ///
    /// HOW THIS GETS PAST THE 4x SIZE GUARD. An in-between frame has no 1x counterpart —
    /// it is not in the resource file, it never existed — and <see cref="HighRes"/> rejects
    /// any twin that is not exactly <see cref="RenderScale.Factor"/> times the 1x bitmap it
    /// is standing in for. So this does not invent a new kind of bitmap: it hands back
    /// ANOTHER INSTANCE of the ordinary 1x cel, which the view models measure exactly as
    /// before, and registers the interpolated file as THAT instance's twin. The twin table
    /// is keyed by reference (see <see cref="Twins"/>), which is precisely what makes one
    /// 1x cel able to stand for several 4x frames. The guard still applies, literally and
    /// unchanged: an in-between frame is generated at the size of the pair it sits between,
    /// so it is exactly 4x, and a file at any other size falls back to the 1x pixels.
    ///
    /// Nothing in the layout can move as a result — every instance is the same cel, so
    /// <c>PixelSize</c> is identical whichever one a caller holds.
    ///
    /// The files are written by <c>tools/smooth_walk.py</c> as
    /// <c>view_V_lL_cC_sS.png</c> beside the ordinary 4x cels. When they are absent — a
    /// fresh clone, a 1x run, or anyone who deletes them — this returns the plain cel and
    /// the animation is simply the original's own four frames.
    /// </summary>
    public static Bitmap? SubCel(int view, int loop, int cel, int sub)
    {
        if (sub <= 0 || RenderScale.Factor <= 1) return Cel(view, loop, cel);

        var key = $"{view}/{loop}/{cel}/s{sub}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        Bitmap? bmp = null;
        {
            var tween = Resolve($"view_{view}_l{loop}_c{cel}_s{sub}.png");
            if (tween is not null)
            {
                // Deliberately NOT the shared instance from Cel(): that one already owns the
                // base frame's twin, and the table holds one factory per bitmap reference.
                bmp = Load($"{Root}view_{view}_l{loop}_c{cel}.png");
                if (bmp is not null) RegisterHighRes(bmp, () => LoadFile(tween));
                Net.ArtRecipes.Note(bmp, Jones.Net.ArtRef.SubCel(view, loop, cel, sub));
            }
        }

        bmp ??= Cel(view, loop, cel);
        Cache[key] = bmp;
        return bmp;
    }

    /// <summary>A background pic, by its original resource number.</summary>
    public static Bitmap? Pic(int number)
    {
        var key = $"pic/{number}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var file = $"pic_{number}.png";
        var bmp = Load($"{Root}{file}");
        if (bmp is not null) PairWithFile(bmp, file);
        Net.ArtRecipes.Note(bmp, Jones.Net.ArtRef.Pic(number));

        Cache[key] = bmp;
        return bmp;
    }

    /// <summary>
    /// A bitmap in Assets/game addressed by file name, for the handful that are not a
    /// numbered cel or pic.
    /// </summary>
    internal static Bitmap? Named(string file)
    {
        var key = $"file/{file}";
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var bmp = Load($"{Root}{file}");
        if (bmp is not null) PairWithFile(bmp, file);
        Net.ArtRecipes.Note(bmp, Jones.Net.ArtRef.Named(file));

        Cache[key] = bmp;
        return bmp;
    }

    /// <summary>
    /// The town board backdrop, which the XAML draws directly rather than through a view
    /// model. Already substituted for its high-resolution twin, because there is no
    /// binding here for the view's converter to sit on.
    /// </summary>
    public static Bitmap? Board => HighRes(Named("board.png"));

    private static Bitmap? Load(string uri)
    {
        try
        {
            var u = new Uri(uri);
            if (!AssetLoader.Exists(u)) return null;
            using var s = AssetLoader.Open(u);
            return new Bitmap(s);
        }
        catch
        {
            // A missing cel is not fatal; the screen simply omits it.
            return null;
        }
    }

    // ------------------------------------------------------------------
    // High-resolution twins
    // ------------------------------------------------------------------

    // Keyed by REFERENCE: two different cels can be pixel-identical, and the caches above
    // hand out one shared instance per cel anyway, so identity is the right relation here
    // and the dictionary never has to hash a bitmap's contents.
    private static readonly Dictionary<Bitmap, Func<Bitmap?>> Twins =
        new(ReferenceEqualityComparer.Instance);

    private static readonly Dictionary<Bitmap, Bitmap?> Loaded =
        new(ReferenceEqualityComparer.Instance);

    private static void PairWithFile(Bitmap oneX, string file)
    {
        if (RenderScale.ArtDirectories.Length == 0) return;

        // Resolved LAZILY, inside the factory, for the same reason the factory exists at
        // all: a screen that is never visited must not cost a directory probe per cel.
        RegisterHighRes(oneX, () => Resolve(file) is { } p ? LoadFile(p) : null);
    }

    /// <summary>
    /// The first of <see cref="RenderScale.ArtDirectories"/> that holds
    /// <paramref name="file"/>, or null. With an art set selected that is the set's copy
    /// if it has one and the ported copy otherwise, which is what lets a set replace
    /// twenty views and stay silent about the other eight hundred.
    /// </summary>
    private static string? Resolve(string file)
    {
        foreach (var dir in RenderScale.ArtDirectories)
        {
            var path = System.IO.Path.Combine(dir, file);
            if (System.IO.File.Exists(path)) return path;
        }
        return null;
    }

    private static Bitmap? LoadFile(string path)
    {
        try
        {
            return System.IO.File.Exists(path) ? new Bitmap(path) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Records how to produce the high-resolution version of a bitmap the view models
    /// have already measured. The factory is not run until the bitmap is first drawn, so
    /// a screen that is never visited costs nothing — which matters when the full 4x set
    /// is 23MB on disk and a good deal more once decoded.
    ///
    /// <see cref="SciFont"/> registers its pixel-doubled text through here too, so the
    /// view has one substitution to make regardless of where the bitmap came from.
    /// </summary>
    public static void RegisterHighRes(Bitmap oneX, Func<Bitmap?> factory) => Twins[oneX] = factory;

    /// <summary>
    /// The bitmap to actually draw for <paramref name="oneX"/>: its high-resolution twin
    /// if there is one, otherwise the bitmap itself. Never null for a non-null argument,
    /// so a missing 4x file degrades to the original pixels instead of a hole.
    /// </summary>
    public static Bitmap? HighRes(Bitmap? oneX)
    {
        if (oneX is null || RenderScale.Factor <= 1) return oneX;

        if (Loaded.TryGetValue(oneX, out var cached)) return cached ?? oneX;
        if (!Twins.TryGetValue(oneX, out var factory)) return oneX;

        var hi = factory();

        // Guard against an upscale that is not the scale we are drawing at: a stale or
        // hand-dropped file at the wrong size would land at the wrong place on screen,
        // which is far worse than a soft edge.
        if (hi is not null &&
            (hi.PixelSize.Width != oneX.PixelSize.Width * RenderScale.Factor ||
             hi.PixelSize.Height != oneX.PixelSize.Height * RenderScale.Factor))
            hi = null;

        Loaded[oneX] = hi;
        return hi ?? oneX;
    }
}
