using Jones.Core;
using Jones.Core.Model;
using Jones.Core.Sci;
using Xunit;

namespace Jones.Tests;

/// <summary>
/// Moving house, and asking the landlord for more time — `rentOffice.sc` (script 201).
///
/// These were the last two things the Rent Office could not do: the port could take your
/// rent but not rent you a different apartment, which made Le Security Apartments
/// unreachable unless you happened to start there.
/// </summary>
public class RentOfficeTests
{
    private sealed class MidRandom : IRandomSource
    {
        public int Next(int min, int max) => min + (max - min) / 2;
    }

    /// <summary>Always returns the bottom of the range, so every `(Random 1 12)` is 1.</summary>
    private sealed class LowRandom : IRandomSource
    {
        public int Next(int min, int max) => min;
    }

    private static Game Started(IRandomSource? rng = null)
    {
        var g = new Game(rng ?? new MidRandom());
        g.StartTurn();
        return g;
    }

    /// <summary>
    /// Sets the prepaid weeks outright. `Player.Init` hands out three weeks of Low-Cost rent
    /// and the turn start eats one, so `Receive` would add to a stock that is already there.
    /// </summary>
    private static void PrepaidWeeks(Player p, int itemId, int weeks)
    {
        var item = p.Consumables.At(itemId) ?? p.Consumables.Receive(itemId, 0);
        item.Quantity = weeks;
    }

    // ------------------------------------------------------------------
    // rentLowCost / rentSecurity
    // ------------------------------------------------------------------

    [Fact]
    public void RentingTheApartmentYouAlreadyLiveInDoesNothing()
    {
        var g = Started();
        Assert.Equal(0, g.Current.LivesAt);

        // `(if (== (global302 livesAt:) 0) (proc0_18 192 …) (return 0))` — rentOffice.sc:327.
        Assert.Equal(Game.RentOutcome.AlreadyThere, g.RentApartment(0));
    }

    [Fact]
    public void MovingToSecurityBuysFourWeeksAndRewritesCurRentAndLivesAt()
    {
        var g = Started();
        var p = g.Current;

        // No prepaid weeks on the old flat, so there is nothing to forfeit and no question.
        PrepaidWeeks(p, ItemIds.LowCostRent, 0);
        p.Cash = 5000;

        var price = g.RentAsking(2);
        Assert.Equal(Game.RentOutcome.Moved, g.RentApartment(2));

        Assert.Equal(price, p.CurRent);
        Assert.Equal(1, p.LivesAt);                                   // the port's 0/1 form
        Assert.Equal(4, p.Consumables.At(ItemIds.SecurityRent)!.Quantity);
        Assert.Equal(5000 - price, p.Cash);
    }

    [Fact]
    public void TheSecurityApartmentCostsMoreThanTheLowCostOne()
    {
        var g = Started();

        // basePrice 325 against 475 (`rentOffice.sc:323`, `:390`), both through the goods
        // index, so the ordering holds whatever the economy is doing.
        Assert.True(g.RentAsking(2) > g.RentAsking(0));
    }

    [Fact]
    public void PrepaidWeeksOnTheOldFlatForceTheQuestionAndAreThenForfeited()
    {
        var g = Started();
        var p = g.Current;
        p.Cash = 5000;
        PrepaidWeeks(p, ItemIds.LowCostRent, 3);

        // `rentOffice.sc:400-405`: affordable AND weeks left on the current flat.
        Assert.Equal(Game.RentOutcome.NeedsConfirmation, g.RentApartment(2));
        Assert.Equal(3, g.PrepaidWeeksAt(0));           // nothing has happened yet
        Assert.Equal(0, p.LivesAt);
        Assert.Equal(5000, p.Cash);

        Assert.Equal(Game.RentOutcome.Moved, g.RentApartment(2, confirmed: true));

        // `(temp1 quantity: 0)` at `:426` — NON-REFUNDABLE, which is what the question says.
        Assert.Equal(0, p.Consumables.At(ItemIds.LowCostRent)!.Quantity);
        Assert.Equal(1, p.LivesAt);
    }

    [Fact]
    public void WithoutTheCashNothingMovesAndNothingIsForfeited()
    {
        var g = Started();
        var p = g.Current;
        p.Cash = 10;
        PrepaidWeeks(p, ItemIds.LowCostRent, 3);

        // `global416` comes back 0 in `CostDItem::doit`, which routes to `notEnoughCash`.
        Assert.Equal(Game.RentOutcome.CannotAfford, g.RentApartment(2));
        Assert.Equal(0, p.LivesAt);
        Assert.Equal(3, p.Consumables.At(ItemIds.LowCostRent)!.Quantity);
        Assert.Equal(10, p.Cash);
    }

