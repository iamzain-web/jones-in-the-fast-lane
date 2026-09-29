using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jones.Core.Sci;

namespace Jones.App.ViewModels;

/// <summary>
/// THE MENU BAR — `MenuBar::init` and `MenuBar::handleEvent`, `Menu.sc:118-407`.
///
/// <para>
/// WHY IT EXISTS NOW. Save, Restore, Restart, Help, About and Quit were keyboard-only in
/// this port, and the Android head has no keyboard, so six of the game's commands were
/// simply unreachable on a phone. They were never keyboard commands: `MenuBar::init` puts
/// every one of them on a menu bar (`Menu.sc:121-131`) and the function keys are the
/// ACCELERATORS for those items. Text 997[7] names the bar itself — "Esc   - Menu
/// Bar/Pauses Game". Building it is the faithful fix and the portable one at the same time.
/// </para>
///
/// <para>
/// NOTHING HERE DUPLICATES A COMMAND. Every item calls the method the accelerator already
/// called — `SaveGameFromMenu`, `RestoreGameFromMenu`, `RestartFromMenu`,
/// <see cref="ShowStatsScreen"/>, <see cref="ShowGoalsScreen"/> — because in the original
/// they are one branch of one `switch`, reached identically whether the player pressed F5 or
/// pointed at the item.
/// </para>
///
/// <para>
/// SEVEN ITEMS ARE PERMANENTLY DISABLED, and that is deliberate rather than an oversight.
/// See <see cref="Unimplemented"/> for the list and the reason for each. They are SHOWN,
/// greyed, rather than dropped, because disabling an item is the game's own vocabulary —
/// `Menu.sc:132-136` disables five of them itself the instant the bar is built, and
/// `room1.sc:1296-1300` switches the same five back on — so a grey line is a thing the
/// original does, not chrome this port invented. It also keeps the bar item-for-item
/// against `Menu.sc:121-131`, which is the only way the next reader can check it.
/// </para>
/// </summary>
public sealed partial class MainViewModel
{
    private IReadOnlyList<SciMenu>? _bar;
    private Dictionary<int, MenuItemVm>? _barItems;

    private IReadOnlyList<SciMenu> Bar => _bar ??= SciMenuBar.Build();

    /// <summary>
    /// The four bar titles, verbatim. The first is a space, character <c>0x01</c> and a
    /// space — glyph 1 of font 0, a 9x8 shape rather than a letter — so it is drawn as
    /// <see cref="MenuAboutGlyph"/> and this string is never shown.
    /// </summary>
    public string MenuTitleGame => SciMenuBar.GameTitle;

    public string MenuTitleOptions => SciMenuBar.OptionsTitle;

    public string MenuTitleStatus => SciMenuBar.StatusTitle;

    private Bitmap? _aboutGlyph;
    private bool _aboutGlyphTried;

    /// <summary>
    /// `(AddMenu { \01 } …)` (`Menu.sc:121`) — the first menu's title is character 1 of the
    /// system font, a 9x8 shape. It is rendered from `assets/raw/font/0.font` rather than
    /// substituted with a letter or an icon, because the resource has the shape and a
    /// stand-in would be invention (CLAUDE.md §1). Null if the font is missing, in which
    /// case the view falls back to showing nothing there.
    /// </summary>
    public Bitmap? MenuAboutGlyph
    {
        get
        {
            if (_aboutGlyphTried) return _aboutGlyph;
            _aboutGlyphTried = true;

            // The bar is chrome outside the game's 320x200 space, like the sound switches,
            // so the glyph takes the chrome's own foreground rather than a game palette
            // entry. The SHAPE is the game's; the colour is the bar's.
            _aboutGlyph = SciFont.Load(0)?.Render(SciMenuBar.AboutTitle.Trim(), 0xFFF0F0F0u);
            return _aboutGlyph;
        }
    }

    // ------------------------------------------------------------------
    // The items
    // ------------------------------------------------------------------

