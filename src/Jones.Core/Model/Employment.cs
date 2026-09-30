using Jones.Core.Economy;
using Jones.Core.Sci;

namespace Jones.Core.Model;

/// <summary>Why a job application succeeded or failed.</summary>
public enum JobOutcome
{
    Hired,
    NotEnoughEducation,
    NotEnoughDependability,
    NotEnoughExperience,
    NoOpenings,
    CannotAffordUniform,
}

/// <summary>
/// `global433` — the Employment Office's answer, and the clip it speaks. `proc206_1`
/// (`employment.sc:30-49`) plays `420 + global433` for anything from 0 up and `434 + n`
/// for the refusal, so these values ARE the audio ids minus 420.
///
/// The four raise codes (`employment.sc:171-197`) were entirely missing: re-applying to the
/// job you already hold is a RAISE REQUEST, not an application, and the port used to send it
/// through the hire path — resetting your raise count to 0 and handing out +2 experience and
/// +3 happiness every time you clicked. A free stat pump the game does not offer.
/// </summary>
public enum ApplicationCode
{
    /// <summary>Refused on the merits. `proc206_1` speaks `434 + reason` and costs a point.</summary>
    Refused = -1,

    /// <summary>Raise: you are already paid above the board rate. Nothing happens.</summary>
    AlreadyPaidMore = 0,

    /// <summary>Raise: your wage already IS the board rate. Nothing happens.</summary>
    AlreadyAtRate = 1,

    /// <summary>Raise granted: +3 happiness, `raises++`, wage set to the board rate.</summary>
    RaiseGranted = 2,

    /// <summary>Raise refused: not dependable enough yet. Nothing happens.</summary>
    NotDependableEnough = 3,

    /// <summary>Hired.</summary>
    Hired = 4,

    /// <summary>The office is shut for the week — the clock ran out (`employment.sc:167`).</summary>
    OfficeClosed = 5,
}

/// <summary>
/// Which failed requirement the clerk cites when he turns you down.
///
/// The original does NOT report the first failure: `proc206_1` (`employment.sc:30-58`)
/// draws `(Random 0 2)` over the three qualification globals — 325 education,
/// 326 dependability, 327 experience — and keeps drawing until it lands on one that is
/// FALSE, so with two requirements unmet either may be the one you hear about. When all
/// three passed and the application still failed (the `global328` openings roll), no draw
/// happens at all and the answer is 3. The values are the offsets the clip id is built
/// from, `434 + n`.
/// </summary>
public enum RejectionReason
{
    Education = 0,
    Dependability = 1,
    Experience = 2,
    NoOpenings = 3,
}

/// <summary>
/// The four flags `JobDItem::qualify` (`employment.sc:87-129`) leaves behind in
/// global325..328. They outlive the call because `proc206_1` reads them back to choose
/// what the clerk says.
/// </summary>
public readonly record struct Qualification(
    bool Education,
    bool Dependability,
    bool Experience,
    bool Openings)
{
    /// <summary>`temp0`, the value `qualify:` returns — all four, ANDed.</summary>
    public bool Hired => Education && Dependability && Experience && Openings;
}

/// <summary>
/// Job applications, ported from `JobDItem::qualify` (`employment.sc:87`).
///
/// The Employment Office is where the game's difficulty actually lives: the requirements
/// are only half the story, because even a fully qualified applicant faces a probabilistic
/// "no openings" refusal that improves with degrees.
/// </summary>
public static class Employment
{
    /// <summary>
    /// Tracks jobs refused this turn. Once a job says no, it keeps saying no until the
    /// turn ends (`JobDItem::turnedDown`, `employment.sc:132`) — so shopping around the
    /// same counter repeatedly is pointless.
    /// </summary>
    public sealed class TurnedDownTracker
    {
        private readonly HashSet<int> _refused = [];

        /// <summary>The job numbers refused so far this turn, so a save can record them.</summary>
        public IEnumerable<int> Refused => _refused;

        public bool WasRefused(int jobNum) => _refused.Contains(jobNum);
        public void Refuse(int jobNum) => _refused.Add(jobNum);
        public void Clear() => _refused.Clear();
    }

    /// <summary>
    /// The "no openings" threshold as a percentage: `(dep + exp + degreeBonus)/3 + 30`,
    /// where `degreeBonus = numDegrees * 8 + 10`. Truncating division.
    ///
    /// Worth knowing: with all 11 degrees and zero dependability and experience, this is
    /// `(0 + 0 + 98)/3 + 30 = 62`. The wiki claims 66%; the source gives **62%**.
    /// </summary>
    public static int OpeningsChance(Player p)
    {
        var degreeBonus = p.NumDegrees() * 8 + 10;
        return SciMath.Div(p.Dependibility + p.Experience + degreeBonus, 3) + 30;
    }

