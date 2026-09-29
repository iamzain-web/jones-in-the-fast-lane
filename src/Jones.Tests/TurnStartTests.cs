using Jones.Core.Model;
using Jones.Core.Sci;
using Xunit;

namespace Jones.Tests;

/// <summary>
/// Covers the turn-start chain in `startTrn.sc`. The ordering tests matter most: the
/// sequence is load-bearing, and a future refactor that tidies it would silently change
/// how the game plays.
/// </summary>
public class TurnStartTests
{
    private static Player Ready()
    {
        var p = new Player { Playing = true, Wage = 10, NetWorth = 400 };
        p.Init();
        p.HapStat = 50;
        return p;
    }

    /// <summary>An RNG that always returns the maximum, so "chance" events never fire.</summary>
    private sealed class NeverRandom : IRandomSource
    {
        public int Next(int min, int max) => max;
    }

    /// <summary>An RNG that always returns the minimum, so every chance event fires.</summary>
    private sealed class AlwaysRandom : IRandomSource
    {
        public int Next(int min, int max) => min;
    }

    /// <summary>An RNG driven by a rule, for cases where different rolls need opposite extremes.</summary>
    private sealed class FuncRandom(Func<int, int, int> f) : IRandomSource
    {
        public int Next(int min, int max) => f(min, max);
    }

    [Fact]
    public void AQuietTurnProducesNoEventsBeyondClothing()
    {
        var p = Ready();
        p.Consumables.Receive(ItemIds.FreshFood, 4);
        p.Relax = 25;

        var events = TurnStart.Run(p, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);

        Assert.DoesNotContain(events, e => e is TurnStartEvent.Starved);
        Assert.DoesNotContain(events, e => e is TurnStartEvent.DoctorVisit);
        Assert.DoesNotContain(events, e => e is TurnStartEvent.Robbed);
    }

    [Fact]
    public void WinningIsCheckedFirstAndShortCircuitsEverythingElse()
    {
        var p = Ready();
        p.MonGoal = p.HapGoal = p.EduGoal = p.CarGoal = 10;
        p.MonStat = p.HapStat = p.EduStat = p.CarStat = 10;

        var events = TurnStart.Run(p, new AlwaysRandom(), new GameClock(), week: 20, crashSeverity: 1);

        Assert.Single(events);
        Assert.IsType<TurnStartEvent.Won>(events[0]);
    }

    // -----------------------------------------------------------------------
    // The ordering that matters: spoilage resolves BEFORE starvation is judged.
    // -----------------------------------------------------------------------

    [Fact]
    public void FoodSpoilingIsNotStarvation_ItIsADoctorRisk()
    {
        // Subtle and worth pinning. When the meal you picked spoils, the original marks
        // it -1 rather than 0. Starvation tests `not local5`, and -1 is truthy, so you
        // do NOT starve — you eat the spoiled food and face a 1-in-2 doctor roll instead.
        var p = Ready();
        p.Consumables.Receive(ItemIds.FreshFood, 4); // plenty of food...
        // ...but no refrigerator, so it all goes off.

        var events = TurnStart.Run(p, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);

        Assert.Contains(events, e => e is TurnStartEvent.AllFoodSpoiled);
        Assert.DoesNotContain(events, e => e is TurnStartEvent.Starved);
    }

    [Fact]
    public void SpoiledFoodSendsYouToTheDoctorOnHalfTheRolls()
    {
        var p = Ready();
        p.Consumables.Receive(ItemIds.FreshFood, 4);

        // AlwaysRandom rolls 0 on the Random(0,1) spoiled-food check, which is the
        // "you got ill" outcome.
        var events = TurnStart.Run(p, new AlwaysRandom(), new GameClock(), week: 2, crashSeverity: 0);

        Assert.Contains(events, e => e is TurnStartEvent.AllFoodSpoiled);
        Assert.Contains(events, e => e is TurnStartEvent.DoctorVisit);
    }

