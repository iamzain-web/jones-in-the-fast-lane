# Unbuilt screens — source specs

Specs for screens the port has not built, read from the scripts so the work can start without
re-deriving them. Every number below is cited; nothing here is inferred.

Standing rules in `CLAUDE.md` apply — in particular, take **strings** from the raw resources
(`assets/raw/text/<n>.text`, `assets/raw/script/<n>.script`), never from the decompiled listing.

---

## Who's Winning — `viewGoals`, script 238  — **BUILT**

Built in `MainViewModel.BuildWhosWinning`. The spec below is kept as the citation trail; the
"still to derive" question at the end is answered underneath it.

Reached from the middle mouse button or Ctrl-left click (text 997[7], the game's own help
screen), and from F6 (`Menu.sc:390-395`, menu id 1026).

**Dialog:** `window: global38` (the INVISIBLE window), `moveTo: 69 44`. So the town board stays
behind it, as with the weekend, newspaper and broker. Closing does the usual
`Graph grFILL_BOX` wipe (`viewGoals.sc:191`).

**Backdrop:** `background` DIcon, **view 505** (`:199-201`).

**Four player columns, 44 px apart.** Each column is a thermometer, a fine thermometer, a player
number token and a percentage caption:

| Instance | view | loop | cel | nsTop | nsLeft |
|---|---|---|---|---|---|
| `therm1..4` | 505 | 1 | — | 35 | 21 / 65 / 109 / 153 |
| `fineTherm1..4` | 505 | 2 | — | 83 | 22 / 66 / 110 / 154 |
| `playerNumber1..4` | 505 | 3 | 0 / 1 / 2 / 3 | 90 | 18 / 62 / 106 / 150 |
| `percent1..4` | (text) | — | — | 25 | 14 / 58 / 102 / 146 |

`exitButton`: ErasableDIcon, view 250, nsTop 108, nsLeft 143 (`:206-211`) — the same exit
position every dialog in the game uses.

Columns are added only for players in the game: `viewGoals.sc:84-118` adds column 1 with the
background, then 2, 3 and 4 behind their own guards, then the exit button last.

**How the columns are driven** (`viewGoals.sc:84-117`), one percentage `pct` per player from
`localproc_0`:

```
therm.cel       = pct / 10                 ; loop 1, eleven cels, empty to full
fineTherm.nsTop = 83 - (therm.cel * 5)     ; the fine marker rides up as the bar fills
fineTherm.cel   = (pct mod 10) / 2         ; loop 2, five sub-steps
percent.value   = pct                      ; text 238[0] "%3d~", font 4, colour 0
playerNumber.cel = player whatNum:         ; the player's INDEX, or 4 for Jones
```

`whatNum` is assigned by `players::init` (`room1.sc:960-965`) after the non-playing players are
deleted from the list, so it is the seat number and not the body the player picked. `~` is the
percent sign in font 4 (glyph 0x7E).

**`localproc_0`** (`viewGoals.sc:22-59`): each of monStat/hapStat/eduStat/carStat is clamped to
its own goal first, so overshooting one goal cannot cover for neglecting another. Then

```
stats = sum of the four clamped stats;  goals = sum of the four goals
pct   = stats == 0 ? 0
      : (stats * 50 / goals) * 2 + ((stats * 50 mod goals) * 2 / goals)
pct   = min(pct, 100)                      ; :55-57, unreachable after the clamps
```

That is `stats * 100 / goals` rearranged so no intermediate leaves a signed 16-bit word: four
goals of 100 make the naive numerator 40000, past 32767.

**Centring** (`viewGoals.sc:119-153`): every element of every shown column shifts RIGHT by 65 for
1 player, 42 for 2, 20 for 3. There is no case 4 — four players sit where the instances declare
them.

**The Jones token** (`viewGoals.sc:135-137`): in the 2-player case, if player 2 has `playJones`,
`playerNumber2.cel` is forced to 4. This is redundant and there is no equivalent in the 3- or
4-player cases: `select4.sc:26` sets `playJones` on the same player whose `playing` it has
already set to 29 (`select4.sc:268`), so `players::init` has already made `whatNum` 4.

**No `x` key.** Every other dialog's exit button carries `key 120` beside its `state 419`
(`newspaper.sc:187`, `weekend.sc:268`); `viewGoals.sc:206-213` has the state and no key. Esc
closes it, via `Dialog::handleEvent` (`Interface.sc:1121-1124`).

**Not built:** each `playerNumber` is an ErasableDIcon whose `doit` opens `select3` (script 236)
in mode 2 — a read-only view of that player's own goal sliders (`viewGoals.sc:226-235`). That
mode of select3 is unported, so the tokens are drawn and inert.

