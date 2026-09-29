using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Jones.Core.Model;

namespace Jones.App.ViewModels;

/// <summary>
/// The animated props the port drew nothing for: the building doors (view 751), the bank's
/// piggy bank (view 704), the work clock (view 750) and the lottery win (view 340).
///
/// <para>
/// THE THREE CLOCKS, and every animation here is driven by exactly one of them. Getting this
/// wrong is the `ticksToDo` class of bug all over again, so they are named once:
/// </para>
///
/// <list type="number">
/// <item><b>`ticksToDo`</b> — the wall clock in 60ths, <see cref="Jones.Core.Sci.CelCycler"/>.
///   NOTHING in this file uses it: `door` is a `Prop` whose `ticksToDo` is the class default 0,
///   `TimeClock` and `piggyBank` are `DCIcon`s whose `ticksToDo` is 0, and the lotto actors
///   declare none. `Cycle::nextCel` (`Motion.sc:26-57`) treats 0 as "take the OTHER branch",
///   which is (2).</item>
/// <item><b>`cycleSpeed`</b> — a count of GAME CYCLES. `Cycle::nextCel`'s else arm is
///   `(++ cycleCnt)` then `(if (&lt;= cycleCnt (client cycleSpeed:)) (client cel:) else …
///   changeCel)`, so a cel holds <b>cycleSpeed + 1</b> cycles: `cycleSpeed 1` on the door is a
///   cel every 2 cycles, `5` on the piggy bank every 6, `10` on the work clock every 11. The
///   port's game cycle is <see cref="MainViewModel.CycleMs"/> = 25ms, the same one the marble
///   and the `items` parade already count.</item>
/// <item><b>`Wait n`</b> — a BLOCKING sleep of n ticks of 1/60s, and the only thing driving the
///   door OPENING. It is not a cycler at all: `Place::openDoor` (`room1.sc:107-120`) writes the
///   cel itself, calls `(proc0_1)` — `(Animate (gCast elements:) 0)`, a redraw with no `doit:`
///   — and then `(Wait 6)`, three times. The interpreter is stopped for the duration. That the
///   unit is 1/60s is settled inside this game: `proc0_3` (`Main.sc:955-970`) implements its
///   argument as `(for … (&lt; temp0 (/ param1 6)) … (Wait 6))`, and the port already reads
///   `(proc0_3 240)` as the four seconds a notice holds.</item>
/// </list>
///
/// <para>
/// GAME TIMING IS UNTOUCHED. Nothing here spends an Hour, moves the marble or changes what
/// `Game` decides; every one of these is a picture, and each timer stops itself.
/// </para>
/// </summary>
public sealed partial class MainViewModel
{
    // ==================================================================
    // 1. The door — `Place::openDoor` / `closeDoor` and `door` (view 751)
    // ==================================================================

    /// <summary>
    /// `(instance door of Prop (properties view 751 cycleSpeed 1))` — `room1.sc:66-71`.
    /// ONE instance for the whole board: each `Place` lends it a loop and a position.
    /// </summary>
    private const int DoorView = 751;

    /// <summary>
    /// `doorLoop`, `doorX` and `doorY` off each `Place` instance (`room1.sc:294-574`).
    ///
    /// <para>
    /// `doorLoop` happens to equal `placeNum` for all thirteen, but they are separate
    /// properties in the source and are kept separate here; view 751 carries exactly 13
    /// loops of 4 cels (`assets/raw/view/751.view`, loop count byte 0 = 13), which is one
    /// per building.
    /// </para>
    ///
    /// <para>
    /// `apartmentsP` declares no `doorLoop`, so it keeps the `Place` class default of 0
    /// (`room1.sc:93`) — loop 0 is a real loop, not "none".
    /// </para>
    ///
    /// <para>
    /// FOUR OF THEM CARRY AN EGA FIXUP that this build never takes: `bankP::init`,
    /// `employmentP::init`, `applianceP::init`, `fastFoodP::init` and `pawnShopP::init`
    /// adjust `doorX`/`doorY` by a pixel inside `(if (not global535))`, and `global535` is 1
    /// on any non-16-colour display (`Main.sc:1193`). The VGA values are the declared ones,
    /// which is what is tabulated here.
    /// </para>
    /// </summary>
    private static (int Loop, int X, int Y) DoorFor(LocationId id) => id switch
    {
        LocationId.LowCostHousing     => (0, 144,  30),  // apartmentsP, room1.sc:294
        LocationId.RentOffice         => (1, 105,  29),  // rentOfficeP, :311
        LocationId.SecurityApartments => (2,  29,  44),  // securityP,   :330
        LocationId.BlacksMarket       => (3,  46, 105),  // marketP,     :349
        LocationId.Bank               => (4,  20, 147),  // bankP,       :368
        LocationId.Factory            => (5,  50, 181),  // factoryP,    :395
        LocationId.EmploymentOffice   => (6,  97, 183),  // employmentP, :414
        LocationId.HiTechU            => (7, 220, 179),  // universityP, :440
        LocationId.SocketCity         => (8, 277, 183),  // applianceP,  :459
        LocationId.QtClothing         => (9, 277, 113),  // clothingP,   :485
        LocationId.MonolithBurgers    => (10, 288, 75),  // fastFoodP,   :504
        LocationId.ZMart              => (11, 282, 34),  // discountP,   :531
        _                             => (12, 221, 34),  // pawnShopP,   :550
    };

