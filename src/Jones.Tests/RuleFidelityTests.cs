using Jones.Core;
using Jones.Core.Economy;
using Jones.Core.Model;
using Jones.Core.Sci;

namespace Jones.Tests;

/// <summary>
/// The rules corrected against `GATES.md`, each pinned to the script that decides it. Every
/// test here exists because the port was enforcing something the source does not, or missing
/// something it does.
/// </summary>
public class RuleFidelityTests
{
    private sealed class MidRandom : IRandomSource
    {
        public int Next(int min, int max) => min + (max - min) / 2;
    }

    private sealed class FixedRandom(int value) : IRandomSource
    {
        public int Next(int min, int max) => value < min ? min : value > max ? max : value;
    }

    /// <summary>Counts the range every roll was taken over, so a scaling factor is visible.</summary>
    private sealed class RecordingRandom : IRandomSource
    {
        public List<(int Min, int Max)> Ranges { get; } = [];

        public int Next(int min, int max)
        {
            Ranges.Add((min, max));
            return min + (max - min) / 2;
        }
    }

    /// <summary>A player who will not trip the turn-start illness chain.</summary>
    private static void FeedAndChill(Player p)
    {
        p.Consumables.Receive(ItemIds.FreshFood, 4);
        p.Durables.Receive(ItemIds.Refrigerator, 1);
    }

    private static Game NewGame(IRandomSource? rng = null, int players = 1)
    {
        var g = new Game(rng ?? new MidRandom(), players);
        foreach (var p in g.Players) FeedAndChill(p);
        g.StartTurn();
        return g;
    }

    // --- 1. Travel -------------------------------------------------------------

    [Fact]
    public void TheClockAndTheMarbleNowAgreeOnEveryJourney()
    {
        // `Board.StepsBetween` used to wrap forward while `MarblePath.Distance` took the
        // shorter arc, so a backward journey was animated one way and billed the other.
        foreach (var a in Board.All)
            foreach (var b in Board.All)
                Assert.Equal(
                    MarblePath.Distance(a.PathIndex, b.PathIndex),
                    Board.StepsBetween(a.Id, b.Id));
    }

    [Fact]
    public void FifteenPathStepsMakeAnHour()
    {
        // `global475 = (marble moveSpeed:) * 14` (`room1.sc:1319`) and the hour ticks on
        // `(> (++ global324) global475)` (`room1.sc:1488`), so the fifteenth step is the
        // one that costs.
        var clock = new GameClock();

        clock.SpendTravelSteps(14);
        Assert.Equal(60, clock.HoursRemaining);

        clock.SpendTravelSteps(1);
        Assert.Equal(59, clock.HoursRemaining);
        Assert.Equal(0, clock.SubHourTicks);
    }

    [Fact]
    public void TheSubHourRemainderCarriesAcrossTheWholeTurn()
    {
        // No per-journey rounding and no floor of 1: `global324` is zeroed only at turn
        // start (`room1.sc:1094`).
        var byOnes = new GameClock();
        for (var i = 0; i < 30; i++) byOnes.SpendTravelSteps(1);

        var inOneGo = new GameClock();
        inOneGo.SpendTravelSteps(30);

        Assert.Equal(inOneGo.HoursUsed, byOnes.HoursUsed);
        Assert.Equal(2, byOnes.HoursUsed);
    }

    // --- 2. The Employment Office ----------------------------------------------

    private static Job Clerk => Jobs.All.First(j => j.Workplace == Workplace.ZMart && j.Title == "Clerk");

    [Fact]
    public void ApplyingForAJobCostsFourHoursWhateverTheAnswer()
    {
        // `employment.sc:78` `visitTime 4`, spent at `:168` BEFORE `qualify:`.
        var hired = NewGame(new FixedRandom(1));                 // roll of 1 always passes
        var before = hired.Clock.HoursRemaining;
        Assert.Equal(ApplicationCode.Hired, hired.ApplyFor(Clerk));
        Assert.Equal(before - GameClock.JobApplicationCost, hired.Clock.HoursRemaining);

        var refused = NewGame(new FixedRandom(100));             // roll of 100 always fails
        var manager = Jobs.All.First(j => j.Workplace == Workplace.Factory && j.Title == "General Mgr.");
        before = refused.Clock.HoursRemaining;
        Assert.Equal(ApplicationCode.Refused, refused.ApplyFor(manager));
        Assert.Equal(before - GameClock.JobApplicationCost, refused.Clock.HoursRemaining);
    }

