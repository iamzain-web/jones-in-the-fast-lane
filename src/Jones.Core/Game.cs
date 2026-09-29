using Jones.Core.Economy;
using Jones.Core.Model;
using Jones.Core.Sci;

namespace Jones.Core;

/// <summary>
/// The game. Owns the players, the economy and the calendar, and drives the turn cycle.
///
/// This is deliberately headless: it reports events and exposes state, and renders
/// nothing. The same instance drives the Avalonia UI, the AI opponent and the test
/// harness, which is what makes the port verifiable.
/// </summary>
public sealed partial class Game
{
    private readonly IRandomSource _rng;

    public EconomyState Economy { get; } = new();
    public GameCalendar Calendar { get; } = new();
    public GameClock Clock { get; } = new();
    public List<Player> Players { get; } = [];
    public Employment.TurnedDownTracker TurnedDown { get; } = new();

    public int CurrentPlayerIndex { get; private set; }
    public Player Current => Players[CurrentPlayerIndex];

    /// <summary>Events from the most recent turn start, for the UI to present.</summary>
    public IReadOnlyList<TurnStartEvent> LastTurnEvents { get; private set; } = [];

    /// <summary>Players who have met all four goals, in the order they finished.</summary>
    public List<Player> Winners { get; } = [];

    /// <summary>
    /// The globals <see cref="JonesAi"/> reads that are not on the Player — the economy
    /// readings, the clock, and the "already done this" latches that stop him going round
    /// the same errand twice. Cleared at the start of every turn, as `startTrn.sc:203`.
    /// </summary>
    public JonesWorld JonesWorld { get; } = new();

    public bool IsOver => Players.Count(p => p.FinStat == 0) <= 1 && Winners.Count > 0;

    public Game(IRandomSource rng, int playerCount = 1)
    {
        _rng = rng;

        for (var i = 0; i < playerCount; i++)
        {
            var p = new Player { Playing = true, ActualName = $"Player {i + 1}" };
            p.Init();
            Players.Add(p);
        }

        // global374 IS the player count — `(= global374 size)` at `room1.sc:967`, set from
        // the player-count screen at `select1b.sc:88-142` — and it doubles as the economy's
        // volatility term. Because the economy ticks once per PLAYER TURN rather than once
        // per week, multiplying the roll's range by the player count is what keeps crashes
        // and booms at roughly the same frequency per week however many are playing.
        Economy.Volatility = Players.Count;

        foreach (var p in Players) p.RecalculateGoals(Economy);
    }

    /// <summary>The goods index, which drives every shop price.</summary>
    public int GoodsIndex => Economy.Goods.Reading;

    /// <summary>
    /// Begins the current player's turn: advances the economy, resets the clock, puts the
    /// player at home, and runs the turn-start event chain.
    /// </summary>
    public void StartTurn()
    {
        var p = Current;

        // The economy ticks once per player turn, not once per week — `Player::startTurn`
        // calls economicIndex before startTrn.
        Economy.ClearHeadline();
        Economy.Tick(_rng, Calendar.Week);

        Clock.Reset();
        TurnedDown.Clear();

        // `startTrn.sc:203-204`: global329 back to 1 and global402/404/405 — plus
        // global484/485/503 from the block at `startTrn.sc:69` — all back to 0.
        JonesWorld.WorkedThisTurn = false;
        JonesWorld.UsedEmploymentOffice = false;
        JonesWorld.UsedZMart = false;
        JonesWorld.UsedRentOffice = false;
        JonesWorld.BoughtAppliance = false;
        JonesWorld.BoughtInvestments = false;
        JonesWorld.SoldInvestments = false;

        // `startTrn.sc:176-178` — globals 466-472, the per-item happiness latches.
        _hapBaseball = _hapTheatre = _hapConcert = false;
        _hapLottery = _hapFood = _hapDrinks = _hapMeals = false;

        Degrees.RecalculateExtraCredit(p);
        p.RecalculateGoals(Economy);

        // Turns always begin at the player's own front door.
        p.Location = p.LivesAt == 0
            ? LocationId.LowCostHousing
            : LocationId.SecurityApartments;

        // `Economy.Goods` is `global309`, which state 34 prices the relative's gift through.
        LastTurnEvents = TurnStart.Run(p, _rng, Clock, Calendar.Week, Economy.CrashSeverity, Economy.Boom,
                                       Economy.Goods.Reading);

        if (LastTurnEvents.Any(e => e is TurnStartEvent.Won))
        {
            p.FinStat = Calendar.Week;
            if (!Winners.Contains(p)) Winners.Add(p);
        }

        p.RecalculateGoals(Economy);
    }

