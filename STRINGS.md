# String audit — port vs. raw resources

Audited 2026-09-23. Read-only: no source file was modified.

Every user-visible string literal in `src/Jones.App/**/*.cs` and `src/Jones.Core/**/*.cs`
(excluding `obj/`, `bin/` and `Jones.Tests`) was extracted and compared **byte for byte,
case sensitively** against:

* `assets/raw/script/<n>.script` — runs of printable ASCII (32..126) between NUL bytes
* `assets/raw/text/<n>.text` — NUL-separated strings, index 0 first

The decompiled `.sc` listing was used only to find *where* a string is displayed, never
to decide *what* it says.

**Totals: 79 WRONG, 76 INVENTED, 2 MISSING.**
Everything in `ItemText.cs` and all 60 weekend texts verified OK.

---

## Cross-check: font 10 column widths

`assets/raw/font/10.font` gives space = 5 px, `|` = 1 px, `.` = 3 px, `_` = 4 px.
Measuring the ported job and item labels confirms the transcription:

| Group | script | measured widths |
|---|---|---|
| Z-Mart jobs | 216 | 93, 93, 93 |
| Monolith jobs | 217 | 96, 96, 96, 96 |
| QT jobs | 218 | 93, 93, 93, 93 |
| Socket City jobs | 219 | 98, 98, 98, 98 |
| University jobs | 220 | 93, 93, 93 |
| Factory jobs | 221 | 95 x 9 |
| Bank jobs | 222 | 93, 93, 93, 93, 93 |
| Black's Market jobs | 223 | 93, 93, 93, 93, 93 |
| Rent Office jobs | 224 | 96, 96 |
| Socket City items | 208 | 76 x 9 |
| QT items | 209 | 70, 70, 70 |

Every job list and both fixed-layout shops measure to a single uniform width, which is
what puts the wage/price column in one place. No group has an odd label out, so no
transcription error remains in `ItemText.cs`.

(Z-Mart, script 211, measures 69 or 74 depending on the line — that split is in the
original itself, because Z-Mart shows a random six of eighteen and lays them out in two
columns at runtime. Black's Market, script 203, is 84/85/90/94 for the same reason:
mixed columns. Both are ported verbatim.)

---

## OK — verified matching

### `ItemText.cs` — fully correct
All 38 job labels and all 39 shop item labels match their script resource exactly,
including trailing spaces and `|` shims. Spot citations:

* `"Cook               ||"` — script 217 @0x0500
* `"Electronic's Repair   "` — script 219 @0x0541
* `"Machinist's Helper   |"` — script 221 @0x07A6
* `"VCR...................|"` — script 211 @0x1867 (19 dots, one pipe)
* `"Food For 2 Weeks..|"` — script 203 @0x0DFD
* `"Business Suit  |"` — script 209 @0x0A18

`WithPrice` is correct: `WButton.sc:185-193` selects text `104[1]` `"%s $%d"` when
`price < 100`, else `104[2]` `"%s$%d"`.

`WithWage` is correct: `employment.sc:156-161` selects text `206[0]` `"%s  $%d Hr."`
(two spaces) when `price < 10`, else `206[1]` `"%s $%d Hr."` (one).

### `Weekend.cs` — fully correct
All 60 weekend texts match text 232 indices 0–60 exactly.
`$"You spent ${r.Cost}."` matches the format at `232[61]` `"You spent $%d."`.

### `StoreLayout.cs` — headers and employer menu correct
* All nine `JobList` headers match scripts 216–224 (e.g. `"Black's Market Jobs Available:"`, script 223 @0x0547).
* All nine `Employment` labels match script 206 @0x10FE–0x11D4.

### `MainViewModel.cs` — a few real strings
* `"How Many Players?"` — text 239[0]
* `"Market"` / `"Value"` / `"Your"` / `"Holdings"` — text 213[0..3]
* `"Deposit  "` / `"Withdraw  "` / `"Apply For Loan"` / `"See The Broker"` — script 204 @0x10A9, 0x10BB, 0x10E8, 0x110B
* `$"Loan Payment ${…}"` — correct: `bank.sc:293-296` gives `loanPayment` its own `doFormat` using text `204[0]` `"%s $%d"`, one space regardless of price
* `$"Week #{week,2}"` on the board — text `1[2]` / `700[85]` / `994[0]` `"Week #%2d"`
* `$"Player {i + 1}"` in `Game.cs` — room1.sc:1030-1050 `actualName {Player 1}` … `{Player 4}`

### `Catalogue.cs` / `Jobs.cs` / `Degrees.cs` / `Board.cs` — the majority
Most names match text 700 exactly; only the exceptions below are listed.

---

## WRONG — ported but does not match the resource

### 1. `Jones.Core/Model/Headlines.cs` — 48 of 63 headlines (resource: text 215)