    [Fact]
    public void TheOfficeIsShutOnceTheWeekIsGone()
    {
        // `employment.sc:167` — `(if (!= global323 60) … else (= global433 5))`, clip 425.
        var g = NewGame(new FixedRandom(1));
        g.Clock.Spend(GameClock.HoursPerTurn);

        Assert.Equal(ApplicationCode.OfficeClosed, g.ApplyFor(Clerk));
        Assert.Equal(0, g.Current.WorksAt);          // and nothing happened
    }

    [Fact]
    public void ReApplyingToYourOwnJobIsARaiseRequestNotARehire()
    {
        // `employment.sc:171-197`. Re-applying used to go through the hire path, which reset
        // `raises` to 0 and handed out +2 experience and +3 happiness every click.
        var g = NewGame(new FixedRandom(1));
        var p = g.Current;
        p.HapStat = 50;

        Assert.Equal(ApplicationCode.Hired, g.ApplyFor(Clerk));
        var experience = p.Experience;
        var happiness = p.HapStat;

        // Wage now equals the board rate, so the answer is global433 1 and nothing moves.
        Assert.Equal(ApplicationCode.AlreadyAtRate, g.ApplyFor(Clerk));
        Assert.Equal(experience, p.Experience);
        Assert.Equal(happiness, p.HapStat);
        Assert.Equal(0, p.Raises);
    }

    [Fact]
    public void AnEqualWageDoesNotBuyARaise()
    {
        // The `(== wage price)` clause was missing entirely, so an equal-wage request fell
        // through to the dependability test and granted a raise that changed nothing.
        var p = new Player { Dependibility = 99 };
        Employment.Hire(p, Clerk, 100);

        Assert.Equal(ApplicationCode.AlreadyAtRate, Employment.AskForRaise(p, Clerk, 100));
        Assert.Equal(0, p.Raises);
    }

    [Fact]
    public void BeingPaidAboveTheBoardRateEndsTheRequestAtOnce()
    {
        // `(> wage price)` → global433 0, clip 420, and nothing else is even tested.
        var p = new Player
        {
            WorksAt = (int)Clerk.Workplace + 1,
            Occupation = Clerk.OccupationId,
            Wage = Pricing.Price(100, Clerk.BaseWage) + 1,
            Dependibility = 99,        // would otherwise sail through the raise test
            HapStat = 50,
        };

        Assert.Equal(ApplicationCode.AlreadyPaidMore, Employment.AskForRaise(p, Clerk, 100));
        Assert.Equal(0, p.Raises);
        Assert.Equal(50, p.HapStat);
    }

    [Fact]
    public void ARaiseIsGrantedOnceDependabilityCatchesUp()
    {
        var p = new Player
        {
            WorksAt = (int)Clerk.Workplace + 1,
            Occupation = Clerk.OccupationId,
            Wage = 1,                         // well under the board rate
            Dependibility = 5,                // under the job's requirement of 10
            HapStat = 50,
        };

        Assert.Equal(ApplicationCode.NotDependableEnough, Employment.AskForRaise(p, Clerk, 100));
        Assert.Equal(1, p.Wage);
        Assert.Equal(50, p.HapStat);

        p.Dependibility = 10;
        Assert.Equal(ApplicationCode.RaiseGranted, Employment.AskForRaise(p, Clerk, 100));
        Assert.Equal(Pricing.Price(100, Clerk.BaseWage), p.Wage);
        Assert.Equal(1, p.Raises);
        Assert.Equal(53, p.HapStat);

        // Each raise raises the bar by 5 (`(+ dependibility (* 5 raises))`).
        p.Wage = 1;
        Assert.Equal(ApplicationCode.NotDependableEnough, Employment.AskForRaise(p, Clerk, 100));
    }

    [Fact]
    public void NeedEdIsClearedWhenTheEducationTestPasses()
    {
        // `employment.sc:94-97` writes both on EVERY qualify:, zero when education passed.
        var p = new Player { NeedEd1 = 7, NeedEd2 = 9 };
        Employment.Qualify(p, Clerk, new FixedRandom(1), new Employment.TurnedDownTracker());

        Assert.Equal(0, p.NeedEd1);
        Assert.Equal(0, p.NeedEd2);
    }

    // --- 3. Turn start ----------------------------------------------------------

    [Fact]
    public void GoingHungryCostsTwentyHours()
    {
        // `startTrn.sc:432` → `:1041`.
        var g = new Game(new MidRandom());       // deliberately unfed
        g.StartTurn();

        Assert.Contains(g.LastTurnEvents, e => e is TurnStartEvent.Starved);
        Assert.Equal(GameClock.HoursPerTurn - GameClock.StarvationCost, g.Clock.HoursRemaining);
    }

