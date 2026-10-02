using System;
using System.Collections.Generic;

namespace Jones.App;

/// <summary>
/// THE TAP TARGET, GROWN FROM THE DEVICE RATHER THAN FROM THE PLATFORM.
///
/// <para>
/// THE PROBLEM, MEASURED. Every button in a building's interior is view 250: 32x9 game
/// pixels (loop 7 is 34, loop 9 is 16, loop 10 is 40), and a shop line is about 72x6. THE
/// 9 AND THE 6 ARE THE WHOLE PROBLEM. Android's minimum touch target is 48 dp — 48/160
/// inch, about 7.6 mm, which is <see cref="MinimumUnits"/> = 28.8 Avalonia units, since an
/// Avalonia unit is 1/96 inch. Against that:
/// </para>
///
/// <list type="bullet">
/// <item>desktop, 1280x800 window, art 12x: 4 units per game pixel, so the button row is
///   36 units / 60 dp. CLEARS IT, with room to spare.</item>
/// <item>android portrait, 1080 wide, art 1x: 2.03 units per game pixel, so the row is
///   18.2 units / 30 dp. SHORT BY 1.6x.</item>
/// <item>android landscape, 1080 tall, art 1x: 3.24 units per game pixel, 29.2 units /
///   48.6 dp. Clears it by 1%.</item>
/// </list>
///
/// <para>
/// WHAT THIS CLASS DOES, AND WHAT IT DELIBERATELY DOES NOT. It grows the RECTANGLE THAT
/// RESPONDS, never the art. Nothing drawn moves by a pixel, so this is not a fidelity
/// deviation at all and needs no switch: the control is still exactly where Sierra put it
/// and still exactly the size Sierra drew it. The growth is bounded three ways — by the
/// device (no control grows past <see cref="MinimumUnits"/>, so a desktop gets no growth
/// whatsoever and behaves identically to before), by its neighbours (two controls split the
/// gap between them and can never overlap), and by the dialog's own box.
/// </para>
///
/// <para>
/// GROWING THE ART WAS CONSIDERED AND IS NOT POSSIBLE, which is worth recording so nobody
/// re-proposes it. Portrait needs 1.6x on the binding dimension; the button row sits at
/// `nsTop 108` and the Dialog is `nsBottom 119`, so 9 x 1.6 = 15 would put the row's base at
/// 123 — four pixels through the floor of a panel the scripts fix at 119. There is no
/// arrangement of a 1.6x row inside that box, and moving the row up collides with the price
/// lines. The rectangle is the only half that can be had.
/// </para>
/// </summary>
public static class TouchTargets
{
    /// <summary>
    /// 48 dp in Avalonia units. Android's dp is 1/160 inch and an Avalonia unit is 1/96, so
    /// 48 dp = 48 * 96 / 160 = 28.8 units = 0.3 inch = 7.62 mm.
    /// </summary>
    public const double MinimumUnits = 28.8;

    /// <summary>
    /// How many Avalonia units one GAME pixel occupies on this screen right now, measured by
    /// the view from the canvas's own transform (see <c>MainView.MeasureTouchScale</c>) —
    /// NOT assumed from the platform, which is the whole point. 0 until it has been measured,
    /// and 0 means no growth: an unmeasured device behaves exactly as the port always did.
    /// </summary>
    public static double UnitsPerGamePixel { get; set; }

    /// <summary>
    /// <see cref="MinimumUnits"/> expressed in game pixels on this screen, which is the size
    /// a control is grown TOWARDS. Zero when the scale has not been measured.
    /// </summary>
    public static double MinimumGamePixels =>
        UnitsPerGamePixel > 0 ? MinimumUnits / UnitsPerGamePixel : 0;

