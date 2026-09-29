using Jones.Core.Economy;

namespace Jones.Core.Model;

/// <summary>Result of taking a lesson.</summary>
public enum StudyOutcome
{
    LessonTaken,
    Graduated,
    NotEnrolled,
    NoTimeLeft,
    AlreadyHeld,
    PrerequisiteMissing,
}

/// <summary>
/// Hi-Tech U, ported from `university.sc` (script 207).
///
/// The economics of this building decide most games: a course costs an enrollment fee plus
/// ten 6-Hour lessons, which is sixty Hours — a whole turn — for a degree that may unlock
/// nothing on its own. Extra credit cuts that by 20%.
/// </summary>
public static class University
{
    /// <summary>Enrollment fee at the current economy (base 50, goods index).</summary>
    public static int EnrollmentFee(int goodsIndexReading) =>
        Pricing.Price(goodsIndexReading, Degrees.EnrollmentBasePrice);

    /// <summary>
    /// Buys one enrollment credit. Credits are fungible — the original lets you pay
    /// several fees and decide which courses to take later — and enrolling costs NO
    /// Hours, so it can be done after the turn clock has run out.
    /// </summary>
    public static bool Enroll(Player p, int goodsIndexReading)
    {
        // `university.sc:471` `(>= (proc0_11) price)` — and `proc0_11` (`Main.sc:1071-1082`)
        // returns CASH. Testing net worth let a player with $10 in hand and a $600 computer
        // enrol for free and go to -$40.
        var fee = EnrollmentFee(goodsIndexReading);
        if (p.Cash < fee) return false;

        p.Cash -= fee;
        p.Enrollments++;
        return true;
    }

    /// <summary>
    /// Takes one lesson in a course, spending an enrollment credit if this is the first
    /// lesson. Costs 6 Hours; a short clock is not penalised, so the last hour of a turn
    /// buys a whole lesson.
    /// </summary>
    public static StudyOutcome Study(Player p, int degreeId, GameClock clock)
    {
        if (Degrees.HasDegree(p, degreeId)) return StudyOutcome.AlreadyHeld;

        var degree = Degrees.ById(degreeId);
        if (degree.PreReq != 0 && !Degrees.HasDegree(p, degree.PreReq))
            return StudyOutcome.PrerequisiteMissing;

        // ORDER MATTERS. `UniversityDIcon::doit` tests the enrolment gate FIRST
        // (`university.sc:179-191`) and the clock SECOND (`:193`), so an unenrolled player
        // with no hours left is told they are not enrolled, not that the day is over. The
        // port had these the other way round and played the wrong clip.
        var course = p.Education.At(degreeId);
        if (course is null && p.Enrollments <= 0) return StudyOutcome.NotEnrolled;

        // Any hour left at all buys a lesson.
        if (clock.TurnOver) return StudyOutcome.NoTimeLeft;

        if (course is null)
        {
            // Starting a new course consumes one enrollment credit.
            //
            // OUTSTANDING (`GATES.md` §4.7): the source never decrements `enrollments`.
            // Its gate is comparative — `enrollments > activeCourses + numDegrees`, or
            // equal to it while the course is already active — so the stored value is the
            // total number of fees ever paid. The arithmetic comes out the same here, but
            // `JonesAi` reads the raw value and is wrong because of it.
            p.Enrollments--;

            course = p.Education.Receive(degreeId, 0);
            course.UnitsToGraduate = Degrees.BaseUnitsToGraduate;
        }

        course.Quantity++;
        clock.Spend(GameClock.LessonCost);

        // Extra credit is applied at the comparison rather than baked into the course, so
        // buying a computer mid-course shortens the course you are already taking.
        if (course.Quantity >= course.UnitsToGraduate - p.XCred)
        {
            Graduate(p, course);
            return StudyOutcome.Graduated;
        }

        return StudyOutcome.LessonTaken;
    }

    /// <summary>
    /// Awards the degree. The bonuses here are the reason education compounds: the
    /// dependability boost can exceed the normal cap, and the permanent cap increases
    /// let a graduate qualify for jobs several rungs above their current one.
    /// </summary>
    private static void Graduate(Player p, Item course)
    {
        // Normalise so hasDegree's `quantity >= unitsToGraduate` test holds even when
        // extra credit ended the course early (`university.sc:201`).
        course.Quantity = course.UnitsToGraduate;

        // `(global302 notEnoughEd: 0)` — `university.sc:207`, the first thing graduation
        // does after the song fades. The port only ever SET this flag (`Game.cs`,
        // `JonesAi.cs`) and never cleared it, so `WhereShouldIGo.sc:789/803/1059`'s port
        // routed a graduate back to school for the rest of the game.
        p.NotEnoughEd = false;

        p.HapStat += 5;
        p.Dependibility += 5;   // may exceed the ceiling, and decays normally from there

        // Education raises the CEILINGS through credits, which is how `n108.sc` reads
        // them: the caps are `minDepend + 20 + eduCredit` and `maxExper + expCredit`.
        // Degrees do not raise the caps directly at hire.
        p.EduCredit += 5;
        p.ExpCredit += 5;
        p.CoursesDone++;

        // Education goal progress is a pure function of degrees held.
        p.EduStat = 1 + 9 * p.NumDegrees();
    }

    /// <summary>
    /// Education goal progress: `1 + (9 x degrees)`. Reaching 100 needs all 11.
    /// </summary>
    public static int EducationScore(Player p) => 1 + 9 * p.NumDegrees();
}
