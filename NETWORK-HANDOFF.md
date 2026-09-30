# Handoff: network play

> **STATUS (2026-09-29): BUILT, desktop head.** The design chosen is **host-authoritative
> remote display**, not snapshot sync or lockstep. The host runs the only game; a joiner is
> a terminal that shows the host's screen and plays its sound, and sends its clicks and keys
> back. The RNG blocker below therefore never had to be solved: only the host rolls.
>
> - **Run it:** `Jones.App.Desktop --host [port]` and `Jones.App.Desktop --join address[:port]`
>   (default port 7117). Logs: `%TEMP%\jones-host.log`, `%TEMP%\jones-join.log`.
> - **Seats:** host = player 1; joiners take players 2, 3, 4 in arrival order; unjoined seats
>   are hot-seat at the host; a dropped joiner keeps their seat and gets it back on
>   reconnect. Only the seat named by `MainViewModel.ActingSeat` may act.
> - **How a screen travels:** every bitmap is tagged by the loader that made it
>   (`Jones.App/Net/ArtRecipes.cs`, hooked into `SciArt` and `SciFont`), and text lines carry
>   their own constructor arguments (`MenuLineVm.Args`, `TextVm.Args`), so a frame is a list
>   of recipes a joiner rebuilds from its own copy of the art. About 3 KB/s in play.
> - **Where things are:** `src/Jones.Net` (protocol, `NetLink`, `SeatTable`, `HostServer`,
>   `JoinClient`, tested in `Jones.Tests/NetworkTests.cs`); `Jones.App/Net` (`NetHost`,
>   `NetClient`/`NetLaunch`, `InputGate`, `BroadcastAudioPlayer`); `MainViewModel.Network.cs`.
> - **Android joins from inside the app:** the link switch in the chrome row (title screen
>   only) takes the host's address; the manifest requests INTERNET. Hosting is desktop-only.
> - **Open:** a joiner playing a whole turn has not been watched live yet, nor the in-app join
>   tapped through on a phone; no authentication or encryption (use a LAN or a VPN such as
>   Tailscale). See PARITY.md, "Network play".
>
> The briefing below is the pre-build analysis, kept because its reasoning still holds.

Briefing for a new conversation about adding network play to this port. Written at the end
of the session that built everything described below, so the next chat does not start cold.

**Read `CLAUDE.md` first** — it is the standing rule set for this project and it is not
optional. In short: this is an exact port from the game's own decompiled source, nothing
user-visible may be invented, and the raw resources beat the decompiled listing.

---

## What the project is

A C#/Avalonia recreation of Sierra's *Jones in the Fast Lane* (SCI1 VGA, 1990/91), ported
from the decompiled original scripts so it can then be modified. At `C:\AI-Accelerator\jones`.

Two heads ship: **Desktop** (Windows, working) and **Android** (builds; at time of writing
it installs but does not launch — under active investigation).

### Layers, and why they matter here

| Project | What it is | Network relevance |
|---|---|---|
| `Jones.Core` | All game logic. No UI, no platform APIs. ~503 tests. | **This is the thing to network.** It already has no notion of a screen. |
| `Jones.Audio` | Portable OPL2 synth, renders PCM. | None. |
| `Jones.App` | Shared Avalonia UI, screen building, input. | Knows about "the current player" only via `Game`. |
| `Jones.App.Desktop` / `.Android` | Platform heads: audio sink, startup wiring. | Each would need a transport. |

`Jones.Core` is genuinely platform-free and already drives everything. A headless server
is a realistic option.

---

## What already exists that network play can build on

### 1. The whole game state already serialises
`src/Jones.Core/Save/` — `SaveSnapshot.cs`, `GameSave.cs`, `SaveStore.cs`.

`Game.Capture()` / `Game.Restore()` round-trip **the entire game**: every `Player` (stats,
goals, inventory with attributes and prices, degrees, job, rent, loans, investments), the
`Economy` (all eight indices, readings AND trend indices, crash/boom state, headline),
`Calendar`, `CurrentPlayerIndex`, `Winners`, `TurnedDown`, the clock including
`SubHourTicks`, the per-turn latches, mugging state, and all of `JonesWorld`.

