using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Jones.App;

/// <summary>
/// Renders text using the game's own bitmap fonts, extracted from the resource volume.
///
/// This matters more than it sounds: drawing the interface with a modern TrueType face
/// makes everything look wrong even when every coordinate is correct, because the
/// original's glyphs are 4-6 pixels wide and a proportional outline font at the same
/// nominal size is two to three times wider. The layouts in the scripts assume the
/// original metrics.
///
/// THAT PARAGRAPH IS STILL TRUE AND IS NOW THE REASON <see cref="UiFont"/> EXISTS, rather
/// than the reason nothing else may be drawn. The interface text can be drawn in Quicksand,
/// behind a switch that is OFF by default — but only because the columns the padded strings
/// produce are re-measured onto the stops THIS class gives them, which is what the naive
/// version of the idea does not do. This class remains the default and the reference: it
/// draws the resource bytes unchanged, and every column the other face lands on is a width
/// <see cref="Measure"/> returned.
///
/// Format, verified against this game's resources:
///   0..1   u16  unused
///   2..3   u16  character count (128)
///   4..5   u16  line height
///   6+     u16  offset of each character's glyph, one per character
///
///   glyph: u8 width, u8 height, then ceil(width / 8) * height bytes,
///          one bit per pixel, most significant bit leftmost.
/// </summary>
public sealed class SciFont
{
    private readonly byte[] _data;

    /// <summary>
    /// The measuring half, in <c>Jones.Core</c> so that the column arithmetic and the tests
    /// that check it can reach the game's own metrics without a rendering platform. The
    /// parsing lives there now; this class is the rasteriser.
    /// </summary>
    public Jones.Core.Text.BitmapFontMetrics Metrics { get; }

    public int Height { get; }

    /// <summary>The font resource this was loaded from — part of a text bitmap's network recipe.</summary>
    public int Number { get; }

    private SciFont(byte[] data, int number)
    {
        Number = number;
        _data = data;
        Metrics = Jones.Core.Text.BitmapFontMetrics.From(data)!;
        Height = Metrics.Height;
    }

    private static readonly Dictionary<int, SciFont?> Cache = [];

    /// <summary>Loads a font by its resource number, or null if it is not present.</summary>
    public static SciFont? Load(int number)
    {
        if (Cache.TryGetValue(number, out var cached)) return cached;

        SciFont? font = null;
        try
        {
            var uri = new Uri($"avares://Jones.App/Assets/game/font_{number}.font");
            if (AssetLoader.Exists(uri))
            {
                using var s = AssetLoader.Open(uri);
                using var ms = new System.IO.MemoryStream();
                s.CopyTo(ms);
                font = new SciFont(ms.ToArray(), number);
            }
        }
        catch
        {
        }

        Cache[number] = font;
        return font;
    }

    private (int Width, int Height, int DataOffset) Glyph(char c) => Metrics.Glyph(c);

    public int Measure(string text) => Metrics.Measure(text);

    private readonly Dictionary<string, Bitmap?> _rendered = [];

    /// <summary>
    /// Renders a line of text to a bitmap. Pixels that are set take
    /// <paramref name="argb"/>; everything else is transparent, so the text sits over the
    /// game art the way the original's transparent text does.
    ///
    /// Always at 1:1 — the bitmap that comes back is the one the callers MEASURE, and
    /// every layout in the port is in the original's 320x200 space. When the screen is
    /// drawn at 4x the view asks <see cref="SciArt.HighRes"/> for the twin registered
    /// below, which is the same glyphs with every pixel blown up into an NxN block.
    ///
    /// WHY PIXEL-DOUBLED AND NOT RESAMPLED: smoothing a digitised photograph is the point
    /// of the upscale; smoothing a 6-pixel-tall bitmap face is vandalism. These glyphs
    /// were drawn a pixel at a time to be legible at exactly this size, and a cubic
    /// resample turns the one-pixel stems of font 10 into grey smudges. Replicating each
    /// pixel is what the original got from a CRT and from every integer-scaled port since;
    /// it is also exactly what the game looked like at 1x, only bigger.
    /// </summary>
    public Bitmap? Render(string text, uint argb) => Render(text, argb, null, null);

    /// <summary>
    /// The same, with the two things a raw <see cref="Render(string, uint)"/> cannot draw.
    ///
    /// <paramref name="shadowArgb"/> is <c>WButton::draw</c> (`WButton.sc:52-65`), which
    /// draws every label TWICE — once at (nsLeft+1, nsTop+1) in <c>shadowColor</c>, then
    /// again at (nsLeft, nsTop) in <c>textColor</c>. That is every shop line, job listing
    /// and menu line in the game. The bitmap that comes back is one pixel wider and taller
    /// than the plain one, which is exactly what <c>WButton::setTextSize</c> does to the
    /// control's own rectangle (`:125-132`, <c>(!= shadowColor 0)</c> added to nsRight and
    /// nsBottom).
    ///
    /// <paramref name="backArgb"/> is <c>Display</c>'s <c>dsBACKGROUND</c> — an opaque band
    /// the width of the text and the height of the font, under the glyphs. Twenty calls in
    /// the game pass a real colour rather than −1.
    ///
    /// The two never combine in this game: the only thing with a shadow is a
    /// <c>WButton</c>, whose <c>backColor</c> is −1 on the class and is never overridden
    /// (`WButton.sc:34`).
    /// </summary>
    public Bitmap? Render(string text, uint argb, uint? shadowArgb, uint? backArgb)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var key = $"{argb:X8}|{shadowArgb?.ToString("X8") ?? "-"}|{backArgb?.ToString("X8") ?? "-"}|{text}";
        if (_rendered.TryGetValue(key, out var cached)) return cached;

