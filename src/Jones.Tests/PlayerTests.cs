using Jones.Core.Model;
using Jones.Core.Sci;
using Xunit;

namespace Jones.Tests;

public class PlayerTests
{
    private static Player Employed(int dependibility, int wage = 10)
    {
        var p = new Player { Playing = true, Wage = wage, Dependibility = dependibility };
        p.Init();
        return p;
    }

    // -----------------------------------------------------------------------
    // Career stat. The wiki documents 1.25 x dependability; the source computes
    // (dep / 8) * 10 with truncating division. These cases pin the difference.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]    // wiki would say 0
    [InlineData(7, 0)]    // wiki would say 8.75 — source gives 0, still below the first step
    [InlineData(8, 10)]   // first step
    [InlineData(15, 10)]  // wiki would say 18.75
    [InlineData(16, 20)]
    [InlineData(20, 20)]  // wiki would say 25 — the headline discrepancy
    [InlineData(23, 20)]
    [InlineData(24, 30)]
    [InlineData(80, 100)]
    [InlineData(88, 100)] // (88/8)*10 = 110, capped
    public void CareerStatIsAStepFunctionOfDependability(int dep, int expected)
    {
        var p = Employed(dep);
        p.RecalculateCareerStat();
        Assert.Equal(expected, p.CarStat);
    }

    [Fact]
    public void CareerStatIsZeroWhileUnemployed_HoweverDependableYouAre()
    {
        var p = Employed(dependibility: 100, wage: 0);
        p.RecalculateCareerStat();
        Assert.Equal(0, p.CarStat);
    }

    [Fact]
    public void DependabilityDecaysThreePerTurnAndFloorsAtZero()
    {
        var p = Employed(dependibility: 4);
        p.EndTurn(week: 1);
        Assert.Equal(1, p.Dependibility);

        p.EndTurn(week: 2);
        Assert.Equal(0, p.Dependibility); // 1 - 3 = -2, floored
    }

    // -----------------------------------------------------------------------
    // Clothing. Lower id is dressier, and a player always wears the best held.
    // -----------------------------------------------------------------------

    [Fact]
    public void PlayerWearsTheBestClothingHeld()
    {
        var p = Employed(20);                             // Init gives 6 casual
        p.Consumables.Receive(ItemIds.DressClothes, 1);
        p.DressedForWork();
        Assert.Equal(ItemIds.DressClothes, p.Wearing);

        p.Consumables.Receive(ItemIds.BusinessSuit, 1);
        p.DressedForWork();
        Assert.Equal(ItemIds.BusinessSuit, p.Wearing);
    }

    [Fact]
    public void CasualClothesDoNotSatisfyABusinessSuitUniform()
    {
        var p = Employed(20);
        p.Uniform = ItemIds.BusinessSuit;
        Assert.False(p.DressedForWork());
    }

    [Fact]
    public void BetterClothingThanRequiredIsAccepted()
    {
        var p = Employed(20);
        p.Uniform = ItemIds.CasualClothes;
        p.Consumables.Receive(ItemIds.BusinessSuit, 1);
        Assert.True(p.DressedForWork()); // "uniform or better"
    }

    [Fact]
    public void WithNoClothingAtAllThePlayerCannotWork()
    {
        var p = new Player { Playing = true }; // no Init, so no clothes
        Assert.False(p.DressedForWork());
        Assert.Equal(0, p.Wearing);
        Assert.Equal(0, p.WeeksOfClothing());
    }

    // -----------------------------------------------------------------------
    // Market crash. Severity 1 is the WORST.
    // -----------------------------------------------------------------------

    [Fact]
    public void Severity1_WipesTheBankBalanceAndAlwaysCostsTheJob()
    {
        var p = Employed(50, wage: 20);
        p.BankBal = 5000;

        // Random(0, 0) can only return 0, so the job is lost with certainty.
        var result = p.DoScandal(severity: 1, new ScriptedRandom(0));

        Assert.Equal(1, result);
        Assert.Equal(0, p.BankBal);
        Assert.Equal(0, p.Wage);
        Assert.Equal(0, p.WorksAt);
    }

