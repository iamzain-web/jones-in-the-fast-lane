using System.Collections.Generic;
using Jones.Core.Model;

namespace Jones.App;

/// <summary>
/// Exact on-screen positions for each location's interface, read from the instance
/// properties in that location's script. Coordinates are relative to the dialog origin
/// at (69,44).
///
/// Only Z-Mart lays its items out at runtime (it shows a random 6 of 18, so it has to).
/// Every other location has fixed coordinates per item, which is why applying Z-Mart's
/// formula everywhere produced the staggered mess.
/// </summary>
public static class StoreLayout
{
    /// <param name="ItemId">Inventory id, or 0 where the line is not an inventory item.</param>
    /// <param name="Key">
    /// The instance's own <c>key</c> â€” its keyboard accelerator, and the handle the Jones AI
    /// presses it by. <see cref="SciKey"/> establishes what the numbers mean. Note that each
    /// list numbers its lines in declaration order but steps over 9 (Tab) and 13 (Return),
    /// which is why Socket City's ninth item is 10 and not 9.
    /// </param>
    public sealed record Slot(string Label, int ItemId, int Left, int Top, int Key = 0);

    /// <summary>Entering ANY location costs 2 hours â€” every script calls `gTimeKeep doit: 2`.</summary>
    public const int EnterCost = 2;

    /// <summary>Applying for a job costs 4 hours (`JobDItem` visitTime 4).</summary>
    public const int JobApplicationCost = 4;

    // Buttons shared across the shops. workButton's x varies by a few pixels per store.
    public const int ExitLeft = 143, ExitTop = 108;
    public const int WorkLeft = 75, WorkTop = 108;

    /// <summary>
    /// Socket City (`appliance.sc:185-488`, script 208), in the script's own declaration
    /// order. Three lines start at nsLeft 77 (nsTop 31/39/47, 8px apart), the other six
    /// at nsLeft 10 (nsTop 60..100, 8px apart) â€” nothing like Z-Mart's stagger.
    ///
    /// Alignment check: every label measures 76px in font 10, and `doFormat`
    /// (`appliance.sc:197-203`) picks res 208,0 "%s.||$%3d" under $1000 and 208,1
    /// "%s$%4d" at or above it. Both land the price's right edge at x+36 from the
    /// label's left, so the right column ends at 179 and the left at 112 regardless of
    /// whether the price has three digits or four.
    /// </summary>
    public static readonly Slot[] SocketCity =
    [
        new("Refrigerator", ItemIds.Refrigerator, 77, 31, 1),
        new("Freezer",      ItemIds.Freezer,      77, 39, 2),
        new("Stove",        ItemIds.Stove,        77, 47, 3),
        new("Color TV",     ItemIds.ColorTV,      10, 60, 4),
        new("Vcr",          ItemIds.Vcr,          10, 68, 5),
        new("Stereo",       ItemIds.Stereo,       10, 76, 6),
        new("Microwave",    ItemIds.Microwave,    10, 84, 7),
        new("Hot Tub",      ItemIds.HotTub,       10, 92, 8),
        new("Computer",     ItemIds.Computer,     10, 100, 10),
    ];

    /// <summary>
    /// QT Clothing (`clothing.sc:169-243`, script 209): one column at nsLeft 84, nsTop
    /// 63/78/93 â€” 15px apart, the widest spacing of any shop. The instance named
    /// `leisureSuit` carries the text "Dress Clothes"; that is the source's own naming,
    /// not a slip.
    ///
    /// Alignment check: all three labels measure 70px, and clothing.sc declares no
    /// `doFormat`, so CostDItem's default runs (`WButton.sc:185-195`) â€” res 104,1
    /// "%s $%d" under $100 and 104,2 "%s$%d" at or above. Every line ends at 175.
    /// </summary>
    public static readonly Slot[] QtClothing =
    [
        new("Business Suit",  ItemIds.BusinessSuit,  84, 63, 2),
        new("Leisure Suit",   ItemIds.DressClothes,  84, 78, 3),
        new("Casual Clothes", ItemIds.CasualClothes, 84, 93, 4),
    ];

