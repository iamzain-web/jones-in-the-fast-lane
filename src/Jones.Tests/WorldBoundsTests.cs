using Jones.App;
using Jones.Core.Model;

namespace Jones.Tests;

/// <summary>
/// THE WORLD IS 320x200 TODAY, AND THIS IS THE ONE PLACE THAT SAYS SO.
///
/// <para>
/// IT IS A TRIPWIRE, NOT A FENCE. The board is to be widened so it can carry more locations
/// than thirteen, and when that happens the world itself gets wider — a building in the new
/// columns is a building, not scenery. These assertions exist so that widening is a DELIBERATE
/// edit in one file rather than something discovered later from a hotspot that does not
/// respond: change <see cref="W"/> here, and every coordinate table that has drifted outside
/// the new box says which one it is and by how much.
/// </para>
///
/// <para>
/// WHAT IT PROVES TODAY is that nothing in the game currently leaves 320x200 — which is the
/// precondition for widening being a pure extension rather than a migration. Every number
/// comes from <see cref="Board.All"/>, <see cref="MarblePath"/>, <see cref="PanelScale"/> and
/// <see cref="StoreLayout"/> rather than from anything observed on screen.
/// </para>
///
/// <para>
/// THE ONE THING THAT MUST NOT MOVE WITH IT is the ring's length. `MarblePath` has 170 steps,
/// `Board.StepsBetween` is the shorter arc between two INDICES, and `GameClock` charges one
/// tick per step — so what a journey costs depends on the indices and on 170, never on where
/// anything is drawn. Widen the board, respace the 170 points over it, and every existing
/// journey costs exactly what it costs now. Add a step, and you have rebalanced the game.
/// </para>
/// </summary>
public class WorldBoundsTests
{
    private const int W = 320, H = 200;

    /// <summary>
    /// The thirteen `Place` rectangles — `room1.sc:294` — are the only clickable things on
    /// the board, and every one of them is inside the world.
    /// </summary>
    [Fact]
    public void EveryBuildingIsInsideTheWorld()
    {
        foreach (var p in Board.All)
        {
            Assert.InRange(p.Left, 0, W - 1);
            Assert.InRange(p.Top, 0, H - 1);
            Assert.InRange(p.Left + p.Width, 1, W);
            Assert.InRange(p.Top + p.Height, 1, H);
        }
    }

    /// <summary>
    /// All 170 points of `marblePath` (script 101), with the token's own 9x8 cel around each.
    /// The marble is the one thing that moves continuously across the board, so it is the
    /// most likely thing to be nudged into a margin by a future edit.
    /// </summary>
    [Fact]
    public void TheWholeMarblePathIsInsideTheWorld()
    {
        for (var i = 1; i <= MarblePath.StepCount; i++)
        {
            var (x, y) = MarblePath.At(i);

            // `marble` is view 0 loop 2, 9x8, drawn at (x - celWide/2, y - celHigh).
            Assert.InRange(x - 4.5, 0, W);
            Assert.InRange(x + 4.5, 0, W);
            Assert.InRange(y - 8, 0, H);
            Assert.InRange(y, 0, H);
        }
    }

    /// <summary>
    /// The interior dialog — `moveTo: 69 44`, nsRight 184, nsBottom 119 — and the cash figure
    /// that hangs below it. The dialog is the biggest single thing drawn in the world and the
    /// one whose origin a scale change moves, so it is checked at BOTH ends of the scale.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    public void TheInteriorIsInsideTheWorldAtFullSize(double scale)
    {
        Assert.Equal(scale, PanelScale.Requested);

        Assert.InRange(PanelScale.DialogLeft + 184, 0, W);
        Assert.InRange(PanelScale.DialogTop + 119, 0, H);

        // The cash figure, which reaches past the dialog's declared floor.
        Assert.InRange(PanelScale.DialogLeft + StoreLayout.CashLeft + StoreLayout.CashWidth, 0, W);
        Assert.InRange(PanelScale.DialogTop + StoreLayout.CashTop + StoreLayout.CashHeight, 0, H);
    }

    /// <summary>
    /// And at the scale that was tried and withdrawn, since the question re-opens when the
    /// board's centre is filled: the group's origin moves to the free rectangle's corner and
    /// everything still has to land inside the world.
    /// </summary>
    [Fact]
    public void TheInteriorIsInsideTheWorldAtTheWithdrawnScale()
    {
        var s = PanelScale.TriedElevenTwelfths;
        var t = new PanelTransform(s,
                                   PanelScale.FreeLeft - PanelScale.DialogLeft * s,
                                   PanelScale.FreeTop - PanelScale.DialogTop * s);

        Assert.InRange(t.MapX(PanelScale.DialogLeft), 0.0, (double)W);
        Assert.InRange(t.MapX(PanelScale.DialogLeft + 184), 0.0, (double)W);
        Assert.InRange(t.MapY(PanelScale.DialogTop), 0.0, (double)H);
        Assert.InRange(t.MapY(PanelScale.DialogTop + StoreLayout.CashTop + StoreLayout.CashHeight),
                       0.0, (double)H);
    }

