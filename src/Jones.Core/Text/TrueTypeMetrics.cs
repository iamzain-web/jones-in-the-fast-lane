using System;
using System.Collections.Generic;

namespace Jones.Core.Text;

/// <summary>
/// Just enough of a TrueType file to LAY OUT text with: the advance width of every character,
/// the cap height, and how far the ink actually reaches above and below the baseline.
///
/// <para>
/// WHY THE PORT PARSES ITS OWN METRICS RATHER THAN ASKING THE TOOLKIT. Two reasons, and both
/// are about the same thing — the layout has to be the same number on every head and inside a
/// test.
/// </para>
/// <list type="number">
/// <item>The tests live in <c>Jones.Tests</c>, which has no Avalonia and no display. The whole
///   point of the new column assertions is that a broken column goes red in CI rather than
///   being noticed on screen six weeks later, and a measurement that needs a rendering
///   platform cannot do that.</item>
/// <item>Whatever the desktop's font engine and Android's font engine each decide about
///   hinting, shaping and kerning, the port draws glyph by glyph at the pen positions computed
///   HERE. The two heads therefore put the price column in the same place to the pixel, and so
///   does the test.</item>
/// </list>
///
/// <para>
/// VARIABLE FONTS. Quicksand ships as a single file with a `wght` axis running 300..700 whose
/// DEFAULT is 300 (Light) — so reading `hmtx` alone gives Light's advances, and Light is not
/// the instance that was chosen. The difference is not academic: at Medium the full stop is
/// 204/1000 em against Light's 158, which is 29% wider, and the shop lists are full of leader
/// dots. `HVAR` is therefore read and applied. Checked against FreeType's own answer for the
/// same instance in <c>TrueTypeMetricsTests</c>.
/// </para>
/// </summary>
public sealed class TrueTypeMetrics
{
    private readonly byte[] _f;
    private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<char, ushort> _cmap = [];
    private readonly Dictionary<char, double> _advance = [];

    /// <summary>Design units per em — 1000 for Quicksand, 2048 for most Microsoft-era faces.</summary>
    public int UnitsPerEm { get; private set; } = 1000;

    /// <summary>The height of a capital as a fraction of the em. 0.704 for Quicksand.</summary>
    public double CapHeightEm { get; private set; }

    /// <summary>How far the tallest printable ASCII ink reaches ABOVE the baseline, in ems.</summary>
    public double InkAscentEm { get; private set; }

    /// <summary>How far the deepest printable ASCII ink reaches BELOW the baseline, in ems.</summary>
    public double InkDescentEm { get; private set; }

    private TrueTypeMetrics(byte[] file) => _f = file;

    /// <summary>
    /// Reads a font. <paramref name="axes"/> names a variation instance in USER coordinates —
    /// <c>{ ["wght"] = 500 }</c> for Medium — and is ignored by a static font. Returns null
    /// for anything that is not a parseable TrueType file, because a missing face must fall
    /// back to the game's own bitmap font rather than stop the game.
    /// </summary>
    public static TrueTypeMetrics? From(byte[]? file, IReadOnlyDictionary<string, double>? axes = null)
    {
        if (file is not { Length: > 12 }) return null;

        try
        {
            var m = new TrueTypeMetrics(file);
            m.Parse(axes);
            return m.CapHeightEm > 0 ? m : null;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException
                                       or OverflowException)
        {
            return null;
        }
    }

    // ---- big-endian readers ------------------------------------------------

    private byte U8(int o) => _f[o];
    private int U16(int o) => (_f[o] << 8) | _f[o + 1];
    private int S16(int o) { var v = U16(o); return v >= 0x8000 ? v - 0x10000 : v; }
    private long U32(int o) => ((long)_f[o] << 24) | ((long)_f[o + 1] << 16) | ((long)_f[o + 2] << 8) | _f[o + 3];
    private int S32(int o) => (_f[o] << 24) | (_f[o + 1] << 16) | (_f[o + 2] << 8) | _f[o + 3];

