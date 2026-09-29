namespace Jones.App;

/// <summary>
/// Whether this head is driven by a finger rather than a mouse.
///
/// WHY THIS EXISTS. Three of the game's bindings have no touch equivalent at all:
/// text 997[7] gives "Middle Mouse/CTRL-Left - Goals" and "Right Mouse/SHIFT-Left - Stats",
/// and a phone has one button, no modifier keys and no keyboard for the F4 and F6 that are
/// the same two commands (997[6]). Without somewhere to press, the Goals and Statistics
/// screens are simply unreachable on Android.
///
/// So MainView draws two on-screen switches for them, and only when this is set. It is a
/// plain static read once through <c>x:Static</c> rather than an observable property,
/// because a head knows what it is before it builds a window and never changes its mind:
/// the Android head sets it in JonesRuntime, and nothing else ever does.
///
/// The sound switches beside them are drawn unconditionally, because the keyboard has F2
/// and F3 but neither is any use to someone with a mouse alone.
/// </summary>
public static class TouchUi
{
    /// <summary>Set by a head with no pointer and no keyboard. False on the desktop.</summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// How tall one row of the menu bar has to be.
    ///
    /// THE MENU BAR IS WHY THE TWO SWITCHES ABOVE ARE GONE. `MenuBar::init`
    /// (`Menu.sc:121-131`) puts Statistics and Goals on a Status menu of their own, with
    /// the game's own words, alongside the six other commands that were keyboard-only. A
    /// bar that can be tapped reaches all eight, so a pair of bespoke buttons for two of
    /// them is chrome this port no longer needs.
    ///
    /// The original's bar is ten pixels tall — `Main.sc:965` treats a click at
    /// <c>y &gt;= 10</c> as being in the game rather than in the bar — but the original was
    /// pointed at with a mouse. Ten pixels is not a finger, and this bar lives outside the
    /// 320x200 space (see MainView) where nothing forces it to Sierra's scale, so on a
    /// touch head it takes the same 44px the sound switches already take and 48px for a
    /// dropped-down row, which is above both Android's and Apple's minimum target.
    /// </summary>
    public static double BarHeight => Enabled ? 44 : 26;

    /// <summary>One line of an open menu. See <see cref="BarHeight"/>.</summary>
    public static double ItemHeight => Enabled ? 48 : 28;

    /// <summary>The type size on the bar and in the menus.</summary>
    public static double FontSize => Enabled ? 16 : 13;
}
