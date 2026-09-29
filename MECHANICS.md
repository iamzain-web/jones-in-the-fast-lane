# Jones in the Fast Lane — Mechanics Reference

Source: Jones in the Fast Lane Wiki (CC-BY-SA), cross-checked against the Sierra manual.
Status: **documented mechanics only.** Items marked `[UNVERIFIED]` are not publicly documented
and must be confirmed by decompiling the original scripts (SCI Companion) or by measurement.

Original: Sierra On-Line, 1990 (floppy) / 1991 (CD). Engine: SCI1. Runs in ScummVM.

---

## 1. Time

| Unit | Definition |
|---|---|
| Turn | One player's go. 60 Hours. |
| Hour | The time point. Each action costs Hours. |
| Week | Advances once all players have taken a turn. |
| Month | 4 Weeks. Rent + loan payments fall due in week 4. |

- Turn begins with a Weekend, then queued events, then the player is placed at their Apartment.
  The weekend runs on **every player's turn** from week 2 onward, not once per week.
- **Entering any location costs 2 Hours** — every time, even re-entering immediately, and
  including the building you are already standing in (`room1.sc:164-167`).
- Movement costs Hours by distance. The board path is **170 steps** (`marblePath.sc:13`) and
  the marble always takes the **shorter arc** (`marblePath.sc:86-104`). The rate is
  **15 path steps per Hour** (`room1.sc:1319/1488`, `global475 = moveSpeed × 14`), so a full
  lap is about **11.3 Hours** — not the ~10 this table used to claim. The sub-hour remainder
  is carried across the whole turn rather than rounded per journey.
- Turn ends when 60 Hours are spent and the player exits a building (`proc1_9`, `room1.sc:50-64`,
  called from `Place::endCue` at `room1.sc:273`). If Hours run out mid-travel, `timeKeep::doit`
  calls it directly (`room1.sc:1513`).
- Illness costs Hours, not just money: **starving 20**, **the doctor 10**
  (`startTrn.sc:432/464` → `:1035/1041`).

### What survives the clock running out

This list used to be a guess and was wrong. Of the ~45 `global323` references in the scripts,
**only nine gate a player-facing action**, and seven of those speak a line rather than doing
nothing at all.

- **Buying is never gated, anywhere.** `CostDItem::doit` (`WButton.sc:197-235`) — the buy action
  every shop uses — has no clock test whatsoever. A player whose week has ended can still buy
  food, which is the point: they have to eat before the next one.
- Gated: working (`fastFood.sc:328` and equivalents), applying for a job (`employment.sc:167`,
  which speaks clip 425), studying (`university.sc:193`), relaxing (`lowcost.sc:183`,
  `security.sc:201`), the bank's loan and the broker (`bank.sc:341/417`, `broker.sc:770`).

## 2. Goals & Victory

Four goals, each set 10–100 at game start, per player. Jones's are random.
A player wins when **all four** are met, checked **at the start of their turn**.

| Goal | Progress formula |
|---|---|
| Wealth | `liquid_assets / 100` |
| Happiness | `= Happiness stat` |
| Education | `1 + (9 × degrees)` |
| Career | `(Dependability / 8) × 10`, truncating, capped at 100, **forced to 0 if unemployed** |

- Education 100 requires all 11 degrees.
- Career progress is **forced to 0 while unemployed**, regardless of Dependability.

> **The wiki is wrong about Career.** It documents `1.25 × Dependability`, a smooth ramp. The
> source (`room1.sc:757`, in `Player::endTurn`) computes `(dependibility / 8) * 10` with
> truncating division — a **step function** in increments of 10. Dependability 20 scores 20, not
> 25; anything from 24 to 31 scores 30. It is recomputed at the END of each turn, not live.
> Consequence for play: dependability gains only pay off when they cross a multiple of 8, so
> the last point before a boundary is worth ten times the one after it.

## 3. Stats

**Experience** — +1 per work session, capped by job. **Dependability** — +1 per work session,
capped by job; **decays -3 every Week**. **Relaxation** — starts 10, max 50, floor 10;
-1 per turn unless Hot Tub owned. **Happiness** — see §7.

Each degree grants a permanent **+5 max Dependability and +5 max Experience**.

## 4. Work

- A work session costs **6 Hours** and pays `wage × 8`.
- Partial session: `wage × 8 × (hours_remaining / 6)`.
- Requires the job's **uniform** (or better) to be owned.
- Fired when Dependability falls **5 below** the job's requirement.
- In rent debt: earnings **garnished by half, plus a $2 interest fee**.

### Jobs