    /// <summary>Ends the turn and passes play on, advancing the week after the last player.</summary>
    public void EndTurn()
    {
        var p = Current;

        // The turn cannot end with the bank dialog still open, so a mugging rolled on
        // arrival is paid here if the player never left the building.
        ResolvePendingMugging();

        p.EndTurn(Calendar.Week);
        p.RecalculateGoals(Economy);

        CurrentPlayerIndex++;
        if (CurrentPlayerIndex >= Players.Count)
        {
            CurrentPlayerIndex = 0;
            Calendar.AdvanceWeek();
        }
    }

    // --- Actions -----------------------------------------------------------

    /// <summary>
    /// Moves to a location and enters it. Returns false only when the clock has run out —
    /// `room1.sc:138-141` accepts a Place click on `(or (< global323 60) …)` and nothing
    /// else, and closure never refuses a journey (see <see cref="Board.IsOpen"/>).
    ///
    /// Two invented rules used to live here. A shut Rent Office refused the trip outright;
    /// in the source `Place::cue` (`room1.sc:174-209`) always opens the dialog and a closed
    /// building merely skips the door animation. And re-entering the building you were
    /// already standing in was free; `room1.sc:164-167` routes that straight to `self cue:`,
    /// whose `init` charges the same 2 hours as any other door.
    /// </summary>
    public bool TravelTo(LocationId destination)
    {
        var p = Current;
        if (Clock.TurnOver) return false;

        // Wild Willy takes the money as the dialog CLOSES, not on arrival — see
        // <see cref="ResolvePendingMugging"/>. Leaving the building is where that happens.
        ResolvePendingMugging();

        // Hours are charged one marble step at a time into the turn-long sub-hour
        // accumulator, exactly as `room1.sc:1374-1377` ticks the clock per cycle while the
        // marble is moving. No per-journey rounding and no one-hour floor: the source has
        // no per-journey arithmetic at all.
        if (p.Location != destination)
            Clock.SpendTravelSteps(Board.StepsBetween(p.Location, destination));

        Clock.Spend(GameClock.EnterLocationCost);
        p.Location = destination;

        // `bank.sc:120-122` and `market.sc:142-144` both roll in `init`, on arrival, and only
        // when you are carrying something worth taking. `global446` records WHICH: 2 for the
        // Bank and 1 for Black's Market, which is how `Place::cue` picks between
        // `muggedByBank` and `muggedByMarket` (script 114).
        BrokerChargedThisVisit = false;
        MuggingPendingSite = destination switch
        {
            LocationId.Bank => Bank.RollMugging(p, _rng, Calendar.Week)
                ? MuggingSite.Bank : MuggingSite.None,
            LocationId.BlacksMarket => Bank.RollMuggingAtMarket(p, _rng, Calendar.Week)
                ? MuggingSite.Market : MuggingSite.None,
            _ => MuggingSite.None,
        };
        MuggingPending = MuggingPendingSite != MuggingSite.None;

        return true;
    }

    /// <summary>`global446`: 0 nobody, 1 Black's Market, 2 the Bank.</summary>
    public enum MuggingSite { None = 0, Market = 1, Bank = 2 }

    /// <summary>
    /// Wild Willy is waiting outside, rolled on arrival but not yet paid.
    /// `bank.sc:120-133`: `init` sets `global446` and only `(if global446 (global302 cash: 0
    /// cashHi: 0))` — AFTER `(self doit: 0 0)` returns, i.e. when the dialog closes — takes
    /// the cash. The port used to empty the pocket on arrival, so money you banked during
    /// the visit was already gone. Banking before you leave really does save it.
    /// </summary>
    public bool MuggingPending { get; private set; }

    /// <summary>
    /// `global446` itself — which building he is waiting outside. Kept beside the boolean
    /// rather than replacing it so the save format stays a plain flag; the site only matters
    /// for the few seconds the walk is on screen, which no save can span.
    /// </summary>
    public MuggingSite MuggingPendingSite { get; private set; }

    /// <summary>Whether Wild Willy actually took the money on the most recent exit.</summary>
    public bool LastMugging { get; private set; }

    /// <summary>Which of the two he did it outside, so the UI can walk the right path.</summary>
    public MuggingSite LastMuggingSite { get; private set; }

    /// <summary>
    /// `local0` in `bank.sc` — whether the broker's 2 hours have already been charged this
    /// visit. `bank.sc:417-421` charges them once per bank visit however many times you open
    /// his window, unlike *Apply For Loan*, which charges on every press. Cleared by
    /// <see cref="TravelTo"/>, which is the port's equivalent of the dialog re-opening.
    /// </summary>
    public bool BrokerChargedThisVisit { get; set; }

