# The talker / speech-balloon window

Every fact below has a `file:line` citation. Paths are relative to the repo root
(`C:\AI-Accelerator\jones`). Where a fact is **not** in the game source or the
extracted resources, it is called out explicitly as *NOT IN SOURCE*.

**Which build is authoritative.** The floppy tree
`scripts/jones-dos-1.000.060/src` is the authority: it calls `Print` and renders
text. The CD tree `scripts/jones-cd-dos-1.0/src` replaced every shopkeeper
`Print` with `proc0_18`, which only plays digitised audio and lip-syncs the
portrait — no balloon, no text:

- `scripts/jones-cd-dos-1.0/src/Main.sc:1174-1180` — `(procedure (proc0_18 param1 param2 param3) (DoAudio audWPLAY param1) ... (DoAudio audPLAY param1))`
- `scripts/jones-cd-dos-1.0/src/clothing.sc:33` — `(proc0_18 (+ local1 10) global413 computerScript)` replaces the floppy's `Print`
- compare `scripts/jones-dos-1.000.060/src/clothing.sc:66-76`

Unless stated otherwise, **all citations below are the floppy tree**.

---

## 1. What draws it

### 1.1 The window class

`BubbleWindow`, script #113, subclass of `SysWindow`.

- `scripts/jones-dos-1.000.060/src/BubbleWindow.sc:3` — `(script# 113)`
- `scripts/jones-dos-1.000.060/src/BubbleWindow.sc:8` — `(class BubbleWindow of SysWindow`
- `scripts/jones-dos-1.000.060/src/BubbleWindow.sc:24` — `(method (open param1 param2 param3 param4 &tmp temp0 temp1)` — **this method draws the entire balloon**
- `scripts/jones-dos-1.000.060/src/BubbleWindow.sc:228` — `(method (dispose)` — restores saved bits

Class properties (`BubbleWindow.sc:9-22`):

| property | value | note |
|---|---|---|
| `priority` | `15` | passed as the priority arg to every `Graph`/`DrawCel` call |
| `type` | `129` | window style bitmask handed to the `NewWindow` kernel |
| `view` | `0` | **view resource 0** — never reassigned anywhere in either tree |
| `vColor` | `0` | outline colour index — never reassigned anywhere in either tree |
| `tailLeft/Top/Bottom/Right`, `tNum`, `underBits`, `tailUnderbits`, `pPort` | `0` | runtime state |

Inherited from `SysWindow` (`scripts/jones-dos-1.000.060/src/Game.sc:27-58`):
`top left bottom right color back(7) priority(-1) window type title hMargin(4)
vMargin(4) brTop(0) brLeft(0) brBottom(190) brRight(320) animateObj`.
`SysWindow:open` is just
`(NewWindow top left bottom right title type priority color back)` —
`Game.sc:48-52`. `hMargin`/`vMargin` are **never read** by `BubbleWindow`.

*NOT IN SOURCE*: the meaning of `type` `129` ( = `$81` ) is a kernel-side
window-style bitmask; the decompiled game source does not define the flag
constants (there is no `sci.sh` in either tree — verified by a recursive `*.sh`
search over `C:\AI-Accelerator\jones`). Empirically the window is fully
self-drawn before `(super open:)` is called, so the kernel must not paint a
frame or background over it.

### 1.2 The only instance

- `scripts/jones-dos-1.000.060/src/Main.sc:1234-1236` — `(instance bubbleWindow of BubbleWindow (properties))` — **no property overrides**, so `view` stays `0` and `vColor` stays `0`
- `scripts/jones-dos-1.000.060/src/Main.sc:1191` — `(= gBubbleWindow bubbleWindow)`

### 1.3 Every `Print` in the game uses it

`Print` is overridden by the game in script #255:

- `scripts/jones-dos-1.000.060/src/Interface.sc:3` — `(script# 255)`
- `scripts/jones-dos-1.000.060/src/Interface.sc:12` — `Print 0` (public export)
- `scripts/jones-dos-1.000.060/src/Interface.sc:45` — `(procedure (Print args ...)`
- `scripts/jones-dos-1.000.060/src/Interface.sc:59` — `((= temp1 (Dialog new:)) window: gBubbleWindow name: {PrintD})`

Line 59 is unconditional: **every** `Print` in the game is a `BubbleWindow`.
Calls that do not supply tail parameters get a tailless rounded box (see §2.4).

### 1.4 Call chain

```
Print (Interface.sc:45)
  -> Dialog new:, window: gBubbleWindow          (Interface.sc:59)
  -> Dialog:open  (Interface.sc:847)
       -> (= window (window new:))               (Interface.sc:851)
       -> window top:/left:/bottom:/right:/title:/type:/priority:
          open: param3 param4 param5 param6      (Interface.sc:855-864)
            -> BubbleWindow:open p1 p2 p3 p4     (BubbleWindow.sc:24)
                 -> (super open:) = SysWindow:open -> NewWindow  (Game.sc:48-52)
  -> Dialog:doit                                 (Interface.sc:873)
  -> Dialog:dispose -> BubbleWindow:dispose      (BubbleWindow.sc:228)
```

`BubbleWindow:open`'s four parameters, in order, are therefore whatever `Print`
passed as `temp28 temp29 temp13 temp14`:

| param | meaning | source |
|---|---|---|
| `param1` | `animateObj` — the talker portrait to cycle | `BubbleWindow.sc:48` `(= animateObj param1)` |
| `param2` | `tNum` — tail number 1..12 (0 = no tail) | `BubbleWindow.sc:47` `(= tNum param2)` |
| `param3` | tail tip **x** (absolute) | `BubbleWindow.sc:67,69,73` |
| `param4` | tail tip **y** (absolute) | `BubbleWindow.sc:54,56,61` |