    /// <summary>`(Wait 6)` between cels — 6 ticks of 1/60s. See the class remarks.</summary>
    private const int DoorOpenMs = 6 * 1000 / 60;

    /// <summary>
    /// `closeDoor:` runs a `Beg` cycler on a `cycleSpeed 1` Prop, so a cel every 2 game
    /// cycles — NOT the blocking 6-tick wait the opening uses.
    /// </summary>
    private const int DoorCloseMs = 2 * CycleMs;

    /// <summary>
    /// Where the door is standing and what it is showing. `(door init: setPri: 6)` at
    /// `room1.sc:1224` puts it in room1's cast at the room's own init and NOTHING ever takes
    /// it out, so between visits it stays on the last building's doorstep showing cel 0 —
    /// the shut door, drawn over the pic's own shut door and therefore invisible.
    ///
    /// <para>
    /// Before the first `openDoor:` it has never been given a `posn:` and so sits at (0,0)
    /// on loop 0 — a 4x1 sliver of the Low-Cost door's bottom row in the screen's top-left
    /// corner. That is in the shipped game. The port declines to draw it only because it has
    /// no cast to hold the prop before the first entry; <see cref="_doorLoop"/> of -1 is
    /// that "not placed yet" state, and it is the one difference from the original here.
    /// </para>
    /// </summary>
    private int _doorLoop = -1;
    private int _doorCel;
    private int _doorX, _doorY;

    private DispatcherTimer? _doorTimer;

    /// <summary>What to run once the four cels of `openDoor:` have played.</summary>
    private Action? _doorOpened;

    /// <summary>True while `closeDoor:`'s `Beg` cycler is stepping back down to cel 0.</summary>
    private bool _doorClosing;

    /// <summary>
    /// `global516` — whether this building's door animates at all. `Place::cue`
    /// (`room1.sc:176-193`) sets it from three clauses and falls through to `(= global516 0)`
    /// otherwise:
    ///
    /// <code>
    /// (or (and (== placeNum 0) (== (global302 livesAt:) 0))
    ///     (and (== placeNum 2) (== (global302 livesAt:) 2))
    ///     (and (== placeNum 1) (or (== (global302 worksAt:) 1)
    ///                              (not (mod global372 4))
    ///                              (global302 leaveOpen:)))
    ///     (and (!= placeNum 0) (!= placeNum 2) (!= placeNum 1)))
    /// </code>
    ///
    /// The two apartment clauses are <see cref="LivesHere"/> and the Rent Office clause is
    /// <see cref="Board.IsOpen"/>, both of which the port already had; this is only the
    /// `or`. It is COSMETIC: the dialog opens either way (`room1.sc:209` is outside the
    /// test), which is why a locked-out player still pays the 2 Hours.
    /// </summary>
    private bool DoorAnimates(LocationId id) => id switch
    {
        LocationId.LowCostHousing or LocationId.SecurityApartments => LivesHere(id),
        LocationId.RentOffice => Board.IsOpen(id, _game!.Calendar.Week, P),
        _ => true,
    };

    /// <summary>
    /// `Place::openDoor` (`room1.sc:107-120`): loop and position from the Place, then cels
    /// 0, 1, 2, 3 with a `(Wait 6)` between each. The interpreter is BLOCKED for those three
    /// waits, so the building's dialog cannot open until the door is open — which is why
    /// <paramref name="thenOpen"/> is a continuation rather than something run alongside.
    ///
    /// <para>
    /// When the door does not animate (<see cref="DoorAnimates"/>) the original simply skips
    /// the method, and so does this: <paramref name="thenOpen"/> runs at once.
    /// </para>
    /// </summary>
    private void OpenDoor(LocationId id, Action thenOpen)
    {
        StopDoorTimer();

        if (_game is null || !DoorAnimates(id)) { thenOpen(); return; }

        var (loop, x, y) = DoorFor(id);
        _doorLoop = loop;
        _doorX = x;
        _doorY = y;
        _doorCel = 0;                 // `(door setCel: 0 startUpd:)`
        _doorClosing = false;
        _doorOpened = thenOpen;

        _doorTimer ??= new DispatcherTimer();
        _doorTimer.Interval = TimeSpan.FromMilliseconds(DoorOpenMs);
        _doorTimer.Tick -= DoorTick;
        _doorTimer.Tick += DoorTick;
        _doorTimer.Start();

        BuildScreen();
    }

