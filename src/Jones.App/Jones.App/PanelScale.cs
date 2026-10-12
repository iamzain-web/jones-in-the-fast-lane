using System;
using System.Globalization;

namespace Jones.App;

/// <summary>
/// THE IN-LOCATION PANEL SWITCH — a DELIBERATE DEVIATION, requested by Zain, and built the
/// way <see cref="RenderScale"/> and <c>MainViewModel.SmoothMotion</c> are built: the
/// original is one value away and the arithmetic above it never changes.
///
/// <para>
/// WHAT THE ORIGINAL DOES. Every building interior is one Dialog at <c>moveTo: 69 44</c>
/// with <c>nsRight 184</c> and <c>nsBottom 119</c> (`fastFood.sc:50-52` and its twelve
/// siblings), so the whole room — backdrop, shopkeeper, price lines, item panel, WORK and
/// DONE — is laid out as <c>nsLeft</c>/<c>nsTop</c> offsets from (69,44). One thing is NOT:
/// <c>gCalc</c>, the calculator that shows your cash, is at the ABSOLUTE (252,160)
/// (`room1.sc:1419`) and every location script draws it on entry and erases it on the way
/// out (`room1.sc:39`). It happens to land exactly on the dialog's own bottom-right corner —
/// (252,160) is (69+183, 44+116) — so it is treated here as one more dialog-relative part,
/// which is what keeps it from being left behind.
/// </para>
///
/// <para>
/// THE GROUP IS 184x124 AT (69,44), because the calculator's BODY is no longer drawn — see
/// <see cref="CalculatorBody"/>, which is the deviation that made this whole trade easy. The
/// cash figure alone sits at dialog-relative (150,117) and is 34x7, so the group is the
/// Dialog's own box deepened by five rows. While the body was drawn the group was 244x150:
/// that 61x34 cel hung out to (244,150) from the same origin and was, by itself, the largest
/// thing the interior put on top of the town.
/// </para>
///
/// <para>
/// WHAT THE BOARD LEAVES FREE. The thirteen <c>Place</c> rectangles in `room1.sc:294` (see
/// <c>Board.All</c>) ring the screen. The largest rectangle that touches none of them is
/// COLUMNS 68..250 and ROWS 44..157 — 183 x 114 — bounded on the left by the Bank, Black's
/// Market, the Factory and Le Security all ending at column 67, on the right by Monolith,
/// QT Clothing and Socket City all starting at column 251, above by the top row ending at
/// row 43 and below by the Employment Office and the University starting at row 158.
/// </para>
///
/// <para>
/// SO THE LARGEST THE GROUP CAN BE WITHOUT TOUCHING A BUILDING is 183/184 = 0.995 by width
/// and 114/126 = 0.905 by height; HEIGHT binds — <see cref="LargestWithNoCoverage"/>. That
/// number is kept and pinned, but it is NOT what this port draws:
/// <see cref="Requested"/> is 1, because the margin any smaller panel opens is blank board
/// rather than town. See <see cref="Requested"/>.
/// </para>
///
/// <para>
/// BECAUSE THERE IS NO TOWN UNDER THE DIALOG. `pic 11` has a flat #F8F8F0 HOLE in the middle
/// — columns 70..248 of rows 45..153, measured off the shipped picture and 93.4% a single
/// colour — and the backdrop is sized to fill it: it takes 0.9891 of the width and 0.9821 of
/// the height to cover that hole. So shrinking the interior does not uncover buildings, it
/// uncovers BLANK BOARD, and at 0.75 that is 44 bare columns to the right of the panel and
/// 26 bare rows below it. The thirteen buildings were never behind the dialog in the first
/// place: at full size it covers a 2-pixel column of Monolith, QT Clothing and Socket City,
/// five rows off the Employment Office's and the University's roofs, and — by far the
/// largest, and by the CALCULATOR rather than the dialog — 60x33 of Socket City.
/// </para>
///
/// <para>
/// SO THE SCALE IS A TRADE, NOT AN OPTIMUM — and on today's board every rung of it loses.
/// 0.75 leaves 44 bare columns and 26 bare rows; <see cref="TriedElevenTwelfths"/> leaves 13
/// and 7. Both are #F8F8F0 and nothing else, and the second shipped and came straight back:
/// <i>"I DO NOT LIKE THE UGLY WHITE BARS ON THE RIGHT AND BOTTOM"</i>. The port therefore
/// draws the interior at FULL SIZE.
/// </para>
///
/// <para>
/// HOLD IT THERE. The board's blank middle is being filled with generated town, and once
/// there is something under the dialog the trade changes completely — shrinking would start
/// buying back real buildings instead of cream. The right order was always to fill the hole
/// first and decide the scale against what is behind it; this file keeps every number needed
/// to decide it then, and none of them should be acted on before.
/// </para>
///
/// <para>
/// ELEVENTH TWELFTHS, NOT 0.9, AND THE REASON IS SHARPNESS. <see cref="RenderScale.Factor"/>
/// is 4, 8 or 12, and a scale whose product with the factor is a whole number keeps every
/// coordinate and every bitmap on an exact device pixel — which matters most for the game's
/// own 6-pixel font 10, whose one-pixel stems turn to uneven grey the moment they are
/// resampled. Whole at ALL THREE factors needs 4s to be an integer, so between 0.75 and 1
/// there is nothing. This build runs at 12 (`RenderScale.Requested`), where the exact
/// fractions are twelfths: 11/12 gives 11 device pixels per game pixel, where 0.9 gives 10.8
/// and 0.875 gives 10.5. 0.875 is exact only at factor 8, which this build does not use.
/// </para>
///
/// <para>
/// TO GO BACK TO THE ORIGINAL: turn the switch beside the sound switches off, or set
/// <see cref="Requested"/> to 1, or run with <c>JONES_PANEL_SCALE=1</c> in the environment,
/// which needs no rebuild. At 1 the origin returns to the script's own (69,44) and the
/// transform is the identity — not "nearly" the identity: <see cref="Of"/> returns
/// <see cref="PanelTransform.None"/> and every Draw coordinate is the game coordinate.
/// </para>
///
/// <para>
/// THAT RESTORES THE GEOMETRY AND NOT THE WHOLE ROOM, and the difference is worth stating
/// rather than being discovered. Two other deviations live inside this panel and have their
/// own switches: the calculator body (<see cref="CalculatorBody"/>, <c>JONES_CALCULATOR=1</c>)
/// and the three board elements that are hidden while the panel is scaled — those come back
/// on their own at scale 1, since they are gated on it. Sierra's room exactly is
/// <c>JONES_PANEL_SCALE=1 JONES_CALCULATOR=1</c>.
/// </para>
///
/// <para>
/// NOTHING ABOVE THIS CLASS KNOWS ABOUT IT. The view models still compute
/// <c>DialogX + nsLeft</c> in the original's 320x200 space, exactly as the scripts do, and
/// still measure the 1x fonts; the transform is applied in the VIEW, through the
/// <c>DrawX</c>/<c>DrawY</c>/<c>DrawW</c>/<c>DrawH</c> properties the XAML binds. That is
/// the same separation <see cref="RenderScale"/> insists on and for the same reason: the
/// original's arithmetic, including its off-by-ones and its integer division, stays
/// bit-for-bit what it was.
/// </para>
/// </summary>
public static class PanelScale
{
    /// <summary>The interior dialog's origin — `moveTo: 69 44`, in every location script.</summary>
    public const int DialogLeft = 69;
    public const int DialogTop = 44;