---

## 2. The `Print` parameter list for shopkeeper dialogue

### 2.1 Keyword codes

The decompiler emitted raw keyword numbers (no `sci.sh` available). From the
dispatch `switch` in `Interface.sc:78-165`:

| code | effect | line |
|---|---|---|
| `30` | `#mode` — `(temp10 mode: ...)` | `Interface.sc:80-83` |
| `33` | `#font` — `(temp10 font: ... setSize: temp19)` | `Interface.sc:84-87` |
| `70` | `#width` — `(= temp19 ...) (temp10 setSize: temp19)` | `Interface.sc:88-91` |
| `25` | `#time` — `(temp1 time: ...)` | `Interface.sc:92-95` |
| `80` | `#title` — `(temp1 text: ...)` | `Interface.sc:96-99` |
| `67` | `#at` — sets `temp17`/`temp18` (x, y) | `Interface.sc:100-103` |
| `83` | `#draw` — `(Animate (gCast elements:) 0)` | `Interface.sc:104-106` |
| `41` | `#edit` | `Interface.sc:107-112` |
| `81` | `#button` text + value | `Interface.sc:113-121` |
| `82` | `#icon` (view/loop/cel or an object) | `Interface.sc:122-137` |
| `103` | `#modeless` | `Interface.sc:138-143` |
| `35` | `#window` | `Interface.sc:144-147` |
| **`310`** | **game-specific: talker + tail.** `(= temp28 …) (= temp29 …) (= temp13 …) (= temp14 …)` | `Interface.sc:148-153` |
| `311` | game-specific: keyboard/mouse item list, sets `standard: 0` | `Interface.sc:154-164` |

There is **no `#dispose` keyword** in this implementation; `Print` always
disposes the dialog itself at `Interface.sc:260`.

The keyword scan starts at argument index 2 when arg0 is a text-resource module
number (`(u< [args 0] 1000)` → `GetFarText`, `(= temp16 2)`) —
`Interface.sc:61-74`. So in `(Print 209 30 310 …)` the `30` is a **string index,
not** `#mode`.

### 2.2 The exact shopkeeper call

Canonical form, e.g. QT Clothing's greeting
(`scripts/jones-dos-1.000.060/src/clothing.sc:169-181`):

```
(Print
    209                 ; text resource module (= the room's script number)
    (Random 0 8)        ; string index
    310  global413      ;   animateObj = theTalker
         global440      ;   tNum       (tail number)
         global441      ;   tail tip x
         global442      ;   tail tip y
    70   100            ; #width 100
    25   global426      ; #time  (reading-speed seconds)
)
```

and the "bought an item" reaction (`clothing.sc:66-76`) is the same minus
`70 …`, i.e. it takes the default width of 100.

All 73 balloon-bearing `Print` calls in the floppy tree were enumerated. The
argument shape is always
`<module> <index> 310 global413 global440 global441 global442 [70 <width>] [25 global426] [81 …] [311]`.
Notable variants:

| call site | width | timer |
|---|---|---|
| `clothing.sc:66` | default 100 | `25 global426` |
| `clothing.sc:85` "not enough cash" | `70 70` | `25 global426` |
| `clothing.sc:169` greeting | `70 100` | `25 global426` |
| `bank.sc:135` greeting | `70 130` | `25 global426` |
| `bank.sc:474` loan Yes/No | `70 110` | none — `311`, buttons |
| `pawnShop.sc:780` offer Take It / Leave It | `70 107` | none — `311`, buttons |
| `rentOffice.sc:524`, `rentOffice.sc:647` YES/NO | `70 150` | none — `311`, buttons |
| `pawnShop.sc:743`, `761`; `rentOffice.sc:421`, `441` | `70 90` | `25 global426` |
| `university.sc:319`, `766`, `781` | `70 113` | **none** (click-only) |
| `university.sc:746` enroll Yes/No | `70 113` | none — `311`, buttons |

**No shopkeeper `Print` ever passes `#at` (67), `#font` (33) or `#mode` (30).**
Verified by extracting the full argument list of every `Print` call containing
`310` across `scripts/jones-dos-1.000.060/src/*.sc`.

### 2.3 `global440/441/442` per location (`tNum`, tail x, tail y)

These three globals are set once in each location's `init`, then read by every
`Print` in that location. The colour scheme (`proc0_17` argument, §5) is set
immediately before them.

| location | script | `proc0_17` | `global440` = `tNum` | `global441` = x | `global442` = y | citation |
|---|---|---|---|---|---|---|
| Appliance store | `appliance.sc` | 3 (`:144`) | 1 | 205 | 132 | `appliance.sc:148-150` |
| Bank | `bank.sc` | 2 (`:80`) | 7 | 113 | 82 | `bank.sc:84-86` |
| QT Clothing | `clothing.sc` | 1 (`:116`) | 3 | 205 | 80 | `clothing.sc:120-122` |
| Discount store | `discount.sc` | 1 (`:175`) | 3 | 209 | 71 | `discount.sc:180-182` |
| Employment office | `employment.sc` | 1 (`:325`) | 2 | 205 | 95 | `employment.sc:329-331` |
| Factory | `factory.sc` | 3 (`:44`) | 9 | 129 | 120 | `factory.sc:48-50` |
| Fast food | `fastFood.sc` | 2 (`:140`) | 3 | 205 | 80 | `fastFood.sc:144-146` |
| Market | `market.sc` | 2 (`:146`) | 3 | 207 | 78 | `market.sc:150-152` |
| Pawn shop | `pawnShop.sc` | 2 (`:455`) | 7 | 110 | 78 | `pawnShop.sc:460-462` |
| Rent office | `rentOffice.sc` | 3 (`:87`) | 3 | 209 | 74 | `rentOffice.sc:91-93` |
| University | `university.sc` | 2 (`:374`) | 7 | 110 | 78 | `university.sc:378-380` |