    [Fact]
    public void TheDoctorCostsTenHoursAndIsBilledAgainstCash()
    {
        // `startTrn.sc:464` → `:1035` for the hours; `:447-454` uses `proc0_11` (cash) for
        // the cap, not net worth.
        var p = new Player { Cash = 100 };
        p.Consumables.Receive(ItemIds.FreshFood, 4);   // no refrigerator, so it spoils
        var clock = new GameClock();

        var events = TurnStart.Run(p, new FixedRandom(0), clock, week: 2, crashSeverity: 0);

        var visit = Assert.Single(events.OfType<TurnStartEvent.DoctorVisit>());
        Assert.Equal(GameClock.HoursPerTurn - GameClock.DoctorVisitCost, clock.HoursRemaining);

        // Cash was 100, so the cap is 50 and then 50 again; the bill cannot exceed it.
        Assert.InRange(visit.Cost, 30, 50);
    }

    [Fact]
    public void APlayerWithNoCashButPlentyOfFurnitureEscapesTheDoctor()
    {
        // The old net-worth test charged this player as though wealthy.
        var p = new Player { Cash = 0 };
        p.Durables.Receive(ItemIds.HotTub, 1).PricePaid = 1200;
        p.Consumables.Receive(ItemIds.FreshFood, 4);

        var events = TurnStart.Run(p, new FixedRandom(0), new GameClock(), week: 2, crashSeverity: 0);

        Assert.Empty(events.OfType<TurnStartEvent.DoctorVisit>());
    }

    // --- 4. Happiness ------------------------------------------------------------

    [Fact]
    public void HappinessIsClampedToTheHundredPointScale()
    {
        // `proc0_13` (`Main.sc:1102-1111`) clamps on every adjustment, and `HasWon` compares
        // the result against a goal.
        var p = new Player { HapStat = 98 };
        p.HapStat += 10;
        Assert.Equal(100, p.HapStat);

        p.HapStat = 2;
        p.HapStat -= 10;
        Assert.Equal(0, p.HapStat);
    }

    [Fact]
    public void MonStatIsClampedAtBothEnds()
    {
        // `proc0_10` (`Main.sc:1063-1068`). Only the lower bound was here.
        var p = new Player { Cash = 1_000_000 };
        p.RecalculateGoals(new EconomyState());
        Assert.Equal(100, p.MonStat);
    }

    [Fact]
    public void TicketsPayHappinessOncePerTurn()
    {
        // `discount.sc:526-577`, latches global466/467/468, reset at `startTrn.sc:176-178`.
        var g = NewGame();
        g.Current.Cash = 10_000;
        g.Current.HapStat = 50;

        var baseball = Catalogue.ZMart.Single(i => i.Name == "Baseball Tickets");
        Assert.True(g.Buy(baseball));
        Assert.Equal(52, g.Current.HapStat);

        Assert.True(g.Buy(baseball));
        Assert.Equal(52, g.Current.HapStat);   // latched for the rest of the turn

        // A different ticket has its own latch.
        Assert.True(g.Buy(Catalogue.ZMart.Single(i => i.Name == "Theatre Tickets")));
        Assert.Equal(54, g.Current.HapStat);
    }

    [Fact]
    public void TheThreeFoodPacksShareOneLatch()
    {
        // `market.sc:217/250/283` all guard on global470, so the second pack of a turn is
        // worth nothing however large it is.
        var g = NewGame();
        g.Current.Cash = 10_000;
        g.Current.HapStat = 50;

        Assert.True(g.Buy(Catalogue.BlacksMarket.Single(i => i.Name == "Food For 1 Week")));
        Assert.Equal(51, g.Current.HapStat);

        Assert.True(g.Buy(Catalogue.BlacksMarket.Single(i => i.Name == "Food For 4 Weeks")));
        Assert.Equal(51, g.Current.HapStat);
    }

    [Fact]
    public void DrinksAndHotMealsShareALatchEachAndAreNeverStored()
    {
        // `fastFood.sc:200/224` share global472 and `:263/287` share global471. Shakes and
        // colas are `typeOfGoods 3`, so the drink itself never reaches the inventory.
        var g = NewGame();
        g.Current.Cash = 10_000;
        g.Current.HapStat = 50;

        Assert.True(g.Buy(Catalogue.MonolithBurgers.Single(i => i.Name == "Cheeseburgers")));
        Assert.Equal(51, g.Current.HapStat);

        // Astro Chicken is worth 2, but the burger already spent the shared latch.
        Assert.True(g.Buy(Catalogue.MonolithBurgers.Single(i => i.Name == "Astro Chicken")));
        Assert.Equal(51, g.Current.HapStat);

        Assert.True(g.Buy(Catalogue.MonolithBurgers.Single(i => i.Name == "Shakes")));
        Assert.Equal(53, g.Current.HapStat);
        Assert.Null(g.Current.Consumables.At(ItemIds.Shakes));

        Assert.True(g.Buy(Catalogue.MonolithBurgers.Single(i => i.Name == "Colas")));
        Assert.Equal(53, g.Current.HapStat);
        Assert.Null(g.Current.Consumables.At(ItemIds.Colas));
    }

