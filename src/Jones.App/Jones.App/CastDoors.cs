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
        // The only set that moves any door, and after measuring all ten rebuilt buildings
        // one at a time it moves SIX. The door survey's original shortlist came off a
        // dark/wall ratio and that ratio has now been wrong three times — it cleared Hire
        // Ground at 0.40 and Rentflix at 0.54, both of which needed an offset, and it
        // flagged Bare Necessities at 1.68 and Monolith at 1.24, neither of which did. It
        // is a first-pass hint and nothing here rests on it.
        //
        // What each entry rests on is the same measurement every time: the painted doorway
        // read off the installed façade at 12x, against the footprint `DoorFor` produces.
        // Six moved — the Factory by nine rows, Casa El Cheapo by five, Rentflix by three
        // upwards, Bun Believable, Hire Ground and Watt a Bargain by one. Three did not,
        // and are absent.
        //
        // THE SPREAD IS THE ARGUMENT FOR DESIGNING THE DOORWAY FIRST. Nine rows, five
        // rows, three rows — those are façades generated without the sprite in mind, and
        // one of them (Casa El Cheapo) still cannot be fully covered at any offset because
        // its door is eleven rows and her leaf is six. Watt a Bargain is the only building
        // whose doorway was placed from `DoorFor` before its init was written, and it came
        // out one row off.
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

            // HIRE GROUND, +1 DOWN. His timber double door measures x 92..101 y 172..182
            // off the board at 12x — the same ten columns as her sprite, which is the first
            // exact horizontal match in the programme, so dx is genuinely 0 here. Her leaf
            // body is y 171..181, one row high: at 0 its top row sits on his lintel and his
            // bottom door row shows below the leaf. At +1 the body is y 172..182, flush top
            // and bottom, and her feet land on his pale step at y 183..184.
            LocationId.EmploymentOffice => (0, 1),

            // RENTFLIX, 3 ROWS UP, and it is the only NEGATIVE offset in this table. His
            // glazed shop door is x 102..108 — her exact seven columns again — but y 17..24,
            // three rows above where DoorFor puts her sprite. Her body is 9 rows against his
            // 8, so −3 seats the body's bottom on y 24 and `head_rows: 1` keeps the row
            // above as façade. The building's region ends at y 24, so her feet at y 25..26
            // are standing on her own board below it.
            //
            // A building may overhang the marble's path; it may also sit HIGHER than she
            // drew it. The rule in the remarks holds either way — the doorway is what has
            // to be under the sprite.
            LocationId.RentOffice => (0, -3),

            // CASA EL CHEAPO, −1 ACROSS AND +5 DOWN, and it is the one that does not fit.
            // His timber door is x 139..146 y 23..33 — eleven rows — and her loop 0 sprite
            // is 8x8 with a SIX row body. No offset makes a six-row leaf cover an eleven-row
            // door, so the choice is which half of his door opens:
            //
            //     dy  0   body y 23..28, his door's top six rows. The opening is a hole in
            //             the upper half of a door whose lower half is still painted shut,
            //             and the marble walks into the part that did not move.
            //     dy +5   body y 28..33, BOTTOM FLUSH with his door. His top five rows stay
            //             as façade and read as the door's head; the opening reaches the
            //             floor, which is the thing Zain asked for by name.
            //
            // dx −1 because his jambs measure #4E4E4D at x 138 and #3A3A3F at x 147, so the
            // leaf is x 139..146 against her footprint's x 140..147.
            LocationId.LowCostHousing => (-1, 5),

            // WATT A BARGAIN, +1 DOWN, AND IT IS THE SMALLEST OFFSET IN THE TABLE BECAUSE
            // ITS BUILDING WAS DESIGNED AROUND THIS SPRITE. Every other façade here was
            // generated first and measured afterwards, and three of them needed an offset
            // to recover. This one had its doorway placed at cel rows 38..49 and cols
            // 15..28 of its region *before* the init was written, because that is exactly
            // where `DoorFor` lands her loop 8 leaf body (game x 270..283, y 170..181).
            //
            // Measured on the result at 12x: his head rail at y 172, his entrance glazing
            // y 173..182, his threshold line y 183, his jambs at x 270 and x 282..283 with
            // eleven columns of glazing between them. Her body is y 170..181 against
            // glazing that ends at y 182, so +1 seats it flush and `head_rows: 2` lands on
            // the head rail. One row — and that one row is the generator's own tolerance,
            // not a mistake in the placement.
            LocationId.SocketCity => (0, 1),

            // NOT HERE, AND THAT IS ALSO A MEASUREMENT. Open Door University, Needful Things
            // and Common Threads were each measured off the board at 12x and each came back
            // zero — her body rows ARE the painted doorway's rows in all three. They are
            // handled entirely by `head_rows`, `foot_rows` and `opening_cols` in
            // door_cels.py, which shape the opening without moving the sprite. A building
            // that does not need an offset must not have one: its absence here is what
            // keeps Sierra's own coordinate authoritative for it.

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
