using Jones.Core.Economy;
using Jones.Core.Model;
using Jones.Core.Save;
using Jones.Core.Sci;

namespace Jones.Core;

/// <summary>
/// Save and restore. The part of <see cref="Game"/> that reads and writes its own state —
/// kept in this file so the rest of the class stays about the rules, and made a partial
/// rather than a set of new public setters so that nothing else in the port can reach the
/// private turn latches.
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// A complete picture of this game. <paramref name="shell"/> carries the presentation
    /// layer's own per-turn state, which <c>Game</c> does not own and does not read.
    /// </summary>
    public GameSnapshot Capture(ShellSnapshot? shell = null) => new()
    {
        CurrentPlayerIndex = CurrentPlayerIndex,
        Week = Calendar.Week,

        Clock = new ClockSnapshot
        {
            HoursRemaining = Clock.HoursRemaining,
            SubHourTicks = Clock.SubHourTicks,
        },

        Economy = CaptureEconomy(Economy),
        Players = [.. Players.Select(CapturePlayer)],
        Winners = [.. Winners.Select(w => Players.IndexOf(w))],
        TurnedDown = [.. TurnedDown.Refused],
        JonesWorld = CaptureJonesWorld(JonesWorld),

        Latches = new LatchSnapshot
        {
            HapBaseball = _hapBaseball,
            HapTheatre = _hapTheatre,
            HapConcert = _hapConcert,
            HapLottery = _hapLottery,
            HapFood = _hapFood,
            HapDrinks = _hapDrinks,
            HapMeals = _hapMeals,

            MuggingPending = MuggingPending,
            LastMugging = LastMugging,
            BrokerChargedThisVisit = BrokerChargedThisVisit,

            LastWorkResult = (int)LastWorkResult,
            LastGarnished = LastGarnished,
            LastJobOutcome = (int)LastJobOutcome,
            LastRejection = LastRejection is { } r ? (int)r : null,
        },

        Shell = shell ?? new ShellSnapshot(),
    };

    /// <summary>
    /// Rebuilds a game from a snapshot.
    ///
    /// THE RANDOM SOURCE IS NOT RESTORED, BECAUSE IT IS NOT SAVED — see the note on
    /// <see cref="SaveStore"/>. The caller supplies a fresh one, exactly as it does for a
    /// new game.
    ///
    /// <see cref="LastTurnEvents"/> comes back EMPTY. It is the turn-start chain's report
    /// to the UI — the weekend, the newspaper and the notice sounds — and by the time a
    /// player can reach the Save menu the whole chain has already been presented. Replaying
    /// it on restore would show the week's weekend and headline a second time; re-running
    /// the chain would be worse still, since it would tick the economy again.
    /// </summary>
    public static Game Restore(GameSnapshot s, IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(s);

        // playerCount 0 builds the shell without inventing any players; the snapshot's own
        // are added below with their real state.
        var g = new Game(rng, 0);

        while (g.Calendar.Week < s.Week) g.Calendar.AdvanceWeek();

        g.Clock.HoursRemaining = s.Clock.HoursRemaining;
        g.Clock.SubHourTicks = s.Clock.SubHourTicks;

        RestoreEconomy(g.Economy, s.Economy);

        foreach (var ps in s.Players) g.Players.Add(RestorePlayer(ps));

        g.CurrentPlayerIndex = s.Players.Count == 0
            ? 0
            : Math.Clamp(s.CurrentPlayerIndex, 0, s.Players.Count - 1);

        foreach (var i in s.Winners)
            if (i >= 0 && i < g.Players.Count && !g.Winners.Contains(g.Players[i]))
                g.Winners.Add(g.Players[i]);

        g.TurnedDown.Clear();
        foreach (var jobNum in s.TurnedDown) g.TurnedDown.Refuse(jobNum);

        RestoreJonesWorld(g.JonesWorld, s.JonesWorld);

        g._hapBaseball = s.Latches.HapBaseball;
        g._hapTheatre = s.Latches.HapTheatre;
        g._hapConcert = s.Latches.HapConcert;
        g._hapLottery = s.Latches.HapLottery;
        g._hapFood = s.Latches.HapFood;
        g._hapDrinks = s.Latches.HapDrinks;
        g._hapMeals = s.Latches.HapMeals;

        g.MuggingPending = s.Latches.MuggingPending;
        g.LastMugging = s.Latches.LastMugging;
        g.BrokerChargedThisVisit = s.Latches.BrokerChargedThisVisit;
        g.LastWorkResult = (Employment.WorkResult)s.Latches.LastWorkResult;
        g.LastGarnished = s.Latches.LastGarnished;
        g.LastJobOutcome = (JobOutcome)s.Latches.LastJobOutcome;
        g.LastRejection = s.Latches.LastRejection is { } r ? (RejectionReason)r : null;

        g.LastTurnEvents = [];

        return g;
    }

    // --- Economy -------------------------------------------------------------

    private static EconomySnapshot CaptureEconomy(EconomyState e) => new()
    {
        Volatility = e.Volatility,
        CrashSeverity = e.CrashSeverity,
        Boom = e.Boom,
        Headline = e.Headline,
        Main = Snap(e.Main),
        Invest = Snap(e.Invest),
        Goods = Snap(e.Goods),
        Gold = Snap(e.Gold),
        Silver = Snap(e.Silver),
        Pork = Snap(e.Pork),
        BlueChip = Snap(e.BlueChip),
        Penny = Snap(e.Penny),
    };

    private static IndexSnapshot Snap(EconomicIndex ix) =>
        new() { Index = ix.Index, Reading = ix.Reading };

    private static void RestoreEconomy(EconomyState e, EconomySnapshot s)
    {
        e.Volatility = s.Volatility;
        e.CrashSeverity = s.CrashSeverity;
        e.Boom = s.Boom;
        e.Headline = s.Headline;

        Apply(e.Main, s.Main);
        Apply(e.Invest, s.Invest);
        Apply(e.Goods, s.Goods);
        Apply(e.Gold, s.Gold);
        Apply(e.Silver, s.Silver);
        Apply(e.Pork, s.Pork);
        Apply(e.BlueChip, s.BlueChip);
        Apply(e.Penny, s.Penny);

        static void Apply(EconomicIndex ix, IndexSnapshot v)
        {
            ix.Index = v.Index;
            ix.Reading = v.Reading;
        }
    }

    // --- Jones's globals -----------------------------------------------------

    private static JonesWorldSnapshot CaptureJonesWorld(JonesWorld w) => new()
    {
        GoodsIndex = w.GoodsIndex,
        InvestIndex = w.InvestIndex,
        MainTrend = w.MainTrend,
        Week = w.Week,
        HoursUsed = w.HoursUsed,
        SubHourTicks = w.SubHourTicks,
        TicksPerHour = w.TicksPerHour,
        CurrentPlace = w.CurrentPlace,
        WorkedThisTurn = w.WorkedThisTurn,
        UsedEmploymentOffice = w.UsedEmploymentOffice,
        UsedZMart = w.UsedZMart,
        UsedRentOffice = w.UsedRentOffice,
        BoughtAppliance = w.BoughtAppliance,
        BoughtInvestments = w.BoughtInvestments,
        SoldInvestments = w.SoldInvestments,
        HappinessGoalMet = w.HappinessGoalMet,
        MoneyIsAllThatIsLeft = w.MoneyIsAllThatIsLeft,
    };

    private static void RestoreJonesWorld(JonesWorld w, JonesWorldSnapshot s)
    {
        w.GoodsIndex = s.GoodsIndex;
        w.InvestIndex = s.InvestIndex;
        w.MainTrend = s.MainTrend;
        w.Week = s.Week;
        w.HoursUsed = s.HoursUsed;
        w.SubHourTicks = s.SubHourTicks;
        w.TicksPerHour = s.TicksPerHour;
        w.CurrentPlace = s.CurrentPlace;
        w.WorkedThisTurn = s.WorkedThisTurn;
        w.UsedEmploymentOffice = s.UsedEmploymentOffice;
        w.UsedZMart = s.UsedZMart;
        w.UsedRentOffice = s.UsedRentOffice;
        w.BoughtAppliance = s.BoughtAppliance;
        w.BoughtInvestments = s.BoughtInvestments;
        w.SoldInvestments = s.SoldInvestments;
        w.SetGoalFlags(s.HappinessGoalMet, s.MoneyIsAllThatIsLeft);
    }

    // --- Players -------------------------------------------------------------

    private static PlayerSnapshot CapturePlayer(Player p) => new()
    {
        ActualName = p.ActualName,
        Playing = p.Playing,
        IsJones = p.IsJones,
        Location = (int)p.Location,

        MonGoal = p.MonGoal, HapGoal = p.HapGoal, EduGoal = p.EduGoal, CarGoal = p.CarGoal,
        MonStat = p.MonStat, HapStat = p.HapStat, EduStat = p.EduStat, CarStat = p.CarStat,
        FinStat = p.FinStat,

        Cash = p.Cash, NetWorth = p.NetWorth, LqAss = p.LqAss, InvAss = p.InvAss,
        BankBal = p.BankBal,

        LivesAt = p.LivesAt, CurRent = p.CurRent, RentOwed = p.RentOwed,
        RentExt = p.RentExt, TriedExt = p.TriedExt, TurnedOver = p.TurnedOver,
        LeaveOpen = p.LeaveOpen,

        WorksAt = p.WorksAt, Wage = p.Wage, BaseWage = p.BaseWage,
        Occupation = p.Occupation, Raises = p.Raises,
        Uniform = p.Uniform, Wearing = p.Wearing, NakedCount = p.NakedCount,

        LoanBal = p.LoanBal, LatePay = p.LatePay, PaySched = p.PaySched, MadePay = p.MadePay,

        Relax = p.Relax, Dependibility = p.Dependibility, MinDepend = p.MinDepend,
        Experience = p.Experience, MaxExper = p.MaxExper, NotEnoughEd = p.NotEnoughEd,

        NeedEd1 = p.NeedEd1, NeedEd2 = p.NeedEd2,
        ExpCredit = p.ExpCredit, EduCredit = p.EduCredit, Enrollments = p.Enrollments,
        XCred = p.XCred, CoursesDone = p.CoursesDone,

        Consumables = CaptureItems(p.Consumables),
        Durables = CaptureItems(p.Durables),
        Education = CaptureItems(p.Education),
        Holdings =
        [
            .. p.Holdings.AllHeld.Select(h => new HoldingSnapshot
            {
                Instrument = (int)h.Instrument,
                Shares = h.Shares,
            })
        ],
    };

    private static Player RestorePlayer(PlayerSnapshot s)
    {
        // Built field by field rather than through `Player.Init`, which would post the
        // starting $200 and the opening rent and clothing entries on top of the saved ones.
        var p = new Player
        {
            ActualName = s.ActualName,
            Playing = s.Playing,
            IsJones = s.IsJones,
            Location = (LocationId)s.Location,

            MonGoal = s.MonGoal, HapGoal = s.HapGoal, EduGoal = s.EduGoal, CarGoal = s.CarGoal,
            MonStat = s.MonStat, HapStat = s.HapStat, EduStat = s.EduStat, CarStat = s.CarStat,
            FinStat = s.FinStat,

            Cash = s.Cash, NetWorth = s.NetWorth, LqAss = s.LqAss, InvAss = s.InvAss,
            BankBal = s.BankBal,

            LivesAt = s.LivesAt, CurRent = s.CurRent, RentOwed = s.RentOwed,
            RentExt = s.RentExt, TriedExt = s.TriedExt, TurnedOver = s.TurnedOver,
            LeaveOpen = s.LeaveOpen,

            WorksAt = s.WorksAt, Wage = s.Wage, BaseWage = s.BaseWage,
            Occupation = s.Occupation, Raises = s.Raises,
            Uniform = s.Uniform, Wearing = s.Wearing, NakedCount = s.NakedCount,

            LoanBal = s.LoanBal, LatePay = s.LatePay, PaySched = s.PaySched,
            MadePay = s.MadePay,

            Relax = s.Relax, Dependibility = s.Dependibility, MinDepend = s.MinDepend,
            Experience = s.Experience, MaxExper = s.MaxExper, NotEnoughEd = s.NotEnoughEd,

            NeedEd1 = s.NeedEd1, NeedEd2 = s.NeedEd2,
            ExpCredit = s.ExpCredit, EduCredit = s.EduCredit, Enrollments = s.Enrollments,
            XCred = s.XCred, CoursesDone = s.CoursesDone,
        };

        RestoreItems(p.Consumables, s.Consumables);
        RestoreItems(p.Durables, s.Durables);
        RestoreItems(p.Education, s.Education);

        foreach (var h in s.Holdings) p.Holdings.Add((Instrument)h.Instrument, h.Shares);

        return p;
    }

    private static List<ItemSnapshot> CaptureItems(ItemList list) =>
    [
        .. list.Items.Select(i => new ItemSnapshot
        {
            IndexNum = i.IndexNum,
            Quantity = i.Quantity,
            PricePaid = i.PricePaid,
            Attributes = (int)i.Attributes,
            UnitsToGraduate = i.UnitsToGraduate,
            RedemptionPrice = i.RedemptionPrice,
        })
    ];

    private static void RestoreItems(ItemList list, List<ItemSnapshot> items)
    {
        // Order is preserved: `ItemList` is a list, not a set, and the pawn shop's resale
        // rack and the stats screen both walk it in order.
        foreach (var i in items)
            list.Add(new Item(i.IndexNum, i.Quantity)
            {
                PricePaid = i.PricePaid,
                Attributes = (DurableAttributes)i.Attributes,
                UnitsToGraduate = i.UnitsToGraduate,
                RedemptionPrice = i.RedemptionPrice,
            });
    }
}