    /// <summary>F2Dot14: a signed 16-bit fixed-point number with 14 fractional bits.</summary>
    private double F2Dot14(int o) => S16(o) / 16384.0;

    private string Tag(int o) => $"{(char)_f[o]}{(char)_f[o + 1]}{(char)_f[o + 2]}{(char)_f[o + 3]}";

    // ---- the parse ---------------------------------------------------------

    private void Parse(IReadOnlyDictionary<string, double>? axes)
    {
        var numTables = U16(4);
        for (var i = 0; i < numTables; i++)
        {
            var o = 12 + i * 16;
            if (o + 16 > _f.Length) break;
            _tables[Tag(o)] = ((int)U32(o + 8), (int)U32(o + 12));
        }

        if (!_tables.TryGetValue("head", out var head)) return;
        UnitsPerEm = U16(head.Offset + 18);
        if (UnitsPerEm <= 0) UnitsPerEm = 1000;
        var longLoca = U16(head.Offset + 50) == 1;

        var numGlyphs = _tables.TryGetValue("maxp", out var maxp) ? U16(maxp.Offset + 4) : 0;

        ReadCmap();

        // The normalised position on each axis, which is what every variation table is
        // expressed against. No `avar` in Quicksand, so this is the plain linear map.
        var coords = NormaliseAxes(axes);

        ReadAdvances(numGlyphs, coords);
        ReadInk(head, longLoca, numGlyphs);

        // THE OUTLINE'S OWN `X`, NOT OS/2's sCapHeight. The two are close but not the same
        // thing — OS/2 carries the designer's declared cap line, where `X` is the ink that is
        // actually drawn, flat-topped and with no overshoot — and it is the ink that has to
        // line up with font 10's five-row capital. OS/2 is the fallback for a face whose
        // outlines cannot be read.
        CapHeightEm = _capFromOutline;
        if (CapHeightEm <= 0
            && _tables.TryGetValue("OS/2", out var os2) && U16(os2.Offset) >= 2 && os2.Length >= 90)
            CapHeightEm = S16(os2.Offset + 88) / (double)UnitsPerEm;
    }

    private double _capFromOutline;

    // ---- cmap (format 4 is all this game's ASCII needs) --------------------

    private void ReadCmap()
    {
        if (!_tables.TryGetValue("cmap", out var cmap)) return;

        var best = -1;
        var n = U16(cmap.Offset + 2);
        for (var i = 0; i < n; i++)
        {
            var rec = cmap.Offset + 4 + i * 8;
            var platform = U16(rec);
            var encoding = U16(rec + 2);
            var sub = cmap.Offset + (int)U32(rec + 4);
            // Windows Unicode BMP (3,1) first; (3,0) symbol and (0,x) Unicode as fallbacks.
            var score = (platform, encoding) switch
            {
                (3, 1) => 3,
                (0, _) => 2,
                (3, 0) => 1,
                _ => 0,
            };
            if (score > 0 && U16(sub) == 4 && score > best) { best = score; ReadCmap4(sub); }
        }
    }

    private void ReadCmap4(int t)
    {
        var segX2 = U16(t + 6);
        var ends = t + 14;
        var starts = ends + segX2 + 2;
        var deltas = starts + segX2;
        var ranges = deltas + segX2;

        for (var s = 0; s < segX2 / 2; s++)
        {
            int end = U16(ends + s * 2), start = U16(starts + s * 2);
            if (start > end || start > 0xFF) continue;

            int delta = S16(deltas + s * 2), rangeOffset = U16(ranges + s * 2);

            for (var c = start; c <= Math.Min(end, 0xFF); c++)
            {
                int g;
                if (rangeOffset == 0)
                {
                    g = (c + delta) & 0xFFFF;
                }
                else
                {
                    var gi = ranges + s * 2 + rangeOffset + (c - start) * 2;
                    if (gi + 1 >= _f.Length) continue;
                    g = U16(gi);
                    if (g != 0) g = (g + delta) & 0xFFFF;
                }
                if (g != 0) _cmap[(char)c] = (ushort)g;
            }
        }
    }