    /// <summary>
    /// The layout the controls being built right now belong to, or null outside a building's
    /// interior. Set by <see cref="Open"/> and read by each view model's constructor, for the
    /// same reason <see cref="PanelScale.Current"/> is: a model must carry the layout it was
    /// built into, and a property set after the collection's <c>Add</c> may be read too late.
    /// </summary>
    public static TouchLayout? Current { get; private set; }

    /// <summary>
    /// A layout for the interior as it is drawn RIGHT NOW. The panel scale has to be handed
    /// over because the entries are registered in the original's unscaled 320x200 space while
    /// the 48 dp rule is about what the player's finger meets: a 9-pixel button drawn at
    /// <see cref="PanelScale"/> 0.75 is 6.75 pixels of screen, and comparing the 9 would say
    /// it was fine when it is not.
    /// </summary>
    public static TouchLayout NewLayout() => new(PanelScale.Of().Scale);

    public static Scope Open(TouchLayout layout) => new(layout);

    public readonly struct Scope : IDisposable
    {
        private readonly TouchLayout? _previous;

        internal Scope(TouchLayout layout)
        {
            _previous = Current;
            Current = layout;
        }

        public void Dispose() => Current = _previous;
    }
}

/// <summary>How far one control's tap rectangle reaches beyond its art, in game pixels.</summary>
public readonly record struct HitGrowth(double Left, double Top, double Right, double Bottom)
{
    public static readonly HitGrowth None = new(0, 0, 0, 0);
}

/// <summary>
/// One screenful of tap rectangles. Controls register themselves as they are built; the
/// growth is worked out ONCE, lazily, the first time anything asks — which is during the
/// view's first layout pass, by which time every control on the screen has registered.
/// </summary>
public sealed class TouchLayout(double panelScale)
{
    private readonly List<(object Owner, double X, double Y, double W, double H)> _entries = [];
    private Dictionary<object, HitGrowth>? _grown;

    /// <summary>
    /// What <see cref="PanelScale"/> is drawing the interior at, so the threshold can be
    /// stated in the same unscaled game pixels the entries are registered in.
    /// </summary>
    private readonly double _panelScale = panelScale > 0 ? panelScale : 1;

    /// <summary>
    /// The box a control may grow inside: the Dialog's own `nsRight 184` / `nsBottom 119` at
    /// `moveTo: 69 44`. A tap outside the dialog belongs to the board, not to the shop.
    /// </summary>
    private const double BoundsLeft = PanelScale.DialogLeft;
    private const double BoundsTop = PanelScale.DialogTop;
    private const double BoundsRight = PanelScale.DialogLeft + 184;
    private const double BoundsBottom = PanelScale.DialogTop + 119;

    /// <summary>
    /// Registers a clickable control at its position in the original's 320x200 space. Called
    /// from the view model constructor; ignored once the growth has been worked out, which
    /// cannot happen before the screen is finished.
    /// </summary>
    public void Add(object owner, double x, double y, double w, double h)
    {
        if (_grown is not null || w <= 0 || h <= 0) return;
        _entries.Add((owner, x, y, w, h));
    }

    public HitGrowth For(object owner)
    {
        _grown ??= Compute();
        return _grown.TryGetValue(owner, out var g) ? g : HitGrowth.None;
    }

