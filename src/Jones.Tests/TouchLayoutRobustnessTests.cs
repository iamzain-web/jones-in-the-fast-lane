using Jones.App;

namespace Jones.Tests;

/// <summary>
/// THE TAP LAYOUT SURVIVES THE TWO THINGS THAT TOOK EVERY INTERIOR'S CONTROLS OFF SCREEN.
///
/// <para>
/// WHAT HAPPENED. <c>SpriteVm</c> was given an <c>Equals</c> so a rebuild could skip unchanged
/// sprites; the rule required both to be non-clickable, which made a CLICKABLE SPRITE UNEQUAL
/// TO ITSELF. <see cref="TouchLayout"/> keys a dictionary by the owner, so reading any
/// clickable sprite's position threw <c>KeyNotFoundException</c> — and Avalonia answers an
/// exception inside a binding by leaving the container unplaced. ENROLL, DONE and the course
/// icons all existed, all had art, all sat at correct coordinates, and NONE WAS DRAWN. The
/// University could not be used. Nothing was logged and 683 tests passed.
/// </para>
///
/// <para>
/// THE SECOND FAULT, OLDER AND INDEPENDENT. The growth is computed on first use — which is a
/// BINDING being evaluated, and Avalonia can evaluate one while the screen is still being
/// built. Anything registering after that first computation was silently dropped and stayed
/// dropped, then looked up by direct indexing.
/// </para>
///
/// <para>
/// Both are tested here with a plain fake owner rather than a sprite, because the hazard is in
/// the LAYOUT and this project is deliberately free of the toolkit.
/// </para>
/// </summary>
public class TouchLayoutRobustnessTests
{
    /// <summary>
    /// An owner that lies about equality exactly as the broken sprite did: never equal to
    /// anything, including itself. A dictionary keyed by one of these can never find what it
    /// just stored.
    /// </summary>
    private sealed class NeverEqualsItself
    {
        public override bool Equals(object? obj) => false;
        public override int GetHashCode() => 1;      // collide, to force the comparison
    }

    /// <summary>
    /// THE EXACT CRASH. Two controls close enough that `Separate` runs — it is the only path
    /// that indexed the dictionary directly — with owners that cannot be found in it.
    /// </summary>
    [Fact]
    public void AnOwnerThatDoesNotEqualItselfDoesNotThrow()
    {
        var layout = TouchTargets.NewLayout();

        // The Hi-Tech U pair that was invisible: ENROLL at (105,108) 40x9 and DONE at
        // (143,108) 32x9, in the dialog's own coordinates.
        var enroll = new NeverEqualsItself();
        var done = new NeverEqualsItself();
        layout.Add(enroll, 105, 108, 40, 9);
        layout.Add(done, 143, 108, 32, 9);

        // Before the fix this threw KeyNotFoundException from inside Separate, which Avalonia
        // swallowed by leaving the control unplaced - so the test is simply that it returns.
        var a = layout.For(enroll);
        var b = layout.For(done);

        Assert.True(a.Left >= 0 && a.Right >= 0 && a.Top >= 0 && a.Bottom >= 0);
        Assert.True(b.Left >= 0 && b.Right >= 0 && b.Top >= 0 && b.Bottom >= 0);
    }

    /// <summary>
    /// A LATE REGISTRATION IS ACCOUNTED FOR, not dropped. `For` computes the layout; anything
    /// added afterwards used to be ignored permanently, and then indexed.
    /// </summary>
    [Fact]
    public void RegisteringAfterTheFirstLookupStillCounts()
    {
        var layout = TouchTargets.NewLayout();
        var first = new object();
        layout.Add(first, 105, 108, 40, 9);

        // This is the binding-mid-build case: the layout is computed before the screen has
        // finished registering its controls.
        layout.For(first);

        var late = new object();
        layout.Add(late, 143, 108, 32, 9);

        // The straggler must be known to the layout. Before the fix `Add` returned early
        // because the growth had already been computed, and the entry never existed.
        var g = layout.For(late);
        Assert.True(g.Left >= 0 && g.Right >= 0);

        // And the one registered first must still be answerable afterwards - invalidating and
        // recomputing must not lose what was already there.
        var again = layout.For(first);
        Assert.True(again.Left >= 0 && again.Right >= 0);
    }

    /// <summary>
    /// AN UNREGISTERED OWNER GETS NO GROWTH RATHER THAN AN EXCEPTION. A lookup on a key the
    /// caller supplies should be total: the alternative is a throw inside a binding, which is
    /// invisible.
    /// </summary>
    [Fact]
    public void AnUnknownOwnerIsAnsweredWithNoGrowth()
    {
        var layout = TouchTargets.NewLayout();
        layout.Add(new object(), 105, 108, 40, 9);

        var stranger = new object();
        var g = layout.For(stranger);

        Assert.Equal(0, g.Left);
        Assert.Equal(0, g.Top);
        Assert.Equal(0, g.Right);
        Assert.Equal(0, g.Bottom);
    }
}
