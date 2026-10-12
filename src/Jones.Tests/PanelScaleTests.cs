using Jones.App;
using Jones.Core.Model;

namespace Jones.Tests;

/// <summary>
/// THE IN-LOCATION PANEL SIZE, checked against the board it has to fit inside.
///
/// <para>
/// <b><see cref="PanelScale.Requested"/> IS 1 AND THE PORT DRAWS SIERRA'S OWN INTERIOR.</b>
/// A smaller panel was tried at 11/12 and withdrawn: the margin it opens is `pic 11`'s flat
/// #F8F8F0 hole, not town, so it bought two cream bars and nothing else. THE ARITHMETIC IS
/// KEPT ANYWAY, and that is the point of this file — the zero-coverage ceiling, the twelfths
/// sharpness argument and what 11/12 would cost are all still pinned, because the question
/// re-opens the moment the board's centre is filled and whoever re-opens it should find
/// measurements rather than a story.
/// </para>
///
/// <para>
/// Every claim here is arithmetic over <see cref="Board.All"/> and the coordinates in
/// <see cref="StoreLayout"/> rather than anything eyeballed on a screenshot, so it fails the
/// day somebody edits either table — which is exactly when they need to be told.
/// </para>
/// </summary>
public class PanelScaleTests
{
    /// <summary>
    /// <c>gCalc</c>'s cel — view 0 loop 4 cel 0, measured off <c>assets/png/view_0_l4_c0.png</c>.
    /// It is the part of the interior that hangs outside the dialog's own 184x119 box.
    /// </summary>
    private const int CalcWidth = 61;
    private const int CalcHeight = 34;

    /// <summary>The interior group, as drawn, at a given scale: left, top, width, height.</summary>
    private static (double L, double T, double W, double H) Group(double scale)
    {
        var t = new PanelTransform(scale,
                                   PanelScale.FreeLeft - PanelScale.DialogLeft * scale,
                                   PanelScale.FreeTop - PanelScale.DialogTop * scale);
        return (t.MapX(PanelScale.DialogLeft),
                t.MapY(PanelScale.DialogTop),
                t.Size(PanelScale.GroupWidth),
                t.Size(PanelScale.GroupHeight));
    }

    private static bool CoversAnyBuilding((double L, double T, double W, double H) g)
    {
        foreach (var place in Board.All)
        {
            // Rectangles in PIXEL COLUMNS AND ROWS: a Place owns columns Left..Left+Width-1.
            // The group's right edge is exclusive, so `L + W` is the first column it does not
            // paint, which is why these are strict comparisons on one side only.
            var overlapX = g.L < place.Left + place.Width && place.Left < g.L + g.W;
            var overlapY = g.T < place.Top + place.Height && place.Top < g.T + g.H;
            if (overlapX && overlapY) return true;
        }

        return false;
    }

    /// <summary>
    /// The group's extent is not a guess: it is the dialog's origin to the far corner of the
    /// calculator, which every location script draws on entry at the absolute (252,160).
    /// </summary>
    [Fact]
    public void GroupExtentIsTheDialogPlusTheCashFigure()
    {
        Assert.Equal(184, PanelScale.GroupWidth);
        Assert.Equal(StoreLayout.CashLeft + StoreLayout.CashWidth, PanelScale.GroupWidth);
        Assert.Equal(StoreLayout.CashTop + StoreLayout.CashHeight, PanelScale.GroupHeight);
    }

    /// <summary>
    /// AND THE CALCULATOR BODY IS WHAT USED TO MAKE IT 244x150. Kept as an assertion,
    /// because the whole justification for removing it is the difference between the two.
    /// </summary>
    [Fact]
    public void TheCalculatorBodyWasWhatPushedTheGroupOntoTheTown()
    {
        Assert.Equal(244, StoreLayout.CalcLeft + CalcWidth - PanelScale.DialogLeft);
        Assert.Equal(150, StoreLayout.CalcTop + CalcHeight - PanelScale.DialogTop);
        Assert.True(PanelScale.GroupWidth < 244 && PanelScale.GroupHeight < 150);
    }