    [Fact]
    public void TheSuitsPayEveryTimeAndOnlyAtQtClothing()
    {
        // `clothing.sc:184-192` (+2) and `:210-218` (+1) have no latch at all. Z-Mart's
        // cheaper versions award nothing (`discount.sc` declares no `doit` for them).
        var g = NewGame();
        g.Current.Cash = 10_000;
        g.Current.HapStat = 50;

        var suit = Catalogue.QtClothing.Single(i => i.Name == "Business Suit");
        Assert.True(g.Buy(suit));
        Assert.Equal(52, g.Current.HapStat);
        Assert.True(g.Buy(suit));
        Assert.Equal(54, g.Current.HapStat);

        Assert.True(g.Buy(Catalogue.ZMart.Single(i => i.Name == "Casual Clothes")));
        Assert.Equal(54, g.Current.HapStat);
    }

    [Fact]
    public void TheLatchesResetWithTheTurn()
    {
        // `startTrn.sc:176-178`.
        var g = NewGame();
        g.Current.Cash = 10_000;
        g.Current.HapStat = 50;

        var tickets = Catalogue.ZMart.Single(i => i.Name == "Baseball Tickets");
        Assert.True(g.Buy(tickets));
        Assert.True(g.Buy(tickets));
        Assert.Equal(52, g.Current.HapStat);

        g.EndTurn();
        g.StartTurn();
        g.Current.HapStat = 50;

        Assert.True(g.Buy(tickets));
        Assert.Equal(52, g.Current.HapStat);
    }

    // --- 5. Rent ------------------------------------------------------------------

    [Fact]
    public void PayingRentBuysTheWeeksLeftInTheMonthAndTouchesNothingElse()
    {
        // `rentOffice.sc:209-222`: `(- 4 (mod global372 4))` weeks, `curRent` not arrears,
        // and neither `rentOwed` nor `turnedOver` is written.
        var g = NewGame();
        var p = g.Current;
        p.Cash = 5000;
        p.RentOwed = 900;
        p.TurnedOver = true;

        var held = p.Consumables.At(ItemIds.LowCostRent)!.Quantity;
        Assert.True(g.PayRent());

        Assert.Equal(5000 - p.CurRent, p.Cash);         // curRent, not the $900 arrears
        Assert.Equal(900, p.RentOwed);                  // arrears untouched
        Assert.True(p.TurnedOver);                      // and so is turnedOver
        Assert.Equal(held + 4 - g.Calendar.Week % 4, p.Consumables.At(ItemIds.LowCostRent)!.Quantity);
    }

    [Fact]
    public void ArrearsAreASeparateButton()
    {
        // `payGarnishment` (`rentOffice.sc:448-473`) — price = rentOwed, and it buys no weeks.
        var g = NewGame();
        var p = g.Current;
        p.Cash = 5000;
        p.RentOwed = 900;

        var held = p.Consumables.At(ItemIds.LowCostRent)!.Quantity;
        Assert.True(g.PayGarnishment());

        Assert.Equal(4100, p.Cash);
        Assert.Equal(0, p.RentOwed);
        Assert.Equal(held, p.Consumables.At(ItemIds.LowCostRent)!.Quantity);
    }

    // --- 6. The bank ---------------------------------------------------------------

    [Fact]
    public void MoneyBankedDuringTheVisitIsOutOfWildWillysReach()
    {
        // `bank.sc:120-133` rolls in `init` and zeroes the cash only after the dialog
        // returns. The port used to empty the pocket on arrival.
        var g = NewGame(new FixedRandom(0));
        var p = g.Current;
        p.Cash = 500;

        for (var w = 0; w < 4; w++) { g.EndTurn(); g.StartTurn(); }   // Willy needs week >= 4
        p.Cash = 500;

        Assert.True(g.TravelTo(LocationId.Bank));
        Assert.True(g.MuggingPending);
        Assert.Equal(500, p.Cash);        // still there while you are inside

        Bank.Deposit(p);                  // $100 to safety
        Assert.True(g.TravelTo(LocationId.ZMart));

        Assert.True(g.LastMugging);
        Assert.Equal(0, p.Cash);
        Assert.Equal(100, p.BankBal);     // he cannot touch the savings
    }

