# Workflow

## Tools (pinned)
- .NET SDK: see `global.json` (10.0.x). `sim/` targets net8.0 (Godot-compatible); tests and tools target net10.0. Godot: **4.7.1 .NET** build; `godot` on PATH.
- `make test` (sim), `make check` (structure), `make godot-test` (headless import + run), `make scenario S=…`, `make run`.

## Branching and review
- `main` is protected: PR required, CI green, 1 approval, squash merge, no direct pushes (humans included).
- A human reviews any PR touching `sim/core/`, `sim/CONTEXT.md`, `decisions/`, or the sim/game seam. Everything else: agent review + CI, human as approver of record. Agents open PRs; agents never merge.
- Issues are the task queue; labels are folder names. One issue → one PR where possible.

## CI (`.github/workflows/ci.yml`)
1. `dotnet build` + `dotnet test` on `sim/` and `tools/` (every push).
2. Structure checks: `_scripts/check.sh` (contracts present, no engine refs in sim, indexes fresh, formatting).
3. Godot headless import + build (PRs).

## Knowledge
- Observation → PR description. Promotion → ADR or `docs/` line, by PR. Contradiction → new ADR with `supersedes`.

## Assets
- Git LFS for binaries (`.gitattributes`). Keep slice binaries tiny.
