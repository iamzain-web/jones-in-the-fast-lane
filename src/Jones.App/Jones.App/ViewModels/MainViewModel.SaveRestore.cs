using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Jones.Core;
using Jones.Core.Model;
using Jones.Core.Save;
using Jones.Core.Sci;

namespace Jones.App.ViewModels;

/// <summary>
/// Save, Restore and Restart — the Game menu, `Menu.sc:122-124`.
///
/// THERE IS NO SAVE OR RESTORE DIALOG IN THIS GAME, and looking for one is the mistake to
/// avoid. Script 990 (`Save.sc`) declares no `Dialog`, no `DIcon`, no view, loop or cel and
/// no file-list widget: it is four procedures that call the KERNEL and print the result.
/// `proc990_0` is `(SaveGame (gGame name:) 1 (gGame name:) @global539)` (`:28`) and
/// `proc990_1` is `CheckSaveGame` then `RestoreGame`, both with the same literal save
/// number **1** (`:55-56`). `GetSaveFiles` (`:53`) is called for one reason only — to find
/// out whether there is anything to restore at all. The player is never shown a list and
/// never types a name, which is exactly why the Save menu item has to warn first
/// (text 997[11], "Saving a game will overwrite a previously saved game. Continue?").
///
/// So the whole user interface is `Print`s, and every one of them is a plain, TAIL-LESS,
/// CENTRED balloon: `Print` defaults its tail number to 0 (`Interface.sc:49-58`) and only
/// keyword 318 sets one, which none of these calls passes. `Menu.sc` puts the default
/// colour scheme up first with `(proc0_17 0)` (`:220`, `:233`) — the cream box with the red
/// text, `Main.sc:1166-1170`.
///
/// The FORMAT those procedures write is not ported and could not be: see
/// <see cref="SaveStore"/>.
///
/// NOT PORTED: Ctrl-Y, "Set Save Directory" (`Menu.sc:227-230` → `proc990_2`). It is a
/// `Print` with an `#edit` field 29 characters wide (`Save.sc:78-94`) validated with
/// `ValidPath`, and it exists to let a 1990 player put saves on a second floppy — the same
/// reason `proc990_3` asks them to swap disks (text 990[6]). This port writes one slot to
/// the platform's application-data directory, there is no text-entry widget anywhere in it,
/// and a directory picker would be a screen the game does not have. Text 990[4] and 990[5]
/// are therefore unused.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// One of the Game menu's `Print`s while it is on screen. <paramref name="Buttons"/>
    /// empty means a text-only `Print`, which any key or click takes down
    /// (`Interface.sc:1108-1125`); non-empty makes it modal until a button is pressed.
    /// </summary>
    /// <param name="Mode">
    /// `Print`'s `#mode`, keyword 30 (`Interface.sc:80-83`) — `DText`'s alignment. The
    /// default is 1, centred, set at `Interface.sc:77` alongside the default width of 100;
    /// About passes 1 explicitly and Help passes 0, left (`Menu.sc:184-188`, `:194-195`).
    /// </param>
    private sealed record SystemPrint(string Text, int Width, int FontNumber,
                                      BalloonButton[] Buttons, Action<int>? Answered,
                                      bool Timed, int Mode = 1);

    private SystemPrint? _systemPrint;
    private DispatcherTimer? _systemTimer;
    private DateTime _systemPrintOpenedAt;

    /// <summary>
    /// `Print`s still waiting their turn. `Print` is a blocking call — `Dialog::doit` runs
    /// its own event loop — so the six in a row at `Menu.sc:175-188` are six boxes the
    /// player dismisses one after another, not six at once. Nothing but About and Help
    /// queues anything.
    /// </summary>
    private readonly Queue<SystemPrint> _systemQueue = new();

    /// <summary>`#button {YES} 1 #button {NO} 0` — the literals in script 997's strings.</summary>
    private static readonly BalloonButton[] YesNo =
        [new("YES", 1), new("NO", 0)];

    /// <summary>
    /// `#button {Yes} 1 #button {No} 0`. Restart spells them in mixed case where Save,
    /// Restore and Quit shout — both pairs are separate literals in script 997, so the
    /// difference is the game's and not a typo here.
    /// </summary>
    private static readonly BalloonButton[] YesNoMixed =
        [new("Yes", 1), new("No", 0)];

    /// <summary>`#button {OK} 1` — script 990's own literal.</summary>
    private static readonly BalloonButton[] Ok = [new("OK", 1)];

    // ------------------------------------------------------------------
    // The menu items
    // ------------------------------------------------------------------

    /// <summary>
    /// Whether Save and Restore are available. `room1.sc:1296-1300` enables menu items 513
    /// and 515 in the same block that enables the Goals screen (1026), and `Main.sc:985-991`
    /// switches 513 off and on again around the turn-start `Print`s — so they are live
    /// exactly when the board is, which is the condition
    /// <see cref="MainViewModel.CanShowGoalsScreen"/> already expresses.
    /// </summary>
    private bool CanSaveOrRestore =>
        _game is not null && _screen is Screen.Board or Screen.LocationPanel;

    /// <summary>
    /// Menu item 513, F5 (`Menu.sc:218-226`). The warning comes FIRST and nothing is written
    /// unless it is answered YES.
    /// </summary>
    private void SaveGameFromMenu()
    {
        if (!CanSaveOrRestore) return;

        Ask997(11, 180, YesNo, answer =>
        {
            if (answer != 1) return;
            DoSave();
        });
    }

    /// <summary>
    /// `(gGame save:)` → `proc990_0` (`Save.sc:17-40`). Success is announced with a TIMED
    /// `Print` — text 990[1] with `#time global426`, no buttons — and failure with
    /// text 990[0] behind an OK button.
    /// </summary>
    private void DoSave()
    {
        var ok = SaveStore.Write(_game!.Capture(CaptureShell()));

        if (ok) SystemNotice(990, 1, 100, fontNumber: 1);
        else SystemAsk(990, 0, 250, fontNumber: 0, Ok, null);

        BuildScreen();
    }

    /// <summary>
    /// Menu item 515, F7 (`Menu.sc:231-239`): the confirmation, then `global529 = 1`, which
    /// `Main::doit` (`Main.sc:1235-1238`) turns into `(gGame restore:)` on the next cycle.
    /// </summary>
    private void RestoreGameFromMenu()
    {
        if (!CanSaveOrRestore) return;

        Ask997(12, 150, YesNo, answer =>
        {
            if (answer != 1) return;
            DoRestore();
        });
    }

    /// <summary>
    /// The main menu's Restore button — `restoreGame` in select1 (`select1.sc:96-110`),
    /// view 10 loop 1 cel 2 at 27,47. NOTE THAT IT ASKS NOTHING: its `doit` is
    /// `(super doit:)` then `(= global529 1)` and nothing else, so unlike the menu item it
    /// goes straight to `proc990_1`. Only that procedure's own messages appear.
    /// </summary>
    private void RestoreGameFromTitle() => DoRestore();

    /// <summary>
    /// `(gGame restore:)` → `proc990_1` (`Save.sc:42-71`), whose three outcomes are the
    /// three this has:
    ///
    ///  * nothing on disk — `(GetSaveFiles …)` false, text 990[3], `#width 150 #font 0`;
    ///  * something unusable — `CheckSaveGame` false, text 990[2], `#width 200 #font 0`
    ///    (and its own list of reasons ends "…The game was saved under a different
    ///    interpreter", so refusing an unknown version is what the original did too);
    ///  * restored, which shows nothing at all. The original simply comes back in the
    ///    restored room.
    /// </summary>
    private void DoRestore()
    {
        switch (SaveStore.Read(out var snapshot))
        {
            case RestoreOutcome.NoSaveFound:
                SystemAsk(990, 3, 150, fontNumber: 0, Ok, null);
                BuildScreen();
                return;

            case RestoreOutcome.Unreadable:
                SystemAsk(990, 2, 200, fontNumber: 0, Ok, null);
                BuildScreen();
                return;
        }

        ApplyRestored(Game.Restore(snapshot!, new SciRandom(Environment.TickCount)),
                      snapshot!.Shell);
    }

    /// <summary>
    /// Menu item 518, F9 (`Menu.sc:199-210`). `(gGame restart:)` calls `RestartGame`, after
    /// which `Main::init` sees `GameIsRestarting` and goes to `newRoom: 1` rather than the
    /// notice room (`Main.sc:1213-1226`) — i.e. back to the front of the game with nothing
    /// carried over, skipping only Sierra's opening notice. The port's equivalent is the
    /// main menu.
    ///
    /// Effect 23 plays on BOTH answers (`:202` and `:207`), not only on Yes.
    /// </summary>
    private void RestartFromMenu()
    {
        Ask997(8, 100, YesNoMixed, answer =>
        {
            Effect(Audio.LocationMusic.ButtonClick);
            if (answer != 1) return;

            StopTalking();
            Sound.StopMusic();

            _game = null;
            _screen = Screen.MainMenu;
            _demoMode = false;
            _playerCount = 1;
            _choosingPlayer = 0;
            for (var p = 0; p < 4; p++)
                for (var g = 0; g < 4; g++)
                    _goals[p, g] = 50;
        });
    }

    // ------------------------------------------------------------------
    // Putting a restored game back on screen
    // ------------------------------------------------------------------

    /// <summary>The shell's own share of the turn in progress. See <see cref="ShellSnapshot"/>.</summary>
    private ShellSnapshot CaptureShell() => new()
    {
        RelaxedThisTurn = _relaxedThisTurn,
        WorkedThisTurn = _workedThisTurn,
        GarnishSpokenThisTurn = _garnishSpokenThisTurn,
        LastWeekendId = _lastWeekendId,
        ZMartStock = [.. _zmartStock.Select(s => s.Name)],
    };

    /// <summary>
    /// Installs a restored game and hands the board back.
    ///
    /// THE TURN-START CHAIN IS NOT RE-RUN. `StartTurn` ticks the economy, rerolls the
    /// weekend and rebuilds the turn's events; running it here would advance the game a week
    /// every time a save was loaded. The restored player picks up mid-turn with the clock,
    /// the latches and the economy exactly as they were left, which is what `RestoreGame`
    /// does — it puts the heap back and resumes.
    ///
    /// The presentation latches are therefore set to "already shown": the weekend and the
    /// newspaper for this turn were read before the save was taken, and the turn-start
    /// sounds have played.
    /// </summary>
    private void ApplyRestored(Game game, ShellSnapshot shell)
    {
        StopTalking();
        _systemPrint = null;
        _systemQueue.Clear();
        _systemTimer?.Stop();

        _game = game;
        _playerCount = game.Players.Count;
        _demoMode = game.Players.Count > 0 && game.Players[0].IsJones;

        _relaxedThisTurn = shell.RelaxedThisTurn;
        _workedThisTurn = shell.WorkedThisTurn;
        _garnishSpokenThisTurn = shell.GarnishSpokenThisTurn;
        _lastWeekendId = shell.LastWeekendId;

        // Z-Mart's shelves for the turn in progress, matched back by name against the
        // catalogue. Anything that no longer matches falls back to the catalogue order
        // rather than leaving the shop empty.
        var stock = shell.ZMartStock
            .Select(n => Catalogue.ZMart.FirstOrDefault(s => s.Name == n))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToArray();
        if (stock.Length > 0) _zmartStock = stock;

        // Nothing from the turn-start chain is replayed — see the summary above.
        _turnSoundsPending = false;
        _weekOverChimed = _game.Clock.TurnOver;
        _pendingNewspaper = null;
        _newspaperReturn = null;
        _weekendResult = null;

        _route.Clear();
        _screen = Screen.Board;
        _marbleIndex = CurrentPathIndex();

        // `room1.sc:281` — the board's theme, started wherever the board comes up.
        Music(Audio.LocationMusic.Board);

        BuildScreen();
    }

    // ------------------------------------------------------------------
    // The balloon these messages use
    // ------------------------------------------------------------------

    /// <summary>A `Print` of a text 997 string with buttons.</summary>
    private void Ask997(int index, int width, BalloonButton[] buttons, Action<int> answered) =>
        SystemAsk(997, index, width, fontNumber: 1, buttons, answered);

    private void SystemAsk(int resource, int index, int width, int fontNumber,
                           BalloonButton[] buttons, Action<int>? answered)
    {
        var text = SciText.Get(resource, index);
        if (text.Length == 0) return;

        _systemTimer?.Stop();
        _systemPrint = new SystemPrint(text, width, fontNumber, buttons, answered, Timed: false);
    }

    /// <summary>
    /// A `Print` with `#time global426` and no buttons — text 990[1] is the only one. The
    /// reading-speed gauge is <see cref="MainViewModel.ReadingSpeedSeconds"/>; a click or a
    /// key takes it down sooner, as `Dialog::handleEvent` does.
    /// </summary>
    private void SystemNotice(int resource, int index, int width, int fontNumber)
    {
        var text = SciText.Get(resource, index);
        if (text.Length == 0) return;

        _systemPrint = new SystemPrint(text, width, fontNumber, [], null, Timed: true);
        _systemPrintOpenedAt = DateTime.UtcNow;

        _systemTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _systemTimer.Stop();
        _systemTimer.Tick -= SystemPrintTick;
        _systemTimer.Tick += SystemPrintTick;
        _systemTimer.Start();
    }

    private void SystemPrintTick(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow - _systemPrintOpenedAt < TimeSpan.FromSeconds(ReadingSpeedSeconds))
            return;

        CloseSystemPrint();
    }

    private void CloseSystemPrint()
    {
        _systemTimer?.Stop();
        if (_systemPrint is null) return;

        _systemPrint = null;
        ShowNextQueuedPrint();
        BuildScreen();
    }

    private void AnswerSystemPrint(int value)
    {
        if (_systemPrint is not { } p) return;

        _systemTimer?.Stop();
        _systemPrint = null;       // cleared first: the handler may put another one up
        p.Answered?.Invoke(value);
        ShowNextQueuedPrint();
        BuildScreen();
    }

    /// <summary>
    /// The next box in a run of consecutive `Print`s, if the handler that just ran did not
    /// put one up itself. A queued `Print` never displaces one that is already on screen.
    /// </summary>
    private void ShowNextQueuedPrint()
    {
        if (_systemPrint is not null) return;
        if (_systemQueue.Count == 0) return;

        _systemPrint = _systemQueue.Dequeue();
    }

    /// <summary>
    /// Shows a text-only `Print` now, or lines it up behind the one already showing. Used by
    /// About and Help, whose boxes arrive as a run.
    /// </summary>
    private void QueueSystemPrint(string text, int width, int fontNumber, int mode)
    {
        if (text.Length == 0) return;

        var print = new SystemPrint(text, width, fontNumber, [], null, Timed: false, mode);

        if (_systemPrint is null) _systemPrint = print;
        else _systemQueue.Enqueue(print);
    }

    /// <summary>
    /// Draws the Game menu's balloon, if one is up. Returns true when it did, which is the
    /// signal to <see cref="MainViewModel.BuildBalloon"/> that the shopkeeper's balloon must
    /// not also be drawn — a `Print` replaces whatever was on screen.
    ///
    /// tNum 0 and scheme 0: see the class summary.
    /// </summary>
    private bool BuildSystemBalloon()
    {
        if (_systemPrint is not { } p) return false;

        var font = SciFont.Load(p.FontNumber);
        var hits = new List<BalloonButtonRect>();
        var buttons = p.Buttons.Length > 0 ? p.Buttons : null;

        if (!BubbleWindow.Build(Balloon, p.Text, p.Width, 0, 0, 0, 0,
                                buttons, buttons is null ? null : hits, font, p.Mode))
        {
            // Nothing drawable, so nothing pressable. Drop it rather than leave the player
            // behind an invisible modal — the same call the shop balloon makes.
            _systemPrint = null;
            _systemTimer?.Stop();
            return false;
        }

        foreach (var hit in hits)
        {
            var value = hit.Value;
            BalloonButtons.Add(new BalloonButtonVm(hit.X, hit.Y, hit.W, hit.H, hit.Text,
                                                   () => AnswerSystemPrint(value)));
        }

        return true;
    }

    /// <summary>
    /// A click anywhere while one of these is up. Returns true when the click belongs to the
    /// `Print` — which it always does, because a `Print` is modal: a text-only one is taken
    /// down by it (`Interface.sc:1108-1125`) and one with buttons swallows it.
    /// </summary>
    private bool DismissSystemPrint()
    {
        if (_systemPrint is not { } p) return false;
        if (p.Buttons.Length > 0) return true;

        CloseSystemPrint();
        return true;
    }

    /// <summary>
    /// The keyboard while one of these is up, and the F5 / F7 / F9 accelerators themselves.
    /// `MenuBar::init` declares them at `Menu.sc:122-124` and text 997[6] names them:
    /// "F5 - Saves the current game", "F7 - Restores the saved game", "F9 - Restarts the
    /// game".
    /// </summary>
    private bool HandleSaveRestoreKey(string key, bool ctrl)
    {
        if (_systemPrint is { } p)
        {
            // Enter presses the FIRST button, which `Print` has pre-selected
            // (`Interface.sc:230-241`); Esc returns -1, which `Print` maps to 0
            // (`Interface.sc:241-243`) — the value of the No button.
            if (p.Buttons.Length > 0)
            {
                if (key == "Return") AnswerSystemPrint(p.Buttons[0].Value);
                else if (key == "Escape") AnswerSystemPrint(0);
            }
            else
            {
                CloseSystemPrint();
            }

            // Everything else is swallowed: the dialog is modal and the menu bar is not
            // reachable behind it.
            return true;
        }

        if (ctrl) return false;

        switch (key)
        {
            case "F5":
                SaveGameFromMenu();
                BuildScreen();
                return true;

            case "F7":
                RestoreGameFromMenu();
                BuildScreen();
                return true;

            case "F9":
                RestartFromMenu();
                BuildScreen();
                return true;
        }

        return false;
    }
}