    [Fact]
    public void MovingBackToLowCostWorksTheSameWayRound()
    {
        var g = Started();
        var p = g.Current;
        p.Cash = 5000;
        PrepaidWeeks(p, ItemIds.LowCostRent, 0);

        g.RentApartment(2);
        Assert.Equal(1, p.LivesAt);

        // Four weeks were just bought at Le Security, so going back asks first.
        Assert.Equal(Game.RentOutcome.NeedsConfirmation, g.RentApartment(0));
        Assert.Equal(Game.RentOutcome.Moved, g.RentApartment(0, confirmed: true));

        Assert.Equal(0, p.LivesAt);
        Assert.Equal(g.RentAsking(0), p.CurRent);
        Assert.Equal(0, p.Consumables.At(ItemIds.SecurityRent)!.Quantity);
    }

    // ------------------------------------------------------------------
    // moreTime
    // ------------------------------------------------------------------

    [Fact]
    public void AskingForMoreTimeWithRentAlreadyPaidIsRefusedAsUnnecessary()
    {
        var g = Started();
        PrepaidWeeks(g.Current, ItemIds.LowCostRent, 2);

        // `(if (not ((global302 consumables:) objectAtIndexQuan: temp1)) … else …)` — :237.
        Assert.Equal(Game.MoreTimeOutcome.NotNeeded, g.AskForMoreTime());
    }

    [Fact]
    public void TheFirstExtensionIsAlwaysGranted()
    {
        // `(switch (global302 rentExt:) … (0 1) …)` — a literal 1, no roll at all (`:242`).
        var g = Started(new LowRandom());
        var p = g.Current;
        p.HapStat = 50;
        PrepaidWeeks(p, ItemIds.LowCostRent, 0);

        Assert.Equal(Game.MoreTimeOutcome.Granted, g.AskForMoreTime());
        Assert.Equal(1, p.RentExt);
        Assert.Equal(1, p.TriedExt);
        Assert.Equal(51, p.HapStat);            // `(proc0_13 1)` at `:256`
    }

    [Fact]
    public void ASecondExtensionInTheSameTurnOnlyGetsToldYesAgain()
    {
        var g = Started(new LowRandom());
        var p = g.Current;
        PrepaidWeeks(p, ItemIds.LowCostRent, 0);

        g.AskForMoreTime();
        var hap = p.HapStat;

        // `(switch (global302 triedExt:) (1 … 186 …))` — `:272-275`, no stat change.
        Assert.Equal(Game.MoreTimeOutcome.AskedAgainAfterYes, g.AskForMoreTime());
        Assert.Equal(1, p.TriedExt);
        Assert.Equal(hap, p.HapStat);
    }

    [Fact]
    public void AnEarnedExtensionGetsHarderEachTimeAndABadRollCostsHappiness()
    {
        // rentExt 1 needs `(> (Random 1 12) 3)`; LowRandom gives 1, so it fails.
        var g = Started(new LowRandom());
        var p = g.Current;
        p.RentExt = 1;
        p.HapStat = 50;
        PrepaidWeeks(p, ItemIds.LowCostRent, 0);

        Assert.Equal(Game.MoreTimeOutcome.Refused, g.AskForMoreTime());
        Assert.Equal(1, p.RentExt);             // not incremented on a refusal
        Assert.Equal(2, p.TriedExt);
        Assert.Equal(49, p.HapStat);            // `(proc0_13 -1)` at `:263`
    }

    [Fact]
    public void PressingTheButtonAfterANoWalksUpTheInsultsAndThenStops()
    {
        var g = Started(new LowRandom());
        var p = g.Current;
        p.RentExt = 1;
        PrepaidWeeks(p, ItemIds.LowCostRent, 0);

        g.AskForMoreTime();                     // refused, triedExt 2
        Assert.Equal(2, p.TriedExt);

        // Cases 2, 3, 4 and 5 each bump it; there is no case 6 (`:271-304`).
        foreach (var expected in new[] { 3, 4, 5, 6 })
        {
            Assert.Equal(Game.MoreTimeOutcome.AskedAgainAfterNo, g.AskForMoreTime());
            Assert.Equal(expected, p.TriedExt);
        }

        Assert.Equal(Game.MoreTimeOutcome.AskedAgainAfterNo, g.AskForMoreTime());
        Assert.Equal(6, p.TriedExt);            // stuck: the switch has run out of cases
    }

    [Fact]
    public void AGrantedExtensionIsWhatOpensTheRentOfficeOffSchedule()
    {
        var g = Started(new LowRandom());
        var p = g.Current;
        PrepaidWeeks(p, ItemIds.LowCostRent, 0);

        // Closed in weeks 1..3 to a player who neither works there nor has an extension.
        Assert.False(Board.IsOpen(LocationId.RentOffice, 1, p));

        g.AskForMoreTime();
        p.EndTurn(1);                           // `leaveOpen: (== triedExt 1)`, room1.sc:806

        Assert.True(p.LeaveOpen);
        Assert.True(Board.IsOpen(LocationId.RentOffice, 1, p));
    }
}
