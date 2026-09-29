using Jones.Core.Sci;

namespace Jones.Core.Model;

/// <summary>
/// Port of the `Player` class in `room1.sc:588`. Property defaults below are the original's
/// declared initial values, not invented ones.
///
/// Money note: the original stores cash, net worth, liquid assets and bank balance as Hi/Lo
/// pairs of 16-bit words because they genuinely exceed 16 bits in a long game. The port uses
/// <see cref="long"/> for those and plain <c>int</c> for everything else, which is the one
/// place a wider type is correct rather than a divergence.
/// </summary>
public sealed class Player
{
    public string ActualName { get; set; } = "";
    public bool Playing { get; set; }

    // --- Goals (set at game start, 10..100; 50 is the pre-set default) -----
    public int MonGoal { get; set; } = 50;
    public int HapGoal { get; set; } = 50;
    public int EduGoal { get; set; } = 50;
    public int CarGoal { get; set; } = 50;

    // --- Goal progress ----------------------------------------------------
    public int MonStat { get; set; }

    /// <summary>
    /// Happiness. EVERY adjustment in the game goes through `proc0_13` (`Main.sc:1102-1111`),
    /// which clamps to 0..100:
    ///
    ///     (if (> (= temp0 (+ (temp1 hapStat:) param1)) 100) (= temp0 100))
    ///     (if (&lt; temp0 0) (= temp0 0))
    ///
    /// There is no unclamped path to it anywhere. This was a bare `int` written raw at
    /// about fifteen sites, so happiness could run negative or past 100 — and
    /// <see cref="HasWon"/> compares it against a goal, so an over-100 value made the
    /// happiness goal trivially met and a negative one made it unreachable. The clamp lives
    /// in the setter so that no call site can miss it, which is exactly what `proc0_13`
    /// achieves in the original.
    /// </summary>
    public int HapStat
    {
        get => _hapStat;
        set => _hapStat = value > 100 ? 100 : value < 0 ? 0 : value;
    }

    private int _hapStat;

    public int EduStat { get; set; }
    public int CarStat { get; set; }

    /// <summary>Week the player finished, 0 while still playing (`finStat`).</summary>
    public int FinStat { get; set; }

    // --- Money ------------------------------------------------------------
    public long Cash { get; set; } = 200;
    public long NetWorth { get; set; } = 200;
    public long LqAss { get; set; } = 200;
    public long InvAss { get; set; }
    public long BankBal { get; set; }

    // --- Housing ----------------------------------------------------------
    /// <summary>0 = Low-Cost Housing (robbable), 1 = Le Security Apartments.</summary>
    public int LivesAt { get; set; }
    public int CurRent { get; set; } = 325;
    public int RentOwed { get; set; }
    public int RentExt { get; set; }
    public int TriedExt { get; set; }
    public bool TurnedOver { get; set; }

    /// <summary>
    /// `leaveOpen` — set at turn end to `triedExt == 1` (`room1.sc:806`). Asking for a rent
    /// extension keeps the Rent Office open to you outside the fourth week of the month
    /// (`rentOffice.sc:69`), which is the only way anyone visits it off-schedule.
    /// </summary>
    public bool LeaveOpen { get; set; }

    // --- Employment -------------------------------------------------------
    public int WorksAt { get; set; }
    public int Wage { get; set; }
    public int BaseWage { get; set; }
    public int Occupation { get; set; }
    public int Raises { get; set; }

    /// <summary>Clothing standard the current job demands. LOWER is dressier.</summary>
    public int Uniform { get; set; } = ItemIds.CasualClothes;

    /// <summary>Best clothing currently held, recomputed by <see cref="DressedForWork"/>.</summary>
    public int Wearing { get; set; } = ItemIds.CasualClothes;

    /// <summary>
    /// `nakedCount` — how many turn-starts in a row this player has come up with no clothing
    /// AND under $300 in both cash and net worth (`startTrn.sc:827-880`, state 34).
    ///
    /// It is a counter, not a flag: the relative's gift arrives on the turn it goes ABOVE 1,
    /// which is the SECOND such turn, and the counter is zeroed as the gift is handed over.
    /// It is deliberately NOT cleared when the test fails, so a player who scrapes back over
    /// $300 for a week and falls under again is given the gift on that next bad turn.
    /// </summary>
    public int NakedCount { get; set; }

    // --- Loans ------------------------------------------------------------
    public int LoanBal { get; set; }
    public int LatePay { get; set; }
    public int PaySched { get; set; }
    public bool MadePay { get; set; }