    public MenuItemVm MenuAbout => Item(SciMenuBar.AboutId);
    public MenuItemVm MenuHelp => Item(SciMenuBar.HelpId);
    public MenuItemVm MenuSaveGame => Item(SciMenuBar.SaveGameId);
    public MenuItemVm MenuSetSaveDirectory => Item(SciMenuBar.SetSaveDirectoryId);
    public MenuItemVm MenuRestoreGame => Item(SciMenuBar.RestoreGameId);
    public MenuItemVm MenuQuit => Item(SciMenuBar.QuitId);
    public MenuItemVm MenuRestart => Item(SciMenuBar.RestartId);
    public MenuItemVm MenuDeletePlayer => Item(SciMenuBar.DeletePlayerId);
    public MenuItemVm MenuAnimationSpeed => Item(SciMenuBar.AnimationSpeedId);
    public MenuItemVm MenuGraphicsDetail => Item(SciMenuBar.GraphicsDetailId);
    public MenuItemVm MenuVolume => Item(SciMenuBar.VolumeId);
    public MenuItemVm MenuTurnMessages => Item(SciMenuBar.TurnMessagesId);
    public MenuItemVm MenuStatistics => Item(SciMenuBar.StatisticsId);
    public MenuItemVm MenuGoals => Item(SciMenuBar.GoalsId);

    /// <summary>
    /// THE ITEMS WITH NOTHING BEHIND THEM, and why each one is dead rather than dropped.
    /// Every one of them is on the bar, greyed, and does nothing when pointed at.
    ///
    /// <list type="bullet">
    /// <item><b>514 Set Save Directory</b> (Ctrl-Y) — `proc990_2`, a `Print` with a
    /// 29-character `#edit` field validated by `ValidPath` (`Save.sc:73-109`), for putting
    /// saves on a second floppy. This port has one slot in the platform's application-data
    /// directory, no text-entry widget anywhere, and nowhere for a path to point.</item>
    /// <item><b>769 Delete Current Player</b> (Ctrl-Z) — `Menu.sc:240-292` counts the
    /// players still in, asks text 997[13] with the walker's own view as its `#icon`, sets
    /// `finStat`, moves `global521` on and re-disables itself at two players. The YES/NO
    /// `Print` is the easy half; `finStat` is not modelled at all.</item>
    /// <item><b>770 Change Animation Speed</b> (Ctrl-S) — `Gauge.sc` is unported, and the
    /// handler also rewrites `global475` (`Menu.sc:317`), the travel cost, which
    /// `Board` does not model.</item>
    /// <item><b>771 Graphics Detail Level</b> (Ctrl-T) — the same unported `Gauge`, over
    /// `global534`, which has no equivalent here: this port draws everything at full
    /// detail unconditionally.</item>
    /// <item><b>774 Turn Messages Off</b> (F8) — `global427` gates `gBoughtItem` and the
    /// shopkeeper chatter; no such setting exists here. Its label also changes with the
    /// setting (`proc997_3` rewrites it through `SetMenu 774 110`), and what is shown is
    /// the state the game boots in: `Main.sc:1197` sets `global427` to 1, so the item
    /// reads "Turn Messages Off".</item>
    /// </list>
    ///
    /// <para>
    /// NOT ON THIS LIST, though it might look as though it should be: <b>773 Change
    /// Volume</b>. The original opens a 0..15 `Gauge` and this port has a boolean master,
    /// so the item does less than Sierra's — but it does the same thing Ctrl-V already does
    /// and has done for some time, and the setting is persisted. A deviation, not a gap.
    /// </para>
    ///
    /// <para>
    /// ALSO NOT ON IT: <b>Change Reading Speed</b>. It is not in this build. The CD's
    /// Options menu (`Menu.sc:129`) has no such item; the floppy's does
    /// (`jones-dos-1.000.060/src/Menu.sc:128`, `Change Reading Speed `^R`), along with
    /// "Turn Music Off `#2" and "Turn Sound Effects Off `#3". The port follows the CD build,
    /// so none of the three is on this bar — which is also why the music and effects
    /// switches stay where they are, outside it.
    /// </para>
    /// </summary>
    private static readonly int[] Unimplemented =
    [
        SciMenuBar.SetSaveDirectoryId,
        SciMenuBar.DeletePlayerId,
        SciMenuBar.AnimationSpeedId,
        SciMenuBar.GraphicsDetailId,
        SciMenuBar.TurnMessagesId,
    ];

