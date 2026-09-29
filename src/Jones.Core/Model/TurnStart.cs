using Jones.Core.Sci;

namespace Jones.Core.Model;

/// <summary>
/// Port of the turn-start state machine in `startTrn.sc` (script 111). See MECHANICS.md §15
/// for the state-by-state table.
///
/// The ORDER IS LOAD-BEARING and must not be rearranged for tidiness:
///   - spoilage (state 10) resolves BEFORE starvation is judged (state 12), so losing a
///     refrigerator can starve you in the same turn it spoils your food;
///   - the meal is chosen (state 8) BEFORE spoilage, so the food you were going to eat can
///     spoil out from under you — which is exactly what triggers the doctor roll;
///   - the win check (state 0) runs FIRST, so a player always plays the turn they won on.
///
/// This reports events; it renders nothing.
/// </summary>
public static class TurnStart
{
    /// <summary>
    /// Rolls the whole chain for one player. Returns what happened, in order.
    ///
    /// <paramref name="clock"/> is `gTimeKeep`: two of these events cost HOURS, charged by
    /// the notice script that displays them (`startTrn.sc:1032-1042`). Starving costs 20 —
    /// a third of the week, and the strongest incentive in the game to keep food in the
    /// fridge — and the doctor costs 10. Neither was charged at all.
    /// </summary>
    /// <summary>
    /// HOUSE RULE, off by default: skip spoilage, starvation and the doctor in week 1.
    ///
    /// **The original does not do this, and the core must stay faithful by default** — see
    /// <see cref="Player.StartingFoodWeeks"/> for what happened when a deviation was made
    /// the default instead. Each platform head opts in; a head that does not gets Sierra's
    /// game exactly.
    ///
    /// Why it exists: a new player is given no food (`room1.sc:667-683`) and this chain runs
    /// unconditionally on turn one (`room1.sc:1318`), so a fresh game opens by taking 20 of
    /// the week's 60 hours for hunger, plus a 1-in-4 roll for a further 10 at the doctor —
    /// before the player has clicked anything.
    ///
    /// Why it is gated HERE rather than by handing out food: giving a new player food does
    /// not help, because they have no refrigerator either. State 10 then spoils it, costs
    /// 2 happiness, and — since the meal was already chosen at state 8 — marks it `-1`,
    /// which arms the 1-in-2 doctor roll in state 14. The gift merely swaps one punishment
    /// for another and a different accusing notice. The hardship itself has to be skipped.
    ///
    /// Week 1 specifically, because that is the precedent the authors set: the weekend is
    /// skipped in week 1 and nothing else is (`startTrn.sc:205`). From week 2 the player has
    /// had a turn to buy food and a fridge, and every rule applies in full.
    /// </summary>
    public static bool SkipWeekOneHardship { get; set; }