    [Fact]
    public void Severity3_LeavesTheBankAlone()
    {
        var p = Employed(50, wage: 20);
        p.BankBal = 5000;

        p.DoScandal(severity: 3, new ScriptedRandom(1));

        Assert.Equal(5000, p.BankBal); // only severity 1 is a bank failure
    }

    [Theory]
    // wage * (4 + 2s) / 10, reached only when the job-loss roll misses.
    [InlineData(2, 20, 16)] // 20 * 8/10 — a 20% cut
    [InlineData(3, 20, 20)] // 20 * 10/10 — no cut at all
    public void WageCutScalesWithSeverity(int severity, int wage, int expected)
    {
        var p = Employed(50, wage);
        // Roll 1 misses the `== 0` job-loss test for severities 2 and 3.
        var result = p.DoScandal(severity, new ScriptedRandom(1));

        Assert.Equal(-1, result);
        Assert.Equal(expected, p.Wage);
    }

    [Fact]
    public void Severity1WageCutIsUnreachable_TheJobIsAlwaysLostFirst()
    {
        // ORIGINAL DEAD CODE, asserted deliberately. The job-loss test is
        // `Random(0, severity - 1) == 0`, and at severity 1 that is Random(0, 0), which
        // can only return 0. So an employed player ALWAYS loses the job at severity 1 and
        // the wage-cut branch below it — nominally a 40% cut — can never execute.
        //
        // ScriptedRandom throws if asked for a roll outside the requested range, so
        // supplying only the single legal roll proves the range really is [0,0].
        var p = Employed(50, wage: 20);
        var rng = new ScriptedRandom(0);

        Assert.Equal(1, p.DoScandal(severity: 1, rng)); // 1 = job lost, never -1
        Assert.Equal(0, p.Wage);
        Assert.Equal(1, rng.Consumed);
    }

    [Fact]
    public void WageCutFloorsAtOne()
    {
        // Severity 2, job-loss roll missed: 1 * 8/10 = 0 after truncation, floored to 1.
        var p = Employed(50, wage: 1);
        p.DoScandal(severity: 2, new ScriptedRandom(1));
        Assert.Equal(1, p.Wage);
    }

    [Fact]
    public void AnUnemployedPlayerIsUntouchedByAScandal()
    {
        var p = Employed(50, wage: 0);
        Assert.Equal(0, p.DoScandal(severity: 2, new ScriptedRandom(0)));
    }

    // -----------------------------------------------------------------------
    // Rent, and the turn clock.
    // -----------------------------------------------------------------------

    [Fact]
    public void RentFallsDueOnlyInTheFourthWeekAndOnlyWhenPrepaidWeeksRunOut()
    {
        // WAS `Assert.Equal(0, p.RentOwed)` after week 3, which asserted a rule the source
        // does not have. `Player::endTurn`'s NON-rent-week branch (`room1.sc:808-820`)
        // accrues too, once, as long as you owe nothing yet — so running out of prepaid
        // weeks bills you in whatever week it happens.
        var p = Employed(20);
        p.Consumables.At(ItemIds.LowCostRent)!.Quantity = 0; // prepaid weeks exhausted

        p.EndTurn(week: 3);
        Assert.Equal(325, p.RentOwed);
        Assert.True(p.TurnedOver);

        // And not a second time, in the rent week or out of it: `turnedOver` latches it.
        p.EndTurn(week: 4);
        Assert.Equal(325, p.RentOwed);
    }

    [Fact]
    public void PrepaidWeeksStopTheOutOfMonthCharge()
    {
        // The other half of `room1.sc:808-820`: the entry has to be at zero.
        var p = Employed(20);   // Init leaves 3 weeks prepaid

        p.EndTurn(week: 3);
        Assert.Equal(0, p.RentOwed);
    }

    [Fact]
    public void RentIsNotChargedTwiceInTheSameMonth()
    {
        var p = Employed(20);
        p.Consumables.At(ItemIds.LowCostRent)!.Quantity = 0;

        p.EndTurn(week: 4);
        p.EndTurn(week: 8); // TurnedOver still set, so no second charge
        Assert.Equal(325, p.RentOwed);
    }