| Location | Job | Base Wage | Exp | Dep | Degrees | Uniform |
|---|---|---|---|---|---|---|
| Z-Mart | Clerk | $5 | 10 | 10 | — | Casual |
| | Assistant Manager | $7 | 20 | 20 | — | Dress |
| | Manager | $8 | 30 | 30 | Junior College | Business |
| Monolith Burgers | Cook | $5 | 0 | 10 | — | Casual |
| | Clerk | $6 | 10 | 20 | — | Casual |
| | Assistant Manager | $7 | 20 | 30 | — | Casual |
| | Manager | $8 | 30 | 40 | Junior College | Dress |
| QT Clothing | Janitor † | $6 | 10 | 20 | — | Casual |
| | Salesperson | $8 | 30 | 30 | — | Dress |
| | Assistant Manager | $9 | 40 | 40 | Junior College | Business |
| | Manager | $12 | 50 | 50 | Business Admin. | Business |
| Socket City | Clerk † | $6 | 10 | 20 | — | Casual |
| | Salesperson | $7 | 30 | 30 | — | Dress |
| | Electronics Repairman | $11 | 40 | 40 | Electronics | Casual |
| | Manager | $14 | 40 | 40 | Electronics + Junior College | Business |
| Hi-Tech U | Janitor | $5 | 10 | 10 | — | Casual |
| | Teacher | $11 | 40 | 50 | Academic | Dress |
| | Professor | $20 | 50 | 60 | Research | Dress |
| Factory | Janitor | $7 | 10 | 20 | — | Casual |
| | Assembly Worker | $8 | 30 | 30 | Trade School | Casual |
| | Secretary | $9 | 40 | 40 | Junior College | Dress |
| | Machinist's Helper | $10 | 40 | 40 | Pre-Engineering | Casual |
| | Executive Secretary | $18 | 50 | 50 | Business Admin. | Business |
| | Machinist | $19 | 50 | 50 | Engineering | Casual |
| | Department Manager | $22 | 60 | 60 | Junior College + Engineering | Business |
| | Engineer | $23 | 60 | 60 | Junior College + Engineering | Business |
| | General Manager | $25 | 70 | 70 | Business Admin. + Engineering | Business |
| Bank | Janitor | $6 | 10 | 20 | — | Casual |
| | Teller | $10 | 40 | 40 | Junior College | Dress |
| | Assistant Manager | $14 | 50 | 50 | Business Admin. | Business |
| | Manager | $19 | 60 | 60 | Business Admin. | Business |
| | Broker | $22 | 70 | 70 | Business Admin. + Academic | Business |
| Black's Market | Janitor | $6 | 10 | 10 | — | Casual |
| | Checker | $8 | 20 | 20 | — | Casual |
| | Butcher | $12 | 30 | 30 | Trade School | Casual |
| | Assistant Manager | $15 | 40 | 40 | Junior College | Dress |
| | Manager | $18 | 50 | 50 | Business Admin. | Business |
| Rent Office | Groundskeeper | $7 | 10 | 20 | — | Casual |
| | Apartment Manager | $9 | 30 | 30 | Junior College | Casual |

† CD-ROM version only. Cook has no effective requirements — anyone can take it.

Job application refusals: "Not enough Education" if degrees missing. "No Openings" chance depends
on degrees, Dependability and Experience in near-equal weight; all degrees guarantees ≥66% to
avoid it at worst Dep/Exp. `[UNVERIFIED]` — exact formula.

## 5. Education (Hi-Tech U)

- Enrollment: **$50 base** per course, economy-adjusted. Costs no Hours; allowed after turn end.
- Enrollments are fungible credits — buy several, choose courses later.
- Default **10 lessons** per course. Each lesson costs **6 Hours**; needs ≥1 Hour remaining, and a
  short clock is not penalised.
- Up to **4 courses active** simultaneously, completed in any order, no time limit.
- **Extra credit:** Computer = -1 lesson. Encyclopedia + Dictionary + Atlas (all three) = -1 lesson.
  Both = 8 lessons minimum.
- **On graduating:** +5 Happiness, +5 Dependability (may exceed cap), +5 max Dep, +5 max Exp.

### Degrees (11 total)

| Degree | Unlocked by |
|---|---|
| Junior College | available at start |
| Trade School | available at start |
| Business Administration | Junior College |
| Academic | Junior College |
| Electronics | Trade School |
| Pre-Engineering | Trade School |
| Graduate School | Academic |
| Engineering | Pre-Engineering |
| Post-Doctoral | Graduate School |
| Research | Post-Doctoral |
| Publishing | Research |

Graduate School, Post-Doctoral and Publishing gate further degrees but unlock no jobs directly.

## 6. Locations

Clockwise from top of board: Low-Cost Housing, Pawn Shop, Z-Mart, Monolith Burgers, QT Clothing,
Socket City, Hi-Tech U, Employment Office, Factory, Bank, Black's Market, Le Security Apartments,
Rent Office.

- **Rent Office** opening is a THREE-clause rule (`room1.sc:176-193`), not just the week:
  `worksAt == 1` **or** `week % 4 == 0` **or** `leaveOpen`. Someone employed at the Rent Office
  can always get in — otherwise they would be locked out of their own job three weeks in four.
- **"Closed" does not block travel.** `Place::cue` opens the dialog and charges the 2 Hours
  regardless; being closed just draws a shut-office picture. Refusing the journey outright is
  not what the game does.
- Exiting **Bank** (1/31) or **Black's Market** (1/51) can trigger a Wild Willy mugging — on the
  way OUT, not on arrival.

## 7. Happiness