The original typesets every long headline over two lines with a hard `0x0A`. The port
replaced each `\n` with a space and then word-wraps at 20 characters in
`MainViewModel.BuildNewspaper`, so the paper breaks in the wrong places on almost every
issue. Two headlines also lost an intentional double space.

| idx | resource (text 215) | port |
|---|---|---|
| 1 | `BANKS FALTER!\nSAVINGS LOST!··JOBS LOST!` | `BANKS FALTER! SAVINGS LOST! JOBS LOST!` |
| 2 | `SCANDAL ON WALL ST.··ECONOMY\nDROPS! UNEMPLOYMENT RISES` | `SCANDAL ON WALL ST. ECONOMY DROPS! UNEMPLOYMENT RISES` |
| 55 | `EXTRA! EXTRA!\n` | `EXTRA! EXTRA!` |

(`··` = two spaces.) The other 45 differ by `\n` → space only: indices 3, 4, 6, 8, 10,
11, 12, 13, 14, 15, 16, 22, 24, 26, 29, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42,
43, 44, 47, 48, 49, 50, 51, 52, 53, 54, 56, 57, 58, 59, 60, 61, 62, 63.

The 16 single-line headlines (0, 5, 7, 9, 17, 18, 19, 20, 21, 23, 25, 27, 28, 30, 45, 46)
are correct.

### 2. `StoreLayout.cs` — Rent Office labels, 5 wrong (resource: script 201)

Every label lost its trailing `.` and its padding spaces.

| resource (script 201) | port |
|---|---|
| `Pay rent for 1 month.··` @0x11F3 | `Pay rent for 1 month` |
| `Ask For More Time.·······` @0x1213 | `Ask For More Time` |
| `Rent Low-Cost Apartment··` @0x1236 | `Rent Low-Cost Apartment` |
| `Rent Security Apartment··` @0x1267 | `Rent Security Apartment` |
| `Pay Garnishment Balance··` @0x128E | `Pay Garnishment Balance` |

(`·` = one space; the trailing counts are 2, 7, 2, 2, 2.)

### 3. `StoreLayout.cs` — Bank labels, 2 wrong (resource: script 204)

`Slot("Deposit", …)` and `Slot("Withdraw", …)` drop the two trailing spaces the resource
carries (`Deposit··` @0x10A9, `Withdraw··` @0x10BB). `MainViewModel`'s own copies of
these two strings are correct, so today nothing renders the wrong version — but the
transcription in `StoreLayout` is still wrong.

### 4. `Jones.Core/Model/Board.cs` — 5 location names (resource: text 700[71..83])

`inventories.sc:36` prints the player's workplace with `Format … 231 1 700 (worksAt + 71)`,
so text 700 is the game's own name for each place.

| resource | port |
|---|---|
| `Low Cost Apartment` 700[71] | `Low-Cost Housing` |
| `Security Apartment` 700[73] | `Le Security Apartments` |
| `University` 700[78] | `Hi-Tech U` |
| `Monolith` 700[81] | `Monolith Burgers` |
| `Pawn Shoppe` 700[83] | `Pawn Shop` |

