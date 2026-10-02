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
/// (252,160) is (69+183, 44+116) — so it is treated here as one more dialog-relative part
/// at nsLeft 183, nsTop 116, which is what keeps it from being left behind.
/// </para>
///
/// <para>
/// THE GROUP, THEREFORE, IS 244x150 AT (69,44): columns 69..312, rows 44..193. The dialog
/// box is only 184x119, but the calculator's 61x34 cel hangs out to (313,194), and that
/// overhang is the part that sits on top of Socket City.
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
/// SO THE LARGEST THE GROUP CAN BE is 183/244 = <b>0.75</b> by width and 114/150 = 0.76 by
/// height; width binds, and three quarters is exact. At 0.75 the group is 183 x 112.5,
/// which placed at (68,44) covers columns 68..250 and rows 44..156 and touches no building
/// rectangle at all. That is <see cref="Requested"/>.
/// </para>
///
/// <para>
/// THREE QUARTERS IS ALSO THE ONLY FRACTION THAT COSTS NOTHING IN SHARPNESS, which is why
/// it is not rounded off to a friendlier-looking 0.7. <see cref="RenderScale.Factor"/> is 4,
/// 8 or 12, and 0.75 times every one of those is a whole number — 3, 6 and 9 — so every
/// coordinate and every bitmap in the panel still lands on an exact device pixel, and the
/// game's own 6-pixel font 10 is still drawn at a whole-number nearest-neighbour scale
/// rather than resampled into grey. Pick any other value and the interface text turns to
/// mush at the one size the player reads it at.
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
    /// The group's true extent from that origin: the 184x119 dialog box widened and
    /// deepened by the calculator cel's overhang to (313,194). Measured, not assumed —
    /// view 0 loop 4 cel 0 is 61x34 and sits at (252,160).
    /// </summary>
    public const int GroupWidth = 244;
    public const int GroupHeight = 150;

    /// <summary>
    /// The largest rectangle on the board that no <c>Place</c> rectangle touches: columns
    /// 68..250, rows 44..157. See the class remarks for which building bounds each edge.
    /// </summary>
    public const int FreeLeft = 68;
    public const int FreeTop = 44;
    public const int FreeWidth = 183;
    public const int FreeHeight = 114;

    /// <summary>
    /// THE SWITCH. 0.75 is the new default Zain asked for; 1 is Sierra's own full-size
    /// interior, and nothing else in the port changes when it is 1.
    /// </summary>
    public const double Requested = 0.75;

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
