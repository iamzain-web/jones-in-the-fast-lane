using System;

namespace Jones.Core.Model;

/// <summary>
/// KEEPS SIERRA'S WALKER OFF THE BOARD FOR A CHARACTER WE HAVE REBUILT.
///
/// <para>
/// THE GAME DRAWS A PLAYER WITH ONE OF SIXTEEN VIEWS. Four characters, four views each -
/// <c>baseView</c> 280, 284, 290 and 294 - and within a character the view is chosen by
/// what they are WEARING: <c>baseView + (Wearing - 34)</c> for the business suit, the
/// dress clothes and the casual outfit, and <c>baseView + 3</c> when
/// <c>WeeksOfClothing()</c> has reached zero and they have nothing left.
/// </para>
///
/// <para>
/// THE REPLACEMENT SET COVERS 280, 281 AND 282. That is one character, dressed three ways,
/// and it is everything Zain has photographed so far. The fourth view of that character -
/// 283, the one with no clothes left - was never made, so the moment a player's wardrobe
/// ran out mid-game the figure on the board turned into SIERRA'S. Zain, watching it
/// happen: "It swops between Sierras art and mine for the man", and then the part that
/// matters: "The other man shouldn't be there at all."
/// </para>
///
/// <para>
/// He is right, and the reason it happened is worth stating plainly: the resolver's
/// fall-through is per-CEL, and it is correct for nine hundred cels the set deliberately
/// does not replace. It is wrong for a character the set HAS replaced, because there the
/// fall-through does not produce 1990 pixels of a background - it produces a different
/// person.
/// </para>
///
/// <para>
/// SO A GAP INSIDE A REBUILT CHARACTER IS FILLED FROM THAT CHARACTER, not from Sierra. The
/// stand-in is the casual outfit, which is what a player wears by default and what the
/// set's other cels were shot in.
/// </para>
///
/// <para>
/// THIS IS A STAND-IN AND NOT A FIX, and the difference should not be blurred. The game
/// means "this player has run out of clothes" and will be showing them fully dressed, so
/// the state is no longer legible on the board. The real answer is the shorts-and-vest
/// sheet for 283; this is what the game does until that exists. Characters two, three and
/// four are not covered at all - there is one man - and nothing here invents them.
/// </para>
/// </summary>
/// <para>
/// IT LIVES IN Jones.Core AND NOT IN Jones.App, for the reason <see cref="CastNameTable"/>
/// was moved here: WHICH cel stands in for which is pure string arithmetic over a view
/// number, while LOADING it needs the toolkit. Left beside the loader it was unreachable
/// from Jones.Tests, which is net8.0 and deliberately free of Avalonia - and a rule nobody
/// can test is how view 283 stayed Sierra's in the first place.
/// </para>
public static class CastWalkers
{
    /// <summary>
    /// The cel to use instead of <paramref name="file"/> when the art set does not have it
    /// and the fall-through would be Sierra's walker, or null when there is nothing better
    /// to offer.
    ///
    /// <para>
    /// Only ever maps WITHIN one character, and only for a character the set covers. A
    /// miss on view 284 returns null, because the set has no second man and substituting
    /// the first would be inventing a cast rather than filling a gap.
    /// </para>
    /// </summary>
    public static string? StandIn(string file)
    {
        // view_<n>_l<loop>_c<cel>[_s<sub>].png - only the view number changes.
        if (!file.StartsWith("view_", StringComparison.Ordinal)) return null;

        var rest = file.AsSpan(5);
        var underscore = rest.IndexOf('_');
        if (underscore <= 0) return null;

        if (!int.TryParse(rest[..underscore], out var view)) return null;

        // The undressed view of each character is its base + 3. Everything else either has
        // its own art or has none at all.
        var baseView = view switch
        {
            283 => 280,
            _ => 0,
        };

        return baseView == 0 ? null : $"view_{baseView + 2}{rest[underscore..]}";
    }
}
