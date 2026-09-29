using System.Collections.Generic;
using Jones.Core.Model;

namespace Jones.App;

/// <summary>
/// The verbatim `text` property of every selectable item, byte for byte from the source.
///
/// DO NOT TIDY THESE STRINGS. The trailing dots, spaces and pipes are not decoration and
/// not padding-by-eye: in font 10 (the interface font) `.` is a 3-pixel dot, a space is
/// 5 pixels, and `|` is a 1-pixel-wide BLANK used as a fine alignment shim. Every label
/// within a store is padded to an identical pixel width so that the price, appended
/// afterwards, lands in the same column whether it has two digits or four. A single space
/// is worth five pipes. Removing one character misaligns the column.
///
/// THE PADDING IS SPACES, NOT UNDERSCORES. The decompiler renders a space inside a string
/// literal as `_` so that runs of them are visible in the listing, and I copied that
/// straight across — which drew a row of dashes after every job title and every shop item,
/// something the original never had. The raw script resources settle it: script 217 holds
/// `Cook               ||`, and with real spaces all four Monolith labels measure exactly
/// 96 px, which is what puts the wages in one column. With underscores they measured
/// 81/81/94/85 and the column was visibly ragged. Verify against `assets/raw/script/*`,
/// never against the decompiled `.sc` listing.
///
/// EQUAL LABEL WIDTH IS A HEURISTIC, NOT THE RULE. Measured against
/// `assets/raw/font/10.font` (space 5px, `|` 1px, `.` 3px) the shop labels come out at:
/// Socket City 76px on all nine; QT Clothing 70px on all three; Monolith 79px in the left
/// column and 81px in the right; Black's Market 85/85/84/90/94px; Z-Mart 69px on the five
/// items whose price can reach four digits and 74px on the other thirteen. The uneven ones
/// are uneven in the shipped game — each store's own `doFormat` prepends a different
/// amount (`.||`, a leading space, or nothing) and the invariant the original actually
/// holds is the price's RIGHT edge. Socket City stops at 179/112, QT Clothing at 175,
/// Monolith at 110/177, Black's Market at 112/180, Z-Mart at 95. Every shop line in this
/// file was re-checked against those stops; the only line that misses is Black's Market's
/// "Food For 4 Weeks..", 1px short, which is the original's own slip.
/// </summary>
public static class ItemText
{
    // --- Jobs, keyed by workplace and title. Format appends " $N Hr." ---------

