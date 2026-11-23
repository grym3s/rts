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

- [ ] **T05 [blocks others] Enemy behaviour: hold, chase, return.** Enemies hold position, chase anything that comes in range, then return to their spot. No such rule exists in `sim/` yet.
- [ ] **T06 [blocks others] Win and lose condition.** Win when every enemy is dead, lose when all your units are. Needs a new sim event that the host passes on.
- [ ] **T07 Restart a match.** The host refuses a second `init` (`already_initialized`). Either add a reset or have Unreal relaunch the host cleanly.
- [x] **T08 [blocks others] Demo map and 30v30 scenario.** One hand-made map with blocked cells and chokepoints, plus a 30v30 scenario file. Pin its replay hash in CI. *Done 2026-11-24 on `feat/tri-point-map`: Tri-Point, water version, 30v30 T1 combined arms.*
- [ ] **T09 [blocks others] Richer sim events.** Events only carry (tick, subject, target). Health bars and effects need damage amounts and deaths, so this is a sim contract change first, then the host protocol.

## Phase 2 — Unreal controls (finish slice 4)

All game rules stay in the sim. Unreal only sends Commands.

- [x] **T10 Attack, attack-move and stop orders** (after T02). Unreal only sends `move` today. The host already accepts the others. *Done on `feat/unreal-orders`.*
- [x] **T11 Box select, shift-add, control groups 1–3** (after T02). Copy the behaviour from `game/selection/`. *Done on `feat/unreal-box-select`.*
- [x] **T12 Order feedback within one frame** (after T10). A ground marker or acknowledgement on every order. This is pillar 1. *Done on `feat/unreal-feedback-smoothing`.*
- [x] **T13 Smooth movement and facing** (after T02). Interpolate positions between the 20 t/s snapshots and turn units toward where they're moving. *Done on `feat/unreal-feedback-smoothing`.*
- [ ] **T14 Draw the map's terrain and obstacles** (after T08). Show blocked cells so chokepoints can be read. *Basic version on `feat/tri-point-map`: tiles drawn as boxes and on the minimap; real art later.*

## Phase 3 — Make fights readable (slice 5)

- [ ] **T15 Health bars, deaths and corpse removal** (after T09). Driven only by snapshots and events.
- [ ] **T16 Show who is shooting whom** (after T09). Tracers or muzzle flashes per shot. This is pillar 4.
- [ ] **T17 Rifleman with `idle`, `move` and `fire` animations** (after T03). Follow the GRY-295 spec. Check axis, scale and clip names in the pinned editor first.
- [ ] **T18 One representative explosion.** Check that it reads well at close, default and far zoom (GRY-297).
- [ ] **T19 Win/lose screen with restart** (after T06 and T07). The only menu the demo needs.
- [ ] **T20 Debug overlays on F-keys.** Paths, targets, unit IDs and state, tick counter and sim step in ms. The Godot version has these in `game/debug/`.

## Phase 4 — Prove it and lock it down (slice 6)

- [ ] **T21 [blocks others] Fun test.** Both of us play it and judge it against the 6 points in `docs/game-design-pillars.md`, then write a short dated note. If two points fail after tuning, the direction changes.
- [ ] **T22 500 units at 60 fps or better** on both machines, plus the headless 500/2000-unit `tools/bench` run.
- [ ] **T23 Unreal build in CI, then remove Godot,** following the GRY-298 checklist. Public CI keeps running all sim tests without Epic credentials.
- [ ] **T24 Packaged build that runs on both machines.** It must find the self-contained `sim-host` without a global .NET install.

## Tri-Point follow-ups (added 2026-11-24, after T08)

T08 built the water version of Tri-Point (`content/maps/tri-point.json`, generated by `tools/mapgen/tri-point.mjs`) but the sim can only use its land: water and cliffs are blocked cells. These tasks make the rest of the map real. Each is a sim change, so each one re-pins golden hashes in the same PR.

- [ ] **T25 [blocks others] Terrain and movement domains.** Teach the sim the map's tile types (land, water, cliff, bridge) and give units a domain: land units on land and bridges, naval on water and under bridges, air anywhere, hover/amphibious on both (Skimmer, Ripple, Sever-class walking ashore, beach landings). Navigation plans per domain; combat honours the `targets` field (`ground` / `air` / `sea`) that unit files already carry.
- [ ] **T26 Destructible bridges and causeways — units must be able to attack them.** Bridges (`=` tiles, including the strait, channel and pier bridges) and causeways are structures with hit points that any unit able to hit structures can target and destroy. Destroyed, they become impassable to ground units (ships can still pass under a broken bridge), and path plans through them are invalidated. An engineer touching one repairs it instantly to full health and is consumed (map design rule). Needs: a targetable terrain-structure entity, a nav-grid update when one dies, `attack` orders on them from Unreal, and a visible broken state. Depends on T25 for the bridge tile type; T09 events for showing the hit and the collapse.
- [ ] **T27 Ascendant as a real third faction.** `UnitCatalog` maps Hegemony to faction 1 and everything else to 0, so Coalition and Ascendant are allies today. Make factions 0/1/2 and seat Ascendant in Tri-Point's empty top home.
- [ ] **T28 Captures: Seam radars and the central rig.** Engineer-touch capture (consumed, instant), the Tri-Point control states (Neutral, Occupied, Contested, Held, Fortified) with weighted majority, income and the reveal rules from the map design.
- [ ] **T29 Bring in the rest of the 79 units by domain,** after T25 (and their owned mechanics: Marking, Conscription, Phase) — infantry and vehicles first, then naval, then air.

---

## After the demo (not in scope)

- Economy and production in the UI: placing buildings and training units. The sim already has these.
- Power, the other factions and fog of war.
- **Multiplayer (lockstep).** Nothing to build now: the tick-stamped, faction-tagged Commands, fixed-point maths and pinned replay hashes keep it possible. Two things will need changing when it starts:
  - Unreal assumes the player is faction 0 and stamps each order with the current tick, but lockstep needs orders stamped a few ticks ahead (input delay).
  - Cross-OS determinism (Windows vs Linux) has never been checked. Run one scenario on both machines and compare hashes.

## Splitting the work

Phase 1 needs no Unreal, so it suits whoever doesn't have the engine installed. Phases 2–3 need Unreal. T01 and T05–T09 hold up almost everything else, so start there.
