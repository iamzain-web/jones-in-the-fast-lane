using System.Reflection;
using System.Text.Json;
using Jones.Core;
using Jones.Core.Economy;
using Jones.Core.Model;
using Jones.Core.Save;
using Jones.Core.Sci;
using Xunit;

namespace Jones.Tests;

/// <summary>
/// Save and restore. The test that matters is <see cref="EverySavedFieldSurvivesTheRoundTrip"/>:
/// a game is filled in until nothing is left at its default, written as JSON, read back and
/// compared field by field BY REFLECTION — so a property added to <c>Player</c> and forgotten
/// in <c>PlayerSnapshot</c> fails here rather than turning up as a lost degree three turns
/// into somebody's restored game.
/// </summary>
[Collection(SaveDirectoryCollection.Name)]
public class SaveRestoreTests
{
    // ------------------------------------------------------------------
    // A game with nothing left at its default
    // ------------------------------------------------------------------

    private static Game FullyPopulated()
    {
        var g = new Game(new SciRandom(12345), playerCount: 3);

        // Week 27, so `week % 4` is not zero and the calendar has plainly moved.
        for (var w = 1; w < 27; w++) g.Calendar.AdvanceWeek();

        g.Clock.HoursRemaining = 17;
        g.Clock.SubHourTicks = 9;

        // Every index carries BOTH a reading and a trend, and no two are alike.
        var e = g.Economy;
        e.Volatility = 3;
        e.CrashSeverity = 2;
        e.Boom = false;
        e.Headline = 19;
        Set(e.Main, -2, 88);
        Set(e.Invest, 3, 145);
        Set(e.Goods, 1, 121);
        Set(e.Gold, -1, 74);
        Set(e.Silver, 2, 133);
        Set(e.Pork, -3, 190);
        Set(e.BlueChip, 0, 101);
        Set(e.Penny, 4, 70);

        PopulatePlayer(g.Players[0], seed: 1);
        PopulatePlayer(g.Players[1], seed: 2);
        PopulatePlayer(g.Players[2], seed: 3);

        g.Players[2].IsJones = true;
        g.Players[1].FinStat = 24;
        g.Winners.Add(g.Players[1]);

        // Mid-turn: the second player is up.
        SetProp(g, nameof(Game.CurrentPlayerIndex), 1);

        g.TurnedDown.Refuse(4);
        g.TurnedDown.Refuse(11);

        var w2 = g.JonesWorld;
        w2.GoodsIndex = 121;
        w2.InvestIndex = 145;
        w2.MainTrend = -2;
        w2.Week = 27;
        w2.HoursUsed = 43;
        w2.SubHourTicks = 9;
        w2.TicksPerHour = 28;
        w2.CurrentPlace = 6;
        w2.WorkedThisTurn = true;
        w2.UsedEmploymentOffice = true;
        w2.UsedZMart = true;
        w2.UsedRentOffice = true;
        w2.BoughtAppliance = true;
        w2.BoughtInvestments = true;
        w2.SoldInvestments = true;

        // The seven per-turn happiness latches, the pending mugging and the broker's
        // once-a-visit charge, plus the last-action results.
        SetLatches(g);

        return g;

        static void Set(EconomicIndex ix, int index, int reading)
        {
            ix.Index = index;
            ix.Reading = reading;
        }
    }

    /// <summary>
    /// The private per-turn latches on <c>Game</c> have no public setters by design, and the
    /// public ones that do are driven by play. Both are set here the way play sets them, so
    /// the snapshot is exercised rather than the test's own reflection.
    /// </summary>
    private static void SetLatches(Game g)
    {
        foreach (var name in new[]
                 {
                     "_hapBaseball", "_hapTheatre", "_hapConcert", "_hapLottery",
                     "_hapFood", "_hapDrinks", "_hapMeals",
                 })
        {
            typeof(Game).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                        .SetValue(g, true);
        }

        SetProp(g, nameof(Game.MuggingPending), true);
        SetProp(g, nameof(Game.LastMugging), true);
        g.BrokerChargedThisVisit = true;
        SetProp(g, nameof(Game.LastWorkResult), Employment.WorkResult.Fired);
        SetProp(g, nameof(Game.LastGarnished), 42);
        SetProp(g, nameof(Game.LastJobOutcome), JobOutcome.NotEnoughExperience);
        SetProp(g, nameof(Game.LastRejection), (RejectionReason?)RejectionReason.Dependability);
    }

    /// <summary>
    /// These properties are written by play and have private setters, which is right — the
    /// test drives them from outside because it is standing in for a hundred turns of play,
    /// not because the encapsulation is wrong.
    /// </summary>
    private static void SetProp(Game g, string name, object? value) =>
        typeof(Game).GetProperty(name)!.GetSetMethod(nonPublic: true)!.Invoke(g, [value]);