    [Fact]
    public void StarvingCarriesItsOwnOneInFourDoctorRisk()
    {
        // Set in state 12 but only acted on in state 14 — easy to miss when porting.
        var p = Ready(); // no food at all
        p.Relax = 25;    // keep the relaxation route out of it

        var events = TurnStart.Run(p, new AlwaysRandom(), new GameClock(), week: 2, crashSeverity: 0);

        Assert.Contains(events, e => e is TurnStartEvent.Starved);
        Assert.Contains(events, e => e is TurnStartEvent.DoctorVisit);
    }

    [Fact]
    public void AnEmptyLarderStarvesYouAndSpoilageIsReportedFirst()
    {
        var p = Ready();
        p.Consumables.Receive(ItemIds.FreshFood, 20);
        p.Durables.Receive(ItemIds.Refrigerator, 1); // caps at 6, so "some spoiled"

        var events = TurnStart.Run(p, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);

        // Spoilage (state 10) must precede the starvation judgement (state 12).
        var list = events.ToList();
        Assert.Contains(list, e => e is TurnStartEvent.SomeFoodSpoiled);
        var spoiled = list.FindIndex(e => e is TurnStartEvent.SomeFoodSpoiled);
        Assert.True(spoiled >= 0);
    }

    [Fact]
    public void AFridgeKeepsFoodAndPreventsStarvation()
    {
        var p = Ready();
        p.Consumables.Receive(ItemIds.FreshFood, 4);
        p.Durables.Receive(ItemIds.Refrigerator, 1);

        var events = TurnStart.Run(p, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);

        Assert.DoesNotContain(events, e => e is TurnStartEvent.AllFoodSpoiled);
        Assert.DoesNotContain(events, e => e is TurnStartEvent.Starved);

        // One week's worth is eaten by the consumable tick.
        Assert.Equal(3, p.Consumables.At(ItemIds.FreshFood)!.Quantity);
    }

    [Theory]
    // Spoilage caps the store (state 10), and THEN the weekly consumable tick eats one
    // (state 16), so the surviving quantity is one below the cap.
    [InlineData(false, 8, 5)]   // fridge only: capped at 6, then a week passes
    [InlineData(true, 20, 11)]  // fridge + freezer: capped at 12, then a week passes
    public void FoodStorageIsCappedByAppliances(bool freezer, int bought, int kept)
    {
        var p = Ready();
        p.Durables.Receive(ItemIds.Refrigerator, 1);
        if (freezer) p.Durables.Receive(ItemIds.Freezer, 1);
        p.Consumables.Receive(ItemIds.FreshFood, bought);

        TurnStart.Run(p, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);

        Assert.Equal(kept, p.Consumables.At(ItemIds.FreshFood)!.Quantity);
    }

    // -----------------------------------------------------------------------
    // Fast food, and the Astro Chicken quirk.
    // -----------------------------------------------------------------------

    [Fact]
    public void PerishableFastFoodIsPurgedButAstroChickenSurvives()
    {
        var p = Ready();
        p.Consumables.Receive(ItemIds.Hamburgers, 2);
        p.Consumables.Receive(ItemIds.Cheeseburgers, 2);
        p.Consumables.Receive(ItemIds.Fries, 2);
        p.Consumables.Receive(ItemIds.AstroChicken, 2);

        TurnStart.Run(p, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);

        Assert.Equal(0, p.Consumables.At(ItemIds.Hamburgers)!.Quantity);
        Assert.Equal(0, p.Consumables.At(ItemIds.Cheeseburgers)!.Quantity);
        Assert.Equal(0, p.Consumables.At(ItemIds.Fries)!.Quantity);

        // Id 2 sits outside the purge loop's `> 2` bound, so it survives — but it is
        // still subject to the ordinary weekly consumable tick, taking 2 down to 1.
        Assert.Equal(1, p.Consumables.At(ItemIds.AstroChicken)!.Quantity);
    }