    private MenuItemVm Item(int id)
    {
        _barItems ??= BuildMenuItems();
        return _barItems[id];
    }

    private Dictionary<int, MenuItemVm> BuildMenuItems()
    {
        var byId = new Dictionary<int, SciMenuItem>();
        foreach (var menu in Bar)
            foreach (var item in menu.Items)
                byId[item.Id] = item;

        var items = new Dictionary<int, MenuItemVm>();

        // A network joiner has the Options menu's volume and nothing else: the game and its
        // Save/Restore/Quit belong to the host, which is the only machine playing it.
        void Wire(int id, Func<bool> enabled, Action? run) =>
            items[id] = new MenuItemVm(byId[id].Text,
                                       () => (!_remoteView || id == SciMenuBar.VolumeId) && enabled(),
                                       run is null ? null : () =>
            {
                run();
                BuildScreen();
            });

        // `Menu.sc:172-190` / `:191-197` — About and Help are on the bar from the first
        // frame and are never disabled.
        Wire(SciMenuBar.AboutId, () => CanUseMenuBar, ShowAbout);
        Wire(SciMenuBar.HelpId, () => CanUseMenuBar, ShowHelp);

        // `Menu.sc:218-226` / `:231-239` / `:198-210`. Save and Restore are the two
        // `room1.sc:1297-1298` switches on with the board; Restart never moves.
        Wire(SciMenuBar.SaveGameId, () => CanUseMenuBar && CanSaveOrRestore, SaveGameFromMenu);
        Wire(SciMenuBar.RestoreGameId, () => CanUseMenuBar && CanSaveOrRestore, RestoreGameFromMenu);
        Wire(SciMenuBar.RestartId, () => CanUseMenuBar, RestartFromMenu);
        Wire(SciMenuBar.QuitId, () => CanUseMenuBar, QuitFromMenu);

        // `Menu.sc:319-334`. See ToggleMasterVolume for what this port's 0..15 gauge is.
        Wire(SciMenuBar.VolumeId, () => CanUseMenuBar, ToggleMasterVolume);

        // `Menu.sc:384-395`, both gated on their own menu item exactly as `proc997_1` and
        // `proc997_2` gate themselves on `GetMenu 1025 112` / `GetMenu 1026 112`.
        Wire(SciMenuBar.StatisticsId, () => CanUseMenuBar && CanShowStatsScreen, ShowStatsScreen);
        Wire(SciMenuBar.GoalsId, () => CanUseMenuBar && CanShowGoalsScreen, ShowGoalsScreen);

        foreach (var id in Unimplemented) Wire(id, () => false, null);

        return items;
    }

    // ------------------------------------------------------------------
    // Opening, closing and pausing
    // ------------------------------------------------------------------

    /// <summary>
    /// Whether the bar may be used at all.
    ///
    /// <para>
    /// A `Print` blocks it. `Dialog`'s class default is `menuBarOK 0` (`Interface.sc:814`)
    /// and `Dialog::handleEvent` only forwards to the menu when that property is set
    /// (`:1008-1010`); every location's own Dialog overrides it to 1
    /// (`fastFood.sc:52` and its twelve siblings), but the anonymous `(Dialog new:)` a
    /// `Print` builds at `Interface.sc:59` does not. So while a message box is up the menu
    /// bar is unreachable, which is also what stops a queued About box being interrupted
    /// half-way through its six pages.
    /// </para>
    /// </summary>
    public bool CanUseMenuBar => _systemPrint is null;

    private readonly List<DispatcherTimer> _pausedByMenuBar = [];

