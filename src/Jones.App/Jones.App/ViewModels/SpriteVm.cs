using System;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;

namespace Jones.App.ViewModels;

/// <summary>
/// One positioned piece of art on the 320x200 screen — the port's equivalent of the
/// original's DIcon. A screen is just a list of these, which is how the SCI scripts
/// themselves compose their dialogs (`add: background windowTitle exButton ...`).
/// </summary>
public sealed class SpriteVm
{
    /// <param name="sub">
    /// An in-between frame within <paramref name="cel"/>, for the smoothed walk cycle only.
    /// 0 — every caller but the board walker — is the cel itself. See
    /// <see cref="SciArt.SubCel"/>: the bitmap is still the 1x cel, at the 1x cel's size,
    /// so nothing this class measures changes.
    /// </param>
    public SpriteVm(int view, int loop, int cel, double x, double y,
                    Action? onClick = null, string? tooltip = null, int sub = 0)
    {
        View = view;
        Loop = loop;
        Cel = cel;
        X = x;
        Y = y;
        Tooltip = tooltip;
        Image = SciArt.SubCel(view, loop, cel, sub);
        if (onClick is not null) Command = Net.InputGate.Command(onClick);
    }

    /// <summary>Wraps an already-loaded bitmap, for backgrounds taken from a pic.</summary>
    public SpriteVm(double x, double y, Bitmap image, Action? onClick = null, string? tooltip = null)
    {
        X = x;
        Y = y;
        Image = image;
        Tooltip = tooltip;
        if (onClick is not null) Command = Net.InputGate.Command(onClick);
    }

    public int View { get; }
    public int Loop { get; }
    public int Cel { get; }
    public double X { get; }
    public double Y { get; }
    public string? Tooltip { get; }
    public Bitmap? Image { get; }
    public ICommand? Command { get; }

    public bool IsClickable => Command is not null;

    public double W => Image?.PixelSize.Width ?? 0;
    public double H => Image?.PixelSize.Height ?? 0;
}