    /// <summary>
    /// The free rectangle really is free: no Place touches columns 68..250 of rows 44..157.
    /// </summary>
    [Fact]
    public void TheFreeRectangleTouchesNoBuilding()
    {
        Assert.False(CoversAnyBuilding((PanelScale.FreeLeft, PanelScale.FreeTop,
                                        PanelScale.FreeWidth, PanelScale.FreeHeight)));
    }

    /// <summary>
    /// And it is the LARGEST such rectangle at that origin: one column wider or one row
    /// taller and it is on top of Monolith or the Employment Office.
    /// </summary>
    [Fact]
    public void TheFreeRectangleIsTheLargestOne()
    {
        Assert.True(CoversAnyBuilding((PanelScale.FreeLeft, PanelScale.FreeTop,
                                       PanelScale.FreeWidth + 1, PanelScale.FreeHeight)));
        Assert.True(CoversAnyBuilding((PanelScale.FreeLeft, PanelScale.FreeTop,
                                       PanelScale.FreeWidth, PanelScale.FreeHeight + 1)));
        Assert.True(CoversAnyBuilding((PanelScale.FreeLeft - 1, PanelScale.FreeTop,
                                       PanelScale.FreeWidth, PanelScale.FreeHeight)));
    }

    /// <summary>
    /// 0.75 covers no building, and is the LARGEST scale that does: a whole game pixel more
    /// width puts the group on Monolith's first column. It is not the default — see the next
    /// three tests for why — but the arithmetic is the floor of the trade and is pinned here.
    /// </summary>
    [Fact]
    public void TheNoCoverageScaleCoversNoBuildingAndIsTheLargestThatDoes()
    {
        var g = Group(PanelScale.LargestWithNoCoverage);
        Assert.False(CoversAnyBuilding(g));
        Assert.True(CoversAnyBuilding((g.L, g.T, g.W, g.H + 1)));

        // HEIGHT binds now that the calculator body is gone: 114/124 against 183/184.
        Assert.Equal(PanelScale.FreeHeight / (double)PanelScale.GroupHeight,
                     PanelScale.LargestWithNoCoverage, 10);
        Assert.True(PanelScale.FreeHeight / (double)PanelScale.GroupHeight
                    < PanelScale.FreeWidth / (double)PanelScale.GroupWidth);
    }

    /// <summary>
    /// WHY 0.75 IS NOT THE DEFAULT. `pic 11` has no town under the dialog — its middle is a
    /// flat #F8F8F0 hole — and the backdrop is sized to fill that hole, so anything smaller
    /// leaves blank board showing rather than revealing buildings. It takes 0.9891 of the
    /// width to cover it, which is almost exactly Sierra's own size.
    /// </summary>
    [Fact]
    public void TheDialogIsSizedToTheHoleInTheBoardPicture()
    {
        var needWidth = (PanelScale.HoleLeft + PanelScale.HoleWidth - PanelScale.FreeLeft)
                        / 183.0;   // the backdrop cel is 183x112
        var needHeight = (PanelScale.HoleTop + PanelScale.HoleHeight - PanelScale.FreeTop)
                         / 112.0;

        Assert.InRange(needWidth, 0.98, 0.995);
        Assert.InRange(needHeight, 0.97, 0.99);

        // AND THIS IS WHY THE DEFAULT IS 1. Anything below `needWidth` leaves part of that
        // hole showing, and what shows is flat cream - not town. The scale that was tried
        // and withdrawn is below it; full size is not.
        Assert.True(PanelScale.TriedElevenTwelfths < needWidth);
        Assert.False(PanelScale.Requested < needWidth);
    }

    /// <summary>
    /// THE DEFAULT IS SIERRA'S OWN INTERIOR. At 1 the transform is exactly the identity, so
    /// the panel work costs the shipped game nothing at all: every coordinate is the number
    /// the script gives, and the board elements the full-size dialog covers are drawn, since
    /// their suppression is gated on the panel being scaled.
    /// </summary>
    [Fact]
    public void TheDefaultIsFullSizeAndTheTransformIsTheIdentity()
    {
        Assert.Equal(1.0, PanelScale.Requested);

        var was = PanelScale.Enabled;
        try
        {
            PanelScale.Enabled = true;
            Assert.Equal(PanelTransform.None, PanelScale.Of());
            Assert.Equal(1.0, PanelScale.Factor);

            // And the chrome switch hides itself, because it would have nothing to switch.
            Assert.False(PanelScale.Scalable);
        }
        finally
        {
            PanelScale.Enabled = was;
        }
    }

