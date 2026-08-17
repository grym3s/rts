# Conventions

## Code
- C# 13; `sim/` targets `net8.0` (Godot), tools/tests `net10.0`; nullable enabled, `dotnet format` clean. Sim code: no floats, no LINQ in hot loops, no allocations per tick where avoidable, no engine namespaces.
- Sim systems are static functions over `SimWorld`, run in the order documented in `sim/CONTEXT.md`.
- Everything the player or AI does is a `Command` (`sim/core/Commands.cs`).
- Godot: one scene per area, `.tscn`/`.tres` text, small scenes; C# scripts next to their scene.

## Content
- JSON with a `schemaVersion` field from the first file. Ids are kebab-case.

## Git
- Branches: `feat/<folder>-<slug>`, `fix/…`, `agent/<who>/<slug>`.
- Commits: `feat(nav): …`, `fix(combat): …` — scope = folder name. Squash-merged.
- PR template fields are mandatory, including "CONTEXT.md files touched / why not".

## Contracts
- Every `sim/<system>/`, `game/<area>/`, `tools/<tool>/` folder has a `CONTEXT.md` (20–40 lines): owns / reads / writes / runs at / tests / do-not-touch / change impact / known limits (dated).
- Contracts describe boundaries and where to look, never algorithms.
