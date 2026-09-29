using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Jones.Core.Model;

namespace Jones.App.ViewModels;

/// <summary>
/// One piece of the speech balloon: either a flat colour rectangle or a view cel.
/// The balloon is a short painter's-order list of these, drawn in the same order
/// <c>BubbleWindow::open</c> draws them.
/// </summary>
public sealed class BalloonPartVm
{
    public double X { get; init; }
    public double Y { get; init; }
    public double W { get; init; }
    public double H { get; init; }

    /// <summary>Set for the code-drawn fills and outline; null for a cel.</summary>
    public IBrush? Fill { get; init; }

    /// <summary>Set for a corner cel, the tail cel, or a rendered line of text.</summary>
    public Bitmap? Image { get; init; }
}

/// <summary>
/// One of <c>Print</c>'s <c>#button</c> arguments — keyword <b>81</b>, handled at
/// <c>Interface.sc:113-121</c>, which reads a TEXT then a VALUE and makes a <c>DButton</c>
/// out of them. The value is what <c>Print</c> returns when that button is pressed
/// (<c>Interface.sc:247-252</c> maps the chosen item back to its <c>value:</c>).
/// </summary>
public readonly record struct BalloonButton(string Text, int Value);

/// <summary>
/// Where a balloon button ended up, in the 320x200 screen space, so the view can put a
/// hit rectangle over the art. The art itself is already in the balloon's part list.
/// </summary>
public readonly record struct BalloonButtonRect(int X, int Y, int W, int H, string Text, int Value);

/// <summary>
/// The game's speech balloon, ported from <c>BubbleWindow</c> — floppy script #113,
/// <c>scripts/jones-dos-1.000.060/src/BubbleWindow.sc</c>.
///
/// Every <c>Print</c> in the game goes through this window (<c>Interface.sc:59</c> sets
/// <c>window: gBubbleWindow</c> unconditionally), so this is the only dialogue chrome the
/// game has. Nothing here is invented: the numbers below are all either literals in
/// <c>BubbleWindow.sc</c>/<c>Interface.sc</c>/<c>Main.sc</c> or cel dimensions read off
/// the extracted art. See TALKER.md for the full citation list.
///
/// Coordinates are absolute in the 320x200 screen space — <c>BubbleWindow:open</c> does
/// <c>(SetPort 0)</c> first (<c>BubbleWindow.sc:26</c>), so the tail x/y the location
/// scripts pass are screen pixels, unrelated to the shop dialog's own origin.
/// </summary>
public static class BubbleWindow
{
    /// <summary>The balloon art lives in view 0 (<c>BubbleWindow.sc:13</c>, never overridden).</summary>
    private const int View = 0;

    /// <summary>
    /// The corner inset. This is the literal 12 in the five <c>grFILL_BOX</c> calls and the
    /// four <c>grDRAW_LINE</c> calls (<c>BubbleWindow.sc:159-219</c>) — not a cel
    /// measurement, though cels 12-15 do happen to be 12x12.
    /// </summary>
    private const int Corner = 12;

    /// <summary>
    /// The uniform text margin. <c>DText</c> sits at (5,5) inside the dialog
    /// (<c>Interface.sc:75</c>) and <c>Dialog:setSize</c> adds 5 to the right and bottom
    /// (<c>Interface.sc:1219-1247</c>), so the box is the wrapped text plus 5px all round.
    /// </summary>
    private const int Margin = 5;

    /// <summary>
    /// A colour scheme, as installed by <c>proc0_17</c> (<c>Main.sc:1146-1170</c>).
    /// <paramref name="Loop"/> is <c>global513</c>, the loop the corner and tail cels come
    /// from; <paramref name="Back"/> is <c>global512</c> and <paramref name="Text"/> is
    /// <c>global511</c>, given here as the RGB of that palette index in view 0's own
    /// palette. These are the VGA values (<c>global535</c> = 1).
    /// </summary>
    public readonly record struct Scheme(int Loop, string Back, string Text);