    private static void PopulatePlayer(Player p, int seed)
    {
        var n = seed * 7;

        p.ActualName = $"Tester {seed}";
        p.Playing = true;
        p.Location = LocationId.PawnShop;

        p.MonGoal = 60 + seed; p.HapGoal = 70 + seed;
        p.EduGoal = 40 + seed; p.CarGoal = 80 + seed;
        p.MonStat = 33 + seed; p.HapStat = 44 + seed;
        p.EduStat = 19 + seed; p.CarStat = 50 + seed;

        p.Cash = 123456 + n;
        p.NetWorth = 987654 + n;
        p.LqAss = 55555 + n;
        p.InvAss = 4444 + n;
        p.BankBal = 3333 + n;

        p.LivesAt = 1;
        p.CurRent = 615 + n;
        p.RentOwed = 615 + n;
        p.RentExt = 1;
        p.TriedExt = 1;
        p.TurnedOver = true;
        p.LeaveOpen = true;

        p.WorksAt = 5; p.Wage = 27 + seed; p.BaseWage = 21 + seed;
        p.Occupation = 13 + seed; p.Raises = 4;
        p.Uniform = ItemIds.BusinessSuit;
        p.Wearing = ItemIds.DressClothes;

        p.LoanBal = 1800 + n; p.LatePay = 3; p.PaySched = -1; p.MadePay = true;

        p.Relax = 31 + seed; p.Dependibility = 67 + seed; p.MinDepend = 40;
        p.Experience = 55 + seed; p.MaxExper = 90; p.NotEnoughEd = true;

        p.NeedEd1 = 2; p.NeedEd2 = 6;
        p.ExpCredit = 12; p.EduCredit = 9; p.Enrollments = 3;
        p.XCred = 2; p.CoursesDone = 5;

        // Consumables: rent, three kinds of clothing, food, in that order.
        p.Consumables.Receive(ItemIds.SecurityRent, 2).PricePaid = 615;
        p.Consumables.Receive(ItemIds.CasualClothes, 6).PricePaid = 30;
        p.Consumables.Receive(ItemIds.DressClothes, 4).PricePaid = 125;
        p.Consumables.Receive(ItemIds.FreshFood, 3).PricePaid = 90;

        // Durables, including one IN HOCK — ticket bits, redemption price and a zero
        // quantity together — and one FORFEITED and sitting on the resale rack.
        var fridge = p.Durables.Receive(ItemIds.Refrigerator, 1);
        fridge.PricePaid = 650;
        fridge.Attributes = DurableAttributes.Breakable;

        var stereo = p.Durables.Receive(ItemIds.Stereo, 0);
        stereo.PricePaid = 450;
        stereo.RedemptionPrice = 225;
        stereo.Attributes = DurableAttributes.Breakable | DurableAttributes.WearMask;

        var tv = p.Durables.Receive(ItemIds.ColorTV, 0);
        tv.PricePaid = 349;
        tv.RedemptionPrice = 174;
        tv.Attributes = DurableAttributes.Breakable | DurableAttributes.WornOut
                      | DurableAttributes.CheapBuild;

        // Education: one degree finished, one part-way.
        var trade = p.Education.Receive(Degrees.TradeSchool, 10);
        trade.UnitsToGraduate = 10;
        var junior = p.Education.Receive(Degrees.JuniorCollege, 6);
        junior.UnitsToGraduate = 9;

        p.Holdings.Add(Instrument.TBills, 4 + seed);
        p.Holdings.Add(Instrument.Gold, 2);
        p.Holdings.Add(Instrument.Silver, 40);
        p.Holdings.Add(Instrument.Pork, 11);
        p.Holdings.Add(Instrument.BlueChip, 7);
        p.Holdings.Add(Instrument.Penny, 900);
    }

    private static ShellSnapshot FullShell() => new()
    {
        RelaxedThisTurn = true,
        WorkedThisTurn = true,
        GarnishSpokenThisTurn = true,
        LastWeekendId = 13,
        ZMartStock = ["Stereo", "Color TV", "Refrigerator"],
    };

    // ------------------------------------------------------------------
    // The round trip
    // ------------------------------------------------------------------

