using Jones.Core.Economy;
using Jones.Core.Sci;

// Verification harness. Runs the ported economy headlessly so its behaviour can be
// inspected and its distributions checked over long runs. Everything here exercises
// Jones.Core only — no UI, no platform dependencies.

var weeks = 52;
var seed = 1991;
var runs = 1;

for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--weeks") weeks = int.Parse(args[i + 1]);
    if (args[i] == "--seed") seed = int.Parse(args[i + 1]);
    if (args[i] == "--runs") runs = int.Parse(args[i + 1]);
}

if (runs == 1)
{
    PrintSingleRun(seed, weeks);
}
else
{
    PrintDistribution(seed, weeks, runs);
}

static void PrintSingleRun(int seed, int weeks)
{
    var rng = new SciRandom(seed);
    var econ = new EconomyState();

    Console.WriteLine($"Jones economy — seed {seed}, {weeks} weeks");
    Console.WriteLine("(crash and boom cannot fire before week 8)");
    Console.WriteLine();
    Console.WriteLine("  wk   main  tr   invest  goods   gold  silver   pork    blue  penny  event");
    Console.WriteLine("  " + new string('-', 88));

    for (var week = 1; week <= weeks; week++)
    {
        econ.ClearHeadline();
        econ.Tick(rng, week);

        // Severity 1 is the WORST crash (-15%), 3 the mildest (-5%).
        var evt = econ.CrashSeverity != 0 ? $"CRASH sev{econ.CrashSeverity} (-{5 * (4 - econ.CrashSeverity)}%)"
                : econ.Boom ? "BOOM (+10%)"
                : econ.Headline != 0 ? $"headline {econ.Headline}"
                : "";

        Console.WriteLine(
            $"  {week,2}   {econ.Main.Reading,4}  {econ.Main.Index,+2}   " +
            $"{econ.Invest.Reading,5}  {econ.Goods.Reading,5}  {econ.Gold.Reading,5}  " +
            $"{econ.Silver.Reading,5}  {econ.Pork.Reading,5}  {econ.BlueChip.Reading,6}  " +
            $"{econ.Penny.Reading,5}  {evt}");
    }

    Console.WriteLine();
    Console.WriteLine($"  final main reading {econ.Main.Reading} (band is 70..190, neutral 100)");
}

static void PrintDistribution(int seed, int weeks, int runs)
{
    var crashes = 0;
    var booms = 0;
    var eligibleWeeks = 0;
    var finals = new List<int>();
    var pennyFinals = new List<int>();

    for (var r = 0; r < runs; r++)
    {
        var rng = new SciRandom(seed + r);
        var econ = new EconomyState();

        for (var week = 1; week <= weeks; week++)
        {
            econ.ClearHeadline();
            econ.Tick(rng, week);
            if (week >= 8) eligibleWeeks++;
            if (econ.CrashSeverity != 0) crashes++;
            if (econ.Boom) booms++;
        }

        finals.Add(econ.Main.Reading);
        pennyFinals.Add(econ.Penny.Reading);
    }

    finals.Sort();
    pennyFinals.Sort();

    Console.WriteLine($"Jones economy — {runs} runs x {weeks} weeks, seeds {seed}..{seed + runs - 1}");
    Console.WriteLine();
    Console.WriteLine($"  crash-eligible weeks   {eligibleWeeks}");
    Console.WriteLine($"  crashes                {crashes}  ({100.0 * crashes / eligibleWeeks:F2}% of eligible weeks)");
    Console.WriteLine($"  booms                  {booms}  ({100.0 * booms / eligibleWeeks:F2}% of eligible weeks)");
    Console.WriteLine();
    Console.WriteLine("  final main reading     " + Summarise(finals));
    Console.WriteLine("  final penny reading    " + Summarise(pennyFinals));
    Console.WriteLine();
    Console.WriteLine("  Note: the main index sits near neutral because mean reversion dominates");
    Console.WriteLine("  it. High-risk indices skew low: the original's adjustment cond tests");
    Console.WriteLine("  lowerRange in both arms, so the downside kick fires and the upside kick");
    Console.WriteLine("  is dead code, and the kick scales with risk. Penny stocks (risk 10) feel");
    Console.WriteLine("  it hardest, blue chip (risk 1) barely at all. See MECHANICS.md section 12.");

    static string Summarise(List<int> xs) =>
        $"min {xs[0],3}   median {xs[xs.Count / 2],3}   mean {xs.Average(),6:F1}   max {xs[^1],3}";
}
