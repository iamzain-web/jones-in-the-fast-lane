using System;
using System.Collections.Generic;

namespace Jones.Core.Sci;

/// <summary>
/// What kind of accelerator an item declares, from the escape that follows the backtick.
/// </summary>
public enum SciMenuAccelerator
{
    /// <summary>No backtick block — a separator, or an item reachable only by pointing at it.</summary>
    None,

    /// <summary><c>`#N</c>. See <see cref="SciMenuItem.FunctionKey"/> for the digit-to-F mapping.</summary>
    Function,

    /// <summary><c>`^X</c>.</summary>
    Ctrl,
}

/// <summary>
/// One line of a menu, with the id the interpreter gives it.
/// </summary>
/// <param name="Id">
/// <c>(menu &lt;&lt; 8) | item</c>, both counted from 1 — the number every
/// <c>SetMenu</c>/<c>GetMenu</c> call and every branch of <c>MenuBar::handleEvent</c> uses.
/// `Menu.sc:132-136` names 769, 513, 515, 1025 and 1026, and they land on Delete Current
/// Player, Save Game, Restore Game, Statistics and Goals respectively, which is the
/// arithmetic confirmed against the script.
/// </param>
/// <param name="Text">
/// Everything before the backtick, verbatim, INCLUDING trailing spaces — `Set Save
/// Directory ` and `Graphics Detail Level ` both carry one and `Save Game` does not, and
/// per CLAUDE.md §2 these come from the raw script bytes (script 997 at 0x0A13..0x0B0F),
/// not from the decompiled listing.
/// </param>
/// <param name="IsSeparator">
/// The <c>--!</c> line. It still consumes an item number, which is why Quit is 517 and not
/// 516 (`Menu.sc:211`).
/// </param>
/// <param name="FunctionKey">
/// The F number for <see cref="SciMenuAccelerator.Function"/>: the digit as written, except
/// that <c>`#0</c> is F10. `Menu.sc:122` binds `#0` to About JONES and text 997[6] lists it
/// as "F10- About Jones", which settles it.
/// </param>
/// <param name="CtrlKey">The letter for <see cref="SciMenuAccelerator.Ctrl"/>, upper case.</param>
public sealed record SciMenuItem(
    int Id,
    string Text,
    bool IsSeparator,
    SciMenuAccelerator Accelerator,
    int FunctionKey,
    char CtrlKey);

/// <summary>One <c>AddMenu</c> call: a bar title and the items under it.</summary>
/// <param name="Number">The menu's position on the bar, counted from 1.</param>
/// <param name="Title">
/// The title string verbatim, spaces and all. The first menu's is a space, character
/// <c>0x01</c> and a space — glyph 1 of font 0, a 9x8 shape, not a letter.
/// </param>
public sealed record SciMenu(int Number, string Title, IReadOnlyList<SciMenuItem> Items);

/// <summary>
/// The game's menu bar, exactly as <c>MenuBar::init</c> builds it (`Menu.sc:121-137`).
///
/// <para>
/// WHY THIS IS HERE AND NOT IN THE VIEW. Save, Restore, Restart, Help, About and Quit were
/// keyboard-only in this port, which makes them unreachable on a head with no keyboard —
/// and the game already has the answer: the function keys are ACCELERATORS for items on a
/// menu bar the port simply had not built. Text 997[7] says so itself: "Esc   - Menu
/// Bar/Pauses Game". Putting the structure in the core keeps the four strings in one place,
/// lets the tests measure them against the script, and leaves the view with nothing to do
/// but draw what it is given.
/// </para>
///
/// <para>
/// THE STRINGS ARE THE SCRIPT'S, CHARACTER FOR CHARACTER. They were read out of
/// `assets/raw/script/997.script` rather than the decompiled listing, per CLAUDE.md §2 —
/// the listing renders a space inside a string literal as <c>_</c>, and three of these
/// items end in one.
/// </para>
///
/// <para>
/// THIS IS THE CD BUILD'S BAR. The floppy's Options menu is a different list
/// (`jones-dos-1.000.060/src/Menu.sc:126-129`): it opens with "Change Reading Speed `^R"
/// and ends with "Turn Music Off `#2" and "Turn Sound Effects Off `#3", none of which the
/// CD build has. That is why the CD's own help text, 997[6], names no F2 and no F3, and why
/// this bar carries neither.
/// </para>
/// </summary>
public static class SciMenuBar
{
    /// <summary>
    /// The separator line. `Menu.sc:125` and `:129` each contain one.
    /// </summary>
    public const string SeparatorMark = "--!";

    // The four AddMenu calls, `Menu.sc:121-131`, raw script 997 at 0x0A13, 0x0A2C/0x0A33,
    // 0x0A80/0x0A8A and 0x0B06/0x0B0F. Nothing here is retyped from the listing.

    /// <summary>`(AddMenu { \01 } …)` — a space, glyph 1 of font 0, a space.</summary>
    public const string AboutTitle = " \u0001 ";

    private const string AboutItems = "About JONES `#0:Help `#1";

    public const string GameTitle = " Game ";

    private const string GameItems =
        "Save Game`#5:Set Save Directory `^Y:Restore Game`#7:--!:Quit `^Q:Restart `#9";

