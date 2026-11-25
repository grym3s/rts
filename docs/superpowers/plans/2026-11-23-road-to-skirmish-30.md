# Road to Skirmish 30 — plan to a playable Unreal demo

**Goal:** a playable demo in Unreal. Select about 30 units, move and attack, fight 30 enemies that react, and get a win or lose screen with restart. This is the "Skirmish 30" vertical slice (`decisions/2026-08-17-foundation-ultra-review.md` §7) on the Unreal presentation chosen in ADR 0006.

**Builds on:** `2026-09-26-unreal-rts-migration.md` (slices 1–6) and `../specs/2026-09-26-unreal-rts-migration-design.md`. Feel is judged against `docs/game-design-pillars.md`.

**State on 2026-11-23 (`main` @ `133eb46`):**
- The sim has movement, combat with the counter matrix, economy, production and power.
- Unreal is at slice 4. It has the RTS camera and click/A select, and right-click move sends tick-stamped commands to the .NET host. Units are drawn as cubes.
- The gaps below come from reading the docs and searching the code, not from running the game, so a few may already be partly done.

Tasks marked **[blocks others]** hold up later work. Tasks tagged *.NET only* can be built and tested headless, without Unreal.

---

## Phase 0 — Get unblocked

- [x] **T01 [blocks others] Install Unreal 5.8.3 on a machine we use.** The pinned Linux install (`~/.local/opt/UnrealEngine-5.8.3`) can't be found any more, which is why GRY-288 is blocked. The bridge is written to work on Windows too, but that path has never been tested.
- [x] **T02 [blocks others] Build the host and the Unreal project, and play it once** (after T01). Run `make host-publish`, build the `RtsBridgeEditor` target, then try camera pan/zoom/rotate, select and right-click move. Steps are in `unreal/CONTEXT.md`. This finishes GRY-288.
- [ ] **T03 Review and merge the open GRY branches.**
  - GRY-296 (headless slice 4 tests) is the easiest.
  - GRY-295 (rifleman spec) and GRY-297 (slice 5 combat effects spec) are needed by Phase 3.
  - GRY-298 is the Godot removal checklist.
  - GRY-280 and GRY-281 are art.
- [ ] **T04 Update `CLAUDE.md` and the root docs for Unreal.** They still describe Godot as the presentation and route to `make godot-test`.

## Phase 1 — Sim rules missing for the demo (*.NET only*)

Each task gets a scenario in `content/scenarios/`. New behaviour gets its replay hash pinned in CI.

- [x] **T05 [blocks others] Enemy behaviour: hold, chase, return.** Enemies hold position, chase anything that comes in range, then return to their spot. *Done on `feat/t05-guard-behaviour`: leash 1.5 × sight, applies to both sides, scenario `guard-hold`.*
- [x] **T06 [blocks others] Win and lose condition.** Win when every enemy is dead, lose when all your units are. Needs a new sim event that the host passes on. *Done on `feat/t06-t09-match-events`: `sim/match/`, host `state.match` and a `match_end` event; Unreal shows a placeholder VICTORY / DEFEAT message until T19.*
- [x] **T07 Restart a match.** The host refuses a second `init` (`already_initialized`). Either add a reset or have Unreal relaunch the host cleanly. *Done on `feat/t07-restart-match`: host `reset` request, Ctrl+R in Unreal (relaunches the host if it died).*
- [x] **T08 [blocks others] Demo map and 30v30 scenario.** One hand-made map with blocked cells and chokepoints, plus a 30v30 scenario file. Pin its replay hash in CI. *Done 2026-11-24 on `feat/tri-point-map`: Tri-Point, water version, 30v30 T1 combined arms.*
- [x] **T09 [blocks others] Richer sim events.** Events only carry (tick, subject, target). Health bars and effects need damage amounts and deaths, so this is a sim contract change first, then the host protocol. *Done on `feat/t06-t09-match-events`: `shot` (damage amount) and `death` (killer, faction) events through the host.*