    /// <summary>
    /// The four schemes, keyed by <c>proc0_17</c>'s argument.
    /// 1: back 121 / colour 27; 2: back 119 / colour 26; 3: back 136 / colour 28;
    /// anything else: back 128 / colour 63, the game-wide default set at
    /// <c>Main.sc:1183</c>.
    /// </summary>
    public static Scheme SchemeFor(int arg) => arg switch
    {
        1 => new Scheme(6, "#F8C0A0", "#682028"),
        2 => new Scheme(7, "#9BEBD3", "#204838"),
        3 => new Scheme(8, "#C0D8F8", "#383848"),
        _ => new Scheme(5, "#F8E898", "#C03838"),
    };

    /// <summary>The outline colour: <c>vColor</c> 0 (<c>BubbleWindow.sc:14</c>) = #000000.</summary>
    private const string OutlineColour = "#000000";

    /// <summary>
    /// What each location's script sets up in its own <c>init</c> before any
    /// <c>Print</c>: the <c>proc0_17</c> colour scheme, then <c>global440</c> (tNum),
    /// <c>global441</c> (tail tip x) and <c>global442</c> (tail tip y). Width is the
    /// <c>#width</c> (keyword 70) that location's GREETING passes; every greeting passes
    /// 100 except the Bank's 130 (<c>bank.sc:135</c>), and Z-Mart passes none at all
    /// (<c>discount.sc:264</c>), which is the same 100 by default (<c>Interface.sc:77</c>).
    ///
    /// These are the only assignments to global440/441/442 in the floppy tree.
    /// </summary>
    public static (int Scheme, int TNum, int X, int Y, int Width)? TalkFor(LocationId id) => id switch
    {
        // appliance.sc:144,148-150 / appliance.sc:202
        LocationId.SocketCity       => (3, 1, 205, 132, 100),
        // bank.sc:80,84-86 / bank.sc:135
        LocationId.Bank             => (2, 7, 113,  82, 130),
        // clothing.sc:116,120-122 / clothing.sc:169
        LocationId.QtClothing       => (1, 3, 205,  80, 100),
        // discount.sc:175,180-182 / discount.sc:264 (no #width -> the default 100)
        LocationId.ZMart            => (1, 3, 209,  71, 100),
        // employment.sc:325,329-331 / employment.sc:374
        LocationId.EmploymentOffice => (1, 2, 205,  95, 100),
        // factory.sc:44,48-50 / factory.sc:84
        LocationId.Factory          => (3, 9, 129, 120, 100),
        // fastFood.sc:140,144-146 / fastFood.sc:195
        LocationId.MonolithBurgers  => (2, 3, 205,  80, 100),
        // market.sc:146,150-152 / market.sc:201
        LocationId.BlacksMarket     => (2, 3, 207,  78, 100),
        // pawnShop.sc:455,460-462 / pawnShop.sc:496
        LocationId.PawnShop         => (2, 7, 110,  78, 100),
        // rentOffice.sc:87,91-93 / rentOffice.sc:167
        LocationId.RentOffice       => (3, 3, 209,  74, 100),
        // university.sc:374,378-380 / university.sc:434
        LocationId.HiTechU          => (2, 7, 110,  78, 100),
        _ => null,
    };

