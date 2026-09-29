using Jones.Core.Sci;

namespace Jones.Core.Economy;

/// <summary>
/// Port of the `economicIndex` singleton's `doit` method in `economicIndex.sc`
/// (script 107), lines 204–281. Advances the whole economy by one week.
///
/// Eight indices tick in a fixed order, and they are NOT independent — each takes the
/// freshly-updated trend of the index above it as drift. The dependency tree is:
///
///     main ──┬── invest ──┬── gold, silver, pork, blueChip, penny
///            └── goods
///
/// Note that `goods` follows MAIN, not invest (source line 229 passes global315, while
/// lines 232–245 pass global316). Easy to get wrong; it changes how goods prices track
/// the town economy versus the markets.
/// </summary>
public sealed class EconomyState
{
    // Instance properties from lines 152–202. Risk drives the volatility kick at the
    // extremes; headline is the newspaper story id this index publishes.
    public EconomicIndex Main { get; }      = new(risk: 4,  headline: 20);
    public EconomicIndex Invest { get; }    = new(risk: 4,  headline: 18);
    public EconomicIndex Goods { get; }     = new(risk: 4,  headline: 20);
    public EconomicIndex Gold { get; }      = new(risk: 2,  headline: 5);
    public EconomicIndex Silver { get; }    = new(risk: 2,  headline: 7);
    public EconomicIndex Pork { get; }      = new(risk: 4,  headline: 9);
    public EconomicIndex BlueChip { get; }  = new(risk: 1,  headline: 11);
    public EconomicIndex Penny { get; }     = new(risk: 10, headline: 13);

    /// <summary>Crash severity rolled this week: 0 = none, else 1–3 (global373).</summary>
    /// <remarks>
    /// The setters on these three are <c>internal</c> for one caller: <c>Game.Restore</c>.
    /// Only <see cref="Tick"/> and <see cref="ClearHeadline"/> may write them during play.
    /// </remarks>
    public int CrashSeverity { get; internal set; }

    /// <summary>Whether a boom fired this week (global444).</summary>
    public bool Boom { get; internal set; }

    /// <summary>Newspaper headline id published this week (global415).</summary>
    public int Headline { get; internal set; }

    /// <summary>
    /// global374 — the volatility divisor. Crash and boom each roll
    /// `Random(0, Volatility * 30) == 0`, so a LARGER value makes events RARER.
    /// TODO: the value is set outside script 107; find where it is initialised
    /// (likely game setup in Main.sc or room1.sc) and whether the player can change it.
    /// Until then, 1 gives a 1-in-31 chance per week, which matches the game's
    /// reputation for regular but not constant upheaval.
    /// </summary>
    public int Volatility { get; set; } = 1;

    private IEnumerable<EconomicIndex> All =>
        [Main, Invest, Goods, Gold, Silver, Pork, BlueChip, Penny];

    public EconomyState()
    {
        // TODO: confirm the starting readings and indices from game setup. 100 is the
        // neutral point of the 70..190 band and the value all the arithmetic centres on.
        foreach (var ix in All) { ix.Reading = 100; ix.Index = 0; }
    }

    /// <summary>Advances the economy one week. <paramref name="week"/> is global372.</summary>
    public void Tick(IRandomSource rng, int week)
    {
        // Lines 207–222. Both events are gated on week >= 8 so the game cannot be
        // wrecked before players have established themselves. A crash additionally
        // requires the economy not to be depressed already (>= 80); a boom requires
        // it not to be overheated (<= 120).
        CrashSeverity = 0;
        Boom = false;

        var mainReadingBefore = Main.Reading;

        if (mainReadingBefore >= 80 && week >= 8 && rng.Next(0, Volatility * 30) == 0)
        {
            CrashSeverity = rng.Next(1, 3);
            Headline = CrashSeverity; // line 217: the crash sets the headline directly
        }
        else if (week >= 8 && mainReadingBefore <= 120 && rng.Next(0, Volatility * 30) == 0)
        {
            Boom = true;
        }

        // Lines 223–246. Order matters: each index reads the parent's NEW trend.
        // `lowest` and `highest` track the extremes for headline selection (localproc_0,
        // lines 17–26). Both start at Main, and note that Main itself is never passed
        // to the tracker — only the seven below it can become the headline.
        Advance(Main, null, rng);
        var lowest = Main;
        var highest = Main;

        void Track(EconomicIndex ix)
        {
            if (ix.Index < lowest.Index) lowest = ix;
            else if (ix.Index > highest.Index) highest = ix;
        }

        Advance(Invest, Main.Index, rng);   Track(Invest);
        Advance(Goods, Main.Index, rng);    Track(Goods);   // follows MAIN, not invest
        Advance(Gold, Invest.Index, rng);   Track(Gold);
        Advance(Silver, Invest.Index, rng); Track(Silver);
        Advance(Pork, Invest.Index, rng);   Track(Pork);
        Advance(BlueChip, Invest.Index, rng); Track(BlueChip);
        Advance(Penny, Invest.Index, rng);  Track(Penny);

        // Lines 255–277. A crash has already claimed the headline, so this only runs in
        // a normal week. The more extreme of the two tails wins; a bad tail publishes
        // headline+1 (the negative variant of the story), a good tail publishes headline.
        if (Headline != 0 || CrashSeverity != 0) return;

        if (SciMath.Abs(lowest.Index) > SciMath.Abs(highest.Index))
        {
            if (Publishes(lowest.Index, rng)) Headline = lowest.Headline + 1;
        }
        else if (Publishes(highest.Index, rng))
        {
            Headline = highest.Headline;
        }
    }

    /// <summary>Clears the headline at the start of a week (global415 reset).</summary>
    public void ClearHeadline() => Headline = 0;

    private void Advance(EconomicIndex ix, int? parentIndex, IRandomSource rng)
    {
        ix.Init(ix.Index, ix.Reading, rng, CrashSeverity, Boom);
        ix.Doit(parentIndex, CrashSeverity, Boom);
    }

    /// <summary>
    /// Lines 258–273. A swing of 3 or more publishes on a 3-in-4 roll; a swing of
    /// exactly 2 publishes on a 1-in-3 roll. Anything milder is not news.
    /// </summary>
    private static bool Publishes(int index, IRandomSource rng)
    {
        var mag = SciMath.Abs(index);
        if (mag >= 3) return rng.Next(0, 3) != 0;
        if (mag == 2) return rng.Next(0, 2) == 0;
        return false;
    }
}
