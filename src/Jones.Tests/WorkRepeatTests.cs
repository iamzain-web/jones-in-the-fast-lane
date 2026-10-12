using Jones.Core;
using Jones.Core.Model;

namespace Jones.Tests;

/// <summary>
/// CLICKING WORK FASTER MUST DO EXACTLY WHAT CLICKING IT SLOWLY DOES.
///
/// <para>
/// The WORK button used to miss clicks while its clock turned — not because anything was
/// disabled, but because the animation rebuilt the whole screen and an Avalonia Button that
/// is replaced between press and release swallows the click. The fix is in the VIEW: an
/// animation tick now swaps its own sprite instead of clearing the collection.
/// </para>
///
/// <para>
/// THE ACCOUNTING IS NOT TOUCHED BY THAT, and this file is the proof rather than the claim.
/// `Game.Work` is synchronous and knows nothing about any animation, so "fast" and "slow" are
/// the same sequence of calls; what these tests pin is the property that makes that safe —
/// that the clock is checked FIRST and a shift with no hours left does nothing at all
/// (`Employment.Work`, which mirrors `n108.sc`'s order of tests). A fast click cannot spend
/// hours the player does not have, because the spend is inside the same call that refuses.
/// </para>
/// </summary>
public class WorkRepeatTests
{
    /// <summary>A player employed at Z-Mart as a clerk, dressed, standing at their workplace.</summary>
    private static Game Employed()
    {
        var g = new Game(new Jones.Core.Sci.SciRandom(1), 1);
        var p = g.Current;

        // Hired through the game's own hire path rather than by poking fields, so wage,
        // uniform, minDepend and maxExper are whatever `employment.sc:198-230` would set.
        var job = Jobs.At(Workplace.ZMart).Single(j => j.Title == "Clerk");
        Employment.Hire(p, job, g.GoodsIndex);
        p.Location = LocationId.ZMart;
        p.RentOwed = 0;                 // keep garnishment out of the arithmetic under test

        Assert.True(p.DressedForWork(), "the fixture must be able to work at all");
        return g;
    }

    /// <summary>Works until the clock refuses, and reports what the whole turn did.</summary>
    private static (int Shifts, int Paid, int Hours) WorkUntilSpent(Game g, int limit = 100)
    {
        var shifts = 0;
        var paid = 0;
        var before = g.Clock.HoursRemaining;

        while (!g.Clock.TurnOver && shifts < limit)
        {
            var got = g.Work();
            if (g.LastWorkResult == Employment.WorkResult.NotWorked) break;
            paid += got;
            shifts++;
        }

        return (shifts, paid, before - g.Clock.HoursRemaining);
    }

    /// <summary>
    /// THE TEST THE CHANGE WAS ASKED FOR: hammer the button to zero and the turn ends exactly
    /// as it should — every hour spent, nothing over-spent, and the clock reading over.
    /// </summary>
    [Fact]
    public void WorkingUntilTheHoursRunOutEndsTheTurnCleanly()
    {
        var g = Employed();
        var all = GameClock.HoursPerTurn;

        var (shifts, paid, hours) = WorkUntilSpent(g);

        Assert.True(shifts > 0, "the fixture never worked a shift");
        Assert.True(g.Clock.TurnOver);
        Assert.Equal(0, g.Clock.HoursRemaining);
        Assert.Equal(all, hours);                  // every hour accounted for, none invented
        Assert.True(paid > 0);
    }

    /// <summary>
    /// AND HAMMERING IT PAST ZERO CHANGES NOTHING. `Employment.Work` tests `clock.TurnOver`
    /// before anything else and returns `NotWorked` with no spend and no pay, so the twenty
    /// extra clicks a fast player gets in are exactly as harmless as the one a slow player
    /// manages. This is the assertion that says a fast click cannot overdraw the clock.
    /// </summary>
    [Fact]
    public void ClicksAfterTheHoursAreGoneCostAndPayNothing()
    {
        var g = Employed();
        WorkUntilSpent(g);

        var p = g.Current;
        var cash = p.Cash;
        var hours = g.Clock.HoursRemaining;
        var experience = p.Experience;
        var dependability = p.Dependibility;
        var worksAt = p.WorksAt;

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(0, g.Work());
            Assert.Equal(Employment.WorkResult.NotWorked, g.LastWorkResult);
        }

        Assert.Equal(cash, p.Cash);
        Assert.Equal(hours, g.Clock.HoursRemaining);
        Assert.Equal(experience, p.Experience);
        Assert.Equal(dependability, p.Dependibility);
        Assert.Equal(worksAt, p.WorksAt);
        Assert.True(g.Clock.TurnOver);
    }

    /// <summary>
    /// TWO IDENTICAL RUNS AGREE TO THE PENNY. There is no timing anywhere in the core — the
    /// animation lives entirely in the view — so a run of N shifts is the same whether those
    /// N calls arrive in a second or a minute. Stated as a test because the request was
    /// explicitly "the same hours spent, the same money earned".
    /// </summary>
    [Fact]
    public void TwoRunsOfTheSameShiftsAgreeExactly()
    {
        var fast = Employed();
        var slow = Employed();

        var a = WorkUntilSpent(fast);
        var b = WorkUntilSpent(slow);

        Assert.Equal(a.Shifts, b.Shifts);
        Assert.Equal(a.Paid, b.Paid);
        Assert.Equal(a.Hours, b.Hours);
        Assert.Equal(fast.Current.Cash, slow.Current.Cash);
        Assert.Equal(fast.Current.Experience, slow.Current.Experience);
        Assert.Equal(fast.Current.Dependibility, slow.Current.Dependibility);
        Assert.Equal(fast.Clock.HoursRemaining, slow.Clock.HoursRemaining);
    }

    /// <summary>
    /// THE LAST SHIFT IS PRORATED, NOT REFUSED, and that is `n108.sc`'s own behaviour: a
    /// shift started with fewer than six hours left pays `wage * 8 * hoursLeft / 6`. Pinned
    /// here because a fast clicker meets this case every turn, where a slow one may not.
    /// </summary>
    [Fact]
    public void AShortLastShiftIsProratedRatherThanRefused()
    {
        var g = Employed();
        var p = g.Current;

        // Spend down to fewer hours than a session costs.
        g.Clock.Spend(GameClock.HoursPerTurn - (GameClock.WorkSessionCost - 2));
        Assert.False(g.Clock.TurnOver);
        Assert.True(g.Clock.HoursRemaining < GameClock.WorkSessionCost);

        var full = p.Wage * 8;
        var paid = g.Work();

        Assert.NotEqual(Employment.WorkResult.NotWorked, g.LastWorkResult);
        Assert.True(paid > 0, "a short shift still pays");
        Assert.True(paid < full, "a short shift pays less than a full one");
        Assert.True(g.Clock.TurnOver, "and it takes the rest of the week with it");
    }
}
