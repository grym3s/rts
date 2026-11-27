# sim/transport — riding inside things: transports, bunkers, the Tunnel Network, Teleport Nodes

Owns: `TransportSystem` — the `load` / `unload` / `teleport` commands and boarding. A passenger has `Unit.Inside` set to its holder: it moves with the holder, can't be seen, targeted or pushed, and doesn't act (garrisons are the exception: combat lets bunker passengers shoot out). Slots: infantry 1, any other ground unit 4 (`TransportSystem.Slots`); `Abilities.Carries` = "infantry" or "ground". A dead holder's passengers die with it, except a tunnel: they move to another tunnel of the same side (all tunnels share one hold), and die only when the last one goes. Teleport Nodes: units within `TeleportReach` of any own node move instantly to the chosen node.
Reads: `Unit.Ability` (Capacity, Carries, Garrison, Tunnel, Teleport), `GameMap` (where unloading units may stand).
Writes: `Unit.Inside`, `Unit.BoardTarget`, positions of passengers and teleported units, move orders for boarders.
Runs at: tick step 4b, after production and before navigation.
Tests: `sim/tests/TransportTests.cs`; `make test`.
Do NOT: let a passenger act through any other system (each checks `Inside`); hash anything not set (Inside / BoardTarget mix only while set).
Change impact: `map/effects/CONTEXT.md` (units: new state)
Known limits (2026-11-28): no per-passenger IFV weapon change; air transports unload where they hover (onto walkable land only).
