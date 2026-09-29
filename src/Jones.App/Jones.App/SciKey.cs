namespace Jones.App;

/// <summary>
/// WHAT A CONTROL'S <c>key</c> NUMBER IS.
///
/// `key` is declared 151 times across the scripts and only two of its values — 119 and
/// 120 — were implemented. An earlier pass could not establish what the small numbers
/// meant, so this states the evidence before using it.
///
/// <b>The matcher.</b> <c>Item::handleEvent</c> (`Interface.sc:407-420`) fires a control on
/// <c>(and (&amp; evtType evKEYBOARD) (== (event message:) key))</c>. So `key` is compared,
/// raw, against the event's <c>message</c> — the character code of the key that was struck.
/// Nothing translates it on the way in. (This is NOT the <c>KeyMouse</c> system, which
/// steers a cursor between controls with the arrow keys and the joystick and never looks at
/// `key` at all — `Main.sc:993-1017`, and PROPERTIES.md §4 lists its properties.)
///
/// <b>The large values are literal ASCII</b>, and mnemonic: 120 <c>x</c> on all seventeen
/// <c>exitButton</c>s, 119 <c>w</c> on the nine <c>workButton</c>s, 98 <c>b</c> on the
/// broker's Buy (`broker.sc:504-506`). The odd one out is 106, <c>j</c>, on the broker's
/// Sell (`broker.sc:547-549`) — not <c>s</c>, and there is nothing in either tree to explain
/// the choice. It is ported as declared rather than "corrected".
///
/// <b>The small values are the Ctrl-letter codes</b>, Ctrl-A = 1 through Ctrl-X = 24, and
/// the proof is what the scripts REFUSE to use. Every list numbers its controls in
/// declaration order and every list steps over 9 and 13:
///
/// * Socket City: 1,2,3,4,5,6,7,8, then <b>10</b> for the ninth (`appliance.sc`).
/// * The Factory's nine jobs: 1..8, then <b>10</b> (`factoryJobs.sc`).
/// * Hi-Tech U's eleven courses: 2..8, 10,11,12, then <b>14</b> (`university.sc`).
/// * Z-Mart needs eighteen and takes 1-8, 10,11,12, 14, 16,17,18 and then 20, 23, 24 —
///   its 9th, 13th and 15th are bumped out (`discount.sc`).
///
/// 9 is Tab, 13 is Return and 15 is Shift-Tab, and those are exactly the three keys
/// <c>Dialog::handleEvent</c> claims before any control sees them: `Interface.sc:1126`
/// (KEY_TAB → advance), `:1113` (KEY_RETURN → press the selected item) and `:1152`
/// (KEY_SHIFTTAB → retreat). A control keyed 9, 13 or 15 could never fire, and none is.
/// That only makes sense if these numbers are keystrokes.
///
/// <b>`key` is also a handle, not only an accelerator.</b> Two other things read it, which
/// is why the numbering is ordinal rather than mnemonic. The Jones AI presses a control by
/// fabricating an event: <c>(event message: (refrigerator key:))</c> and sixteen more
/// (`appliance.sc:629-686`, `bank.sc:561-612`, `discount.sc:874…`, `clothing.sc:387-417`).
/// And the broker uses it as a list index outright: <c>((global302 investments:) at:
/// (global430 key:))</c> (`broker.sc:523`, `:566`), with gold 1, silver 2, porkBellies 3,
/// blueChipStocks 4, pennyStocks 5.
///
/// <b>What is NOT resolved</b>, and is left unbound rather than guessed: Ctrl-Q, Ctrl-S,
/// Ctrl-T, Ctrl-V, Ctrl-Y and Ctrl-Z (17, 19, 20, 22, 25, 26) are also menu-bar
/// accelerators (`Menu.sc:123-130`). The menu bar is live while a dialog is up
/// (<c>menuBarOK 1</c> on all 35 dialogs, read at `Interface.sc:1008`), so it claims them
/// first — which means Z-Mart's `eightTrack` (key 17) and `leisureSuit` (key 20) are, as
/// shipped, unreachable from the keyboard. <see cref="MenuClaims"/> reproduces that.
/// </summary>
public static class SciKey
{
    /// <summary>
    /// The <c>event message</c> a keystroke produces, or 0 for one that produces none this
    /// matcher would recognise.
    /// </summary>
    /// <param name="key">
    /// The key name as the Avalonia view hands it over — "A".."Z", "F1", "Escape".
    /// </param>
    /// <param name="ctrl">Whether Ctrl was held.</param>
    public static int Message(string key, bool ctrl)
    {
        if (key.Length != 1) return 0;

        var c = char.ToUpperInvariant(key[0]);
        if (c is < 'A' or > 'Z') return 0;

        // Ctrl-A..Ctrl-Z are 1..26; a bare letter is its own lower-case code, which is what
        // 98 / 106 / 119 / 120 are.
        return ctrl ? c - 'A' + 1 : char.ToLowerInvariant(c);
    }

    /// <summary>
    /// The Ctrl-letter codes the menu bar takes before any dialog control sees them:
    /// Ctrl-Q Quit, Ctrl-S Change Animation Speed, Ctrl-T Graphics Detail Level,
    /// Ctrl-V Change Volume, Ctrl-Y Set Save Directory, Ctrl-Z Delete Current Player
    /// (`Menu.sc:123-130`; the game states them again in text 997[7]).
    /// </summary>
    public static bool MenuClaims(int message) =>
        message is 17 or 19 or 20 or 22 or 25 or 26;

    /// <summary>
    /// The three keys <c>Dialog::handleEvent</c> takes for itself before the controls are
    /// offered the event — Tab, Return and Shift-Tab (`Interface.sc:1113`, `:1126`,
    /// `:1152`). No control in the game declares any of them; see the class remarks.
    /// </summary>
    public static bool DialogClaims(int message) => message is 9 or 13 or 15;
}