    /// <summary>The glyph id for a character, or 0 (.notdef) when the face has none.</summary>
    public ushort GlyphId(char c) => _cmap.TryGetValue(c, out var g) ? g : (ushort)0;

    // ---- fvar: user coordinates to normalised ones -------------------------

    private double[] NormaliseAxes(IReadOnlyDictionary<string, double>? axes)
    {
        if (!_tables.TryGetValue("fvar", out var fvar)) return [];

        var axisArray = fvar.Offset + U16(fvar.Offset + 4);
        var axisCount = U16(fvar.Offset + 8);
        var axisSize = U16(fvar.Offset + 10);

        var coords = new double[axisCount];
        for (var i = 0; i < axisCount; i++)
        {
            var o = axisArray + i * axisSize;
            var tag = Tag(o);
            double min = S32(o + 4) / 65536.0, def = S32(o + 8) / 65536.0, max = S32(o + 12) / 65536.0;

            var want = axes is not null && axes.TryGetValue(tag, out var v) ? v : def;
            want = Math.Clamp(want, min, max);

            coords[i] = want switch
            {
                _ when want == def => 0,
                _ when want < def => def > min ? (want - def) / (def - min) : 0,
                _ => max > def ? (want - def) / (max - def) : 0,
            };
        }

        return coords;
    }

    // ---- hmtx + HVAR -------------------------------------------------------

    private void ReadAdvances(int numGlyphs, double[] coords)
    {
        if (!_tables.TryGetValue("hmtx", out var hmtx) || !_tables.TryGetValue("hhea", out var hhea))
            return;

        var numH = U16(hhea.Offset + 34);
        var hvar = ReadHvar(coords);

        foreach (var (c, g) in _cmap)
        {
            var i = Math.Min(g, Math.Max(numH - 1, 0));
            var o = hmtx.Offset + i * 4;
            if (o + 1 >= _f.Length) continue;

            double adv = U16(o);
            if (hvar is not null) adv += hvar(g);

            _advance[c] = Math.Round(adv) / UnitsPerEm;
        }

        // A face with no `space` glyph still has to put a gap somewhere.
        if (!_advance.ContainsKey(' ') && _advance.TryGetValue('n', out var n)) _advance[' '] = n * 0.5;

        _ = numGlyphs;
    }

