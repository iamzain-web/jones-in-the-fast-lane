using Jones.Core;
using Jones.Core.Economy;
using Jones.Core.Model;
using Jones.Core.Sci;
using Xunit;

namespace Jones.Tests;

/// <summary>
/// `WhereShouldIGo` (script 300), rule by rule.
///
/// Every test drives the real decision function and asserts on <c>Reason</c> — global403,
/// the original's own rule number — so a test failure names the rule that moved rather than
/// just "he went somewhere else". <see cref="ScriptedRandom"/> is used throughout: it throws
/// when a draw falls outside the range asked for and when more draws are taken than were
/// scripted, which is what proves a branch is dead rather than merely unvisited.
///
/// The goods index is 100 in every test, where <c>Pricing.Price</c> is the identity, so
/// clothing costs 73 / 125 / 295 and food costs 55 at Black's Market or 65 at Monolith.
/// </summary>
public class JonesAiTests
{
    /// <summary>The port's `worksAt` encoding: Workplace + 1 (`Employment.cs:108`).</summary>
    private static int WorksAtFactory => (int)Workplace.Factory + 1;

    private static Player Jones(Action<Player>? tweak = null)
    {
        var p = new Player { Playing = true, IsJones = true };
        p.Init();                       // $200, 3 weeks of rent at $325, 6 casual outfits
        p.Wage = 10;                    // employed by default, so rule 1 does not swallow everything
        p.BaseWage = 10;
        p.WorksAt = WorksAtFactory;
        tweak?.Invoke(p);
        return p;
    }

    private static JonesWorld World(Action<JonesWorld>? tweak = null)
    {
        var w = new JonesWorld
        {
            GoodsIndex = 100,
            InvestIndex = 100,
            MainTrend = 0,
            Week = 1,                   // NOT rent week, so the rent block is off by default
            TicksPerHour = 14,          // marble moveSpeed 1, `room1.sc:1084`
            CurrentPlace = PlaceNum.LowCostHousing,

            // global402 defaults to SET here, which is not the original's turn-start value.
            // It is simply that rules 1, 28, 29 and 30 all sit on `not global402` and would
            // otherwise swallow every test aimed at a rule below them. The four tests that
            // target those rules clear it explicitly.
            UsedEmploymentOffice = true,
        };
        tweak?.Invoke(w);
        return w;
    }

    /// <summary>Gives him a meal in hand, which switches off the whole food block.</summary>
    private static void Feed(Player p) => p.Consumables.Receive(ItemIds.AstroChicken, 1);

    /// <summary>Leaves him with one week of clothing, the "running out" state.</summary>
    private static void Threadbare(Player p) => p.Consumables.At(ItemIds.CasualClothes)!.Quantity = 1;

    private static void Naked(Player p) => p.Consumables.At(ItemIds.CasualClothes)!.Quantity = 0;

    /// <summary>A durable worth pawning: 40% of $1000 at index 100 is $400.</summary>
    private static void GiveSomethingToPawn(Player p, int price = 1000) =>
        p.Durables.Receive(ItemIds.ColorTV, 1).PricePaid = price;

    /// <summary>
    /// A PAWNED durable: bits 3-4 set and a redemption price, as `pawnShop.sc:687` leaves it.
    /// </summary>
    private static void GiveAPawnTicket(Player p, int redemption = 50)
    {
        var item = p.Durables.Receive(ItemIds.Vcr, 0);
        item.Attributes |= DurableAttributes.WearMask;
        item.RedemptionPrice = redemption;
    }

    private static JonesDecision Decide(Player p, JonesWorld w, params int[] rolls) =>
        JonesAi.Decide(p, w, new ScriptedRandom(rolls));

    // ==================================================================
    // Rule 1 — a job comes before everything
    // ==================================================================

    [Fact]
    public void WithNoJobHeGoesStraightToTheEmploymentOffice()
    {
        var p = Jones(x => { x.Wage = 0; x.WorksAt = 0; });
        var d = Decide(p, World(w => w.UsedEmploymentOffice = false));

        Assert.Equal(1, d.Reason);
        Assert.Equal(LocationId.EmploymentOffice, d.Destination);
        Assert.Equal(JonesIntent.ApplyForJob, d.Intent);
        Assert.True(d.WantsTo(JonesIntent.ApplyForJob));
    }

    [Fact]
    public void HeDoesNotGoBackToTheEmploymentOfficeTwiceInATurn()
    {
        var p = Jones(x => { x.Wage = 0; x.WorksAt = 0; });
        Feed(p);

        var d = Decide(p, World(w => w.UsedEmploymentOffice = true));

        Assert.NotEqual(1, d.Reason);
    }

    // ==================================================================
    // The rent block — only in the fourth week, or after an extension
    // ==================================================================

    private static JonesWorld RentWeek() => World(w => w.Week = 4);

    [Fact]
    public void RentAndFoodInOneTripAndTheRentOfficeIsTheNearerStop()
    {
        var p = Jones(x => x.Cash = 400);            // 325 rent + 65 for a meal
        var d = Decide(p, RentWeek());

        Assert.Equal(2, d.Reason);
        Assert.Equal(JonesIntent.PayRent, d.Intent);
        Assert.Equal(JonesIntent.BuyFood, d.Intent2);

        // The Rent Office sits at path index 164 and he is at 1: 163 steps forward, but
        // only 7 backward. The shorter arc is what he costs, so it beats Monolith Burgers
        // at 34 steps ahead.
        Assert.Equal(LocationId.RentOffice, d.Destination);
    }

    [Fact]
    public void RentFoodAndClothesFillAllThreeIntentSlots()
    {
        var p = Jones(x => x.Cash = 500);            // 325 + 73 + 65
        Threadbare(p);

        var d = Decide(p, RentWeek());

        Assert.Equal(3, d.Reason);
        Assert.Equal(JonesIntent.PayRent, d.Intent);
        Assert.Equal(JonesIntent.BuyFood, d.Intent2);
        Assert.Equal(JonesIntent.BuyClothing, d.Intent3);
    }

    [Fact]
    public void RentAndClothesWhenHeAlreadyHasAMeal()
    {
        var p = Jones(x => x.Cash = 400);            // 325 + 73
        Threadbare(p);
        Feed(p);

        var d = Decide(p, RentWeek());

        Assert.Equal(4, d.Reason);
        Assert.Equal(JonesIntent.PayRent, d.Intent);
        Assert.Equal(JonesIntent.BuyClothing, d.Intent2);
    }