These are the **only** assignments to `global440/441/442` in the floppy tree.
Locations that print via a balloon but never set them (e.g. `broker.sc`,
`newspaper.sc`, `lowcost.sc`, `security.sc`, `diploma.sc`, `weekend.sc`) inherit
the values left by the last location that did.

### 2.4 Coordinate space

**Absolute, in the 320×200 screen space.** `BubbleWindow:open` does
`(SetPort 0)` before computing anything (`BubbleWindow.sc:26`) and
`BubbleWindow:dispose` does the same (`BubbleWindow.sc:229`), restoring the
caller's port from `pPort` (`BubbleWindow.sc:25`, `:240`). All `Graph` and
`DrawCel` calls happen inside that port. The tail x/y are therefore absolute
screen pixels, unrelated to the host shop `Dialog`'s own `moveTo:` (e.g.
`clothing.sc:155` `moveTo: 69 44`).

---

## 3. Geometry

### 3.1 How the balloon's size is derived

The size comes from the wrapped text, computed in `Print` before the window
exists:

1. `Interface.sc:75` — `(temp10 text: @temp33 moveTo: 5 5 font: gUserFont setSize:)`
   → the `DText` sits at (5, 5) inside the dialog.
2. `Interface.sc:77` — `(temp10 setSize: (= temp19 100) mode: 1)` → default wrap
   width **100**, mode **1**.
3. Keyword `70` overrides the width: `Interface.sc:88-91`.
4. `DText:setSize` (`Interface.sc:548-552`):
   `(TextSize @[r 0] text font (if argc w else 0))`,
   `(= nsBottom (+ nsTop [r 2]))`, `(= nsRight (+ nsLeft [r 3]))`.
   `[r 2]`/`[r 3]` are the *measured* height/width returned by the kernel, so the
   balloon is as wide as the text actually needed, **≤ the requested wrap width**.
5. `Dialog:setSize` (`Interface.sc:1219-1247`): `text` is 0 for a `Print` with no
   `#title`, so it starts at `nsTop=nsLeft=nsBottom=nsRight=0`, unions in the
   `DText` rect, then `(+= nsRight 5)` `(+= nsBottom 5)` and `(self moveTo: 0 0)`.

   **Result: dialog rect = (0, 0) … (textW + 10, textH + 10)** — a uniform 5px
   text margin on all four sides.
6. `Interface.sc:189` — `(temp1 setSize: center:)`. `Dialog:center`
   (`Interface.sc:1193-1217`) centres inside
   `window brLeft:/brTop:/brRight:/brBottom:` — which for the shared
   `bubbleWindow` instance are the `SysWindow` defaults **0, 0, 320, 190**
   (`Game.sc:41-44`; never overridden — `Main.sc:1234-1236`).
7. `Interface.sc:203-215` — `moveTo:` honours `#at` if given, else keeps the
   centred position.
8. `Dialog:open` copies `nsTop/nsLeft/nsBottom/nsRight` onto the window
   (`Interface.sc:855-864`).

For a **tailed** balloon the centred position is then *discarded*:
`BubbleWindow:open` keeps only the size and re-derives the origin from the tail
(`BubbleWindow.sc:50-51`: `(= temp0 (- right left))`, `(= temp1 (- bottom top))`).
For a **tailless** balloon (`tNum` 0 / fewer than 2 args) the centred rect is used
as-is: `left = (320 - w) / 2`, `top = (190 - h) / 2`.

With font 1 (line height 12, §4) a one-line balloon is **22 px** tall and
`textW + 10` wide.

### 3.2 Tail rectangle

`view` = 0, `loop` = `global513`, `cel` = `tNum - 1`.

Vertical placement of the tail cel (`BubbleWindow.sc:52-64`):

| condition | `tailTop` |
|---|---|
| `tNum == 1` or `tNum >= 9` | `param4 - CelHigh(view, global513, tNum-1)` |
| `3 <= tNum <= 7` | `param4` |
| otherwise (2, 8) | `param4 - CelHigh(…)/2` |

Horizontal (`BubbleWindow.sc:65-77`):

| condition | `tailLeft` |
|---|---|
| `tNum <= 4` or `tNum == 12` | `param3 - CelWide(view, global513, tNum-1)` |
| `5 <= tNum <= 10` | `param3` |
| otherwise (11) | `param3 - CelWide(…)/2` |

`tailBottom = tailTop + CelHigh(…)` (`:78`), `tailRight = tailLeft + CelWide(…)` (`:79`).

The tail's pixels behind are saved first, only at high detail:
`BubbleWindow.sc:80-84` — `(if (< global534 2) (= tailUnderbits (Graph grSAVE_BOX tailTop tailLeft tailBottom tailRight 3)))`.

### 3.3 Balloon rectangle, derived from the tail

Step 1 — which side the balloon sits on (`BubbleWindow.sc:85-102`):

| `tNum` | rule | meaning |
|---|---|---|
| `<= 3` | `right = tailLeft + 1`, `left = right - w` | balloon **left** of the tail; tail points **right** |
| `4..6` | `top = tailBottom - 1`, `bottom = top + h` | balloon **below**; tail points **up** |
| `7..9` | `left = tailRight - 1`, `right = left + w` | balloon **right** of the tail; tail points **left** |
| `10..12` | `bottom = tailTop + 1`, `top = bottom - h` | balloon **above**; tail points **down** |

