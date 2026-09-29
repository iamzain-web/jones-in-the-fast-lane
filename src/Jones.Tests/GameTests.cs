using Jones.Core;
using Jones.Core.Model;
using Jones.Core.Sci;
using Xunit;

namespace Jones.Tests;

public class UniversityTests
{
    private static Player Student()
    {
        var p = new Player { Playing = true, NetWorth = 10000, Cash = 10000 };
        p.Init();
        return p;
    }

    [Fact]
    public void ElevenDegreesExistAndOnlyTwoAreOpenAtTheStart()
    {
        Assert.Equal(11, Degrees.All.Length);

        var open = Degrees.InitiallyAvailable.Select(d => d.Id).OrderBy(x => x).ToArray();
        Assert.Equal([Degrees.TradeSchool, Degrees.JuniorCollege], open);
    }

    [Fact]
    public void ACourseTakesTenLessonsAndAwardsItsDegree()
    {
        var p = Student();
        University.Enroll(p, 100);
        var clock = new GameClock();

        StudyOutcome last = StudyOutcome.LessonTaken;
        for (var i = 0; i < 10; i++)
        {
            clock.Reset();
            last = University.Study(p, Degrees.JuniorCollege, clock);
        }

        Assert.Equal(StudyOutcome.Graduated, last);
        Assert.True(Degrees.HasDegree(p, Degrees.JuniorCollege));
        Assert.Equal(1, p.NumDegrees());
    }

    [Fact]
    public void GraduationPaysHappinessDependabilityAndPermanentCeilingRises()
    {
        var p = Student();
        p.HapStat = 10;
        p.Dependibility = 20;
        p.MaxExper = 10;
        University.Enroll(p, 100);

        var clock = new GameClock();
        for (var i = 0; i < 10; i++) { clock.Reset(); University.Study(p, Degrees.JuniorCollege, clock); }

        Assert.Equal(15, p.HapStat);
        Assert.Equal(25, p.Dependibility);
        Assert.Equal(5, p.EduCredit);
        Assert.Equal(5, p.ExpCredit);
    }

    [Fact]
    public void ExtraCreditShortensCoursesAndAppliesRetroactively()
    {
        var p = Student();

        // A computer alone is worth one lesson.
        p.Durables.Receive(ItemIds.Computer, 1);
        Degrees.RecalculateExtraCredit(p);
        Assert.Equal(1, p.XCred);

        // The three books together are worth a second — but only as a complete set.
        p.Durables.Receive(31, 1);
        p.Durables.Receive(32, 1);
        Degrees.RecalculateExtraCredit(p);
        Assert.Equal(1, p.XCred); // two of three buys nothing

        p.Durables.Receive(33, 1);
        Degrees.RecalculateExtraCredit(p);
        Assert.Equal(2, p.XCred);

        // With both, a course finishes in 8 lessons rather than 10.
        University.Enroll(p, 100);
        var clock = new GameClock();
        StudyOutcome last = StudyOutcome.LessonTaken;
        for (var i = 0; i < 8; i++) { clock.Reset(); last = University.Study(p, Degrees.JuniorCollege, clock); }

        Assert.Equal(StudyOutcome.Graduated, last);
    }

    [Fact]
    public void PrerequisitesGateTheDegreeTree()
    {
        var p = Student();
        University.Enroll(p, 100);

        Assert.Equal(StudyOutcome.PrerequisiteMissing,
            University.Study(p, Degrees.Engineering, new GameClock()));

        // Engineering needs Pre-Engineering, which needs Trade School.
        var chain = new[] { Degrees.TradeSchool, Degrees.PreEngineering, Degrees.Engineering };
        foreach (var id in chain)
        {
            University.Enroll(p, 100);
            var clock = new GameClock();
            for (var i = 0; i < 10; i++) { clock.Reset(); University.Study(p, id, clock); }
        }

        Assert.True(Degrees.HasDegree(p, Degrees.Engineering));
    }

    [Fact]
    public void StudyingWithoutEnrollingFails()
    {
        var p = Student();
        Assert.Equal(StudyOutcome.NotEnrolled, University.Study(p, Degrees.JuniorCollege, new GameClock()));
    }

    [Fact]
    public void EducationScoreReachesOneHundredOnlyWithEveryDegree()
    {
        var p = Student();
        Assert.Equal(1, University.EducationScore(p));

        foreach (var d in Degrees.All)
            p.Education.Receive(d.Id, 10).UnitsToGraduate = 10;

        Assert.Equal(100, University.EducationScore(p)); // 1 + 9*11
    }
}