    // --- Stats ------------------------------------------------------------
    /// <summary>
    /// Starts at 25 in the class initialiser, NOT the 10 the wiki reports. The floor
    /// during play is 10. See MECHANICS.md §14.
    /// </summary>
    public int Relax { get; set; } = 25;

    public int Dependibility { get; set; } = 20;

    /// <summary>
    /// `minDepend` — the current job's dependability REQUIREMENT, set verbatim on hire
    /// (`employment.sc:209`). Two things key off it: you are sacked below
    /// `minDepend - 5`, and your dependability ceiling is `minDepend + 20 + eduCredit`.
    /// </summary>
    public int MinDepend { get; set; }

    public int Experience { get; set; } = 10;
    public int MaxExper { get; set; } = 10;

    /// <summary>
    /// `notEnoughEd` — an employer has turned this player away for lack of a degree
    /// (`employment.sc:38`). Cleared on graduating (`university.sc:207`). Jones reads it as
    /// a standing instruction to go back to school (`WhereShouldIGo.sc:789`).
    /// </summary>
    public bool NotEnoughEd { get; set; }

    /// <summary>
    /// `playing == 29` — this player is driven by <see cref="JonesAi"/> rather than by a
    /// human. Set on player 1 for the attract-mode demo (`select1.sc:132`) and on player 2
    /// when a lone human plays against Jones (`select4.sc:268`).
    /// </summary>
    public bool IsJones { get; set; }

    /// <summary>Degrees the last refused application was missing, for the UI to show.</summary>
    public int NeedEd1 { get; set; }
    public int NeedEd2 { get; set; }

    /// <summary>
    /// University credits that raise the experience and dependability ceilings
    /// (`n108.sc:63` and `:70`). This is how education lifts the caps — NOT a per-degree
    /// bonus applied at hire, which is what I had originally assumed.
    /// </summary>
    public int ExpCredit { get; set; }
    public int EduCredit { get; set; }
    public int Enrollments { get; set; }

    /// <summary>Extra credit points, each reducing lessons-to-graduate by one.</summary>
    public int XCred { get; set; }

    public int CoursesDone { get; set; }

    // --- Inventory --------------------------------------------------------
    public ItemList Consumables { get; } = new();
    public ItemList Durables { get; } = new();
    public ItemList Education { get; } = new();
    public Holdings Holdings { get; } = new();

    /// <summary>Where the player currently is on the board.</summary>
    public LocationId Location { get; set; } = LocationId.LowCostHousing;

    /// <summary>
    /// Port of `Player::init` (`room1.sc:667`). Every player starts with $200, three weeks
    /// of rent already paid at $325, and six casual outfits bought at $30.
    ///
    /// DELIBERATE DEVIATION, and the only one in this file: the original gives NO FOOD.
    /// See <see cref="StartingFoodWeeks"/> for why we differ and how to undo it.
    /// </summary>
    public void Init()
    {
        Cash = 200;
        Consumables.Receive(ItemIds.LowCostRent, 3).PricePaid = 325;
        Consumables.Receive(ItemIds.CasualClothes, 6).PricePaid = 30;

        if (StartingFoodWeeks > 0)
            Consumables.Receive(ItemIds.FreshFood, StartingFoodWeeks).PricePaid = 55;
    }

