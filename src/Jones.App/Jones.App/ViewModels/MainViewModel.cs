using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Jones.Core;
using Jones.Core.Economy;
using Jones.Core.Model;
using Jones.Core.Save;
using Jones.Core.Sci;

namespace Jones.App.ViewModels;

/// <summary>Which screen is showing, mirroring the original's script sequence.</summary>
public enum Screen
{
    MainMenu,        // select1  (script 233)
    PlayerCount,     // select1b (script 239)
    CharacterSelect, // select2  (script 235)
    GoalSetting,     // select3  (script 236)
    Board,           // room1    (script 1)
    LocationPanel,   // the per-building dialogs
    Newspaper,       // newspaper (script 215), on a crash, boom or robbery
    Weekend,         // weekend   (script 232), every week after the first
    Broker,          // broker    (script 213), reached from inside the Bank
    Diploma,         // diploma   (script 230), on graduating from Hi-Tech U
    WhosWinning,     // viewGoals (script 238), F6 / middle mouse / Ctrl-left
    Stats,           // inventories (script 231), F4 / right mouse / Shift-left
    Winner,          // winnerScript (script 234), when all four goals are met
    GoalsHelp,       // goalsDefine (script 229), the `?` on the goal-setting screen
    Intro,           // introRoom (script 2), the credits sequence the game boots into
}