    /// <summary>
    /// `Place::closeDoor` (`room1.sc:122-124`) and the identical line in `Game::restore`
    /// (`Game.sc:183`):
    ///
    /// <code>(door setCel: -1 startUpd: setCycle: Beg)</code>
    ///
    /// `setCel: -1` does NOT set the cel to -1 — `Actor::setCel` with that argument CLEARS
    /// signal $1000 (`Actor.sc:148-150`), i.e. it unfreezes the cycler and leaves the cel
    /// where `openDoor:` left it, at 3. `Beg` is `CT` with `endCel 0` and `cycleDir -1`
    /// (`Motion.sc:153-159`), so the door walks 3 → 2 → 1 → 0 and stops.
    ///
    /// <para>
    /// This one is NOT blocking: it is a cycler on the board, started as the building's
    /// dialog closes and left to run while the player looks at the town.
    /// </para>
    /// </summary>
    private void CloseDoor()
    {
        // `(if global516 (self closeDoor:))` — `room1.sc:274-276`. No open, no close.
        if (_doorLoop < 0 || _doorCel == 0) return;

        StopDoorTimer();
        _doorClosing = true;
        _doorOpened = null;

        _doorTimer ??= new DispatcherTimer();
        _doorTimer.Interval = TimeSpan.FromMilliseconds(DoorCloseMs);
        _doorTimer.Tick -= DoorTick;
        _doorTimer.Tick += DoorTick;
        _doorTimer.Start();
    }

    private void DoorTick(object? sender, EventArgs e)
    {
        if (_doorClosing)
        {
            // `CT::doit` stops the moment the cel reaches `endCel`, which `Beg` set to 0.
            if (--_doorCel <= 0) { _doorCel = 0; StopDoorTimer(); }
            BuildScreen();
            return;
        }

        if (++_doorCel >= DoorCels)
        {
            // Cel 3 is the last one `openDoor:` writes, and the method returns with the door
            // standing open; the dialog goes up next.
            _doorCel = DoorCels - 1;
            StopDoorTimer();

            var go = _doorOpened;
            _doorOpened = null;
            BuildScreen();
            go?.Invoke();
            return;
        }

        BuildScreen();
    }

    private void StopDoorTimer()
    {
        _doorTimer?.Stop();
        _doorClosing = false;
    }

    /// <summary>View 751 holds exactly four cels in every one of its thirteen loops.</summary>
    private const int DoorCels = 4;

    /// <summary>True while `openDoor:` is holding the game up, as the `(Wait 6)`s do.</summary>
    private bool IsDoorOpening => _doorOpened is not null;

    /// <summary>
    /// The door, drawn into room1's cast. `setPri: 6` (`room1.sc:1224`) puts it over the
    /// board pic and under the walker; no other board sprite overlaps a doorstep, so list
    /// order carries that here.
    /// </summary>
    private void BuildDoor()
    {
        if (_doorLoop < 0) return;
        AddCastView(DoorView, _doorLoop, _doorCel, _doorX, _doorY);
    }

    /// <summary>
    /// A `View`/`Act` in a room's cast, positioned the way the interpreter positions one:
    /// the cel is anchored by its BASE-CENTRE at the object's `x`,`y`, offset by the cel's
    /// own displacement.
    ///
    /// <para>
    /// `nsLeft = x + displaceX - (width / 2)` and `nsBottom = y + displaceY + 1`, so
    /// `nsTop = y + displaceY + 1 - height`. Displacement is a per-cel field of the view
    /// resource (bytes 4 and 5 of each cel header — see <c>tools/decode_views.py</c>) and the
    /// decoder does NOT bake it into the PNG, so it has to be applied here. It is 0 for every
    /// cel of views 751, 750 and 704; view 340's falling dollar swings on it, which is why
    /// this takes it at all — see <see cref="LottoBuckDisplaceX"/>.
    /// </para>
    ///
    /// <para>
    /// The older board sprites (walker, marble, notice, ambulance, podium) use `y - height`
    /// rather than `y + 1 - height` and so sit one pixel high. That is not corrected here,
    /// because correcting it piecemeal would make the board internally inconsistent; it is
    /// recorded in PARITY.md instead.
    /// </para>
    /// </summary>
    private void AddCastView(int view, int loop, int cel, double x, double y,
                             int displaceX = 0, int displaceY = 0)
    {
        var bmp = SciArt.Cel(view, loop, cel);
        if (bmp is null) return;

        Sprites.Add(new SpriteVm(view, loop, cel,
            x + displaceX - bmp.PixelSize.Width / 2,
            y + displaceY + 1 - bmp.PixelSize.Height));
    }

    // ==================================================================
    // 2. The piggy bank — `piggyBank` (view 704), `bank.sc:510-546`
    // ==================================================================