    /// <summary>
    /// Cost of the uniform a job demands, at the current economy. Casual costs nothing
    /// because every player starts with six sets (`employment.sc:60`).
    /// </summary>
    public static int UniformCost(int uniform, int goodsIndexReading) => uniform switch
    {
        ItemIds.CasualClothes => 0,
        ItemIds.DressClothes => Pricing.Price(goodsIndexReading, 125),
        ItemIds.BusinessSuit => Pricing.Price(goodsIndexReading, 295),
        _ => 0,
    };

    /// <summary>
    /// Whether the player can afford to take the job: net worth must cover the uniform
    /// plus a base-65 buffer, both economy-adjusted (`employment.sc:72`).
    /// </summary>
    public static bool CanAffordUniform(Player p, Job job, int goodsIndexReading) =>
        p.NetWorth >= UniformCost(job.Uniform, goodsIndexReading)
                    + Pricing.Price(goodsIndexReading, 65);

    /// <summary>
    /// Applies for a job. On success the player's wage, workplace and occupation are set.
    /// </summary>
    public static JobOutcome Apply(
        Player p,
        Job job,
        IRandomSource rng,
        int goodsIndexReading,
        TurnedDownTracker turnedDown)
        => Apply(p, job, rng, goodsIndexReading, turnedDown, out _);

    /// <summary>
    /// As <see cref="Apply(Player, Job, IRandomSource, int, TurnedDownTracker)"/>, and also
    /// hands back the four qualification flags so the caller can reproduce
    /// <c>proc206_1</c>'s draw for the spoken refusal. See <see cref="Qualification"/>.
    /// </summary>
    public static JobOutcome Apply(
        Player p,
        Job job,
        IRandomSource rng,
        int goodsIndexReading,
        TurnedDownTracker turnedDown,
        out Qualification qualification)
    {
        qualification = Qualify(p, job, rng, turnedDown);

        if (qualification.Hired)
        {
            Hire(p, job, goodsIndexReading);
            return JobOutcome.Hired;
        }

        return Refuse(p, job, qualification, turnedDown);
    }

    /// <summary>
    /// `JobDItem::qualify` (`employment.sc:87-130`), which runs BEFORE the cond in
    /// `JobDItem::doit` — so it fires on a raise request too, and the openings roll is
    /// consumed on every visit to the counter whatever the answer.
    ///
    /// NO UNIFORM CHECK. `JobDItem::doit` (`employment.sc:164-243`) calls `qualify:` and
    /// nothing else; the uniform-affordability test `localproc_0` is called from exactly one
    /// place, `employment.sc:627`, inside the COMPUTER player's job search. It never touches
    /// a human application.
    /// </summary>
    public static Qualification Qualify(
        Player p, Job job, IRandomSource rng, TurnedDownTracker turnedDown)
    {
        var hasEducation = Degrees.HasDegree(p, job.ReqDegree1)
                        && Degrees.HasDegree(p, job.ReqDegree2);

        // `employment.sc:94-97` writes needEd1/needEd2 on EVERY qualify:, to 0 when the
        // education test passed:
        //
        //     (global302 needEd1: (if global325 0 else education)
        //                needEd2: (if global325 0 else education2))
        //
        // The port only wrote them inside the refusal branch, so a successful application
        // left the last refusal's missing degrees showing.
        //
        // NOTHING READS THEM BACK, in this port or in either script tree, and that is not
        // an omission here. A whole-word search of both trees finds exactly four hits:
        // the two `(properties)` declarations (`room1.sc:637-638`, floppy `:625-626`) and
        // these two writes (`employment.sc:95-96`, floppy `:137-138`). No `needEd1:` or
        // `needEd2:` READ exists anywhere, so the player-facing "you still need X and Y"
        // they were evidently meant to feed was never written. The port stores them, saves
        // them and shows them nowhere — which is the original's behaviour exactly.
        // Recorded so this is not re-found; PROPERTIES.md §1.14 has the same conclusion.
        p.NeedEd1 = hasEducation ? 0 : job.ReqDegree1;
        p.NeedEd2 = hasEducation ? 0 : job.ReqDegree2;

        // A requirement of exactly 10 is subtracted away to nothing, so entry-level jobs
        // have NO dependability requirement at all. `employment.sc:101`.
        var effectiveDepReq = job.ReqDependibility - (job.ReqDependibility == 10 ? 10 : 0);
        var hasDependability = p.Dependibility >= effectiveDepReq;

        var hasExperience = p.Experience >= job.ReqExperience;

        // The roll happens regardless of the other checks, exactly as the original does,
        // so it consumes an RNG value on every application.
        var openings = rng.Next(1, 100) <= OpeningsChance(p);

        if (turnedDown.WasRefused(job.JobNum)) openings = false;

        // The Cook bypasses the openings roll entirely — the game's guaranteed floor.
        if (job.OccupationId == Jobs.CookOccupationId) openings = true;

        return new Qualification(hasEducation, hasDependability, hasExperience, openings);
    }

