# Porting Plan

## Source of truth

| Thing | Where |
|---|---|
| Game files, CD DOS 1.0 | `original/cd/` |
| Game files, floppy 1.000.060 | `original/floppy/` |
| **Decompiled original source, CD** | `scripts/jones-cd-dos-1.0/src/` (140 files) |
| **Decompiled original source, floppy** | `scripts/jones-dos-1.000.060/src/` (138 files) |
| Documented mechanics | `MECHANICS.md` |

Both script sets are **exact version matches** for the game files we hold â€” confirmed against the
`version` file in each install (`1.0` and `1.000.060`). Scripts are from
[sluicebox/sci-scripts](https://github.com/sluicebox/sci-scripts), decompiled with a
purpose-built SCI decompiler and auto-annotated.

The CD version is the primary target: it has two extra jobs (QT Clothing Janitor, Socket City
Clerk) and speech. Keep the floppy set for cross-checking â€” where the two agree, a decompile
artefact is unlikely.

## Approach

Port **file by file**, keeping Sierra's module boundaries and names. `bank.sc` becomes the bank
module, `employment.sc` the employment module. This is deliberate: it makes any behaviour
question answerable by opening the same-named original, and it means a reviewer can diff intent
against source. Resist the urge to "improve" the structure until the port is verified.

Rules:
1. **Constants live in data tables, never inline.** Wages, prices, probabilities, thresholds.
   This is what makes the later modifications cheap.
2. **Replicate original bugs**, flagged with a comment pointing at the source line. See the
   `economicIndex.sc` adjustment bug in `MECHANICS.md` Â§12. "Exact" includes the warts.
3. **Assets via manifest only.** One JSON map of logical name â†’ file path. Never hardcode a path.
   Original art can sit behind it locally; swapping to new art is editing one file.
4. **Integer arithmetic throughout.** SCI used 16-bit ints and truncating division. Floating point
   will silently diverge â€” `(/ reading 3)` truncates.
5. `Random(a, b)` in SCI is **inclusive of both bounds**. Get this wrong and every probability
   in the game is subtly off.

## Global variable decoder

Globals 0â€“30 are SCI kernel standards (`gEgo`, `gGame`, `gCurRoom`â€¦) and are named in the source.
From `global31` up they are game state with names stripped at compile time. Decoding them by
usage is the main intellectual work of the port. Confirmed so far, from `economicIndex.sc`:

| Global | Meaning | Evidence |
|---|---|---|
| `global307` | main economy reading | `= global307 (mainI init: ... doit:)` |
| `global308`â€“`global314` | readings: invest, gds, gld, sil, prk, bc, pen | parallel assignment block |
| `global315` | main economy trend index | `= global315 (mainI index:)` |
| `global316`â€“`global322` | trend indices for the seven sub-indices | parallel assignment block |
| `global372` | week number | crash/boom gate `>= 8` |
| `global373` | crash severity this turn (0 = none, else 1â€“3) | `Random 1 3`, used as `(16 + g373)/20` |
| `global374` | **player count (1â€“4)**, which doubles as the economy's volatility term | set to 1â€“4 by `select1b.sc:88-142`, to `size` at `room1.sc:967`; used as `Random 0 (* global374 30)` at `economicIndex.sc:215/220`. The two readings of this global were both right â€” see MECHANICS Â§Open items for why the scaling exists |
| `global415` | current newspaper headline id | set from `headline` property |
| `global444` | boom flag this turn | `(= global444 (not (Random ...)))` |
| `global553` | scratch roll, reused all over | trend roll, lottery roll, doctor roll |
| `global302` | **the player whose turn it is** | `(global302 monStat:)`, `(global302 durables:)` everywhere |
| `global372` | week number | `(mod global372 4)` for rent week; `>= 8` for catastrophes |
| `global414` | "a robbery took something" flag | set in the theft loop, drives the newspaper |
| `global448` | a player has won | gates the end-of-game branch in `startTrn` state 2 |
| `global454` / `global455` | hi/lo result of the 32-bit maths helper | `proc0_12` writes both |
| `global457` | `livesAt` captured at turn start | `Player::startTurn` |
| `global433` | Employment Office outcome code | 0 asked less, 1 asked same, 2 raise granted, 3 refused, 4 hired, 5 office closed; `proc206_1` speaks `420 + global433` (`employment.sc:30-58`) |
| `global440`/`441`/`442` | speech balloon tail: tNum, x, y | set per location beside its `proc0_17` call; see `TALKER.md` |
| `global473` / `global474` | control handed back to the player | set at `startTrn.sc:884-919`; `room1.sc:141` tests 474 before accepting a click on a place |
| `global534` | graphics detail level | `< 2` skips the balloon tail, the shop picture panel's animation and the newspaper fly-in. Unported — the port always takes the high-detail branch |
| `global566` | shifts available this visit | gates the Work button with the clock: `(and (< global323 60) (> global566 0))` |
| `global323` | Hours used this turn, 0-60 | `room1.sc:1484-1495`; 60 is the week gone. Only NINE of its ~45 references gate a player action — see `GATES.md` |
| `(ScriptID 1 2)` | the players list | `room1.sc:951` |

Useful procedures: `proc0_10 n` adjusts cash, `proc0_11` returns net worth, `proc0_13 n` adjusts
happiness, `proc0_12 hi lo hi lo` is 32-bit addition.

Append to this table as each module is read. It is the single most reusable artefact of the port.

## Script â†’ module map

Read `game.ini` in the script folder for the full script-number â†’ name mapping. Grouping:

- **Core loop:** `Main` (0), `room1` (1, the board â€” 28KB), `startTrn` (111, turn start events â€”
  23KB), `weekend` (232), `System`, `Game`
- **Locations:** `lowcost` (200), `rentOffice` (201), `security` (202), `market` (203),
  `bank` (204), `factory` (205), `employment` (206), `university` (207), `appliance` (208,
  Socket City), `clothing` (209), `fastFood` (210), `discount` (211, Z-Mart), `pawnShop` (212),
  `broker` (213, stock market), `newspaper` (215)
- **Job tables:** `discountJobs`, `fastFoodJobs`, `clothingJobs`, `applianceJobs`,
  `universityJobs`, `factoryJobs`, `bankJobs`, `marketJobs`, `rentJobs` (216â€“224)
- **Systems:** `economicIndex` (107, **decoded**), `lottoScript` (116), `inventories` (231),
  `goalsDefine` (229), `viewGoals` (238), `winnerScript` (234), `diploma` (230),
  `muggedByMarket` (114)
- **AI opponent:** `WhereShouldIGo` (300, 23.7KB) â€” this is Jones's decision logic, and it is
  substantial. Port it last; it is self-contained and the game is playable without it.
- **Engine/UI plumbing:** `Interface`, `Menu`, `Motion`, `Actor`, `Sound`, `Save`, `KeyMouse`,
  `Gauge`, `WButton`, windows. Mostly **not** worth porting literally â€” this is SCI's widget
  toolkit, and the target platform will have its own. Port the behaviour, not the plumbing.

## Verification

Run the original in ScummVM beside the port and compare. The decisive test is **determinism**:
seed the port's RNG, play a fixed action sequence, and check state matches. Priority order â€”

1. Economy readings over 50 weeks (pure function of the RNG â€” easiest to verify, catches
   integer-division and inclusive-Random mistakes immediately)
2. Work payouts, experience and dependability accrual, firing threshold
3. Prices at each store across economy swings
4. Turn-start event frequencies over a long run
5. Goal progress and win detection

## Order of work

1. Decode globals for the core loop (`Main`, `startTrn`, `room1`)
2. Time, turn and week structure
3. Economy (**already decoded** â€” do this first as the reference implementation of the port's
   arithmetic conventions)
4. Player state, stats, inventory
5. Locations, starting with the simple stores
6. Employment and education
7. Bank, broker, lottery, pawn
8. Events: crashes, Wild Willy, doctor, spoilage, weekend
9. Goals and win detection
10. `WhereShouldIGo` â€” the Jones AI
