# tools/bench — movement scale check

Owns: the 500-unit × 1000-tick scenario in code and its ms/tick report.
Reads: sim core/world/orders/navigation/units (as libraries), content/units for speeds.
Writes: stdout only. Runs: by hand or CI (not gating yet — numbers feed `sim/navigation/CONTEXT.md` limits).
Usage: `dotnet run --project tools/bench -- [units] [ticks]`.
Change impact: none golden — the bench is not a golden source; scenario hashes are.
Results (2026-11-28, Windows dev machine, Release): 500 units 0.65 ms/tick, 2000 units 8.75 ms/tick (1000 ticks, all arrived); a full three-AI Tri-Point skirmish (~250 units, every system on) runs about 0.15 ms/tick. The tick budget at 20 t/s is 50 ms. The 60 fps half of T22 is a play-test on both machines.
