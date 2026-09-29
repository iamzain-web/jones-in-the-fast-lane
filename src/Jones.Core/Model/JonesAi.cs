using Jones.Core.Economy;
using Jones.Core.Sci;

namespace Jones.Core.Model;

/// <summary>
/// Place numbers — the `placeNum` property of each `Place` instance in `room1.sc:294-565`,
/// and the currency the AI speaks in. `WhereShouldIGo` returns one of these, compares them
/// against `worksAt` and `livesAt`, and uses them to index `gPlaces`.
///
/// These are NOT the same ordering as <see cref="LocationId"/>, which follows the marble
/// path clockwise. Keeping them separate rather than renumbering the board is deliberate:
/// every threshold in the AI is written in place numbers and must stay legible against the
/// source.
/// </summary>
public static class PlaceNum
{
    public const int LowCostHousing = 0;      // the Place class default, `room1.sc:84`
    public const int RentOffice = 1;
    public const int SecurityApartments = 2;
    public const int BlacksMarket = 3;
    public const int Bank = 4;
    public const int Factory = 5;
    public const int EmploymentOffice = 6;
    public const int HiTechU = 7;
    public const int SocketCity = 8;
    public const int QtClothing = 9;
    public const int MonolithBurgers = 10;
    public const int ZMart = 11;
    public const int PawnShop = 12;
}

/// <summary>
/// The "what do I want to do when I get there" codes the AI writes into global407/409/410.
/// It never performs an action itself: it picks a destination and leaves these behind, and
/// the destination's own script asks `proc0_6` (`Main.sc:979`) whether any of the three
/// slots holds its code. Every value here is confirmed by a `proc0_6` call site.
/// </summary>
public static class JonesIntent
{
    /// <summary>Work a shift. global408 carries the number of sessions. `DialogScript.sc:22`</summary>
    public const int Work = 1;

    /// <summary>Buy food. `fastFood.sc:430`, `market.sc:503`</summary>
    public const int BuyFood = 2;

    /// <summary>Apply for a first job. `employment.sc:581`</summary>
    public const int ApplyForJob = 3;

    /// <summary>Ask for a raise. `employment.sc:653`</summary>
    public const int AskForRaise = 4;

    /// <summary>Apply for a better job. `employment.sc:581`</summary>
    public const int ApplyForBetterJob = 5;

    /// <summary>Relax at home. global408 carries the number of sessions. `lowcost.sc:183`</summary>
    public const int Relax = 6;

    /// <summary>Pay the rent. `rentOffice.sc:570`</summary>
    public const int PayRent = 7;

    /// <summary>Ask for a rent extension. `rentOffice.sc:611`</summary>
    public const int AskRentExtension = 8;

    /// <summary>Pawn things. global408 carries the amount to raise. `pawnShop.sc:957`</summary>
    public const int Pawn = 9;

    /// <summary>Redeem pawned things. `pawnShop.sc:963`</summary>
    public const int Redeem = 10;

    /// <summary>Buy clothing. `clothing.sc:380`, `discount.sc:903`</summary>
    public const int BuyClothing = 11;

    /// <summary>Shop at Z-Mart. `discount.sc:870`</summary>
    public const int ShopZMart = 12;

    /// <summary>Buy a lottery ticket at Black's Market. `market.sc:550`</summary>
    public const int BuyLotteryTicket = 13;

    /// <summary>Buy an appliance at Socket City. `appliance.sc:626`</summary>
    public const int BuyAppliance = 15;

    /// <summary>Study at Hi-Tech U. `university.sc:765`</summary>
    public const int Study = 16;

    /// <summary>
    /// Raise global408 dollars at the bank, by whatever means: withdraw, else sell
    /// investments, else take a loan. `bank.sc:594`
    /// </summary>
    public const int RaiseCash = 17;

    /// <summary>Deposit. `bank.sc:558`</summary>
    public const int Deposit = 18;

    /// <summary>Buy investments. `broker.sc:613`</summary>
    public const int BuyInvestments = 19;

    /// <summary>Sell investments; global408 of -1 means sell the lot. `broker.sc:667`</summary>
    public const int SellInvestments = 20;

    /// <summary>Make a loan payment. `bank.sc:582`</summary>
    public const int PayLoan = 21;

    /// <summary>Withdraw global408 times (in $100 steps). `bank.sc:570`</summary>
    public const int Withdraw = 22;
}

/// <summary>
/// One decision by <see cref="JonesAi"/>: the return value of `WhereShouldIGo::doit` plus
/// the three intent slots it left behind.
/// </summary>
/// <param name="PlaceNum">Raw return value — a <see cref="Model.PlaceNum"/>.</param>
/// <param name="Destination">The same place as a <see cref="LocationId"/>.</param>
/// <param name="Reason">global403. Purely diagnostic in the original; it exists so the
/// SetDebug stepping in `WhereShouldIGo` can report which rule fired, and it is the single
/// most useful thing to assert in a test.</param>
/// <param name="Intent">global407.</param>
/// <param name="IntentArg">global408 — sessions, dollars, or -1 for "sell everything".</param>
/// <param name="Intent2">global409.</param>
/// <param name="Intent3">global410. Usually an intent code, but rule 34 stores a NUMBER
/// here instead (see <see cref="JonesAi"/>).</param>
public sealed record JonesDecision(
    int PlaceNum,
    LocationId Destination,
    int Reason,
    int Intent,
    int IntentArg,
    int Intent2,
    int Intent3)
{
    /// <summary>Port of `proc0_6` (`Main.sc:979`) — does any slot hold this code?</summary>
    public bool WantsTo(int code) => Intent == code || Intent2 == code || Intent3 == code;
}

/// <summary>
/// The globals `WhereShouldIGo` reads that do not live on the <see cref="Player"/>.
/// Gathered into one object rather than scattered through the call so each one keeps its
/// original number in a comment and can be pinned in a test.
/// </summary>
public sealed class JonesWorld
{
    /// <summary>global309 — the goods index reading, which prices everything he buys.</summary>
    public int GoodsIndex { get; set; } = 100;