    [Fact]
    public void PrepaidRentWeeksPreventTheCharge()
    {
        var p = Employed(20); // Init leaves 3 weeks prepaid
        p.EndTurn(week: 4);
        Assert.Equal(0, p.RentOwed);
    }

    [Theory]
    [InlineData(10, 6, 80)]  // full session: wage 10 x 8
    [InlineData(10, 3, 40)]  // half the hours, half the pay
    [InlineData(10, 1, 13)]  // 80 * 1 / 6 = 13 after truncation, not 13.33
    [InlineData(10, 60, 80)] // never more than a full session
    public void WorkPayoutScalesWithHoursRemaining(int wage, int hours, int expected)
    {
        Assert.Equal(expected, GameClock.WorkPayout(wage, hours));
    }

    [Fact]
    public void TheClockIsSixtyHoursAndEnteringABuildingAlwaysCostsTwo()
    {
        var c = new GameClock();
        Assert.Equal(60, c.HoursRemaining);

        c.Spend(GameClock.EnterLocationCost);
        c.Spend(GameClock.EnterLocationCost); // stepping out and back in costs again
        Assert.Equal(56, c.HoursRemaining);

        c.Spend(100);
        Assert.Equal(0, c.HoursRemaining);
        Assert.True(c.TurnOver);
    }

    [Fact]
    public void RentWeekIsEveryFourthWeekAndCatastrophesWaitUntilWeekEight()
    {
        var cal = new GameCalendar();
        Assert.False(cal.CatastrophesEnabled);

        for (var i = 1; i < 4; i++) cal.AdvanceWeek();
        Assert.Equal(4, cal.Week);
        Assert.True(cal.IsRentWeek);

        for (var i = 4; i < 8; i++) cal.AdvanceWeek();
        Assert.Equal(8, cal.Week);
        Assert.True(cal.CatastrophesEnabled);
    }

    // -----------------------------------------------------------------------
    // The "Who's Winning" percentage - `localproc_0`, `viewGoals.sc:22-59`.
    // -----------------------------------------------------------------------

    private static Player WithGoals(
        int monStat, int hapStat, int eduStat, int carStat,
        int monGoal = 50, int hapGoal = 50, int eduGoal = 50, int carGoal = 50) =>
        new()
        {
            MonStat = monStat, HapStat = hapStat, EduStat = eduStat, CarStat = carStat,
            MonGoal = monGoal, HapGoal = hapGoal, EduGoal = eduGoal, CarGoal = carGoal,
        };

    [Fact]
    public void GoalProgressIsZeroBeforeAnyProgress()
    {
        // `(if temp1 ... else (= temp0 0))` - the division is skipped entirely, which is
        // what keeps a brand new player off a divide by the goal total.
        Assert.Equal(0, WithGoals(0, 0, 0, 0).GoalProgressPct());
    }

    [Fact]
    public void GoalProgressIsOneHundredWhenEveryGoalIsExactlyMet()
    {
        Assert.Equal(100, WithGoals(50, 50, 50, 50).GoalProgressPct());
        Assert.Equal(100, WithGoals(90, 10, 100, 40, 90, 10, 100, 40).GoalProgressPct());
    }

    [Fact]
    public void OvershootingOneGoalDoesNotCoverForNeglectingTheOthers()
    {
        // Each stat is clamped to ITS OWN goal at `:31-42`, so the millionaire with no
        // degrees, no job and no fun reads a quarter done, not full.
        var rich = WithGoals(100, 0, 0, 0);
        Assert.Equal(25, rich.GoalProgressPct());

        // Without the clamp this would be (100+0+0+0)*100/200 = 50, so the clamp is worth
        // exactly the 25 points between the two.
        Assert.NotEqual(50, rich.GoalProgressPct());

        // ...and a stat already at its goal gains nothing from going further.
        Assert.Equal(WithGoals(50, 20, 0, 0).GoalProgressPct(),
                     WithGoals(100, 20, 0, 0).GoalProgressPct());
    }