    /// <summary>
    /// Wraps text the way the kernel's <c>TextSize</c> does for <c>DText:setSize</c>
    /// (<c>Interface.sc:548-552</c>): break at spaces so no line exceeds the requested
    /// width, then report the width actually used — the balloon shrinks to the longest
    /// real line rather than always being <paramref name="width"/> wide.
    /// </summary>
    private static List<string> Wrap(SciFont font, string text, int width)
    {
        var lines = new List<string>();

        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var current = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && font.Measure(candidate) > width)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }

            lines.Add(current);
        }

        return lines;
    }

    /// <summary>
    /// The gap between buttons, and between the last button and the box's right edge.
    /// <c>Interface.sc:119</c> accumulates <c>(+ nsRight 5)</c> per button and
    /// <c>Interface.sc:187</c> starts the next one at <c>(+ 5 nsRight)</c>.
    /// (The CD build's <c>Interface.sc:119</c> uses 4; the floppy — the authority for the
    /// balloon, see TALKER.md §1 — uses 5.)
    /// </summary>
    private const int ButtonGap = 5;

    /// <summary>
    /// <c>DButton</c>'s class default is <c>font 0</c> (<c>Interface.sc:586</c>) and
    /// <c>DButton:setSize</c> measures with it (<c>Interface.sc:590</c>). No <c>Print</c>
    /// in the game overrides a button's font, so every balloon button is font 0 —
    /// resource height 8 — NOT the dialogue font 1 and NOT the interface font 10.
    /// </summary>
    private const int ButtonFontNumber = 0;

    /// <summary>
    /// A button's own size — <c>DButton:setSize</c>, <c>Interface.sc:589-596</c>:
    /// <code>
    /// (TextSize @[r 0] text font)      ; [r 2] = height, [r 3] = width
    /// (+= [r 2] 2) (+= [r 3] 2)
    /// (= nsBottom (+ nsTop [r 2]))
    /// (= [r 3] (* (/ (+ [r 3] 15) 16) 16))   ; width rounded UP to a multiple of 16
    /// (= nsRight (+ [r 3] nsLeft))
    /// </code>
    /// Note the asymmetry: the height keeps its +2, the WIDTH's +2 is then swallowed by
    /// the rounding. `Yes` measures 21 in font 0, so its button is 32 wide and 10 tall.
    /// </summary>
    private static int ButtonWidth(SciFont font, string text) =>
        (font.Measure(text) + 2 + 15) / 16 * 16;

    /// <summary>
    /// Builds the balloon for one line of dialogue and appends its parts to
    /// <paramref name="into"/> in the order <c>BubbleWindow:open</c> paints them.
    /// Returns false if the font or the art is missing, in which case nothing is added.
    /// </summary>
    /// <param name="buttons">
    /// The <c>#button</c> (keyword 81) arguments, in the order the <c>Print</c> lists them.
    /// A balloon with buttons waits for one to be pressed: it passes no <c>#time</c>, and
    /// <c>Dialog:handleEvent</c>'s dismiss-on-anything branch is gated on the dialog having
    /// NO selectable item (<c>Interface.sc:1108-1125</c>), which a <c>DButton</c> is.
    /// </param>
    /// <param name="hits">
    /// Receives each button's rectangle so the caller can make it clickable. The buttons'
    /// pixels go into <paramref name="into"/> like everything else.
    /// </param>
    /// <param name="textFont">
    /// The <c>#font</c> (keyword 33) a <c>Print</c> passes, when it passes one. Null uses
    /// <c>DText</c>'s own — <c>DText:new</c> assigns <c>gUserFont</c> (<c>Interface.sc:60</c>,
    /// and again at <c>:75</c>), i.e. font 1. Script 990's four messages are the game's only
    /// <c>Print</c>s that override it, all to <c>#font 0</c> (<c>Save.sc:29</c>, <c>:60</c>,
    /// <c>:64</c>, <c>:106</c>).
    /// </param>
    /// <param name="mode">
    /// The <c>#mode</c> (keyword 30) a <c>Print</c> passes — <c>DText</c>'s alignment.
    /// <c>Print</c> defaults it to <b>1</b>, centred, at <c>Interface.sc:77</c>
    /// (<c>(temp10 setSize: (= temp19 100) mode: 1)</c>), which is why every balloon in the
    /// game is centred without saying so. <b>0 is left</b>, and exactly one caller passes
    /// it: the two Help boxes at <c>Menu.sc:194-195</c>, whose ten "Ctrl-Q - Quit" lines
    /// only line up flush left.
    /// </param>
    public static bool Build(IList<BalloonPartVm> into, string text,
                             int wrapWidth, int tNum, int tailX, int tailY, int schemeArg,
                             IReadOnlyList<BalloonButton>? buttons = null,
                             IList<BalloonButtonRect>? hits = null,
                             SciFont? textFont = null,
                             int mode = 1)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Font resource 1, gUserFont (Main.sc:64), line height 12. NOT font 10, which is
        // the interface font on WButton (WButton.sc:33).
        var font = textFont ?? SciFont.Dialogue;
        if (font is null) return false;

        var scheme = SchemeFor(schemeArg);
        var loop = scheme.Loop;

        var lines = Wrap(font, text, wrapWidth);
        var textW = lines.Count == 0 ? 0 : lines.Max(font.Measure);
        var textH = lines.Count * font.Height;
        if (textW <= 0) return false;

        // Dialog:setSize -> the box is the measured text plus a 5px margin all round.
        // This is `(temp1 setSize:)` at Interface.sc:172, BEFORE the buttons are added.
        var w = textW + Margin * 2;   // temp0 = right - left   (BubbleWindow.sc:50)
        var h = textH + Margin * 2;   // temp1 = bottom - top   (BubbleWindow.sc:51)

        // --- The button row ------------------------------------------------------------
        // Interface.sc:177-189. `temp25` is the whole run's length, accumulated at :119 as
        // the sum of (width + 5); `temp27` is where the run starts, which right-aligns it
        // against the box unless it is too long to fit, in which case it starts at 5 and
        // the box grows to it. Each button is then moved to (temp27, dialog.nsBottom) —
        // i.e. BELOW the text block — and `(temp1 setSize: center:)` at :189 unions the row
        // back in and adds another 5, so the box grows downward to contain the buttons.
        var buttonFont = SciFont.Load(ButtonFontNumber);

        // A question with no drawable buttons would be unanswerable, so refuse the whole
        // balloon rather than put up a modal box the player cannot get out of.
        if (buttons is { Count: > 0 } && buttonFont is null) return false;

        var btns = buttons ?? (IReadOnlyList<BalloonButton>)[];

        var buttonW = new int[btns.Count];
        var buttonH = 0;
        var run = 0;                                    // temp25

        if (btns.Count > 0)
        {
            buttonH = buttonFont!.Height + 2;           // (+= [r 2] 2), Interface.sc:591
            for (var i = 0; i < btns.Count; i++)
            {
                buttonW[i] = ButtonWidth(buttonFont, btns[i].Text);
                run += buttonW[i] + ButtonGap;          // Interface.sc:119
            }
        }

        var rowX = 0;                                   // temp27, relative to the box
        var rowY = h;                                   // dialog nsBottom at Interface.sc:185

        if (btns.Count > 0)
        {
            rowX = run > w ? ButtonGap : w - run;       // Interface.sc:177-183

            // Interface.sc:189's second `setSize:`. nsRight becomes the wider of the text's
            // own right edge (w - 5) and the last button's, then gains 5; nsBottom becomes
            // the button row's bottom, then gains 5.
            var lastRight = rowX + run - ButtonGap;
            w = Math.Max(w - Margin, lastRight) + Margin;
            h = rowY + buttonH + Margin;
        }

        var tail = tNum > 0 ? SciArt.Cel(View, loop, tNum - 1) : null;
        var celW = tail?.PixelSize.Width ?? 0;
        var celH = tail?.PixelSize.Height ?? 0;

        int left, top, right, bottom;
        int tailLeft = 0, tailTop = 0, tailRight = 0, tailBottom = 0;

        if (tNum != 0 && tail is not null)
        {
            // Tail top — BubbleWindow.sc:52-64.
            if (tNum == 1 || tNum >= 9) tailTop = tailY - celH;
            else if (tNum >= 3 && tNum <= 7) tailTop = tailY;
            else tailTop = tailY - celH / 2;

            // Tail left — BubbleWindow.sc:65-77.
            if (tNum <= 4 || tNum == 12) tailLeft = tailX - celW;
            else if (tNum >= 5 && tNum <= 10) tailLeft = tailX;
            else tailLeft = tailX - celW / 2;

            tailBottom = tailTop + celH;   // BubbleWindow.sc:78
            tailRight = tailLeft + celW;   // BubbleWindow.sc:79

            // Which side of the tail the balloon sits on — BubbleWindow.sc:85-102.
            // The +/-1 makes the body overlap the tail's base by one pixel.
            if (tNum <= 3)
            {
                right = tailLeft + 1; left = right - w;
                top = 0; bottom = h;
            }
            else if (tNum <= 6)
            {
                top = tailBottom - 1; bottom = top + h;
                left = 0; right = w;
            }
            else if (tNum <= 9)
            {
                left = tailRight - 1; right = left + w;
                top = 0; bottom = h;
            }
            else
            {
                bottom = tailTop + 1; top = bottom - h;
                left = 0; right = w;
            }

            // Where along that side — BubbleWindow.sc:103-152.
            switch (tNum)
            {
                case 1:  bottom = tailBottom + 12; top = bottom - h; break;
                case 2:  bottom = (tailBottom + tailTop) / 2 + h / 2; top = bottom - h; break;
                case 3:  top = tailTop - 12; bottom = top + h; break;
                case 4:  right = tailRight + 12; left = right - w; break;
                case 5:  right = (tailRight + tailLeft) / 2 + w / 2; left = right - w; break;
                case 6:  left = tailLeft - 12; right = left + w; break;
                case 7:  top = tailTop - 12; bottom = top + h; break;
                case 8:  bottom = (tailBottom + tailTop) / 2 + h / 2; top = bottom - h; break;
                case 9:  bottom = tailBottom + 12; top = bottom - h; break;
                case 10: left = tailLeft - 12; right = left + w; break;
                case 11: right = (tailRight + tailLeft) / 2 + w / 2; left = right - w; break;
                case 12: right = tailRight + 12; left = right - w; break;
            }

            // Short-balloon fixup — BubbleWindow.sc:153-156. Both statements are the `if`
            // body; there is no else. A one-line balloon is 22px tall, so for the side
            // tails (1-3, 7-9) this replaces the vertical placement above almost always.
            if (h <= 24 && (tNum <= 3 || (tNum >= 7 && tNum <= 9)))
            {
                top = tailTop - 6;
                bottom = top + h;
            }
        }
        else
        {
            // No tail: Dialog:center inside the window's brLeft/brTop/brRight/brBottom,
            // which for the shared bubbleWindow are the SysWindow defaults 0,0,320,190
            // (Game.sc:41-44; Main.sc:1234-1236 overrides nothing).
            left = (320 - w) / 2;
            top = (190 - h) / 2;
            right = left + w;
            bottom = top + h;
        }

        var back = new ImmutableSolidColorBrush(Color.Parse(scheme.Back));
        var ink = new ImmutableSolidColorBrush(Color.Parse(OutlineColour));

        // 1-5. The five fills — BubbleWindow.sc:159-172. Together they cover the whole
        // rect EXCEPT the four 12x12 corner squares, which the cels supply. A fill whose
        // edges cross over (a balloon shorter than 24px) covers nothing, which is exactly
        // what a degenerate grFILL_BOX does.
        AddFill(into, back, left + Corner, top + Corner, right - Corner, bottom - Corner);
        AddFill(into, back, left + Corner, top, right - Corner, top + Corner);
        AddFill(into, back, left, top + Corner, left + Corner, bottom - Corner);
        AddFill(into, back, left + Corner, bottom - Corner, right - Corner, bottom);
        AddFill(into, back, right - Corner, top + Corner, right, bottom - Corner);

        // 6. The four corner cels — BubbleWindow.sc:173-197. Each is placed from the edge
        // it hugs using its own CelWide/CelHigh, exactly as the DrawCel calls do.
        AddCorner(into, loop, 12, left, top, right, bottom, fromRight: false, fromBottom: false);
        AddCorner(into, loop, 13, left, top, right, bottom, fromRight: true,  fromBottom: false);
        AddCorner(into, loop, 14, left, top, right, bottom, fromRight: false, fromBottom: true);
        AddCorner(into, loop, 15, left, top, right, bottom, fromRight: true,  fromBottom: true);

        // 7. The 1px outline on the four straight edges, vColor 0 — BubbleWindow.sc:198-219.
        // Note the asymmetry: top/left sit on `top`/`left`, bottom/right on `bottom-1`/
        // `right-1`, so the box occupies [left, right-1] x [top, bottom-1] inclusive.
        AddHLine(into, ink, top, left + Corner, right - Corner);
        AddHLine(into, ink, bottom - 1, left + Corner, right - Corner);
        AddVLine(into, ink, left, top + Corner, bottom - Corner);
        AddVLine(into, ink, right - 1, top + Corner, bottom - Corner);

        // 8. The tail cel — BubbleWindow.sc:220-222.
        if (tail is not null)
            into.Add(new BalloonPartVm
            {
                X = tailLeft, Y = tailTop,
                W = tail.PixelSize.Width, H = tail.PixelSize.Height,
                Image = tail,
            });

        // 9. The text. The kernel paints it after the window opens, so it goes on top.
        // DText sits at (5,5) inside the box and mode 1 centres each line
        // (Interface.sc:75-77).
        var argb = TextVm.ToArgb(scheme.Text);
        for (var i = 0; i < lines.Count; i++)
        {
            var bmp = font.Render(lines[i], argb);
            if (bmp is null) continue;

            into.Add(new BalloonPartVm
            {
                X = left + Margin + (mode == 1 ? (textW - bmp.PixelSize.Width) / 2 : 0),
                Y = top + Margin + i * font.Height,
                W = bmp.PixelSize.Width,
                H = bmp.PixelSize.Height,
                Image = bmp,
            });
        }

        // 10. The buttons. Their POSITIONS and SIZES are the script's (above); their
        // APPEARANCE is not, and this is the one part of the balloon that is not derived
        // from the game source. `DButton` is `type 1` (Interface.sc:583) and `Item:draw`
        // hands a typed control straight to the `DrawControl` KERNEL (Interface.sc:508-510),
        // so the frame is drawn by the interpreter and appears nowhere in either script
        // tree. What is drawn here matches the screenshot: a 1px rounded box — a rectangle
        // with its four corner pixels left out — in the balloon's own text colour, with the
        // label centred inside. NOT IN SOURCE.
        var pen = new ImmutableSolidColorBrush(Color.Parse(scheme.Text));
        var x = left + rowX;

        for (var i = 0; i < btns.Count; i++)
        {
            var bw = buttonW[i];
            var by = top + rowY;

            AddHLine(into, pen, by, x + 1, x + bw - 2);
            AddHLine(into, pen, by + buttonH - 1, x + 1, x + bw - 2);
            AddVLine(into, pen, x, by + 1, by + buttonH - 2);
            AddVLine(into, pen, x + bw - 1, by + 1, by + buttonH - 2);

            var label = buttonFont!.Render(btns[i].Text, argb);
            if (label is not null)
                into.Add(new BalloonPartVm
                {
                    X = x + (bw - label.PixelSize.Width) / 2,
                    Y = by + (buttonH - buttonFont.Height) / 2,
                    W = label.PixelSize.Width,
                    H = label.PixelSize.Height,
                    Image = label,
                });

            hits?.Add(new BalloonButtonRect(x, by, bw, buttonH, btns[i].Text, btns[i].Value));

            x += bw + ButtonGap;                        // Interface.sc:187
        }

        return true;
    }

    private static void AddFill(IList<BalloonPartVm> into, IBrush brush,
                                int x1, int y1, int x2, int y2)
    {
        if (x2 <= x1 || y2 <= y1) return;
        into.Add(new BalloonPartVm { X = x1, Y = y1, W = x2 - x1, H = y2 - y1, Fill = brush });
    }

    private static void AddCorner(IList<BalloonPartVm> into, int loop, int cel,
                                  int left, int top, int right, int bottom,
                                  bool fromRight, bool fromBottom)
    {
        var bmp = SciArt.Cel(View, loop, cel);
        if (bmp is null) return;

        into.Add(new BalloonPartVm
        {
            X = fromRight ? right - bmp.PixelSize.Width : left,
            Y = fromBottom ? bottom - bmp.PixelSize.Height : top,
            W = bmp.PixelSize.Width,
            H = bmp.PixelSize.Height,
            Image = bmp,
        });
    }

    // grDRAW_LINE takes two points and draws between them whichever way round they are,
    // so the endpoints are normalised and both are included.
    private static void AddHLine(IList<BalloonPartVm> into, IBrush brush, int y, int x1, int x2)
    {
        var lo = Math.Min(x1, x2);
        var hi = Math.Max(x1, x2);
        into.Add(new BalloonPartVm { X = lo, Y = y, W = hi - lo + 1, H = 1, Fill = brush });
    }

    private static void AddVLine(IList<BalloonPartVm> into, IBrush brush, int x, int y1, int y2)
    {
        var lo = Math.Min(y1, y2);
        var hi = Math.Max(y1, y2);
        into.Add(new BalloonPartVm { X = x, Y = lo, W = 1, H = hi - lo + 1, Fill = brush });
    }
}
