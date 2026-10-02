using Jones.App;
using Jones.Core.Model;

namespace Jones.Tests;

/// <summary>
/// THE IN-LOCATION PANEL SIZE, checked against the board it has to fit inside.
///
/// <para>
/// This is the guard on a DELIBERATE DEVIATION. The whole justification for
/// <see cref="PanelScale.Requested"/> being 0.75 is that it is the largest scale at which a
/// building's interior covers none of the thirteen <c>Place</c> rectangles in
/// <c>room1.sc:294</c>. That claim is arithmetic over <see cref="Board.All"/> and over the
/// calculator's position in <see cref="StoreLayout"/>, so it is checked here rather than
/// eyeballed on a screenshot — and it will fail the day someone edits either table or
/// nudges the scale, which is exactly when somebody needs to be told.
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
    public void GroupExtentIsTheDialogPlusTheCalculator()
    {
        Assert.Equal(PanelScale.DialogLeft + PanelScale.GroupWidth,
                     StoreLayout.CalcLeft + CalcWidth);
        Assert.Equal(PanelScale.DialogTop + PanelScale.GroupHeight,
                     StoreLayout.CalcTop + CalcHeight);
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
    public void SeventyFivePercentCoversNoBuildingAndIsTheLargestThatDoes()
    {
        var g = Group(PanelScale.LargestWithNoCoverage);
        Assert.False(CoversAnyBuilding(g));
        Assert.True(CoversAnyBuilding((g.L, g.T, g.W + 1, g.H)));

        // Stated as the ratio as well, so the reason survives a change to either number.
        Assert.Equal(PanelScale.FreeWidth / (double)PanelScale.GroupWidth,
                     PanelScale.LargestWithNoCoverage, 10);
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

        // So the chosen scale cannot cover it, and that is the accepted cost.
        Assert.True(PanelScale.Requested < needWidth);
    }

    /// <summary>
    /// THE CLAIM THE DEFAULT RESTS ON: at <see cref="PanelScale.Requested"/> the DIALOG
    /// covers no building at all. Everything that is covered is covered by the calculator,
    /// which is a separate decision about where `gCalc` lives.
    /// </summary>
    [Fact]
    public void AtTheRequestedScaleTheDialogItselfCoversNoBuilding()
    {
        var s = PanelScale.Requested;
        var t = new PanelTransform(s,
                                   PanelScale.FreeLeft - PanelScale.DialogLeft * s,
                                   PanelScale.FreeTop - PanelScale.DialogTop * s);

        // The Dialog's own declared box - nsRight 184, nsBottom 119 - and nothing else.
        var dialog = (t.MapX(PanelScale.DialogLeft), t.MapY(PanelScale.DialogTop),
                      t.Size(184), t.Size(119));

        Assert.False(CoversAnyBuilding(dialog));
    }

    /// <summary>
    /// AND WHAT THE CALCULATOR COSTS AT THAT SCALE, so a change to either number has to be
    /// looked at rather than slipping through: it clips Socket City's top-left corner and the
    /// University's right edge, and nothing else. Full size costs 60x33 of Socket City.
    /// </summary>
    [Fact]
    public void AtTheRequestedScaleOnlyTheCalculatorCoversAnything()
    {
        var s = PanelScale.Requested;
        var t = new PanelTransform(s,
                                   PanelScale.FreeLeft - PanelScale.DialogLeft * s,
                                   PanelScale.FreeTop - PanelScale.DialogTop * s);

        var calc = (t.MapX(StoreLayout.CalcLeft), t.MapY(StoreLayout.CalcTop),
                    t.Size(CalcWidth), t.Size(CalcHeight));

        var hit = new List<string>();
        foreach (var place in Board.All)
        {
            var overlapX = calc.Item1 < place.Left + place.Width
                           && place.Left < calc.Item1 + calc.Item3;
            var overlapY = calc.Item2 < place.Top + place.Height
                           && place.Top < calc.Item2 + calc.Item4;
            if (overlapX && overlapY) hit.Add(place.Name);
        }

        Assert.Equal(["Socket City", "University"], hit.OrderBy(n => n).ToArray());
    }

    /// <summary>
    /// ELEVENTH TWELFTHS, for the sharpness reason: this build draws at
    /// <c>RenderScale.Requested</c> 12, and 11/12 x 12 is a whole 11 device pixels per game
    /// pixel, so font 10's one-pixel stems are nearest-neighboured rather than resampled.
    /// </summary>
    [Fact]
    public void TheRequestedScaleIsWholeAtTheArtFactorThisBuildRuns()
    {
        // 12 is `RenderScale.Requested`, quoted rather than referenced because RenderScale
        // pulls Avalonia in and this project deliberately does not.
        const int artFactor = 12;
        var product = PanelScale.Requested * artFactor;
        Assert.Equal(11.0, product, 10);
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
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void TheNoCoverageScaleIsWholeAtEveryArtFactor(int artFactor)
    {
        var product = PanelScale.LargestWithNoCoverage * artFactor;
        Assert.Equal(Math.Round(product), product, 10);
    }

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
    /// With it on, the dialog's origin lands on the free rectangle's corner and the DONE
    /// button — view 250 at nsLeft 143, nsTop 108 in every location — comes with it.
    /// </summary>
    [Fact]
    public void EverythingDialogRelativeMovesWithTheOrigin()
    {
        var was = PanelScale.Enabled;
        try
        {
            PanelScale.Enabled = true;
            var t = PanelScale.Of();

            Assert.Equal(PanelScale.FreeLeft, t.MapX(PanelScale.DialogLeft), 10);
            Assert.Equal(PanelScale.FreeTop, t.MapY(PanelScale.DialogTop), 10);

            // The exit button, at the dialog's own (143,108).
            var exitX = t.MapX(PanelScale.DialogLeft + StoreLayout.ExitLeft);
            var exitY = t.MapY(PanelScale.DialogTop + StoreLayout.ExitTop);
            Assert.Equal(PanelScale.FreeLeft + StoreLayout.ExitLeft * PanelScale.Requested, exitX, 10);
            Assert.Equal(PanelScale.FreeTop + StoreLayout.ExitTop * PanelScale.Requested, exitY, 10);

            // And the calculator, which the scripts position absolutely and which would
            // otherwise be the one piece left behind on top of Socket City. It MOVES; at the
            // default scale it still clips Socket City, which is a separate decision about
            // where `gCalc` lives and is pinned by
            // AtTheRequestedScaleOnlyTheCalculatorCoversAnything.
            var calc = (L: t.MapX(StoreLayout.CalcLeft), T: t.MapY(StoreLayout.CalcTop),
                        W: t.Size(CalcWidth), H: t.Size(CalcHeight));
            Assert.True(calc.L < StoreLayout.CalcLeft, "the calculator did not move inwards");
        }
        finally
        {
            PanelScale.Enabled = was;
        }
    }
}
