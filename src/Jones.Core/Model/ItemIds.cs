namespace Jones.Core.Model;

/// <summary>
/// Item index numbers (`indexNum`) used by the original's consumable and durable lists.
/// Every value here is confirmed by a usage site in the decompiled source; the citation is
/// on each constant. Unconfirmed ids are deliberately absent rather than guessed.
/// </summary>
public static class ItemIds
{
    // --- Consumables -------------------------------------------------------

    /// <summary>Fresh food, from Black's Market. Spoils without a refrigerator. `market.sc:202`</summary>
    public const int FreshFood = 1;

    // Monolith Burgers, from `fastFood.sc`.
    public const int AstroChicken = 2;
    public const int Hamburgers = 3;
    public const int Cheeseburgers = 4;
    public const int Fries = 5;
    public const int Shakes = 6;
    public const int Colas = 7;

    /// <summary>The newspaper, from Black's Market. `market.sc:330`</summary>
    public const int Newspaper = 8;

    /// <summary>
    /// Lottery tickets. Zeroed every turn whether or not they win. `startTrn.sc:218`
    /// </summary>
    public const int LotteryTickets = 9;

    /// <summary>
    /// Fast food that does NOT keep: hamburgers, cheeseburgers and fries are zeroed
    /// outright at the start of the next turn (`startTrn.sc:420`, which loops 5 down to
    /// 3 and stops before 2).
    ///
    /// QUIRK, verified: Astro Chicken (id 2) is NOT in that range, so it escapes the
    /// purge and instead just decrements by one like any other consumable. It is the only
    /// fast food that keeps. Whether Sierra intended chicken to be a storable premium meal
    /// or simply wrote `> 2` where they meant `> 1` is unknowable, but the behaviour is
    /// real and the port reproduces it.
    /// </summary>
    public const int PerishableFastFoodFirst = 3;
    public const int PerishableFastFoodLast = 5;

    // Clothing. LOWER id is BETTER: dressedForWork scans 34 → 35 → 36 and takes the first
    // held, and the check is `wearing <= uniform`. That is how "the required uniform or
    // better" works. `room1.sc:685`
    public const int BusinessSuit = 34;
    public const int DressClothes = 35;
    public const int CasualClothes = 36;

    /// <summary>Weeks of rent paid at Low-Cost Housing. `room1.sc:788`</summary>
    public const int LowCostRent = 40;

    /// <summary>Weeks of rent paid at Le Security Apartments. `room1.sc:788`</summary>
    public const int SecurityRent = 41;

    // --- Durables ----------------------------------------------------------

    /// <summary>Refrigerator. Without it all fresh food spoils. `startTrn.sc:389`</summary>
    public const int Refrigerator = 21;

    /// <summary>Freezer. Raises fresh food storage from 6 to 12. `startTrn.sc:398`</summary>
    public const int Freezer = 22;

    // Appliances, from the Socket City and Z-Mart instance blocks.
    public const int Stove = 23;
    public const int ColorTV = 24;
    public const int Vcr = 25;
    public const int Stereo = 26;
    public const int Microwave = 27;
    public const int BlackWhiteTV = 30;

    /// <summary>Hot tub. Stops the per-turn relaxation decay entirely. `startTrn.sc:272`</summary>
    public const int HotTub = 28;

    /// <summary>Computer. 1-in-7 chance per turn of earning money. `startTrn.sc:255`</summary>
    public const int Computer = 29;

    /// <summary>
    /// The three reference books — encyclopedia, dictionary and atlas. Owning ALL THREE
    /// grants one extra-credit point, as does the computer separately (`room1.sc:191`).
    /// The individual id-to-title mapping is `[UNVERIFIED]`; only the set matters so far.
    /// </summary>
    public static readonly int[] ReferenceBooks = [31, 32, 33];

    /// <summary>
    /// Items a Wild Willy apartment robbery can never take: the fridge, freezer, item 23,
    /// the books and the computer. `startTrn.sc:292`
    /// </summary>
    public static readonly int[] NotStealable = [21, 22, 23, 29, 31, 32, 33];
}

/// <summary>
/// Durable attribute bit flags, from their usage in `room1.sc:768` and `startTrn.sc:568`.
/// </summary>
[Flags]
public enum DurableAttributes
{
    None = 0,

    /// <summary>
    /// THE PAWN TICKET, not a wear counter. `pawnShop.sc:687` is the only place this is
    /// ever written, and it writes both bits at once — `attributes: (| attributes $0018)`
    /// alongside `redemptionPrice: (/ pricePaid 2)` — as the item goes over the counter.
    /// `room1.sc:767-782` then ages it one step per turn, 24 → 16 → 8 → 0, which is the
    /// three weeks you have to redeem it; `pawnShop.sc:60` tests these bits to decide
    /// whether you have anything to redeem at all.
    /// </summary>
    WearMask = 0x0018,

    /// <summary>
    /// FORFEITED. Set the moment the ticket runs out (`room1.sc:779-782`), and from then on
    /// the item sits on the pawn shop's resale rack where ANY player can buy it —
    /// `pawnShop.sc:36` and `:270` scan every player's durables for exactly this bit to
    /// build the Buyable Items list.
    /// </summary>
    WornOut = 0x0020,

    /// <summary>
    /// Ticket plus forfeit. An item with any of these bits set is not yours to use: it is
    /// in hock. Tested together when deciding what may be pawned, what may break, and what
    /// Wild Willy may steal.
    /// </summary>
    WearOrWornMask = 0x0038,

    /// <summary>The item is breakable at all. `startTrn.sc:568`</summary>
    Breakable = 0x0040,

    /// <summary>
    /// Second-hand. Shortens the breakage interval from 1-in-51 to 1-in-36, which is the
    /// mechanical basis for "Z-Mart appliances break more often" (`startTrn.sc:595`) — but
    /// Z-Mart is not the only source: `pawnShop.sc:326` sets the same bit on every item it
    /// puts up on the resale rack, so anything bought out of hock is just as flimsy.
    /// </summary>
    CheapBuild = 0x0100,
}