    [Fact]
    public void EverySavedFieldSurvivesTheRoundTrip()
    {
        var before = FullyPopulated();
        var shell = FullShell();

        // Through actual JSON, not just through the snapshot objects: a property the
        // serialiser cannot see would pass a snapshot-only comparison.
        var json = JsonSerializer.Serialize(
            new SaveFile { State = before.Capture(shell) }, SaveFormatOptions);
        var read = JsonSerializer.Deserialize<SaveFile>(json, SaveFormatOptions)!;

        var after = Game.Restore(read.State, new SciRandom(999));

        AssertSameGame(before, after);
        AssertSameShell(shell, read.State.Shell);
    }

    [Fact]
    public void ARestoredGameKeepsPlayingWhereItLeftOff()
    {
        var before = FullyPopulated();
        var after = RoundTrip(before);

        Assert.Equal(before.CurrentPlayerIndex, after.CurrentPlayerIndex);
        Assert.Equal(before.Current.ActualName, after.Current.ActualName);
        Assert.Equal(17, after.Clock.HoursRemaining);

        // And it is a live game, not a frozen picture of one: a journey still spends the
        // clock through the same accumulator.
        var hoursBefore = after.Clock.HoursRemaining;
        Assert.True(after.TravelTo(LocationId.Bank));
        Assert.True(after.Clock.HoursRemaining < hoursBefore);
    }

    /// <summary>
    /// <c>EconomicIndex.High</c>, <c>Low</c> and <c>Adjustment</c> are deliberately not
    /// saved, on the grounds that <c>init</c> recomputes all three from the reading and the
    /// trend before anything reads them (`economicIndex.sc:66-128`). This is that claim
    /// under test: a restored economy handed the same random stream ticks to exactly the
    /// same numbers as the one it came from.
    /// </summary>
    [Fact]
    public void TheUnsavedEconomyFieldsAreDerivedAndNotNeeded()
    {
        var before = FullyPopulated();

        // Give the live economy a tick first, so High/Low/Adjustment hold real values that
        // the snapshot does not carry.
        before.Economy.Tick(new SciRandom(4242), before.Calendar.Week);

        var after = RoundTrip(before);

        before.Economy.Tick(new SciRandom(77), 27);
        after.Economy.Tick(new SciRandom(77), 27);

        AssertSameEconomy(before.Economy, after.Economy);
    }

    [Fact]
    public void AFreshGameRoundTripsToo()
    {
        var before = new Game(new SciRandom(1), playerCount: 4);
        AssertSameGame(before, RoundTrip(before));
    }

    [Fact]
    public void PawnTicketsAndPricesPaidSurvive()
    {
        var after = RoundTrip(FullyPopulated());
        var p = after.Players[0];

        var stereo = p.Durables.At(ItemIds.Stereo)!;
        Assert.Equal(0, stereo.Quantity);
        Assert.Equal(450, stereo.PricePaid);
        Assert.Equal(225, stereo.RedemptionPrice);
        Assert.Equal(DurableAttributes.WearMask, stereo.Attributes & DurableAttributes.WearMask);

        var tv = p.Durables.At(ItemIds.ColorTV)!;
        Assert.True(tv.Attributes.HasFlag(DurableAttributes.WornOut));

        // And the shop still finds them: `Redeemable` reads the ticket bits and `Buyable`
        // the forfeit bit, so both lists prove the attributes came back as bits, not as a
        // number that merely compares equal.
        Assert.Contains(after.Redeemable(), i => i.IndexNum == ItemIds.Stereo);
        Assert.Contains(after.Buyable(), b => b.Item.IndexNum == ItemIds.ColorTV);
    }

    // ------------------------------------------------------------------
    // The version gate
    // ------------------------------------------------------------------

    [Fact]
    public void AnUnknownVersionIsRefusedRatherThanHalfLoaded()
    {
        InATempSaveDirectory(() =>
        {
            Assert.True(SaveStore.Write(FullyPopulated().Capture()));

            var text = File.ReadAllText(SaveStore.SlotPath);
            File.WriteAllText(SaveStore.SlotPath,
                text.Replace($"\"version\": {SaveFormat.Version}", "\"version\": 4242"));

            Assert.Equal(RestoreOutcome.Unreadable, SaveStore.Read(out var state));
            Assert.Null(state);
        });
    }

    [Fact]
    public void AnotherProgramsJsonIsRefused()
    {
        InATempSaveDirectory(() =>
        {
            File.WriteAllText(SaveStore.SlotPath,
                """{ "version": 1, "game": "something-else", "state": {} }""");

            Assert.Equal(RestoreOutcome.Unreadable, SaveStore.Read(out var state));
            Assert.Null(state);
        });
    }

    [Fact]
    public void RubbishIsRefused()
    {
        InATempSaveDirectory(() =>
        {
            File.WriteAllText(SaveStore.SlotPath, "not json at all {{{");
            Assert.Equal(RestoreOutcome.Unreadable, SaveStore.Read(out _));
        });
    }