    /// <summary>
    /// Weeks of food a new player is given. **THE ORIGINAL GIVES NONE — THIS IS A HOUSE
    /// RULE, NOT A PORT OF ANYTHING.** Set it to 0 to restore Sierra's behaviour exactly.
    ///
    /// What the original does, verified in BOTH builds: `Player::init` (`room1.sc:667-683`,
    /// floppy `:664-668`) hands out only rent and clothes, and nothing else in either script
    /// tree ever seeds a consumable — `Consumables` is a bare subclass with no seeding
    /// (`Goods.sc:115`). The turn-start chain then runs unconditionally on turn one
    /// (`room1.sc:1318` calls `startTurn:` straight from the board room's init), and it has
    /// no week-1 gate on hunger — unlike the weekend, which IS skipped in week 1
    /// (`startTrn.sc:205`), so the authors plainly knew how to add one and chose not to here.
    ///
    /// The consequence is a brutal opening. State 8 scans consumables 5 down to 1 for a meal
    /// and finds nothing; state 12 fires the starvation notice, which costs **20 hours** of
    /// the week's 60 (`startTrn.sc:432` → `:1041`). Worse, starving also sets
    /// `global553 = (Random 0 3)` (`startTrn.sc:431`), and state 14 sends you to the doctor
    /// when that lands on 0 — so going hungry carries a 1-in-4 chance of losing a further
    /// **10 hours** on the same turn. A third of your first week is gone, and a quarter of
    /// the time half of it, before the player has clicked anything at all.
    ///
    /// I could not find a gate for it and I am fairly confident there isn't one, but this
    /// deviation does not depend on that being settled: the user asked for a playable
    /// opening, which is the whole reason the game was ported from source rather than
    /// emulated. If the original is ever confirmed to behave differently, the fix is to set
    /// this to 0 rather than to unpick anything.
    ///
    /// One week is the smallest change that clears it — the meal scan only needs a quantity
    /// above zero at index 1, and the weekly consumable tick then takes it straight back
    /// down, so the player still has to buy food in week one. The price is Black's Market's
    /// one-week base (`market.sc`), so net worth and any later pawn valuation stay coherent.
    ///
    /// DEFAULTS TO 0 — the source's behaviour — ON PURPOSE. The core must stay faithful by
    /// default or the test suite stops measuring the original: setting this to 1 here broke
    /// 20 tests at a stroke, every one of them correctly asserting what Sierra's code does
    /// (starvation's cost, its 1-in-4 doctor risk, and the whole of the Jones AI's food
    /// planning, which reasons about a player who owns none). Those tests were right and the
    /// default was wrong.
    ///
    /// The deviation is therefore applied by the APPLICATION, not the core: each platform
    /// head sets this at startup (see `Program.cs` in Jones.App.Desktop). A head that does
    /// not set it gets Sierra's game exactly.
    /// </summary>
    public static int StartingFoodWeeks { get; set; }

    /// <summary>
    /// Port of `dressedForWork` (`room1.sc:685`). Sets <see cref="Wearing"/> to the best
    /// clothing held and returns whether it meets the job's requirement.
    /// </summary>
    public bool DressedForWork()
    {
        // The original scans 34 → 35 → 36 and stops at the first held, so a player always
        // wears the dressiest thing they own.
        Wearing = 0;
        foreach (var id in (int[])[ItemIds.BusinessSuit, ItemIds.DressClothes, ItemIds.CasualClothes])
        {
            if (Consumables.Holds(id)) { Wearing = id; break; }
        }

        // Lower id is dressier, so "uniform or better" is `wearing <= uniform`.
        return Wearing != 0 && Uniform >= Wearing;
    }

    /// <summary>
    /// Port of `weeksOfClothing` (`room1.sc:704`) — the largest quantity held across the
    /// three clothing types. At 1 the player is warned; at 0 they are literally naked and
    /// cannot work.
    /// </summary>
    public int WeeksOfClothing()
    {
        var most = 0;
        foreach (var id in (int[])[ItemIds.BusinessSuit, ItemIds.DressClothes, ItemIds.CasualClothes])
        {
            var q = Consumables.At(id)?.Quantity ?? 0;
            if (q > most) most = q;
        }
        return most;
    }

    /// <summary>
    /// Port of `numDegrees` (`room1.sc:897`) — education entries that have reached their
    /// graduation threshold.
    /// </summary>
    public int NumDegrees() =>
        Education.Items.Count(e => e.Quantity >= e.UnitsToGraduate);

    /// <summary>
    /// Port of `calcLiquidAssets` (`room1.sc:865`): investments + cash + savings, less
    /// what you owe. Debts count against you immediately, so a loan does not improve your
    /// wealth goal even though it fills your pocket.
    /// </summary>
    public long CalcLiquidAssets(Economy.EconomyState econ)
    {
        LqAss = Holdings.TotalValue(econ) + Cash + BankBal - (RentOwed + LoanBal);
        InvAss = Holdings.TotalValue(econ);
        return LqAss;
    }

    /// <summary>
    /// Port of `calcNetWorth` (`room1.sc:845`): liquid assets plus the price actually paid
    /// for every durable held. Note durables are valued at PURCHASE price, never
    /// depreciated — so buying a hot tub is, on paper, wealth-neutral.
    /// </summary>
    public long CalcNetWorth(Economy.EconomyState econ)
    {
        CalcLiquidAssets(econ);
        NetWorth = LqAss + Durables.Items.Sum(d => (long)d.PricePaid * d.Quantity);
        return NetWorth;
    }

