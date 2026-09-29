using Jones.Core.Economy;
using Jones.Core.Sci;
using Xunit;

namespace Jones.Tests;

/// <summary>
/// Pins the arithmetic of `economicIndex.sc` exactly. Each test scripts the RNG so the
/// expected value is computed by hand from the original source rather than from this
/// port — otherwise the tests only prove the port agrees with itself.
///
/// Roll order inside Init is: (1) the trend walk, (2) the adjustment, (3) the risk kick,
/// if and only if the adjustment lands on lowerRange.
/// </summary>
public class EconomicIndexTests
{
    private static EconomicIndex MainIndex() => new(risk: 4, headline: 20);

    [Fact]
    public void NeutralWeek_LeavesReadingUnchanged()
    {
        // reading 100 → High=0, Low=0. Walk rolls 100 → 0, equal to index, so no move.
        // Adjustment rolls 100 → 0. No crash, no boom, no parent drift.
        var ix = MainIndex();
        var rng = new ScriptedRandom(100, 100);

        ix.Init(index: 0, reading: 100, rng, crashSeverity: 0, boom: false);
        var reading = ix.Doit(parentIndex: null, crashSeverity: 0, boom: false);

        Assert.Equal(0, ix.Index);
        Assert.Equal(100, reading);
        Assert.Equal(2, rng.Consumed);
    }

    [Theory]
    // reading 100, crash severity s → 100 * (16 + s) / 20, truncating.
    [InlineData(1, 85)]  // 1700/20
    [InlineData(2, 90)]  // 1800/20
    [InlineData(3, 95)]  // 1900/20
    public void Crash_CutsReadingBySeverity(int severity, int expected)
    {
        // Crash drives index from 0 to -3, so the walk range is [-3,3] and the roll of
        // 100 (→ 0) is greater than -3, nudging index to -2. lowerRange becomes -7, so
        // the adjustment roll of 100 (→ 0) does not land on it and takes no risk kick.
        var ix = MainIndex();
        var rng = new ScriptedRandom(100, 100);

        ix.Init(index: 0, reading: 100, rng, crashSeverity: severity, boom: false);
        var reading = ix.Doit(parentIndex: null, crashSeverity: severity, boom: false);

        Assert.Equal(expected, reading);
        Assert.Equal(-2, ix.Index);
    }

    [Fact]
    public void Boom_AddsTenPercent()
    {
        // Boom drives index 0 → 3; walk roll 100 (→0) is less than 3, so index falls to 2.
        // upperRange becomes 7; adjustment roll 100 (→0) misses lowerRange (-3).
        var ix = MainIndex();
        var rng = new ScriptedRandom(100, 100);

        ix.Init(index: 0, reading: 100, rng, crashSeverity: 0, boom: true);
        var reading = ix.Doit(parentIndex: null, crashSeverity: 0, boom: true);

        Assert.Equal(110, reading); // 100 * 11 / 10
        Assert.Equal(2, ix.Index);
    }

    [Fact]
    public void MeanReversion_CheapReadingGainsUpwardHeadroom()
    {
        // reading 75 (< 80) → High = 2, so the walk range widens to [-3, 5].
        var ix = MainIndex();
        var rng = new ScriptedRandom(105, 100); // walk roll 5, adjustment 0

        ix.Init(index: 0, reading: 75, rng, crashSeverity: 0, boom: false);

        Assert.Equal(2, ix.High);
        Assert.Equal(0, ix.Low);
        Assert.Equal(1, ix.Index); // roll 5 > index 0 → index++ (capped at 3 + High = 5)
    }

    [Fact]
    public void MeanReversion_ExpensiveReadingGainsDownwardHeadroom()
    {
        // reading 165 (> 160) → Low = 2, so the walk range widens to [-5, 3].
        var ix = MainIndex();
        var rng = new ScriptedRandom(95, 100); // walk roll -5, adjustment 0

        ix.Init(index: 0, reading: 165, rng, crashSeverity: 0, boom: false);

        Assert.Equal(0, ix.High);
        Assert.Equal(2, ix.Low);
        Assert.Equal(-1, ix.Index); // roll -5 < index 0 → index-- (floored at -3 - Low = -5)
    }