public class BoardTests
{
    [Fact]
    public void ThirteenLocationsInClockwiseOrder()
    {
        Assert.Equal(13, Board.All.Length);

        var indices = Board.All.Select(l => l.PathIndex).ToArray();
        Assert.Equal(indices.OrderBy(x => x), indices); // already sorted = clockwise
    }

    [Fact]
    public void TravelTakesTheShorterArcInEitherDirection()
    {
        // `MarblePath::setDirection` (`marblePath.sc:86-104`) compares both arcs against
        // `(/ [local0 0] 2)` = 85 and walks the shorter one. This test replaces
        // `TravelWrapsForwardAroundTheRing`, which asserted the port's invented one-way
        // rule — and which disagreed with the port's own MarblePath, so the marble walked
        // the short way while the clock charged for the long one.
        Assert.Equal(10, Board.StepsBetween(LocationId.PawnShop, LocationId.ZMart));
        Assert.Equal(10, Board.StepsBetween(LocationId.ZMart, LocationId.PawnShop));

        // The furthest apart any two Places can be is half the ring.
        foreach (var a in Board.All)
            foreach (var b in Board.All)
                Assert.InRange(Board.StepsBetween(a.Id, b.Id), 0, Board.PathLength / 2);
    }

    [Fact]
    public void ThePathIsOneHundredAndSeventyStepsRound()
    {
        // `marblePath.sc:13` — element 0 of the coordinate table IS the count.
        Assert.Equal(170, Board.PathLength);
        Assert.Equal(MarblePath.StepCount, Board.PathLength);

        // Visiting every Place in order and coming home walks the whole ring exactly once.
        var total = 0;
        for (var i = 0; i < Board.All.Length; i++)
        {
            var from = Board.All[i].Id;
            var to = Board.All[(i + 1) % Board.All.Length].Id;
            total += Board.StepsBetween(from, to);
        }

        Assert.Equal(170, total);
    }

    [Fact]
    public void ALapCostsElevenHoursAtFifteenStepsToTheHour()
    {
        // `global475 = (marble moveSpeed:) * 14` = 14, and the hour ticks on
        // `(> (++ global324) global475)` — so fifteen steps per hour (`room1.sc:1319/1488`),
        // and a 170-step lap is 11 hours with 5 ticks left in the accumulator. The old
        // `ALapCostsAboutTenHours` asserted 9..14 against an `[UNVERIFIED]` HoursPerLap of
        // 10 over an `[UNVERIFIED]` PathLength of 175.
        var clock = new GameClock();
        clock.SpendTravelSteps(Board.PathLength);

        Assert.Equal(11, clock.HoursUsed);
        Assert.Equal(5, clock.SubHourTicks);
    }

    [Fact]
    public void ShortHopsCostNothingUntilTheAccumulatorFills()
    {
        // global324 is zeroed only at turn start (`room1.sc:1094`) and carries across the
        // whole turn: there is no per-journey rounding and no one-hour floor.
        var clock = new GameClock();

        clock.SpendTravelSteps(7);
        Assert.Equal(0, clock.HoursUsed);   // a short hop can be free

        clock.SpendTravelSteps(8);
        Assert.Equal(1, clock.HoursUsed);   // and two of them cost an hour between them

        clock.Reset();
        Assert.Equal(0, clock.SubHourTicks);
    }

    [Fact]
    public void TheRentOfficeOpensOnThreeClausesNotOne()
    {
        // `room1.sc:176-193` / `rentOffice.sc:66-71`:
        //   (or (== (global302 worksAt:) 1) (not (mod global372 4)) (global302 leaveOpen:))
        var p = new Player();

        Assert.False(Board.IsOpen(LocationId.RentOffice, 3, p));
        Assert.True(Board.IsOpen(LocationId.RentOffice, 4, p));
        Assert.True(Board.IsOpen(LocationId.ZMart, 3, p));

        // Clause one: someone employed there is never locked out of their own job.
        var clerk = new Player { WorksAt = Board.RentOfficeWorksAt };
        Assert.True(Board.IsOpen(LocationId.RentOffice, 3, clerk));

        // Clause three: a successful extension request keeps it open next month.
        var extended = new Player { LeaveOpen = true };
        Assert.True(Board.IsOpen(LocationId.RentOffice, 3, extended));
    }