    private static readonly Dictionary<(Workplace, string), string> Jobs = new()
    {
        [(Workplace.ZMart, "Clerk")]                    = "Clerk              |||",
        [(Workplace.ZMart, "Assist. Manager")]          = "Assistant Manager  ",
        [(Workplace.ZMart, "Manager")]                  = "Manager           |",

        [(Workplace.MonolithBurgers, "Cook")]              = "Cook               ||",
        [(Workplace.MonolithBurgers, "Clerk")]             = "Clerk               |",
        [(Workplace.MonolithBurgers, "Assist. Manager")]   = "Assistant Manager  |||",
        [(Workplace.MonolithBurgers, "Manager")]           = "Manager           ||||",

        [(Workplace.QtClothing, "Janitor")]           = "Janitor            |",
        [(Workplace.QtClothing, "Salesperson")]       = "Salesperson        ||",
        [(Workplace.QtClothing, "Assist. Manager")]   = "Assistant Manager  ",
        [(Workplace.QtClothing, "Manager")]           = "Manager           |",

        [(Workplace.SocketCity, "Clerk")]                 = "Clerk               |||",
        [(Workplace.SocketCity, "Salesperson")]           = "Salesperson         ||",
        [(Workplace.SocketCity, "Repairman")]             = "Electronic's Repair   ",
        [(Workplace.SocketCity, "Manager")]               = "Manager            |",

        [(Workplace.HiTechU, "Janitor")]   = "Janitor            |",
        [(Workplace.HiTechU, "Teacher")]   = "Teacher            ",
        [(Workplace.HiTechU, "Professor")] = "Professor          ",

        [(Workplace.Factory, "Janitor")]             = "Janitor            |||",
        [(Workplace.Factory, "Assembly Worker")]     = "Assembly Worker    ||||",
        [(Workplace.Factory, "Secretary")]           = "Secretary          ||",
        [(Workplace.Factory, "Machinist Helper")]    = "Machinist's Helper   |",
        [(Workplace.Factory, "Exec. Secretary")]     = "Executive Secretary ||",
        [(Workplace.Factory, "Machinist")]           = "Machinist           ",
        [(Workplace.Factory, "Department Mgr.")]     = "Department Manager ",
        [(Workplace.Factory, "Engineer")]            = "Engineer            ",
        [(Workplace.Factory, "General Mgr.")]        = "General Manager    ||",

        [(Workplace.Bank, "Janitor")]           = "Janitor            |",
        [(Workplace.Bank, "Teller")]            = "Teller              ||",
        [(Workplace.Bank, "Assist. Manager")]   = "Assistant Manager  ",
        [(Workplace.Bank, "Manager")]           = "Manager           |",
        [(Workplace.Bank, "Broker")]            = "Investment Broker  |||",

        [(Workplace.BlacksMarket, "Janitor")]           = "Janitor            |",
        [(Workplace.BlacksMarket, "Checker")]           = "Checker            |",
        [(Workplace.BlacksMarket, "Butcher")]           = "Butcher           ||||",
        [(Workplace.BlacksMarket, "Assist. Manager")]   = "Assistant Manager  ",
        [(Workplace.BlacksMarket, "Manager")]           = "Manager           |",

        [(Workplace.RentOffice, "Groundskeeper")]     = "Groundskeeper      ||||",
        [(Workplace.RentOffice, "Apartment Mgr")]     = "Apartment Manager  ",
    };

    public static string For(Job job) =>
        Jobs.TryGetValue((job.Workplace, job.Title), out var t) ? t : job.Title;

    // --- Shop items, keyed by the catalogue's display name -------------------

    private static readonly Dictionary<(LocationId, string), string> Items = new()
    {
        // Z-Mart (discount.sc)
        [(LocationId.ZMart, "Refrigerator")]     = "Refrigerator.....",
        [(LocationId.ZMart, "Stove")]            = "Stove|..............||",
        [(LocationId.ZMart, "Stereo")]           = "Stereo|.............||",
        [(LocationId.ZMart, "Color TV")]         = "Color TV..........||",
        [(LocationId.ZMart, "Black & White TV")] = "Black & White TV||",
        [(LocationId.ZMart, "Microwave")]        = "Microwave|.........|",
        [(LocationId.ZMart, "Vcr")]              = "VCR...................|",
        [(LocationId.ZMart, "Casual Clothes")]   = "Casual Clothes...",
        [(LocationId.ZMart, "Leisure Suit")]     = "Dress Clothes.....",
        [(LocationId.ZMart, "Baseball Tickets")] = "Baseball Tickets..",
        [(LocationId.ZMart, "Theatre Tickets")]  = "Theatre Tickets...",
        [(LocationId.ZMart, "Concert Tickets")]  = "Concert Tickets|..|",
        [(LocationId.ZMart, "Encyclopedia")]     = "Encyclopedia.....",
        [(LocationId.ZMart, "Dictionary")]       = "Dictionary..........",
        [(LocationId.ZMart, "Atlas")]            = "Atlas.................|",
        [(LocationId.ZMart, "Dog Food")]         = "Dog Food...........|",
        [(LocationId.ZMart, "8-Track Player")]   = "8-Track Player...",
        [(LocationId.ZMart, "Works of Capote")]  = "Works of Capote|",

        // Socket City (appliance.sc)
        [(LocationId.SocketCity, "Refrigerator")] = "Refrigerator|.......",
        [(LocationId.SocketCity, "Freezer")]      = "Freezer|..............|",
        [(LocationId.SocketCity, "Stove")]        = "Stove.................|",
        [(LocationId.SocketCity, "Color TV")]     = "Color TV.............",
        [(LocationId.SocketCity, "Vcr")]          = "VCR....................",
        [(LocationId.SocketCity, "Stereo")]       = "Stereo................|",
        [(LocationId.SocketCity, "Microwave")]    = "Microwave..........|",
        [(LocationId.SocketCity, "Hot Tub")]      = "Hot Tub..............",
        [(LocationId.SocketCity, "Computer")]     = "Computer............",

        // QT Clothing (clothing.sc)
        [(LocationId.QtClothing, "Business Suit")]  = "Business Suit  |",
        [(LocationId.QtClothing, "Leisure Suit")]  = "Dress Clothes  |",
        [(LocationId.QtClothing, "Casual Clothes")] = "Casual Clothes ",

        // Black's Market (market.sc)
        [(LocationId.BlacksMarket, "Food For 1 Week")]    = "Food For 1 Week....",
        [(LocationId.BlacksMarket, "Food For 2 Weeks")]   = "Food For 2 Weeks..|",
        [(LocationId.BlacksMarket, "Food For 4 Weeks")]   = "Food For 4 Weeks..",
        // The other two lines market.sc declares. Raw script 203 @0x0E40 and @0x0E67.
        [(LocationId.BlacksMarket, "10 Lottery Tickets")] = "10 Lottery Tickets|...|",
        [(LocationId.BlacksMarket, "Newspaper")]          = "Newspaper................|",

        // Monolith Burgers (fastFood.sc)
        [(LocationId.MonolithBurgers, "Hamburgers")]    = "Hamburgers.........",
        [(LocationId.MonolithBurgers, "Cheeseburgers")] = "Cheeseburger.......|",
        [(LocationId.MonolithBurgers, "Astro Chicken")] = "Astro Chicken.......|",
        [(LocationId.MonolithBurgers, "Fries")]         = "Fries....................",
        [(LocationId.MonolithBurgers, "Shakes")]        = "Shakes.................|",
        [(LocationId.MonolithBurgers, "Colas")]         = "Colas...................|",
    };

