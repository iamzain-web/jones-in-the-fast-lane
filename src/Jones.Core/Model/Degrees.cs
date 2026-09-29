namespace Jones.Core.Model;

/// <summary>
/// The 11 degrees, ids 10–20, with the prerequisite tree from the `UniversityDIcon`
/// instances in `university.sc`. Every prerequisite here is read from the source.
/// </summary>
public static class Degrees
{
    public const int TradeSchool = 10;
    public const int Electronics = 11;
    public const int PreEngineering = 12;
    public const int Engineering = 13;
    public const int JuniorCollege = 14;
    public const int BusinessAdmin = 15;
    public const int Academic = 16;
    public const int GraduateSchool = 17;
    public const int PostDoctoral = 18;
    public const int Research = 19;
    public const int Publishing = 20;

    /// <summary>Lessons required before extra credit is applied (`Goods.sc:37`).</summary>
    public const int BaseUnitsToGraduate = 10;

    /// <summary>Enrollment fee before the economy is applied (`university.sc`).</summary>
    public const int EnrollmentBasePrice = 50;

    /// <param name="PreReq">The degree that must be held first, or 0 for none.</param>
    public sealed record Degree(int Id, string Name, int PreReq);

    /// <summary>
    /// The names are text resource 700 at the degree's own id — `diploma.sc:153` formats
    /// them straight out of it, so 700[12] is `Pre Engineering` (no hyphen), 700[15]
    /// `Business Admin.` and 700[18] `Post Doctoral`. The tidied spellings were mine.
    /// </summary>
    public static readonly Degree[] All =
    [
        new(TradeSchool,    "Trade School",    0),
        new(JuniorCollege,  "Junior College",  0),
        new(Electronics,    "Electronics",     TradeSchool),
        new(PreEngineering, "Pre Engineering", TradeSchool),
        new(Engineering,    "Engineering",     PreEngineering),
        new(BusinessAdmin,  "Business Admin.", JuniorCollege),
        new(Academic,       "Academic",        JuniorCollege),
        new(GraduateSchool, "Graduate School", Academic),
        new(PostDoctoral,   "Post Doctoral",   GraduateSchool),
        new(Research,       "Research",        PostDoctoral),
        new(Publishing,     "Publishing",      Research),
    ];

    public static Degree ById(int id) => All.Single(d => d.Id == id);

    /// <summary>
    /// The two degrees available from the outset. Everything else is gated behind one of
    /// these, which is why Junior College is the single most valuable early purchase —
    /// ten jobs require it directly and it opens the Business Admin and Academic lines.
    /// </summary>
    public static IEnumerable<Degree> InitiallyAvailable => All.Where(d => d.PreReq == 0);

    /// <summary>
    /// Courses the player may enrol in: not already held, and the prerequisite satisfied
    /// (`university.sc:162`, `addCourse`).
    /// </summary>
    public static IEnumerable<Degree> AvailableTo(Player p) =>
        All.Where(d => !HasDegree(p, d.Id) && (d.PreReq == 0 || HasDegree(p, d.PreReq)));

    /// <summary>Port of `Player::hasDegree` — held once quantity reaches the threshold.</summary>
    public static bool HasDegree(Player p, int degreeId)
    {
        if (degreeId == 0) return true; // "no degree required" always passes
        var e = p.Education.At(degreeId);
        return e is not null && e.Quantity >= e.UnitsToGraduate;
    }

    /// <summary>
    /// Port of `Player::courseActive` — enrolled and started, but not yet graduated.
    /// </summary>
    public static bool CourseActive(Player p, int degreeId)
    {
        var e = p.Education.At(degreeId);
        return e is not null && e.Quantity > 0 && e.Quantity < e.UnitsToGraduate;
    }

    /// <summary>
    /// Lessons still needed, after extra credit. `university.sc:117` computes
    /// `unitsToGraduate - quantity - xcred`, so extra credit shortens every course the
    /// player is taking, retroactively.
    /// </summary>
    public static int LessonsRemaining(Player p, int degreeId)
    {
        var e = p.Education.At(degreeId);
        if (e is null) return BaseUnitsToGraduate - p.XCred;
        return e.UnitsToGraduate - e.Quantity - p.XCred;
    }

    /// <summary>
    /// Recomputes extra credit from durables held (`room1.sc:191`). A computer is worth
    /// one, and the three reference books together are worth one more — but only as a
    /// complete set, so owning two of the three buys nothing.
    /// </summary>
    public static void RecalculateExtraCredit(Player p)
    {
        var credit = 0;
        if (p.Durables.Holds(ItemIds.Computer)) credit++;
        if (ItemIds.ReferenceBooks.All(p.Durables.Holds)) credit++;
        p.XCred = credit;
    }
}