    /// <summary>`employment.sc:198-230` — the hire branch, `global433 = 4`.</summary>
    public static void Hire(Player p, Job job, int goodsIndexReading)
    {
        p.WorksAt = (int)job.Workplace + 1;
        p.Occupation = job.OccupationId;
        p.Uniform = job.Uniform;

        // The wage you are paid is the ECONOMY-ADJUSTED figure shown on the job
        // board, not the raw base. A slump really does mean lower pay.
        p.Wage = Pricing.Price(goodsIndexReading, job.BaseWage);
        p.BaseWage = job.BaseWage;

        // `minDepend` is simply the job's dependability requirement (employment.sc:209).
        p.MinDepend = job.ReqDependibility;

        // Experience ceiling on hire: the job's requirement plus 10, or 20 flat when
        // the job asks for none (employment.sc:203). NOT a per-degree bonus — that was
        // my invention. Degrees raise the ceiling through eduCredit/expCredit instead.
        p.MaxExper = job.ReqExperience == 0 ? 20 : job.ReqExperience + 10;

        // Being hired grants +2 experience outright and floors dependability at 10.
        p.Experience += 2;
        if (p.Dependibility < 10) p.Dependibility = 10;

        p.Raises = 0;
        p.RecalculateCareerStat();
        p.HapStat += 3; // getting a job is worth +3 Happiness
    }

    /// <summary>`employment.sc:231-236` — the refusal branch, `global433 = -1`.</summary>
    public static JobOutcome Refuse(
        Player p, Job job, Qualification q, TurnedDownTracker turnedDown)
    {
        // Blacklisting is NOT what happens on every refusal. `employment.sc:232-235`:
        //
        //     (if (and global325 global326 global327 (not global328))
        //         (self turnedDown: jobNum))
        //
        // Only a candidate who met education, dependability AND experience and then lost
        // the openings roll is remembered and refused next time. Being turned away for a
        // missing degree leaves no mark, so you can come back the moment you have it.
        if (q.Education && q.Dependability && q.Experience && !q.Openings)
            turnedDown.Refuse(job.JobNum);

        // `proc0_13 -1` at `employment.sc:33` — the penalty is in the SPEAK procedure and
        // therefore belongs to the refusal alone, never to a raise request.
        p.HapStat -= 1;

        if (!q.Education) return JobOutcome.NotEnoughEducation;
        if (!q.Dependability) return JobOutcome.NotEnoughDependability;
        if (!q.Experience) return JobOutcome.NotEnoughExperience;
        return JobOutcome.NoOpenings;
    }

    /// <summary>
    /// `proc206_1`'s rejection draw (`employment.sc:33-46`), verbatim:
    ///
    ///     (if (or (not global325) (not global327) (not global326))
    ///         (repeat (if (not [global325 (= temp0 (Random 0 2))]) (break)))
    ///     else (= temp0 3))
    ///
    /// Note that the draw happens ONLY when a requirement actually failed — when the
    /// refusal was the openings roll the answer is 3 and no random value is consumed. That
    /// matters: the roll is on the same stream as everything else.
    /// </summary>
    public static RejectionReason DrawRejectionReason(Qualification q, IRandomSource rng)
    {
        if (q.Education && q.Dependability && q.Experience) return RejectionReason.NoOpenings;

        // The original's `repeat` has no exit but the break, and with a real Random it
        // terminates with probability 1 because at least one of the three is false. The
        // attempt count is a guard against a degenerate source — a test RNG pinned to one
        // value would spin here for ever — and never changes the answer for a real one.
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var n = rng.Next(0, 2);
            var passed = n switch
            {
                0 => q.Education,
                1 => q.Dependability,
                _ => q.Experience,
            };
            if (!passed) return (RejectionReason)n;
        }