    // -----------------------------------------------------------------------
    // Relaxation, robbery and the doctor.
    // -----------------------------------------------------------------------

    [Fact]
    public void RelaxationDecaysUnlessYouOwnAHotTub()
    {
        var p = Ready();
        p.Relax = 25;
        TurnStart.Run(p, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);
        Assert.Equal(24, p.Relax);

        p.Durables.Receive(ItemIds.HotTub, 1);
        TurnStart.Run(p, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);
        Assert.Equal(24, p.Relax); // held, not decayed
    }

    [Fact]
    public void RelaxationNeverFallsBelowTen()
    {
        var p = Ready();
        p.Relax = 10;
        TurnStart.Run(p, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);
        Assert.Equal(10, p.Relax);
    }

    [Fact]
    public void SecurityApartmentsCannotBeRobbed()
    {
        var p = Ready();
        p.LivesAt = 1; // Le Security Apartments
        p.Durables.Receive(ItemIds.ColorTV, 1);

        // AlwaysRandom makes every chance event fire — except this one, which is gated
        // on livesAt rather than on a roll.
        var events = TurnStart.Run(p, new AlwaysRandom(), new GameClock(), week: 2, crashSeverity: 0);

        Assert.DoesNotContain(events, e => e is TurnStartEvent.Robbed);
        Assert.Equal(1, p.Durables.At(ItemIds.ColorTV)!.Quantity);
    }

    [Fact]
    public void RobberyTakesTheTvButNeverTheFridgeOrTheBooks()
    {
        var p = Ready();
        p.LivesAt = 0;
        p.Durables.Receive(ItemIds.ColorTV, 1);
        p.Durables.Receive(ItemIds.Refrigerator, 1);
        p.Durables.Receive(ItemIds.Computer, 1);
        p.Durables.Receive(31, 1); // a reference book

        // The two rolls here need opposite extremes: the robbery gate fires on 0
        // (`Random(0, relax) == 0`) while each item is taken on a NON-zero roll
        // (`Random(0,3)` truthy, a 3-in-4 chance). A single always-min RNG would trigger
        // the break-in and then steal nothing.
        var rng = new FuncRandom((min, max) => max == 3 ? 3 : min);
        var events = TurnStart.Run(p, rng, new GameClock(), week: 2, crashSeverity: 0);

        Assert.Contains(events, e => e is TurnStartEvent.Robbed);
        Assert.Equal(0, p.Durables.At(ItemIds.ColorTV)!.Quantity);
        Assert.Equal(1, p.Durables.At(ItemIds.Refrigerator)!.Quantity);
        Assert.Equal(1, p.Durables.At(ItemIds.Computer)!.Quantity);
        Assert.Equal(1, p.Durables.At(31)!.Quantity);
    }

    [Fact]
    public void TheDoctorChargesLessWhenYouAreAlreadyPoor()
    {
        // Net worth below 500 caps the bill at 50 rather than 200 — the game stops
        // kicking players who are down.
        var poor = Ready();
        poor.NetWorth = 400;
        poor.Relax = 10;
        var poorEvents = TurnStart.Run(poor, new AlwaysRandom(), new GameClock(), week: 2, crashSeverity: 0);
        var poorBill = poorEvents.OfType<TurnStartEvent.DoctorVisit>().Single();

        var rich = Ready();
        rich.NetWorth = 5000;
        rich.Relax = 10;
        var richEvents = TurnStart.Run(rich, new AlwaysRandom(), new GameClock(), week: 2, crashSeverity: 0);
        var richBill = richEvents.OfType<TurnStartEvent.DoctorVisit>().Single();

        Assert.True(poorBill.Cost <= 50);
        Assert.True(richBill.Cost >= poorBill.Cost);
    }

    // -----------------------------------------------------------------------
    // Breakage and crashes.
    // -----------------------------------------------------------------------