        var bmp = Rasterise(text, argb, shadowArgb, backArgb, 1);
        _rendered[key] = bmp;
        Net.ArtRecipes.Note(bmp, Jones.Net.ArtRef.Text(Number, text, argb, shadowArgb, backArgb));

        if (bmp is not null && RenderScale.Factor > 1)
        {
            var line = text;
            SciArt.RegisterHighRes(bmp,
                () => Rasterise(line, argb, shadowArgb, backArgb, RenderScale.Factor));
        }

        return bmp;
    }

    /// <summary>
    /// Draws the glyphs at <paramref name="scale"/>, each source pixel filling a
    /// scale x scale block. The result is exactly <c>scale</c> times the 1x bitmap in both
    /// directions, which is what lets the view swap one for the other without moving
    /// anything.
    /// </summary>
    private Bitmap? Rasterise(string text, uint argb, uint? shadowArgb, uint? backArgb, int scale)
    {
        if (scale < 1) scale = 1;

        var width = Measure(text);
        if (width <= 0 || Height <= 0) return null;

        // The shadow pass sits one pixel down and right, so the bitmap grows by one in
        // each direction — `WButton::setTextSize` grows the control by the same one pixel.
        var offset = shadowArgb is null ? 0 : 1;

        var outW = (width + offset) * scale;
        var outH = (Height + offset) * scale;
        var pixels = new uint[outW * outH];

        // `dsBACKGROUND`: an opaque band the size of the text rect, painted before the
        // glyphs. The band is the TEXT's rect, not the shadowed one — the two never occur
        // together here (see the Render overload).
        if (backArgb is { } back)
            for (var y = 0; y < Height * scale; y++)
                for (var x = 0; x < width * scale; x++)
                    pixels[y * outW + x] = back;

        // The shadow first, then the text over it, which is the order WButton::draw uses.
        if (shadowArgb is { } shadow) Blit(text, shadow, 1, 1, width, scale, outW, pixels);
        Blit(text, argb, 0, 0, width, scale, outW, pixels);

        var bmp = new WriteableBitmap(
            new PixelSize(outW, outH), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);

        using (var fb = bmp.Lock())
        {
            unsafe
            {
                var dst = (uint*)fb.Address;
                for (var i = 0; i < pixels.Length; i++) dst[i] = pixels[i];
            }
        }

        return bmp;
    }

    /// <summary>
    /// One pass of glyphs into <paramref name="pixels"/>, at a whole-pixel offset in the
    /// original's 320x200 space (so <paramref name="dx"/> of 1 is <c>scale</c> device
    /// pixels, and the shadow stays one game pixel away at every zoom).
    /// </summary>
    private void Blit(string text, uint argb, int dx, int dy,
                      int width, int scale, int outW, uint[] pixels)
    {
        var penX = 0;

        foreach (var c in text)
        {
            var (gw, gh, data) = Glyph(c);
            var bytesPerRow = (gw + 7) / 8;

            for (var y = 0; y < gh && y < Height; y++)
            {
                for (var x = 0; x < gw; x++)
                {
                    var byteIndex = data + y * bytesPerRow + x / 8;
                    if (byteIndex >= _data.Length) continue;

                    // Most significant bit is the leftmost pixel.
                    var bit = (_data[byteIndex] >> (7 - (x % 8))) & 1;
                    if (bit == 0) continue;

                    var px = penX + x;
                    if (px >= width) continue;

                    for (var sy = 0; sy < scale; sy++)
                    {
                        var row = ((y + dy) * scale + sy) * outW + (px + dx) * scale;
                        for (var sx = 0; sx < scale; sx++) pixels[row + sx] = argb;
                    }
                }
            }

            penX += gw;
        }
    }

    /// <summary>
    /// THE interface font. `theFont 10` is set on the WButton class (`WButton.sc:32`) and
    /// is never overridden by any instance or subclass, so every button, shop item, job
    /// listing and price in the game is drawn in font 10.
    ///
    /// This is not interchangeable with the other fonts. Font 10 gives `|` a 1-pixel-wide
    /// BLANK glyph — a hard space used for fine column alignment — where fonts 0, 1, 3, 4,
    /// 8 and 14 all draw a visible vertical bar. Render an interface string in any other
    /// font and every label sprouts pipes.
    /// </summary>
    public static SciFont? Interface => Load(10);

    /// <summary>
    /// THE dialogue font. `gUserFont` is 1 (`Main.sc:64`) and is never reassigned — its
    /// only other references in the whole floppy tree are `Interface.sc:75` and
    /// `Interface.sc:545`, both of which put it on the `DText` that every `Print` draws.
    /// No shopkeeper `Print` passes keyword 33 (`#font`), so every speech balloon in the
    /// game is font 1, line height 12.
    ///
    /// Not interchangeable with font 10: that is the interface face, six pixels tall, and
    /// was never used for dialogue.
    /// </summary>
    public static SciFont? Dialogue => Load(1);

    /// <summary>Alias kept for call sites that predate the font-10 discovery.</summary>
    public static SciFont? Default => Interface ?? Load(0);

    public static SciFont? Small => Interface ?? Load(4) ?? Load(0);
}