    /// <summary>global308 — the INVESTMENT index reading.</summary>
    public int InvestIndex { get; set; } = 100;

    /// <summary>global315 — the MAIN index's trend (not its reading). His only market signal.</summary>
    public int MainTrend { get; set; }

    /// <summary>global372 — the week. Only ever tested as `week % 4` (is the rent due).</summary>
    public int Week { get; set; } = 1;

    /// <summary>global323 — hours already used this turn, counting UP to 60 (`room1.sc:1484`).</summary>
    public int HoursUsed { get; set; }

    /// <summary>global324 — the sub-hour tick accumulator (`room1.sc:1488`).</summary>
    public int SubHourTicks { get; set; }

    /// <summary>
    /// global475 — interpreter ticks per game hour, `marble.moveSpeed * 14` (`room1.sc:1319`).
    /// The marble's declared `moveSpeed` is 1 (`room1.sc:1084`), so 14 is the value in a
    /// game where nobody has touched the speed control. The speed menu sets
    /// `moveSpeed = 7 - slider` (`Menu.sc:306`), so a slower marble RAISES this.
    ///
    /// It matters more than it looks — see the travel-estimate defect in <see cref="JonesAi"/>.
    /// </summary>
    public int TicksPerHour { get; set; } = 14;

    /// <summary>global400 — the place he is standing in.</summary>
    public int CurrentPlace { get; set; } = PlaceNum.LowCostHousing;

    /// <summary>
    /// global329 — set at turn start (`startTrn.sc:203`) and cleared by the first work
    /// session (`n108.sc:60`). Inverted here because "has worked" reads better than
    /// "may still work", but every use site restores the original's sense.
    /// </summary>
    public bool WorkedThisTurn { get; set; }

    /// <summary>global402 — he has already used the Employment Office (`employment.sc:296`).</summary>
    public bool UsedEmploymentOffice { get; set; }

    /// <summary>global404 — he has already shopped at Z-Mart (`discount.sc:141`).</summary>
    public bool UsedZMart { get; set; }

    /// <summary>global405 — he has already been to the Rent Office (`rentOffice.sc:61`).</summary>
    public bool UsedRentOffice { get; set; }

    /// <summary>global484 — he has already bought an appliance (`appliance.sc:650`).</summary>
    public bool BoughtAppliance { get; set; }

    /// <summary>global485 — he has already bought investments (`broker.sc:659`).</summary>
    public bool BoughtInvestments { get; set; }

    /// <summary>global503 — he has already sold investments (`broker.sc:762`).</summary>
    public bool SoldInvestments { get; set; }

    /// <summary>
    /// global550 — happiness goal already met. Computed by the AI itself each call and
    /// then read by `fastFood.sc:435` and `discount.sc:923`, so it is exposed rather than
    /// kept local.
    /// </summary>
    public bool HappinessGoalMet { get; private set; }

    /// <summary>
    /// global551 — "money is the only thing left to earn". Computed by the AI each call and
    /// read by `DialogScript.sc:53`, where it buys him a FOURTH work session instead of three.
    /// </summary>
    public bool MoneyIsAllThatIsLeft { get; private set; }

    internal void SetGoalFlags(bool happinessMet, bool moneyOnly)
    {
        HappinessGoalMet = happinessMet;
        MoneyIsAllThatIsLeft = moneyOnly;
    }
}

/// <summary>
/// Jones, the computer opponent — a port of `WhereShouldIGo::doit`, script 300.
///
/// He is not a planner. He is a memoryless, greedy, first-match-wins priority list of 44
/// rules re-evaluated from scratch after every stop (`DialogScript.sc:71` calls back into
/// here the moment a building's dialog closes). There is no lookahead, no memory of what he
/// did last turn, and NO DIFFICULTY SETTING anywhere in the script: Jones plays identically
/// in every game.
///
/// The priority order, top to bottom:
///   get a job → rent → clothing → food → career → work → discretionary → go home.
/// Clothing outranks food because `dressedForWork` gates all earning, and a naked Jones
/// cannot work his way out of anything.
///
/// Four random draws exist in the entire file, and no others:
///   rule 34 study-while-working 1-in-2, rule 39 books 1-in-3,
///   rule 40 appliance 1-in-2, rule 44 university 3-in-4.
///
/// THE TRAVEL-ESTIMATE DEFECT (replicated, not fixed). <see cref="RouteTicks"/> adds two
/// things of different units: raw marble-path steps, and hours converted to interpreter
/// ticks at <see cref="JonesWorld.TicksPerHour"/>. <see cref="Affordable"/> then divides the
/// whole sum by that same figure. The marble advances one path step per `moveSpeed` ticks
/// and an hour is `moveSpeed * 14` ticks, so the true cost of a step is 1/14 hour however
/// fast the marble runs — but Jones charges himself 1/(moveSpeed*14). At the declared
/// `moveSpeed` of 1 those agree exactly; every slower setting makes him underestimate travel
/// by that factor and wander further than his clock can pay for.
/// </summary>
public static class JonesAi
{
    /// <summary>
    /// `(gPlaces at: n) index:` — the marble-path index of a place number. gPlaces is built
    /// by `Place::init` (`room1.sc:103`) in instance-declaration order, which is placeNum
    /// order 0..12, so the list index and the place number are the same thing.
    /// </summary>
    private static int PathIndexOf(int placeNum) =>
        Board.All.Single(l => l.PlaceNum == placeNum).PathIndex;

    /// <summary>The board location a place number names.</summary>
    public static LocationId LocationOf(int placeNum) =>
        Board.All.Single(l => l.PlaceNum == placeNum).Id;

    /// <summary>The place number of a board location.</summary>
    public static int PlaceNumOf(LocationId id) => Board.Get(id).PlaceNum;

