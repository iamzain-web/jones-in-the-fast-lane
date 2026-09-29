using Jones.Core.Economy;

namespace Jones.Core.Model;

/// <summary>
/// Plays a whole turn for a Jones-controlled player.
///
/// SCOPE, so nobody mistakes this for part of the port of script 300. `WhereShouldIGo`
/// decides one thing — where to go and which intent codes to leave behind — and that is
/// what <see cref="JonesAi"/> is. It performs no actions at all. Carrying out an intent is
/// each destination script's own job: `employment.sc` drives the job icons, `university.sc`
/// picks the course, `bank.sc` works the teller window, and so on. Those scripts are not
/// ported yet.
///
/// This class is the joint between the two. The LOOP is faithful — `DialogScript.sc:71`
/// re-invokes script 300 the instant a building's dialog closes, which is why Jones
/// re-plans after every single stop and never follows a plan. Everything inside
/// <see cref="Perform"/> is a stand-in for an unported destination script, chosen to be the
/// simplest thing that satisfies the intent using APIs this port already has, and every
/// such choice is called out where it is made. None of it is a guess about script 300.
/// </summary>
public static class JonesTurn
{
    /// <summary>
    /// Safety net on the re-plan loop. The original has none: it relies on the clock, and
    /// on every location spending hours. Here an intent the port cannot carry out spends
    /// nothing, so a cap is needed to keep a stalled turn from spinning.
    /// </summary>
    public const int MaxStops = 40;

    /// <summary>
    /// Runs the current player's turn to its end. Returns every decision taken, in order,
    /// which is what a UI wants for a "here is what Jones did" summary and what a test
    /// wants for an assertion.
    /// </summary>
    public static IReadOnlyList<JonesDecision> Run(Game game)
    {
        var p = game.Current;
        var taken = new List<JonesDecision>();
        var relaxed = false;

        while (!game.Clock.TurnOver && taken.Count < MaxStops)
        {
            var decision = game.DecideForJones();
            taken.Add(decision);

            var here = p.Location;

            // TravelTo refuses when the clock is spent or the building is shut; either way
            // there is nothing further to try, exactly as the original's marble simply
            // never sets off.
            if (here != decision.Destination && !game.TravelTo(decision.Destination)) break;

            var didSomething = Perform(game, p, decision, ref relaxed);

            // No travel and no action means the next call would decide the same thing
            // again. The original cannot reach this state because every location spends
            // hours; this port can, so stop rather than spin.
            if (!didSomething && here == decision.Destination) break;
        }

        return taken;
    }