The `± 1` means the balloon body overlaps the tail's base by one pixel.

Step 2 — where along that side (`BubbleWindow.sc:103-152`):

| `tNum` | rule |
|---|---|
| 1 | `bottom = tailBottom + 12`; `top = bottom - h` |
| 2 | `bottom = (tailBottom + tailTop)/2 + h/2`; `top = bottom - h` |
| 3 | `top = tailTop - 12`; `bottom = top + h` |
| 4 | `right = tailRight + 12`; `left = right - w` |
| 5 | `right = (tailRight + tailLeft)/2 + w/2`; `left = right - w` |
| 6 | `left = tailLeft - 12`; `right = left + w` |
| 7 | `top = tailTop - 12`; `bottom = top + h` |
| 8 | `bottom = (tailBottom + tailTop)/2 + h/2`; `top = bottom - h` |
| 9 | `bottom = tailBottom + 12`; `top = bottom - h` |
| 10 | `left = tailLeft - 12`; `right = left + w` |
| 11 | `right = (tailRight + tailLeft)/2 + w/2`; `left = right - w` |
| 12 | `right = tailRight + 12`; `left = right - w` |

Step 3 — short-balloon fixup (`BubbleWindow.sc:153-156`):

```
(if (and (<= temp1 24) (or (<= tNum 3) (<= 7 tNum 9)))
    (= top (- tailTop 6))
    (= bottom (+ top temp1))
)
```

Both statements are the `if` body (there is no `else`). So when the balloon is
**24 px tall or shorter** and the tail is a side tail (1–3 or 7–9) — which is
every location except the factory's `tNum 9`… note `tNum 9` *is* in range, so it
applies to all 11 locations — the vertical placement from step 2 is replaced by
`top = tailTop - 6`.

Since a single line of font 1 gives h = 22, **this fixup fires for almost every
shopkeeper line.**

### 3.4 Clipping

*NOT IN SOURCE*: `BubbleWindow:open` performs **no** clamping of `left/top/
right/bottom` to the screen. Whatever clipping occurs is done by the `Graph`,
`DrawCel` and `NewWindow` kernels.

---

## 4. Font

**Font resource 1.**

- `scripts/jones-dos-1.000.060/src/Interface.sc:75` — `(temp10 text: @temp33 moveTo: 5 5 font: gUserFont setSize:)`
- `scripts/jones-dos-1.000.060/src/Interface.sc:545` — `DText:new` → `((super new:) font: gUserFont yourself:)`
- `scripts/jones-dos-1.000.060/src/Main.sc:64` — `gUserFont = 1`

`gUserFont` is **never reassigned** — the only three references in the whole
floppy tree are `Interface.sc:75`, `Interface.sc:545` and the declaration
`Main.sc:64`. No shopkeeper `Print` passes keyword `33` (`#font`), so dialogue is
always font 1.

Font 1 is **not** the interface font: `WButton` (all the shop buttons) uses
`theFont 10` — `scripts/jones-dos-1.000.060/src/WButton.sc:33`.

Extracted metrics (`assets/raw/font/*.font`, SCI font header: u16 at +2 =
character count, u16 at +4 = line height):

| resource | chars | line height |
|---|---|---|
| `assets/raw/font/1.font` (dialogue) | 128 | **12** |
| `assets/raw/font/4.font` (`gSmallFont`, `Main.sc:65`) | 128 | 9 |
| `assets/raw/font/10.font` (interface / `WButton`) | 128 | 6 |

---

## 5. Colours

### 5.1 How `color` (text) and `back` (fill) are chosen

`BubbleWindow.sc:27-45`:

```
(= color (if (or (not global535) global439) global511 else (Palette palFIND_COLOR 164 59 59)))
(= back  (if (or (not global535) global439) global512 else (Palette palFIND_COLOR 248 236 156)))
(if global552 (= color 0) (= back 15) (= global513 8))
```

- `global535` = 0 in 16-colour EGA, 1 otherwise — `Main.sc:1181`
  `(= global535 (if (== (Graph grGET_COLOURS) 16) 0 else 1))`
- `global552` = 1 in 2-colour mono — `Main.sc:1182`
- `global439` is set to 1 in the board room's init (`room1.sc:1332`) and is
  **never cleared** — it is the only assignment to it in the tree.

So during normal play (`global439 = 1`) the `palFIND_COLOR` literals
`(164,59,59)` and `(248,236,156)` are **dead** — they only apply on VGA before
the board room has ever been entered. The live values are `global511` (text) and
`global512` (background).

The `global552` branch has three body statements with no `else`: in mono, text
index 0, background index 15, loop 8.

### 5.2 The colour-scheme table

`proc0_17` — `scripts/jones-dos-1.000.060/src/Main.sc:1146-1170`:

| `proc0_17` arg | `global513` (loop) | `global512` = `back` VGA / EGA | `global511` = `color` VGA / EGA | lines |
|---|---|---|---|---|
| 1 | 6 | 121 / 10 | 27 / 0 | `Main.sc:1149-1153` |
| 2 | 7 | 119 / 11 | 26 / 1 | `Main.sc:1154-1158` |
| 3 | 8 | 136 / 15 | 28 / 8 | `Main.sc:1159-1163` |
| 0 / else | 5 | 128 / 14 | 63 / 4 | `Main.sc:1164-1168` |

`(= global514 global513)` at `Main.sc:1147` saves the previous loop so menus can
restore it (`Menu.sc:197`, `:266`, `:303` …). Scheme 0 is the game-wide default,
set at startup (`Main.sc:1183`) and restored on leaving every location
(e.g. `clothing.sc:212`).