    /// <summary>
    /// `HVAR`: the per-glyph advance-width deltas for a variation instance. Returns null when
    /// the font has none (a static font, or a variable one whose advances do not vary), in
    /// which case `hmtx` is already the answer.
    /// </summary>
    private Func<ushort, double>? ReadHvar(double[] coords)
    {
        if (coords.Length == 0 || !_tables.TryGetValue("HVAR", out var hvar)) return null;

        var storeOffset = (int)U32(hvar.Offset + 4);
        if (storeOffset == 0) return null;
        var store = hvar.Offset + storeOffset;

        // ItemVariationStore
        if (U16(store) != 1) return null;
        var regionListOffset = store + (int)U32(store + 2);
        var dataCount = U16(store + 6);

        // VariationRegionList: one scalar per region, for the instance we are building.
        var axisCount = U16(regionListOffset);
        var regionCount = U16(regionListOffset + 2);
        var scalars = new double[regionCount];
        for (var r = 0; r < regionCount; r++)
        {
            var scalar = 1.0;
            for (var a = 0; a < axisCount; a++)
            {
                var o = regionListOffset + 4 + (r * axisCount + a) * 6;
                double start = F2Dot14(o), peak = F2Dot14(o + 2), end = F2Dot14(o + 4);
                var coord = a < coords.Length ? coords[a] : 0;

                // The spec's own rules, in its own order: a zero peak means the axis does not
                // take part, and anything outside [start,end] contributes nothing at all.
                if (peak == 0) continue;
                if (coord < start || coord > end) { scalar = 0; break; }
                if (coord == peak) continue;
                scalar *= coord < peak
                    ? (peak > start ? (coord - start) / (peak - start) : 0)
                    : (end > peak ? (end - coord) / (end - peak) : 0);
            }
            scalars[r] = scalar;
        }

        // Each ItemVariationData subtable: its own region subset and one delta set per item.
        var datas = new (int[] Regions, int ItemCount, int Offset, int WordCount, bool LongWords, int RowSize)[dataCount];
        for (var d = 0; d < dataCount; d++)
        {
            var sub = store + (int)U32(store + 8 + d * 4);
            var itemCount = U16(sub);
            var wordDeltaCount = U16(sub + 2);
            var longWords = (wordDeltaCount & 0x8000) != 0;
            var wordCount = wordDeltaCount & 0x7FFF;
            var regionIndexCount = U16(sub + 4);

            var regions = new int[regionIndexCount];
            for (var i = 0; i < regionIndexCount; i++) regions[i] = U16(sub + 6 + i * 2);

            var shortSize = longWords ? 2 : 1;
            var wordSize = longWords ? 4 : 2;
            var rowSize = wordCount * wordSize + (regionIndexCount - wordCount) * shortSize;

            datas[d] = (regions, itemCount, sub + 6 + regionIndexCount * 2, wordCount, longWords, rowSize);
        }

        var mapOffset = (int)U32(hvar.Offset + 8);
        var map = mapOffset == 0 ? null : ReadDeltaSetIndexMap(hvar.Offset + mapOffset);

        return glyph =>
        {
            var (outer, inner) = map is null ? (0, glyph) : map(glyph);
            if (outer >= datas.Length) return 0;

            var (regions, itemCount, dataOffset, wordCount, longWords, rowSize) = datas[outer];
            if (inner >= itemCount) return 0;

            var row = dataOffset + inner * rowSize;
            var delta = 0.0;
            var p = row;

            for (var i = 0; i < regions.Length; i++)
            {
                int v;
                if (i < wordCount)
                {
                    if (longWords) { v = S32(p); p += 4; }
                    else { v = S16(p); p += 2; }
                }
                else
                {
                    if (longWords) { v = S16(p); p += 2; }
                    else { v = (sbyte)U8(p); p += 1; }
                }

                var r = regions[i];
                if (r < scalars.Length && scalars[r] != 0) delta += scalars[r] * v;
            }

            return delta;
        };
    }

    /// <summary>
    /// A `DeltaSetIndexMap`, which turns a glyph id into an (outer, inner) pair naming one row
    /// of one ItemVariationData subtable. Glyph ids past the end of the map take the last
    /// entry, which is the spec's own rule and is how a font compresses a long tail.
    /// </summary>
    private Func<ushort, (int Outer, int Inner)> ReadDeltaSetIndexMap(int t)
    {
        var format = U8(t);
        var entryFormat = U8(t + 1);
        int mapCount, data;

        if (format == 0) { mapCount = U16(t + 2); data = t + 4; }
        else { mapCount = (int)U32(t + 2); data = t + 6; }

        var entrySize = ((entryFormat & 0x30) >> 4) + 1;
        var innerBits = (entryFormat & 0x0F) + 1;
        var innerMask = (1 << innerBits) - 1;

        return glyph =>
        {
            if (mapCount == 0) return (0, glyph);
            var i = Math.Min((int)glyph, mapCount - 1);
            var o = data + i * entrySize;

            var entry = 0;
            for (var b = 0; b < entrySize; b++) entry = (entry << 8) | U8(o + b);

            return (entry >> innerBits, entry & innerMask);
        };
    }