    /// <summary>
    /// Black's Market (`market.sc:194-366`, script 203). Lottery tickets are sold TEN at
    /// a time for a fixed $10 (`fixedPrice 1`, `price 10`, `units 10`), and the newspaper
    /// costs a fixed $1 plus a further hour of your time (`visitTime 1`).
    ///
    /// Alignment check: this store is the one place where the label widths are NOT all
    /// equal, and that is the original, not a transcription slip. Each item uses a
    /// different `doFormat` (`market.sc:207-213, 307-309, 337-339`): res 203,0
    /// "%s.||$%2d" / 203,1 "%s$%3d" for food, 203,2 "%s$%2d" for the lottery, 203,3
    /// "%s$%d" for the paper. Measured in font 10 the labels are 85/85/84/90/94px and the
    /// lottery line is nudged one pixel left (nsLeft 74, not 75) â€” which puts the right
    /// column's price right edge at 180 for all three of its rows and the left column's
    /// at 112 for both of its rows. Only "Food For 4 Weeks.." falls 1px short of its
    /// neighbours' stop; that 1px is in the shipped game.
    /// </summary>
    public static readonly Slot[] BlacksMarket =
    [
        new("Food For 1 Week",    ItemIds.FreshFood,       6, 31, 1),
        new("Food For 2 Weeks",   ItemIds.FreshFood,       6, 46, 2),
        new("Food For 4 Weeks",   ItemIds.FreshFood,      75, 64, 3),
        new("10 Lottery Tickets", ItemIds.LotteryTickets, 74, 79, 4),
        new("Newspaper",          ItemIds.Newspaper,      75, 94, 5),
    ];

    /// <summary>
    /// Monolith Burgers (`fastFood.sc:170-293`, script 210): two lines at nsLeft 10
    /// (nsTop 33/45) and four at nsLeft 75 (nsTop 63/74/86/97). The spacing is uneven on
    /// purpose â€” 12, then 11/12/11.
    ///
    /// Alignment check: labels measure 79px in the left column and 81px in the right, and
    /// fastFood.sc declares no `doFormat`, so CostDItem's default applies. Left column
    /// ends at 110, right column at 177.
    ///
    /// NOTE the order: the script declares hamburgers, cheeseburgers, chicken, fries,
    /// shakes, colas, but `Catalogue.MonolithBurgers` stores Astro Chicken first. See
    /// <see cref="ScriptOrder"/>.
    /// </summary>
    public static readonly Slot[] MonolithBurgers =
    [
        new("Hamburgers",    ItemIds.Hamburgers,    10, 33, 1),
        new("Cheeseburger",  ItemIds.Cheeseburgers, 10, 45, 2),
        new("Astro Chicken", ItemIds.AstroChicken,  75, 63, 3),
        new("Fries",         ItemIds.Fries,         75, 74, 4),
        new("Shakes",        ItemIds.Shakes,        75, 86, 5),
        new("Colas",         ItemIds.Colas,         75, 97, 6),
    ];

    /// <summary>
    /// The order each script DECLARES its CostDItem instances in, named by the
    /// <c>Catalogue</c> entry that corresponds to each. The slot tables above follow the
    /// script, so the stock list has to be put into the same order before the slots are
    /// handed out positionally.
    ///
    /// Three of the four already agree with the catalogue; Monolith does not
    /// (`fastFood.sc:80-92` adds hamburgers first, `Catalogue.MonolithBurgers` lists
    /// Astro Chicken first), which silently slid every Monolith line onto the wrong row.
    /// </summary>
    public static string[]? ScriptOrder(LocationId id) => id switch
    {
        // appliance.sc:93-108
        LocationId.SocketCity =>
        [
            "Refrigerator", "Freezer", "Stove", "Color TV", "Vcr",
            "Stereo", "Microwave", "Hot Tub", "Computer",
        ],
        // clothing.sc:83-92 (businessSuit, leisureSuit = "Dress Clothes", casualClothes)
        LocationId.QtClothing => ["Business Suit", "Leisure Suit", "Casual Clothes"],
        // market.sc:99-110, all five lines.
        LocationId.BlacksMarket =>
        [
            "Food For 1 Week", "Food For 2 Weeks", "Food For 4 Weeks",
            "10 Lottery Tickets", "Newspaper",
        ],
        // fastFood.sc:80-92
        LocationId.MonolithBurgers =>
        [
            "Hamburgers", "Cheeseburgers", "Astro Chicken", "Fries", "Shakes", "Colas",
        ],
        // Z-Mart picks six of eighteen at random, so its order is decided at runtime.
        _ => null,
    };

