using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Jones.App.Net;
using Jones.Net;

namespace Jones.App.ViewModels;

/// <summary>
/// NETWORK PLAY — both ends of it. See Jones.Net/Protocol.cs for the model: the host runs
/// the one and only game; a joiner is a terminal that shows the host's screen and sends its
/// input back.
///
/// <para>
/// ON THE HOST this adds three things: <see cref="ActingSeat"/> (whose input counts right
/// now), <see cref="CaptureFrame"/> (the screen as recipes) and
/// <see cref="ExecuteRemoteInput"/> (a joiner's click, carried out as if it were local).
/// </para>
///
/// <para>
/// ON A JOINER the view model is built by <see cref="CreateRemoteView"/> and never has a
/// game of its own. <see cref="BuildScreen"/> does nothing; the collections are filled by
/// <see cref="ApplyFrame"/> instead, and every clickable thing in them sends an
/// <see cref="InputMsg"/> rather than doing anything. The view is unchanged — it binds to the
/// same collections and cannot tell the difference.
/// </para>
///
/// <para>
/// NOTHING HERE IS USER-VISIBLE TEXT (CLAUDE.md §1). The game has no network screens and
/// this port adds none: hosting and joining are launch options, and progress goes to the log.
/// </para>
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>True on a joiner. The view model then mirrors the host instead of playing.</summary>
    private readonly bool _remoteView;

    /// <summary>Where a joiner's input goes. Set by <see cref="NetClient"/>.</summary>
    internal Action<InputMsg>? SendInput { get; set; }

    public bool IsRemoteView => _remoteView;

    public static MainViewModel CreateRemoteView() => new(remoteView: true);

    // ------------------------------------------------------------------
    // Joining from inside the app — the join switch in the chrome row
    // ------------------------------------------------------------------
    //
    // A phone has no command line, so `--join` alone leaves the Android head unable to join.
    // The chrome row outside the 320x200 canvas — where the sound switches already live, and
    // for the same reason — carries one more switch: a link glyph that opens a box for the
    // host's address. It is offered only on the title screen (a game in progress is never
    // thrown away by it) and never on a host.

    /// <summary>The joiner's connection, so leaving can close it. Set by <see cref="NetClient"/>.</summary>
    internal IDisposable? Connection { get; set; }

    private bool _isConnected;

    /// <summary>True while a joiner is seated at the host. Drives the switch's glyph.</summary>
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected == value) return;
            _isConnected = value;
            OnPropertyChanged(nameof(IsConnected));
        }
    }

    /// <summary>The join switch is shown on the title screen of a game that is neither hosting nor joined.</summary>
    public bool CanJoinNetwork =>
        !_remoteView && !NetLaunch.Host && _screen is Screen.MainMenu or Screen.Intro;

    /// <summary>What the join box holds: the last address that worked, until edited.</summary>
    public string JoinAddressText { get; set; } = NetClient.LastAddress;

    /// <summary>
    /// Raised with the view model that should replace this one — the joiner's on Join, a
    /// fresh local game on Leave. The view swaps its DataContext; nothing else needs to know.
    /// </summary>
    public event Action<MainViewModel>? Replace;

    public ICommand JoinCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
    {
        if (!CanJoinNetwork) return;
        if (!Address.TrySplit(JoinAddressText, out var host, out var port)) return;

        Shutdown();
        Replace?.Invoke(NetClient.Start(host, port));
    });

    public ICommand LeaveCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
    {
        if (!_remoteView) return;

        StartupLog.Say("network: left the host");
        Shutdown();
        Replace?.Invoke(new MainViewModel());
    });

    /// <summary>
    /// Silences and stops this view model before another takes the screen: its clocks, its
    /// sound, and — on a joiner — its connection.
    /// </summary>
    private void Shutdown()
    {
        foreach (var timer in AnimationTimers) timer?.Stop();
        _introTimer?.Stop();

        LocalSound.StopSpeech();
        LocalSound.StopMusic();
        LocalSound.StopEffects();

        Connection?.Dispose();
        Connection = null;
        IsConnected = false;
    }

    private MainViewModel(bool remoteView)
    {
        _remoteView = remoteView;
        RefreshAll();
    }

    // ------------------------------------------------------------------
    // Whose turn it is to touch anything
    // ------------------------------------------------------------------

    /// <summary>
    /// The seat — the 0-based player number — whose input the game is waiting for.
    ///
    /// <list type="bullet">
    /// <item>A Game-menu `Print` (Save, Restore, Quit, Help, About) belongs to the host: only
    /// the host has the menu bar, so only the host can have opened one.</item>
    /// <item>Choosing a character and setting goals belong to the player choosing —
    /// `global507`, which is <c>_choosingPlayer</c>.</item>
    /// <item>The title, the player count and the credits belong to the host, who is the one
    /// starting the game.</item>
    /// <item>Everything else belongs to the player whose turn it is. That includes the
    /// between-turn screens (weekend, newspaper, the winner's podium), because the turn has
    /// already passed to that player when they appear.</item>
    /// </list>
    /// </summary>
    public int ActingSeat
    {
        get
        {
            if (_systemPrint is not null) return 0;

            switch (_screen)
            {
                case Screen.MainMenu:
                case Screen.PlayerCount:
                case Screen.Intro:
                    return 0;

                case Screen.CharacterSelect:
                case Screen.GoalSetting:
                case Screen.GoalsHelp:
                    return _choosingPlayer;
            }

            return _game?.CurrentPlayerIndex ?? 0;
        }
    }

    /// <summary>
    /// The input boundary for the handful of inputs that are not element clicks. Returns
    /// TRUE when the caller should do nothing more: on a joiner because the input has been
    /// sent to the host, on the host because this machine may not act right now.
    /// </summary>
    internal bool RouteInput(InputMsg input)
    {
        if (_remoteView)
        {
            SendInput?.Invoke(input);
            return true;
        }

        return !InputGate.Allows;
    }

    internal bool RouteInput(string kind) => RouteInput(new InputMsg { Kind = kind });

    // ------------------------------------------------------------------
    // Host: the screen as a frame
    // ------------------------------------------------------------------

    /// <summary>
    /// The seven collections the view draws, as recipes, each paired with the element it came
    /// from. The pairing is what <see cref="ExecuteRemoteInput"/> resolves a click against, and
    /// using ONE function for both keeps a frame's indices and the host's in step even when
    /// something is left out for want of a recipe.
    /// </summary>
    private List<(SpriteDto Dto, SpriteVm Vm)> SpriteRecipes()
    {
        var list = new List<(SpriteDto, SpriteVm)>(Sprites.Count);
        foreach (var s in Sprites)
            if (ArtRecipes.Of(s.Image) is { } art)
                list.Add((new SpriteDto(s.X, s.Y, art, s.Tooltip, s.IsClickable), s));
        return list;
    }

    private List<(PartDto Dto, BalloonPartVm Vm)> BalloonRecipes()
    {
        var list = new List<(PartDto, BalloonPartVm)>(Balloon.Count);
        foreach (var p in Balloon)
        {
            var fill = (p.Fill as ISolidColorBrush)?.Color.ToString();
            var art = ArtRecipes.Of(p.Image);
            if (fill is null && art is null) continue;
            list.Add((new PartDto(p.X, p.Y, p.W, p.H, fill, art), p));
        }
        return list;
    }

    internal FrameMsg CaptureFrame(int seq)
    {
        var f = new FrameMsg
        {
            Seq = seq,
            Screen = (int)_screen,
            Acting = ActingSeat,
            CanGoals = CanShowGoalsScreen,
            CanStats = CanShowStatsScreen,
        };

        f.Sprites.AddRange(SpriteRecipes().Select(p => p.Dto));
        f.Hotspots.AddRange(Hotspots.Select(h => new RectDto(h.X, h.Y, h.W, h.H, h.Tooltip)));
        f.Texts.AddRange(Texts.Select(t => t.Args));
        f.Lines.AddRange(MenuLines.Select(l => l.Args));
        f.Tracks.AddRange(GoalTracks.Select(t => new TrackDto(t.Index, t.X, t.Y, t.W, t.H)));
        f.Balloon.AddRange(BalloonRecipes().Select(p => p.Dto));
        f.BalloonButtons.AddRange(BalloonButtons.Select(b => new RectDto(b.X, b.Y, b.W, b.H, b.Text)));
        return f;
    }

    // ------------------------------------------------------------------
    // Host: a joiner's input
    // ------------------------------------------------------------------

    /// <summary>
    /// Carries out a joiner's input. The caller (<see cref="NetHost"/>) has already checked
    /// that the joiner owns <see cref="ActingSeat"/>; this runs it through exactly the path a
    /// local click takes, so there is one implementation of every action, not two.
    /// </summary>
    internal void ExecuteRemoteInput(InputMsg m) => InputGate.AsRemote(() =>
    {
        switch (m.Kind)
        {
            case InputKind.Sprite:
                Press(Find(SpriteRecipes().Select(p => p.Vm).ToList(), m, s => Identity.Of(new SpriteDto(s.X, s.Y, ArtRecipes.Of(s.Image)!, s.Tooltip, s.IsClickable)))?.Command);
                break;

            case InputKind.Hotspot:
                Press(Find(Hotspots.ToList(), m, h => Identity.Of(new RectDto(h.X, h.Y, h.W, h.H, h.Tooltip)))?.Command);
                break;

            case InputKind.Line:
                if (Find(MenuLines.ToList(), m, l => Identity.Of(l.Args)) is { Enabled: true } line)
                    Press(line.Command);
                break;

            case InputKind.BalloonButton:
                Press(Find(BalloonButtons.ToList(), m, b => Identity.Of(new RectDto(b.X, b.Y, b.W, b.H, b.Text)))?.Command);
                break;

            case InputKind.DismissBalloon: Press(DismissBalloonCommand); break;
            case InputKind.EndTurn: Press(EndTurnCommand); break;

            case InputKind.Key:
                if (m.KeyName is { } key) HandleGameKey(key, m.Ctrl, m.Shift);
                break;

            case InputKind.GoalBegin: BeginGoalDrag(m.Index, m.Y); break;
            case InputKind.GoalDrag: DragGoalTo(m.Y); break;
            case InputKind.GoalEnd: EndGoalDrag(); break;

            case InputKind.SkipIntro: if (IsIntroShowing) EndIntro(); break;
            case InputKind.DismissWinner: if (IsWinnerSequenceShowing) DismissWinnerSequence(); break;
            case InputKind.Goals: ShowGoalsScreen(); break;
            case InputKind.Stats: ShowStatsScreen(); break;
        }
    });

    private static void Press(ICommand? command)
    {
        if (command?.CanExecute(null) == true) command.Execute(null);
    }

    /// <summary>
    /// The element a click was aimed at: the one at the same index if it is still the same
    /// element, otherwise the first with the same identity. Walking animates by rebuilding the
    /// screen, so a click routinely arrives a rebuild or two after the frame it was made on;
    /// matching on identity keeps it hitting what the joiner saw, and a click on something that
    /// has genuinely gone is dropped — exactly what a local click on a vanished button does.
    /// </summary>
    private static T? Find<T>(IReadOnlyList<T> items, InputMsg m, Func<T, string> identity)
        where T : class
    {
        if (m.Index >= 0 && m.Index < items.Count && identity(items[m.Index]) == m.Id)
            return items[m.Index];

        foreach (var item in items)
            if (identity(item) == m.Id) return item;

        return default;
    }

    // ------------------------------------------------------------------
    // Joiner: the host's frame on this screen
    // ------------------------------------------------------------------

    private FrameMsg? _lastFrame;
    private bool _remoteCanGoals;
    private bool _joinerDragging;

    /// <summary>
    /// Replaces the screen with the host's. Each collection is rebuilt only if it changed, so
    /// the marble walking across the board does not re-render every shop line under it.
    /// </summary>
    internal void ApplyFrame(FrameMsg f)
    {
        var last = _lastFrame;
        _lastFrame = f;

        _screen = (Screen)f.Screen;
        _remoteCanGoals = f.CanGoals;

        if (last is null || !last.Sprites.SequenceEqual(f.Sprites))
        {
            Sprites.Clear();
            for (var i = 0; i < f.Sprites.Count; i++)
            {
                var s = f.Sprites[i];
                if (ArtRecipes.Load(s.Art) is not { } bmp) continue;
                var click = s.Click ? Sender(InputKind.Sprite, f.Seq, i, Identity.Of(s)) : null;
                Sprites.Add(new SpriteVm(s.X, s.Y, bmp, click, s.Tip));
            }
        }

        if (last is null || !last.Hotspots.SequenceEqual(f.Hotspots))
        {
            Hotspots.Clear();
            for (var i = 0; i < f.Hotspots.Count; i++)
            {
                var h = f.Hotspots[i];
                Hotspots.Add(new HotspotVm(h.X, h.Y, h.W, h.H,
                    Sender(InputKind.Hotspot, f.Seq, i, Identity.Of(h)), h.Text ?? ""));
            }
        }

        if (last is null || !last.Texts.SequenceEqual(f.Texts))
        {
            Texts.Clear();
            foreach (var t in f.Texts)
                Texts.Add(new TextVm(t.Text, t.X, t.Y, t.Size, t.Colour, t.Bold, t.FontNumber,
                                     t.Background, t.Shadow, t.Value));
        }

        if (last is null || !last.Lines.SequenceEqual(f.Lines))
        {
            MenuLines.Clear();
            for (var i = 0; i < f.Lines.Count; i++)
            {
                var l = f.Lines[i];
                MenuLines.Add(new MenuLineVm(l.Text, l.X, l.Y,
                    Sender(InputKind.Line, f.Seq, i, Identity.Of(l)),
                    l.TextColour, l.ShadowColour, l.FlashColour, l.Size, l.Tooltip,
                    l.Enabled, l.Selected, l.Key, l.Value));
            }
        }

        if (last is null || !last.Tracks.SequenceEqual(f.Tracks))
        {
            GoalTracks.Clear();
            foreach (var t in f.Tracks)
                GoalTracks.Add(new GoalTrackVm(t.Index, t.X, t.Y, t.W, t.H, _ => { }));
        }

        if (last is null || !last.Balloon.SequenceEqual(f.Balloon))
        {
            Balloon.Clear();
            foreach (var p in f.Balloon)
                Balloon.Add(new BalloonPartVm
                {
                    X = p.X, Y = p.Y, W = p.W, H = p.H,
                    Fill = p.Fill is null ? null : new ImmutableSolidColorBrush(Color.Parse(p.Fill)),
                    Image = ArtRecipes.Load(p.Art),
                });
        }

        if (last is null || !last.BalloonButtons.SequenceEqual(f.BalloonButtons))
        {
            BalloonButtons.Clear();
            for (var i = 0; i < f.BalloonButtons.Count; i++)
            {
                var b = f.BalloonButtons[i];
                BalloonButtons.Add(new BalloonButtonVm(b.X, b.Y, b.W, b.H, b.Text ?? "",
                    Sender(InputKind.BalloonButton, f.Seq, i, Identity.Of(b))));
            }
        }

        RefreshAll();
    }

    private Action Sender(string kind, int seq, int index, string id) =>
        () => SendInput?.Invoke(new InputMsg { Kind = kind, Seq = seq, Index = index, Id = id });

    /// <summary>
    /// A joiner's keyboard. The sound switches are this machine's own and act here; the Game
    /// menu's keys (Save, Restore, Restart, Quit, Help, About) are the host's and do nothing;
    /// everything else is the game's and goes to the host.
    /// </summary>
    private bool HandleJoinerKey(string key, bool ctrl, bool shift)
    {
        if (ctrl && key == "V") { ToggleMasterVolume(); return true; }
        if (!ctrl && key == "F2") { MusicOn = !MusicOn; return true; }
        if (!ctrl && key == "F3") { EffectsOn = !EffectsOn; return true; }

        if (ctrl && key == "Q") return true;
        if (!ctrl && key is "F1" or "F5" or "F7" or "F9" or "F10") return true;

        SendInput?.Invoke(new InputMsg { Kind = InputKind.Key, KeyName = key, Ctrl = ctrl, Shift = shift });
        return true;
    }

    // ------------------------------------------------------------------
    // Sound, both ends
    // ------------------------------------------------------------------

    /// <summary>
    /// Where a call the host's own switches have MUTED still has to go: to the joiners, who
    /// apply their own. Silent when not hosting. See <see cref="BroadcastAudioPlayer"/>.
    /// </summary>
    private static Audio.IAudioPlayer MutedHere =>
        (Sound as BroadcastAudioPlayer)?.NetworkOnly ?? SilentSound;

    private static readonly Audio.IAudioPlayer SilentSound = new Audio.SilentAudioPlayer();

    /// <summary>This machine's speakers only — for the switches, which must not reach anyone else.</summary>
    private static Audio.IAudioPlayer LocalSound => (Sound as BroadcastAudioPlayer)?.Inner ?? Sound;

    /// <summary>The bed the host last started, so a joiner turning music back on hears it.</summary>
    private static (int Id, bool Loop)? _remoteBed;

    /// <summary>A joiner replaying one of the host's sound calls, through its OWN switches.</summary>
    internal static void PlayRemoteAudio(AudioMsg a)
    {
        switch (a.Op)
        {
            case AudioOp.Speech: if (!SpeechOff) Sound.PlaySpeech(a.Id); break;
            case AudioOp.StopSpeech: Sound.StopSpeech(); break;

            case AudioOp.Music:
                _remoteBed = (a.Id, a.Loop);
                if (!MusicOff) Sound.PlayMusic(a.Id, a.Loop);
                break;

            case AudioOp.StopMusic: _remoteBed = null; Sound.StopMusic(); break;
            case AudioOp.CutMusic: _remoteBed = null; Sound.CutMusic(); break;

            // The pause is only ever the duck under a sting (DuckedSting), and with the
            // effects off there is no sting to bring the bed back — so no pause either.
            case AudioOp.PauseMusic: if (!EffectsOff) Sound.PauseMusic(); break;

            case AudioOp.Effect: if (!EffectsOff) Sound.PlayEffect(a.Id, a.Loop, a.Resume); break;
            case AudioOp.EndEffectLoop: Sound.EndEffectLoop(); break;
            case AudioOp.Effect2: if (!EffectsOff) Sound.PlayEffect2(a.Id); break;
            case AudioOp.StopEffects: Sound.StopEffects(); break;
        }
    }
}