    [Fact]
    public void ClothesBeatRentWhenHeCannotAffordBoth()
    {
        var p = Jones(x => x.Cash = 100);            // enough for clothes, not for rent
        Threadbare(p);
        Feed(p);

        var d = Decide(p, RentWeek());

        Assert.Equal(5, d.Reason);
        Assert.Equal(LocationId.QtClothing, d.Destination);
    }

    [Fact]
    public void FoodBeatsRentWhenHeCannotAffordBoth()
    {
        var p = Jones(x => x.Cash = 100);            // 65 for a meal, nothing like 325

        var d = Decide(p, RentWeek());

        Assert.Equal(6, d.Reason);
        Assert.Equal(LocationId.MonolithBurgers, d.Destination);
        Assert.Equal(JonesIntent.BuyFood, d.Intent);
    }

    [Fact]
    public void OtherwiseHeSimplyPaysTheRent()
    {
        var p = Jones(x => x.Cash = 350);
        Feed(p);

        var d = Decide(p, RentWeek());

        Assert.Equal(7, d.Reason);
        Assert.Equal(LocationId.RentOffice, d.Destination);
    }

    [Fact]
    public void ShortOfTheRentHeWorksTheShiftsItTakesRoundedUp()
    {
        var p = Jones(x => x.Cash = 100);            // $225 short, and a shift pays 8 x 10
        Feed(p);

        var d = Decide(p, RentWeek());

        Assert.Equal(8, d.Reason);
        Assert.Equal(LocationId.Factory, d.Destination);
        Assert.Equal(JonesIntent.Work, d.Intent);
        Assert.Equal(3, d.IntentArg);                // 225 / 80 = 2 remainder 65, so 3
    }

    [Fact]
    public void WithNoJobToWorkHeRaisesTheRentAtTheBank()
    {
        var p = Jones(x => { x.Wage = 0; x.WorksAt = 0; x.Cash = 100; x.BankBal = 400; });
        Feed(p);
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, RentWeek());