## Phase 2 — Unreal controls (finish slice 4)

All game rules stay in the sim. Unreal only sends Commands.

- [x] **T10 Attack, attack-move and stop orders** (after T02). Unreal only sends `move` today. The host already accepts the others. *Done on `feat/unreal-orders`.*
- [x] **T11 Box select, shift-add, control groups 1–3** (after T02). Copy the behaviour from `game/selection/`. *Done on `feat/unreal-box-select`.*
- [x] **T12 Order feedback within one frame** (after T10). A ground marker or acknowledgement on every order. This is pillar 1. *Done on `feat/unreal-feedback-smoothing`.*
- [x] **T13 Smooth movement and facing** (after T02). Interpolate positions between the 20 t/s snapshots and turn units toward where they're moving. *Done on `feat/unreal-feedback-smoothing`.*
- [ ] **T14 Draw the map's terrain and obstacles** (after T08). Show blocked cells so chokepoints can be read. *Basic version on `feat/tri-point-map`: tiles drawn as boxes and on the minimap; real art later.*

## Phase 3 — Make fights readable (slice 5)

- [x] **T15 Health bars, deaths and corpse removal** (after T09). Driven only by snapshots and events. *Done on `feat/t15-health-deaths`: HUD bars on damaged or selected units; killed units topple, lie 2 s, sink.*
- [x] **T16 Show who is shooting whom** (after T09). Tracers or muzzle flashes per shot. This is pillar 4. *Done on `feat/t16-shot-tracers`: per-shot line, streak, muzzle flash and hit spark in the shooter's colour.*
- [ ] **T17 Rifleman with `idle`, `move` and `fire` animations** (after T03). Follow the GRY-295 spec. Check axis, scale and clip names in the pinned editor first.
- [ ] **T18 One representative explosion.** Check that it reads well at close, default and far zoom (GRY-297).
- [x] **T19 Win/lose screen with restart** (after T06 and T07). The only menu the demo needs. Restart lives only in this menu (and a single-player pause menu), never on a hotkey; it calls `RestartMatch()` from T07. *Done on `feat/t19-match-screen`: result panel with time and losses, Play again (single player only) and Quit.*
- [x] **T20 Debug overlays on F-keys.** Paths, targets, unit IDs and state, tick counter and sim step in ms. The Godot version has these in `game/debug/`. *Done on `feat/t20-debug-overlays`: F4 toggles stats, paths, targets and id/state labels (the editor swallows F1-F3 in PIE).*

## Phase 4 — Prove it and lock it down (slice 6)

- [ ] **T21 [blocks others] Fun test.** Both of us play it and judge it against the 6 points in `docs/game-design-pillars.md`, then write a short dated note. If two points fail after tuning, the direction changes.
- [ ] **T22 500 units at 60 fps or better** on both machines, plus the headless 500/2000-unit `tools/bench` run.
- [ ] **T23 Unreal build in CI, then remove Godot,** following the GRY-298 checklist. Public CI keeps running all sim tests without Epic credentials.
- [ ] **T24 Packaged build that runs on both machines.** It must find the self-contained `sim-host` without a global .NET install.

## Tri-Point follow-ups (added 2026-11-24, after T08)

T08 built the water version of Tri-Point (`content/maps/tri-point.json`, generated by `tools/mapgen/tri-point.mjs`) but the sim can only use its land: water and cliffs are blocked cells. These tasks make the rest of the map real. Each is a sim change, so each one re-pins golden hashes in the same PR.