    /// <summary>
    /// Carries out whichever of the three intent slots this location can serve. Returns
    /// whether anything actually happened.
    /// </summary>
    private static bool Perform(Game game, Player p, JonesDecision d, ref bool relaxed)
    {
        var did = false;
        var w = game.JonesWorld;
        var goods = game.GoodsIndex;

        // --- Work (1). global408 is the number of sessions. -------------------
        if (d.WantsTo(JonesIntent.Work))
        {
            // `DialogScript.sc:52` gives him three sessions by default and a FOURTH when
            // global551 is set — when money is the only goal he still has to reach.
            var sessions = d.IntentArg;
            if (sessions <= 0) sessions = 3;
            if (w.MoneyIsAllThatIsLeft && d.Intent == JonesIntent.Work && sessions == 3) sessions++;

            for (var i = 0; i < sessions && !game.Clock.TurnOver; i++)
            {
                if (game.Work() == 0 && game.LastWorkResult != Employment.WorkResult.Paid) break;
                w.WorkedThisTurn = true;   // global329 is cleared by the first shift
                did = true;
            }
        }

        // --- Relax (6) --------------------------------------------------------
        if (d.WantsTo(JonesIntent.Relax))
        {
            var sessions = d.IntentArg <= 0 ? 1 : d.IntentArg;
            for (var i = 0; i < sessions; i++)
            {
                if (!game.Relax(ref relaxed)) break;
                did = true;
            }
        }

        // --- Rent Office (7, 8) -----------------------------------------------
        if (d.WantsTo(JonesIntent.PayRent) && p.Location == LocationId.RentOffice)
        {
            w.UsedRentOffice = true;                 // global405
            if (game.PayRent()) did = true;
        }

        if (d.WantsTo(JonesIntent.AskRentExtension) && p.Location == LocationId.RentOffice)
        {
            // The extension itself is `rentOffice.sc`'s; all that is modelled here is the
            // attempt, which `triedExt` records and `Player::endTurn` turns into leaveOpen.
            w.UsedRentOffice = true;
            p.TriedExt = 1;
            did = true;
        }

        // --- Employment Office (3, 4, 5) --------------------------------------
        if (p.Location == LocationId.EmploymentOffice
            && (d.WantsTo(JonesIntent.ApplyForJob)
                || d.WantsTo(JonesIntent.ApplyForBetterJob)
                || d.WantsTo(JonesIntent.AskForRaise)))
        {
            w.UsedEmploymentOffice = true;           // global402
            did |= UseEmploymentOffice(game, p, d, goods);
        }

        // --- Shops ------------------------------------------------------------
        if (d.WantsTo(JonesIntent.BuyFood)) did |= BuyFood(game, p);
        if (d.WantsTo(JonesIntent.BuyClothing)) did |= BuyClothing(game, p);

        if (d.WantsTo(JonesIntent.BuyAppliance) && p.Location == LocationId.SocketCity)
        {
            w.BoughtAppliance = true;                // global484
            did |= BuyFirstAffordable(game, Catalogue.SocketCity, p);
        }

        if (d.WantsTo(JonesIntent.ShopZMart) && p.Location == LocationId.ZMart)
        {
            w.UsedZMart = true;                      // global404
            // Rule 39 sends him for the reference books specifically, so that is what he
            // buys — the rest of the Z-Mart shelf is `discount.sc`'s business.
            foreach (var id in ItemIds.ReferenceBooks)
            {
                if (p.Durables.Holds(id)) continue;
                var stock = Catalogue.ZMart.FirstOrDefault(i => i.ItemId == id);
                if (stock is not null && game.Buy(stock)) did = true;
            }
        }

        // --- Pawn Shop (9, 10) -------------------------------------------------
        if (d.WantsTo(JonesIntent.Pawn) && p.Location == LocationId.PawnShop)
            did |= PawnUpTo(game, p, d.IntentArg);

        // Redeem (10) is not modelled: this port has no pawn-ticket state — Game.Pawn
        // decrements the quantity without recording a redemption price — so the branch is
        // reachable in the AI but has nothing to act on. See the report.

        // --- Bank and broker (17, 18, 19, 20, 22) ------------------------------
        if (p.Location == LocationId.Bank) did |= UseBank(game, p, d);

        // --- University (16) ---------------------------------------------------
        if (d.WantsTo(JonesIntent.Study) && p.Location == LocationId.HiTechU)
            did |= Study(game, p, goods);

        return did;
    }

    // ------------------------------------------------------------------
    // Stand-ins for the unported destination scripts
    // ------------------------------------------------------------------

    /// <summary>
    /// STAND-IN for `employment.sc:581-660`. Which job he applies for is decided there, not
    /// in script 300, and the icon-driving logic is not ported. Best-paying job he is
    /// allowed to try, first success wins.
    /// </summary>
    private static bool UseEmploymentOffice(Game game, Player p, JonesDecision d, int goods)
    {
        if (d.WantsTo(JonesIntent.AskForRaise))
        {
            var current = Jobs.All.FirstOrDefault(j => Employment.IsCurrentJob(p, j));
            if (current is null) return false;

            // Through `Game.ApplyFor` rather than straight to `Employment.AskForRaise`:
            // Jones presses the same `JobDItem` the player does, so his raise request is
            // gated on the clock, costs him the same 4 hours and consumes the same
            // openings roll (`employment.sc:164-197`).
            return game.ApplyFor(current) == ApplicationCode.RaiseGranted;
        }

        foreach (var job in Jobs.All.OrderByDescending(j => j.BaseWage))
        {
            if (Employment.IsCurrentJob(p, job)) continue;
            if (game.ApplyFor(job) == ApplicationCode.Hired) return true;
        }

        return false;
    }

    /// <summary>
    /// STAND-IN for `market.sc` / `fastFood.sc`. He buys the cheapest thing on the shelf,
    /// which at Black's Market is the one-week pack and at Monolith Burgers is fries — the
    /// same 55 and 65 base prices the AI costed the trip with (`WhereShouldIGo.sc:165`).
    /// </summary>
    private static bool BuyFood(Game game, Player p)
    {
        var shelf = p.Location switch
        {
            LocationId.BlacksMarket => Catalogue.BlacksMarket,
            LocationId.MonolithBurgers => Catalogue.MonolithBurgers,
            _ => null,
        };

        if (shelf is null) return false;

        var cheapest = shelf.OrderBy(i => i.BasePrice).FirstOrDefault();
        return cheapest is not null && game.Buy(cheapest);
    }

