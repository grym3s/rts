#!/usr/bin/env bash
# Structure checks (CI runs this). Fails loudly; fix structure, not the script.
set -uo pipefail
cd "$(dirname "$0")/.."
fail=0
err() { echo "CHECK FAIL: $*"; fail=1; }

# 1. every working folder has a CONTEXT.md
for d in sim game tools/* content; do
  [ -d "$d" ] && [ ! -f "$d/CONTEXT.md" ] && err "$d has no CONTEXT.md"
done
for d in sim/*/; do
  [ -d "$d" ] || continue
  case "$d" in sim/tests/|sim/bin/|sim/obj/) continue;; esac
  [ -f "$d/CONTEXT.md" ] || err "$d has no CONTEXT.md"
done
for d in game/*/; do
  [ -d "$d" ] || continue
  case "$d" in game/.godot/|game/bin/|game/obj/) continue;; esac
  [ -f "$d/CONTEXT.md" ] || err "$d has no CONTEXT.md"
done

# 2. sim never references an engine
if grep -rniE 'using Godot|Godot\.|GodotSharp|UnityEngine' sim --include='*.cs' --include='*.csproj' | grep -v '/obj/' | grep -v '/bin/'; then
  err "sim/ references an engine (ADR 0001)"
fi
# 2b. no floats in sim source (double/float keywords), except explicitly allowed conversion sites
if grep -rnE '\b(float|double)\b' sim --include='*.cs' | grep -v '/obj/' | grep -v 'sim/tests/' | grep -v 'FromDouble\|ToDouble\|// float-ok\|///'; then
  err "float/double used inside sim/ (ADR 0003) — mark deliberate render-boundary conversions with // float-ok"
fi

# 3. generated files are fresh.
# check must be READ-ONLY: gen-indexes.sh rewrites in place, so snapshot first,
# compare, then restore — otherwise `make check` silently mutates tracked files
# and the reported staleness cannot be reproduced on a second run.
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
cp AGENTS.md "$tmp/AGENTS.md" 2>/dev/null || true
cp decisions/_index.md "$tmp/dindex.md" 2>/dev/null || true
_scripts/gen-indexes.sh >/dev/null
cmp -s AGENTS.md "$tmp/AGENTS.md" || err "AGENTS.md is stale — run make gen"
cmp -s decisions/_index.md "$tmp/dindex.md" || err "decisions/_index.md is stale — run make gen"
cp "$tmp/AGENTS.md" AGENTS.md 2>/dev/null || true
cp "$tmp/dindex.md" decisions/_index.md 2>/dev/null || true

# 4. CLAUDE.md stays small
lines=$(wc -l < CLAUDE.md); [ "$lines" -le 70 ] || err "CLAUDE.md is $lines lines (limit 70) — move payload to a shelf"

# 5. root map shelves are not empty stubs
for d in map/objects map/processes; do
  [ -d "$d" ] && [ -z "$(ls -A "$d" 2>/dev/null | grep -v _index.md)" ] && err "$d exists but is empty (System Map forbids empty shelves)"
done

[ $fail -eq 0 ] && echo "structure checks passed"
exit $fail
