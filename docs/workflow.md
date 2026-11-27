# Workflow

## Tools (pinned)
- .NET SDK: see `global.json` (10.0.x). `sim/` targets net8.0; tests and tools target net10.0.
- Unreal Engine **5.8** (presentation, `unreal/`): build `RtsBridgeEditor`, run the `RtsBridge` automation tests headless (`unreal/CONTEXT.md`). It launches the self-contained host from `unreal/dist/<rid>/` (`make host-publish`).
- `make test` (sim), `make host-test` (bridge), `make check` (structure), `make scenario S=…`.
- Legacy Godot shell (`game/`, retiring): Godot 4.7.1 .NET, `make godot-test`, `make run`.

## Branching and review
- `main` is protected: PR required, CI green, 1 approval, squash merge, no direct pushes (humans included).
- A human reviews any PR touching `sim/core/`, `sim/CONTEXT.md`, `decisions/`, or the sim/game seam. Everything else: agent review + CI, human as approver of record. Agents open PRs; agents never merge.
- Issues are the task queue; labels are folder names. One issue → one PR where possible.

## CI (`.github/workflows/ci.yml`)
1. `dotnet build` + `dotnet test` on `sim/` and `tools/` (every push).
2. Structure checks: `_scripts/check.sh` (contracts present, no engine refs in sim, indexes fresh, formatting).
3. Godot headless import + build (PRs; legacy until retirement). Unreal has no CI lane: it needs Epic engine binaries, so the editor build and automation tests run on our machines before merge.

## Knowledge
- Observation → PR description. Promotion → ADR or `docs/` line, by PR. Contradiction → new ADR with `supersedes`.

## Assets
- Git LFS for binaries (`.gitattributes`). Keep slice binaries tiny.
