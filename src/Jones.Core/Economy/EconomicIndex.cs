using Jones.Core.Sci;

namespace Jones.Core.Economy;

/// <summary>
/// Direct port of the EconomicIndex class in `economicIndex.sc` (script 107) from
/// jones-cd-dos-1.0. Line references below are to that file.
///
/// One instance per tradable index. `Index` is the trend (roughly -3..+3), `Reading`
/// is the value (clamped 70..190), `Risk` scales the volatility kick at the extremes.
/// </summary>
public sealed class EconomicIndex
{
    public int Index { get; set; }
    public int Reading { get; set; }
    public int Risk { get; }
    public int Headline { get; }

    /// <summary>Upward bias when the reading is cheap. Recomputed each tick.</summary>
    public int High { get; private set; }

    /// <summary>Downward bias when the reading is expensive. Recomputed each tick.</summary>
    public int Low { get; private set; }

    public int Adjustment { get; private set; }

    public EconomicIndex(int risk, int headline)
    {
        Risk = risk;
        Headline = headline;
    }

    /// <summary>
    /// Port of `(method (init param1 param2))`, lines 41–129.
    /// Call with the previous tick's index and reading, then call <see cref="Doit"/>.
    /// </summary>
    /// <param name="crashSeverity">global373 — 0 for none, else 1–3.</param>
    /// <param name="boom">global444.</param>
    public void Init(int index, int reading, IRandomSource rng, int crashSeverity, bool boom)
    {
        Index = index;
        Reading = reading;

        // Lines 44–65. Note the else-branches: an index already at or beyond the bound
        // is nudged one further rather than clamped, so Index can legitimately sit
        // outside -3..+3 on entry to the walk below. This is the original's behaviour,
        // not an oversight in the port.
        if (crashSeverity != 0)
        {
            if (Index > -3)
            {
                Index -= 3;
                if (Index < -3) Index = -3;
            }
            else Index--;
        }
        else if (boom)
        {
            if (Index < 3)
            {
                Index += 3;
                if (Index > 3) Index = 3;
            }
            else Index++;
        }

        // Lines 66–83. Mean reversion: cheap readings gain upward headroom,
        // expensive ones gain downward headroom.
        High = Reading < 80 ? 2 : Reading < 90 ? 1 : 0;
        Low = Reading > 160 ? 2 : Reading > 130 ? 1 : 0;

        // Lines 84–108. Trend random walk, bounded by the reversion bias.
        var roll = rng.Next(100 + (-3 - Low), 100 + 3 + High) - 100;
        if (roll < Index)
        {
            Index--;
            if (Index < -3 - Low) Index = -3 - Low;
        }
        else if (roll > Index)
        {
            Index++;
            if (Index > 3 + High) Index = 3 + High;
        }

        // Lines 109–118. A negative trend widens the downside, a positive one the upside.
        var lowerRange = -3;
        var upperRange = 3;
        if (Index < 0) lowerRange = -3 + 2 * Index;
        else if (Index > 0) upperRange = 3 + 2 * Index;

        // Lines 119–128.
        Adjustment = rng.Next(100 + lowerRange, 100 + upperRange) - 100;

        // ORIGINAL BUG — replicated deliberately. Both arms of the cond at lines 121–128
        // test `(== adjustment lowerRange)`. The second was plainly meant to test
        // upperRange. As written, the extra downside kick fires when the roll bottoms out,
        // but the matching upside kick is unreachable dead code. The original economy is
        // therefore biased downward at the extremes, and penny stocks (Risk 10) feel it
        // hardest. Do not "fix" this without deciding it is a modification.
        if (Adjustment == lowerRange)
            Adjustment -= rng.Next(0, Risk * SciMath.Abs(Index));
        // (unreachable in the original:)
        // else if (Adjustment == upperRange) Adjustment += rng.Next(0, Risk * Abs(Index));
    }

    /// <summary>
    /// Port of `(method (doit param1))`, lines 131–149. Returns the new reading.
    /// </summary>
    /// <param name="parentIndex">
    /// The trend of the index this one follows, divided by 3 and added to the drift.
    /// Pass <c>null</c> for the main index, which the original calls with no argument.
    /// </param>
    public int Doit(int? parentIndex, int crashSeverity, bool boom)
    {
        // Line 132 computes `temp0 = argc ? param1 : 0` and then line 133 uses `param1`
        // directly, ignoring temp0. When called with no argument — which is exactly how
        // mainI is invoked at line 223 — `param1` reads an uninitialised stack slot.
        // That is undefined behaviour in the original, not a rule we can reproduce, so
        // the port uses the evident intent (temp0, i.e. 0). Flagged as the one knowing
        // divergence from the original in this module.
        var parent = parentIndex ?? 0;

        Reading += Adjustment + SciMath.Div(parent, 3);

        // Lines 134–141. Crash cuts 15/20/25% by severity; boom adds a flat 10%.
        if (crashSeverity != 0)
            Reading = SciMath.Div(Reading * (16 + crashSeverity), 20);
        else if (boom)
            Reading = SciMath.Div(Reading * 11, 10);

        // Lines 142–147.
        if (Reading < 70) Reading = 70;
        if (Reading > 190) Reading = 190;

        return Reading;
    }
}