    /// <summary>
    /// STAND-IN for `clothing.sc` / `discount.sc`. He buys exactly what his job's uniform
    /// demands; lower ids are dressier, so anything at or below `uniform` would do, but the
    /// AI only ever budgets for the uniform itself (`WhereShouldIGo.sc:157`).
    /// </summary>
    private static bool BuyClothing(Game game, Player p)
    {
        var shelf = p.Location switch
        {
            LocationId.QtClothing => Catalogue.QtClothing,
            LocationId.ZMart => Catalogue.ZMart,
            _ => null,
        };

        if (shelf is null) return false;

        var wanted = shelf.FirstOrDefault(i => i.ItemId == p.Uniform);
        return wanted is not null && game.Buy(wanted);
    }

    private static bool BuyFirstAffordable(Game game, StockItem[] shelf, Player p)
    {
        foreach (var item in shelf.OrderBy(i => i.BasePrice))
        {
            if (item.ItemId is not { } id || p.Durables.Holds(id)) continue;
            if (game.Buy(item)) return true;
        }

        return false;
    }

    /// <summary>
    /// STAND-IN for `pawnShop.sc`. global408 carries the sum he came to raise; he pawns
    /// until he has it. Cheapest first is this port's choice, not the original's.
    /// </summary>
    private static bool PawnUpTo(Game game, Player p, int target)
    {
        var raised = 0L;

        while (raised < target)
        {
            var next = p.Durables.Items
                .Where(i => i.Quantity > 0)
                .OrderBy(i => i.PricePaid)
                .FirstOrDefault();

            if (next is null) break;

            var offer = game.Pawn(next.IndexNum);
            if (offer <= 0) break;
            raised += offer;
        }

        return raised > 0;
    }

    /// <summary>
    /// STAND-IN for `bank.sc:550-615` and `broker.sc`. The ORDER inside intent 17 is the
    /// original's, and it is the one piece of the bank worth keeping: withdraw first, sell
    /// investments only if the savings run out, and take a loan only as the last resort.
    /// </summary>
    private static bool UseBank(Game game, Player p, JonesDecision d)
    {
        var did = false;
        var w = game.JonesWorld;

        if (d.WantsTo(JonesIntent.Deposit))
        {
            // `bank.sc:558`: he keeps depositing while over $500 stays in his pocket.
            while (p.Cash > 500 && Bank.Deposit(p) > 0) did = true;
        }

        if (d.WantsTo(JonesIntent.Withdraw))
        {
            // global408 withdrawals of $100 each (`bank.sc:570`).
            for (var i = 0; i < d.IntentArg; i++)
            {
                if (Bank.Withdraw(p) <= 0) break;
                did = true;
            }
        }

        if (d.WantsTo(JonesIntent.RaiseCash))
        {
            var target = d.IntentArg;

            while (p.Cash < target && Bank.Withdraw(p) > 0) did = true;

            if (p.Cash < target)
            {
                foreach (var (instrument, shares) in p.Holdings.AllHeld.ToList())
                {
                    if (p.Cash >= target) break;
                    if (Broker.Sell(p, instrument, shares, game.Economy) > 0) did = true;
                }
            }

            if (p.Cash < target && Bank.TakeLoan(p) > 0) did = true;
        }

        if (d.WantsTo(JonesIntent.SellInvestments))
        {
            w.SoldInvestments = true;                // global503
            // global408 of -1 is "all of it" (`broker.sc:668`).
            foreach (var (instrument, shares) in p.Holdings.AllHeld.ToList())
                if (Broker.Sell(p, instrument, shares, game.Economy) > 0) did = true;
        }

        if (d.WantsTo(JonesIntent.BuyInvestments))
        {
            w.BoughtInvestments = true;              // global485

            // STAND-IN: which instrument he picks is `broker.sc`'s, not script 300's.
            // Penny stocks are the cheapest unit, so they are what fits whatever he has.
            var unit = Holdings.UnitPrice(Instrument.Penny, game.Economy);
            var shares = unit > 0 ? (int)(p.Cash / unit) : 0;
            if (shares > 0 && Broker.Buy(p, Instrument.Penny, shares, game.Economy) > 0) did = true;
        }

        if (did) p.RecalculateGoals(game.Economy);
        return did;
    }

    /// <summary>
    /// STAND-IN for `university.sc`. Script 300 says "go and study"; which course, and how
    /// many lessons, is decided there. One lesson per stop here, which the re-plan loop
    /// repeats for as long as rule 44 keeps firing.
    /// </summary>
    private static bool Study(Game game, Player p, int goods)
    {
        var course = Degrees.AvailableTo(p).FirstOrDefault(dg => Degrees.CourseActive(p, dg.Id))
                  ?? Degrees.AvailableTo(p).FirstOrDefault();

        if (course is null) return false;

        if (!Degrees.CourseActive(p, course.Id) && !University.Enroll(p, goods)) return false;

        var outcome = University.Study(p, course.Id, game.Clock);
        return outcome is StudyOutcome.LessonTaken or StudyOutcome.Graduated;
    }
}