    public const string OptionsTitle = " Options ";

    private const string OptionsItems =
        "Delete Current Player `^Z:Change Animation Speed `^S:Graphics Detail Level `^T :--!:" +
        "Change Volume `^V:Turn Messages Off `#8";

    public const string StatusTitle = " Status ";

    private const string StatusItems = "Statistics`#4:Goals`#6";

    // The ids the rest of the port refers to by name. Every one of them appears verbatim in
    // a SetMenu, a GetMenu or a switch branch of `MenuBar::handleEvent`.
    public const int AboutId = 257;              // Menu.sc:172
    public const int HelpId = 258;               // Menu.sc:191
    public const int SaveGameId = 513;           // Menu.sc:218
    public const int SetSaveDirectoryId = 514;   // Menu.sc:227
    public const int RestoreGameId = 515;        // Menu.sc:231
    public const int GameSeparatorId = 516;      // the --! at Menu.sc:125
    public const int QuitId = 517;               // Menu.sc:211
    public const int RestartId = 518;            // Menu.sc:198
    public const int DeletePlayerId = 769;       // Menu.sc:240
    public const int AnimationSpeedId = 770;     // Menu.sc:293
    public const int GraphicsDetailId = 771;     // Menu.sc:362
    public const int OptionsSeparatorId = 772;   // the --! at Menu.sc:129
    public const int VolumeId = 773;             // Menu.sc:319
    public const int TurnMessagesId = 774;       // Menu.sc:335, and proc997_3's SetMenu … 110
    public const int StatisticsId = 1025;        // Menu.sc:384
    public const int GoalsId = 1026;             // Menu.sc:390

    /// <summary>
    /// The five items <c>MenuBar::init</c> switches OFF the moment it has built the bar
    /// (`Menu.sc:132-136`), and the same five `room1.sc:1296-1300` switches back on when the
    /// town board comes up. There is no other list: everything else on the bar is live from
    /// the first frame.
    ///
    /// <para>
    /// 513 alone moves again during play — `proc0_7` and `proc0_8` (`Main.sc:985-991`) take
    /// Save off and put it back around the turn-start prints — and `Menu.sc:278-280` drops
    /// 769 for good once deleting a player would leave only one.
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> DisabledUntilTheBoard { get; } =
        [DeletePlayerId, SaveGameId, RestoreGameId, StatisticsId, GoalsId];

    /// <summary>The bar, in the order <c>AddMenu</c> is called.</summary>
    public static IReadOnlyList<SciMenu> Build() =>
    [
        Parse(1, AboutTitle, AboutItems),
        Parse(2, GameTitle, GameItems),
        Parse(3, OptionsTitle, OptionsItems),
        Parse(4, StatusTitle, StatusItems),
    ];

    /// <summary>
    /// One <c>AddMenu</c> call.
    ///
    /// <para>
    /// The item string's grammar, taken from the four strings themselves: items are
    /// separated by <c>:</c>; <c>--!</c> is a separator; a backtick introduces the
    /// accelerator, <c>^</c> then a letter for Ctrl and <c>#</c> then a digit for a function
    /// key. The text is whatever precedes the backtick.
    /// </para>
    ///
    /// <para>
    /// NOTHING IS DERIVED FROM THE ACCELERATOR FOR DISPLAY. These strings carry no
    /// right-hand column — SCI's own convention for one is an <c>=</c> in the item text and
    /// not one of Jones's items has it — so whether the 1990 interpreter drew "F5" beside
    /// "Save Game" cannot be settled from the scripts, and a port that drew it anyway would
    /// be inventing user-visible text (CLAUDE.md §1). The keys are listed where the game
    /// itself lists them, in text 997[6] and 997[7], which Help puts on screen.
    /// </para>
    /// </summary>
    internal static SciMenu Parse(int number, string title, string items)
    {
        var parsed = new List<SciMenuItem>();
        var pieces = items.Split(':');

        for (var i = 0; i < pieces.Length; i++)
        {
            var id = (number << 8) | (i + 1);
            parsed.Add(ParseItem(id, pieces[i]));
        }

        return new SciMenu(number, title, parsed);
    }

    private static SciMenuItem ParseItem(int id, string piece)
    {
        if (piece == SeparatorMark)
            return new SciMenuItem(id, "", true, SciMenuAccelerator.None, 0, '\0');

        var tick = piece.IndexOf('`');
        if (tick < 0)
            return new SciMenuItem(id, piece, false, SciMenuAccelerator.None, 0, '\0');

        var text = piece[..tick];
        var block = piece[(tick + 1)..];

        if (block.Length >= 2 && block[0] == '^')
            return new SciMenuItem(id, text, false, SciMenuAccelerator.Ctrl, 0,
                                   char.ToUpperInvariant(block[1]));

        if (block.Length >= 2 && block[0] == '#' && char.IsDigit(block[1]))
        {
            // `#0` is F10. Nine function keys fit in one digit; the tenth wraps to zero.
            var digit = block[1] - '0';
            return new SciMenuItem(id, text, false, SciMenuAccelerator.Function,
                                   digit == 0 ? 10 : digit, '\0');
        }

        return new SciMenuItem(id, text, false, SciMenuAccelerator.None, 0, '\0');
    }
}