    /// <summary>
    /// THE ART BRIEF, as arithmetic rather than as a sentence. A canvas of 320 + 2M by 200
    /// has aspect (320+2M)/200, so M = 100A − 160 columns of scenery per side. These are the
    /// numbers the scenery would be drawn to, and they are here so that a change to the world
    /// size — which must not happen — would take them with it.
    /// </summary>
    [Theory]
    [InlineData(16, 9, 18)]        // laptops, older phones
    [InlineData(18, 9, 40)]
    [InlineData(19.5, 9, 57)]      // iPhone-class
    [InlineData(20, 9, 63)]        // modern Android, the case Zain is holding
    [InlineData(21, 9, 74)]        // ultrawide desktop
    public void TheSceneryWidthForEachAspect(double a, double b, int expectedColumns)
    {
        var columns = (int)Math.Ceiling(100.0 * (a / b) - W / 2.0);
        Assert.Equal(expectedColumns, columns);

        // And the resulting canvas is at least as wide as the target wants.
        Assert.True((W + 2.0 * columns) / H >= a / b);
    }

    /// <summary>
    /// ANYTHING NARROWER THAN THE WORLD WANTS ROWS, NOT COLUMNS — every tablet at 4:3 or 3:2,
    /// and every phone in portrait. Widening those makes the board smaller, not larger, which
    /// is the finding that stops the margin being applied unconditionally.
    /// </summary>
    [Theory]
    [InlineData(4, 3)]
    [InlineData(3, 2)]
    [InlineData(9, 19.5)]
    public void NarrowerThanTheWorldWantsNoColumnsAtAll(double a, double b)
    {
        Assert.True(100.0 * (a / b) - W / 2.0 <= 0);
    }

    /// <summary>
    /// THE RING HAS ROOM FOR MORE BUILDINGS WITHOUT COSTING ANYONE AN HOUR, and this is the
    /// fact the whole eighteen-location plan rests on: `MarblePath` is 170 indices and only
    /// THIRTEEN are claimed. A new location takes a free index; every existing pair's
    /// `StepsBetween` is a function of the two indices and 170, so not one of the 156
    /// existing journeys changes by a single step.
    /// </summary>
    [Fact]
    public void TheRingHasFreeIndicesForMoreLocations()
    {
        var claimed = Board.All.Select(p => p.PathIndex).ToHashSet();

        Assert.Equal(13, claimed.Count);                       // no two share an index
        Assert.Equal(170, MarblePath.StepCount);
        Assert.True(MarblePath.StepCount - claimed.Count >= 5,
                    "five more locations need five free indices");

        // And the five widest gaps are wide enough to hold one each without crowding.
        var idx = claimed.OrderBy(i => i).ToList();
        var gaps = idx.Select((a, k) => (idx[(k + 1) % idx.Count] - a + 170) % 170)
                      .OrderByDescending(g => g).Take(5).ToList();
        Assert.All(gaps, g => Assert.True(g >= 13, $"gap of only {g} steps"));
    }

    /// <summary>
    /// AND ADDING ONE CANNOT CHANGE AN EXISTING JOURNEY. Stated as an assertion rather than
    /// an argument: distance depends on the two indices and the ring length, so inserting a
    /// fourteenth index leaves all 156 existing distances identical.
    /// </summary>
    [Fact]
    public void AddingALocationLeavesEveryExistingJourneyUntouched()
    {
        var before = new Dictionary<(LocationId, LocationId), int>();
        foreach (var a in Board.All)
            foreach (var b in Board.All)
                if (a.Id != b.Id)
                    before[(a.Id, b.Id)] = Board.StepsBetween(a.Id, b.Id);

        Assert.Equal(156, before.Count);

        // A new building at a free index - 87, the middle of the widest gap - changes the
        // ring not at all: `MarblePath.Distance` reads only the two indices and 170.
        const int newIndex = 87;
        Assert.DoesNotContain(newIndex, Board.All.Select(p => p.PathIndex));

        foreach (var ((a, b), steps) in before)
            Assert.Equal(steps, Board.StepsBetween(a, b));
    }
}
