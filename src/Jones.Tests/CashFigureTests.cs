using Jones.App;
using Jones.Core.Model;

namespace Jones.Tests;

/// <summary>
/// WHERE THE CASH FIGURE SITS, checked against every control in all thirteen interiors.
///
/// <para>
/// `gCalc`'s 61x34 body is no longer drawn (<see cref="PanelScale.CalculatorBody"/>) and the
/// figure alone sits at dialog-relative (128,119), 56x12 in font 8. The Dialog is 184x119 and
/// Sierra used all of it, so the one claim that matters is that this slot collides with
/// nothing a player presses or reads, in ANY of the thirteen layouts — including the ones that only appear
/// sometimes: a workplace's WORK button, an apartment's RELAX, Hi-Tech U's ENROLL, the Pawn
/// Shoppe's three modes and its lists, and the Employment Office's nine job lists.
/// </para>
///
/// <para>
/// The rectangles come from <see cref="StoreLayout"/> wherever it has them, so a change to
/// a shop's coordinates is caught here rather than on a screenshot.
/// </para>
/// </summary>
public class CashFigureTests
{
    private readonly record struct Rect(string What, double X, double Y, double W, double H);

    /// <summary>view 250's buttons: 32x9 for all but ENROLL (loop 10, 40 wide).</summary>
    private const int ButtonW = 32, ButtonH = 9;
    private const int EnrollW = 40;

    /// <summary>A WButton's drawn height: font 10's 6 rows plus one for the shadow.</summary>
    private const int LineH = 7;

    /// <summary>
    /// Every CONTROL and every line of information in one location, dialog-relative. The
    /// shopkeeper's portrait, the items panel and the furniture are deliberately NOT here —
    /// they are decoration, and the figure is allowed to sit over them if it must.
    /// </summary>
    private static List<Rect> ControlsIn(LocationId id)
    {
        var r = new List<Rect>
        {
            // exitButton, in all thirteen (`pawnShop.sc:613` only changes its loop).
            new("DONE", StoreLayout.ExitLeft, StoreLayout.ExitTop, ButtonW, ButtonH),
        };

        // workButton, wherever the player can be employed. The Bank's sits at 77.
        if (Board.Get(id).Workplace is not null)
            r.Add(new("WORK", id == LocationId.Bank ? 77 : StoreLayout.WorkLeft,
                      StoreLayout.WorkTop, ButtonW, ButtonH));

        // relaxButton, in both apartments.
        if (id is LocationId.LowCostHousing or LocationId.SecurityApartments)
            r.Add(new("RELAX", StoreLayout.RelaxLeft, StoreLayout.RelaxTop, ButtonW, ButtonH));

        // Every shop's priced lines, at the script's own nsLeft/nsTop.
        if (StoreLayout.For(id) is { } slots)
            foreach (var s in slots)
                r.Add(new($"line {s.Label}", s.Left, s.Top, 110, LineH));

        // Z-Mart is the one that lays out at runtime (`discount.sc:186-193`): six of
        // eighteen, nsTop (n+1)*13+20, nsLeft 11 for the first two and 78 after.
        if (id == LocationId.ZMart)
            for (var n = 0; n < 6; n++)
                r.Add(new($"z-mart line {n}", n < 2 ? 11 : 78, (n + 1) * 13 + 20, 110, LineH));

        if (id == LocationId.HiTechU)
        {
            r.Add(new("enrollment fee", 85, 32, 95, LineH));
            r.Add(new("ENROLL", StoreLayout.EnrollLeft, StoreLayout.EnrollTop, EnrollW, ButtonH));
            for (var i = 0; i < 4; i++)
            {
                var (left, top) = StoreLayout.CourseSlot(i);
                r.Add(new($"course {i}", left, top, 86, 9));          // view 707 loop 2
                r.Add(new($"lessons left {i}", 153, top + 1, 12, LineH));
            }
        }

        if (id == LocationId.PawnShop)
        {
            for (var i = 0; i < 3; i++)
                r.Add(new($"pawn mode {i}", 76, 40 + 20 * i, 99, 11));   // view 712 loop 1
            for (var n = 0; n < 8; n++)
                r.Add(new($"pawn row {n}", 71, 40 + 10 * n, 107, LineH));
            r.Add(new("pawn", 77, 108, ButtonW, ButtonH));
            r.Add(new("done pawning", 110, 108, ButtonW, ButtonH));
        }

        if (id == LocationId.EmploymentOffice)
        {
            foreach (var (_, _, left, top, _) in StoreLayout.Employment)
                r.Add(new("employer", left, top, 104 - left, LineH));

            // The nine job lists that replace that menu, each at its own header and pitch.
            foreach (var w in Enum.GetValues<Workplace>())
            {
                var (_, hLeft, hTop, firstTop, spacing) = StoreLayout.JobList(w);
                r.Add(new($"{w} header", hLeft, hTop, 160 - hLeft, LineH));
                var rows = Jobs.At(w).Count();
                for (var i = 0; i < rows; i++)
                    r.Add(new($"{w} job {i}", StoreLayout.JobLeft, firstTop + spacing * i,
                              130, LineH));
            }
        }

        return r;
    }

    private static IEnumerable<LocationId> Everywhere() =>
        Enum.GetValues<LocationId>();