These are user-visible through `WhereText` and the board hotspot tooltips.
(`Monolith Burgers` does exist — script 206 @0x111C — but only as the Employment
Office's employer-menu label, which `StoreLayout.Employment` already uses correctly.)

### 5. `Jones.Core/Model/Degrees.cs` — 3 names (resource: text 700[10..20])

`diploma.sc:153` formats degree names out of text 700.

| resource | port |
|---|---|
| `Pre Engineering` 700[12] | `Pre-Engineering` |
| `Business Admin.` 700[15] | `Business Administration` |
| `Post Doctoral` 700[18] | `Post-Doctoral` |

The other eight (`Trade School`, `Electronics`, `Engineering`, `Junior College`,
`Academic`, `Graduate School`, `Research`, `Publishing`) match.

### 6. `Jones.Core/Model/Jobs.cs` — 7 titles (resource: text 700[42..64])

`inventories.sc:37` prints the current job with `Format … 231 2 700 (occupation)`, so
text 700 is the game's name for the occupation. The padded job-list labels in
`ItemText.cs` spell the titles out in full and are correct as they stand — these are the
*other* spelling, used on the stats screen and in `JobTitle()` / `StatusLine1`.

| resource | port |
|---|---|
| `Assist. Manager` 700[45] | `Assistant Manager` |
| `Machinist Helper` 700[52] | `Machinist's Helper` |
| `Exec. Secretary` 700[53] | `Executive Secretary` |
| `Department Mgr.` 700[56] | `Department Manager` |
| `General Mgr.` 700[57] | `General Manager` |
| `Apartment Mgr` 700[63] | `Apartment Manager` |
| `Repairman` 700[64] | `Electronics Repairman` |

### 7. `Jones.Core/Model/Catalogue.cs` — 7 names (resource: text 700)

`pawnShop.sc:672` names items from text 700 (`"Do you accept $%d for your %s?"`), as does
the stats screen.

| resource | port |
|---|---|
| `Vcr` 700[25] | `VCR` (two entries) |
| `Leisure Suit` 700[35] | `Dress Clothes` (two entries) |
| `T-BillS` 700[65] | `T-Bills` |
| `Blue Chip Stocks` 700[69] | `Blue Chip` |
| `Food For 1 Week` script 203 @0x0DDC | `1 Week of Food` |
| `Food For 2 Weeks` script 203 @0x0DFD | `2 Weeks of Food` |
| `Food For 4 Weeks` script 203 @0x0E1F | `4 Weeks of Food` |

`Dog Food`, `8-Track Player` and `Works of Capote` have no text-700 entry; their only
origin is the Z-Mart label (script 211 @0x1987, @0x19A4, @0x19C1), which the port's names
match once the dot padding is removed. Those are fine.

### 8. `MainViewModel.cs` — `"Goal Points = {total,3}"` (resource: script 236 @0x0E66 + text 236[0])

The label text `Goal Points = ` is right, but the format is `"%s%3d·"` (text 236[0]) —
there is a **trailing space** after the number that the port drops.

### 9. `MainViewModel.cs` — enrollment label (resource: script 207)

Port shows `"Pay enrollment fee"` with a separate `"$50"` detail. The resource label is
`Enrollment Fee·` (script 207, `enrollmentFee` instance), and it is a `CostDItem`, so the
game renders `Enrollment Fee··$50` via text `104[1]`.

---

## MISSING — in the resource, absent from the port

Black's Market (script 203) declares **five** lines; the port only carries three.

| resource | where |
|---|---|
| `10 Lottery Tickets\|...\|` | script 203 @0x0E40 |
| `Newspaper................\|` | script 203 @0x0E67 |

`StoreLayout.BlacksMarket` already reserves slots for both, but `Catalogue.BlacksMarket`
has no entries and `ItemText` has no labels, so the two lines never render.

---

## INVENTED — displayed by the port, no origin in the game

The CD build speaks nearly all of its feedback (`proc0_13` in `Main.sc:1102`, driving the
Talker heads) and prints almost nothing. There are only 46 `Print` calls in the whole
game, and they are the menu, save/restore, credits, the pawn-shop offer, the rent-office
confirmations, the university enrollment confirmation and the end-of-game prompt. None of
the port's status line, notice text or tooltips exists anywhere in the resources.

### `MainViewModel.Describe(TurnStartEvent)` — 20 strings, all invented

`You have met all four goals. You win!` · `The lottery came in - ${n}!` ·
`You made ${n} on the computer.` · `Wild Willy took {n} item(s).` ·
`No refrigerator - all your food spoiled.` · `Some food spoiled; {n} weeks kept.` ·
`Nothing to eat. You went hungry.` · `You were ill. The doctor cost ${n}.` ·
`Rent of ${n} is due.` · `One week of clothing left.` ·
`You have nothing to wear and cannot work!` · `The bank wants a loan payment.` ·
`Your loan payment is overdue.` · `An appliance broke - ${n} to fix.` ·
`Market crash - you lost your job!` · `Market crash - your wage was cut.` ·
`There was a market crash.` · `The economy is booming - your investments gained.` ·
`Something happened.`

`startTrn.sc` has exactly one `Print` — text `111[0]` `"What should be done with the
remaining players?"` — and no message for any of these events. The robbery, crash and
boom are announced by the **newspaper** (which the port already does); everything else is
spoken.

### Job application outcomes — 6 invented

`Hired as {title}!` · `Not enough education.` · `Not dependable enough.` ·
`Not enough experience.` · `You cannot afford the uniform.` · `No openings just now.`

`employment.sc:327-330` speaks these as audio 420–425.

### Working — 2 invented, and there are real strings for them

`You earned ${n}.` · `You cannot work right now.`

Text 108 holds the real ones: `[0]` `You are not properly dressed for work!`,
`[1]` `You have missed too much work! You're fired!`, `[2]` `Your work habits better pick
up soon or you're fired!`, `[3]` `No time is left to work.`, `[4]` `I'm sorry. I had to
garnish $%d.`, `[5]` `Your Landlord garnished $%d.` Nothing announces a successful shift.

### Apartments — 3 invented

`Relax` · `6 hrs, +3 relaxation` · `You put your feet up.`

`relaxButton` in `lowcost.sc` / `security.sc` (scripts 200, 202) is a **button sprite**
with no text at all. The only apartment string is text `200[0]` / `202[0]`
`No time is left to relax.`

### Hi-Tech University — 9 invented

`Pay enrollment fee` (see WRONG #9) · `Enrolled. Pick a course.` ·
`You cannot afford the fee.` · `Study {degree}` · `{n} lessons left` ·
`Graduated - {degree}!` · `Studied {degree}.` · `Pay an enrollment fee first.` ·
`You cannot take that course.`

The courses are **icons**, not text lines: every `UniversityDIcon` in `university.sc` is
`view 707 loop 2` with a cel per course and no `text` property. Lessons remaining is drawn
as a bare number — `Format … 207 0` (`"%d"`) at x 222 (`university.sc:130-140`). The only
printed line is text `207[1]` `Enroll for $%d?`.

### Rent Office — 3 invented

`Pay rent` · `Rent paid.` · `You cannot afford it.`

The real labels are in script 201 (see WRONG #2); the confirmations are text `201[0]`
`Rent Low-Cost Apartment?` and `201[1]` `Rent Security Apartment?` with buttons
`·YES·` / `·NO·` (script 201 @0x1250, @0x1256).

### Pawn Shop — 3 invented

`Pawn item #{n}` · `${offer}` · `Pawned for ${n}.`

Script 212 holds `Pawnable Items` @0x1B29, `Buyable Items` @0x1B8C,
`Redeemable Items` @0x1BA5, and the buttons `Take It` @0x1B44 / `Leave It` @0x1B4C.
The offer is text `212[3]` `Do you accept $%d for your %s?`, naming the item from text 700.

### Shops — 2 invented

`Bought {item}.` · `Not enough cash.`

`boughtItem` and `notEnoughCash` are Talker instances in every shop script — spoken, not
printed. QT Clothing is the one place with a printed fallback: text `209[30]`
`You do not have enough cash.`

### Travel — 2 invented

`The Rent Office only opens in the last week of the month.` ·
`Not enough hours left in the week.`

### Status line — 5 invented

`{JobTitle()} - ${wage}/hr` · `Dep {n}  Exp {n}  Rlx {n}  Deg {n}/11` ·
`{n} hrs left` · `${cash:N0}` · `Working`

The stats screen has its own format table, text 231: `[1]` `Works at %-16s`,
`[2]` `As a %-20s`, `[3]` `Hourly wage: $%-11d`, `[5]` `Unemployed` (the one string the
port does get right), `[6]` `Cash: $%-18s`, `[7]` `Savings: $%-15s`,
`[8]` `Rent Owed: $%-13d`, `[9]` `Loan Balance: $%-10d`, `[10]` `Total Goods: $%-11d`,
`[11]` `Investment Total: $%-6s`, `[12]` `Net Worth: $%-13s`, `[13]` `-----GOODS-----`,
`[19]` `---EDUCATION---`, `[20]` `--INVESTMENTS--`.
`ClockText`/`CashText` have no counterpart at all — the original shows the hours as a
clock sprite (view 270) and the cash on a calculator sprite (view 0 loop 4), which the
port already draws.

### Goal-setting slider names — 4 invented

`Wealth` · `Happiness` · `Education` · `Career` (tooltips in `BuildGoalSetting`).

The sliders are art (`view 501 loop 2`). The four words do appear as the opening of the
four help paragraphs in script 229 (`goalsDefine`, @0x05AA, @0x0660, @0x071C, @0x07C6),
but never as labels.

### Tooltips — 16 invented

`Play Game` · `Restore Game - not yet ported` · `Watch Demo` · `{n} player(s)` ·
`Already chosen` · `Choose character {n}` · `{goal}: {value}` · `Start the game` ·
`Next player` · `Done` · `Continue` · `Leave` · `Leave the broker` · `Buy 1 {x}` ·
`Sell 1 {x}` · `Work a shift - ${n}`

SCI has no tooltips; every one of these is a button sprite in the original. (`Continue`
does occur in script 111, but as an unrelated menu word.)

### `ActionVm.MenuText` — 1 invented

`$"{Label}   {Detail}"` joins the label and the detail with three spaces. The original
never does this: `CostDItem::doFormat` appends `" $%d"` or `"$%d"` directly to the
label, which is what the label's own dot/pipe padding is measured for. Any line that
carries a `Detail` (University, Rent Office, Pawn Shop) is therefore drawn in a layout the
game never had.

---

## Note — `assets/audio/subtitles.json` (outside the .cs sweep)

533 subtitle lines. 227 match a text resource exactly. Of the remainder:

* **48 newspaper reads (audio 461–522) carry the same `\n` → space flattening as
  `Headlines.cs`**, including the lost double space in audio 461. Same fix as WRONG #1.
* 255 lines — the Pawn Shop greetings, the bank and university clerk lines, and most of
  the shopkeeper chatter — have **no counterpart in any raw text resource**. This build
  has no MESSAGE resources, so those lines exist only as audio; they appear to have been
  transcribed by ear and cannot be verified byte for byte here.
