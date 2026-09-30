# Property-and-selector inventory

A sweep of every `(properties …)` declaration and every behaviour/presentation selector in
`scripts/jones-cd-dos-1.0/src/*.sc`, checked against `src/Jones.Core` and `src/Jones.App`.

This is the `ticksToDo` class of gap: not missing screens, but **properties the scripts state
outright and the port never reads**. It was written without touching any source file.

## Method

- All 70 CD scripts parsed for `(properties …)` blocks: **2,821 declarations, 283 distinct
  property names**. The floppy tree (`jones-dos-1.000.060/src`, 69 scripts) gave 2,795 / 277.
- "Readers in scripts" = every occurrence of the identifier in the CD tree minus its
  declarations, so a property with 0 readers is read by nothing, including its own class.
- "Port mentions" = case-insensitive whole-word matches across every `.cs` under `src/`
  excluding `obj/` and `bin/`. The port PascalCases, so the match is deliberately loose —
  a non-zero count is a hint, not proof, and each finding below was confirmed by reading.
- Strings quoted from `assets/raw/text/*.text`, never from the decompiled listing.

## Counts

> **Status, after the drawing-and-input pass.** Of the fourteen findings in §1, six are
> mine and are now: **1.1 CLOSED**, **1.9 CLOSED**, **1.14 CONFIRMED (nothing to do)**,
> **1.6 PART**, **1.7 PART**, **1.8 PART**, **1.10 not done with the reason recorded**.
> Each section carries its own note. The CD/floppy record (1.11) is nobody's and is untouched.
>
> **Status, after the missing-animations-and-sprites pass.** The six animation findings are
> now: **1.2 CLOSED** (the door), **1.3 CLOSED** (the piggy bank), **1.4 CLOSED** (the work
> clock), **1.5 CLOSED** (the lottery), **1.12 CLOSED narrowly** (`setPri:` is modelled where
> it varies at runtime; the deviations table in PARITY.md states what the general gap costs),
> **1.13 CLOSED as nothing-to-do**, with all 61 sends accounted for in a table. Each section
> carries its own note, including the three places where the finding as written was wrong
> about the mechanism.

| class | count |
| --- | ---: |
| **NOT MODELLED, MATTERS — findings** | **14** |
| …property names they cover | 16 |
| …plus selector families: `setPri:`, `stopUpd:`/`startUpd:`/`forceUpd:`/`addToPic:`, `enable:` | 3 |
| NOT MODELLED, DEAD — property names, each with the search that found no reader | 50 |
| MODELLED or N/A — the remaining property names, §2 and §4 | 217 |
| distinct property names, total | 283 |

