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

    /// <summary>THE CLAIM: at <see cref="PanelScale.Requested"/> no building is covered.</summary>
    [Fact]
    public void TheRequestedScaleCoversNoBuilding()
    {
        Assert.False(CoversAnyBuilding(Group(PanelScale.Requested)));
    }

    /// <summary>
    /// AND IT IS THE LARGEST THAT DOES. A whole game pixel more width than the group gets at
    /// 0.75 puts it on Monolith's first column, so there is nothing left on the table.
    /// </summary>
    [Fact]
    public void TheRequestedScaleIsTheLargestThatCoversNoBuilding()
    {
        var g = Group(PanelScale.Requested);
        Assert.True(CoversAnyBuilding((g.L, g.T, g.W + 1, g.H)));

        // Stated as the ratio as well, so the reason survives a change to either number.
        Assert.Equal(PanelScale.FreeWidth / (double)PanelScale.GroupWidth,
                     PanelScale.Requested, 10);
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
    /// The sharpness argument: 0.75 times every scale the art is ever drawn at is a whole
    /// number, so the game's own 6-pixel font 10 is still nearest-neighboured by an integer
    /// and never resampled into grey. Pick 0.7 and this fails.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void TheScaleIsAWholeNumberOfDevicePixelsAtEveryArtFactor(int artFactor)
    {
        var product = PanelScale.Requested * artFactor;
        Assert.Equal(Math.Round(product), product, 10);
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

            // And the calculator, which the scripts position absolutely and which is the one
            // piece that would otherwise be left behind on top of Socket City.
            var calc = (L: t.MapX(StoreLayout.CalcLeft), T: t.MapY(StoreLayout.CalcTop),
                        W: t.Size(CalcWidth), H: t.Size(CalcHeight));
            Assert.False(CoversAnyBuilding(calc));
        }
        finally
        {
            PanelScale.Enabled = was;
        }
    }
}
