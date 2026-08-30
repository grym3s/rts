# Ascendant — exotic tech & mobility

> Rewrites the rules of the fight.

The Ascendant trade durability for movement and trickery. Everything phases in fast, dies fast, and repositions faster than either rival can respond. Its owned verb — **Phase** — lets units blink out of targetability. Its holes are **transports (it teleports), the interceptor, the heavy infantry, and the true submarine** (it cloaks surface hulls instead). Its whole logistics model is different: a **Teleport Network** replaces every transport in the game (reference Part 7).

## Global modifier (applied to the base ladders in `CONTEXT.md`)

| Dial | Ascendant | Effect |
|---|---|---|
| Unit cost | **×1.2** | the most expensive army; every unit is a commitment |
| Unit HP | **−20%** | glass — loses straight fights, wins by not being in them |
| Speed | **fast** | best repositioning in the game |
| Production time | **×0.85** | phases units in quickly |
| Building construction | **×0.7**, structure HP **−25%** | expands fastest, sieges easiest |

Tables show the Ascendant-adjusted numbers directly (costs rounded to 25).

## Economy

Slower per-trip harvest, but the **Cultivator's fields never deplete** — Ascendant income is lower early and *infinite* late. It never has to relocate its economy, so a defended Ascendant base out-scales on time. Single credit currency (reference Part 6.7).

## Owned verb — Phase

Ascendant units can briefly enter an **untargetable** state — dodging a volley, crossing a kill-zone, escaping a lost fight. Phase is on a cooldown and costs the unit its own actions while active, so it is an escape/timing tool, not a permanent shield. Combined with the faction's speed, it means the Ascendant chooses every engagement. The power budget: Phase units carry **less HP/DPS** than the mirror slot to pay for the trick (reference Part 5, "cool ability = reduced stats").

## Logistics — the Teleport Network (replaces all transports)

The Ascendant builds **Teleport Nodes** instead of transports. Any friendly unit can move between nodes instantly. This is why the roster has *no air or sea transport* in any domain — the network is the whole logistics model, and its counter is destroying or contesting the nodes rather than shooting a transport out of the sky.

## Buildings (all phase in — fast, fragile)

| Structure | Tier | Prereq | Build time | Cost | HP | Power | Produces / unlocks |
|---|---|---|---|---|---|---|---|
| Seed Core (MCV → Construction Yard) | T1 | — | 8s deploy | 2500 | low | — | all structures; build radius |
| Conduit | T1 | Core | 14s | 400 | very low | **+** | power |
| Wellspring | T1 | Core | 21s | 1500 | low | − | credits; ships 1 Cultivator; **fields never deplete** |
| Manifold | T1 | Core | 14s | 400 | low | − | infantry |
| Forge | T1 | Core | 25s | 1500 | low | − | ground vehicles |
| Nexus | T2 | Forge | 35s | 1500 | low | −− | T2 units, Aerie |
| Aerie | T2 | Nexus | 28s | 800 | low | − | aircraft |
| Ascendancy | T3 | Nexus | 53s | 2500 | med | −− | T3 units, Superweapon |
| Rift (superweapon) | T3 | Ascendancy | 84s | 3000 | med | −−− | singularity strike (charge power) |
| **Teleport Node** | T1 | Core | 14s | 600 | very low | − | instant travel between nodes (replaces transports) |
| Phase Turret | T1 | Manifold | 11s | 500 | low | − | static defense (phases between shots) |
| Skyward Spire | T2 | Nexus | 14s | 800 | low | − | anti-air |
| Rift Node | T2 | Nexus | 21s | 1000 | low | −− | long-range static defense (line damage) |
| Tidal Forge (Shipyard) | T1* | Core + shore | 32s | 1000 | low | − | naval units |
| Anchor Spire (coastal battery) | T3 | Tidal Forge | 28s | 1200 | med | −− | outranges bombardment ships (range 28) |
| Drift Platform (Sea Platform) | T2 | Tidal Forge | 18s | 800 | low | − | offshore power/defense/expansion |

\* Naval is post-slice; shown for completeness (reference Part 6).

## Units — Infantry (from Manifold)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Adept | Basic body | T1 | 5s | 125 | Infantry / Energy | short range; **phases** to close or escape |
| Disruptor | Anti-armor | T2 | 8s | 350 | Infantry / Energy | **drains vehicles** (slows/disables) rather than raw AP |
| Skyward | AA **+ anti-infantry** | T2 | 8s | 350 | Infantry / Autocannon | dual-role air denial |
| Weaver | Engineer | T1 | 12s | 600 | Infantry / — | rebuilds structures; **cannot capture** |
| Resonator | AoE / garrison-clear | T2 | 9s | 425 | Infantry / Explosive | resonance burst |