/// <summary>
/// The game screen. Everything is laid out in the original's 320x200 space using the
/// coordinates from the decompiled scripts, so positions are ported rather than designed.
///
/// Dialogs open at (69,44) and are 184x119 - `moveTo: 69 44`, `nsRight 184`,
/// `nsBottom 119` in select1/1b/2/3 - so every icon inside a dialog is drawn at
/// DialogX + nsLeft, DialogY + nsTop.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    // Dialog geometry, from the Dialog instances in the setup scripts.
    private const int DialogX = 69;
    private const int DialogY = 44;
    private const int DialogW = 184;
    private const int DialogH = 119;

    private Game? _game;
    private Screen _screen = Screen.MainMenu;
    private bool _relaxedThisTurn;

    // Setup state. global374 is the player count; global507 is which player is choosing.
    private int _playerCount = 1;
    private int _choosingPlayer;
    private readonly int[] _whichBody = new int[4];
    private readonly int[,] _goals = new int[4, 4];  // [player, goal] 10..100
    private bool _demoMode;

    /// <summary>Z-Mart's six lines for the current turn, rerolled each turn.</summary>
    private StockItem[] _zmartStock = Catalogue.ZMart.Take(6).ToArray();
    private readonly SciRandom _stockRng = new(Environment.TickCount ^ 0x5A17);

    // --- Walking -------------------------------------------------------
    // The marble does not teleport: MarblePath::doit advances it one path index at a
    // time while gTheWalker cycles its frames, and the journey is the thing that costs
    // the turn's hours. `moveSpeed` on the marble instance is 1, i.e. one index per tick.
    private int _marbleIndex = 1;
    private readonly Queue<int> _route = new();
    private LocationId _walkTarget;
    private DispatcherTimer? _walkTimer;

    /// <summary>
    /// `theWalker`'s cycler. The walker's frame is DERIVED from the wall clock rather than
    /// stepped by the walk timer — see <see cref="WalkerFrame"/> for why that is what the
    /// original does, and <see cref="CelCycler"/> for the rule it shares with Willy.
    /// </summary>
    private readonly CelCycler _walkerCycler = new(WalkerTicksToDo);

    public bool IsWalking => _route.Count > 0;

    /// <summary>
    /// Which goal slider is being dragged, or -1. Held here rather than on the slider
    /// element because every drag tick rebuilds the screen, which would destroy the
    /// element holding the pointer capture and kill the drag after a single frame.
    /// </summary>
    private int _draggingGoal = -1;

    // A network joiner sends the drag to the host instead (MainViewModel.Network.cs); the
    // host's own mouse is ignored while a joiner is the one choosing.
    public void BeginGoalDrag(int index, int canvasY)
    {
        if (_remoteView)
        {
            _joinerDragging = true;
            SendInput?.Invoke(new Jones.Net.InputMsg { Kind = Jones.Net.InputKind.GoalBegin, Index = index, Y = canvasY });
            return;
        }

        if (!Net.InputGate.Allows) return;
        _draggingGoal = index;
        DragGoalTo(canvasY);
    }

    public void DragGoalTo(int canvasY)
    {
        if (_remoteView)
        {
            if (_joinerDragging)
                SendInput?.Invoke(new Jones.Net.InputMsg { Kind = Jones.Net.InputKind.GoalDrag, Y = canvasY });
            return;
        }

        if (!Net.InputGate.Allows) return;
        if (_draggingGoal < 0) return;
        var track = GoalTracks.FirstOrDefault(t => t.Index == _draggingGoal);
        track?.SetFromScreenY(canvasY);
    }

    public void EndGoalDrag()
    {
        if (_remoteView)
        {
            if (!_joinerDragging) return;
            _joinerDragging = false;
            SendInput?.Invoke(new Jones.Net.InputMsg { Kind = Jones.Net.InputKind.GoalEnd });
            return;
        }

        if (!Net.InputGate.Allows) return;
        _draggingGoal = -1;
    }

    public MainViewModel()
    {
        for (var p = 0; p < 4; p++)
            for (var g = 0; g < 4; g++)
                _goals[p, g] = 50; // the class default in room1.sc

        // `Main::play` opens on room 2, `introRoom`, which runs its credits and then
        // `(gCurRoom newRoom: 1)` — the main menu (`introRoom.sc:256-258`).
        //
        // DELIBERATE DEVIATION, off by default: the intro runs about 50 seconds on every
        // launch. Faithful, and unbearable when you are starting the game repeatedly to
        // test it. Skippable with any click or key, which is what `introRoom::handleEvent`
        // does — but that still means a keypress every single launch.
        //
        // Set PlayIntro back to true for Sierra's opening. The sequence itself is fully
        // ported and unchanged; this only decides whether it runs on startup.
        if (PlayIntro) StartIntro();

        BuildScreen();
    }

    public ObservableCollection<SpriteVm> Sprites { get; } = [];
    public ObservableCollection<HotspotVm> Hotspots { get; } = [];
    public ObservableCollection<TextVm> Texts { get; } = [];
    public ObservableCollection<GoalTrackVm> GoalTracks { get; } = [];
    public ObservableCollection<MenuLineVm> MenuLines { get; } = [];
    public ObservableCollection<ActionVm> Actions { get; } = [];

    // THERE IS NO NOTICE LIST. The `Notices` collection that used to live here carried a
    // running commentary â€” "Wild Willy took 2 item(s)", "Rent paid.", "Hired as Clerk!" â€”
    // none of which exists anywhere in the game. `startTrn.sc` has exactly one `Print` in
    // the whole turn-start chain (text 111[0], the multi-player hand-over prompt); the
    // robbery, the crash and the boom are announced by the NEWSPAPER, and everything else
    // is spoken. See `Speak`.

    /// <summary>
    /// The speech balloon, in painter's order â€” see <see cref="BubbleWindow"/>. Empty
    /// whenever nobody is talking.
    /// </summary>
    public ObservableCollection<BalloonPartVm> Balloon { get; } = [];

    /// <summary>
    /// Hit rectangles over the balloon's buttons, when the balloon is a question. Empty
    /// otherwise. See <see cref="Ask"/>.
    /// </summary>
    public ObservableCollection<BalloonButtonVm> BalloonButtons { get; } = [];

    /// <summary>
    /// The town board stays on screen behind the between-turn dialogs.
    ///
    /// Both of them open on `global38`, the INVISIBLE window â€” `weekend.sc:205` and
    /// `newspaper.sc:118`, each `moveTo: 69 44` then `open: 0 -1`. An invisible window
    /// paints no backdrop of its own, so the dialog is drawn straight over whatever room
    /// is showing, which between turns is room1: the board. Hiding the board on these
    /// screens left the dialog floating on black.
    /// </summary>
    /// The broker (`broker.sc:118`) does the same thing, reached from inside the Bank.
    /// `viewGoals.sc:155` opens on `global38` too, so the board shows through it as well.
    public bool ShowBoard =>
        _screen is Screen.Board or Screen.LocationPanel or Screen.Newspaper
                or Screen.Weekend or Screen.Broker or Screen.Diploma
                or Screen.WhosWinning or Screen.Stats
                // The podium is painted INTO room1 (`winnerScript.sc:38-44` uses
                // `addToPic:`), so the town is still the backdrop around it.
                or Screen.Winner;
    public bool ShowPanel => _screen == Screen.LocationPanel;

    private Player P => _game!.Current;

    // ------------------------------------------------------------------
    // Screen construction
    // ------------------------------------------------------------------

    private void BuildScreen()
    {
        // A network joiner has no game to build a screen from; the host's arrives as frames.
        if (_remoteView) return;

        Sprites.Clear();
        Hotspots.Clear();
        Texts.Clear();
        Actions.Clear();
        GoalTracks.Clear();
        MenuLines.Clear();
        Balloon.Clear();
        BalloonButtons.Clear();
        _keyBindings.Clear();

        switch (_screen)
        {
            case Screen.MainMenu: BuildMainMenu(); break;
            case Screen.PlayerCount: BuildPlayerCount(); break;
            case Screen.CharacterSelect: BuildCharacterSelect(); break;
            case Screen.GoalSetting: BuildGoalSetting(); break;
            case Screen.GoalsHelp: BuildGoalsHelp(); break;
            case Screen.Intro: BuildIntro(); break;
            case Screen.Board:
            case Screen.Winner:
            case Screen.LocationPanel: BuildBoard(); break;
            case Screen.Newspaper: BuildNewspaper(); break;
            case Screen.Weekend: BuildWeekend(); break;
            case Screen.Broker: BuildBroker(); break;
            case Screen.Diploma: BuildDiploma(); break;
            case Screen.WhosWinning: BuildWhosWinning(); break;
            case Screen.Stats: BuildStats(); break;
        }

        // Every clickable line that declares a `key` becomes a binding, so the accelerators
        // follow the controls that are actually on screen — which is what the original does
        // too, since `Item::handleEvent` is only reached for controls in the open dialog's
        // own list. Disabled lines are skipped: bit 0 of `state` gates the KEY as much as
        // the mouse (`Interface.sc:410-416`).
        foreach (var line in MenuLines)
            if (line.Key != 0 && line.Enabled)
                Accelerator(line.Key, () => line.Command.Execute(null));

        BuildBalloon();
        RefreshAll();
    }

    /// <summary>
    /// The `key` accelerators of whatever is on screen: the event message a control matches
    /// on, against what pressing it does. Rebuilt with the screen, exactly as the original's
    /// dialog is. <see cref="SciKey"/> establishes what the numbers are.
    /// </summary>
    private readonly Dictionary<int, Action> _keyBindings = [];

    /// <summary>
    /// Binds one control's declared `key`. Icon buttons go through here; text lines carry
    /// theirs on the <c>MenuLineVm</c> and are swept up at the end of <see cref="BuildScreen"/>.
    /// </summary>
    private void Accelerator(int key, Action press)
    {
        // FIRST one wins, not last: `Dialog` is a `List` and `List::handleEvent` walks its
        // elements in `add:` order, stopping at the first that claims the event. Two
        // controls on one screen can share a number — the icon buttons are registered
        // before the text lines are swept up — and the original would fire the earlier.
        if (key != 0) _keyBindings.TryAdd(key, press);
    }

    /// <summary>
    /// select1 (script 233). View 10 loop 0 cel 0 is the 183x112 menu backdrop - exactly
    /// dialog-sized - and loop 1's three 133x20 cels are the buttons:
    /// cel 0 Play, cel 2 Restore, cel 1 Demonstration, at nsLeft 27 / nsTop 17, 47, 77.
    /// </summary>
    private void BuildMainMenu()
    {
        // The setup dialogs use `global38`, the INVISIBLE window (back -1), so they have
        // no backdrop of their own and draw over whatever room is showing - which is
        // introRoom, i.e. the title screen. View 10 loop 0 is fully transparent, which
        // is why using it as a background left the screen black.
        TitleBackdrop();

        // NO TOOLTIPS. SCI has none, and every one that was here ("Play Game",
        // "Restore Game - not yet ported", "Watch Demo") was wording I made up. The button
        // cels say what they do; a made-up caption is worse than none.
        Sprites.Add(Icon(10, 1, 0, 27, 17, () => { _screen = Screen.PlayerCount; BuildScreen(); }));

        // `restoreGame` (`select1.sc:96-110`). Its `doit` sets `global529`, which
        // `Main::doit` (`Main.sc:1235-1238`) turns into `(gGame restore:)` — and it asks
        // nothing first, unlike the Game menu's own Restore item. See
        // MainViewModel.SaveRestore.cs.
        Sprites.Add(Icon(10, 1, 2, 27, 47, RestoreGameFromTitle));
        Sprites.Add(Icon(10, 1, 1, 27, 77, () =>
        {
            // The original sets the first player's `playing` to 29 - Jones playing
            // himself, the attract-mode demonstration.
            _demoMode = true;
            _playerCount = 1;
            _choosingPlayer = 0;
            _screen = Screen.CharacterSelect;
            BuildScreen();
        }));
    }

    /// <summary>
    /// select1b (script 239): "How Many Players?" at 33,20 and four buttons at nsTop 55,
    /// nsLeft 20/60/100/140. The script's icons carry no `view`, inheriting the class
    /// default, so the digit is drawn over a plain button face here.
    /// </summary>
    private void BuildPlayerCount()
    {
        TitleBackdrop();

        Texts.Add(new TextVm("How Many Players?", DialogX + 33, DialogY + 20, 10, "#000000", true));

        var xs = new[] { 20, 60, 100, 140 };
        for (var i = 0; i < 4; i++)
        {
            var count = i + 1;

            // select1b's num1..num4 declare `loop 3` with no view, so they use the DIcon
            // class default of view 0. View 0 loop 3 holds five 25x24 cels - the player
            // number tokens - and the script selects cels 0..3.
            Sprites.Add(Icon(0, 3, i, xs[i], 55, () =>
            {
                _playerCount = count;
                _choosingPlayer = 0;
                _screen = Screen.CharacterSelect;
                BuildScreen();
            }));
        }
    }

    /// <summary>
    /// select2 (script 235): background view 500, the four character portraits
    /// (view 499 loop 0 cels 0-3, 45x97) at nsLeft 1/47/93/139 nsTop 14, and their
    /// select buttons (view 250 loop 7) at nsTop 108, nsLeft 6/52/98/144.
    /// </summary>
    private void BuildCharacterSelect()
    {
        // `select2.sc:38` opens on `global38`, the INVISIBLE window, exactly as the main
        // menu and player-count screens do â€” so the title screen stays behind it rather
        // than the dialog sitting on black.
        TitleBackdrop();

        Sprites.Add(Icon(500, 0, 0, 0, 0));

        var portraitX = new[] { 1, 47, 93, 139 };
        var buttonX = new[] { 6, 52, 98, 144 };

        for (var i = 0; i < 4; i++)
        {
            var body = i;

            // The four characters are painted into the background (view 500). View 499
            // loop 0 is NOT the portraits - it is the DIMMING overlay, which the script
            // adds as firstDim..fourthDim only once a character has been taken. Drawing
            // it over all four hides the characters behind a dither.
            var taken = Enumerable.Range(0, _choosingPlayer).Any(p => _whichBody[p] == body);
            if (taken)
                Sprites.Add(Icon(499, 0, body, portraitX[i], 14));

            if (!taken)
            {
                Sprites.Add(Icon(250, 7, 0, buttonX[i], 108, () =>
                {
                    _whichBody[_choosingPlayer] = body;
                    _choosingPlayer++;
                    _goalsViewPlayer = null;      // setup, not select3's read-only mode 2
                    _screen = Screen.GoalSetting;
                    BuildScreen();
                }));
            }
        }

        // The background already carries the "SELECT YOUR CHARACTER" heading, so the only
        // thing to add is which player is choosing. The original shows this with the
        // playerNumber icon (view 499 loop 1) at nsLeft 42, nsTop 1.
        Sprites.Add(Icon(499, 1, Math.Min(_choosingPlayer, 3), 42, 1));
    }

    /// <summary>
    /// Which player's goals are being LOOKED at rather than set — `select3`'s `param2 == 2`,
    /// reached from a player token on the Who's Winning screen (`viewGoals.sc:226-235`).
    /// Null during setup.
    /// </summary>
    private int? _goalsViewPlayer;

    /// <summary>
    /// `select3::init`'s `param2`, which decides the title plate and whether the sliders can
    /// be dragged. Its three values each have exactly one call site:
    ///
    /// <list type="bullet">
    /// <item><c>0</c> — setup, from `select2.sc:127` and its three siblings.</item>
    /// <item><c>1</c> — the computer's own goals, from `room1.sc:1255`, and only when
    ///   `(players at: 1) playing:` is 29 in a one-human game.</item>
    /// <item><c>2</c> — read-only, from `viewGoals.sc:231/254/277/300`.</item>
    /// </list>
    ///
    /// Mode 1 is not reachable in this port: there is no step between character select and
    /// the first turn that hands the screen to Jones. It is named here rather than collapsed
    /// away so the title cel is already right if that step is ported.
    /// </summary>
    private int GoalScreenMode => _goalsViewPlayer is not null ? 2 : 0;

    /// <summary>select3 (script 236): four star sliders, 10..100 each.</summary>
    private void BuildGoalSetting()
    {
        // `select3.sc:104` â€” same invisible window, same title screen behind it.
        // In mode 2 the screen it opens over is the Who's Winning dialog, which is itself
        // over the board; both are `global38`, so the board is the backdrop either way.
        TitleBackdrop();

        Sprites.Add(Icon(501, 1, 0, 0, 0));      // background

        // `windowTitle` DECLARES `view 501` with `nsLeft 24` and `priority 9`, but the view
        // is overwritten at init in every mode, so the declaration only survives mode 2
        // (`select3.sc:71-75`):
        //
        //     (if (== param2 2) (windowTitle cel: 0 view: 501)
        //      else             (windowTitle cel: param2 view: 506))
        //
        // Taking the declared view as the truth put "GOALS" on the setup screen, where the
        // original reads "SET YOUR GOALS" — the exact trap CLAUDE.md §3 warns about.
        // Decoding the two cels of view 506 settles which mode is which without guessing:
        // cel 0 is "SET YOUR GOALS" and cel 1 is "JONES GOALS", matching the call sites —
        // `select2.sc:127/159/191/224` pass 0 for a human player and `room1.sc:1255` passes
        // 1 for the computer's own goals. 501 loop 0 cel 0 is the short "GOALS" plate.
        var mode = GoalScreenMode;
        Sprites.Add(mode == 2 ? Icon(501, 0, 0, 24, 0) : Icon(506, 0, mode, 24, 0));

        // In mode 2 the values are the chosen player's own, read off the Player rather than
        // out of the setup array, and nothing can be dragged (`:42-48` disables all four
        // stars and puts the exit button back on loop 0).
        var viewing = _goalsViewPlayer is { } vp && _game is not null
            ? _game.Players[Math.Clamp(vp, 0, _game.Players.Count - 1)]
            : null;

        var p = _choosingPlayer - 1;
        if (p < 0) p = 0;

        // StarSlider instances: nsTop 38, nsLeft 32 / 68 / 104 / 139, view 501 loop 2.
        // cel = (goalValue - 1) / 10, so 10 -> cel 0 and 100 -> cel 9.
        // The four sliders are NOT labelled. They are art (view 501 loop 2), and the words
        // "Wealth" / "Happiness" / "Education" / "Career" appear in this game only as the
        // opening of the four help paragraphs in script 229 (`goalsDefine`, @0x05AA,
        // @0x0660, @0x071C, @0x07C6) â€” never as captions on the sliders. The tooltip that
        // used to name and read out each one was mine.
        var xs = new[] { 32, 68, 104, 139 };

        for (var g = 0; g < 4; g++)
        {
            var goal = g;
            var value = viewing is null
                ? _goals[p, g]
                : g switch
                {
                    0 => viewing.MonGoal,
                    1 => viewing.HapGoal,
                    2 => viewing.EduGoal,
                    _ => viewing.CarGoal,
                };

            // The slider art itself: cel = (goalValue - 1) / 10, so 10 -> cel 0 and
            // 100 -> cel 9, exactly as StarSlider::updStar computes it.
            Sprites.Add(Icon(501, 2, (value - 1) / 10, xs[g], 38));

            // Mode 2's sliders are disabled, so there is no track to drag.
            if (viewing is not null) continue;

            // The star is dragged, not stepped - there are no arrow buttons in the
            // original. StarSlider::track clamps the pointer to y 43..82 and maps it
            // with `((82 - y) / 4 + 1) * 10`, giving 100 at the top and 10 at the bottom.
            // The slider cel is 14x51 at nsTop 38, so the track occupies 43..82 within it.
            GoalTracks.Add(new GoalTrackVm(
                goal,
                DialogX + xs[g], DialogY + 43, 14, 40,
                canvasY =>
                {
                    // StarSlider::track works in DIALOG-relative coordinates, so the
                    // pointer's canvas y must have the dialog origin removed first.
                    // Feeding it canvas coordinates clamps every value to the bottom of
                    // the range and pins all four goals at 10.
                    var dialogY = canvasY - DialogY;
                    var clamped = Math.Clamp(dialogY, 43, 82);
                    _goals[p, goal] = Math.Clamp(((82 - clamped) / 4 + 1) * 10, 10, 100);
                    BuildScreen();
                }));
        }

        // `goalPoints` is a WButton whose text is `Goal Points = ` (raw script 236 @0x0E66)
        // run through text 236[0] `"%s%3d "` â€” which has a TRAILING SPACE after the number.
        // It is there to blank the third digit's column when the total drops from 100+ back
        // to two digits, so it is not decoration and is not droppable.
        var total = viewing is null
            ? Enumerable.Range(0, 4).Sum(g => _goals[p, g])
            : viewing.MonGoal + viewing.HapGoal + viewing.EduGoal + viewing.CarGoal;
        Texts.Add(new TextVm($"Goal Points = {total,3} ", DialogX + 31, DialogY + 29, 8));

        // Mode 2 adds the four CURRENT-VALUE markers over the sliders — `select3.sc:118-122`
        // adds `currentWealth currentHappy currentEducation currentCareer` only when
        // `param2 == 2`. They are NOT on the setup screen, which is why they could not be
        // drawn there: during setup the players do not exist yet AND the game does not ask
        // for them.
        if (viewing is not null)
        {
            // view 501 loop 3, `priority 15`, nsLeft 30 / 66 / 102 / 137 — two pixels left of
            // each star slider (`select3.sc:371-501`). `nsTop` is recomputed by `draw` on
            // every one of them, so the declared 43 / 43 / 80 / 43 never survives.
            var lefts = new[] { 30, 66, 102, 137 };
            var stats = new[] { viewing.MonStat, viewing.HapStat, viewing.EduStat, viewing.CarStat };
            var goals = new[] { viewing.MonGoal, viewing.HapGoal, viewing.EduGoal, viewing.CarGoal };

            for (var g = 0; g < 4; g++)
            {
                var (cel, top) = CurrentGoalMarker(stats[g], goals[g]);
                Sprites.Add(Icon(501, 3, cel, lefts[g], top));
            }

            // DELIBERATE ADDITION, switchable — `Player.ShowPerGoalProgress`. The original
            // prints NO per-goal figure anywhere: `select3` shows progress as the position of
            // the marker above, and `viewGoals` prints one percentage per PLAYER covering all
            // four goals at once (`viewGoals.sc:393-408`). This is a house extra, asked for by
            // the player, and it is drawn only on the read-only screen — on the SETUP screen
            // there are no Players yet and every figure would read 0, which is noise.
            //
            // WHERE IT GOES, measured off view 501 loop 1 rather than eyeballed. In the band
            // the tracks occupy (y 42..87) the only painted columns are the two tick scales
            // at x 14..18 and 165..169 and the four bars at 37..40 / 73..76 / 109..112 /
            // 144..147; the star cels are 14 wide at nsLeft 32 / 68 / 104 / 139 and opaque
            // edge to edge. That leaves clear gutters at x 19..31, 46..67, 82..103 and
            // 118..138. Each figure is therefore RIGHT-ALIGNED to end two pixels before its
            // own star, at a fixed y 45 — clear of the star whatever value it shows, of the
            // "Goal Points" line above (which ends at y 40), of the bars, and of the four
            // icons along the bottom, whose tops are at y 92-95 with at most six clear rows
            // above them — too few for font 4's seven-row digits, which is why this row is up
            // here and not under the sliders.
            //
            // The one tight case, stated rather than hidden: the WEALTH figure sits in the
            // leftmost gutter, which is only 13 pixels wide, and "100" is 15. It is clamped
            // to x 19 — one pixel clear of the tick scale, which ends at 18 — so at 100% its
            // last two columns fall inside the star cel's bounding box instead. The star is
            // a star: those corner columns are transparent at most rows, and losing two
            // columns of art beats painting over the scale. Every other figure, and every
            // wealth figure below 100, sits in clear blue.
            if (Player.ShowPerGoalProgress)
            {
                for (var g = 0; g < 4; g++)
                {
                    // Font 4 is what this dialog's other text uses (`viewGoals.sc:404` and
                    // the notice Displays), so the added figure is in the game's own face.
                    var pct = viewing.GoalProgressPct(g).ToString();
                    var measured = new TextVm(pct, 0, 0, 8, "#000000", false, fontNumber: 4);

                    Texts.Add(new TextVm(pct,
                        DialogX + Math.Max(19, xs[g] - 2 - measured.W), DialogY + 45,
                        8, "#000000", false, fontNumber: 4));
                }
            }
        }

        // Whose turn it is to choose is shown by two number TOKENS in the top corners, not
        // by a caption. `playerNumber1` declares only `loop 3` and `priority 15`, so it
        // takes DIcon's default view 0 at the dialog origin; `playerNumber2` is the same
        // with `nsLeft 158`. The cel is the player index.
        //
        // The "Player N" line that used to sit here was mine — the game never writes those
        // words on this screen.
        Sprites.Add(Icon(0, 3, p, 0, 0));
        Sprites.Add(Icon(0, 3, p, 158, 0));

        // `questionButton`, view 250 loop 9 at nsLeft 124 / nsTop 108 — the `?` that opens
        // goalsDefine (script 229) and explains what the four goals mean.
        Sprites.Add(Icon(250, 9, 0, 124, 108, ShowGoalsHelp));

        // exButton: view 250 loop 8, nsTop 108 nsLeft 143. Loop 11 for the last player,
        // and `:47` puts it back on LOOP 0 in every mode but setup.
        if (viewing is not null)
        {
            Sprites.Add(Icon(250, 0, 0, 143, 108, () =>
            {
                _goalsViewPlayer = null;
                _screen = Screen.WhosWinning;
                BuildScreen();
            }));
            return;
        }

        var lastPlayer = _choosingPlayer >= _playerCount;
        Sprites.Add(Icon(250, lastPlayer ? 11 : 8, 0, 143, 108, () =>
        {
            if (_choosingPlayer >= _playerCount) StartGame();
            else { _screen = Screen.CharacterSelect; BuildScreen(); }
        }));
    }

    /// <summary>
    /// `currentWealth::draw` and its three identical siblings (`select3.sc:380-401`). The
    /// marker has two cels and two baselines and they do NOT share a scale:
    ///
    /// <code>
    /// (if (&lt; temp0 0) (= temp0 0))
    /// (if (&gt;= temp0 temp1) (= temp0 temp1) (= temp2 71) (= cel 1)
    ///  else                                  (= temp2 80) (= cel 0))
    /// (if (&gt;= (= value temp0) temp1) (= nsTop (- temp2 (* (/ (- value 10) 10) 4)))
    ///  else                           (= nsTop (- temp2 (/ (+ value 3) 4))))
    /// </code>
    ///
    /// So a goal that is MET clamps the stat to the goal, switches to cel 1, and rides a
    /// 4-pixel-per-ten-points scale from 71; a goal still short of its target sits on cel 0
    /// and a 1-pixel-per-four-points scale from 80. Both divisions truncate.
    /// </summary>
    private static (int Cel, int Top) CurrentGoalMarker(int stat, int goal)
    {
        if (stat < 0) stat = 0;

        if (stat >= goal)
        {
            var value = goal;
            return (1, 71 - SciMath.Div(value - 10, 10) * 4);
        }

        return (0, 80 - SciMath.Div(stat + 3, 4));
    }

    // ------------------------------------------------------------------
    // The intro — `introRoom`, script 2
    // ------------------------------------------------------------------

    /// <summary>One overlay `View` in a state of `introDuction`: view, loop, cel and posn.</summary>
    private sealed record IntroCel(int View, int Loop, int Cel, int X, int Y);

    /// <summary>
    /// One state of `introDuction` (`introRoom.sc:56-260`): the pic it draws, how long it
    /// holds, and every overlay that is on screen while it does.
    ///
    /// The script expresses these as changes — `(littlepic3 setLoop: 1)` with the name plate
    /// left alone at state 9, then only the name changing at state 10 — so each state here
    /// lists the full set rather than the delta, which is what is actually on screen.
    /// </summary>
    private sealed record IntroState(int Pic, int Seconds, params IntroCel[] Cels);

    /// <summary>
    /// The 20 states, transcribed one for one. `DrawPic`'s second argument is the transition
    /// style — 3, 2, 3, 2, 3, 2 across the six pics — which this port has no equivalent for
    /// and does not fake; the pic simply changes.
    ///
    /// The three character pics each carry a photograph (`littlepicN` at (162,160)) and a name
    /// plate (`littlenameN` at (160,56), or a top plate at (160,50) and a bottom one at
    /// (165,178) for the last two).
    /// </summary>
    private static readonly IntroState[] IntroStates =
    [
        new(0, 4, []),                                                          // :58-62

        new(1, 4, new(1, 0, 0, 162, 160), new(1, 0, 1, 160, 56)),               // :63-70
        new(1, 3, new(1, 1, 0, 162, 160), new(1, 1, 1, 160, 56)),               // :71-76
        new(1, 3, new(1, 2, 0, 162, 160), new(1, 2, 1, 160, 56)),               // :77-82

        new(2, 4, new(2, 0, 0, 162, 160), new(2, 0, 1, 160, 56)),               // :83-95
        new(2, 3, new(2, 1, 0, 162, 160), new(2, 1, 1, 160, 56)),               // :96-101
        new(2, 2, new(2, 2, 0, 162, 160), new(2, 2, 1, 160, 56)),               // :102-107

        new(3, 4, new(3, 0, 0, 162, 160), new(3, 0, 1, 160, 56)),               // :108-120
        new(3, 4, new(3, 0, 0, 162, 160), new(3, 0, 2, 160, 56)),               // :121-127
        new(3, 3, new(3, 1, 0, 162, 160), new(3, 1, 1, 160, 56)),               // :128-133
        new(3, 3, new(3, 1, 0, 162, 160), new(3, 1, 2, 160, 56)),               // :134-138
        new(3, 3, new(3, 2, 0, 162, 160), new(3, 2, 1, 160, 56)),               // :139-144
        new(3, 2, new(3, 2, 0, 162, 160), new(3, 2, 2, 160, 56)),               // :145-149

        new(4, 3, new(4, 0, 0, 162, 160), new(4, 0, 1, 160, 50), new(4, 0, 2, 165, 178)), // :150-163
        new(4, 2, new(4, 1, 0, 162, 160), new(4, 1, 1, 160, 50), new(4, 1, 2, 165, 178)), // :164-170
        new(4, 2, new(4, 2, 0, 162, 160), new(4, 2, 1, 160, 50), new(4, 2, 2, 165, 178)), // :171-177

        new(5, 3, new(5, 0, 0, 162, 160), new(5, 0, 1, 160, 50), new(5, 0, 2, 165, 178)), // :178-192
        new(5, 2, new(5, 1, 0, 162, 160), new(5, 1, 1, 160, 50), new(5, 1, 2, 165, 178)), // :193-199

        // State 18 HIDES both name plates and leaves the picture on its own (`:200-206`).
        new(5, 2, [new IntroCel(5, 2, 0, 162, 160)]),

        // State 19 is the voice-talent roll: twelve cels of view 5 loop 4 down the screen at
        // `(+ (* n 15) 30)`, plus loop 3's heading at (160,11) (`:207-245`).
        new(5, 6, [.. IntroVoiceTalent()]),
    ];

    private static IEnumerable<IntroCel> IntroVoiceTalent()
    {
        for (var n = 0; n < 12; n++) yield return new IntroCel(5, 4, n, 160, n * 15 + 30);
        yield return new IntroCel(5, 3, 0, 160, 11);
    }

    /// <summary>
    /// Whether the intro sequence runs on startup. **The original always does** — `Main::play`
    /// opens on room 2 — so this is a deliberate deviation, off by default at the user's
    /// request because a 50-second opening is punishing when you relaunch to test.
    ///
    /// Only the AUTOPLAY is switched; `StartIntro`, `BuildIntro` and all 20 states are
    /// intact, so setting this true restores Sierra's opening exactly.
    /// </summary>
    public static bool PlayIntro { get; set; }

    private int _introState;
    private DispatcherTimer? _introTimer;

    /// <summary>
    /// `introRoom::init` — `(self setScript: introDuction)` (`:23-27`). Skipped entirely when
    /// the art is not there, because a black screen for fifty seconds is worse than no intro.
    /// </summary>
    private void StartIntro()
    {
        if (SciArt.Pic(0) is null) { _screen = Screen.MainMenu; return; }

        _screen = Screen.Intro;
        _introState = 0;
        ScheduleIntroState();
    }

    private void ScheduleIntroState()
    {
        _introTimer ??= new DispatcherTimer();
        _introTimer.Stop();
        _introTimer.Interval = TimeSpan.FromSeconds(IntroStates[_introState].Seconds);
        _introTimer.Tick -= IntroTick;
        _introTimer.Tick += IntroTick;
        _introTimer.Start();
    }

    private void IntroTick(object? sender, EventArgs e)
    {
        _introState++;
        if (_introState >= IntroStates.Length) { EndIntro(); return; }

        ScheduleIntroState();
        BuildScreen();
    }

    /// <summary>
    /// `introRoom::handleEvent` (`:29-50`) claims any mouse or key event below y 10 and jumps
    /// the script to state 20, which disposes everything and goes to room 1 — the main menu.
    /// </summary>
    public bool IsIntroShowing => _screen == Screen.Intro;

    public void EndIntro()
    {
        if (_screen != Screen.Intro) return;

        _introTimer?.Stop();
        _screen = Screen.MainMenu;
        BuildScreen();
    }

    private void BuildIntro()
    {
        var state = IntroStates[Math.Clamp(_introState, 0, IntroStates.Length - 1)];

        var pic = SciArt.Pic(state.Pic);
        if (pic is not null) Sprites.Add(new SpriteVm(0, 0, pic));

        foreach (var c in state.Cels)
        {
            var bmp = SciArt.Cel(c.View, c.Loop, c.Cel);
            if (bmp is null) continue;

            // `View::posn` is base-centre, as everywhere else in this port.
            Sprites.Add(new SpriteVm(c.View, c.Loop, c.Cel,
                c.X - bmp.PixelSize.Width / 2.0, c.Y - bmp.PixelSize.Height));
        }
    }

    // ------------------------------------------------------------------
    // Goal definitions — `goalsDefine`, script 229
    // ------------------------------------------------------------------

    /// <summary>`local0` — which of the four goals the page is showing.</summary>
    private int _goalsHelpPage;

    /// <summary>
    /// The four paragraphs, which are STRING LITERALS IN THE SCRIPT rather than a text
    /// resource — this build has no text 229 at all. Transcribed from the raw script bytes
    /// (`assets/raw/script/229.script`) at 0x05AA, 0x0660, 0x071C and 0x07C6, which is the
    /// same route `StoreLayout` takes for the shop labels, and they agree with the decompiled
    /// listing here character for character (no embedded spaces to be mangled into `_`).
    ///
    /// `localproc_0` (`goalsDefine.sc:18-53`) switches on `local0` for the text and then
    /// plays clip `590 + local0`, so the paragraph is spoken as well as printed.
    /// </summary>
    private static readonly string[] GoalHelpText =
    [
        "Wealth is defined as the total accumulation of money, savings, and investments. Try the stock market, and be on the watch for Wild Willy. And most importantly... sorry, out of room.",
        "Happiness is accumulated by acquiring goods, achieving goals, taking time off from work, and helping little old ladies cross the street so that they don't get hit by any speeding marbles.",
        "Education is accumulated by attending the university and graduating from the classes offered. A computer and some reference books can be very beneficial to your studies.",
        "Career is achieved by working hard, climbing the corporate ladder, improving your skills, dependability, and advancing your education. Remember, hire a kid, they have all the answers.",
    ];

    private void ShowGoalsHelp()
    {
        _goalsHelpPage = 0;
        _screen = Screen.GoalsHelp;

        // `(DoAudio audPLAY 590)` at `:95`, and `(gASong fade: …)` immediately after, so the
        // title bed drops under the narration rather than being cut.
        Sound.PlaySpeech(590);
        BuildScreen();
    }

    /// <summary>
    /// `goalsDefine` (script 229). Its own instance blocks give every coordinate:
    ///
    /// <code>
    /// background   view 501 loop 4                       (:140-145)
    /// theTitle     view 501 loop 5  cel local0  41,  4    (:208-215)
    /// corner1..4   view 501 loop 6  cel local0   0,0 / 147,0 / 0,88 / 147,88   (:176-206)
    /// textOne      DText font 4 at 6,30, dsWIDTH 171, alCENTER, dsCOLOR 0      (:217-245)
    /// rightArrow   view 250 loop 8  47,108                (:157-174)
    /// doneButton   view 250 loop 2 106,108                (:147-155)
    /// </code>
    ///
    /// It is PAGED, one goal at a time: `rightArrow::doit` increments `local0`, wraps past 3
    /// back to 0, and redraws (`:166-173`). The title plate and all four corner pieces are
    /// cels of the SAME index, so the whole frame changes colour with the page.
    /// </summary>
    private void BuildGoalsHelp()
    {
        TitleBackdrop();

        Sprites.Add(Icon(501, 4, 0, 0, 0));
        Sprites.Add(Icon(501, 5, _goalsHelpPage, 41, 4));
        Sprites.Add(Icon(501, 6, _goalsHelpPage, 0, 0));
        Sprites.Add(Icon(501, 6, _goalsHelpPage, 147, 0));
        Sprites.Add(Icon(501, 6, _goalsHelpPage, 0, 88));
        Sprites.Add(Icon(501, 6, _goalsHelpPage, 147, 88));

        // `Display … dsFONT 4 dsCOORD 6 30 dsCOLOR 0 dsWIDTH 171 dsALIGN alCENTER` — the
        // paragraph is wrapped to a 171-wide column and each line centred inside it, which
        // is the same shape as the newspaper headline's own Display.
        var font = SciFont.Load(4);
        const int left = 6, top = 30, width = 171;

        var lines = new List<string>();
        var line = "";
        foreach (var word in GoalHelpText[_goalsHelpPage].Split(' '))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && (font?.Measure(candidate) ?? 0) > width)
            {
                lines.Add(line);
                line = word;
            }
            else line = candidate;
        }
        if (line.Length > 0) lines.Add(line);

        var lineHeight = font?.Height ?? 9;
        for (var i = 0; i < lines.Count; i++)
        {
            var indent = (width - (font?.Measure(lines[i]) ?? 0)) / 2;
            Texts.Add(new TextVm(lines[i], DialogX + left + indent, DialogY + top + i * lineHeight,
                                 fontNumber: 4, colour: "#000000"));
        }

        Sprites.Add(Icon(250, 8, 0, 47, 108, () =>
        {
            // `(if (> (++ local0) 3) (= local0 0))` — it wraps rather than stopping.
            _goalsHelpPage = _goalsHelpPage + 1 > 3 ? 0 : _goalsHelpPage + 1;
            Sound.PlaySpeech(590 + _goalsHelpPage);
            BuildScreen();
        }));

        Sprites.Add(Icon(250, 2, 0, 106, 108, () =>
        {
            // `(DoAudio audSTOP)` at `:115` before the dialog closes.
            Sound.StopSpeech();
            _screen = Screen.GoalSetting;
            BuildScreen();
        }));
    }

    /// <summary>
    /// newspaper (script 215). The paper is view 603 at nsLeft 56, nsTop 35; the
    /// bottom strip is view 603 loop 1 cel 1 at nsTop 108, and Done is view 250 loop 2
    /// at nsLeft 143, nsTop 108.
    ///
    /// The headline is whatever the economy published this week â€” the same `global415`
    /// the indices set, which is why crashes and commodity swings read as news.
    /// </summary>
    private void BuildNewspaper()
    {
        // The paper flies in first: view 603 loop 0 is a six-frame 80x55 animation at
        // nsLeft 56, nsTop 35, played once (`setCycle: End`, cycleSpeed 1). Only when it
        // finishes does the full page and its headline appear.
        if (_newsFrame < NewsFrames)
        {
            Sprites.Add(Icon(603, 0, _newsFrame, 56, 35));
            return;
        }

        // Loop 1 is the full 183x112 page. Cel 1 is the blank sheet the script names as
        // the dialog background; cel 0 carries the masthead and columns, which is what
        // the player actually sees behind the headline.
        Sprites.Add(Icon(603, 1, 0, 0, 0));

        // `newspaperText::draw` (newspaper.sc:246-265) typesets the headline itself:
        //
        //     (TextSize @[temp0 0] text 3 0)
        //     (Display text dsCOORD 11 (- 43 (/ [temp0 2] 2))
        //                  dsCOLOR 0 dsWIDTH 155 dsBACKGROUND -1
        //                  dsFONT 3 dsALIGN alCENTER)
        //
        // So: font 3, a 155-wide column at x=11, centred, and vertically CENTRED about
        // y=43 rather than started there â€” the block is measured first and half its
        // height subtracted. Every value here was wrong before: font 8, x=14, a fixed
        // y=40 stepping 10, and a wrap at 20 CHARACTERS.
        //
        // The line breaks come from the headline itself. 48 of the 64 strings in text
        // resource 215 carry an embedded newline placed by hand, which is where the
        // masthead's two-deck headlines come from. The port had replaced each of those
        // with a space and re-wrapped by character count, so nearly every issue broke in
        // the wrong place.
        var text = Headlines.Get(_headlineToShow);
        var font = SciFont.Load(3);

        const int columnLeft = 11, columnWidth = 155, centreY = 43;

        // Honour the authored breaks first, then wrap anything still too wide for the
        // column, as Display does.
        var lines = new List<string>();
        foreach (var authored in text.Split('\n'))
        {
            var line = "";
            foreach (var word in authored.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && (font?.Measure(candidate) ?? 0) > columnWidth)
                {
                    lines.Add(line);
                    line = word;
                }
                else line = candidate;
            }
            lines.Add(line);
        }

        var lineHeight = font?.Height ?? 10;
        var top = centreY - lines.Count * lineHeight / 2;

        for (var i = 0; i < lines.Count; i++)
        {
            // alCENTER, within the 155-wide column.
            var indent = (columnWidth - (font?.Measure(lines[i]) ?? 0)) / 2;
            Texts.Add(new TextVm(lines[i], DialogX + columnLeft + indent,
                                 DialogY + top + i * lineHeight,
                                 8, "#000000", false, fontNumber: 3));
        }

        // doneButton: view 250 loop 2 at nsLeft 143, nsTop 108. It goes back through
        // ShowNewspaperOrBoard so that the single hand-off to the board stays the single
        // place a Jones-controlled turn is started from; the paper it was printing has
        // already been cleared, so nothing else changes.
        Sprites.Add(Icon(250, 2, 0, 143, 108, ShowNewspaperOrBoard, "Done"));
    }

    /// <summary>
    /// weekend (script 232). Background view 608; the player's name is displayed at
    /// (13,35) and the weekend text at (13,47), both centred over a 155-wide column in
    /// font 4, with "You spent $N." at (59,100). Exit is view 250 loop 0 at (143,108).
    /// </summary>
    private void BuildWeekend()
    {
        Sprites.Add(Icon(Weekend.BackgroundView, 0, 0, 0, 0));

        var r = _weekendResult;
        if (r is null) { _screen = Screen.Board; BuildScreen(); return; }

        Texts.Add(new TextVm(P.ActualName, DialogX + 13, DialogY + 35, 7, "#000000", true));

        // Centre-wrapped over the 155-wide column the original uses.
        var y = 47;
        var line = "";
        foreach (var word in Weekend.TextFor(r.TextId).Split(' '))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (candidate.Length > 30)
            {
                Texts.Add(new TextVm(line, DialogX + 13, DialogY + y, 7, "#000000"));
                y += 8;
                line = word;
            }
            else line = candidate;
        }
        if (line.Length > 0)
            Texts.Add(new TextVm(line, DialogX + 13, DialogY + y, 7, "#000000"));

        if (r.Cost > 0)
            Texts.Add(new TextVm($"You spent ${r.Cost}.", DialogX + 59, DialogY + 100, 7, "#000000"));

        // The weekend hands on to the rest of `startTrn`, which may still have a paper to
        // print before the board comes back.
        Sprites.Add(Icon(250, 0, 0, 143, 108, ShowNewspaperOrBoard));
    }

    /// <summary>
    /// broker (script 213). Background view 696; the six instruments are view 696 loop 1
    /// cels 0-5 down the left at nsLeft 8, nsTop 27/40/53/66/79/92, with the market price
    /// at x 109 and your holdings right-aligned at x 142 + the icon's height.
    /// Buy is view 250 loop 4 at (7,108), Sell loop 5 at (77,108), Exit loop 0 at (143,108).
    /// </summary>
    private void BuildBroker()
    {
        Sprites.Add(Icon(696, 0, 0, 0, 0));

        Texts.Add(new TextVm("Market", DialogX + 112, DialogY + 8, 7, "#000000"));
        Texts.Add(new TextVm("Value",  DialogX + 115, DialogY + 16, 7, "#000000"));
        Texts.Add(new TextVm("Your",   DialogX + 154, DialogY + 8, 7, "#000000"));
        Texts.Add(new TextVm("Holdings", DialogX + 147, DialogY + 16, 7, "#000000"));

        var tops = new[] { 27, 40, 53, 66, 79, 92 };
        var instruments = new[]
        {
            Instrument.TBills, Instrument.Gold, Instrument.Silver,
            Instrument.Pork, Instrument.BlueChip, Instrument.Penny,
        };

        for (var i = 0; i < instruments.Length; i++)
        {
            var inst = instruments[i];
            var price = Holdings.UnitPrice(inst, _game!.Economy);
            var shares = P.Holdings.SharesOf(inst);

            // The instrument's own icon, which is what you click to select it.
            Sprites.Add(Icon(696, 1, i, 8, tops[i], () =>
            {
                _brokerSelected = inst;
                BuildScreen();
            }));

            Texts.Add(new TextVm($"${price}", DialogX + 109, DialogY + tops[i] + 4, 7,
                                 _brokerSelected == inst ? "#0000C0" : "#000000"));
            // The holdings figure is a WButton — `tBillsHoldings` and its five siblings,
            // nsLeft 142, nsTop = the icon's + 4, `state 288` (bit 0 clear: a caption) and
            // `shadowColor 93` with the class's textColor 0 (`broker.sc:339-343` and
            // `:367-371`, `:395-399`, `:423-427`, `:451-455`, `:479-483`). So it carries the
            // drop shadow every other label does.
            Texts.Add(new TextVm($"${(long)shares * price}", DialogX + 142, DialogY + tops[i] + 4, 7,
                                 shadow: StoreLayout.BrokerColours.Shadow));
        }

        // One share per click, no commission â€” except a flat $3 when selling a T-bill.
        // `buyButton key 98` = `b`, `sellButton key 106` = `j` (`broker.sc:504-508`,
        // `:547-551`). `j` is what the instance declares; see SciKey for why it is ported
        // as declared rather than "corrected" to `s`.
        void BuyOne()
        {
            Broker.Buy(P, _brokerSelected, 1, _game!.Economy);
            P.RecalculateGoals(_game!.Economy);
            BuildScreen();
        }

        void SellOne()
        {
            Broker.Sell(P, _brokerSelected, 1, _game!.Economy);
            P.RecalculateGoals(_game!.Economy);
            BuildScreen();
        }

        Sprites.Add(Icon(250, 4, 0, 7, 108, BuyOne));
        Sprites.Add(Icon(250, 5, 0, 77, 108, SellOne));
        Accelerator(98, BuyOne);
        Accelerator(106, SellOne);

        Sprites.Add(Icon(250, 0, 0, 143, 108, () =>
        {
            _screen = Screen.LocationPanel;
            BuildScreen();
        }));
    }

    /// <summary>
    /// "Who's Winning" — `viewGoals` (script 238). One column per player: a coarse
    /// thermometer, a fine marker riding on top of it, the player's number token and a
    /// percentage caption. Reached by F6, the middle mouse button or Ctrl-left
    /// (`Menu.sc:145-157` and `:390-395`, and the game's own help text 997[7]).
    ///
    /// Opens on `global38`, the INVISIBLE window, `moveTo: 69 44` (`viewGoals.sc:154-160`),
    /// so the board stays behind it like the weekend and the newspaper.
    /// </summary>
    private void BuildWhosWinning()
    {
        if (_game is null) return;

        // `background`, view 505 with no loop or cel — DIcon's defaults, at the dialog
        // origin (`viewGoals.sc:199-203`). It is exactly dialog-sized, 183x112.
        Sprites.Add(Icon(505, 0, 0, 0, 0));

        var players = _game.Players;

        // `viewGoals.sc:119-153`. With fewer than four columns the whole block is nudged
        // right so it stays centred: every element of every shown column gets the same
        // offset. There is no case 4 in the switch, so four players sit where the instances
        // declare them.
        var shift = players.Count switch { 1 => 65, 2 => 42, 3 => 20, _ => 0 };

        for (var i = 0; i < players.Count && i < 4; i++)
        {
            var p = players[i];
            var pct = p.GoalProgressPct();

            // The columns are 44px apart; the instance blocks give column 1's nsLeft for
            // each element and each later instance is exactly +44 from the last
            // (`viewGoals.sc:307-385`, `:216-305`, `:387-481`).
            var col = 44 * i + shift;

            // `therm.cel: (/ pct 10)` — loop 1 holds eleven cels, empty through full.
            var thermCel = SciMath.Div(pct, 10);
            Sprites.Add(Icon(505, 1, thermCel, 21 + col, 35));

            // The fine marker rides UP the bar as it fills: its nsTop is recomputed from the
            // coarse cel, and its own cel is the leftover tenth in five sub-steps
            // (`viewGoals.sc:87-88`).
            Sprites.Add(Icon(505, 2, SciMath.Div(pct % 10, 2),
                             22 + col, 83 - thermCel * 5));

            // `playerNumber.cel: (player whatNum:)`. `whatNum` is the player's INDEX in the
            // players list, not the body they chose — `players::init` (`room1.sc:960-965`)
            // assigns it after deleting everyone who is not playing, and substitutes 4 for
            // a Jones-driven player.
            //
            // `viewGoals.sc:135-137` forces cel 4 again for player 2 when that player has
            // `playJones`. It is redundant: `select4.sc:26` sets `playJones` on the same
            // player whose `playing` it has already set to 29 (`select4.sc:268`), so
            // `players::init` has made `whatNum` 4 before this screen ever runs. Ported as
            // the one thing it can be here — this port has a single Jones flag — and left
            // in place rather than tidied away.
            var whatNum = p.IsJones ? 4 : i;

            // Each token is an ErasableDIcon whose `doit` swaps `global302` to that player,
            // opens select3 in MODE 2 — the same goal screen, read-only, with the current
            // value markers over the sliders — and swaps it back (`viewGoals.sc:226-235`).
            var seat = i;
            Sprites.Add(Icon(505, 3, whatNum, 18 + col, 90, () =>
            {
                _goalsViewPlayer = seat;
                _screen = Screen.GoalSetting;
                BuildScreen();
            }));

            // `(Format @temp0 238 0 value)` with text 238[0] = "%3d~" — right-aligned in
            // three columns, and `~` is the percent sign in font 4 (glyph 0x7E). Font 4,
            // colour 0 (`viewGoals.sc:393-408`).
            //
            // The `dsBACKGROUND (if global535 99 else 9)` fill is not drawn: it exists to
            // blank the previous number in place, and this port rebuilds the screen instead.
            // `BuildGoalSetting` already omits the identical fill at `select3.sc:355-366`.
            Texts.Add(new TextVm($"{pct,3}~", DialogX + 14 + col, DialogY + 25,
                                 fontNumber: 4, colour: "#000000"));
        }

        // `exitButton`, view 250 with no loop — loop 0 — at the usual (143,108).
        Sprites.Add(Icon(250, 0, 0, 143, 108, CloseWhosWinning));
    }

    /// <summary>Where the goals screen was opened from, so closing it goes back there.</summary>
    private Screen _screenBehindGoals = Screen.Board;

    /// <summary>
    /// `proc997_2` (`Menu.sc:80-116`). The Goals menu item is `SetMenu 1026 112` — enabled
    /// on the board (`room1.sc:1300`) and switched OFF for the whole turn-start sequence
    /// (`startTrn.sc:347`, `:641`), the market's buying loop (`market.sc:349`), graduation
    /// (`university.sc:220`) and the winner script (`winnerScript.sc:32`).
    ///
    /// `global509` is the re-entry latch: `viewGoals::init` clears it on the way in and sets
    /// it again on the way out, and a second request while it is clear only prints text
    /// 997[17] "Another Goals Screen is not allowed." Here that is simply refusing to open
    /// on top of itself.
    /// </summary>
    public bool CanShowGoalsScreen =>
        _remoteView ? _remoteCanGoals
                    : _game is not null && _screen is Screen.Board or Screen.LocationPanel;

    public void ShowGoalsScreen()
    {
        if (!CanShowGoalsScreen) return;

        _screenBehindGoals = _screen;
        _screen = Screen.WhosWinning;
        BuildScreen();
    }

    private void CloseWhosWinning()
    {
        _screen = _screenBehindGoals;
        BuildScreen();
    }

    // ------------------------------------------------------------------
    // Statistics / net worth — `inventories`, script 231
    // ------------------------------------------------------------------

    /// <summary>`invSelector`'s `x` — the record width, in CHARACTERS (`inventories.sc:243`).</summary>
    private const int StatsColumns = 25;

    /// <summary>`invSelector`'s `y` — how many records are on screen at once (`:244`).</summary>
    private const int StatsRows = 7;

    /// <summary>The first visible record — the selector's `topString` (`Interface.sc:630`).</summary>
    private int _statsTop;

    private Screen _screenBehindStats = Screen.Board;

    /// <summary>
    /// `localproc_0` (`inventories.sc:19-149`). THE WHOLE SCREEN IS ONE STRING: every line is
    /// a `Format` appended to `@local0` through `StrEnd`, and the result is handed to a
    /// `DSelector` whose `x` is 25 — so the buffer is a run of fixed 25-CHARACTER records and
    /// the widget's only job is to show seven of them at a time.
    ///
    /// Every one of the sixteen format strings in text resource 231 is exactly 25 wide once
    /// filled: `Works at %-16s` is 9 + 16, `Hourly wage: $%-11d` is 14 + 11, `%=25s` is 25 on
    /// its own. That is the whole layout — the columns line up by PADDING, not by position,
    /// which is why this is built as strings rather than as positioned labels.
    ///
    /// `%=25s` is SCI's centring width, handled by <see cref="Audio.Subtitles.Format"/>
    /// alongside `%-Ns`.
    /// </summary>
    private List<string> StatsRecords()
    {
        var lines = new List<string>();
        void Add(string s) => lines.Add(s);

        // `(global302 calcNetWorth:)` — `:20`, before anything is read.
        var worth = P.CalcNetWorth(_game!.Economy);

        Add(SciText.Get(231, 0, P.ActualName));                       // "%=25s"

        if (P.WorksAt != 0)
        {
            // `700 (+ (global302 worksAt:) 71)` — the employer's `placeNum` plus 71 is its
            // name in text 700. This port stores `(int)Workplace + 1` instead of `placeNum`,
            // so the place is fetched back through the board.
            var place = Board.ForWorkplace((Workplace)(P.WorksAt - 1));
            Add(SciText.Get(231, 1, SciText.Get(700, (place?.PlaceNum ?? 0) + 71)));
            Add(SciText.Get(231, 2, SciText.Get(700, P.Occupation)));  // "As a %-20s"
            Add(SciText.Get(231, 3, P.Wage));                          // "Hourly wage: $%-11d"
        }
        else
        {
            // `(Format … 231 4 231 5)` — `%-25s` fed text 231[5], `Unemployed`.
            Add(SciText.Get(231, 4, SciText.Get(231, 5)));
        }

        // `proc115_0` (`n115.sc:23-67`) renders a 32-bit hi/lo pair as a plain decimal
        // string, which is why these four are `%s` and the three below them are `%d`.
        Add(SciText.Get(231, 6, P.Cash));
        Add(SciText.Get(231, 7, P.BankBal));
        Add(SciText.Get(231, 8, P.RentOwed));
        Add(SciText.Get(231, 9, P.LoanBal));

        // `temp3`: every durable's purchase price times the quantity held (`:26-33`).
        var goodsTotal = P.Durables.Items.Sum(d => (long)d.PricePaid * d.Quantity);
        Add(SciText.Get(231, 10, goodsTotal));

        Add(SciText.Get(231, 11, P.InvAss));
        Add(SciText.Get(231, 12, worth));

        Add(SciText.Get(231, 0, SciText.Get(231, 13)));                // "-----GOODS-----"

        // The three clothing lines, in the script's own order: consumable 36, then 35, then
        // 34 (`:70-90`), each printed through `%=25s` from its own heading string. The extra
        // `temp4` argument the script passes is surplus to the single `%s` and is dropped.
        foreach (var (id, index) in new[]
                 {
                     (ItemIds.CasualClothes, 14),
                     (ItemIds.DressClothes, 15),
                     (ItemIds.BusinessSuit, 16),
                 })
        {
            if (P.Consumables.AtHeld(id) is not null)
                Add(SciText.Get(231, 0, SciText.Get(231, index)));
        }

        // "%3d Weeks of Food        "
        if (P.Consumables.AtHeld(ItemIds.FreshFood) is { } food)
            Add(SciText.Get(231, 17, food.Quantity));

        // "%3d %-21s", the name out of text 700 at the durable's own indexNum.
        foreach (var d in P.Durables.Items)
            if (d.Quantity > 0)
                Add(SciText.Get(231, 18, d.Quantity, SciText.Get(700, d.IndexNum)));

        Add(SciText.Get(231, 0, SciText.Get(231, 19)));                // "---EDUCATION---"

        // `(if (global302 numDegrees:) …)` then, per education entry, `hasDegree:` —
        // the courses still in progress are not listed.
        if (P.NumDegrees() > 0)
        {
            foreach (var e in P.Education.Items)
                if (e.Quantity >= e.UnitsToGraduate)
                    Add(SciText.Get(231, 4, SciText.Get(700, e.IndexNum)));
        }

        Add(SciText.Get(231, 0, SciText.Get(231, 20)));                // "--INVESTMENTS--"

        // A NESTED Format: `231 21` is `%d %s` (shares and the instrument's text-700 name),
        // and the result is then poured into `231 4`'s `%-25s` (`:134-147`). The six
        // instruments are text 700[65..70] in `Instrument`'s own order.
        foreach (var (instrument, shares) in P.Holdings.AllHeld)
        {
            if (shares <= 0) continue;
            var name = SciText.Get(700, 65 + (int)instrument);
            Add(SciText.Get(231, 4, SciText.Get(231, 21, shares, name)));
        }

        return lines;
    }

    /// <summary>
    /// `inventories` (script 231). `window: global38`, `moveTo: 69 44`, so the board shows
    /// behind it; `background` is view 505 LOOP 4 (`:222-227`) and `doneButton` is view 250
    /// loop 2 at the usual (143,108) (`:229-237`).
    ///
    /// SIMPLIFIED, stated: `invSelector` is a kernel `DSelector` control, which draws its own
    /// frame — `setSize` reserves 20 pixels for it on top of `y` rows of the font's height
    /// (`Interface.sc:651-657`). This draws the seven rows at the selector's own
    /// `moveTo: 17 20` at font 4's 9-pixel pitch and no frame, which is exactly what the
    /// Pawn Shoppe's list already does for the same widget.
    /// </summary>
    private void BuildStats()
    {
        if (_game is null) return;

        Sprites.Add(Icon(505, 4, 0, 0, 0));

        var records = StatsRecords();

        // The selector cannot scroll past its last record (`advance:` breaks on the NUL).
        var maxTop = Math.Max(0, records.Count - StatsRows);
        if (_statsTop > maxTop) _statsTop = maxTop;

        for (var row = 0; row < StatsRows; row++)
        {
            var i = _statsTop + row;
            if (i >= records.Count) break;

            // Trailing padding is invisible but load-bearing above; trim it for rendering
            // only, so a right-hand edge of spaces cannot widen the measured line.
            var text = records[i].TrimEnd();
            if (text.Length == 0) continue;

            Texts.Add(new TextVm(text, DialogX + 17, DialogY + 20 + row * 9,
                                 fontNumber: 4, colour: "#000000"));
        }

        // `doneButton`: view 250 LOOP 2 — the same Done the newspaper uses, not the plain
        // exit arrow (`inventories.sc:229-236`).
        Sprites.Add(Icon(250, 2, 0, 143, 108, CloseStats));
    }

    /// <summary>
    /// `proc997_1` (`Menu.sc:42-78`), the F4 / right-mouse / Shift-left entry point. Gated on
    /// `GetMenu 1025 112` exactly as the goals screen is gated on 1026 — the pair are enabled
    /// and disabled together everywhere in the game (`startTrn.sc:347-352`, `:641-642`,
    /// `market.sc:348-349`), so this shares
    /// <see cref="CanShowGoalsScreen"/>'s condition.
    /// </summary>
    public bool CanShowStatsScreen => CanShowGoalsScreen;

    public void ShowStatsScreen()
    {
        if (!CanShowStatsScreen) return;

        _screenBehindStats = _screen;
        _statsTop = 0;
        _screen = Screen.Stats;
        BuildScreen();
    }

    private void CloseStats()
    {
        _screen = _screenBehindStats;
        BuildScreen();
    }

    /// <summary>
    /// `DSelector::handleEvent` (`Interface.sc:711-760`): Up and Down move one record, Page
    /// Up and Page Down move `y - 1`, Home and End run to the ends (`:734-745`).
    /// </summary>
    private bool HandleStatsKey(string key)
    {
        var records = StatsRecords().Count;
        var maxTop = Math.Max(0, records - StatsRows);

        int? top = key switch
        {
            "Up" => _statsTop - 1,
            "Down" => _statsTop + 1,
            "PageUp" => _statsTop - (StatsRows - 1),
            "PageDown" => _statsTop + (StatsRows - 1),
            "Home" => 0,
            "End" => maxTop,
            _ => null,
        };

        if (top is not { } to) return false;

        _statsTop = Math.Clamp(to, 0, maxTop);
        BuildScreen();
        return true;
    }

    private Instrument _brokerSelected = Instrument.TBills;

    /// <summary>Which player has already been greeted where (the proc0_14 latch).</summary>
    private readonly HashSet<(int Player, LocationId Where)> _greeted = [];

    // --- The shopkeeper speaking ---------------------------------------
    private int _talkerCel;
    private Audio.LipSync.Frame[] _mouth = [];
    private int _mouthNext;
    private DateTime _speechStarted;
    private bool _sawAudioPosition;
    private DispatcherTimer? _mouthTimer;

    /// <summary>
    /// How long to wait for a clip that never starts before giving up on it. Only reached
    /// when the audio device never reports a position at all (missing file, dead device).
    /// </summary>
    private static readonly TimeSpan SpeechStartTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The "Reading Speed" gauge, `global426`. `Main.sc:549` sets it to 5 and
    /// `Menu.sc:268-283` lets the player move it between 2 and 15. The balloon closes
    /// this many whole seconds after it opened (`Interface.sc:865`, `:942-952`).
    /// </summary>
    private const int ReadingSpeedSeconds = 5;

    private DispatcherTimer? _balloonTimer;
    private DateTime _balloonOpenedAt;

    /// <summary>
    /// Plays a line and moves the shopkeeper's mouth in step with it, using the clip's
    /// own sync resource. The line is also drawn in the game's own speech balloon, so it
    /// is readable with the sound off or by a deaf player â€” the floppy build printed
    /// every one of these lines rather than speaking them.
    /// </summary>
    /// <param name="args">
    /// Values for the line's printf placeholders, where it has any â€” 17 of the subtitles
    /// are format strings because the text they came from was built with `Format`. See
    /// <see cref="Audio.Subtitles.For(int, object[])"/>.
    /// </param>
    private void Speak(int audioId, params object[] args)
    {
        // The SUBTITLE and the mouth carry on when speech is muted: the floppy build
        // printed every one of these lines, so a silent game is still a playable one.
        if (!SpeechOff) Sound.PlaySpeech(audioId); else MutedHere.PlaySpeech(audioId);

        SpokenLine = Audio.Subtitles.For(audioId, args);
        OnPropertyChanged(nameof(SpokenLine));

        // Dismissal is a click, or the reading-speed timer â€” but NOT while the clip is
        // still playing. The floppy's 5 seconds was tuned to its own printed text; the CD
        // recordings are frequently longer, so obeying the timer literally snatched the
        // subtitle away mid-sentence, which defeats the point of having it for a player
        // reading rather than listening. The balloon therefore closes at the LATER of the
        // reading timer and the end of the clip. Clicking still closes it at any time
        // (`Interface.sc:1108-1125`).
        _balloonOpenedAt = DateTime.UtcNow;
        _balloonTimer ??= new DispatcherTimer();
        _balloonTimer.Stop();
        _balloonTimer.Interval = TimeSpan.FromMilliseconds(250);
        _balloonTimer.Tick -= BalloonTick;
        _balloonTimer.Tick += BalloonTick;
        if (SpokenLine.Length > 0) _balloonTimer.Start();

        StartMouth(audioId);
    }

    /// <summary>
    /// The lip-sync half of <see cref="Speak"/>, split out so a question can play its clip
    /// and move the mouth while its own balloon — the one carrying the buttons — stays up.
    /// </summary>
    private void StartMouth(int audioId)
    {
        _mouth = Audio.LipSync.For(audioId);
        _mouthNext = 0;
        _talkerCel = 0;
        if (_mouth.Length == 0) return;

        _speechStarted = DateTime.UtcNow;
        _sawAudioPosition = false;
        _mouthTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _mouthTimer.Tick -= MouthTick;
        _mouthTimer.Tick += MouthTick;
        _mouthTimer.Start();
    }

    /// <summary>
    /// A question asked by the shopkeeper HIMSELF: one balloon carrying the spoken line and
    /// the buttons together, rather than a speech balloon followed by a second, shorter box.
    ///
    /// The FLOPPY build is the authority on what that balloon says, because it prints what
    /// the CD speaks. `jones-dos-1.000.060/src/university.sc:746` is a single `Print`:
    ///
    /// <code>
    /// (Print (Format @global100 207 28 (enrollmentFee price:))
    ///        310 global413 global440 global441 global442   ; talker + tail
    ///        70 113                                        ; #width 113
    ///        81 {Yes} 1  81 {No} 0)
    /// </code>
    ///
    /// — the long line, the tail, the width and both buttons in one dialog. The same shape
    /// appears at `bank.sc:474` (width 110), `pawnShop.sc:780` (width 107) and
    /// `rentOffice.sc:524` / `:647` (width 150). The CD build replaced the printed sentence
    /// with a recording and left a SHORT prompt behind in its own text resource (CD 207[1]
    /// `Enroll for $%d?`, CD 201[0] `Rent Low-Cost Apartment?`); reading those as the thing
    /// to print produced two balloons where the game has one.
    ///
    /// The line here is therefore the clip's own subtitle — which is the floppy string, since
    /// that is where the subtitles came from — and the buttons sit on it.
    /// </summary>
    private void SpeakQuestion(int audioId, int width, Action<int> answered,
                               object[] args, params BalloonButton[] buttons)
    {
        if (!SpeechOff) Sound.PlaySpeech(audioId); else MutedHere.PlaySpeech(audioId);

        // No SpokenLine: the balloon is the QUESTION's, so the reading-speed timer must not
        // take it down while the player is still deciding. `Dialog::handleEvent` does the
        // same thing — its claim-anything branch is skipped once a selectable button exists
        // (`Interface.sc:1108-1125`), so a question waits however long it waits.
        Ask(Audio.Subtitles.For(audioId, args), width, answered, buttons);

        StartMouth(audioId);
    }

    /// <summary>
    /// The Employment Office's answer, spoken. `proc206_1` (`employment.sc:30-58`) is the
    /// whole of it: the outcome code goes into `global433` and the clerk says
    /// `420 + global433` for anything from 0 up, or `434 + n` for a refusal, where `n` is
    /// the failed requirement drawn in <see cref="Employment.DrawRejectionReason"/>.
    ///
    /// Nothing is PRINTED. The six sentences that used to appear here â€” "Hired as Clerk!",
    /// "Not enough education." and the rest â€” were mine; the real lines are recordings
    /// 424 and 434-437, whose subtitles the balloon already carries.
    ///
    /// The whole 420..425 range is now reachable, because <see cref="Game.ApplyFor"/> hands
    /// back `global433` itself: 420-423 are the four raise answers (`employment.sc:176-197`),
    /// 424 is the hire, and 425 is "we're closing, come back next week" when the clock has
    /// run out (`employment.sc:167`).
    /// </summary>
    private void SpeakJobOutcome(ApplicationCode code)
    {
        if (code == ApplicationCode.Refused)
        {
            if (_game?.LastRejection is not { } reason) return;

            // `(gASong pause: 1)` `(gASoundEffect play: 44 gASong)` â€” employment.sc:35-36.
            // The Employment Office's bed ducks under the refusal and comes back when it
            // ends. Clicking anything else while the sting plays replaces it on the effect
            // slot and the cue is lost, so the bed stays down until the next `playBed:` â€”
            // `Sound::play` clears `client` when called with one argument (Sound.sc:49),
            // so that is the original's behaviour too, not something added here.
            DuckedSting(Audio.SoundEffects.BadNews);
            Speak(434 + (int)reason);         // 434 education, 435 history, 436 experience, 437 no openings
            return;
        }

        // `(if (or (== global433 2) (== global433 4)) (gASong pause: 1)
        //     (gASoundEffect play: 45 gASong))` â€” employment.sc:51-53. The sting under the
        // good news, i.e. under a granted raise and under a hire.
        if (code is ApplicationCode.RaiseGranted or ApplicationCode.Hired)
            DuckedSting(Audio.SoundEffects.GoodNews);

        Speak(420 + (int)code);
    }

    private void MouthTick(object? sender, EventArgs e)
    {
        // The mouth follows the AUDIO's own clock, not a wall clock started when Play()
        // returned. Sound can begin well after that call â€” device wake-up plus buffering â€”
        // and timing the animation from the call ran the whole mouth to a standstill
        // before a word came out. While the clip is still spinning up the position stays
        // at 0 here and the mouth simply waits for it.
        var position = Sound.SpeechPosition;
        int elapsed;

        if (position is { } pos)
        {
            _sawAudioPosition = true;
            elapsed = (int)pos.TotalMilliseconds;
        }
        else if (_sawAudioPosition)
        {
            // Had a position, now has none: the clip has played out. Run the remaining
            // frames so the mouth always finishes closed rather than mid-syllable.
            elapsed = int.MaxValue;
        }
        else if (SpeechOff || !Sound.Enabled)
        {
            // Sound off, or speech muted from the toggle: nothing will ever report a
            // position, so drive the mouth and the subtitle from the wall clock. The
            // animation is the whole point for a player reading subtitles.
            elapsed = (int)(DateTime.UtcNow - _speechStarted).TotalMilliseconds;
        }
        else if (DateTime.UtcNow - _speechStarted > SpeechStartTimeout)
        {
            // Sound is on but the clip never started. Abandon it rather than hang.
            elapsed = int.MaxValue;
        }
        else
        {
            // Waiting for playback to begin. Mouth stays shut.
            return;
        }

        var changed = false;
        while (_mouthNext < _mouth.Length && _mouth[_mouthNext].AtMs <= elapsed)
        {
            _talkerCel = _mouth[_mouthNext].Cel;
            _mouthNext++;
            changed = true;
        }

        if (_mouthNext >= _mouth.Length)
        {
            // Finished talking: close the mouth and take the balloon down with it.
            _mouthTimer?.Stop();
            _talkerCel = 0;
            CloseBalloon(rebuild: false);
            changed = true;
        }

        if (changed) BuildScreen();
    }

    private void BalloonTick(object? sender, EventArgs e)
    {
        // Hold the balloon until the reading time has elapsed AND the clip has finished
        // speaking â€” see the note in Speak(). _mouthTimer still running means the sync
        // table has frames left to play, which covers the clips that have no position to
        // report (sound off, or a head with no audio player wired up).
        if (DateTime.UtcNow - _balloonOpenedAt < TimeSpan.FromSeconds(ReadingSpeedSeconds))
            return;

        if (Sound.SpeechPosition is not null) return;
        if (_mouthTimer is { IsEnabled: true }) return;

        CloseBalloon();
    }

    /// <summary>
    /// Takes the balloon down. `Dialog:handleEvent` (`Interface.sc:1108-1125`) claims any
    /// mouse button, Enter, joystick button or Esc for a text-only `Print` and returns -1,
    /// which breaks `Dialog:doit` and disposes the window; the reading-speed timer does
    /// the same thing via `cue:`.
    /// </summary>
    private void CloseBalloon(bool rebuild = true)
    {
        _balloonTimer?.Stop();
        if (SpokenLine.Length == 0) return;

        SpokenLine = "";
        OnPropertyChanged(nameof(SpokenLine));
        if (rebuild) BuildScreen();
    }

    /// <summary>
    /// Shuts the shopkeeper up completely â€” clip, mouth and balloon together.
    ///
    /// Every location script does this as its dialog closes: `(DoAudio audSTOP)` followed
    /// by `(gASong fade:)` appears in all thirteen locations plus room1, weekend and
    /// goalsDefine (`employment.sc:346`, `bank.sc:134`, `market.sc:156`, and so on). The
    /// port was leaving the clip running, so a shopkeeper carried on talking to an empty
    /// shop after the player had walked out onto the board.
    ///
    /// The mouth timer has to be stopped explicitly too: it is driven by the audio
    /// position, and silencing the clip would otherwise leave it ticking against a
    /// position that never arrives.
    /// </summary>
    private void StopTalking()
    {
        Sound.StopSpeech();

        _mouthTimer?.Stop();
        _mouth = [];
        _mouthNext = 0;
        _talkerCel = 0;

        // Any pending question goes with it: `Print` disposes its dialog before the
        // location's own dialog closes, so a question can never outlive the shop.
        _question = null;

        CloseBalloon(rebuild: false);
    }

    /// <summary>
    /// Any click anywhere dismisses the balloon, as the original's modal `Print` does —
    /// but ONLY a text-only one. `Dialog::handleEvent`'s claim-anything branch
    /// (`Interface.sc:1108-1125`) is gated on `(not (self firstTrue: #checkState 1))`, and
    /// a `DButton` has `state 3` (`Interface.sc:584`), so bit 1 is set and a balloon with
    /// buttons falls straight through it. A question waits for its answer.
    /// </summary>
    public ICommand DismissBalloonCommand => new RelayCommand(() =>
    {
        if (RouteInput(Jones.Net.InputKind.DismissBalloon)) return;

        // A Save/Restore `Print` owns the click while it is up — see
        // MainViewModel.SaveRestore.cs.
        if (DismissSystemPrint()) return;

        if (_question is not null && SpokenLine.Length == 0) return;

        Effect(Audio.LocationMusic.ButtonClick);

        // Taking the balloon down SILENCES the clip, rather than leaving the shopkeeper
        // talking to a balloon that is no longer there.
        //
        // The thirteen shop balloons do not do this in the original: its twenty
        // `(DoAudio audSTOP)` calls are all at dialog EXITS, so a line dismissed early
        // plays on until you leave the building. The end-of-game prompt is the one place
        // it does do it — `room1.sc:1014-1024` plays clip 600, puts up a modal `Print` and
        // cuts the audio the moment it returns — so the behaviour is the game's, applied
        // more widely than the game applies it. A deliberate departure, asked for: on a
        // touch screen a dismissed line that keeps talking reads as a bug.
        StopTalking();
        BuildScreen();
    });

    /// <summary>
    /// A `Print` that is waiting for a button — keyword 81, `Interface.sc:113-121`. There
    /// are four in the game: the university's enrolment, the bank's loan, the pawn shop's
    /// offer and the rent office's two apartments.
    /// </summary>
    private sealed record Question(string Text, int Width,
                                   BalloonButton[] Buttons, Action<int> Answered);

    private Question? _question;

    /// <summary>
    /// Puts a question up. In the CD build the shopkeeper's clip plays to completion BEFORE
    /// the `Print` appears — `university.sc:668` then `:671`, `bank.sc:368` then `:373`,
    /// `pawnShop.sc:666` then `:671` — so the question queues behind whatever is being
    /// spoken and <see cref="BuildBalloon"/> shows it once the speech balloon has gone.
    ///
    /// The strings come from the game's own text resources via <see cref="SciText"/>; the
    /// button labels are the script's literal `#button` arguments, which are NOT uniformly
    /// Yes/No — the pawn shop offers `Take It` / `Leave It` and the rent office spells its
    /// pair `{ YES }` / `{ NO }`, spaces and capitals included.
    /// </summary>
    private void Ask(string text, int width, Action<int> answered,
                     params BalloonButton[] buttons)
    {
        if (text.Length == 0 || buttons.Length == 0) return;
        _question = new Question(text, width, buttons, answered);
    }

    /// <summary>
    /// A button was pressed. `Print` looks the chosen item up in its button array and
    /// returns that button's `value:` (`Interface.sc:247-252`), which is what the call site
    /// branches on.
    /// </summary>
    private void Answer(int value)
    {
        if (_question is not { } q) return;

        // Cleared FIRST: the handler may put a new balloon up, and `Interface.sc:260`
        // disposes the dialog before anything the caller does next.
        _question = null;
        q.Answered(value);
        BuildScreen();
    }

    /// <summary>The line currently being spoken, shown in the balloon.</summary>
    public string SpokenLine { get; private set; } = "";

    /// <summary>True while the balloon is up, which also makes the dialogue modal.</summary>
    public bool IsBalloonShowing => Balloon.Count > 0;

    /// <summary>
    /// Builds the current speaker's balloon, using the tail number, tail tip and colour
    /// scheme that this location's own script installs â€” see
    /// <see cref="BubbleWindow.TalkFor"/>.
    /// </summary>
    private void BuildBalloon()
    {
        // The Game menu's own `Print`s come first and replace everything: they are modal,
        // they are drawn on the same `gBubbleWindow`, and they can be up on screens that
        // have no shopkeeper at all — including the main menu, before any game exists.
        // See MainViewModel.SaveRestore.cs.
        if (BuildSystemBalloon()) return;

        if (_game is null) return;
        if (_screen != Screen.LocationPanel) return;
        if (BubbleWindow.TalkFor(P.Location) is not { } t) return;

        // The spoken line first. A queued question waits for it to finish, as it does in
        // the CD build, where `proc0_18` plays the clip out before the `Print` goes up.
        if (SpokenLine.Length > 0)
        {
            BubbleWindow.Build(Balloon, SpokenLine, t.Width, t.TNum, t.X, t.Y, t.Scheme);
            return;
        }

        if (_question is not { } q) return;

        // The question's own `#width` (keyword 70) — 113 at the university, 110 at the
        // bank, 107 at the pawn shop, 150 at the rent office — NOT the location's greeting
        // width. Putting the wrong one here is what made the enrolment box oversized.
        var hits = new List<BalloonButtonRect>();
        if (!BubbleWindow.Build(Balloon, q.Text, q.Width, t.TNum, t.X, t.Y, t.Scheme,
                                q.Buttons, hits))
        {
            // Nothing was drawn, so nothing can be pressed. Drop the question rather than
            // leave the player stuck behind an invisible modal.
            _question = null;
            return;
        }

        foreach (var hit in hits)
        {
            var value = hit.Value;
            BalloonButtons.Add(new BalloonButtonVm(hit.X, hit.Y, hit.W, hit.H, hit.Text,
                                                   () => Answer(value)));
        }
    }

    // ------------------------------------------------------------------
    // The win sequence — `winnerScript`, script 234
    // ------------------------------------------------------------------

    /// <summary>Cycles since the sequence opened, one per <see cref="CycleMs"/>.</summary>
    private int _winnerCycle;

    /// <summary>`local0` — which end of the panel Jones walks in from on this pass.</summary>
    private int _winnerFrom;

    /// <summary>`jonesGuy`'s x, stepped 10 a cycle by `setStep: 10 10` (`:76`).</summary>
    private int _winnerGuyX;

    /// <summary>`setPri: (+ 2 (* (Random 0 1) 3))` — 2 or 5, rerolled on every pass (`:77`).</summary>
    private bool _winnerGuyInFront;

    /// <summary>Confetti bursts alive right now: x, the loop, and the cycle it started on.</summary>
    private readonly List<(int X, int Y, int Loop, int Born, bool InFront)> _confetti = [];

    private DispatcherTimer? _winnerTimer;

    /// <summary>
    /// `winnerScript` state 0 (`:26-71`). The whole of the sound is three lines:
    ///
    /// <code>
    /// (gASoundEffect stop:) (gASoundEffect2 stop:) (gASong play: 7)
    /// </code>
    ///
    /// The walker is hidden (`:36`) and the panel is repainted as a podium: view 0 loop 0
    /// CEL 4 — the 183x112 frame — at (69,44), the player's own colour panel inside it at
    /// (70,45), a plinth (view 609 loop 0) at (160,154) and the winner standing on it at
    /// (160,149) in the walker's current view.
    /// </summary>
    private void StartWinnerSequence()
    {
        _winnerCycle = 0;
        _winnerGuyX = -1;                 // jonesGuy is not on yet: `= cycles 10` first
        _confetti.Clear();
        _screen = Screen.Winner;

        Sound.StopEffects();
        Music(Audio.LocationMusic.Winner);   // `(gASong play: 7)`
        _turnSoundsPending = false;

        _winnerTimer ??= new DispatcherTimer();
        _winnerTimer.Stop();
        _winnerTimer.Interval = TimeSpan.FromMilliseconds(CycleMs);
        _winnerTimer.Tick -= WinnerTick;
        _winnerTimer.Tick += WinnerTick;
        _winnerTimer.Start();

        EnsureSmoothTimer();

        BuildScreen();
    }

    private void WinnerTick(object? sender, EventArgs e)
    {
        if (_screen != Screen.Winner) { _winnerTimer?.Stop(); return; }

        _winnerCycle++;

        // `(= cycles 10)` at `:70` before state 1 starts Jones walking.
        if (_winnerCycle == 10) StartWinnerPass();

        if (_winnerGuyX >= 0)
        {
            // `setStep: 10 10` with `setMotion: MoveTo`, one step a cycle.
            _winnerGuyX += _winnerFrom == 0 ? 10 : -10;
            _winnerGuyTween.Step(_winnerGuyX, WinnerGuyY, NowMs, CycleMs);

            // `jonesGuy::cue` (`:165-184`) drops a confetti burst whenever the cel cycle
            // finishes inside 70 <= x <= 260, at x + 50 or x - 50, on the loop TWO above
            // his own — so loop 2 throws loop 4 and loop 3 throws loop 5.
            if (_winnerCycle % WinnerGuyCels == 0 && _winnerGuyX is >= 70 and <= 260)
            {
                _confetti.Add((
                    _winnerFrom == 0 ? _winnerGuyX + 50 : _winnerGuyX - 50,
                    WinnerGuyY, 4 + _winnerFrom, _winnerCycle, _winnerGuyInFront));
            }

            // The pass ends when MoveTo reaches the far side; state 2 starts another, and
            // `winnerScript::cue` keeps knocking the state back to 1 (`:126-128`) so it
            // repeats until the player clicks.
            if ((_winnerFrom == 0 && _winnerGuyX >= 302) ||
                (_winnerFrom == 1 && _winnerGuyX <= 20))
                StartWinnerPass();
        }

        // A confetti Prop disposes itself at the end of one pass through its twelve cels
        // (`confetti::cue`, `:192-194`).
        _confetti.RemoveAll(c => _winnerCycle - c.Born >= WinnerConfettiCels);

        BuildScreen();
    }

    /// <summary>State 1 / state 2 — a fresh walk across, rerolled each time (`:72-95`).</summary>
    private void StartWinnerPass()
    {
        _winnerFrom = _stockRng.Next(0, 1);
        _winnerGuyX = 20 + _winnerFrom * 282;
        _winnerGuyInFront = _stockRng.Next(0, 1) == 1;

        // `(jonesGuy posn: …)` puts him on the far side outright; a teleport, not a step.
        _winnerGuyTween.Jump(_winnerGuyX, WinnerGuyY);

        // He is not on screen for the first ten cycles (`(= cycles 10)` at `:70`), so the
        // display-rate redraw will have stopped itself by now; this is where it restarts.
        EnsureSmoothTimer();
    }

    /// <summary>`jonesGuy`'s loops 2 and 3 hold six cels each; the confetti loops hold twelve.</summary>
    private const int WinnerGuyCels = 6, WinnerConfettiCels = 12;

    /// <summary>`(jonesGuy posn: … 153)` — `winnerScript.sc:75`.</summary>
    private const int WinnerGuyY = 153;

    /// <summary>
    /// The ten `star` Props (`:197-345`), each a `FwdCount` cycler on view 609 loop 1 with
    /// its own `cycleSpeed` — 5, 4, 6, 5, 4, 6, 5, 4, 6, 5 down the two columns.
    /// </summary>
    private static readonly (int X, int Y, int Speed)[] WinnerStars =
    [
        (129, 59, 5), (119, 74, 4), (114, 94, 6), (119, 114, 5), (124, 134, 4),
        (186, 59, 6), (196, 74, 5), (201, 94, 4), (196, 114, 6), (191, 134, 5),
    ];

    private void BuildWinner()
    {
        if (_game is null) return;

        // DRAW ORDER HERE IS `priority`, NOT LIST ORDER, and this is the one screen in the
        // port where the two differ. `winnerScript` states 1 and 2 give Jones
        // `setPri: (+ 2 (* (Random 0 1) 3))` (`:77`, `:89`) — 2 or 5, rerolled on every pass —
        // against the pedestal's declared `priority 4`, so he walks BEHIND the plinth on about
        // half his crossings and in front of it on the rest. Each confetti burst takes the
        // priority of the pass that threw it: `jonesGuy::cue` does `setPri: priority`
        // (`:178`), reading Jones's own current value, which `Actor::setPri` has just written.
        //
        // SCI draws the cast in ascending priority and leaves equal priorities in cast order,
        // which the stable ordering below reproduces. Note what that alone fixes: `pedistal`
        // is priority 4 and `theWinner` priority 3, so the plinth covers the winner's feet —
        // the port drew the plinth first and had the figure standing in front of it.
        _winnerParts.Clear();

        // `background1` and `background2`, both `addToPic:` — baked into the background pic
        // (`Actor::addToPic` sets signal $8021, `Actor.sc:167-172`), so they are under
        // everything whatever their declared `priority 1`.
        _winnerParts.Add((0, new SpriteVm(0, 0, 4, 69, 44)));
        _winnerParts.Add((0, new SpriteVm(0, 0, _whichBody[_game.CurrentPlayerIndex], 70, 45)));

        // `theWinner view: (gTheWalker view:)` at (160,149), `priority 3`: the walker's
        // CURRENT view, so the figure on the podium is dressed the way the player finished.
        var body = _whichBody[_game.CurrentPlayerIndex];
        var baseView = body switch { 0 => 280, 1 => 284, 2 => 290, _ => 294 };
        var view = P.WeeksOfClothing() == 0 ? baseView + 3 : baseView + (P.Wearing - 34);
        AddWinnerPri(3, view, 0, 0, 160, 149);

        // The ten stars, `priority 3`, each on its own count.
        foreach (var (x, y, speed) in WinnerStars)
            AddWinnerPri(3, 609, 1, _winnerCycle / (speed + 1) % 5, x, y);

        // `pedistal` — view 609 loop 0 at `x 160 y 154`, `priority 4`.
        AddWinnerPri(4, 609, 0, 0, 160, 154);

        foreach (var c in _confetti)
            AddWinnerPri(c.InFront ? 5 : 2, 609, c.Loop,
                         (_winnerCycle - c.Born) % WinnerConfettiCels, c.X, c.Y);

        if (_winnerGuyX >= 0)
        {
            // The six cels are art and stay stepped; only his x is interpolated.
            var gx = SmoothMotion ? _winnerGuyTween.At(NowMs).X : _winnerGuyX;
            AddWinnerPri(_winnerGuyInFront ? 5 : 2, 609, 2 + _winnerFrom,
                         _winnerCycle % WinnerGuyCels, gx, WinnerGuyY);
        }

        // `OrderBy` is a stable sort, so equal priorities keep the cast order above.
        foreach (var part in _winnerParts.OrderBy(p => p.Priority))
            Sprites.Add(part.Sprite);

        // `pedistal::init` with a non-zero argument displays the placing instead of drawing
        // the cel: text 234[0..3], `  WINNER` / `2ND PLACE` / `3RD PLACE` / `4TH PLACE`,
        // at (137,148) in FONT 10 (`:355-379`). Which one depends on how many players have
        // already finished — globals 461-464 are filled in order, which is exactly the order
        // `Game.Winners` records them in.
        var place = Math.Clamp(_game.Winners.IndexOf(P), 0, 3);
        Texts.Add(new TextVm(SciText.Get(234, place), 137, 148,
                             fontNumber: 10, colour: "#000000"));
    }

    /// <summary>
    /// The podium's cast, each part with the `priority` its instance declares or is given at
    /// runtime, collected here so the whole screen can be emitted in priority order. Held as
    /// a field rather than a local so the per-frame redraw does not allocate a list.
    /// </summary>
    private readonly List<(int Priority, SpriteVm Sprite)> _winnerParts = [];

    /// <summary>A `View` in room1's own cast: base-centre, as the walker and marble are.</summary>
    private void AddWinnerPri(int priority, int view, int loop, int cel, double x, double y)
    {
        var bmp = SciArt.Cel(view, loop, cel);
        if (bmp is null) return;

        _winnerParts.Add((priority, new SpriteVm(view, loop, cel,
            x - bmp.PixelSize.Width / 2.0, y - bmp.PixelSize.Height)));
    }

    /// <summary>
    /// `winnerScript::handleEvent` (`:151-156`): from state 2 on, ANY event is claimed and
    /// sets `local1`, which makes the next `cue` step past the walking loop to state 5 —
    /// dispose, `global532 = 1`, and the turn chain picks up where it left off.
    /// </summary>
    public bool IsWinnerSequenceShowing => _screen == Screen.Winner;

    public void DismissWinnerSequence()
    {
        if (_screen != Screen.Winner) return;

        _winnerTimer?.Stop();
        _confetti.Clear();
        ContinueTurnStart();
    }

    private const int NewsFrames = 6;
    private int _newsFrame;
    private DispatcherTimer? _newsTimer;

    /// <summary>Plays the paper's fly-in once, then reveals the headline.</summary>
    private void StartNewspaper()
    {
        _newsFrame = 0;
        _screen = Screen.Newspaper;

        // `newsPaper::init` (newspaper.sc:202-204) silences the song and plays effect 8,
        // the paper unfolding. `stop: 1` is `(DoSound sndSTOP self)` — a CUT, not the fade
        // a location exit performs. This used to fade because the interface offered
        // nothing else; it now offers the cut the script asks for.
        Sound.CutMusic();
        Effect(Audio.SoundEffects.Newspaper);

        BuildScreen();

        _newsTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        _newsTimer.Tick -= NewsTick;
        _newsTimer.Tick += NewsTick;
        _newsTimer.Start();
    }

    private void NewsTick(object? sender, EventArgs e)
    {
        _newsFrame++;
        if (_newsFrame >= NewsFrames) _newsTimer?.Stop();
        BuildScreen();
    }

    /// <summary>room1 (script 1): the board, the walker, the marble and the clock.</summary>
    private void BuildBoard()
    {
        if (_game is null) return;

        // The walker stands in the middle of the board at x 160, y 146 (theWalker's
        // own properties). View = body base + clothing offset, or +3 when naked.
        var body = _whichBody[_game.CurrentPlayerIndex];
        var baseView = body switch { 0 => 280, 1 => 284, 2 => 290, _ => 294 };
        var view = P.WeeksOfClothing() == 0 ? baseView + 3 : baseView + (P.Wearing - 34);

        // The board's centre panel is repainted in the CURRENT PLAYER'S colour at the
        // start of every turn and after every building visit. `proc0_16` is literally
        // `DrawCel 0 0 whichBody 69 45 1` â€” view 0 loop 0, one flat 181x110 cel per
        // character body, at an absolute top-left of (69,45).
        Sprites.Add(new SpriteVm(0, 0, body, 69, 45));

        // `picPatch` (view 0 loop 1, 183x25 with a +7 displacement) repaints the strip
        // the panel would otherwise paint over â€” the roofs of the Employment Office and
        // Hi-Tech U stick up above the panel's bottom edge.
        var patch = SciArt.Cel(0, 1, 0);
        if (patch is not null) Sprites.Add(new SpriteVm(0, 1, 0, 68, 138));

        // The four corner badges, cel = whatNum: the player's seat number, or 4 for
        // Jones. Positioned by SCI's bottom-centre anchoring from (80,67) etc.
        var badge = _demoMode ? 4 : _game.CurrentPlayerIndex;
        foreach (var (bx, by) in new[] { (80, 67), (238, 67), (80, 155), (238, 155) })
        {
            var cel = SciArt.Cel(0, 3, badge);
            if (cel is null) continue;
            Sprites.Add(new SpriteVm(0, 3, badge,
                bx - cel.PixelSize.Width / 2.0,
                by - cel.PixelSize.Height + 1));
        }

        // `door` â€” one Prop for all thirteen buildings, `(door init: setPri: 6)` at
        // room1.sc:1224, so it is over the board pic and under the walker. See
        // MainViewModel.Animation.cs.
        BuildDoor();

        // The walker stands at x 160, y 146 and cycles its frames while travelling.
        // `(gTheWalker hide:)` at `winnerScript.sc:36` takes him off for the podium.
        var (walkerCel, walkerSub) = WalkerFrame();
        var walker = _screen == Screen.Winner
            ? null
            : SciArt.SubCel(view, 0, walkerCel, walkerSub) ?? SciArt.Cel(view, 0, 0);
        if (walker is not null)
        {
            // Acts are positioned by their base-centre, so offset by half the width
            // and the full height.
            //
            // All four cels of a walker view are the same size (checked: 280-283, 284-287,
            // 290-293, 294-297 and 274-277 are each uniform), and an in-between frame is by
            // construction the size of the pair it sits between, so this offset does not
            // move as the cycle plays.
            Sprites.Add(new SpriteVm(view, 0, walkerCel,
                160 - walker.PixelSize.Width / 2.0,
                146 - walker.PixelSize.Height, sub: walkerSub));
        }

        // The marble token, at its current position ON THE PATH rather than at the
        // building - it walks between them. Its cel is the player's whichBody (4 = Jones).
        //
        // While walking the position comes from the tween, which is the path index's own
        // coordinate plus the fraction of the current 25ms step that has elapsed. Standing
        // still it is read straight off the index, so nothing can drift.
        double mx, my;
        if (SmoothMotion && IsWalking)
        {
            (mx, my) = _marbleTween.At(NowMs);
        }
        else
        {
            var step = MarblePath.At(_marbleIndex);
            (mx, my) = (step.X, step.Y);
        }
        var marbleCel = _demoMode ? 4 : body;
        // `marble` in room1.sc declares `loop 2` and no view, so it takes the View class
        // default of view 0. View 0 loop 2 holds exactly five 9x8 cels: one per player
        // body plus Jones at cel 4, which is what `marble cel:` selects.
        var marble = SciArt.Cel(0, 2, marbleCel);
        if (marble is not null)
            Sprites.Add(new SpriteVm(0, 2, marbleCel,
                mx - marble.PixelSize.Width / 2.0, my - marble.PixelSize.Height));

        // The clock: view 270, loop = hoursUsed / 10, cel = hoursUsed % 10, drawn at
        // x 159 y 180 anchored bottom-centre (`x - celWide/2`, `y - celHigh + 1`).
        var used = GameClock.HoursPerTurn - _game.Clock.HoursRemaining;
        used = Math.Clamp(used, 0, 60);

        // `timeKeep::doit` (room1.sc:1497-1501), the same method that draws this cel:
        //
        //     (if (and (not global478) (== global323 60))
        //         (= global478 1)
        //         (gASong stop:)
        //         (gASoundEffect2 play: 29))
        //
        // The week's 60 Hours are gone: the song is CUT and the chime sounds on the SECOND
        // effect object, because the click of the button that spent the last hour is still
        // on the first. `global478` is cleared at `startTrn.sc:69`, so it sounds once a
        // turn; `_weekOverChimed` is that latch.
        if (used >= GameClock.HoursPerTurn && !_weekOverChimed)
        {
            _weekOverChimed = true;
            Sound.CutMusic();
            Effect2(Audio.SoundEffects.WeekOver);
        }
        var clock = SciArt.Cel(270, used / 10, used % 10);
        if (clock is not null)
            Sprites.Add(new SpriteVm(270, used / 10, used % 10,
                159 - clock.PixelSize.Width / 2.0,
                180 - clock.PixelSize.Height + 1));

        // "Week #%2d" at 140,184, as the Display call in marble::cue.
        //
        // `dsBACKGROUND (if global535 86 else 7)` — an OPAQUE band, at all three of the
        // places the game draws this label (`room1.sc:1161-1171`, `:1320-1330`,
        // `Game.sc:113-123`). It is the one background fill in the game that shows: the
        // board pic under (140,184) is a dithered strip of #8890A0 / #7088A0 / #708090, so
        // the flat index-86 band is visible, unlike the nineteen Display fills that sit on
        // panels the artist already filled with the same index. Font 10 and colour 0 are
        // also the script's.
        Texts.Add(new TextVm($"Week #{_game.Calendar.Week,2}", 140, 184, 8, "#000000",
                             fontNumber: 10, background: 86));

        // Money is shown on a CALCULATOR, not as a text string: `calc` (room1.sc:1419)
        // is view 0 loop 4 at absolute (252,160).
        //
        // It belongs to the BUILDING INTERIORS, not to the board. Every location script
        // draws it on entry â€” `(gCalc setSize: value: (- (global302 cash:) 1) draw:)` in
        // all thirteen of them â€” and `proc1_8` (`room1.sc:39`), the routine that puts the
        // board back, opens with `(gCalc erase:)`. So walking around town you cannot see
        // your money; you check it by going somewhere that sells something. Drawing it on
        // the board was mine, and it gives away information the original withholds.
        if (ShowPanel)
        {
            Sprites.Add(new SpriteVm(StoreLayout.CalcView, StoreLayout.CalcLoop, 0,
                                     StoreLayout.CalcLeft, StoreLayout.CalcTop));

            // The readout, exactly as `calc::doit` draws it (`room1.sc:1437-1455`): text
            // 1[3] `"%6s "` — a six-character RIGHT-ALIGNED FIELD plus a trailing space —
            // LEFT-aligned from (nsLeft+22, nsTop+6) = (274,166), in FONT 14, colour index
            // 0, on an opaque band of index 101.
            //
            // Every one of those was wrong: the port picked font 10 from `size 7`,
            // right-aligned the string itself to x 308, put it at y 165 and coloured it
            // #203020, which is in no script. Font 14's digits are 5px and its space is
            // 4px, so padding to six characters does NOT line the right edge up — the
            // readout's right edge creeps as the figure gains digits, and true
            // right-alignment hid that.
            Texts.Add(new TextVm($"{P.Cash,6} ",
                                 StoreLayout.CalcLeft + StoreLayout.CalcDisplayLeft,
                                 StoreLayout.CalcTop + StoreLayout.CalcDisplayTop,
                                 fontNumber: StoreLayout.CalcFont,
                                 colour: SciPalette.Hex(StoreLayout.CalcColour),
                                 background: StoreLayout.CalcBackground));
        }

        // Building hotspots, at the Place instances' own rectangles.
        foreach (var loc in Board.All)
        {
            var target = loc;
            Hotspots.Add(new HotspotVm(loc.Left, loc.Top, loc.Width, loc.Height,
                () => GoTo(target.Id), loc.Name));
        }

        if (_screen == Screen.LocationPanel) BuildLocationPanel();

        // The podium, drawn over the room the same way `winnerScript` state 0 paints it in.
        else if (_screen == Screen.Winner) BuildWinner();

        // The turn-start notice, last so it is over everything — `notice` is added to the
        // cast after the room is drawn and carries `priority 5` (`startTrn.sc:1055-1058`).
        else
        {
            BuildNotice();

            // The ambulance the doctor's notice sends out, `priority 5` like the notice
            // itself and drawn after it (`startTrn.sc:1118-1141`).
            BuildAmbulance();

            // Wild Willy, `priority 8` — over the room, and he has it to himself because
            // `Place::cue` runs script 114 before anything else can start (`room1.sc:223`).
            BuildWilly();

            // The lottery win: seven falling bills and the note, all `priority 5`
            // (`lottoScript.sc:242-350`). It owns the turn-start chain while it runs, so it
            // is never on screen at the same time as a notice.
            BuildLotto();
        }
    }

    /// <summary>Nearest path index to the player's current building.</summary>
    private int CurrentPathIndex() =>
        _game is null ? 1 : Board.Get(P.Location).PathIndex;

    // ------------------------------------------------------------------
    // Motion smoothing
    // ------------------------------------------------------------------

    /// <summary>
    /// HOUSE RULE, on by default: draw the IN-BETWEEN positions of everything that moves, and
    /// redraw at display rate rather than at the original's step rate.
    ///
    /// **This changes nothing about the game.** Every step the scripts specify still happens,
    /// in the same order, at the same moment, and lands in the same place:
    ///
    /// <list type="bullet">
    /// <item>the marble still advances exactly one <see cref="MarblePath"/> index per
    ///   <see cref="CycleMs"/> tick, so a journey still takes the same number of ticks — and
    ///   the hours it costs were charged by `Game.TravelTo` before it set off anyway;</item>
    /// <item>the turn-start notice still climbs 16 pixels a tick from y 50 to y 143
    ///   (`setStep: 16 16`, `startTrn.sc:1063-1066`) and still rests there for
    ///   `(proc0_3 240)` — four seconds;</item>
    /// <item>Wild Willy still moves 3 pixels every fourth cycle (`setStep: 3 3` with
    ///   `moveSpeed 3`, script 114), the ambulance 10 every cycle (`:1102`), and the winner
    ///   sequence's Jones 10 every cycle (`winnerScript.sc:76`).</item>
    /// </list>
    ///
    /// All that is added is where a thing is BETWEEN two of those steps. The step values stay
    /// the authority for the destination and the duration; <see cref="Tween"/> only fills the
    /// gap, and turning this off restores the original's stepping exactly for comparison.
    ///
    /// Why it was worth doing: the app now renders at 4x (<see cref="RenderScale"/>), so the
    /// notice's 16-pixel step is a 64-screen-pixel jump and the marble's 6-pixel one is 24.
    /// At 320x200 on a CRT those were a blur; upscaled they are a stutter.
    ///
    /// What is deliberately NOT smoothed, because interpolating it would make it worse:
    /// <list type="bullet">
    /// <item><b>The talker's mouth.</b> The eleven cels are driven by the game's own 60Hz
    ///   sync resource (`assets/raw/sync`), which is already frame-accurate. They are
    ///   digitised photographs and the whole head shifts a pixel or two between cels, so
    ///   cross-fading them would wobble the face rather than smooth the lips.</item>
    /// <item><b>Cel animation generally</b> — the walker's four-frame cycle, Willy's eight,
    ///   the newspaper's six-cel fly-in (which does not move: it plays in place at
    ///   nsLeft 56, nsTop 35), the win sequence's stars and confetti. These are sequences of
    ///   drawn frames, not positions; smoothing them needs NEW ART, which is out of scope for
    ///   a port built from the original's own resources.</item>
    /// </list>
    /// </summary>
    public static bool SmoothMotion { get; set; } = true;

    /// <summary>
    /// The redraw rate while something is being smoothed — about 60Hz, against the original's
    /// <see cref="CycleMs"/> of 25. It drives BuildScreen only; no game state moves on it.
    /// </summary>
    private const int SmoothFrameMs = 16;

    private DispatcherTimer? _smoothTimer;

    /// <summary>Wall clock the tweens measure against. Never used for game timing.</summary>
    private readonly Stopwatch _smoothClock = Stopwatch.StartNew();

    private long NowMs => _smoothClock.ElapsedMilliseconds;

    /// <summary>
    /// One thing's position between two of the original's steps.
    ///
    /// <see cref="Step"/> is called from the stepping timer with where the script's step has
    /// just put the object and how long that step is meant to take; <see cref="At"/> then
    /// returns the position at any moment in between. With <see cref="SmoothMotion"/> off it
    /// returns the step's endpoint and nothing else, which is the original's behaviour.
    ///
    /// <see cref="Jump"/> is the teleport — `posn:` in the scripts, which several sequences
    /// use at corners (the ambulance does it four times). A teleport must not be interpolated
    /// or the vehicle slides backwards across the board.
    /// </summary>
    private sealed class Tween
    {
        private double _fromX, _fromY, _toX, _toY;
        private long _startedMs = long.MinValue / 2;
        private int _spanMs = 1;

        public void Jump(double x, double y)
        {
            _fromX = _toX = x;
            _fromY = _toY = y;
            _spanMs = 1;
            _startedMs = long.MinValue / 2;
        }

        public void Step(double toX, double toY, long nowMs, int spanMs)
        {
            _fromX = _toX;
            _fromY = _toY;
            _toX = toX;
            _toY = toY;
            _startedMs = nowMs;
            _spanMs = Math.Max(1, spanMs);
        }

        public (double X, double Y) At(long nowMs)
        {
            if (!SmoothMotion) return (_toX, _toY);

            var t = (nowMs - _startedMs) / (double)_spanMs;
            if (t <= 0) return (_fromX, _fromY);
            if (t >= 1) return (_toX, _toY);

            return (_fromX + (_toX - _fromX) * t, _fromY + (_toY - _fromY) * t);
        }
    }

    private readonly Tween _marbleTween = new();
    private readonly Tween _noticeTween = new();
    private readonly Tween _willyTween = new();
    private readonly Tween _ambulanceTween = new();
    private readonly Tween _winnerGuyTween = new();

    /// <summary>Whether anything is mid-step and therefore worth redrawing at display rate.</summary>
    private bool AnySmoothedMotion =>
        IsWalking
        || (_notice is not null && !_noticeSettled)
        || _willyPath is not null
        || _ambulanceLeg >= 0
        || (_screen == Screen.Winner && _winnerGuyX >= 0);
        // The lotto is deliberately NOT in this list: nothing in it is interpolated, so a
        // display-rate redraw would draw the same picture several times over. See
        // MainViewModel.Animation.cs.

    /// <summary>
    /// Starts the display-rate redraw. Each stepping timer calls this when it starts; the
    /// redraw stops itself as soon as nothing is moving, so an idle board costs nothing.
    /// </summary>
    private void EnsureSmoothTimer()
    {
        if (!SmoothMotion) return;

        _smoothTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SmoothFrameMs) };
        _smoothTimer.Tick -= SmoothTick;
        _smoothTimer.Tick += SmoothTick;
        _smoothTimer.Start();
    }

    private void SmoothTick(object? sender, EventArgs e)
    {
        if (!SmoothMotion || !AnySmoothedMotion) { _smoothTimer?.Stop(); return; }
        BuildScreen();
    }

    // ------------------------------------------------------------------
    // Game flow
    // ------------------------------------------------------------------

    private void StartGame()
    {
        _game = new Game(new SciRandom(Environment.TickCount), _playerCount);

        for (var i = 0; i < _game.Players.Count; i++)
        {
            var p = _game.Players[i];
            p.MonGoal = _goals[i, 0];
            p.HapGoal = _goals[i, 1];
            p.EduGoal = _goals[i, 2];
            p.CarGoal = _goals[i, 3];
            p.RecalculateGoals(_game.Economy);
        }

        // `select1.sc:132` â€” the attract-mode demo is literally Jones playing himself:
        // it sets the FIRST player's `playing` to 29, the flag `DialogScript.sc:57` tests
        // to decide whether to drive the turn from script 300 instead of from the mouse.
        if (_demoMode) _game.Players[0].IsJones = true;

        _screen = Screen.Board;
        BeginTurn();
    }

    private void BeginTurn()
    {
        _relaxedThisTurn = false;
        _game!.StartTurn();

        // `startTrn.sc:69` clears `global478` as the chain opens, which is the once-a-turn
        // latch on the end-of-week chime; the notice sounds wait for the board.
        _weekOverChimed = false;
        _turnSoundsPending = true;

        // Reroll Z-Mart's shelves for this turn.
        _zmartStock = Catalogue.ZMart.OrderBy(_ => _stockRng.Next(0, 1000)).Take(6).ToArray();

        _workedThisTurn = false;
        _garnishSpokenThisTurn = false;

        // The newspaper is NOT printed every week. `startTrn` calls it only after a
        // robbery or a market crash or boom, which is why an ordinary week goes straight
        // to the board.
        //
        // It is also NOT an alternative to the weekend. The weekend runs first, at state 2
        // (`startTrn.sc:205-214`); the robbery paper comes later at state 6
        // (`startTrn.sc:350`) and the crash/boom paper later still at state 24
        // (`startTrn.sc:644-686`). Showing the paper INSTEAD of the weekend swallowed the
        // weekend entirely on any week that made the news.
        var robbed = _game.LastTurnEvents.Any(e => e is TurnStartEvent.Robbed);
        var crashed = _game.Economy.CrashSeverity != 0;
        var boomed = _game.Economy.Boom;

        // startTrn.sc:644 sets the headline directly: the crash severity itself for 1/2/3,
        // and 4 for a boom ("INFLATION IS UP PRICES COULD SOAR!"). The robbery at state 6
        // sets it to 15 (`startTrn.sc:344`).
        _pendingNewspaper = robbed ? 15
                          : crashed ? _game.Economy.CrashSeverity
                          : boomed ? 4
                          : null;

        // `startTrn` state 0: all four goals met and `(self setScript: (ScriptID 234 0))`
        // `(return)` — the winner sequence runs BEFORE the weekend, and the chain only
        // resumes when `winnerScript` sets `global532` on the way out (`:103`, `:112`).
        if (_game.LastTurnEvents.Any(e => e is TurnStartEvent.Won))
        {
            StartWinnerSequence();
            return;
        }

        ContinueTurnStart();
    }

    /// <summary>
    /// `startTrn` states 1 onward — what happens once the winner sequence, if any, is over.
    /// </summary>
    private void ContinueTurnStart()
    {
        // The weekend, which runs every week except the first
        // (`startTrn.sc:205` gates it on week != 1).
        if (_game!.Calendar.Week != 1)
        {
            _weekendResult = Weekend.Roll(P, _weekendRng, _game.Calendar.Week, ref _lastWeekendId);
            P.RecalculateGoals(_game.Economy);
            _screen = Screen.Weekend;
            // The weekend dialog opens with its own track (`weekend.sc:183`) and fades it
            // on the way out (`weekend.sc:228`); leaving the weekend runs
            // ShowNewspaperOrBoard, which starts the next bed and cancels the fade.
            Music(Audio.LocationMusic.Weekend);
            BuildScreen();
            return;
        }

        ShowNewspaperOrBoard();
    }

    /// <summary>
    /// Prints this turn's paper if one is due, otherwise hands the board back. The paper
    /// follows the weekend rather than replacing it â€” see <see cref="BeginTurn"/>.
    /// </summary>
    private void ShowNewspaperOrBoard()
    {
        if (_pendingNewspaper is { } headline)
        {
            _pendingNewspaper = null;
            _headlineToShow = headline;
            StartNewspaper();
            return;
        }

        // A paper bought over the counter at Black's Market is a dialog over the SHOP, so
        // Done puts you back in the shop rather than out on the board.
        if (_newspaperReturn is { } back)
        {
            _newspaperReturn = null;
            _screen = back;
            // `market.sc:356` puts the shop's own bed back on once the paper is folded.
            PlayLocationBed();
            BuildScreen();
            return;
        }

        _screen = Screen.Board;
        // `(gASong loop: -1 play: 5)` â€” the board's theme, started wherever the board
        // comes up: `room1.sc:281`, `startTrn.sc:216` and `Game.sc:189`. The turn-start
        // sounds sit on either side of it: the winner's fanfare replaces it (state 0
        // returns before the theme is ever started) and everything else plays over or
        // against it, exactly as the state order has them.
        if (!PlayWinnerFanfare())
        {
            Music(Audio.LocationMusic.Board);
            PlayTurnStartSounds();
        }

        PlayJonesTurnIfNeeded();
        BuildScreen();
    }

    /// <summary>Set by <see cref="BeginTurn"/>; cleared by whichever sound block runs.</summary>
    private bool _turnSoundsPending;

    /// <summary>
    /// `startTrn` state 0: all four goals met, so `(self setScript: (ScriptID 234 0))` and
    /// `(return)` â€” the rest of the turn-start chain, the board theme at state 216
    /// included, never runs. `winnerScript.sc:67-69` is the whole of the sound:
    ///
    /// <code>
    /// (gASoundEffect stop:)
    /// (gASoundEffect2 stop:)
    /// (gASong play: 7)
    /// </code>
    ///
    /// `gASong`'s `loop` is -1 wherever the board theme was last started (`room1.sc:281`,
    /// `startTrn.sc:216`), and `Sound::play` leaves a non-zero `loop` alone (Sound.sc:46),
    /// so song 7 loops â€” and resource 7 is one of the eleven that marks a loop point.
    ///
    /// The fanfare now starts WITH the podium — see <see cref="StartWinnerSequence"/>, which
    /// is where `startTrn` state 0 actually plays it — and the board theme comes back when
    /// the podium is dismissed and the chain runs on. This remains as the fallback for a Won
    /// event that somehow reaches the board without the sequence having run.
    ///
    /// Returns true when it took the board's theme over.
    /// </summary>
    private bool PlayWinnerFanfare()
    {
        if (_game is null || !_turnSoundsPending) return false;
        if (!_game.LastTurnEvents.Any(e => e is TurnStartEvent.Won)) return false;

        _turnSoundsPending = false;
        Sound.StopEffects();
        Music(Audio.LocationMusic.Winner);
        return true;
    }

    /// <summary>
    /// The sounds `startTrn` plays on its way through a turn opening, in its own state
    /// order. Each is a literal call with the condition already decided by the core, which
    /// reports the same five states as <see cref="TurnStartEvent"/>s.
    ///
    /// The rest of them now travel WITH their notice, because that is where `startTrn`
    /// plays them: effect 27 alongside notice 2 at `:483-485`, effect 30 alongside notice 7
    /// at `:705`, and effect 21 out of the ambulance that notice 3 turns into
    /// (`:1034-1037`, `:1078`). See <see cref="StartNextNotice"/>.
    /// </summary>
    private void PlayTurnStartSounds()
    {
        if (_game is null || !_turnSoundsPending) return;
        _turnSoundsPending = false;

        var events = _game.LastTurnEvents;

        // State 2, the lottery. The lottery is the one turn-start event with no notice of
        // its own: it has a whole script (116) instead, and `startTrn.sc:229-238` hands the
        // chain to it and RETURNS. Script 116 gives the chain back at its state 20
        // (`(client script: 0 cue:)`, `:231`), which is when `startTrn` reaches state 3 and
        // the notices begin â€” so the notices wait for the bills to fall rather than
        // arriving over them. See StartLotto in MainViewModel.Animation.cs.
        if (events.FirstOrDefault(e => e is TurnStartEvent.LotteryWin)
            is TurnStartEvent.LotteryWin win)
        {
            StartLotto(P.ActualName, win.Amount, () => QueueNotices(events));
            return;
        }

        QueueNotices(events);
    }

    // ------------------------------------------------------------------
    // The turn-start notice window (`moveNotice`, `startTrn.sc:927-1070`)
    // ------------------------------------------------------------------

    /// <summary>
    /// One notice. `notice` is an `Act` whose VIEW carries the words:
    /// `(notice view: (+ 310 register) init:)` at `startTrn.sc:934`, so register 0 is view
    /// 310 `LESS TIME! DUE TO HUNGER`, register 2 is view 312 `RENT IS DUE`, and so on
    /// through register 12 / view 322. Nothing on the notice is typeset except the one or
    /// two VALUES `moveNotice` state 3 displays over it (`:946-1030`), which is why no
    /// string here is invented: the sentences are pixels in the resource.
    /// </summary>
    /// <param name="Register">The `register` the script passes, i.e. view 310 + this.</param>
    /// <param name="Name">
    /// The `%s` line — text 111[1] fed either the player's name (registers 7 and 11) or an
    /// item name out of text 700 (register 1).
    /// </param>
    /// <param name="Amount">The figure — text 111[3] `$%d`, or 111[2] `$%d an hour`.</param>
    private sealed record Notice(int Register,
                                 string? Name = null, int NameY = 0,
                                 string? Amount = null, int AmountY = 0);

    private readonly Queue<Notice> _notices = new();
    private Notice? _notice;
    private int _noticeY;
    private bool _noticeSettled;
    private DispatcherTimer? _noticeTimer;

    /// <summary>`(notice posn: 159 50 … setMotion: MoveTo 159 143)` — `startTrn.sc:1063-1066`.</summary>
    private const int NoticeFromY = 50, NoticeToY = 143, NoticeX = 159;

    /// <summary>`(notice setStep: 16 16)` — `startTrn.sc:1065`.</summary>
    private const int NoticeStep = 16;

    /// <summary>
    /// Turns this turn's events into the notices `startTrn` opens for them, in its own state
    /// order — which is the order the core already reports them in.
    ///
    /// Two of the fifteen `setScript: moveNotice` call sites are still narrower here than in
    /// the original and are listed rather than guessed at: register 11 (the wage cut) and
    /// register 7 (the sacking) are raised for the CURRENT player only, where the original
    /// runs `doScandal` for all four at every turn start (`:694-822`).
    ///
    /// Register 12, the relative's gift, was the third: it is now reachable — `TurnStart`
    /// ports state 34 and raises <see cref="TurnStartEvent.RelativeGift"/>.
    /// </summary>
    private void QueueNotices(IReadOnlyList<TurnStartEvent> events)
    {
        _notices.Clear();

        foreach (var e in events)
        {
            switch (e)
            {
                // `:261` — register 10, `(Random 20 100)` earned by the computer. The figure
                // goes at y 95 (`:1017`).
                case TurnStartEvent.ComputerIncome c:
                    _notices.Enqueue(new Notice(10, Amount: Money(c.Amount), AmountY: 95));
                    break;

                // `:395` / `:402` and `:409` — registers 8 and 9, no values on either.
                case TurnStartEvent.AllFoodSpoiled:
                    _notices.Enqueue(new Notice(8));
                    break;
                case TurnStartEvent.SomeFoodSpoiled:
                    _notices.Enqueue(new Notice(9));
                    break;

                // `:432` — register 0. The 20 hours it charges at `:1041` are already spent
                // by `TurnStart`; this is the window only.
                case TurnStartEvent.Starved:
                    _notices.Enqueue(new Notice(0));
                    break;

                // `:464` — register 3 with the bill, at y 135 (the `else` at `:1021`). The
                // 10 hours at `:1035` are likewise already charged.
                case TurnStartEvent.DoctorVisit d:
                    _notices.Enqueue(new Notice(3, Amount: Money(d.Cost), AmountY: 135));
                    break;

                // `:483` — register 2 with `curRent`, at y 125 (`:1019`).
                case TurnStartEvent.RentDue r:
                    _notices.Enqueue(new Notice(2, Amount: Money(r.Amount), AmountY: 125));
                    break;

                // `:517` — register 6, and ONLY at exactly one week of clothing left
                // (`:516`). Running out entirely shows no notice at all.
                case TurnStartEvent.ClothingLow:
                    _notices.Enqueue(new Notice(6));
                    break;

                // `:529` and `:534` — registers 5 and 4.
                case TurnStartEvent.LoanPaymentDemanded:
                    _notices.Enqueue(new Notice(5));
                    break;
                case TurnStartEvent.LoanOverdue:
                    _notices.Enqueue(new Notice(4));
                    break;

                // `:625` — register 1. Two lines: the item's name out of text 700 at y 125
                // (`:978`) and the repair bill at y 135.
                case TurnStartEvent.ApplianceBroke a:
                    _notices.Enqueue(new Notice(1,
                        Name: SciText.Get(111, 1, SciText.Get(700, a.ItemId)), NameY: 125,
                        Amount: Money(a.RepairCost), AmountY: 135));
                    break;

                // `:704` — register 7, the name at y 75 (`:976`). `:714` — register 11, the
                // name at y 65 and the new wage through text 111[2] at y 131 (`:1020`).
                case TurnStartEvent.CrashFallout { Outcome: 1 }:
                    _notices.Enqueue(new Notice(7,
                        Name: SciText.Get(111, 1, P.ActualName), NameY: 75));
                    break;
                case TurnStartEvent.CrashFallout { Outcome: -1 }:
                    _notices.Enqueue(new Notice(11,
                        Name: SciText.Get(111, 1, P.ActualName), NameY: 65,
                        Amount: SciText.Get(111, 2, P.Wage), AmountY: 131));
                    break;

                // `:875` — register 12, the relative's gift. `moveNotice` state 3 formats it
                // through text 111[3] `"$%d"` like every other money line, and its `cond`
                // at `:1015-1022` puts register 12's figure at y 95, the same row as the
                // computer's earnings. View 322 is the card itself (310 + 12).
                case TurnStartEvent.RelativeGift r:
                    _notices.Enqueue(new Notice(12, Amount: Money(r.Amount), AmountY: 95));
                    break;
            }
        }

        StartNextNotice();
    }

    /// <summary>Text 111[3], `$%d` — the only money format `moveNotice` uses.</summary>
    private static string Money(int amount) => SciText.Get(111, 3, amount);

    /// <summary>
    /// `moveNotice` states 0-5. State 0 initialises the actor and starts it moving; state 2
    /// clicks it into place with effect 23, unless effect 27 already has the slot
    /// (`:941-943`); state 3 displays the values; state 4 waits `(proc0_3 240)`, four
    /// seconds at SCI's sixty ticks (`:1043`); state 5 disposes it.
    /// </summary>
    private void StartNextNotice()
    {
        if (_notices.Count == 0)
        {
            _notice = null;
            _noticeTimer?.Stop();
            return;
        }

        _notice = _notices.Dequeue();
        _noticeY = NoticeFromY;
        _noticeSettled = false;
        _noticeTween.Jump(NoticeX, NoticeFromY);   // `(notice posn: 159 50)` — a teleport

        // The sounds the script plays at the `setScript:` itself, before the window moves.
        switch (_notice.Register)
        {
            case 2:  DuckedSting(Audio.SoundEffects.EvictionNotice); break;  // `:484-485`
            case 7:  Effect(Audio.SoundEffects.Sacked); break;               // `:705`
        }

        _noticeTimer ??= new DispatcherTimer();
        _noticeTimer.Stop();
        _noticeTimer.Interval = TimeSpan.FromMilliseconds(CycleMs);
        _noticeTimer.Tick -= NoticeTick;
        _noticeTimer.Tick += NoticeTick;
        _noticeTimer.Start();

        EnsureSmoothTimer();

        BuildScreen();
    }

    private void NoticeTick(object? sender, EventArgs e)
    {
        if (_notice is null) { _noticeTimer?.Stop(); return; }

        // The board is the only room this animation belongs to; anything that takes the
        // player somewhere else abandons it rather than drawing it over a dialog.
        if (_screen != Screen.Board)
        {
            _noticeTimer?.Stop();
            StopAmbulance();
            _notice = null;
            _notices.Clear();
            return;
        }

        if (_noticeY < NoticeToY)
        {
            // One `setStep: 16 16` step of the MoveTo, exactly as before. The tween records
            // where it has landed so the redraw can fill the 16 pixels in — see SmoothMotion.
            _noticeY = Math.Min(NoticeToY, _noticeY + NoticeStep);
            _noticeTween.Step(NoticeX, _noticeY, NowMs, CycleMs);
            BuildScreen();
            return;
        }

        if (!_noticeSettled)
        {
            _noticeSettled = true;

            // `(if (!= (gASoundEffect number:) 27) (gASoundEffect play: 23))` — the rent
            // notice is the one that arrives under effect 27, so it arrives in silence.
            if (_notice.Register != 2) Effect(Audio.LocationMusic.ButtonClick);

            // `moveNotice` state 4 (`startTrn.sc:1029-1046`). Register 3 does NOT take the
            // `(proc0_3 240)` wait the other twelve take: it inits the ambulance and hands
            // the script over to `moveAmbulance`, which is what plays effect 21. The port
            // used to sit out the four seconds and then sound the siren into an empty board.
            //
            // The notice card stays up while the ambulance drives. `moveAmbulance` state 5
            // disposes only the ambulance (`:1104-1108`); the notice is not disposed until
            // `startTrn` state 36 (`:892-894`).
            if (_notice.Register == 3)
            {
                _noticeTimer!.Stop();
                StartAmbulance();
                BuildScreen();
                return;
            }

            _noticeTimer!.Interval = TimeSpan.FromSeconds(240 / 60.0);
            BuildScreen();
            return;
        }

        _noticeTimer!.Stop();
        StartNextNotice();
    }

    /// <summary>
    /// Draws the notice over the board. It IS over the board in the original too: `startTrn`
    /// shows the walker again at `:212`, well before the first `moveNotice`, and the notice
    /// is an `Act` in room1's cast at `priority 5` — so the marble, the badges and the
    /// walker's head and feet around the window are the room, not a hole.
    /// </summary>
    private void BuildNotice()
    {
        if (_notice is not { } n) return;

        var cel = SciArt.Cel(310 + n.Register, 0, 0);
        if (cel is null) return;

        // Acts anchor bottom-centre, as the walker and the marble do above. The y is the
        // tween's, which is `_noticeY` itself once the current 16-pixel step has run out.
        var (_, ny) = _noticeTween.At(NowMs);

        Sprites.Add(new SpriteVm(310 + n.Register, 0, 0,
            NoticeX - cel.PixelSize.Width / 2.0,
            ny - cel.PixelSize.Height));

        if (!_noticeSettled) return;

        // `(Display … dsCOORD (- 160 (/ [local17 3] 2)) <y> dsCOLOR 0 dsBACKGROUND -1
        //   dsFONT 4)` — measured, then centred on x 160. `[local17 3]` is TextSize's
        // width, the fourth word it fills in.
        AddCentredNoticeLine(n.Name, n.NameY);
        AddCentredNoticeLine(n.Amount, n.AmountY);
    }

    // ------------------------------------------------------------------
    // The ambulance — `moveAmbulance` and `ambulance`, `startTrn.sc:1072-1141`
    // ------------------------------------------------------------------

    /// <summary>
    /// One state of `moveAmbulance`. Each one sets the cel it drives in, teleports the vehicle
    /// with `posn:` and then `MoveTo`s across one side of the board — so the corners are JUMPS
    /// in the original, not turns, and they are jumps here.
    /// </summary>
    /// <param name="Cel">`(ambulance cel: n)` — loop 1's four facings.</param>
    private sealed record AmbulanceLeg(int Cel, int FromX, int FromY, int ToX, int ToY);

    /// <summary>
    /// `moveAmbulance` states 0-4 verbatim (`startTrn.sc:1076-1095`). State 0's start is
    /// `ambulance::init`'s own `(posn: 59 155)` at `:1101`; every later state carries its
    /// `posn:` in the state itself:
    ///
    /// <code>
    /// (0 (gASoundEffect play: 21) (ambulance cel: 0 setMotion: MoveTo 230 155 self))
    /// (1 (ambulance posn: 242 155 cel: 1 setMotion: MoveTo 242  50 self))
    /// (2 (ambulance posn: 230  60 cel: 2 setMotion: MoveTo  69  60 self))
    /// (3 (ambulance posn:  77  80 cel: 3 setMotion: MoveTo  77 155 self))
    /// (4 (ambulance posn:  59 155 cel: 0 setMotion: MoveTo 230 155 self))
    /// </code>
    ///
    /// It drives the ring twice along the bottom — states 0 and 4 are the same run — which is
    /// what the script says, not a duplicate.
    /// </summary>
    private static readonly AmbulanceLeg[] AmbulanceLegs =
    [
        new(0,  59, 155, 230, 155),
        new(1, 242, 155, 242,  50),
        new(2, 230,  60,  69,  60),
        new(3,  77,  80,  77, 155),
        new(0,  59, 155, 230, 155),
    ];

    /// <summary>
    /// `ambulance` — view 608 loop 1, `setStep: 10 10`, `priority 5` (`:1097-1112`).
    ///
    /// <para>
    /// IT DECLARES `ticksToDo 8` (`startTrn.sc:1107`) AND THAT PROPERTY IS DEAD. `ticksToDo`
    /// is read in exactly one place — `Cycle::init` (`Motion.sc:21`), i.e. when a cycler is
    /// attached — and `moveAmbulance` never calls `setCycle:`. Every one of its five states
    /// writes the cel outright (`cel: 0`, `cel: 1`, `cel: 2`, `cel: 3`), because loop 1's four
    /// cels are the four FACINGS of the vehicle, not a cycle. So there is nothing here for
    /// <see cref="CelCycler"/> to drive, and the cel table above is already the whole truth.
    /// Recorded rather than silently skipped, because the declaration is the kind of thing a
    /// later reader would assume was missed.
    /// </para>
    /// </summary>
    private const int AmbulanceView = 608, AmbulanceLoop = 1, AmbulanceStep = 10;

    /// <summary>Which state of `moveAmbulance` is running, or -1 when it is not on screen.</summary>
    private int _ambulanceLeg = -1;
    private int _ambulanceX, _ambulanceY;
    private DispatcherTimer? _ambulanceTimer;

    /// <summary>
    /// `moveNotice` state 4's register-3 branch: `(ambulance init:)` then
    /// `(self setScript: moveAmbulance)`, and state 0 of that script plays effect 21.
    ///
    /// `ambulance::init` takes its `moveSpeed` from the marble (`(ScriptID 1 7) moveSpeed:`,
    /// which is `moveSpeed 1` on `room1.sc`'s `marble` instance), so it steps on the same
    /// cycle the marble does — one step per <see cref="CycleMs"/>, ten pixels at a time.
    /// </summary>
    private void StartAmbulance()
    {
        Effect(Audio.SoundEffects.Ambulance);

        StartAmbulanceLeg(0);

        _ambulanceTimer ??= new DispatcherTimer();
        _ambulanceTimer.Stop();
        _ambulanceTimer.Interval = TimeSpan.FromMilliseconds(CycleMs);
        _ambulanceTimer.Tick -= AmbulanceTick;
        _ambulanceTimer.Tick += AmbulanceTick;
        _ambulanceTimer.Start();

        EnsureSmoothTimer();
    }

    private void StartAmbulanceLeg(int leg)
    {
        _ambulanceLeg = leg;
        _ambulanceX = AmbulanceLegs[leg].FromX;
        _ambulanceY = AmbulanceLegs[leg].FromY;

        // `posn:` — the vehicle is PUT there, so this must not be interpolated.
        _ambulanceTween.Jump(_ambulanceX, _ambulanceY);
    }

    private void AmbulanceTick(object? sender, EventArgs e)
    {
        if (_ambulanceLeg < 0) { _ambulanceTimer?.Stop(); return; }

        // Same rule as the notice and Willy: this belongs to the board and nothing else.
        if (_screen != Screen.Board) { StopAmbulance(); StartNextNotice(); return; }

        var leg = AmbulanceLegs[_ambulanceLeg];

        _ambulanceX += Math.Sign(leg.ToX - _ambulanceX)
                     * Math.Min(AmbulanceStep, Math.Abs(leg.ToX - _ambulanceX));
        _ambulanceY += Math.Sign(leg.ToY - _ambulanceY)
                     * Math.Min(AmbulanceStep, Math.Abs(leg.ToY - _ambulanceY));

        _ambulanceTween.Step(_ambulanceX, _ambulanceY, NowMs, CycleMs);

        if (_ambulanceX == leg.ToX && _ambulanceY == leg.ToY)
        {
            if (_ambulanceLeg + 1 < AmbulanceLegs.Length)
            {
                StartAmbulanceLeg(_ambulanceLeg + 1);
            }
            else
            {
                // State 5: `(client script: 0 cue:)` `(ambulance dispose:)` — the chain goes
                // on, which here means the next notice in the queue.
                StopAmbulance();
                StartNextNotice();
                return;
            }
        }

        BuildScreen();
    }

    private void StopAmbulance()
    {
        _ambulanceTimer?.Stop();
        _ambulanceLeg = -1;
    }

    private void BuildAmbulance()
    {
        if (_ambulanceLeg < 0) return;

        var cel = AmbulanceLegs[_ambulanceLeg].Cel;
        var bmp = SciArt.Cel(AmbulanceView, AmbulanceLoop, cel);
        if (bmp is null) return;

        var (ax, ay) = SmoothMotion
            ? _ambulanceTween.At(NowMs)
            : ((double)_ambulanceX, (double)_ambulanceY);

        // An Act, so base-centre like the marble, the walker and Willy.
        Sprites.Add(new SpriteVm(AmbulanceView, AmbulanceLoop, cel,
            ax - bmp.PixelSize.Width / 2.0, ay - bmp.PixelSize.Height));
    }

    private void AddCentredNoticeLine(string? text, int y)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Measured with the font the Display call names, then re-laid at the centred x.
        var measured = new TextVm(text, 0, 0, 8, "#000000", false, fontNumber: 4);
        Texts.Add(new TextVm(text, 160 - measured.W / 2.0, y, 8, "#000000", false,
                             fontNumber: 4));
    }

    // The lottery sequence lives in MainViewModel.Animation.cs: the sound-only stand-in that
    // used to be here (a cut, the looping machine and a bare 240-tick timer) is now the tail
    // of the script's own twenty-one states, with the seven bills and the note drawn.

    /// <summary>
    /// A Jones-controlled player takes his whole turn here, in one go, and the board is then
    /// left showing where he finished and what he did. The human presses End Turn to watch
    /// the next one.
    ///
    /// The original interleaves it with the animation: `room1.sc:1381` asks script 300 for a
    /// destination, walks the marble there, and `DialogScript.sc:71` asks again the moment
    /// that building's dialog closes. The decision loop is identical â€” see
    /// <see cref="JonesTurn"/> â€” and only the pacing differs, because the marble walk is
    /// driven by a UI timer that the headless core knows nothing about.
    /// </summary>
    private void PlayJonesTurnIfNeeded()
    {
        if (_game is null || !P.IsJones) return;

        var stops = JonesTurn.Run(_game);

        // Put the marble where he ended up rather than walking it; the walk timer belongs
        // to GoTo and would fight with a turn that has already been played out.
        _marbleIndex = CurrentPathIndex();

        // Nothing is announced. The original shows Jones's turn by WALKING HIS MARBLE and
        // opening each dialog in turn; it never writes "Jones went to the Bank." anywhere.
    }

    /// <summary>The headline waiting to be printed once the weekend has been read, if any.</summary>
    private int? _pendingNewspaper;

    private WeekendResult? _weekendResult;
    private int _lastWeekendId;
    private readonly SciRandom _weekendRng = new(Environment.TickCount ^ 0x1234);

    private int _headlineToShow;

    /// <summary>Which workplace's job list is open at the Employment Office, if any.</summary>
    private Workplace? _jobListFor;

    /// <summary>
    /// Sound, supplied by the platform head. Silent until one is set, so the game runs
    /// perfectly well without audio and nothing in the core knows it exists.
    /// </summary>
    public static Audio.IAudioPlayer Sound { get; set; } = new Audio.SilentAudioPlayer();

    /// <summary>
    /// The floppy build's two independent mutes â€” `Turn Music Off` (F2) and
    /// `Turn Sound Effects Off` (F3), `jones-dos-1.000.060/src/Menu.sc:128`.
    ///
    /// Held here rather than on <see cref="Audio.IAudioPlayer"/> so that the heads stay
    /// responsible for playback alone, and so a head that gains real mixing later can take
    /// them over without this changing. Speech is governed by neither: the original's two
    /// switches are music and effects, and silencing dialogue is Ctrl-V's job.
    /// </summary>
    public static bool MusicOff { get; private set; }

    public static bool EffectsOff { get; private set; }

    /// <summary>
    /// Dialogue, muted. There is NO such switch in the original: the floppy's Options menu
    /// has music (F2) and effects (F3) only, and the CD build dropped even those. It is
    /// here because the two that do exist are function keys, and a phone has none — see
    /// <see cref="SpeechOn"/>.
    ///
    /// Separate from `Sound.Enabled`, which is Ctrl-V's master switch and kills everything.
    /// Muting speech leaves the subtitle and the mouth running, because the floppy build
    /// printed every one of these lines.
    /// </summary>
    public static bool SpeechOff { get; private set; }

    // ------------------------------------------------------------------
    // Remembering the switches between launches
    //
    // A DEVIATION, and recorded as one in PARITY.md. The original does NOT persist its
    // volume: `global520` is a plain global initialised to 12 in the script's variable block
    // (`Main.sc:664`), and the only FileIO the game performs anywhere is READING a file named
    // "version" into the buffer `global539` (`Main.sc:1199-1201`) — the version string that
    // `Menu.sc:176` prints and `Save.sc` hands to `SaveGame`. The CD decompilation calls the
    // receiving global `gVolume`; the floppy decompilation of the identical code calls it
    // `gVersion`. It has nothing to do with sound. See SettingsStore for the full trail.
    //
    // What the original DOES do is carry the volume inside a saved game — `SaveGame`
    // snapshots the heap, and `Game.sc:110` re-applies `(DoSound sndMASTER_VOLUME global520)`
    // on the restore path because of it. So this is an extension of something the game half
    // does, not an invention; but across a launch it is ours.
    // ------------------------------------------------------------------

    /// <summary>
    /// Stops the four switches writing the file four times when Ctrl-V moves all of them.
    /// </summary>
    private static bool _settingsBatch;

    /// <summary>
    /// Reads the remembered switches, if there are any. Called by each head once the audio
    /// player is in place and before the UI is built, so the first binding already reads the
    /// remembered value and nothing has had a chance to make a noise.
    ///
    /// <para>
    /// The backing fields are set directly rather than through the properties: the setters
    /// lift Ctrl-V's master when they are switched ON, which is right for a player pressing a
    /// button and wrong for restoring a state where the master was deliberately down.
    /// </para>
    /// </summary>
    public static void LoadSoundSettings()
    {
        if (SettingsStore.ReadFile() is not { } file) return;

        var s = file.Sound;
        MusicOff = s.MusicOff;
        EffectsOff = s.EffectsOff;
        SpeechOff = s.SpeechOff;
        Sound.Enabled = s.SoundEnabled;

        // The interface face, by the same argument the sound switches are persisted by: a
        // player who chose one and quit should not find it reset. `LoadSetting` defers to
        // `JONES_UI_FONT` if that is set, and to the bitmap face if Quicksand will not parse.
        UiFont.LoadSetting(file.Interface.QuicksandInterfaceFont);
    }

    /// <summary>
    /// Writes the four switches. Called from every place one of them changes — both the key
    /// handler and the on-screen switches go through the same properties, so there is one
    /// path. A failure is silent: the game has no message for it.
    /// </summary>
    private static void SaveSoundSettings()
    {
        if (_settingsBatch) return;

        SettingsStore.Write(new SoundSettings
        {
            SoundEnabled = Sound.Enabled,
            MusicOff = MusicOff,
            EffectsOff = EffectsOff,
            SpeechOff = SpeechOff,
        },
        new InterfaceSettings
        {
            QuicksandInterfaceFont = UiFont.Enabled,
        });
    }

    // ------------------------------------------------------------------
    // The interface face
    // ------------------------------------------------------------------

    /// <summary>
    /// THE INTERFACE FACE SWITCH, as the player sees it — off is the game's own font 10.
    ///
    /// <para>
    /// A DELIBERATE DEVIATION, and it is live on purpose. Setting it rebuilds the screen
    /// immediately, so the same shop list or job list redraws in the other face with nothing
    /// else changing: that back-to-back comparison on one screen is the only way to judge it,
    /// and a switch that took effect on the next screen would be useless for it.
    /// </para>
    ///
    /// <para>
    /// IT IS NOT ON A KEY, and that is checked rather than assumed. The game binds F1, F4, F5,
    /// F6, F7, F8, F9 and F10 (text 997[6]), Ctrl-Q, Ctrl-S, Ctrl-T, Ctrl-V, Ctrl-Y, Ctrl-Z,
    /// Esc, Ctrl-Left and Shift-Left (997[7]); this port has taken F2 and F3 for the floppy's
    /// music and effects items; and every list in the game binds the DIGITS as its own
    /// accelerators — eighteen of them at Z-Mart alone (`discount.sc`), ten on the Factory's
    /// job list. What is left is F11 and F12, which the game never mentions, and inventing a
    /// binding out of them is exactly the kind of thing working rule 1 is defending against.
    /// So it lives beside the sound switches instead: modern chrome, outside the Viewbox,
    /// where the port's other non-Sierra controls already are — and reachable on Android,
    /// which has no keyboard at all.
    /// </para>
    /// </summary>
    public bool QuicksandFont
    {
        get => UiFont.Enabled;
        set
        {
            if (UiFont.Enabled == value) return;

            UiFont.Current = value ? UiFont.Face.Quicksand : UiFont.Face.Bitmap;
            SaveSoundSettings();

            OnPropertyChanged();

            // Every line on screen is a view model built with the old face. Rebuilding is
            // what makes this a comparison rather than a restart.
            BuildScreen();
        }
    }

    /// <summary>False where the font could not be loaded, which hides the switch.</summary>
    public static bool QuicksandFontAvailable => UiFont.Available;

    // ------------------------------------------------------------------
    // The on-screen sound switches
    //
    // NOT PORTED FROM ANYTHING. The original's switches are `Turn Music Off `#2` and
    // `Turn Sound Effects Off `#3` on the floppy's Options menu (`Menu.sc:128`), with the
    // master volume on Ctrl-V — all three keyboard-only, and this port is heading for
    // Android where there is no keyboard at all. So these are three touch targets that do
    // the same thing, plus a speech mute the game never had.
    //
    // They are drawn OUTSIDE the 320x200 canvas, over the letterbox, precisely so nobody
    // later mistakes them for Sierra's. Nothing inside the game's own coordinate space is
    // touched by them. The keys still work and stay in step, because both routes go
    // through these same properties.
    // ------------------------------------------------------------------

    /// <summary>Bound to the effects switch. True when effects are audible.</summary>
    public bool EffectsOn
    {
        get => !EffectsOff;
        set
        {
            if (EffectsOff == !value) return;
            EffectsOff = !value;
            if (value) Sound.Enabled = true;      // lift Ctrl-V's master if it is down
            SaveSoundSettings();
            OnPropertyChanged(nameof(EffectsOn));
        }
    }

    /// <summary>Bound to the music switch. True when the bed is audible.</summary>
    public bool MusicOn
    {
        get => !MusicOff;
        set
        {
            if (MusicOff == !value) return;
            MusicOff = !value;
            if (value) Sound.Enabled = true;      // lift Ctrl-V's master if it is down

            // Turning it off silences what is playing; turning it back on restarts the bed
            // the current screen would have started, rather than leaving the player in
            // silence until they next walk through a door.
            if (MusicOff) LocalSound.StopMusic(); else PlayCurrentBed();
            SaveSoundSettings();
            OnPropertyChanged(nameof(MusicOn));
        }
    }

    /// <summary>Bound to the speech switch. True when dialogue is audible.</summary>
    public bool SpeechOn
    {
        get => !SpeechOff;
        set
        {
            if (SpeechOff == !value) return;
            SpeechOff = !value;
            if (value) Sound.Enabled = true;      // lift Ctrl-V's master if it is down

            // Cut the line in progress rather than letting it finish over a muted game.
            // The balloon stays: the subtitle is the point of muting rather than quitting.
            if (SpeechOff) LocalSound.StopSpeech();
            SaveSoundSettings();
            OnPropertyChanged(nameof(SpeechOn));
        }
    }

    /// <summary>
    /// Restarts whatever bed the screen the player is looking at would have started, so
    /// switching music back on does not leave them in silence until they next walk through
    /// a door. The board and the location panel are the only two screens with a bed of
    /// their own that is not already tied to an event.
    /// </summary>
    private void PlayCurrentBed()
    {
        // A joiner has no screen of its own to pick a bed from; it restarts the host's.
        if (_remoteView)
        {
            if (_remoteBed is { } bed) Sound.PlayMusic(bed.Id, bed.Loop);
            return;
        }

        if (_game is null) return;

        switch (_screen)
        {
            case Screen.Board: Music(Audio.LocationMusic.Board); break;
            case Screen.LocationPanel: PlayLocationBed(); break;
            case Screen.Weekend: Music(Audio.LocationMusic.Weekend); break;
        }
    }

    /// <summary>Plays a music bed unless F2 has silenced them.</summary>
    private static void Music(int soundResource, bool loop = true)
    {
        if (!MusicOff) Sound.PlayMusic(soundResource, loop);
    }

    /// <summary>Plays a one-shot effect unless F3 has silenced them.</summary>
    internal static void Effect(int soundResource)
    {
        if (!EffectsOff) Sound.PlayEffect(soundResource);
    }

    /// <summary>
    /// The pair `(gASong pause: 1)` / `(gASoundEffect play: n gASong)`, which is how the
    /// game ducks a location's bed under a sting: `employment.sc:35-36` and `:52-53`, and
    /// `rentOffice.sc:257-301`. The second argument is the sound's `client`, and
    /// `aSong::cue` is `(self pause: 0)` (`Main.sc:1267-1270`), so the bed comes back by
    /// itself when the sting ends.
    ///
    /// F3 is checked BEFORE the pause rather than inside <see cref="Effect"/>: with the
    /// effects silenced there is no sting to cue the bed back, and pausing anyway would
    /// leave the location silent for the rest of the visit.
    /// </summary>
    private static void DuckedSting(int soundResource)
    {
        if (EffectsOff)
        {
            MutedHere.PauseMusic();
            MutedHere.PlayEffect(soundResource, resumeMusicWhenDone: true);
            return;
        }

        Sound.PauseMusic();
        Sound.PlayEffect(soundResource, resumeMusicWhenDone: true);
    }

    /// <summary>`gASoundEffect2 play: n` — the second effect slot, unless F3 is set.</summary>
    private static void Effect2(int soundResource)
    {
        if (!EffectsOff) Sound.PlayEffect2(soundResource);
    }

    public void GoTo(LocationId id)
    {
        if (_game is null || IsWalking) return;

        // `openDoor:` blocks the interpreter for its three `(Wait 6)`s, so no click can be
        // taken while it plays.
        if (IsDoorOpening) return;

        // Jones's turn is already played out by the time the board is shown; clicking a
        // building would be the human moving Jones's marble.
        if (P.IsJones) return;

        // `Place::doit` (room1.sc:146-167) plays the click in both of its branches â€” the
        // one that sends the marble off and the one that just re-opens the building you
        // are standing on. The whole handler is gated on hours remaining (room1.sc:141),
        // so a click with the week spent makes no sound.
        if (!_game.Clock.TurnOver) Effect(Audio.LocationMusic.ButtonClick);

        var from = P.Location;
        // Re-opening the building you are already standing in is still a fresh dialog,
        // so it clears any drilled-in sub-screen too. See Arrive().
        if (from == id)
        {
            // `Place::handleEvent`'s second branch is a bare `(self cue:)` (room1.sc:166),
            // the same `cue` the marble's arrival runs â€” so the door animates on this path
            // too. See Arrive/EnterBuilding.
            OpenDoor(id, () =>
            {
                _jobListFor = null;
                _pawnMode = PawnMode.Menu;
                _pawnSelected = null;
                ResetPiggyBank();
                ResetWorkClock();
                _screen = Screen.LocationPanel;
                PlayLocationBed();
                BuildScreen();
            });
            return;
        }

        // Check the move is legal and charge the hours before setting off, as the
        // original does - running out of hours mid-journey ends the turn without
        // arriving, which is why the cost is taken up front.
        // A refused move says NOTHING. `Place::doit` (room1.sc) simply declines to start
        // the marble; there is no message anywhere for a shut Rent Office or an empty
        // clock, and the two sentences that used to be printed here were mine. The clock
        // dial on the board is the feedback the game gives.
        if (!_game.TravelTo(id))
        {
            BuildScreen();
            return;
        }

        // `TravelTo` resolves a rolled mugging too, for the path that goes straight from
        // one building to the next without closing the panel first.
        NoteMugging();

        // TravelTo has already moved the player in the model; walk the marble there so
        // the journey is visible, then open the building.
        _walkTarget = id;
        _route.Clear();
        foreach (var step in MarblePath.Route(_marbleIndex, Board.Get(id).PathIndex))
            _route.Enqueue(step);

        StartWalking();
        BuildScreen();
    }

    private void StartWalking()
    {
        if (_route.Count == 0) { Arrive(); return; }

        // Smoothing starts from wherever the marble is standing now; the first tick steps
        // away from here. See SmoothMotion — the 25ms per path index is untouched.
        var (sx, sy) = MarblePath.At(_marbleIndex);
        _marbleTween.Jump(sx, sy);

        // `MarblePath::init` ends with `(gTheWalker setCycle: Fwd)` (marblePath.sc:38), and
        // `Cycle::init` sets `lastTime (GetTime)` — so the cel clock restarts here, with the
        // journey, and not on the marble's steps. See WalkerFrame.
        //
        // The CEL is deliberately not reset with it. `Cycle::init` does not touch the
        // client's cel and neither `marblePath.sc:48` nor `room1.sc:194` does on the way out,
        // so the next journey picks the walk up on whatever foot the last one ended on.
        _walkerCycler.Restart(NowMs);

        _walkTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _walkTimer.Tick -= WalkTick;
        _walkTimer.Tick += WalkTick;
        _walkTimer.Start();

        EnsureSmoothTimer();
    }

    private void WalkTick(object? sender, EventArgs e)
    {
        if (_route.Count == 0)
        {
            _walkTimer?.Stop();
            Arrive();
            return;
        }

        _marbleIndex = _route.Dequeue();

        // The index — the thing the game counts — has already moved. This only records
        // where that index is on screen and how long the marble has to get there.
        var (tx, ty) = MarblePath.At(_marbleIndex);
        _marbleTween.Step(tx, ty, NowMs, CycleMs);

        // The walker's cels are NOT advanced here. `setCycle: Fwd` runs on the walker's own
        // `ticksToDo 10` clock, not on the marble's steps — see WalkerFrame, which reads it
        // off the wall clock at draw time. Stepping it here is what made him vibrate.
        BuildScreen();
    }

    /// <summary>
    /// Which cel of the walk cycle is showing, and how far through that cel we are.
    ///
    /// THIS USED TO BE WRONG, and it was the whole of "the walker looks like trash". The
    /// port advanced the cel once per marble step — `_walkCel = (_walkCel + 1) % 4` in
    /// <see cref="WalkTick"/>, i.e. every <see cref="CycleMs"/> — so the four frames played
    /// in 100ms: ten complete walk cycles a second. Sixty per cent of the figure's pixels
    /// change between adjacent cels, so at that rate a photographed actor does not walk, he
    /// vibrates.
    ///
    /// The original does not tie the two together at all. The cel rate is the walker's OWN
    /// property and the marble's speed is the marble's:
    ///
    /// <list type="bullet">
    /// <item>`theWalker` declares `ticksToDo 10` (room1.sc:1060);</item>
    /// <item>`marble` declares `ticksToDo 1` and `moveSpeed 1` (room1.sc:1083-1084), and the
    ///   Game Speed gauge rewrites only the MARBLE's pair — `ticksToDo: (- 7 temp4)
    ///   moveSpeed: (- 7 temp4)` at Menu.sc:307-310, normal 6, so 1 at the default setting.
    ///   Nothing anywhere writes the walker's. The marble's own `ticksToDo` is in fact
    ///   INERT: nothing ever calls `setCycle:` on it, so no `Cycle` is ever attached to read
    ///   the property — which is why Menu.sc:314 has to guard the third write with
    ///   `(if ((ScriptID 1 7) cycler:))`. Its cel is the player's body, written directly.
    ///   `moveSpeed` is the half of that pair that does anything.</item>
    /// <item>`Cycle::nextCel` (Motion.sc:26-44) gates on `ticksToDo` against the WALL CLOCK,
    ///   not the game cycle: it changes cel only when
    ///   `(u&lt; (+ ticksToDo lastTime) (GetTime))`, then resamples `lastTime`.</item>
    /// </list>
    ///
    /// `GetTime` counts 60ths of a second — the same unit as `(proc0_3 240)` being the
    /// notice's documented four seconds. The comparison is STRICT, so a cel holds for
    /// `ticksToDo + 1` ticks: 11/60s, and the four-cel cycle takes about 0.73s. That is a
    /// human walking pace, and it is what the marble's speed setting is deliberately not
    /// allowed to disturb.
    ///
    /// <para>
    /// The rule itself now lives in <see cref="CelCycler"/>, because the walker is not the
    /// only actor that declares `ticksToDo` — Wild Willy declares 8 and had the same bug.
    /// This method is the walker's use of it plus the port's own in-between art.
    /// </para>
    ///
    /// GAME TIMING IS UNTOUCHED by this. The marble still advances exactly one
    /// <see cref="MarblePath"/> index per <see cref="CycleMs"/> tick in <see cref="WalkTick"/>,
    /// so a journey still takes the same number of ticks and the same real time; the hours it
    /// costs were charged by `Game.TravelTo` before it set off. All that changed is which
    /// picture of the man is on screen while that happens — the cels are art, not position.
    ///
    /// <para>
    /// The second return value is the IN-BETWEEN frame within the cel. At 11 ticks a cel and
    /// a 16ms redraw the same picture is drawn eleven times over, which is a slideshow; the
    /// sub-frames are interpolated art generated by <c>tools/smooth_walk.py</c> and are
    /// pure decoration — <see cref="SciArt.SubCel"/> falls back to the plain cel when they
    /// are not on disk, and <see cref="SmoothMotion"/> off suppresses them entirely, which
    /// restores the original's own stepping for comparison.
    /// </para>
    /// </summary>
    private (int Cel, int Sub) WalkerFrame()
    {
        if (!IsWalking) return (0, 0);

        var now = NowMs;
        var cel = _walkerCycler.Advance(now, WalkerCels);

        if (!SmoothMotion) return (cel, 0);

        var sub = (int)(_walkerCycler.Phase(now) * WalkerSubFrames);
        return (cel, Math.Clamp(sub, 0, WalkerSubFrames - 1));
    }

    /// <summary>
    /// `theWalker ticksToDo 10` (room1.sc:1060) — 11 ticks a cel, 183ms, a 0.73s walk cycle.
    /// The arithmetic is <see cref="CelCycler"/>'s; this is only the script's number.
    /// </summary>
    private const int WalkerTicksToDo = 10;

    /// <summary>The walk loop is four cels in every walker view (280-297, and 274-277).</summary>
    private const int WalkerCels = 4;

    /// <summary>
    /// In-between frames generated per cel, including the cel itself — 4 gives about 22
    /// distinct pictures a second out of a 5.5-per-second original. Must match the
    /// <c>SUBS</c> constant in <c>tools/smooth_walk.py</c>, which names the files.
    /// </summary>
    private const int WalkerSubFrames = 4;

    private void Arrive()
    {
        // The marble may have been walking HOME to close out the week rather than walking
        // into a building. `proc1_9` (room1.sc:50-64) sends it to the player's own front
        // door once the 60 Hours are gone; when it gets there `room1::doit`
        // (room1.sc:1369-1373) cues `marble::cue:` (room1.sc:1092), which zeroes the
        // clock, bumps the week if play has come back round to the first player, and calls
        // `startTurn:`. That is the whole of Jones's turn hand-over â€” there is no button.
        if (_goingHome)
        {
            _goingHome = false;
            BeginTurn();
            return;
        }

        // THE DOOR OPENS FIRST. `Place::cue` (room1.sc:174-209) runs `openDoor:` and its
        // three blocking `(Wait 6)`s BEFORE `((ScriptID sNumber 0) init: room1)` puts the
        // building's dialog up, so the board is still showing while the four cels play.
        // `OpenDoor` runs the continuation immediately when this building's door does not
        // animate, which is exactly what the `global516 = 0` arm of that test does.
        OpenDoor(P.Location, EnterBuilding);
    }

    /// <summary>
    /// `Place::cue` from `((ScriptID sNumber 0) init: room1)` (`room1.sc:209`) onwards — the
    /// building's dialog itself, once the door is open.
    /// </summary>
    private void EnterBuilding()
    {
        if (_game is null) return;

        _screen = Screen.LocationPanel;

        // Every building opens FRESH. In the original each location is a dialog that is
        // built on entry and destroyed on the way out â€” `employment.sc:360-364` disposes
        // the dialog and then the whole script â€” so nothing you were looking at last time
        // can still be on screen when you walk back in. Any drilled-in sub-screen state
        // has to be cleared here to match; leaving it set showed you the job list you
        // happened to be reading on your previous visit instead of the employer list.
        _jobListFor = null;
        _pawnMode = PawnMode.Menu;
        _pawnSelected = null;

        // `items::init` runs with the rest of the dialog's `eachElementDo: #init`, so the
        // picture panel starts over on every visit too. `piggyBank::init` is `(= cel 0)`
        // (`bank.sc:519-521`) and the work clock is drawn on cel 0 until it is run, so both
        // of those reset with it.
        ResetItemsPanel(P.Location);
        ResetPiggyBank();
        ResetWorkClock();

        // The shopkeeper greets you as you walk in, from the greeting range read out of
        // each location's own script. The offsets are NOT uniform: the Employment Office
        // greets with `420 + Random(6,12)` (`employment.sc:327-330`), starting at offset
        // SIX, because 420-425 are the job-application outcomes.
        //
        // `proc0_14` latches per player per location, so you are greeted only on your
        // first visit to a building.
        var band = Audio.SpeechBands.For(P.Location);
        if (band is not null && _greeted.Add((_game!.CurrentPlayerIndex, P.Location)))
            Speak(_stockRng.Next(band.First, band.Last));

        PlayLocationBed();
        BuildScreen();
    }

    /// <summary>
    /// `gASong playBed: n`, the first thing every building's dialog does once it is on
    /// screen â€” `appliance.sc:119`, `bank.sc:103`, `employment.sc:322` and the rest, one
    /// per location. Starting it replaces whatever the board was playing, which is what
    /// happens in the original too: there is a single `gASong`.
    /// </summary>
    private void PlayLocationBed()
    {
        var bed = Audio.LocationMusic.For(P.Location);
        if (bed != 0) Music(bed);
    }

    /// <summary>True while the marble is walking the player home to close out the week.</summary>
    private bool _goingHome;

    /// <summary>
    /// Stepping back out of a building onto the board. `Place::endCue` (room1.sc:246-283)
    /// is what runs as a building's dialog closes, and its last act before the board comes
    /// back is `(proc1_9)` (room1.sc:273). With the week's 60 Hours gone that hands play
    /// on and walks the marble home; with Hours left it does nothing at all.
    ///
    /// This is the piece that was missing. Nothing in the port ever ended a turn, so once
    /// `Clock.TurnOver` went true every action was disabled and `TravelTo` refused every
    /// destination â€” the board was still drawn, but no click did anything.
    /// </summary>
    private void LeaveBuilding()
    {
        // `(DoAudio audSTOP)` is the dialog's own exit step â€” see StopTalking().
        StopTalking();

        // The dialog is disposed on the way out and the panel's cycler with it
        // (`DCIcon::dispose`); every shop also does `(items setCycle: 0)` explicitly when
        // the work button runs (`fastFood.sc:329` and siblings). The piggy bank and the
        // work clock are DIcons in the same dialog and go with it.
        _itemsTimer?.Stop();
        _piggyTimer?.Stop();
        StopWorkClock();

        // `(gASong fade:)` is the very next line in every location's exit path
        // (`employment.sc:347`, `bank.sc:135`, `market.sc:157`, ...). A fade, not a cut:
        // Sound.sc:102-104 spells out the arguments.
        Sound.StopMusic();

        // Captured before EndTurn, which rolls the clock on to the next player.
        var weekSpent = _game is not null && _game.Clock.TurnOver;

        _screen = Screen.Board;

        // `Place::cue` (room1.sc:223-242) checks `global446` the moment the building's
        // dialog returns and, if it is set, hands the room to script 114 before anything
        // else. That is this point: the panel has just closed.
        _game?.ResolvePendingMugging();
        NoteMugging();

        // proc1_9 fires only at exactly 60 Hours used (room1.sc:51).
        if (_game is not null && _game.Clock.TurnOver && !_goingHome && !IsWalking)
        {
            // `players::doit` (room1.sc:971-1011) runs the OUTGOING player's `endTurn:`
            // and rotates `global302` on to the next player before proc1_9 starts the
            // marble moving, so the marble walks to the INCOMING player's front door.
            _game.EndTurn();

            var home = P.LivesAt == 0
                ? LocationId.LowCostHousing
                : LocationId.SecurityApartments;

            _goingHome = true;
            _route.Clear();
            foreach (var step in MarblePath.Route(_marbleIndex, Board.Get(home).PathIndex))
                _route.Enqueue(step);

            StartWalking();
        }

        // `Place::endCue` brings the board theme back on the way out of a building, but
        // only while there are hours left in the week (room1.sc:280-282). With the week
        // spent the marble walks home in silence.
        if (!weekSpent) Music(Audio.LocationMusic.Board);

        // `(if global516 (self closeDoor:))` is `endCue`'s last act before the theme
        // (room1.sc:273-282), and unlike `openDoor:` it does not block: the `Beg` cycler
        // runs the door shut while the board is back and the marble may already be walking.
        CloseDoor();

        BuildScreen();
    }

    /// <summary>
    /// Wild Willy, heard rather than seen. `muggedByMarket.sc:30-31` and `:94-95` are
    /// identical openings to the two mugging scripts:
    ///
    /// <code>
    /// (gASong stop:)
    /// (gASoundEffect play: 20)
    /// </code>
    ///
    /// A CUT, not the fade the exit itself just performed, and then the sting. Both
    /// scripts then walk `willy` across the board and print the paper; this port draws
    /// neither, so the board's own theme comes straight back where the original holds it
    /// until `Place::endCue` runs after the animation.
    ///
    /// Both sites now roll: the Bank at 1-in-31 (`bank.sc:120-122`) and Black's Market at
    /// 1-in-51 (`market.sc:142-144`), which is what `Game.MuggingSite` carries through.
    /// </summary>
    private void NoteMugging()
    {
        if (_game is null || _game.LastMuggingSite == Game.MuggingSite.None) return;

        Sound.CutMusic();
        Effect(Audio.SoundEffects.Mugging);

        StartWillyWalk(_game.LastMuggingSite);
    }

    // ------------------------------------------------------------------
    // Wild Willy — `muggedByMarket` / `muggedByBank`, script 114
    // ------------------------------------------------------------------

    /// <summary>
    /// One leg of Willy's walk: the loop he faces in, where he is going, and — for the two
    /// states that are a pause rather than a move — how long he stands there.
    /// </summary>
    /// <param name="Loop">`setLoop:`.</param>
    /// <param name="ToX">Destination x, or the current x for a pause.</param>
    /// <param name="ToY">Destination y.</param>
    /// <param name="Cel">A fixed cel for a pause leg, or -1 to keep cycling `Fwd`.</param>
    /// <param name="PauseSeconds">`(= seconds 3)` for the robbery itself, 0 otherwise. WALL-CLOCK
    /// seconds — see <see cref="WillyTick"/>, which is where this used to be counted wrong.</param>
    /// <param name="Restart">Whether this state calls `setCycle: Fwd`. That clears signal
    /// $1000 and resamples `Cycle::lastTime`, so the cel clock starts again here. The states
    /// that only say `setLoop:` leave the running cycler — and its clock — alone.</param>
    /// <param name="ResetCel">Whether this state also writes `cel: 0` outright. Two of the
    /// four `setCycle: Fwd` states do and two do not; `setCycle:` itself never resets a cel.
    /// </param>
    private sealed record WillyLeg(
        int Loop, int ToX, int ToY,
        int Cel = -1, int PauseSeconds = 0, bool Restart = false, bool ResetCel = false);

    /// <summary>
    /// `muggedByBank` (`muggedByMarket.sc:86-148`). He comes out of the alley beside the
    /// Bank at (17,119), works his way down the left-hand side, turns to face the player for
    /// three seconds at loop 6 while he takes the money, and walks off the bottom.
    /// </summary>
    private static readonly WillyLeg[] WillyAtBank =
    [
        // (0 … loop: 3 setCycle: Fwd setMotion: MoveTo 5 125 self)
        new(3, 5, 125, Restart: true),

        // (1 (willy setLoop: 4 setMotion: MoveTo 5 152 self))   — no setCycle:, so the
        // (2 (willy setLoop: 2 setMotion: MoveTo 10 152 self))     clock and cel carry on
        new(4, 5, 152),
        new(2, 10, 152),

        // (3 (willy setLoop: 6 setCel: 0) (= seconds 3))
        new(6, 10, 152, Cel: 0, PauseSeconds: 3),

        // (4 (willy setLoop: 4 cel: 0 setCycle: Fwd setMotion: MoveTo 0 173 self))
        new(4, 0, 173, Restart: true, ResetCel: true),
    ];

    /// <summary>
    /// `muggedByMarket` (`:22-84`). A different corner and a different path: in at (31,166),
    /// up past the market front, the same three-second pause on loop 7, then off to the left.
    /// </summary>
    private static readonly WillyLeg[] WillyAtMarket =
    [
        // (0 … loop: 5 setCycle: Fwd setMotion: MoveTo 64 141 self)
        new(5, 64, 141, Restart: true),

        // (1 (willy setMotion: MoveTo 57 110 self))  — same loop, same running cycler
        new(5, 57, 110),

        // `(willy setLoop: 7 setCel: 1)` — loop 7 holds ONE cel, so cel 1 does not exist.
        // `Actor::setCel` clamps to `lastCel` (Actor.sc:153-159), so the original shows cel 0
        // too; the script's own number is recorded here and CelCycler.FixCel does the clamp.
        new(7, 57, 110, Cel: 1, PauseSeconds: 3),

        // (3 (willy setLoop: 3 cel: 0 setCycle: Fwd setMotion: MoveTo 17 109 self))
        new(3, 17, 109, Restart: true, ResetCel: true),

        // (4 (willy setLoop: 5 setCycle: Fwd setMotion: MoveTo -10 72 self)) — NO `cel: 0`,
        // so the cel carries over from loop 3 and Fwd wraps it if it overshoots loop 5.
        new(5, -10, 72, Restart: true),
    ];

    private WillyLeg[]? _willyPath;
    private int _willyLeg, _willyX, _willyY, _willyCycle;

    /// <summary>When the current pause leg began, on the same wall clock `(= seconds n)` uses.</summary>
    private long _willyPauseStartMs;
    private DispatcherTimer? _willyTimer;

    /// <summary>`willy`'s cycler. Its clock is his own, not the mover's — see <see cref="WillyTicksToDo"/>.</summary>
    private readonly CelCycler _willyCycler = new(WillyTicksToDo);

    /// <summary>
    /// `willy` — view 340, `setStep: 3 3`, `moveSpeed 3`, `ticksToDo 8`
    /// (`muggedByMarket.sc:150-161`).
    ///
    /// <para>
    /// THE TWO NUMBERS ARE NOT THE SAME MECHANISM, and conflating them is the bug this pair
    /// of constants now separates. `moveSpeed 3` belongs to the MOVER: it skips three game
    /// cycles between 3-pixel steps, which is the 100ms-per-step cadence below. `ticksToDo 8`
    /// belongs to the CYCLER and is measured against `GetTime`, so his eight-cel walk changes
    /// picture every 9/60s — 150ms, not 100ms. The port drove the cels off the move cadence,
    /// which ran the walk 50% fast. See <see cref="CelCycler"/>.
    /// </para>
    /// </summary>
    private const int WillyView = 340, WillyStep = 3, WillyMoveSpeed = 3, WillyTicksToDo = 8;

    /// <summary>
    /// How many cels each of view 340's eight loops holds, read off the resource. The two
    /// the scripts freeze on — 6 and 7 — hold exactly one apiece, which is why
    /// `(willy setLoop: 7 setCel: 1)` asks for a cel that is not there.
    /// </summary>
    private static readonly int[] WillyCels = [8, 1, 8, 8, 6, 6, 1, 1];

    private void StartWillyWalk(Game.MuggingSite site)
    {
        _willyPath = site == Game.MuggingSite.Bank ? WillyAtBank : WillyAtMarket;
        (_willyX, _willyY) = site == Game.MuggingSite.Bank ? (17, 119) : (31, 166);
        _willyLeg = 0;
        _willyCycle = 0;

        // State 0 on both scripts is `init: … setCycle: Fwd`, and `init:` is the first time
        // the actor exists, so cel 0 with a fresh clock.
        _willyCycler.PutCel(0);
        EnterWillyLeg(0);

        _willyTimer ??= new DispatcherTimer();
        _willyTimer.Stop();
        _willyTimer.Interval = TimeSpan.FromMilliseconds(CycleMs);
        _willyTimer.Tick -= WillyTick;
        _willyTimer.Tick += WillyTick;
        _willyTimer.Start();

        _willyTween.Jump(_willyX, _willyY);
        EnsureSmoothTimer();
    }

    private void WillyTick(object? sender, EventArgs e)
    {
        if (_willyPath is null || _screen != Screen.Board) { StopWilly(); return; }

        _willyCycle++;

        var leg = _willyPath[_willyLeg];

        if (leg.PauseSeconds > 0)
        {
            // `(= seconds 3)` IS THREE WALL-CLOCK SECONDS, not a count of game cycles.
            // `Timer::doit` (`System.sc:551-557`) decrements `seconds` only when
            // `(GetTime 1)` — SysTime12, the real-time clock — changes value, exactly as
            // `ticksToDo` gates on `GetTime`. This used to be `3 * 60` counted down by this
            // 25ms timer, which is 40 ticks a second, not 60: the robbery froze for 4.5s.
            // Same conflation of the game cycle with the wall clock that `CelCycler` removed
            // from the cel rate one field above. The notice's `(proc0_3 240)` already
            // converts the same way — `TimeSpan.FromSeconds(240 / 60.0)` in StartNextNotice.
            if (NowMs - _willyPauseStartMs < leg.PauseSeconds * 1000L) { BuildScreen(); return; }
        }
        else
        {
            // `moveSpeed 3` skips three cycles between steps; `setStep: 3 3` is the step.
            if (_willyCycle % (WillyMoveSpeed + 1) != 0) return;

            _willyX += Math.Sign(leg.ToX - _willyX) * Math.Min(WillyStep, Math.Abs(leg.ToX - _willyX));
            _willyY += Math.Sign(leg.ToY - _willyY) * Math.Min(WillyStep, Math.Abs(leg.ToY - _willyY));

            // The step itself is unchanged: 3 pixels, once every fourth cycle. The tween
            // spreads those 3 pixels over the 100ms between steps — see SmoothMotion.
            _willyTween.Step(_willyX, _willyY, NowMs, CycleMs * (WillyMoveSpeed + 1));

            if (_willyX != leg.ToX || _willyY != leg.ToY) { BuildScreen(); return; }
        }

        _willyLeg++;

        if (_willyLeg < _willyPath.Length) { EnterWillyLeg(_willyLeg); BuildScreen(); return; }

        // State 5 on both scripts: `(willy dispose:)`, `(= global415 16)` — the street
        // mugging headline — and then the paper, `((ScriptID 215 0) init: 0)`.
        StopWilly();
        _headlineToShow = 16;
        StartNewspaper();
    }

    /// <summary>
    /// What `changeState` does to the CYCLER when Willy enters a state, in the order the
    /// scripts write it: `cel: 0`, then `setCycle: Fwd`, or `setCel: n` for the two states
    /// that freeze him. A state that says none of those leaves the cycler running with the
    /// clock it already had, which is the point of doing this per state rather than per tick.
    /// </summary>
    private void EnterWillyLeg(int index)
    {
        if (_willyPath is null) return;

        var leg = _willyPath[index];

        if (leg.ResetCel) _willyCycler.PutCel(0);
        if (leg.Restart) _willyCycler.Restart(NowMs);
        if (leg.Cel >= 0) _willyCycler.FixCel(leg.Cel, WillyCels[leg.Loop]);

        // `(= seconds 3)` is written by the same `changeState` case, so the countdown starts
        // when the state is entered — not when the previous move happened to finish.
        if (leg.PauseSeconds > 0) _willyPauseStartMs = NowMs;
    }

    private void StopWilly()
    {
        _willyTimer?.Stop();
        _willyPath = null;
        BuildScreen();
    }

    private void BuildWilly()
    {
        if (_willyPath is null) return;

        var leg = _willyPath[_willyLeg];

        // `setCycle: Fwd` runs the loop's cels round and round on WILLY'S OWN CLOCK; a pause
        // leg freezes on the cel the script names. This used to read
        // `_willyCycle / (WillyMoveSpeed + 1) % cels` — the mover's cadence, not the
        // cycler's, which played his eight cels in 800ms instead of 1200ms. EnterWillyLeg
        // holds the cycler's state; all that happens here is reading it at draw time.
        var cel = _willyCycler.Advance(NowMs, WillyCels[leg.Loop]);

        var bmp = SciArt.Cel(WillyView, leg.Loop, cel);
        if (bmp is null) return;

        var (wx, wy) = SmoothMotion ? _willyTween.At(NowMs) : ((double)_willyX, (double)_willyY);

        Sprites.Add(new SpriteVm(WillyView, leg.Loop, cel,
            wx - bmp.PixelSize.Width / 2.0, wy - bmp.PixelSize.Height));
    }

    public ICommand CloseePanelCommand => new RelayCommand(() =>
    {
        Effect(Audio.LocationMusic.ButtonClick);
        LeaveBuilding();
    });

    public ICommand EndTurnCommand => new RelayCommand(() =>
    {
        if (RouteInput(Jones.Net.InputKind.EndTurn)) return;
        if (_game is null) return;
        Effect(Audio.LocationMusic.ButtonClick);
        _game.EndTurn();
        BeginTurn();
    });

    // ------------------------------------------------------------------
    // Location actions
    // ------------------------------------------------------------------

    /// <summary>
    /// Each location's own interior art, read from the `background`, `theTalker` and
    /// `items` instances in that location's script. The shopkeeper portrait and the
    /// backdrop are the original's, not stand-ins.
    /// </summary>
    /// <remarks>
    /// <c>BackgroundLoop</c> is loop 0 everywhere but the Rent Office. `rentOffice.sc:185-191`
    /// declares its `background` as <c>view 701 LOOP 1</c>, and loop 1 is the one 183x112 cel
    /// that fills the dialog; view 701 loop 0 is the two title plates — cel 0 the 49x30
    /// `RENT OFFICE` sign (`theShortTitle`) and cel 1 the 116x16 bar (`theLongTitleLeft`).
    /// Drawing loop 0 cel 0 as the backdrop left five sixths of the panel unpainted, so the
    /// board — walker, badges and all — showed through it.
    /// </remarks>
    private static (int Background, int BackgroundLoop, int Talker, int Items) ArtFor(LocationId id) => id switch
    {
        LocationId.ZMart            => (811, 0, 361, 711),
        LocationId.SocketCity       => (808, 0, 358, 708),
        LocationId.QtClothing       => (809, 0, 359, 709),
        LocationId.BlacksMarket     => (803, 0, 353, 703),
        LocationId.MonolithBurgers  => (810, 0, 360, 710),
        LocationId.HiTechU          => (807, 0, 357, 0),
        LocationId.EmploymentOffice => (706, 0, 356, 0),
        LocationId.Bank             => (804, 0, 354, 0),
        LocationId.PawnShop         => (712, 0, 362, 708),
        LocationId.RentOffice       => (701, 1, 351, 0),
        LocationId.Factory          => (705, 0, 355, 0),
        _                           => (0, 0, 0, 0),
    };

    /// <summary>
    /// `livesAt` in the SOURCE's own encoding, which is **0 for Low-Cost Housing and 2 for
    /// Le Security Apartments** — `security.sc:47` and `:49` both test `== 2`, and
    /// `rentOffice.sc:394` / `:437` are what write the two values. There is no 1.
    ///
    /// The port stores 0/1 on <see cref="Player.LivesAt"/> (`Player.cs:65`), and the core
    /// tests it as `LivesAt == 0` in several places (`Player.cs:280`, `:294`,
    /// `MainViewModel.cs:1578`). Rather than renumber the field and disturb all of that,
    /// the two encodings are translated at this one boundary — the only place a script
    /// literal (0 / 2) is compared against it.
    /// </summary>
    private int ScriptLivesAt => P.LivesAt == 0 ? 0 : 2;

    /// <summary>
    /// `temp1` in `rentOffice::init`, which decides which of three completely different
    /// dialogs the Rent Office is (`rentOffice.sc:65-71`, branched at `:78-108`):
    ///
    /// <code>
    /// (= temp1 0)
    /// (if (== (global302 worksAt:) 1) (= temp1 -1))
    /// (if (or (not (mod global372 4)) (global302 leaveOpen:)) (= temp1 1))
    /// </code>
    ///
    /// <list type="bullet">
    /// <item><c>1</c> — rent week, or a granted extension: the clerk, the long title bar and
    ///   all five lines.</item>
    /// <item><c>-1</c> — you WORK here and it is not rent week: the clerk and the short title
    ///   plate, and no lines. This is the state that has nothing to click.</item>
    /// <item><c>0</c> — shut: backdrop 697 and nothing else.</item>
    /// </list>
    ///
    /// The order matters — the rent-week test runs SECOND and overwrites <c>-1</c>, so an
    /// employee still gets the full menu during a rent week.
    /// </summary>
    private int RentOfficeTemp1
    {
        get
        {
            var temp1 = 0;
            if (P.WorksAt == Board.RentOfficeWorksAt) temp1 = -1;
            if (_game is not null && (_game.Calendar.Week % 4 == 0 || P.LeaveOpen)) temp1 = 1;
            return temp1;
        }
    }

    /// <summary>
    /// Whether the current player lives in the apartment being drawn — the condition both
    /// apartment scripts wrap their backdrop, their contents and their relax button in
    /// (`lowcost.sc:47/49`, `security.sc:47/49`). False everywhere that is not an apartment.
    /// </summary>
    private bool LivesHere(LocationId id) => id switch
    {
        LocationId.LowCostHousing     => ScriptLivesAt == 0,
        LocationId.SecurityApartments => ScriptLivesAt == 2,
        _                             => false,
    };

    // ------------------------------------------------------------------
    // The `items` picture panel (`gItems`)
    // ------------------------------------------------------------------

    private int _itemsCel;
    private int _itemsLoop;

    /// <summary>
    /// Whether the panel is on screen at all. Every shop but one adds `items` inside the
    /// `add:` list that precedes `eachElementDo: #init` / `#setSize` / `open:`, so it is
    /// drawn with the rest of the dialog. The Pawn Shoppe adds it AFTER `open:`
    /// (`pawnShop.sc:441` then `:449`), so it is never initialised or drawn on entry â€”
    /// `items::doit` is what runs `init: setSize: draw:` there (`pawnShop.sc:915`), and the
    /// panel therefore stays blank until you take something off the rack.
    /// </summary>
    private bool _itemsShown;

    /// <summary>
    /// The cycler's own counter. `Cycle::nextCel` (`Motion.sc:45-57`) increments it once
    /// per game cycle and only changes cel once it passes `client cycleSpeed:`, which is
    /// 300 on every one of these panels. A purchase sets it to âˆ’400
    /// (`fastFood.sc:395` and siblings), which holds the bought item's picture on screen
    /// for a further 700 cycles before the parade resumes.
    /// </summary>
    private int _itemsCount;

    /// <summary>Z-Mart's cycler indexes the six lines on show, not the view's cels (`FS`).</summary>
    private int _itemsIndex;

    private DispatcherTimer? _itemsTimer;

    /// <summary>
    /// One SCI game cycle. The port already fixes this rate in <see cref="StartWalking"/>,
    /// where the marble advances one path index per cycle exactly as `MarblePath::doit`
    /// does, so the panel counts against the same clock rather than inventing a second one.
    /// At 25ms a cel change every 301 cycles is about 7.5 seconds.
    /// </summary>
    private const int CycleMs = 25;

    /// <summary>`items::cycleSpeed` â€” 300 in all six shops.</summary>
    private const int ItemsCycleSpeed = 300;

    /// <summary>The celNums of the six lines Z-Mart is showing, in the order it shows them.</summary>
    private int[] ZMartPanelCels() =>
        [.. _zmartStock.Select(s => StoreLayout.ItemCel(LocationId.ZMart, s.Name))];

    /// <summary>
    /// Opens the panel on the cel the script gives it. Z-Mart is the one shop that does not
    /// start at its declared cel: `discount.sc:194` runs `(items cel: [local0 0])` once the
    /// random six have been chosen, so the panel opens on whichever line came out first.
    ///
    /// Note that this is a bare `cel:` and not the `doit:` that would also set the loop, so
    /// a first line with celNum 16 or 17 asks loop 1 for a cel it does not have (view 711
    /// loop 1 holds 16). The original draws nothing in that case and so does this â€” see
    /// `SciArt.Cel`, which returns null for a combination that is not in the resource.
    /// </summary>
    private void ResetItemsPanel(LocationId id)
    {
        _itemsTimer?.Stop();

        if (StoreLayout.ItemsFor(id) is not { } panel) return;

        _itemsTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CycleMs) };
        _itemsTimer.Tick -= ItemsTick;
        _itemsTimer.Tick += ItemsTick;

        _itemsLoop = panel.Loop;
        _itemsCount = 0;
        _itemsIndex = 0;
        _itemsShown = panel.Cycles;
        _itemsCel = id == LocationId.ZMart
            ? ZMartPanelCels() is { Length: > 0 } cels ? cels[0] : 0
            : panel.FirstCel;

        // `items::init` is `(if (< global534 2) (self setCycle: FwdCount self))` â€” the Pawn
        // Shoppe alone declares no init, so its panel has no cycler and does not run until
        // a purchase gives it an FCue.
        if (panel.Cycles) _itemsTimer.Start();
    }

    /// <summary>
    /// One game cycle of the panel's cycler.
    ///
    /// `items::cycle` in all six shops is
    /// <c>(if (== (DoAudio audPOSITION) -1) (super cycle:))</c> â€” the parade stops dead
    /// while anyone is speaking and picks up where it left off afterwards, which is why the
    /// shopkeeper's line is never competing with a moving picture.
    /// </summary>
    private void ItemsTick(object? sender, EventArgs e)
    {
        if (_screen != Screen.LocationPanel || _game is null) { _itemsTimer?.Stop(); return; }
        if (StoreLayout.ItemsFor(P.Location) is not { } panel) { _itemsTimer?.Stop(); return; }

        // `(== (DoAudio audPOSITION) -1)` â€” nothing is playing.
        if (Sound.SpeechPosition is not null) return;

        if (++_itemsCount <= ItemsCycleSpeed) return;
        _itemsCount = 0;

        if (!panel.Cycles)
        {
            // The Pawn Shoppe's one-shot `FCue` (`pawnShop.sc:915`), whose `cue` puts the
            // panel back on its resting cel 13 (`pawnShop.sc:920-923`) and stops.
            _itemsTimer?.Stop();
            _itemsCel = StoreLayout.PawnShopRestCel;
            _itemsLoop = panel.Loop;
            BuildScreen();
            return;
        }

        if (P.Location == LocationId.ZMart)
        {
            // `FS::doit` / `FS::next` (`discount.sc:73-92`): step through the six shown
            // lines, wrapping at `lastCel` 5, and take each one's loop from its celNum.
            var cels = ZMartPanelCels();
            if (cels.Length == 0) return;

            if (++_itemsIndex > panel.LastCel) _itemsIndex = 0;
            var (loop, cel) = StoreLayout.ZMartPicture(cels[_itemsIndex % cels.Length]);
            _itemsLoop = loop;
            _itemsCel = cel;
        }
        else
        {
            // `FwdCount::doit` (`FwdCount.sc:20-26`): one cel forward, and `cycleDone`
            // drops back to cel 0 once it passes `lastCel`.
            _itemsCel = _itemsCel + 1 > panel.LastCel ? 0 : _itemsCel + 1;
        }

        BuildScreen();
    }

    /// <summary>
    /// `(if (and gItems (IsObject gItems) (&lt; global534 2)) (gItems doit: celNum))` â€”
    /// `WButton.sc:236-237`, inside `CostDItem::doit`'s success branch, and again at
    /// `pawnShop.sc:821-822` for a second-hand durable.
    ///
    /// `global534` is the graphics detail level, moved by Ctrl-T (`Menu.sc:362-383`); at
    /// level 2 or 3 the whole panel is skipped. The port has no detail setting, so the
    /// panel is always drawn and this test is always taken.
    /// </summary>
    private void ShowBoughtItem(int celNum)
    {
        if (StoreLayout.ItemsFor(P.Location) is not { } panel) return;

        _itemsShown = true;

        if (P.Location == LocationId.ZMart)
        {
            var (loop, cel) = StoreLayout.ZMartPicture(celNum);
            _itemsLoop = loop;
            _itemsCel = cel;
        }
        else
        {
            _itemsLoop = panel.Loop;
            _itemsCel = celNum;
        }

        // `(cycler cycleCnt: -400)` â€” the picture holds before the parade resumes. The Pawn
        // Shoppe uses âˆ’100 and an `FCue` that returns to cel 13 (`pawnShop.sc:915-923`).
        _itemsCount = panel.Cycles ? -400 : -100;

        // The Pawn Shoppe's panel is given its cycler only now, by the purchase itself
        // (`self ... setCycle: FCue self`), so the timer has to be started here.
        if (!panel.Cycles) _itemsTimer?.Start();
    }

    /// <summary>
    /// Lays the interior out the way the store scripts do. Every shop dialog is the same
    /// shape â€” `moveTo: 69 44`, nsRight 184, nsBottom 119 â€” and the backdrop DIcon fills
    /// it at loop 0, cel 0. The exit button is view 250 loop 0 at (143,108) and the work
    /// button view 250 loop 1 at (75,108), in all five shops.
    ///
    /// Item positions come from <see cref="StoreLayout"/>, which holds the explicit
    /// nsLeft/nsTop each script declares per item. ONLY Z-Mart computes them at runtime
    /// (`discount.sc:186-193`): it shows a random six of eighteen, so it must â€”
    /// `nsTop = (n + 1) * 13 + 20` and `nsLeft` 11 for the first two lines, 78 after.
    /// </summary>
    private void BuildLocationPanel()
    {
        var here = Board.Get(P.Location);
        var (bg, bgLoop, talker, _) = ArtFor(here.Id);

        // The two apartments are the only interiors whose backdrop is not fixed: each
        // script assigns `background view:` from whether THIS player lives here
        // (`lowcost.sc:47`, `security.sc:47`). They have no shopkeeper and no `items`
        // panel, so `ArtFor` rightly gives them nothing.
        var livesHere = LivesHere(here.Id);
        var apartmentBg = StoreLayout.ApartmentBackground(here.Id, ScriptLivesAt);
        if (apartmentBg != 0) bg = apartmentBg;

        // A workplace's job list is its own dialog (scripts 216-224) with a DIFFERENT
        // backdrop: view 706 CEL 1, the plain tan panel â€” not cel 0, which is the
        // Employment Office interior. It shows no shopkeeper.
        var showingJobs = here.Id == LocationId.EmploymentOffice && _jobListFor is not null;

        // A SHUT Rent Office is a different picture, not the open one with its contents
        // removed. `rentOffice.sc:102-108` — the `else` arm of the three-way `cond` on
        // `temp1` — replaces the backdrop with `view: 697 loop: 0 cel: 0`, the boarded
        // shopfront, and adds NOTHING: no `theTalker`, no title plate, no lines. The port
        // kept the open interior (701 loop 1) with the clerk and the title bar and merely
        // dropped the five lines, which is why it read as a broken empty room.
        var rentOfficeShut = here.Id == LocationId.RentOffice && RentOfficeTemp1 == 0;
        if (rentOfficeShut)
        {
            bg = 697;
            bgLoop = 0;
            talker = 0;
        }

        if (showingJobs)
        {
            Sprites.Add(Icon(706, 0, 1, 0, 0));
        }
        else
        {
            if (bg != 0) Sprites.Add(Icon(bg, bgLoop, 0, 0, 0));

            // The Rent Office's title plate, which is a SEPARATE DIcon from its backdrop.
            // `rentOffice.sc:537-551`: `theShortTitle` is view 701 loop 0 cel 0 at nsLeft 67,
            // `theLongTitleLeft` is view 701 loop 0 CEL 1 at the dialog origin. The long one
            // goes up with the full rent menu; the short one when you WORK here, because
            // `:109-111` deletes the long title and adds `theShortTitle workButton` in its
            // place, and `:99` uses the short title on its own outside a rent week.
            //
            // Neither goes up on a shut office: `:102-108` adds no title at all.
            if (here.Id == LocationId.RentOffice && !rentOfficeShut)
            {
                var worksHere = P.WorksAt == Board.RentOfficeWorksAt;
                Sprites.Add(worksHere ? Icon(701, 0, 0, 67, 0) : Icon(701, 0, 1, 0, 0));
            }

            // The shopkeeper. Each location positions its own portrait; several override
            // the Talker class default of (115, 1). The cel is the current mouth
            // position, driven by the clip's own lip-sync data while they are speaking.
            if (talker != 0)
            {
                var (tx, ty) = StoreLayout.TalkerAt(here.Id);
                Sprites.Add(Icon(talker, 0, _talkerCel, tx, ty));
            }

            // The `items` picture panel, third in every shop's `add:` list â€” after the
            // backdrop and the shopkeeper, before the price lines (`appliance.sc:93-108`,
            // `market.sc:99-110`). It was missing entirely; the shops were drawing their
            // list against bare backdrop.
            if (_itemsShown && StoreLayout.ItemsFor(here.Id) is { } panel)
                Sprites.Add(Icon(panel.View, _itemsLoop, _itemsCel, panel.Left, panel.Top));

            // The Bank has no `items` panel at all â€” it never publishes `gItems` â€” it has
            // `piggyBank`, which occupies the same slot and is third in its unconditional
            // `add:` list (`bank.sc:73-84`), right where `items` sits everywhere else.
            if (here.Id == LocationId.Bank)
                Sprites.Add(Icon(PiggyView, PiggyLoop, _piggyCel, PiggyLeft, PiggyTop));

            // What the apartment CONTAINS. Both scripts add these inside the same
            // `(if (== (global302 livesAt:) ...))` that chooses the lived-in backdrop
            // (`lowcost.sc:49-54`, `security.sc:49-63`), so the room you are only visiting
            // is bare. At Le Security each one is conditional on still owning that durable
            // (`objectAtIndexQuan:`), which is the room filling up as you buy things; at
            // Low-Cost the three fixtures come free with the flat.
            if (livesHere && StoreLayout.Furnishings(here.Id) is { } furnishings)
            {
                foreach (var f in furnishings)
                {
                    if (f.DurableId != 0 && !P.Durables.Holds(f.DurableId)) continue;
                    Sprites.Add(Icon(f.View, f.Loop, f.Cel, f.Left, f.Top));
                }
            }
        }

        BuildLocationActions();

        // relaxButton: view 250 loop 3 at nsLeft 9, nsTop 108 (`lowcost.sc:104-113`,
        // `security.sc:113-121`). It is a button, not a line of text — and both scripts add
        // it ONLY inside the lives-here branch (`lowcost.sc:49-50`, `security.sc:49-50`), so
        // there is nothing to relax on in the apartment you are merely visiting.
        if (livesHere)
            Sprites.Add(Icon(250, 3, 0, StoreLayout.RelaxLeft, StoreLayout.RelaxTop, Relax));

        // Hi-Tech U: the enrollment fee line, the enroll button, and the courses as icons.
        if (here.Id == LocationId.HiTechU) BuildUniversity();

        // The Pawn Shoppe: three mode buttons and, once one is chosen, a list.
        if (here.Id == LocationId.PawnShop) BuildPawnShop();

        // The Employment Office is a menu of WORKPLACES, not a list of jobs: picking one
        // opens that workplace's own job list. Its nine entries have fixed coordinates.
        if (here.Id == LocationId.EmploymentOffice && _jobListFor is null)
        {
            var office = StoreLayout.ColoursFor(LocationId.EmploymentOffice);

            foreach (var (label, place, left, top, key) in StoreLayout.Employment)
            {
                var employer = place;
                MenuLines.Add(new MenuLineVm(label, DialogX + left, DialogY + top,
                    () => { _jobListFor = employer; BuildScreen(); },
                    office.Text, office.Shadow, office.Flash, key: key));
            }
        }
        else if (here.Id == LocationId.EmploymentOffice && _jobListFor is { } employer)
        {
            // A workplace's own job list (scripts 216-224): a header, then one line per
            // job at nsLeft 25 with the spacing that workplace uses.
            var (header, hLeft, hTop, firstTop, spacing) = StoreLayout.JobList(employer);
            var jobs = StoreLayout.JobListColours;

            // `jobsAvailable` is a WButton with `state 0` — bit 0 clear, so `Item::handleEvent`
            // drops every event on it (`Interface.sc:410-416`). It is a CAPTION, and it is
            // drawn exactly like the lines below it: the same shadow 107, the same font 10
            // (`applianceJobs.sc:108-116` and its eight siblings). Nine of the port's eleven
            // `state 0` declarations are these headers.
            Texts.Add(new TextVm(header, DialogX + hLeft, DialogY + hTop, 7,
                                 SciPalette.Hex(jobs.Text), true, shadow: jobs.Shadow));

            var row = 0;
            // DECLARATION order, not wage order. Each script lists its JobDItem instances
            // with an explicit nsTop (`fastFoodJobs.sc:118-176`: 50, 60, 70, 80), and
            // Jobs.All is stored in that same order. Sorting by wage was my own idea and
            // happens to agree at Monolith only because its list is already ascending.
            foreach (var job in Jobs.At(employer))
            {
                var j = job;
                var y = DialogY + firstTop + row * spacing;

                // The wage shown is the CURRENT wage, not the base: it is run through the
                // goods index like every other price, so a slump really does mean lower
                // pay on the board. Format is "$N Hr." (`employment.sc`, res 206,0).
                var wage = Pricing.Price(_game!.GoodsIndex, j.BaseWage);

                // The label is the source string verbatim: its trailing spaces and pipes are
                // the padding that puts every wage in one column in FONT 10. `Line` also
                // names the `$N Hr.` suffix, which is what lets the outline face put that
                // column back where font 10 had it — see `UiFont`. Neither edits the string.
                var label = ItemText.WageLine(ItemText.For(j), wage);

                MenuLines.Add(new MenuLineVm(label.Full, DialogX + StoreLayout.JobLeft, y,
                    () =>
                    {
                        _jobListFor = null;
                        var outcome = _game!.ApplyFor(j);
                        SpeakJobOutcome(outcome);
                        BuildScreen();
                    },
                    jobs.Text, jobs.Shadow, jobs.Flash, key: StoreLayout.JobKey(j.JobNum),
                    value: label.Value));

                row++;
            }
        }
        else
        {
            // Every other location has its own fixed item coordinates. ONLY Z-Mart lays
            // out at runtime, because it shows a random six of eighteen.
            var slots = StoreLayout.For(here.Id);
            var shop = StoreLayout.ColoursFor(here.Id);
            var shown = 0;

            foreach (var a in Actions)
            {
                if (shown >= (slots?.Length ?? 6)) break;

                int left, top, key;
                if (slots is not null && shown < slots.Length)
                {
                    left = slots[shown].Left;
                    top = slots[shown].Top;
                    key = slots[shown].Key;
                }
                else
                {
                    // NOT WIRED: Z-Mart's eighteen items each declare a `key`
                    // (`discount.sc` — 1-8, 10-12, 14, 16-18, 20, 23, 24), but they belong
                    // to the ITEM and this screen is the one the port lays out at runtime
                    // from `Actions`, which carry a label and nothing that identifies which
                    // DiscountDItem they came from. Binding them needs the item identity
                    // carried through, not a guess from the formatted label.
                    key = 0;
                    // Z-Mart only. discount.sc:186-193 walks the dialog's surviving
                    // DiscountDItems and sets `nsTop: (+ (* (+ n 1) 13) 20)` on each,
                    // then `nsLeft: 78` once n >= 2. Every instance declares nsLeft 11,
                    // which is what the first two lines keep.
                    top = (shown + 1) * 13 + 20;
                    left = shown >= 2 ? 78 : 11;
                }

                var item = a;

                // `Enabled` is `state` bit 0. It used to be checked INSIDE the handler,
                // which means the line still took the click and swallowed it;
                // `Item::handleEvent` never gets that far — it tests the bit before it even
                // looks at the event (`Interface.sc:410`). The line is still drawn.
                MenuLines.Add(new MenuLineVm(item.MenuText, DialogX + left, DialogY + top,
                    () => item.Command.Execute(null),
                    shop.Text, shop.Shadow, shop.Flash,
                    enabled: item.Enabled, key: key, value: item.Value));
                shown++;
            }
        }

        // workButton: view 250 loop 1 at nsLeft 75, nsTop 108, added only when the
        // player works here (`discount.sc:180` tests worksAt against the place).
        if (here.Workplace is { } wp && P.WorksAt == (int)wp + 1)
            Sprites.Add(Icon(250, 1, 0, 75, 108, WorkShift));

        // exitButton: view 250 at nsLeft 143, nsTop 108. Closing the dialog runs
        // `Place::endCue`, which is where the turn ends if the Hours are gone. Every
        // location uses loop 0 except the Pawn Shoppe, whose `exitButton` declares
        // loop 13 (`pawnShop.sc:613-620`).
        Sprites.Add(Icon(250, here.Id == LocationId.PawnShop ? 13 : 0, 0, 143, 108, LeaveBuilding));

        // The work clock, LAST: `(self add: timeClock)` runs after `open:` in all nine
        // workplaces, so it is the final element and draws over everything, including the
        // `items` panel it shares a position with at Socket City.
        BuildWorkClock(here.Id);
    }

    /// <summary>Which pawn-shop list is open: none, Pawn, Redeem or Buy (`local0`).</summary>
    private enum PawnMode { Menu, Pawn, Redeem, Buy }

    private PawnMode _pawnMode;
    private Item? _pawnSelected;

    /// <summary>
    /// The Pawn Shoppe (`pawnShop.sc`, script 212). Its dialog has three states, and the
    /// three mode buttons are replaced by whichever list you pick (`localproc_5` through
    /// `localproc_12`).
    ///
    /// Everything here is the script's own geometry and the resources' own words:
    ///
    /// * background view 712, theTalker view 362 at nsLeft 0, exitButton view 250 LOOP 13.
    /// * pawnButton / redeemButton / buyButton, all view 712 loop 1 cels 0/1/2, at
    ///   nsLeft 76 and nsTop 40 / 60 / 80.
    /// * `Pawnable Items` (raw script 212 @0x1B29) at nsLeft 95 nsTop 27,
    ///   `Redeemable Items` (@0x1BA5) at 91/25, `Buyable Items` (@0x1B8C) at 93/25.
    /// * the redeem and buy lists are CostDItems at nsLeft 78, nsTop 40 + 10n, named from
    ///   text 700 through text 212[2] `"%s"` and priced through 212[4] `"%s $%d"` â€” one
    ///   space whatever the figure, because `aRedeemableItem` overrides `doFormat`.
    /// * `pawn` is view 250 loop 6 at (77,108); all three Done buttons are view 250 loop 2
    ///   at (110,108).
    ///
    /// Two things are NOT reproduced exactly and are flagged rather than guessed at: the
    /// pawnable list is a `DSelector` (a framed, scrolling widget with its own insets of
    /// x 18 / y 4) which is drawn here as plain rows on the selector's own `moveTo: 71 40`
    /// at the same 10px pitch the other two lists use; and the Yes/No `Print` that carries
    /// text 212[3] `Do you accept $%d for your %s?` needs modal-dialog machinery the port
    /// does not have yet, so the offer is taken as accepted.
    /// </summary>
    private void BuildPawnShop()
    {
        // `pawnMessage`, `buyMessage`, `redeemMessage` and `aRedeemableItem` all declare
        // textColor 26 / shadowColor 65 (`pawnShop.sc:638-639`, `:792-793`, `:803-804`,
        // `:812-813`). The `#000080` the three captions used to be drawn in was invented.
        var pawnColours = StoreLayout.ColoursFor(LocationId.PawnShop);

        if (_pawnMode == PawnMode.Menu)
        {
            // localproc_5. The three buttons only appear when no list is open.
            // `key 1` / `2` / `3` on pawnButton / redeemButton / buyButton
            // (`pawnShop.sc:507-511`, `:557-561`, `:585-589`).
            Sprites.Add(Icon(712, 1, 0, 76, 40, () => OpenPawnList(PawnMode.Pawn)));
            Sprites.Add(Icon(712, 1, 1, 76, 60, () => OpenPawnList(PawnMode.Redeem)));
            Sprites.Add(Icon(712, 1, 2, 76, 80, () => OpenPawnList(PawnMode.Buy)));
            Accelerator(1, () => OpenPawnList(PawnMode.Pawn));
            Accelerator(2, () => OpenPawnList(PawnMode.Redeem));
            Accelerator(3, () => OpenPawnList(PawnMode.Buy));
            return;
        }

        // donePawning / doneRedeeming / doneBuying: view 250 loop 2 at (110,108).
        Sprites.Add(Icon(250, 2, 0, 110, 108, () =>
        {
            _pawnMode = PawnMode.Menu;
            _pawnSelected = null;
            BuildScreen();
        }));

        if (_pawnMode == PawnMode.Pawn)
        {
            // `pawnMessage`: state 288, so bit 0 is clear — a caption, not a control.
            Texts.Add(new TextVm("Pawnable Items", DialogX + 95, DialogY + 27, 7,
                                 SciPalette.Hex(pawnColours.Text), true,
                                 shadow: pawnColours.Shadow));

            var row = 0;
            foreach (var d in Pawnable().ToList())
            {
                var item = d;
                var chosen = ReferenceEquals(item, _pawnSelected);

                // text 212[1] is "%-18s": the name, left-aligned in an 18-character field.
                MenuLines.Add(new MenuLineVm(
                    $"{Catalogue.NameOf(item.IndexNum),-18}",
                    DialogX + 71, DialogY + 40 + 10 * row,
                    () => { _pawnSelected = item; BuildScreen(); },
                    pawnColours.Text, pawnColours.Shadow, pawnColours.Flash,
                    selected: chosen));
                row++;
            }

            // `pawn`, view 250 loop 6 at (77,108) â€” the button that makes the offer.
            // `key 5` (`pawnShop.sc:643-647`).
            Sprites.Add(Icon(250, 6, 0, 77, 108, TakeTheOffer));
            Accelerator(5, TakeTheOffer);
            return;
        }

        var buying = _pawnMode == PawnMode.Buy;

        Texts.Add(buying
            ? new TextVm("Buyable Items", DialogX + 93, DialogY + 25, 7,
                         SciPalette.Hex(pawnColours.Text), true, shadow: pawnColours.Shadow)
            : new TextVm("Redeemable Items", DialogX + 91, DialogY + 25, 7,
                         SciPalette.Hex(pawnColours.Text), true, shadow: pawnColours.Shadow));

        var n = 0;
        var rows = buying
            ? _game!.Buyable().ToList()
            : [.. _game!.Redeemable().Select(i => ((Player?)null, i))!];

        foreach (var (owner, d) in rows)
        {
            var item = d;
            var from = owner;

            // 212[4] "%s $%d" â€” one space regardless of the price, unlike CostDItem.
            MenuLines.Add(new MenuLineVm(
                $"{Catalogue.NameOf(item.IndexNum)} ${item.RedemptionPrice}",
                DialogX + 78, DialogY + 40 + 10 * n,
                () =>
                {
                    if (!_game!.Redeem(item, from)) SpeakNotEnoughCash();
                    else
                    {
                        // `aRedeemableItem::doit` names the picture from the durable
                        // itself: `(= celNum (- (theDurable indexNum:) 21))`
                        // (`pawnShop.sc:817`), then `gItems doit: celNum` at `:821-822`.
                        ShowBoughtItem(StoreLayout.PawnShopCel(item.IndexNum));
                        SpeakBoughtItem();
                    }
                    BuildScreen();
                },
                pawnColours.Text, pawnColours.Shadow, pawnColours.Flash));
            n++;
        }
    }

    /// <summary>
    /// Durables that may be pawned: held, and not already in hock â€” unless you have more
    /// than one, which is the `(> quantity 1)` half of `pawnButton::doit`'s test.
    /// </summary>
    private IEnumerable<Item> Pawnable() =>
        P.Durables.Items.Where(i =>
            (i.Quantity > 0 && (i.Attributes & DurableAttributes.WearOrWornMask) == 0)
            || i.Quantity > 1);

    /// <summary>
    /// Opening one of the three lists. Each button first checks that its list would have
    /// anything in it and, if not, SPEAKS the refusal instead of switching: clip 87 for
    /// nothing to pawn, 88 for nothing to redeem, 89 for nothing on the rack
    /// (`pawnShop.sc:516-608`).
    /// </summary>
    private void OpenPawnList(PawnMode mode)
    {
        var any = mode switch
        {
            PawnMode.Pawn => Pawnable().Any(),
            PawnMode.Redeem => _game!.Redeemable().Any(),
            _ => _game!.Buyable().Any(),
        };

        if (!any)
        {
            Speak(mode switch { PawnMode.Pawn => 87, PawnMode.Redeem => 88, _ => 89 });
            BuildScreen();
            return;
        }

        _pawnMode = mode;
        _pawnSelected = null;
        BuildScreen();
    }

    /// <summary>
    /// `pawn::doit` (`pawnShop.sc:642-712`). Three refusals before any money changes hands:
    /// clip 90 when six or more items are already in hock across ALL players, clip 91 when
    /// the highlighted row is one of your own tickets rather than a pawnable item, and
    /// nothing at all when no row is selected. Otherwise the broker speaks clip 92 and
    /// makes his offer.
    /// </summary>
    private void TakeTheOffer()
    {
        if (_game is null) return;

        // localproc_4 counts every ticketed or forfeited durable held by every player.
        var inHock = _game.Players.Sum(pl => pl.Durables.Items.Count(i =>
            (i.Attributes & DurableAttributes.WearOrWornMask) != 0));

        if (inHock >= 6) { Speak(90); BuildScreen(); return; }

        if (_pawnSelected is not { } item) { BuildScreen(); return; }

        if ((item.Attributes & DurableAttributes.WearOrWornMask) != 0 && item.Quantity <= 1)
        {
            Speak(91);
            BuildScreen();
            return;
        }

        // ONE balloon. The floppy's `pawnShop.sc:780` prints the offer and both buttons in a
        // single `Print` â€” text 212[25], `I'll give you $%d for your %s, take it or leave
        // it.`, `#width 107`, `#button {Take It} 1` and `#button {Leave It} 0` â€” and the CD
        // speaks that same sentence as clip 92. CD text 212[3] (`Do you accept $%d for your
        // %s?`) is the short prompt the CD left behind; printing it as well made a second box.
        //
        // The `%s` is a NESTED resource reference: `(Format @global100 212 25 local1 700
        // (local2 indexNum:))` feeds it from text 700 at the durable's own indexNum â€”
        // 700[21] `Refrigerator`, 700[29] `Computer`. The name is never spelled out in the
        // script.
        var offer = Pricing.PawnOffer(_game.GoodsIndex, item.PricePaid);
        var name = SciText.Get(700, item.IndexNum);

        SpeakQuestion(92, 107, answer =>
        {
            // `(gASoundEffect play: 23)` on BOTH branches â€” `pawnShop.sc:684` and `:704`.
            Effect(Audio.LocationMusic.ButtonClick);
            if (answer == 0) return;

            _game.Pawn(item);
            if (!Pawnable().Contains(item)) _pawnSelected = null;
        },
        [offer, name],
        new BalloonButton("Take It", 1),
        new BalloonButton("Leave It", 0));

        BuildScreen();
    }

    /// <summary>True once this player has worked this turn â€” the original's `global329`.</summary>
    private bool _workedThisTurn;

    /// <summary>`global478`: the end-of-week chime sounds once a turn. See BuildBoard.</summary>
    private bool _weekOverChimed;

    /// <summary>`global562`: the garnishment line is spoken at most once a turn.</summary>
    private bool _garnishSpokenThisTurn;

    /// <summary>
    /// A shift, and what the boss says about it. `proc108_0` (`n108.sc:18-57`) speaks every
    /// outcome, at a clip id built from the base plus `global400` â€” which is the place
    /// number, i.e. <see cref="Location.PlaceNum"/>:
    ///
    ///     900 + place  you are not properly dressed for work
    ///     920 + place  you have missed too much work, you're fired
    ///     940 + place  your work habits had better pick up soon
    ///     960 + place  no time is left to work (the clock is already at 60)
    ///     980 + place  the landlord garnished your wages, once per turn
    ///
    /// A shift that simply goes well says NOTHING â€” there is no "You earned $96." line in
    /// the game, and `n108.sc` contains no `Print` at all. Text resource 108 holds the same
    /// five sentences as printed strings, but this build never formats them: they are the
    /// floppy's copies of what the CD speaks, which is why the subtitles match them.
    /// </summary>
    private void WorkShift()
    {
        if (_game is null) return;

        var place = Board.Get(P.Location).PlaceNum;

        // The conditions are read BEFORE the shift, in the order proc108_0 tests them.
        var outOfTime = _game.Clock.TurnOver;
        var dressed = P.DressedForWork();
        var sacked = P.Dependibility < P.MinDepend - 5;
        var warned = !sacked && P.Dependibility < P.MinDepend - 2 && !_workedThisTurn;
        var owedRent = P.RentOwed > 0;

        _game.Work();

        if (outOfTime) Speak(960 + place);
        else if (!dressed) Speak(900 + place);
        else if (sacked)
        {
            // `n108.sc:28-29`: the sacking branch plays effect 30 BEFORE the boss speaks.
            Effect(Audio.SoundEffects.Sacked);
            Speak(920 + place);
        }
        else
        {
            _workedThisTurn = true;

            // Both can fire on the same shift. The original calls proc0_18 twice and the
            // second clip simply takes over, so the garnishment is what you actually hear.
            if (warned) Speak(940 + place);
            if (owedRent && !_garnishSpokenThisTurn)
            {
                _garnishSpokenThisTurn = true;

                // "Your Landlord garnished $%d." â€” the amount is what `n108.sc:93-99`
                // actually took out of this shift, which (the replicated quirk) it works
                // out from the UN-prorated gross.
                Speak(980 + place, _game.LastGarnished);
            }

            // The work clock. `workButton::doit` (`appliance.sc:518-527` and the same
            // method in all six workplaces) runs
            //
            //     ((and (< global323 60) (> global566 0)) (items setCycle: 0) (timeClock doit:))
            //
            // where `global566` is `proc108_0`'s return, and only the shift that actually
            // happened returns 1 (`n108.sc:114`). `TimeClock::doit` is `(self cel: 0
            // setCycle: FwdCount self 1)` then `(gASoundEffect play: 31)` (`WButton.sc:306-310`)
            // â€” the clock face spinning down the six hours.
            //
            // The hours test is read AFTER the shift, because `localproc_0` spends them at
            // `n108.sc:116` before `workButton` gets its answer back. A shift that takes
            // the week to exactly 60 therefore ends on the end-of-week chime, not on this.
            if (!_game.Clock.TurnOver)
            {
                Effect(Audio.SoundEffects.WorkClock);

                // `(timeClock doit:)` itself, which is the same line of the same `cond`.
                // One pass of view 750's four cels at `cycleSpeed 10`; its `cue` then puts
                // the shop's `items` parade back, or at the Bank starts the piggy bank.
                StartWorkClock();
            }
        }

        BuildScreen();
    }

    /// <summary>
    /// `relaxButton::doit` (`lowcost.sc:113-129`, `security.sc`). The six hours are spent
    /// first and the clock is read BEFORE they are; if the week was already gone the
    /// apartment speaks clip 210 (Low Cost) or 370 (Security) â€” "No time is left to relax."
    /// â€” and nothing else happens. A successful relax says nothing at all.
    ///
    /// The apartments have no Talker, so the balloon has nowhere to hang and the subtitle
    /// does not appear: the line is audio only, exactly as the original plays it.
    /// </summary>
    private void Relax()
    {
        var wasOver = _game!.Clock.TurnOver;

        var flag = _relaxedThisTurn;
        _game.Relax(ref flag);
        _relaxedThisTurn = flag;

        if (wasOver)
            Speak(P.Location == LocationId.SecurityApartments ? 370 : 210);

        BuildScreen();
    }

    /// <summary>
    /// Hi-Tech U (`university.sc`). Nothing here is a menu line:
    ///
    /// * `enrollmentFee` is a display-only CostDItem at nsLeft 85, nsTop 32 whose text is
    ///   `Enrollment Fee ` (raw script 207 @0x1611) with the fee appended by CostDItem's
    ///   own format â€” `Enrollment Fee  $50`, not "Pay enrollment fee" with a separate $50.
    /// * `enrollButton` is view 250 loop 10 at (105,108).
    /// * every course is an ICON: view 707 loop 2, cel = degreeId - 10, stacked UPWARD
    ///   from the bottom at nsLeft 76, nsTop 108 - 14n, highest degree first
    ///   (`localproc_2` adds publishing down to tradeSchool).
    /// * lessons remaining is a BARE NUMBER â€” `Format â€¦ 207 0`, i.e. `"%d"` â€” displayed at
    ///   absolute x 222 (`university.sc:130-141`), which is 153 inside the dialog.
    /// * `books`, view 707 loop 1, is the shelf behind them, its cel chosen by how many
    ///   courses are showing.
    ///
    /// "Study Trade School", "3 lessons left", "Graduated - Electronics!", "Enrolled. Pick
    /// a course." and the rest were all mine.
    /// </summary>
    private void BuildUniversity()
    {
        var fee = University.EnrollmentFee(_game!.GoodsIndex);
        var feeLine = ItemText.PriceLine("Enrollment Fee ", fee);
        Texts.Add(new TextVm(feeLine.Full, DialogX + 85, DialogY + 32, 7, "#000080",
                             value: feeLine.Value));

        var courses = Degrees.AvailableTo(P).OrderByDescending(d => d.Id).Take(4).ToList();

        // books: nsLeft 71, cel = count - 1, nsTop = 42 + (4 - count) * 14.
        if (courses.Count > 0)
            Sprites.Add(Icon(707, 1, courses.Count - 1, 71, 42 + (4 - courses.Count) * 14));

        // enrollButton, view 250 loop 10. ONE balloon, not two: the floppy's
        // `university.sc:746` prints the whole question — text 207[28], "The enrollment fee
        // is $%d. Would you like to enroll?" — with the talker tail, `#width 113` and both
        // buttons in a single `Print`. The CD speaks that same sentence as clip 406 and
        // keeps a short `Enroll for $%d?` at ITS 207[1]; printing that as well put a second
        // box on screen that the game never had. See <see cref="SpeakQuestion"/>.
        Sprites.Add(Icon(250, 10, 0, StoreLayout.EnrollLeft, StoreLayout.EnrollTop, () =>
        {
            // `(enrollmentFee price:)` â€” the economy-adjusted fee, the same figure printed
            // on the counter above.
            SpeakQuestion(406, 113, answer =>
            {
                if (answer == 0)
                {
                    // `(global427 (proc0_18 408 …))` â€” university.sc:696-698. Declining is
                    // not silent: the clerk says so, and nothing is charged.
                    Speak(408);
                    return;
                }

                // `(enrollmentFee doit:)` â€” a CostDItem, and `WButton::doit` plays 23
                // before anything else (`WButton.sc:152-153`).
                Effect(Audio.LocationMusic.ButtonClick);
                if (!University.Enroll(P, _game!.GoodsIndex)) SpeakNotEnoughCash();
            },
            [fee],
            new BalloonButton("Yes", 1),
            new BalloonButton("No", 0));

            BuildScreen();
        }));

        for (var i = 0; i < courses.Count; i++)
        {
            var degree = courses[i];
            var (left, top) = StoreLayout.CourseSlot(i);

            Sprites.Add(Icon(707, 2, degree.Id - 10, left, top, () =>
            {
                var outcome = University.Study(P, degree.Id, _game!.Clock);
                P.RecalculateGoals(_game!.Economy);

                // `(if (global302 hasDegree: indexNum) â€¦ ((ScriptID 230 0) init: client
                // indexNum))` â€” university.sc:205-223. The lesson that completes the course
                // opens the diploma; every other lesson draws nothing.
                if (outcome == StudyOutcome.Graduated) { ShowDiploma(degree.Id); return; }

                // The other outcomes are spoken in the original (clip 402 out of hours,
                // 403 not enrolled â€” `university.sc:283-290`) and are still silent here;
                // that is GATES.md Â§12's ordering item, not this change.
                BuildScreen();
            }));

            // The remaining count, drawn only while it is inside the course's own range.
            //
            // `localproc_3` (`university.sc:102-145`) draws it at ABSOLUTE
            // `(222, [local5 temp3])` on a background of `[local9 temp3]`, with
            // `[local5 4] = [97 111 125 139]` and `[local9 4] = [98 75 87 56]`
            // (`university.sc:25-26`). Both are indexed by the same `temp3`, so THE COLOUR
            // BELONGS TO THE ROW, not to the course — which is what makes it safe to port
            // even though the two loops walk their lists in different orders. The port's
            // rows land on exactly those four y values (DialogY + top + 1 = 139/125/111/97
            // for `CourseSlot` 0..3), so the mapping is by `top`.
            //
            // This one IS visible: the course bar (view 707 loop 2, 86x9) is transparent at
            // x+77, and the panel behind it (view 807) is a flat #C8E0F8 there — so the
            // script is painting a coloured chip, not blanking in the panel's own colour the
            // way the other nineteen Display fills do.
            var chip = top switch
            {
                94 => 56,   // y 139 — #38B038
                80 => 87,   // y 125 — #6070C8
                66 => 75,   // y 111 — #E03838
                52 => 98,   // y  97 — #9898A8
                _  => SciPalette.Transparent,
            };

            var left1 = Degrees.LessonsRemaining(P, degree.Id);
            if (left1 >= 0)
                Texts.Add(new TextVm($"{left1}", DialogX + 153, DialogY + top + 1, 7, "#000000",
                                     background: chip));
        }
    }

    // ------------------------------------------------------------------
    // The diploma (script 230)
    // ------------------------------------------------------------------

    /// <summary>The degree just graduated, which is `param2` / `local0` in `diploma.sc`.</summary>
    private int _diplomaDegreeId;

    /// <summary>theDiploma's cel, 0..4. The text only appears once the roll is open.</summary>
    private int _diplomaCel;
    private int _diplomaCount;
    private DispatcherTimer? _diplomaTimer;

    /// <summary>
    /// Opening the diploma. `UniversityDIcon::doit` (`university.sc:205-228`) on the lesson
    /// that completes a course:
    ///
    /// <code>
    /// (gASong fade:)                              ; :206
    /// (global302 notEnoughEd: 0)                  ; :207  â€” done in University.Graduate
    /// (proc0_13 5) eduCredit/dependibility/expCredit +5    ; :208-213
    /// ((ScriptID 230 0) init: client indexNum)    ; :223   â€” this dialog
    /// (gASong play: 41)                           ; :227   â€” the U's bed comes back
    /// </code>
    ///
    /// The sting is a SONG, not a sound effect: `diploma.sc:59` is
    /// `(gASong loop: 1 play: 42 compScript)` â€” resource 42, `loop: 1` meaning play it
    /// through once â€” and there is no `gASoundEffect` call anywhere in script 230. It goes
    /// through <see cref="Audio.IAudioPlayer.PlayMusic"/> with looping off, which is the
    /// same call `gASong play:` maps onto everywhere else in this port, so it will sound
    /// the moment the audio work lands.
    /// </summary>
    private void ShowDiploma(int degreeId)
    {
        _diplomaDegreeId = degreeId;
        _diplomaCel = 0;
        _diplomaCount = 0;
        _screen = Screen.Diploma;

        // `(gASong fade:)` at university.sc:206, then song 42 played once.
        Sound.StopMusic();
        Music(Audio.LocationMusic.Diploma, loop: false);

        // `theDiploma::init` is `(if (not global534) (self setCycle: End self))` â€” the roll
        // unfurls cel by cel and cues its own `cue:` at the end, which is what puts the
        // degree name on it. With detail turned down the original skips straight to
        // `(theDiploma cel: 4 draw: cue:)` (`diploma.sc:60-62`); the port has no detail
        // setting, so it always animates. `cycleSpeed 1` means a cel every other cycle.
        _diplomaTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CycleMs) };
        _diplomaTimer.Tick -= DiplomaTick;
        _diplomaTimer.Tick += DiplomaTick;
        _diplomaTimer.Start();

        BuildScreen();
    }

    private void DiplomaTick(object? sender, EventArgs e)
    {
        // `cycleSpeed 1`: Cycle::nextCel changes cel once cycleCnt passes it.
        if (++_diplomaCount <= DiplomaCycleSpeed) return;
        _diplomaCount = 0;

        // `End` stops on `lastCel` rather than wrapping to 0 the way `Fwd` does.
        if (_diplomaCel >= DiplomaLastCel) { _diplomaTimer?.Stop(); return; }

        _diplomaCel++;
        BuildScreen();
    }

    /// <summary>`theDiploma cycleSpeed 1` (`diploma.sc:127`).</summary>
    private const int DiplomaCycleSpeed = 1;

    /// <summary>View 607 loop 0 holds five cels, so `DCIcon::lastCel` is 4.</summary>
    private const int DiplomaLastCel = 4;

    /// <summary>
    /// The diploma dialog (script 230), which the port had not built at all â€” finishing a
    /// course produced nothing on screen.
    ///
    /// Everything here is declared outright in `diploma.sc`:
    ///
    /// * the dialog is the usual 184x119 at (69,44) â€” `nsRight 184`, `nsBottom 119`,
    ///   `moveTo: (client nsLeft:) (client nsTop:)` where the client is the university
    ///   dialog, itself at `moveTo: 69 44`.
    /// * `background`, view 607 LOOP 1, priority 10, at the dialog origin (`diploma.sc:102`).
    /// * `theDiploma`, view 607 loop 0, priority 15, at nsLeft 22 nsTop 15
    ///   (`diploma.sc:121`).
    /// * `doneButton`, an ErasableDIcon, view 250 LOOP 2, nsLeft 143 nsTop 108, key 120
    ///   â€” `x` (`diploma.sc:110`). Same corner as every other dialog's exit.
    /// * `diplomaText` (`diploma.sc:148-171`): the degree's name out of text 700 through
    ///   text 230[0] `"%s"`, measured in FONT 8 and centred on (97,77) â€”
    ///   `dsCOORD (- 97 (/ width 2)) (- 77 (/ height 2))`, `dsCOLOR 0`, `dsWIDTH 150`,
    ///   `dsBACKGROUND -1`, `dsFONT 8`. It is added by `theDiploma::cue`, so it appears
    ///   only once the roll has finished unfurling.
    /// </summary>
    private void BuildDiploma()
    {
        Sprites.Add(Icon(607, 1, 0, 0, 0));
        Sprites.Add(Icon(607, 0, _diplomaCel, 22, 15));

        if (_diplomaCel >= DiplomaLastCel)
        {
            // `(Format @temp4 230 0 700 local0)` â€” text 700 at the degree's own id, which
            // is exactly what Degrees.Name carries (700[10] `Trade School` through 700[20]
            // `Publishing`).
            var name = Degrees.ById(_diplomaDegreeId).Name;

            var font = SciFont.Load(8);
            var w = font?.Measure(name) ?? 0;
            var h = font?.Height ?? 0;

            Texts.Add(new TextVm(name, DialogX + 97 - w / 2, DialogY + 77 - h / 2,
                                 fontNumber: 8, colour: "#000000"));
        }

        Sprites.Add(Icon(250, 2, 0, 143, 108, CloseDiploma));
    }

    /// <summary>
    /// Leaving the diploma. Control returns into `UniversityDIcon::doit`, which brings the
    /// university's own bed back with `(gASong play: 41)` (`university.sc:227`) and redraws
    /// the dialog â€” with the graduated course now gone from the shelf, which
    /// <see cref="BuildUniversity"/> handles on its own because `Degrees.AvailableTo` no
    /// longer lists it.
    /// </summary>
    private void CloseDiploma()
    {
        _diplomaTimer?.Stop();
        _screen = Screen.LocationPanel;

        Music(41);
        ResetItemsPanel(P.Location);
        BuildScreen();
    }

    private void BuildLocationActions()
    {
        var here = Board.Get(P.Location);

        // Relaxing is the relaxButton SPRITE (view 250 loop 3 at nsLeft 9, nsTop 108 â€”
        // `lowcost.sc:103-112`), not a line of text. "Relax", "6 hrs, +3 relaxation" and
        // "You put your feet up." were all mine; the apartments' only line is the one they
        // speak when the week is already gone.

        // Working is offered by the workButton sprite in BuildLocationPanel, at the
        // position the script gives it, not as a line in the item list.

        // Job lists are built directly in BuildLocationPanel, because each job needs its
        // wage right-aligned in a second column rather than appended to its label.

        // Hi-Tech U's courses are ICONS, not menu lines, and its enrollment fee is a
        // display-only CostDItem â€” see BuildUniversity.

        AddStore(here.Id, LocationId.ZMart, Catalogue.ZMart);
        AddStore(here.Id, LocationId.SocketCity, Catalogue.SocketCity);
        AddStore(here.Id, LocationId.QtClothing, Catalogue.QtClothing);
        AddStore(here.Id, LocationId.BlacksMarket, Catalogue.BlacksMarket);
        AddStore(here.Id, LocationId.MonolithBurgers, Catalogue.MonolithBurgers);

        // The Rent Office's five lines are the CostDItem/WButton `text` properties from
        // raw script 201 â€” see StoreLayout.RentOffice. The `add:` list is
        // `theTalker theLongTitleLeft payRent moreTime rentLowCost rentSecurity`
        // (`rentOffice.sc:82-90`), with `payGarnishment` appended only when `rentOwed` is
        // non-zero (`:91-94`).
        //
        // The whole list is behind `temp1 == 1`. The other two arms of that `cond` add no
        // lines at all: `temp1 == -1` (you work here, outside a rent week) is the clerk and
        // the short title plate, and `temp1 == 0` is the shut office — see
        // <see cref="RentOfficeTemp1"/> and the backdrop swap in BuildLocationPanel.
        if (here.Id == LocationId.RentOffice)
        {
            // The same `temp1` the backdrop is chosen from, so the picture and the lines can
            // never disagree about which of the three offices this is.
            if (RentOfficeTemp1 == 1)
            {
                // `payRent price: (global302 curRent:)` (`:74`) â€” `fixedPrice 1`, so it is
                // the rent itself and never the arrears.
                Add(ItemText.PriceLine(StoreLayout.RentOffice[0].Label, P.CurRent), () =>
                {
                    ResetWorkClock();              // CostDItem, `WButton.sc:197-200`
                    if (!_game!.PayRent()) SpeakNotEnoughCash();
                });

                // moreTime is a plain WButton: no price, so no `$N` suffix. It is also the
                // one clickable line in a workplace that does NOT reset the work clock:
                // `moreTime::doit` (`rentOffice.sc:235`) never touches `aTimeClock`.
                Add(StoreLayout.RentOffice[1].Label, AskForMoreTime);

                // The two rentals. `CostDItem::init` prices them from `basePrice` through
                // the goods index, so both labels carry a live figure.
                Add(ItemText.PriceLine(StoreLayout.RentOffice[2].Label, _game!.RentAsking(0)),
                    () => { ResetWorkClock(); RentApartment(0); });
                Add(ItemText.PriceLine(StoreLayout.RentOffice[3].Label, _game.RentAsking(2)),
                    () => { ResetWorkClock(); RentApartment(2); });

                // `(if (global302 rentOwed:) (self add: payGarnishment) …)` â€” `:91-94`.
                if (P.RentOwed > 0)
                    Add(ItemText.PriceLine(StoreLayout.RentOffice[4].Label, P.RentOwed), () =>
                    {
                        // `payGarnishment::doit` is `(super doit:)` then, on `global416`,
                        // `(global302 rentOwed: 0)` and clip 198 (`:459-471`).
                        ResetWorkClock();          // CostDItem, `WButton.sc:197-200`
                        if (_game!.PayGarnishment()) Speak(198); else SpeakNotEnoughCash();
                    });
            }
        }

        if (here.Id == LocationId.Bank)
        {
            // Exactly the five lines bank.sc lists, using its own label strings. The
            // broker is NOT on this screen â€” "See The Broker" opens its own dialog.
            // Savings earn no interest; the bank exists to keep cash from Wild Willy.
            Add(ItemText.PriceLine(StoreLayout.Bank[0].Label, Bank.TransferLimit), () =>
            {
                ResetWorkClock();                  // `bank.sc:185-187`
                var moved = Bank.Deposit(P);

                // `(if temp0 … (piggyBank cel: 0 doit: End))` â€” `bank.sc:191-198`. A press
                // that moves nothing leaves the pig alone.
                if (moved > 0) RunPiggyBank(0, PiggyLastCel);

                P.RecalculateGoals(_game!.Economy);
            });

            Add(ItemText.PriceLine(StoreLayout.Bank[1].Label, Bank.TransferLimit), () =>
            {
                ResetWorkClock();                  // `bank.sc:236-238`
                var moved = Bank.Withdraw(P);

                // `(piggyBank cel: 4 doit: Beg)` â€” `bank.sc:259`, the same cels backwards.
                if (moved > 0) RunPiggyBank(PiggyLastCel, 0);

                P.RecalculateGoals(_game!.Economy);
            });

            // The loan payment line only appears once you owe something.
            if (P.LoanBal > 0)
                Add($"Loan Payment ${Math.Min(P.LoanBal, Bank.PaymentCost)}", () =>
                {
                    // `loanPayment` is a CostDItem, so `CostDItem::doit`'s own reset applies
                    // (`WButton.sc:197-200`). It drives no piggy bank.
                    ResetWorkClock();
                    Bank.MakePayment(P);
                    P.RecalculateGoals(_game!.Economy);
                });

            Add("Apply For Loan", () =>
            {
                // `applyForLoan` is a plain WButton, so it repeats the work clock's reset
                // inline (`bank.sc:338-340`) rather than inheriting CostDItem's.
                ResetWorkClock();

                // `bank.sc:341-342`. One of the nine real clock gates: at 60 hours the
                // teller says clip 323 and nothing else happens. Otherwise the 2 hours are
                // spent on EVERY press, before the eligibility test, whatever the answer.
                if (_game!.Clock.TurnOver) { Speak(323); return; }
                _game.Clock.Spend(GameClock.LoanApplicationCost);

                // `bank.sc:360-373`. The teller works out the offer, SPEAKS clip 317 when
                // there is already a balance to extend or 318 when there is not
                // (`(= temp3 17)` / `18`, then `(proc0_18 (+ temp3 300) …)`), and then
                // puts the buttons on THAT sentence: the floppy's `bank.sc:474` is one
                // `Print` of the already-formatted `@global100` with `#width 110` and
                // Yes/No, so it is one balloon, not the line plus CD text 204[1].
                // Nothing is lent until the button is pressed.
                //
                // When the bank will not lend at all there is no question: `TakeLoan`
                // applies the ineligibility penalty (`bank.sc:389-395`) and that is that.
                var offer = Bank.LoanOffer(P);
                if (offer <= 0)
                {
                    Bank.TakeLoan(P);
                    P.RecalculateGoals(_game!.Economy);
                    return;
                }

                SpeakQuestion(P.LoanBal > 0 ? 317 : 318, 110, answer =>
                {
                    Effect(Audio.LocationMusic.ButtonClick);   // `bank.sc:375` and `:383`
                    if (answer == 0) return;                   // declining costs nothing

                    Bank.TakeLoan(P);
                    Speak(319, offer);                         // "Here is your %d, payable â€¦"
                    P.RecalculateGoals(_game!.Economy);
                },
                [offer],
                new BalloonButton("Yes", 1),
                new BalloonButton("No", 0));
            });

            Add("See The Broker", () =>
            {
                ResetWorkClock();                  // `bank.sc:414-416`, inline as above

                // `bank.sc:417-421`. Gated on the clock (clip 324), and the 2 hours are
                // charged ONCE per bank visit â€” the `local0` latch â€” not once per press.
                if (_game!.Clock.TurnOver) { Speak(324); return; }
                if (!_game.BrokerChargedThisVisit)
                {
                    _game.BrokerChargedThisVisit = true;
                    _game.Clock.Spend(GameClock.BrokerVisitCost);
                }

                _screen = Screen.Broker;
                BuildScreen();
            });
        }
    }

    /// <summary>
    /// `moreTime::doit` (`rentOffice.sc:235-311`). Every branch SPEAKS and most duck the
    /// bed under a sting: `(gASong pause: 1)` `(gASoundEffect play: 45 gASong)` for the yes
    /// and 44 for every no. The clip ids are the CD's, and they are the floppy's printed
    /// text 201 indices plus 160 throughout this script â€” `(proc0_18 (+ temp2 160) â€¦)` at
    /// `:216` against `(Print 201 (Random 18 22))` in the floppy's `:255` is the statement
    /// of that offset.
    /// </summary>
    private void AskForMoreTime()
    {
        if (_game is null) return;

        // Captured BEFORE the call: the repeat branches bump `triedExt` as they run, and the
        // clip is chosen from its value on entry (`:271-304`).
        var tried = P.TriedExt;

        switch (_game.AskForMoreTime())
        {
            case Game.MoreTimeOutcome.NotNeeded:
                Speak(191);                                   // "You don't need an extension."
                break;

            case Game.MoreTimeOutcome.Granted:
                DuckedSting(Audio.SoundEffects.GoodNews);      // `(gASoundEffect play: 45 â€¦)`
                Speak(184);
                break;

            case Game.MoreTimeOutcome.Refused:
                DuckedSting(Audio.SoundEffects.BadNews);       // `(gASoundEffect play: 44 â€¦)`
                Speak(185);
                break;

            case Game.MoreTimeOutcome.AskedAgainAfterYes:
                Speak(186);                                    // no sting on this one
                break;

            case Game.MoreTimeOutcome.AskedAgainAfterNo:
                // `switch triedExt` cases 2..5 speak 187, 188, 189, 190. There is no case 6,
                // so a sixth press is silent â€” the original's own end of the joke.
                if (tried is >= 2 and <= 5)
                {
                    DuckedSting(Audio.SoundEffects.BadNews);
                    Speak(185 + tried);
                }
                break;
        }
    }

    /// <summary>
    /// `rentLowCost::doit` / `rentSecurity::doit` (`rentOffice.sc:326-378` and `:393-445`).
    ///
    /// The question is the one the gate at `:400-405` guards: it is asked only when the move
    /// is affordable AND there are prepaid weeks on the current flat to lose, and those weeks
    /// are not refunded. `#width 150`, `{ YES }` = 1 and `{ NO }` = 0, spaces and capitals as
    /// the script spells them.
    /// </summary>
    /// <param name="scriptLivesAt">The SOURCE's encoding: 0 Low-Cost, 2 Le Security.</param>
    private void RentApartment(int scriptLivesAt)
    {
        if (_game is null) return;

        // `(gASoundEffect play: 23)` fires on every branch of both methods.
        Effect(Audio.LocationMusic.ButtonClick);

        switch (_game.RentApartment(scriptLivesAt))
        {
            case Game.RentOutcome.AlreadyThere:
                // 192 "You already live at the low-cost housing." / 196 the security one.
                Speak(scriptLivesAt == 0 ? 192 : 196);
                return;

            case Game.RentOutcome.CannotAfford:
                SpeakNotEnoughCash();                          // `global424 doit:`, clip 183
                return;

            case Game.RentOutcome.Moved:
                // `(if global427 (proc0_18 (+ temp2 160) â€¦))` with temp2 `(Random 13 17)`
                // for the low-cost move and `(Random 8 12)` for the security one â€” the
                // clerk's chatter, gated on the non-essential-messages flag.
                Speak(_stockRng.Next(scriptLivesAt == 0 ? 173 : 168,
                                     scriptLivesAt == 0 ? 177 : 172));
                return;

            case Game.RentOutcome.NeedsConfirmation:
                var weeks = _game.PrepaidWeeksAt(scriptLivesAt == 0 ? 2 : 0);

                // Clip 193 for the low-cost move, 197 for the security one â€” the sentence
                // that names the weeks about to be forfeited. ONE balloon, buttons on it:
                // the floppy's `rentOffice.sc:524` and `:647` are single `Print`s of text
                // 201[33] / 201[37] with `#width 150` and both buttons.
                SpeakQuestion(scriptLivesAt == 0 ? 193 : 197, 150, answer =>
                {
                    Effect(Audio.LocationMusic.ButtonClick);
                    if (answer == 0) { Speak(195); return; }   // "Smart move, buddy."

                    Speak(194);                                // "OK. It was your decision."
                    _game.RentApartment(scriptLivesAt, confirmed: true);
                },
                [weeks],
                new BalloonButton(" YES ", 1),
                new BalloonButton(" NO ", 0));
                return;
        }
    }

    private void AddStore(LocationId here, LocationId store, StockItem[] stock)
    {
        if (here != store) return;

        // Z-Mart stocks only 6 of its 18 lines, rerolled at the start of each player's
        // turn (`discount.sc:170` deletes at random until the dialog is down to size 10,
        // which is four fixed elements plus six items).
        if (store == LocationId.ZMart) stock = _zmartStock;

        // Everywhere else the lines are handed the fixed slots in StoreLayout positionally,
        // so the stock has to be in the order the script DECLARES its items. Catalogue
        // stores Monolith's Astro Chicken first; fastFood.sc declares it third.
        if (StoreLayout.ScriptOrder(store) is { } order)
            stock = [.. stock.OrderBy(s =>
            {
                var i = System.Array.IndexOf(order, s.Name);
                return i < 0 ? int.MaxValue : i;
            })];

        foreach (var s in stock)
        {
            var item = s;
            var price = item.PriceAt(_game!.GoodsIndex);

            // Verbatim source label plus the price, in the format THAT SHOP declares. Three
            // of the five override `CostDItem`'s: Black's Market per line (res 203), Socket
            // City and Z-Mart per shop (res 208 and 211, the same two strings). Only QT
            // Clothing and Monolith Burgers inherit the default (res 104).
            var text = ItemText.For(store, item.Name);
            var label = store switch
            {
                LocationId.BlacksMarket => ItemText.BlacksMarketLine(item.Name, text, price),
                LocationId.SocketCity or LocationId.ZMart => ItemText.ApplianceLine(text, price),
                _ => ItemText.PriceLine(text, price),
            };

            Add(label, () =>
            {
                // `CostDItem::doit` opens by stopping the work clock and putting it back on
                // cel 0 (`WButton.sc:197-200`) â€” before the affordability test, so it
                // happens whether or not the purchase goes through.
                ResetWorkClock();

                if (!_game!.Buy(item)) { SpeakNotEnoughCash(); return; }

                // `CostDItem::doit` shows the bought item's own picture before the
                // shopkeeper says anything (`WButton.sc:236-237`).
                ShowBoughtItem(StoreLayout.ItemCel(store, item.Name));

                // The newspaper is not a purchase you keep: `newspaper::doit`
                // (`market.sc:337-366`) charges its extra hour, opens script 215 and
                // returns. `boughtItem` is explicitly skipped for it, so nothing is said.
                if (store == LocationId.BlacksMarket && item.Name == "Newspaper")
                {
                    _game.Clock.Spend(Catalogue.NewspaperVisitHours);
                    ReadNewspaper();
                    return;
                }

                SpeakBoughtItem();
            });
        }
    }

    /// <summary>
    /// `boughtItem::doit` â€” a random compliment from this shop's band, never the same one
    /// twice running (`while (== localN (= temp0 (Random lo hi))) 1`).
    /// </summary>
    private void SpeakBoughtItem()
    {
        if (Audio.ShopSpeech.For(P.Location) is not { BoughtFirst: > 0 } lines) return;

        int clip;
        do { clip = _stockRng.Next(lines.BoughtFirst, lines.BoughtLast); }
        while (clip == _lastShopLine && lines.BoughtLast > lines.BoughtFirst);

        _lastShopLine = clip;
        Speak(clip);
    }

    /// <summary>`notEnoughCash::doit` â€” one fixed clip per location, spoken, never printed.</summary>
    private void SpeakNotEnoughCash()
    {
        if (Audio.ShopSpeech.For(P.Location) is { } lines) Speak(lines.NotEnoughCash);
    }

    private int _lastShopLine;

    /// <summary>
    /// Opens the paper bought at Black's Market. `newspaper.sc:120-130`: if `global415` is
    /// not a usable story id it picks `(Random 25 62)` â€” one of the filler headlines â€” and
    /// the paper returns to the shop you bought it in rather than to the board.
    /// </summary>
    private void ReadNewspaper()
    {
        var published = _game!.Economy.Headline;
        _headlineToShow = published is > 0 and <= 62 ? published : _stockRng.Next(25, 62);
        _newspaperReturn = Screen.LocationPanel;
        StartNewspaper();
    }

    /// <summary>Where the Done button goes after a paper bought over the counter.</summary>
    private Screen? _newspaperReturn;

    /// <summary>
    /// Actions stay CLICKABLE when the week's hours are gone. The original gates each
    /// action individually, and most are not gated at all.
    ///
    /// `CostDItem::doit` (`WButton.sc:197-235`) â€” the buy action, used by every shop â€” has
    /// no clock test whatsoever: it checks that you can afford the item, hands the goods
    /// over and takes the money. Running out of hours at Monolith Burgers does not stop
    /// you buying a meal, which is exactly the point, because a player whose week has
    /// ended still has to eat before the next one.
    ///
    /// The clock checks that do exist are on specific actions and each one has its own
    /// answer rather than a dead button:
    ///   work           `fastFood.sc:328` and its equivalents â€” `(and (&lt; global323 60)
    ///                  (&gt; global566 0))`; the shift speaks clip 960 + placeNum instead
    ///   job applying   `employment.sc:167` â€” sets global433 to 5, which speaks clip 425,
    ///                  "we're closing, come back next week"
    ///   studying       `university.sc:193`        relaxing  `lowcost.sc:183`
    ///   bank, broker   `bank.sc:341/417`, `broker.sc:770`
    ///
    /// Those gates live in the core (`Game.TravelTo`, `Game.Relax`, `University.Study`,
    /// `Employment.Work` all test `Clock.TurnOver`) and the handlers here speak the right
    /// refusal. Disabling every button on top of that was my own rule, and it locked the
    /// player out of the things the game deliberately leaves open.
    /// </summary>
    private void Add(string label, Action run, string? value = null) =>
        Actions.Add(new ActionVm(label, () => { run(); BuildScreen(); }, enabled: true, value));

    /// <summary>The same, for a line the formatter already split into label and price.</summary>
    private void Add(ItemText.Line line, Action run) => Add(line.Full, run, line.Value);

    // ------------------------------------------------------------------
    // Display strings
    // ------------------------------------------------------------------

    /// <summary>
    /// The player's location, named as text 700 names it (`inventories.sc:36`).
    /// </summary>
    public string WhereText => _game is null ? "" : Board.Get(P.Location).Name;

    /// <summary>`Week #%2d` â€” text 1[2] / 700[85] / 994[0], the same string the board draws.</summary>
    public string WeekText => _game is null ? "" : $"Week #{_game.Calendar.Week,2}";

    // NO STATUS LINES. `{JobTitle()} - ${wage}/hr`, `Dep n  Exp n  Rlx n  Deg n/11`,
    // `{n} hrs left` and `${cash}` were all invented. The game has a stats screen with its
    // own format table (text 231: `Works at %-16s`, `As a %-20s`, `Hourly wage: $%-11d`,
    // `Cash: $%-18s`, â€¦) which this port has not built yet, and on the board itself the
    // hours are a CLOCK SPRITE (view 270) and the money a CALCULATOR (view 0 loop 4) â€”
    // both of which BuildBoard already draws. The only one of these the port had right was
    // `Unemployed`, which is text 231[5].

    public double WealthPct => Pct(_game is null ? 0 : P.MonStat, _game is null ? 50 : P.MonGoal);
    public double HappinessPct => Pct(_game is null ? 0 : P.HapStat, _game is null ? 50 : P.HapGoal);
    public double EducationPct => Pct(_game is null ? 0 : P.EduStat, _game is null ? 50 : P.EduGoal);
    public double CareerPct => Pct(_game is null ? 0 : P.CarStat, _game is null ? 50 : P.CarGoal);

    private static double Pct(int stat, int goal) =>
        goal <= 0 ? 100 : Math.Clamp(100.0 * stat / goal, 0, 100);

    // THE TURN-START EVENTS ARE NOT DESCRIBED IN WORDS. There was a `Describe` method here
    // turning each of the twenty `TurnStartEvent` cases into a sentence â€” "Wild Willy took
    // 2 item(s).", "You were ill. The doctor cost $120.", "Something happened." â€” and not
    // one of those sentences exists in the game. `startTrn.sc` has a single `Print` in the
    // whole chain (text 111[0], "What should be done with the remaining players?"); the
    // robbery, the crash and the boom are announced by the NEWSPAPER, which BeginTurn
    // already prints, and every other event is spoken by the character or simply shown by
    // the numbers changing.

    // ------------------------------------------------------------------

    /// <summary>
    /// The setup dialogs open over the TOWN BOARD, not the title screen. They use the
    /// invisible window, and the board's cream centre panel is what the buttons sit on â€”
    /// there is no separate dialog backdrop at all.
    /// </summary>
    private void TitleBackdrop()
    {
        var board = SciArt.Pic(11);
        if (board is not null) Sprites.Add(new SpriteVm(0, 0, board));
    }

    private SpriteVm Dialog() => Icon(501, 1, 0, 0, 0);

    private SpriteVm Icon(int view, int loop, int cel, int nsLeft, int nsTop,
                          Action? onClick = null, string? tip = null) =>
        new(view, loop, cel, DialogX + nsLeft, DialogY + nsTop, onClick, tip);

    // ------------------------------------------------------------------
    // Keyboard
    // ------------------------------------------------------------------

    /// <summary>
    /// Raised by Ctrl-Q. The window closes itself; the view model does not know how.
    /// </summary>
    public event Action? QuitRequested;

    /// <summary>
    /// The game's own keyboard bindings. Two sources, and they agree:
    ///
    /// * `MenuBar::init` (`Menu.sc:121-138`), which is where the accelerators are declared:
    ///   <c>`^Q</c> Quit, <c>`^S</c> Change Animation Speed, <c>`^T</c> Graphics Detail
    ///   Level, <c>`^V</c> Change Volume, <c>`^Y</c> Set Save Directory, <c>`^Z</c> Delete
    ///   Current Player, and the function keys <c>`#1</c> Help, <c>`#4</c> Statistics,
    ///   <c>`#5</c> Save, <c>`#6</c> Goals, <c>`#7</c> Restore, <c>`#8</c> Turn Messages
    ///   Off, <c>`#9</c> Restart, <c>`#0</c> About JONES.
    /// * text 997 index 6 and index 7, which the game prints for itself on F1
    ///   (`Menu.sc:191-197`) and which name the same keys in the same order.
    ///
    /// THERE IS NO F2 OR F3 IN THIS BUILD. 997[6] runs F1, F4, F5, F6, F7, F8, F9, F10 and
    /// nothing else, and `Menu.sc:129` puts the volume control on Ctrl-V.
    ///
    /// Implemented here are the bindings the port has machinery for. The rest are listed in
    /// <see cref="UnboundKeys"/> with the reason.
    /// </summary>
    public bool HandleKey(string key, bool ctrl, bool shift)
    {
        // NETWORK PLAY splits the keyboard in two. The Game menu's keys and the sound
        // switches belong to the machine they are pressed on (HandleSystemKey); everything
        // else is the GAME's and belongs to whichever seat is acting — on a joiner it goes to
        // the host, and on the host it is ignored while a joiner is the one acting. Returning
        // false for an ignored key leaves Esc free to open the host's own menu bar.
        if (_remoteView) return HandleJoinerKey(key, ctrl, shift);
        if (HandleSystemKey(key, ctrl)) return true;
        if (!Net.InputGate.Allows) return false;
        return HandleGameKey(key, ctrl, shift);
    }

    /// <summary>
    /// The Game menu's accelerators and the sound switches — `MenuBar::handleEvent`'s own
    /// share of the keyboard, which the menu bar claims before any dialog sees the event.
    /// </summary>
    private bool HandleSystemKey(string key, bool ctrl)
    {
        // F5 Save, F7 Restore, F9 Restart (`Menu.sc:122-124`), and the keyboard while one
        // of their `Print`s is up — which is modal, so it takes precedence over everything
        // below. See MainViewModel.SaveRestore.cs.
        if (HandleSaveRestoreKey(key, ctrl)) return true;

        if (ctrl)
        {
            switch (key)
            {
                // `Menu.sc:211-217`, menu id 517: `(= gQuit (Print 997 10 â€¦))`. The YES/NO
                // confirmation is a modal `Print` and the port has no modal machinery yet â€”
                // the same gap that leaves the pawn shop's "Take It / Leave It" and the
                // university's "Enroll for $%d?" unasked â€” so this quits directly.
                // THE ACCELERATOR AND THE MENU ITEM ARE ONE BRANCH OF ONE SWITCH in the
                // original, so they are one method here: `QuitFromMenu` in
                // MainViewModel.MenuBar.cs, which asks text 997[10] "Quitting?" and plays
                // effect 23 on both answers. This used to quit on the spot because the port
                // had no modal `Print`; it has had one since Save landed.
                case "Q":
                    QuitFromMenu();
                    BuildScreen();
                    return true;

                // `Menu.sc:319-334`, menu id 773. The original opens a Gauge (`Gauge.sc`)
                // running 0..15 and calls `(DoSound sndMASTER_VOLUME global520)`. Neither
                // the Gauge dialog nor a volume level exists in this port: `IAudioPlayer`
                // offers `Enabled` and nothing else, so Ctrl-V is the two ends of that
                // slider and nothing in between. Deliberately no on-screen feedback â€” the
                // Gauge is art this port does not draw, and inventing a caption for it
                // would be inventing user-visible text.
                // Shared with the Options menu's Change Volume, for the same reason as
                // Ctrl-Q above. The body moved to `ToggleMasterVolume`.
                case "V":
                    ToggleMasterVolume();
                    return true;
            }

            return false;
        }

        // F2 and F3. I reported twice that this build has no F2 binding; that was wrong
        // both times, and wrong the same way the subtitle audit was â€” I only searched the
        // CD scripts. The FLOPPY build's Options menu (`jones-dos-1.000.060/src/Menu.sc:128`)
        // ends:
        //
        //     â€¦ Change Volume `^V:Turn Music Off `#2:Turn Sound Effects Off `#3
        //
        // `` `# `` is the function-key escape and the digit is the F-number, so music is F2
        // and effects are F3. The CD build dropped both menu entries, which is why they are
        // absent from its 997[6] help text â€” but they are unmistakably part of the game.
        //
        // They are SEPARATE from Ctrl-V's master volume: you can silence the bed and keep
        // the button clicks, or the reverse.
        switch (key)
        {
            // Both go through the bound properties rather than the raw flags, so the
            // on-screen switches follow the keys and the keys follow the switches.
            case "F2":
                MusicOn = !MusicOn;
                return true;

            case "F3":
                EffectsOn = !EffectsOn;
                return true;

            // F1 and F10 - Help and About, menu ids 258 and 257 (`Menu.sc:191-197`,
            // `:172-190`). They were on the unwired list below because each is a run of
            // modal `Print`s and the port had none. It has had them since Save landed, and
            // the queue that makes a run of six work is in MainViewModel.SaveRestore.cs.
            // `MenuBar::init` binds them with `` `#1 `` and `` `#0 `` - `#0` is F10, which
            // text 997[6] confirms with "F10- About Jones".
            case "F1":
                ShowHelp();
                BuildScreen();
                return true;

            case "F10":
                ShowAbout();
                BuildScreen();
                return true;
        }

        return false;
    }

    /// <summary>
    /// The game's own keys — the controls' accelerators, F4/F6, and the per-screen keys.
    /// Reached from the local keyboard when this machine may act, and from a network joiner
    /// through <see cref="ExecuteRemoteInput"/>.
    /// </summary>
    private bool HandleGameKey(string key, bool ctrl, bool shift)
    {
        // Ctrl-A .. Ctrl-X are the `key` values 1..24 that the controls declare — see
        // SciKey for the evidence. The menu bar's own six are handled above and above
        // only: `menuBarOK` is 1 on all 35 dialogs (`Interface.sc:1008`), so the menu
        // takes them first and the shop line that happens to share the number never
        // fires. Z-Mart's `eightTrack` (17) and `leisureSuit` (20) are unreachable in
        // the shipped game for exactly this reason. (A joiner's Ctrl-Q and Ctrl-V stop at
        // HandleJoinerKey, so the same holds across the network.)
        if (ctrl) return PressAccelerator(SciKey.Message(key, true));

        switch (key)
        {
            // F6 — Goals. `Menu.sc:390-395` dispatches menu id 1026 to `proc997_2`, and
            // text 997[6] names it "F6 - View Goals Screens".
            case "F6":
                ShowGoalsScreen();
                return true;

            // F4 — Statistics. `Menu.sc:386-389` dispatches menu id 1025 to `proc997_1`;
            // text 997[6] names it "F4 - Current Player's Stats" and 997[7] gives it the
            // right mouse button and Shift-left as well.
            case "F4":
                ShowStatsScreen();
                return true;
        }

        // The stats list scrolls on its own keys while it is open.
        if (_screen == Screen.Stats && HandleStatsKey(key)) return true;

        // `winnerScript::handleEvent` claims any event at all, keys included.
        if (_screen == Screen.Winner) { DismissWinnerSequence(); return true; }

        // `introRoom::handleEvent` does the same for the credits.
        if (_screen == Screen.Intro) { EndIntro(); return true; }

        // A balloon with BUTTONS answers these keys instead of closing on them.
        // `Dialog::handleEvent` (`Interface.sc:1067-1105`) turns Enter into a press of
        // `theItem`, which `Print` has already pre-selected as the FIRST button
        // (`Interface.sc:230-236` sets state bit $0002 on the first item whose state has
        // bit 1, and hands it to `doit:` at `:241`). Esc is the one branch that fires even
        // with a selectable item present (`Interface.sc:1121-1124`): it returns -1, which
        // `Print` maps to 0 at `:241-243` — the value of the No/Leave It button.
        if (key is "Escape" or "Return" && _question is { } pending && SpokenLine.Length == 0)
        {
            Answer(key == "Return" ? pending.Buttons[0].Value : 0);
            return true;
        }

        // A text-only `Print` is dismissed by Enter, Esc, any mouse button or the joystick
        // button â€” `Dialog::handleEvent`, `Interface.sc:1106-1123`, which claims the event
        // and returns -1. The balloon is this port's `Print`.
        if (key is "Escape" or "Return" && IsBalloonShowing)
        {
            CloseBalloon();
            return true;
        }

        // `key 120` â€” `x` â€” is the exitButton in EVERY dialog in the game: `fastFood.sc:300`
        // and its twelve siblings, `diploma.sc:115`, `newspaper.sc:187`, `weekend.sc:268`,
        // `broker.sc:499`, `pawnShop.sc:618`. Same button the mouse already clicks.
        if (key == "X")
        {
            switch (_screen)
            {
                case Screen.LocationPanel: LeaveBuilding(); return true;
                case Screen.Diploma:       CloseDiploma(); return true;
                case Screen.Newspaper:
                case Screen.Weekend:       ShowNewspaperOrBoard(); return true;
                case Screen.Broker:        _screen = Screen.LocationPanel; BuildScreen(); return true;
            }

            return false;
        }

        // `key 119` â€” `w` â€” is the workButton, declared on every workplace's own instance
        // (`fastFood.sc:311`, `appliance.sc:506`, `bank.sc:458`, and so on). It is only
        // added to the dialog when the player works there, so the key only does anything
        // there either.
        // THE GOALS SCREEN HAS NO `x` KEY. Every other dialog's exit button declares
        // `key 120` beside its `state 419` (`newspaper.sc:187`, `weekend.sc:268`), and
        // `viewGoals.sc:206-213` declares the state and NOT the key. What closes it from the
        // keyboard is Esc: the one branch of `Dialog::handleEvent` that fires even with a
        // selectable item present (`Interface.sc:1121-1124`), returning -1 out of the
        // `(self doit: 0 0)` at `viewGoals.sc:164`.
        if (key == "Escape" && _screen == Screen.WhosWinning)
        {
            CloseWhosWinning();
            return true;
        }

        if (key == "W" && _screen == Screen.LocationPanel && _game is not null)
        {
            if (Board.Get(P.Location).Workplace is { } wp && P.WorksAt == (int)wp + 1)
            {
                Effect(Audio.LocationMusic.ButtonClick);
                WorkShift();
                return true;
            }
        }

        // Everything else a control on this screen declares: the bare-letter keys
        // (broker Buy `b` 98 and Sell `j` 106) as well as the Ctrl ones handled above.
        return PressAccelerator(SciKey.Message(key, false));
    }

    /// <summary>
    /// Fires whatever control on the current screen declares this <c>key</c>, if any —
    /// the port's <c>Item::handleEvent</c> (`Interface.sc:407-420`). Returns whether the
    /// event was claimed, which is what that method's `(event claimed: 1)` amounts to.
    /// </summary>
    private bool PressAccelerator(int message)
    {
        if (message == 0 || !_keyBindings.TryGetValue(message, out var press)) return false;

        // Deliberately the SAME action the mouse runs and nothing more. `WButton::track`
        // (`WButton.sc:80-114`) routes the keyboard branch and the mouse branch into one
        // `doit`, so a key press and a click must not differ — including in whether they
        // play effect 23, which `ActionVm` already does for the lines that are ActionVms.
        press();
        return true;
    }

    // THE BINDINGS THAT ARE NOT WIRED UP, and why. From `Menu.sc:121-138` and text
    // 997[6..7], which are the game's own two statements of its keyboard map.
    //
    //   Ctrl-S     animation speed         Gauge.sc is unported; it also rewrites global475
    //                                      (`Menu.sc:317`), the travel cost, which Board.cs
    //                                      does not model
    //   Ctrl-T     graphics detail         global534 has no equivalent here â€” the port
    //                                      draws everything at full detail unconditionally
    //   Ctrl-Y     save directory          `proc990_2` is a `Print` with a 29-character
    //                                      `#edit` field validated by `ValidPath`
    //                                      (`Save.sc:73-109`), so a 1990 player could put
    //                                      saves on a second floppy. This port writes one
    //                                      slot to the platform's application-data
    //                                      directory, has no text-entry widget, and has
    //                                      nowhere sensible for a path to point. Text
    //                                      990[4] and 990[5] are unused.
    //   Ctrl-Z     delete current player   needs the YES/NO Print and `finStat` handling
    //   F8         non-essential messages  global427, which gates `gBoughtItem` and the
    //                                      shopkeeper chatter; no setting exists here
    //
    // Each of those five is nevertheless ON THE MENU BAR, greyed - see `Unimplemented` in
    // MainViewModel.MenuBar.cs for why a dead item is shown rather than dropped.
    //
    // F5, F7 and F9 WERE on this list and are not any more - see
    // MainViewModel.SaveRestore.cs. NOR ARE F1, F10 and Esc: Help and About are runs of
    // modal `Print`s, which the port now has, and Esc opens the menu bar, which it now has
    // too - the view hands it over in MainView.axaml.cs once nothing else has claimed it.

    private void RefreshAll()
    {
        foreach (var n in new[]
        {
            nameof(ShowBoard), nameof(ShowPanel), nameof(WhereText), nameof(WeekText),
            nameof(IsBalloonShowing),
            nameof(WealthPct), nameof(HappinessPct), nameof(EducationPct), nameof(CareerPct),
        })
        {
            OnPropertyChanged(n);
        }
    }
}

