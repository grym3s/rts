# Slice 4 headless sim test coverage (GRY-296)

Unreal 5.8.3 is not installed yet, so GRY-288 (interactive GUI play-test) and
GRY-290 are blocked. This task advances slice 4 at the simulation level instead.

Tests live in `sim/tests/Slice4GameplayLoopTests.cs` and run under `make test`.
They exercise the same sim-level behaviour the Unreal GUI play-test drives,
using the same wiring as `NavigationTests` (OrderSystem + NavigationSystem
registered as static systems over a shared UnitStore and orders dictionary).
The tests construct only SimWorld, OrderSystem and NavigationSystem. They do
not exercise a host, protocol or bridge; every check below is sim-only.

## What is covered headlessly (sim-only)

| GRY-288 play-test check | Headless? | Sim evidence |
|---|---|---|
| issue a move | Sim-only | `Single_unit_move_order_*`, `Multiple_units_move_order_*`, `Multiple_units_with_distinct_targets_*` — move orders for single and multiple units, arrival, distinct targets |
| confirm host state changes | Sim-only (determinism only) | `Same_input_over_N_ticks_gives_identical_state_hashes` — identical `StateHash()` every tick over 60 ticks with move/stop/re-issue. Proves sim-level determinism; host round-trip and state propagation are not exercised. |
| confirm host failure does not fabricate state | Not covered headlessly | `Idle_world_does_not_fabricate_movement` shows an uncommanded sim world moves nothing (sim-only proxy). It does not exercise a host, protocol or bridge failure, so it cannot rule out Unreal fabricating snapshots after host failure (`unreal/Source/RtsBridge/Private/RtsBridgeGameMode.cpp:201-209`). Remains in GRY-288's acceptance. |

Additional sim-level behaviour covered by the same tests:

- Arrival and stop behaviour: `Single_unit_move_order_moves_toward_target_and_arrives`, `Stop_command_halts_a_moving_unit_and_keeps_it_put`, `Stop_command_on_idle_unit_is_a_noop_that_stays_put`.
- Re-issuing an order mid-move: `Re_issuing_move_mid_move_redirects_the_unit_to_the_new_target`, `Re_issuing_move_after_arrival_resumes_movement_from_rest`.

## What still needs the GUI / integration

| GRY-288 play-test check | Why it needs the GUI |
|---|---|
| confirm host state changes (host round-trip/state propagation) | Needs the host/bridge (Unreal); the sim-only hash check does not cover it |
| confirm host failure does not fabricate state | Needs the host/bridge (Unreal); host failure must be visible, never replaced by local rules (`RtsBridgeGameMode.cpp:201-209`) |
| pan/zoom/rotate camera | Presentation layer only; no sim equivalent |
| select | Input/presentation only; no sim equivalent |

These checks require the Unreal editor and a display (the host checks also need
the bridge). They cannot be verified headlessly and remain part of GRY-288's
acceptance.

## Notes

- No changes to `unreal/`.
- Determinism is asserted on `SimWorld.StateHash()`, which mixes the tick, id
  count, and per-unit position/hp (via `UnitStore.Hash`). Identical inputs to
  two same-seed worlds must produce identical hashes every tick.
- `Idle_world_does_not_fabricate_movement` proves exactly two things: (1) an
  uncommanded sim world keeps units at their spawn position (no spurious
  movement), and (2) the state hash differs across ticks because the hash
  mixes the tick counter. `Assert.NotEqual` on two hashes does not isolate
  which fields changed, and the test does not exercise a host failure.
- "Arrival" is signalled by the order being removed from the orders dictionary
  (the navigation system clears it when the unit reaches its target within the
  0.25 arrival threshold). Assertions use a distance bound of 1.0, matching
  `NavigationTests`.