    [Fact]
    public void NoSaveAtAllIsItsOwnAnswer()
    {
        InATempSaveDirectory(() =>
        {
            Assert.False(SaveStore.Exists());
            Assert.Equal(RestoreOutcome.NoSaveFound, SaveStore.Read(out var state));
            Assert.Null(state);
        });
    }

    [Fact]
    public void TheSlotIsOverwrittenAndThereIsOnlyOne()
    {
        InATempSaveDirectory(() =>
        {
            var first = FullyPopulated();
            Assert.True(SaveStore.Write(first.Capture(FullShell())));
            Assert.True(SaveStore.Exists());

            var second = new Game(new SciRandom(2), playerCount: 1);
            Assert.True(SaveStore.Write(second.Capture()));

            Assert.Single(Directory.GetFiles(SaveStore.Directory));

            Assert.Equal(RestoreOutcome.Ok, SaveStore.Read(out var state));
            Assert.Single(state!.Players);
        });
    }

    [Fact]
    public void WritingThenReadingThroughTheStoreRoundTripsTheWholeGame()
    {
        InATempSaveDirectory(() =>
        {
            var before = FullyPopulated();
            var shell = FullShell();

            Assert.True(SaveStore.Write(before.Capture(shell)));
            Assert.Equal(RestoreOutcome.Ok, SaveStore.Read(out var state));

            AssertSameGame(before, Game.Restore(state!, new SciRandom(5)));
            AssertSameShell(shell, state!.Shell);
        });
    }