/// <summary>
/// A clickable line of text — a shop item, a job, a menu option. The port's
/// <c>WButton</c> (`WButton.sc:28-160`), which is what every one of those is.
///
/// HOW A WBUTTON IS DRAWN (`WButton.sc:52-65`), and it is not once:
///
/// <code>
/// (if (and global535 shadowColor)
///     (self doDisplay: (+ nsLeft 1) (+ nsTop 1) shadowColor 1))
/// (self doDisplay: nsLeft nsTop textColor 0)
/// </code>
///
/// <c>global535</c> is 1 on any non-16-colour display (`Main.sc:1193`), so the CD build
/// always takes the coloured branch: a SHADOW one pixel down and right, then the text over
/// it. 109 instances in the game declare a <c>shadowColor</c> and 43 a <c>textColor</c>.
/// Drawing one flat bitmap was the port's single biggest visual gap.
///
/// AND THERE IS NO HOVER STATE. The second bitmap here is the PRESS colour:
/// <c>WButton::hilite</c> (`:67-78`) and <c>WButton::track</c> (`:80-114`) swap to
/// <c>flashColor</c> while the mouse button is DOWN and back to <c>textColor</c> on
/// release. Nothing in the original reacts to the pointer merely passing over. The
/// <c>#0000C0</c> hover this class used to carry was mine.
/// </summary>
public sealed class MenuLineVm
{
    /// <param name="textColour">
    /// <c>textColor</c>, as a PALETTE INDEX. The <c>WButton</c> class default is 0
    /// (`WButton.sc:33`) and six screens override it; <see cref="StoreLayout.LineColours"/>
    /// has the table with its citations.
    /// </param>
    /// <param name="shadowColour">
    /// <c>shadowColor</c>, palette index; the class default is 6 (`WButton.sc:35`). Zero
    /// means no shadow — that is the literal test in <c>WButton::draw</c> and in
    /// <c>setTextSize</c>.
    /// </param>
    /// <param name="flashColour">
    /// <c>flashColor</c>, palette index: the colour while the button is held down. 100 on
    /// the class (`WButton.sc:36`), 255 on the three QT Clothing items (`clothing.sc:176`,
    /// `:202`, `:228`).
    /// </param>
    /// <param name="enabled">
    /// <c>state</c> bit 0. <c>Item::handleEvent</c> (`Interface.sc:407-420`) ignores every
    /// event on a control whose bit 0 is clear, and <c>Item::enable</c> (`:385-390`) is what
    /// sets and clears it. A disabled control is still DRAWN — it just cannot be pressed.
    /// </param>
    /// <param name="selected">
    /// <c>state</c> bit 3, <c>Item::select</c> (`:392-400`). DEVIATION, and it is only used
    /// by the Pawn Shoppe's list: <c>WButton::select</c> (`WButton.sc:40-46`) overrides
    /// <c>Item::select</c> and does NOT redraw, so selecting a WButton changes nothing on
    /// screen in the original. The pawnable list is a <c>DSelector</c> whose highlight comes
    /// from the interpreter's own control drawing and is in no script, so the port shows the
    /// chosen row in <paramref name="flashColour"/> — a colour the scripts do name — rather
    /// than leaving the player no way to see what they picked.
    /// </param>
    /// <param name="key">
    /// <c>key</c>, the control's own accelerator: <c>Item::handleEvent</c> fires it on
    /// <c>(== (event message:) key)</c>. See <see cref="SciKey"/> for what the numbers are.
    /// </param>
    /// <param name="value">
    /// The text of the RIGHT-HAND COLUMN, where the line has one: `$89`, `$12 Hr.`. It is the
    /// same characters the concatenated <paramref name="text"/> already ends with, handed over
    /// separately so that <see cref="UiFont"/> can put the column where font 10 put it instead
    /// of trusting padding that only means something in font 10's metrics. Null for a line
    /// with no column, and IGNORED ENTIRELY when the bitmap face is in use — which is why
    /// nothing in `ItemText` had to be edited.
    /// </param>
    public MenuLineVm(string text, double x, double y, Action onClick,
                      int textColour = 0, int shadowColour = 6, int flashColour = 100,
                      double size = 7, string? tooltip = null,
                      bool enabled = true, bool selected = false, int key = 0,
                      string? value = null)
    {
        X = x;
        Y = y;
        Tooltip = tooltip ?? text;
        Command = Net.InputGate.Command(onClick);
        Enabled = enabled;
        Key = key;
        Args = new Jones.Net.LineDto(text, x, y, textColour, shadowColour, flashColour, size,
                                     tooltip, enabled, selected, key, value);

        var font = size <= 7 ? SciFont.Small : SciFont.Default;
        uint? shadow = shadowColour == 0 ? null : SciPalette.Argb(shadowColour);
        var normalArgb = SciPalette.Argb(selected ? flashColour : textColour);
        var pressedArgb = SciPalette.Argb(flashColour);

        // The measured size is the SHADOWED bitmap's, which is one pixel larger in each
        // direction when there is a shadow — `WButton::setTextSize` (`WButton.sc:125-132`):
        //     (= nsBottom (+ [temp40 2] nsTop (!= shadowColor 0)))
        //     (= nsRight  (+ [temp40 3] nsLeft (!= shadowColor 0)))
        // so the hit rectangle grows with it, which is the third consequence of the shadow
        // and the one nobody sees until they click the last pixel of a line.
        //
        // IT IS ALWAYS FONT 10's, whichever face draws the glyphs. The outline face is fitted
        // INTO this rectangle — its value column is right-aligned onto font 10's own right
        // edge — so the clickable area of every line in the game is unchanged by the switch.
        var offset = shadow is null ? 0 : 1;
        W = (font?.Measure(text) ?? 0) + offset;
        H = (font?.Height ?? 0) + offset;

        // Read ONCE, here, and carried on this instance: the face is a live switch and a line
        // must never be drawn in one face and sampled for the other.
        var outline = UiFont.Enabled && text.Length > 0;
        Smooth = outline;

        if (outline)
        {
            var line = UiFont.Lay(text, value is null ? text : text[..^value.Length], value ?? "");
            Normal = UiFont.Render(line, normalArgb, shadow, null);
            Pressed = UiFont.Render(line, pressedArgb, shadow, null);
            ImageDx = UiFont.BoxLeft;
            ImageDy = UiFont.BoxTop;
        }
        else
        {
            Normal = font?.Render(text, normalArgb, shadow, null);
            Pressed = font?.Render(text, pressedArgb, shadow, null);
        }

        ImageW = (Normal?.PixelSize.Width ?? 0) / (double)(outline ? UiFont.TextScale : 1);
        ImageH = (Normal?.PixelSize.Height ?? 0) / (double)(outline ? UiFont.TextScale : 1);
    }

    public double X { get; }
    public double Y { get; }
    public double W { get; }
    public double H { get; }

    /// <summary>
    /// Where the drawn bitmap sits relative to the line's own coordinate, in game pixels.
    /// Zero for the bitmap face; for the outline face the box reaches a pixel left and a
    /// pixel above, because a real ascender is taller than font 10's five-row capital and a
    /// real descender hangs below its six-row box. The HIT RECTANGLE does not move with it —
    /// see <see cref="W"/>.
    /// </summary>
    public double ImageDx { get; }
    public double ImageDy { get; }

    /// <summary>The drawn bitmap's size in game pixels, which is not the hit rectangle's.</summary>
    public double ImageW { get; }
    public double ImageH { get; }