    /// <summary>
    /// WHAT 11/12 WOULD COVER, kept because the question is still open and this is the number
    /// it will be re-decided against: the DIALOG covers no building at all there.
    /// </summary>
    [Fact]
    public void AtElevenTwelfthsTheDialogItselfCoversNoBuilding()
    {
        var s = PanelScale.TriedElevenTwelfths;
        var t = new PanelTransform(s,
                                   PanelScale.FreeLeft - PanelScale.DialogLeft * s,
                                   PanelScale.FreeTop - PanelScale.DialogTop * s);

        // The Dialog's own declared box - nsRight 184, nsBottom 119 - and nothing else.
        var dialog = (t.MapX(PanelScale.DialogLeft), t.MapY(PanelScale.DialogTop),
                      t.Size(184), t.Size(119));

        Assert.False(CoversAnyBuilding(dialog));
    }

    /// <summary>
    /// WHAT THE CASH FIGURE COSTS AT 11/12, to the pixel, so that the two rows spent on its
    /// gap from DONE stay a decision somebody took rather than a drift nobody noticed. It
    /// clips the UNIVERSITY'S ROOF and nothing else.
    /// </summary>
    [Fact]
    public void AtElevenTwelfthsOnlyTheCashFigureTouchesABuilding()
    {
        var s = PanelScale.TriedElevenTwelfths;
        var t = new PanelTransform(s,
                                   PanelScale.FreeLeft - PanelScale.DialogLeft * s,
                                   PanelScale.FreeTop - PanelScale.DialogTop * s);

        var cash = (X: t.MapX(PanelScale.DialogLeft + StoreLayout.CashLeft),
                    Y: t.MapY(PanelScale.DialogTop + StoreLayout.CashTop),
                    W: t.Size(StoreLayout.CashWidth), H: t.Size(StoreLayout.CashHeight));

        // The Dialog's own box - everything the player presses or reads - is still clear.
        Assert.False(CoversAnyBuilding((t.MapX(PanelScale.DialogLeft),
                                        t.MapY(PanelScale.DialogTop),
                                        t.Size(184), t.Size(119))));

        // The figure is not, and this is exactly which building and exactly how much.
        var hit = new List<(string Name, double Area)>();
        foreach (var place in Board.All)
        {
            var w = Math.Min(cash.X + cash.W, place.Left + place.Width) - Math.Max(cash.X, place.Left);
            var h = Math.Min(cash.Y + cash.H, place.Top + place.Height) - Math.Max(cash.Y, place.Top);
            if (w > 0 && h > 0) hit.Add((place.Name, w * h));
        }

        Assert.Single(hit);
        Assert.Equal("University", hit[0].Name);

        // About 284 px^2 of its roof. It was about 47 while the figure was the script's
        // font 14 at 34x7; font 8 is 56x12, so it reaches 22 columns further left and five
        // rows further down. See StoreLayout.CashFont for why the face changed.
        Assert.InRange(hit[0].Area, 275, 295);

        // Still clear of Socket City, which is what removing the calculator bought.
        Assert.True(cash.X + cash.W < 251);
    }

