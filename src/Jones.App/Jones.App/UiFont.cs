using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Jones.Core.Text;

namespace Jones.App;

/// <summary>
/// THE INTERFACE FACE SWITCH.
///
/// <para>
/// Every button, shop line, job listing and price in the game is drawn in font 10
/// (`WButton.sc:32`, never overridden) — six pixels to a line, one-pixel stems, and at 4K each
/// of those pixels is an eleven-pixel block. This class draws that same text in **Quicksand**
/// instead, as an outline face, while leaving every coordinate the scripts give exactly where
/// it is.
/// </para>
///
/// <para>
/// SET <see cref="Requested"/> TO <see cref="Face.Bitmap"/> — or run with
/// <c>JONES_UI_FONT=bitmap</c>, which needs no rebuild — and the game draws font 10 again,
/// byte for byte as before. Nothing else changes: the switch is read once at startup, in the
/// same spirit as <see cref="RenderScale.Requested"/>, <c>MainViewModel.SmoothMotion</c> and
/// <c>Player.ShowPerGoalProgress</c>, so the two can be compared side by side.
/// </para>
///
/// <para>
/// WHAT IS AND IS NOT AFFECTED. Interface text only — font 10. The speech balloon (font 1),
/// the newspaper (font 3), the goals and statistics screens (font 4) and the calculator
/// readout (font 14) keep their own bitmap faces: each has its own geometry, and the
/// calculator's readout wanders as the figure grows because font 14's space is 4px against its
/// 5px digits, which is a quirk of the shipped game that is deliberately ported
/// (`StoreLayout.CalcDisplayLeft`).
/// </para>
///
/// <para>
/// THE DROP SHADOW SURVIVES. `WButton::draw` (`WButton.sc:52-65`) draws every label twice —
/// once at (+1,+1) in `shadowColor`, then at (0,0) in `textColor` — and <see cref="Render"/>
/// does the same, one GAME pixel apart at every zoom.
/// </para>
///
/// <para>
/// THE LICENCE TRAVELS WITH THE FONT. Quicksand is SIL Open Font License 1.1 and the licence is
/// shipped beside it as <c>Assets/fonts/OFL-Quicksand.txt</c>, which is what the OFL requires
/// of a bundled face. The file is unmodified, so no Reserved Font Name question arises.
/// </para>
/// </summary>
public static class UiFont
{
    public enum Face
    {
        /// <summary>The game's own font 10, exactly as before this class existed.</summary>
        Bitmap,

        /// <summary>Quicksand, positioned by measurement.</summary>
        Quicksand,
    }

    /// <summary>
    /// THE DEFAULT, AND IT IS THE ORIGINAL. A fresh install draws font 10, exactly as the
    /// shipped game does; Quicksand is the alternative, not the replacement.
    /// </summary>
    public const Face Default = Face.Bitmap;

    /// <summary>
    /// The weight actually drawn. Quicksand's variable file DEFAULTS to Light 300, which is too
    /// thin to survive a 5-pixel capital; Medium is the instance the comparison sheets were made
    /// at and the one that was chosen. <see cref="TrueTypeMetrics"/> applies the font's `HVAR`
    /// table so the advances are Medium's too, not Light's — a distinction worth 46/1000 em on
    /// the full stop alone, and the shop lists are made of leader dots.
    /// </summary>
    public const double Weight = 500;

    /// <summary>`avares` family reference. The `#` names the family inside the folder.</summary>
    private const string FamilyUri = "avares://Jones.App/Assets/fonts#Quicksand";

    private static Face? _face;

    /// <summary>
    /// THE SWITCH, AND IT IS LIVE. Assigning it changes the face for every line built from
    /// then on; the caller rebuilds the screen and the same list redraws in the other face,
    /// which is the only arrangement in which the two can honestly be compared — flipping it
    /// on the Factory's job list and watching the same nine rows change is the whole point.
    ///
    /// <para>
    /// It is a plain static rather than a setting the view models read, because every one of
    /// them is rebuilt by <c>BuildScreen</c> anyway. Nothing caches the answer across a
    /// rebuild: the bitmaps are keyed per face and <see cref="Interpolation"/> is carried on
    /// each view model rather than bound to this class, so a screen never ends up drawn in one
    /// face and sampled for the other.
    /// </para>
    ///
    /// <para>
    /// Starts at <c>JONES_UI_FONT=bitmap|quicksand</c> if that is set, else at whatever was
    /// persisted, else at <see cref="Default"/>. A font file that will not parse forces the
    /// bitmap face: a face that cannot be measured cannot be laid out, and the game must still
    /// come up.
    /// </para>
    /// </summary>
    public static Face Current
    {
        get
        {
            if (_face is { } f) return f;
            _face = Resolve(Default);
            return _face.Value;
        }
        set
        {
            var want = Resolve(value);
            if (_face == want) return;
            _face = want;
            _em = null;
        }
    }

