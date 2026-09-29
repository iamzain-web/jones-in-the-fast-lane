# Behaviour audit — preconditions and rules

The counterpart to `STRINGS.md`. Every player-facing **decision** the port makes, checked
against the decompiled script that makes it. Text is out of scope here.

`MECHANICS.md` was used only as a cross-check. Where it disagrees with a script the script
wins, and the disagreement is noted.

**Totals: 18 INVENTED RULES, 38 MISSING RULES, 9 WRONG VALUES.**

Classification:

- **INVENTED RULE** — the port enforces something the source does not.
- **MISSING RULE** — the source enforces something the port does not.
- **WRONG VALUE** — right rule, wrong number or wrong comparison.
- **OK** — verified matching.

---

## 0. The headline: what the clock actually gates

There are 45 `global323` references in the scripts. `global323` counts hours **used**, 0..60,
clamped to 60 at `room1.sc:1494-1495`. Sorted by what they do:

| Site | Gates | What happens INSTEAD |
|---|---|---|
| `n108.sc:20` | working a shift | speech `global400 + 960` |
| `employment.sc:167` | applying for a job | `global433 = 5` → clip 425 |
| `university.sc:193` | taking a lesson | clip 402 |
| `market.sc:342` | buying/reading the newspaper | clip 578 |
| `bank.sc:341` | applying for a loan | clip 323 |
| `bank.sc:417` | seeing the broker | clip 324 |
| `lowcost.sc:119`, `security.sc:127` | the *benefit* of relaxing | clip 210 / 370 — **the 6 hours are still spent** |
| `room1.sc:141` | clicking a Place while the marble is walking home | nothing |
| `Menu.sc:282` | — | the End Turn menu item *sets* it to 60 |
| `room1.sc:1484-1516` | — | the clock itself |
| `appliance.sc:523`, `bank.sc:475`, `clothing.sc:278`, `discount.sc:761`, `factory.sc:161`, `fastFood.sc:328`, `market.sc:401`, `rentOffice.sc:509`, `university.sc:727` | the work button's **timeclock cel animation** only | nothing; the shift already resolved in `proc108_0` |
| `DialogScript.sc:21/44/71`, `lowcost.sc:183`, `security.sc:201`, `market.sc:541`, `broker.sc:770`, `employment.sc:591`, `WhereShouldIGo.sc:41/1088/1107` | the Jones AI's own planner | Jones picks something else |
| `Game.sc:145/188`, `room1.sc:51/218/280` | walker visibility and background music | nothing |

**Nine gates, seven of which speak a line.** Nothing else. `CostDItem::doit`
(`WButton.sc:197-253`) — the buy action behind every shop shelf — contains no clock test at
all. The blanket "disable everything at 60 hours" rule was invented, and it is now removed
(`MainViewModel.cs:1164-1192`, `:1879-1901`).

But the removal over-corrected in two directions: four of the nine real gates are **still
missing** from the port, and two gates the port kept are enforced more harshly than the
source does.

---

## 1. Clock gates

### 1.1 Job application — no clock gate at all — **MISSING RULE**

`employment.sc:164-241`. `JobDItem::doit` opens `(if (!= global323 60) … else (= global433 5))`.
Clip 425 is "we're closing, come back next week".

Port: `Game.ApplyFor` (`src/Jones.Core/Game.cs:196`) and the handler at
`src/Jones.App/Jones.App/ViewModels/MainViewModel.cs:1339` test nothing. You can apply for
jobs all night. `MainViewModel.cs:1893` *documents* the gate and does not implement it.

### 1.2 Job application costs 4 hours — **MISSING RULE**

`employment.sc:78` `visitTime 4`; `employment.sc:168` `(gTimeKeep doit: visitTime)` fires
**before** `qualify:` and therefore whether you are hired, refused or merely asking for a
raise. Four hours per counter visit is the whole reason the Employment Office is expensive.

Port: no `Clock.Spend` anywhere on the application path. The only `Clock.Spend` in
`MainViewModel` is the newspaper's, at `:1819`.

### 1.3 Newspaper — no clock gate, and half the hours — **MISSING RULE / WRONG VALUE**

`market.sc:341-365`. Gated on `(!= global323 60)` → clip 578. On success the hours are
charged **twice**: `visitTime 1` (`market.sc:333`, spent by `WButton.sc:251`) *plus* an
explicit `(gTimeKeep doit: 1)` at `market.sc:346`. A paper costs **2 hours**, not 1.

Port: `MainViewModel.cs:1827-1831` calls `Buy` unconditionally and spends
`Catalogue.NewspaperVisitHours`, which is `1`.

### 1.4 Loan application and broker — **MISSING RULE**

- `bank.sc:341-342`: pressing *Apply For Loan* is gated, and costs **2 hours per press**
  regardless of the answer. Refused: clip 323.
- `bank.sc:417-421`: *See The Broker* is gated, and costs **2 hours once per bank visit**
  (`local0` latch). Refused: clip 324.

Port: `MainViewModel.cs:1755-1760` and `:1762-1767` charge nothing and test nothing. The
comment at `:1764` claims the cost that is not taken.

