---
status: accepted
date: 2026-08-17
---
# 0004 — The repository is an ICM System Map subject with a thin factory

- Root `CLAUDE.md` routes (< 60 lines); `AGENTS.md` is generated from it.
- Every working folder (`sim/<system>/`, `game/<area>/`, `tools/<tool>/`) carries a `CONTEXT.md` contract (owns / reads / writes / tests / do-not-touch), reviewed in the same PR as its code. CI checks presence.
- `map/` holds only what a folder cannot say about itself: shared-noun cards and the change-impact index. Cards cite `path:line`; `verified` needs a commit hash. Empty shelves are not created.
- `decisions/` and `docs/` are the factory; GitHub Issues/PRs are the working state. No second methodology tree (e.g. `.planning/`) inside the walkable repo.
- No method shelf until the same lesson recurs three times.

Why: contracts co-located with code are the only ones that stay true; the map cites rather than duplicates; working state has one home. Full reasoning: foundation review §2, §5.