    /// <summary>
    /// Everything with a clock on it. `MenuSelect` runs the interpreter's own event loop
    /// while a menu is down, so no game cycle happens and nothing animates — which is what
    /// text 997[7] means by "Esc   - Menu Bar/Pauses Game".
    ///
    /// <para>
    /// `_systemTimer` is deliberately absent: it only runs while a `Print` is on screen, and
    /// <see cref="CanUseMenuBar"/> means the bar cannot be open then.
    /// </para>
    /// </summary>
    private DispatcherTimer?[] AnimationTimers =>
    [
        _walkTimer, _introTimer, _mouthTimer, _balloonTimer, _winnerTimer, _newsTimer,
        _smoothTimer, _noticeTimer, _ambulanceTimer, _lottoCycleTimer, _willyTimer,
        _itemsTimer, _diplomaTimer,

        // The props from MainViewModel.Animation.cs. The door's is here for the same reason
        // as the rest â€” `openDoor:`'s `(Wait 6)` is the interpreter blocking, and the menu
        // bar cannot come down inside it â€” but a menu opened during `closeDoor:`'s cycler
        // would otherwise run it while the game is stopped.
        _doorTimer, _piggyTimer, _clockTimer,
    ];

    /// <summary>Called by the view when the bar drops down.</summary>
    public void MenuBarOpened()
    {
        RefreshMenuBar();

        if (_pausedByMenuBar.Count > 0) return;

        foreach (var timer in AnimationTimers)
        {
            if (timer is not { IsEnabled: true }) continue;
            timer.Stop();
            _pausedByMenuBar.Add(timer);
        }
    }

    /// <summary>
    /// Called by the view when the bar closes. The counterpart of the `DoAudio audRESUME`
    /// that ends `MenuBar::handleEvent` (`Menu.sc:405`).
    ///
    /// NOT REPLICATED: the audio half of that. `DoAudio audRESUME` picks the speech clip up
    /// where the modal loop suspended it, and `IAudioPlayer` has no pause/resume for
    /// speech — only `StopSpeech`. Cutting a shopkeeper off mid-sentence to open a menu
    /// would be worse than letting the line finish, so the line finishes.
    /// </summary>
    public void MenuBarClosed()
    {
        foreach (var timer in _pausedByMenuBar) timer.Start();
        _pausedByMenuBar.Clear();
    }

    /// <summary>
    /// Re-reads every item's enabled state. The original does this continuously through
    /// `SetMenu … 112` (`room1.sc:1296-1300`, `Main.sc:985-991`); here it is cheaper to ask
    /// the five conditions once, when the bar comes down, since nothing can change while it
    /// is down.
    /// </summary>
    public void RefreshMenuBar()
    {
        if (_barItems is null) return;
        foreach (var item in _barItems.Values) item.Refresh();
    }

    // ------------------------------------------------------------------
    // The handlers that had no home before
    // ------------------------------------------------------------------

    /// <summary>
    /// Menu id 517, Ctrl-Q (`Menu.sc:211-217`):
    /// <code>(= gQuit (Print 997 10 #button {YES} 1 #button {NO} 0))</code>
    /// then effect 23 on BOTH answers, as at `:215`. No `#width`, so the box takes
    /// `Print`'s own default of 100 (`Interface.sc:77`) — the same default "Restarting?"
    /// takes.
    ///
    /// <para>
    /// This USED TO QUIT WITHOUT ASKING, on the grounds that the port had no modal
    /// machinery. It has had some since Save and Restore landed, so the question is now
    /// asked.
    /// </para>
    /// </summary>
    private void QuitFromMenu() =>
        Ask997(10, 100, YesNo, answer =>
        {
            Effect(Audio.LocationMusic.ButtonClick);
            if (answer == 1) QuitRequested?.Invoke();
        });

    /// <summary>
    /// Menu id 773, Ctrl-V (`Menu.sc:319-334`). The original opens a `Gauge` running 0..15
    /// and calls `(DoSound sndMASTER_VOLUME global520)`; `Gauge.sc` is unported and
    /// `IAudioPlayer` offers `Enabled` and nothing else, so this is the two ends of that
    /// slider and nothing in between. A DEVIATION, and the only one on this bar.
    ///
    /// <para>
    /// The master gates all three channels, so it takes all three on-screen switches with
    /// it — otherwise the game falls silent while the icons still claim everything is on.
    /// Batched so four switches moving together are one file write.
    /// </para>
    /// </summary>
    private void ToggleMasterVolume()
    {
        var on = !Sound.Enabled;
        Sound.Enabled = on;
        if (!on) { LocalSound.StopMusic(); LocalSound.StopSpeech(); }

        _settingsBatch = true;
        try
        {
            EffectsOn = on;
            MusicOn = on;
            SpeechOn = on;
        }
        finally
        {
            _settingsBatch = false;
        }

        SaveSoundSettings();
    }

