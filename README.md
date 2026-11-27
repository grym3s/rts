# RTS (working title)

A real-time strategy game built by two humans and their coding agents. Deterministic .NET simulation, Unreal 5.8 presentation (through the `tools/sim-host` bridge).

## Run

- Install the .NET SDK version in `global.json` and Unreal Engine 5.8 (see `docs/workflow.md`).
- `make test` — build and test the simulation headlessly (no engine needed).
- `make scenario S=content/scenarios/smoke.json` — run a scenario headlessly.
- `make host-publish` — build the self-contained `sim-host` the Unreal project launches (copy it to `unreal/dist/<rid>/`).
- Open `unreal/RtsBridge.uproject` in the editor and press Play: pick a side, the other two are computer players. Build and test steps are in `unreal/CONTEXT.md`.
- `make check` — structure checks CI runs.

The Godot shell in `game/` (`make run`, `make godot-test`) is legacy and will be removed once `docs/godot-retirement-checklist.md` is satisfied.

## Structure

Start at `CLAUDE.md` (routing) and `CONTEXT.md` (how to walk). Everything else is one hop from there.