    /// <summary>
    /// THE CALCULATOR BODY. False — the default — draws the cash figure alone, at
    /// <see cref="StoreLayout.CashLeft"/>; true restores `gCalc`, the 61x34 cel at the
    /// absolute (252,160).
    ///
    /// <para>
    /// A USER-REQUESTED CHANGE, and it is the one that dissolved a problem rather than
    /// trading it. The calculator was the single largest thing the interior put on top of
    /// the town — 60x33 of Socket City at full size — and it could not be moved: a search of
    /// all 320x200 for a 61x34 rectangle clear of the thirteen `Place` rectangles, the parked
    /// marble, the clock, the week counter and the panel returns ZERO positions, the best
    /// alternative (237,137) is worse than where it already sat, and the first size that fits
    /// anywhere at all is 0.70 — at which the readout's own six characters no longer fit the
    /// cel at the script's offsets. Removing the body takes that 60x33 to nothing and shrinks
    /// the group from 244x150 to 184x124, which is what lifts
    /// <see cref="LargestWithNoCoverage"/> from 0.75 to 0.919.
    /// </para>
    ///
    /// <para>
    /// WHAT IS KEPT: the value, its format (text 1[3] `"%6s "`), font 14, colour index 0 and
    /// the opaque band of index 101 — all `room1.sc:1437-1455`'s own — and WHEN it appears,
    /// which is inside a building and nowhere else (`room1.sc:39` erases it on the way out).
    /// Only the container goes.
    /// </para>
    ///
    /// <para>
    /// <c>JONES_CALCULATOR=1</c> puts the body back without a rebuild, which is the only way
    /// to compare the two.
    /// </para>
    /// </summary>
    public static bool CalculatorBody =>
        Environment.GetEnvironmentVariable("JONES_CALCULATOR") is "1";