    /// <summary>
    /// `Main.sc:1199-1201` reads `global539` with
    /// <c>(FileIO fiREAD_STRING @global539 10 …)</c> from a plain file called `version` in
    /// the game directory — not a resource, so it is not under `assets/raw`. The CD build's
    /// copy is in this repository at `original/cd/version` and holds one line: `1.0`. That
    /// line is quoted here rather than invented, and it is the only thing text 997[0]'s
    /// `%s` ever receives.
    /// </summary>
    private const string GameVersion = "1.0";

    /// <summary>
    /// Menu id 257, F10 (`Menu.sc:172-190`). SIX `Print`s in a row, all `#font 4 #mode 1
    /// #width 150`: text 997[0] with the version substituted, then 997[1] through 997[5].
    /// Each is a separate box the player dismisses, because `Print` blocks.
    /// </summary>
    private void ShowAbout()
    {
        QueueSystemPrint(SciText.Get(997, 0, GameVersion), 150, fontNumber: 4, mode: 1);

        for (var i = 1; i <= 5; i++)
            QueueSystemPrint(SciText.Get(997, i), 150, fontNumber: 4, mode: 1);

        ShowPortCredit();
    }

    /// <summary>
    /// THE ONLY USER-VISIBLE TEXT IN THIS PORT THAT IS NOT SIERRA'S, and it is here on
    /// purpose. Working rule 1 in CLAUDE.md — never invent user-visible text — exists so that
    /// no invented sentence can be mistaken for a resource; it is not a rule against the port
    /// having an author. These two boxes are the port's own, added at Zain Cassimjee's
    /// explicit request, and they say so by sitting AFTER the six that come out of text 997.
    ///
    /// <para>
    /// Nothing above this method changes: 997[0] through 997[5] are still read from the
    /// resource, still font 4, still `#mode 1`, still width 150. Anyone auditing the port's
    /// fidelity can delete this one method and its single call site and be back to Sierra's
    /// About box exactly. It writes to no resource and no save.
    /// </para>
    ///
    /// <para>
    /// WHY THE DEDICATION IS FONT 14 AND NOT FONT 4. It has to be ONE box — the names and the
    /// verse belong on the same page — and font 4 cannot hold that much text on a 320x200
    /// screen at any width. Measured against the font resources, summing per-glyph width bytes
    /// the way <c>SciFont.Measure</c> does and breaking lines the way
    /// <c>BubbleWindow.Wrap</c> does: font 4 is 9 pixels a line and bottoms out at 23 lines /
    /// 207px even at width 296, before the box's own border and padding, so it runs off the top
    /// of the screen. **Font 14 is 7 pixels a line: at width 280 the same text is 23 lines /
    /// 161px, about 175px with chrome, and the widest line is 278px of the 320 available.**
    /// Fonts 0, 1, 3 and 8 are all taller or wider and fail the same way; font 10 (6px) fits
    /// with more room but is smaller than it needs to be.
    /// </para>
    ///
    /// <para>
    /// Font 14 is a face THE GAME ITSELF USES, which is why it is the one picked: `room1.sc:1451`
    /// draws the board's cash readout with `dsFONT 14`. It is shipped already, as
    /// `Assets/game/font_14.font` — <c>SciFont.Load</c> reads embedded assets, not
    /// `assets/raw/font`, and a font it cannot find falls back to font 1 (12px), which would
    /// overflow worse than what this replaced.
    /// </para>
    ///
    /// <para>
    /// THE AUTHOR'S LINE BREAKS ARE DELIBERATELY NOT KEPT INSIDE A STANZA. `Wrap` treats `\n`
    /// as a hard paragraph break and refills from there, so a mid-stanza break ends its line
    /// early and leaves an orphan — the first attempt printed "them." and "where you end up,"
    /// alone on their own lines, which is what made it look broken. Each stanza is therefore
    /// one paragraph and fills the column; the blank lines BETWEEN stanzas are kept, because
    /// those are the shape of the verse rather than an accident of where it was typed.
    /// </para>
    ///
    /// <para>
    /// The em dash is written as a hyphen on purpose: these are 128-character SCI fonts with no
    /// glyph at U+2014, and an absent glyph renders through <c>Glyph</c>'s `'?'` fallback.
    /// Every character in both strings was checked against the resource for a non-zero width.
    /// </para>
    /// </summary>
    private void ShowPortCredit()
    {
        QueueSystemPrint(
            "Zain Cassimjee\n"
            + "\n"
            + "Ported to the modern age\n"
            + "for Windows and Android",
            150, fontNumber: 4, mode: 1);

        QueueSystemPrint(
            "Dedicated to\n"
            + "\n"
            + "Zahra and Qailah Cassimjee\n"
            + "and Lameez Hoosen\n"
            + "\n"
            + "May life take you on roads you never expected, and may you always have the "
            + "courage to travel them.\n"
            + "\n"
            + "I hope you discover that life is not simply about where you end up, but about "
            + "the people you meet, the lessons you learn, the mistakes you survive, and the "
            + "moments that make the journey worthwhile.\n"
            + "\n"
            + "May you learn to embrace the good days, grow through the difficult ones, and "
            + "never be afraid to take the road less travelled.\n"
            + "\n"
            + "Above all, I wish you a life filled with curiosity, courage, laughter and "
            + "love - and enough wisdom to know that sometimes the greatest adventures begin "
            + "when you have no idea where the road is going.\n"
            + "\n"
            + "With all my love and every good wish for the journeys ahead.",
            280, fontNumber: 14, mode: 1);
    }