        Assert.Equal(9, d.Reason);
        Assert.Equal(LocationId.Bank, d.Destination);
        Assert.Equal(JonesIntent.RaiseCash, d.Intent);
        Assert.Equal(325, d.IntentArg);
    }

    [Fact]
    public void WithNothingToRaiseHeAsksForAnExtension()
    {
        // Not dressed for the job, so the "go and work for it" rule is skipped, and nothing
        // in the bank, so the bank rule is too.
        var p = Jones(x => { x.Uniform = ItemIds.BusinessSuit; x.Cash = 100; });
        Feed(p);
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, RentWeek());

        Assert.Equal(10, d.Reason);
        Assert.Equal(LocationId.RentOffice, d.Destination);
        Assert.Equal(JonesIntent.AskRentExtension, d.Intent);
    }

    [Fact]
    public void AndFailingThatHePawnsHisThings()
    {
        // Wage of 1 makes three months' pay ($24) useless against $325, killing rule 10.
        var p = Jones(x => { x.Uniform = ItemIds.BusinessSuit; x.Wage = 1; x.Cash = 100; });
        Feed(p);
        GiveSomethingToPawn(p);
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, RentWeek());

        Assert.Equal(11, d.Reason);
        Assert.Equal(LocationId.PawnShop, d.Destination);
        Assert.Equal(JonesIntent.Pawn, d.Intent);
        Assert.Equal(225, d.IntentArg);              // the shortfall, not the whole rent
    }

    [Fact]
    public void OutsideTheFourthWeekTheWholeRentBlockIsSkipped()
    {
        var p = Jones(x => x.Cash = 400);
        Feed(p);

        var d = Decide(p, World(w => w.Week = 3), 0);

        Assert.NotInRange(d.Reason, 2, 11);
    }

    [Fact]
    public void AnExtensionKeepsTheRentOfficeOpenOutsideTheFourthWeek()
    {
        var p = Jones(x => { x.Cash = 350; x.LeaveOpen = true; });
        Feed(p);

        var d = Decide(p, World(w => w.Week = 3));

        Assert.Equal(7, d.Reason);
        Assert.Equal(LocationId.RentOffice, d.Destination);
    }

    [Fact]
    public void AskingForAnExtensionSetsLeaveOpenAtTurnEnd()
    {
        var p = Jones(x => x.TriedExt = 1);
        p.EndTurn(week: 4);

        Assert.True(p.LeaveOpen);
        Assert.Equal(0, p.TriedExt);
    }

    // ==================================================================
    // The clothing block — it outranks food because it gates earning
    // ==================================================================

    [Fact]
    public void NakedAndBrokeHeRaisesTheClothingMoneyAtTheBank()
    {
        var p = Jones(x => { x.Cash = 10; x.BankBal = 200; });
        Naked(p);
        Feed(p);
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, World());

        Assert.Equal(12, d.Reason);
        Assert.Equal(LocationId.Bank, d.Destination);
        Assert.Equal(73, d.IntentArg);               // casual clothes at index 100
    }

    [Fact]
    public void NakedWithNoSavingsHePawns()
    {
        var p = Jones(x => x.Cash = 10);
        Naked(p);
        Feed(p);
        GiveSomethingToPawn(p);
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, World());

        Assert.Equal(13, d.Reason);
        Assert.Equal(LocationId.PawnShop, d.Destination);
        Assert.Equal(63, d.IntentArg);
    }

    [Fact]
    public void NakedAndSolventHeJustBuysClothes()
    {
        var p = Jones(x => x.Cash = 100);
        Naked(p);
        Feed(p);

        var d = Decide(p, World());

        Assert.Equal(14, d.Reason);
        Assert.Equal(LocationId.QtClothing, d.Destination);
    }

    [Fact]
    public void FoodAndClothesTogetherWhenHeCanAffordBoth()
    {
        var p = Jones(x => x.Cash = 200);            // 65 + 73
        Threadbare(p);

        var d = Decide(p, World());

        Assert.Equal(16, d.Reason);
        Assert.Equal(JonesIntent.BuyFood, d.Intent);
        Assert.Equal(JonesIntent.BuyClothing, d.Intent2);
        Assert.Equal(LocationId.MonolithBurgers, d.Destination);
    }

    [Fact]
    public void ShortOfFoodAndClothesHeWorksForTheDifference()
    {
        var p = Jones(x => x.Cash = 100);            // over 73, under 138
        Threadbare(p);

        var d = Decide(p, World());

        Assert.Equal(17, d.Reason);
        Assert.Equal(JonesIntent.Work, d.Intent);
        Assert.Equal(1, d.IntentArg);                // $38 short: less than one shift, so one
        Assert.Equal(JonesIntent.BuyClothing, d.Intent2);

        // He sets off for the CLOTHES SHOP carrying a work order: the destination is
        // whichever of the workplace and the shop is nearer, not necessarily the one the
        // intent names.
        Assert.Equal(LocationId.QtClothing, d.Destination);
    }

    [Fact]
    public void WithNoJobHeDrawsTheDifferenceAtTheBankInstead()
    {
        var p = Jones(x => { x.Wage = 0; x.WorksAt = 0; x.Cash = 100; x.BankBal = 200; });
        Threadbare(p);
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, World(w => w.UsedEmploymentOffice = true));

        Assert.Equal(18, d.Reason);
        Assert.Equal(JonesIntent.RaiseCash, d.Intent);
        Assert.Equal(138, d.IntentArg);
        Assert.Equal(JonesIntent.BuyClothing, d.Intent2);
    }

    [Fact]
    public void AndWithNoSavingsEitherHePawnsForTheDifference()
    {
        var p = Jones(x => { x.Wage = 0; x.WorksAt = 0; x.Cash = 100; });
        Threadbare(p);
        GiveSomethingToPawn(p);
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, World(w => w.UsedEmploymentOffice = true));

        Assert.Equal(19, d.Reason);
        Assert.Equal(JonesIntent.Pawn, d.Intent);
        Assert.Equal(38, d.IntentArg);
        Assert.Equal(LocationId.PawnShop, d.Destination);
    }

    [Fact]
    public void ClothesAloneWhenHeAlreadyHasAMeal()
    {
        var p = Jones(x => x.Cash = 100);
        Threadbare(p);
        Feed(p);

        var d = Decide(p, World());

        Assert.Equal(20, d.Reason);
        Assert.Equal(LocationId.QtClothing, d.Destination);
    }

    [Fact]
    public void TheSecondCopyOfTheBankAndPawnRulesCarriesTheOriginalsOwn220Numbering()
    {
        var bank = Jones(x => { x.Cash = 10; x.BankBal = 200; });
        Threadbare(bank);
        Feed(bank);
        bank.CalcLiquidAssets(new EconomyState());
        Assert.Equal(220, Decide(bank, World()).Reason);

        var pawn = Jones(x => x.Cash = 10);
        Threadbare(pawn);
        Feed(pawn);
        GiveSomethingToPawn(pawn);
        pawn.CalcLiquidAssets(new EconomyState());
        Assert.Equal(221, Decide(pawn, World()).Reason);
    }

    [Fact]
    public void Rule222IsDeadCodeInTheOriginalAndStaysDeadHere()
    {
        // Its test is word for word rule 20's, and rule 20 has already returned for every
        // state that can reach it. Sweep the space it lives in and prove it never fires.
        foreach (var cash in new[] { 0, 10, 72, 73, 74, 200, 900, 2000 })
        foreach (var weeks in new[] { 0, 1 })
        foreach (var fed in new[] { true, false })
        foreach (var uniform in new[] { ItemIds.CasualClothes, ItemIds.BusinessSuit })
        {
            var p = Jones(x => { x.Cash = cash; x.Uniform = uniform; });
            p.Consumables.At(ItemIds.CasualClothes)!.Quantity = weeks;
            if (fed) Feed(p);
            p.CalcLiquidAssets(new EconomyState());

            // Generous roll supply: this sweep is about reason 222, not about draw counts.
            var d = JonesAi.Decide(p, World(), new ScriptedRandom(0, 0, 0, 0, 0, 0));
            Assert.NotEqual(222, d.Reason);
        }
    }

    // ==================================================================
    // The food block
    // ==================================================================

    [Fact]
    public void HeWorksForTheMealAndSetsNoShoppingIntent()
    {
        var p = Jones(x => x.Cash = 10);

        var d = Decide(p, World());

        Assert.Equal(21, d.Reason);
        Assert.Equal(LocationId.Factory, d.Destination);
        Assert.Equal(JonesIntent.Work, d.Intent);
        Assert.Equal(1, d.IntentArg);

        // QUIRK: the food shop is in the route he costed but NOT in an intent slot. He
        // earns the money and works out the shopping on the next call.
        Assert.False(d.WantsTo(JonesIntent.BuyFood));
    }

    [Fact]
    public void WithNoJobHeDrawsTheMealMoneyAtTheBank()
    {
        var p = Jones(x => { x.Wage = 0; x.WorksAt = 0; x.Cash = 10; x.BankBal = 200; });
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, World(w => w.UsedEmploymentOffice = true));

        Assert.Equal(22, d.Reason);
        Assert.Equal(65, d.IntentArg);
    }

    [Fact]
    public void AndPawnsForItWhenThereIsNothingToDraw()
    {
        var p = Jones(x => { x.Wage = 0; x.WorksAt = 0; x.Cash = 10; });
        GiveSomethingToPawn(p);
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, World(w => w.UsedEmploymentOffice = true));

        Assert.Equal(23, d.Reason);
        Assert.Equal(55, d.IntentArg);
    }

    [Fact]
    public void AFridgeMovesHisShoppingToBlacksMarketAndDropsThePriceToFiftyFive()
    {
        var withFridge = Jones(x => { x.Wage = 0; x.WorksAt = 0; x.Cash = 10; x.BankBal = 200; });
        withFridge.Durables.Receive(ItemIds.Refrigerator, 1).PricePaid = 650;
        withFridge.CalcLiquidAssets(new EconomyState());
        Assert.Equal(55, Decide(withFridge, World(w => w.UsedEmploymentOffice = true)).IntentArg);

        var without = Jones(x => { x.Wage = 0; x.WorksAt = 0; x.Cash = 10; x.BankBal = 200; });
        without.CalcLiquidAssets(new EconomyState());
        Assert.Equal(65, Decide(without, World(w => w.UsedEmploymentOffice = true)).IntentArg);
    }

    [Fact]
    public void FlushAndFedUpHeBanksTheSurplusOnHisWayToWork()
    {
        var p = Jones(x => x.Cash = 2000);
        p.Durables.Receive(ItemIds.Refrigerator, 1).PricePaid = 650;

        var d = Decide(p, World());

        Assert.Equal(24, d.Reason);
        Assert.Equal(JonesIntent.Work, d.Intent);
        Assert.Equal(3, d.IntentArg);
        Assert.Equal(JonesIntent.Deposit, d.Intent2);
    }

    [Fact]
    public void WithNoJobTheSameSurplusJustGoesToTheBank()
    {
        var p = Jones(x => { x.Wage = 0; x.WorksAt = 0; x.Cash = 2000; });
        p.Durables.Receive(ItemIds.Refrigerator, 1).PricePaid = 650;

        var d = Decide(p, World(w => w.UsedEmploymentOffice = true));

        Assert.Equal(224, d.Reason);
        Assert.Equal(LocationId.Bank, d.Destination);
        Assert.Equal(JonesIntent.Deposit, d.Intent);
    }

    [Fact]
    public void AFullDaysWorkAndThenTheFoodShop()
    {
        var p = Jones(x => x.Cash = 200);

        var d = Decide(p, World());

        Assert.Equal(25, d.Reason);
        Assert.Equal(JonesIntent.Work, d.Intent);
        Assert.Equal(3, d.IntentArg);
        Assert.Equal(JonesIntent.BuyFood, d.Intent2);
        Assert.Equal(LocationId.MonolithBurgers, d.Destination);
    }

    [Fact]
    public void WithNoJobHeSimplyGoesShopping()
    {
        var p = Jones(x => { x.Wage = 0; x.WorksAt = 0; x.Cash = 200; });

        var d = Decide(p, World(w => w.UsedEmploymentOffice = true));

        Assert.Equal(225, d.Reason);
        Assert.Equal(LocationId.MonolithBurgers, d.Destination);
    }

    [Fact]
    public void OnceHeHasWorkedTheSurplusStillGoesToTheBank()
    {
        var p = Jones(x => x.Cash = 2000);
        p.Durables.Receive(ItemIds.Refrigerator, 1).PricePaid = 650;

        var d = Decide(p, World(w => w.WorkedThisTurn = true));

        Assert.Equal(26, d.Reason);
        Assert.Equal(LocationId.Bank, d.Destination);
    }

    [Fact]
    public void OnceHeHasWorkedAndHasNoSurplusHeJustBuysFood()
    {
        var p = Jones(x => x.Cash = 200);

        var d = Decide(p, World(w => w.WorkedThisTurn = true));

        Assert.Equal(27, d.Reason);
        Assert.Equal(LocationId.MonolithBurgers, d.Destination);
    }

    // ==================================================================
    // Career
    // ==================================================================

    [Fact]
    public void HeAsksForARaiseWhileHisBaseWageStillBeatsHisPacket()
    {
        var p = Jones(x =>
        {
            x.Cash = 200; x.BaseWage = 20; x.Wage = 10;
            x.Dependibility = 30; x.MinDepend = 10;
        });
        Feed(p);

        var d = Decide(p, World(w => w.UsedEmploymentOffice = false));

        Assert.Equal(28, d.Reason);
        Assert.Equal(LocationId.EmploymentOffice, d.Destination);
        Assert.Equal(JonesIntent.AskForRaise, d.Intent);
    }

    [Fact]
    public void AfterTwoRaisesHeLooksForABetterJobInstead()
    {
        var p = Jones(x =>
        {
            x.Cash = 200; x.Raises = 2; x.BaseWage = 20; x.Wage = 10;
            x.Dependibility = 30; x.MinDepend = 10;
            x.Experience = 10; x.MaxExper = 10;
        });
        Feed(p);

        var d = Decide(p, World(w => w.UsedEmploymentOffice = false));

        Assert.Equal(29, d.Reason);
        Assert.Equal(JonesIntent.ApplyForBetterJob, d.Intent);
    }

    [Fact]
    public void OtherwiseHeGrindsTheShiftsThePromotionNeeds()
    {
        var p = Jones(x =>
        {
            x.Cash = 200; x.BaseWage = 30; x.Wage = 30;
            x.Dependibility = 30; x.MinDepend = 10;
            x.Experience = 5; x.MaxExper = 10;
        });
        Feed(p);

        var d = Decide(p, World(w => w.UsedEmploymentOffice = false));

        Assert.Equal(30, d.Reason);
        Assert.Equal(LocationId.Factory, d.Destination);
        Assert.Equal(5, d.IntentArg);                // maxExper 10 - experience 5
    }

    [Fact]
    public void TheDependabilityGapIsComparedTheWrongWayRoundAndNeverRaisesTheEstimate()
    {
        // Experience is 5 short; dependability is 8 short. Taking the larger gap would ask
        // for 8 shifts. The original's test is `<`, so the bigger figure is discarded and he
        // plans 5 — the port replicates that rather than reading `>`.
        var p = Jones(x =>
        {
            x.Cash = 200; x.BaseWage = 30; x.Wage = 30;
            x.Dependibility = 12; x.MinDepend = 10;
            x.Experience = 5; x.MaxExper = 10;
        });
        Feed(p);

        Assert.Equal(5, Decide(p, World(w => w.UsedEmploymentOffice = false)).IntentArg);
    }

    // ==================================================================
    // The working day
    // ==================================================================

    [Fact]
    public void HeRedeemsAPawnTicketOnHisWayToWork()
    {
        var p = Jones(x => x.Cash = 200);
        Feed(p);
        GiveAPawnTicket(p, redemption: 50);

        var d = Decide(p, World());

        Assert.Equal(31, d.Reason);
        Assert.Equal(JonesIntent.Work, d.Intent);
        Assert.Equal(JonesIntent.Redeem, d.Intent2);
        Assert.Equal(LocationId.PawnShop, d.Destination);
    }

    [Fact]
    public void AtTheRelaxationFloorHeGoesHomeAndDoesNothingElse()
    {
        var p = Jones(x => { x.Cash = 200; x.Relax = 10; });
        Feed(p);

        var d = Decide(p, World());

        Assert.Equal(32, d.Reason);
        Assert.Equal(LocationId.LowCostHousing, d.Destination);
        Assert.Equal(JonesIntent.Relax, d.Intent);
        Assert.Equal(0, d.Intent2);                  // no work folded in: 10 is the doctor line
    }

    [Fact]
    public void BelowSeventeenHeWorksAndThenRelaxes()
    {
        var p = Jones(x => { x.Cash = 200; x.Relax = 15; });
        Feed(p);

        var d = Decide(p, World());

        Assert.Equal(33, d.Reason);
        Assert.Equal(JonesIntent.Work, d.Intent);
        Assert.Equal(JonesIntent.Relax, d.Intent2);
    }

    [Fact]
    public void StudyAfterWorkIsACoinFlipAndTheThirdSlotHoldsANumberNotAnIntent()
    {
        var p = Jones(x => x.Cash = 500);
        Feed(p);

        var d = Decide(p, World(), 1);               // RANDOM 1 of 4: Random(0,1)

        Assert.Equal(34, d.Reason);
        Assert.Equal(JonesIntent.Work, d.Intent);
        Assert.Equal(JonesIntent.Study, d.Intent2);

        // `university.sc:781` subtracts 100 from global410 and derives the lesson count from
        // what is left, so this slot is a travel estimate plus 100, not a code.
        Assert.True(d.Intent3 > 100);
    }

    [Fact]
    public void TheStudyCoinFlipIsTakenEvenWhenHeCannotAffordTheCourse()
    {
        // Under $400, no enrolment in progress: the draw still happens, because the `and`
        // puts Random first and the degree test is inside the branch it guards.
        var p = Jones(x => x.Cash = 300);
        Feed(p);

        var d = Decide(p, World(), 1);

        Assert.Equal(334, d.Reason);
        Assert.Equal(LocationId.Factory, d.Destination);
        Assert.False(d.WantsTo(JonesIntent.Study));
    }

    [Fact]
    public void LosingTheStudyFlipHeShopsForAnApplianceInstead()
    {
        var p = Jones(x => x.Cash = 2000);
        Feed(p);

        var d = Decide(p, World(), 0);               // the study flip comes up 0

        Assert.Equal(35, d.Reason);
        Assert.Equal(JonesIntent.Work, d.Intent);
        Assert.Equal(JonesIntent.BuyAppliance, d.Intent2);
    }

    [Fact]
    public void WithNothingToFoldInHeJustGoesToWork()
    {
        var p = Jones(x => x.Cash = 1000);           // under the $1500 appliance line
        Feed(p);

        var d = Decide(p, World(), 0);

        Assert.Equal(36, d.Reason);
        Assert.Equal(LocationId.Factory, d.Destination);
        Assert.Equal(3, d.IntentArg);
    }

    [Fact]
    public void AnAlreadyHappyJonesWillNotBuyAnAppliance()
    {
        // The only thing an appliance buys is happiness, so meeting the happiness goal
        // switches both appliance rules off (global550).
        var p = Jones(x => { x.Cash = 2000; x.HapStat = 60; x.HapGoal = 50; });
        Feed(p);

        var d = Decide(p, World(), 0);

        Assert.Equal(36, d.Reason);
        Assert.False(d.WantsTo(JonesIntent.BuyAppliance));
    }

    // ==================================================================
    // Discretionary
    // ==================================================================

    [Fact]
    public void TiredAndOffTheClockHeGoesHome()
    {
        var p = Jones(x => { x.Cash = 200; x.Relax = 15; });
        Feed(p);

        var d = Decide(p, World(w => w.WorkedThisTurn = true));

        Assert.Equal(37, d.Reason);
        Assert.Equal(LocationId.LowCostHousing, d.Destination);
    }

    [Fact]
    public void Rule37CostsAPlaceNumberInsteadOfTheJourneyHome()
    {
        // ORIGINAL BUG. `localproc_2` takes one argument and rule 37 passes two, so the time
        // check is handed global400 — a place number — rather than the route. With 59 of the
        // 60 hours gone he still sets off, though the walk plus the door costs three.
        var w = World(x => { x.WorkedThisTurn = true; x.HoursUsed = 59; });
        var p = Jones(x => { x.Cash = 200; x.Relax = 15; x.Raises = 2; x.NotEnoughEd = true; });
        Feed(p);

        Assert.Equal(37, Decide(p, w).Reason);

        // What the check was meant to ask, and the answer it would have given.
        Assert.False(JonesAi.Affordable(w, JonesAi.RouteTicks(w, PlaceNum.LowCostHousing,
                                                                 PlaceNum.LowCostHousing)));
    }

    [Fact]
    public void RestedAndOffTheClockHeRedeemsAPawnTicket()
    {
        var p = Jones(x => x.Cash = 200);
        Feed(p);
        GiveAPawnTicket(p, redemption: 50);

        var d = Decide(p, World(w => w.WorkedThisTurn = true));

        Assert.Equal(38, d.Reason);
        Assert.Equal(LocationId.PawnShop, d.Destination);
        Assert.Equal(JonesIntent.Redeem, d.Intent);
    }

    [Fact]
    public void BooksAreAOneInThreeErrand()
    {
        var p = Jones(x => x.Cash = 1000);
        Feed(p);

        var d = Decide(p, World(w => w.WorkedThisTurn = true), 0);   // RANDOM 2 of 4

        Assert.Equal(39, d.Reason);
        Assert.Equal(LocationId.ZMart, d.Destination);
        Assert.Equal(JonesIntent.ShopZMart, d.Intent);
    }

    [Fact]
    public void HeDoesNotGoForBooksHeAlreadyOwnsButStillSpendsTheDraw()
    {
        var p = Jones(x => x.Cash = 1000);
        Feed(p);
        foreach (var id in ItemIds.ReferenceBooks) p.Durables.Receive(id, 1).PricePaid = 100;

        // Two draws: the books roll, which is taken before the shelf is checked, then the
        // appliance coin flip below it.
        var d = Decide(p, World(w => w.WorkedThisTurn = true), 0, 1);

        Assert.Equal(40, d.Reason);
        Assert.Equal(LocationId.SocketCity, d.Destination);
        Assert.Equal(JonesIntent.BuyAppliance, d.Intent);
    }

    [Fact]
    public void OverFifteenHundredWithAFridgeHeBanksIt()
    {
        var p = Jones(x => x.Cash = 2000);
        Feed(p);
        p.Durables.Receive(ItemIds.Refrigerator, 1).PricePaid = 650;

        // The appliance flip (RANDOM 3) comes up 0; the books roll is skipped because
        // Z-Mart is already spent.
        var d = Decide(p, World(w => { w.WorkedThisTurn = true; w.UsedZMart = true; }), 0);

        Assert.Equal(41, d.Reason);
        Assert.Equal(LocationId.Bank, d.Destination);
        Assert.Equal(JonesIntent.Deposit, d.Intent);
    }

    [Fact]
    public void HeBuysTheMarketOnMomentumAlone()
    {
        // Main trend positive and the investment index still below `80 + 15 * trend`.
        var p = Jones(x => x.Cash = 1000);
        Feed(p);

        var d = Decide(p, World(w =>
        {
            w.WorkedThisTurn = true;
            w.UsedZMart = true;
            w.MainTrend = 3;
            w.InvestIndex = 100;          // under 80 + 45
        }), 0);                           // the appliance flip

        Assert.Equal(42, d.Reason);
        Assert.Equal(LocationId.Bank, d.Destination);
        Assert.Equal(JonesIntent.BuyInvestments, d.Intent2);
        Assert.Equal(0, d.Intent);        // nothing in the first slot: he is already flush
    }

    [Fact]
    public void ShortOfCashHeWithdrawsTenTimesFirst()
    {
        var p = Jones(x => { x.Cash = 700; x.BankBal = 800; });
        Feed(p);
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, World(w =>
        {
            w.WorkedThisTurn = true;
            w.MainTrend = 3;
            w.InvestIndex = 100;
        }));                              // under $800, so neither discretionary draw happens

        Assert.Equal(42, d.Reason);
        Assert.Equal(JonesIntent.Withdraw, d.Intent);
        Assert.Equal(10, d.IntentArg);
        Assert.Equal(JonesIntent.BuyInvestments, d.Intent2);
    }

    [Fact]
    public void AnOverpricedMarketIsNotBought()
    {
        var p = Jones(x => { x.Cash = 700; x.BankBal = 800; });
        Feed(p);
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, World(w =>
        {
            w.WorkedThisTurn = true;
            w.MainTrend = 1;
            w.InvestIndex = 120;          // 80 + 15 is 95, so this is dear
        }), 1);                           // the roll is the university draw below

        Assert.NotEqual(42, d.Reason);
    }

    [Fact]
    public void HeSellsTheLotTheMomentTheTrendDropsBelowMinusOne()
    {
        var econ = new EconomyState();
        var p = Jones(x => x.Cash = 200);
        Feed(p);
        p.Holdings.Add(Instrument.Gold, 1);
        p.CalcLiquidAssets(econ);

        var d = Decide(p, World(w => { w.WorkedThisTurn = true; w.MainTrend = -2; }));

        Assert.Equal(43, d.Reason);
        Assert.Equal(JonesIntent.SellInvestments, d.Intent);
        Assert.Equal(-1, d.IntentArg);               // -1 is "everything"
    }

    [Fact]
    public void TBillsAloneAreNotWorthFleeing()
    {
        // The holding test nets off the T-bills at their flat base price, because they do
        // not move and there is nothing to escape.
        var econ = new EconomyState();
        var p = Jones(x => x.Cash = 200);
        Feed(p);
        p.Holdings.Add(Instrument.TBills, 5);
        p.CalcLiquidAssets(econ);

        var d = Decide(p, World(w => { w.WorkedThisTurn = true; w.MainTrend = -2; }), 0);

        Assert.NotEqual(43, d.Reason);
    }

    [Fact]
    public void UniversityIsAThreeInFourDraw()
    {
        var p = Jones(x => x.Cash = 500);
        Feed(p);

        var d = Decide(p, World(w => w.WorkedThisTurn = true), 1);   // RANDOM 4 of 4

        Assert.Equal(44, d.Reason);
        Assert.Equal(LocationId.HiTechU, d.Destination);
        Assert.Equal(JonesIntent.Study, d.Intent2);
        Assert.Equal(0, d.Intent);
    }

    [Fact]
    public void ACourseAlreadyStartedNeedsNoDrawAtAll()
    {
        // `enrollments > numDegrees` short-circuits the whole money-and-dice clause: he
        // finishes what he started. The empty roll list proves no draw is taken.
        var p = Jones(x => { x.Cash = 100; x.Enrollments = 1; });
        Feed(p);

        var d = Decide(p, World(w => w.WorkedThisTurn = true));

        Assert.Equal(44, d.Reason);
    }

    [Fact]
    public void BeingToldHeIsUnderEducatedOverridesTheDraw()
    {
        var p = Jones(x => { x.Cash = 500; x.NotEnoughEd = true; });
        Feed(p);

        // `notEnoughEd` sits on the far side of an `or` from the draw, so the draw is taken
        // first and its result then ignored.
        var d = Decide(p, World(w => w.WorkedThisTurn = true), 0);

        Assert.Equal(44, d.Reason);
    }

    [Fact]
    public void MeetingMoneyAndCareerButNotEducationSetsNotEnoughEdItself()
    {
        var p = Jones(x =>
        {
            x.Cash = 500;
            x.HapStat = 60; x.HapGoal = 50;
            x.CarStat = 60; x.CarGoal = 50;
            x.MonStat = 60; x.MonGoal = 50;
            x.EduStat = 10; x.EduGoal = 50;
        });
        Feed(p);

        Decide(p, World(w => w.WorkedThisTurn = true), 0);

        Assert.True(p.NotEnoughEd);
    }

    [Fact]
    public void LosingTheUniversityDrawHeGoesToWork()
    {
        var p = Jones(x => x.Cash = 500);
        Feed(p);

        var d = Decide(p, World(w => w.WorkedThisTurn = true), 0);

        Assert.Equal(46, d.Reason);
        Assert.Equal(LocationId.Factory, d.Destination);
        Assert.Equal(3, d.IntentArg);
    }

    [Fact]
    public void StandingInTheUniversityAtDaysEndHeTakesOneMoreLesson()
    {
        var p = Jones(x => { x.Wage = 0; x.WorksAt = 0; x.Cash = 500; });
        Feed(p);

        var d = Decide(p, World(w =>
        {
            w.WorkedThisTurn = true;
            w.UsedEmploymentOffice = true;
            w.CurrentPlace = PlaceNum.HiTechU;
            w.HoursUsed = 54;                        // 54 + 6 reaches the 60-hour wall
        }), 0);

        Assert.Equal(47, d.Reason);
        Assert.Equal(LocationId.HiTechU, d.Destination);
    }

    [Fact]
    public void StandingInHisWorkplaceAtDaysEndHeTakesOneMoreShift()
    {
        // Not dressed for the job, which is what stops rule 46 taking this first; the
        // clothing block falls through because he can neither buy, borrow nor pawn for a suit.
        var p = Jones(x =>
        {
            x.Uniform = ItemIds.BusinessSuit;
            x.Cash = 10; x.Raises = 2; x.NotEnoughEd = true;
        });
        Feed(p);
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, World(w =>
        {
            w.WorkedThisTurn = true;
            w.CurrentPlace = PlaceNum.Factory;
            w.HoursUsed = 54;
        }));

        Assert.Equal(48, d.Reason);
        Assert.Equal(LocationId.Factory, d.Destination);
        Assert.Equal(1, d.IntentArg);                // the original leaves global408 at 1 here
    }

    [Fact]
    public void WithNothingLeftToDoHeGoesHomeAndTakesNoDrawOnTheWay()
    {
        var p = Jones(x =>
        {
            x.Wage = 0; x.WorksAt = 0; x.Cash = 10; x.NotEnoughEd = true;
        });
        Feed(p);
        p.CalcLiquidAssets(new EconomyState());

        // No rolls scripted: ScriptedRandom throws if the walk to the last rule takes one.
        var d = Decide(p, World(w =>
        {
            w.WorkedThisTurn = true;
            w.UsedEmploymentOffice = true;
        }));

        Assert.Equal(49, d.Reason);
        Assert.Equal(LocationId.LowCostHousing, d.Destination);
        Assert.Equal(JonesIntent.Relax, d.Intent);
    }

    [Fact]
    public void LivingAtLeSecurityHeGoesHomeToPlaceTwo()
    {
        var p = Jones(x =>
        {
            x.Wage = 0; x.WorksAt = 0; x.Cash = 10; x.NotEnoughEd = true; x.LivesAt = 1;
        });
        Feed(p);
        p.CalcLiquidAssets(new EconomyState());

        var d = Decide(p, World(w => { w.WorkedThisTurn = true; w.UsedEmploymentOffice = true; }));

        Assert.Equal(LocationId.SecurityApartments, d.Destination);
        Assert.Equal(PlaceNum.SecurityApartments, d.PlaceNum);
    }

    // ==================================================================
    // The travel model, and the defects in it
    // ==================================================================

    [Fact]
    public void ARouteCostsTheShorterArcPlusTwoHoursPerDoor()
    {
        var w = World();

        // Low-Cost Housing is at path index 1, the Rent Office at 164. Forward is 163 steps;
        // backward is 7, and 7 is what he charges himself.
        Assert.Equal(7 + 2 * 14, JonesAi.RouteTicks(w, PlaceNum.LowCostHousing, PlaceNum.RentOffice));

        // Two legs, two doors.
        Assert.Equal(7 + 13 + 4 * 14,
            JonesAi.RouteTicks(w, PlaceNum.LowCostHousing, PlaceNum.RentOffice, PlaceNum.SecurityApartments));
    }

    [Fact]
    public void NearestPicksTheCheapestSingleLeg()
    {
        var w = World();

        Assert.Equal(PlaceNum.RentOffice,
            JonesAi.Nearest(w, PlaceNum.LowCostHousing, PlaceNum.RentOffice, PlaceNum.MonolithBurgers));

        Assert.Equal(PlaceNum.MonolithBurgers,
            JonesAi.Nearest(w, PlaceNum.LowCostHousing, PlaceNum.MonolithBurgers, PlaceNum.QtClothing));
    }

    [Fact]
    public void ThreeStopPlanningAcceptsARouteThatSkipsAStop()
    {
        // ORIGINAL BUG. The permutation guard chains as `i2 != i3 && i3 != i4` and never
        // compares i2 with i4, so Socket City → Hi-Tech U → Socket City is costed as though
        // it were a plan, and at 83 steps it undercuts the cheapest real permutation
        // (Rent Office → Socket City → Hi-Tech U, 88). The figure he acts on is for a trip
        // that never reaches the Rent Office at all.
        var w = World();

        var best = JonesAi.BestOfThree(w, PlaceNum.LowCostHousing,
                                       PlaceNum.HiTechU, PlaceNum.SocketCity, PlaceNum.RentOffice);

        var cheapestRealPlan = JonesAi.RouteTicks(w, PlaceNum.LowCostHousing,
                                                  PlaceNum.RentOffice, PlaceNum.SocketCity, PlaceNum.HiTechU);

        Assert.Equal(83 + 3 * 14 * 2, best);
        Assert.Equal(88 + 3 * 14 * 2, cheapestRealPlan);
        Assert.True(best < cheapestRealPlan);
    }

    [Fact]
    public void TheTravelEstimateOnlyHoldsAtTheDefaultMarbleSpeed()
    {
        // 64 path steps from Low-Cost Housing to the Factory, plus one door.
        var fast = World();                              // moveSpeed 1: 14 ticks to the hour
        var slow = World(w => w.TicksPerHour = 84);       // moveSpeed 6

        var fastTicks = JonesAi.RouteTicks(fast, PlaceNum.LowCostHousing, PlaceNum.Factory);
        var slowTicks = JonesAi.RouteTicks(slow, PlaceNum.LowCostHousing, PlaceNum.Factory);

        Assert.Equal(64 + 28, fastTicks);
        Assert.Equal(64 + 168, slowTicks);

        // Six hours at the default, and the marble really does take about six, so with 54 of
        // the 60 gone the trip does not fit. At the slow setting he believes the same journey
        // costs two, because the steps are divided by a figure six times larger while the
        // true steps-per-hour has not changed — and he sets off.
        Assert.False(JonesAi.Affordable(World(w => w.HoursUsed = 54), fastTicks));
        Assert.True(JonesAi.Affordable(World(w => { w.TicksPerHour = 84; w.HoursUsed = 54; }), slowTicks));
    }

    [Fact]
    public void StandingInThePawnShopHeForgetsWhereHeIs()
    {
        // ORIGINAL BUG. The sanity clamp at line 141 accepts 0 to 11, but the Pawn Shop is
        // place 12, so it is rewritten to Low-Cost Housing and every route is then measured
        // from the wrong end of the board.
        var w = World(x =>
        {
            x.CurrentPlace = PlaceNum.PawnShop;
            x.HoursUsed = 53;
            x.WorkedThisTurn = true;
        });

        // From where he really is, the walk to the Factory does not fit.
        Assert.False(JonesAi.Affordable(w, JonesAi.RouteTicks(w, PlaceNum.PawnShop, PlaceNum.Factory)));

        // From where he thinks he is, it does — and that is the one he acts on.
        Assert.True(JonesAi.Affordable(w, JonesAi.RouteTicks(w, PlaceNum.LowCostHousing, PlaceNum.Factory)));

        var p = Jones(x => x.Cash = 500);
        Feed(p);

        var d = JonesAi.Decide(p, w, new ScriptedRandom());
        Assert.Equal(46, d.Reason);
    }

    [Fact]
    public void PlaceNumbersAndBoardLocationsAgreeBothWays()
    {
        for (var place = 0; place <= 12; place++)
            Assert.Equal(place, JonesAi.PlaceNumOf(JonesAi.LocationOf(place)));
    }
}

