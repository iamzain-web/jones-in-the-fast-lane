namespace Jones.Core.Model;

/// <summary>The nine workplaces that offer jobs.</summary>
public enum Workplace
{
    ZMart,
    MonolithBurgers,
    QtClothing,
    SocketCity,
    HiTechU,
    Factory,
    Bank,
    BlacksMarket,
    RentOffice,
}

/// <summary>
/// One job. Ported from the `JobDItem` instances in the nine `*Jobs.sc` scripts.
/// Class defaults (`employment.sc:75`) are dependability 10, experience 10, no degrees,
/// uniform 36 (casual); those are filled in explicitly here rather than left implicit.
/// </summary>
/// <param name="OccupationId">`indexNum` — the occupation title's id in text resource 700.</param>
/// <param name="JobNum">`jobNum` — unique slot id, used by the turned-down tracker.</param>
public sealed record Job(
    Workplace Workplace,
    string Title,
    int BaseWage,
    int ReqDependibility,
    int ReqExperience,
    int ReqDegree1,
    int ReqDegree2,
    int Uniform,
    int OccupationId,
    int JobNum);

/// <summary>
/// All 39 jobs, verified against the source. Wages and requirements reproduce the
/// published job table exactly.
///
/// THE TITLES ARE TEXT RESOURCE 700 at the job's own `OccupationId`. `inventories.sc:37`
/// prints the current job with `Format … 231 2 700 (occupation)`, so 700[45] is
/// `Assist. Manager`, 700[52] `Machinist Helper`, 700[53] `Exec. Secretary`,
/// 700[56] `Department Mgr.`, 700[57] `General Mgr.`, 700[63] `Apartment Mgr` and
/// 700[64] `Repairman`. Spelling them out in full was mine. The padded job-list labels in
/// `ItemText` are the OTHER spelling — the `text` property of each `JobDItem` — and are
/// keyed off these titles, so the two tables must stay in step.
/// </summary>
public static class Jobs
{
    private const int Casual = ItemIds.CasualClothes;   // 36
    private const int Dress = ItemIds.DressClothes;     // 35
    private const int Suit = ItemIds.BusinessSuit;      // 34