    // ---- glyf bounding boxes, for the ink extents --------------------------

    /// <summary>
    /// How far the ink really goes, from each glyph's own bounding box in `glyf`. The port
    /// needs this rather than the font's declared ascender and descender because those are a
    /// LINE BOX — Quicksand declares 1.0 up and 0.25 down, 1.25 em in all, which would not fit
    /// the Factory's 8px rows however the face is sized, while the ink is only 0.94 em and fits
    /// with room to spare. Sizing to the declared box is what makes a vector face look as
    /// though it cannot be used here.
    /// </summary>
    private void ReadInk(in (int Offset, int Length) head, bool longLoca, int numGlyphs)
    {
        if (!_tables.TryGetValue("loca", out var loca) || !_tables.TryGetValue("glyf", out var glyf))
            return;

        _ = head;
        double top = 0, bottom = 0, cap = 0;

        for (var c = (char)32; c < (char)127; c++)
        {
            var g = GlyphId(c);
            if (g == 0 || g >= numGlyphs) continue;

            int start, end;
            if (longLoca)
            {
                start = (int)U32(loca.Offset + g * 4);
                end = (int)U32(loca.Offset + g * 4 + 4);
            }
            else
            {
                start = U16(loca.Offset + g * 2) * 2;
                end = U16(loca.Offset + g * 2 + 2) * 2;
            }

            if (end <= start) continue;              // an empty glyph: the space
            var o = glyf.Offset + start;
            if (o + 10 > _f.Length) continue;

            double yMin = S16(o + 4), yMax = S16(o + 8);
            _ink[c] = (yMin / UnitsPerEm, yMax / UnitsPerEm);
            if (yMax > top) top = yMax;
            if (yMin < bottom) bottom = yMin;
            if (c == 'X') cap = yMax;
        }

        InkAscentEm = top / UnitsPerEm;
        InkDescentEm = -bottom / UnitsPerEm;
        _capFromOutline = cap / UnitsPerEm;
    }

    // ---- the public measuring surface --------------------------------------

    private readonly Dictionary<char, (double Min, double Max)> _ink = [];

    /// <summary>
    /// How far the ink of ONE STRING reaches above and below the baseline, in ems.
    ///
    /// <para>
    /// <see cref="InkAscentEm"/> and <see cref="InkDescentEm"/> are the worst case over all
    /// printable ASCII, which is what the drawn bitmap has to be sized for — a brace or a
    /// parenthesis reaches higher than any letter. Whether a row of text CLEARS the row below
    /// it is a different question, and it is about the characters that row actually contains.
    /// </para>
    /// </summary>
    public (double Ascent, double Descent) InkOf(string text)
    {
        double up = 0, down = 0;
        foreach (var c in text)
        {
            if (!_ink.TryGetValue(c, out var e)) continue;
            if (e.Max > up) up = e.Max;
            if (-e.Min > down) down = -e.Min;
        }
        return (up, down);
    }

    /// <summary>The advance of one character as a fraction of the em.</summary>
    public double AdvanceEm(char c) =>
        _advance.TryGetValue(c, out var a) ? a
        : _advance.TryGetValue('?', out var q) ? q
        : 0;

    /// <summary>
    /// The width of a string at <paramref name="emSize"/>, as the sum of the advances and
    /// nothing else. NO KERNING AND NO SHAPING, deliberately: the port draws each glyph at the
    /// pen position this returns, so what is measured here is exactly what lands on screen, on
    /// every head, and the same arithmetic runs in the tests.
    /// </summary>
    public double Measure(string text, double emSize)
    {
        var w = 0.0;
        foreach (var c in text) w += AdvanceEm(c) * emSize;
        return w;
    }

    /// <summary>The em size at which a capital is exactly <paramref name="capPixels"/> tall.</summary>
    public double EmForCapHeight(double capPixels) => CapHeightEm > 0 ? capPixels / CapHeightEm : 0;
}