    /// <summary>
    /// `worksAt` as a place number.
    ///
    /// The original stores the place number itself (`employment.sc:207` assigns global419,
    /// which each `*Jobs.sc` sets to its own `placeNum`). This port instead stores
    /// `Workplace + 1` (`Employment.cs:108`), and the store and jobs screens read it that
    /// way, so the translation is done here at the AI's boundary rather than by renumbering
    /// a field half the codebase already depends on. 0 still means unemployed.
    /// </summary>
    private static int WorkPlaceNum(Player p) =>
        p.WorksAt == 0 ? 0 : Board.ForWorkplace((Workplace)(p.WorksAt - 1))!.PlaceNum;

    /// <summary>
    /// `livesAt` as a place number. The original stores 0 or 2 — the place numbers
    /// themselves (`room1.sc:1380`) — while this port stores 0 or 1.
    /// </summary>
    private static int HomePlaceNum(Player p) =>
        p.LivesAt == 0 ? PlaceNum.LowCostHousing : PlaceNum.SecurityApartments;

    // ------------------------------------------------------------------
    // The five helper procedures, ported one for one
    // ------------------------------------------------------------------

    /// <summary>
    /// `proc0_11` (`Main.sc:1071`) — spendable cash. The original clamps to 32767 when the
    /// high word is set and to 0 when cash has gone negative, so every affordability test in
    /// the AI is against a 16-bit figure however rich he is.
    /// </summary>
    private static int Cash(Player p) =>
        p.Cash > short.MaxValue ? short.MaxValue : p.Cash < 0 ? 0 : (int)p.Cash;

    /// <summary>
    /// `localproc_0` (line 13) — could he raise this much AT THE BANK? Note the third term:
    /// `lqAss` has already had rent arrears and the loan balance subtracted
    /// (`Player::calcLiquidAssets`), so adding them back asks "what do I hold" rather than
    /// "what am I worth". Jones will happily spend money he owes.
    /// </summary>
    private static bool CanRaise(Player p, int amount) =>
        p.LqAss > short.MaxValue
        || p.LqAss >= amount
        || p.LqAss + p.RentOwed + p.LoanBal >= amount;

    /// <summary>
    /// `localproc_1` (line 23) — cost of a route through the given places, in interpreter
    /// ticks. Two parts: the marble-path distance of each leg by the SHORTER way round the
    /// 170-step ring, plus a flat two hours per leg for walking through the door.
    ///
    /// The `> 85` test is the half-ring: a gap wider than that is quicker the other way.
    /// A gap of exactly 85 is left alone, which is correct — both arcs are 85.
    /// </summary>
    public static int RouteTicks(JonesWorld w, params int[] places)
    {
        var total = 0;

        for (var leg = 0; leg < places.Length - 1; leg++)
        {
            var from = PathIndexOf(places[leg]);
            var to = PathIndexOf(places[leg + 1]);

            var gap = from > to ? from - to : to - from;
            if (gap > 85) gap = MarblePath.StepCount - gap;

            total += gap;
        }

        // Two hours of ticks per door. This term is dimensionally correct; the distance
        // above is not. See the class remarks.
        return total + (places.Length - 1) * w.TicksPerHour * 2;
    }

    /// <summary>
    /// `localproc_2` (line 40) — is there time? Returns the original's 1/0 as a bool.
    /// The hour budget is the same 60 the player gets; Jones gets no allowance.
    /// </summary>
    public static bool Affordable(JonesWorld w, int ticks) =>
        w.HoursUsed + SciMath.Div(w.SubHourTicks + ticks, w.TicksPerHour) < 60;

    /// <summary>
    /// `localproc_3` (line 48) — the cheaper of the two orders in which he could visit two
    /// places.
    /// </summary>
    public static int BestOfTwo(JonesWorld w, int start, int a, int b) =>
        Math.Min(RouteTicks(w, start, a, b), RouteTicks(w, start, b, a));

    /// <summary>
    /// `localproc_4` (line 54) — the cheapest order for THREE stops.
    ///
    /// ORIGINAL BUG, replicated. The guard is `(!= temp2 temp3 temp4)` followed by a
    /// redundant `(!= temp3 temp4)`. SCI's comparison operators chain, so the first form
    /// means `temp2 != temp3 && temp3 != temp4` — it never checks `temp2 != temp4`.
    /// Degenerate routes that visit one stop twice and skip another entirely (A → B → A)
    /// are therefore costed alongside the six real permutations, and being shorter they
    /// often win. The figure that comes back is then an estimate for a trip he is not
    /// going to make. Only rule 24 uses this, so the damage is contained, but the estimate
    /// there is genuinely too low and the port must not quietly repair it.
    /// </summary>
    public static int BestOfThree(JonesWorld w, int start, int a, int b, int c)
    {
        int[] targets = [a, b, c];
        var best = 1000;

        for (var i2 = 0; i2 < 3; i2++)
        for (var i3 = 0; i3 < 3; i3++)
        for (var i4 = 0; i4 < 3; i4++)
        {
            // The missing `i2 != i4` is the bug. Do not add it.
            if (i2 == i3 || i3 == i4) continue;

            var cost = RouteTicks(w, start, targets[i2], targets[i3], targets[i4]);
            if (cost < best) best = cost;
        }

        return best;
    }

    /// <summary>
    /// `localproc_5` (line 85) — of these places, the one nearest <paramref name="start"/>.
    /// The 1000 sentinel is the original's; a single leg can never cost that much (85 steps
    /// plus two hours of ticks), so the uninitialised-result path it guards is unreachable.
    /// </summary>
    public static int Nearest(JonesWorld w, int start, params int[] options)
    {
        var best = 1000;
        var chosen = 0;

        foreach (var option in options)
        {
            var cost = RouteTicks(w, start, option);
            if (cost < best) { chosen = option; best = cost; }
        }

        return chosen;
    }