    /// <summary>
    /// Sets the starting face from persisted settings, unless <c>JONES_UI_FONT</c> has already
    /// spoken. Called once before the UI is built, as the sound switches are.
    /// </summary>
    public static void LoadSetting(bool quicksand)
    {
        if (_face is not null) return;
        _face = Resolve(quicksand ? Face.Quicksand : Face.Bitmap);
    }

    private static Face Resolve(Face want)
    {
        var env = Environment.GetEnvironmentVariable("JONES_UI_FONT")?.Trim().ToLowerInvariant();
        want = env switch
        {
            "bitmap" or "sci" or "font10" or "0" => Face.Bitmap,
            "quicksand" or "vector" or "1" => Face.Quicksand,
            _ => want,
        };

        return want == Face.Quicksand && Metrics is null ? Face.Bitmap : want;
    }

    /// <summary>True when the interface is being drawn in the outline face.</summary>
    public static bool Enabled => Current == Face.Quicksand;

    /// <summary>Whether the outline face is available at all — false hides the switch.</summary>
    public static bool Available => Metrics is not null;

    /// <summary>
    /// How a text bitmap drawn in <paramref name="face"/> must be sampled. The bitmap face
    /// must stay <c>None</c> — smoothing a six-pixel bitmap face is vandalism, see
    /// <see cref="SciFont"/> — but the outline face is rasterised ABOVE the screen's own scale
    /// (see <see cref="TextScale"/>) and then fitted down, so it wants the resample.
    ///
    /// <para>
    /// This is taken PER LINE, at the moment the line is built, and carried on the view model.
    /// It used to be a static the XAML bound to with <c>x:Static</c>, which is evaluated once
    /// when the view loads — fine for a compile-time switch and quietly wrong for a live one,
    /// because after a toggle the old value would still be sampling the new bitmaps.
    /// </para>
    /// </summary>
    public static BitmapInterpolationMode InterpolationFor(bool outline) =>
        outline ? BitmapInterpolationMode.HighQuality : BitmapInterpolationMode.None;

    // ---- metrics -----------------------------------------------------------

    private static TrueTypeMetrics? _metrics;
    private static bool _metricsTried;

    /// <summary>The parsed face, or null when the font is not on the search path.</summary>
    public static TrueTypeMetrics? Metrics
    {
        get
        {
            if (_metricsTried) return _metrics;
            _metricsTried = true;

            try
            {
                var uri = new Uri("avares://Jones.App/Assets/fonts/Quicksand.ttf");
                if (AssetLoader.Exists(uri))
                {
                    using var s = AssetLoader.Open(uri);
                    using var ms = new System.IO.MemoryStream();
                    s.CopyTo(ms);
                    _metrics = TrueTypeMetrics.From(ms.ToArray(),
                        new Dictionary<string, double> { ["wght"] = Weight });
                }
            }
            catch (Exception e)
            {
                StartupLog.Blame("UiFont.Metrics (falling back to the bitmap face)", e);
            }

            return _metrics;
        }
    }

    /// <summary>
    /// The em size, in GAME pixels, that puts a Quicksand capital at exactly the height of a
    /// font 10 capital — measured from both files, never typed. Font 10's `X` is 5 rows and
    /// Quicksand's cap height is 0.704 em, so this comes out at 7.10.
    /// </summary>
    public static double EmSize
    {
        get
        {
            if (_em is { } e) return e;
            var cap = SciFont.Interface?.Metrics?.CapHeight ?? 5;
            _em = Metrics?.EmForCapHeight(cap) ?? 0;
            return _em.Value;
        }
    }

    private static double? _em;

    /// <summary>
    /// The baseline, in game pixels below a line's own top — font 10's, so that the outline
    /// face sits on exactly the baseline the bitmap face sat on and every `nsTop` in the
    /// scripts keeps meaning what it meant.
    /// </summary>
    public static int Baseline => SciFont.Interface?.Metrics?.Baseline ?? 5;