/// <summary>
/// The re-plan loop and the joint between it and the game — <see cref="JonesTurn"/>.
/// The decisions themselves are covered above; these are about a turn actually running.
/// </summary>
public class JonesTurnTests
{
    private sealed class MidRandom : IRandomSource
    {
        public int Next(int min, int max) => min + (max - min) / 2;
    }

    private static Game OneJones()
    {
        var g = new Game(new MidRandom());
        g.Players[0].IsJones = true;
        return g;
    }

    [Fact]
    public void TheTurnStartLatchesAreClearedEveryTurn()
    {
        var g = OneJones();
        g.JonesWorld.WorkedThisTurn = true;
        g.JonesWorld.UsedEmploymentOffice = true;
        g.JonesWorld.UsedZMart = true;
        g.JonesWorld.UsedRentOffice = true;
        g.JonesWorld.BoughtAppliance = true;
        g.JonesWorld.BoughtInvestments = true;
        g.JonesWorld.SoldInvestments = true;

        g.StartTurn();

        Assert.False(g.JonesWorld.WorkedThisTurn);
        Assert.False(g.JonesWorld.UsedEmploymentOffice);
        Assert.False(g.JonesWorld.UsedZMart);
        Assert.False(g.JonesWorld.UsedRentOffice);
        Assert.False(g.JonesWorld.BoughtAppliance);
        Assert.False(g.JonesWorld.BoughtInvestments);
        Assert.False(g.JonesWorld.SoldInvestments);
    }

