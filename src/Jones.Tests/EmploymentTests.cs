using Jones.Core;
using Jones.Core.Model;
using Jones.Core.Sci;
using Xunit;

namespace Jones.Tests;

public class EmploymentTests
{
    private sealed class FixedRandom(int value) : IRandomSource
    {
        public int Next(int min, int max) => Math.Clamp(value, min, max);
    }

    private static Player Candidate(int dep = 50, int exp = 50)
    {
        var p = new Player { Playing = true, Dependibility = dep, Experience = exp, NetWorth = 5000 };
        p.Init();
        return p;
    }

    private static void GrantDegree(Player p, int degreeId)
    {
        var e = p.Education.Receive(degreeId, 10);
        e.UnitsToGraduate = 10;
    }

    [Fact]
    public void AllThirtyNineJobsAreDefined()
    {
        Assert.Equal(39, Jobs.All.Length);
        Assert.Equal(39, Jobs.All.Select(j => j.JobNum).Distinct().Count());
    }

    [Fact]
    public void WagesMatchThePublishedTable()
    {
        Assert.Equal(5, Jobs.At(Workplace.ZMart).Single(j => j.Title == "Clerk").BaseWage);
        Assert.Equal(25, Jobs.At(Workplace.Factory).Single(j => j.Title == "General Mgr.").BaseWage);
        Assert.Equal(20, Jobs.At(Workplace.HiTechU).Single(j => j.Title == "Professor").BaseWage);
        Assert.Equal(22, Jobs.At(Workplace.Bank).Single(j => j.Title == "Broker").BaseWage);
    }

    [Fact]
    public void EntryLevelJobsHaveNoRealDependabilityRequirement()
    {
        // A requirement of exactly 10 is subtracted away to zero (`employment.sc:101`),
        // so a hopeless candidate can still be hired as a Z-Mart clerk.
        var p = Candidate(dep: 0, exp: 10);
        var clerk = Jobs.At(Workplace.ZMart).Single(j => j.Title == "Clerk");

        var outcome = Employment.Apply(p, clerk, new FixedRandom(1), 100, new Employment.TurnedDownTracker());

        Assert.Equal(JobOutcome.Hired, outcome);
    }

    [Fact]
    public void TheCookBypassesTheOpeningsRollEntirely()
    {
        // FixedRandom(100) fails every openings roll. The Cook is hired anyway — it is
        // the game's guaranteed floor, so a player can never be permanently unemployable.
        var p = Candidate(dep: 0, exp: 0);

        var outcome = Employment.Apply(p, Jobs.Cook, new FixedRandom(100), 100, new Employment.TurnedDownTracker());

        Assert.Equal(JobOutcome.Hired, outcome);
    }

    [Fact]
    public void MissingDegreesAreReportedAsAnEducationFailure()
    {
        var p = Candidate(dep: 70, exp: 70);
        var broker = Jobs.At(Workplace.Bank).Single(j => j.Title == "Broker");

        var outcome = Employment.Apply(p, broker, new FixedRandom(1), 100, new Employment.TurnedDownTracker());

        Assert.Equal(JobOutcome.NotEnoughEducation, outcome);
        Assert.Equal(Degrees.BusinessAdmin, p.NeedEd1);
        Assert.Equal(Degrees.Academic, p.NeedEd2);
    }

    [Fact]
    public void BothDegreesAreNeededForTheTopJobs()
    {
        var p = Candidate(dep: 70, exp: 70);
        GrantDegree(p, Degrees.BusinessAdmin);
        var broker = Jobs.At(Workplace.Bank).Single(j => j.Title == "Broker");

        Assert.Equal(JobOutcome.NotEnoughEducation,
            Employment.Apply(p, broker, new FixedRandom(1), 100, new Employment.TurnedDownTracker()));

        GrantDegree(p, Degrees.Academic);
        Assert.Equal(JobOutcome.Hired,
            Employment.Apply(p, broker, new FixedRandom(1), 100, new Employment.TurnedDownTracker()));
    }

