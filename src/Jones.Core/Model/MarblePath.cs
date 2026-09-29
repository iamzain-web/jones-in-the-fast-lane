namespace Jones.Core.Model;

/// <summary>
/// The walking path around the board, taken verbatim from `marblePath.sc` (script 101).
///
/// The player token walks these points one at a time; it does not teleport between
/// buildings. Journey length is what consumes the turn's hours, which is why the
/// board layout is the whole strategy of the game.
/// </summary>
public static class MarblePath
{
    /// <summary>Number of steps in the ring. The original stores this as element 0.</summary>
    public const int StepCount = 170;

    /// <summary>X coordinates in the original 320x200 screen space.</summary>
    public static readonly int[] X =
    [
        144, 150, 156, 162, 168, 174, 180, 186, 192, 198, 204, 210, 216, 222, 228, 234, 240, 246, 252, 258,
        264, 270, 276, 282, 280, 275, 270, 264, 259, 257, 259, 264, 270, 276, 282, 288, 294, 299, 304, 306,
        306, 304, 299, 294, 288, 282, 276, 281, 286, 291, 296, 301, 305, 305, 306, 306, 306, 306, 307, 307,
        304, 299, 294, 288, 282, 276, 270, 264, 258, 252, 246, 240, 234, 228, 222, 216, 210, 204, 198, 193,
        188, 183, 178, 172, 167, 162, 158, 152, 148, 143, 138, 133, 128, 123, 118, 113, 108, 103, 97, 91,
        85, 79, 73, 67, 61, 55, 49, 43, 37, 31, 25, 19, 14, 11, 11, 14, 19, 21, 23, 24,
        30, 36, 42, 48, 54, 59, 62, 63, 62, 61, 58, 54, 49, 43, 37, 31, 25, 19, 16, 14,
        13, 15, 19, 24, 29, 34, 39, 40, 38, 33, 29, 35, 41, 47, 53, 58, 63, 68, 74, 80,
        86, 92, 98, 104, 109, 115, 120, 126, 132, 138,
    ];

    /// <summary>Y coordinates in the original 320x200 screen space.</summary>
    public static readonly int[] Y =
    [
        37, 37, 38, 39, 40, 40, 39, 39, 39, 39, 39, 40, 40, 40, 40, 40, 39, 38, 37, 37,
        38, 39, 40, 39, 43, 47, 52, 55, 60, 66, 72, 76, 79, 80, 81, 83, 86, 89, 94, 100,
        106, 112, 117, 119, 120, 121, 121, 126, 128, 130, 133, 137, 142, 148, 152, 158, 164, 170, 176, 182,
        187, 189, 189, 189, 188, 187, 187, 188, 188, 188, 190, 191, 190, 189, 188, 188, 189, 189, 189, 186,
        184, 179, 177, 175, 173, 169, 169, 169, 169, 170, 173, 176, 180, 183, 185, 187, 188, 188, 188, 188,
        188, 187, 186, 185, 185, 185, 186, 187, 187, 187, 187, 187, 183, 178, 173, 167, 162, 157, 153, 149,
        150, 150, 150, 150, 149, 146, 141, 135, 129, 123, 118, 114, 111, 110, 109, 108, 107, 105, 100, 94,
        88, 83, 78, 74, 70, 66, 63, 59, 54, 50, 45, 45, 45, 44, 44, 42, 40, 37, 37, 37,
        38, 38, 39, 39, 39, 39, 39, 38, 38, 37,
    ];

    /// <summary>Position of a path index (1-based, as the original indexes it).</summary>
    public static (int X, int Y) At(int index)
    {
        var i = ((index - 1) % StepCount + StepCount) % StepCount;
        return (X[i], Y[i]);
    }

    /// <summary>
    /// Walks from one path index to another the way `MarblePath::setDirection` does:
    /// whichever way round is SHORTER. The marble does not always travel forward.
    /// </summary>
    public static IReadOnlyList<int> Route(int from, int to)
    {
        var forward = ((to - from) % StepCount + StepCount) % StepCount;
        var backward = StepCount - forward;
        var steps = new List<int>();

        if (forward <= backward)
            for (var i = 1; i <= forward; i++) steps.Add(((from + i - 1) % StepCount) + 1);
        else
            for (var i = 1; i <= backward; i++) steps.Add(((from - i - 1 + StepCount * 2) % StepCount) + 1);

        return steps;
    }

    /// <summary>Steps between two indices, always by the shorter arc.</summary>
    public static int Distance(int from, int to)
    {
        var forward = ((to - from) % StepCount + StepCount) % StepCount;
        return Math.Min(forward, StepCount - forward);
    }
}