    /// <summary>
    /// The group's true extent from the dialog's origin, WITH the calculator body gone: the
    /// 184x119 box, deepened to 131 by the cash figure, which sits at dialog-relative
    /// (128,119) and is 56x12 (font 8 — see <see cref="StoreLayout.CashFont"/>) — so
    /// 128+56 = 184 is the box's own right edge and 119+12 = 131 is twelve rows below its
    /// floor. It was 126 while the figure was drawn in the script's font 14 at 34x7.
    ///
    /// <para>
    /// It was 244x150 while `gCalc` was drawn, because that cel hung out to (244,150) from
    /// the same origin. Restoring it with <see cref="CalculatorBody"/> restores those
    /// numbers too, and the arithmetic below then describes a group this port no longer
    /// draws — which is why <see cref="LargestWithNoCoverage"/> is stated against THIS one.
    /// </para>
    /// </summary>
    public const int GroupWidth = 184;
    public const int GroupHeight = 131;

    /// <summary>
    /// The largest rectangle on the board that no <c>Place</c> rectangle touches: columns
    /// 68..250, rows 44..157. See the class remarks for which building bounds each edge.
    /// </summary>
    public const int FreeLeft = 68;
    public const int FreeTop = 44;
    public const int FreeWidth = 183;
    public const int FreeHeight = 114;

    /// <summary>
    /// `pic 11`'s blank middle: a flat #F8F8F0 rectangle at columns 70..248 of rows 45..153,
    /// which is 93.4% one colour. This is what the dialog exists to fill and what any smaller
    /// dialog leaves showing. Measured off the shipped picture, not assumed.
    /// </summary>
    public const int HoleLeft = 70;
    public const int HoleTop = 45;
    public const int HoleWidth = 179;
    public const int HoleHeight = 109;

    /// <summary>
    /// THE SWITCH, AND IT IS BACK AT 1 — Sierra's own full-size interior.
    ///
    /// <para>
    /// 11/12 was tried and WITHDRAWN. It was a reasonable trade against the arithmetic and a
    /// bad one against the screen: the margin a smaller panel opens up is `pic 11`'s
    /// <b>#F8F8F0 hole</b> and nothing else, so at 11/12 the player sees two plain cream bars,
    /// 13 columns down the right and 7 rows along the bottom. Shrinking buys nothing until
    /// there is a town behind the panel to buy.
    /// </para>
    ///
    /// <para>
    /// AND FULL SIZE IS NOW NEARLY FREE, which is what makes the revert easy rather than a
    /// retreat. With <see cref="CalculatorBody"/> gone the interior is the Dialog alone: the
    /// 60x33 of Socket City and the 1626 px² the group cost at 11/12 both went with the
    /// calculator. What is left at 1 is Sierra's own footprint — a 1-pixel column of Monolith,
    /// QT Clothing and Socket City where the backdrop's right edge lands, and the button row
    /// clipping the Employment Office's and the University's roofs — which is not a cost of
    /// this port at all. It is what the 1990 game draws.
    /// </para>
    ///
    /// <para>
    /// THE DECISION IS TO BE RE-MADE ONCE THE BOARD'S CENTRE IS FILLED, against what is
    /// actually behind the panel then, and not before. Everything needed to re-make it is
    /// still here: <see cref="TriedElevenTwelfths"/>, <see cref="LargestWithNoCoverage"/> and
    /// the twelfths argument below.
    /// </para>
    /// </summary>
    public const double Requested = 1.0;