    /// <summary>
    /// <code>
    /// (instance piggyBank of DCIcon
    ///     (properties nsTop 57 view 704 loop 1 priority 14 cycleSpeed 5))
    /// </code>
    ///
    /// It is in the bank's unconditional `add:` list (`bank.sc:73-84`, third after the
    /// backdrop and the teller), so it is on screen for the whole visit, and `init` is
    /// `(= cel 0)` (`:519-521`).
    ///
    /// <para>
    /// `doit` is <c>(if (&lt; global534 2) (self loop: 6 setCycle: param1 self 1))</c>
    /// (`:523-527`) — and <b>view 704 has only two loops</b>: byte 0 of
    /// <c>assets/raw/view/704.view</c> is 2, loop 0 being a single 8x8 cel and loop 1 the five
    /// 68x55 frames. A loop index past the end is clamped to the last loop, so `loop: 6` is
    /// loop 1, which is the loop the instance declares and the only one whose five cels fit
    /// all three of its drivers (0→4 forward on a deposit, 4→0 back on a withdrawal, 1→0 back
    /// when the work clock finishes). The port therefore draws loop 1 and treats the `6` as
    /// the no-op it is. THIS IS AN INFERENCE from the resource, not a statement in the
    /// scripts — see PARITY.md.
    /// </para>
    ///
    /// <para>
    /// `global534` is the graphics detail level (Ctrl-T). The port has no detail setting, so
    /// every `(&lt; global534 2)` guard in this instance is taken, exactly as
    /// <see cref="ShowBoughtItem"/> already assumes for the `items` panel.
    /// </para>
    /// </summary>
    private const int PiggyView = 704, PiggyLoop = 1, PiggyLeft = 0, PiggyTop = 57;

    /// <summary>`cycleSpeed 5` — `Cycle::nextCel` holds a cel for cycleSpeed + 1 cycles.</summary>
    private const int PiggyCycleSpeed = 5;

    /// <summary>Loop 1 holds five cels (`704.view`), so `lastCel` is 4.</summary>
    private const int PiggyLastCel = 4;

    private int _piggyCel;
    private int _piggyTargetCel;
    private int _piggyDir;
    private int _piggyCount;
    private DispatcherTimer? _piggyTimer;