It is versioned JSON, the version is checked before deserialising, and there is a test that
compares a restored game against the original **by reflection over every public property** —
so a field added and forgotten fails by name.

**That is a ready-made state-sync payload.** A full snapshot is small (a few KB) and the
game is turn-based, so shipping the whole state at each turn boundary is entirely viable and
removes most of the hard problems.

### 2. The turn structure is already explicit
`Game.StartTurn()` / `Game.EndTurn()`, `CurrentPlayerIndex`, `Players`. Turn handover is a
single well-defined moment (`MainViewModel.LeaveBuilding` → `Game.EndTurn` → `BeginTurn`),
which is the natural synchronisation point.

### 3. Computer players already exist
`JonesAi` / `JonesTurn` — a full port of the original's AI. A disconnected or absent player
could be handed to it rather than stalling the game.

### 4. Player count is already 1–4
The setup flow, the board, the goals screen and `viewGoals` all handle 1–4 players, with
per-player state throughout. Nothing assumes a single human.

---

## The one real blocker: the RNG

**`SciRandom` wraps `System.Random`, and its state is deliberately NOT persisted.**

The reasoning (recorded in the save work) was sound for save/restore: `System.Random`'s
internal state is unreachable, .NET does not guarantee its sequence across versions, and the
original didn't save a seed either — a restored 1990 game rolled fresh numbers too.

**For networking this decision has to be revisited**, and which way depends on the model:

- **Snapshot sync** (ship the whole state each turn): the RNG does not need to be shared at
  all. Whoever is authoritative rolls; everyone else receives the result. This is the
  simplest correct design and it fits a turn-based game well.
- **Lockstep / deterministic replay** (ship only inputs): every client must produce an
  identical sequence, so `IRandomSource` needs a seeded, serialisable implementation with a
  guaranteed algorithm. `ScriptedRandom` already exists for tests, and `IRandomSource` is a
  clean seam, so this is a contained change — but it must be done deliberately.

`Jones.Core` already uses `IRandomSource` everywhere rather than `Random` directly, and SCI's
`Random(a,b)` is inclusive of both bounds, which the port models.

---

## Design questions worth settling early

1. **Authority.** Server-authoritative, host-authoritative, or peer-to-peer? Snapshot sync
   plus one authority is by far the least work and the least cheatable.
2. **What travels.** Full snapshots at turn boundaries (simple, robust) versus actions
   (smaller, needs determinism). Given the game is turn-based with no real-time element,
   bandwidth is not a constraint.
3. **Where the split lives.** `Jones.Core` is already headless — a console host is plausible.
   The alternative is host-and-play from one of the existing heads.
4. **Reconnect and drop-out.** The snapshot makes reconnect nearly free; `JonesAi` can cover
   an absent player.
5. **The UI assumes it owns the game.** `MainViewModel` calls `Game` methods directly and
   rebuilds the screen from state. It has no concept of "not my turn" beyond the existing
   Jones-turn handling in `PlayJonesTurnIfNeeded`, which is the precedent to follow.
6. **Save/restore interaction.** A networked game's snapshot and the save file are the same
   shape; decide whether a host can save a multiplayer game and resume it.

---

## Things that will bite

- **Tests only cover `Jones.Core`.** The test project does not reference `Jones.App`, so
  nothing tests the UI layer at all. Anything networking touches in the view model is
  unverified by the suite.
- **Deliberate deviations exist** and are listed in `PARITY.md`'s deviations table
  (starting food, week-one hardship, motion smoothing, per-goal percentages, intro autoplay,
  sound-settings persistence). Clients must agree on these or they will diverge — they are
  effectively rules.
- **`Player.StartingFoodWeeks` and `TurnStart.SkipWeekOneHardship` are statics set by each
  head.** In a networked game these are shared rules and belong in the session, not in
  `Program.cs`.
- **The Android head does not currently launch.** Check its state before assuming
  cross-platform play.

## Documents worth reading

`CLAUDE.md` (rules) · `PARITY.md` (feature status and deviations) · `MECHANICS.md` (game
rules with citations) · `PORTING.md` (globals decoder) · `GATES.md`, `STRINGS.md`,
`PROPERTIES.md` (fidelity audits) · `SCREENS.md`, `TALKER.md` (screen specs)