The **cream balloon with dark-red text** is scheme 0: `back` 128, `color` 63,
loop 5.

### 5.3 Palette → RGB

The balloon art lives in **view 0**, which carries its own embedded palette
(offset from bytes 6-7 of the view = 15923; format documented in
`tools/decode_views.py:52-71` — 260-byte header then 256 × `(flag, r, g, b)`).
This is the palette that must be used: the global palette
`assets/raw/palette/999.palette` marks indices ≥ 119 as unused (`flag = 0`).

Read from `assets/raw/view/0.view`:

| index | role | RGB hex |
|---|---|---|
| `0` | `vColor` — balloon outline; also mono text | `#000000` |
| `15` | mono `back` | `#EB8787` |
| `26` | scheme 2 text | `#204838` |
| `27` | scheme 1 text | `#682028` |
| `28` | scheme 3 text | `#383848` |
| `63` | **scheme 0 text (the dark red)** | `#C03838` |
| `119` | scheme 2 background (mint) | `#9BEBD3` |
| `121` | scheme 1 background (peach) | `#F8C0A0` |
| `128` | **scheme 0 background (the cream)** | `#F8E898` |
| `136` | scheme 3 background (pale blue) | `#C0D8F8` |
| `127` | player-balloon background, body 3 | `#A0F8E0` |
| `129` | player-balloon background, body 2 | `#D8C8E0` |
| `133` | player-balloon background, body 1 | `#F8F0A0` |

**Cross-check (this is the proof the mapping is right):** the dominant opaque
colour of each loop's top-left corner cel `assets/png/view_0_l<N>_c12.png`
exactly equals that scheme's `global512` RGB —
l5 `#F8E898` (128), l6 `#F8C0A0` (121), l7 `#9BEBD3` (119), l8 `#C0D8F8` (136),
l9 `#C0D8F8` (136), l10 `#F8F0A0` (133), l11 `#D8C8E0` (129), l12 `#A0F8E0` (127).

*NOT IN SOURCE*: the exact screen palette at runtime is whatever the room's pic
installed merged with view 0's palette; the values above are view 0's own
palette, which is what the balloon cels were authored against.

### 5.4 Player-coloured balloons (loops 9-12)

`Menu.sc:317-357` — the "Delete this player?" prompt:

- `Menu.sc:322` — `(= global513 (+ 9 (global302 whichBody:)))`
- `Menu.sc:323` — `(= global512 [local2 (global302 whichBody:)])`
- `Menu.sc:20` — `[local2 4] = [136 133 129 127]`
- `Menu.sc:325` — `(= global511 0)` (black text)

So loops 9/10/11/12 are the four player body colours. Previous values are saved
in `local6/7/8` (`Menu.sc:318-320`) and restored (`Menu.sc:341-343`, `:353-355`).

---

## 6. The balloon art

**It is both**: the four rounded corners and the tail are **view cels**; the
straight edges and the interior fill are **drawn by code**.

### 6.1 View / loop / cel numbers

- View: **0** (`BubbleWindow.sc:13` `view 0`, never overridden)
- Loop: **`global513`** — 5, 6, 7 or 8 for the four colour schemes (§5.2); 9-12
  for player-coloured balloons (§5.4)
- Cels within the loop:

| cel | role | citation |
|---|---|---|
| `0 … 11` | tail, one per `tNum` 1…12 (`cel = tNum - 1`) | `BubbleWindow.sc:221` |
| `12` | top-left corner | `BubbleWindow.sc:173` |
| `13` | top-right corner | `BubbleWindow.sc:174-181` |
| `14` | bottom-left corner | `BubbleWindow.sc:182-189` |
| `15` | bottom-right corner | `BubbleWindow.sc:190-197` |

Extracted PNGs, e.g. `assets/png/view_0_l5_c12.png` … `assets/png/view_0_l5_c15.png`
for scheme 0. Every one of loops 5-12 has exactly 16 cels.

Measured cel sizes (identical across loops 5-8, except `l8_c8` which is 17×9):

| cel | `tNum` | size |
|---|---|---|
| 0 | 1 | 16×9 |
| 1 | 2 | 16×8 |
| 2 | 3 | 16×9 |
| 3,4,5 | 4,5,6 | 6×2 each — stubs, **never used** (no location sets `tNum` 4-6) |
| 6 | 7 | 16×9 |
| 7 | 8 | 16×8 |
| 8 | 9 | 16×9 (`l8_c8` is 17×9) |
| 9,10,11 | 10,11,12 | 6×2 each — stubs, **never used** |
| 12-15 | — | 12×12 each |

Only `tNum` 1, 2, 3, 7, 9 occur in the game (§2.3), i.e. cels 0, 1, 2, 6, 8 —
all the real 16×9 / 16×8 tails. Cels 0-2 point **right** (balloon on the left),
cels 6-8 point **left** (balloon on the right); cel 0/8 slope downward toward the
tip, cel 2/6 slope upward, cel 1/7 are horizontal. The tip pixel sits at the far
x edge of the cel, which is exactly `param3`.

### 6.2 What the code draws

All in `BubbleWindow.sc:158-222`, in this order. `Graph grFILL_BOX` takes
`(top, left, bottom, right, mapMask, colour, priority)`; `Graph grDRAW_LINE`
takes `(y1, x1, y2, x2, colour, priority, control)`. `mapMask 3` = visual +
priority. `priority` is the property, **15**.

