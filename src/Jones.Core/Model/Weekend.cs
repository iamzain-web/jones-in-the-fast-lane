using Jones.Core.Sci;

namespace Jones.Core.Model;

/// <param name="TextId">Index into <see cref="Weekend.Texts"/>.</param>
/// <param name="Cost">Money spent, already capped at what the player is worth.</param>
/// <param name="Happiness">Happiness gained (only the charity weekends give any).</param>
public sealed record WeekendResult(int TextId, int Cost, int Happiness);

/// <summary>
/// The weekend that opens every turn, ported from `weekend.sc` (script 232).
///
/// It is not flavour text: the weekend COSTS MONEY every week, and how much depends on
/// which one you get. It is also themed by what you own — buy a hot tub and you start
/// throwing hot tub parties — which is one of the quiet ways the game rewards spending.
/// </summary>
public static class Weekend
{
    /// <summary>Background art for the weekend dialog (`weekend.sc:256`).</summary>
    public const int BackgroundView = 608;

    public static readonly string[] Texts =
    [
        /*  0 */ "Filler",
        /*  1 */ "You spent the whole weekend watching some of the food in your refrigerator grow mold and spores. It sure was fun.",
        /*  2 */ "You spent the whole weekend watching the water in your refrigerator freeze.",
        /*  3 */ "You spent the whole weekend baking oatmeal cookies.",
        /*  4 */ "You spent the entire weekend watching Star Trek reruns.",
        /*  5 */ "You rented some movies and ate artificially flavored buttered popcorn.",
        /*  6 */ "You spent the weekend playing your stereo and patching the plaster your speakers cracked.",
        /*  7 */ "You spent the weekend cleaning your microwave after you tried to dry your pet rat in it. You also need a new pet rat.",
        /*  8 */ "You and some friends had a hot tub party this weekend.",
        /*  9 */ "You played games on your computer all weekend.",
        /* 10 */ "You watched CELEBRITY INCOME TAX EVASION on TV this weekend.",
        /* 11 */ "You read all about the mating habits of the North American computer programmer in your encyclopedia.",
        /* 12 */ "You read your dictionary all weekend. Boy, was that fun.",
        /* 13 */ "You read your atlas and committed the population of 43 countries to memory. OH WOW!!!",
        /* 14 */ "You went to the baseball game this weekend and ate hotdogs till you puked.",
        /* 15 */ "You went to the theatre this weekend and saw the one MAN version of Cats.",
        /* 16 */ "You had front row seats at a rock concert. The doctor said that the hearing loss shouldn't be permanent.",
        /* 17 */ "You watched them change the mannequins at QT Clothing this weekend.",
        /* 18 */ "You washed and waxed your marble this weekend right before it rained.",
        /* 19 */ "You stayed home and did absolutely nothing this weekend.",
        /* 20 */ "You spent the weekend hiking around Yosemite.",
        /* 21 */ "You listened to the Talking Bear 256 times this weekend.",
        /* 22 */ "You read the 'Wall Street Journal' this weekend.",
        /* 23 */ "You thought about what you would do on your next turn.",
        /* 24 */ "You spent the weekend in a hotel because they had to fumigate your apartment.",
        /* 25 */ "You played in a ping pong tournament this weekend.",
        /* 26 */ "You pitched horseshoes in your apartment all weekend. The people downstairs love you.",
        /* 27 */ "You sat around and played solitaire all weekend.",
        /* 28 */ "You went panning for gold this weekend, but all you got was wet.",
        /* 29 */ "You spent the weekend in the laundromat washing your clothes. Now that was exciting.",
        /* 30 */ "You took a friend out to a cheap restaurant this weekend.",
        /* 31 */ "You went out and caught your own froglegs this weekend.",
        /* 32 */ "You crawled around on your knees chasing snails this weekend.",
        /* 33 */ "You spent your weekend thinking about work. Eccch.",
        /* 34 */ "You spent your weekend trying to remove the mildew between the shower tiles.",
        /* 35 */ "You spent the weekend listening to the newlyweds in the next apartment set up a new waterbed.",
        /* 36 */ "This weekend, you won first prize in a beauty contest and collected $10. Whoops, wrong game.",
        /* 37 */ "This weekend, you closed your curtains, locked your doors, turned off the lights, and ate presweetened morning breakfast cereal, with little marshmallows!",
        /* 38 */ "You played stickball this weekend with the neighborhood kids and ended up wrenching your back and spraining your ankle.",
        /* 39 */ "You read a romance novel, NURSE'S TURN TO CRY, in one sitting.",
        /* 40 */ "You took a long hot bath this weekend and emerged looking like a California Raisin.",
        /* 41 */ "You watched a torrid romance movie, LIBRARIAN'S DILEMMA, this weekend.",
        /* 42 */ "One of your fillings came loose this weekend. It's a good thing you're handy with a soldering iron.",
        /* 43 */ "You spent the weekend examining yourself under the fluorescent lights in the bathroom. Eccch!",
        /* 44 */ "You spent the weekend wondering if black holes were lit with black lights.",
        /* 45 */ "This weekend, you hung out at the mall, filled up on junk food, and made your mother ashamed of you.",
        /* 46 */ "You went bowling with friends this weekend.",
        /* 47 */ "You played two rounds of golf this weekend.",
        /* 48 */ "This weekend, you had to bail your nephew out of jail.",
        /* 49 */ "You had your marble repainted this weekend.",
        /* 50 */ "You played in a volleyball tournament this weekend.",
        /* 51 */ "You took a friend out to an expensive restaurant this weekend.",
        /* 52 */ "You went to San Diego to play in the Over The Line Tournament.",
        /* 53 */ "You went to Las Vegas in a $20,000 car and came back in a $200,000 Greyhound bus.",
        /* 54 */ "You tried to drive to Hawaii to watch a surfing contest.",
        /* 55 */ "You went scuba diving in La Jolla.",
        /* 56 */ "You went deep sea fishing this weekend.",
        /* 57 */ "You volunteered to take the local scouts to Disneyland.",
        /* 58 */ "You drove the senior citizens' bus this weekend and they drove you - crazy.",
        /* 59 */ "You helped several little old ladies cross the street to get to their aerobics class.",
        /* 60 */ "You visited a sick friend in the hospital. REALLY!",
    ];