    /// <summary>Measures a string in the outline face, in game pixels.</summary>
    public static double Measure(string text) => Metrics?.Measure(text, EmSize) ?? 0;

    /// <summary>
    /// Lays a padded resource string out against the column stop font 10 gives it.
    /// <paramref name="value"/> is the right-hand column's text, or empty for a plain line.
    /// </summary>
    public static LaidOutLine Lay(string full, string label, string value)
    {
        var stop = SciFont.Interface?.Measure(full) ?? 0;
        return ColumnLayout.Lay(label, value, stop, Measure, EmSize);
    }

    // ---- the drawn box -----------------------------------------------------

    /// <summary>
    /// A line's bitmap is TALLER AND ONE PIXEL WIDER TO THE LEFT than the six-pixel box font 10
    /// draws into, because an outline face has real ascenders and real descenders. The view
    /// draws the image at this offset from the line's own coordinate and keeps the hit
    /// rectangle where it was, so nothing the scripts position moves.
    /// </summary>
    public static int BoxLeft => -1;

    /// <summary>The top of the drawn box, relative to the line's `nsTop`. Negative.</summary>
    public static int BoxTop
    {
        get
        {
            var asc = (Metrics?.InkAscentEm ?? 0) * EmSize;
            return (int)Math.Floor(Baseline - asc);
        }
    }

    /// <summary>The height of the drawn box, ascender to descender plus the shadow's pixel.</summary>
    public static int BoxHeight
    {
        get
        {
            var desc = (Metrics?.InkDescentEm ?? 0) * EmSize;
            return (int)Math.Ceiling(Baseline + desc) + 1 - BoxTop;
        }
    }

    /// <summary>
    /// The scale the glyphs are RASTERISED at, which is not always the scale the screen is
    /// drawn at. An outline face rasterised at 7 pixels and then enlarged by the Viewbox — the
    /// Android head's case, where <see cref="RenderScale.Factor"/> is 1 and the playfield is
    /// blown up 3.375× to fill a 1080-wide phone — would look worse than the bitmap font it
    /// replaced. So it is always rasterised at 4× the screen's scale or better and fitted down
    /// with <see cref="Interpolation"/>, which is the one place in this port where resampling
    /// is the right answer.
    /// </summary>
    public static int TextScale => Math.Max(RenderScale.Factor, 4);

    // ---- rendering ---------------------------------------------------------

    private static readonly Dictionary<string, Bitmap?> Cache = [];

    /// <summary>
    /// Draws a laid-out line. <paramref name="shadowArgb"/> is `WButton::draw`'s second pass —
    /// one game pixel down and right, underneath — and <paramref name="backArgb"/> is
    /// `Display`'s `dsBACKGROUND` band. The bitmap's top-left corresponds to
    /// (<see cref="BoxLeft"/>, <see cref="BoxTop"/>) relative to the line's own coordinate.
    /// </summary>
    public static Bitmap? Render(LaidOutLine line, uint argb, uint? shadowArgb, uint? backArgb)
    {
        if (line is null || line.Runs.Count == 0) return null;
        if (Metrics is null) return null;

        var key = string.Create(CultureInfo.InvariantCulture,
            $"{argb:X8}|{shadowArgb?.ToString("X8") ?? "-"}|{backArgb?.ToString("X8") ?? "-"}|{line.Width:F2}|")
            + string.Join('\u0001', RunKeys(line));

        if (Cache.TryGetValue(key, out var cached)) return cached;

        var bmp = Rasterise(line, argb, shadowArgb, backArgb);
        Cache[key] = bmp;
        return bmp;
    }

    private static IEnumerable<string> RunKeys(LaidOutLine line)
    {
        foreach (var r in line.Runs)
            yield return string.Create(CultureInfo.InvariantCulture, $"{r.X:F2}:{r.Text}");
    }

