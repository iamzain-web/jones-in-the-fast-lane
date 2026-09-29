namespace Jones.Core.Sci;

/// <summary>
/// SCI's `Random` kernel call.
///
/// CRITICAL: SCI's Random(a, b) is inclusive of BOTH bounds, unlike .NET's
/// Random.Next(a, b) which excludes the upper. Getting this wrong shifts every
/// probability in the game — `(not (Random 0 (* g374 30)))` is a 1-in-(30g+1) chance,
/// not 1-in-30g.
/// </summary>
public interface IRandomSource
{
    /// <summary>Uniform integer in [min, max], both inclusive.</summary>
    int Next(int min, int max);
}

/// <summary>
/// Default seedable implementation.
///
/// This does NOT reproduce the 1991 DOS interpreter's PRNG sequence, and cannot:
/// ScummVM replaced Sierra's original generator with its own, so no available
/// reference produces the original stream either. Verification therefore tests
/// formulas exactly (given a fixed roll) and distributions statistically, rather
/// than diffing sequences against the original.
/// </summary>
public sealed class SciRandom : IRandomSource
{
    private readonly Random _rng;

    public SciRandom(int seed) => _rng = new Random(seed);

    public int Next(int min, int max)
    {
        if (min > max) (min, max) = (max, min);
        return _rng.Next(min, max + 1); // +1: SCI's upper bound is inclusive
    }
}

/// <summary>
/// Test double that replays a fixed sequence of rolls, so a formula can be asserted
/// exactly. Used by the economy tests to pin every branch of the original's arithmetic.
/// </summary>
public sealed class ScriptedRandom : IRandomSource
{
    private readonly int[] _rolls;
    private int _i;

    public ScriptedRandom(params int[] rolls) => _rolls = rolls;

    public int Consumed => _i;

    public int Next(int min, int max)
    {
        if (_i >= _rolls.Length)
            throw new InvalidOperationException(
                $"ScriptedRandom exhausted after {_rolls.Length} rolls; asked for [{min},{max}]");
        var v = _rolls[_i++];
        if (v < min || v > max)
            throw new InvalidOperationException(
                $"scripted roll {v} (#{_i - 1}) is outside the requested range [{min},{max}]");
        return v;
    }
}