    /// <summary>
    /// THE CLAIM: the figure's slot collides with no control anywhere. If this fails, the
    /// message names the location and the control, which is the whole point of building the
    /// rectangles from `StoreLayout` rather than from a screenshot.
    /// </summary>
    [Fact]
    public void TheCashSlotClearsEveryControlInEveryLocation()
    {
        var x = StoreLayout.CashLeft;
        var y = StoreLayout.CashTop;
        var w = StoreLayout.CashWidth;
        var h = StoreLayout.CashHeight;

        foreach (var id in Everywhere())
            foreach (var c in ControlsIn(id))
            {
                var hit = x < c.X + c.W && c.X < x + w && y < c.Y + c.H && c.Y < y + h;
                Assert.False(hit, $"the cash figure collides with {c.What} at {id}");
            }
    }

    /// <summary>
    /// AND IT IS THE BOTTOM-RIGHT CORNER: its widest form ends exactly on the Dialog's own
    /// right edge, and it sits two rows below DONE — close enough to read as that corner's
    /// figure, far enough not to read as that button's caption.
    /// </summary>
    [Fact]
    public void TheCashSlotIsTheDialogsBottomRightCorner()
    {
        Assert.Equal(184, StoreLayout.CashLeft + StoreLayout.CashWidth);
        Assert.Equal(StoreLayout.ExitTop + ButtonH + 2, StoreLayout.CashTop);
    }

    /// <summary>
    /// THE TOP BAND IS CLEAR OF EVERY CONTROL, AND THAT IS NOT WHY THE FIGURE IS NOT UP
    /// THERE. Zain asked for the top right; this test is the half of the answer that lives in
    /// the layout, so the next person does not re-derive it. Rows 0..18 of the FULL 184 width
    /// collide with nothing clickable in any of the thirteen — the earliest control anywhere
    /// is the Employment Office's Factory job-list header at nsTop 19 — so
    /// <see cref="NoSlotInsideTheDialogsOwnBoxIsClearEverywhere"/>, which starts its search at
    /// row 24, never spoke about the top band at all.
    ///
    /// <para>
    /// What blocks it is ART AND LETTERING, which no layout constant can express: the
    /// shopkeeper's 68x55 portrait owns columns 115..182 of rows 0..54 in five interiors
    /// (<see cref="StoreLayout.TalkerAt"/>'s default), and the right-hand end of the
    /// location's own painted name runs into the frame in the rest. Measured against the
    /// composed art, the flat run at the top-right corner is at most 2px wide in all thirteen
    /// and zero in four. That measurement is in `StoreLayout.CashLeft`'s remarks; this test
    /// pins only the part that can go stale when a script coordinate changes.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTopBandOfTheDialogIsFreeOfControlsEverywhere()
    {
        var all = Everywhere().SelectMany(ControlsIn).ToList();

        // The first control in any layout, so the claim moves if a coordinate does.
        Assert.Equal(19, all.Min(c => c.Y));

        for (var y = 0; y + StoreLayout.CashHeight <= 19; y++)
            for (var x = 0; x + StoreLayout.CashWidth <= 184; x++)
                Assert.DoesNotContain(all, c =>
                    x < c.X + c.W && c.X < x + StoreLayout.CashWidth &&
                    y < c.Y + c.H && c.Y < y + StoreLayout.CashHeight);
    }

    /// <summary>
    /// AND THE FIGURE IS DRAWN IN FONT 8, NOT THE SCRIPT'S FONT 14 — a user-requested
    /// legibility deviation, pinned here so it cannot be reverted by accident along with a
    /// position change. Font 8 is monospaced, which is why <see cref="StoreLayout.CashWidth"/>
    /// is a single number rather than the widest of six: `"%6s "` is seven cells of 8px at
    /// every digit count the original's 16-bit cash can reach.
    /// </summary>
    [Fact]
    public void TheFigureUsesTheLargerFaceAndIsSizedForIt()
    {
        Assert.Equal(8, StoreLayout.CashFont);
        Assert.NotEqual(StoreLayout.CalcFont, StoreLayout.CashFont);
        Assert.Equal(7 * 8, StoreLayout.CashWidth);   // "%6s " = six digits plus one space
        Assert.Equal(12, StoreLayout.CashHeight);     // font 8's own row count
    }

    /// <summary>
    /// THERE IS NOWHERE ELSE BELOW THE TOP BAND, and this is the assertion that says so
    /// rather than the comment. Nothing in the Dialog below row 24 is clear of every control
    /// in every layout - Sierra used the whole box - so a slot that clears them all AND is at
    /// the bottom cannot be found inside it, which is why the figure sits at `nsBottom 119`
    /// itself. The top band is a different matter and has its own test above; it is clear of
    /// controls and full of art.
    /// </summary>
    [Fact]
    public void NoSlotInsideTheDialogsOwnBoxIsClearEverywhere()
    {
        var w = StoreLayout.CashWidth;
        var h = StoreLayout.CashHeight;
        var all = Everywhere().SelectMany(ControlsIn).ToList();

        var clearInsideTheBox = 0;
        for (var y = 24; y + h <= 119; y++)
            for (var x = 0; x + w <= 184; x++)
                if (!all.Any(c => x < c.X + c.W && c.X < x + w && y < c.Y + c.H && c.Y < y + h))
                    clearInsideTheBox++;

        Assert.Equal(0, clearInsideTheBox);
    }
}