### 1.5 Relaxing refuses instead of wasting the hours — **INVENTED RULE**

`lowcost.sc:115-132` / `security.sc:123-141`: the clock is read into `temp1` **first**, then
`(gTimeKeep doit: 6)` runs **unconditionally**, and only then does `(if (== temp1 60))`
choose between the speech and the benefit.

Port: `Game.Relax` (`src/Jones.Core/Game.cs:172`) returns `false` on `Clock.TurnOver` and
spends nothing. Harmless today because the clock clamps at 60, but it is the wrong shape and
it will diverge the moment anything reads the spend.

### 1.6 Travel to a shut building is refused — **INVENTED RULE**

`Place::cue` (`room1.sc:174-209`) calls `((ScriptID sNumber 0) init: room1)`
**unconditionally**. A "closed" building simply skips the door animation (`global516 = 0`)
and shows a shut-office picture (`rentOffice.sc:101-107`, background view 697). You always
walk in and you always pay the 2 hours.

Port: `src/Jones.Core/Game.cs:130` refuses the journey outright.

### 1.7 Verified matching

- **OK** — `Employment.Work` (`src/Jones.Core/Model/Employment.cs:245`) tests
  `clock.TurnOver` **before** `DressedForWork`, matching `n108.sc:20-22` exactly.
  `global323` is clamped to 60, so `!= 60` ≡ `HoursRemaining > 0`.
- **OK** — `University.Study` (`src/Jones.Core/Model/University.cs:58`) matches
  `university.sc:193`. (Ordering nit in §5.4.)
- **OK** — the pawn shop is not clock-gated at all; `global323` does not appear in
  `pawnShop.sc`. The port charges no time there either.
- **OK** — every shop's buy button is ungated in both.

---

## 2. Travel, the board and entering

### 2.1 The marble takes the SHORTER arc — **INVENTED RULE**

`MarblePath::setDirection` (`marblePath.sc:86-104`) compares the two arcs against
`(/ [local0 0] 2)` = 85 and walks whichever way is shorter. Travel is bidirectional.

`src/Jones.Core/Model/Board.cs:105-111` (`StepsBetween`) wraps forward only, and its own doc
comment asserts the opposite of the source: *"going 'back' one shop means walking all the
way round"*. This is the rule that prices every journey.

The port already has this right elsewhere — `src/Jones.Core/Model/MarblePath.cs:54-73`
(`Route`/`Distance`) implements the shorter arc correctly and drives the animation. So the
marble **walks the short way while the clock charges the long way**.

### 2.2 Path length — **WRONG VALUE**

`marblePath.sc:13`: element 0 of the table is the count, **170**. Indices run 1..170.

`Board.cs:87` `PathLength = 175`, marked `[UNVERIFIED]` and inferred from the highest Place
index. `MarblePath.cs:13` already has `StepCount = 170`; the two constants contradict.

### 2.3 Hours per lap — **WRONG VALUE**

The clock does not know about journeys. `room1.sc:1374-1377` calls `gTimeKeep doit:` with no
argument once per room cycle while the marble has a mover; `room1.sc:1488-1491` ticks an hour
when `(> (++ global324) global475)`. `global475 = (marble moveSpeed:) * 14` (`room1.sc:1319`)
and `marble moveSpeed 1`, so **15 cycles per hour**; `marblePath.sc:42-46` advances one path
step per cycle. That is **15 path steps per hour**, and a full 170-step lap is ≈11.3 hours.

`Board.cs:93` `HoursPerLap = 10` over 175 steps = 17.5 steps/hour. Also `[UNVERIFIED]`.

(Note `Menu.sc:317` `(= global475 (* (- 7 temp4) 14))` — the animation-speed slider changes
the travel cost. Unmodelled; arguably out of scope.)

### 2.4 Journeys are rounded and floored at 1 hour — **INVENTED RULE**

`Board.cs:117-123` rounds each journey to a whole hour and floors it at 1. The source has no
per-journey arithmetic at all: `global324` is a sub-hour accumulator that **persists across
the whole turn** and is zeroed only at turn start (`room1.sc:1094`). A two-step hop can
therefore cost nothing, and three short hops can cost one hour between them.

### 2.5 Re-entering the building you are standing in is free — **MISSING RULE**

`room1.sc:164-167`: clicking your current Place calls `self cue:` directly, which opens the
dialog, whose `init` runs `(gTimeKeep doit: 2)` like any other. Stepping out and back in
costs 2 hours.

Port: `src/Jones.Core/Game.cs:131` returns `true` with no charge, and
`MainViewModel.cs:1064-1072` rebuilds the panel for free. `GameClock.cs:15` documents the
rule ("every time") that the code then skips.

### 2.6 Verified matching

- **OK** — 2 hours to enter, in every building: `appliance.sc:88`, `bank.sc:68`,
  `clothing.sc:76`, `discount.sc:139`, `employment.sc:294`, `factory.sc:46`,
  `fastFood.sc:78`, `lowcost.sc:40`, `market.sc:92`, `pawnShop.sc:435`, `rentOffice.sc:59`,
  `security.sc:40`, `university.sc:337`. `GameClock.cs:16` / `Board.cs:129-130`.