    public static readonly Job[] All =
    [
        // --- Z-Mart -------------------------------------------------------
        new(Workplace.ZMart, "Clerk",              5, 10, 10, 0, 0, Casual, 42, 1),
        new(Workplace.ZMart, "Assist. Manager",  7, 20, 20, 0, 0, Dress,  45, 2),
        new(Workplace.ZMart, "Manager",            8, 30, 30, Degrees.JuniorCollege, 0, Suit, 46, 3),

        // --- Monolith Burgers ---------------------------------------------
        // The Cook is the game's safety net: experience requirement 0, and it bypasses
        // the "no openings" roll entirely (`employment.sc:122`).
        new(Workplace.MonolithBurgers, "Cook",              5, 10,  0, 0, 0, Casual, 44, 4),
        new(Workplace.MonolithBurgers, "Clerk",             6, 20, 10, 0, 0, Casual, 42, 5),
        new(Workplace.MonolithBurgers, "Assist. Manager", 7, 30, 20, 0, 0, Casual, 45, 6),
        new(Workplace.MonolithBurgers, "Manager",           8, 40, 30, Degrees.JuniorCollege, 0, Dress, 46, 7),

        // --- QT Clothing --------------------------------------------------
        new(Workplace.QtClothing, "Janitor",           6, 20, 10, 0, 0, Casual, 43, 38),
        new(Workplace.QtClothing, "Salesperson",       8, 30, 30, 0, 0, Dress,  47, 8),
        new(Workplace.QtClothing, "Assist. Manager", 9, 40, 40, Degrees.JuniorCollege, 0, Suit, 45, 9),
        new(Workplace.QtClothing, "Manager",          12, 50, 50, Degrees.BusinessAdmin,  0, Suit, 46, 10),

        // --- Socket City ---------------------------------------------------
        new(Workplace.SocketCity, "Clerk",                  6, 20, 10, 0, 0, Casual, 42, 39),
        new(Workplace.SocketCity, "Salesperson",            7, 30, 20, 0, 0, Dress,  47, 11),
        new(Workplace.SocketCity, "Repairman", 11, 40, 40, Degrees.Electronics, 0, Casual, 64, 12),
        new(Workplace.SocketCity, "Manager",               14, 40, 40, Degrees.JuniorCollege, Degrees.Electronics, Suit, 46, 13),

        // --- Hi-Tech U ------------------------------------------------------
        new(Workplace.HiTechU, "Janitor",    5, 10, 10, 0, 0, Casual, 43, 14),
        new(Workplace.HiTechU, "Teacher",   11, 50, 40, Degrees.Academic, 0, Dress, 48, 15),
        new(Workplace.HiTechU, "Professor", 20, 60, 50, Degrees.Research, 0, Dress, 49, 16),

        // --- Factory --------------------------------------------------------
        new(Workplace.Factory, "Janitor",              7, 20, 10, 0, 0, Casual, 43, 18),
        new(Workplace.Factory, "Assembly Worker",      8, 30, 30, Degrees.TradeSchool,    0, Casual, 50, 17),
        new(Workplace.Factory, "Secretary",            9, 40, 40, Degrees.JuniorCollege,  0, Dress,  51, 19),
        new(Workplace.Factory, "Machinist Helper",  10, 40, 40, Degrees.PreEngineering, 0, Casual, 52, 20),
        new(Workplace.Factory, "Exec. Secretary", 18, 50, 50, Degrees.BusinessAdmin,  0, Suit,   53, 21),
        new(Workplace.Factory, "Machinist",           19, 50, 50, Degrees.Engineering,    0, Casual, 54, 22),
        new(Workplace.Factory, "Department Mgr.",  22, 60, 60, Degrees.JuniorCollege, Degrees.Engineering, Suit, 56, 23),
        new(Workplace.Factory, "Engineer",            23, 60, 60, Degrees.Engineering,   Degrees.JuniorCollege, Suit, 55, 24),
        new(Workplace.Factory, "General Mgr.",     25, 70, 70, Degrees.BusinessAdmin, Degrees.Engineering, Suit, 57, 25),

        // --- Bank ------------------------------------------------------------
        new(Workplace.Bank, "Janitor",            6, 20, 10, 0, 0, Casual, 43, 26),
        new(Workplace.Bank, "Teller",            10, 40, 40, Degrees.JuniorCollege, 0, Dress, 58, 27),
        new(Workplace.Bank, "Assist. Manager", 14, 50, 50, Degrees.BusinessAdmin, 0, Suit,  45, 28),
        new(Workplace.Bank, "Manager",           19, 60, 60, Degrees.BusinessAdmin, 0, Suit,  46, 29),
        new(Workplace.Bank, "Broker",            22, 70, 70, Degrees.BusinessAdmin, Degrees.Academic, Suit, 59, 30),

        // --- Black's Market -----------------------------------------------------
        new(Workplace.BlacksMarket, "Janitor",            6, 10, 10, 0, 0, Casual, 43, 31),
        new(Workplace.BlacksMarket, "Checker",            8, 20, 20, 0, 0, Casual, 60, 32),
        new(Workplace.BlacksMarket, "Butcher",           12, 30, 30, Degrees.TradeSchool,   0, Casual, 61, 33),
        new(Workplace.BlacksMarket, "Assist. Manager", 15, 40, 40, Degrees.JuniorCollege, 0, Dress,  45, 34),
        new(Workplace.BlacksMarket, "Manager",           18, 50, 50, Degrees.BusinessAdmin, 0, Suit,   46, 35),

        // --- Rent Office ---------------------------------------------------------
        new(Workplace.RentOffice, "Groundskeeper",     7, 20, 10, 0, 0, Casual, 62, 36),
        new(Workplace.RentOffice, "Apartment Mgr", 9, 30, 30, Degrees.JuniorCollege, 0, Casual, 63, 37),
    ];

    public static IEnumerable<Job> At(Workplace w) => All.Where(j => j.Workplace == w);

    public static Job ByJobNum(int jobNum) => All.Single(j => j.JobNum == jobNum);

    /// <summary>The Cook, the one job anyone can always get.</summary>
    public static Job Cook => ByJobNum(4);

    /// <summary>`indexNum` 44 bypasses the openings roll — see `employment.sc:122`.</summary>
    public const int CookOccupationId = 44;
}
