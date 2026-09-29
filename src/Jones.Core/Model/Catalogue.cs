using Jones.Core.Economy;

namespace Jones.Core.Model;

/// <summary>What kind of shelf entry this is.</summary>
public enum GoodsType
{
    /// <summary>A durable — kept, valued in net worth, pawnable, breakable.</summary>
    Durable,

    /// <summary>A consumable — decremented one per turn by `Consumable::doit`.</summary>
    Consumable,

    /// <summary>
    /// Junk. Has no item id at all, so it is never added to inventory: it takes your
    /// money, costs you happiness, and is gone. `discount.sc:665`
    /// </summary>
    Junk,

    /// <summary>
    /// `typeOfGoods 3` — the newspaper (`market.sc:337`), the shakes (`fastFood.sc:256`)
    /// and the colas (`:280`). `CostDItem::doit` (`WButton.sc:203-226`) switches on
    /// typeOfGoods over cases 0, 1 and 2 only, so a 3 falls through the switch and is never
    /// received into any list. The money is taken and the `visitTime` is spent; no
    /// inventory entry is ever created, and `global418` stays 0 so no `pricePaid` is
    /// written either. That is why the paper's `indexNum 8` is a number that never lands
    /// anywhere.
    /// </summary>
    NotStored,
}

/// <param name="ItemId">Inventory id, or null for junk which is never stored.</param>
/// <param name="BasePrice">Price before the economy is applied.</param>
/// <param name="Quantity">Units received per purchase (food is sold in packs).</param>
/// <param name="FixedPrice">
/// `fixedPrice 1` — the instance declares `price` outright rather than `basePrice`, and
/// `CostDItem::init` (`WButton.sc:183-188`) skips the economy conversion entirely. Only
/// Black's Market's lottery tickets ($10) and newspaper ($1) set it.
/// </param>
public sealed record StockItem(
    string Name,
    int? ItemId,
    int BasePrice,
    GoodsType Type = GoodsType.Durable,
    int Quantity = 1,
    bool FixedPrice = false)
{
    /// <summary>
    /// Shelf price at the current economy. Goods use the GOODS index (`global309`),
    /// confirmed at `employment.sc:66` and `pawnShop.sc:664`.
    /// </summary>
    public int PriceAt(int goodsIndexReading) =>
        FixedPrice ? BasePrice : Pricing.Price(goodsIndexReading, BasePrice);
}