    [Fact]
    public void OnlyLosingTheOpeningsRollBlacklistsAJob()
    {
        // `employment.sc:232-235`:
        //     (if (and global325 global326 global327 (not global328))
        //         (self turnedDown: jobNum))
        //
        // Being turned away for a missing degree leaves NO mark — otherwise one early
        // rejection would make the job unobtainable for the rest of the turn, which is
        // what the port used to do.
        var p = Candidate(dep: 0, exp: 0);
        var manager = Jobs.At(Workplace.ZMart).Single(j => j.Title == "Manager");
        var tracker = new Employment.TurnedDownTracker();

        Assert.Equal(JobOutcome.NotEnoughEducation,
            Employment.Apply(p, manager, new FixedRandom(1), 100, tracker));
        Assert.False(tracker.WasRefused(manager.JobNum));

        // Qualify and the job is still open to you.
        GrantDegree(p, Degrees.JuniorCollege);
        p.Dependibility = 100;
        p.Experience = 100;
        Assert.Equal(JobOutcome.Hired,
            Employment.Apply(p, manager, new FixedRandom(1), 100, tracker));
    }

    [Fact]
    public void AQualifiedCandidateWhoLosesTheOpeningsRollIsRefusedAgain()
    {
        // Meets all three requirements exactly, so the openings roll is the only thing
        // left to fail: chance = (30 + 30 + (1 degree * 8 + 10)) / 3 + 30 = 56, and a draw
        // of 90 loses it.
        var p = Candidate(dep: 30, exp: 30);
        GrantDegree(p, Degrees.JuniorCollege);
        var manager = Jobs.At(Workplace.ZMart).Single(j => j.Title == "Manager");
        var tracker = new Employment.TurnedDownTracker();

        Assert.Equal(JobOutcome.NoOpenings,
            Employment.Apply(p, manager, new FixedRandom(90), 100, tracker));
        Assert.True(tracker.WasRefused(manager.JobNum));

        // Now blacklisted: even a winning roll is refused until the turn ends.
        Assert.Equal(JobOutcome.NoOpenings,
            Employment.Apply(p, manager, new FixedRandom(1), 100, tracker));

        tracker.Clear();
        Assert.Equal(JobOutcome.Hired,
            Employment.Apply(p, manager, new FixedRandom(1), 100, tracker));
    }

    [Fact]
    public void OpeningsChanceWithEveryDegreeAndNoStatsIsSixtyTwoPercent()
    {
        // The wiki claims 66%. The source gives (0 + 0 + 11*8 + 10)/3 + 30 = 62.
        var p = Candidate(dep: 0, exp: 0);
        foreach (var d in Degrees.All) GrantDegree(p, d.Id);

        Assert.Equal(11, p.NumDegrees());
        Assert.Equal(62, Employment.OpeningsChance(p));
    }

    [Fact]
    public void BeingUnableToAffordTheUniformDoesNotStopYouBeingHired()
    {
        // The reverse of what this file used to assert. `JobDItem::doit`
        // (`employment.sc:164-243`) calls `qualify:` and nothing else — there is no
        // uniform-affordability gate on a human application. `localproc_0`, the test that
        // looks like one, is called from a single place, `employment.sc:627`, inside the
        // COMPUTER player's job search.
        //
        // The old rule returned an outcome the original has no clip for, so applying for
        // a job you could not dress for silently did nothing at all.
        var p = Candidate(dep: 70, exp: 70);
        p.NetWorth = 50;   // nowhere near a 295-base suit
        p.Cash = 50;
        GrantDegree(p, Degrees.BusinessAdmin);
        var mgr = Jobs.At(Workplace.Bank).Single(j => j.Title == "Manager");

        Assert.Equal(JobOutcome.Hired,
            Employment.Apply(p, mgr, new FixedRandom(1), 100, new Employment.TurnedDownTracker()));
    }

    // --- Working -----------------------------------------------------------

    [Fact]
    public void HiringSetsTheCeilingsFromTheJobsOwnRequirements()
    {
        var p = Candidate(dep: 10, exp: 10);
        var clerk = Jobs.At(Workplace.ZMart).Single(j => j.Title == "Clerk");
        Employment.Apply(p, clerk, new FixedRandom(1), 100, new Employment.TurnedDownTracker());

        // The experience ceiling is the job's requirement plus 10 (`employment.sc:203`),
        // and minDepend is the requirement verbatim — NOT a per-degree bonus.
        Assert.Equal(20, p.MaxExper);      // clerk requires 10
        Assert.Equal(10, p.MinDepend);

        // Being hired grants +2 experience outright and floors dependability at 10.
        Assert.Equal(12, p.Experience);
        Assert.Equal(10, p.Dependibility);
    }

