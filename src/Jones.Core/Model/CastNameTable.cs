namespace Jones.Core.Model;

/// <summary>
/// WHAT EACH ART SET CALLS EACH BUILDING. Pure data over <see cref="LocationId"/>.
///
/// <para>
/// WHY IT IS HERE AND NOT IN <c>Jones.App</c>. It began inside <c>CastNames</c>, which also
/// decides WHICH set is in force and therefore depends on <c>RenderScale</c> and through it on
/// Avalonia. That made the table unreachable from <c>Jones.Tests</c>, which is net8.0 and
/// deliberately toolkit-free — and the consequence was that nothing compared the Employment
/// Office's nine employer labels against the names the rest of the game prints. The renamed
/// board shipped with that menu still listing Sierra's nine while every sign outside said
/// something else, and 683 tests passed throughout.
/// </para>
///
/// <para>
/// The split is the useful one: WHAT a building is called is data; WHICH set is loaded is a
/// question about the running app. Only the second needs the toolkit.
/// </para>
/// </summary>
public static class CastNameTable
{
    /// <summary>
    /// The name <paramref name="set"/> gives <paramref name="id"/>, or null if it does not
    /// rename it.
    ///
    /// <para>
    /// ALL THIRTEEN BOARD PLACES ARE HERE. The Bank was the last holdout — it kept Sierra's
    /// name while its art was still hers, because a new name over her building would be the
    /// inverse of the bug this exists to fix. It became Hyper Save when the artwork for it
    /// arrived, which is the order those two things have to happen in.
    /// </para>
    /// </summary>
    public static string? NameIn(string set, LocationId id) => set switch
    {
        "cast0" => id switch
        {
            LocationId.RentOffice         => "Rentflix",
            LocationId.LowCostHousing     => "Casa El Cheapo",
            LocationId.SecurityApartments => "Villa Locke & Keyes",
            LocationId.BlacksMarket       => "Bare Necessities",
            LocationId.Factory            => "The Industrial Revolution",
            LocationId.EmploymentOffice   => "Hire Ground",
            LocationId.HiTechU            => "Open Door University",
            LocationId.SocketCity         => "Watt a Bargain",
            LocationId.QtClothing         => "Common Threads",
            LocationId.MonolithBurgers    => "Bun Believable",
            LocationId.ZMart              => "General Dealer",
            LocationId.PawnShop           => "Needful Things",
            LocationId.Bank               => "Hyper Save",
            _                             => null,
        },
        _ => null,
    };
}