    public static string For(LocationId where, string name) =>
        Items.TryGetValue((where, name), out var t) ? t : name;

    /// <summary>
    /// CostDItem's format (`WButton.sc:185`): a space before the figure under $100,
    /// none at or above it â€” which is how the column stays put as the price grows.
    /// </summary>
    public static string WithPrice(string label, int price) =>
        price < 100 ? $"{label} ${price}" : $"{label}${price}";

    /// <summary>
    /// JobDItem's format (`employment.sc:156-161`): the wage picks one of TWO format
    /// strings, and they are not the same.
    ///
    ///     (if (&lt; price 10) (Format param1 206 0 ...) else (Format param1 206 1 ...))
    ///
    /// The decompiler prints the comment "%s $%d Hr." against both, which is wrong and
    /// cost me an afternoon. The actual text resource 206 holds:
    ///
    ///     index 0:  "%s  $%d Hr."   <- TWO spaces, used when the wage is under $10
    ///     index 1:  "%s $%d Hr."    <- one space
    ///
    /// The extra space makes up for the missing second digit, so "Hr." stays in one
    /// column whether the job pays $9 or $12. Same trick as CostDItem's price column.
    /// </summary>
    public static string WithWage(string label, int wage) =>
        wage < 10 ? $"{label}  ${wage} Hr." : $"{label} ${wage} Hr.";

    /// <summary>
    /// SOCKET CITY AND Z-MART DO NOT INHERIT CostDItem's FORMAT EITHER, and the port had them
    /// doing so — which left Socket City's price column RAGGED BY FIVE PIXELS. Found by the
    /// column assertions in `ColumnStopTests`, which is what they are for.
    ///
    /// <code>
    /// appliance.sc  (method (doFormat param1)
    ///                   (if (&lt; price 1000) (Format param1 208 0 text price)
    ///                   else               (Format param1 208 1 text price)))
    /// discount.sc   the same, against resource 211
    /// </code>
    ///
    /// Both resources hold the same two strings, verbatim from
    /// `assets/raw/text/208.text` and `211.text`:
    ///
    ///     [0] "%s.||$%3d"   under $1000     [1] "%s$%4d"   at or above it
    ///
    /// The `.||` is 3 + 1 + 1 = 5 pixels in font 10 — the same width as the space
    /// `CostDItem` would have used — but it is there on BOTH arms, where CostDItem's space
    /// appears only under $100. With the correct format all nine Socket City lines measure
    /// exactly 102px and the column lands at 179 and 112, which is what `StoreLayout` has
    /// always said it does. With CostDItem's they measured 97 or 102 depending on whether
    /// the price had three digits or four.
    ///
    /// Z-Mart is 95 or 100 either way: its eighteen labels are not all one width (69px on
    /// the five that can reach four digits, 74px on the other thirteen), so its column is
    /// five pixels ragged in the shipped game. That raggedness is the original's and is now
    /// reproduced from the original's own format string rather than by accident.
    /// </summary>
    public static string AppliancePrice(string label, int price) =>
        price < 1000 ? $"{label}.||${price,3}" : $"{label}${price,4}";