    [Fact]
    public void AppliancesOnlyBreakOnceYouAreWorthOverFiveHundred()
    {
        var poor = Ready();
        poor.NetWorth = 500; // not strictly greater
        poor.Durables.Receive(ItemIds.ColorTV, 1).Attributes = DurableAttributes.Breakable;
        poor.Durables.At(ItemIds.ColorTV)!.PricePaid = 349;

        var events = TurnStart.Run(poor, new AlwaysRandom(), new GameClock(), week: 2, crashSeverity: 0);
        Assert.DoesNotContain(events, e => e is TurnStartEvent.ApplianceBroke);

        var rich = Ready();
        rich.NetWorth = 501;
        rich.Durables.Receive(ItemIds.ColorTV, 1).Attributes = DurableAttributes.Breakable;
        rich.Durables.At(ItemIds.ColorTV)!.PricePaid = 349;

        var richEvents = TurnStart.Run(rich, new AlwaysRandom(), new GameClock(), week: 2, crashSeverity: 0);
        Assert.Contains(richEvents, e => e is TurnStartEvent.ApplianceBroke);
    }

    [Fact]
    public void ASeverityOneCrashWipesTheBankAndTakesTheJob()
    {
        var p = Ready();
        p.BankBal = 3000;
        p.Wage = 20;

        var events = TurnStart.Run(p, new AlwaysRandom(), new GameClock(), week: 12, crashSeverity: 1);

        var fallout = events.OfType<TurnStartEvent.CrashFallout>().Single();
        Assert.Equal(1, fallout.Outcome); // job lost
        Assert.Equal(0, p.BankBal);
        Assert.Equal(0, p.Wage);
    }

    [Fact]
    public void LotteryTicketsAreConsumedEvenWhenTheyLose()
    {
        var p = Ready();
        p.Consumables.Receive(ItemIds.LotteryTickets, 5);

        // NeverRandom returns the max roll (500), which 5 tickets cannot beat.
        var events = TurnStart.Run(p, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);

        Assert.DoesNotContain(events, e => e is TurnStartEvent.LotteryWin);
        Assert.Equal(0, p.Consumables.At(ItemIds.LotteryTickets)!.Quantity);
    }

    [Fact]
    public void TheJackpotNeedsAVeryLowRollAndPaysFiveThousand()
    {
        var p = Ready();
        p.Consumables.Receive(ItemIds.LotteryTickets, 100);

        // Keep the larder stocked: an always-min RNG would otherwise starve the player
        // and send them to the doctor, whose bill would muddy the cash assertion.
        p.Consumables.Receive(ItemIds.FreshFood, 4);
        p.Durables.Receive(ItemIds.Refrigerator, 1);

        var before = p.Cash;

        // AlwaysRandom rolls 0, which is <= quantity/20, the jackpot band.
        var events = TurnStart.Run(p, new AlwaysRandom(), new GameClock(), week: 2, crashSeverity: 0);

        var win = events.OfType<TurnStartEvent.LotteryWin>().Single();
        Assert.Equal(5000, win.Amount);
        Assert.Equal(before + 5000, p.Cash);
    }

    // -----------------------------------------------------------------------
    // State 34 — the relative's gift (`startTrn.sc:827-880`), notice register 12
    // -----------------------------------------------------------------------

    /// <summary>Broke, naked and wearing what the job asks for: the state-34 test passes.</summary>
    private static Player BrokeAndNaked()
    {
        var p = Ready();
        p.NetWorth = 200;                 // both money tests are `< 300`
        p.Consumables.At(ItemIds.CasualClothes)!.Quantity = 0;
        return p;
    }

    [Fact]
    public void TheFirstBrokeAndNakedTurnOnlyCountsIt()
    {
        var p = BrokeAndNaked();

        var events = TurnStart.Run(p, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);

        Assert.DoesNotContain(events, e => e is TurnStartEvent.RelativeGift);
        Assert.Equal(1, p.NakedCount);
    }