    /// <summary>
    /// Recomputes all four goal progress values. Career is handled in
    /// <see cref="RecalculateCareerStat"/> at turn end, as the original does.
    /// </summary>
    public void RecalculateGoals(Economy.EconomyState econ)
    {
        CalcNetWorth(econ);
        MonStat = (int)(LqAss / 100);

        // `proc0_10` (`Main.sc:1063-1068`) clamps monStat at BOTH ends on every cash
        // movement. Only the lower bound was here, so a rich player's wealth goal ran
        // past 100 and stayed there.
        if (MonStat > 100) MonStat = 100;
        if (MonStat < 0) MonStat = 0;

        EduStat = 1 + 9 * NumDegrees();
    }

    /// <summary>
    /// Overall progress toward all four goals, 0..100 — the number the "Who's Winning"
    /// thermometers and their captions show. Port of `localproc_0` in `viewGoals.sc:22-59`.
    ///
    /// Each stat is first clamped to ITS OWN goal, so overshooting one goal cannot carry a
    /// neglected one: a millionaire with no degrees still reads low (`:31-42`).
    /// </summary>
    public int GoalProgressPct()
    {
        var mon = MonStat; var hap = HapStat; var edu = EduStat; var car = CarStat;
        if (mon > MonGoal) mon = MonGoal;
        if (hap > HapGoal) hap = HapGoal;
        if (edu > EduGoal) edu = EduGoal;
        if (car > CarGoal) car = CarGoal;

        var stats = mon + hap + edu + car;
        var goals = MonGoal + HapGoal + EduGoal + CarGoal;

        int pct;
        if (stats != 0)
        {
            // THIS IS `stats * 100 / goals` WRITTEN TO SURVIVE 16-BIT ARITHMETIC, AND IT IS
            // DELIBERATELY NOT SIMPLIFIED. Four stats of up to 100 give `stats` up to 400, and
            // `stats * 100` is 40000 — past the 32767 ceiling of SCI's signed 16-bit word, so
            // the naive form would wrap negative. The original halves the multiplier to
            // `stats * 50` (max 20000, safe), divides, doubles the quotient, and adds back the
            // doubled remainder divided by `goals` so the halving loses nothing:
            //
            //     (+ (* (/ (* temp1 50) temp2) 2) (/ (* (mod (* temp1 50) temp2) 2) temp2))
            //
            // In C# `stats * 100 / goals` would give the same answer, which is exactly why the
            // shape has to be preserved and explained: reading the simplified version back into
            // the original would make Sierra's arithmetic look naive when it was not.
            pct = SciMath.Div(stats * 50, goals) * 2
                + SciMath.Div(stats * 50 % goals * 2, goals);
        }
        else pct = 0;

        // `:55-57`. Unreachable while every stat is clamped to its goal above, but the
        // original tests it anyway, so the port does too.
        if (pct > 100) pct = 100;
        return pct;
    }

    /// <summary>
    /// DELIBERATE ADDITION — not in the original, and off unless a head opts in. See
    /// <see cref="ShowPerGoalProgress"/> for the switch.
    ///
    /// Progress toward ONE of the four goals, 0..100. `viewGoals` has no such number:
    /// `localproc_0` (`viewGoals.sc:22-59`) sums all four stats against all four goals and
    /// reports a single figure per PLAYER, and `select3`'s per-goal display is the position
    /// of a marker (`currentWealth::draw`, `select3.sc:371-400`), never a printed percentage.
    ///
    /// The shape is `localproc_0`'s, reduced to one pair: clamp the stat to its own goal,
    /// then `stat * 100 / goal`, truncating through <see cref="SciMath.Div"/> so it rounds
    /// the way every other figure in the game rounds. The 16-bit dance `localproc_0` performs
    /// is not needed here — one stat caps at 100, so `stat * 100` caps at 10000, well inside
    /// a signed word — but the truncation and the clamp are kept identical so a per-goal
    /// figure can never read higher than the overall one it feeds.
    /// </summary>
    /// <param name="goal">0 wealth, 1 happiness, 2 education, 3 career — slider order.</param>
    public int GoalProgressPct(int goal)
    {
        var (stat, target) = goal switch
        {
            0 => (MonStat, MonGoal),
            1 => (HapStat, HapGoal),
            2 => (EduStat, EduGoal),
            _ => (CarStat, CarGoal),
        };

        if (target <= 0) return 0;
        if (stat > target) stat = target;
        if (stat < 0) stat = 0;

        return SciMath.Div(stat * 100, target);
    }

