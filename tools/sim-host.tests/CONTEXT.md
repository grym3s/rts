# tools/sim-host.tests — protocol + parity tests for the sim host

Owns: xunit tests for `tools/sim-host` — codec accept/reject (malformed JSON, unknown fields/kinds, bad types), session behaviour (init/step, empty rosters/command lists, duplicate/skipped ticks, wrong command stamps, unsupported protocol versions, bounded errors), and deterministic replay parity: every golden scenario from `.github/workflows/ci.yml` re-driven through the host must reproduce the scenario runner's final state hash.
Reads: `tools/sim-host` public API, `content/scenarios/*.json`. Writes: nothing.
Tests: run by `dotnet test tools/sim-host.tests/SimHost.Tests.csproj`.
Do NOT: change sim behaviour to make a hash pass — a parity failure means host wiring drifted from `tools/scenario/Program.cs`.