### Gains
Relax at apartment +2 (once/turn) · New job +3 · Raise +3 · Bank loan +5 · Rent extension +1 ·
New degree +5 · Microwave *or* Stove owned at turn start +1 · Economic boom +5 (needs ≥$1000 in
stocks) · Senior citizen bus weekend +2..+4 (~1/41) · Computer earns money +3 (1/7, needs Computer) ·
Lottery small/medium win +5 (~1/500 per ticket) · Lottery $5000 +10 (~1/10000 per ticket).

**Purchases** (Socket City): Refrigerator +1, Freezer +2, Stove +1, Color TV +2, VCR +2, Stereo +2,
Microwave +2, Hot Tub +3, Computer +3 — only if not already owned.
**(Z-Mart):** Refrigerator/Stove/Color TV/VCR/Stereo/Microwave/Encyclopedia/Dictionary/Atlas +1 each,
only if not already owned. Tickets (Baseball/Theatre/Concert) +2 each, first of each type per turn,
max +6. B&W TV, Casual and Dress Clothes: 0.
**(Black's Market):** 1 week food +1, 2 weeks +2, 4 weeks +4 — first fresh food purchase per turn
only. Lottery tickets +2, first purchase per turn.
**(QT Clothing):** Dress Clothes +1, Business Suit +2 — every purchase.
**(Monolith Burgers):** Cheeseburger +1 / Astro Chicken +2, first of either per turn.
Colas +1 / Shakes +2, first of either per turn.

### Losses
Starvation -2 · Doctor visit -4 · Appliance broken -1 each · All food spoiled -2 · Some food
spoiled -1 · Minor crash -1 / Moderate -2 / Major -3 · plus -1/-2/-5 by severity if ≥$1000 in
stocks · Lost job to crash -7 · Wage cut from crash -3 · Mugged by Wild Willy -3 · Apartment
robbed -4 · Loan default -1 per month unpaid · Job refused -1 · Loan denied -2 (or -1 if you
already hold a loan) · Rent extension refused -1 (first attempt/turn) · Pawning -1 (extra -1 if
pawning a Refrigerator while holding fresh food).
**Junk purchases:** Dog Food -1, 8-Track Player -1, Works of Capote -2 — every time.

## 8. Relaxation, Health, Robbery

- Relax action: **6 Hours, +3 Relaxation** (max 50). First relax each turn also +2 Happiness.
- **Doctor visit:** if Relaxation is at floor (10) at turn start, 25% chance — costs **10 Hours,
  up to $200, -4 Happiness**. Also 50% chance when eating spoiled food.
- **Apartment robbery** (Low-Cost Housing only, must own durables):
  `chance = 1 / (Relaxation + 1)`. Costs -4 Happiness and any number of durables.
  Le Security Apartments cannot be robbed.

## 9. Food

- **Fast food** (Monolith Burgers): prevents starvation for the **next turn only**; consumed at the
  start of that turn. Buying multiples does not stack across turns.
- **Fresh food** (Black's Market): sold in packs, 1 unit per turn. Storage: **6 units** with a
  Refrigerator, **12** with Refrigerator + Freezer. No refrigerator ⇒ all spoils (-2 Happiness).
  Over capacity ⇒ excess spoils (-1 Happiness).
- No food at all ⇒ **starvation, -2 Happiness**.

## 10. Housing & Rent

- Two apartments: **Low-Cost Housing** (start, robbable) and **Le Security Apartments** (costly, safe).
- Rent starts at **$325**, due week 4 of each month at the Rent Office.
- Rent is static while you stay put; other apartments' rents move with the economy.
- Rent extension available (+1 Happiness on success, -1 on first refusal each turn).
- Unpaid rent ⇒ **debt, and wage garnishment of 50% + $2 per work session**.

## 11. Prices (Z-Mart base)

Z-Mart stocks **6 of its 17 items at random**, rerolled at the start of each player's turn.
Appliances bought here break more often.

| Item | Type | Base | Saving vs |
|---|---|---|---|
| Refrigerator | Appliance | $650 | 25% vs Socket City |
| Stove | Appliance | $490 | 14% vs Socket City |
| Stereo | Appliance | $450 | **-9%** (dearer than Socket City) |
| Color TV | Appliance | $349 | 33% vs Socket City |
| Black & White TV | Appliance | $110 | — |
| Microwave | Appliance | $220 | 33% vs Socket City |
| VCR | Appliance | $250 | 25% vs Socket City |
| Encyclopedia | Book | $475 | — |
| Dictionary | Book | $70 | — |
| Atlas | Book | $55 | — |
| Casual Clothes | Clothing | $35 | 52% vs QT Clothing |
| Dress Clothes | Clothing | $90 | 28% vs QT Clothing |
| Baseball Tickets | Ticket | $45 | — |
| Theatre Tickets | Ticket | $30 | — |
| Concert Tickets | Ticket | $40 | — |
| Dog Food | Junk | $18 | — |
| 8-Track Player | Junk | $75 | — |
| Works of Capote | Junk | $100 | — |

All prices move with the economy; discount percentages stay fixed relative to the other store.

---

## 12. Economy — VERIFIED from original source (`economicIndex.sc`, script 107)

Eight indices run in parallel. `mainI` drives the town economy; the rest are investment
instruments. Each carries `index` (trend, roughly -3..+3), `reading` (the value, clamped **70..190**)
and `risk`.

| Instance | risk | headline | Role |
|---|---|---|---|
| mainI | 4 | 20 | main economy — drives prices and wages |
| investIndex | 4 | 18 | drives all commodity indices below |
| gdsIndex | 4 | 20 | goods |
| gldIndex | 2 | 5 | gold |
| silIndex | 2 | 7 | silver |
| prkIndex | 4 | 9 | pork bellies |
| bcIndex | 1 | 11 | blue chip |
| penIndex | **10** | 13 | penny stocks — by far the most volatile |

**Mean reversion.** Cheap readings get an upward bias, expensive ones a downward bias:
`high` = 2 if reading < 80, 1 if reading < 90, else 0. `low` = 2 if reading > 160, 1 if > 130, else 0.

**Trend random walk.** `roll = Random(100 + (-3 - low), 100 + 3 + high) - 100`.
If `roll < index` then `index--`, floored at `-3 - low`. If `roll > index` then `index++`,
capped at `3 + high`.

**Per-tick adjustment.** `lowerRange = -3`, `upperRange = 3`; if `index < 0` then
`lowerRange = -3 + 2×index`; if `index > 0` then `upperRange = 3 + 2×index`.
Then `adjustment = Random(100 + lowerRange, 100 + upperRange) - 100`.

**New reading.** `reading += adjustment + (parent_index / 3)`, truncating division. Then
crash/boom is applied and the result clamped to 70..190.

**Dependency tree** — the indices are not independent; each takes its parent's *freshly updated*
trend as drift, in this order:

```
main ──┬── invest ──┬── gold, silver, pork, blueChip, penny
       └── goods
```

Note **goods follows main, not invest** (source line 229 passes `global315`, whereas lines
232–245 pass `global316`). Easy to misread, and it changes whether goods prices track the town
economy or the markets.

**Crash and boom.**
- Crash fires only if `main_reading >= 80` **and** `week >= 8` **and** `Random(0, g374 × 30) == 0`.
  Severity `s = Random(1, 3)`; applied as `reading = reading × (16 + s) / 20`.
  > **The severity scale runs backwards.** `s = 1` is the WORST crash (×17/20, a **15%** cut) and
  > `s = 3` the mildest (×19/20, **5%**). Intermediate `s = 2` is **10%**. Every other effect
  > follows the same inversion — see §13.
- Boom fires if `week >= 8` and `main_reading <= 120` and the same 1-in-`(g374 × 30)` roll.
  Applied as `reading = reading × 11 / 10` — a flat **+10%**.
- Crash also drags `index` down by 3 (floored at -3); boom pushes it up by 3 (capped at +3).

**Headline selection.** Tracks the most extreme index either way; if `|index| >= 3` with a 3-in-4
roll, or `|index| == 2` with a 1-in-3 roll, that index's headline number is published.

> **Authentic bug — replicate deliberately.** Lines 121–128 test `(== adjustment lowerRange)` in
> *both* branches of the `cond`. The second was plainly meant to be `upperRange`. The effect is
> that the extra downside kick (`-= Random(0, risk × |index|)`) fires at the bottom of the range
> but the matching upside kick is **dead code that can never run**. The original economy is
> therefore biased downward at the extremes. An exact port must keep this.

---

## 13. Crash effects on players — VERIFIED (`room1.sc:825`, `Player::doScandal`)

Called per player with the crash severity `s`. Remember **`s = 1` is the worst**.

1. **`s == 1` wipes the bank balance to 0.** A severity-1 crash is a bank failure as well as a
   market crash. Savings are not safe. Severities 2 and 3 leave the bank alone.
2. **Job loss:** if employed and `Random(0, s - 1) == 0`, the player loses wage, workplace and
   occupation outright. That is **100% at s=1**, **1-in-2 at s=2**, **1-in-3 at s=3**.
3. **Otherwise, a wage cut:** `wage = wage × (4 + 2s) / 10`, floored at 1. That is a **40% cut at
   s=1**, **20% at s=2**, and **no change at s=3** (`10/10`).

So a severity-1 crash is genuinely brutal — bank wiped, job gone, and the economy down 15% — while
a severity-3 crash costs most players nothing but a 1-in-3 job risk.

## 14. Player model — VERIFIED (`room1.sc:588`, `Player` class)

### Starting state

| Property | Start | Notes |
|---|---|---|
| `cash` | 200 | also `netWorth` and `lqAss` = 200 |
| `relax` | **25** | wiki says 10 — see below |
| `dependibility` | 20 | |
| `experience` | 10 | `maxExper` also 10 |
| `curRent` | 325 | matches the wiki |
| `uniform` / `wearing` | 36 / 36 | 36 = Casual |
| goals | 50 each | the default before players set them |
| consumables | 3 × item 40 @ $325, 6 × item 36 @ $30 | 3 weeks' rent paid, 6 casual outfits |

> **Possible wiki error on Relaxation.** The class initialiser sets `relax 25`, not 10. The wiki
> states 10, and the doctor-visit rule keys off the stat being "at its minimum". Both can be true
> if the floor is 10 but the start is 25. `[UNVERIFIED]` — confirm whether game setup overwrites
> `relax` before play begins.

### Clothing — lower index is better

Items **34 = Business Suit, 35 = Dress, 36 = Casual**. `dressedForWork` scans 34 → 35 → 36 and
takes the first one held as `wearing`, so a player always wears the best they own. The check is
`wearing <= uniform`, which is how "the required uniform **or better**" works: better clothing has
a lower number.

### Money is 32-bit

Cash, net worth, liquid assets and bank balance are each stored as a **Hi/Lo pair of 16-bit words**
(`cash`/`cashHi`) and combined by a 32-bit helper. Money genuinely exceeds 16 bits in a long game.
A port must not model money as a 16-bit value, even though everything else is.

- `calcLiquidAssets` = investments + cash + bank balance − (rent owed + loan balance)
- `calcNetWorth` = liquid assets + Σ(durable `pricePaid` × `quantity`)

### Investments

Six instruments. T-bills are valued at a flat `basePrice`; the other five are valued against their
economic index reading (`global310`–`global314`, i.e. gold, silver, pork, blue chip, penny).

| Instrument | Base price | Index |
|---|---|---|
| T-bills | $100 | none — fixed |
| Gold | $413 | gldIndex |
| Silver | $14 | silIndex |
| Pork bellies | $20 | prkIndex |
| Blue chip | $49 | bcIndex |
| Penny stocks | $7 | penIndex |

### Turn end (`Player::endTurn`)

1. `dependibility -= 3`, floored at 0
2. Career stat recomputed — see §2
3. Every durable ages one step through a 2-bit wear counter (24 → 16 → 8 → 0); on reaching 0 a
   "broken" flag is set. This is the appliance breakage mechanic.
4. On week 4 of the month (`week % 4 == 0`): loan payment schedule decrements, a missed payment
   increments `latePay`, and if the rent consumable has run out, `rentOwed += curRent`

## 15. Turn start sequence — VERIFIED (`startTrn.sc`, script 111)

A turn opens with a fixed chain of checks, run as a state machine. The order matters: food
spoils *before* starvation is judged, which is why losing a refrigerator can starve you in the
same turn. Odd-numbered states are just one-cycle pauses for the UI and are omitted.

| State | Event | Rule |
|---|---|---|
| 0 | Win check | All four goals met ⇒ `winnerScript`. Checked at turn START, so you always see the turn you won on. |
| 2 | Weekend + lottery | Non-first weeks run the weekend script. Lottery: `roll = Random(0,500)`; you win if `tickets > roll`. Prize **$5000** if `roll <= tickets/20`, **$500** if `roll <= tickets/5`, else **$200**. Happiness +10 for the jackpot, else +5. **Tickets are zeroed every turn either way.** |
| 4 | Computer income | Owning a Computer: 1-in-7 (`Random(0,6)==0`) to earn **`Random(20,100)`** and +3 Happiness. |
| 6 | Relax decay + robbery | No Hot Tub ⇒ `relax -= 1`, floor 10. Then at Low-Cost Housing with durables, robbery chance `1/(relax+1)`; each eligible durable is taken on a 3-in-4 roll. Fridge, freezer, item 23, the books and the computer are never stolen. On any loss: −4 Happiness, headline 15. |
| 8 | Pick the meal | Scans consumables 5 → 1 and takes the first held. |
| 10 | Spoilage | No Refrigerator ⇒ all fresh food lost, −2 Happiness. No Freezer ⇒ capped at 6, −1. Otherwise capped at 12, −1. |
| 12 | Fast food expires, starvation | Consumables 3–5 are zeroed unconditionally — this is why fast food never carries over. Nothing to eat ⇒ −2 Happiness. |
| 14 | Doctor | Ate spoiled food ⇒ 1-in-2. Relaxation at its floor of 10 ⇒ **1-in-5** (`Random(0,4)==0`). Cost: cap **$200**, or **$50** if net worth < 500, then `Random(30, cap)`. −4 Happiness. |
| 16 | Rent due | Rent week and prepaid weeks exhausted ⇒ rent notice. |
| 18 | Clothing | Recomputes what you're wearing. **1 week left ⇒ warning; 0 ⇒ you are naked** and cannot work. |
| 20 | Loan | Rent week with a balance: no schedule left ⇒ demand; overdue ⇒ −1 Happiness. |
| 22 | Appliance breakage | Only if **net worth > 500**. Books exempt. Per breakable durable: **1-in-36** if Z-Mart-bought, else **1-in-51**. Repair costs `Random(price/20, price/4)`, −1 Happiness. |
| 24 | **Crash / boom newspaper** | `startTrn.sc:644-686` sets the headline directly — the crash severity itself for 1/2/3, and 4 for a boom — and prints the paper. This is NOT `doScandal`; the table used to say it was. |
| 26, 28, 30, 32 | **`doScandal`, once per player** | `startTrn.sc:694-822` — see §13. Extra Happiness penalty if investments exceed $1000. Four states because it runs for each of the four players in turn. |
| 34 | **Destitute clothing handout** | `startTrn.sc:827-880`. A player with no clothing at all is given something to wear rather than being left permanently unable to work. |
| 36 | **Control handed back** | `startTrn.sc:884-919` sets `global473` and `global474`, which is what re-enables clicking on places (`room1.sc:141`). The chain is not finished until this runs. |

> **Corrections made after porting this table.** State 24 was labelled "applies `doScandal`";
> it is the newspaper, and `doScandal` is states 26-32. States 34 and 36 were missing entirely —
> and 36 is the one that gives the player control back, so omitting it is not cosmetic. State 2
> also needed correcting: the weekend runs on **every player's turn** in weeks ≥ 2, not once
> per week.

> **The two illness states also cost Hours**, which this table did not record: starving spends
> **20 Hours** and a doctor's visit **10** (`startTrn.sc:432/464` → `:1035/1041`). The doctor's
> bill is capped on **cash** (`proc0_11`, `startTrn.sc:447-454`), not on net worth.

> **Two wiki corrections here.** The doctor visit is **20%**, not 25% (`Random(0,4)==0` is one
> chance in five). Appliance breakage is **1-in-36 and 1-in-51**, not 1-in-35 and 1-in-50 — the
> inclusive upper bound again.

> **Wealth gating.** Appliances only break when net worth exceeds 500, and the doctor charges
> $200 rather than $50 on the same threshold. The game deliberately stops kicking players who
> are already down.

## 16. Pricing — VERIFIED (`n109.sc`, `proc109_0`)

Every price in the game — shelves, investments, pawn offers, the uniform affordability check —
goes through one function of `(index reading, base price)`.

1. **Amplify** the deviation from neutral by 5/3:
   `effective = reading < 100 ? reading - (100-reading)*2/3 : reading + (reading-100)*2/3`
2. **Floor at 50.** A reading of 70 gives an effective 50; 190 gives 250. So shelf prices swing
   from **half to two-and-a-half times** base. There is a floor but **no ceiling**.
3. **Apply as a percentage, split into tens and units:**
   `price = (base × (effective/10))/10 + (base × (effective mod 10))/100`, then floored at 1.

The split is not an optimisation to tidy away — it truncates twice, so it returns a slightly
lower answer than `base × effective / 100` would. Preserving it is required for exactness.

**Goods use the GOODS index** (`global309`), confirmed at `employment.sc:66` and
`pawnShop.sc:664`. Investments use their own indices; T-bills bypass the function entirely.

**Pawn shop pays 40%** of the economy-adjusted price you paid:
`offer = Price(goodsIndex, pricePaid) × 4 / 10`.

> **ORIGINAL BUG — the computer gets cheaper in a boom.** The tens multiply is 16-bit, and the
> original explicitly tests whether its own product has gone negative, substituting 32767 when it
> has. Socket City's computer (base **1599**) overflows once `effective` reaches 210 — a goods
> reading of **166**. At reading 165 it costs **3325**; at 166 it costs **3276** and stays pinned
> there however strong the economy gets. So the most expensive item in the game is *cheapest*
> during a boom, backwards from everything else. Replicated deliberately, with a test.

## 17. Store catalogues — VERIFIED (base prices from each store's script)

All prices below are pre-economy base prices. These reproduce the documented Z-Mart discounts to
within a percentage point, which is a strong cross-check that the extraction is right.

### Z-Mart (`discount.sc`) — stocks only 6 of these 18, rerolled each turn

| Item | Id | Base | | Item | Id | Base |
|---|---|---|---|---|---|---|
| Refrigerator | 21 | 650 | | Encyclopedia | 31 | 475 |
| Stove | 23 | 490 | | Dictionary | 32 | 70 |
| Stereo | 26 | 450 | | Atlas | 33 | 55 |
| Color TV | 24 | 349 | | Casual Clothes | 36 | 35 |
| B&W TV | 30 | 110 | | Dress Clothes | 35 | 90 |
| Microwave | 27 | 220 | | Baseball Tickets | 37 | 45 |
| VCR | 25 | 250 | | Theatre Tickets | 38 | 30 |
| | | | | Concert Tickets | 39 | 40 |
| Dog Food | — | 18 | | 8-Track Player | — | 75 |
| Works of Capote | — | 100 | | | | |

> **Junk has no item id at all.** Dog food, the 8-track and the Capote have no `indexNum`, so
> they are never added to inventory. They take your money, cost happiness, and vanish.

### Socket City (`appliance.sc`) — full price, but the only source of three key items

| Item | Id | Base | | Item | Id | Base |
|---|---|---|---|---|---|---|
| Refrigerator | 21 | 876 | | Microwave | 27 | 330 |
| Freezer | 22 | 513 | | **Hot Tub** | 28 | 1255 |
| Stove | 23 | 570 | | **Computer** | 29 | 1599 |
| Color TV | 24 | 525 | | Stereo | 26 | 412 |
| VCR | 25 | 333 | | | | |

The freezer, hot tub and computer are **Socket City exclusives**, and each changes a rule rather
than merely adding happiness: double food storage, no relaxation decay, and income plus extra
credit respectively.

> **The stereo is a trap.** Z-Mart charges 450 against Socket City's 412 — the "discount store"
> is 9% dearer on exactly one item.

### QT Clothing (`clothing.sc`) · Black's Market (`market.sc`) · Monolith Burgers (`fastFood.sc`)

| QT Clothing | Id | Base | | Black's Market | Id | Base | | Monolith | Id | Base |
|---|---|---|---|---|---|---|---|---|---|---|
| Business Suit | 34 | 295 | | 1 Week Food | 1 | 55 | | Astro Chicken | 2 | 124 |
| Dress Clothes | 35 | 125 | | 2 Weeks Food | 1 | 100 | | Hamburgers | 3 | 79 |
| Casual Clothes | 36 | 73 | | 4 Weeks Food | 1 | 190 | | Cheeseburgers | 4 | 89 |
| | | | | Newspaper | 8 | — | | Fries | 5 | 65 |
| | | | | Lottery Tickets | 9 | — | | Shakes | 6 | 102 |
| | | | | | | | | Colas | 7 | 69 |

Bulk food is the better deal: $55, $50 and $47.50 per week respectively.

> **QUIRK — Astro Chicken keeps.** The next-turn purge loop at `startTrn.sc:420` runs
> `for local0 = 5; local0 > 2` and so clears only ids 3–5 (hamburgers, cheeseburgers, fries).
> Astro Chicken at id 2 escapes it and merely decrements like any other consumable, making it
> the only fast food that survives the night.

## 18. Two corrections to §15 from porting it

- **Spoiled food is not starvation.** When the meal you picked spoils, the original marks it
  `-1`, and the starvation test is `not local5` — which `-1` fails. You eat it and take a
  **1-in-2** doctor roll instead of starving.
- **Starving carries its own 1-in-4 doctor risk** (`Random(0,3)`, `startTrn.sc:431`). The roll is
  set in state 12 but only acted on in state 14, which is easy to miss when porting. There are
  therefore three independent routes to the doctor, not two.

---

## 19. The Bank — VERIFIED (`bank.sc`)

> **There is no interest on savings.** The only writes to `bankBal` anywhere in the game are
> deposit, withdraw, and a severity-1 crash zeroing it. Saving protects money; it does not grow it.

- **Deposit and withdraw are capped at $100 per click** — `min(cash, 100)` and `min(bankBal, 100)`.
- **Loan size:** `(wage + lqAss/1000 - (5 + latePay + loanBal/100 + (loanBal ? 1 : 0))) × 100`,
  granted only when that is positive and `paySched >= 0`. Employment is **not** a hard requirement —
  a rich unemployed player with `lqAss >= 6000` still qualifies.
- **Interest is charged per payment, not on the balance:** a payment costs **$50 cash and clears
  only $45** of principal — a flat **$5 per payment**. The final payment, when the balance is under
  50, clears exactly with no interest.
- **`latePay`** increments once per unpaid month and is read in exactly one place: it shrinks every
  future loan by **$100 per late payment**. There is no repossession or default anywhere in the code.
- **Applying costs 2 hours**, charged before you hear the answer. Loan approved: +5 happiness.
  Refused: -1, or -2 if you hold no loan.

> **Mugging at the bank.** From week 4, a **1-in-31** roll on entering, but only if you are carrying
> cash. It zeroes **cash on hand only** — money already deposited is safe. That is the real reason
> the bank exists.

## 20. The Broker — VERIFIED (`broker.sc`)

- **One share per click. No commission, no spread** — buy and sell use the same price.
- **The only fee in the game is $3 when selling a T-bill**, so a $100 T-bill returns $97.
- **T-bills are fixed at $100** and never consult an index; the other five track their own.

| Instrument | Base | Index |
|---|---|---|
| T-Bills | 100 | none — fixed |
| Gold | 413 | global310 |
| Silver | 14 | global311 |
| Pork Bellies | 20 | global312 |
| Blue Chip | 49 | global313 |
| Penny Stocks | 7 | global314 |

## 21. Jones, the AI opponent — VERIFIED (`WhereShouldIGo.sc`)

Not a planner: a **memoryless, greedy, first-match-wins priority list** with **53 return points**,
re-invoked after every stop (`DialogScript.sc:71`). It returns one destination and writes "intent"
codes into three slots (global407/409/410) that the destination's own script then reads back
through `proc0_6` (`Main.sc:979`). It performs no action itself.

Priority order: **get a job → rent → clothing → food → career → work → discretionary → go home**.
Clothing outranks food because `dressedForWork` gates all earning.

- **Investing is pure momentum:** buy when the main trend is positive and the investment index is
  still below `80 + 15 × trend`; **sell everything** (global408 = -1) when the trend drops below -1.
- **No difficulty scaling exists.** Jones is identical in every game.
- Only four random choices in the whole file: study while working (1/2, line 894), buy books
  (1/3, line 974), buy an appliance (1/2, line 991), enrol at university (3/4, line 1058).

### The rule-by-rule list, thresholds and defects

Ported in full, with every line citation, in `src/Jones.Core/Model/JonesAi.cs`. The reason codes
that file asserts on are the original's own global403 values, so the port and the listing can be
read side by side.

Five defects are in the script and are replicated on purpose:

1. **The travel estimate mixes units.** `localproc_1` sums raw marble-path steps and hours
   converted to interpreter ticks; `localproc_2` divides the lot by `global475` (ticks per hour,
   `marble.moveSpeed × 14`). A path step really costs 1/14 hour at any speed, so the estimate is
   exact at the declared `moveSpeed` of 1 (`room1.sc:1084`) and **underestimates travel by that
   factor at every slower setting**, sending him further than his clock can pay for.
2. **The Pawn Shop erases his position.** The clamp at line 141 accepts places 0–11, but the Pawn
   Shop is place 12, so standing in it rewrites global400 to Low-Cost Housing and every route he
   costs is measured from the wrong end of the board.
3. **Three-stop planning skips a stop.** `localproc_4`'s guard chains as `i2 != i3 && i3 != i4`
   and never compares `i2` with `i4`, so a degenerate A → B → A route is costed as a plan and,
   being shorter, usually wins.
4. **Rule 37 never checks the clock.** `localproc_2` takes one argument; line 946 passes two, so
   the check is handed a place NUMBER instead of the journey home. He goes home to relax whenever
   relaxation is under 17, with any number of hours left.
5. **Rule 30 compares the wrong way round.** Line 811 takes the dependability gap only when it is
   *smaller* than the experience gap, so the estimate of shifts needed is only ever lowered.

Reason 222 (line 598) is **dead code**: its test is word for word rule 20's, which has already
returned for every state that can reach it.

---

## Open items

### Resolved since this list was written
- **"No Openings" formula** — `(Random 1 100) <= (dependability + experience + (degrees × 8 + 10)) / 3 + 30`
  (`employment.sc:105-120`). Truncating division. The Cook bypasses the roll entirely
  (`employment.sc:122`).
- **Broker** — one share per click, no commission, a single $3 fee on selling a T-bill. §20.
- **Bank** — no interest on savings at all, $100 cap per transfer, $5 interest per payment
  rather than on the balance, mugging zeroes cash only. §19.
- **Lottery and newspaper prices** — both `fixedPrice 1`, so the economy never touches them:
  10 tickets for $10, the paper $1 plus an extra Hour (`market.sc`).
- **Movement costs** — 170 path steps, shorter arc, 15 steps per Hour. See §1.
- **Weekend event table** — all 60 texts confirmed byte-for-byte against text resource 232.
- **Subtitle provenance — settled, and the doubt was misplaced.** A later audit reported that
  ~255 of the 533 subtitles had "no counterpart in any resource" and looked transcribed by ear.
  That audit checked only the CD's extracted text resources. The lines come from the FLOPPY
  build's resource volumes, which are present at `original/floppy` (`resource.001`,
  `resource.002`) and are read directly by `tools/build_subtitles.py` through its own SCI
  method-2 (LZW1) decoder — a decoder verified byte-for-byte against the nine text resources the
  CD kept intact. Running `python tools/build_subtitles.py --check` prints a resource-and-index
  citation for every line (`n108 text 108/2`, and so on). Nothing was transcribed by ear.

  The lesson is about the audits, not the data: an audit that searches the wrong corpus reports
  absence with just as much confidence as one that searches the right one.
- **`global374` is the player count**, and the two readings in these notes were both right.
  It is set to 1-4 by `select1b.sc:88-142` and to `size` at `room1.sc:967` — but it is also the
  economy's volatility term. `economicIndex.sc:215/220`:

      (not (Random 0 (* global374 30)))

  gates both the crash (`global373 = Random 1 3`, the severity) and the boom (`global444`), so
  the chance of either in a given player's turn is **1 in (players × 30 + 1)**.

  That scaling is deliberate rather than incidental. The economy ticks once per PLAYER TURN, not
  once per week, so a four-player game would see four times as many rolls per week; multiplying
  the range by the player count holds the per-week frequency of crashes and booms roughly
  constant however many people are playing. Both are additionally gated on `week >= 8`, with the
  crash needing `global307 >= 80` and the boom `global307 <= 120`.

### Genuinely still open
*(Nothing here is a guess; each is a question the scripts have not answered yet.)*
- **`global534`, the graphics detail level.** It gates the balloon's tail, the shop picture
  panel's animation and the newspaper's fly-in. The port has no detail setting, so it always
  takes the high-detail branch. Faithful for a default game, but the setting itself is unported.
- **Modal `Print` with buttons.** Now posed for the loan, the pawn offer and enrolment
  (`TALKER.md` §7a). Rent's two are still unasked because the two apartment-rental lines
  are not implemented at all — there is no offer there yet to accept or decline.
- **Clip 514** and the 435/436 pair, below, are the only subtitle lines still in doubt.
- **Clip 514** — the recording matches neither build's text for that headline.
- **Clips 435 and 436** — the two rejection reasons cannot be told apart by duration, so the
  assignment between them is an assumption.
