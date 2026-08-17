# game — Godot presentation and input

Owns: camera, input → `Command`s, selection, rendering of sim state, UI, debug overlays. `Main.cs` owns the `SimWorld` and steps it at `SimWorld.TicksPerSecond`, interpolating for display.
Reads: `SimWorld` state and events (read-only), `content/`.
Writes: `Command`s into the sim's inbox. Nothing else in `sim/`.
Tests: `make godot-test` (headless import + run); gdUnit4 scene tests to be added with the first area.
Do NOT: mutate sim state; put game rules here; use sim `Fix64` for anything but conversion at the render boundary.
Areas (create a folder + CONTEXT.md with the issue that adds them): `camera/`, `selection/`, `orders/` (input→command), `render/`, `debug/`, `ui/`.
Godot hygiene: one scene per area, small; `.tscn`/`.tres` text; do not edit another area's scene in the same PR unless the issue says so.
Known limits (2026-08-17): skeleton only — empty world + camera; project files hand-written, first editor open may rewrite `project.godot`/`Game.sln` — commit that as its own change.