    [Fact]
    public void AJobWithNoExperienceRequirementStillCeilingsAtTwenty()
    {
        // The Cook asks for none, and the original gives a flat 20 rather than 10.
        var p = Candidate(dep: 10, exp: 0);
        Employment.Apply(p, Jobs.Cook, new FixedRandom(1), 100, new Employment.TurnedDownTracker());

        Assert.Equal(20, p.MaxExper);
    }

    [Fact]
    public void WorkingIsCappedByTheJobsCeilingUntilEducationRaisesIt()
    {
        var p = Candidate(dep: 10, exp: 10);
        var clerk = Jobs.At(Workplace.ZMart).Single(j => j.Title == "Clerk");
        Employment.Apply(p, clerk, new FixedRandom(1), 100, new Employment.TurnedDownTracker());

        var clock = new GameClock();
        for (var i = 0; i < 20; i++) { clock.Reset(); Employment.Work(p, clock); }

        // Experience stops at maxExper + expCredit, dependability at minDepend + 20.
        Assert.Equal(p.MaxExper + p.ExpCredit, p.Experience);
        Assert.Equal(p.MinDepend + 20 + p.EduCredit, p.Dependibility);

        // A degree lifts both ceilings through credits, so grinding resumes.
        p.EduCredit += 5;
        p.ExpCredit += 5;
        clock.Reset();
        Employment.Work(p, clock);

        Assert.Equal(p.MaxExper + p.ExpCredit - 4, p.Experience);
    }

    [Fact]
    public void LargeArrearsTakeHalfYourWagesAndTwoDollarsMore()
    {
        var p = Candidate();
        p.Wage = 10;
        p.RentOwed = 1000;   // far more than half a shift
        var before = p.Cash;

        var (result, paid, _) = Employment.Work(p, new GameClock());

        // Gross 80. Half (40) goes to the arrears, and a further $2 simply vanishes —
        // that $2 is literal and unexplained in the original.
        Assert.Equal(Employment.WorkResult.Paid, result);
        Assert.Equal(38, paid);
        Assert.Equal(before + 38, p.Cash);
        Assert.Equal(960, p.RentOwed);
    }

    [Fact]
    public void SmallArrearsAreClearedOutrightAndYouKeepTheRest()
    {
        var p = Candidate();
        p.Wage = 10;
        p.RentOwed = 30;     // less than half a shift's gross of 80

        var (_, paid, _) = Employment.Work(p, new GameClock());

        // The whole debt comes off and the remainder is yours: 80 - 30.
        Assert.Equal(50, paid);
        Assert.Equal(0, p.RentOwed);
    }

    [Fact]
    public void YouAreSackedOnceDependabilityFallsMoreThanFiveBelowTheRequirement()
    {
        var p = Candidate();
        p.Wage = 10;
        p.MinDepend = 30;
        p.Dependibility = 24;   // 30 - 5 = 25, and 24 is below it

        var (result, paid, _) = Employment.Work(p, new GameClock());

        Assert.Equal(Employment.WorkResult.Fired, result);
        Assert.Equal(0, paid);
        Assert.Equal(0, p.Wage);
        Assert.Equal(0, p.WorksAt);
    }

    [Fact]
    public void YouCannotWorkWithoutTheRightUniform()
    {
        var p = Candidate();
        p.Wage = 10;
        p.Uniform = ItemIds.BusinessSuit; // only casual in the wardrobe

        var (result, paid, _) = Employment.Work(p, new GameClock());
        Assert.Equal(Employment.WorkResult.NotWorked, result);
        Assert.Equal(0, paid);
    }

    [Fact]
    public void WorkingCostsNoHappiness()
    {
        // Confirmed by absence: n108.sc contains no happiness call at all.
        var p = Candidate();
        p.Wage = 10;
        p.HapStat = 50;

        Employment.Work(p, new GameClock());

        Assert.Equal(50, p.HapStat);
    }
}
