using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Jones.App.ViewModels;

namespace Jones.App.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>
    /// The game's keyboard map (`Menu.sc:121-138`, text 997[6..7]) — see
    /// <see cref="MainViewModel.HandleKey"/> for which keys and which are skipped.
    ///
    /// Attached to the TOP LEVEL rather than to this control, and on the TUNNEL pass. A
    /// UserControl only sees KeyDown when it has focus, and the screen is a canvas full of
    /// Buttons that take focus as soon as one is clicked; a bubbling handler would then
    /// have to wait for whichever button had focus to decline the key first.
    /// </summary>
    private bool _keysHooked;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_keysHooked) return;
        if (TopLevel.GetTopLevel(this) is not { } top) return;

        _keysHooked = true;
        top.AddHandler(KeyDownEvent, TopLevel_KeyDown, RoutingStrategies.Tunnel);

        GameMenu.PropertyChanged += GameMenu_PropertyChanged;

        // The Goals screen's two mouse bindings. `MenuBar::handleEvent` (`Menu.sc:145-157`)
        // claims a Ctrl-modified mouse event OUTRIGHT, before anything under the pointer can
        // see it, and acts on the RELEASE half. Both halves are hooked here on the TUNNEL
        // pass for the same reason: the screen is a canvas full of Buttons, and a bubbling
        // handler would let the button under the pointer fire first.
        top.AddHandler(PointerPressedEvent, TopLevel_PointerPressed, RoutingStrategies.Tunnel);
        top.AddHandler(PointerReleasedEvent, TopLevel_PointerReleased, RoutingStrategies.Tunnel);

        // THE SAFE AREA. The Android head is fullscreen and lays out into the display cutout
        // (values-v31/styles.xml), and from Android 15 every app is edge to edge whether it
        // asks or not. That was fine for the playfield, which the Viewbox fits inside
        // whatever it is given - but the menu bar is docked at the very top, and there it sat
        // under the status bar and the camera, where a finger cannot reach it. On a phone
        // with no Back button that left no way to Quit, Save or open Help at all.
        //
        // So the whole view is padded in by the insets the platform reports, on every side
        // (in landscape the cutout is on the LEFT, over the first menu). The padding is inside
        // the UserControl, so it stays black like the rest of the border. Done here rather
        // than through TopLevel.AutoSafeAreaPadding so there is exactly one thing setting it.
        // The desktop reports no insets, so this is a no-op there.
        if (top.InsetsManager is { } insets)
        {
            TopLevel.SetAutoSafeAreaPadding(this, false);
            Padding = insets.SafeAreaPadding;
            insets.SafeAreaChanged += (_, a) => Padding = a.SafeAreaPadding;
        }

        Hook(Vm);

        // The tap scale, measured rather than assumed — see MeasureTouchScale.
        GameCanvas.LayoutUpdated += (_, _) => MeasureTouchScale();

        if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("JONES_DEBUG_HITTEST")))
        {
            // Waits for the screen to exist and for the shopkeeper's greeting to finish: a
            // balloon is MODAL and puts a click-to-dismiss layer over the whole canvas, so
            // probing under one would correctly report that nothing else is reachable.
            var t = new Avalonia.Threading.DispatcherTimer
            {
                Interval = System.TimeSpan.FromMilliseconds(1000),
            };
            var tries = 0;
            t.Tick += (_, _) =>
            {
                tries++;
                if (Vm is not { } v || v.Sprites.Count == 0)
                {
                    if (tries > 40) { t.Stop(); System.Console.WriteLine("HITTEST no screen"); }
                    return;
                }

                // The greeting is spoken on entry and blocks everything underneath, which is
                // faithful and useless to probe under. Dismissed through the view model's own
                // command rather than with a synthetic click.
                if (v.IsBalloonShowing)
                {
                    v.DismissBalloonCommand.Execute(null);
                    if (tries > 40) { t.Stop(); System.Console.WriteLine("HITTEST balloon never cleared"); }
                    return;
                }

                // A first pass that finds NOTHING means the containers have not been realised
                // yet, not that the screen is broken, so it keeps trying.
                if (ProbeHitTesting() > 0 || tries > 40) t.Stop();
            };
            t.Start();
        }
    }

    /// <summary>
    /// THE HIT-TEST PROBE — <c>JONES_DEBUG_HITTEST=1</c>, inert without it.
    ///
    /// <para>
    /// Asks the visual tree, WITH NO MOUSE INVOLVED, whether the centre of every clickable
    /// thing on screen actually hit-tests to that thing: every shop line, every icon button,
    /// every balloon button and all thirteen board hotspots. It exists because
    /// <see cref="PanelScale"/> moves and resizes a building's whole interior, and a control
    /// that draws in the new place but answers in the old one is worse than no change at all
    /// — and because the machine this is developed on cannot be driven with synthetic input.
    /// </para>
    ///
    /// <para>
    /// A pass that finds NOTHING means the item containers have not been realised yet, not
    /// that the screen is broken, so the caller tries again; the balloon is dismissed first,
    /// because it is modal and correctly covers everything underneath it.
    /// </para>
    /// </summary>
    /// <summary>
    /// HOW BIG A GAME PIXEL ACTUALLY IS ON THIS SCREEN, which is what
    /// <see cref="TouchTargets"/> grows the tap rectangles from.
    ///
    /// <para>
    /// MEASURED, NOT ASSUMED FROM THE PLATFORM. The canvas is <c>RenderScale.Factor</c> times
    /// the original's 320x200 and the Viewbox then fits it to whatever the window or the phone
    /// gives it, so the only honest source is the transform the layout has actually settled
    /// on. <c>M11</c> of the canvas-to-top-level matrix is canvas units per Avalonia unit;
    /// multiplying by the art factor gives Avalonia units per GAME pixel, and an Avalonia unit
    /// is 1/96 inch on every head — which is what makes a 48 dp (1/160 inch) comparison
    /// meaningful without asking what sort of device this is.
    /// </para>
    ///
    /// <para>
    /// Re-measured on every layout pass, because the window can be resized and a phone can be
    /// rotated. It only ever sets a number; the growth is recomputed on the next screen build.
    /// </para>
    /// </summary>
    private void MeasureTouchScale()
    {
        // `JONES_TOUCH_UNITS` pretends the screen is a different size, so the grown
        // rectangles can be exercised and probed on a desktop. 2.025 is a 1080-wide phone in
        // portrait with no upscaled art (3.375 game pixels per device pixel, 0.6 Avalonia
        // units per dp); 3.24 is the same phone in landscape. Without it nothing here is
        // guessed at — the scale is measured.
        if (double.TryParse(System.Environment.GetEnvironmentVariable("JONES_TOUCH_UNITS"),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var forced)
            && forced > 0)
        {
            TouchTargets.UnitsPerGamePixel = forced;
            return;
        }

        if (TopLevel.GetTopLevel(this) is not { } top) return;
        if (GameCanvas.TransformToVisual(top) is not { } m) return;
        if (m.M11 <= 0) return;

        TouchTargets.UnitsPerGamePixel = m.M11 * RenderScale.Factor;
    }

    private int ProbeHitTesting()
    {
        if (Vm is not { } vm) return 0;

        // What the tap rectangles are actually working against on this screen, in the units
        // the 48 dp rule is stated in. Printed before the probe so a miss can be read against
        // it rather than guessed at.
        MeasureTouchScale();
        var upx = TouchTargets.UnitsPerGamePixel;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 0;
        System.Console.WriteLine(
            $"TOUCH RenderScaling {scaling:0.###}; {upx:0.###} Avalonia units per game pixel; " +
            $"48 dp = {TouchTargets.MinimumGamePixels:0.##} game px; " +
            $"a 32x9 button is {32 * upx / 0.6:0.#} x {9 * upx / 0.6:0.#} dp at panel scale 1, " +
            $"{32 * upx * PanelScale.Factor / 0.6:0.#} x {9 * upx * PanelScale.Factor / 0.6:0.#} dp " +
            $"at {PanelScale.Factor:0.####}");

        var f = RenderScale.Factor;
        int pass = 0, fail = 0;
        var misses = new System.Collections.Generic.List<string>();

        void Probe(object model, double x, double y, double w, double h, string what)
        {
            if (w <= 0 || h <= 0) return;
            var p = new Point((x + w / 2) * f, (y + h / 2) * f);
            var hit = GameCanvas.InputHitTest(p) as Avalonia.Visual;
            while (hit is not null && !ReferenceEquals(hit.DataContext, model))
                hit = Avalonia.VisualTree.VisualExtensions.GetVisualParent(hit);

            if (hit is not null) { pass++; return; }
            fail++;
            var what2 = (GameCanvas.InputHitTest(p) as Control)?.GetType().Name ?? "nothing";
            misses.Add($"HITTEST MISS {what} at game ({x:0.##},{y:0.##}) {w:0.##}x{h:0.##} -> {what2}");
        }

        // THE DRAWN CENTRE, which is the invariant that matters: a control must answer where
        // it is painted. Then the four corners of the GROWN rectangle, which is what proves
        // the expanded area responds and that no neighbour has taken it — on a desktop the
        // two rectangles are the same and this is simply the same check five times.
        var grown = 0;

        foreach (var s in vm.Sprites)
        {
            if (!s.IsClickable) continue;
            Probe(s, s.DrawX, s.DrawY, s.DrawW, s.DrawH, $"sprite v{s.View} l{s.Loop} c{s.Cel}");
            if (s.HitW > s.DrawW || s.HitH > s.DrawH) grown++;
            Corners(s, s.HitLeft, s.HitTop, s.HitW, s.HitH,
                    $"sprite v{s.View} l{s.Loop} c{s.Cel} GROWN");
        }

        foreach (var m in vm.MenuLines)
        {
            if (!m.Enabled) continue;
            Probe(m, m.DrawX, m.DrawY, m.DrawW, m.DrawH, $"line \"{m.Tooltip}\"");
            if (m.HitW > m.DrawW || m.HitH > m.DrawH) grown++;
            Corners(m, m.HitLeft, m.HitTop, m.HitW, m.HitH,
                    $"line \"{m.Tooltip}\" GROWN");
        }

        void Corners(object model, double x, double y, double w, double h, string what)
        {
            // A game pixel in from each corner. Probe() takes a RECTANGLE and tests its
            // centre, so these are 2x2 boxes whose centres land one pixel inside the
            // rectangle's corners — on the boundary itself a hit is undefined, and an
            // earlier version of this probed exactly there and reported false misses.
            foreach (var (cx, cy) in new[]
                     {
                         (x, y), (x + w - 2, y),
                         (x, y + h - 2), (x + w - 2, y + h - 2),
                     })
                Probe(model, cx, cy, 2, 2, what);
        }

        foreach (var b in vm.BalloonButtons)
            Probe(b, b.DrawX, b.DrawY, b.DrawW, b.DrawH, $"balloon button \"{b.Text}\"");

        foreach (var hs in vm.Hotspots)
            Probe(hs, hs.X, hs.Y, hs.W, hs.H, $"hotspot \"{hs.Tooltip}\"");

        if (pass == 0) return 0;   // nothing realised yet; the caller tries again

        // DOES A REBUILD DESTROY THE CONTROLS? This is the link in the "clicking WORK is
        // unreliable while the clock turns" chain that cannot be read off the source: a
        // Button raises Click only if it is still the element holding the pointer capture
        // when the button comes up, so a control replaced between press and release
        // swallows the click. BuildScreen clears every collection, and whether an
        // ObservableCollection Reset recycles containers or recreates them is Avalonia's
        // business, not ours - so it is measured rather than assumed.
        if (System.Linq.Enumerable.FirstOrDefault(vm.Sprites, s => s.IsClickable) is { } probe)
        {
            var at = new Point((probe.DrawX + probe.DrawW / 2) * f,
                               (probe.DrawY + probe.DrawH / 2) * f);
            var before = GameCanvas.InputHitTest(at);
            var builds = ViewModels.MainViewModel.BuildCount;

            vm.DebugRebuild();
            GameCanvas.UpdateLayout();

            var after = GameCanvas.InputHitTest(at);
            System.Console.WriteLine(
                $"REBUILD: BuildScreen {builds} -> {ViewModels.MainViewModel.BuildCount}; " +
                $"the control at the first clickable sprite is " +
                (ReferenceEquals(before, after)
                    ? "THE SAME OBJECT - a rebuild keeps it, so a click cannot be lost to one"
                    : "A DIFFERENT OBJECT - a rebuild destroys it, so a click spanning one is lost"));

            // AND THE SAME QUESTION OF A SINGLE-SPRITE REPLACE, which is what an animation
            // tick does now. This is the measurement the fix stands on: the animating cel's
            // own container may be recreated, but every control around it must survive.
            var beforeSwap = GameCanvas.InputHitTest(at);
            if (vm.DebugReplaceAnimatedSprite())
            {
                GameCanvas.UpdateLayout();
                var afterSwap = GameCanvas.InputHitTest(at);
                System.Console.WriteLine(
                    "ANIMATION TICK: the control at the first clickable sprite is " +
                    (ReferenceEquals(beforeSwap, afterSwap)
                        ? "THE SAME OBJECT - a tick no longer destroys it, so clicks land"
                        : "A DIFFERENT OBJECT - the tick still destroys it, THE FIX DOES NOT WORK"));
            }
            else
            {
                System.Console.WriteLine("ANIMATION TICK: no animated sprite on this screen");
            }
        }

        // THE MARGIN MUST BE DEAD. With the wide-board probe on, points out in the scenery
        // are hit-tested through the OUTER canvas; nothing there may resolve to a game view
        // model, because the world's canvas clips at column 319 and the margin holds no
        // controls. This is the claim the whole separation rests on, so it is measured.
        if (RenderScale.MarginColumns > 0)
        {
            var m = RenderScale.MarginColumns * RenderScale.Factor;
            var outerW = RenderScale.OuterCanvasWidth;
            var live = 0;
            foreach (var p in new[]
                     {
                         new Point(2, RenderScale.CanvasHeight / 2),
                         new Point(m / 2, 4),
                         new Point(m - 2, RenderScale.CanvasHeight - 4),
                         new Point(outerW - 2, RenderScale.CanvasHeight / 2),
                         new Point(outerW - m / 2, 4),
                     })
            {
                var hit = OuterCanvas.InputHitTest(p) as Avalonia.Visual;
                while (hit is not null)
                {
                    if (hit.DataContext is ViewModels.SpriteVm or ViewModels.MenuLineVm
                                        or ViewModels.HotspotVm or ViewModels.BalloonButtonVm)
                    {
                        live++;
                        System.Console.WriteLine($"MARGIN LIVE at {p} -> {hit.GetType().Name}");
                        break;
                    }
                    hit = Avalonia.VisualTree.VisualExtensions.GetVisualParent(hit);
                }
            }

            System.Console.WriteLine(
                $"MARGIN {RenderScale.MarginColumns} columns per side, outer canvas " +
                $"{outerW}x{RenderScale.CanvasHeight}, world at {RenderScale.WorldLeft}; " +
                $"{live} of 5 margin points reach a game control");
        }

        foreach (var m in misses) System.Console.WriteLine(m);
        System.Console.WriteLine($"HITTEST {pass} hit, {fail} missed, {grown} controls grown");
        return pass;
    }

    // ------------------------------------------------------------------
    // The view model can be REPLACED: joining a network game swaps in a joiner's, and
    // leaving swaps in a fresh local one (MainViewModel.Network.cs). So the two events the
    // view listens to are hooked per view model, not once at attach.
    // ------------------------------------------------------------------

    private MainViewModel? _hooked;

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        Hook(Vm);
    }

    private void Hook(MainViewModel? vm)
    {
        if (ReferenceEquals(_hooked, vm)) return;

        if (_hooked is { } old)
        {
            old.QuitRequested -= OnQuitRequested;
            old.Replace -= OnReplace;
        }

        _hooked = vm;

        if (vm is { } next)
        {
            next.QuitRequested += OnQuitRequested;
            next.Replace += OnReplace;
        }
    }

    /// <summary>
    /// How a head with no <see cref="Window"/> to close ends the game. On Android the top
    /// level is the activity's view, not a Window, so closing "the window" did nothing: Quit
    /// asked "Quitting?", took YES, and left the game running. The Android head sets this
    /// to finish its activity. Null on the desktop, which has a Window.
    /// </summary>
    public static System.Action? QuitWithoutWindow { get; set; }

    private void OnQuitRequested()
    {
        if (TopLevel.GetTopLevel(this) is Window window) window.Close();
        else QuitWithoutWindow?.Invoke();
    }

    private void OnReplace(MainViewModel next)
    {
        JoinButton.Flyout?.Hide();
        LeaveButton.Flyout?.Hide();
        DataContext = next;
    }

    private void TopLevel_KeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;

        // Typing a host address into the join box is typing, not playing: every digit and
        // letter is also a game accelerator, and the box must get them instead.
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;

        // While the bar is down the game is not listening. `MenuSelect` runs the
        // interpreter's own event loop for as long as a menu is open, which is what text
        // 997[7] means by "Esc   - Menu Bar/Pauses Game": no game cycle happens, so no key
        // reaches the game either. Esc is the way back out.
        if (GameMenu.IsOpen)
        {
            if (e.Key != Key.Escape) return;

            GameMenu.Close();
            e.Handled = true;
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (vm.HandleKey(e.Key.ToString(), ctrl, shift))
        {
            e.Handled = true;
            return;
        }

        // Text 997[7]: "Esc   - Menu Bar/Pauses Game". OFFERED TO THE GAME FIRST, above,
        // because Esc already means something on several screens - it closes the Who's
        // Winning screen (`viewGoals.sc` has no `x` key, so Esc is what `Dialog::handleEvent`
        // leaves to close it) and it answers a balloon's question with the No button
        // (`Interface.sc:1121-1124`). Only a plain Esc that nothing else wanted opens the
        // bar.
        //
        // The gate is MainViewModel.CanUseMenuBar - `Dialog`'s class default `menuBarOK 0`
        // (`Interface.sc:814`), which a `Print`'s anonymous dialog never overrides, so the
        // bar is unreachable while a message box is up.
        if (e.Key == Key.Escape && !ctrl && !shift && vm.CanUseMenuBar)
        {
            // `Menu.Open()` on its own only makes the bar ACTIVE — it takes focus and waits
            // for an arrow key, and nothing visible happens. Esc in the original drops a
            // menu down, so the leftmost one is dropped here, and the arrow keys then move
            // along the bar exactly as they would have.
            GameMenu.Open();
            FirstMenu.IsSubMenuOpen = true;
            FirstMenu.Focus();
            e.Handled = true;
        }
    }

    private bool _menuWasOpen;

    /// <summary>
    /// The bar going up and coming down, watched through the control's own property changes
    /// rather than through a routed event, so this depends on nothing but
    /// <c>Menu.IsOpen</c>.
    ///
    /// Opening it stops the clocks and closing it starts them again — text 997[7]'s
    /// "Esc   - Menu Bar/Pauses Game". See <see cref="MainViewModel.MenuBarOpened"/>.
    /// </summary>
    private void GameMenu_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (GameMenu.IsOpen == _menuWasOpen) return;

        _menuWasOpen = GameMenu.IsOpen;

        if (_menuWasOpen) Vm?.MenuBarOpened();
        else Vm?.MenuBarClosed();
    }

    /// <summary>
    /// Swallows the press half of the chord, as `(event claimed: 1)` at `Menu.sc:153` does,
    /// so a Ctrl-left on a button does not also press the button. The gate is
    /// <see cref="MainViewModel.CanShowGoalsScreen"/>, this port's `GetMenu 1026 112` —
    /// where the item is disabled, the original never claims the event either.
    /// </summary>
    private void TopLevel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { CanShowGoalsScreen: true }) return;

        var props = e.GetCurrentPoint(null).Properties;
        if (props.IsMiddleButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Control))
            e.Handled = true;

        // The Shift branch of the same `cond` (`Menu.sc:158-169`), which claims the event
        // just as outright and opens the Statistics screen on the release. The right button
        // is the other half of the same binding, per text 997[7].
        if (props.IsRightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            e.Handled = true;
    }

    /// <summary>
    /// Text 997[7]: "Middle Mouse/CTRL-Left - Goals". The two are one binding, and
    /// `Menu.sc:151-156` opens the screen on the mouse-UP.
    /// </summary>
    private void TopLevel_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // `introRoom::handleEvent` (`introRoom.sc:29-50`) claims any click below y 10 and
        // runs the credits out. The top ten rows are the menu bar's, which this port has no
        // equivalent for, so the whole window skips.
        //
        // Each of these four goes through RouteInput first: in network play a joiner sends
        // it to the host, and the host ignores its own mouse while a joiner is acting.
        if (Vm is { IsIntroShowing: true } intro)
        {
            e.Handled = true;
            if (!intro.RouteInput(Jones.Net.InputKind.SkipIntro)) intro.EndIntro();
            return;
        }

        // `winnerScript::handleEvent` (`winnerScript.sc:151-156`) claims ANY event outright
        // once Jones has started walking, which is how the podium is dismissed. It is
        // checked first for the same reason the CTRL branch is: nothing underneath should
        // see the click.
        if (Vm is { IsWinnerSequenceShowing: true } winner)
        {
            e.Handled = true;
            if (!winner.RouteInput(Jones.Net.InputKind.DismissWinner)) winner.DismissWinnerSequence();
            return;
        }

        if (Vm is not { CanShowGoalsScreen: true } vm) return;

        // On release the button is already up, so the chord is read from the button that
        // was JUST released rather than from what is still held down.
        //
        // `Menu.sc:145-169` tests CTRL first and SHIFT second, in one `cond`, so a
        // Ctrl-Shift-left opens the Goals screen and not the Statistics one.
        if (e.InitialPressMouseButton == MouseButton.Middle ||
            e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            if (!vm.RouteInput(Jones.Net.InputKind.Goals)) vm.ShowGoalsScreen();
            return;
        }

        if (e.InitialPressMouseButton != MouseButton.Right &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) return;

        e.Handled = true;
        if (!vm.RouteInput(Jones.Net.InputKind.Stats)) vm.ShowStatsScreen();
    }

    /// <summary>
    /// Drag handling for the goal sliders on the "Set Your Goals" screen.
    ///
    /// The original's StarSlider::track follows the pointer for as long as the button is
    /// held and converts its y straight into a goal value, so the value is read from the
    /// pointer's position in the game's own 320x200 space. Measuring against GameCanvas
    /// gives that space whatever the Viewbox has scaled the window to.
    ///
    /// The move and release handlers live on the CANVAS, not on the slider. Each drag
    /// tick rebuilds the screen's element collections, so a handler attached to the
    /// slider would be destroyed mid-drag and the star would move exactly once.
    ///
    /// The canvas is no longer 320x200 — at 4x it is 1280x800 — so the pointer's position
    /// in it is divided back down by <see cref="RenderScale.Factor"/>. The view model is
    /// handed the y the original's StarSlider would have seen, and its own mapping from y
    /// to goal value is untouched. Rounding DOWN (integer division of the already-
    /// truncated pixel) is what the original did with a 320x200 mouse.
    /// </summary>
    private void GoalTrack_Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: GoalTrackVm track }) return;

        Vm?.BeginGoalDrag(track.Index, ScreenY(e.GetPosition(GameCanvas).Y));
        e.Handled = true;
    }

    private void Canvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(GameCanvas).Properties.IsLeftButtonPressed)
        {
            Vm?.EndGoalDrag();
            return;
        }

        Vm?.DragGoalTo(ScreenY(e.GetPosition(GameCanvas).Y));
    }

    /// <summary>A canvas y back in the original's 320x200 screen space.</summary>
    private static int ScreenY(double canvasY) => (int)(canvasY / RenderScale.Factor);

    private void Canvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        Vm?.EndGoalDrag();
    }

    // THE TWO TOUCH BUTTONS THAT USED TO BE HERE ARE GONE. They stood in for
    // "Middle Mouse/CTRL-Left - Goals" and "Right Mouse/SHIFT-Left - Stats" (text 997[7])
    // on a head with no second mouse button, and the Status menu now reaches both with the
    // game's own labels - see the comment where they used to be drawn, in MainView.axaml.
}