    [Fact]
    public void AShutBuildingDoesNotRefuseTheJourney()
    {
        // `Place::cue` (`room1.sc:174-209`) opens the dialog unconditionally; closure only
        // skips the door animation. The port used to refuse the trip outright.
        var game = new Game(new MidRandom());
        game.StartTurn();

        Assert.False(Board.IsOpen(LocationId.RentOffice, game.Calendar.Week, game.Current));
        Assert.True(game.TravelTo(LocationId.RentOffice));
        Assert.Equal(LocationId.RentOffice, game.Current.Location);
    }

    [Fact]
    public void ReEnteringTheBuildingYouStandInCostsTwoHours()
    {
        // `room1.sc:164-167` routes a click on your current Place to `self cue:`, whose
        // `init` charges the same 2 hours as any other door. The port returned true and
        // charged nothing.
        var game = new Game(new MidRandom());
        game.StartTurn();

        var before = game.Clock.HoursRemaining;
        Assert.True(game.TravelTo(game.Current.Location));

        Assert.Equal(before - GameClock.EnterLocationCost, game.Clock.HoursRemaining);
    }

    private sealed class MidRandom : IRandomSource
    {
        public int Next(int min, int max) => min + (max - min) / 2;
    }
}

public class GameTests
{
    private sealed class MidRandom : IRandomSource
    {
        public int Next(int min, int max) => min + (max - min) / 2;
    }

    [Fact]
    public void ANewGameStartsEveryoneAtHomeWithTwoHundredDollars()
    {
        var g = new Game(new MidRandom(), playerCount: 2);

        Assert.All(g.Players, p =>
        {
            Assert.Equal(200, p.Cash);
            Assert.Equal(LocationId.LowCostHousing, p.Location);
            Assert.Equal(2, p.MonStat); // 200 / 100
        });
    }

    [Fact]
    public void TurnsRotateAndTheWeekAdvancesAfterTheLastPlayer()
    {
        var g = new Game(new MidRandom(), playerCount: 2);
        Assert.Equal(1, g.Calendar.Week);

        g.StartTurn(); g.EndTurn();
        Assert.Equal(1, g.Calendar.Week); // still week 1, player 2's go
        Assert.Equal(1, g.CurrentPlayerIndex);

        g.StartTurn(); g.EndTurn();
        Assert.Equal(2, g.Calendar.Week);
        Assert.Equal(0, g.CurrentPlayerIndex);
    }

    [Fact]
    public void TravellingSpendsHoursAndMovesThePlayer()
    {
        var g = new Game(new MidRandom());
        g.StartTurn();
        var before = g.Clock.HoursRemaining;

        Assert.True(g.TravelTo(LocationId.ZMart));
        Assert.Equal(LocationId.ZMart, g.Current.Location);
        Assert.True(g.Clock.HoursRemaining < before);
    }

    [Fact]
    public void TheRentOfficeCanBeVisitedOutsideRentWeekAndStillCharges()
    {
        // WAS `TheRentOfficeCannotBeVisitedOutsideRentWeek`, which asserted an invented
        // rule: `Place::cue` (`room1.sc:174-209`) initialises the destination script
        // unconditionally and a closed building only skips `openDoor:`. You walk in, you
        // see the shut-office picture, and you pay the 2 hours like anyone else.
        var g = new Game(new MidRandom());
        g.StartTurn();

        var before = g.Clock.HoursRemaining;
        Assert.False(Board.IsOpen(LocationId.RentOffice, g.Calendar.Week, g.Current));

        Assert.True(g.TravelTo(LocationId.RentOffice));
        Assert.Equal(LocationId.RentOffice, g.Current.Location);
        Assert.True(g.Clock.HoursRemaining <= before - GameClock.EnterLocationCost);
    }

    [Fact]
    public void BuyingADurableCostsCashAndAddsItToNetWorth()
    {
        var g = new Game(new MidRandom());
        g.StartTurn();
        g.Current.Cash = 5000;

        var tv = Catalogue.SocketCity.Single(i => i.Name == "Color TV");
        Assert.True(g.Buy(tv));

        Assert.True(g.Current.Durables.Holds(ItemIds.ColorTV));
        Assert.True(g.Current.Cash < 5000);
    }

    [Fact]
    public void ZMartGoodsAreFlaggedAsFlimsier()
    {
        var g = new Game(new MidRandom());
        g.StartTurn();
        g.Current.Cash = 5000;

        g.Buy(Catalogue.ZMart.Single(i => i.Name == "Color TV"));
        var item = g.Current.Durables.At(ItemIds.ColorTV)!;

        Assert.True(item.Attributes.HasFlag(DurableAttributes.CheapBuild));
    }