    /// <summary>
    /// Closes the building the player is standing in, which is when a rolled mugging is
    /// paid. Called on travelling away and at end of turn.
    /// </summary>
    public void ResolvePendingMugging()
    {
        LastMugging = false;
        LastMuggingSite = MuggingSite.None;
        if (!MuggingPending) return;

        // A restored save carries the flag but not the site; the Bank is the commoner of the
        // two and is what an unknown site falls back to.
        LastMuggingSite = MuggingPendingSite == MuggingSite.None
            ? MuggingSite.Bank
            : MuggingPendingSite;

        MuggingPending = false;
        MuggingPendingSite = MuggingSite.None;
        LastMugging = true;
        Bank.ApplyMugging(Current);
        Current.RecalculateGoals(Economy);
    }

    /// <summary>Outcome of the last shift, so the UI can report being sacked.</summary>
    public Employment.WorkResult LastWorkResult { get; private set; }

    /// <summary>
    /// What the landlord took out of the last shift (`n108.sc:93-100`), for the
    /// `980 + placeNum` clip. Zero when nothing was garnished.
    /// </summary>
    public int LastGarnished { get; private set; }

    /// <summary>Works a shift, if the player is at their own workplace and dressed for it.</summary>
    public int Work()
    {
        var p = Current;
        var here = Board.Get(p.Location);
        if (here.Workplace is null) return 0;
        if (p.WorksAt != (int)here.Workplace + 1) return 0;

        var shift = Employment.Work(p, Clock);
        LastWorkResult = shift.Result;
        LastGarnished = shift.Garnished;
        p.RecalculateGoals(Economy);
        return shift.Paid;
    }

    /// <summary>Relaxes at home: 6 Hours for +3 Relaxation, and +2 Happiness once a turn.</summary>
    public bool Relax(ref bool alreadyRelaxedThisTurn)
    {
        var p = Current;

        // `lowcost.sc:49-50` adds the relax button only when `livesAt == 0`, and
        // `security.sc:49-50` only when `livesAt == 2`: you cannot relax in an apartment
        // you do not live in. The port used to accept either.
        var atHome = p.LivesAt == 0
            ? p.Location == LocationId.LowCostHousing
            : p.Location == LocationId.SecurityApartments;
        if (!atHome) return false;

        // `lowcost.sc:115-132` / `security.sc:123-141`: the clock is READ FIRST, the six
        // hours are then spent UNCONDITIONALLY, and only then does `(if (== temp1 60))`
        // choose between the "no time left" line and the benefit. Refusing outright and
        // spending nothing — which is what this did — is the wrong shape.
        var turnWasOver = Clock.TurnOver;
        Clock.Spend(GameClock.RelaxCost);

        if (turnWasOver) return false;

        if (!alreadyRelaxedThisTurn)
        {
            p.HapStat += 2;
            alreadyRelaxedThisTurn = true;
        }

        p.Relax += 3;
        if (p.Relax > 50) p.Relax = 50;

        return true;
    }

    /// <summary>
    /// Which failed requirement the clerk cites for the most recent refusal, or null when
    /// the last application was not refused on its merits. See
    /// <see cref="Employment.DrawRejectionReason"/> — the original picks at random among
    /// the requirements that actually failed rather than reporting the first.
    /// </summary>
    public RejectionReason? LastRejection { get; private set; }

    /// <summary>Why the last application was refused, for the UI. See <see cref="LastRejection"/>.</summary>
    public JobOutcome LastJobOutcome { get; private set; }

    /// <summary>
    /// One visit to the Employment Office counter — `JobDItem::doit` (`employment.sc:164-241`)
    /// in full. The three things the port was missing are all here:
    ///
    ///  - the clock gate `(if (!= global323 60) … else (= global433 5))` (`:167`);
    ///  - the 4 hours (`visitTime 4`, `:78`), spent at `:168` BEFORE `qualify:` and therefore
    ///    charged whether you are hired, refused or only asking for a raise;
    ///  - the raise branch (`:171-197`), which re-applying to your own job takes. That used
    ///    to fall through to the hire path and RE-HIRE you: raises back to 0, +2 experience,
    ///    +3 happiness, every single click.
    ///
    /// `qualify:` runs before the cond, so the openings roll is consumed on a raise request
    /// too, and `turnedDown:` is reached only from the refusal branch.
    /// </summary>
    public ApplicationCode ApplyFor(Job job)
    {
        var p = Current;
        LastRejection = null;

        if (Clock.TurnOver) return ApplicationCode.OfficeClosed;

        Clock.Spend(GameClock.JobApplicationCost);

        var q = Employment.Qualify(p, job, _rng, TurnedDown);

        ApplicationCode code;

        if (Employment.IsCurrentJob(p, job))
        {
            code = Employment.AskForRaise(p, job, GoodsIndex);
        }
        else if (q.Hired)
        {
            Employment.Hire(p, job, GoodsIndex);
            LastJobOutcome = JobOutcome.Hired;
            code = ApplicationCode.Hired;
        }
        else
        {
            LastJobOutcome = Employment.Refuse(p, job, q, TurnedDown);

            // `proc206_1` runs immediately after `JobDItem::doit` and only takes its draw on
            // the global433 == -1 branch, so nothing else consumes from the random stream.
            LastRejection = Employment.DrawRejectionReason(q, _rng);

            // `(if (not global325) (global302 notEnoughEd: 1))` — set from the flag, not from
            // the drawn reason, so a player who failed on education is flagged even when the
            // clerk happened to cite experience instead.
            if (!q.Education) p.NotEnoughEd = true;

            code = ApplicationCode.Refused;
        }

        p.RecalculateGoals(Economy);
        return code;
    }