*No heavy infantry* — the Ascendant has no frontline soak; it phases and kites instead.

## Units — Vehicles (from Forge)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Cultivator | Harvester | T1 | 10s | 850 | Light / — | **field never depletes** — the Ascendant economy |
| Skimmer | Scout | T1 | 10s | 600 | Light / Autocannon | **hovers over terrain**; fast raids |
| **Glaive** | Main battle tank | T1 | 17s | 1075 | Light / Energy | fast, **phases**, low HP — the Ascendant baseline; also handles air |
| Lance Platform | Light AT | T2 | 14s | 725 | Light / Energy | glass cannon vs Heavy |
| Rift Lance | Artillery | T3 | 26s | 1200 | Light / Energy | **line damage** (pierces a row), range 14 |
| Anchor | Support | T2 | 19s | 1075 | Light / — | **shields allies** in radius |
| **Monolith** | Superheavy | T3 | 47s | 3000 | Heavy / Energy | the statement piece; slow exception to the fast rule |

*No dedicated mobile AA (the Glaive covers air) and no APC (teleport instead).*

## Units — Aircraft (from Aerie, T2+)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Eye | Scout / detector | T2 | 13s | 475 | Air / — | cheap vision |
| Wraith | Gunship | T2 | 24s | 1450 | Air / Energy | anti-ground; phases |
| Singularity | Bomber (AoE) | T3 | 34s | 1925 | Air / Explosive | area implosion, base-breaker |

*No interceptor* — the Glaive and Skyward carry the air-defense load; there is no dedicated air-superiority fighter, and no air transport (teleport).

## Units — Specialists

| Unit | Role | Tier | Time | Cost | Built from | Notes |
|---|---|---|---|---|---|---|
| Seer | Sniper (+ detector) | T2 | 14s | 725 | Manifold | phasing energy marksman — long range, low HP, blinks out after firing; also reveals stealth |
| Oracle | Commando | T3 | 38s | 1800 | Manifold | pop-capped high-value unit; phases |
| Mindbender | Infiltrator (+ area cloak) | T2 | 14s | 950 | Manifold | mind-control / subversion; **cloaks nearby friendly units** |

The Ascendant sniper is the Seer: an energy marksman that phases out of retaliation, doubling as a detector — precision *and* concealment in one exotic package.

## Units — Naval (post-slice; from Tidal Forge; no submarine, no transport)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Ripple | Corvette | T1 | 17s | 725 | Light naval / Autocannon | **hovers over shallows**; spots |
| Sever-class | Destroyer | T2 | 34s | 1450 | Light naval / Naval gun | **walks ashore** (amphibious); low armor — pays for the trick (reference Part 6.5) |
| Warden | AA cruiser | T2 | 32s | 1325 | Heavy naval / — | **shields the fleet** instead of shooting air down |
| Rift Hull | Bombardment | T3 | 43s | 2150 | Heavy naval / Energy | line damage; range 22 via spotters |
| Nest | Carrier | T3 | 60s | 2650 | Heavy naval / — | **spawns drones, no rearm needed** — a different carrier verb |

*No submarine* — the Ascendant **cloaks surface hulls** instead (same job, faction verb). *No sea transport* — teleport network.

## The four universal capabilities

The Ascendant does all four the *exotic* way — nothing is a conventional unit, everything phases or cloaks.

| Capability | Ascendant version | Delivery |
|---|---|---|
| **Detection** | Eye (phasing recon drone) + Seer | mobile, blinks in and out of danger |
| **Stealth** | Phase (temporary untargetability) + Mindbender **area cloak** + cloaked surface hulls | not lone infiltrators but *fields* of concealment, and units that dodge targeting entirely |
| **Sniper** | Seer | phasing energy marksman, also a detector |
| **Carrier** | Nest | spawns its own drones, **no rearm loop** — a self-sufficient carrier |

## Build orders

**Slice (ground T1):** Conduit → Wellspring → Manifold → Forge → Adept ×N + Skimmer → Glaive. Fastest slice arsenal to field, weakest to trade.

**Standard opening:** Conduit → Wellspring → Manifold → Forge → Teleport Node (establish mobility early) → Nexus → Glaives + Anchor; use Phase and the network to strike where the enemy isn't.

## Holes & compensation

- **No transports / no interceptor / no heavy / no sub** → each is solved by a faction verb in a different currency: Teleport Network for logistics, Glaive+Skyward for air, phase-kiting instead of soak, cloaked hulls instead of subs (reference Part 5.4).
- **Glass and expensive** → punished in any straight fight; loses sustained engagements. Wins only by choosing the fight, escaping the ones it's losing (Phase), and out-scaling on never-depleting economy.