    [Fact]
    public void SavesDoNotLandNextToTheExecutable()
    {
        var previous = SaveStore.DirectoryOverride;
        try
        {
            SaveStore.DirectoryOverride = null;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            Assert.False(string.IsNullOrEmpty(appData));
            Assert.StartsWith(appData, SaveStore.Directory);
            Assert.NotEqual(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
                            Path.GetDirectoryName(SaveStore.SlotPath));
        }
        finally
        {
            SaveStore.DirectoryOverride = previous;
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>The store's own options, so the tests go through the real serialiser.</summary>
    private static readonly JsonSerializerOptions SaveFormatOptions = SaveFormat.Options;

    private static Game RoundTrip(Game g)
    {
        var json = JsonSerializer.Serialize(new SaveFile { State = g.Capture() }, SaveFormatOptions);
        var read = JsonSerializer.Deserialize<SaveFile>(json, SaveFormatOptions)!;
        return Game.Restore(read.State, new SciRandom(999));
    }

    private static void InATempSaveDirectory(Action body)
    {
        var previous = SaveStore.DirectoryOverride;
        var dir = Path.Combine(Path.GetTempPath(), "jones-save-tests-" + Guid.NewGuid().ToString("N"));

        try
        {
            SaveStore.DirectoryOverride = dir;
            Directory.CreateDirectory(dir);
            body();
        }
        finally
        {
            SaveStore.DirectoryOverride = previous;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static void AssertSameGame(Game a, Game b)
    {
        Assert.Equal(a.CurrentPlayerIndex, b.CurrentPlayerIndex);
        Assert.Equal(a.Calendar.Week, b.Calendar.Week);
        Assert.Equal(a.Clock.HoursRemaining, b.Clock.HoursRemaining);
        Assert.Equal(a.Clock.SubHourTicks, b.Clock.SubHourTicks);
        Assert.Equal(a.Clock.HoursUsed, b.Clock.HoursUsed);
        Assert.Equal(a.IsOver, b.IsOver);

        AssertSameEconomy(a.Economy, b.Economy);

        Assert.Equal(a.Players.Count, b.Players.Count);
        for (var i = 0; i < a.Players.Count; i++) AssertSamePlayer(a.Players[i], b.Players[i]);

        Assert.Equal(a.Winners.Select(w => a.Players.IndexOf(w)),
                     b.Winners.Select(w => b.Players.IndexOf(w)));

        Assert.Equal(a.TurnedDown.Refused.OrderBy(x => x), b.TurnedDown.Refused.OrderBy(x => x));

        AssertSameByReflection(a.JonesWorld, b.JonesWorld, "JonesWorld");

        // The `Game` surface that carries the per-turn and per-visit latches.
        Assert.Equal(a.MuggingPending, b.MuggingPending);
        Assert.Equal(a.LastMugging, b.LastMugging);
        Assert.Equal(a.BrokerChargedThisVisit, b.BrokerChargedThisVisit);
        Assert.Equal(a.LastWorkResult, b.LastWorkResult);
        Assert.Equal(a.LastGarnished, b.LastGarnished);
        Assert.Equal(a.LastJobOutcome, b.LastJobOutcome);
        Assert.Equal(a.LastRejection, b.LastRejection);

        foreach (var name in new[]
                 {
                     "_hapBaseball", "_hapTheatre", "_hapConcert", "_hapLottery",
                     "_hapFood", "_hapDrinks", "_hapMeals",
                 })
        {
            var f = typeof(Game).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.True(Equals(f.GetValue(a), f.GetValue(b)), name);
        }
    }

    private static void AssertSameEconomy(EconomyState a, EconomyState b)
    {
        Assert.Equal(a.Volatility, b.Volatility);
        Assert.Equal(a.CrashSeverity, b.CrashSeverity);
        Assert.Equal(a.Boom, b.Boom);
        Assert.Equal(a.Headline, b.Headline);

        Index(a.Main, b.Main, nameof(a.Main));
        Index(a.Invest, b.Invest, nameof(a.Invest));
        Index(a.Goods, b.Goods, nameof(a.Goods));
        Index(a.Gold, b.Gold, nameof(a.Gold));
        Index(a.Silver, b.Silver, nameof(a.Silver));
        Index(a.Pork, b.Pork, nameof(a.Pork));
        Index(a.BlueChip, b.BlueChip, nameof(a.BlueChip));
        Index(a.Penny, b.Penny, nameof(a.Penny));

        static void Index(EconomicIndex x, EconomicIndex y, string name)
        {
            Assert.True(x.Index == y.Index, $"{name}.Index {x.Index} != {y.Index}");
            Assert.True(x.Reading == y.Reading, $"{name}.Reading {x.Reading} != {y.Reading}");
            Assert.True(x.Risk == y.Risk, $"{name}.Risk");
            Assert.True(x.Headline == y.Headline, $"{name}.Headline");
        }
    }

    private static void AssertSamePlayer(Player a, Player b)
    {
        AssertSameByReflection(a, b, $"Player {a.ActualName}",
                               skip: [typeof(ItemList), typeof(Holdings)]);

        Items(a.Consumables, b.Consumables, "consumables");
        Items(a.Durables, b.Durables, "durables");
        Items(a.Education, b.Education, "education");

        foreach (Instrument i in Enum.GetValues<Instrument>())
            Assert.True(a.Holdings.SharesOf(i) == b.Holdings.SharesOf(i), $"holdings {i}");

        static void Items(ItemList x, ItemList y, string what)
        {
            Assert.True(x.Count == y.Count, $"{what}: {x.Count} entries became {y.Count}");

            for (var i = 0; i < x.Count; i++)
            {
                var (p, q) = (x.Items[i], y.Items[i]);
                Assert.True(p.IndexNum == q.IndexNum, $"{what}[{i}].IndexNum");
                Assert.True(p.Quantity == q.Quantity, $"{what}[{i}].Quantity");
                Assert.True(p.PricePaid == q.PricePaid, $"{what}[{i}].PricePaid");
                Assert.True(p.Attributes == q.Attributes, $"{what}[{i}].Attributes");
                Assert.True(p.UnitsToGraduate == q.UnitsToGraduate, $"{what}[{i}].UnitsToGraduate");
                Assert.True(p.RedemptionPrice == q.RedemptionPrice, $"{what}[{i}].RedemptionPrice");
            }
        }
    }

    /// <summary>
    /// Compares every public readable property. This is what makes the round trip a
    /// REGRESSION test rather than a snapshot of today's fields: add a property to
    /// <c>Player</c> or <c>JonesWorld</c> without adding it to the save format and this
    /// fails, naming it.
    /// </summary>
    private static void AssertSameByReflection(object a, object b, string what,
                                               Type[]? skip = null)
    {
        foreach (var pi in a.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!pi.CanRead || pi.GetIndexParameters().Length > 0) continue;
            if (skip is not null && Array.IndexOf(skip, pi.PropertyType) >= 0) continue;

            var x = pi.GetValue(a);
            var y = pi.GetValue(b);
            Assert.True(Equals(x, y), $"{what}.{pi.Name}: {x} != {y}");
        }
    }

    private static void AssertSameShell(ShellSnapshot a, ShellSnapshot b)
    {
        Assert.Equal(a.RelaxedThisTurn, b.RelaxedThisTurn);
        Assert.Equal(a.WorkedThisTurn, b.WorkedThisTurn);
        Assert.Equal(a.GarnishSpokenThisTurn, b.GarnishSpokenThisTurn);
        Assert.Equal(a.LastWeekendId, b.LastWeekendId);
        Assert.Equal(a.ZMartStock, b.ZMartStock);
    }
}
