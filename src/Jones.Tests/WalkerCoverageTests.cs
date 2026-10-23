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
    /// NO WALKER VIEW ANYWHERE IN THE GAME FALLS THROUGH TO SIERRA. Sixteen views, four
    /// characters: three have their own art and the other thirteen are answered from the
    /// rebuilt character. This is the assertion that "the other man" cannot come back.
    /// </summary>
    [Fact]
    public void NoWalkerViewFallsThroughToSierra()
    {
        var uncovered = new List<int>();

        foreach (var view in AllWalkerViews)
        {
            if (CelsFor("png4x-cast0", view).Length > 0) continue;   // its own art

            var standIn = Jones.Core.Model.CastWalkers.StandIn($"view_{view}_l0_c0.png");
            if (standIn is null) { uncovered.Add(view); continue; }

            // The stand-in has to be a view the set actually holds, or it is a promise
            // rather than a cel.
            var target = int.Parse(standIn.Split('_')[1]);
            if (CelsFor("png4x-cast0", target).Length == 0) uncovered.Add(view);
        }

        Assert.True(uncovered.Count == 0,
            "these walker views still fall through to Sierra: " + string.Join(", ", uncovered));
    }

    /// <summary>
    /// THE CLOTHING STATE SURVIVES THE SUBSTITUTION. A player in a business suit gets the
    /// rebuilt character's business suit, not whatever was nearest - the person changes and
    /// the state does not. The exception is "nothing left to wear", which has no art of its
    /// own and borrows the casual outfit.
    /// </summary>
    [Theory]
    [InlineData(284, 280)] [InlineData(285, 281)] [InlineData(286, 282)] [InlineData(287, 282)]
    [InlineData(290, 280)] [InlineData(291, 281)] [InlineData(292, 282)] [InlineData(293, 282)]
    [InlineData(294, 280)] [InlineData(295, 281)] [InlineData(296, 282)] [InlineData(297, 282)]
    [InlineData(283, 282)]
    public void TheStandInKeepsTheClothingState(int asked, int expected)
    {
        Assert.Equal($"view_{expected}_l0_c1_s3.png",
                     Jones.Core.Model.CastWalkers.StandIn($"view_{asked}_l0_c1_s3.png"));
    }

    /// <summary>A view that has its own art is never redirected, and neither is anything else.</summary>
    [Fact]
    public void ArtTheSetOwnsIsLeftAlone()
    {
        foreach (var view in new[] { 280, 281, 282 })
            Assert.Null(Jones.Core.Model.CastWalkers.StandIn($"view_{view}_l0_c0.png"));

        Assert.Null(Jones.Core.Model.CastWalkers.StandIn("view_501_l1_c0.png"));
        Assert.Null(Jones.Core.Model.CastWalkers.StandIn("board.png"));
    }

    /// <summary>
    /// THE INVENTORY. Three of the four characters still have no replacement art of their
    /// own - there is one man photographed - and they are now DRAWN as him rather than as
    /// Sierra's people. The number belongs somewhere readable either way: it is what stops
    /// being borrowed the day those three are shot.
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