    /// <summary>
    /// Per-turn happiness latches, globals 466-472, cleared at `startTrn.sc:176-178`. Note
    /// how few there are for how many shelf lines use them: 470 covers all three food packs
    /// between them, 471 both drinks and 472 both hot meals, so buying a cheeseburger locks
    /// out the Astro Chicken's larger award for the rest of the turn.
    /// </summary>
    private bool _hapBaseball;   // global466
    private bool _hapTheatre;    // global467
    private bool _hapConcert;    // global468
    private bool _hapLottery;    // global469
    private bool _hapFood;       // global470
    private bool _hapDrinks;     // global471
    private bool _hapMeals;      // global472

    /// <summary>Buys an item from a store, paying the economy-adjusted price.</summary>
    public bool Buy(StockItem item)
    {
        var p = Current;
        var price = item.PriceAt(GoodsIndex);
        if (price > p.Cash) return false;

        p.Cash -= price;

        if (item.Type == GoodsType.Junk)
        {
            // Junk has no id and is never stored — it just costs you.
            p.HapStat -= item.Name == "Works of Capote" ? 2 : 1;
        }
        else if (item.Type == GoodsType.NotStored)
        {
            // `typeOfGoods 3` falls straight through `CostDItem::doit`'s switch
            // (`WButton.sc:203-226`), which handles 0, 1 and 2 only. Nothing is received,
            // `global418` stays 0 and so no `pricePaid` is written either. The newspaper,
            // the shakes (`fastFood.sc:256`) and the colas (`:280`) are the only three.
        }
        else if (item.ItemId is { } id)
        {
            var list = item.Type == GoodsType.Durable ? p.Durables : p.Consumables;
            var alreadyOwned = list.Holds(id);

            var received = list.Receive(id, item.Quantity);

            // `WButton.sc:229-231`: `(if (not (& (global418 attributes:) $0038)) …)`.
            // Buying a replacement for something you have pawned must NOT overwrite
            // `pricePaid`, because `redemptionPrice` was derived from it.
            if ((received.Attributes & DurableAttributes.WearOrWornMask) == 0)
                received.PricePaid = price;

            if (item.Type == GoodsType.Durable)
            {
                received.Attributes |= DurableAttributes.Breakable;
                // Z-Mart stock is flimsier: 1-in-36 rather than 1-in-51.
                if (Catalogue.ZMart.Contains(item))
                    received.Attributes |= DurableAttributes.CheapBuild;

                if (!alreadyOwned) p.HapStat += 1;
                Degrees.RecalculateExtraCredit(p);
            }
        }

        AwardPurchaseHappiness(p, item);

        p.RecalculateGoals(Economy);
        return true;
    }