    private static Bitmap? Rasterise(LaidOutLine line, uint argb, uint? shadowArgb, uint? backArgb)
    {
        var scale = TextScale;
        var boxW = (int)Math.Ceiling(line.Width) - BoxLeft + 2;   // +1 shadow, +1 right bearing
        var boxH = BoxHeight;
        if (boxW <= 0 || boxH <= 0) return null;

        var typeface = new Typeface(new FontFamily(FamilyUri), FontStyle.Normal, FontWeight.Medium);

        var rtb = new RenderTargetBitmap(new PixelSize(boxW * scale, boxH * scale), new Vector(96, 96));

        using (var ctx = rtb.CreateDrawingContext())
        {
            // `dsBACKGROUND`: an opaque band the width of the text and the height of the FONT
            // BOX — font 10's six rows, not the outline face's taller one, because the band's
            // job is to blank what the shipped game blanked.
            if (backArgb is { } back)
            {
                var top = (0 - BoxTop) * scale;
                var h = (SciFont.Interface?.Height ?? 6) * scale;
                ctx.FillRectangle(new SolidColorBrush(back),
                    new Rect(-BoxLeft * scale, top, Math.Ceiling(line.Width) * scale, h));
            }

            if (shadowArgb is { } shadow)
                Draw(ctx, line, typeface, scale, new SolidColorBrush(shadow), 1, 1);

            Draw(ctx, line, typeface, scale, new SolidColorBrush(argb), 0, 0);
        }

        return rtb;
    }

    /// <summary>
    /// One pass of glyphs, GLYPH BY GLYPH at the pen positions
    /// <see cref="TrueTypeMetrics.Measure"/> computed.
    ///
    /// <para>
    /// Drawing the run as one string would be faster and would let the toolkit kern and shape
    /// it — and would mean the layout the tests check and the layout on screen were two
    /// different things, computed by two different pieces of code, one of which is a different
    /// implementation on Android. Advancing the pen by the font's own `hmtx` (plus `HVAR`) is
    /// also exactly what the interpreter did with font 10: no kerning, no shaping, sum the
    /// advances.
    /// </para>
    /// </summary>
    private static void Draw(DrawingContext ctx, LaidOutLine line, Typeface typeface,
                             int scale, IBrush brush, double dx, double dy)
    {
        var em = EmSize * scale;
        var baseline = (Baseline - BoxTop + dy) * scale;
        var weightPen = new Pen(brush, WeightGain * em)
        {
            LineJoin = PenLineJoin.Round,
            LineCap = PenLineCap.Round,
        };

        foreach (var run in line.Runs)
        {
            var x = (run.X - BoxLeft + dx) * scale;

            foreach (var c in run.Text)
            {
                var advance = (Metrics?.AdvanceEm(c) ?? 0) * em;

                if (c != ' ')
                {
                    var ft = new FormattedText(c.ToString(), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, typeface, em, brush);
                    var origin = new Point(x, baseline - ft.Baseline);

                    if (WeightGain > 0 && ft.BuildGeometry(origin) is { } glyph)
                        ctx.DrawGeometry(brush, weightPen, glyph);
                    else
                        ctx.DrawText(ft, origin);
                }

                x += advance;
            }
        }
    }

    /// <summary>
    /// HOW MUCH STEM TO ADD, in ems — and why the port adds any at all.
    ///
    /// <para>
    /// Quicksand ships as ONE file with a `wght` axis running 300..700 whose default is 300,
    /// and Avalonia's embedded font collection will not give us an arbitrary point on that
    /// axis: asked for 400, 500 or 600 it hands back the default Light 300, and only 700
    /// produces a different instance. Measured, not assumed —
    /// <c>JONES_FONT_SHEET</c> prints what the font manager actually resolved, and it prints
    /// <c>Quicksand Light 300</c> for every request below 600.
    /// </para>
    ///
    /// <para>
    /// Light is not what was chosen and not what the comparison sheets showed. A 5-pixel
    /// capital drawn at 0.040 em of stem is exactly the "thin joins drop out at phone size"
    /// failure `tools/samples/FONTS.md` warned about. So the Light outline is STROKED, which
    /// adds the pen's full width to every stem: Quicksand's stems measure 0.040 em at Light
    /// and 0.080 em at Medium, so a 0.040 em pen lands on Medium's weight exactly. The two
    /// steps between them, Regular at 0.060 and SemiBold at 0.100, confirm the axis is linear
    /// in stem width, so this is interpolation along the designer's own axis rather than a
    /// guess at what bolder should look like.
    /// </para>
    ///
    /// <para>
    /// It is NOT the usual faked bold: the ADVANCES are already Medium's, read from the font's
    /// own `HVAR` table, so the glyphs are spaced as the designer spaced them at this weight
    /// and only the strokes are synthesised. Set this to 0 to draw the Light instance as the
    /// toolkit hands it over.
    /// </para>
    /// </summary>
    public const double WeightGain = 0.040;
}