    /// <summary>
    /// The Rent Office (`rentOffice.sc`). A Security apartment rents at base 475 against
    /// Low-Cost's 325 â€” the price of never being robbed by Wild Willy.
    ///
    /// The labels are the instances' own `text`, byte for byte from raw script 201 at
    /// 0x11F3, 0x1213, 0x1236, 0x1267 and 0x128E. Every one of them carries a trailing
    /// full stop and/or run of spaces that the port had tidied away; the spaces are the
    /// padding that puts the price column in one place (2, 7, 2, 2 and 2 of them).
    /// </summary>
    public static readonly Slot[] RentOffice =
    [
        new("Pay rent for 1 month.  ",   0, 20, 60, 1),
        new("Ask For More Time.       ", 0, 20, 70, 2),
        new("Rent Low-Cost Apartment  ", 0, 20, 80, 3),
        new("Rent Security Apartment  ", 0, 20, 90, 4),
        new("Pay Garnishment Balance  ", 0, 20, 100, 5),
    ];

    /// <summary>
    /// The Bank (`bank.sc`). Its work button sits at 77, not 75.
    /// `Deposit  ` and `Withdraw  ` keep the two trailing spaces raw script 204 gives them
    /// at 0x10A9 and 0x10BB; the other three carry none.
    /// </summary>
    public static readonly Slot[] Bank =
    [
        new("Deposit  ",      0, 97, 35, 1),
        new("Withdraw  ",     0, 92, 50, 2),
        new("Loan Payment",   0, 94, 65, 3),
        new("Apply For Loan", 0, 93, 80, 4),
        new("See The Broker", 0, 95, 95, 5),
    ];

    /// <summary>
    /// The Employment Office (`employment.sc`) â€” a menu of workplaces, not of jobs.
    /// Choosing one opens that workplace's own job list.
    /// </summary>
    /// <remarks>
    /// The `key` column is each instance's own and is NOT in row order — the nine were
    /// numbered by some other logic and 1, 3, 4, 5, 7, 8, 10, 11, 12 is what they got
    /// (`employment.sc`: theRentOffice 1, theMarket 3, theBank 4, theFactory 5,
    /// theUniversity 7, applianceStore 8, fastFoodStore 10, discountStore 11,
    /// clothingStore 12). 9 is missing because it is Tab; see <see cref="SciKey"/>.
    /// </remarks>
    /// <remarks>
    /// RENAMED, DELIBERATELY. The university's row reads <c>Open Door University</c>, not the
    /// resource's <c>Hi-Tech University</c> (raw script 206 @0x117A, `employment.sc:462`).
    /// This is the ONE user-requested rename, recorded in `PARITY.md` under Deliberate
    /// deviations; it is not a fidelity fix and nothing else in the port may follow it.
    ///
    /// <para>
    /// `nsLeft` moves 20 → 12 with it, and that is measured, not chosen. These nine `nsLeft`
    /// values are not a left margin — each was picked so its label sits on ONE optical centre.
    /// In font 10 (`assets/raw/font/10.font`) the nine labels measure 72, 74, 48, 92, 77, 35,
    /// 20, 65 and 47 pixels, and `left + width/2` comes out 59.0, 59.0, 59.0, 58.0, 58.5,
    /// 58.5, 58.0, 58.5, 58.5 — the column centre is 58.5±0.5. `Open Door University` is 92px,
    /// which is exactly `Socket City Appliance`'s width, so it takes exactly that row's
    /// `nsLeft` of 12 and stops at x 104, the same right edge as the longest line the original
    /// already drew. Nothing sits to the right of this column; the panel behind it ends at 180
    /// (`ColumnStopTests.Quicksand_never_widens_the_employer_list`), so 104 clears it by 76px.
    /// </para>
    /// </remarks>
    public static readonly (string Label, Workplace Place, int Left, int Top, int Key)[] Employment =
    [
        ("Z-Mart Discount",        Workplace.ZMart,           23, 37,  11),
        ("Monolith Burgers",       Workplace.MonolithBurgers, 22, 45,  10),
        ("QT Clothing",            Workplace.QtClothing,      35, 53,  12),
        ("Socket City Appliance",  Workplace.SocketCity,      12, 61,   8),
        ("Open Door University",   Workplace.HiTechU,         12, 69,   7),
        ("Factory",                Workplace.Factory,         41, 77,   5),
        ("Bank",                   Workplace.Bank,            48, 85,   4),
        ("Black's Market",         Workplace.BlacksMarket,    26, 93,   3),
        ("Rent Office",            Workplace.RentOffice,      35, 101,  1),
    ];

    /// <summary>
    /// Hi-Tech U stacks available courses UPWARD from the bottom: nsLeft 76, and
    /// nsTop = 108 - 14 * n for the nth course added. Only four rows fit.
    /// </summary>
    public static (int Left, int Top) CourseSlot(int index) => (76, 108 - 14 * (index + 1));

    public const int EnrollLeft = 105, EnrollTop = 108;

