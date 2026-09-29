namespace Jones.Core.Model;

public enum LocationId
{
    LowCostHousing,
    PawnShop,
    ZMart,
    MonolithBurgers,
    QtClothing,
    SocketCity,
    HiTechU,
    EmploymentOffice,
    Factory,
    Bank,
    BlacksMarket,
    SecurityApartments,
    RentOffice,
}

/// <param name="PathIndex">Position on the marble path — the `index` property of the Place.</param>
/// <param name="PlaceNum">The original's `placeNum`.</param>
/// <param name="Left">Hotspot rectangle on the 320x200 board, from the Place's leftR.</param>
public sealed record Location(
    LocationId Id,
    string Name,
    int PathIndex,
    int PlaceNum,
    int Left,
    int Top,
    int Right,
    int Bottom,
    Workplace? Workplace = null)
{
    public int Width => Right - Left + 1;
    public int Height => Bottom - Top + 1;
}

/// <summary>
/// The town, ported from the `Place` instances in `room1.sc:294`.
///
/// Locations sit at fixed positions on a circular path and the player's marble walks it,
/// so travel cost is proportional to distance travelled — you pay for the journey, not
/// per destination. Combined with the flat 2-Hour cost of walking through any door, this
/// is what makes route planning the core skill of the game.
/// </summary>
public static class Board
{
    /// <summary>The original's screen size. All hotspot rectangles are in this space.</summary>
    public const int ScreenWidth = 320;
    public const int ScreenHeight = 200;

    /// <summary>
    /// Clockwise from the top, matching the original's path indices. The rectangles are
    /// the Place instances' own leftR/topR/rightR/bottomR from `room1.sc:294`, so
    /// clicking the board hits exactly the areas the 1991 game did.
    ///
    /// THE NAMES ARE TEXT RESOURCE 700, not labels of my own. `inventories.sc:36` prints
    /// the player's workplace with `Format … 231 1 700 (worksAt + 71)`, and `placeNum + 71`
    /// is exactly the index of each entry below: 700[71] `Low Cost Apartment`,
    /// 700[78] `University`, 700[81] `Monolith`, 700[83] `Pawn Shoppe`. "Low-Cost Housing",
    /// "Hi-Tech U", "Monolith Burgers", "Le Security Apartments" and "Pawn Shop" were mine.
    /// (`Monolith Burgers` does exist — script 206 @0x111C — but only as the Employment
    /// Office's employer-menu label, which `StoreLayout.Employment` carries separately.)
    /// </summary>
    public static readonly Location[] All =
    [
        new(LocationId.LowCostHousing,     "Low Cost Apartment",     1,  0, 130,  10, 190,  43),
        new(LocationId.PawnShop,           "Pawn Shoppe",           14, 12, 191,  10, 250,  43),
        new(LocationId.ZMart,              "Z-Mart",                24, 11, 251,  10, 311,  43, Model.Workplace.ZMart),
        new(LocationId.MonolithBurgers,    "Monolith",              35, 10, 251,  44, 311,  81, Model.Workplace.MonolithBurgers),
        new(LocationId.QtClothing,         "QT Clothing",           47,  9, 251,  82, 311, 118, Model.Workplace.QtClothing),
        new(LocationId.SocketCity,         "Socket City",           66,  8, 251, 133, 311, 192, Model.Workplace.SocketCity),
        new(LocationId.HiTechU,            "University",            75,  7, 190, 158, 250, 192, Model.Workplace.HiTechU),
        new(LocationId.EmploymentOffice,   "Employment Office",     99,  6,  68, 158, 128, 192),
        new(LocationId.Factory,            "Factory",              107,  5,   7, 155,  67, 192, Model.Workplace.Factory),
        new(LocationId.Bank,               "Bank",                 120,  4,   7, 119,  67, 154, Model.Workplace.Bank),
        new(LocationId.BlacksMarket,       "Black's Market",       134,  3,  24,  69,  67, 114, Model.Workplace.BlacksMarket),
        new(LocationId.SecurityApartments, "Security Apartment",   151,  2,   7,  10,  67,  43),
        new(LocationId.RentOffice,         "Rent Office",          164,  1,  68,  10, 129,  43, Model.Workplace.RentOffice),
    ];

    /// <summary>
    /// Length of the circular path in marble steps. `marblePath.sc:13`: element 0 of the
    /// coordinate table IS the count, 170, and the usable indices are 1..170. The old 175
    /// here was `[UNVERIFIED]` and guessed from the highest Place index.
    /// </summary>
    public const int PathLength = MarblePath.StepCount;

    public static Location Get(LocationId id) => All.Single(l => l.Id == id);

    public static Location? ForWorkplace(Workplace w) =>
        All.FirstOrDefault(l => l.Workplace == w);

    /// <summary>
    /// Steps between two locations, by the SHORTER arc. `MarblePath::setDirection`
    /// (`marblePath.sc:86-104`) measures both ways round and compares against
    /// `(/ [local0 0] 2)` = 85, walking whichever is shorter — travel is bidirectional.
    ///
    /// This used to wrap forward only, and its doc comment asserted the opposite of the
    /// source. Because <see cref="MarblePath.Distance"/> already had it right, the marble
    /// walked the short way while the clock charged for the long one.
    /// </summary>
    public static int StepsBetween(LocationId from, LocationId to) =>
        MarblePath.Distance(Get(from).PathIndex, Get(to).PathIndex);

    /// <summary>
    /// Whether the building's door animation plays. This is COSMETIC: `Place::cue`
    /// (`room1.sc:174-209`) calls `((ScriptID sNumber 0) init: room1)` unconditionally, so
    /// a "closed" building simply skips `openDoor:` (`global516 = 0`) and shows a shut-office
    /// picture. You always walk in and you always pay the 2 hours — closure never refuses a
    /// journey.
    ///
    /// The Rent Office rule has THREE clauses, not one (`room1.sc:176-193`, and the same
    /// three at `rentOffice.sc:66-71`):
    ///
    ///     (or (== (global302 worksAt:) 1) (not (mod global372 4)) (global302 leaveOpen:))
    ///
    /// Only the middle one was here, which locked a player employed at the Rent Office out
    /// of their own job for three weeks in four and made `leaveOpen` — the reward for a
    /// successful extension request — completely dead.
    /// </summary>
    public static bool IsOpen(LocationId id, int week, Player? p = null)
    {
        if (id != LocationId.RentOffice) return true;
        if (p is not null && p.WorksAt == RentOfficeWorksAt) return true;
        if (week % 4 == 0) return true;
        return p is not null && p.LeaveOpen;
    }

    /// <summary>
    /// <see cref="Player.WorksAt"/> for the Rent Office. The original stores `worksAt` as the
    /// employer's `placeNum`, so its test reads `(== (global302 worksAt:) 1)`; this port
    /// stores `(int)Workplace + 1` instead, which is a different encoding of the same fact.
    /// </summary>
    public const int RentOfficeWorksAt = (int)Model.Workplace.RentOffice + 1;

    /// <summary>
    /// Leaving these two can trigger a mugging by Wild Willy: the Bank at 1-in-31 and
    /// Black's Market at 1-in-51 (MECHANICS.md §6).
    /// </summary>
    public static int MuggingOdds(LocationId id) => id switch
    {
        LocationId.Bank => 30,          // Random(0,30) == 0
        LocationId.BlacksMarket => 50,  // Random(0,50) == 0
        _ => 0,
    };
}