1. **Save under** — `:158` `(= underBits (Graph grSAVE_BOX top left bottom right 3))`
2. **Interior** — `:159-168` `grFILL_BOX (top+12) (left+12) (bottom-12) (right-12) 3 back priority`
3. **Top strip** — `:169` `grFILL_BOX top (left+12) (top+12) (right-12) 3 back priority`
4. **Left strip** — `:170` `grFILL_BOX (top+12) left (bottom-12) (left+12) 3 back priority`
5. **Bottom strip** — `:171` `grFILL_BOX (bottom-12) (left+12) bottom (right-12) 3 back priority`
6. **Right strip** — `:172` `grFILL_BOX (top+12) (right-12) (bottom-12) right 3 back priority`
   → together 2-6 fill the whole rect **except** the four 12×12 corner squares.
7. **Corner cels** — `:173-197`
   - TL: `DrawCel view global513 12 left top priority`
   - TR: `DrawCel view global513 13 (right - CelWide(…13)) top priority`
   - BL: `DrawCel view global513 14 left (bottom - CelHigh(…14)) priority`
   - BR: `DrawCel view global513 15 (right - CelWide(…15)) (bottom - CelHigh(…15)) priority`
8. **Outline, 1 px, colour `vColor` = 0 (`#000000`)** — `:198-219`
   - top: `grDRAW_LINE top (left+12) top (right-12) vColor priority -1`
   - bottom: `grDRAW_LINE (bottom-1) (left+12) (bottom-1) (right-12) vColor priority -1`
   - left: `grDRAW_LINE (top+12) left (bottom-12) left vColor priority -1`
   - right: `grDRAW_LINE (top+12) (right-1) (bottom-12) (right-1) vColor priority -1`
9. **Tail cel** — `:220-222`
   `(if (and (< global534 2) (>= argc 2) tNum) (DrawCel view global513 (- param2 1) tailLeft tailTop priority))`
10. **Blit** — `:223` `(Graph grUPDATE_BOX top left bottom right 1)`
11. `:224-225` `(= type 129)` then `(super open:)`.

So: **corner radius = 12 px**, supplied as art, not as a computed arc.
**Outline width = 1 px**, index 0 black on the straight edges; the corners' own
outline comes from the cel art (the extracted PNGs use `#202020`/`#281818` for
the corner/tail outline, slightly lighter than the code-drawn `#000000`).

Note the asymmetry: the top and left outlines are at `top` and `left`, the bottom
and right at `bottom-1` and `right-1`. The box therefore occupies
`[left, right-1] × [top, bottom-1]` inclusive.

### 6.3 Detail level

`global534` is the "Graphic Detail" gauge, `4 - <gauge value>`:
`Menu.sc:411-423` (`minimum: 1 normal: 4 maximum: 4`, `(= global534 (- 4 temp6))`).
It is declared with no initialiser (`Main.sc:678`), i.e. 0 = maximum detail.

`(< global534 2)` gates both the tail's `grSAVE_BOX` (`:80`) and the tail
`DrawCel` (`:220`). **At detail levels 1 and 2 (i.e. `global534` 2 or 3) the
balloon is drawn with no tail at all**, but the geometry is still computed from
the tail rectangle, so the balloon stays in the same place.

The same flag hides the talker portrait: `WButton.sc` `Talker:init/draw/setCycle/
setSize` are all wrapped in `(if (< global534 2) …)`.

---

## 7. Word wrap and alignment

- **Wrap width**: `Interface.sc:77` sets the default to **100**; keyword `70`
  overrides it (`Interface.sc:88-91`). Per-call values are tabulated in §2.2 —
  70, 90, 100, 107, 110, 113, 130 and 150 all occur.
- The width is passed to the kernel as `(TextSize @[r 0] text font w)`
  (`Interface.sc:549`); the kernel returns the *measured* width, so the balloon
  shrinks to the longest actual line.
- **Alignment**: `mode` **1**, set at `Interface.sc:77`
  (`(temp10 setSize: (= temp19 100) mode: 1)`). No shopkeeper `Print` passes
  keyword `30`, so it is never changed — dialogue is always mode 1.
  `DText` declares `mode 0` as its class default (`Interface.sc:541`), which
  `Print` immediately overrides.
  *NOT IN SOURCE*: the numeric meaning of `mode`. The SCI text kernel's
  convention is `-1` = right, `0` = left, `1` = centre, which agrees with the
  centred text seen on screen.
- The text is painted by the control kernel, not by the game:
  `Item:draw` → `(if type (DrawControl self))` — `Interface.sc:508-510`, with
  `DText type 2` (`Interface.sc:538`). Pen and background colours come from the
  window port that `NewWindow` created from `color`/`back` (`Game.sc:50`).

---

## 7a. Buttons (`#button`, keyword 81)

Four `Print` calls in the game put buttons in the balloon. Everything below is from
the floppy tree unless marked.

### 7a.1 The argument shape

`Interface.sc:113-121` — each `81` is followed by **text then value**:

```
(81 ((= [temp4 temp26] (DButton new:)) text: [args (++ temp16)] value: [args (++ temp16)] setSize:)
    (+= temp25 (+ ([temp4 temp26] nsRight:) 5))
    (++ temp26))
```

`temp25` is the run length, `temp26` the count. The CD build's `Interface.sc:119`
adds **4**, not 5 — the only difference between the two trees in this path.

### 7a.2 A button's own size — `DButton:setSize`, `Interface.sc:589-596`

```
(TextSize @[r 0] text font)          ; [r 2] height, [r 3] width; font is the class
(+= [r 2] 2)                         ; default 0 (Interface.sc:586), NOT 1 or 10
(+= [r 3] 2)
(= nsBottom (+ nsTop [r 2]))
(= [r 3] (* (/ (+ [r 3] 15) 16) 16)) ; width rounded UP to a multiple of 16
(= nsRight (+ [r 3] nsLeft))
```