    /// <summary>
    /// Menu id 258, F1 (`Menu.sc:191-197`). Two `Print`s, `#font 4 #width 160 #mode 0` —
    /// text 997[6], the function keys, then 997[7], the control keys and the mouse. LEFT
    /// aligned, which is the only place in the game that passes `#mode 0`: the lines are a
    /// two-column list and centring them would shuffle the dashes about.
    ///
    /// <para>
    /// This is the game telling the player what every key does, including the six this port
    /// does not implement. It is printed as written: 997[6] and 997[7] are the game's own
    /// account of itself, and editing them to match the port would be rewriting a resource.
    /// </para>
    /// </summary>
    private void ShowHelp()
    {
        QueueSystemPrint(SciText.Get(997, 6), 160, fontNumber: 4, mode: 0);
        QueueSystemPrint(SciText.Get(997, 7), 160, fontNumber: 4, mode: 0);
    }
}

/// <summary>
/// One line of the menu bar, bound straight to a <c>MenuItem</c>.
///
/// <see cref="IsEnabled"/> is a live condition rather than a stored flag because that is
/// what the original has: `SetMenu &lt;id&gt; 112 &lt;0|1&gt;` is called from six places
/// across three scripts, and re-asking the question when the bar opens is both simpler and
/// harder to get out of step than mirroring every one of those calls.
/// </summary>
public sealed class MenuItemVm : ObservableObject
{
    private readonly Func<bool> _enabled;

    internal MenuItemVm(string label, Func<bool> enabled, Action? run)
    {
        Label = label;
        _enabled = enabled;
        Command = new RelayCommand(() => run?.Invoke(), () => run is not null && _enabled());
    }

    /// <summary>
    /// The item's text from `Menu.sc:121-131`, verbatim — trailing space and all, since
    /// three of them have one and CLAUDE.md §2 says the bytes are the truth.
    /// </summary>
    public string Label { get; }

    public bool IsEnabled => _enabled();

    public ICommand Command { get; }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsEnabled));
        (Command as RelayCommand)?.NotifyCanExecuteChanged();
    }
}