    /// <summary>Durables 21..33 each have their own weekend, at text id (item - 20).</summary>
    private const int DurableFirst = 21;
    private const int DurableLast = 33;

    /// <summary>
    /// Rolls the weekend and applies it. <paramref name="lastTextId"/> is the original's
    /// `global421`, which stops the same weekend happening twice running; pass the
    /// previous result back in.
    /// </summary>
    public static WeekendResult Roll(Player p, IRandomSource rng, int week, ref int lastTextId)
    {
        // The three budgets the original rolls up front, whichever weekend is chosen.
        var cheap = p.NetWorth > 0 ? rng.Next(5, 20) : 0;
        var ticket = rng.Next(15, 55);
        var lavish = week < 8 ? rng.Next(15, 55) : rng.Next(50, 100);

        // 1. Tickets are consumed first, in order, and the weekend is the event you
        //    bought them for. Buying tickets is therefore a deliberate purchase of a
        //    specific weekend.
        foreach (var (item, id) in new[] { (37, 14), (38, 15), (39, 16) })
        {
            var held = p.Consumables.AtHeld(item);
            if (held is null) continue;

            held.Quantity = 0;
            return Apply(p, id, ticket, 0, ref lastTextId);
        }

        // 2. Otherwise a weekend themed by something you own.
        var durable = RollDurableWeekend(p, rng, lastTextId);
        if (durable != 0) return Apply(p, durable, cheap, 0, ref lastTextId);

        // 3. Otherwise a generic weekend, never the same as last week's.
        int textId;
        do { textId = rng.Next(17, 58); } while (textId == lastTextId);

        // The charity weekends are the only ones that pay you back, in happiness.
        var happiness = textId >= 58 ? rng.Next(2, 4) : 0;

        // How expensive the weekend was depends on which one it was.
        var budget = textId <= 44 ? cheap : textId <= 52 ? ticket : lavish;
        return Apply(p, textId, budget, happiness, ref lastTextId);
    }

    /// <summary>
    /// Port of `localproc_1`. Scans durables 21..33 and picks the first that is both held
    /// and passes a 1-in-(4 x durable count) roll, so the more you own the less likely any
    /// single item is to set the theme.
    /// </summary>
    private static int RollDurableWeekend(Player p, IRandomSource rng, int lastTextId)
    {
        var count = p.Durables.Count;
        if (count == 0) return 0;

        for (var item = DurableFirst; item <= DurableLast; item++)
        {
            if (!p.Durables.Holds(item)) continue;
            if (rng.Next(0, 4 * count) != 0) continue;

            var id = item - 20;
            if (id == lastTextId) return 0;   // never twice running
            return id;
        }

        return 0;
    }

    private static WeekendResult Apply(Player p, int textId, int budget, int happiness,
                                       ref int lastTextId)
    {
        lastTextId = textId;

        // You can only spend what you have.
        var cost = 0;
        if (p.NetWorth > 0)
        {
            cost = budget;
            if (p.NetWorth < cost) cost = (int)p.NetWorth;
            p.Cash -= cost;
        }

        if (happiness != 0) p.HapStat += happiness;

        return new WeekendResult(textId, cost, happiness);
    }

    public static string TextFor(int id) => id >= 0 && id < Texts.Length ? Texts[id] : Texts[19];
}