    /// <summary>
    /// `(piggyBank cel: from doit: End)` or `doit: Beg`. `End` and `Beg` are both `CT`
    /// (`Motion.sc:145-159`): `End` runs forward to `lastCel`, `Beg` backwards to 0, and
    /// each stops there rather than wrapping.
    ///
    /// The three call sites, all in `bank.sc`:
    /// <list type="bullet">
    /// <item>`:198` — a deposit that actually moved money: `(piggyBank cel: 0 doit: End)`.</item>
    /// <item>`:259` — a withdrawal: `(piggyBank cel: 4 doit: Beg)`.</item>
    /// <item>`:501` — the bank's `timeClock::cue`, i.e. the end of a shift worked here:
    ///   `(piggyBank cel: 1 doit: Beg)`.</item>
    /// </list>
    /// Both of the money ones are inside `(if temp0 …)`, so a press that moves nothing —
    /// an empty account, or a wallet the teller cannot take $100 out of — leaves the pig
    /// alone and only the teller speaks.
    /// </summary>
    private void RunPiggyBank(int fromCel, int toCel)
    {
        _piggyCel = fromCel;
        _piggyTargetCel = toCel;
        _piggyDir = Math.Sign(toCel - fromCel);
        _piggyCount = 0;

        _piggyTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CycleMs) };
        _piggyTimer.Tick -= PiggyTick;
        _piggyTimer.Tick += PiggyTick;

        if (_piggyDir != 0) _piggyTimer.Start();
        BuildScreen();
    }

    private void PiggyTick(object? sender, EventArgs e)
    {
        // The pig belongs to the bank's dialog and is disposed with it.
        if (_screen != Screen.LocationPanel || _game is null || P.Location != LocationId.Bank)
        {
            _piggyTimer?.Stop();
            return;
        }

        if (++_piggyCount <= PiggyCycleSpeed) return;
        _piggyCount = 0;

        _piggyCel = Math.Clamp(_piggyCel + _piggyDir, 0, PiggyLastCel);
        if (_piggyCel == _piggyTargetCel) _piggyTimer?.Stop();

        BuildScreen();
    }

    /// <summary>`piggyBank::init` is `(= cel 0)` — every visit opens on the resting cel.</summary>
    private void ResetPiggyBank()
    {
        _piggyTimer?.Stop();
        _piggyCel = 0;
        _piggyCount = 0;
        _piggyDir = 0;
    }

    // ==================================================================
    // 3. The work clock — `TimeClock` (view 750), `WButton.sc:299-311`
    // ==================================================================

    /// <summary>
    /// <code>
    /// (class TimeClock of DCIcon (properties view 750 priority 14 cycleSpeed 10)
    ///     (method (doit)
    ///         (self cel: 0 setCycle: FwdCount self 1)
    ///         (gASoundEffect play: 31)
    ///         (super doit:)))
    /// </code>
    ///
    /// View 750 is one loop of four 68x55 cels — the same slot the `items` panel and the
    /// piggy bank occupy.
    ///
    /// <para>
    /// A `timeClock` instance is declared in the nine workplaces (`appliance.sc:595`,
    /// `bank.sc:494`, `clothing.sc:347`, `discount.sc:781`, `factory.sc:180`,
    /// `fastFood.sc:348`, `market.sc:421`, `rentOffice.sc:528`, `university.sc:737`) and
    /// added to the dialog ONLY when the player works there — `(if (== (global302 worksAt:) 8)
    /// (self add: timeClock) (timeClock setSize:))` (`appliance.sc:120-123` and the eight
    /// siblings). It goes up AFTER `open:`, so it is the last element and draws over the
    /// `items` panel where the two share a position.
    /// </para>
    /// </summary>
    private const int TimeClockView = 750, TimeClockLoop = 0, TimeClockCels = 4;

    /// <summary>`cycleSpeed 10` — a cel every 11 game cycles, so one pass is about 1.1s.</summary>
    private const int TimeClockCycleSpeed = 10;

    /// <summary>
    /// Each instance's own `nsLeft`/`nsTop`, relative to the dialog origin at (69,44).
    /// `appliance.sc:595` and `rentOffice.sc:528` declare `(properties)` with nothing in it,
    /// so they keep the DIcon defaults of 0,0 — which at Socket City is exactly where the
    /// `items` panel sits, and the clock covers it while you work.
    /// </summary>
    private static (int Left, int Top) TimeClockAt(LocationId id) => id switch
    {
        LocationId.SocketCity  => (0,  0),   // appliance.sc:595 — no properties
        LocationId.RentOffice  => (0,  0),   // rentOffice.sc:528 — no properties
        LocationId.Factory     => (96, 40),  // factory.sc:180
        LocationId.HiTechU     => (0, 56),   // university.sc:737
        _                      => (0, 57),   // bank, clothing, discount, fastFood, market
    };

    /// <summary>
    /// Which workplace's dialog carries a clock. `worksAt` is the port's
    /// `(int)Workplace + 1`; the original tests the employer's `placeNum`.
    /// </summary>
    private bool HasWorkClock(LocationId id) =>
        _game is not null
        && Board.Get(id).Workplace is { } w
        && P.WorksAt == (int)w + 1;

    private int _clockCel;
    private int _clockCount;
    private DispatcherTimer? _clockTimer;

    /// <summary>
    /// `(timeClock doit:)` — `workButton::doit` runs it on a shift that actually happened:
    /// <c>((and (&lt; global323 60) (&gt; global566 0)) (items setCycle: 0) (timeClock doit:))</c>
    /// (`appliance.sc:518-527` and the eight siblings).
    ///
    /// `FwdCount` with `count 1` (`FwdCount.sc:7-35`) runs the four cels once and then
    /// `cycleDone` puts the clock back on cel 0, decrements the count to zero and cues its
    /// caller — which is the instance's own `cue`. Eight of the nine do `(self setCycle: 0)`
    /// then `(items init:)`, restarting the shop parade the work button stopped; the bank's
    /// does `(self setCycle: 0)` then `(piggyBank cel: 1 doit: Beg)` (`bank.sc:497-501`).
    ///
    /// The sound is <see cref="Audio.SoundEffects.WorkClock"/>, which <see cref="WorkShift"/>
    /// already plays on the same branch; it is left there so the shift's sounds stay in one
    /// place.
    /// </summary>
    private void StartWorkClock()
    {
        if (_game is null || !HasWorkClock(P.Location)) return;

        _clockCel = 0;
        _clockCount = 0;

        // `(items setCycle: 0)` — the parade stops for the duration and is restarted by the
        // clock's own `cue`.
        _itemsTimer?.Stop();

        _clockTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CycleMs) };
        _clockTimer.Tick -= ClockTick;
        _clockTimer.Tick += ClockTick;
        _clockTimer.Start();
    }

    private void ClockTick(object? sender, EventArgs e)
    {
        if (_screen != Screen.LocationPanel || _game is null || !HasWorkClock(P.Location))
        {
            StopWorkClock();
            return;
        }

        if (++_clockCount <= TimeClockCycleSpeed) return;
        _clockCount = 0;

        if (++_clockCel >= TimeClockCels)
        {
            // `FwdCount::cycleDone` — back to cel 0, then `cue:`.
            _clockCel = 0;
            _clockTimer?.Stop();
            WorkClockCue();
            return;
        }

        BuildScreen();
    }

    /// <summary>`timeClock::cue` — different at the bank from everywhere else.</summary>
    private void WorkClockCue()
    {
        if (_game is not null && P.Location == LocationId.Bank)
            RunPiggyBank(1, 0);                  // `bank.sc:501`
        else
            ResetItemsPanel(P.Location);         // `(items init:)`

        BuildScreen();
    }

    /// <summary>
    /// `CostDItem::doit` opens with
    /// <c>(if (global502 aTimeClock:) ((global502 aTimeClock:) cel: 0 setCycle: 0))</c>
    /// (`WButton.sc:197-200`) — every purchase in the game stops the clock dead and puts it
    /// back on cel 0. The four bank buttons that are plain `WButton`s repeat the same two
    /// lines inline (`bank.sc:185-187`, `:236-238`, `:338-340`, `:414-416`).
    ///
    /// `moreTime` (`rentOffice.sc:227`) is the one clickable line in a workplace that is a
    /// bare `WButton` with no such reset, and it is deliberately not wired to this.
    /// </summary>
    private void ResetWorkClock()
    {
        _clockTimer?.Stop();
        _clockCel = 0;
        _clockCount = 0;
    }

    /// <summary>The dialog is disposed on the way out and the clock with it.</summary>
    private void StopWorkClock() => ResetWorkClock();

    /// <summary>
    /// The clock, drawn last in the shop's element list because that is where
    /// `(self add: timeClock)` puts it — after `open:`, so after everything else.
    /// Positioned by `nsLeft`/`nsTop` like every other DIcon, not base-centred.
    /// </summary>
    private void BuildWorkClock(LocationId id)
    {
        if (!HasWorkClock(id)) return;

        var (left, top) = TimeClockAt(id);
        Sprites.Add(Icon(TimeClockView, TimeClockLoop, _clockCel, left, top));
    }

    // ==================================================================
    // 4. The lottery win — `lottoScript` (script 116) and view 340
    // ==================================================================

    /// <summary>
    /// `lottobuck1`..`lottobuck7` and `lottonote`, all `view 340 priority 5`
    /// (`lottoScript.sc:242-350`). The bills declare `cycleSpeed 1 moveSpeed 1`; the note
    /// declares neither and so takes the `Act` defaults.
    /// </summary>
    private const int LottoView = 340;

    /// <summary>
    /// `setLoop: 0` on every bill (`:35` and siblings). Loop 0 is eight 44-wide cels whose
    /// <b>displacement swings from -22 to +22</b> — that is the flutter, and it is the only
    /// place in the port where a cel's displacement changes the picture's position.
    /// Values read from <c>assets/raw/view/340.view</c>; `displaceY` is 6 on all eight.
    /// </summary>
    private static readonly int[] LottoBuckDisplaceX = [-22, -11, 3, 17, 22, 18, 6, -5];
    private const int LottoBuckDisplaceY = 6;
    private const int LottoBuckCels = 8;

    /// <summary>`(lottonote setLoop: 1 …)` — `:164`. Loop 1 is one 86x84 cel, no displacement.</summary>
    private const int LottoNoteLoop = 1;

    /// <summary>`(lottonote … posn: 29 80 … setMotion: MoveTo 159 143 self)` — `:165-167`.</summary>
    private const int LottoNoteFromX = 29, LottoNoteFromY = 80;
    private const int LottoNoteToX = 159, LottoNoteToY = 143;

    /// <summary>`(self setPri: priority setStep: 12 12)` — `lottonote::init`, `:249`.</summary>
    private const int LottoNoteStep = 12;

    /// <summary>`setStep: 0 7` — no horizontal step, seven pixels down per move.</summary>
    private const int LottoBuckStep = 7;

    /// <summary>One falling dollar bill.</summary>
    private sealed class LottoBuck
    {
        public int X;
        public int Y;
        public int TargetY;
        /// <summary>The sequence cycle it was dropped on; its cel counts from here.</summary>
        public int Born;
        /// <summary>`stopUpd:` at state 13 — it stops being redrawn and so stops cycling.</summary>
        public bool Frozen;
    }

    private readonly List<LottoBuck> _lottoBucks = [];

    /// <summary>`lottoScript::state`, or -1 when the script is not running.</summary>
    private int _lottoState = -1;

    /// <summary>`Script::cycles` — how many game cycles until the next state.</summary>
    private int _lottoCycles;

    /// <summary>True while a state is waiting on a `cue` from a `MoveTo` instead of on cycles.</summary>
    private bool _lottoAwaitingCue;

    /// <summary>Cycles since the sequence started; the bills' cels count off this.</summary>
    private int _lottoCycle;

    private int _lottoNoteStepsDone = -1;
    private int _lottoNoteSteps;
    private bool _lottoTextShown;

    private string? _lottoName;
    private int _lottoAmount;

    /// <summary>What to run when the script hands the turn chain back at state 20.</summary>
    private Action? _lottoDone;

    private DispatcherTimer? _lottoCycleTimer;

    /// <summary>
    /// `startTrn.sc:229-238` — <c>(self setScript: (ScriptID 116 0) 0 (global302 actualName:)
    /// local4)</c> and then `(return)`. The turn-start chain STOPS there: `lottoScript` state
    /// 20 does `(client script: 0 cue:)` (`:231`) and only then does `startTrn` move on to
    /// state 3, which is where the notices live. So the lottery is not something that plays
    /// under the notices — it plays instead of them, and then they follow.
    ///
    /// <para>
    /// `register` is the winner's name and `register2` the prize; state 17 displays them
    /// through text 116[0] `%s` and 116[1] `%d` — a bare number, with no dollar sign, which
    /// is the one place in the game money is shown that way.
    /// </para>
    /// </summary>
    private void StartLotto(string name, int amount, Action done)
    {
        _lottoName = name;
        _lottoAmount = amount;
        _lottoDone = done;

        _lottoBucks.Clear();
        _lottoCycle = 0;
        _lottoNoteStepsDone = -1;
        _lottoTextShown = false;
        _lottoAwaitingCue = false;

        // `(gASong stop:)` `(gASoundEffect loop: -1 play: 25)` — `:42-43`. A cut, then the
        // machine running until `:232` lets it play its current pass out.
        Sound.CutMusic();
        if (!EffectsOff) Sound.PlayEffect(Audio.SoundEffects.Lotto, loop: true);
        else MutedHere.PlayEffect(Audio.SoundEffects.Lotto, loop: true);

        _lottoCycleTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CycleMs) };
        _lottoCycleTimer.Tick -= LottoCycleTick;
        _lottoCycleTimer.Tick += LottoCycleTick;
        _lottoCycleTimer.Start();

        // NOT smoothed. The bills fall 7 pixels a cycle and the note 12, both of which the
        // 25ms cycle timer already redraws; interpolating them would be inventing positions
        // between the script's own steps for no visible gain, and the sequence is over in
        // about six seconds. Recorded in PARITY.md as the one moving thing left stepped.
        LottoChangeState(0);
    }

    /// <summary>True while script 116 owns the board.</summary>
    private bool IsLottoRunning => _lottoState >= 0;

    private void LottoCycleTick(object? sender, EventArgs e)
    {
        if (_lottoState < 0 || _screen != Screen.Board) { EndLotto(); return; }

        _lottoCycle++;

        // Every unfrozen bill takes one `setStep: 0 7` step. `MoveTo::onTarget` stops it
        // within one step of the target, which for a pure vertical move is the target.
        foreach (var b in _lottoBucks)
        {
            if (b.Frozen || b.Y >= b.TargetY) continue;
            b.Y = Math.Min(b.TargetY, b.Y + LottoBuckStep);
        }

        // The note's own MoveTo.
        if (_lottoNoteStepsDone >= 0 && _lottoNoteStepsDone < _lottoNoteSteps)
            _lottoNoteStepsDone++;

        if (_lottoAwaitingCue)
        {
            if (LottoMotionDone()) { _lottoAwaitingCue = false; LottoChangeState(_lottoState + 1); }
        }
        else if (--_lottoCycles <= 0)
        {
            LottoChangeState(_lottoState + 1);
        }

        BuildScreen();
    }

    /// <summary>Whether the `MoveTo` the current state handed its own `self` to has finished.</summary>
    private bool LottoMotionDone() => _lottoState switch
    {
        // `:150` — bill SEVEN alone passes `self`, so state 13 waits for it to land.
        12 => _lottoBucks.Count == 0 || _lottoBucks[^1].Y >= _lottoBucks[^1].TargetY,
        14 => _lottoNoteStepsDone >= _lottoNoteSteps,
        _  => true,
    };

    /// <summary>
    /// `lottoScript::changeState` (`:24-239`), state for state.
    ///
    /// States 0, 2, 4, 6, 8, 10 and 12 each drop one bill; the odd states between them are
    /// pure `(= cycles (Random 1 2))` pauses, so two rolls separate consecutive bills.
    /// </summary>
    private void LottoChangeState(int state)
    {
        _lottoState = state;
        _lottoCycles = 1;

        switch (state)
        {
            case 0 or 2 or 4 or 6 or 8 or 10 or 12:
            {
                // `(= temp1 (Random 100 230)) (= temp2 (Random 20 35)) (= temp3 (Random 250 300))`
                // then `posn: temp1 temp2` and `MoveTo temp1 (+ temp2 temp3)` — straight down,
                // and well off the bottom of the 190-row screen, which is where they go.
                var x = _stockRng.Next(100, 230);
                var y = _stockRng.Next(20, 35);
                var fall = _stockRng.Next(250, 300);

                _lottoBucks.Add(new LottoBuck { X = x, Y = y, TargetY = y + fall, Born = _lottoCycle });

                // `:150` — the seventh is the only one given a caller, so only it is waited on.
                if (state == 12) _lottoAwaitingCue = true;
                else _lottoCycles = _stockRng.Next(1, 2);
                break;
            }

            case 1 or 3 or 5 or 7 or 9 or 11:
                _lottoCycles = _stockRng.Next(1, 2);
                break;

            case 13:
                // `(lottobuckN stopUpd:)` x7 — `:153-159`.
                foreach (var b in _lottoBucks) b.Frozen = true;
                _lottoCycles = 1;
                break;

            case 14:
            {
                // `(lottonote setLoop: 1 posn: 29 80 init: setMotion: MoveTo 159 143 self)`.
                // `setStep: 12 12` on a 130x63 move: `DoBresen` steps the MAJOR axis by its
                // step each move and interpolates the minor, so it takes ceil(130/12) = 11
                // moves. The port interpolates both axes over those eleven moves, which lands
                // on the same endpoint and is within a pixel throughout.
                _lottoNoteSteps = Math.Max(
                    (Math.Abs(LottoNoteToX - LottoNoteFromX) + LottoNoteStep - 1) / LottoNoteStep,
                    (Math.Abs(LottoNoteToY - LottoNoteFromY) + LottoNoteStep - 1) / LottoNoteStep);
                _lottoNoteStepsDone = 0;
                _lottoAwaitingCue = true;
                break;
            }

            case 15:
                _lottoCycles = 1;
                break;

            case 16:
                // `(lottonote stopUpd:)` — `:174`. Nothing to model: loop 1 is a single cel,
                // the note has no cycler, and its `MoveTo` cued state 15 by finishing. The
                // state exists so the state numbers below it line up with the script's.
                _lottoCycles = 1;
                break;

            case 17:
                // The two `Display` calls at `:185-211`, both font 4, colour 0, background -1,
                // centred on the measured width. They stay on the screen until the room is
                // repainted, so this is a latch rather than a one-shot.
                _lottoTextShown = true;
                _lottoCycles = 1;
                break;

            case 18:
                // `(proc0_3 240)` then `(self cue:)` — four seconds at sixty ticks, the same
                // wait every notice takes.
                _lottoCycles = 240 * 1000 / 60 / CycleMs;
                break;

            case 19:
                // `(lottobuckN dispose:)` x7 and `(lottonote dispose:)`, then `(= cycles 2)`.
                _lottoBucks.Clear();
                _lottoNoteStepsDone = -1;
                _lottoCycles = 2;
                break;

            default:
                // State 20: `(proc0_1)` `(client script: 0 cue:)` `(gASoundEffect loop: 1)`.
                EndLotto();
                break;
        }
    }

    /// <summary>
    /// State 20, and the bail-out for anything that takes the board away underneath it.
    /// `(gASoundEffect loop: 1)` lets the machine finish its current pass rather than cutting
    /// it, which is what `Sound.EndEffectLoop` does.
    /// </summary>
    private void EndLotto()
    {
        if (_lottoState < 0) return;

        _lottoState = -1;
        _lottoCycleTimer?.Stop();
        _lottoBucks.Clear();
        _lottoNoteStepsDone = -1;
        _lottoTextShown = false;

        Sound.EndEffectLoop();

        var go = _lottoDone;
        _lottoDone = null;
        go?.Invoke();
    }

    /// <summary>
    /// The lotto actors over the board. All eight declare `priority 5`, the same priority the
    /// turn-start `notice` carries, so they sit over the room and under nothing.
    /// </summary>
    private void BuildLotto()
    {
        if (_lottoState < 0) return;

        foreach (var b in _lottoBucks)
        {
            // `setCycle: Fwd` on `cycleSpeed 1`: a cel every 2 cycles, wrapping at cel 7.
            // `stopUpd:` takes the actor out of the update list, so a frozen bill holds the
            // cel it had — which by then is far below the bottom of the screen anyway.
            var age = _lottoCycle - b.Born;
            var cel = b.Frozen ? 0 : age / (LottoBuckCycleSpeed + 1) % LottoBuckCels;

            AddCastView(LottoView, 0, cel, b.X, b.Y,
                        LottoBuckDisplaceX[cel], LottoBuckDisplaceY);
        }

        if (_lottoNoteStepsDone >= 0)
        {
            var t = _lottoNoteSteps == 0
                ? 1.0
                : Math.Min(1.0, (double)_lottoNoteStepsDone / _lottoNoteSteps);

            var nx = LottoNoteFromX + (LottoNoteToX - LottoNoteFromX) * t;
            var ny = LottoNoteFromY + (LottoNoteToY - LottoNoteFromY) * t;

            AddCastView(LottoView, LottoNoteLoop, 0, nx, ny);
        }

        if (!_lottoTextShown) return;

        // `(Format @local5 116 0 (global302 actualName:))` at (158 - w/2, 64) and
        // `(Format @global100 116 1 register2)` at (160 - w/2, 125). Note the x: 158 for the
        // name and 160 for the figure — the script's own two-pixel disagreement, kept.
        AddLottoLine(SciText.Get(116, 0, _lottoName ?? ""), 158, 64);
        AddLottoLine(SciText.Get(116, 1, _lottoAmount), 160, 125);
    }

    /// <summary>`cycleSpeed 1` on every bill — a cel every two game cycles.</summary>
    private const int LottoBuckCycleSpeed = 1;

    /// <summary>
    /// One of state 17's `Display` calls: measured in font 4, then centred on the given x.
    /// The same shape as <see cref="AddCentredNoticeLine"/>, which is the same `Display`
    /// pattern in `moveNotice`, but centred on the script's own x rather than always on 160.
    /// </summary>
    private void AddLottoLine(string text, int centreX, int y)
    {
        if (string.IsNullOrEmpty(text)) return;

        var measured = new TextVm(text, 0, 0, 8, "#000000", false, fontNumber: 4);
        Texts.Add(new TextVm(text, centreX - measured.W / 2.0, y, 8, "#000000", false,
                             fontNumber: 4));
    }
}
