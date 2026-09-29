using Jones.Core.Sci;
using Xunit;

namespace Jones.Tests;

/// <summary>
/// `Cycle::nextCel` (`Motion.sc:26-44`) and `Fwd::doit` (`:78-84`).
///
/// <para>
/// WHAT THESE ARE GUARDING. The port modelled `ticksToDo` nowhere: every actor that declares
/// one had its cels driven by whichever timer happened to be moving it. The walker's four cels
/// ran off the marble's 25ms step and played ten walk cycles a second; Willy's eight ran off
/// his mover's 100ms cadence, 50% fast. The numbers below are the scripts' own, so a later
/// change that re-ties a cel rate to a game timer fails here.
/// </para>
/// </summary>
public class CelCyclerTests
{
    // ------------------------------------------------------------------
    // The rule itself
    // ------------------------------------------------------------------

    /// <summary>
    /// `GetTime` is 60ths of a second, and `(u&lt; (+ ticksToDo lastTime) (GetTime))` is
    /// STRICT — so the cel is still current at exactly `ticksToDo` ticks and changes on the
    /// next one. A cel therefore holds `ticksToDo + 1` ticks.
    /// </summary>
    [Theory]
    [InlineData(10, 11)]   // theWalker, room1.sc:1060
    [InlineData(8, 9)]     // willy, muggedByMarket.sc:154 — and ambulance, startTrn.sc:1107
    [InlineData(1, 2)]     // marble, room1.sc:1083
    [InlineData(6, 7)]     // the floppy Talker, jones-dos-1.000.060/src/WButton.sc:273
    public void ACelHoldsOneMoreTickThanTicksToDo(int ticksToDo, int ticks)
    {
        Assert.Equal(ticks * (1000.0 / 60.0), CelCycler.CelMs(ticksToDo), 9);
    }

    /// <summary>theWalker's four cels at `ticksToDo 10` are a 0.73-second walk cycle.</summary>
    [Fact]
    public void TheWalkCycleIsThreeQuartersOfASecond()
    {
        Assert.Equal(183.33, CelCycler.CelMs(10), 2);
        Assert.Equal(733.33, CelCycler.CelMs(10) * 4, 2);
    }

    /// <summary>
    /// Willy's eight cels at `ticksToDo 8` are 1.2 seconds. The port drew them in 800ms,
    /// because it used his mover's `moveSpeed 3` — four 25ms cycles a step — as the cel rate.
    /// The two numbers are different mechanisms and this is the gap between them.
    /// </summary>
    [Fact]
    public void WillysWalkCycleIsOnePointTwoSecondsAndNotTheMoversEightHundredMilliseconds()
    {
        Assert.Equal(150.0, CelCycler.CelMs(8), 1);
        Assert.Equal(1200.0, CelCycler.CelMs(8) * 8, 1);

        const double moverCadenceMs = 25 * (3 + 1);   // CycleMs * (moveSpeed + 1)
        Assert.Equal(800.0, moverCadenceMs * 8, 1);
    }

