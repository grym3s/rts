# Coalition — precision & information

> Sees you first, kills you from range.

The Coalition wins the information war. Everything it fields is expensive, fragile, and long-ranged, and its owned verb — **Marking** — punishes any enemy its network has spotted. It answers every job in the game; its **top-end finisher arrives from the air and sea** (Vulture bomber, Vanguard carrier) rather than a ground superheavy — a distinctive delivery, not a missing answer.

## Global modifier (applied to the base ladders in `CONTEXT.md`)

| Dial | Coalition | Effect |
|---|---|---|
| Unit cost | **×1.15** | premium army; every loss hurts |
| Unit HP | **−10%** | fragile — must not be caught out of position |
| Range | **+2** over the ladder (MBT 12, arty 16) | outranges the mirror slot in every other faction |
| Construction / production time | **×1.0** | standard |

Tables below show the Coalition-adjusted numbers directly (costs rounded to 25).

## Economy

Standard harvest (Refinery + Collector) plus **Intel**, a secondary meter that fills as Coalition units deal and take damage in combat. Intel gates the T3 tech and fuels the Marking network — a Coalition that never fights never reaches its late game. Single credit currency otherwise (reference Part 6.7).

## Owned verb — Marking

A unit spotted by any Coalition sensor (Comms Rig, Spotter Drone, Marksman, Sensor Command radius) is **marked**: it takes bonus damage from *all* Coalition sources until it breaks line of sight. This is why the whole faction is built around vision — the payoff for spotting is damage, not just information. Killing the enemy's counter-recon is the Coalition's core skill.

## Buildings

| Structure | Tier | Prereq | Build time | Cost | HP | Power | Produces / unlocks |
|---|---|---|---|---|---|---|---|
| Mobile Command (MCV → Construction Yard) | T1 | — | 10s deploy | 2500 | high | — | all structures; build radius |
| Reactor | T1 | Yard | 20s | 400 | low | **+** | power |
| Refinery | T1 | Yard | 30s | 1500 | med | − | credits; ships 1 Collector |
| Barracks | T1 | Yard | 20s | 400 | med | − | infantry |
| Vehicle Bay | T1 | Yard | 35s | 1500 | med | − | ground vehicles |
| Sensor Command | T2 | Vehicle Bay | 50s | 1500 | med | −− | **T2 units, Airbase, Marking network** |
| Airbase | T2 | Sensor Command | 40s | 800 | med | − | aircraft (rearm/repair pads) |
| Aerospace Command | T3 | Sensor Command | 75s | 2500 | high | −− | T3 units, Superweapon |
| Orbital Lance (superweapon) | T3 | Aerospace Command | 120s | 3000 | high | −−− | targeted orbital strike (charge power) |
| Guard Turret | T1 | Barracks | 15s | 500 | med | − | static defense (AP) |
| SAM Site | T2 | Sensor Command | 20s | 800 | med | − | anti-air |
| Precision Turret | T2 | Sensor Command | 30s | 1000 | med | −− | long-range static defense (marks) |
| Shipyard | T1* | Yard + shore | 45s | 1000 | med | − | naval units |
| Coastal Lance (battery) | T3 | Shipyard | 40s | 1200 | high | −− | outranges bombardment ships (range 28) |
| Sea Platform | T2 | Shipyard | 25s | 800 | med | − | offshore power/defense/expansion |

\* Naval is post-slice; shown for completeness (reference Part 6).

## Units — Infantry (from Barracks)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Rifleman | Basic body | T1 | 6s | 125 | Infantry / Small arms | **marks on hit** — the cheapest way to tag a target |
| Lancer | AT **+ AA** | T2 | 9s | 350 | Infantry / Missile | folded AT/AA — mandatory but flattens comp (reference Part 3) |
| Engineer | Capture / repair | T1 | 14s | 575 | Infantry / — | non-combat |
| Breacher | Heavy frontliner | T2 | 11s | 450 | Infantry / Small arms | riot shield: frontal damage reduction |
| Grenadier | AoE / garrison-clear | T2 | 11s | 400 | Infantry / Explosive | clears garrisons |

