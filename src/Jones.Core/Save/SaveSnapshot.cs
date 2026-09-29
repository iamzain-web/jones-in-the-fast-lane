using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jones.Core.Save;

/// <summary>
/// THE SAVE FORMAT IS NOT THE ORIGINAL'S, DELIBERATELY.
///
/// Script 990 does not define one. `proc990_0` (`Save.sc:28`) hands the whole job to the
/// interpreter — `(SaveGame (gGame name:) 1 (gGame name:) @global539)` — which snapshots
/// SCI's heap and its script-variable blocks verbatim. That layout is a picture of the 1990
/// interpreter's memory, and this port's state is C# objects, so there is nothing to be
/// bug-compatible WITH: the bytes could only be reproduced by reproducing the interpreter.
///
/// What IS carried over from the original is the SHAPE of the feature, not the bytes:
/// one slot. `SaveGame`, `CheckSaveGame` and `RestoreGame` are all called with the literal
/// save number **1** (`Save.sc:28`, `:55`, `:56`), the description is always the game name,
/// and the player is never shown a file list — `GetSaveFiles` (`Save.sc:53`) is used purely
/// as an existence test before the restore is attempted. Hence text 997[11], which the
/// Save menu item prints before it does anything: "Saving a game will overwrite a
/// previously saved game. Continue?"
/// </summary>
public static class SaveFormat
{
    /// <summary>
    /// The only version this build writes, and the only one it will read.
    ///
    /// VERSIONED FROM DAY ONE AND CHECKED BEFORE ANYTHING IS DESERIALISED. A file whose
    /// version is not this one is refused outright (<see cref="SaveVersionException"/>)
    /// rather than deserialised on a best-effort basis: a half-loaded game is worse than no
    /// game, because the damage shows up turns later as impossible money or a lost degree.
    ///
    /// Bump this for ANY change that alters the meaning of an existing field or removes
    /// one. Adding a new optional field with a safe default does not need a bump — but
    /// silently defaulting a field that the game's rules depend on does, so when in doubt
    /// bump it.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Written into the file and checked on load, so that pointing the restore at some
    /// other program's JSON fails as a wrong file rather than as a corrupt save.
    /// </summary>
    public const string Magic = "jones-in-the-fast-lane";

    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}

/// <summary>A save file exists but this build cannot read it.</summary>
public sealed class SaveVersionException(string message) : Exception(message);

/// <summary>The envelope. Only <see cref="Version"/> is read before anything else is.</summary>
public sealed class SaveFile
{
    public int Version { get; set; } = SaveFormat.Version;
    public string Game { get; set; } = SaveFormat.Magic;
    public DateTimeOffset SavedUtc { get; set; } = DateTimeOffset.UtcNow;
    public GameSnapshot State { get; set; } = new();
}

/// <summary>
/// Everything the port needs to reconstruct a game in progress. One class per aggregate so
/// that a field added to a model shows up as a compile error in exactly one place here.
/// </summary>
public sealed class GameSnapshot
{
    public int CurrentPlayerIndex { get; set; }
    public int Week { get; set; } = 1;

    public ClockSnapshot Clock { get; set; } = new();
    public EconomySnapshot Economy { get; set; } = new();
    public List<PlayerSnapshot> Players { get; set; } = [];

    /// <summary>Indices into <see cref="Players"/>, in finishing order (`Game.Winners`).</summary>
    public List<int> Winners { get; set; } = [];

    /// <summary>`turnedDown` — job numbers refused to the current player this turn.</summary>
    public List<int> TurnedDown { get; set; } = [];

    public JonesWorldSnapshot JonesWorld { get; set; } = new();
    public LatchSnapshot Latches { get; set; } = new();
    public ShellSnapshot Shell { get; set; } = new();
}

/// <summary>global323 / global324 — see <c>GameClock</c>.</summary>
public sealed class ClockSnapshot
{
    public int HoursRemaining { get; set; } = 60;
    public int SubHourTicks { get; set; }
}

