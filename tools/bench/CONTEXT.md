# tools/bench — movement scale check

Owns: the 500-unit × 1000-tick scenario in code and its ms/tick report.
Reads: sim core/world/orders/navigation/units (as libraries), content/units for speeds.
Writes: stdout only. Runs: by hand or CI (not gating yet — numbers feed `sim/navigation/CONTEXT.md` limits).
Usage: `dotnet run --project tools/bench -- [units] [ticks]`.
Change impact: none golden — the bench is not a golden source; scenario hashes are.