    /// <summary>Relax button at the apartments: view 250 loop 3, and it costs 6 hours.</summary>
    public const int RelaxLeft = 9, RelaxTop = 108;

    // ------------------------------------------------------------------
    // The two apartments (`lowcost.sc` script 200, `security.sc` script 202)
    // ------------------------------------------------------------------

    /// <summary>
    /// The apartment backdrop, chosen at run time by whether the visiting player LIVES
    /// here â€” the one `background` in the game whose `view:` is assigned rather than
    /// declared:
    ///
    /// <code>
    /// lowcost.sc:47   (background view: (if (== (global302 livesAt:) 0) 700 else 699))
    /// security.sc:47  (background view: (if (== (global302 livesAt:) 2) 702 else 698))
    /// </code>
    ///
    /// <paramref name="livesAt"/> is in the SOURCE's encoding â€” 0 for Low-Cost Housing and
    /// 2 for Le Security Apartments, not the port's 0/1. Returns 0 for any other location.
    /// Views 699 and 698 are the "you are only visiting" rooms and carry a single cel each.
    /// </summary>
    public static int ApartmentBackground(LocationId id, int livesAt) => id switch
    {
        LocationId.LowCostHousing     => livesAt == 0 ? 700 : 699,
        LocationId.SecurityApartments => livesAt == 2 ? 702 : 698,
        _                             => 0,
    };

    /// <summary>
    /// One furnishing DIcon inside an apartment.
    /// </summary>
    /// <param name="DurableId">
    /// The durable that must be held for it to be drawn (`objectAtIndexQuan:`), or 0 where
    /// the script adds it unconditionally.
    /// </param>
    public sealed record Furnishing(int View, int Loop, int Cel, int Left, int Top, int DurableId = 0);

    /// <summary>
    /// Low-Cost Housing's three fixtures (`lowcost.sc:145-171`), in the order the script
    /// adds them (`lowcost.sc:52`: `theSaying theChair theStool`), which is painter's order.
    /// All three are cels of the same view 700 that draws the room itself.
    ///
    /// They are added only inside `(if (== global414 0))` (`lowcost.sc:51`) â€” global414 is
    /// the Wild Willy robbery flag. It is set while the robbery is being resolved
    /// (`startTrn.sc:338`) and cleared again at `startTrn.sc:355`, in the same block, before
    /// the turn proper begins, so it is ALWAYS 0 by the time this dialog can open. The port
    /// models the robbery as a transient `TurnStartEvent.Robbed` for exactly that reason and
    /// so has nothing to test here; the test is quoted rather than invented.
    /// </summary>
    public static readonly Furnishing[] LowCostFixtures =
    [
        new(700, 0, 2,  49, 28),  // theSaying â€” `lowcost.sc:155-162`
        new(700, 0, 1,  32, 57),  // theChair  â€” `lowcost.sc:145-153`
        new(700, 0, 3, 108, 81),  // theStool  â€” `lowcost.sc:164-171`
    ];

    /// <summary>
    /// Le Security Apartments' possessions (`security.sc:153-189`), in the script's own
    /// `add:` order (`security.sc:51-62`: colour TV, stereo, books, VCR) â€” which is NOT the
    /// order they are declared in, and is painter's order, so the stereo at (121,43) lies
    /// over the television at (137,34). Each appears only while that durable is held, which
    /// is what makes this room fill up as the player buys things.
    ///
    /// `theStereo` declares `loop 1` (`security.sc:167`), but view 702 holds exactly ONE
    /// loop (its resource header gives loopCount 1), so SCI clamps the request to loop 0 and
    /// the stereo draws from the same loop as everything else. Passing 1 through here would
    /// simply find no cel and draw nothing.
    ///
    /// `theBooks` keys off durable 31 alone (`security.sc:57`) â€” the first of
    /// <see cref="ItemIds.ReferenceBooks"/>, not the whole set of three.
    /// </summary>
    public static readonly Furnishing[] SecurityPossessions =
    [
        new(702, 0, 1, 137, 34, ItemIds.ColorTV),  // theColorTV â€” `security.sc:153-160`
        new(702, 0, 4, 121, 43, ItemIds.Stereo),   // theStereo  â€” `security.sc:162-170`
        new(702, 0, 3, 120, 15, 31),               // theBooks   â€” `security.sc:172-180`
        new(702, 0, 2, 137, 64, ItemIds.Vcr),      // theVCR     â€” `security.sc:182-189`
    ];