Note the asymmetry: the height keeps its `+2`; the width's `+2` is then swallowed by
the rounding. `DButton` is `type 1` and `state 3` (`Interface.sc:583-584`).

Font 0 is 8 px high (`assets/raw/font/0.font`), so **every button is 10 px tall**.
Measured widths of the game's six button labels:

| label | font-0 width | `+2` | rounded | site |
|---|---|---|---|---|
| `Yes` | 21 | 23 | **32** | university, bank |
| `No` | 14 | 16 | **16** | university, bank |
| `Take It` | 47 | 49 | **64** | pawn shop |
| `Leave It` | 55 | 57 | **64** | pawn shop |
| ` YES ` | 38 | 40 | **48** | rent office |
| ` NO ` | 31 | 33 | **48** | rent office |

The rent office's labels really do carry a leading and trailing space — raw script
201 @0x124B is `<SP>YES<SP><NUL><SP>NO<SP><NUL>` — and the pawn shop's are not
`Yes`/`No` at all.

### 7a.3 Layout of the row — `Interface.sc:177-189`

```
temp25 = sum over buttons of (width + 5)
temp27 = (temp25 > dialog.nsRight) ? 5 : (dialog.nsRight - temp25)
for each button i:
    button.moveTo(temp27, dialog.nsBottom)     ; y = BELOW the text block
    temp27 = 5 + button.nsRight
(temp1 setSize: center:)
```

`dialog.nsRight`/`nsBottom` here are the values from the FIRST `setSize:`
(`Interface.sc:172`), i.e. `textW + 10` and `textH + 10` (§3.1). So the run is
**right-aligned with a 5 px gap to the box's right edge**, unless it is longer than
the box, in which case it starts at x = 5 and the box grows to it.

The second `setSize:` at `:189` unions the row back in and adds 5 again
(`Interface.sc:1244-1245`), giving the final box:

```
w = max(textW + 5, lastButtonRight) + 5
h = (textH + 10) + buttonHeight + 5
```

`BubbleWindow:open` then derives the balloon's position from these
(`BubbleWindow.sc:50-51`). Because `h` is always ≥ 37, the ≤ 24 px short-balloon
fixup (§3.3) **never fires for a balloon with buttons** — these balloons sit where
step 2 of §3.3 puts them.

Worked numbers, computed from the shipped fonts:

| site | `#width` | text | box before | run | rowX | final box |
|---|---|---|---|---|---|---|
| university | 113 | `Enroll for $51?` (1 line, 78) | 88×22 | 58 | 30 | **88×37** |
| bank | 110 | `$400. Is this acceptable?` (2 lines, 65) | 75×34 | 58 | 17 | **75×49** |
| pawn shop | 107 | `Do you accept $120 for your Refrigerator?` (3 lines, 98) | 108×46 | 138 | 5 | **143×61** |
| rent office | 150 | `Rent Low-Cost Apartment?` (1 line, 145) | 155×22 | 106 | 49 | **155×37** |

The pawn shop is the case where the run does not fit: `temp25` 138 > 108, so
`temp27` falls to 5 and the box widens from 108 to 143.

### 7a.4 The frame — *NOT IN SOURCE*

`DButton` is `type 1`, and `Item:draw` hands a typed control straight to the
`DrawControl` **kernel** (`Interface.sc:508-510`). Neither script tree draws the
button, so its border exists only in the interpreter. The port draws what the
screenshots show: a 1 px rounded box — a rectangle with its four corner pixels left
out — in the balloon's own text colour (`global511`), with the label centred. The
pen colour itself *is* sourced: the control inherits the window port's colours from
`NewWindow color back` (`Game.sc:50`).

### 7a.5 The four call sites

Strings are the CD text resources, which is what `assets/raw/text` holds.

| site | script (CD) | string | `#width` | buttons |
|---|---|---|---|---|
| university enrolment | `university.sc:671-682` | 207[1] `Enroll for $%d?` | 113 | `Yes` 1 / `No` 0 |
| bank loan | `bank.sc:373` | 204[1] `$%d. Is this acceptable?` | 110 | `Yes` 1 / `No` 0 |
| pawn offer | `pawnShop.sc:671-682` | 212[3] `Do you accept $%d for your %s?` | 107 | `Take It` 1 / `Leave It` 0 |
| rent, low-cost | `rentOffice.sc:345-356` | 201[0] `Rent Low-Cost Apartment?` | 150 | ` YES ` 1 / ` NO ` 0 |
| rent, security | `rentOffice.sc:412-423` | 201[1] `Rent Security Apartment?` | 150 | ` YES ` 1 / ` NO ` 0 |

The floppy tree formats longer strings from its own (larger) text resources at
`university.sc:746`, `bank.sc:474`, `pawnShop.sc:780`, `rentOffice.sc:524` and
`:647`, and passes `310` so the box carries the location's tail; the CD's button
`Print`s pass no `310` at all, so in the CD they are tailless and centred. The
CD `Print`s also use keyword `319` where the floppy uses `311`.

**The university is two different strings.** The clerk SPEAKS clip 406 — "The
enrollment fee is $51. Would you like to enroll?" — and the game then PRINTS the
short `Enroll for $51?`. Putting the spoken subtitle in the button box makes it
three lines wide instead of one.

---

## 8. Dismissal

Two ways, whichever comes first.

### 8.1 Timer (`#time`, keyword 25)