    /// <summary>
    /// `localproc_6` (line 96) — is there an appliance left to want? True when he has not
    /// already bought one this turn AND is missing at least one of durables 21-29.
    /// </summary>
    private static bool WantsAnAppliance(Player p, JonesWorld w)
    {
        if (w.BoughtAppliance) return false;

        for (var id = 21; id <= 29; id++)
            if (!p.Durables.Holds(id)) return true;

        return false;
    }

    // ------------------------------------------------------------------
    // The decision
    // ------------------------------------------------------------------

    /// <summary>
    /// Port of `WhereShouldIGo::doit` (line 118). Returns where Jones goes and what he means
    /// to do there. He may write <see cref="Player.NotEnoughEd"/> on the way through — the
    /// original does the same (line 138), and on `global302`, which during a Jones turn is
    /// this same player.
    /// </summary>
    public static JonesDecision Decide(Player p, JonesWorld w, IRandomSource rng)
    {
        // --- Preamble (lines 119-238): the derived figures every rule below reads ---

        // global550 / global551. If happiness and career are already banked, then either
        // money is the only thing left (551) or education is (notEnoughEd, which forces
        // the university rules on regardless of how rich he is).
        var happinessMet = p.HapStat >= p.HapGoal;
        var moneyOnly = false;

        if (p.HapStat >= p.HapGoal && p.CarStat >= p.CarGoal)
        {
            if (p.MonStat < p.MonGoal && p.EduStat >= p.EduGoal) moneyOnly = true;
            if (p.MonStat >= p.MonGoal && p.EduStat < p.EduGoal) p.NotEnoughEd = true;
        }

        w.SetGoalFlags(happinessMet, moneyOnly);

        // Line 141. ORIGINAL BUG, replicated: the sanity clamp on global400 accepts 0..11,
        // but the Pawn Shop is place 12. Standing in the Pawn Shop therefore resets his idea
        // of where he is to Low-Cost Housing, and every route he costs from there is measured
        // from the wrong end of the board.
        var here = w.CurrentPlace;
        if (here < 0 || here > 11) here = PlaceNum.LowCostHousing;

        // Line 149. A bank balance too big for 16 bits is reported as a flat 2000, NOT as
        // the real figure — so a very rich Jones underrates his own savings.
        var savings = p.BankBal > short.MaxValue ? 2000 : (int)p.BankBal;

        // Line 156. Owning a refrigerator (durable 21) moves his food shopping from Monolith
        // Burgers to Black's Market — cheaper per week and it keeps.
        var foodPlace = p.Durables.Holds(ItemIds.Refrigerator)
            ? PlaceNum.BlacksMarket
            : PlaceNum.MonolithBurgers;

        // Line 157. The QT Clothing base price of whatever his job demands. A uniform value
        // outside 34-36 falls through the switch to 0, as in the original.
        var clothingBase = p.Uniform switch
        {
            ItemIds.BusinessSuit => 295,
            ItemIds.DressClothes => 125,
            ItemIds.CasualClothes => 73,
            _ => 0,
        };
        var clothingPrice = Pricing.Price(w.GoodsIndex, clothingBase);

        // Line 165. A week of fresh food at Black's Market is 55; a meal at Monolith is 65.
        var foodBase = foodPlace == PlaceNum.BlacksMarket ? 55 : 65;
        var foodPrice = Pricing.Price(w.GoodsIndex, foodBase);

        // Line 167. global411 — does he have food? Any prepared meal in consumables 2-5
        // counts as 1; otherwise it is the number of weeks of fresh food (id 1) he holds.
        var foodHeld = 0;
        if (p.Consumables.Holds(ItemIds.AstroChicken)
            || p.Consumables.Holds(ItemIds.Hamburgers)
            || p.Consumables.Holds(ItemIds.Cheeseburgers)
            || p.Consumables.Holds(ItemIds.Fries))
        {
            foodHeld = 1;
        }
        else
        {
            foodHeld = p.Consumables.At(ItemIds.FreshFood)?.Quantity ?? 0;
        }

        // Line 194. pawnValue = what the shop would give him for everything he owns;
        // redeemTotal / redeemCheapest = the pawn tickets he could buy back.
        var pawnValue = 0;
        var redeemTotal = 0;
        var redeemCheapest = 0;

        foreach (var d in p.Durables.Items)
        {
            // Note: added ONCE per item type, never multiplied by quantity, so a second
            // television is invisible to his own valuation.
            if (d.Quantity != 0)
                pawnValue += SciMath.Div(Pricing.Price(w.GoodsIndex, d.PricePaid) * 4, 10);

            // Bits 3-4 are set when an item is pawned, together with its redemption price
            // (`pawnShop.sc:687`), and count down to zero over the three turns he has to
            // buy it back (`room1.sc:769`).
            if (((int)d.Attributes & (int)DurableAttributes.WearMask) == 0) continue;

            redeemTotal += d.RedemptionPrice;

            if (redeemCheapest == 0) redeemCheapest = redeemTotal;
            else if (d.RedemptionPrice < redeemCheapest) redeemCheapest = d.RedemptionPrice;
        }

        // Line 238. One full work session pays wage * 8, and this is the divisor he uses to
        // turn a shortfall into a number of shifts.
        var sessionPay = 8 * p.Wage;

        var cash = Cash(p);
        var dressed = p.DressedForWork();
        var clothing = p.WeeksOfClothing();
        var work = WorkPlaceNum(p);
        var home = HomePlaceNum(p);

        JonesDecision At(int place, int reason, int intent = 0, int arg = 1, int intent2 = 0, int intent3 = 0)
            => new(place, LocationOf(place), reason, intent, arg, intent2, intent3);

        // Shifts needed to cover a shortfall, rounded UP, never fewer than one
        // (lines 338-349 and the two copies of the same block below).
        int ShiftsFor(int shortfall)
        {
            var shifts = SciMath.Div(shortfall, sessionPay);
            if (shortfall % sessionPay != 0) shifts++;
            return shifts <= 0 ? 1 : shifts;
        }

        // ============================================================
        // Rule 1 (line 242) — no job and the office unused: go and get one.
        // ============================================================
        if (p.Wage == 0 && !w.UsedEmploymentOffice)
            return At(PlaceNum.EmploymentOffice, 1, JonesIntent.ApplyForJob, arg: 3);

        // ============================================================
        // Rent block (line 250). Only when the Rent Office is unvisited this turn AND it is
        // actually open: the fourth week of the month, or any week if an extension he took
        // last month left it open for him (`Player::endTurn`, `room1.sc:806`).
        // ============================================================
        if (!w.UsedRentOffice && (w.Week % 4 == 0 || p.LeaveOpen))
        {
            // Rule 2 (line 258) — rent and food in one trip, clothing still comfortable.
            if (foodHeld == 0 && clothing > 1 && cash >= p.CurRent + foodPrice)
                return At(Nearest(w, here, PlaceNum.RentOffice, foodPlace),
                          2, JonesIntent.PayRent, 2, JonesIntent.BuyFood);

            if (clothing <= 1)
            {
                // Rule 3 (line 276) — rent, food and clothes.
                if (foodHeld == 0 && cash >= p.CurRent + clothingPrice + foodPrice)
                    return At(Nearest(w, here, PlaceNum.RentOffice, foodPlace, PlaceNum.QtClothing),
                              3, JonesIntent.PayRent, 2, JonesIntent.BuyFood, JonesIntent.BuyClothing);

                // Rule 4 (line 293) — rent and clothes; he already has food.
                if (foodHeld != 0 && cash >= p.CurRent + clothingPrice)
                    return At(Nearest(w, here, PlaceNum.RentOffice, PlaceNum.QtClothing),
                              4, JonesIntent.PayRent, 2, JonesIntent.BuyClothing);

                // Rule 5 (line 306) — clothes alone. Note this abandons the rent entirely.
                if (cash >= clothingPrice)
                    return At(PlaceNum.QtClothing, 5, JonesIntent.BuyClothing);
            }

            // Rule 6 (line 315) — rent unaffordable but food is not: eat instead.
            if (foodHeld == 0 && cash < p.CurRent && cash >= foodPrice)
                return At(foodPlace, 6, JonesIntent.BuyFood);

            // Rule 7 (line 328) — just pay the rent.
            if (cash >= p.CurRent)
                return At(PlaceNum.RentOffice, 7, JonesIntent.PayRent);

            // Rule 8 (line 336) — work enough shifts to cover the rent, then pay it.
            // The route costed is here → work → Rent Office, so he only does this when the
            // whole errand fits in the remaining hours.
            if (p.Wage != 0 && dressed)
            {
                var shifts = ShiftsFor(p.CurRent - cash);
                var route = RouteTicks(w, here, work, PlaceNum.RentOffice);

                if (Affordable(w, route + 6 * shifts * w.TicksPerHour))
                    return At(work, 8, JonesIntent.Work, shifts);
            }

            // Rule 9 (line 361) — raise the rent at the bank. The extra two hours are added
            // when his pocket AND his savings together still fall short, because he will
            // have to see the broker or the loan officer as well as the teller.
            var bankRoute = RouteTicks(w, here, PlaceNum.Bank, PlaceNum.RentOffice);
            var bankCost = cash + savings < p.CurRent
                ? bankRoute + 2 * w.TicksPerHour
                : bankRoute;

            if (CanRaise(p, p.CurRent) && Affordable(w, bankCost))
                return At(PlaceNum.Bank, 9, JonesIntent.RaiseCash, p.CurRent);

            // Rule 10 (line 378) — ask for an extension, but only if three months of wages
            // would actually clear it. `triedExt` allows one attempt per turn.
            if (p.TriedExt == 0 && cash + 3 * 8 * p.Wage >= p.CurRent)
                return At(PlaceNum.RentOffice, 10, JonesIntent.AskRentExtension);

            // Rule 11 (line 393) — last resort: pawn enough to cover the rent.
            if (pawnValue + cash >= p.CurRent)
                return At(PlaceNum.PawnShop, 11, JonesIntent.Pawn, p.CurRent - cash);
        }

        // ============================================================
        // Clothing block (line 403). Entered when clothing is running out OR he is not
        // dressed well enough for his job. This outranks food deliberately: without the
        // uniform he cannot work, and without work nothing else recovers.
        // ============================================================
        if (clothing <= 1 || !dressed)
        {
            if (clothing == 0)
            {
                // Rule 12 (line 411) — naked: raise the money at the bank.
                if (cash < clothingPrice && CanRaise(p, clothingPrice))
                    return At(PlaceNum.Bank, 12, JonesIntent.RaiseCash, clothingPrice);

                // Rule 13 (line 424) — naked: pawn for it.
                if (cash < clothingPrice && pawnValue + cash >= clothingPrice)
                    return At(PlaceNum.PawnShop, 13, JonesIntent.Pawn, clothingPrice - cash);

                // Rule 14 (line 437) — naked and solvent: buy clothes.
                if (cash >= clothingPrice)
                    return At(PlaceNum.QtClothing, 14, JonesIntent.BuyClothing);
            }

            // Rule 16 (line 446) — food and clothes in one trip. (The original numbers its
            // reasons 14 then 16; there is no rule 15.)
            if (foodHeld == 0 && cash >= foodPrice + clothingPrice)
                return At(Nearest(w, here, foodPlace, PlaceNum.QtClothing),
                          16, JonesIntent.BuyFood, 1, JonesIntent.BuyClothing);

            // Lines 459-465: dressed for work, no food, and enough for clothes but not for
            // both. Three ways to find the difference, in order of preference.
            if (dressed && foodHeld == 0
                && cash < foodPrice + clothingPrice && cash >= clothingPrice)
            {
                // Rule 17 (line 469) — earn it. `stop` is whichever of the clothing shop and
                // the food shop is nearer his workplace, and `other` is the one left over;
                // both go into the estimate so the full errand is costed.
                if (p.Wage != 0)
                {
                    var stop = Nearest(w, work, PlaceNum.QtClothing, foodPlace);
                    var other = stop == PlaceNum.QtClothing ? foodPlace : PlaceNum.QtClothing;

                    var shifts = ShiftsFor(foodPrice + clothingPrice - cash);
                    var route = RouteTicks(w, here, work, stop, other);

                    if (Affordable(w, route + 6 * shifts * w.TicksPerHour))
                        return At(Nearest(w, here, work, PlaceNum.QtClothing),
                                  17, JonesIntent.Work, shifts, JonesIntent.BuyClothing);
                }

                // Rule 18 (line 515) — draw it at the bank.
                {
                    var stop = Nearest(w, PlaceNum.Bank, PlaceNum.QtClothing, foodPlace);
                    var other = stop == PlaceNum.QtClothing ? foodPlace : PlaceNum.QtClothing;

                    var route = RouteTicks(w, here, PlaceNum.Bank, stop, other);
                    var cost = cash + savings < clothingPrice + foodPrice
                        ? route + 2 * w.TicksPerHour
                        : route;

                    if (Affordable(w, cost) && CanRaise(p, clothingPrice + foodPrice))
                        return At(Nearest(w, here, PlaceNum.Bank, PlaceNum.QtClothing),
                                  18, JonesIntent.RaiseCash, clothingPrice + foodPrice,
                                  JonesIntent.BuyClothing);
                }

                // Rule 19 (line 540) — pawn for it.
                {
                    var stop = Nearest(w, PlaceNum.PawnShop, PlaceNum.QtClothing, foodPlace);
                    var other = stop == PlaceNum.QtClothing ? foodPlace : PlaceNum.QtClothing;

                    var route = RouteTicks(w, here, PlaceNum.PawnShop, stop, other);

                    if (Affordable(w, route) && pawnValue + cash >= clothingPrice + foodPrice)
                        return At(Nearest(w, here, PlaceNum.PawnShop, PlaceNum.QtClothing),
                                  19, JonesIntent.Pawn, clothingPrice + foodPrice - cash,
                                  JonesIntent.BuyClothing);
                }
            }

            // Rule 20 (line 564) — clothes, plainly.
            if (cash >= clothingPrice)
                return At(PlaceNum.QtClothing, 20, JonesIntent.BuyClothing);

            // Rule 220 (line 572) — bank for clothes. Reasons 220/221/222 are the original's
            // own numbering for this second, unconditional copy of the naked-block rules.
            if (cash < clothingPrice && CanRaise(p, clothingPrice))
                return At(PlaceNum.Bank, 220, JonesIntent.RaiseCash, clothingPrice);

            // Rule 221 (line 585) — pawn for clothes.
            if (cash < clothingPrice && pawnValue + cash >= clothingPrice)
                return At(PlaceNum.PawnShop, 221, JonesIntent.Pawn, clothingPrice - cash);

            // Rule 222 (line 598) — DEAD CODE in the original: the test is identical to rule
            // 20 above, which has already returned for every state that reaches it. Kept so
            // the port and the listing line up rule for rule.
            if (cash >= clothingPrice)
                return At(PlaceNum.QtClothing, 222, JonesIntent.BuyClothing);
        }

        // ============================================================
        // Food block (line 607).
        // ============================================================
        if (foodHeld == 0)
        {
            if (cash < foodPrice)
            {
                // Rule 21 (line 615) — work for the price of a meal. Note the destination is
                // his workplace with intent WORK only: no BuyFood intent is set, even though
                // the food shop is in the route he costed. The shopping trip is left to the
                // next call, once he has the money.
                if (p.Wage != 0 && dressed)
                {
                    var shifts = ShiftsFor(foodPrice - cash);
                    var route = RouteTicks(w, here, work, foodPlace);

                    if (Affordable(w, route + 6 * shifts * w.TicksPerHour))
                        return At(work, 21, JonesIntent.Work, shifts);
                }

                // Rule 22 (line 642) — bank for it.
                var bankRoute = RouteTicks(w, here, PlaceNum.Bank, foodPlace);
                var bankCost = cash + savings < foodPrice
                    ? bankRoute + 2 * w.TicksPerHour
                    : bankRoute;

                if (Affordable(w, bankCost) && CanRaise(p, foodPrice))
                    return At(PlaceNum.Bank, 22, JonesIntent.RaiseCash, foodPrice);

                // Rule 23 (line 655) — pawn for it. Unlike the rent and clothing versions
                // this one also demands `pawnValue` be non-zero outright, which is redundant
                // with the sum test that follows it.
                if (Affordable(w, RouteTicks(w, here, PlaceNum.PawnShop, foodPlace))
                    && pawnValue != 0 && pawnValue + cash >= foodPrice)
                    return At(PlaceNum.PawnShop, 23, JonesIntent.Pawn, foodPrice - cash);
            }

            // Rule 24 (line 671) — flush, not yet worked, and shopping at Black's Market:
            // bank the surplus on the way. The one-hour `+= TicksPerHour` is the shopping
            // stop itself; the eighteen hours are three work sessions.
            if (!w.WorkedThisTurn && cash > 1500 && foodPlace == PlaceNum.BlacksMarket)
            {
                var route = p.Wage != 0 && dressed
                    ? BestOfThree(w, here, work, PlaceNum.Bank, foodPlace)
                    : BestOfTwo(w, here, PlaceNum.Bank, foodPlace);

                route += w.TicksPerHour;
                var cost = route + (p.Wage != 0 ? 18 * w.TicksPerHour : 0);

                if (Affordable(w, cost))
                {
                    // The `worksAt != 3` guard stops him from "travelling" to Black's Market
                    // when Black's Market is where he works.
                    if (p.Wage != 0 && dressed && work != PlaceNum.BlacksMarket)
                        return At(Nearest(w, here, work, PlaceNum.Bank),
                                  24, JonesIntent.Work, 3, JonesIntent.Deposit);

                    return At(PlaceNum.Bank, 224, JonesIntent.Deposit);
                }
            }

            // Rule 25 (line 711) — work a full day and buy food.
            if (!w.WorkedThisTurn && cash >= foodPrice)
            {
                var cost = p.Wage != 0 && dressed
                    ? RouteTicks(w, here, work, foodPlace) + 18 * w.TicksPerHour
                    : RouteTicks(w, here, foodPlace);

                if (Affordable(w, cost))
                {
                    if (p.Wage != 0 && dressed)
                        return At(Nearest(w, here, work, foodPlace),
                                  25, JonesIntent.Work, 3, JonesIntent.BuyFood);

                    return At(foodPlace, 225, JonesIntent.BuyFood);
                }
            }

            // Rule 26 (line 747) — bank the surplus. The route is costed to Black's Market
            // (place 3) but the destination returned is the Bank, so the estimate covers a
            // trip he is not taking. Faithful to the source.
            if (foodPlace == PlaceNum.BlacksMarket && cash > 1500
                && Affordable(w, RouteTicks(w, here, PlaceNum.Bank, PlaceNum.BlacksMarket)))
                return At(PlaceNum.Bank, 26, JonesIntent.Deposit);

            // Rule 27 (line 761) — just buy food.
            if (cash >= foodPrice)
                return At(foodPlace, 27, JonesIntent.BuyFood);
        }

        // ============================================================
        // Career (line 770).
        // ============================================================

        // Rule 28 — ask for a raise. Two raises per job is the ceiling, and each one raises
        // the dependability bar by five. He only bothers if the job's base wage, revalued at
        // today's economy, is actually above what he is paid.
        if (p.Raises < 2
            && p.Dependibility >= p.MinDepend + 10 + 5 * p.Raises
            && Pricing.Price(w.GoodsIndex, p.BaseWage) > p.Wage
            && !w.UsedEmploymentOffice)
            return At(PlaceNum.EmploymentOffice, 28, JonesIntent.AskForRaise);

        // Rule 29 (line 787) — look for a better job. `maxExper < 80` keeps him from
        // throwing away a top-tier post, and the wage test is against a flat base of 25.
        if (!p.NotEnoughEd
            && p.Experience >= p.MaxExper
            && p.Dependibility >= p.MinDepend + 10
            && !w.UsedEmploymentOffice
            && p.MaxExper < 80
            && Pricing.Price(w.GoodsIndex, 25) > p.Wage)
            return At(PlaceNum.EmploymentOffice, 29, JonesIntent.ApplyForBetterJob);

        // Rule 30 (line 803) — grind the shifts that a better job will need.
        if (!p.NotEnoughEd)
        {
            var needed = 1;
            if (p.Experience < p.MaxExper) needed = p.MaxExper - p.Experience;

            // ORIGINAL BUG, replicated: this is meant to take the LARGER of the two gaps,
            // but the test is `<`, so a dependability gap only replaces the experience gap
            // when it is SMALLER — which always lowers the estimate. The comparison is the
            // wrong way round and he consistently plans too few shifts.
            if (p.Dependibility < p.MinDepend + 10
                && p.MinDepend + 10 - p.Dependibility < needed)
                needed = p.MinDepend + 10 - p.Dependibility;

            if (!w.UsedEmploymentOffice && p.Wage != 0 && dressed)
            {
                var cost = RouteTicks(w, here, work, PlaceNum.EmploymentOffice)
                         + 6 * needed * w.TicksPerHour
                         + 8 * w.TicksPerHour;

                if (Affordable(w, cost))
                    return At(work, 30, JonesIntent.Work, needed <= 0 ? 1 : needed);
            }
        }

        // ============================================================
        // The working day (line 844). Everything from here assumes he has a job, is dressed
        // for it, and has not yet worked this turn.
        // ============================================================
        if (!w.WorkedThisTurn && p.Wage != 0 && dressed)
        {
            // Rule 31 (line 848) — redeem a pawn ticket on the way to work, unless money is
            // the only goal left (redeeming spends cash for no gain against that goal).
            if (!moneyOnly && redeemTotal != 0 && cash >= redeemCheapest)
            {
                var cost = RouteTicks(w, here, PlaceNum.PawnShop, work)
                         + 18 * w.TicksPerHour;

                if (Affordable(w, cost))
                    return At(Nearest(w, here, work, PlaceNum.PawnShop),
                              31, JonesIntent.Work, 3, JonesIntent.Redeem);
            }

            // Rule 32 (line 868) — relaxation has bottomed out at its floor of 10. Go home
            // and do nothing else; at 10 the doctor roll is live every turn.
            if (p.Relax == 10)
                return At(home, 32, JonesIntent.Relax);

            // Rule 33 (line 876) — below 17, work then relax.
            if (p.Relax < 17)
                return At(Nearest(w, here, work, home),
                          33, JonesIntent.Work, 3, JonesIntent.Relax);

            // Rule 34 (line 888) — RANDOM 1: a coin flip on studying after work. The draw
            // happens BEFORE the time check, so it is consumed even when he has no time.
            {
                var cost = BestOfTwo(w, here, PlaceNum.HiTechU, work) + 18 * w.TicksPerHour;

                if (rng.Next(0, 1) != 0 && Affordable(w, cost))
                {
                    if (p.NumDegrees() != 11
                        && (p.Enrollments > p.NumDegrees() || cash > 400))
                    {
                        // global410 here is NOT an intent code: `university.sc:781` subtracts
                        // 100 from it and derives the lesson count from `(60 - it) / 6`. It
                        // shares a slot with the intent codes all the same, so `proc0_6` can
                        // match it by accident — another defect carried across verbatim.
                        return At(Nearest(w, here, work, PlaceNum.HiTechU),
                                  34, JonesIntent.Work, 3, JonesIntent.Study, cost + 100);
                    }

                    return At(work, 334, JonesIntent.Work, 3);
                }
            }

            // Rule 35 (line 916) — buy an appliance after work. Blocked when happiness is
            // already banked, because the only thing an appliance buys him is happiness.
            {
                var cost = BestOfTwo(w, here, PlaceNum.SocketCity, work) + 18 * w.TicksPerHour;

                if (cash > 1500 && Affordable(w, cost) && WantsAnAppliance(p, w) && !happinessMet)
                    return At(Nearest(w, here, work, PlaceNum.SocketCity),
                              35, JonesIntent.Work, 3, JonesIntent.BuyAppliance);
            }

            // Rule 36 (line 935) — nothing else to fold in: go to work.
            return At(work, 36, JonesIntent.Work, 3);
        }

        // Rule 37 (line 943) — ORIGINAL BUG, replicated. `localproc_2` takes ONE argument and
        // this call passes two, so the time check costs `global400` — a place NUMBER, 0 to 12
        // — instead of the journey home. A number that small always fits, so the guard never
        // fires and Jones goes home to relax whenever relaxation is below 17, however little
        // of the week is left. Compare rule 38 below, which wraps the same call correctly.
        if (p.Relax < 17 && Affordable(w, here))
            return At(home, 37, JonesIntent.Relax);

        // Rule 38 (line 955) — redeem a pawn ticket.
        if (!moneyOnly && redeemTotal != 0 && cash >= redeemCheapest
            && Affordable(w, RouteTicks(w, here, PlaceNum.PawnShop)))
            return At(PlaceNum.PawnShop, 38, JonesIntent.Redeem);

        // Rule 39 (line 969) — RANDOM 2: 1-in-3 to go and buy reference books at Z-Mart,
        // if he is missing any of the three. Owning all three is worth an extra-credit point
        // at the university, which is why this outranks plain shopping.
        if (!moneyOnly && cash > 800 && !w.UsedZMart && rng.Next(0, 2) == 0
            && (!p.Durables.Holds(33) || !p.Durables.Holds(32) || !p.Durables.Holds(31)))
            return At(PlaceNum.ZMart, 39, JonesIntent.ShopZMart);

        // Rule 40 (line 988) — RANDOM 3: a coin flip on an appliance.
        if (cash > 800 && rng.Next(0, 1) != 0 && WantsAnAppliance(p, w) && !happinessMet)
            return At(PlaceNum.SocketCity, 40, JonesIntent.BuyAppliance);

        // Rule 41 (line 1003) — bank the surplus.
        if (cash > 1500 && foodPlace == PlaceNum.BlacksMarket)
            return At(PlaceNum.Bank, 41, JonesIntent.Deposit);

        // Rule 42 (line 1011) — BUY the market. His entire investment thesis: the main trend
        // is positive and the investment index has not caught up with it yet
        // (`invest < 80 + 15 * trend`). Pure momentum, no valuation, no diversification.
        if ((cash > 750 || savings > 750)
            && w.MainTrend > 0
            && w.InvestIndex < 80 + w.MainTrend * 15
            && !w.BoughtInvestments)
        {
            // Under $750 in his pocket he withdraws first — ten times, in $100 steps.
            return cash < 750
                ? At(PlaceNum.Bank, 42, JonesIntent.Withdraw, 10, JonesIntent.BuyInvestments)
                : At(PlaceNum.Bank, 42, intent2: JonesIntent.BuyInvestments);
        }

        // Rule 43 (line 1029) — SELL the market, everything at once (`arg -1`), the moment
        // the main trend drops below -1. The holding test subtracts the T-bills at their flat
        // base price, since T-bills never move and are not what he is fleeing.
        var atRisk = p.InvAss - (long)p.Holdings.SharesOf(Instrument.TBills)
                                * Holdings.BasePrice(Instrument.TBills);

        if (atRisk != 0 && w.MainTrend < -1 && !w.SoldInvestments)
            return At(PlaceNum.Bank, 43, JonesIntent.SellInvestments, -1);

        // Rule 44 (line 1049) — RANDOM 4: university. He goes if he is part-way through a
        // course (`enrollments > numDegrees`), or, with over $400, on a 3-in-4 roll — and
        // unconditionally if an employer has told him he is under-educated.
        if (p.NumDegrees() != 11
            && Affordable(w, RouteTicks(w, here, PlaceNum.HiTechU))
            && (p.Enrollments > p.NumDegrees()
                || (cash > 400 && ((!moneyOnly && rng.Next(0, 3) != 0) || p.NotEnoughEd))))
            return At(PlaceNum.HiTechU, 44, intent2: JonesIntent.Study);

        // Rule 46 (line 1071) — work, if the trip alone fits. (There is no rule 45.)
        if (p.Wage != 0 && dressed && Affordable(w, RouteTicks(w, here, work)))
            return At(work, 46, JonesIntent.Work, 3);

        // Rule 47 (line 1085) — already standing in the university with under six hours left:
        // take a lesson rather than walk anywhere.
        if (here == PlaceNum.HiTechU
            && w.HoursUsed + 6 >= 60
            && p.NumDegrees() != 11
            && (p.Enrollments > p.NumDegrees() || cash > 400)
            && !moneyOnly)
            return At(PlaceNum.HiTechU, 47, intent2: JonesIntent.Study);

        // Rule 48 (line 1103) — same idea at his workplace.
        if (work != 0 && here == work && w.HoursUsed + 6 >= 60)
            return At(work, 48, JonesIntent.Work);

        // Rule 49 (line 1116) — nothing left worth doing: go home and relax.
        return At(home, 49, JonesIntent.Relax);
    }
}