    /// <summary>
    /// THE GAP FROM DONE NO LONGER BUYS THE LAST OF THE COVERAGE, AND THAT CHANGED WITH THE
    /// FACE. While the figure was the script's font 14 at 34x7, `CashTop` 117 — hard against
    /// the bottom of `exitButton` — covered nothing at all, and the two rows of gap cost about
    /// 47 px² of the University's roof. In font 8 at 56x12 the figure is wide and deep enough
    /// that 117 clips the roof too, so there is no row at which the withdrawn scale is free.
    /// The test is kept and inverted rather than deleted: the fact it pins is now that the
    /// choice has no cost-free option, which is exactly what a later reader needs to know.
    /// </summary>
    [Fact]
    public void NoCashRowIsFreeOfTheTownAtTheWithdrawnScale()
    {
        var s = PanelScale.TriedElevenTwelfths;
        var t = new PanelTransform(s,
                                   PanelScale.FreeLeft - PanelScale.DialogLeft * s,
                                   PanelScale.FreeTop - PanelScale.DialogTop * s);

        (double, double, double, double) At(int top) =>
            (t.MapX(PanelScale.DialogLeft + StoreLayout.CashLeft),
             t.MapY(PanelScale.DialogTop + top),
             t.Size(StoreLayout.CashWidth), t.Size(StoreLayout.CashHeight));

        // 117 used to be free with the smaller face. It is not any more.
        Assert.True(CoversAnyBuilding(At(117)));
        Assert.True(CoversAnyBuilding(At(StoreLayout.CashTop)));

        // Nor is any row from the bottom of DONE down to the figure's own.
        for (var top = StoreLayout.ExitTop + 9; top <= StoreLayout.CashTop; top++)
            Assert.True(CoversAnyBuilding(At(top)), $"row {top} was expected to cover a building");

        // And the chosen row is still the first row clear of the button plus two.
        Assert.Equal(StoreLayout.ExitTop + 9 + 2, StoreLayout.CashTop);
    }

    /// <summary>
    /// ELEVENTH TWELFTHS, for the sharpness reason: this build draws at
    /// <c>RenderScale.Requested</c> 12, and 11/12 x 12 is a whole 11 device pixels per game
    /// pixel, so font 10's one-pixel stems are nearest-neighboured rather than resampled.
    /// </summary>
    [Fact]
    public void TheWithdrawnScaleIsWholeAtTheArtFactorThisBuildRuns()
    {
        // 12 is `RenderScale.Requested`, quoted rather than referenced because RenderScale
        // pulls Avalonia in and this project deliberately does not.
        const int artFactor = 12;
        Assert.Equal(11.0, PanelScale.TriedElevenTwelfths * artFactor, 10);

        // And the default is trivially whole at every factor, being 1.
        foreach (var f in new[] { 4, 8, 12 })
            Assert.Equal(f, PanelScale.Requested * f, 10);
    }

    /// <summary>
    /// Sierra's own interior does NOT fit — which is the whole reason this switch exists, and
    /// a sanity check that the overlap test is not simply always false.
    /// </summary>
    [Fact]
    public void TheOriginalSizeCoversABuilding()
    {
        Assert.True(CoversAnyBuilding((PanelScale.DialogLeft, PanelScale.DialogTop,
                                       PanelScale.GroupWidth, PanelScale.GroupHeight)));
    }

    /// <summary>
    /// THE SHARPNESS ARITHMETIC, pinned so the next person to propose a round 0.9 or an
    /// 0.875 can see what it costs. A scale is whole at EVERY art factor (4, 8 and 12) only
    /// when 4s is an integer, so between 0.75 and 1 there is nothing — which is exactly why
    /// the default is a twelfth, whole at the 12 this build runs and nowhere else.
    /// </summary>
    [Fact]
    public void NothingBetweenThreeQuartersAndFullSizeIsWholeAtEveryArtFactor()
    {
        foreach (var candidate in new[] { 10 / 12.0, 0.85, 0.875, 0.90, 11 / 12.0 })
        {
            var wholeEverywhere = new[] { 4, 8, 12 }
                .All(f => Math.Abs(candidate * f - Math.Round(candidate * f)) < 1e-9);
            Assert.False(wholeEverywhere, $"{candidate} unexpectedly whole at 4, 8 and 12");
        }
    }

