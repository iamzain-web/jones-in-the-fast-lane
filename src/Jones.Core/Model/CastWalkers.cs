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
/// SO EVERY WALKER VIEW IS ANSWERED FROM THE REBUILT CHARACTER, and Sierra's people are
/// off the board entirely. Zain, having first seen one of them appear mid-game: "replace
/// ALL instances of the other character with him."
/// </para>
///
/// <para>
/// The mapping keeps the CLOTHING STATE and changes only the person: a second player in a
/// business suit gets the rebuilt character's business suit, not his casual outfit. The
/// one state with no art of its own - "nothing left to wear" - borrows the casual one,
/// which is what a player starts in and what the rest of the set was shot in.
/// </para>
///
/// <para>
/// WHAT THIS COSTS, SAID PLAINLY. All four players now look the same. In a four-handed
/// game the figure on the board no longer says WHOSE turn it is - the token beside him
/// still does, and the screen says so in words, but the walker does not. That is a worse
/// game than four distinct characters and a better one than a cast half-replaced, and it
/// is the trade Zain asked for. It stops being a trade the moment three more people are
/// photographed: give any of 284, 290 or 294 its own cels and that character stops
/// borrowing, automatically, with no change here.
/// </para>
///
/// <para>
/// IT IS STILL A STAND-IN AND NOT A FIX for 283. The game means "this player has run out
/// of clothes" and will be showing them dressed, so that state is no longer legible on the
/// board. The real answer is the shorts-and-vest sheet.
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

        // Which character's view is this, and which clothing state within it.
        var baseView = view switch
        {
            >= 280 and <= 283 => 280,
            >= 284 and <= 287 => 284,
            >= 290 and <= 293 => 290,
            >= 294 and <= 297 => 294,
            _ => 0,
        };
        if (baseView == 0) return null;

        // THE SAME CLOTHING STATE ON THE REBUILT CHARACTER. The set has the suit, the
        // dress clothes and the casual outfit; it has no "nothing left to wear", so that
        // state borrows the casual one - which is what the player is in by default and
        // what the rest of the set was shot in.
        var state = Math.Min(view - baseView, 2);
        var target = Rebuilt + state;

        // A view that already has its own art needs no stand-in.
        return target == view ? null : $"view_{target}{rest[underscore..]}";
    }

    /// <summary>The one character the replacement set covers: views 280, 281, 282.</summary>
    private const int Rebuilt = 280;
}
