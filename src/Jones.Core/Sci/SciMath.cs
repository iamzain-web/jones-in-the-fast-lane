namespace Jones.Core.Sci;

/// <summary>
/// Arithmetic that matches Sierra's SCI interpreter.
///
/// Every number in the original game is a 16-bit signed integer and every division
/// truncates. Using floating point anywhere in ported game logic will silently diverge:
/// the economy alone divides three times per index per week, and the drift compounds.
///
/// C#'s int division already truncates toward zero, which matches x86 IDIV as SCI used it,
/// so <see cref="Div"/> exists to make the intent explicit at call sites rather than to
/// change behaviour.
/// </summary>
public static class SciMath
{
    /// <summary>Truncating integer division, as SCI's `/` operator.</summary>
    public static int Div(int a, int b) => a / b;

    /// <summary>SCI's `Abs`.</summary>
    public static int Abs(int a) => a < 0 ? -a : a;

    /// <summary>
    /// A 16-bit signed multiply that WRAPS on overflow, exactly as the original's
    /// arithmetic does. This is not paranoia: the price function at `n109.sc:30` tests
    /// whether its own product has gone negative and substitutes 32767 when it has, so
    /// the wrap is load-bearing behaviour and a widened multiply would skip that branch.
    /// </summary>
    public static short Mul16(int a, int b) => unchecked((short)(a * b));

    /// <summary>
    /// Clamps to the 16-bit signed range. SCI values wrap rather than saturate, but no
    /// documented Jones value approaches the boundary; this asserts that assumption in debug
    /// builds so a future modification that breaks it is noticed rather than silently wrong.
    /// </summary>
    public static int Word(int a)
    {
        System.Diagnostics.Debug.Assert(a is >= short.MinValue and <= short.MaxValue,
            $"value {a} left the 16-bit range SCI guarantees");
        return a;
    }
}
