using Jones.App;

namespace Jones.Tests;

/// <summary>
/// THE TAP RECTANGLES, checked as arithmetic rather than on a screenshot — which matters
/// more here than anywhere else in the port, because the case that needs them is a phone and
/// the machine this is built on is not one.
///
/// <para>
/// The three properties that make <see cref="TouchTargets"/> safe to have on everywhere with
/// no switch are all asserted below: a screen that already clears 48 dp gets NO growth at
/// all; grown rectangles never overlap each other; and none of them leaves the Dialog's own
/// `nsRight 184` / `nsBottom 119` box.
/// </para>
/// </summary>
public class TouchTargetTests
{
    /// <summary>view 250's common loop, which is every WORK / DONE / RELAX / ENROLL button.</summary>
    private const int ButtonW = 32, ButtonH = 9;

    /// <summary>
    /// MEASURED ON THE DEVELOPMENT MACHINE by the probe in `MainView.MeasureTouchScale`:
    /// RenderScaling 1, 3.835 Avalonia units per game pixel in the default 1280x800 window.
    /// </summary>
    private const double Desktop = 3.835;

    /// <summary>
    /// A 1080-wide phone in portrait with no upscaled art: the playfield is enlarged 3.375x
    /// from 320 and Avalonia's unit is 1/96 inch against Android's 1/160, so 3.375 * 0.6.
    /// </summary>
    private const double PhonePortrait = 3.375 * 0.6;

    private static double Dp(double gamePixels, double unitsPerGamePixel, double panelScale) =>
        gamePixels * panelScale * unitsPerGamePixel / 0.6;

    private static (double L, double T, double R, double B) Box(
        TouchLayout layout, object owner, double x, double y, double w, double h)
    {
        var g = layout.For(owner);
        return (x - g.Left, y - g.Top, x + w + g.Right, y + h + g.Bottom);
    }

    /// <summary>
    /// THE NUMBERS THE WHOLE THING EXISTS FOR, stated as assertions so they cannot drift:
    /// the button row clears 48 dp on the desktop and does not on a phone.
    /// </summary>
    [Fact]
    public void TheButtonRowClearsFortyEightDpOnTheDesktopAndNotOnAPhone()
    {
        Assert.True(Dp(ButtonH, Desktop, 1.0) > 48);          // 57.5 dp
        Assert.True(Dp(ButtonH, Desktop, 0.75) < 48);         // 43.1 dp — 0.75 costs the desktop its margin
        Assert.True(Dp(ButtonH, PhonePortrait, 1.0) < 48);    // 30.4 dp
        Assert.True(Dp(ButtonW, PhonePortrait, 1.0) > 48);    // 108 dp — only the HEIGHT is short
    }

    /// <summary>
    /// A DESKTOP GETS NOTHING. This is the property that lets the feature be unconditional:
    /// where the control already clears 48 dp the growth is exactly zero and the tap rectangle
    /// is the drawn one, byte for byte as before.
    /// </summary>
    [Fact]
    public void NothingGrowsWhereTheControlAlreadyClearsTheTarget()
    {
        TouchTargets.UnitsPerGamePixel = Desktop;
        var layout = new TouchLayout(1.0);

        var work = new object();
        var done = new object();
        layout.Add(work, PanelScale.DialogLeft + StoreLayout.WorkLeft,
                   PanelScale.DialogTop + StoreLayout.WorkTop, ButtonW, ButtonH);
        layout.Add(done, PanelScale.DialogLeft + StoreLayout.ExitLeft,
                   PanelScale.DialogTop + StoreLayout.ExitTop, ButtonW, ButtonH);

        Assert.Equal(HitGrowth.None, layout.For(work));
        Assert.Equal(HitGrowth.None, layout.For(done));
    }

    /// <summary>
    /// The real WORK / DONE / RELAX row, at the coordinates the scripts give them, on a phone:
    /// every one grows, none overlaps another, and none leaves the dialog.
    /// </summary>
    [Fact]
    public void TheButtonRowGrowsOnAPhoneWithoutOverlappingOrLeavingTheDialog()
    {
        TouchTargets.UnitsPerGamePixel = PhonePortrait;
        var layout = new TouchLayout(1.0);

        var row = new (string Name, object Key, int Left, int Top)[]
        {
            ("RELAX", new object(), StoreLayout.RelaxLeft, StoreLayout.RelaxTop),
            ("WORK",  new object(), StoreLayout.WorkLeft,  StoreLayout.WorkTop),
            ("DONE",  new object(), StoreLayout.ExitLeft,  StoreLayout.ExitTop),
        };

        foreach (var (_, key, left, top) in row)
            layout.Add(key, PanelScale.DialogLeft + left, PanelScale.DialogTop + top,
                       ButtonW, ButtonH);

        var boxes = row.Select(r => (r.Name,
            B: Box(layout, r.Key, PanelScale.DialogLeft + r.Left,
                   PanelScale.DialogTop + r.Top, ButtonW, ButtonH))).ToList();

        // Every one actually grew — the point of the exercise.
        foreach (var (name, b) in boxes)
            Assert.True(b.B - b.T > ButtonH, $"{name} did not grow: {b.B - b.T:0.##} tall");

        // None overlaps another.
        for (var i = 0; i < boxes.Count; i++)
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var (ni, a) = boxes[i];
                var (nj, c) = boxes[j];
                var over = a.L < c.R && c.L < a.R && a.T < c.B && c.T < a.B;
                Assert.False(over, $"{ni} and {nj} overlap");
            }