    /// <summary>
    /// SWITCHED OFF IS THE ORIGINAL, exactly — the identity transform, not an approximation
    /// of it, so every coordinate is the number the script gives.
    /// </summary>
    [Fact]
    public void SwitchingItOffRestoresTheOriginalExactly()
    {
        var was = PanelScale.Enabled;
        try
        {
            PanelScale.Enabled = false;
            var t = PanelScale.Of();

            Assert.Equal(PanelTransform.None, t);
            Assert.Equal(PanelScale.DialogLeft, t.MapX(PanelScale.DialogLeft));
            Assert.Equal(PanelScale.DialogTop, t.MapY(PanelScale.DialogTop));
            Assert.Equal(184, t.Size(184));
        }
        finally
        {
            PanelScale.Enabled = was;
        }
    }

    /// <summary>
    /// WHY THE BOARD'S HOTSPOTS MUST BE DEAD WHILE AN INTERIOR IS OPEN, and it is not a
    /// matter of taste: AT FULL SIZE THEY OVERLAP THE BUTTON ROW. `exitButton` is at the
    /// absolute (212,152) and 32x9; the University's rectangle starts at row 158 and runs
    /// from column 190, so it covers DONE's bottom three rows. `relaxButton` at (78,152) is
    /// under the Employment Office's in exactly the same way.
    ///
    /// <para>
    /// The hotspot layer is declared AFTER the sprite layer so that it sits over the board
    /// art, which means those three rows answered to the hotspot and walked the player to
    /// Hi-Tech U instead of closing the room. `Dialog::handleEvent` (`Interface.sc:1008-1125`)
    /// is given the event before room1's cast ever sees it, so the fix is the original's own
    /// behaviour; this test is here so that nobody puts the hotspots back unconditionally
    /// without discovering why they were gated.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(143, "University")]        // exitButton, nsLeft 143
    [InlineData(9, "Employment Office")]   // relaxButton, nsLeft 9
    public void AtFullSizeTheBoardsHotspotsReachIntoTheButtonRow(int nsLeft, string place)
    {
        var x = PanelScale.DialogLeft + nsLeft;
        var y = PanelScale.DialogTop + StoreLayout.ExitTop;   // the whole row is nsTop 108
        const int w = 32, h = 9;

        var p = Board.All.Single(b => b.Name == place);
        var overlapX = x < p.Left + p.Width && p.Left < x + w;
        var overlapY = y < p.Top + p.Height && p.Top < y + h;

        Assert.True(overlapX && overlapY,
                    $"{place} no longer reaches the button at nsLeft {nsLeft} - if the board " +
                    "or the buttons moved, re-check whether the hotspot gate is still needed");
    }

    /// <summary>
    /// THE TRANSFORM ITSELF, which outlives whatever the default happens to be: under a
    /// scaled panel the dialog's origin lands on the free rectangle's corner and everything
    /// positioned from that origin — the DONE button at (143,108), the cash figure at
    /// (150,119) — comes with it. Tested at <see cref="PanelScale.TriedElevenTwelfths"/>,
    /// because the shipping default is 1 and the identity would prove nothing.
    /// </summary>
    [Fact]
    public void EverythingDialogRelativeMovesWithTheOrigin()
    {
        var s = PanelScale.TriedElevenTwelfths;
        var t = new PanelTransform(s,
                                   PanelScale.FreeLeft - PanelScale.DialogLeft * s,
                                   PanelScale.FreeTop - PanelScale.DialogTop * s);

        Assert.Equal(PanelScale.FreeLeft, t.MapX(PanelScale.DialogLeft), 10);
        Assert.Equal(PanelScale.FreeTop, t.MapY(PanelScale.DialogTop), 10);

        // The exit button, at the dialog's own (143,108).
        Assert.Equal(PanelScale.FreeLeft + StoreLayout.ExitLeft * s,
                     t.MapX(PanelScale.DialogLeft + StoreLayout.ExitLeft), 10);
        Assert.Equal(PanelScale.FreeTop + StoreLayout.ExitTop * s,
                     t.MapY(PanelScale.DialogTop + StoreLayout.ExitTop), 10);

        // And the cash figure, which replaced the calculator and is dialog-relative like
        // everything else, so it needs no special case at all.
        Assert.Equal(PanelScale.FreeLeft + StoreLayout.CashLeft * s,
                     t.MapX(PanelScale.DialogLeft + StoreLayout.CashLeft), 10);
    }
}