    /// <summary>
    /// THE SCALE THAT WAS TRIED AND WITHDRAWN, kept because the arithmetic behind it is still
    /// the arithmetic and the question is still open.
    ///
    /// <para>
    /// 11/12 rather than a round 0.9 for sharpness: a scale whose product with
    /// <see cref="RenderScale.Factor"/> is whole keeps every coordinate and bitmap on an exact
    /// device pixel, which matters most for the game's own 6-pixel font 10 and its one-pixel
    /// stems. Whole at ALL of 4, 8 and 12 needs 4s to be an integer, so between 0.75 and 1
    /// there is nothing; this build runs at 12, where 11/12 gives a whole 11 device pixels per
    /// game pixel against 0.9's 10.8 and 0.875's 10.5. (0.875 is exact only at factor 8.)
    /// </para>
    ///
    /// <para>
    /// At this scale the DIALOG covers no building at all and the only thing that touches one
    /// is the cash figure's bottom edge, on about 47 px² of the University's roof — see
    /// <c>PanelScaleTests</c>, which still pins both.
    /// </para>
    /// </summary>
    public const double TriedElevenTwelfths = 11.0 / 12.0;

    /// <summary>
    /// Whether the panel is drawn at anything other than full size, and therefore whether the
    /// chrome switch has anything to switch. False while <see cref="Requested"/> is 1 and
    /// <c>JONES_PANEL_SCALE</c> is unset, which hides the control — a visible toggle that does
    /// nothing is worse than no toggle, and this is the same gate
    /// <c>MainViewModel.QuicksandFontAvailable</c> puts on the font switch beside it.
    /// </summary>
    public static bool Scalable => Wanted < 1.0;

    /// <summary>
    /// The largest scale at which the interior touches none of the thirteen <c>Place</c>
    /// rectangles. With the group at 184x126 in a 183x114 hole it is the HEIGHT that binds —
    /// 114/126 = 0.9048, against 183/184 = 0.9946 by width.
    ///
    /// <para>
    /// IT WAS 0.75 while the calculator body was drawn (183/244, width-bound), and that
    /// number is the whole reason the first attempt looked ridiculous: it left 44 columns and
    /// 26 rows of blank #F8F8F0 board showing. Removing the body bought back everything
    /// between 0.75 and 0.919.
    /// </para>
    ///
    /// <para>
    /// <see cref="TriedElevenTwelfths"/> SITS OUTSIDE IT, and that was a choice taken with the
    /// numbers in front of it rather than an oversight. Butted straight against DONE the
    /// figure reads as a caption of that button rather than as the cash, so it is two rows
    /// lower, and that costs ceiling: 114/129 = 0.884 at row 117 against 114/131 = 0.870 at
    /// 119. That reasoning is unaffected by the revert to full size and the two rows stay.
    /// </para>
    ///
    /// <para>
    /// THE BIGGER FACE MOVED THESE NUMBERS AND THEY ARE RESTATED RATHER THAN LEFT STALE.
    /// While the figure was the script's font 14 at 34x7 the ceiling was 114/126 = 0.905, row
    /// 117 covered NOTHING at all, and row 119 clipped about 47 px² of the University's roof.
    /// In font 8 at 56x12 (<see cref="StoreLayout.CashFont"/>, a user-requested legibility
    /// change) the figure reaches 22 columns further left and five rows further down, so at
    /// 11/12 it clips about 284 px² of that roof and row 117 no longer clears it either — it
    /// clips about 198. There is now NO `CashTop` at which the withdrawn scale covers no
    /// building, which is a fact about the withdrawn scale and not about what ships.
    /// </para>
    /// </summary>
    public const double LargestWithNoCoverage = 114.0 / 131.0;

