using Jones.Core.Model;

namespace Jones.App;

/// <summary>
/// WHERE OUR BUILDINGS KEEP THEIR DOORS.
///
/// The door is one shared Prop (view 751, thirteen loops of four cels) and each
/// <c>Place</c> lends it a position: <c>doorX</c>/<c>doorY</c> off the Place instances in
/// <c>room1.sc</c>, tabulated in <c>MainViewModel.DoorFor</c>. Those numbers are SIERRA'S
/// and they are exactly right for SIERRA'S buildings — on her board the door cel's bottom
/// row sits on her building's base, where she drew the doorway.
///
/// THEY ARE NOT RIGHT FOR A BUILDING WE DREW SOMEWHERE ELSE. The Factory is the case that
/// found this: Zain's own picture was fitted with its base nine rows below hers, because a
/// building may overhang the marble's path — the marble walks in front of it. True of the
/// building, false of its doorway. The door went on opening at her wall base, which on his
/// façade is a window and the bottom of his sign board, nine rows above the door the marble
/// is standing at. His words: "the whole point is that the marble enters into the door with
/// the door functioning on entry, this looks wrong on the factory."
///
/// <para>
/// A BUILDING MAY OVERHANG THE MARBLE'S PATH. ITS DOORWAY MAY NOT.
/// </para>
///
/// THREE WAYS TO FIX IT WERE MEASURED AND THIS IS THE FOURTH.
/// <list type="bullet">
/// <item>Construct an opening where the cel lands — no GPU, but it edits his picture and
///       puts the door's head behind his sign board.</item>
/// <item>Re-fit his picture to her base row — faithful, but the building shrinks 22% or its
///       roof pushes into the Bank's cel, and "I asked for the building size to be
///       increased" is a direct instruction it would undo.</item>
/// <item>Edit <c>DoorFor</c> — that is script data, the port's own rule forbids it, and it
///       would desync from her other twelve doors.</item>
/// <item>THIS: the art set declares where ITS doors are. Sierra's table is untouched and
///       still governs her art; a set that moves a building says where it moved the
///       doorway to.</item>
/// </list>
///
/// WHICH IS WHY THE OFFSET IS A PROPERTY OF THE ART SET AND NOT OF THE GAME. With no set in
/// force — <c>JONES_ART_SET=none</c>, or a set that was not found — every offset here is
/// zero and the doors land exactly where <c>room1.sc</c> puts them. Nothing in the port's
/// behaviour changes; what changes is where OUR buildings say their own doors are, which is
/// a fact about our pictures.
///
/// <para>
/// MEASURED, NOT ESTIMATED. Each entry is the difference between the footprint
/// <c>DoorFor</c> produces and the doorway actually painted in the installed façade, read
/// off the board at 12x. The door cels themselves are then generated at the CORRECTED
/// footprint — see <c>tools/door_cels.py</c>, whose <c>from_facade</c> takes the shut
/// state straight off the façade and is exactly wrong if the footprint is wrong: it would
/// paste his window in as the door leaf.
/// </para>
/// </summary>
public static class CastDoors
{
    /// <summary>
    /// How far our art moved this building's doorway from where Sierra put it, in the
    /// original's own 320x200 units. Positive Y is down the screen.
    ///
    /// <para>
    /// ONLY the buildings whose doorway actually moved appear here, and a building we have
    /// not rebuilt must never appear: it is still Sierra's art, so her coordinate is still
    /// the right one. Every other location returns zero.
    /// </para>
    /// </summary>
    private static (int Dx, int Dy) OffsetIn(string set, LocationId id) => set switch
    {
        // The only set that moves any door today, and it moves TWO. The door survey first
        // flagged three on a dark/wall ratio; measuring them one at a time cleared Bare
        // Necessities (her door's bottom row is 105 and the building's base row is 105 -
        // flush) and Watt a Bargain, so the ratio was wrong and the measurement governs.
        // The Factory is here because its doorway moved nine rows; Bun Believable because
        // its doorway moved one. Every other renamed building already has its painted door
        // under the sprite, which is why `from_facade` works on them untouched.
        "cast0" => id switch
        {
            // +11, NOT +9, AND THE DIFFERENCE IS HER OWN ALPHA. The base-row difference is
            // nine — his 190 against her 181 — and that is what an offset derived from the
            // BUILDING gives. But what has to land on his door is her LEAF, and her loop 5
            // cels are only 81.8% opaque: rows 0..8 are the leaf body, rows 9..10 are empty
            // in cel 0 and hold only the swinging feet in cels 1..3. Her leaf body is nine
            // rows and his door is seven, so seating the body's BOTTOM on the door's bottom
            // puts its top two rows over his lintel — which the cels then keep as façade.
            //
            // Measured on the installed façade at 12x, read off a cel grid:
            //     his stone lintel   y 183..184
            //     his door leaf      x 47..51, y 184..190
            //     his step           y 190..191, pavement from y 191
            //
            //     dy  +9   leaf body y 180..188   a row high; a row of his door shows below
            //     dy +10   leaf body y 181..189   still one row of door showing at y 190
            //     dy +11   leaf body y 182..190   BOTTOM FLUSH with his door's bottom, and
            //              her swinging feet land on the pavement at y 191..192
            //
            // The cels are generated to match: `head_rows: 2` keeps his lintel, so the
            // opening is y 184..190, and `opening_cols: (2,6)` keeps it inside his jambs —
            // her sprite is 10 cel px wide and his door is 5.
            LocationId.Factory => (0, 11),

            // BUN BELIEVABLE, +1 AND +1, AND BOTH ARE SMALL BECAUSE HIS PICTURE AND HERS
            // DRAW THE SAME DOOR. The burger shop is the first supplied building whose art
            // already had a glazed double door in roughly the right place, so this entry is
            // a nudge rather than the Factory's rescue. Measured on the installed façade at
            // 12x, read as the median of each 12x block:
            //
            //     his transom and head rail  y 66..68   (CAC8CB is the lit rail at y 67)
            //     his glazing                y 69..74
            //     his left jamb  x 284, panes x 285..292, right jamb x 293
            //     his pull handles x 288 and x 290, meeting stile x 289
            //
            // Sierra's footprint is 14x10 at (288,75), so x 281..294 and y 66..75, of which
            // rows 0..7 are the leaf body and 8..9 hold only the swinging feet.
            //
            //     dx   0   leaf centred on x 287.5, his door's centre is x 289
            //     dx  +1   centred on x 288.5 - half a pixel out, and the meeting stiles
            //              of her sprite and his façade now line up
            //
            //     dy   0   leaf body y 66..73; his glazing runs to y 74, so one row of his
            //              door stays showing under the leaf and the opening stops a row
            //              short of the floor - the Factory's fault at one tenth the size
            //     dy  +1   leaf body y 67..74   BOTTOM FLUSH with his glazing, head_rows 2
            //              lands exactly on his transom and rail, and the opening is his
            //              six glazed rows. Her feet fall on her own path at y 75..76.
            //
            // `opening_cols: (3,10)` in door_cels.py then keeps the hole inside his jambs:
            // her sprite is 14 cel px wide and his panes are 8.
            LocationId.MonolithBurgers => (1, 1),

            _                  => (0, 0),
        },
        _ => (0, 0),
    };

    /// <summary>
    /// The door position for <paramref name="id"/> once the live art set has had its say:
    /// Sierra's own coordinates, plus this set's offset if it declares one.
    ///
    /// <para>
    /// A SET THAT WAS ASKED FOR AND NOT FOUND GETS NOTHING. <see cref="RenderScale"/> draws
    /// Sierra's art in that case and says so loudly in the window title; moving her doors to
    /// suit art that is not on screen would be a second, silent failure on top of the first.
    /// </para>
    /// </summary>
    public static (int X, int Y) Place(LocationId id, int x, int y)
    {
        if (RenderScale.MissingArtSet is not null) return (x, y);
        if (RenderScale.ArtSet is not { } set) return (x, y);

        var (dx, dy) = OffsetIn(set, id);
        return (x + dx, y + dy);
    }
}