---

## Stats / Net Worth — `inventories`, script 231  — **BUILT**

Built in `MainViewModel.BuildStats` / `StatsRecords`. The spec below stands, with one thing it
did not say: `invSelector` is `x 25 y 7`, so the buffer is a run of exactly-25-CHARACTER records
and the widget shows seven of them. Every one of text 231's templates fills to exactly 25, which
is what makes that work. Font 4 is proportional, so the columns line up by character count and
not by pixel — that is the original's own look, not an approximation.


Reached from the right mouse button or Shift-left click (text 997[7]).

**Dialog:** `window: global38` again, `moveTo: 69 44` (`inventories.sc:176-182`). Contents are
`background`, `invSelector` and `doneButton`; the selector's text is placed with
`moveTo: 17 20` (`:178`).

**The whole screen is ONE string.** `inventories.sc:34-138` builds it by appending `Format`
calls into a single buffer (`StrEnd` walks to the end each time) and hands it to a `DSelector`,
which is why it scrolls. Every label lives in **text resource 231** and every one carries a
width specifier, so the columns line up by padding rather than by positioning:

| 231 index | Shape | Source of the value |
|---|---|---|
| 0 | `%=25s` | centred heading — used for the name and for each section title |
| 1 | `Works at %-16s` | text 700 at `worksAt + 71` |
| 2 | `As a %-20s` | text 700 at `occupation` |
| 3 | `Hourly wage: $%-11d` | `wage` |
| 4, 5 | `%-25s` | section rule |
| 6, 7 | `Cash: $%-18s`, `Savings: $%-15s` | formatted as strings, not ints |
| 8, 9 | `Rent Owed: $%-13d`, `Loan Balance: $%-10d` | |
| 10 | `Total Goods: $%-11d` | computed total |
| 11, 12 | `Investment Total: $%-6s`, `Net Worth: $%-13s` | |
| 13-16 | headings via index 0 | |
| 17 | `%3d Weeks of Food` | the food consumable's quantity |
| 18 | `%3d %-21s` | quantity and item name |
| 19, 20 | headings via index 0 | |

**Note for the formatter:** these use `%=25s` for CENTRED, alongside the `%-Ns` left-align the
port already handles. `Audio.Subtitles.For(int, object[])` currently implements `%d`, `%s`,
width and `-`; it does **not** implement `=`. Add it there rather than writing a second
formatter, since both are SCI's `Format`.

The location and occupation names come from text 700 — which is also what `STRINGS.md` found the
port had wrong in `Board.cs` and `Jobs.cs`, now corrected. This screen is the reason those names
matter: it is the one place the game prints them.

---

## Also unbuilt, specs not yet gathered

- `diploma` (230) — in progress.

---

## Save and restore — `Save`, script 990 — **BUILT**

**THERE IS NO DIALOG.** This entry is here because looking for one wastes an afternoon.
Script 990 declares no `Dialog`, no `DIcon`, no view/loop/cel, no file list and no name
field. It is four procedures that call the interpreter and print the answer:

| Procedure | What it is |
|---|---|
| `proc990_0` (`:17-40`) | `(SaveGame (gGame name:) 1 (gGame name:) @global539)` |
| `proc990_1` (`:42-71`) | `GetSaveFiles` as an existence test, then `CheckSaveGame` and `RestoreGame` |
| `proc990_2` (`:73-109`) | Ctrl-Y, the save-directory `#edit` box |
| `proc990_3` (`:111-147`) | the floppy-swap prompt, text 990[6] |

**One slot.** All three kernel calls pass the literal save number **1**, and the description
is always the game name. That is why the Save menu item has to warn first — text 997[11],
"Saving a game will overwrite a previously saved game. Continue?".

**Every message is a plain `Print`,** and therefore a TAIL-LESS, CENTRED balloon: `Print`
defaults its tail number to 0 (`Interface.sc:49-58`) and only keyword 318 sets one, which
none of these calls passes. `Menu.sc` puts the default colour scheme up first with
`(proc0_17 0)` (`:220`, `:233`) — `Main.sc:1166-1170`, the cream box with red text.