    /// <summary>
    /// `ticksToDo 0` is not "as fast as possible": it selects the other branch of
    /// `nextCel`, which counts game cycles against `cycleSpeed`. Constructing one says so
    /// rather than animating something the original animates another way.
    /// </summary>
    [Fact]
    public void ZeroTicksToDoIsTheCycleSpeedBranchAndIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CelCycler(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CelCycler(-1));
    }

    // ------------------------------------------------------------------
    // Fwd
    // ------------------------------------------------------------------

    /// <summary>
    /// A whole-millisecond time just PAST the end of <paramref name="cels"/> cels. The
    /// walker's cel is 11/60s — 183.33ms — so a test cannot say `3 * celMs + 1` and expect to
    /// be on the far side of the third boundary once it is truncated to a millisecond.
    /// </summary>
    private static long JustAfter(int ticksToDo, int cels) =>
        (long)Math.Ceiling(cels * CelCycler.CelMs(ticksToDo)) + 1;

    [Fact]
    public void TheCelHoldsUntilTheClockPassesItAndThenChangesExactlyOnce()
    {
        var c = new CelCycler(10);
        c.Restart(0);

        Assert.Equal(0, c.Advance(0, 4));
        Assert.Equal(0, c.Advance((long)CelCycler.CelMs(10) - 1, 4));
        Assert.Equal(1, c.Advance(JustAfter(10, 1), 4));
        Assert.Equal(1, c.Advance(JustAfter(10, 1) + 1, 4));
        Assert.Equal(2, c.Advance(JustAfter(10, 2), 4));
    }

    /// <summary>`Fwd::cycleDone` is `(client cel: 0)` — it wraps, it does not stop.</summary>
    [Fact]
    public void FwdWrapsRoundTheLoop()
    {
        var c = new CelCycler(10);
        c.Restart(0);

        Assert.Equal(3, c.Advance(JustAfter(10, 3), 4));
        Assert.Equal(0, c.Advance(JustAfter(10, 4), 4));
        Assert.Equal(1, c.Advance(JustAfter(10, 5), 4));
    }

    /// <summary>
    /// The rate must not depend on how often the port asks. Polling every millisecond and
    /// polling twice a cel have to land on the same cel at the same moment, or the animation
    /// speed becomes a function of the frame rate — which is the original bug.
    /// </summary>
    [Fact]
    public void ThePollingRateDoesNotChangeTheAnimationRate()
    {
        var fine = new CelCycler(8);
        var coarse = new CelCycler(8);
        fine.Restart(0);
        coarse.Restart(0);

        for (long t = 0; t <= 2000; t++)
        {
            fine.Advance(t, 8);
            if (t % 70 == 0) coarse.Advance(t, 8);
        }

        Assert.Equal(fine.Advance(2000, 8), coarse.Advance(2000, 8));

        // 2000ms at 150ms a cel is 13 whole cels; 13 % 8 = 5.
        Assert.Equal(5, fine.Cel);
    }

    /// <summary>
    /// THE WALKER MUST NOT HAVE MOVED. `MainViewModel.WalkerFrame` used to derive its cel
    /// straight from the wall clock; it now goes through this class instead, and polled at the
    /// port's 16ms redraw it has to land on the same cel at every one of those frames across a
    /// thirty-second journey — or the one bug already fixed has been reintroduced by the
    /// tidying.
    ///
    /// <para>
    /// The expectation is written in the interpreter's OWN arithmetic — whole 60ths, whole
    /// cels of 11 of them — rather than as `t / 183.333ms`, because that is what `GetTime` is
    /// and because the millisecond form is not exactly representable. Writing the check the
    /// second way is in fact how the millisecond bug in this class was found: it put the 24th
    /// boundary a fraction of a nanosecond out of reach and the walker skipped a cel.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWalkerGetsExactlyTheCelTheOldDerivationGaveIt()
    {
        var c = new CelCycler(10);
        c.Restart(0);

        var changes = 0;
        var last = 0;

        for (long t = 0; t <= 30_000; t += 16)
        {
            var expected = (int)(t * 60 / 1000 / 11 % 4);
            var actual = c.Advance(t, 4);

            Assert.Equal(expected, actual);

            if (actual != last) { changes++; last = actual; }
        }

        // 30s at 11/60s a cel. The rate is the whole point, so it is asserted outright.
        Assert.Equal(163, changes);
    }

    /// <summary>A stall does not spin through thousands of cels, and lands somewhere legal.</summary>
    [Fact]
    public void AVeryLongStallResynchronisesInsteadOfCatchingUp()
    {
        var c = new CelCycler(8);
        c.Restart(0);

        var cel = c.Advance(60L * 60 * 1000, 8);

        Assert.InRange(cel, 0, 7);
        Assert.Equal(cel, c.Advance(60L * 60 * 1000, 8));
    }

    // ------------------------------------------------------------------
    // What the scripts do to a running cycler
    // ------------------------------------------------------------------

    /// <summary>
    /// `Cycle::init` (`Motion.sc:19-24`) sets `client`, `ticksToDo`, `cycleCnt` and
    /// `lastTime`. It does NOT touch the cel — so `muggedByMarket` state 4's
    /// `(willy setLoop: 5 setCycle: Fwd)`, which has no `cel: 0`, really does carry the cel
    /// over from loop 3.
    /// </summary>
    [Fact]
    public void RestartResetsTheClockButNotTheCel()
    {
        var c = new CelCycler(8);
        c.Restart(0);

        Assert.Equal(3, c.Advance(JustAfter(8, 3), 8));

        c.Restart(1000);
        Assert.Equal(3, c.Advance(1000, 8));
        Assert.Equal(3, c.Advance(1000 + (long)CelCycler.CelMs(8) - 1, 8));
        Assert.Equal(4, c.Advance(1000 + (long)CelCycler.CelMs(8) + 1, 8));
    }

    /// <summary>
    /// `Actor::setCel` (`Actor.sc:143-163`) sets signal $1000, and `Cycle::nextCel` then
    /// returns the same cel for ever — the two poses Willy freezes in for his three seconds.
    /// `setCycle:` clears the flag again (`Actor.sc:224`).
    /// </summary>
    [Fact]
    public void FixCelFreezesTheCelAndSetCycleReleasesIt()
    {
        var c = new CelCycler(8);
        c.Restart(0);
        c.FixCel(0, 1);

        Assert.False(c.Cycling);
        Assert.Equal(0, c.Advance(10_000, 1));

        c.Restart(10_000);
        Assert.True(c.Cycling);
        Assert.Equal(1, c.Advance(10_000 + (long)CelCycler.CelMs(8) + 1, 8));
    }

    /// <summary>
    /// `(willy setLoop: 7 setCel: 1)` asks for cel 1 of a loop that holds one cel. The
    /// original clamps it in `setCel` — `(if (>= newCel (self lastCel:)) (self lastCel:))` —
    /// rather than drawing a hole.
    /// </summary>
    [Fact]
    public void FixCelClampsToTheLoopsLastCel()
    {
        var c = new CelCycler(8);
        c.Restart(0);
        c.FixCel(1, 1);

        Assert.Equal(0, c.Cel);
    }

    /// <summary>
    /// `muggedByBank` states 1 and 2 call `setLoop:` with no `setCycle:`, so the cycler keeps
    /// running while the loop under it changes length. `Fwd::doit` compares against the
    /// CURRENT loop's `lastCel` and drops to 0 when the cel no longer exists — which happens
    /// on the next doit whether or not the clock has fired.
    /// </summary>
    [Fact]
    public void ALoopChangeToAShorterLoopSnapsToCelZero()
    {
        var c = new CelCycler(8);
        c.Restart(0);

        Assert.Equal(7, c.Advance(JustAfter(8, 7), 8));

        // setLoop: 4 — six cels, so cel 7 is gone.
        Assert.Equal(0, c.Advance(JustAfter(8, 7), 6));
    }

    // ------------------------------------------------------------------
    // Phase — the port's own in-between art, not the original's
    // ------------------------------------------------------------------

    [Fact]
    public void PhaseRunsFromZeroToJustUnderOneAcrossACel()
    {
        var c = new CelCycler(10);
        c.Restart(0);

        Assert.Equal(0, c.Phase(0), 6);
        Assert.Equal(0.5, c.Phase((long)(CelCycler.CelMs(10) / 2)), 1);

        c.Advance(JustAfter(10, 1), 4);
        Assert.InRange(c.Phase(JustAfter(10, 1)), 0, 0.2);
    }

    [Fact]
    public void AFrozenCelHasNoPhase()
    {
        var c = new CelCycler(8);
        c.Restart(0);
        c.FixCel(0, 1);

        Assert.Equal(0, c.Phase(10_000), 6);
    }
}