    [Fact]
    public void TheClockAndTheBoardPositionFeedTheDecision()
    {
        var g = OneJones();
        g.StartTurn();
        g.TravelTo(LocationId.ZMart);

        var d = g.DecideForJones();

        Assert.Equal(JonesAi.PlaceNumOf(LocationId.ZMart), g.JonesWorld.CurrentPlace);
        Assert.Equal(GameClock.HoursPerTurn - g.Clock.HoursRemaining, g.JonesWorld.HoursUsed);
        Assert.Equal(g.Economy.Goods.Reading, g.JonesWorld.GoodsIndex);
        Assert.Equal(g.Economy.Main.Index, g.JonesWorld.MainTrend);
        Assert.InRange(d.PlaceNum, 0, 12);
    }

    [Fact]
    public void AJoblessJonesSpendsHisFirstStopAtTheEmploymentOffice()
    {
        var g = OneJones();
        g.StartTurn();

        var stops = JonesTurn.Run(g);

        Assert.NotEmpty(stops);
        Assert.Equal(1, stops[0].Reason);
        Assert.Equal(LocationId.EmploymentOffice, stops[0].Destination);
    }

    [Fact]
    public void AWholeTurnRunsToTheEndOfTheClock()
    {
        var g = OneJones();
        g.StartTurn();

        var stops = JonesTurn.Run(g);

        Assert.NotEmpty(stops);
        Assert.InRange(stops.Count, 1, JonesTurn.MaxStops);
        Assert.True(g.Clock.HoursRemaining < GameClock.HoursPerTurn);
    }