    /// <summary>
    /// The thirteen live per-item happiness rules, each from that shelf instance's own
    /// `doit` override, which runs AFTER `(super doit:)` has taken the money:
    ///
    ///     (if (and global416 (not global470)) (= global470 1) (proc0_13 1))
    ///
    /// `global416` is "the purchase succeeded", so a failed buy awards nothing — this is
    /// only reached on the success path. The latches are per TURN, and shared, so the
    /// second food pack of a turn is worth nothing however large it is.
    ///
    /// The clothing pair are the exception: `clothing.sc:184-192` and `:210-218` have no
    /// latch at all and pay every single time. They belong to QT Clothing's instances only —
    /// Z-Mart's cheaper clothes (`discount.sc`) award nothing, and neither do its hamburgers
    /// or fries.
    /// </summary>
    private void AwardPurchaseHappiness(Player p, StockItem item)
    {
        // The durable bonus is NOT here, and must not be: see the note on `alreadyOwned`
        // in Buy. Every `proc0_13` in `discount.sc`/`appliance.sc` is guarded by
        // `(not ((global302 durables:) objectAtIndexQuan: indexNum))` evaluated AFTER
        // `recieve:` has already raised the quantity, so it is dead code in the shipped
        // game. Left as-is pending a decision — recorded in the report.

        if (Catalogue.QtClothing.Contains(item))
        {
            // `clothing.sc:184` business suit +2, `:210` leisure suit +1, both unlatched.
            // Casual clothes have no override and award nothing.
            if (item.ItemId == ItemIds.BusinessSuit) p.HapStat += 2;
            else if (item.ItemId == ItemIds.DressClothes) p.HapStat += 1;
            return;
        }

        if (Catalogue.ZMart.Contains(item))
        {
            switch (item.ItemId)
            {
                case 37 when !_hapBaseball: _hapBaseball = true; p.HapStat += 2; break;  // discount.sc:526
                case 38 when !_hapTheatre:  _hapTheatre = true;  p.HapStat += 2; break;  // discount.sc:550
                case 39 when !_hapConcert:  _hapConcert = true;  p.HapStat += 2; break;  // discount.sc:574
            }
            return;
        }

        if (Catalogue.BlacksMarket.Contains(item))
        {
            if (item.ItemId == ItemIds.LotteryTickets && !_hapLottery)
            {
                _hapLottery = true;
                p.HapStat += 2;                                                          // market.sc:313
            }
            else if (item.ItemId == ItemIds.FreshFood && !_hapFood)
            {
                _hapFood = true;
                // +1, +2 and +4 for the one-, two- and four-week packs — the award is the
                // pack's own `units` (`market.sc:217/250/283`).
                p.HapStat += item.Quantity;
            }
            return;
        }

        if (!Catalogue.MonolithBurgers.Contains(item)) return;

        switch (item.ItemId)
        {
            case ItemIds.Cheeseburgers when !_hapMeals: _hapMeals = true; p.HapStat += 1; break;  // fastFood.sc:200
            case ItemIds.AstroChicken when !_hapMeals:  _hapMeals = true; p.HapStat += 2; break;  // fastFood.sc:224
            case ItemIds.Shakes when !_hapDrinks:       _hapDrinks = true; p.HapStat += 2; break; // fastFood.sc:263
            case ItemIds.Colas when !_hapDrinks:        _hapDrinks = true; p.HapStat += 1; break; // fastFood.sc:287
        }
    }

    /// <summary>Pawns a durable for 40% of its economy-adjusted purchase price.</summary>
    public long Pawn(int itemId)
    {
        var p = Current;
        var item = p.Durables.AtHeld(itemId);
        return item is null ? 0 : Pawn(item);
    }

    /// <summary>
    /// Pawns one durable, `pawnShop.sc:663-697`. The offer is 40% of the item's
    /// economy-adjusted purchase price; the item keeps its entry but goes into hock —
    ///
    ///     (local2 attributes: (| (local2 attributes:) $0018)
    ///             redemptionPrice: (/ (local2 pricePaid:) 2)
    ///             quantity: (- (local2 quantity:) 1))
    ///
    /// — so the ticket bits and the redemption price are set HERE and nowhere else. The
    /// redemption price is half of what you originally paid, NOT the economy-adjusted
    /// figure and NOT related to what you were handed over the counter: redeeming always
    /// costs more than pawning paid, which is the shop's margin.
    ///
    /// The ticket then ages a step a week in `Player.EndTurn` and the item is forfeited
    /// when it expires. Pawning ALWAYS costs a point of happiness, and a second point if
    /// it was the refrigerator and there is fresh food in it — which also spoils.
    /// </summary>
    public long Pawn(Item item)
    {
        var p = Current;

        var offer = Pricing.PawnOffer(GoodsIndex, item.PricePaid);

        item.Attributes |= DurableAttributes.WearMask;
        item.RedemptionPrice = SciMath.Div(item.PricePaid, 2);
        item.Quantity--;

        p.Cash += offer;
        p.HapStat -= 1;

        // Pawning the fridge while there is a fresh-food ENTRY costs an extra point — and
        // the food. `pawnShop.sc:691-698` tests `objectAtIndex: 1`, which needs the entry
        // only to exist; `AtHeld` required a quantity above zero, so in the normal state
        // where the entry sits at zero the penalty and the wipe were both skipped.
        if (item.IndexNum == ItemIds.Refrigerator
            && p.Consumables.At(ItemIds.FreshFood) is { } food)
        {
            p.HapStat -= 1;
            food.Quantity = 0;
        }

        Degrees.RecalculateExtraCredit(p);
        p.RecalculateGoals(Economy);
        return offer;
    }

    /// <summary>Durables of the current player that are in hock and still redeemable.</summary>
    public IEnumerable<Item> Redeemable() =>
        Current.Durables.Items.Where(i =>
            (i.Attributes & DurableAttributes.WearMask) != 0);