    /// <summary>The furnishings for an apartment, or null anywhere else.</summary>
    public static Furnishing[]? Furnishings(LocationId id) => id switch
    {
        LocationId.LowCostHousing     => LowCostFixtures,
        LocationId.SecurityApartments => SecurityPossessions,
        _                             => null,
    };

    /// <summary>
    /// The cash display is a calculator sprite on the BOARD, not text in the shop
    /// window: `calc` at room1.sc:1419 is view 0 loop 4 at absolute (252,160).
    ///
    /// IT DOES NOT SHOW `cash - 1`; this comment used to say so. `(gCalc â€¦ value: (- cash 1)
    /// draw:)`, which all thirteen location scripts run on entry, is a CACHE INVALIDATION:
    /// `value` is the last amount drawn and `calc::doit` (`room1.sc:1430-1456`) opens with
    /// `(if (or (!= value cash) â€¦)` and immediately does `(= value cash)` before the
    /// Display. Setting it one short of the real figure is how the script forces a redraw.
    /// </summary>
    public const int CalcLeft = 252, CalcTop = 160;
    public const int CalcView = 0, CalcLoop = 4;

    /// <summary>
    /// The readout, `room1.sc:1437-1455` â€” and every one of these was wrong before.
    ///
    /// <code>
    /// (Display (Format @temp0 1 3 (proc115_0 global456 value))   ; text 1[3] = "%6s "
    ///     dsCOORD (+ nsLeft 22) (+ nsTop 6)
    ///     dsCOLOR 0
    ///     dsBACKGROUND (cond (global535 101) (global552 15) (else 7))
    ///     dsFONT 14)
    /// </code>
    ///
    /// So: LEFT-aligned from (274,166) â€” not right-aligned to a panel edge â€” in FONT 14,
    /// colour index 0, on an opaque band of index 101. The format is a six-character
    /// right-aligned field plus a trailing space, which is where the alignment actually
    /// comes from.
    ///
    /// The distinction matters because font 14's space is 4px and its digits are 5px, so
    /// padding to six characters does NOT line the right edge up: the readout's right edge
    /// creeps right as the figure gains digits. The port's true right-alignment hid a
    /// wobble that is in the shipped game.
    /// </summary>
    public const int CalcDisplayLeft = 22, CalcDisplayTop = 6;

    /// <summary>text 1[3], `assets/raw/text/1.text` â€” six-wide field, then a space.</summary>
    public const int CalcDisplayWidth = 6;

    /// <summary>`dsFONT 14`, `dsCOLOR 0`, `dsBACKGROUND 101` (the VGA arm of the cond).</summary>
    public const int CalcFont = 14, CalcColour = 0, CalcBackground = 101;

    /// <summary>
    /// Job lists (scripts 216â€“224). Every job sits at nsLeft 25; only the starting row
    /// and the spacing differ, and the Factory tightens to 8px to fit nine jobs.
    /// The header is a WButton above them.
    /// </summary>
    public static (string Header, int HeaderLeft, int HeaderTop, int FirstTop, int Spacing)
        JobList(Workplace w) => w switch
    {
        Workplace.ZMart           => ("Discount Store Jobs Available:", 22, 35, 50, 10),
        Workplace.MonolithBurgers => ("Monolith's Jobs Available:",     33, 35, 50, 10),
        Workplace.QtClothing      => ("QT Clothing Jobs Available:",    32, 35, 50, 10),
        Workplace.SocketCity      => ("Socket City Jobs Available:",    32, 35, 50, 10),
        Workplace.HiTechU         => ("University Jobs Available:",     34, 35, 50, 10),
        Workplace.Factory         => ("Factory Jobs Available:",        40, 19, 30,  8),
        Workplace.Bank            => ("Bank Jobs Available:",           45, 30, 45, 10),
        Workplace.BlacksMarket    => ("Black's Market Jobs Available:", 22, 30, 45, 10),
        _                         => ("Rent Office Jobs Available:",    33, 45, 60, 10),
    };

    public const int JobLeft = 25;