        if (!q.Education) return RejectionReason.Education;
        return !q.Dependability ? RejectionReason.Dependability : RejectionReason.Experience;
    }

    /// <summary>Outcome of attempting a shift, mirroring the original's `global566`.</summary>
    public enum WorkResult { NotWorked = 0, Paid = 1, Fired = -1 }

    /// <summary>
    /// A shift, ported from `proc108_0` / `localproc_0` in `n108.sc`.
    ///
    /// Worth knowing: there are NO random events while working — `n108.sc` contains not a
    /// single Random call. Every outcome is deterministic. Working also costs no
    /// happiness, and there is no once-per-turn limit: you may work repeatedly until the
    /// clock runs out.
    /// </summary>
    /// <param name="Garnished">
    /// What the landlord took out of this shift, for the `980 + placeNum` clip. Zero when
    /// no arrears were owed. This is the figure actually deducted, which — see the quirk
    /// below — the original computes from the UN-prorated gross.
    /// </param>
    public readonly record struct ShiftResult(WorkResult Result, int Paid, int Garnished);

    public static ShiftResult Work(Player p, GameClock clock)
    {
        // The week being over is checked FIRST, before whether you are dressed.
        if (clock.TurnOver) return new ShiftResult(WorkResult.NotWorked, 0, 0);
        if (!p.DressedForWork()) return new ShiftResult(WorkResult.NotWorked, 0, 0);

        // Sacked once dependability falls more than 5 below the job's requirement. Note
        // this costs no time and pays nothing — the shift never happens.
        if (p.Dependibility < p.MinDepend - 5)
        {
            p.Wage = 0;
            p.WorksAt = 0;
            p.Occupation = 0;
            return new ShiftResult(WorkResult.Fired, 0, 0);
        }

        // Experience and dependability each rise by one, under their own ceilings.
        // University credits raise both ceilings, which is how education compounds.
        if (p.Experience < p.MaxExper + p.ExpCredit) p.Experience++;
        if (p.Dependibility < p.MinDepend + 20 + p.EduCredit) p.Dependibility++;

        p.RecalculateCareerStat();

        var gross = p.Wage * 8;
        var take = gross;

        // A short clock prorates the pay: wage * 8 * hoursLeft / 6.
        if (clock.HoursRemaining < GameClock.WorkSessionCost)
            take = SciMath.Div(gross * clock.HoursRemaining, GameClock.WorkSessionCost);

        var garnished = 0;

        if (p.RentOwed > 0)
        {
            // ORIGINAL QUIRK, replicated: garnishment recomputes from the UN-prorated
            // gross, so a short shift worked while in arrears silently loses its
            // proration and pays as though it were a full one (`n108.sc:93`).
            var half = SciMath.Div(gross, 2);

            if (half > p.RentOwed)
            {
                // Half a shift clears the whole debt, and you keep the rest.
                garnished = p.RentOwed;
                take = gross - p.RentOwed;
                p.RentOwed = 0;
            }
            else
            {
                // Otherwise half your gross goes to the arrears — and a further $2
                // vanishes. The $2 is literal and unexplained in the original.
                garnished = half;
                take = half - 2;
                p.RentOwed -= half;
            }

            p.RentExt = -1; // any rent extension is void once wages are being garnished
        }

        p.Cash += take;

        // The 6 hours are charged AFTER the pay is worked out.
        clock.Spend(GameClock.WorkSessionCost);

        return new ShiftResult(WorkResult.Paid, take, garnished);
    }

    /// <summary>
    /// Is this the job the player already holds? `employment.sc:171-175`:
    ///
    ///     (and (== (global302 worksAt:) global419) (== (global302 occupation:) indexNum))
    ///
    /// Re-selecting it is a RAISE REQUEST, not an application.
    /// </summary>
    public static bool IsCurrentJob(Player p, Job job) =>
        p.WorksAt == (int)job.Workplace + 1 && p.Occupation == job.OccupationId;

    /// <summary>
    /// Asking for a raise, `employment.sc:176-197`, which has FOUR outcomes and only one of
    /// them does anything:
    ///
    ///     ((> wage price)  → global433 0)   nothing — already paid above the board rate
    ///     ((== wage price) → global433 1)   nothing
    ///     ((>= dependibility (+ dependibility (* 5 raises)))
    ///                      → global433 2)   proc0_13 3, raises++, wage: price
    ///     (else            → global433 3)   nothing
    ///
    /// The second clause was missing here — `if (p.Wage > current) return false;` let an
    /// equal wage fall through to the dependability test and grant a raise that changes
    /// nothing but still paid out +3 happiness and burned a `raises` step.
    ///
    /// Note the requirement compared against is the JOB's `dependibility` property, raw,
    /// NOT the `== 10 → 0` adjusted figure `qualify:` uses.
    /// </summary>
    public static ApplicationCode AskForRaise(Player p, Job job, int goodsIndexReading)
    {
        var price = Pricing.Price(goodsIndexReading, job.BaseWage);

        if (p.Wage > price) return ApplicationCode.AlreadyPaidMore;
        if (p.Wage == price) return ApplicationCode.AlreadyAtRate;

        if (p.Dependibility < job.ReqDependibility + 5 * p.Raises)
            return ApplicationCode.NotDependableEnough;

        p.HapStat += 3;
        p.Raises++;
        p.Wage = price;
        return ApplicationCode.RaiseGranted;
    }
}