/// <summary>
/// The economy. All eight indices carry BOTH their reading and their trend index, because
/// the trend is what the next tick's random walk starts from and what the newspaper's
/// headline selection compares — saving only the readings would reset every index to a flat
/// trend on restore and visibly change the following week.
///
/// <c>High</c>, <c>Low</c> and <c>Adjustment</c> are deliberately NOT saved: they are
/// recomputed from the reading and the trend at the top of every
/// <c>EconomicIndex.Init</c> (`economicIndex.sc:66-128`) and are never read across ticks.
/// </summary>
public sealed class EconomySnapshot
{
    public int Volatility { get; set; } = 1;
    public int CrashSeverity { get; set; }
    public bool Boom { get; set; }
    public int Headline { get; set; }

    public IndexSnapshot Main { get; set; } = new();
    public IndexSnapshot Invest { get; set; } = new();
    public IndexSnapshot Goods { get; set; } = new();
    public IndexSnapshot Gold { get; set; } = new();
    public IndexSnapshot Silver { get; set; } = new();
    public IndexSnapshot Pork { get; set; } = new();
    public IndexSnapshot BlueChip { get; set; } = new();
    public IndexSnapshot Penny { get; set; } = new();
}

public sealed class IndexSnapshot
{
    public int Index { get; set; }
    public int Reading { get; set; } = 100;
}

public sealed class PlayerSnapshot
{
    public string ActualName { get; set; } = "";
    public bool Playing { get; set; }
    public bool IsJones { get; set; }
    public int Location { get; set; }

    public int MonGoal { get; set; }
    public int HapGoal { get; set; }
    public int EduGoal { get; set; }
    public int CarGoal { get; set; }

    public int MonStat { get; set; }
    public int HapStat { get; set; }
    public int EduStat { get; set; }
    public int CarStat { get; set; }
    public int FinStat { get; set; }

    public long Cash { get; set; }
    public long NetWorth { get; set; }
    public long LqAss { get; set; }
    public long InvAss { get; set; }
    public long BankBal { get; set; }

    public int LivesAt { get; set; }
    public int CurRent { get; set; }
    public int RentOwed { get; set; }
    public int RentExt { get; set; }
    public int TriedExt { get; set; }
    public bool TurnedOver { get; set; }
    public bool LeaveOpen { get; set; }

    public int WorksAt { get; set; }
    public int Wage { get; set; }
    public int BaseWage { get; set; }
    public int Occupation { get; set; }
    public int Raises { get; set; }
    public int Uniform { get; set; }
    public int Wearing { get; set; }

    /// <summary>`nakedCount` — state 34's run of broke-and-naked turns.</summary>
    public int NakedCount { get; set; }

    public int LoanBal { get; set; }
    public int LatePay { get; set; }
    public int PaySched { get; set; }
    public bool MadePay { get; set; }

    public int Relax { get; set; }
    public int Dependibility { get; set; }
    public int MinDepend { get; set; }
    public int Experience { get; set; }
    public int MaxExper { get; set; }
    public bool NotEnoughEd { get; set; }

    public int NeedEd1 { get; set; }
    public int NeedEd2 { get; set; }
    public int ExpCredit { get; set; }
    public int EduCredit { get; set; }
    public int Enrollments { get; set; }
    public int XCred { get; set; }
    public int CoursesDone { get; set; }

    public List<ItemSnapshot> Consumables { get; set; } = [];
    public List<ItemSnapshot> Durables { get; set; } = [];
    public List<ItemSnapshot> Education { get; set; } = [];

    /// <summary>Shares held, keyed by the <c>Instrument</c> enum value.</summary>
    public List<HoldingSnapshot> Holdings { get; set; } = [];
}