/// <summary>
/// Store inventories with base prices, taken from the instance definitions in each store's
/// script. Every price here is verified against the source; nothing is inferred.
///
/// Cross-check: these reproduce the wiki's published Z-Mart discount percentages exactly —
/// 25% on the refrigerator (650 vs 876), 33% on the colour TV (349 vs 525), 52% on casual
/// clothes (35 vs 73), and the stereo genuinely DEARER at Z-Mart (450 vs 412).
///
/// THE NAMES ARE TEXT RESOURCE 700 at the item's own id. `pawnShop.sc:672` names items
/// from it (`"Do you accept $%d for your %s?"`), as does the stats screen, so 700[25] is
/// `Vcr` (not `VCR`), 700[35] `Leisure Suit` (not `Dress Clothes`), 700[65] `T-BillS` and
/// 700[69] `Blue Chip Stocks`. The three food packs share item id 1 (`Food`) and so have
/// no entry of their own; their names are the shelf labels from script 203.
/// `Dog Food`, `8-Track Player` and `Works of Capote` are junk with no item id at all and
/// no text-700 entry either — their only origin is the Z-Mart label.
/// </summary>
public static class Catalogue
{
    /// <summary>
    /// Z-Mart (`discount.sc`). Stocks only **6 of these 18 at random**, rerolled at the
    /// start of each player's turn. Appliances bought here carry the CheapBuild flag and
    /// break on a 1-in-36 roll rather than 1-in-51.
    /// </summary>
    public static readonly StockItem[] ZMart =
    [
        new("Refrigerator",     ItemIds.Refrigerator,   650),
        new("Stove",            ItemIds.Stove,          490),
        new("Stereo",           ItemIds.Stereo,         450),
        new("Color TV",         ItemIds.ColorTV,        349),
        new("Black & White TV", ItemIds.BlackWhiteTV,   110),
        new("Microwave",        ItemIds.Microwave,      220),
        new("Vcr",              ItemIds.Vcr,            250),
        new("Encyclopedia",     31,                     475),
        new("Dictionary",       32,                      70),
        new("Atlas",            33,                      55),
        // `units` is WEEKS OF WEAR for clothing, and Z-Mart's are DELIBERATELY shorter
        // than QT Clothing's: 9 weeks here (`discount.sc`) against 11-13 there
        // (`clothing.sc`). You pay 35 instead of 73 for casual clothes and they fall
        // apart two weeks sooner. Leaving these at the class default of 1 meant a new
        // outfit lasted a single week and the player was undressed again after one
        // weekend, because the weekly consumable tick took the quantity straight to zero.
        new("Casual Clothes",   ItemIds.CasualClothes,   35, GoodsType.Consumable, Quantity: 9),
        new("Leisure Suit",     ItemIds.DressClothes,    90, GoodsType.Consumable, Quantity: 9),
        new("Baseball Tickets", 37,                      45, GoodsType.Consumable, Quantity: 4),
        new("Theatre Tickets",  38,                      30, GoodsType.Consumable, Quantity: 4),
        new("Concert Tickets",  39,                      40, GoodsType.Consumable, Quantity: 4),
        new("Dog Food",         null,                    18, GoodsType.Junk),
        new("8-Track Player",   null,                    75, GoodsType.Junk),
        new("Works of Capote",  null,                   100, GoodsType.Junk),
    ];

    /// <summary>
    /// Socket City (`appliance.sc`) — appliances at full price, but it is the ONLY source
    /// of the freezer, hot tub and computer, each of which changes a rule rather than just
    /// adding happiness.
    /// </summary>
    public static readonly StockItem[] SocketCity =
    [
        new("Refrigerator", ItemIds.Refrigerator,  876),
        new("Freezer",      ItemIds.Freezer,       513),
        new("Stove",        ItemIds.Stove,         570),
        new("Color TV",     ItemIds.ColorTV,       525),
        new("Vcr",          ItemIds.Vcr,           333),
        new("Stereo",       ItemIds.Stereo,        412),
        new("Microwave",    ItemIds.Microwave,     330),
        new("Hot Tub",      ItemIds.HotTub,       1255),
        new("Computer",     ItemIds.Computer,     1599),
    ];

    /// <summary>QT Clothing (`clothing.sc`). Lower item id is dressier.</summary>
    public static readonly StockItem[] QtClothing =
    [
        // Weeks of wear, from each instance's `units` (`clothing.sc`). The dearer outfits
        // last longer, and both suits outlast the casual clothes — see the note on
        // Z-Mart's 9-week versions above.
        new("Business Suit",  ItemIds.BusinessSuit, 295, GoodsType.Consumable, Quantity: 13),
        new("Leisure Suit",   ItemIds.DressClothes, 125, GoodsType.Consumable, Quantity: 13),
        new("Casual Clothes", ItemIds.CasualClothes, 73, GoodsType.Consumable, Quantity: 11),
    ];

    /// <summary>
    /// Black's Market (`market.sc:194-366`). Fresh food is sold in packs that all land on
    /// item 1; the 4-week pack is the best value at $47.50/week against $55 for one.
    ///
    /// The shop has FIVE lines, not three. The lottery tickets and the newspaper carry no
    /// `basePrice` because they declare `price` with `fixedPrice 1` instead — $10 for ten
    /// tickets and $1 for the paper, both immune to the economy. The paper also declares
    /// `visitTime 1`, an hour on top of the two that walking in already cost, and buying it
    /// opens script 215 rather than putting anything in your pocket.
    /// </summary>
    public static readonly StockItem[] BlacksMarket =
    [
        new("Food For 1 Week",    ItemIds.FreshFood,       55, GoodsType.Consumable, Quantity: 1),
        new("Food For 2 Weeks",   ItemIds.FreshFood,      100, GoodsType.Consumable, Quantity: 2),
        new("Food For 4 Weeks",   ItemIds.FreshFood,      190, GoodsType.Consumable, Quantity: 4),
        new("10 Lottery Tickets", ItemIds.LotteryTickets,  10, GoodsType.Consumable, Quantity: 10, FixedPrice: true),
        new("Newspaper",          null,                     1, GoodsType.NotStored,  Quantity:  1, FixedPrice: true),
    ];

