using System;
using System.IO;
using System.Linq;

namespace Jones.Tests;

/// <summary>
/// WHICH PLAYERS ARE OURS AND WHICH ARE STILL SIERRA'S, stated as a number rather than
/// found out by playing.
///
/// <para>
/// THE GAME DRAWS A PLAYER WITH ONE OF SIXTEEN VIEWS: four characters at base 280, 284,
/// 290 and 294, and within each character one view per clothing state - the business suit,
/// the dress clothes, the casual outfit, and <c>base + 3</c> for having nothing left to
/// wear. The replacement set covers 280, 281 and 282: ONE character, dressed three ways.
/// </para>
///
/// <para>
/// Nothing in the game says so. The resolver falls through to the ported 1990 art per cel,
/// silently and correctly, for the nine hundred cels the set never meant to replace - and
/// identically, for these, where the fall-through is not an older picture of the same thing
/// but a DIFFERENT PERSON. Zain found it the only way it could be found, by playing until
/// his wardrobe ran out: "It swops between Sierras art and mine for the man."
/// </para>
///
/// <para>
/// So the coverage is counted here. The first test is the invariant - a character we have
/// rebuilt never turns into Sierra's - and the second is the inventory, which fails the day
/// somebody deletes a sheet and otherwise just records what is left to shoot.
/// </para>
/// </summary>
public class WalkerCoverageTests
{
    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "assets", "png4x"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("could not find assets/png4x above the test binary");
    }

    private static string[] CelsFor(string set, int view)
    {
        var dir = Path.Combine(Root(), "assets", set);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, $"view_{view}_*.png").Select(Path.GetFileName).ToArray()!;
    }

    /// <summary>Every walker view the view model can produce. See MainViewModel's baseView.</summary>
    private static readonly int[] AllWalkerViews =
        [.. new[] { 280, 284, 290, 294 }.SelectMany(b => new[] { b, b + 1, b + 2, b + 3 })];

    /// <summary>The character the set has rebuilt, and its four views.</summary>
    private const int RebuiltBase = 280;

    /// <summary>
    /// A CHARACTER WE HAVE REBUILT IS OURS IN EVERY STATE. Three of his four views have
    /// their own art; the fourth is filled from his casual outfit rather than from Sierra -
    /// see <c>CastWalkers</c>. The point of the test is that NONE of his four falls through.
    /// </summary>
    [Fact]
    public void TheRebuiltCharacterIsNeverSierras()
    {
        var uncovered = new List<int>();

        foreach (var view in new[] { RebuiltBase, RebuiltBase + 1, RebuiltBase + 2, RebuiltBase + 3 })
        {
            var own = CelsFor("png4x-cast0", view).Length;
            if (own > 0) continue;

            // No art of its own: the stand-in must cover it, or the board shows Sierra.
            var standIn = Jones.Core.Model.CastWalkers.StandIn($"view_{view}_l0_c0.png");
            if (standIn is null || CelsFor("png4x-cast0", RebuiltBase + 2).Length == 0)
                uncovered.Add(view);
        }

        Assert.True(uncovered.Count == 0,
            "these views of the rebuilt character fall through to Sierra's walker: "
            + string.Join(", ", uncovered));
    }

    /// <summary>
    /// THE STAND-IN STAYS INSIDE ITS OWN CHARACTER. A miss on another character's view must
    /// return null - there is one man, and dressing three more in his clothes would be
    /// inventing a cast rather than filling a gap.
    /// </summary>
    [Fact]
    public void TheStandInNeverBorrowsFromAnotherCharacter()
    {
        foreach (var view in AllWalkerViews.Where(v => v is < RebuiltBase or > RebuiltBase + 3))
            Assert.Null(Jones.Core.Model.CastWalkers.StandIn($"view_{view}_l0_c0.png"));

        // And it leaves everything that is not a walker alone.
        Assert.Null(Jones.Core.Model.CastWalkers.StandIn("view_501_l1_c0.png"));
        Assert.Null(Jones.Core.Model.CastWalkers.StandIn("board.png"));
    }

    /// <summary>
    /// THE INVENTORY. Three of the four characters have no replacement art at all, which is
    /// not a bug - there is one man photographed - but it IS the reason Sierra's people are
    /// still in this game, and it should be a number somebody can read rather than a
    /// discovery somebody makes mid-game.
    /// </summary>
    [Fact]
    public void TheRemainingCharactersAreAccountedFor()
    {
        var covered = AllWalkerViews.Where(v => CelsFor("png4x-cast0", v).Length > 0).ToArray();
        var missing = AllWalkerViews.Except(covered).ToArray();

        // Sierra ships all sixteen; if one of hers goes missing the game has a hole rather
        // than a fall-through, and that is worth failing over.
        foreach (var view in AllWalkerViews)
            Assert.True(CelsFor("png4x", view).Length > 0,
                $"view {view} is missing from the PORTED set - that is a hole, not a fall-through.");

        Assert.Equal([280, 281, 282], covered);
        Assert.Equal(13, missing.Length);
    }
}