    [Fact]
    public void ReadingIsClampedToTheSeventyToOneNinetyBand()
    {
        // reading 71 is below 80, so High = 2 and the walk range widens to [-3, 5].
        // The adjustment then rolls -3, landing exactly on lowerRange, which fires the
        // risk kick — and the kick still consumes a roll even though index is 0 and
        // Random(0, 0) can only return 0. Hence three rolls, not two.
        var low = MainIndex();
        low.Init(index: 0, reading: 71, new ScriptedRandom(100, 97, 0), crashSeverity: 0, boom: false);
        Assert.Equal(70, low.Doit(null, 0, false)); // 71 - 3 = 68, clamped up to 70

        var high = MainIndex();
        high.Init(index: 0, reading: 189, new ScriptedRandom(100, 103), crashSeverity: 0, boom: false);
        Assert.Equal(190, high.Doit(null, 0, false)); // 189 + 3 = 192, clamped down to 190
    }

    [Fact]
    public void ParentDriftIsAddedWithTruncatingDivision()
    {
        // parentIndex 5 → 5/3 = 1 after truncation, not 1.67.
        var ix = MainIndex();
        ix.Init(index: 0, reading: 100, new ScriptedRandom(100, 100), crashSeverity: 0, boom: false);

        Assert.Equal(101, ix.Doit(parentIndex: 5, crashSeverity: 0, boom: false));
    }

    [Fact]
    public void ParentDriftTruncatesTowardZeroForNegativeParents()
    {
        // -5/3 must be -1 (truncate toward zero), not -2 (floor).
        var ix = MainIndex();
        ix.Init(index: 0, reading: 100, new ScriptedRandom(100, 100), crashSeverity: 0, boom: false);

        Assert.Equal(99, ix.Doit(parentIndex: -5, crashSeverity: 0, boom: false));
    }

    // ---------------------------------------------------------------------------
    // The original bug. See EconomicIndex.Init and MECHANICS.md §12.
    // ---------------------------------------------------------------------------

    [Fact]
    public void BottomOfRange_TakesTheDownsideRiskKick()
    {
        // index -2, walk roll -3 → index -3. lowerRange = -3 + 2(-3) = -9.
        // Adjustment rolls exactly -9, landing on lowerRange, so the kick fires:
        // a third roll of 5 over Random(0, risk 4 * |−3|) = Random(0,12).
        var ix = MainIndex();
        var rng = new ScriptedRandom(97, 91, 5);

        ix.Init(index: -2, reading: 100, rng, crashSeverity: 0, boom: false);
        var reading = ix.Doit(parentIndex: null, crashSeverity: 0, boom: false);

        Assert.Equal(-3, ix.Index);
        Assert.Equal(-14, ix.Adjustment); // -9 - 5
        Assert.Equal(86, reading);
        Assert.Equal(3, rng.Consumed); // the kick consumed a roll
    }

    [Fact]
    public void TopOfRange_TakesNoKick_BecauseTheOriginalsSecondBranchIsDeadCode()
    {
        // Mirror image of the test above. index +2, walk roll +3 → index +3,
        // upperRange = 3 + 2(3) = 9, and the adjustment rolls exactly +9.
        //
        // A symmetric implementation would apply an upward kick here. The original
        // tests `adjustment == lowerRange` in BOTH arms of the cond, so this branch can
        // never fire. Only two rolls are consumed, and the adjustment stays a clean +9.
        //
        // If this test ever fails with a third roll consumed, someone has "fixed" the
        // bug — which is a modification, not a correction.
        var ix = MainIndex();
        var rng = new ScriptedRandom(103, 109);

        ix.Init(index: 2, reading: 100, rng, crashSeverity: 0, boom: false);
        var reading = ix.Doit(parentIndex: null, crashSeverity: 0, boom: false);

        Assert.Equal(3, ix.Index);
        Assert.Equal(9, ix.Adjustment); // no kick applied
        Assert.Equal(109, reading);
        Assert.Equal(2, rng.Consumed); // no third roll
    }

    [Fact]
    public void SciRandomUpperBoundIsInclusive()
    {
        // The single easiest way to get every probability in the game subtly wrong.
        var rng = new SciRandom(seed: 1);
        var seen = new HashSet<int>();
        for (var i = 0; i < 500; i++) seen.Add(rng.Next(0, 3));

        Assert.Equal([0, 1, 2, 3], seen.OrderBy(x => x));
    }
}
