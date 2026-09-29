using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Jones.Net;

namespace Jones.App.Net;

/// <summary>
/// Remembers how every bitmap on screen was made, so a screen can be sent over the network
/// as a list of recipes and rebuilt, bit for bit, from the joiner's own copy of the art.
///
/// <para>
/// WHY AT THE LOADERS. There are over a hundred places that put something on screen, and
/// every one of them gets its bitmap from one of five calls: <see cref="SciArt.Cel"/>,
/// <see cref="SciArt.SubCel"/>, <see cref="SciArt.Pic"/>, SciArt's named files and
/// <see cref="SciFont.Render(string, uint, uint?, uint?)"/>. Tagging the bitmap where it is
/// born covers all of them without touching any, and a screen written tomorrow is covered too.
/// </para>
///
/// <para>
/// Weakly keyed: the loaders cache, so this never holds anything they do not already hold.
/// </para>
/// </summary>
public static class ArtRecipes
{
    private static readonly ConditionalWeakTable<Bitmap, ArtRef> Table = new();

    public static void Note(Bitmap? bmp, ArtRef recipe)
    {
        if (bmp is not null) Table.AddOrUpdate(bmp, recipe);
    }

    public static ArtRef? Of(Bitmap? bmp) =>
        bmp is not null && Table.TryGetValue(bmp, out var recipe) ? recipe : null;

    /// <summary>The joiner's side: the same call the host made, with the same arguments.</summary>
    public static Bitmap? Load(ArtRef? r) => r?.K switch
    {
        "c" => SciArt.Cel(r.A, r.B, r.C),
        "s" => SciArt.SubCel(r.A, r.B, r.C, r.D),
        "p" => SciArt.Pic(r.A),
        "n" => r.S is null ? null : SciArt.Named(r.S),
        "t" => r.S is null ? null : SciFont.Load(r.A)?.Render(r.S, r.F, r.Sh, r.Bg),
        _ => null,
    };
}