    /// <summary>
    /// A newspaper costs TWO hours, and the hours are charged twice over for a reason:
    /// `market.sc:333` declares `visitTime 1`, spent by `CostDItem::doit` at
    /// `WButton.sc:251` on both the success and the failure branch, and the success branch
    /// at `market.sc:346` then runs an explicit `(gTimeKeep doit: 1)` of its own.
    ///
    /// Both are on top of the two hours that walking into Black's Market already cost.
    /// </summary>
    public const int NewspaperVisitHours = 2;

    /// <summary>
    /// What a FAILED newspaper purchase costs — `visitTime` alone, since `WButton.sc:251`
    /// sits after the if/else and runs on both branches. The newspaper is the only line in
    /// the game with a nonzero `visitTime`, so it is the only one where this is visible.
    /// </summary>
    public const int NewspaperFailedVisitHours = 1;

    /// <summary>
    /// Monolith Burgers (`fastFood.sc`). Ids 3–5 are purged at the next turn start; Astro
    /// Chicken (2) is not — see <see cref="ItemIds"/>.
    ///
    /// The shakes and the colas are `typeOfGoods 3` (`fastFood.sc:256` and `:280`), like the
    /// newspaper: they fall through `CostDItem::doit`'s switch and are never received into
    /// any list. Typing them Consumable put ids 6 and 7 into the player's inventory, where
    /// nothing in the shipped game should ever be — a drink you could count. They still pay
    /// their happiness (that award is in the instance's own `doit`, not in the switch); the
    /// drink itself simply does not exist once you have bought it.
    /// </summary>
    public static readonly StockItem[] MonolithBurgers =
    [
        new("Astro Chicken", ItemIds.AstroChicken,  124, GoodsType.Consumable),
        new("Hamburgers",    ItemIds.Hamburgers,     79, GoodsType.Consumable),
        new("Cheeseburgers", ItemIds.Cheeseburgers,  89, GoodsType.Consumable),
        new("Fries",         ItemIds.Fries,          65, GoodsType.Consumable),
        new("Shakes",        ItemIds.Shakes,        102, GoodsType.NotStored),
        new("Colas",         ItemIds.Colas,          69, GoodsType.NotStored),
    ];

    /// <summary>
    /// Text resource 700's name for an item id. The pawn shop names everything this way —
    /// `Format … 212 1 700 (indexNum)` for the pawnable list and `212 2` for the other two
    /// (`pawnShop.sc:83-90`, `:207-215`, `:270-282`) — and so does the stats screen.
    ///
    /// Every durable in the game is on some shelf, so the shelves are the table: the
    /// entries' names ARE text 700's, which is the whole point of the renaming above.
    /// </summary>
    public static string NameOf(int itemId)
    {
        foreach (var shelf in new[] { ZMart, SocketCity, QtClothing, BlacksMarket, MonolithBurgers })
            foreach (var s in shelf)
                if (s.ItemId == itemId) return s.Name;

        return "";
    }

    /// <summary>
    /// Investment base prices (`room1.sc:674`). T-bills are fixed; the rest track their own
    /// economic index rather than the goods index.
    /// </summary>
    public static readonly (string Name, int BasePrice)[] Investments =
    [
        ("T-BillS",          100),
        ("Gold",             413),
        ("Silver",            14),
        ("Pork Bellies",      20),
        ("Blue Chip Stocks",  49),
        ("Penny Stocks",       7),
    ];
}