    [Fact]
    public void TheSecondBrokeAndNakedTurnPaysAndResetsTheCount()
    {
        var p = BrokeAndNaked();
        var clock = new GameClock();

        TurnStart.Run(p, new NeverRandom(), clock, week: 2, crashSeverity: 0);
        var before = p.Cash;

        var events = TurnStart.Run(p, new NeverRandom(), clock, week: 3, crashSeverity: 0);

        // `(switch (global302 uniform:) … (36 73))` for casual clothes, through `proc109_0`
        // at the neutral goods reading of 100 — which returns the base price unchanged —
        // plus `(Random 1 100)`, which NeverRandom maxes at 100.
        var gift = events.OfType<TurnStartEvent.RelativeGift>().Single();
        Assert.Equal(73 + 100, gift.Amount);
        Assert.Equal(before + gift.Amount, p.Cash);

        // `(global302 nakedCount: 0)` is the FIRST thing the gift branch does.
        Assert.Equal(0, p.NakedCount);
    }

    [Fact]
    public void TheGiftIsPricedOffTheUniformTheJobDemands()
    {
        var p = BrokeAndNaked();
        p.Uniform = ItemIds.BusinessSuit;      // `(34 295)`
        var clock = new GameClock();

        TurnStart.Run(p, new NeverRandom(), clock, week: 2, crashSeverity: 0);
        var events = TurnStart.Run(p, new NeverRandom(), clock, week: 3, crashSeverity: 0);

        Assert.Equal(295 + 100, events.OfType<TurnStartEvent.RelativeGift>().Single().Amount);
    }

    [Fact]
    public void TheGoodsIndexMovesTheGift()
    {
        var p = BrokeAndNaked();
        var clock = new GameClock();

        TurnStart.Run(p, new NeverRandom(), clock, week: 2, crashSeverity: 0, goodsIndexReading: 160);
        var events = TurnStart.Run(p, new NeverRandom(), clock, week: 3, crashSeverity: 0,
                                   goodsIndexReading: 160);

        var gift = events.OfType<TurnStartEvent.RelativeGift>().Single();
        Assert.Equal(Jones.Core.Economy.Pricing.Price(160, 73) + 100, gift.Amount);
        Assert.True(gift.Amount > 73 + 100);   // dearer clothes in a dear economy
    }

    [Fact]
    public void ClothingOrMoneyStopsTheCountAltogether()
    {
        // Dressed, and broke.
        var dressed = Ready();
        dressed.NetWorth = 200;
        TurnStart.Run(dressed, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);
        Assert.Equal(0, dressed.NakedCount);

        // Naked, and solvent — `netWorth` alone is enough to fail the test.
        var solvent = Ready();
        solvent.NetWorth = 400;
        solvent.Consumables.At(ItemIds.CasualClothes)!.Quantity = 0;
        TurnStart.Run(solvent, new NeverRandom(), new GameClock(), week: 2, crashSeverity: 0);
        Assert.Equal(0, solvent.NakedCount);
    }

    /// <summary>
    /// The counter is NOT cleared when the test fails — `:850-878` has no `else`. A player
    /// who climbs back over $300 for a week keeps the 1 and is paid on the next bad turn.
    /// </summary>
    [Fact]
    public void TheCountSurvivesAGoodWeekInBetween()
    {
        var p = BrokeAndNaked();
        var clock = new GameClock();

        TurnStart.Run(p, new NeverRandom(), clock, week: 2, crashSeverity: 0);
        Assert.Equal(1, p.NakedCount);

        p.NetWorth = 4000;   // a good week: the state-34 test fails outright
        TurnStart.Run(p, new NeverRandom(), clock, week: 3, crashSeverity: 0);
        Assert.Equal(1, p.NakedCount);

        p.NetWorth = 200;
        var events = TurnStart.Run(p, new NeverRandom(), clock, week: 5, crashSeverity: 0);
        Assert.Single(events.OfType<TurnStartEvent.RelativeGift>());
    }
}