    /// <summary>
    /// Black's Market does NOT inherit CostDItem's format. Every one of its five lines
    /// declares its own `doFormat` (`market.sc:207-213, 307-309, 337-339`) and text
    /// resource 203 holds all four strings:
    ///
    ///     [0] "%s.||$%2d"   food under $100      [1] "%s$%3d"  food at or above it
    ///     [2] "%s$%2d"      the lottery tickets  [3] "%s$%d"   the newspaper
    ///
    /// The food lines' `.||` is 3 + 1 + 1 = 5 pixels in font 10, exactly the width of the
    /// space CostDItem would have used, so the column does not move — but the leader dot
    /// is drawn, and the lottery and newspaper lines get NO gap before the figure at all.
    /// </summary>
    public static string BlacksMarketPrice(string name, string label, int price) => name switch
    {
        "10 Lottery Tickets" => $"{label}${price,2}",
        "Newspaper"          => $"{label}${price}",
        _                    => price < 100 ? $"{label}.||${price,2}" : $"{label}${price,3}",
    };

    // -----------------------------------------------------------------------
    // The same lines, split in two, for the measured layout
    // -----------------------------------------------------------------------

    /// <summary>
    /// A formatted line together with the VALUE TOKEN it ends with.
    ///
    /// <para>
    /// <see cref="Full"/> is exactly what the three formatters above already produced and is
    /// what the bitmap face draws, unchanged. <see cref="Value"/> names the suffix that has to
    /// land in the price or wage column when the interface is drawn in a proportional face,
    /// where the padding in front of it means nothing — see <c>Jones.Core.Text.ColumnLayout</c>.
    /// </para>
    ///
    /// <para>
    /// The two are DERIVED SEPARATELY on purpose, so that neither formatter had to be rewritten
    /// and the resource strings above stay untouched; <c>ColumnStopTests</c> checks every line
    /// the game can draw and fails if a <see cref="Full"/> ever stops ending with its own
    /// <see cref="Value"/>.
    /// </para>
    /// </summary>
    public readonly record struct Line(string Full, string Value);

    /// <summary>`CostDItem`'s line, split. The gap in front of the figure is padding.</summary>
    public static Line PriceLine(string label, int price) =>
        new(WithPrice(label, price), $"${price}");

    /// <summary>Socket City's and Z-Mart's own format, split (res 208 / 211).</summary>
    public static Line ApplianceLine(string label, int price) =>
        new(AppliancePrice(label, price), price < 1000 ? $"${price,3}" : $"${price,4}");

    /// <summary>`JobDItem`'s line, split. Under $10 the format spends a second space.</summary>
    public static Line WageLine(string label, int wage) =>
        new(WithWage(label, wage), $"${wage} Hr.");

    /// <summary>
    /// Black's Market's five own formats, split. The food lines' `.||` is a LEADER plus two
    /// one-pixel blanks and belongs to the padding, not to the figure.
    /// </summary>
    public static Line BlacksMarketLine(string name, string label, int price) =>
        new(BlacksMarketPrice(name, label, price), name switch
        {
            "10 Lottery Tickets" => $"${price,2}",
            "Newspaper"          => $"${price}",
            _                    => price < 100 ? $"${price,2}" : $"${price,3}",
        });
}
