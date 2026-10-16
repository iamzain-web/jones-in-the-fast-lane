using System.Linq;
using Jones.Core.Model;

namespace Jones.App;

/// <summary>
/// WHAT OUR BUILDINGS ARE CALLED, IN THE WORDS THE GAME PRINTS.
///
/// The board has said <c>RENTFLIX</c> in paint and "Rent Office" in words since the first
/// building was rebuilt, because renaming a building was treated as an art job. It is not:
/// a name is a name wherever it appears, and the game prints them in two places.
///
/// <list type="number">
/// <item><c>MainViewModel.WhereText</c>, off <c>Board.Get(id).Name</c> — the port's own
///       table, which stays SIERRA'S because the tests identify places by it and because
///       it is transcribed from the scripts.</item>
/// <item>The Status screen's "Works at %s", which reads <c>text 700</c> at the employer's
///       <c>placeNum + 71</c> — one of HER resources, listing all twelve by name.</item>
/// </list>
///
/// Both are redirected here. Neither underlying table is edited: <c>Board.All</c> keeps her
/// labels, <c>assets/raw/text/700.text</c> is never written, and with no art set in force
/// this class returns null for everything and the game prints exactly what it printed in
/// 1990. The new names apply to the art set that contains the new buildings, which is the
/// only state in which they are true.
///
/// <para>
/// THIS IS NOT CLAUDE.md RULE 1. That rule forbids INVENTING user-visible text — putting
/// words in the game's mouth that no resource contains. These names are not invented and
/// not mine: they are Zain's, for buildings he commissioned to replace Sierra's, and the
/// whole project exists to remove her material. The rule protects the port's fidelity; it
/// does not oblige the port to keep calling his building by her name.
/// </para>
///
/// <para>
/// WHAT THIS DOES NOT TOUCH, and it is the larger half: the twenty-seven lines in text 210
/// that say "Welcome to Monolith Burger" in her own jokes, the eleven naming QT Clothing,
/// the nine naming the Factory. Those are HER WRITING, spoken in her recordings, and a
/// substitution would leave her sentence with his noun in it. They need an author. Until
/// then the sign says one thing and the shopkeeper says another, which is a known and
/// deliberate inconsistency rather than an oversight — see BUILDINGS.md.
/// </para>
/// </summary>
public static class CastNames
{
    /// <summary>
    /// The new name for <paramref name="id"/> in <paramref name="set"/>, or null to leave
    /// the game's own word alone.
    ///
    /// <para>
    /// ALL THIRTEEN ARE HERE NOW. The Bank was the last holdout — it kept Sierra's name
    /// while its art was still hers, because a new name over her building would have been
    /// the inverse of the bug this class fixes. It became Hyper Save when Zain supplied the
    /// artwork for it, which is the order these two things have to happen in.
    /// </para>
    /// </summary>
    /// <summary>
    /// MOVED TO <see cref="CastNameTable"/>, in Jones.Core. WHAT a building is called is pure
    /// data over LocationId; WHICH set is loaded is a question about the running app, and only
    /// the second needs the toolkit. While the two lived together here the table was
    /// unreachable from the tests - which is why nothing compared the Employment Office's nine
    /// employer labels against the names the rest of the game prints, and why that menu shipped
    /// listing Sierra's nine while every sign outside said something else.
    /// </summary>
    private static string? In(string set, LocationId id) => CastNameTable.NameIn(set, id);
    /// <summary>
    /// What <paramref name="set"/> calls <paramref name="id"/>, or null if it does not rename
    /// it — the table above, without consulting what is actually loaded.
    ///
    /// <para>
    /// EXPOSED FOR THE ONE PLACE A NAME IS NOT PRINTED THROUGH THIS CLASS: the Employment
    /// Office's nine employer lines, which carry a hand-measured <c>nsLeft</c> per label and
    /// so cannot simply call <see cref="For"/>. <c>EmployerListTests</c> compares them against
    /// this, because the renamed board shipped with that menu still listing Sierra's nine
    /// while every sign outside said something else.
    /// </para>
    /// </summary>
    public static string? NameIn(string set, LocationId id) => In(set, id);

    /// <summary>
    /// The name to print for <paramref name="id"/>: ours if this art set renamed it, and
    /// <paramref name="original"/> — the game's own word — otherwise.
    ///
    /// <para>
    /// A SET THAT WAS ASKED FOR AND NOT FOUND GETS NOTHING, for the same reason as
    /// <see cref="CastDoors"/>: the player is looking at Sierra's buildings, and calling
    /// them by our names would be a second failure stacked on the first.
    /// </para>
    /// </summary>
    public static string For(LocationId id, string original)
    {
        if (RenderScale.MissingArtSet is not null) return original;
        if (RenderScale.ArtSet is not { } set) return original;
        return In(set, id) ?? original;
    }

    /// <summary>
    /// The same, for the Status screen's read of <c>text 700</c>, which indexes by
    /// <c>placeNum + 71</c> rather than by <see cref="LocationId"/>.
    ///
    /// <para>
    /// WIDTH IS A REAL RISK HERE AND IT IS MEASURED RATHER THAN HOPED PAST. The format is
    /// <c>text 231[1]</c>, <c>"Works at %-16s"</c> — a sixteen-column MINIMUM, not a
    /// truncation, so a longer name simply runs on. Sierra's own longest is "Security
    /// Apartment" at 18, already two past the field; ours reaches 25 at "The Industrial
    /// Revolution". The name is printed at the end of its line with nothing to its right,
    /// so a longer one extends into empty space rather than colliding with another column —
    /// but it is the one place these names can overrun, and it is why this method exists
    /// separately rather than being folded into <see cref="For"/>.
    /// </para>
    /// </summary>
    public static string ForPlaceNum(int placeNum, string original)
    {
        var place = Board.All.FirstOrDefault(p => p.PlaceNum == placeNum);
        return place is null ? original : For(place.Id, original);
    }
}