    // --- 7. Relaxing -----------------------------------------------------------------

    [Fact]
    public void RelaxingSpendsTheSixHoursEvenWhenTheWeekIsGone()
    {
        // `lowcost.sc:115-132`: the clock is read first, the hours are spent
        // unconditionally, and only then does the `(== temp1 60)` branch choose.
        var g = NewGame();
        g.Clock.Spend(GameClock.HoursPerTurn);

        var relaxed = false;
        var relaxBefore = g.Current.Relax;

        Assert.False(g.Relax(ref relaxed));
        Assert.Equal(relaxBefore, g.Current.Relax);   // no benefit
        Assert.False(relaxed);
    }

    [Fact]
    public void YouCannotRelaxInAnApartmentYouDoNotLiveIn()
    {
        // `lowcost.sc:49-50` adds the button only when `livesAt == 0`; `security.sc:49-50`
        // only when it is 2.
        var g = NewGame();
        g.Current.Location = LocationId.SecurityApartments;   // lives at Low Cost

        var relaxed = false;
        var before = g.Clock.HoursRemaining;

        Assert.False(g.Relax(ref relaxed));
        Assert.Equal(before, g.Clock.HoursRemaining);   // the button is not even there
    }

    // --- 8. Elsewhere -----------------------------------------------------------------

    [Fact]
    public void ANewspaperCostsTwoHours()
    {
        // `market.sc:333` `visitTime 1` plus the explicit `(gTimeKeep doit: 1)` at `:346`.
        Assert.Equal(2, Catalogue.NewspaperVisitHours);
        Assert.Equal(1, Catalogue.NewspaperFailedVisitHours);
    }

    [Fact]
    public void GraduatingClearsNotEnoughEd()
    {
        // `university.sc:207`. The port only ever set the flag, so a graduate stayed marked
        // as under-educated for ever and `WhereShouldIGo` kept sending Jones back to school.
        var p = new Player { NotEnoughEd = true, Enrollments = 1 };
        var clock = new GameClock();

        StudyOutcome outcome;
        do { clock.Reset(); outcome = University.Study(p, Degrees.JuniorCollege, clock); }
        while (outcome == StudyOutcome.LessonTaken);

        Assert.Equal(StudyOutcome.Graduated, outcome);
        Assert.False(p.NotEnoughEd);
    }

    [Fact]
    public void EnrolmentIsPaidInCashNotNetWorth()
    {
        // `university.sc:471` `(>= (proc0_11) price)`.
        var p = new Player { Cash = 10 };
        p.Durables.Receive(ItemIds.Computer, 1).PricePaid = 600;
        p.RecalculateGoals(new EconomyState());

        Assert.False(University.Enroll(p, 100));
        Assert.Equal(10, p.Cash);
        Assert.Equal(0, p.Enrollments);
    }

    [Fact]
    public void AGarnishedShiftReportsWhatTheLandlordTook()
    {
        // `n108.sc:93-99`. The figure is computed from the un-prorated gross, which is the
        // replicated quirk — this is what actually came out of the pay.
        var p = new Player { Wage = 10, RentOwed = 1000, MinDepend = 0 };
        p.Consumables.Receive(ItemIds.CasualClothes, 4);

        var shift = Employment.Work(p, new GameClock());

        Assert.Equal(40, shift.Garnished);    // half of the gross 80
        Assert.Equal(38, shift.Paid);         // and the $2 that simply vanishes
    }

    // --- 9. The economy (outside GATES.md) ---------------------------------------------

    [Fact]
    public void CrashAndBoomRollsScaleWithThePlayerCount()
    {
        // `economicIndex.sc:215/220` roll `(not (Random 0 (* global374 30)))`, and global374
        // is the PLAYER COUNT (`room1.sc:967`, `select1b.sc:88-142`). The economy ticks once
        // per player TURN, so scaling the range keeps crashes at the same rate per week
        // however many are playing. The port had it pinned at 1.
        foreach (var players in (int[])[1, 2, 3, 4])
        {
            var rng = new RecordingRandom();
            var g = new Game(rng, players);
            Assert.Equal(players, g.Economy.Volatility);

            rng.Ranges.Clear();
            g.Economy.Tick(rng, week: 10);

            Assert.Equal((0, players * 30), rng.Ranges[0]);
        }
    }
}