    /// <summary>
    /// How this line's bitmap is sampled — <c>None</c> for the game's own pixels, a resample
    /// for the outline face. On the view model rather than on <see cref="UiFont"/> because the
    /// face can change while the game is running; see <see cref="UiFont.InterpolationFor"/>.
    /// </summary>
    public bool Smooth { get; }
    public string Tooltip { get; }
    public Bitmap? Normal { get; }

    /// <summary>The <c>flashColor</c> bitmap, shown while the button is held down.</summary>
    public Bitmap? Pressed { get; }

    /// <summary><c>state</c> bit 0. False means drawn but deaf to every event.</summary>
    public bool Enabled { get; }

    /// <summary><c>key</c>: 0 where the instance declares none.</summary>
    public int Key { get; }

    public ICommand Command { get; }

    /// <summary>What this line was built from, so a network joiner can build it identically.</summary>
    internal Jones.Net.LineDto Args { get; }
}

/// <summary>
/// A clickable rectangle over one of the speech balloon's <c>#button</c> buttons. The
/// button's frame and label are already in the balloon's part list; this is only the hit
/// area, following the same pattern as <see cref="HotspotVm"/>.
///
/// It is a separate collection from <see cref="HotspotVm"/> because it has to sit ABOVE
/// the full-screen click-to-dismiss layer, which everything else sits below.
/// </summary>
public sealed class BalloonButtonVm(double x, double y, double w, double h,
                                    string text, Action onClick)
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double W { get; } = w;
    public double H { get; } = h;

    /// <summary>The button's own label, from the script's `#button` argument.</summary>
    public string Text { get; } = text;

    public ICommand Command { get; } = Net.InputGate.Command(onClick);
}

/// <summary>A clickable rectangle with no art of its own, for board hotspots.</summary>
public sealed class HotspotVm(double x, double y, double w, double h,
                              Action onClick, string tooltip)
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double W { get; } = w;
    public double H { get; } = h;
    public string Tooltip { get; } = tooltip;
    public ICommand Command { get; } = Net.InputGate.Command(onClick);
}

/// <summary>
/// A draggable goal track, as the original's StarSlider. The pointer's y within the
/// 320x200 screen is handed back so the caller can apply the original's own mapping;
/// there are no step buttons in the real game.
/// </summary>
public sealed class GoalTrackVm(int index, double x, double y, double w, double h,
                                Action<int> onSetY)
{
    /// <summary>Which of the four goals this track controls.</summary>
    public int Index { get; } = index;

    public double X { get; } = x;
    public double Y { get; } = y;
    public double W { get; } = w;
    public double H { get; } = h;

    /// <summary>Called with a y coordinate in the original's 320x200 screen space.</summary>
    public void SetFromScreenY(int screenY) => onSetY(screenY);
}