    private Dictionary<object, HitGrowth> Compute()
    {
        // 48 dp, in the UNSCALED game pixels the entries are registered in: the panel is drawn
        // at `_panelScale`, so a control needs `want` unscaled pixels to meet 48 dp on screen.
        var want = TouchTargets.MinimumGamePixels / _panelScale;
        var result = new Dictionary<object, HitGrowth>(_entries.Count);

        foreach (var e in _entries)
        {
            // HOW MUCH IS WANTED. Nothing, on any screen where the control already clears
            // 48 dp — which is every desktop — so the common case costs nothing and changes
            // nothing. Split evenly between the two sides, so the tap rectangle stays
            // centred on the art.
            var wantX = Math.Max(0, (want - e.W) / 2);
            var wantY = Math.Max(0, (want - e.H) / 2);

            if (wantX <= 0 && wantY <= 0) { result[e.Owner] = HitGrowth.None; continue; }

            // HOW MUCH IS AVAILABLE. The dialog's edge, and HALF the clear gap to every
            // neighbour that could be reached — half, so that two controls growing towards
            // each other meet exactly in the middle and neither can ever swallow the other's
            // taps. A control already overlapping a neighbour grows no further that way.
            var left = e.X - BoundsLeft;
            var top = e.Y - BoundsTop;
            var right = BoundsRight - (e.X + e.W);
            var bottom = BoundsBottom - (e.Y + e.H);

            foreach (var o in _entries)
            {
                if (ReferenceEquals(o.Owner, e.Owner)) continue;

                var overlapsRows = o.Y < e.Y + e.H && e.Y < o.Y + o.H;
                var overlapsCols = o.X < e.X + e.W && e.X < o.X + o.W;

                if (overlapsRows)
                {
                    if (o.X + o.W <= e.X) left = Math.Min(left, (e.X - (o.X + o.W)) / 2);
                    if (e.X + e.W <= o.X) right = Math.Min(right, (o.X - (e.X + e.W)) / 2);
                }

                if (overlapsCols)
                {
                    if (o.Y + o.H <= e.Y) top = Math.Min(top, (e.Y - (o.Y + o.H)) / 2);
                    if (e.Y + e.H <= o.Y) bottom = Math.Min(bottom, (o.Y - (e.Y + e.H)) / 2);
                }
            }

            result[e.Owner] = new HitGrowth(
                Math.Max(0, Math.Min(wantX, left)),
                Math.Max(0, Math.Min(wantY, top)),
                Math.Max(0, Math.Min(wantX, right)),
                Math.Max(0, Math.Min(wantY, bottom)));
        }

        Separate(result);
        return result;
    }

    /// <summary>
    /// THE BACKSTOP. The half-gap rule above is argued per axis — it only constrains a
    /// neighbour that overlaps in the other one — and a screen whose controls are laid out in
    /// two staggered columns can still produce a pair that meets diagonally. Where two grown
    /// rectangles still intersect, BOTH give up their growth along the axis they overlap least
    /// in, which cannot make anything worse than not growing at all.
    ///
    /// <para>
    /// Found by the hit-test probe with <c>JONES_TOUCH_UNITS</c> set to a phone's scale: two
    /// of Socket City's price lines overlapped and the later one took the earlier one's taps.
    /// </para>
    /// </summary>
    private void Separate(Dictionary<object, HitGrowth> grown)
    {
        (double L, double T, double R, double B) BoxOf(
            (object Owner, double X, double Y, double W, double H) e)
        {
            var g = grown[e.Owner];
            return (e.X - g.Left, e.Y - g.Top, e.X + e.W + g.Right, e.Y + e.H + g.Bottom);
        }

        for (var i = 0; i < _entries.Count; i++)
            for (var j = i + 1; j < _entries.Count; j++)
            {
                var a = BoxOf(_entries[i]);
                var b = BoxOf(_entries[j]);

                var overlapX = Math.Min(a.R, b.R) - Math.Max(a.L, b.L);
                var overlapY = Math.Min(a.B, b.B) - Math.Max(a.T, b.T);
                if (overlapX <= 0 || overlapY <= 0) continue;

                var ga = grown[_entries[i].Owner];
                var gb = grown[_entries[j].Owner];

                if (overlapY <= overlapX)
                {
                    grown[_entries[i].Owner] = ga with { Top = 0, Bottom = 0 };
                    grown[_entries[j].Owner] = gb with { Top = 0, Bottom = 0 };
                }
                else
                {
                    grown[_entries[i].Owner] = ga with { Left = 0, Right = 0 };
                    grown[_entries[j].Owner] = gb with { Left = 0, Right = 0 };
                }
            }
    }
}