    [Fact]
    public void AYearOfJonesTurnsRunsWithoutBlowingUp()
    {
        var g = OneJones();

        for (var week = 0; week < 52; week++)
        {
            g.StartTurn();
            JonesTurn.Run(g);
            g.EndTurn();
        }

        Assert.Equal(53, g.Calendar.Week);

        // He is not meant to be good, but a year of playing himself should leave him
        // employed and no worse off than he started.
        Assert.InRange(g.Economy.Main.Reading, 70, 190);
    }

    [Fact]
    public void JonesPlaysAgainstAHumanWithoutDisturbingTheirTurn()
    {
        var g = new Game(new MidRandom(), playerCount: 2);
        g.Players[1].IsJones = true;

        // Both fed, and with somewhere to keep it. Turn start now charges hours for going
        // hungry (20, `startTrn.sc:432` → `:1041`) and for the doctor (10, `:464` → `:1035`),
        // and food with no refrigerator spoils into the second of those — so without this
        // the "untouched clock" figure below is 40 or 50 for reasons that have nothing to do
        // with Jones.
        foreach (var pl in g.Players)
        {
            pl.Consumables.Receive(ItemIds.FreshFood, 4);
            pl.Durables.Receive(ItemIds.Refrigerator, 1);
        }

        g.StartTurn();                       // the human, who does nothing
        var humanHours = g.Clock.HoursRemaining;
        g.EndTurn();

        g.StartTurn();                       // Jones
        JonesTurn.Run(g);

        Assert.Equal(GameClock.HoursPerTurn, humanHours);
        Assert.True(g.Clock.HoursRemaining < GameClock.HoursPerTurn);
        Assert.Equal(1, g.CurrentPlayerIndex);
    }
}
