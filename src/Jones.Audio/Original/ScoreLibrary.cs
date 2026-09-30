using System.Reflection;

namespace Jones.Audio.Original;

/// <summary>
/// The original cues, by name. Each is a <c>.score</c> text file compiled into this
/// assembly, so both heads read them the same way and neither needs a file path.
/// </summary>
public static class ScoreLibrary
{
    private const string Prefix = "Jones.Audio.Original.Scores.";

    /// <summary>Every cue that exists, without the extension.</summary>
    public static IReadOnlyList<string> Names { get; } =
        typeof(ScoreLibrary).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) &&
                        n.EndsWith(".score", StringComparison.Ordinal))
            .Select(n => n[Prefix.Length..^".score".Length])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    public static string Text(string name)
    {
        using var stream = typeof(ScoreLibrary).Assembly
            .GetManifestResourceStream(Prefix + name + ".score")
            ?? throw new FileNotFoundException($"no score called '{name}'");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static Score Load(string name) => Score.Parse(Text(name));

    /// <summary>
    /// Which original cue stands in for which of the game's sound resources.
    ///
    /// The key is the resource number the scripts ask for â€” `(gASong playBed: 43)` and the
    /// rest â€” so the switch between the two audio paths is a lookup and nothing else has to
    /// know that a second set of music exists. A resource with no entry has not been written
    /// yet and falls back to the AdLib arrangement, which means the two can be mixed while
    /// the set is being filled in.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, string> ForResource =
        new Dictionary<int, string>
        {
            [5] = "res05-town-board",
            [34] = "res34-security-apartments",
            [43] = "res43-employment-office",
            [46] = "res46-factory",
            [47] = "res47-bank-broker",
            [37] = "res37-zmart",
            [39] = "res39-qt-clothing",
            [40] = "res40-socket-city",
            [49] = "res49-blacks-market",
            [50] = "res50-pawn-shoppe",
            [41] = "res41-university",
            [35] = "res35-rent-office",
            [36] = "res36-low-cost-housing",
            [48] = "res48-broker-floor",

            // ONE CUE, TWO RESOURCES. 10.sound and 38.sound are byte-identical - Sierra
            // shipped one arrangement as both the restart bed (`Main.sc:1219`) and Monolith
            // Burgers (`fastFood.sc:104`), and the original set does the same rather than
            // writing the same music twice.
            [10] = "res10-monolith-burgers",
            [38] = "res10-monolith-burgers",

            // The stings.
            [7] = "res07-winner-fanfare",
            [9] = "res09-the-weekend",
            [20] = "res20-mugging",
            [27] = "res27-eviction-notice",
            [30] = "res30-sacked",
            [42] = "res42-diploma",
            [44] = "res44-bad-news",
            [45] = "res45-good-news",

            // THE TITLE, and the low-polyphony fallback aliased to it. 100 exists only for
            // hardware that could not manage four voices at once (`Main.sc:1188-1191`);
            // this synthesiser has no such limit, so composing a second 120-second piece
            // that only a constraint we do not have would ever select is wasted work.
            [6] = "res06-title-theme",
            [100] = "res06-title-theme",
        };

    /// <summary>The original cue for a game resource, or null where none is written yet.</summary>
    public static Score? ForSound(int resource) =>
        ForResource.TryGetValue(resource, out var name) ? Load(name) : null;
}