- `Interface.sc:92-95` — `(temp1 time: [args temp16])`
- `Interface.sc:865` — `Dialog:open` does `(= seconds time)`
- `Interface.sc:942-952` — `Dialog:check`:
  ```
  (if seconds
      (= thisSeconds (GetTime 1))
      (if (!= lastSeconds thisSeconds)
          (= lastSeconds thisSeconds)
          (if (not (-- seconds)) (self cue:))))
  ```
- `Interface.sc:954-960` — `Dialog:cue` → `(if (not busy) (self dispose:) else (= busy 0))`
- `Dialog:doit` sets `busy` 1 on entry (`Interface.sc:875`), calls `self check:`
  each iteration (`Interface.sc:916`) and breaks on `(or (== temp2 -1) (not busy))`
  (`Interface.sc:917-923`).

So the balloon closes **`time` whole seconds** after it opened.

The value used is `global426`, the "Reading Speed" gauge:
- `Main.sc:549` — `global426 = 5` (default)
- `Menu.sc:268-283` — `minimum: 2 normal: 5 maximum: 15`, `(= global426 …)`

### 8.2 Input

`Dialog:handleEvent` (`Interface.sc:1108-1125`): when the dialog has no
selectable item (`(not (self firstTrue: #checkState 1))` — true for a plain
text-only `Print`) and the event is `evMOUSEBUTTON`, `evJOYDOWN`, or keyboard
`KEY_RETURN`; or unconditionally on keyboard `KEY_ESCAPE` — it claims the event
and returns `-1`. `Dialog:doit` breaks on `-1` (`Interface.sc:917-923`),
`Print` maps `-1` to 0 (`Interface.sc:241-243`) and disposes
(`Interface.sc:260`).

**So: any mouse click, Enter, joystick button or Esc closes it immediately** — but
only for a **text-only** `Print`. The first half of that test is
`(not (self firstTrue: #checkState 1))`, and `DButton` has `state 3`
(`Interface.sc:584`), so a balloon with buttons falls straight through it and waits.

For a balloon WITH buttons (§7a):

- **Enter / joystick** — `Interface.sc:1067-1105` presses `theItem`, which `Print`
  has already pre-selected as the FIRST button: `Interface.sc:230-236` ORs state bit
  `$0002` onto the first item whose state has bit 1, and `:241` hands it to `doit:`.
- **Esc** — `Interface.sc:1121-1124` is the one branch that fires regardless of
  selectable items. It returns -1, which `Print` maps to 0 at `:241-243` — i.e. the
  value of the `No` / `Leave It` button.
- **A click elsewhere** does nothing at all.

Calls with buttons (`81` … `311`, e.g. `bank.sc:474`, `pawnShop.sc:780`,
`rentOffice.sc:524`, `university.sc:746`) pass no `#time` at all and wait for a
button. `university.sc:319`, `:766`, `:781` pass neither buttons nor `#time` —
those are dismissed by input only.

### 8.3 Nothing is tied to a speech clip

In the floppy build there is no audio playback in this path at all. (In the CD
build there is no balloon — see the top of this document.)

### 8.4 Teardown

`BubbleWindow:dispose` — `BubbleWindow.sc:228-243`:

1. `(SetPort 0)`
2. `(if animateObj (animateObj cel: 0 draw: setCycle: 0))` — stop and reset the
   talker portrait (**floppy only**; `scripts/jones-cd-dos-1.0/src/BubbleWindow.sc:227-239`
   omits both this and the `(= animateObj param1)` assignment)
3. restore the tail's saved bits + `grUPDATE_BOX` over the tail rect, clear
   `tailUnderbits`
4. restore the balloon's saved bits + `grUPDATE_BOX` over the balloon rect
5. `(SetPort pPort)`, `DisposeWindow`, `DisposeClone`

While the balloon is open the portrait animates:
`Dialog:doit` — `Interface.sc:900-902` —
`(if (window animateObj:) ((window animateObj:) cycle: doit:))`.
`animateObj` is `global413`, which each location sets to its `theTalker`
(e.g. `clothing.sc:137` `(= global413 theTalker)`; `clothing.sc:408-413`
`(instance theTalker of Talker (properties nsTop 0 view 359))`).
`Talker` is defined at `scripts/jones-dos-1.000.060/src/WButton.sc:259-265`
(`class Talker of DCIcon`, `nsTop 1 nsLeft 115 priority 14 cycleSpeed 10`).

---

## 9. Implementation checklist (scheme 0, the cream balloon)

```
font            : resource 1, line height 12
wrap width      : per call (default 100); measure, then use the measured width
text margin     : 5 px on all four sides  -> w = textW + 10, h = textH + 10
alignment       : centred
background      : palette index 128  #F8E898
text colour     : palette index 63   #C03838
straight outline: palette index 0    #000000, 1 px
corner art      : view 0, loop 5, cels 12 (TL) 13 (TR) 14 (BL) 15 (BR), 12x12 each
tail art        : view 0, loop 5, cel (tNum - 1); used tNums 1,2,3,7,9
fill            : whole rect minus the four 12x12 corners
outline rect    : [left, right-1] x [top, bottom-1]
placement       : from the tail (see 3.2 / 3.3), absolute 320x200 coords
dismiss         : click / Enter / Esc / joystick button, or after global426 s (default 5)
buttons         : font 0 (h 10), width = roundUp16(textW + 2), right-aligned run below
                  the text with a 5px gap, box grows by buttonH + 5 (see 7a);
                  frame NOT IN SOURCE (kernel-drawn, type 1)
```

Swap loop 5 -> 6 / 7 / 8 and the colour pair -> (121, 27) / (119, 26) / (136, 28)
for the other three schemes, per §2.3's `proc0_17` column.
