using Jones.Core;
using Jones.Core.Model;
using Jones.Core.Sci;
using Xunit;

namespace Jones.Tests;

/// <summary>
/// The hand-over between turns, which is what the original does when the 60 Hours run out:
///
///   `timeKeep::doit` (room1.sc:1477) counts the Hours and, at exactly 60, either calls
///   `proc1_9` itself (room1.sc:1513, while the marble is still travelling) or leaves it to
///   `Place::endCue` (room1.sc:273) to call it as the player steps out of a building.
///   `proc1_9` (room1.sc:50-64) calls `players::doit` — which runs the outgoing player's
///   `endTurn:` (room1.sc:976) and rotates `global302` on to the next player (room1.sc:989)
///   — and then walks the marble to the INCOMING player's front door. On arrival
///   `room1::doit` (room1.sc:1369-1373) cues `marble::cue:` (room1.sc:1092), which zeroes
///   the clock, bumps the week if play has come back round to the first player
///   (room1.sc:1107-1122) and calls `startTurn:` (room1.sc:1173).
///
/// No button ends a turn in Jones. These tests pin the order down.
/// </summary>
public class EndOfWeekTests
{
    private sealed class MidRandom : IRandomSource
    {
        public int Next(int min, int max) => min + (max - min) / 2;
    }

    /// <summary>Burns the whole 60 Hours the way a turn of walking about does.</summary>
    private static void BurnTheWeek(Game g) => g.Clock.Spend(GameClock.HoursPerTurn);

    [Fact]
    public void WithTheHoursGoneThePlayerCanDoNothing_WhichIsWhyTheTurnMustEndItself()
    {
        var g = new Game(new MidRandom());
        g.StartTurn();
        BurnTheWeek(g);

        // This is the stuck state: `global323 == 60`, so every destination is refused.
        Assert.True(g.Clock.TurnOver);
        Assert.False(g.TravelTo(LocationId.ZMart));
        Assert.Equal(LocationId.LowCostHousing, g.Current.Location);
    }

    [Fact]
    public void EndingTheTurnStartsTheNextWeekWithAFullClockAndTheBoardUsableAgain()
    {
        var g = new Game(new MidRandom());
        g.StartTurn();
        g.TravelTo(LocationId.ZMart);
        BurnTheWeek(g);

        g.EndTurn();

        // Keep the fridge stocked, and give them a fridge to keep it in. Without this the
        // turn-start chain finds no meal and charges the 20-hour starvation penalty
        // (`startTrn.sc:432` → `:1041`), and food with nowhere cold to go spoils into the
        // doctor's 10 (`:464` → `:1035`). The clock reset does NOT undo either — it runs
        // first. This test asserted a flat 60 hours and was measuring their absence.
        g.Current.Consumables.Receive(ItemIds.FreshFood, 4);
        g.Current.Durables.Receive(ItemIds.Refrigerator, 1);

        g.StartTurn();

        // `marble::cue:` zeroes global323 and puts the player back at their own door.
        Assert.Equal(2, g.Calendar.Week);
        Assert.Equal(GameClock.HoursPerTurn, g.Clock.HoursRemaining);
        Assert.False(g.Clock.TurnOver);
        Assert.Equal(LocationId.LowCostHousing, g.Current.Location);

        // And the player can act again.
        Assert.True(g.TravelTo(LocationId.ZMart));
    }

    [Fact]
    public void ASoloPlayerAdvancesTheWeekEveryTurn()
    {
        var g = new Game(new MidRandom());

        for (var week = 1; week <= 5; week++)
        {
            Assert.Equal(week, g.Calendar.Week);
            g.StartTurn();
            BurnTheWeek(g);
            g.EndTurn();
            Assert.Equal(0, g.CurrentPlayerIndex);
        }

        Assert.Equal(6, g.Calendar.Week);
    }

    [Fact]
    public void FourPlayersAllTakeAGoBeforeTheWeekTurnsOver()
    {
        var g = new Game(new MidRandom(), playerCount: 4);

        for (var seat = 0; seat < 4; seat++)
        {
            Assert.Equal(seat, g.CurrentPlayerIndex);
            Assert.Equal(1, g.Calendar.Week); // still week 1 all the way round
            g.StartTurn();
            BurnTheWeek(g);
            g.EndTurn();
        }

        Assert.Equal(0, g.CurrentPlayerIndex);
        Assert.Equal(2, g.Calendar.Week);
    }

    [Fact]
    public void TheOutgoingPlayersEndTurnRunsBeforePlayPassesOn()
    {
        var g = new Game(new MidRandom(), playerCount: 2);
        var first = g.Players[0];
        first.Dependibility = 40;

        g.StartTurn();
        BurnTheWeek(g);
        g.EndTurn();

        // `players::doit` calls `(global302 endTurn:)` on the player who just finished,
        // and only then rotates. Dependability decays by 3 a turn (room1.sc:753).
        Assert.Equal(37, first.Dependibility);
        Assert.Equal(g.Players[1], g.Current);
    }

    [Fact]
    public void TheEndingTurnIsChargedAgainstTheWeekItWasPlayedIn()
    {
        // Rent falls due on `(not (mod global372 4))` inside `Player::endTurn`
        // (room1.sc:789), which runs BEFORE `marble::cue:` increments the week — so the
        // fourth week's rent is charged by the turn played in week 4, not week 5.
        var g = new Game(new MidRandom());

        // FOUR prepaid weeks, one spent per turn, so the entry hits zero exactly as week 4
        // ends. It used to start at zero, which — now that `Player::endTurn`'s non-rent-week
        // branch accrues as well (`room1.sc:808-820`) — bills the player in week 1 and says
        // nothing about where the week boundary falls, which is what this test is for.
        g.Current.Consumables.At(ItemIds.LowCostRent)!.Quantity = 4;

        for (var week = 1; week <= 3; week++)
        {
            g.StartTurn();
            BurnTheWeek(g);
            g.EndTurn();
            Assert.Equal(0, g.Current.RentOwed);
        }

        Assert.Equal(4, g.Calendar.Week);
        g.StartTurn();
        BurnTheWeek(g);
        g.EndTurn();

        Assert.Equal(325, g.Current.RentOwed);
        Assert.Equal(5, g.Calendar.Week);
    }

    [Fact]
    public void TheNextPlayersHomeIsWhereTheMarbleWalks()
    {
        // proc1_9 tests `(global302 livesAt:)` AFTER `players::doit` has rotated, so the
        // marble walks to the incoming player's door. A player who has moved up to Le
        // Security Apartments is fetched there, not to Low-Cost Housing.
        var g = new Game(new MidRandom(), playerCount: 2);
        g.Players[1].LivesAt = 1;

        g.StartTurn();
        BurnTheWeek(g);
        g.EndTurn();

        Assert.Equal(1, g.Current.LivesAt);

        g.StartTurn();
        Assert.Equal(LocationId.SecurityApartments, g.Current.Location);
    }
}