        // None leaves the Dialog's own box — `moveTo: 69 44`, nsRight 184, nsBottom 119.
        foreach (var (name, b) in boxes)
        {
            Assert.True(b.L >= PanelScale.DialogLeft, $"{name} reaches left of the dialog");
            Assert.True(b.T >= PanelScale.DialogTop, $"{name} reaches above the dialog");
            Assert.True(b.R <= PanelScale.DialogLeft + 184, $"{name} reaches right of the dialog");
            Assert.True(b.B <= PanelScale.DialogTop + 119, $"{name} reaches below the dialog");
        }
    }

    /// <summary>
    /// A column of shop lines at the 10-pixel pitch the scripts use: each one takes HALF the
    /// 4-pixel gap and no more, so two neighbours meet exactly in the middle and neither can
    /// swallow the other's taps.
    /// </summary>
    [Fact]
    public void NeighbouringLinesSplitTheGapBetweenThem()
    {
        TouchTargets.UnitsPerGamePixel = PhonePortrait;
        var layout = new TouchLayout(1.0);

        const int lineW = 72, lineH = 6, pitch = 10;
        var keys = new object[5];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = new object();
            layout.Add(keys[i], PanelScale.DialogLeft + 11,
                       PanelScale.DialogTop + 40 + pitch * i, lineW, lineH);
        }

        for (var i = 0; i < keys.Length; i++)
        {
            var g = layout.For(keys[i]);
            // The clear gap is pitch - lineH = 4, so half of it is 2 either way.
            if (i > 0) Assert.Equal(2.0, g.Top, 6);
            if (i < keys.Length - 1) Assert.Equal(2.0, g.Bottom, 6);
        }

        // And the boxes tile without overlapping.
        for (var i = 0; i + 1 < keys.Length; i++)
        {
            var a = Box(layout, keys[i], PanelScale.DialogLeft + 11,
                        PanelScale.DialogTop + 40 + pitch * i, lineW, lineH);
            var b = Box(layout, keys[i + 1], PanelScale.DialogLeft + 11,
                        PanelScale.DialogTop + 40 + pitch * (i + 1), lineW, lineH);
            Assert.True(a.B <= b.T + 1e-9, $"line {i} overlaps line {i + 1}");
        }
    }

    /// <summary>
    /// With the interior drawn SMALLER the threshold is harder to meet, not easier: a 9-pixel
    /// button at panel scale 0.75 is 6.75 pixels of screen. The layout is told the scale so it
    /// compares the right thing — getting this wrong would have reported the desktop as fine
    /// at 0.75 when it is 10% short.
    /// </summary>
    [Fact]
    public void TheThresholdFollowsThePanelScale()
    {
        TouchTargets.UnitsPerGamePixel = Desktop;

        var full = new TouchLayout(1.0);
        var small = new TouchLayout(0.75);
        var a = new object();
        var b = new object();
        full.Add(a, PanelScale.DialogLeft + StoreLayout.ExitLeft,
                 PanelScale.DialogTop + StoreLayout.ExitTop, ButtonW, ButtonH);
        small.Add(b, PanelScale.DialogLeft + StoreLayout.ExitLeft,
                  PanelScale.DialogTop + StoreLayout.ExitTop, ButtonW, ButtonH);

        Assert.Equal(HitGrowth.None, full.For(a));
        Assert.True(small.For(b).Top > 0, "a shrunk panel's button should grow its tap box");
    }

    /// <summary>
    /// An unmeasured screen grows nothing. A head that never reports a scale must behave
    /// exactly as the port always did rather than guessing at one.
    /// </summary>
    [Fact]
    public void AnUnmeasuredScreenGrowsNothing()
    {
        TouchTargets.UnitsPerGamePixel = 0;
        var layout = new TouchLayout(1.0);
        var key = new object();
        layout.Add(key, PanelScale.DialogLeft + StoreLayout.ExitLeft,
                   PanelScale.DialogTop + StoreLayout.ExitTop, ButtonW, ButtonH);

        Assert.Equal(0, TouchTargets.MinimumGamePixels);
        Assert.Equal(HitGrowth.None, layout.For(key));
    }
}