| Where | Text | `#width` | `#font` | Buttons |
|---|---|---|---|---|
| Save asked (`Menu.sc:221`) | 997[11] | 180 | — (font 1) | `{YES}` 1, `{NO}` 0 |
| Saved (`Save.sc:33`) | 990[1] | — (100) | — (font 1) | none, `#time global426` |
| Disk full (`Save.sc:29`) | 990[0] | 250 | 0 | `{OK}` 1 |
| Restore asked (`Menu.sc:234`) | 997[12] | 150 | — (font 1) | `{YES}` 1, `{NO}` 0 |
| Cannot restore (`Save.sc:60`) | 990[2] | 200 | 0 | `{OK}` 1 |
| Nothing saved (`Save.sc:64`) | 990[3] | 150 | 0 | `{OK}` 1 |
| Restart asked (`Menu.sc:201`) | 997[8] | — (100) | — (font 1) | `{Yes}` 1, `{No}` 0 |

Note the capitals: Save, Restore and Quit use `{YES}`/`{NO}`; Restart uses `{Yes}`/`{No}`.
Both pairs are separate literals in script 997's string table.

**Routes in.** F5 → menu id 513 (`Menu.sc:218-226`) → the warning → `(gGame save:)`. F7 →
id 515 (`:231-239`) → the question → `global529 = 1`, which `Main::doit`
(`Main.sc:1235-1238`) turns into `(gGame restore:)`. F9 → id 518 (`:199-210`) →
`(gGame restart:)` → `RestartGame`, after which `Main::init` sees `GameIsRestarting` and
goes to `newRoom: 1` instead of the notice room (`Main.sc:1213-1226`) — back to the front
of the game. **The main menu's Restore button asks nothing:** `select1.sc:96-110` is
`(super doit:)` then `(= global529 1)`, so it goes straight to `proc990_1`.

Save and Restore are live exactly when the board is: `room1.sc:1296-1300` enables menu items
513 and 515 in the same block as the Goals screen (1026), and `Main.sc:985-991` switches 513
off and on around the turn-start `Print`s.

**Not ported: Ctrl-Y, Set Save Directory.** `proc990_2` is a `Print` with a 29-character
`#edit` field validated by `ValidPath`, so a 1990 player could put saves on a second floppy.
This port writes one slot to the platform's application-data directory and has no
text-entry widget; text 990[4], 990[5] and 990[6] are unused.

**The FORMAT is not the original's and cannot be** — `SaveGame` snapshots the SCI heap. The
port writes versioned JSON; see `Jones.Core/Save/`.

---

## Intro — `introRoom`, script 2  — **BUILT**

Built in `MainViewModel.IntroStates` / `BuildIntro`.

**The table below is not the whole story and was misleading.** The repeats are not a pic being
redrawn for its own sake: each state also changes the LOOP of an overlay `View` — `littlepicN`, a
photograph at (162,160), and `littlenameN`, a name plate at (160,56), or a top plate at (160,50)
and a bottom one at (165,178) for pics 4 and 5. The `DrawPic` is there because the overlays were
`addToPic:`'d and the pic has to be repainted underneath them. States 8, 10 and 12 change only the
name plate's CEL and leave the photograph alone. State 19 is a voice-talent roll of twelve cels
(view 5 loop 4, at `15n + 30`) under a heading (loop 3, at (160,11)). Read `:56-260`, not this
table.

| Pic | Transition | Repeats | Lines |
|---|---|---|---|
| 0 | 3 | 1 | `:59` |
| 1 | 2 | 3 | `:66-78` |
| 2 | 3 | 3 | `:90-103` |
| 3 | 2 | 6 | `:115-146` |
| 4 | 3 | 3 | `:158-172` |
| 5 | 2 | 4 | `:187-213` |

The second argument to `DrawPic` is the transition style, and it alternates 3 / 2 / 3 / 2 / 3 / 2
between pics. A `View` is created and added at `:216-217` over the top.

This is also the screen the main menu draws over — `TitleBackdrop()` in the port already puts
pic 0 behind the setup dialogs, which is why the menu is not on black.

---

## Goal definitions help — `goalsDefine`, script 229  — **BUILT**

Built in `MainViewModel.BuildGoalsHelp`. Two corrections to the table below: there are FOUR corner
pieces, not two (`corner3` at nsTop 88 and `corner4` at 147/88), and every one of them plus
`theTitle` takes `cel: local0`, so the whole frame changes with the page. The four paragraphs are
string literals in the SCRIPT — there is no text resource 229 — at raw offsets 0x05AA, 0x0660,
0x071C and 0x07C6; the audio is clips 590..593.

The `?` from the goal-setting screen. Explains what Wealth, Happiness, Education and Career mean.
Both the text and the audio exist in the resources.

**Dialog:** `window: global38`, `moveTo: 69 44` (`:76-89`), usual `grFILL_BOX` wipe on close
(`:134`).