/// <summary>
/// A line of text positioned in 320x200 space, as the original's Display calls.
///
/// Rendered with the game's own bitmap font rather than a system typeface — see
/// <see cref="SciFont"/> for why that is not cosmetic.
/// </summary>
public sealed class TextVm
{
    /// <param name="fontNumber">
    /// The font RESOURCE to draw with, when the source names one. Scripts pass it
    /// explicitly as `dsFONT` — the newspaper uses font 3 (`newspaper.sc:262`), the
    /// weekend font 4 (`weekend.sc:117`), the balloon font 1 (`gUserFont`) — and where a
    /// script says which face to use, guessing from `size` is just a slower way of
    /// getting it wrong. Null keeps the old size-based pick for callers not yet traced
    /// back to their script.
    /// </param>
    /// <param name="background">
    /// <c>dsBACKGROUND</c>, as a PALETTE INDEX, or <see cref="SciPalette.Transparent"/>
    /// (−1) for the transparent default. Twenty <c>Display</c> calls in the game pass a
    /// real colour, and it paints an opaque band the width of the text and the height of
    /// the font before the glyphs go down.
    ///
    /// NINETEEN OF THOSE TWENTY ARE INVISIBLE, measured rather than assumed: the artist
    /// filled the panel behind each one with the very index the script names, so the band
    /// and the art are the same colour. Sampling the shipped art at the coordinates the
    /// scripts draw at — view 696 at the broker's four headings and every price row, view
    /// 501 and 505 at the goals percentages, view 0 loop 4 under the calculator readout —
    /// gives a FLAT #6098C8 (93), #7088E0 (99) and #98A8B0 (101), exactly the declared
    /// backgrounds. In the original the band exists to blank the previous value in place;
    /// this port rebuilds the screen, so it has nothing to erase.
    ///
    /// The exception is the board's <c>Week #%2d</c> (`room1.sc:1165`, `:1324`,
    /// `Game.sc:117`), whose background 86 sits over a dithered strip of pic 11 that mixes
    /// #8890A0, #7088A0 and #708090 — there the band really is drawn, and it is the one
    /// place omitting it changed what you see.
    /// </param>
    /// <param name="shadow">
    /// A <c>shadowColor</c> palette index for the labels that are WButtons rather than
    /// Displays — the nine job-list headers and the Pawn Shoppe's three captions all
    /// declare one and are permanently disabled, so they draw exactly like a shop line but
    /// take no clicks. 0 for no shadow, which is <c>WButton::draw</c>'s own test.
    /// </param>
    /// <param name="value">
    /// The right-hand column's text where this label has one — only Hi-Tech U's enrollment fee
    /// does. See <see cref="MenuLineVm"/>'s own <c>value</c>; null everywhere else.
    /// </param>
    public TextVm(string text, double x, double y, double size = 8,
                  string colour = "#000000", bool bold = false, int? fontNumber = null,
                  int background = SciPalette.Transparent, int shadow = 0,
                  string? value = null)
    {
        Text = text;
        X = x;
        Y = y;

        // `size` selects a font rather than scaling one: the original has several fixed
        // bitmap faces and picks between them. 6-7 maps to the small face, larger to the
        // standard one.
        var font = fontNumber is { } n
            ? SciFont.Load(n)
            : size <= 7 ? SciFont.Small : SciFont.Default;

        var argb = ToArgb(colour);
        uint? shadowArgb = shadow == 0 ? null : SciPalette.Argb(shadow);
        uint? backArgb = background < 0 ? null : SciPalette.Argb(background);

        // INTERFACE TEXT ONLY. `fontNumber` null resolves to font 10 (`SciFont.Small` and
        // `SciFont.Default` are both the interface face), and 10 is what the outline face
        // replaces. The balloon's font 1, the newspaper's 3, the goals and statistics
        // screens' 4, the calculator's 14 and the winner plate's 8 are left exactly as they
        // are: each has its own geometry, and the calculator's creeping readout is a quirk of
        // the shipped game that is deliberately ported.
        var interfaceText = fontNumber is null or 10;
        var outline = UiFont.Enabled && interfaceText && text.Length > 0;
        Smooth = outline;

        if (outline)
        {
            var line = UiFont.Lay(text, value is null ? text : text[..^value.Length], value ?? "");
            Image = UiFont.Render(line, argb, shadowArgb, backArgb);
            Dx = UiFont.BoxLeft;
            Dy = UiFont.BoxTop;
        }
        else
        {
            Image = font?.Render(text, argb, shadowArgb, backArgb);
        }

        // W and H stay FONT 10's — several callers centre a label by measuring one of these
        // and halving its width, and the outline face is fitted into the bitmap face's own
        // width rather than given a new one.
        var offset = shadowArgb is null ? 0 : 1;
        W = outline ? (font?.Measure(text) ?? 0) + offset : Image?.PixelSize.Width ?? 0;
        H = outline ? (font?.Height ?? 0) + offset : Image?.PixelSize.Height ?? 0;

        ImageW = (Image?.PixelSize.Width ?? 0) / (double)(outline ? UiFont.TextScale : 1);
        ImageH = (Image?.PixelSize.Height ?? 0) / (double)(outline ? UiFont.TextScale : 1);

        Args = new Jones.Net.TextDto(text, x, y, size, colour, bold, fontNumber, background,
                                     shadow, value);
    }

    /// <summary>What this line was built from, so a network joiner can build it identically.</summary>
    internal Jones.Net.TextDto Args { get; }

    public string Text { get; }
    public double X { get; }
    public double Y { get; }
    public Bitmap? Image { get; }
    public double W { get; }
    public double H { get; }

    /// <summary>The drawn bitmap's offset from <see cref="X"/>/<see cref="Y"/>, in game pixels.</summary>
    public double Dx { get; }
    public double Dy { get; }

    /// <summary>The drawn bitmap's size in game pixels.</summary>
    public double ImageW { get; }
    public double ImageH { get; }

    /// <summary>Where the bitmap is actually drawn — <see cref="X"/> plus <see cref="Dx"/>.</summary>
    public double ImageX => X + Dx;
    public double ImageY => Y + Dy;

    /// <summary>
    /// How this label's bitmap is sampled. See <see cref="MenuLineVm.Interpolation"/>: the
    /// face is a live switch, so this cannot be a static the XAML binds once.
    /// </summary>
    public bool Smooth { get; }

    internal static uint ToArgb(string hex)
    {
        var s = hex.TrimStart('#');
        var r = Convert.ToByte(s.Substring(0, 2), 16);
        var g = Convert.ToByte(s.Substring(2, 2), 16);
        var b = Convert.ToByte(s.Substring(4, 2), 16);
        // Bgra8888, unpremultiplied.
        return (uint)(0xFF << 24 | r << 16 | g << 8 | b);
    }
}