    [Fact]
    public void JunkTakesYourMoneyAndIsNeverStored()
    {
        var g = new Game(new MidRandom());
        g.StartTurn();
        var before = g.Current.Cash;

        // Happiness is clamped 0..100 by `proc0_13` (`Main.sc:1102-1111`), so this has to
        // start above zero for a penalty to be observable at all. It used to start at 0 and
        // the test asserted -2, which the port's unclamped field was happy to produce.
        g.Current.HapStat = 50;
        var happyBefore = g.Current.HapStat;

        g.Buy(Catalogue.ZMart.Single(i => i.Name == "Works of Capote"));

        Assert.True(g.Current.Cash < before);
        Assert.Equal(happyBefore - 2, g.Current.HapStat);
        Assert.Equal(0, g.Current.Durables.Count);
    }

    [Fact]
    public void PawningReturnsFortyPercentAndCostsHappiness()
    {
        var g = new Game(new MidRandom());
        g.StartTurn();
        g.Current.Cash = 5000;
        g.Buy(Catalogue.SocketCity.Single(i => i.Name == "Color TV"));

        var happyBefore = g.Current.HapStat;
        var offer = g.Pawn(ItemIds.ColorTV);

        Assert.True(offer > 0);
        Assert.Equal(happyBefore - 1, g.Current.HapStat);
        Assert.False(g.Current.Durables.Holds(ItemIds.ColorTV));
    }

    [Fact]
    public void DebtsCountAgainstTheWealthGoalImmediately()
    {
        var g = new Game(new MidRandom());
        g.StartTurn();
        var before = g.Current.MonStat;

        // A loan fills the pocket but does not make you wealthier: liquid assets net off
        // the debt immediately.
        g.Current.Wage = 20;              // give him borrowing capacity
        var borrowed = Bank.TakeLoan(g.Current);
        g.Current.RecalculateGoals(g.Economy);

        Assert.True(borrowed > 0);
        Assert.Equal(before, g.Current.MonStat);
    }

    [Fact]
    public void InvestmentsAreValuedAgainstTheirOwnIndex()
    {
        var g = new Game(new MidRandom());
        g.StartTurn();
        g.Current.Cash = 10000;

        var bought = Broker.Buy(g.Current, Instrument.Gold, 5, g.Economy);
        Assert.Equal(5, bought);
        Assert.Equal(5, g.Current.Holdings.SharesOf(Instrument.Gold));

        var proceeds = Broker.Sell(g.Current, Instrument.Gold, 5, g.Economy);
        Assert.True(proceeds > 0);
        Assert.Equal(0, g.Current.Holdings.SharesOf(Instrument.Gold));
    }

    [Fact]
    public void TBillsNeverMoveInPrice()
    {
        var g = new Game(new MidRandom());
        for (var i = 0; i < 30; i++) { g.StartTurn(); g.EndTurn(); }

        Assert.Equal(100, Holdings.UnitPrice(Instrument.TBills, g.Economy));
    }

    [Fact]
    public void WinningIsDetectedAtTurnStart()
    {
        var g = new Game(new MidRandom());
        var p = g.Players[0];
        p.MonGoal = p.HapGoal = p.EduGoal = p.CarGoal = 10;
        p.HapStat = 100;
        p.Cash = 100000;
        p.Wage = 25;
        p.Dependibility = 100;
        p.RecalculateCareerStat();
        foreach (var d in Degrees.All) p.Education.Receive(d.Id, 10).UnitsToGraduate = 10;

        g.StartTurn();

        Assert.Contains(g.LastTurnEvents, e => e is TurnStartEvent.Won);
        Assert.Contains(p, g.Winners);
    }

    [Fact]
    public void AFullYearOfTurnsRunsWithoutBlowingUp()
    {
        // Smoke test: 52 weeks of the whole loop with the economy ticking.
        var g = new Game(new MidRandom(), playerCount: 4);

        for (var week = 0; week < 52; week++)
        for (var player = 0; player < 4; player++)
        {
            g.StartTurn();
            g.TravelTo(LocationId.ZMart);
            g.EndTurn();
        }

        Assert.Equal(53, g.Calendar.Week);
        Assert.InRange(g.Economy.Main.Reading, 70, 190);
    }
}