| Instance | view | loop | nsTop | nsLeft |
|---|---|---|---|---|
| `background` | 501 | 4 | — | — |
| `doneButton` | 250 | 2 | 108 | 106 |
| `rightArrow` | 250 | 8 | 108 | 47 |
| `corner1` | 501 | 6 | — | — |
| `corner2` | 501 | 6 | — | 147 |

The `rightArrow` means this is a **paged** help screen — one goal at a time, not all four at
once. Read `:59-133` for how the page advances and which text index each page uses before
building it.
---

## Wild Willy — `muggedByMarket`, script 114  — **BUILT**

Built in `MainViewModel.StartWillyWalk`. Corrections: the `newspaper of Act` instance at `:164-174`
is never `init:`'d — state 5 only `dispose:`s it — so it is dead code; the paper that appears is
the real newspaper dialog, `((ScriptID 215 0) init: 0)` with `global415` set to 16. The two scripts
walk DIFFERENT paths, listed in `WillyAtBank` and `WillyAtMarket`. The rolls happen on ARRIVAL
(`bank.sc:120-122`, `market.sc:142-144`, both gated on week >= 4 and cash > 0) and the cash is
zeroed on the way OUT, which is what makes banking before you leave work.


Two separate Script instances, not one shared routine: `muggedByMarket` (`:22`) and
`muggedByBank` (`:86`). Both run the same shape:

- `gASoundEffect play: 20` — the mugging sting (`:31`, `:95`)
- `global415 = 16` — headline 16, the street mugging, so it makes the next paper (`:62`, `:126`)
- `global479 = register`, `global473 = 1` — hands control back (`:69-70`, `:133-134`)
- `global401 = ((ScriptID 300) doit: global302)` — re-plans the computer player's turn, because
  being robbed invalidates whatever it was about to do (`:72`, `:136`)

**Actors:** `willy of Act` view **340** (`:150-152`); `newspaper of Act` view **603** (`:164-166`)
— the same paper view the newspaper screen uses, flown in to announce it.

Odds are on the way OUT, not on arrival: 1-in-31 leaving the Bank, 1-in-51 leaving Black's
Market. `GATES.md` records that the port has this wired to arrival, which is wrong on both
counts — it is unimplemented *and* hooked to the wrong event.

The mugging zeroes **cash only**; savings are untouched (§19). That is the whole reason the Bank
exists as a safety mechanic.

---

## The win sequence — `winnerScript`, script 234  — **BUILT**

Built in `MainViewModel.BuildWinner`. The notes below were wrong in three places, so read the
script: there are **ten** stars (`star1`..`star10`, `:197-345`), not five; `setLoop: (+ loop 2)` at
`:176` is on the CONFETTI, which is a fresh `Prop` per burst, not on `jonesGuy`; and the sequence
runs BEFORE the weekend, because `startTrn` state 0 sets it and returns. It also has a podium the
notes do not mention: `background1` (view 0 loop 0 cel 4) at (69,44), `background2` (the winner's
colour panel) at (70,45), `pedistal` (view 609 loop 0) at (160,154) and `theWinner` in the walker's
own view at (160,149), with the placing from text 234 at (137,148) in font 10.


Winning is already detected (`TurnStart` state 0, checked at turn start so you always play the
turn you won on). Nothing is shown.

- `gASong play: 7` — the victory theme (`:69`)
- `jonesGuy of Act` view **609** (`:159-161`), with `setLoop: (+ loop 2)` at `:176`
- `confetti of Prop` view 609 (`:187-189`)
- `star1` … `star5` of Prop, view 609 **loop 1** (`:197-262`)
- Placement is deliberately random: `(Random 0 1)` chooses at `:73` and `:86`, and priority is
  `(+ 2 (* (Random 0 1) 3))` at `:77` and `:89` — so the stars land in front of or behind the
  figure on each run.

Read `:21-158` for the cue order before building; the animation is a scripted sequence rather
than a static screen.
- Modal `Print` with buttons — built, and wired to all four: the loan, the pawn offer, enrolment
  and the rent office's two (`TALKER.md` §7a). Each is ONE balloon carrying the spoken line and
  the buttons together. The CD build splits them — it speaks the sentence and prints a short
  prompt from its own text resource — but the FLOPPY build prints the whole thing with the
  buttons on it in a single `Print` (`jones-dos-1.000.060/src/university.sc:746`, `bank.sc:474`,
  `pawnShop.sc:780`, `rentOffice.sc:524` and `:647`), and that is what the game looks like.
  Reading the CD's index as the thing to print put a second box on screen.