    /// <summary>
    /// Everything on the shop's resale rack: any player's forfeited durables
    /// (`pawnShop.sc:270-300` scans every player, not just the one standing there).
    /// </summary>
    public IEnumerable<(Player Owner, Item Item)> Buyable() =>
        Players.SelectMany(owner => owner.Durables.Items
            .Where(i => i.Attributes.HasFlag(DurableAttributes.WornOut))
            .Select(i => (owner, i)));

    /// <summary>
    /// Buys a ticketed item back, `aRedeemableItem::doit` (`pawnShop.sc:815-887`). The
    /// price is the item's own `redemptionPrice`, `fixedPrice 1` so the economy does not
    /// touch it; `(& attributes $ffc7)` clears the ticket AND the forfeit bit together.
    ///
    /// Redeeming your own item (`local0 == 2`) just puts the quantity back. Buying someone
    /// else's forfeited one moves the durable across to you, and the rack has already
    /// stamped it second-hand, so it breaks at the Z-Mart rate from then on.
    /// </summary>
    public bool Redeem(Item item, Player? owner = null)
    {
        var p = Current;
        if (item.RedemptionPrice > p.Cash) return false;

        var forfeited = item.Attributes.HasFlag(DurableAttributes.WornOut);

        p.Cash -= item.RedemptionPrice;
        item.Attributes &= ~DurableAttributes.WearOrWornMask;

        if (!forfeited || ReferenceEquals(owner, p))
        {
            item.Quantity++;
        }
        else
        {
            // `pawnShop.sc:850-880`: the durable leaves its owner's list and joins the
            // buyer's, unless the buyer already holds one of that type, in which case a
            // clone of quantity 1 is added instead.
            owner?.Durables.Remove(item);

            // `pawnShop.sc:864-867` uses `hasType:` as the test, and `hasType:`
            // (`Goods.sc:63-73`) matches on `indexNum` ALONE — it is `objectAtIndex:`
            // semantics, not `objectAtIndexQuan:`. `AtHeld` here required a quantity above
            // zero, so buying back a type you already hold at quantity 0 (because you
            // pawned yours) took the wrong branch and left two entries with the same id.
            //
            // Note also what the original's test does on the way past: `hasType: indexNum 1`
            // INCREMENTS the matched entry by 1 as a side effect, which is how the unit
            // actually reaches you on this branch. Using it as a predicate is an original
            // quirk, not a port shortcut — the two happen to agree on the total.
            if (p.Durables.At(item.IndexNum) is not null)
            {
                // Just the quantity. The original's `hasType:` side effect writes nothing
                // else — `pricePaid` and the attributes of the entry you already had are
                // left exactly as they were, so the second-hand stamp does not follow the
                // unit across.
                p.Durables.Receive(item.IndexNum, 1);
            }
            else
            {
                item.Quantity = 1;
                item.Attributes |= DurableAttributes.CheapBuild;
                p.Durables.Add(item);
            }
        }

        Degrees.RecalculateExtraCredit(p);
        p.RecalculateGoals(Economy);
        return true;
    }

    /// <summary>
    /// Asks <see cref="JonesAi"/> where the current player should go next, after syncing the
    /// readings and the clock into <see cref="JonesWorld"/>.
    ///
    /// The clock: the original counts hours UP in global323 and keeps a sub-hour tick
    /// accumulator in global324. This port's <see cref="GameClock"/> counts hours DOWN but
    /// now carries the same accumulator, so both are passed through. The old comment here
    /// claimed global324 was always 0 when the AI is called; it is not. It is zeroed only at
    /// turn start (`room1.sc:1094`) and keeps whatever the last journey left in it, which is
    /// exactly the remainder `localproc_2`'s travel estimate (`WhereShouldIGo.sc:40`) adds
    /// its own ticks to.
    /// </summary>
    public JonesDecision DecideForJones()
    {
        var p = Current;

        JonesWorld.GoodsIndex = Economy.Goods.Reading;
        JonesWorld.InvestIndex = Economy.Invest.Reading;
        JonesWorld.MainTrend = Economy.Main.Index;
        JonesWorld.Week = Calendar.Week;
        JonesWorld.HoursUsed = Clock.HoursUsed;
        JonesWorld.SubHourTicks = Clock.SubHourTicks;
        JonesWorld.TicksPerHour = GameClock.TicksPerHour;
        JonesWorld.CurrentPlace = JonesAi.PlaceNumOf(p.Location);

        return JonesAi.Decide(p, JonesWorld, _rng);
    }