- [ ] **T25 [blocks others] Terrain and movement domains.** Teach the sim the map's tile types (land, water, cliff, bridge) and give units a domain: land units on land and bridges, naval on water and under bridges, air anywhere, hover/amphibious on both (Skimmer, Ripple, Sever-class walking ashore, beach landings). Navigation plans per domain; combat honours the `targets` field (`ground` / `air` / `sea`) that unit files already carry.
- [ ] **T26 Destructible bridges and causeways — units must be able to attack them.** Bridges (`=` tiles, including the strait, channel and pier bridges) and causeways are structures with hit points that any unit able to hit structures can target and destroy. Destroyed, they become impassable to ground units (ships can still pass under a broken bridge), and path plans through them are invalidated. An engineer touching one repairs it instantly to full health and is consumed (map design rule). Needs: a targetable terrain-structure entity, a nav-grid update when one dies, `attack` orders on them from Unreal, and a visible broken state. Depends on T25 for the bridge tile type; T09 events for showing the hit and the collapse. *Built (awaiting play-test): bridges in their own store (not units), `bridge` command, collapse to water, engineer repair, Unreal Ctrl+RMB / engineer RMB, HP bar and collapse view. Causeways too: mapgen lists their over-water cells as `map.causeways`; standing they are land (ships can't pass), down they are water, and nothing may be built on them.*
- [ ] **T27 Ascendant as a real third faction.** `UnitCatalog` mapped Hegemony to faction 1 and everything else to 0, so Coalition and Ascendant were allies (fixed 2026-11-26: `UnitCatalog.FactionIndex`). Make factions 0/1/2 and seat Ascendant in Tri-Point's empty top home.
- [ ] **T28 Captures: Seam radars and the central rig.** Engineer-touch capture (consumed, instant), the Tri-Point control states (Neutral, Occupied, Contested, Held, Fortified) with weighted majority, income and the reveal rules from the map design.
- [ ] **T29 Bring in the rest of the 79 units by domain,** after T25 (and their owned mechanics: Marking, Conscription, Phase) — infantry and vehicles first, then naval, then air. *Owned mechanics built (awaiting play-test): Marking (on-hit + aura with the Sensor Command gate; no line of sight yet), Conscription (hall/field refunds, Sapper detonation), Phase (2 s / 10 s). Not yet: Phase and Conscription player buttons (the player is Coalition, and there is no AI yet), per-unit Phase stat tax, and the unit-by-unit roll-out itself.*

## Base building, production and economy (added 2026-11-24)

The sim already has credits, finite veins, the harvester loop, `place` / `train` / `cancel` with a queue cap of 5 and prerequisites, and the power parity gate. What it lacks: buildings don't block cells, refineries are fixed scenario points rather than entities, and there is no construction-yard deploy. Unreal shows none of it. Sim tasks re-pin golden hashes in the same PR.

- [x] **T30 [blocks others] Host snapshot carries the economy.** Snapshots only send units (id, faction, x, y). Add each faction's credits and power, buildings (type, cells, hp, build progress), production queues, and vein amounts left. Protocol change, so bump or extend protocol v1 and update `tools/sim-host` tests. *.NET only.*
- [x] **T31 Buildings have footprints and block cells.** A placed building occupies its footprint on the nav grid; `place` is refused on blocked, occupied or out-of-radius cells; units path around it; when it dies its cells free up and paths re-plan. Buildings take damage and can be destroyed (same targetable-structure work as bridges in T26). Win/lose: buildings already count in `sim/match` (a side is alive while anything it owns is), so once they can die the match ends only when all of a side's units and buildings are gone; add a match test that a lone building keeps its side alive, and split the T19 screen's losses into units and buildings. *.NET only.*
- [x] **T32 Construction yard deploy and build radius.** An MCV-style unit deploys into the construction yard; new buildings must be placed within a radius of your existing buildings. *.NET only.*
- [x] **T33 Refineries and harvesters as real entities.** A refinery is a building you place, not a scenario point. Harvesters gather from a vein, return to the nearest friendly refinery, unload, repeat; they re-route when it dies and can be killed or raided. Credits rise on each unload. *.NET only.*
- [x] **T34 Resource fields on Tri-Point.** Put veins on the map per the map design (main, natural and contested fields), in the map file and the generator, with their amounts. Re-pin the Tri-Point hash.
- [x] **T35 Unreal: credits, power and resource display.** Top bar with credits and power (used / produced, red when negative). Driven only by T30 snapshots.
- [x] **T36 Unreal: build menu and building placement** (after T30, T31). Sidebar with what you can build (greyed when prereqs, power or credits are missing); a ghost footprint follows the cursor, green or red for valid; LMB sends `place`, RMB cancels. Buildings drawn from snapshots with a construction progress state.
- [x] **T37 Unreal: unit production queue.** Select a production building, click a unit to `train` (shift for 5), right-click to `cancel`; show queue and progress. Rally point by right-clicking ground with the building selected (needs a small sim command too).
- [x] **T38 Unreal: harvesting.** Draw veins (shrinking as they empty), refineries and harvesters with a carrying state; right-click a vein with harvesters selected to send them there.

## Phase 5 — Map scale (added 2026-11-26)

Play-testing T30–T38 showed that Tri-Point is far too small. Its 130×116 grid is about the size of a 1v1 StarCraft II map (148×148 and 128×132 on Liquipedia), but only about 2,430 cells are land. Team Liquid map contests ask for roughly 14,000–20,000 playable cells, and that is for two players. Tri-Point is a three-way map (1v1v1) and should play at the size of a 4–5 player map:

- **1v1v1:** three factions, each with a main and at least one expansion / FOB slot of their own.
- **Shared ground:** a central point of interest, plus side paths around the main lanes.
- **Water:** a large share of the map is sea, and it has to stay. Naval units (T25) need room on the outer sea and in the internal water areas, so they must be able to reach the inner water by passing under the bridges.

Our rifleman (1.8 cells/s) and 0.3-cell unit radius are close to SC2's own scale, so the map is what grows, not the units.

- [ ] **T39 Scale Tri-Point up to 4–5 player size (1v1v1).** Regenerate it with `tools/mapgen/tri-point.mjs`.
  - **Land target** (to confirm before building): SC2 four-player maps run about 160×156 (New Bed of Chaos LE). Three factions each with a main and an expansion, plus a centre and side paths, at 4–5 player scale: aim for about 20–25k walkable cells, around 8–10× today's land. With much of the sea kept, the grid comes out at roughly 220×200 to 260×230.
  - **Layout:** three mains, each with at least one expansion / FOB slot and its own resource fields (T34 rules), a central point of interest, side paths, and the seams and contested fields scaled to match. Rush distance grows with the map.
  - **Water connectivity:**
    - Every internal water area joins the outer sea through at least one channel that passes under a bridge.
    - Under-bridge channels are at least as wide as the largest ship (naval footprint from content; T25).
    - The generator checks this with a water flood fill and exits non-zero if any water body is cut off.
  - **Scale checks:**
    - A* pathing on the bigger grid with 500 units (`tools/bench` on the new map, under the 50 ms tick budget).
    - Camera zoom limits, edge-scroll speed, minimap texture size and the T32 build radius.
  - **Hashes:** re-pin the Tri-Point golden hashes with a reason comment.

---

## After the demo (not in scope)

- Economy and production polish beyond T30–T38 (sell, repair, superweapons, tech tiers).
- Power, the other factions and fog of war.
- **Multiplayer (lockstep).** Nothing to build now: the tick-stamped, faction-tagged Commands, fixed-point maths and pinned replay hashes keep it possible. Two things will need changing when it starts:
  - Unreal assumes the player is faction 0 and stamps each order with the current tick, but lockstep needs orders stamped a few ticks ahead (input delay).
  - Cross-OS determinism (Windows vs Linux) has never been checked. Run one scenario on both machines and compare hashes.

## Splitting the work

Phase 1 needs no Unreal, so it suits whoever doesn't have the engine installed. Phases 2–3 need Unreal. T01 and T05–T09 hold up almost everything else, so start there.