    /// <param name="goodsIndexReading">
    /// `global309`, the GOODS index. Only state 34 reads it, to price the outfit the
    /// relative pays for through <see cref="Economy.Pricing.Price"/> exactly as
    /// `(proc109_0 global309 temp7)` does (`startTrn.sc:869`). 100 is the neutral reading,
    /// so a caller that has no economy to hand gets the base price.
    /// </param>
    public static IReadOnlyList<TurnStartEvent> Run(
        Player p, IRandomSource rng, GameClock clock, int week, int crashSeverity, bool boom = false,
        int goodsIndexReading = 100)
    {
        var events = new List<TurnStartEvent>();

        // See SkipWeekOneHardship. False unless a head has opted in, so the source's
        // behaviour is what the tests measure.
        var spared = SkipWeekOneHardship && week == 1;

        // --- State 0: win check, before anything else can change the standings ---
        if (p.FinStat == 0 &&
            p.MonStat >= p.MonGoal && p.HapStat >= p.HapGoal &&
            p.EduStat >= p.EduGoal && p.CarStat >= p.CarGoal)
        {
            events.Add(new TurnStartEvent.Won());
            return events;
        }

        // --- State 2: lottery. Tickets are consumed every turn, win or lose. ---
        var tickets = p.Consumables.At(ItemIds.LotteryTickets);
        if (tickets is { Quantity: > 0 })
        {
            var roll = rng.Next(0, 500);
            if (tickets.Quantity > roll)
            {
                var prize =
                    roll <= SciMath.Div(tickets.Quantity, 20) ? 5000 :
                    roll <= SciMath.Div(tickets.Quantity, 5) ? 500 :
                    200;

                p.Cash += prize;
                p.HapStat += prize == 5000 ? 10 : 5;
                events.Add(new TurnStartEvent.LotteryWin(prize));
            }
            tickets.Quantity = 0;
        }

        // --- State 4: the computer occasionally earns its keep ---
        if (p.Durables.Holds(ItemIds.Computer) && rng.Next(0, 6) == 0)
        {
            var earned = rng.Next(20, 100);
            p.Cash += earned;
            p.HapStat += 3;
            events.Add(new TurnStartEvent.ComputerIncome(earned));
        }

        // --- State 6: relaxation decay, then Wild Willy ---
        if (!p.Durables.Holds(ItemIds.HotTub))
        {
            p.Relax -= 1;
            if (p.Relax < 10) p.Relax = 10;
        }

        // Robbery chance is 1/(relax + 1): the more rested you are, the safer your home.
        // Le Security Apartments (livesAt 1) are immune.
        if (p.LivesAt == 0 && p.Durables.Count > 0 && rng.Next(0, p.Relax) == 0)
        {
            var taken = new List<int>();
            foreach (var d in p.Durables.Items)
            {
                if (ItemIds.NotStealable.Contains(d.IndexNum)) continue;
                if (rng.Next(0, 3) != 0) // 3-in-4 per eligible item
                {
                    d.Quantity = 0;
                    taken.Add(d.IndexNum);
                }
            }

            if (taken.Count > 0)
            {
                p.HapStat -= 4;
                events.Add(new TurnStartEvent.Robbed(taken));
            }
        }

        // --- State 8: choose the meal. Scans 5 down to 1, first held wins. ---
        var meal = 0;
        for (var id = 5; id >= 1; id--)
        {
            if (p.Consumables.AtHeld(id) is not null) { meal = id; break; }
        }

        // --- State 10: fresh food spoilage ---
        var fresh = p.Consumables.At(ItemIds.FreshFood);
        if (!spared && fresh is { Quantity: > 0 })
        {
            if (!p.Durables.Holds(ItemIds.Refrigerator))
            {
                // The meal itself spoiled. -1 marks it for the doctor roll below.
                if (meal == ItemIds.FreshFood) meal = -1;
                fresh.Quantity = 0;
                p.HapStat -= 2;
                events.Add(new TurnStartEvent.AllFoodSpoiled());
            }
            else if (!p.Durables.Holds(ItemIds.Freezer) && fresh.Quantity > 6)
            {
                fresh.Quantity = 6;
                p.HapStat -= 1;
                events.Add(new TurnStartEvent.SomeFoodSpoiled(6));
            }
            else if (fresh.Quantity > 12)
            {
                fresh.Quantity = 12;
                p.HapStat -= 1;
                events.Add(new TurnStartEvent.SomeFoodSpoiled(12));
            }
        }

        // --- State 12: perishable fast food expires unconditionally, then starvation ---
        // Ids 3–5 only. Astro Chicken (2) is deliberately outside this range — see ItemIds.
        for (var id = ItemIds.PerishableFastFoodLast; id >= ItemIds.PerishableFastFoodFirst; id--)
        {
            var ff = p.Consumables.At(id);
            if (ff is not null) ff.Quantity = 0;
        }

        // The original carries a single "no doctor" flag (global553) from here into state
        // 14. It defaults to 1 (healthy) and gets replaced by a roll on each route in.
        var noDoctor = 1;

        if (meal == 0 && !spared)
        {
            p.HapStat -= 2;

            // `startTrn.sc:432` sets the notice register to 0, and `:1041` charges
            // `(gTimeKeep doit: 20)` when it displays it.
            clock.Spend(GameClock.StarvationCost);
            events.Add(new TurnStartEvent.Starved());

            // Going hungry is itself a 1-in-4 route to the doctor (`startTrn.sc:431`),
            // separate from spoiled food. Easy to miss: the roll is set here in state 12
            // but only acted on in state 14.
            noDoctor = rng.Next(0, 3);
        }

        // --- State 14: the doctor ---
        // Three routes in, each overwriting the flag: starvation (above) at 1-in-4,
        // spoiled food at 1-in-2, bottomed-out relaxation at 1-in-5.
        if (meal == -1) noDoctor = rng.Next(0, 1);
        if (p.Relax == 10 && rng.Next(0, 4) == 0) noDoctor = 0;

        // The bill is capped on CASH, not net worth. `startTrn.sc:447-454` calls `proc0_11`
        // — which returns `cash` (`Main.sc:1071-1082`) — all four times:
        //
        //     (= temp1 200)
        //     (if (< (proc0_11) 500) (= temp1 50))
        //     (if (< (proc0_11) temp1) (= temp1 (proc0_11)))
        //     (if (and (> (proc0_11) 0) (not global553)) …)
        //
        // Using net worth here charged a player with a house full of appliances and $20 in
        // hand as though they were wealthy, and let one with cash but negative net worth
        // skip the doctor entirely.
        if (noDoctor == 0 && p.Cash > 0 && !spared)
        {
            // The cap drops to $50 for players carrying under $500 — the game stops
            // kicking you once you are already down.
            var cap = p.Cash < 500 ? 50 : 200;
            if (p.Cash < cap) cap = (int)p.Cash;

            var cost = cap > 30 ? rng.Next(30, cap) : cap;
            p.Cash -= cost;
            p.HapStat -= 4;

            // `startTrn.sc:464` sets the notice register to 3, and `:1035` charges
            // `(gTimeKeep doit: 10)` when it displays it.
            clock.Spend(GameClock.DoctorVisitCost);
            events.Add(new TurnStartEvent.DoctorVisit(cost));
        }

        // --- State 16: every consumable ticks down by one, THEN rent is checked ---
        //
        // `(consumables eachElementDo: #doit)` and `Consumable::doit` is simply
        // `(if quantity (-- quantity))`. This one line is how a week actually passes:
        // clothing wears out, fresh food is eaten, and prepaid rent weeks run down.
        // Without it nothing ever depletes and the player never faces a bill.
        foreach (var c in p.Consumables.Items)
            if (c.Quantity > 0) c.Quantity--;

        if (week % 4 == 0)
        {
            var rentId = p.LivesAt == 0 ? ItemIds.LowCostRent : ItemIds.SecurityRent;
            var rent = p.Consumables.At(rentId);
            if (rent is { Quantity: 0 })
            {
                p.TurnedOver = false;
                events.Add(new TurnStartEvent.RentDue(p.CurRent));
            }
        }

        // --- State 18: clothing ---
        p.DressedForWork();
        var weeks = p.WeeksOfClothing();
        if (weeks == 0) events.Add(new TurnStartEvent.Naked());
        else if (weeks == 1) events.Add(new TurnStartEvent.ClothingLow());

        // --- State 20: loan ---
        if (week % 4 == 0 && p.LoanBal != 0)
        {
            if (p.PaySched == 0)
            {
                events.Add(new TurnStartEvent.LoanPaymentDemanded());
            }
            else if (p.PaySched < 0)
            {
                p.HapStat -= 1;
                events.Add(new TurnStartEvent.LoanOverdue());
            }
        }

        // --- State 22: appliance breakage, only for players worth over $500 ---
        if (p.NetWorth > 500)
        {
            foreach (var d in p.Durables.Items)
            {
                if (ItemIds.ReferenceBooks.Contains(d.IndexNum)) continue;
                if (!d.Attributes.HasFlag(DurableAttributes.Breakable)) continue;
                if (d.Quantity < 1) continue;

                // Worn-out singletons are skipped; a second copy keeps it eligible.
                var worn = ((int)d.Attributes & (int)DurableAttributes.WearOrWornMask) != 0;
                if (worn && d.Quantity <= 1) continue;

                // Z-Mart builds fail more often: 1-in-36 against 1-in-51.
                var interval = d.Attributes.HasFlag(DurableAttributes.CheapBuild) ? 35 : 50;
                if (rng.Next(0, interval) != 0) continue;

                var cost = rng.Next(SciMath.Div(d.PricePaid, 20), SciMath.Div(d.PricePaid, 4));
                p.Cash -= cost;
                p.HapStat -= 1;
                events.Add(new TurnStartEvent.ApplianceBroke(d.IndexNum, cost));
                break; // the original returns after the first breakage
            }
        }

        // --- State 24: market crash fallout ---
        if (crashSeverity != 0)
        {
            // The base hit scales inversely with severity: -3, -2, -1 for 1, 2, 3.
            p.HapStat -= 4 - crashSeverity;

            // Anyone holding more than $1000 of investments takes a SECOND hit, and a
            // much heavier one in a severity-1 crash. Being in the market is what makes
            // a crash hurt (`startTrn.sc:648`).
            if (p.InvAss > 1000)
                p.HapStat -= crashSeverity switch { 1 => 5, 2 => 2, _ => 1 };

            // IMPORTANT: doScandal runs only for severities 1 and 2 (`<= 1 global373 2`
            // at startTrn.sc:698). A severity-3 crash never costs anyone their job or
            // their wage — it is purely a happiness and market event.
            var outcome = 0;
            if (crashSeverity <= 2)
            {
                outcome = p.DoScandal(crashSeverity, rng);
                if (outcome == 1) p.HapStat -= 7;        // lost the job
                else if (outcome == -1) p.HapStat -= 3;  // wage cut
            }

            events.Add(new TurnStartEvent.CrashFallout(crashSeverity, outcome));
        }
        else if (boom && p.InvAss > 1000)
        {
            // A boom pays out only to players who are actually invested.
            p.HapStat += 5;
            events.Add(new TurnStartEvent.Boom());
        }

        // --- State 34: the relative's gift (`startTrn.sc:827-880`) ---------------------
        //
        // The one notice register the port could not reach. It runs LAST, after the crash
        // fallout of states 24-32, because that is where it sits in the state order.
        //
        // The original opens the state by walking the durables and totalling
        // `(quantity - (if (& attributes $0038) 1 else 0)) * pricePaid` into `temp2`
        // (`:828-849`) — and then never reads `temp2` again. It is dead in the source, not
        // dropped in the port: nothing in states 34-36 uses it, and `changeState`'s other
        // states each assign `temp2` before reading it. Computing it here would change
        // nothing, so it is recorded rather than reproduced.
        //
        // The test itself (`:850-860`) is four conditions: no clothing left, and under $300
        // in BOTH net worth and cash. `netWorthHi` / `cashHi` are the high words of the
        // original's 32-bit-in-two-16-bit-words money; this port keeps both as `long`, so
        // `(not …Hi)` and `(< … 300)` collapse into the single comparison.
        if (p.WeeksOfClothing() == 0 && p.NetWorth < 300 && p.Cash < 300)
        {
            p.NakedCount++;

            // `(if (> nakedCount 1) …)` — the gift lands on the SECOND qualifying turn, and
            // everything below is inside that one `if`. Note what is NOT here: no `else`
            // zeroing the counter when the test fails. See Player.NakedCount.
            if (p.NakedCount > 1)
            {
                p.NakedCount = 0;

                // `(switch (global302 uniform:) (34 295) (35 125) (36 73) (else 50))` —
                // the SHELF price of the outfit the player's JOB demands, which is Q.T.
                // Clothing's own `basePrice` for each (`Catalogue.QtClothing`). The `else`
                // 50 is unreachable in play: `uniform` only ever holds 34, 35 or 36.
                var basePrice = p.Uniform switch
                {
                    ItemIds.BusinessSuit  => 295,
                    ItemIds.DressClothes  => 125,
                    ItemIds.CasualClothes => 73,
                    _                     => 50,
                };

                // `(+ (proc109_0 global309 temp7) (Random 1 100))` — the economy-adjusted
                // price plus a dollar to a hundred, so the gift covers the outfit with a
                // little over. `proc0_10` is the cash adder, which also refreshes monStat.
                var gift = Economy.Pricing.Price(goodsIndexReading, basePrice) + rng.Next(1, 100);

                p.Cash += gift;
                events.Add(new TurnStartEvent.RelativeGift(gift));
            }
        }

        return events;
    }
}