    /// <summary>
    /// `payRent` (`rentOffice.sc:207-222`), which does far less than the port used to:
    ///
    ///     (if (>= (proc0_11 global302) price)
    ///         ((global302 consumables:) recieve: indexNum (- 4 (mod global372 4)))
    ///         (proc0_10 (* theSign price)))
    ///
    /// It charges `curRent`, never the arrears — those are a SEPARATE button,
    /// `payGarnishment` (`rentOffice.sc:448-473`). It does not touch `rentOwed`, which
    /// clears only through that button or through wage garnishment (`n108.sc:93-112`). It
    /// does not touch `turnedOver` either; the turn-start rent notice does that
    /// (`startTrn.sc:486`), and clearing it twice re-arms arrears accrual inside the same
    /// month. And it buys `4 - (week mod 4)` weeks, not a flat 4 — so a `leaveOpen` visit
    /// outside a rent week buys 3, 2 or 1.
    /// </summary>
    public bool PayRent()
    {
        var p = Current;
        if (p.CurRent > p.Cash) return false;

        p.Cash -= p.CurRent;

        var rentId = p.LivesAt == 0 ? ItemIds.LowCostRent : ItemIds.SecurityRent;
        p.Consumables.Receive(rentId, 4 - Calendar.Week % 4);

        p.RecalculateGoals(Economy);
        return true;
    }

    /// <summary>
    /// `payGarnishment` (`rentOffice.sc:448-473`) — the separate button that settles
    /// arrears. `price = rentOwed`, `fixedPrice 1`, and it buys no rent weeks.
    /// </summary>
    public bool PayGarnishment()
    {
        var p = Current;
        if (p.RentOwed <= 0 || p.RentOwed > p.Cash) return false;

        p.Cash -= p.RentOwed;
        p.RentOwed = 0;

        p.RecalculateGoals(Economy);
        return true;
    }

    // ------------------------------------------------------------------
    // Moving house (`rentOffice.sc:314-446`)
    // ------------------------------------------------------------------

    /// <summary>
    /// `rentLowCost::basePrice` and `rentSecurity::basePrice` (`rentOffice.sc:323`, `:390`),
    /// run through the goods index by `CostDItem::init` (`WButton.sc:180-182`) like every
    /// other price on the board.
    /// </summary>
    public const int LowCostBaseRent = 325;
    public const int SecurityBaseRent = 475;

    /// <summary>The asking rent for an apartment, in `livesAt`'s SCRIPT encoding (0 or 2).</summary>
    public int RentAsking(int scriptLivesAt) =>
        Pricing.Price(GoodsIndex,
            scriptLivesAt == 0 ? LowCostBaseRent : SecurityBaseRent);

    /// <summary>
    /// Weeks of rent already paid on the apartment the player is leaving. The two rental
    /// buttons test this — `((global302 consumables:) objectAtIndex: 41)` then `quantity:`
    /// at `rentOffice.sc:335-337` and the mirror at `:403-405` — because those weeks are
    /// forfeited by moving, which is the whole point of the question they then ask.
    /// </summary>
    public int PrepaidWeeksAt(int scriptLivesAt) =>
        Current.Consumables.At(scriptLivesAt == 0
            ? ItemIds.LowCostRent
            : ItemIds.SecurityRent)?.Quantity ?? 0;

    /// <summary>What a press of one of the two apartment buttons did.</summary>
    public enum RentOutcome
    {
        /// <summary>You already live there (`:327` / `:394`): clip 192 / 196, nothing happens.</summary>
        AlreadyThere,

        /// <summary>
        /// Affordable, but prepaid weeks on the current flat would be lost — the gate at
        /// `rentOffice.sc:400-405`. Nothing has changed yet; ask, then call again with
        /// <c>confirmed: true</c>.
        /// </summary>
        NeedsConfirmation,

        /// <summary>Moved in: four weeks bought, `curRent` and `livesAt` rewritten.</summary>
        Moved,

        /// <summary>`global416` came back 0 — `notEnoughCash::doit`, clip 183.</summary>
        CannotAfford,
    }