/// <summary>
/// One inventory entry. <see cref="Attributes"/> carries the pawn ticket: bits 3-4 are the
/// 24 → 16 → 8 → 0 countdown set by `pawnShop.sc:687` and aged a step a week in
/// <c>Player.EndTurn</c>, and bit 6 is the forfeit flag. <see cref="RedemptionPrice"/> is
/// set at the same site and is meaningless without them, so the three travel together.
/// </summary>
public sealed class ItemSnapshot
{
    public int IndexNum { get; set; }
    public int Quantity { get; set; }
    public int PricePaid { get; set; }
    public int Attributes { get; set; }
    public int UnitsToGraduate { get; set; }
    public int RedemptionPrice { get; set; }
}

public sealed class HoldingSnapshot
{
    public int Instrument { get; set; }
    public int Shares { get; set; }
}

/// <summary>
/// The globals Jones reads that are not on the Player. The six "already done this" latches
/// are per-turn and must round-trip: restoring with them cleared would let the computer
/// player shop at Z-Mart or visit the broker a second time in the same turn.
/// </summary>
public sealed class JonesWorldSnapshot
{
    public int GoodsIndex { get; set; } = 100;
    public int InvestIndex { get; set; } = 100;
    public int MainTrend { get; set; }
    public int Week { get; set; } = 1;
    public int HoursUsed { get; set; }
    public int SubHourTicks { get; set; }
    public int TicksPerHour { get; set; } = 14;
    public int CurrentPlace { get; set; }

    public bool WorkedThisTurn { get; set; }
    public bool UsedEmploymentOffice { get; set; }
    public bool UsedZMart { get; set; }
    public bool UsedRentOffice { get; set; }
    public bool BoughtAppliance { get; set; }
    public bool BoughtInvestments { get; set; }
    public bool SoldInvestments { get; set; }

    public bool HappinessGoalMet { get; set; }
    public bool MoneyIsAllThatIsLeft { get; set; }
}

/// <summary>
/// The per-turn and per-visit latches that live on <c>Game</c> itself: globals 466-472
/// (`startTrn.sc:176-178`), the bank's pending mugging (`bank.sc:120-133`) and the broker's
/// once-a-visit charge (`local0` in `bank.sc`). Plus the last-action results the UI reports.
/// </summary>
public sealed class LatchSnapshot
{
    public bool HapBaseball { get; set; }   // global466
    public bool HapTheatre { get; set; }    // global467
    public bool HapConcert { get; set; }    // global468
    public bool HapLottery { get; set; }    // global469
    public bool HapFood { get; set; }       // global470
    public bool HapDrinks { get; set; }     // global471
    public bool HapMeals { get; set; }      // global472

    public bool MuggingPending { get; set; }
    public bool LastMugging { get; set; }
    public bool BrokerChargedThisVisit { get; set; }

    public int LastWorkResult { get; set; }
    public int LastGarnished { get; set; }
    public int LastJobOutcome { get; set; }

    /// <summary>Null when the last application was not refused on its merits.</summary>
    public int? LastRejection { get; set; }
}

/// <summary>
/// State that lives in the presentation layer rather than in <c>Game</c>, but which is
/// nonetheless part of the turn the player is in the middle of. <c>Jones.Core</c> only
/// carries these values; it never interprets them.
/// </summary>
public sealed class ShellSnapshot
{
    /// <summary>The once-a-turn +2 Happiness for relaxing at home has been taken.</summary>
    public bool RelaxedThisTurn { get; set; }

    /// <summary>A shift has been worked this turn, which suppresses the repeat warning.</summary>
    public bool WorkedThisTurn { get; set; }

    /// <summary>The garnishment line has already been spoken this turn.</summary>
    public bool GarnishSpokenThisTurn { get; set; }

    /// <summary>
    /// Which weekend event came up last, so the next roll can avoid repeating it. Rolled
    /// once per turn and read on the following turn, so it is turn-spanning state.
    /// </summary>
    public int LastWeekendId { get; set; }

    /// <summary>
    /// Z-Mart's six shelf lines for the turn in progress, by name. They are rerolled at the
    /// start of every turn, so restoring without them would change the shop under a player
    /// who saved while deciding what to buy.
    /// </summary>
    public List<string> ZMartStock { get; set; } = [];
}