The MODELLED/N/A boundary is a judgement call (is `nsLeft` "modelled" or "layout data the
port reads"?), so those two are not split numerically; §2 and §4 name them instead. The other
two rows are exact and each entry is individually evidenced below.

---

# 1. NOT MODELLED, MATTERS — ranked

## 1.1 Every clickable label in the game has a 1-pixel drop shadow. The port draws none. — **CLOSED**

> **CLOSED.** All three consequences are ported. `SciFont.Render` gained a shadow pass;
> `MenuLineVm` takes `textColour`/`shadowColour`/`flashColour` as palette indices and
> `StoreLayout.ColoursFor` holds the per-screen table with a citation per row;
> `SciPalette` resolves the indices to RGB from the game's own view palettes, cross-checked
> three ways against the shipped art (see PARITY.md §4). The shadowed bitmap is one pixel
> larger in each direction, which is `setTextSize`'s own growth, so the hit rectangle grew
> with it. **The invented `#0000C0` hover is gone**: the second bitmap is `flashColor` and
> the view binds `IsPressed`, because the original has no hover state.

**`shadowColor` (109 declarations), `textColor` (43), `flashColor` (4), `backColor`**

`WButton::draw` (`scripts/jones-cd-dos-1.0/src/WButton.sc:52-65`):

```
(method (draw &tmp [temp0 40])
    (self setTextSize:)
    (super draw:)
    (if (and global535 shadowColor)
        (self doDisplay: (+ nsLeft 1) (+ nsTop 1) (if global535 shadowColor else 7) 1)
    )
    (self doDisplay: nsLeft nsTop (if global535 textColor else 0) 0)
)
```

`global535` is 1 on any non-16-colour display (`Main.sc:1193`:
`(= global535 (if (== (Graph grGET_COLOURS) 16) 0 else 1))`), so the CD build always takes
the coloured branch. Every shop item, every job line, every bank button, every rent-office
option is drawn **twice**: a shadow at (nsLeft+1, nsTop+1) in `shadowColor`, then the text at
(nsLeft, nsTop) in `textColor`.

The pairs are per-screen and consistent within a screen:

| screen | textColor | shadowColor |
| --- | ---: | ---: |
| Socket City (`appliance.sc:191-192`) | 39 | 115 |
| Z-Mart (`discount.sc:273-274`) | 26 | 102 |
| Monolith (`fastFood.sc:176-177`) | 27 | 116 |
| Black's Market (`market.sc:200-201`) | 37 | 112 |
| Pawn Shoppe (`pawnShop.sc:639`) | 26 | 65 |
| Broker (`broker.sc:343`) | 0 (WButton default) | 93 |
| QT Clothing (`clothing.sc:175`) | 0 | 89 |
| Employment Office (`employment.sc:387`) | 0 | 80 |
| every job list (9 scripts, e.g. `applianceJobs.sc:114`) | 0 | 107 |
| `WButton` class default (`WButton.sc:33-36`) | 0 | 6 |

The port: `MenuLineVm` (`src/Jones.App/Jones.App/SpriteVm.cs:67-93`) renders exactly one
bitmap per colour, and every one of its five call sites in `MainViewModel.cs`
(`:4152`, `:4182`, `:4222`, `:4301`, `:4331`) leaves the defaults
`normalColour = "#000000"`, `hoverColour = "#0000C0"`. A search for `Shadow`/`shadow` across
`src/**/*.cs` returns **nothing**.

Three separate consequences:

1. **No shadow anywhere.** Six shops that draw coloured text on a coloured background now
   draw flat black with nothing behind it.
2. **`flashColor` is the press colour and the port's hover colour is invented.** `WButton::hilite`
   (`:67-78`) and `WButton::track` (`:80-114`) swap to `flashColor` **while the mouse button is
   down** and back to `textColor` on release — there is no hover state in the original at all.
   `flashColor` is 100 on the `WButton` class and 255 on `businessSuit`, `leisureSuit` and
   `casualClothes` (`clothing.sc:176`, `:202`, `:228`). `#0000C0` is mine.
3. **The hit rectangle is one pixel short.** `WButton::setTextSize` (`:125-132`) grows the
   control by the shadow: `(= nsBottom (+ [temp40 2] nsTop (!= shadowColor 0)))` and the same
   for `nsRight`.

`theFont 10` (`WButton.sc:32`) is correct in the port by luck — `SciFont.Small` and
`SciFont.Default` both resolve to `Load(10)` (`SciFont.cs:196-213`).

## 1.2 Walking into a building plays a four-cel door animation. The port has no door.

> **CLOSED** (see PARITY.md §2, `MainViewModel.Animation.cs` §1). All thirteen `doorLoop` /
> `doorX` / `doorY` triples are tabulated from `room1.sc:294-574`, `openDoor:` steps cels 0→3
> with the `(Wait 6)` between each, and `closeDoor:` runs the `Beg` cycler back down on the
> board. Two corrections to the finding as written:
> * **`(Wait 6)` is not a cycler.** It is the interpreter's blocking sleep in 60ths of a
>   second — this game settles the unit itself at `Main.sc:955-970`, where `proc0_3 n` is
>   `(/ n 6)` iterations of `(Wait 6)`. So the door is 100ms a cel and the building's dialog
>   cannot open until it has finished, which is why the port runs it as a continuation.
>   `cycleSpeed 1` on the `door` Prop governs only the CLOSING, at 2 game cycles a cel.
> * **`setCel: -1` does not set a cel.** `Actor::setCel` with −1 clears signal $1000
>   (`Actor.sc:148-150`), unfreezing the cycler and leaving the cel at 3.
>
> Also found while closing it: `Game.sc:183` is a SECOND `closeDoor:` site, in `Game::restore`,
> and `(door init: setPri: 6)` at `room1.sc:1224` means the prop is in room1's cast from the
> room's own init — so before the first entry it stands at (0,0) on loop 0, a 4x1 sliver of
> the Low-Cost door in the screen's top-left corner. The port does not draw that.

**`doorLoop` (13 Place instances), `doorX` (14), `doorY` (14)**

`Place::openDoor` (`room1.sc:107-120`):

```
(method (openDoor)
    (door loop: doorLoop posn: doorX doorY)
    (door setCel: 0 startUpd:)   (proc0_1) (Wait 6)
    (door setCel: 1)             (proc0_1) (Wait 6)
    (door setCel: 2)             (proc0_1) (Wait 6)
    (door setCel: 3)             (proc0_1)
)
(method (closeDoor)
    (door setCel: -1 startUpd: setCycle: Beg)
)
```

`door` is `(instance door of Prop … view 751 cycleSpeed 1)` (`room1.sc:66-71`). Each Place
carries its own loop and its own screen position, e.g. `securityP` and `marketP` at
`room1.sc:330-370`; `doorLoop` runs 1..12.

Port: `751` appears **zero** times under `src/`; so do `doorLoop`, `doorX`, `doorY`.
`Board.cs:106-128` models only the open/closed *rule* (`IsOpen`) and says outright that the
animation is cosmetic. It is, but it is also the transition between the board and every
interior — three quarters of a second of animation, played on every one of the dozens of
building entries in a game, replaced by an instant cut.

`closeDoor:` is likewise unmodelled (`closeDoor:` — 1 send, 0 port mentions).

## 1.3 The bank's piggy bank animates on every deposit and withdrawal.

> **CLOSED** (PARITY.md §3, `MainViewModel.Animation.cs` §2). One correction to the finding:
> it should NOT go into `StoreLayout.ItemsFor(Bank)`. The bank never publishes `gItems` — the
> six `(= gItems …)` sites are `appliance.sc:85`, `clothing.sc:74`, `discount.sc:137`,
> `fastFood.sc:73`, `market.sc:90` and `pawnShop.sc:432`, and the bank is not among them — so
> `ItemsFor` returning null is right and the pig is a prop of its own that happens to occupy
> the same slot. Both money drivers are inside `(if temp0 …)`, so a press that moves nothing
> leaves it alone. `doit`'s `loop: 6` is out of range (view 704 has two loops) and is treated
> as the clamp to loop 1 that the instance declares; that inference is flagged in PARITY.md's
> queue as the one thing here nobody can check from the scripts alone.

**`bank.sc:510-546` — `piggyBank of DCIcon`, `view 704 loop 1 nsTop 57 priority 14 cycleSpeed 5`**

```
(method (doit param1)  (if (< global534 2) (self loop: 6 setCycle: param1 self 1)))
```

Drivers:
- deposit — `bank.sc:198`: `(piggyBank cel: 0 doit: End)` (forwards, coin in)
- withdraw — `bank.sc:259`: `(piggyBank cel: 4 doit: Beg)` (backwards)
- work clock finishing — `bank.sc:501`: `(piggyBank cel: 1 doit: Beg)`

`cycleSpeed 5` puts a cel every 6 game cycles. It is in the bank's `add:` list unconditionally
(`bank.sc:75-84`), so it is on screen the whole time you are in the bank.

Port: `StoreLayout.ItemsFor(LocationId.Bank)` is not in the switch, so it falls to
`_ => null` (`StoreLayout.cs:398-407`). `704`, `piggyBank` and `PiggyBank` appear nowhere in
`src/`. The bank panel has no picture at all where the original has an animated one.

## 1.4 The work clock in nine shops.

> **CLOSED** (PARITY.md §3, `MainViewModel.Animation.cs` §3). View 750 is one loop of four
> 68x55 cels — a punch clock whose handle swings down — drawn at each instance's own
> `nsLeft`/`nsTop`, added last so it covers the `items` panel at Socket City, and run once per
> shift on the branch that already played effect 31. Its `cue` restarts the `items` parade the
> work button stopped, or at the Bank starts the piggy bank. Every `CostDItem` press resets
> it, plus the four bank `WButton`s that repeat the two lines inline; `moreTime` is the one
> clickable line in a workplace that does not, and is left alone.

**`WButton.sc:299-311` — `class TimeClock of DCIcon`, `view 750 priority 14 cycleSpeed 10`**

```
(method (doit)
    (self cel: 0 setCycle: FwdCount self 1)
    (gASoundEffect play: 31)
    (super doit:)
)
```

A `timeClock` instance is declared in nine location scripts (`appliance.sc:595`, `bank.sc:494`,
`clothing.sc:347`, `discount.sc:781`, `factory.sc:180`, `fastFood.sc:348`, `market.sc:421`,
`rentOffice.sc:528`, `university.sc:737`) and added to the dialog when the player works there.
Its `setSize` publishes it as `global502 aTimeClock:`, and **every purchase resets it** —
`CostDItem::doit` opens with `(if (global502 aTimeClock:) ((global502 aTimeClock:) cel: 0 setCycle: 0))`
(`WButton.sc:198-200`), and `bank.sc:185`, `:236`, `:338`, `:414` do the same.

`ticksToDo` is 0 on `DCIcon`, so this is the `cycleSpeed` branch of `Cycle::nextCel`: one cel
per 11 game cycles, once round the loop, then `cue:`.

Port: `750` never appears under `src/`. Sound effect 31 **is** modelled
(`IAudioPlayer.cs:239-240`, `WorkClock`) — the clip plays with nothing on screen to explain it.

## 1.5 The lottery-win animation: seven dollar bills and a note.

> **CLOSED** (PARITY.md §3, `MainViewModel.Animation.cs` §4). All twenty-one states are
> played. Three things the finding did not have:
> * **The bills fall off the screen.** `(Random 250 300)` added to a start of 20-35 puts every
>   target below row 190, so `stopUpd:` at state 13 freezes seven sprites nobody can see.
> * **The flutter is cel DISPLACEMENT**, not motion: `setStep: 0 7` is purely vertical, and
>   loop 0's eight cels carry `displaceX` −22, −11, 3, 17, 22, 18, 6, −5. The port had no
>   displacement support at all; it does now, for this one sprite.
> * **`(proc0_3 240)` is at the END**, state 18, after the note has landed and the text is up.
>   The port used to start its 240-tick timer at the beginning of the sequence.
> Also: `startTrn.sc:229-238` hands the chain to script 116 and returns, so the turn-start
> notices now follow the lottery instead of playing over it.

**`lottoScript.sc` — `lottobuck1..7` and `lottonote`, `view 340 priority 5 cycleSpeed 1 moveSpeed 1`, `setStep: 0 7`**

States 0-16 of `lottoScript` (`:26-176`) drop seven bills one at a time from
`(Random 100 230, Random 20 35)` down by `(Random 250 300)` with `setStep: 0 7` — zero
horizontal, seven vertical per step — then `stopUpd:` all seven and fly the `lottonote`
(`setStep: 12 12`, `lottoScript.sc:249`) from (29,80) to (159,143).

Port: `MainViewModel.StartLottoLoop` (`:2928-2946`) plays the looping sound and sets a
240-tick timer, which is the `(proc0_3 240)` at `lottoScript.sc:215`. Nothing is drawn.
`340` appears in the port only for Wild Willy (`MainViewModel.cs:3633`), who shares the view.

## 1.6 `state` bits: nothing separates enabled, selected and disabled controls. — **PART**

> **PART.** Bit 0 is modelled: `MenuLineVm` carries `Enabled` and the view binds
> `IsHitTestVisible` (not `IsEnabled` — the original greys nothing), so a disabled line is
> drawn and deaf, which is what `Item::handleEvent` does. The shop lines were checking
> `Enabled` *inside* the click handler, which swallowed the click instead of never taking
> it. The eleven `state 0` / `state 288` captions are now non-interactive labels drawn with
> their own declared shadow: the nine `jobsAvailable` headers, the Pawn Shoppe's three list
> titles and the broker's six holdings figures. **Still open:** the sixteen `enable:` sends
> are not wired — nothing in the port yet sets a line disabled — and bit 3 (`select`) is a
> deviation, because `WButton::select` redraws nothing and the pawnable list's highlight is
> the interpreter's, not a script's. See PARITY.md's deviations table.

**`state` — 109 declarations, 10 distinct values**

`Item::enable` sets/clears bit 0 and `Item::select` sets/clears bit 3
(`Interface.sc:385-400`); `Item::handleEvent` (`:407-420`) ignores any event on a control whose
bit 0 is clear. Declared values:

| value | hex | where |
| ---: | --- | --- |
| 0 | — | the 9 `jobsAvailable` headers + 2 others: **permanently disabled labels** |
| 1 | `$001` | `InvestmentDIcon`, `UniversityDIcon`, `DEdit` |
| 3 | `$003` | `DButton` |
| 33 | `$021` | `WButton` class, `viewGoals` player numbers |
| 163 | `$0A3` | `JobDItem`, `select1`/`select4` buttons |
| 256/257 | `$100`/`$101` | `calc`, `books`, `StarSlider`, market `workButton` |
| 288/289 | `$120`/`$121` | broker holdings; every `workButton`, `pawn`, `enrollButton` |
| 419 | `$1A3` | every `exitButton` in the game |

`enable:` has 16 sends and `select:` 34; the port mentions `enable` **0** times. The port's
`ActionVm` carries a single `bool enabled` and `MenuLineVm` carries none, so a shop line that
the original has greyed out and click-through-proof is, in the port, either absent or live.

## 1.7 Per-control keyboard accelerators: 151 declared, 2 implemented. — **PART**

> **PART, and the question this section could not answer is now answered.** `key` is the
> `event message` a keystroke produces, compared raw. The large values are literal ASCII
> (120 `x`, 119 `w`, 98 `b`, 106 `j`); the 1-24 range is Ctrl-A..Ctrl-X, and the proof is
> what the scripts REFUSE to use — every list numbers in declaration order and every list
> steps over **9, 13 and 15**, which are Tab, Return and Shift-Tab, the three keys
> `Dialog::handleEvent` claims before any control sees them (`Interface.sc:1113`, `:1126`,
> `:1152`). That is why Socket City's ninth item is `key 10` and Z-Mart bumps its 9th, 13th
> and 15th to 20, 23, 24. `key` is ALSO a handle rather than only an accelerator, which is
> why the numbering is ordinal: the Jones AI presses a control by fabricating an event with
> it, and the broker uses it as a list index (`broker.sc:523`). The reasoning lives in
> `Jones.App.SciKey`. Wired: the six fixed-layout shops, Employment's nine, all 39 jobs,
> the broker's Buy/Sell, the Pawn Shoppe's four. **Not wired**: Z-Mart's eighteen and
> Hi-Tech U's twelve, both because the port's line does not carry the item or degree
> identity the key belongs to. Ctrl-Q/S/T/V/Y/Z are the menu bar's and it takes them first,
> so Z-Mart's `eightTrack` (17) and `leisureSuit` (20) are unreachable in the shipped game
> too.

**`key` — 151 declarations**

`Item::handleEvent` (`Interface.sc:412-413`) fires a control on
`(and (& evtType evKEYBOARD) (== (event message:) key))`. Values: 1-24 on shop items, jobs and
bank buttons (21 controls on `key 1`, 19 on `key 2`, …), `98` on the broker's Buy, `106` on
Sell, `119` (`w`) on the nine `workButton`s, `120` (`x`) on all seventeen `exitButton`s.

Port `MainViewModel.HandleKey` (`:5234-5406`) implements `X` and `W` only, and says so at
`:5362-5382`. The other 125 declarations are unbound. (The 1-24 range is Ctrl-A..Ctrl-X; I have
not confirmed against a running original that they are reachable, so this is ranked below the
drawing findings.)

## 1.8 `Display` draws on an opaque background band. `TextVm` cannot. — **PART**

> **PART, and the count is smaller than it looks.** `TextVm` takes a `background` palette
> index now. **Nineteen of the twenty are invisible**, measured rather than assumed: the
> artist filled each panel with the very index the script names, so sampling the shipped art
> at the coordinates the scripts draw at returns exactly the declared colour — view 696 is a
> flat #6098C8 (93) under the broker's four headings and every price row, views 501 and 505
> a flat #7088E0 (99) under all three goals screens, view 0 loop 4 a flat #98A8B0 (101)
> under the calculator readout. The band exists to blank the previous value in place; this
> port rebuilds. **Two are drawn**: the board's `Week #%2d` (86), which sits over a dithered
> strip of pic 11 mixing #8890A0/#7088A0/#708090, and Hi-Tech U's per-row chip
> (`[local9 4] = [98 75 87 56]`, indexed alongside `[local5 4] = [97 111 125 139]`, so the
> colour belongs to the ROW and not to the course), which sits in a transparent gap in the
> course bar over a flat #C8E0F8 panel. `select4`'s two (79) belong to a screen the port
> does not have. Those three sampled agreements are also the cross-check that `SciPalette`'s
> index→RGB table is right.

Twenty `Display` calls pass a real `dsBACKGROUND` colour rather than −1:

- `room1.sc:1165`, `:1324` and `Game.sc:117` — the board's *Where*/*Week #%2d* labels,
  `(if global535 86 else 7)`
- `viewGoals.sc:404`, `:428`, `:452`, `:476`, `select3.sc:364`, `goalsDefine.sc:236` —
  `(if global535 99 else 9)`
- `select4.sc:214`, `:244` — `(if global535 79 else 9)`
- `broker.sc:209-280` — the holdings table, `temp20 = (if global535 93 else 3)` (`:208`)
- `university.sc:136` — a per-row colour, `[local9 temp3]`
- `room1.sc:1445` — the calculator, `101` in VGA

`TextVm` (`SpriteVm.cs:155-199`) takes `colour` and nothing else; it renders glyphs with
transparent gaps. Every one of those labels is currently drawn onto whatever is behind it.

## 1.9 The calculator readout is the wrong font, position and colour. — **CLOSED**

> **CLOSED.** Now font 14, LEFT-aligned from (274,166), colour index 0 on a background of
> 101, with the string `"%6s "` verbatim. Measured off `font_14.font`: 7px tall, 5px digits,
> **4px space** — so the readout's right edge really does creep, 303 for one digit through
> 308 for six, and the port's true right-alignment was hiding it. Font 10's space is 5px
> like its digits, which is why the old code looked aligned by accident. The
> `StoreLayout.cs` comment claiming the calculator shows `cash - 1` is corrected at the
> site.

**`calc` — `room1.sc:1419-1459`, `state 256 nsTop 160 nsLeft 252 loop 4`**

```
(Display (Format @temp0 1 3 (proc115_0 global456 value))
    dsCOORD (+ nsLeft 22) (+ nsTop 6)
    dsCOLOR 0
    dsBACKGROUND (cond (global535 101) (global552 15) (else 7))
    dsFONT 14)
```

Text 1[3] is `"%6s "` (raw `assets/raw/text/1.text`) — a six-character **right-aligned** field
plus a trailing space, drawn **left-aligned from x=274, y=166, in font 14** on an opaque
background of palette index 101.

Port (`MainViewModel.cs:2105-2117`): font is chosen by `size 7` → `SciFont.Small` →
resource **10**; the string is right-aligned to `CalcLeft + CalcDisplayRight` = **x 308**;
y is `CalcTop + 5` = **165**; the colour is `#203020`, which is invented; there is no
background band. Font 14 is 7px tall with 5px digits and a **4px** space; font 10 is 6px tall
with 5px digits and a 5px space — so the original's "right alignment" is itself imperfect and
the right edge creeps as digits are added, which the port's true right-alignment hides.

`StoreLayout.cs:299-302` also asserts the calculator "shows `cash - 1`". It does not:
`value: (- (global302 cash:) 1)` is a **cache invalidation** — `value` is the last-drawn
amount, and `calc::doit` immediately does `(= value (global302 cash:))` before displaying it.

## 1.10 The Game Speed control changes `ticksToDo`, `moveSpeed` and the travel clock together. — **NOT DONE, reason recorded**

> **Still unported, deliberately.** Two blockers, neither of which this finding removes.
> `Gauge.sc` is the whole user interface of the feature — a slider dialog with its own art
> and its own `higher`/`lower` captions — and it is unported; Ctrl-V has the same gap, which
> is why Change Volume is the two ends of a 0-15 scale. And `MarbleMoveSpeed` /
> `TicksPerHour` are `const` in `Jones.Core.GameClock`, with the marble stepping in the
> animation loop. As this section itself says, the effect is **presentation only** — the
> hours a journey costs are invariant under the setting — so implementing it without the
> Gauge would be a hidden switch with no way to reach it. Menu item 770 stays greyed.

**`Menu.sc:293-318`, menu id 770 (Ctrl-S)**

```
((ScriptID 1 7) ticksToDo: (- 7 temp4) moveSpeed: (- 7 temp4))       ; marble
(if ((ScriptID 1 7) mover:)  (((ScriptID 1 7) mover:)  b-moveCnt: 0))
(if ((ScriptID 1 7) cycler:) (((ScriptID 1 7) cycler:) ticksToDo: (- 7 temp4)))
(= global475 (* (- 7 temp4) 14))
```

Six speeds, 1..6, defaulting to the marble's declared `ticksToDo 1` / `moveSpeed 1`
(`room1.sc:1083-1084`). The port hard-codes `MarbleMoveSpeed = 1` and
`TicksPerHour = MarbleMoveSpeed * 14` (`GameClock.cs:52-65`) and says it does not model the
menu. `MainViewModel.cs:5411-5413` lists Ctrl-S as deliberately unbound.

Worth stating precisely for whoever ports it: because `global475` scales with `moveSpeed`, the
**hours** a journey costs are invariant under the speed setting; only wall-clock time changes.
The `Gauge` class itself (`Gauge.sc:20-28`: `description`, `higher {up}`, `lower {down}`,
`normal 7`, `minimum 0`, `maximum 15`) is unported, and Ctrl-V shares that gap.

## 1.11 `cycleSpeed 300` vs the floppy's `100` — and the CD/floppy wage table.

The shop `items` parade is **three times slower on CD**: `cycleSpeed 300` in all six CD shops,
`100` in all six floppy ones. The port uses 300 (`MainViewModel.cs:3905`) — correct for its
target, but the divergence is worth recording because the two trees disagree on twelve more
job wages and one dependability:

| instance | CD `basePrice` | floppy |
| --- | ---: | ---: |
| `applianceJobs.salesperson` | 7 | 5 |
| `clothingJobs.salesPerson` | 8 | 7 |
| `discountJobs.clerk` / `assistManager` / `manager` | 5 / 7 / 8 | 4 / 6 / 7 |
| `fastFoodJobs.cook` / `clerk` / `assistManager` / `manager` | 5 / 6 / 7 / 8 | 4 / 5 / 6 / 7 |
| `marketJobs.janitor` | 6 | 5 |
| `rentJobs.groundsKeeper` / `apartmentManager` | 7 / 9 | 6 / 8 |
| `universityJobs.janitor` | 5 | 4 |
| `factoryJobs.janitor` `dependibility` | 20 | 30 |

`Jobs.cs` uses the CD column throughout — verified line by line. Also divergent:
`clothing.sc` `businessSuit`/`leisureSuit` **swap `celNum`** (CD 2/1, floppy 1/2), and
`room1.sc` `employmentP topR` is 158 on CD, 168 on floppy — `Board.cs:74` has 158. Correct.

## 1.12 `setPri:` — 37 sends, 3 port mentions.

> **CLOSED, narrowly** (PARITY.md §2 and its deviations table). `BuildWinner` now emits its
> cast in ascending `priority` with a stable sort, so `jonesGuy`'s random 2-or-5 against the
> `priority 4` plinth works, and each `confetti` burst takes the priority of the pass that
> threw it (`jonesGuy::cue` does `setPri: priority`, `:178`, reading the value `Actor::setPri`
> has just written). That also fixed something not in the finding: `pedistal` is priority 4
> and `theWinner` priority 3, so the plinth belongs OVER the winner's feet and the port had
> the figure standing in front of it.
>
> The other two sites need nothing. `room1.sc:1276-1279`'s `setPri: 7 init: addToPic:` bakes
> the four number props into the background pic (`Actor.sc:167-172` sets signal $8021), so
> their priority never competes with anything and `proc1_8`'s order is already correct;
> `lottoScript.sc:249-347` gives all eight actors `priority 5`, so list order IS priority
> order there. Draw order remains list order everywhere else, which costs nothing on any
> screen the port currently draws — the deviations table says why, and what making it general
> would take.

Draw order in the port is list order in `Sprites`. That is right most of the time, but three
sites choose priority at runtime and one of them is random:

- `winnerScript.sc:77`, `:89` — `setPri: (+ 2 (* (Random 0 1) 3))` puts `jonesGuy` and each
  confetti burst either **in front of or behind** the pedestal (priority 4). The port models
  this for confetti (`_confetti` carries `InFront`) but not for `jonesGuy` (`:1855`).
- `room1.sc:1276-1279` — the four player-number props at `setPri: 7` then `addToPic:`.
- `lottoScript.sc:249-347` — all eight lotto actors, unmodelled with the rest of 1.5.

## 1.13 `stopUpd:` / `startUpd:` / `forceUpd:` / `addToPic:` — 61 sends, 2 port mentions.

> **CLOSED as "nothing to do", with the 61 accounted for.** Every send outside `Actor.sc`
> was listed and sorted; none of them changes what the port should draw, because the port
> rebuilds the whole screen from state every frame rather than maintaining an update list.
>
> | group | sends | why it does not matter |
> | --- | ---: | --- |
> | `introRoom.sc:223-240`, `:274-406` | 14 | `stopUpd:`/`addToPic:` on the credit photographs, each of which the port already draws as a static sprite for its state's duration. |
> | `lottoScript.sc:153-159`, `:174` | 8 | Freezing seven bills that are already below the bottom of the screen, and a note that has stopped moving and has a one-cel loop. Modelled anyway (the bills stop cycling); invisible either way. |
> | `room1.sc:41-46`, `:1156-1159`, `:1276-1279`, `:1301`, `winnerScript.sc:38-43` | 15 | `addToPic:` on the board's outline, `picPatch` and the four number props, and on the podium's two backgrounds. These are the pic, and `proc1_8`'s order is the port's `BuildBoard` order. |
> | `gTheWalker` `forceUpd:`/`stopUpd:` — `room1.sc:194`, `:271`, `:1096`, `:1154`, `:1313`, `marblePath.sc:48`, `startTrn.sc:889`, `Game.sc:179` | 8 | Forcing a redraw of a figure the port redraws unconditionally. `room1.sc:194`'s `setCycle: 0` beside it IS modelled — it is why the walker's cel stops when a journey ends. |
> | `room1.sc:109`, `:123`, `Game.sc:183` | 3 | The door's `startUpd:`, which is now §1.2. |
> | `startTrn.sc:940`, `:1116` | 2 | `stopUpd:` on the notice and the ambulance, both of which the port holds still by holding their coordinates still. |
>
> The one real consequence the finding names — "where the original bakes a sprite into the pic
> and then `erase:`s the dialog over it, the port's z-order has to be right by construction" —
> is now covered by §1.12's entry in the deviations table, which states exactly where list
> order and priority order agree and where they do not.

`addToPic:` (`Actor.sc:167-172`) bakes an actor into the background:
`(self signal: (| signal $8021))`. `room1.sc:41-46`, `:1156-1159`, `:1276-1279`, `:1301` and
`introRoom.sc:225`, `:240` use it, and `winnerScript.sc:38`, `:43`. `stopUpd:` freezes a cel
in place (27 sends, mostly `introRoom` and `lottoScript`). The port redraws everything every
frame, so nothing *breaks* — but where the original bakes a sprite into the pic and then
`erase:`s the dialog over it, the port's z-order has to be right by construction instead.
Listed here rather than under N/A because `addToPic:`+`erase:` is how the original composes
the board, and getting it wrong is invisible until it isn't.

## 1.14 `education2` — the second degree requirement. — **CONFIRMED, nothing to do**

> **Confirmed and recorded at the site** (`Jones.Core.Model.Employment`, beside the two
> writes). A whole-word search of BOTH trees finds exactly four hits for `needEd1`/`needEd2`:
> the two `(properties)` declarations (`room1.sc:637-638`, floppy `:625-626`) and the two
> writes (`employment.sc:95-96`, floppy `:137-138`). There is no read anywhere. The port
> stores them, saves them and shows them nowhere, which is the original's behaviour exactly
> — so there is nothing to implement, and the comment now says so to stop this being
> re-found a third time.

**6 declarations: `applianceJobs.manager 11`, `bankJobs.broker 16`, `factoryJobs.departmentManager 13`,
`engineer 14`, `generalManager 13`, class default 0 (`employment.sc:82`)**

Read once, at `employment.sc:96`: `needEd2: (if global325 0 else education2)`. `Employment.cs`
mentions `education2` once and `Jobs.cs` carries a second prerequisite column, so this is
**modelled** — but `needEd1`/`needEd2` themselves (the *stored* "what you were short of",
`room1.sc:637-638`) are read nowhere else in either tree, so the player-facing "you need X and
Y" that they were presumably meant to feed does not exist. Recorded so it is not re-found.

---

# 2. MODELLED

Confirmed by reading, not by grep count.

**Simulation.** `basePrice` (89), `indexNum` (95), `typeOfGoods` (32), `units` (15),
`quantity`, `pricePaid`, `attributes` (`$0018` pawn ticket, `$0020` forfeit, `$0038` mask,
`$0040` breakable, `$0100` second-hand — `ItemIds.cs:94-137`), `redemptionPrice`,
`unitsToGraduate 10`, `preReq` (all nine values verified against `Degrees.All`),
`visitTime` (JobDItem 4, UniversityDIcon 6, newspaper 1, CostDItem 0), `uniform` (34/35/36),
`dependibility`, `experience`, `education`, `education2`, `jobNum`, `minDepend`, `maxExper 10`,
`relax 25`, `curRent 325`, `cash 200`, `netWorth 200`, `lqAss 200`, `wearing 36`,
`nakedCount`, `turnedOver`, `triedExt`, `rentExt`, `leaveOpen`, `loanBal`, `latePay`,
`paySched`, `madePay`, `rentOwed`, `xcred`, `eduCredit`, `expCredit`, `notEnoughEd`,
`enrollments`, `raises`, `worksAt`, `livesAt`, `wage`, `baseWage`, `occupation`, `whichBody`,
`whatNum`, `playing`, `playJones`, `finStat`, `actualName`, `theSign` (by behaviour — the
withdraw/purchase sign is applied, the property name is not used).

**Economy.** The whole `EconomicIndex` set: `reading`, `adjustment`, `high`, `low`,
`lowerRange`, `upperRange`, `risk` (4 / 2 / 2 / 1 / 10), `headline`, `index`.

**Animation.** `ticksToDo` (`CelCycler`, the fix that started this), `cycleSpeed` on `items`
(300), `theDiploma` (1), the newspaper's `End` cycler (1) and the ten winner stars
(5,4,6,5,4,6,5,4,6,5 — `MainViewModel.cs:1871`); `moveSpeed`/`setStep` for Willy (3/3) and the
ambulance (10/10); `view`, `loop`, `cel`, `nsLeft`, `nsTop`, `priority` as layout data
throughout `StoreLayout.cs`; the marble's path, `index`, `moveSpeed 1` and `global475 = 14`.

**Balloon.** The whole `BubbleWindow` geometry — `tNum`, `tailLeft/Top/Right/Bottom`, the
twelve-way tail switch, the short-balloon fixup, `vColor`, and the per-player
`Scheme(Loop, Back, Text)` standing in for `global513`/`global511`/`global512`
(`Main.sc:1148-1172`).

**Lip sync.** `Sync`'s `syncTime`/`syncCue`/`prevCue` are modelled exactly —
`LipSync.cs:56-72` parses the real `.sync` resources and takes `cue & 0x0F`, which is
`Sync.sc:81` verbatim. `syncNum` is declared and never read.

**Board.** `placeNum`, `index`, `topR`/`leftR`/`bottomR`/`rightR` (the hotspot rectangles are
the Place instances' own, `Board.cs:65-80`), `sNumber`.

**Fonts.** `theFont 10` (WButton), `font 1` (DText / `gUserFont`), `font 0` (DButton),
`font 4` (`goalsDefine textOne`), and the newspaper's font 3.

---

# 3. NOT MODELLED, DEAD

Each one proved by a whole-word search of the CD tree that finds no reader outside the
declaration itself.

| property | decls | proof |
| --- | ---: | --- |
| `placeX` / `placeY` | 14 each | `room1.sc:91-92` + 13 instances. **Zero** other occurrences in either tree. The Place's screen position is dead data; the marble is positioned from `marblePath`'s own coordinate table. |
| `gender` | 3 | `room1.sc:592` (Player, 0), `:1044` and `:1051` (player3 and player4, **1**). Zero readers. Players 3 and 4 are declared female and nothing in the game ever asks; the sprite comes from `whichBody`. |
| `looper` | 1 | `Actor.sc:275`. Read at `Motion.sc:199-200` and `marblePath.sc:70-71` — but **nothing ever assigns it**, so the "pick a loop from the heading" path never runs. Which makes `heading` (`Actor.sc:14`, set at `Motion.sc:198` and `marblePath.sc:68`) dead too. |
| `viewer` | 1 | `Actor.sc:276`. Read at `Actor.sc:297-299`; never assigned. |
| `avoider`, `baseSetter` | 1 each | `Actor.sc:273`, `:277`. Zero readers. |
| `blocks` | 2 | `Actor.sc:272` (0), `User.sc:25` (**1**). Zero readers. |
| `illegalBits` | 1 | `Actor.sc:267` = `$8000`. Its only send is `noticeRoom.sc:34` `illegalBits: 0`, and `noticeRoom` is one of the four unreferenced scripts. `Act::cantBeHere` returns 0 unconditionally (`Actor.sc:383-385`), so no control-line collision exists in this game. |
| `prevCel` | 1 | `FwdCount.sc:39`, on `RdmC`. `RdmC::changeCel` returns `(Random 0 (client lastCel:))` and never touches it. Moreover **`RdmC` is never instantiated** — its only occurrence in the tree is its own `(class RdmC of FwdCount` line. |
| `endCel` | 1 | `Motion.sc:103` on `CT`. Read inside `CT` only; no instance declares a value. |
| `b-moveCnt`, `b-i1`, `b-i2`, `b-di`, `b-xAxis`, `b-incr` | 6 | `Motion.sc:169-174`. Bresenham scratch owned by the `InitBresen`/`DoBresen` kernel calls. `b-moveCnt` is the one exception — `MarblePath` reuses it as its own step counter (`marblePath.sc:32`, `:43-45`), which the port models as the marble step loop. |
| `attributes` bits `$0004` and `$0080` | — | `Durable` is 69 = `$45` and `Consumable` is 129 = `$81` (`Goods.sc:18`, `:30`), but every `attributes` test in the tree is against `$0001`, `$0018`, `$0020`, `$0038`, `$0040` or `$0100`. Bits 2 and 7 are never read. |
| `muggedByMarket.newspaper` | — | `muggedByMarket.sc:164-174` — view 603, priority 10, `posn: 159 130`. It is only ever `dispose:`d (`:66`, `:130`); **`init:` is never called on it**, so the dropped-newspaper sprite is never drawn in either mugging. |
| `Talker cycleSpeed 10` | 1 | `WButton.sc:261`. `Talker::init` only ever calls `setCycle:` from the `(>= argc 2)` branch, which first does `ticksToDo: 0 cycleSpeed: 0` (`:270`); the cycler is always `MouthSync`, which sets its own `ticksToDo 1`. The declared 10 can never be reached. |
| `subtitleLang`, `parseLang`, `printLang` | 3 | `Game.sc`. Zero readers. |
| `syncNum` | 1 | `Sync.sc:22`. Zero readers. |
| `mapKeyToDir`, `echo`, `canInput`, `alterEgo`, `prevDir` | 5 | `User.sc`. `mapKeyToDir` is read at `User.sc:39` but only to gate a kernel call the port replaces; the rest have zero readers. |
| `carR`, `monR`, `hapR`, `eduR` | 4 | `room1.sc:601-604`. Zero readers — the goal *ratings* are recomputed, never stored. |
| `blueS`, `goldS`, `silverS`, `porkS`, `pennyS`, `tBillS` | 6 | Each read exactly once, in `Player::init` (`room1.sc:674-679`), to construct the `I` holding; nothing reads them back. The holdings are found by `indexNum` instead. |
| `agent`, `busy`, `animateObj`, `dataInc`, `frame`, `hMargin`, `vMargin`, `registerX`, `registerY`, `lastPlace`, `destIndex`, `firstMove`, `finalDest`, `theDirection` | 14 | Zero readers, or readers only inside the declaring class's own private bookkeeping. |

---

# 4. N/A — engine plumbing Avalonia replaces

Listed so nobody audits them again.

- **Window chrome / port state**: `-oldPort-`, `pPort`, `port`, `window`, `type`, `back`,
  `color`, `title`, `top`/`left`/`bottom`/`right`, `brTop`/`brLeft`/`brBottom`/`brRight`,
  `lsTop`/`lsLeft`/`lsBottom`/`lsRight`, `underBits`, `tailUnderbits`, `style`.
  (`style` — `Rm -1`, `noticeRoom 1`, `room1 2`, `introRoom 2` — is the *pic transition
  effect*; the port cuts. Cosmetic and one line to add later if wanted: `Game.sc:415`.)
- **Keyboard/joystick cursor warping**: the whole `KeyMouse` system —
  `listOfCoords`, `curItem`, `keyMouseX`, `keyMouseY`, `offsetX`, `offsetY`,
  `prevCursorX`, `prevCursorY`, plus the selectors `setCursor:` (74 sends), `setList:` (74),
  `advance:`, `retreat:`. `keyMouseX/Y` are *computed at runtime* from each control's centre
  (`Main.sc:993-1017`), so the declared values are storage, not layout.
- **List management**: `elements`, `size`, `mark`, `topString`, `cursor`, `max`,
  `register`, `register2`, `nHunk`, `Heap`.
- **Sound kernel state**: `handle`, `nodePtr`, `dataInc`, `min`, `sec`, `frame`, `vol 127`,
  `soundOn`, `nextLoop`, `nextNumber`, `prevSignal`, `owner`.
- **Event**: `claimed`, `message`, `modifiers`, `port`, `said`, `value`
  (`Item.value` is a last-drawn cache, not data — see §1.9).
- **Script/Timer**: `cycles`, `seconds`, `lastSeconds`, `caller`, `client`, `completed`,
  `cycleCnt`, `lastTime`, `count`, `state` on `Script` (−1).
- **Dialog**: `aTimeClock` (the *publication* slot; the clock itself is §1.4), `theItem`,
  `prevDialog`, `prevTalker`, `keyMouseList`, `tail`, `time`. (`busy` and `agent` are in §3 —
  declared on `Dialog` and read by nothing.)
- **`signal`** (`View 257 = $0101`): the port models the one bit any script reads, `$1000`
  (`CelCycler.FixCel`), and the visibility bit `$0008` via its own draw list. The rest —
  `$0001` stop-update, `$0002` start-update, `$0010` fixed priority, `$0800` fixed loop,
  `$4000`/`$8000` lifecycle — are interpreter bookkeeping.
- **`menuBarOK`** (35 instances, all 1): read once, `Interface.sc:1008`, to decide whether the
  menu bar stays live while a dialog is up. Belongs to the menu-bar port, not here.
- **`standard`** (35 instances, all 1) and **`front`/`hilite`** plumbing in `Interface.sc`.

---

# Appendix A — every property, with coverage

`readers` counts occurrences in the CD tree outside the `(properties …)` blocks; `port` is
case-insensitive whole-word matches under `src/` excluding `obj/` and `bin/` and is a hint
only. `floppy` marks names absent from `jones-dos-1.000.060`.

Six names exist only in the CD tree and are not simple renames:
`prevCue`, `syncCue`, `syncNum`, `syncTime` (the CD lip-sync), `tnx`, `topString`.
Eight more are the decompiler's abbreviations of floppy names and mean the same thing:
`finStat`/`finishStatus`, `latePay`/`latePayments`, `madePay`/`madePayment`,
`maxExper`/`maxExperience`, `playJones`/`playingAsJones`, `raises`/`raisesGiven`,
`whatNum`/`whichNumber`, `xcred`/`extraCredits`.

| property | decls | owners (first 3) | values seen (non-zero) | readers in scripts | port mentions | floppy |
| --- | ---: | --- | --- | ---: | ---: | --- |
| `actualName` | 5 | Player, player1, player2 (+2) | {} {string} | 5 | 15 | y |
| `adjustment` | 1 | EconomicIndex |  | 7 | 28 | y |
| `agent` | 1 | Dialog |  | 0 | 0 | y |
| `alterEgo` | 1 | User |  | 0 | 0 | y |
| `animateObj` | 1 | SysWindow |  | 0 | 0 | y |
| `aTimeClock` | 1 | Dialog |  | 19 | 0 | y |
| `attributes` | 3 | Goods, Consumable, Durable | 1 129 69 | 36 | 50 | y |
| `avoider` | 1 | Act |  | 0 | 0 | y |
| `back` | 3 | SysWindow, invisibleWindow, MyWindow | -1 41 7 | 13 | 158 | y |
| `backColor` | 1 | WButton | -1 | 2 | 0 | y |
| `bankBal` | 1 | Player |  | 10 | 31 | y |
| `bankBalHi` | 1 | Player |  | 8 | 0 | y |
| `basePrice` | 89 | refrigerator, freezer, stove (+63) | 10 100 102 11 110 12 124 125 ... | 17 | 38 | y |
| `baseSetter` | 1 | Act |  | 0 | 0 | y |
| `baseWage` | 1 | Player |  | 2 | 26 | y |
| `b-di` | 1 | Motion |  | 0 | 0 | y |
| `b-i1` | 1 | Motion |  | 0 | 0 | y |
| `b-i2` | 1 | Motion |  | 0 | 0 | y |
| `b-incr` | 1 | Motion |  | 0 | 0 | y |
| `blocks` | 2 | Act, User | 1 | 0 | 7 | y |
| `blueS` | 1 | Player |  | 1 | 0 | y |
| `b-moveCnt` | 1 | Motion |  | 7 | 0 | y |
| `bottom` | 1 | SysWindow |  | 36 | 58 | y |
| `bottomR` | 14 | Place, apartmentsP, rentOfficeP (+11) | 114 118 154 192 43 81 | 1 | 1 | y |
| `brBottom` | 2 | View, SysWindow | 190 | 2 | 1 | y |
| `brLeft` | 2 | View, SysWindow |  | 4 | 1 | y |
| `brRight` | 2 | View, SysWindow | 320 | 3 | 1 | y |
| `brTop` | 2 | View, SysWindow |  | 3 | 1 | y |
| `busy` | 1 | Dialog |  | 5 | 7 | y |
| `b-xAxis` | 1 | Motion |  | 0 | 0 | y |
| `caller` | 3 | Cycle, Motion, Script |  | 35 | 22 | y |
| `canInput` | 1 | User |  | 0 | 0 | y |
| `carGoal` | 1 | Player | 50 | 12 | 31 | y |
| `carR` | 1 | Player |  | 0 | 0 | y |
| `carStat` | 1 | Player |  | 14 | 24 | y |
| `cash` | 1 | Player | 200 | 37 | 255 | y |
| `cashHi` | 1 | Player |  | 14 | 2 | y |
| `cel` | 54 | View, background, gold (+42) | 1 10 13 2 3 4 5 6 ... | 136 | 329 | y |
| `celNum` | 37 | freezer, stove, colorTV (+28) | 1 10 11 12 13 14 15 16 ... | 6 | 16 | y |
| `claimed` | 1 | Event |  | 42 | 5 | y |
| `client` | 7 | Item, Dialog, Cycle (+4) |  | 176 | 28 | y |
| `color` | 2 | SysWindow, MyWindow | 200 | 5 | 18 | y |
| `completed` | 2 | Cycle, Motion |  | 11 | 0 | y |
| `consumables` | 1 | Player |  | 39 | 101 | y |
| `controls` | 2 | Rm, User |  | 38 | 7 | y |
| `count` | 2 | FS, FwdCount |  | 10 | 119 | y |
| `coursesDone` | 1 | Player |  | 3 | 8 | y |
| `curItem` | 1 | KeyMouse |  | 51 | 0 | y |
| `curRent` | 1 | Player | 325 | 21 | 214 | y |
| `cursor` | 2 | DEdit, DSelector |  | 7 | 0 | y |
| `cycleCnt` | 2 | Cycle, Timer | -1 | 20 | 5 | y |
| `cycleDir` | 1 | Cycle | 1 | 2 | 1 | y |
| `cycler` | 2 | Prop, DCIcon |  | 42 | 27 | y |
| `cycles` | 1 | Script |  | 128 | 28 | y |
| `cycleSpeed` | 21 | Prop, items, piggyBank (+13) | 1 10 300 5 6 | 14 | 18 | y |
| `dataInc` | 1 | Sound |  | 0 | 0 | y |
| `dependibility` | 37 | clerk, salesperson, electronicsRepair (+22) | 10 20 30 40 50 60 70 | 29 | 58 | y |
| `description` | 1 | Gauge |  | 4 | 1 | y |
| `destIndex` | 1 | MarblePath |  | 2 | 0 | y |
| `doorLoop` | 13 | Place, rentOfficeP, securityP (+10) | 1 10 11 12 2 3 4 5 ... | 1 | 0 | y |
| `doorX` | 14 | Place, apartmentsP, rentOfficeP (+11) | 105 144 20 220 221 277 282 288 ... | 3 | 0 | y |
| `doorY` | 14 | Place, apartmentsP, rentOfficeP (+11) | 105 113 147 179 181 183 29 30 ... | 6 | 0 | y |
| `durables` | 1 | Player |  | 142 | 105 | y |
| `dx` | 1 | Motion |  | 5 | 0 | y |
| `dy` | 1 | Motion |  | 8 | 0 | y |
| `echo` | 1 | User | 32 | 0 | 0 | y |
| `education` | 26 | electronicsRepair, manager, teller (+16) | 10 11 12 13 14 15 16 19 | 26 | 56 | y |
| `education2` | 6 | manager, broker, JobDItem (+3) | 11 13 14 16 | 3 | 1 | y |
| `eduCredit` | 1 | Player |  | 3 | 16 | y |
| `eduGoal` | 1 | Player | 50 | 26 | 32 | y |
| `eduR` | 1 | Player |  | 0 | 0 | y |
| `eduStat` | 1 | Player |  | 9 | 22 | y |
| `elements` | 1 | Collect |  | 34 | 1 | y |
| `endCel` | 1 | CT |  | 4 | 0 | y |
| `enrollments` | 1 | Player |  | 9 | 20 | y |
| `expCredit` | 1 | Player |  | 3 | 17 | y |
| `experience` | 32 | salesperson, electronicsRepair, manager (+20) | 10 20 30 40 50 60 70 | 14 | 55 | y |
| `finalDest` | 1 | MarblePath |  | 3 | 0 | y |
| `finStat` | 1 | Player |  | 8 | 15 | CD only |
| `firstMove` | 1 | MarblePath | 1 | 3 | 0 | y |
| `fixedPrice` | 11 | deposit, withdraw, loanPayment (+8) | 1 | 3 | 11 | y |
| `flashColor` | 4 | businessSuit, leisureSuit, casualClothes (+1) | 100 255 | 4 | 0 | y |
| `font` | 8 | textOne, DText, DButton (+5) | 1 4 | 28 | 98 | y |
| `frame` | 1 | Sound |  | 0 | 39 | y |
| `gender` | 3 | Player, player3, player4 | 1 | 0 | 0 | y |
| `goalValue` | 5 | StarSlider, wealthStar, happyStar (+2) | 1 | 5 | 2 | y |
| `goldS` | 1 | Player |  | 1 | 0 | y |
| `handle` | 2 | gamefile_sh, Sound |  | 9 | 1 | y |
| `hapGoal` | 1 | Player | 50 | 24 | 32 | y |
| `hapR` | 1 | Player |  | 0 | 0 | y |
| `hapStat` | 1 | Player |  | 8 | 114 | y |
| `heading` | 1 | Feature |  | 4 | 5 | y |
| `headline` | 9 | EconomicIndex, mainI, investIndex (+6) | 11 13 18 20 5 7 9 | 2 | 65 | y |
| `high` | 1 | EconomicIndex |  | 6 | 33 | y |
| `higher` | 1 | Gauge | {up} | 4 | 1 | y |
| `hMargin` | 1 | SysWindow | 4 | 0 | 0 | y |
| `illegalBits` | 1 | Act | $8000 | 1 | 0 | y |
| `index` | 16 | EconomicIndex, MarblePath, Place (+13) | 1 107 120 134 14 151 164 24 ... | 61 | 226 | y |
| `indexNum` | 95 | refrigerator, freezer, stove (+69) | 1 10 11 12 13 14 15 16 ... | 83 | 56 | y |
| `inputLineAddr` | 1 | User |  | 1 | 0 | y |
| `invAss` | 1 | Player |  | 9 | 12 | y |
| `invAssHi` | 1 | Player |  | 4 | 0 | y |
| `investments` | 1 | Player |  | 24 | 15 | y |
| `jobKey` | 1 | Player |  | 2 | 0 | y |
| `jobNum` | 40 | clerk, salesperson, electronicsRepair (+22) | 1 10 11 12 13 14 15 16 ... | 2 | 19 | y |
| `jobT` | 1 | Player | -1 | 3 | 0 | y |
| `key` | 151 | refrigerator, freezer, stove (+100) | 1 10 106 11 119 12 120 14 ... | 72 | 72 | y |
| `keyMouseList` | 1 | Dialog |  | 204 | 0 | y |
| `keyMouseX` | 15 | Item, Place, apartmentsP (+12) | 160 220 221 281 37 44 98 | 6 | 0 | y |
| `keyMouseY` | 15 | Item, Place, apartmentsP (+12) | 139 182 25 64 89 97 | 6 | 0 | y |
| `lastPlace` | 1 | MarblePath |  | 1 | 0 | y |
| `lastSeconds` | 2 | Dialog, Script |  | 4 | 0 | y |
| `lastTime` | 2 | Cycle, Timer | -1 | 14 | 14 | y |
| `latePay` | 1 | Player |  | 2 | 10 | CD only |
| `leaveOpen` | 1 | Player |  | 5 | 24 | y |
| `left` | 1 | SysWindow |  | 36 | 157 | y |
| `leftR` | 14 | Place, apartmentsP, rentOfficeP (+11) | 130 190 191 24 251 68 7 | 1 | 2 | y |
| `listOfCoords` | 1 | KeyMouse |  | 15 | 0 | y |
| `livesAt` | 1 | Player |  | 27 | 55 | y |
| `loanBal` | 1 | Player |  | 19 | 30 | y |
| `loop` | 118 | View, workButton, items (+90) | 1 10 13 2 3 4 5 6 ... | 45 | 289 | y |
| `looper` | 1 | Act |  | 7 | 0 | y |
| `low` | 1 | EconomicIndex |  | 6 | 46 | y |
| `lower` | 1 | Gauge | {down} | 4 | 10 | y |
| `lowerRange` | 1 | EconomicIndex |  | 5 | 13 | y |
| `lqAss` | 1 | Player | 200 | 9 | 16 | y |
| `lqAssHi` | 1 | Player |  | 7 | 0 | y |
| `lsBottom` | 2 | View, Item |  | 3 | 0 | y |
| `lsLeft` | 2 | View, Item |  | 3 | 0 | y |
| `lsRight` | 2 | View, Item |  | 3 | 0 | y |
| `lsTop` | 2 | View, Item |  | 3 | 0 | y |
| `madePay` | 1 | Player |  | 4 | 10 | CD only |
| `mapKeyToDir` | 1 | User | 1 | 1 | 0 | y |
| `mark` | 1 | DSelector |  | 10 | 4 | y |
| `max` | 1 | DEdit |  | 2 | 62 | y |
| `maxExper` | 1 | Player | 10 | 6 | 25 | CD only |
| `maximum` | 1 | Gauge | 15 | 5 | 4 | y |
| `menuBarOK` | 35 | appliance, applianceJobs, bank (+32) | 1 | 1 | 2 | y |
| `message` | 1 | Event |  | 100 | 8 | y |
| `min` | 1 | Sound |  | 4 | 69 | y |
| `minDepend` | 1 | Player |  | 9 | 32 | y |
| `minimum` | 1 | Gauge |  | 5 | 4 | y |
| `mode` | 1 | DText |  | 12 | 55 | y |
| `modifiers` | 1 | Event |  | 3 | 0 | y |
| `monGoal` | 1 | Player | 50 | 23 | 34 | y |
| `monR` | 1 | Player |  | 0 | 0 | y |
| `monStat` | 1 | Player |  | 11 | 31 | y |
| `mover` | 1 | Act |  | 30 | 6 | y |
| `moveSpeed` | 10 | Act, lottobuck1, lottobuck2 (+7) | 1 3 | 7 | 28 | y |
| `nakedCount` | 1 | Player |  | 9 | 20 | y |
| `name` | 1 | gamefile_sh | {gamefile.sh} | 14 | 243 | y |
| `needEd1` | 1 | Player |  | 1 | 13 | y |
| `needEd2` | 1 | Player |  | 1 | 13 | y |
| `netWorth` | 1 | Player | 200 | 6 | 30 | y |
| `netWorthHi` | 1 | Player |  | 5 | 1 | y |
| `nextLoop` | 1 | Sound | -1 | 3 | 0 | y |
| `nextNumber` | 1 | Sound |  | 5 | 0 | y |
| `nodePtr` | 1 | Sound |  | 3 | 0 | y |
| `normal` | 1 | Gauge | 7 | 4 | 11 | y |
| `notEnoughEd` | 1 | Player |  | 9 | 28 | y |
| `nsBottom` | 37 | View, appliance, applianceJobs (+34) | 119 | 56 | 7 | y |
| `nsLeft` | 248 | View, refrigerator, freezer (+173) | 1 10 100 102 104 105 106 108 ... | 136 | 55 | y |
| `nsRight` | 37 | View, appliance, applianceJobs (+34) | 184 | 63 | 7 | y |
| `nsTop` | 241 | View, refrigerator, freezer (+160) | 1 100 101 108 14 15 160 17 ... | 109 | 56 | y |
| `number` | 5 | Rm, aSong, aSoundEffect (+2) | 6 | 10 | 95 | y |
| `occupation` | 1 | Player |  | 5 | 18 | y |
| `offsetX` | 2 | Item, Place |  | 1 | 0 | y |
| `offsetY` | 2 | Item, Place |  | 1 | 0 | y |
| `-oldPort-` | 3 | Item, InvisibleWindow, KeyMouse |  | 6 | 0 | y |
| `owner` | 4 | aSong, aSoundEffect, aSoundEffect2 (+1) | -1 | 4 | 10 | y |
| `parseLang` | 1 | Game | 1 | 0 | 0 | y |
| `paySched` | 1 | Player |  | 7 | 14 | y |
| `pennyS` | 1 | Player |  | 1 | 0 | y |
| `picture` | 2 | Rm, room1 | 11 | 3 | 26 | y |
| `placeNum` | 13 | Place, rentOfficeP, securityP (+10) | 1 10 11 12 2 3 4 5 ... | 9 | 149 | y |
| `placeX` | 14 | Place, apartmentsP, rentOfficeP (+11) | 11 134 190 201 25 256 257 258 ... | 0 | 0 | y |
| `placeY` | 14 | Place, apartmentsP, rentOfficeP (+11) | 133 134 141 151 2 3 32 4 ... | 0 | 0 | y |
| `playing` | 2 | Player, player1 | 1 | 95 | 52 | y |
| `playJones` | 1 | Player |  | 4 | 2 | CD only |
| `porkS` | 1 | Player |  | 1 | 0 | y |
| `port` | 1 | Event |  | 11 | 175 | y |
| `pPort` | 2 | BubbleWindow, MyWindow |  | 4 | 0 | y |
| `preReq` | 10 | UniversityDIcon, electronics, preEngineering (+7) | 10 12 14 16 17 18 19 | 2 | 7 | y |
| `prevCel` | 1 | RdmC | -1 | 0 | 0 | y |
| `prevCue` | 1 | Sync | -1 | 5 | 1 | CD only |
| `prevCursorX` | 1 | KeyMouse |  | 4 | 0 | y |
| `prevCursorY` | 1 | KeyMouse |  | 4 | 0 | y |
| `prevDialog` | 1 | Dialog |  | 136 | 0 | y |
| `prevDir` | 1 | User |  | 1 | 0 | y |
| `prevSignal` | 1 | Sound |  | 2 | 0 | y |
| `prevTalker` | 1 | Dialog |  | 30 | 0 | y |
| `price` | 9 | deposit, withdraw, loanPayment (+6) | 1 10 100 325 50 | 130 | 170 | y |
| `pricePaid` | 1 | Goods |  | 11 | 50 | y |
| `printLang` | 1 | Game | 1 | 0 | 0 | y |
| `priority` | 164 | View, background, exitButton (+120) | 1 -1 10 12 13 14 15 2 ... | 52 | 21 | y |
| `prompt` | 1 | User | {string} | 2 | 4 | y |
| `quantity` | 1 | Goods | 1 | 80 | 147 | y |
| `raises` | 1 | Player |  | 6 | 33 | CD only |
| `reading` | 1 | EconomicIndex |  | 15 | 155 | y |
| `redemptionPrice` | 1 | Durable |  | 6 | 25 | y |
| `register` | 1 | Script |  | 65 | 58 | y |
| `register2` | 1 | Script |  | 10 | 0 | y |
| `registerX` | 1 | WList |  | 0 | 0 | y |
| `registerY` | 1 | WList |  | 0 | 0 | y |
| `relax` | 1 | Player | 25 | 17 | 65 | y |
| `rentExt` | 1 | Player |  | 4 | 18 | y |
| `rentOwed` | 1 | Player |  | 16 | 50 | y |
| `right` | 1 | SysWindow |  | 38 | 86 | y |
| `rightR` | 14 | Place, apartmentsP, rentOfficeP (+11) | 128 129 190 250 311 67 | 1 | 1 | y |
| `risk` | 5 | EconomicIndex, gldIndex, silIndex (+2) | 1 10 2 4 | 2 | 31 | y |
| `said` | 1 | Item |  | 2 | 2 | y |
| `script` | 6 | Prop, Game, Rm (+3) |  | 143 | 186 | y |
| `sec` | 1 | Sound |  | 4 | 0 | y |
| `seconds` | 3 | Dialog, Script, Timer | -1 | 46 | 45 | y |
| `shadowColor` | 109 | refrigerator, freezer, stove (+75) | 102 107 112 115 116 6 65 80 ... | 4 | 0 | y |
| `shares` | 1 | I |  | 13 | 40 | y |
| `signal` | 4 | View, Prop, DCIcon (+1) | 257 | 45 | 10 | y |
| `silverS` | 1 | Player |  | 1 | 0 | y |
| `size` | 1 | Collect |  | 100 | 42 | y |
| `sNumber` | 14 | Place, apartmentsP, rentOfficeP (+11) | 200 201 202 203 204 205 206 207 ... | 1 | 1 | y |
| `soundOn` | 1 | Sound | 1 | 5 | 4 | y |
| `standard` | 35 | appliance, applianceJobs, bank (+32) | 1 | 5 | 2 | y |
| `start` | 1 | Script |  | 1 | 109 | y |
| `state` | 109 | exitButton, workButton, jobsAvailable (+61) | 1 -1 163 256 257 288 289 3 ... | 119 | 180 | y |
| `style` | 3 | Rm, noticeRoom, room1 | 1 -1 2 | 3 | 2 | y |
| `subtitleLang` | 1 | Game |  | 0 | 0 | y |
| `syncCue` | 1 | Sync | -1 | 4 | 0 | CD only |
| `syncNum` | 1 | Sync | -1 | 0 | 0 | CD only |
| `syncTime` | 1 | Sync | -1 | 4 | 0 | CD only |
| `tail` | 1 | Dialog |  | 0 | 33 | y |
| `tailBottom` | 1 | BubbleWindow |  | 8 | 7 | y |
| `tailLeft` | 1 | BubbleWindow |  | 12 | 11 | y |
| `tailRight` | 1 | BubbleWindow |  | 8 | 7 | y |
| `tailTop` | 1 | BubbleWindow |  | 13 | 12 | y |
| `tailUnderbits` | 1 | BubbleWindow |  | 4 | 0 | y |
| `tBillS` | 1 | Player |  | 1 | 11 | y |
| `text` | 122 | refrigerator, freezer, stove (+88) | {_____________________________} {Atlas.................|} {Bank} {Butcher___________||||} {Checker____________|} {Cheeseburger.......|} {Clerk_______________|||} {Clerk_______________|} ... | 92 | 294 | y |
| `textColor` | 43 | refrigerator, freezer, stove (+34) | 26 27 37 39 | 6 | 0 | y |
| `theDirection` | 1 | MarblePath |  | 3 | 0 | y |
| `theDurable` | 1 | CostDItem |  | 17 | 2 | y |
| `theFont` | 1 | WButton | 10 | 3 | 1 | y |
| `theItem` | 1 | Dialog |  | 80 | 1 | y |
| `theSign` | 2 | withdraw, CostDItem | 1 -1 | 3 | 1 | y |
| `ticksToDo` | 8 | Prop, DCIcon, Cycle (+5) | 1 -1 10 8 | 13 | 53 | y |
| `time` | 1 | Dialog |  | 9 | 82 | y |
| `timer` | 3 | Prop, Rm, Script |  | 12 | 25 | y |
| `title` | 1 | SysWindow |  | 2 | 57 | y |
| `tNum` | 1 | BubbleWindow |  | 21 | 24 | y |
| `tnx` | 1 | Player |  | 2 | 0 | CD only |
| `top` | 1 | SysWindow |  | 36 | 99 | y |
| `topR` | 14 | Place, apartmentsP, rentOfficeP (+11) | 10 119 133 155 158 44 69 82 | 1 | 1 | y |
| `topString` | 1 | DSelector |  | 3 | 1 | CD only |
| `triedExt` | 1 | Player |  | 20 | 40 | y |
| `turnedOver` | 1 | Player |  | 5 | 20 | y |
| `type` | 13 | BubbleWindow, SysWindow, Item (+10) | 1 129 2 3 4 6 7 | 95 | 38 | y |
| `typeOfGoods` | 32 | deposit, withdraw, loanPayment (+27) | 1 2 3 4 | 1 | 5 | y |
| `underBits` | 5 | View, BubbleWindow, Item (+2) |  | 22 | 0 | y |
| `uniform` | 23 | salesperson, manager, teller (+12) | 34 35 36 | 8 | 53 | y |
| `units` | 15 | businessSuit, leisureSuit, casualClothes (+10) | 1 10 11 13 2 4 9 | 3 | 11 | y |
| `unitsToGraduate` | 1 | Educational | 10 | 10 | 24 | y |
| `upperRange` | 1 | EconomicIndex |  | 3 | 7 | y |
| `value` | 1 | Item |  | 69 | 133 | y |
| `vColor` | 2 | BubbleWindow, MyWindow | 100 | 12 | 2 | y |
| `view` | 209 | View, background, exitButton (+125) | 1 10 100 11 2 250 270 3 ... | 59 | 209 | y |
| `viewer` | 1 | Act |  | 5 | 0 | y |
| `visitTime` | 4 | JobDItem, newspaper, UniversityDIcon (+1) | 1 4 6 | 4 | 11 | y |
| `vMargin` | 1 | SysWindow | 4 | 0 | 0 | y |
| `vol` | 1 | Sound | 127 | 2 | 0 | y |
| `wage` | 1 | Player |  | 42 | 146 | y |
| `wearing` | 1 | Player | 36 | 8 | 20 | y |
| `whatNum` | 1 | Player |  | 12 | 6 | CD only |
| `whichBody` | 1 | Player |  | 18 | 2 | y |
| `width` | 1 | WButton |  | 19 | 102 | y |
| `window` | 2 | SysWindow, Dialog |  | 72 | 36 | y |
| `worksAt` | 1 | Player |  | 59 | 56 | y |
| `x` | 30 | Feature, DSelector, invSelector (+27) | -1 114 119 124 129 144 159 160 ... | 85 | 395 | y |
| `xcred` | 1 | Player |  | 9 | 15 | CD only |
| `xLast` | 2 | Act, Motion |  | 8 | 0 | y |
| `xStep` | 1 | Act | 3 | 2 | 0 | y |
| `y` | 30 | Feature, DSelector, invSelector (+27) | -1 114 134 146 149 154 155 180 ... | 75 | 138 | y |
| `yLast` | 2 | Act, Motion |  | 8 | 0 | y |
| `yStep` | 1 | View | 2 | 2 | 0 | y |
| `z` | 1 | Feature |  | 1 | 57 | y |

# Appendix B - behaviour and presentation selectors

Sends counted across the CD tree; port mentions as above.

| selector | sends | port mentions |
| --- | ---: | ---: |
| `addToPic:` | 19 | 2 |
| `advance:` | 20 | 39 |
| `cel:` | 112 | 329 |
| `center:` | 2 | 2 |
| `changeCel:` | 2 | 1 |
| `checkAni:` | 1 | 0 |
| `checkState:` | 6 | 1 |
| `closeDoor:` | 1 | 0 |
| `cue:` | 71 | 45 |
| `curItem:` | 45 | 0 |
| `cycle:` | 7 | 51 |
| `cycleDone:` | 5 | 4 |
| `cycleSpeed:` | 14 | 18 |
| `delete:` | 43 | 16 |
| `dispose:` | 199 | 13 |
| `doDisplay:` | 2 | 0 |
| `doFormat:` | 6 | 9 |
| `doit:` | 242 | 116 |
| `draw:` | 174 | 42 |
| `drawPic:` | 1 | 1 |
| `enable:` | 16 | 0 |
| `endTurn:` | 1 | 43 |
| `erase:` | 48 | 1 |
| `fade:` | 25 | 38 |
| `forceUpd:` | 9 | 0 |
| `hide:` | 20 | 1 |
| `hilite:` | 5 | 1 |
| `hiliteControl:` | 8 | 0 |
| `illegalBits:` | 1 | 0 |
| `init:` | 269 | 97 |
| `isStopped:` | 1 | 0 |
| `lastCel:` | 9 | 17 |
| `localize:` | 7 | 0 |
| `loop:` | 29 | 289 |
| `motionCue:` | 9 | 0 |
| `move:` | 8 | 30 |
| `moveSpeed:` | 7 | 28 |
| `moveTo:` | 53 | 27 |
| `new:` | 56 | 981 |
| `nextCel:` | 5 | 13 |
| `open:` | 42 | 46 |
| `openDoor:` | 1 | 2 |
| `pack:` | 6 | 5 |
| `pause:` | 30 | 35 |
| `play:` | 103 | 91 |
| `playBed:` | 17 | 10 |
| `posn:` | 41 | 15 |
| `release:` | 42 | 22 |
| `retreat:` | 8 | 0 |
| `select:` | 34 | 31 |
| `set60ths:` | 6 | 0 |
| `setCel:` | 47 | 11 |
| `setCursor:` | 74 | 0 |
| `setCycle:` | 73 | 30 |
| `setDirection:` | 1 | 3 |
| `setList:` | 74 | 0 |
| `setLoop:` | 60 | 19 |
| `setMotion:` | 35 | 15 |
| `setPri:` | 37 | 3 |
| `setScript:` | 55 | 6 |
| `setSize:` | 102 | 13 |
| `setSpeed:` | 1 | 0 |
| `setStep:` | 15 | 9 |
| `setTextSize:` | 2 | 0 |
| `show:` | 10 | 12 |
| `signal:` | 7 | 10 |
| `startTurn:` | 2 | 44 |
| `startUpd:` | 6 | 0 |
| `stop:` | 9 | 109 |
| `stopUpd:` | 27 | 0 |
| `ticksToDo:` | 6 | 53 |
| `triedToMove:` | 1 | 0 |
| `update:` | 2 | 0 |
| `view:` | 30 | 209 |