    /// <summary>
    /// `rentLowCost::doit` / `rentSecurity::doit` (`rentOffice.sc:326-378`, `:393-445`). The
    /// two are the same method with the two apartments swapped, so they are one method here.
    ///
    /// <code>
    /// (if (== (global302 livesAt:) 2) (proc0_18 196 …) (return 0))        ; :394-397
    /// (if (and (>= (proc0_11) price) (= temp1 (… objectAtIndex: 40)) (temp1 quantity:))
    ///     … (Print 201 1 { YES } 1 { NO } 0 #width 150)                    ; :400-423
    ///     (temp1 quantity: 0))                                             ; :426
    /// (= temp0 (super doit:))                                              ; :434
    /// (if global416 (global302 curRent: price livesAt: 2))                 ; :435-437
    /// </code>
    ///
    /// `super doit:` is `CostDItem::doit` (`WButton.sc:197-253`): it sets `global416` from
    /// `(>= (proc0_11) price)`, hands over `units` (4) weeks of `indexNum` and takes the
    /// money. The forfeit at `:426` happens BEFORE that, so the weeks on the old flat go
    /// whether or not the new one is ever paid for — but the branch is only reached when the
    /// cash is already there, so in practice it cannot bite.
    ///
    /// <paramref name="scriptLivesAt"/> is the SOURCE's encoding: 0 Low-Cost, 2 Security.
    /// </summary>
    public RentOutcome RentApartment(int scriptLivesAt, bool confirmed = false)
    {
        var p = Current;
        var here = p.LivesAt == 0 ? 0 : 2;
        if (here == scriptLivesAt) return RentOutcome.AlreadyThere;

        var price = RentAsking(scriptLivesAt);
        var leaving = p.Consumables.At(here == 0 ? ItemIds.LowCostRent : ItemIds.SecurityRent);

        if (!confirmed && p.Cash >= price && leaving is { Quantity: > 0 })
            return RentOutcome.NeedsConfirmation;

        // `(temp1 quantity: 0)` — the prepaid weeks are NOT refunded.
        if (confirmed && leaving is not null) leaving.Quantity = 0;

        // CostDItem::doit.
        if (p.Cash < price) return RentOutcome.CannotAfford;

        p.Cash -= price;
        p.Consumables.Receive(
            scriptLivesAt == 0 ? ItemIds.LowCostRent : ItemIds.SecurityRent, 4);

        p.CurRent = price;
        p.LivesAt = scriptLivesAt == 0 ? 0 : 1;   // the port's own 0/1 encoding

        p.RecalculateGoals(Economy);
        return RentOutcome.Moved;
    }

    /// <summary>What `moreTime::doit` did (`rentOffice.sc:235-311`).</summary>
    public enum MoreTimeOutcome
    {
        /// <summary>Rent is already paid up, so there is nothing to extend: clip 191.</summary>
        NotNeeded,

        /// <summary>The extension was granted: clip 184, +1 happiness, sting 45.</summary>
        Granted,

        /// <summary>It was refused: clip 185, −1 happiness, sting 44.</summary>
        Refused,

        /// <summary>Asked again after a yes: clip 186, and nothing else happens.</summary>
        AskedAgainAfterYes,

        /// <summary>
        /// Asked again after a no. The clip is `185 + triedExt` as it was on entry — 187,
        /// 188, 189, 190 — and `triedExt` climbs by one each time. Past 5 the `switch` has
        /// no case and the button falls silent.
        /// </summary>
        AskedAgainAfterNo,
    }

    /// <summary>
    /// `moreTime::doit` (`rentOffice.sc:235-311`). The odds improve nothing: each successful
    /// extension makes the NEXT one less likely, because `rentExt` climbs and the threshold
    /// climbs with it — 1-in-1 the first time, then 9-in-12, 6-in-12, 3-in-12 thereafter.
    ///
    /// <code>
    /// (switch (global302 rentExt:)
    ///     (-1 0) (0 1)
    ///     (1 (> (Random 1 12) 3)) (2 (> (Random 1 12) 6))
    ///     (else (> (Random 1 12) 9)))
    /// </code>
    ///
    /// `triedExt` is what `Player::endTurn` turns into `leaveOpen` (`room1.sc:806`), which is
    /// the only thing that opens the Rent Office outside the fourth week of the month.
    /// </summary>
    public MoreTimeOutcome AskForMoreTime()
    {
        var p = Current;

        var rentId = p.LivesAt == 0 ? ItemIds.LowCostRent : ItemIds.SecurityRent;
        if (p.Consumables.AtHeld(rentId) is not null) return MoreTimeOutcome.NotNeeded;

        if (p.TriedExt != 0)
        {
            if (p.TriedExt == 1) return MoreTimeOutcome.AskedAgainAfterYes;

            // Cases 2..5 each bump it; there is no case 6, so a sixth press does nothing.
            if (p.TriedExt is >= 2 and <= 5)
            {
                p.TriedExt++;
                return MoreTimeOutcome.AskedAgainAfterNo;
            }

            return MoreTimeOutcome.AskedAgainAfterNo;
        }

        var granted = p.RentExt switch
        {
            -1 => false,
            0 => true,
            1 => _rng.Next(1, 12) > 3,
            2 => _rng.Next(1, 12) > 6,
            _ => _rng.Next(1, 12) > 9,
        };

        if (granted)
        {
            p.RentExt++;
            p.HapStat += 1;
            p.TriedExt = 1;
            p.RecalculateGoals(Economy);
            return MoreTimeOutcome.Granted;
        }

        p.HapStat -= 1;
        p.TriedExt = 2;
        p.RecalculateGoals(Economy);
        return MoreTimeOutcome.Refused;
    }
}