/// <summary>
/// One thing the player can do where they are standing.
///
/// There is NO separate "detail" column. This used to carry a `Detail` string that
/// `MenuText` joined onto the label with three spaces, which drew every University, Rent
/// Office and Pawn Shop line in a layout the game never had: `CostDItem::doFormat`
/// (`WButton.sc:196-206`) appends `" $%d"` or `"$%d"` directly to the label, and the
/// label's own dot-and-pipe padding is measured for exactly that. The whole line is now
/// formatted by the caller and handed over as one string.
/// </summary>
public sealed class ActionVm(string label, Action run, bool enabled = true, string? value = null)
{
    public string Label { get; } = label;
    public bool Enabled { get; } = enabled;
    public string MenuText => Label;

    /// <summary>
    /// The suffix of <see cref="Label"/> that is the PRICE, where the line has one, so that
    /// <see cref="UiFont"/> can right-align it onto the column stop font 10 gives it instead
    /// of trusting padding that only means something in font 10's metrics. Null on a line with
    /// no price, and unused by the bitmap face. See <see cref="ItemText.Line"/>.
    /// </summary>
    public string? Value { get; } = value;

    /// <summary>
    /// Every action in the game is a WButton, and `WButton::doit` (WButton.sc:152-153)
    /// plays sound 23 before it does anything else. That one call is why the interface
    /// clicks: it is on the button class, not on the individual handlers.
    /// </summary>
    public ICommand Command { get; } = Net.InputGate.Command(
        () => { MainViewModel.Effect(Audio.LocationMusic.ButtonClick); run(); },
        () => enabled);
}