- **OK** — the marble path coordinate tables (`MarblePath.cs:16-41`) are verbatim.

---

## 3. Opening rules

### 3.1 The Rent Office rule has three clauses, not one — **MISSING RULE**

`room1.sc:176-193`:

```
(and (== placeNum 1)
     (or (== (global302 worksAt:) 1)
         (not (mod global372 4))
         (global302 leaveOpen:)))
```

and the same three at `rentOffice.sc:66-71`.

`src/Jones.Core/Model/Board.cs:133-134` has only `week % 4 == 0`. Consequences:

- A player **employed at the Rent Office** is locked out of their own job for three weeks in
  four.
- `leaveOpen` — set at turn end from `triedExt == 1`, i.e. a **successful** extension request
  (`room1.sc:806`) — is dead. `Player.LeaveOpen` exists (`Player.cs:55`) and `JonesAi.cs:542`
  reads it, so Jones plans a visit that `Game.cs:130` then refuses
  (`JonesTurn.cs:52` breaks the loop).

### 3.2 You cannot relax in an apartment you do not live in — **INVENTED RULE**

`lowcost.sc:49-50` adds `relaxButton` only when `livesAt == 0`; `security.sc:49-50` only when
`livesAt == 2`. Visiting the other apartment shows a different background (view 699 / 698)
and no relax button at all.

`MainViewModel.cs:1289-1290` draws the relax button at **either** apartment regardless of
`LivesAt`, and `Game.Relax` (`Game.cs:171`) accepts either.

Note also that the source's `livesAt` values are **0 and 2**, not 0 and 1
(`security.sc:47/49`, `rentOffice.sc:394/437`). The port treats "not 0" as Security, which is
currently equivalent but is not what the scripts store; `JonesAi.cs:275-279` already
documents 0/2.

### 3.3 Verified matching