    /// <summary>
    /// Each job's own <c>key</c>, by its unique <c>jobNum</c>. Read straight from the nine
    /// job scripts; the numbering is per-list and per-declaration-order, which is why three
    /// different jobs are `key 1` and why the Factory's ninth is 10 rather than 9
    /// (9 is Tab — <see cref="SciKey"/>).
    /// </summary>
    public static int JobKey(int jobNum) => jobNum switch
    {
        1  => 1,  2  => 2,  3  => 3,            // discountJobs:  clerk, assistManager, manager
        4  => 1,  5  => 2,  6  => 3,  7 => 4,   // fastFoodJobs:  cook, clerk, assistManager, manager
        8  => 1,  9  => 2,  10 => 3, 38 => 4,   // clothingJobs:  salesPerson, assistManager, manager, janitor
        11 => 1,  12 => 2,  13 => 3, 39 => 4,   // applianceJobs: salesperson, electronicsRepair, manager, clerk
        14 => 1,  15 => 2,  16 => 3,            // universityJobs
        18 => 1,  17 => 2,  19 => 3, 20 => 4,   // factoryJobs:   janitor, assemblyWorker, secretary, …
        21 => 5,  22 => 6,  23 => 7, 24 => 8,
        25 => 10,                               // factoryJobs generalManager — 9 is Tab
        26 => 1,  27 => 2,  28 => 3, 29 => 4, 30 => 5,   // bankJobs
        31 => 1,  32 => 2,  33 => 3, 34 => 4, 35 => 5,   // marketJobs
        36 => 1,  37 => 2,                      // rentJobs
        _  => 0,
    };

    /// <summary>
    /// The three colours every <c>WButton</c> is drawn with, as PALETTE INDICES â€”
    /// <c>textColor</c>, <c>shadowColor</c> and the press colour <c>flashColor</c>.
    /// <c>MenuLineVm</c> has the draw order; <see cref="SciPalette"/> has the RGB.
    /// </summary>
    /// <param name="Text">
    /// <c>textColor</c>. 0 on the <c>WButton</c> class (`WButton.sc:34`).
    /// </param>
    /// <param name="Shadow">
    /// <c>shadowColor</c>. 6 on the class (`WButton.sc:35`); 0 would mean no shadow, and
    /// no instance in the game declares 0.
    /// </param>
    /// <param name="Flash">
    /// <c>flashColor</c>. 100 on the class (`WButton.sc:36`).
    /// </param>
    public readonly record struct LineColours(int Text, int Shadow, int Flash);

    /// <summary>
    /// Every screen that overrides the <c>WButton</c> defaults, from its own script. The
    /// pair is uniform within a screen â€” all nine Socket City items carry the same two, all
    /// eighteen Z-Mart items carry the same two, and so on.
    ///
    /// Counted from the scripts: 109 <c>shadowColor</c> declarations and 43
    /// <c>textColor</c>, which is every clickable line in the game.
    /// </summary>
    public static LineColours ColoursFor(LocationId id) => id switch
    {
        // appliance.sc:191-192 and its eight siblings â€” 9 instances, 39 / 115.
        LocationId.SocketCity       => new(39, 115, 100),
        // discount.sc:273-274 and seventeen more â€” 18 instances, 26 / 102.
        LocationId.ZMart            => new(26, 102, 100),
        // fastFood.sc:176-177 Ã—6 â€” 27 / 116.
        LocationId.MonolithBurgers  => new(27, 116, 100),
        // market.sc:200-201 Ã—5 â€” 37 / 112.
        LocationId.BlacksMarket     => new(37, 112, 100),
        // clothing.sc:175-176, :201-202, :227-228 â€” shadow 89, textColor NOT declared (so
        // the class's 0), and flashColor 255 on all three. The only instances in the game
        // that override the press colour.
        LocationId.QtClothing       => new(0, 89, 255),
        // employment.sc:387 and its eight siblings â€” the nine workplace lines, shadow 80.
        LocationId.EmploymentOffice => new(0, 80, 100),
        // pawnShop.sc:638-639, :792-793, :803-804, :812-813 â€” 26 / 65, on the three list
        // captions and on `aRedeemableItem`, which is the redeem and buy lists.
        LocationId.PawnShop         => new(26, 65, 100),
        // bank.sc, rentOffice.sc, university.sc, factory.sc and the two apartments declare
        // NEITHER, so their lines take the WButton class defaults: black text on a light
        // grey shadow. Verified by search â€” those files contain no colour declaration.
        _                           => new(0, 6, 100),
    };

    /// <summary>
    /// The broker's holdings labels (`broker.sc:339-343` and five more): WButtons at
    /// nsLeft 142, <c>state 288</c> â€” bit 0 clear, so they are captions â€” with shadow 93
    /// and the class's textColor 0.
    /// </summary>
    public static LineColours BrokerColours => new(0, 93, 100);

    /// <summary>
    /// The nine job lists (scripts 216-224). Every one of them declares shadow 107 on every
    /// line AND on its `jobsAvailable` header, and none declares a textColor
    /// (`applianceJobs.sc:108-179`, `factoryJobs.sc`, â€¦) â€” 107 is the single most common
    /// shadow colour in the game, 48 of the 109 declarations.
    /// </summary>
    public static LineColours JobListColours => new(0, 107, 100);

