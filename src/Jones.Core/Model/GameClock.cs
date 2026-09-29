namespace Jones.Core.Model;

/// <summary>
/// The turn clock. Each player's turn is 60 Hours (`startTrn.sc` / MECHANICS.md §1); Hours
/// are spent on travel and actions, and the turn ends when they run out.
///
/// The subtlety that shapes all of Jones's strategy: the clock is a budget, not a timer.
/// Entering a building costs 2 Hours EVERY time, so stepping out and back in to fetch
/// something you forgot costs the same as a third of a work session.
/// </summary>
public sealed class GameClock
{
    public const int HoursPerTurn = 60;

    /// <summary>Entering any location, every time. MECHANICS.md §6.</summary>
    public const int EnterLocationCost = 2;

    /// <summary>A full work session. Partial sessions pay pro rata.</summary>
    public const int WorkSessionCost = 6;

    /// <summary>One university lesson.</summary>
    public const int LessonCost = 6;

    /// <summary>One relaxation session at your apartment.</summary>
    public const int RelaxCost = 6;

    /// <summary>
    /// A doctor visit, imposed rather than chosen. `startTrn.sc:1035` — the turn-start
    /// notice for register 3 charges `(gTimeKeep doit: 10)`.
    /// </summary>
    public const int DoctorVisitCost = 10;

    /// <summary>
    /// Going hungry. `startTrn.sc:1041` — register 0 charges `(gTimeKeep doit: 20)`, a
    /// third of the week. This is the strongest incentive in the game to keep food in.
    /// </summary>
    public const int StarvationCost = 20;

    /// <summary>
    /// One visit to the Employment Office counter. `employment.sc:78` `visitTime 4`, spent
    /// at `employment.sc:168` BEFORE `qualify:` runs — so it is charged whether you are
    /// hired, refused or merely asking for a raise.
    /// </summary>
    public const int JobApplicationCost = 4;

    /// <summary>Pressing *Apply For Loan*, per press, whatever the answer (`bank.sc:342`).</summary>
    public const int LoanApplicationCost = 2;

    /// <summary>Seeing the broker, ONCE per bank visit (`bank.sc:418-420`, the `local0` latch).</summary>
    public const int BrokerVisitCost = 2;

    /// <summary>
    /// `(marble moveSpeed:)`. The marble instance declares `moveSpeed 1` (`room1.sc:1084`)
    /// and nothing but the animation-speed menu changes it (`Menu.sc:317`), which this port
    /// does not model.
    /// </summary>
    public const int MarbleMoveSpeed = 1;

    /// <summary>
    /// global475 — `(* (marble moveSpeed:) 14)` (`room1.sc:1319`). The hour ticks over when
    /// `(> (++ global324) global475)` (`room1.sc:1488`), so it takes FIFTEEN ticks, and
    /// `marblePath.sc:42-46` advances exactly one path step per tick. Fifteen path steps to
    /// the hour; a full 170-step lap is a shade over 11 hours.
    /// </summary>
    public const int TicksPerHour = MarbleMoveSpeed * 14;

    /// <summary>
    /// The setter is <c>internal</c> for one caller only: <c>Game.Restore</c>, which puts a
    /// saved clock back. Nothing in the rules may assign it — spending goes through
    /// <see cref="Spend"/> so the clamp at zero cannot be bypassed.
    /// </summary>
    public int HoursRemaining { get; internal set; } = HoursPerTurn;

    /// <summary>global323 — hours USED, counting up, which is how the original stores it.</summary>
    public int HoursUsed => HoursPerTurn - HoursRemaining;

    /// <summary>
    /// global324 — the sub-hour tick accumulator. It is NOT per-journey: it is zeroed only
    /// at turn start (`room1.sc:1094`) and carries across every journey of the turn. A
    /// two-step hop can therefore cost nothing at all, and three short hops can cost one
    /// hour between them. There is no rounding and no per-journey floor anywhere.
    /// </summary>
    public int SubHourTicks { get; internal set; }

    /// <summary>The turn is over once the budget is exhausted.</summary>
    public bool TurnOver => HoursRemaining <= 0;

    /// <summary>
    /// Some actions stay available after the clock runs out — paying university enrollment
    /// fees and buying at Z-Mart, per MECHANICS.md §1. Those call nothing here.
    /// </summary>
    public void Reset()
    {
        HoursRemaining = HoursPerTurn;
        SubHourTicks = 0;
    }

    /// <summary>
    /// Walks the marble <paramref name="steps"/> path steps, ticking `gTimeKeep doit:` with
    /// no argument once per step exactly as `room1.sc:1374-1377` does while the marble has
    /// a mover. Verbatim from `room1.sc:1488-1491`:
    ///
    ///     ((> (++ global324) global475) (++ global323) (= global324 0))
    /// </summary>
    public void SpendTravelSteps(int steps)
    {
        for (var i = 0; i < steps; i++)
        {
            if (++SubHourTicks > TicksPerHour)
            {
                SubHourTicks = 0;
                Spend(1);
            }
        }
    }

    /// <summary>
    /// Spends hours, clamping at zero. Overspending is legal and is how the original
    /// behaves: a lesson costs its full 6 Hours even with fewer than 6 left, without
    /// penalty (`Hi-Tech U`, MECHANICS.md §5).
    /// </summary>
    public void Spend(int hours)
    {
        HoursRemaining -= hours;
        if (HoursRemaining < 0) HoursRemaining = 0;
    }

    /// <summary>
    /// Pay for a work session: `wage × 8`, scaled down pro rata when fewer than 6 Hours
    /// remain (MECHANICS.md §4). Truncating division, as the original.
    /// </summary>
    public static int WorkPayout(int wage, int hoursRemaining)
    {
        var full = wage * 8;
        if (hoursRemaining >= WorkSessionCost) return full;
        return full * hoursRemaining / WorkSessionCost;
    }
}

/// <summary>
/// Week and month tracking (global372). A month is four weeks, and the fourth is the one
/// that hurts: rent falls due and loan payments are checked.
/// </summary>
public sealed class GameCalendar
{
    public int Week { get; private set; } = 1;

    /// <summary>
    /// True in the fourth week of a month, when rent is due and the Rent Office opens.
    /// The original tests `(not (mod global372 4))` — i.e. week divisible by 4.
    /// </summary>
    public bool IsRentWeek => Week % 4 == 0;

    /// <summary>
    /// Crashes and booms cannot fire before week 8, so the opening of the game is
    /// guaranteed calm (`economicIndex.sc:213`).
    /// </summary>
    public bool CatastrophesEnabled => Week >= 8;

    public void AdvanceWeek() => Week++;
}