- **OK** — no other location is ever closed.
- **OK** — closure is cosmetic in both (the port's `Game.Relax` location test reaches the
  same place as the source's button-add), apart from §1.6 and §3.1.

---

## 4. Requirement checks

### 4.1 The uniform-affordability gate on a human application — **INVENTED RULE**

`src/Jones.Core/Model/Employment.cs:128-129` refuses outright:

```csharp
if (!CanAffordUniform(p, job, goodsIndexReading))
    return JobOutcome.CannotAffordUniform;
```

`localproc_0` (`employment.sc:60-73`) — the `netWorth >= uniform + 65` test — is called from
**exactly one place**: `employment.sc:627`, inside the *computer player's* job-search loop
(`computerScript`, state 2). `JobDItem::doit` goes straight to `qualify:`. A human is never
tested for it.

`Game.cs:202-205` and `MainViewModel.cs:630-633` both already state this in comments. The
gate is still live and still first in the chain, so a poor player is refused for a reason the
game does not have and gets no line at all.

### 4.2 `turnedDown` is recorded on every refusal — **INVENTED RULE**

`employment.sc:232-235`:

```
(if (and global325 global326 global327 (not global328))
    (self turnedDown: jobNum))
```

The job is blacklisted for the turn **only** when you were fully qualified and lost the
openings roll. Failing on education, dependability or experience does not blacklist it.

`Employment.cs:182` calls `turnedDown.Refuse(job.JobNum)` on every refusal path. Because a
degree can be bought mid-turn at Hi-Tech U, this really does block an application the source
would allow.

### 4.3 The whole raise path is missing — **MISSING RULE**

`employment.sc:171-197`: when `worksAt == global419 && occupation == indexNum`, applying to
the job you already hold is a **raise request**, with four outcomes:

| Condition | `global433` | Effect |
|---|---|---|
| `wage > price` | 0 | nothing (you are already paid above the board rate) |
| `wage == price` | 1 | **nothing** |
| `dependibility >= dependibility + 5 * raises` | 2 | `proc0_13 3`, `raises++`, `wage: price` |
| else | 3 | nothing |

`Employment.AskForRaise` exists (`Employment.cs:310-321`) but **nothing calls it** — there is
no `AskForRaise` reference anywhere in `Jones.App`. Re-applying to your own job goes through
`Employment.Apply`, which never checks whether you already work there, so it re-hires you:
`Raises` reset to 0, `+2` experience, `+3` happiness, `MaxExper` recomputed. That is a free
stat pump the game does not offer.

Within `AskForRaise` itself: `Employment.cs:313` `if (p.Wage > current) return false;` leaves
the `wage == price` case (`global433 = 1`) falling through to the dependability test, so an
equal-wage request can still grant a raise. **MISSING RULE.**

### 4.4 `needEd1` / `needEd2` are never cleared — **MISSING RULE**

`employment.sc:94-97` sets both on **every** `qualify:` call, to 0 when education passed:

```
(global302 needEd1: (if global325 0 else education)
           needEd2: (if global325 0 else education2))
```

`Employment.cs:187-188` writes them only inside the `!hasEducation` refusal branch, so a
successful application leaves stale values behind.

### 4.5 `notEnoughEd` is never cleared — **MISSING RULE**

`university.sc:207` `(global302 notEnoughEd: 0)` runs on every graduation. The port's only
writes to `Player.NotEnoughEd` are `Game.cs:214` and `JonesAi.cs:433`, both setting it true.
Once set it is permanent, and it is read at `JonesAi.cs:811/820/965` (porting
`WhereShouldIGo.sc:789/803/1059`), so a graduate is routed back to school for ever.

### 4.6 Enrolment is charged against net worth, not cash — **WRONG VALUE**

`university.sc:471` `(>= (proc0_11) price)` and `WButton.sc:201` both use `proc0_11`
(`Main.sc:1071-1082`), which returns cash. `src/Jones.Core/Model/University.cs:37` uses
`p.NetWorth`. A player with $10 cash and a $600 computer enrols free and goes to −$40.

### 4.7 `Enrollments` is turned into a consumable — **INVENTED RULE**

`University.cs:65` `p.Enrollments--`. The source never decrements it — the only write is
`university.sc:472` `(+ … 1)`. The gate arithmetic happens to come out the same, but the
stored value is now a different quantity and is read raw at `JonesAi.cs:880` and `:964`
(porting `WhereShouldIGo.sc:902/1054/1091` and `university.sc:829-846`), where it is wrong.

### 4.8 Verified matching

- **OK** — `JobDItem::qualify` (`employment.sc:87-130`) against `Employment.cs:131-150`:
  degree pair, the `dependibility == 10` subtract-to-nothing (`employment.sc:101`), the
  experience test, the openings formula `(dep + exp + numDegrees*8 + 10)/3 + 30`
  (`employment.sc:105-120` ↔ `Employment.cs:76-80`; the wiki's 66% is wrong, the source
  gives 62%), the `turnedDown:` call ordering, and the Cook bypass `indexNum 44`
  (`employment.sc:122-124` ↔ `Jobs.CookOccupationId`).
- **OK** — the roll is consumed on every application, including raise requests, because
  `qualify:` runs before the `cond` (`employment.sc:169`).
- **OK** — `maxExper` on hire: `(+ experience 10 (if (not experience) 10 else 0))`
  (`employment.sc:203-204`) ≡ `Employment.cs:171`.
- **OK** — `minDepend` = the job's requirement verbatim (`employment.sc:209`), the +2
  experience and the dependability floor of 10 (`employment.sc:202/210-215`).
- **OK** — sacking at `dependibility < minDepend - 5`, costing no time and paying nothing
  (`n108.sc:28-36` ↔ `Employment.cs:250-256`). *(The source also sets `jobT: -1`; the port
  has no `JobT` field. Cosmetic — `jobT` only feeds the AI's job picker.)*
- **OK** — `proc206_1`'s rejection draw, including the no-draw-on-openings case
  (`employment.sc:30-49` ↔ `Employment.cs:207-229`).
- **OK** — `DressedForWork` / `weeksOfClothing` (`room1.sc:685-712` ↔ `Player.cs:151-179`).
- **OK** — the degree prerequisite tree (`university.sc:480-634` ↔ `Degrees.cs:37-48`) and
  `addCourse`'s visibility rule (`university.sc:162-172` ↔ `Degrees.cs:63-64`).
- **OK** — the enrolment gate arithmetic itself (`university.sc:179-191`): no cash test, no
  clock test, no prior-degree test on that path.

---

## 5. Hour costs, one by one

| Action | Script | Source | Port | Verdict |
|---|---|---|---|---|
| Enter any building | 13 `init`s, e.g. `bank.sc:68` | 2 | 2 | OK |
| Re-enter where you stand | `room1.sc:164-167` → `init` | 2 | **0** | MISSING (§2.5) |
| Work a shift | `n108.sc:116` | 6, after pay is computed | 6 | OK |
| Job application | `employment.sc:78/168` | **4**, before `qualify:`, always | **0** | MISSING (§1.2) |
| Study a lesson | `university.sc:159/204` | 6, inside both gates | 6 | OK |
| Relax | `lowcost.sc:118`, `security.sc:126` | 6, unconditional | 6, refused at 60 | INVENTED (§1.5) |
| Newspaper | `market.sc:333` + `:346` | **2** (1 + 1) | 1 | WRONG (§1.3) |
| Apply for loan | `bank.sc:342` | 2 per press | 0 | MISSING (§1.4) |
| See the broker | `bank.sc:419` | 2 per bank visit | 0 | MISSING (§1.4) |
| Doctor visit | `startTrn.sc:1035` (register 3, set at `:464`) | **10** | 0 | MISSING (§6.1) |
| Starvation | `startTrn.sc:1041` (register 0, set at `:432`) | **20** | 0 | MISSING (§6.1) |
| Enrolment fee | `WButton.sc:173` (`visitTime 0`) | 0 | 0 | OK |
| Pawn / redeem / buy off the rack | `pawnShop.sc` | 0 | 0 | OK |
| Bank deposit / withdraw | `bank.sc:172-279` | 0 | 0 | OK |
| Broker buy / sell | `broker.sc:504-599` | 0 | 0 | OK |
| Pay rent / extension / move | `rentOffice.sc` (`visitTime 0`) | 0 | 0 | OK |
| Any shop purchase | `WButton.sc:173/251` | `visitTime` = 0 everywhere but the newspaper | 0 | OK |
| Failed purchase | `WButton.sc:251` | still charges `visitTime` | 0 | MISSING (§7.2) |

`MECHANICS.md §1` is wrong where it lists which actions survive the clock, and `GameClock.cs`
carries a `DoctorVisitCost = 10` constant that nothing uses.

---

## 6. Turn start

### 6.1 Neither the doctor nor starvation costs any hours — **MISSING RULE**

`startTrn.sc:432` sets the starvation notice (register 0) and `startTrn.sc:1041` charges
**20 hours**. `startTrn.sc:464` sets the doctor notice (register 3) and `startTrn.sc:1035`
charges **10 hours**. Losing a third of your week to going hungry is the strongest incentive
in the game to keep food in the fridge.

`src/Jones.Core/Model/TurnStart.cs:137-165` charges neither, and `TurnStart.Run` takes no
clock.

### 6.2 The doctor's bill is capped on cash, not net worth — **WRONG VALUE**

`startTrn.sc:447-454` uses `proc0_11` — **cash** — three times:

```
(= temp1 200)
(if (< (proc0_11) 500) (= temp1 50))
(if (< (proc0_11) temp1) (= temp1 (proc0_11)))
(if (and (> (proc0_11) 0) (not global553)) …)
```

`TurnStart.cs:154-161` uses `p.NetWorth` for all three. A player with a house full of
appliances and $20 in hand is charged as though wealthy, and one with cash but negative net
worth escapes the doctor entirely.

### 6.3 The bailout for a destitute, naked player is missing — **MISSING RULE**

`startTrn.sc:852-878` (state 34): when `weeksOfClothing == 0` **and** net worth < 300 **and**
cash < 300, `nakedCount` increments; on the **second** such turn it resets and the player is
**given** `proc109_0(goodsIndex, uniformPrice) + Random(1,100)`, where `uniformPrice` is
295/125/73/50 by `uniform` (`startTrn.sc:863-872`). Notice register 12.

No `nakedCount` exists anywhere in the port.

### 6.4 Verified matching

- **OK** — the state ordering, which `TurnStart.cs:9-14` documents and gets right.
- **OK** — lottery, computer income, relaxation decay, Wild Willy at home, meal choice,
  spoilage, perishable expiry, the three doctor routes (`startTrn.sc:428-446`), consumable
  tick-down, rent, clothing, loan states, appliance breakage, crash fallout.

---

## 7. Affordability, stock and the buy path

### 7.1 `cash >= price` — **OK**

`WButton.sc:201` `(>= (proc0_11) price)` ↔ `src/Jones.Core/Game.cs:225`
`if (price > p.Cash) return false;`. Same comparison, same inclusivity. The `cashHi → 32767`
ceiling and the `cash < 0 → 0` floor of `proc0_11` are both unreachable given shipped prices
and `Pricing.Price`'s floor of 1.

### 7.2 A failed purchase still costs its `visitTime` — **MISSING RULE**

`WButton.sc:251` `(gTimeKeep doit: visitTime)` sits **after** the if/else and runs on both
branches. `Game.cs:225` returns before anything is charged. Only the newspaper has a nonzero
`visitTime`, so this is the newspaper again.

### 7.3 `pricePaid` is overwritten while the item is in hock — **MISSING RULE**

`WButton.sc:229-231`:

```
(if (not (& (global418 attributes:) $0038))
    (global418 pricePaid: price))
```

Buying a replacement for something you have pawned must **not** overwrite `pricePaid`,
because `redemptionPrice` is derived from it. `Game.cs:240` writes it unconditionally.

### 7.4 Shakes and colas are stored — **WRONG VALUE**

`fastFood.sc:256` and `:280` declare `typeOfGoods 3`, which falls through the switch at
`WButton.sc:203-226` and lands in no list — the same status as the newspaper.
`Catalogue.cs:169-170` types both `GoodsType.Consumable`, so `Game.cs:236-239` stores ids 6
and 7 in the player's consumables, where nothing should ever be.

### 7.5 Verified matching

- **OK** — all 18 Z-Mart lines, all 9 Socket City lines, all 3 QT Clothing lines and all 5
  Black's Market lines: `indexNum`, `typeOfGoods`, `units`, `basePrice` (`Catalogue.cs:79-150`).
- **OK** — `fixedPrice 1` on exactly the lottery tickets and the newspaper
  (`market.sc:303/332`), and on the pawn shop's redeemables (`pawnShop.sc:219/309`).
- **OK** — `theSign -1` on every shop CostDItem (class default, `WButton.sc:169`).
- **OK** — no maximum quantity and no repeat-purchase limit anywhere; `recieve:` →
  `hasType:` just accumulates (`Goods.sc:46-73`).
- **OK** — no per-turn "used this shop" latch exists. `global403` is the AI's destination
  code, not a shop latch.
- **OK** — Z-Mart shows a random 6 of 18, rerolled once per turn and stable within it
  (`discount.sc:170-179`, `startTrn.sc:187-189` ↔ `MainViewModel.cs:923`).
- **OK** — the 1-in-36 (Z-Mart) vs 1-in-51 breakage rates: `$0100` is set at
  `discount.sc:101` and `pawnShop.sc:332` only, and `startTrn.sc:593-606` rolls
  `Random 0 35` / `Random 0 50`.
- **OK** — `Pricing.Price` reproduces `proc109_0` including the two-stage truncation, the
  16-bit overflow, the index floor of 50 and the price floor of 1.
- **OK** — the shop cash display showing `cash - 1` (`discount.sc:210` and six siblings ↔
  `StoreLayout.cs:217`).

---

## 8. Happiness

### 8.1 Happiness is never clamped — **MISSING RULE**

`proc0_13` (`Main.sc:1102-1111`) clamps to **0..100** on every single adjustment.
`src/Jones.Core/Model/Player.cs:27` is a bare `int` and no call site clamps —
`Game.cs:232/249/294/300`, `Employment.cs:178/183`, `University.cs:96`,
`Investments.cs:186/192/228`, `TurnStart.cs` throughout, `Weekend.cs`. Every one can run out
of range, and `HasWon()` compares `HapStat` against a goal.

### 8.2 `monStat` has no upper clamp — **MISSING RULE**

`proc0_10` (`Main.sc:1063-1068`) clamps `monStat` to 0..100 on every cash movement.
`Player.cs:219-220` clamps only the lower bound.

### 8.3 The first-durable happiness bonus does not exist — **INVENTED RULE**

Every durable's `proc0_13 1/2/3` in `discount.sc` and `appliance.sc` is guarded by
`(not ((global302 durables:) objectAtIndexQuan: indexNum))` and runs **after**
`(= temp0 (super doit:))`, which has already called `recieve:`. `ListOfGoods::recieve` →
`hasType:` (`Goods.sc:46-73`) adds the units first, so the guard is evaluated when the
quantity is always ≥ 1. **No durable purchase in the shipped game has ever awarded a point of
happiness.** Dead at `discount.sc:294/327/360/393/431/456/609/634/659` and
`appliance.sc:212/246/280/314/348/382/416/450/484`.

`Game.cs:237/249` samples `alreadyOwned` *before* `Receive` precisely so the branch fires.
Sampling it after — as the source does — makes it unreachable, which is the faithful result.

### 8.4 Thirteen live per-item happiness rules are missing — **MISSING RULE**

These use latch globals rather than quantity, so they do fire. The latches reset **every
turn** at `startTrn.sc:176-178`, and several are **shared**.

| Item | Script | Points | Latch |
|---|---|---|---|
| Baseball tickets | `discount.sc:526-529` | +2 | `global466` |
| Theatre tickets | `discount.sc:550-553` | +2 | `global467` |
| Concert tickets | `discount.sc:574-577` | +2 | `global468` |
| Food, 1 week | `market.sc:217-220` | +1 | `global470` |
| Food, 2 weeks | `market.sc:250-253` | +2 | `global470` (shared) |
| Food, 4 weeks | `market.sc:283-286` | +4 | `global470` (shared) |
| Lottery tickets | `market.sc:313-316` | +2 | `global469` |
| Cheeseburgers | `fastFood.sc:200-203` | +1 | `global472` |
| Astro Chicken | `fastFood.sc:224-227` | +2 | `global472` (shared) |
| Shakes | `fastFood.sc:263-266` | +2 | `global471` |
| Colas | `fastFood.sc:287-290` | +1 | `global471` (shared) |
| Business suit | `clothing.sc:184-192` | +2 | none — every purchase |
| Leisure suit | `clothing.sc:210-218` | +1 | none — every purchase |

None are in the port.

### 8.5 Verified matching

- **OK** — the junk penalties: dog food −1 (`discount.sc:680`), 8-track −1 (`:701`),
  Capote −2 (`:722`), all unlatched, ↔ `Game.cs:232`.
- **OK** — pawning always costs 1 (`pawnShop.sc:685` ↔ `Game.cs:294`).
- **OK** — hire +3 (`employment.sc:229`), graduation +5 (`university.sc:205`), relax +2 once
  per turn (`lowcost.sc:122-125` ↔ `_relaxedThisTurn`).
- **OK** — Z-Mart clothes, hamburgers and fries award nothing in either.

---

## 9. Pawn shop

- **OK** — the offer `(/ (* (proc109_0 global309 pricePaid) 4) 10)` (`pawnShop.sc:664`), the
  redemption price `(/ pricePaid 2)` on the raw price (`:688`), the `$0018` ticket bits
  (`:687`), the quantity decrement (`:689`), the global 6-item hock cap across all players
  (`:655`, `localproc_4` at `:125-146`), the `$ffc7` clear on redeem (`:837`), and the
  all-players scan of the resale rack (`:267-270`).
- **MISSING RULE** — the *Take It / Leave It* confirmation (`pawnShop.sc:671-682`). The port
  accepts unconditionally (`MainViewModel.cs:1541-1545`, which acknowledges this).
- **WRONG VALUE** — the fridge rule (`pawnShop.sc:691-698`) uses `objectAtIndex:`, which
  needs the entry only to **exist**. `Game.cs:298` uses `AtHeld`, which requires
  `Quantity > 0`, so the extra −1 happiness and the food wipe are skipped in the normal state
  where the food entry exists at quantity 0.
- **MISSING (original bug)** — `pawnShop.sc:865-867` calls `hasType: indexNum 1` as a *test*,
  but `hasType:` (`Goods.sc:63-73`) **increments** the matched quantity as a side effect. So
  buying a second-hand durable you already own quietly hands you **two** units for one
  payment. `Game.cs:353` uses a pure test. Per `CLAUDE.md` rule 4 this should be reproduced
  with a comment, not silently fixed.
- **MINOR** — `pawnShop.sc:331-333` stamps `$0100` at list-build time, so merely *opening*
  the Buy list makes every forfeited durable in the game flimsy for ever. The port stamps
  only on purchase.

---

## 10. Rent office

- **INVENTED RULE** — `src/Jones.Core/Game.cs:401`
  `var owed = p.RentOwed > 0 ? p.RentOwed : p.CurRent;`. `payRent` always charges `curRent`
  (`rentOffice.sc:74`); arrears are a **separate button**, `payGarnishment`
  (`rentOffice.sc:448-473`, price = `rentOwed`, `fixedPrice 1`).
- **INVENTED RULE** — `Game.cs:405` `p.RentOwed = 0;`. `payRent` never touches `rentOwed`
  (`rentOffice.sc:209-222`). Arrears clear only via `payGarnishment` or wage garnishment
  (`n108.sc:93-112`).
- **INVENTED RULE** — `Game.cs:406` `p.TurnedOver = false;`. `turnedOver` is cleared by the
  turn-start rent notice (`startTrn.sc:486`), which the port already does at
  `TurnStart.cs:182`. Doing it again re-arms arrears accrual inside the same month.
- **WRONG VALUE** — `Game.cs:409` `Receive(rentId, 4)`. The source buys
  **`4 - (week mod 4)`** weeks (`rentOffice.sc:210`), so a `leaveOpen` visit outside a rent
  week buys 3, 2 or 1.
- **MISSING RULE** — the extension (`moreTime`, `rentOffice.sc:227-312`) is entirely absent.
  Precondition: holding **zero** weeks of rent (`:237`), else clip 191. Success odds from
  `rentExt` (`:240-253`): `-1` never (garnishment voids it, `n108.sc:111`); `0` always;
  `1` → `Random(1,12) > 3`; `2` → `> 6`; `≥3` → `> 9`. Success: `rentExt++`, `proc0_13 1`,
  `triedExt = 1`. Failure: `proc0_13 -1`, `triedExt = 2`. `Player.RentExt` is written once
  (`Employment.cs:293`) and never read.
- **MISSING RULE** — `JonesTurn.cs:115` sets `p.TriedExt = 1` unconditionally, so Jones's
  extension always succeeds.
- **MISSING RULE** — `payGarnishment`, `rentLowCost` and `rentSecurity` (moving house:
  blocked if you already live there, forfeits the remaining weeks on the old apartment,
  `rentOffice.sc:314-446`) are all absent.
- **MISSING RULE** — `room1.sc:814`: the source's non-rent-week branch can also accrue
  `rentOwed` once, when you hold no rent weeks, have no extension and owe nothing yet.
  `Player.cs:244-259` only does it in the `week % 4 == 0` branch.

---

## 11. Bank and broker

- **OK** — the loan offer `(wage + lqAss/1000) - (5 + latePay + loanBal/100 + (loanBal?1:0))`,
  times 100 (`bank.sc:345-360` ↔ `Investments.cs:169-178`); repayment $50 out / $45 off
  (`bank.sc:306-311`); the sub-$50 final payment being interest-free; `paySched < 0` blocking
  all future borrowing (`bank.sc:358`).
- **OK** — savings: no minimum, **$100 maximum per click** (`bank.sc:188-190`, `:239-251`),
  unlimited clicks, no hour cost, **no interest** (the only `bankBal` writes in the game are
  `bank.sc:28` and the severity-1 crash at `room1.sc:828`).
- **OK** — broker: one unit per click, no minimum, no maximum, no per-turn limit
  (`broker.sc:504-599`). `global484`/`global485` are AI latches, not player limits.
- **WRONG VALUE** — `Investments.cs:186` `p.HapStat -= p.LoanBal > 0 ? 1 : 2;` fires whenever
  the offer is ≤ 0, and its comment calls it a refusal penalty. In the source that penalty is
  for **ineligibility** (`bank.sc:389-395`: `proc0_13 -1`, plus a second `-1` when debt-free);
  the player **declining** an offer costs nothing (`bank.sc:383-386`).
- ~~**MISSING RULE** — there is no Yes/No prompt on the loan at all
  (`bank.sc:373`, `Format 204 1`), so declining is impossible.~~ Now asked in the balloon
  (`TALKER.md` §7a); declining costs nothing, and the ineligible case still skips the
  question entirely as `bank.sc:389-395` does.
- **MISSING RULE** — bank mugging **timing**. `bank.sc:120-133` rolls in `init` but zeroes the
  cash **at dialog exit**; `room1.sc:233-239` then plays the cutscene. `Investments.cs:222-230`
  (via `Game.cs:141-142`) takes the money **on arrival**, so money you bank during the visit
  is safe in the original and already gone in the port. Real gameplay effect.
- **OK** — `Investments.cs:224` `week < 4` ↔ `(>= global372 4)`; `rng.Next(0, 30) != 0` ↔
  `(not (Random 0 30))`.
- **INVENTED RULE (cosmetic)** — `MainViewModel.cs:1748` hides the Loan Payment line when
  `LoanBal == 0`. `bank.sc:73-88` always shows it at price 0 and speaks clip 316.

---

## 12. University, remaining items

- **INVENTED RULE** — extra credit is recomputed on every durable purchase
  (`Game.cs:250/304/367`). `startTrn.sc:190-200` recomputes `xcred` **only at turn start**, so
  in the original a computer bought mid-course counts from the *next* turn. The comment at
  `University.cs:74-79` describes the port's behaviour, not the game's. (`Degrees.cs:97`
  cites `room1.sc:191`, which is a door-open test; the real site is `startTrn.sc:190-200`.)
- **INVENTED RULE** — `University.cs:104` `p.CoursesDone++`. In the source `coursesDone` is a
  one-shot dialogue flag set at `university.sc:392-396` the first time you enter with no
  courses available, gating clip 404. It is never touched on graduation.
- **INVENTED RULE** — the lessons-remaining number is drawn for courses never started.
  `localproc_3` (`university.sc:110-114`) bails when `objectAtIndex:` returns 0;
  `Degrees.cs:91` returns a value and `MainViewModel.cs:1684-1686` prints it.
- **INVENTED RULE (benign)** — `MainViewModel.cs:1655` `.Take(4)`. `localproc_2`
  (`university.sc:85-101`) caps nothing; the prerequisite tree happens to top out at 4.
- **MISSING RULE** — ordering. The source tests the enrolment gate (`university.sc:179-191`)
  **first** and the clock (`:193`) second; `University.cs:58` tests the clock first, so an
  unenrolled player with no hours gets the wrong branch and the wrong clip.
- ~~**MISSING RULE** — the enrol Yes/No `Print` (`university.sc:670-683`, text 207[1]).
  enrols unconditionally.~~ Now asked in the balloon (`TALKER.md` §7a): the clerk speaks
  clip 406, the short text 207[1] goes in the `#width 113` Yes/No box, and declining
  speaks clip 408 and charges nothing.
- **MISSING RULE** — the first-draw display quirk: `university.sc:121-123` shows **1** for a
  course whose remaining has gone ≤ 0, on the first draw after entering (`local17`, set at
  `:324`, cleared at `:175`), and the window `(<= 0 temp2 (- unitsToGraduate xcred))` at
  `:124-128`.
- **MISSING RULE** — the diploma dialog (`university.sc:223` → `diploma.sc:34`) and songs
  41/42 on graduation.
- **Note** — `typeOfGoods 2` exists exactly once in the game
  (`university.sc:157`, on `UniversityDIcon`), and `UniversityDIcon` is a `DIcon`, not a
  `CostDItem`. **`WButton.sc:214-225` is dead code in this build.** The live clamp is
  `university.sc:196-202`, and the port reproduces it correctly (`University.cs:76/94`).
- **OK** — `unitsToGraduate 10` (`Goods.sc:35-39`), the `xcred` formula (max 2,
  `startTrn.sc:190-200` ↔ `Degrees.cs:100-106`), `eduCredit`/`expCredit`/`dependibility` +5
  each on graduation (`university.sc:205-213` ↔ `University.cs:97-103`), `eduStat = 1 + 9 *
  numDegrees` (`university.sc:280`), the enrolment fee base 50 through the goods index
  (`university.sc:463/467`), courses being free, and the course-icon layout.

---

## 13. Elsewhere

- **OK** — `Player::endTurn` (`room1.sc:788-822` ↔ `Player.cs:234-267`), including reading
  `LeaveOpen` before clearing `TriedExt`, apart from §10's last item.
- **OK** — `carStat = (dependibility / 8) * 10`, zero while unemployed, capped at 100
  (`room1.sc:757`, `n108.sc:77-87`, `employment.sc:218-228` ↔ `Player.cs:274-278`). Not the
  wiki's `1.25 × dependability`.
- **OK** — the work shift's garnishment, including the replicated bug that it recomputes from
  the **un-prorated** gross (`n108.sc:88-100` ↔ `Employment.cs:265-294`) and the unexplained
  $2 loss.
- **OK** — pro-rated pay when fewer than 6 hours remain: `(> (+ global323 6) 60)`
  (`n108.sc:89`) ≡ `HoursRemaining < 6`.
- **OK** — no random events while working; `n108.sc` contains no `Random` at all.
- **OK** — no once-per-turn limit on working; you may work repeatedly until the clock runs out.
- **OK** — `global329` (the "hasn't worked yet this turn" flag behind the dependability
  warning) ↔ `_workedThisTurn`, reset at `startTrn.sc:203`.
- **OK** — `doScandal` (`room1.sc:825` ↔ `Player.cs:304-326`), including severity 3 never
  costing a job or a wage.
- **OK** — the weekend scripts charge no hours and contain no `global323`.