## Units — Vehicles (from Vehicle Bay)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Collector | Harvester | T1 | 12s | 800 | Light / — | economy; defenseless |
| Recon Buggy | Scout | T1 | 12s | 575 | Light / Autocannon | fast vision, raids |
| **Lancer MBT** | Main battle tank | T1 | 20s | 1050 | Heavy / AP | fast, **range 12** — the Coalition baseline |
| Missile Rover | Light AT | T2 | 16s | 700 | Light / Missile | glass cannon vs Heavy |
| IFV | APC | T2 | 18s | 800 | Light / *varies* | **weapon changes by passenger** (reference Part 5.3) |
| SAM Rover | Mobile AA | T2 | 16s | 800 | Light / Missile | protects the ball from air |
| Precision Battery | Artillery | T3 | 30s | 1150 | Light / Explosive | deploys to fire, range 16 |
| Comms Rig | Support | T2 | 22s | 1050 | Light / — | **marks all enemies in radius** — the marking anchor |

*Top-end answer — air/naval, not a ground superheavy.* Coalition's T3 ground ceiling is the Precision Battery; its finisher is the Vulture bomber and Vanguard carrier. It has a late-game answer — it just delivers it from above, not across the field.

## Units — Aircraft (from Airbase, T2+)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Spotter Drone | Scout / detector | T2 | 15s | 450 | Air / — | cheap vision + marking source |
| Hawk | Gunship | T2 | 28s | 1400 | Air / AP | anti-ground |
| Falcon | Interceptor | T2 | 24s | 1150 | Air / Autocannon | air superiority |
| Vulture | Bomber | T3 | 40s | 1850 | Air / Explosive | **stealth**; base-breaker, rearm loop |
| Skylift | Air transport | T2 | 22s | 925 | Air / — | 2–4 infantry or 1 light vehicle |

## Units — Specialists

| Unit | Role | Tier | Time | Cost | Built from | Notes |
|---|---|---|---|---|---|---|
| Marksman | Sniper **+ detector** | T2 | 16s | 700 | Barracks | kills infantry at extreme range; reveals stealth |
| Ghost | Commando | T3 | 45s | 1750 | Barracks | pop-capped high-value unit |
| Operative | Infiltrator | T2 | 16s | 900 | Barracks | stealth, sabotage |

## Units — Naval (post-slice; from Shipyard)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Cutter | Corvette | T1 | 20s | 700 | Light naval / Autocannon | fast, **spots** for bombardment |
| Sentinel-class | Destroyer | T2 | 40s | 1400 | Heavy naval / Naval gun | naval MBT; ASW |
| Silent-class | Submarine | T2 | 30s | 1050 | Submerged / Torpedo | surfaces to hit land |
| Aegis-class | AA cruiser | T2 | 38s | 1275 | Heavy naval / Missile | **fleet AA umbrella** (non-negotiable slot) |
| Longbow-class | Bombardment | T3 | 50s | 2075 | Heavy naval / Naval gun | range 22 via spotters; helpless up close |
| Vanguard-class | Carrier | T3 | 70s | 2550 | Heavy naval / — | **mobile air station** (Coalition owns this) |
| Landing Ship | Sea transport | T2 | 30s | 1050 | Light naval / — | 8–12 capacity, any shoreline |

## Capability profile — strongest at the information jobs

Coalition answers every job (full matrix in `capability-coverage.md`); it is *strongest* at the information cluster, but strong is not free — each tool costs supply and dies when caught.

| Job | Coalition answer | Delivery |
|---|---|---|
| **Detection** | Spotter Drone, Marksman, Comms Rig, Sensor Command radius | pervasive **mobile network** — sees the whole map dynamically, and marks what it sees |
| **Stealth** | Vulture (stealth bomber), Operative (cloaked infiltrator) | individual stealthed units |
| **Sniper** | Marksman | mobile long-range assassin, also a detector |
| **Air projection** | Vanguard-class carrier | mobile air **station** that extends operational radius |

## Build orders

**Slice (ground T1):** Reactor → Refinery → Barracks → Vehicle Bay → Rifleman ×N + Recon Buggy → Lancer MBT. This is the whole vertical-slice arsenal.

**Standard opening:** Reactor → Refinery → Barracks → Vehicle Bay → Sensor Command (opens marking + air) → Comms Rig for the marking anchor, then MBTs behind it.

## Where it's strong and weak (never absent)

- **Strong:** detection, range, precision, air, naval — the whole information game.
- **Weak (but present):** mass and sustained trades. Its army is expensive and fragile, so it loses even-cost brawls and must fight at range; its ground top-end is a Precision Battery, not a superheavy. Weak here means *worse*, never *can't*.
- **Owned verb is deniable:** Marking only works while its sensors live — break line of sight or kill the Comms Rig / drones and the bonus damage stops. That's the counterplay.