    /// <summary>
    /// <c>JONES_PANEL_SCALE</c>, which overrides <see cref="Requested"/> without a rebuild
    /// so the two can be compared side by side. Anything outside (0, 1] is ignored:
    /// a panel larger than the original's is not a thing this switch offers, and a zero or
    /// negative one would draw nothing at all.
    /// </summary>
    private static double Wanted
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("JONES_PANEL_SCALE");
            return double.TryParse(env, NumberStyles.Float, CultureInfo.InvariantCulture, out var e)
                   && e > 0 && e <= 1
                ? e
                : Requested;
        }
    }

    /// <summary>
    /// The live switch, as the player sees it: true draws the fitted panel, false Sierra's.
    /// Set from the chrome switch beside the sound switches
    /// (<c>MainViewModel.SmallLocationView</c>), which rebuilds the screen so the two can be
    /// compared back to back on one shop.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>The scale actually in force, or 1 when the switch is off.</summary>
    public static double Factor => Enabled ? Wanted : 1.0;

    /// <summary>
    /// The transform the location panel is drawn through right now. Exactly
    /// <see cref="PanelTransform.None"/> when the scale is 1, so the original is restored
    /// rather than approximated.
    /// </summary>
    public static PanelTransform Of()
    {
        var s = Factor;
        if (s >= 1.0) return PanelTransform.None;

        // The origin moves one column left, to the free rectangle's own top-left corner.
        // At 0.75 the group is exactly 183 wide, which is exactly the free rectangle's
        // width, so there is nowhere else for it to go: anchored at 69 its right edge would
        // land on column 251, which is Monolith's first column.
        return new PanelTransform(s,
                                  FreeLeft - DialogLeft * s,
                                  FreeTop - DialogTop * s);
    }

    // ------------------------------------------------------------------
    // The ambient transform
    // ------------------------------------------------------------------

    /// <summary>
    /// The transform in force while the location panel's view models are being BUILT.
    /// Every positioned view model reads it in its constructor and carries it, so a model
    /// is never drawn under a transform other than the one it was built under — which a
    /// property set after <c>Add</c> could not promise, because the view may have realised
    /// the container by then.
    ///
    /// <para>
    /// A static, like everything else in <see cref="RenderScale"/>: one UI thread, one
    /// screen being built at a time. <see cref="Open"/> always restores what it found, so a
    /// throw inside the panel builder cannot leave the board being drawn through it.
    /// </para>
    /// </summary>
    public static PanelTransform Current { get; private set; } = PanelTransform.None;

    /// <summary>
    /// Opens a scope in which everything built belongs to the location panel. Dispose —
    /// i.e. leave the <c>using</c> — puts back whatever was in force before.
    /// </summary>
    public static Scope Open() => new(Of());

    public readonly struct Scope : IDisposable
    {
        private readonly PanelTransform _previous;

        internal Scope(PanelTransform t)
        {
            _previous = Current;
            Current = t;
        }

        public void Dispose() => Current = _previous;
    }
}

/// <summary>
/// Maps one coordinate or size from the original's 320x200 space into the place the
/// location panel is actually drawn. <see cref="None"/> is the identity and is what every
/// view model outside the panel carries.
/// </summary>
public readonly record struct PanelTransform(double Scale, double Dx, double Dy)
{
    public static readonly PanelTransform None = new(1.0, 0.0, 0.0);

    public double MapX(double x) => x * Scale + Dx;
    public double MapY(double y) => y * Scale + Dy;

    /// <summary>A width, a height or an offset — anything that is a length, not a place.</summary>
    public double Size(double v) => v * Scale;
}