    // There is no explicit wage column. The original aligns the wages entirely through
    // the padding baked into each job's own `text` string â€” the trailing underscores
    // (3px dashes) and `|` characters (1px blanks) â€” combined with the two-space format
    // used under $10. Porting those strings verbatim aligns the column for free, so the
    // JobWageRight constant that used to sit here was invented and did nothing.

    /// <summary>The job list's own backdrop: view 706 CEL 1, not the office interior.</summary>
    public const int JobListView = 706, JobListCel = 1;

    /// <summary>
    /// Where each location's shopkeeper portrait sits. These are the `theTalker`
    /// instances' own nsLeft/nsTop; the Talker class default is (115, 1), which most
    /// shops inherit, but several override it.
    /// </summary>
    public static (int Left, int Top) TalkerAt(LocationId id) => id switch
    {
        LocationId.SocketCity       => (115, 57),
        LocationId.HiTechU          => (0, 1),
        LocationId.EmploymentOffice => (115, 17),
        LocationId.Bank             => (0, 0),
        LocationId.PawnShop         => (0, 1),
        LocationId.Factory          => (18, 40),
        _                           => (115, 0),
    };

    // ------------------------------------------------------------------
    // The `items` picture panel
    // ------------------------------------------------------------------

    /// <summary>
    /// The `items` DCIcon every shop declares â€” the picture of a product that sits beside
    /// the price list. Read straight off the instance properties:
    ///
    /// <code>
    /// appliance.sc:543  view 708 loop 1               priority 14 cycleSpeed 300  lastCel 8
    /// clothing.sc:298   view 709 loop 1 nsTop 57      priority 14 cycleSpeed 300
    /// discount.sc:797   view 711 loop 1 nsTop 57      priority 14 cycleSpeed 300  lastCel 5
    /// fastFood.sc:371   view 710 loop 1 nsTop 57      priority 14 cycleSpeed 300
    /// market.sc:444     view 703 loop 1 nsTop 57      priority 14 cycleSpeed 300
    /// pawnShop.sc:896   view 708 loop 1 nsTop 56 cel 13          cycleSpeed 300
    /// </code>
    ///
    /// Socket City's declares NO nsTop and NO nsLeft, so its panel sits at the dialog's own
    /// origin (0,0) â€” its three top lines are at nsLeft 77 and the rest start at nsTop 60,
    /// which is exactly the gap the picture fills. Every other shop puts it at nsLeft 0 and
    /// nsTop 57 (56 at the Pawn Shoppe), under the left-hand column.
    ///
    /// It is published as <c>gItems</c> on entry (`appliance.sc:85`, `clothing.sc:74`,
    /// `discount.sc:137`, `fastFood.sc:73`, `market.sc:90`, `pawnShop.sc:432`) and cleared
    /// at `room1.sc:214` / `Game.sc:142`.
    /// </summary>
    /// <param name="FirstCel">The cel the panel opens on (`cel 13` at the Pawn Shoppe).</param>
    /// <param name="LastCel">
    /// The highest cel the idle animation reaches. `DCIcon::lastCel` returns
    /// <c>NumCels - 1</c>, but Socket City overrides it to 8 (`appliance.sc:572-574`)
    /// because view 708 carries the Pawn Shoppe's extra cels too, and Z-Mart overrides it
    /// to 5 (`discount.sc:846-848`) because its cycler indexes the SIX lines on show rather
    /// than the view's cels.
    /// </param>
    /// <param name="Cycles">
    /// False at the Pawn Shoppe, whose `items` declares no `init` and so is never given a
    /// cycler: it sits on cel 13 until a purchase moves it, then `FCue` puts it back.
    /// </param>
    public sealed record ItemsPanel(
        int View, int Loop, int Left, int Top, int FirstCel, int LastCel, bool Cycles = true);

    public static ItemsPanel? ItemsFor(LocationId id) => id switch
    {
        LocationId.SocketCity      => new(708, 1, 0,  0, 0, 8),
        LocationId.QtClothing      => new(709, 1, 0, 57, 0, 2),
        LocationId.ZMart           => new(711, 1, 0, 57, 0, 5),
        LocationId.MonolithBurgers => new(710, 1, 0, 57, 0, 5),
        LocationId.BlacksMarket    => new(703, 1, 0, 57, 0, 4),
        LocationId.PawnShop        => new(708, 1, 0, 56, 13, 13, Cycles: false),
        _                          => null,
    };

