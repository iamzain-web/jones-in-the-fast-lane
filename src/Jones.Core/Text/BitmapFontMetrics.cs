namespace Jones.Core.Text;

/// <summary>
/// The measuring half of an SCI bitmap font, split out of <c>Jones.App.SciFont</c> so that the
/// column arithmetic — and the tests that check it — can reach the game's own metrics without
/// dragging Avalonia in.
///
/// <para>
/// THIS IS THE AUTHORITY FOR EVERY COLUMN IN THE GAME. The original aligns its price and wage
/// columns entirely through padding baked into each label (`ItemText`), and that padding only
/// means anything in font 10's metrics: a space is 5px, `.` is 3px and `|` is a 1px BLANK.
/// When the interface is drawn in a proportional face the padding is meaningless, but the
/// column stop it produced is not — so the stop is recomputed here, from these bytes, rather
/// than typed into a table by eye.
/// </para>
///
/// Format, verified against this game's resources (and identical to the reader in
/// <c>SciFont</c>, which now uses this one):
/// <code>
///   0..1   u16  unused
///   2..3   u16  character count (128)
///   4..5   u16  line height
///   6+     u16  offset of each character's glyph, one per character
///   glyph: u8 width, u8 height, then ceil(width / 8) * height bytes, 1bpp, MSB leftmost.
/// </code>
/// </summary>
public sealed class BitmapFontMetrics
{
    private readonly byte[] _data;

    private BitmapFontMetrics(byte[] data)
    {
        _data = data;
        CharCount = ReadU16(2);
        Height = ReadU16(4);
    }

    /// <summary>Wraps the raw resource bytes. Null for anything too short to be a font.</summary>
    public static BitmapFontMetrics? From(byte[]? data) =>
        data is { Length: >= 6 } ? new BitmapFontMetrics(data) : null;

    /// <summary>The number of glyphs the resource declares — 128 in every font this game ships.</summary>
    public int CharCount { get; }

    /// <summary>The declared LINE height: 6 for font 10, which is one row taller than a capital.</summary>
    public int Height { get; }

    /// <summary>The raw bytes, for the rasteriser that draws from the same resource.</summary>
    public byte[] Data => _data;

    private int ReadU16(int offset) => _data[offset] | (_data[offset + 1] << 8);

    /// <summary>
    /// One glyph's width, height and the offset of its first row of bits. A character past the
    /// end of the table falls back to `?`, which is what the interpreter does.
    /// </summary>
    public (int Width, int Height, int DataOffset) Glyph(char c)
    {
        int index = c;
        if (index >= CharCount) index = '?';
        var offset = ReadU16(6 + index * 2);
        if (offset + 2 > _data.Length) return (0, 0, 0);
        return (_data[offset], _data[offset + 1], offset + 2);
    }

    /// <summary>
    /// The width of a string in this font: the sum of the glyph advances, with no kerning and
    /// no spacing of any kind — which is exactly what the interpreter's own `TextWidth` does
    /// and why the padding in `ItemText` lines the columns up at all.
    /// </summary>
    public int Measure(string text)
    {
        var w = 0;
        foreach (var c in text) w += Glyph(c).Width;
        return w;
    }

    /// <summary>
    /// Where the baseline sits inside the line box, measured down from its top.
    ///
    /// <para>
    /// SCI bitmap glyphs are TOP-ALIGNED in the box: font 10's `H` is 5 rows and starts at row
    /// 0, its `g` is 6 rows and the sixth is the descender. So the height of a capital IS the
    /// distance from the box top to the baseline, and the rest of the box is descender room.
    /// Measured rather than assumed: `X` is used because every font in the game has one and it
    /// has no overshoot, where `O` and `S` round past the cap line in some faces.
    /// </para>
    /// </summary>
    public int Baseline
    {
        get
        {
            var h = Glyph('X').Height;
            return h > 0 ? h : Height;
        }
    }

    /// <summary>The height of a capital, which is what a replacement face is sized to match.</summary>
    public int CapHeight => Baseline;
}
