namespace Jones.Core.Model;

/// <summary>
/// Something that happened to a player during the turn-start chain. The core reports these
/// rather than displaying anything, so the same sequence drives the UI, the AI opponent and
/// the headless harness. Each maps to a notice the original shows.
/// </summary>
public abstract record TurnStartEvent
{
    /// <summary>All four goals met — the game is over for this player.</summary>
    public sealed record Won : TurnStartEvent;

    public sealed record LotteryWin(int Amount) : TurnStartEvent;

    public sealed record ComputerIncome(int Amount) : TurnStartEvent;

    /// <summary>Wild Willy emptied the apartment. `Items` are the durable ids taken.</summary>
    public sealed record Robbed(IReadOnlyList<int> Items) : TurnStartEvent;

    /// <summary>No refrigerator — the lot went off.</summary>
    public sealed record AllFoodSpoiled : TurnStartEvent;

    /// <summary>More fresh food than the fridge (and freezer) could hold.</summary>
    public sealed record SomeFoodSpoiled(int KeptQuantity) : TurnStartEvent;

    public sealed record Starved : TurnStartEvent;

    public sealed record DoctorVisit(int Cost) : TurnStartEvent;

    public sealed record RentDue(int Amount) : TurnStartEvent;

    /// <summary>One week of clothing left — a warning, not yet a problem.</summary>
    public sealed record ClothingLow : TurnStartEvent;

    /// <summary>No clothing at all. The player cannot work.</summary>
    public sealed record Naked : TurnStartEvent;

    public sealed record LoanPaymentDemanded : TurnStartEvent;

    public sealed record LoanOverdue : TurnStartEvent;

    public sealed record ApplianceBroke(int ItemId, int RepairCost) : TurnStartEvent;

    /// <summary>Market crash fallout. <paramref name="Outcome"/> is doScandal's return.</summary>
    public sealed record CrashFallout(int Severity, int Outcome) : TurnStartEvent;

    /// <summary>An economic boom, which only pays players holding over $1000 in stock.</summary>
    public sealed record Boom : TurnStartEvent;

    /// <summary>
    /// State 34 (`startTrn.sc:827-880`): a second consecutive turn with no clothing and under
    /// $300 to your name, and "a favorite relative" sends enough to re-clothe you — the
    /// economy-adjusted price of the outfit your JOB demands, plus $1-100. Notice register 12,
    /// view 322.
    /// </summary>
    public sealed record RelativeGift(int Amount) : TurnStartEvent;
}