    /// <summary>
    /// Every `CostDItem` carries a `celNum` naming its own picture, and `CostDItem::doit`
    /// (`WButton.sc:236-238`) shows it on a SUCCESSFUL purchase â€” the call is inside the
    /// `(= global416 (>= (proc0_11) price))` branch, so failing to afford something leaves
    /// the panel where it was.
    ///
    /// The values are the instances' own, in each script's declaration order; the first
    /// item of every shop leaves `celNum` at its class default of 0, which is why
    /// `refrigerator` (`appliance.sc:185`, `discount.sc:268`), `hamburgers`
    /// (`fastFood.sc:170`), `foodFor1Week` (`market.sc:194`) and `casualClothes`
    /// (`clothing.sc:221`) are not in the celNum grep.
    ///
    /// Keyed on the <c>Catalogue</c> name because Z-Mart's catalogue order is NOT its
    /// script's declaration order, and Z-Mart is the one shop whose lines are picked at
    /// runtime.
    /// </summary>
    public static int ItemCel(LocationId store, string itemName) => store switch
    {
        // appliance.sc:185-466
        LocationId.SocketCity => itemName switch
        {
            "Refrigerator" => 0, "Freezer" => 1, "Stove" => 2, "Color TV" => 3,
            "Vcr" => 4, "Stereo" => 5, "Microwave" => 6, "Hot Tub" => 7,
            "Computer" => 8, _ => 0,
        },

        // clothing.sc:169-233. The business suit is cel 2 and the leisure suit cel 1;
        // casual clothes, declared last, keep the default 0.
        LocationId.QtClothing => itemName switch
        {
            "Business Suit" => 2, "Leisure Suit" => 1, "Casual Clothes" => 0, _ => 0,
        },

        // market.sc:194-334
        LocationId.BlacksMarket => itemName switch
        {
            "Food For 1 Week" => 0, "Food For 2 Weeks" => 1, "Food For 4 Weeks" => 2,
            "10 Lottery Tickets" => 3, "Newspaper" => 4, _ => 0,
        },

        // fastFood.sc:170-282
        LocationId.MonolithBurgers => itemName switch
        {
            "Hamburgers" => 0, "Cheeseburgers" => 1, "Astro Chicken" => 2,
            "Fries" => 3, "Shakes" => 4, "Colas" => 5, _ => 0,
        },

        // discount.sc:268-716. Eighteen lines, so the last two spill into LOOP 2 â€” see
        // ZMartLoop/ZMartCel below.
        LocationId.ZMart => itemName switch
        {
            "Refrigerator" => 0, "Stove" => 1, "Stereo" => 2, "Color TV" => 3,
            "Black & White TV" => 4, "Microwave" => 5, "Vcr" => 6,
            "Casual Clothes" => 7, "Leisure Suit" => 8,
            "Baseball Tickets" => 9, "Theatre Tickets" => 10, "Concert Tickets" => 11,
            "Encyclopedia" => 12, "Dictionary" => 13, "Atlas" => 14,
            "Dog Food" => 15, "8-Track Player" => 16, "Works of Capote" => 17,
            _ => 0,
        },

        _ => 0,
    };

    /// <summary>
    /// Z-Mart's panel splits its eighteen pictures across two loops:
    /// `(self cel: (mod param1 16) loop: (if (&lt; param1 16) 1 else 2))`
    /// (`discount.sc:823-824`, and the same split in its `FS::next`, `discount.sc:84-92`).
    /// </summary>
    public static (int Loop, int Cel) ZMartPicture(int celNum) =>
        celNum < 16 ? (1, celNum) : (2, celNum % 16);

    /// <summary>
    /// The Pawn Shoppe names its picture from the durable itself â€”
    /// `(= celNum (- (theDurable indexNum:) 21))` (`pawnShop.sc:817`, `:222`, `:314`) â€”
    /// and `items::cue` (`pawnShop.sc:920-923`) puts the panel back on cel 13 afterwards.
    /// </summary>
    public const int PawnShopRestCel = 13;
    public static int PawnShopCel(int durableIndexNum) => durableIndexNum - 21;

    public static Slot[]? For(LocationId id) => id switch
    {
        LocationId.SocketCity      => SocketCity,
        LocationId.QtClothing      => QtClothing,
        LocationId.BlacksMarket    => BlacksMarket,
        LocationId.MonolithBurgers => MonolithBurgers,
        LocationId.RentOffice      => RentOffice,
        LocationId.Bank            => Bank,
        _                          => null,
    };
}