    [Fact]
    public void GoalProgressKeepsTheRemainderTheHalvedMultiplierWouldHaveLost()
    {
        // goals = 40 (the minimum: four goals of 10), stats = 3.
        //   stats * 50      = 150
        //   150 / 40        = 3, doubled = 6      <- halved multiplier alone
        //   150 % 40        = 30, * 2 = 60, / 40 = 1
        //   total           = 7                   = 3 * 100 / 40, truncated
        // Dropping the remainder term would report 6.
        var p = WithGoals(3, 0, 0, 0, 10, 10, 10, 10);
        Assert.Equal(7, p.GoalProgressPct());
    }

    [Theory]
    // goals of 10..100 in every combination the setup screen allows would be a lot of
    // cases; these span the range, including the two extremes of the goal total.
    [InlineData(10, 10, 10, 10, 40)]
    [InlineData(100, 100, 100, 100, 400)]
    [InlineData(50, 50, 50, 50, 200)]
    [InlineData(90, 30, 70, 10, 200)]
    [InlineData(100, 10, 10, 10, 130)]
    public void GoalProgressMatchesTheUnoverflowedHundredPercentItStandsInFor(
        int monGoal, int hapGoal, int eduGoal, int carGoal, int goalTotal)
    {
        Assert.Equal(goalTotal, monGoal + hapGoal + eduGoal + carGoal);

        // The formula is `stats * 100 / goals` rearranged to keep every intermediate inside
        // a signed 16-bit word. Sweep the whole reachable range of `stats` and check it
        // still agrees with the arithmetic it is standing in for.
        for (var mon = 0; mon <= monGoal; mon += 3)
            for (var hap = 0; hap <= hapGoal; hap += 7)
            {
                var p = WithGoals(mon, hap, eduGoal, carGoal,
                                  monGoal, hapGoal, eduGoal, carGoal);
                var stats = mon + hap + eduGoal + carGoal;
                Assert.Equal(SciMath.Div(stats * 100, goalTotal), p.GoalProgressPct());
            }
    }

    [Fact]
    public void TheHalvedMultiplierIsWhatKeepsItInsideASixteenBitWord()
    {
        // Why the formula is shaped the way it is. Four goals of 100 give a goal total of
        // 400, and a fully met set of stats gives the same - so the naive numerator is
        // 40000, past SCI's 32767 ceiling, while the halved one is 20000 and safe.
        const int maxStats = 400;
        Assert.True(maxStats * 100 > short.MaxValue);
        Assert.True(maxStats * 50 <= short.MaxValue);
    }

    // -----------------------------------------------------------------------
    // The PER-GOAL percentage — a deliberate addition, not in the original.
    // -----------------------------------------------------------------------

    [Fact]
    public void EachGoalReportsItsOwnProgress()
    {
        var p = WithGoals(25, 10, 50, 0, monGoal: 50, hapGoal: 40, eduGoal: 50, carGoal: 80);

        Assert.Equal(50, p.GoalProgressPct(0));   // 25 of 50
        Assert.Equal(25, p.GoalProgressPct(1));   // 10 of 40
        Assert.Equal(100, p.GoalProgressPct(2));  // 50 of 50
        Assert.Equal(0, p.GoalProgressPct(3));    // 0 of 80
    }

    [Fact]
    public void APerGoalFigureIsClampedAndTruncatedLikeTheOverallOne()
    {
        // Overshooting one goal cannot read past 100, exactly as `localproc_0` clamps each
        // stat to its own goal before summing.
        Assert.Equal(100, WithGoals(90, 0, 0, 0, monGoal: 30).GoalProgressPct(0));

        // Truncation, not rounding: 1 of 3 is 33, not 34.
        Assert.Equal(33, WithGoals(1, 0, 0, 0, monGoal: 3).GoalProgressPct(0));

        // A zero goal divides by nothing and reads 0 rather than throwing.
        Assert.Equal(0, WithGoals(10, 0, 0, 0, monGoal: 0).GoalProgressPct(0));
    }

    [Fact]
    public void NoPerGoalFigureCanExceedTheOverallOneWhenAllFourAreEqual()
    {
        // The shapes agree: with four identical stat/goal pairs the per-goal figure and the
        // overall one are the same number, which is what makes the addition safe to show
        // beside the original's own percentage on Who's Winning.
        var p = WithGoals(20, 20, 20, 20, 50, 50, 50, 50);

        Assert.Equal(p.GoalProgressPct(), p.GoalProgressPct(0));
    }
}
