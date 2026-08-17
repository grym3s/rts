# RTS (working title)

A real-time strategy game built by two humans and their coding agents. Deterministic .NET simulation, Godot 4 presentation.

## Run

- Install the .NET SDK version in `global.json` and Godot 4.7.x **.NET** build (see `docs/workflow.md`).
- `make test` — build and test the simulation headlessly (no Godot needed).
- `make scenario S=content/scenarios/smoke.json` — run a scenario headlessly.
- `make run` — open the game in Godot (requires `godot` on PATH).
- `make check` — structure checks CI runs.

## Structure

Start at `CLAUDE.md` (routing) and `CONTEXT.md` (how to walk). Everything else is one hop from there.