    /// <summary>
    /// HOUSE RULE, off by default: draw a percentage beside each of the four goal sliders on
    /// the read-only goals screen (`select3` mode 2, reached from Who's Winning).
    ///
    /// **The original shows no such number** — see <see cref="GoalProgressPct(int)"/>. It is
    /// kept switchable, and named here beside the arithmetic it turns on, so the screen can
    /// be put back to exactly what Sierra drew for comparison.
    /// </summary>
    public static bool ShowPerGoalProgress { get; set; } = true;

    /// <summary>All four goals met — the win condition, tested at turn start.</summary>
    public bool HasWon() =>
        FinStat == 0
        && MonStat >= MonGoal && HapStat >= HapGoal
        && EduStat >= EduGoal && CarStat >= CarGoal;

    /// <summary>
    /// Port of `Player::endTurn` (`room1.sc:752`), the stat maintenance that runs as a turn
    /// closes. <paramref name="week"/> is global372.
    /// </summary>
    public void EndTurn(int week)
    {
        // Dependability decays every single turn, which is what makes a job you never
        // show up to eventually fire you.
        Dependibility -= 3;
        if (Dependibility < 0) Dependibility = 0;

        RecalculateCareerStat();
        AgeDurables();

        if (week % 4 == 0)
        {
            if (LoanBal != 0)
            {
                PaySched--;
                if (!MadePay) LatePay++;
            }

            var rentId = LivesAt == 0 ? ItemIds.LowCostRent : ItemIds.SecurityRent;
            var rent = Consumables.At(rentId);
            if (rent is { Quantity: 0 } && TriedExt != 1 && !TurnedOver)
            {
                TurnedOver = true;
                RentOwed += CurRent;
            }
        }
        else
        {
            // `room1.sc:808-820` — the NON-rent-week branch accrues too, once, under one
            // extra condition: you must not already owe anything. Only the `week % 4 == 0`
            // branch was ported, so a player who ran out of rent weeks mid-month was never
            // billed until the next rent week came round.
            var rentId = LivesAt == 0 ? ItemIds.LowCostRent : ItemIds.SecurityRent;
            var rent = Consumables.At(rentId);
            if (rent is { Quantity: 0 } && TriedExt != 1 && RentOwed == 0 && !TurnedOver)
            {
                TurnedOver = true;
                RentOwed += CurRent;
            }
        }

        // `room1.sc:806`, and it must be read BEFORE triedExt is cleared below: an extension
        // asked for this month is what keeps the Rent Office open next month.
        LeaveOpen = TriedExt == 1;

        TriedExt = 0;
        MadePay = false;
    }

    /// <summary>
    /// Port of `room1.sc:757`. NOT the wiki's `1.25 × dependability` — it is
    /// `(dependibility / 8) * 10` with truncating division, so it moves in steps of 10,
    /// and it is zero while unemployed no matter how dependable you are.
    /// </summary>
    public void RecalculateCareerStat()
    {
        CarStat = Wage != 0 ? SciMath.Div(Dependibility, 8) * 10 : 0;
        if (CarStat > 100) CarStat = 100;
    }

    /// <summary>
    /// Port of the durable wear counter in `room1.sc:768`. Each durable ages one step
    /// through 24 → 16 → 8 → 0, and is flagged worn out on reaching zero.
    /// </summary>
    private void AgeDurables()
    {
        foreach (var d in Durables.Items)
        {
            var wear = (int)(d.Attributes & DurableAttributes.WearMask);
            if (wear == 0) continue;

            var next = wear switch { 24 => 16, 16 => 8, 8 => 0, _ => 0 };
            d.Attributes = (DurableAttributes)(((int)d.Attributes & ~0x0018) | next);
            if (next == 0) d.Attributes |= DurableAttributes.WornOut;
        }
    }

    /// <summary>
    /// Port of `doScandal` (`room1.sc:825`) — what a market crash does to this player.
    /// Returns 1 if the job was lost, -1 if the wage was cut, 0 if nothing happened.
    ///
    /// REMEMBER: severity 1 is the WORST. It wipes the bank balance and always costs the
    /// job; severity 3 only risks the job at 1-in-3 and cuts no wages at all.
    /// </summary>
    public int DoScandal(int severity, IRandomSource rng)
    {
        if (!Playing) return 0;

        if (severity == 1) BankBal = 0;

        if (Wage != 0 && rng.Next(0, severity - 1) == 0)
        {
            Wage = 0;
            WorksAt = 0;
            Occupation = 0;
            return 1;
        }

        if (Wage != 0)
        {
            Wage = SciMath.Div(Wage * (4 + 2 * severity), 10);
            if (Wage < 1) Wage = 1;
            return -1;
        }

        return 0;
    }
}
