# Hegemony — mass & attrition

> Buries you under more than you can kill.

The Hegemony is the blunt instrument: the cheapest units in the game, the toughest structures, and the willingness to spend lives as a resource. Its owned verb — **Conscription** — turns its own infantry into fuel. Its holes are **detection, snipers, stealth, and the carrier**: it is bad against artillery and its air stages from land only, so its fleet is short-legged and it must fight closer to shore (reference Part 7).

## Global modifier (applied to the base ladders in `CONTEXT.md`)

| Dial | Hegemony | Effect |
|---|---|---|
| Unit cost | **×0.8** | out-produces everyone; trades bodies freely |
| Unit HP | **+20%** | soaks damage; wins wars of attrition |
| Speed | **slow** | poor at picking fights; punished by kiting and artillery |
| Vehicle / air production time | **×1.1** | premium units come slower… |
| Basic infantry time | **fast** (Conscript 5s) | …but the trash tap never stops |
| Building construction | **×1.1**, structure HP **+25%** | slow to expand, brutal to siege |

Tables show the Hegemony-adjusted numbers directly (costs rounded to 25).

## Economy

Standard harvest (Refinery + Miner), cheapest unit costs in the game, plus **Conscription income** — infantry can be spent for an instant credit refund or to fuel effects. A Hegemony that is losing bodies anyway can convert that loss into tempo. Single credit currency (reference Part 6.7).

## Owned verb — Conscription

Any Hegemony infantry can be **conscripted** — sacrificed at a Conscription Hall or in the field — for an instant burst of credits or to power a faction effect (e.g. the Sapper's self-destruct, an emergency repair, a defense overcharge). This makes cheap infantry a *currency*, not just a body, and is the reason the Hegemony can spam without going broke. It is a mechanic, not just flavor (reference Part 5.5).

## Buildings

| Structure | Tier | Prereq | Build time | Cost | HP | Power | Produces / unlocks |
|---|---|---|---|---|---|---|---|
| Foundry Crawler (MCV → Construction Yard) | T1 | — | 10s deploy | 2500 | very high | — | all structures; build radius |
| Furnace | T1 | Yard | 22s | 400 | med | **+** | power |
| Refinery | T1 | Yard | 33s | 1500 | high | − | credits; ships 1 Miner |
| Conscription Hall | T1 | Yard | 22s | 400 | high | − | infantry; **conscription site** |
| War Factory | T1 | Yard | 40s | 1500 | high | − | ground vehicles |
| Industrial Works | T2 | War Factory | 55s | 1500 | high | −− | T2 units, Helipad |
| Helipad | T2 | Industrial Works | 44s | 800 | high | − | aircraft (land-based only — no carrier) |
| War Academy | T3 | Industrial Works | 85s | 2500 | very high | −− | T3 units, Superweapon |
| Siege Cannon (superweapon) | T3 | War Academy | 130s | 3000 | very high | −−− | massive static bombardment (charge power) |
| Bunker | T1 | Conscription Hall | 15s | 500 | very high | − | garrisonable static defense |
| Flak Cannon | T2 | Industrial Works | 22s | 800 | high | − | anti-air |
| Cannon Tower | T2 | Industrial Works | 30s | 1000 | very high | −− | heavy static defense (AP) |
| Shipyard | T1* | Yard + shore | 50s | 1000 | high | − | naval units |
| Shore Gun (coastal battery) | T3 | Shipyard | 44s | 1200 | very high | −− | **longest range in the game** (range 30) |
| Sea Platform | T2 | Shipyard | 27s | 800 | high | − | offshore power/defense/expansion |

\* Naval is post-slice; shown for completeness (reference Part 6).

## Units — Infantry (from Conscription Hall)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Conscript | Basic body | T1 | 5s | 75 | Infantry / Small arms | cheapest, spammable; **conscription fodder** |
| AT Squad | Anti-armor | T2 | 9s | 250 | Infantry / Missile | 2-man, slow; splits AT from AA (real comp choice) |
| Flak Squad | Anti-air | T2 | 9s | 250 | Infantry / Autocannon | dedicated air denial |
| Sapper | Engineer | T1 | 14s | 400 | Infantry / — | capture/repair; **can self-destruct** (conscription) |
| Shock Trooper | Heavy | T2 | 11s | 325 | Infantry / Small arms | tanky frontliner |
| Incendiary | AoE / garrison-clear | T2 | 11s | 275 | Infantry / Explosive | flamer; clears garrisons |

## Units — Vehicles (from War Factory)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Miner | Harvester | T1 | 12s | 550 | Heavy / — | armored economy — hard to raid |
| Scout Halftrack | Scout | T1 | 12s | 400 | Light / Autocannon | vision, cheap |
| **Bulwark** | Main battle tank | T1 | 22s | 725 | Heavy / AP | slow, tanky, hits hard — the Hegemony baseline |
| Troop Carrier | APC | T2 | 20s | 550 | Heavy / Small arms | **12-slot** bulk transport |
| Flak Track | Mobile AA | T2 | 18s | 550 | Light / Autocannon | protects the ball from air |
| Rocket Barrage | Artillery | T2 | 33s | 800 | Light / Explosive | inaccurate but cheap; area denial |
| Field Repair | Support | T2 | 24s | 725 | Heavy / — | area heal/repair |
| **Colossus** | Superheavy | T3 | 60s | 2000 | Heavy / AP | the statement piece; dies to massed AT (reference Part 4 rule 1) |

*No light-AT tank destroyer* — the AT Squad and Bulwark cover armor; Hegemony trades finesse for the Colossus.

## Units — Aircraft (from Helipad, T2+; land-based only)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Hind | Gunship | T2 | 31s | 950 | Air / AP | tanky, slow anti-ground |
| MiG | Interceptor | T2 | 26s | 800 | Air / Autocannon | cheap glass air-superiority |
| Heavy Bomber | Bomber | T3 | 44s | 1275 | Air / Explosive | base-breaker, rearm loop |
| Heavy Lifter | Air transport | T2 | 24s | 650 | Air / — | bulk airlift |

*No scout drone* — a real detection hole; the Hegemony must scout with ground units and pays for it.

## Units — Specialists

| Unit | Role | Tier | Time | Cost | Built from | Notes |
|---|---|---|---|---|---|---|
| Champion | Commando | T3 | 45s | 1200 | Conscription Hall | pop-capped high-value unit |
| Hijacker | Infiltrator | T2 | 16s | 650 | Conscription Hall | steals enemy vehicles |

*No sniper, no stealth infantry* — the Hegemony has no answer to garrisoned defenders at range and no infiltration of its own beyond the Hijacker.

## Units — Naval (post-slice; from Shipyard; no carrier)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Gunboat | Corvette | T1 | 22s | 475 | Light naval / Autocannon | cheap, tanky screen |
| Bastion-class | Destroyer | T2 | 44s | 950 | Heavy naval / Naval gun | slow, heavy guns; naval MBT |
| Pike-class | Submarine | T2 | 30s | 725 | Submerged / Torpedo | **cheapest deterrent**; surfaces to hit land |
| Flak Barge | AA cruiser | T2 | 40s | 875 | Heavy naval / Autocannon | fleet AA umbrella |
| Siege Barge | Bombardment | T3 | 55s | 1450 | Heavy naval / Naval gun | inaccurate, cheap; range 22 via spotters |
| Heavy Lander | Sea transport | T2 | 33s | 725 | Heavy naval / — | 12 capacity, any shoreline |

*No carrier* — Hegemony air stages from land only, so its fleet is short-legged and must fight closer to shore (reference Part 7).

## Build orders

**Slice (ground T1):** Furnace → Refinery → Conscription Hall → War Factory → Conscript ×N + Scout Halftrack → Bulwark. Cheapest, tankiest slice arsenal.

**Standard opening:** Furnace → Refinery → Conscription Hall (spam Conscripts as a floor) → War Factory → Industrial Works → mass Bulwarks + AT/Flak Squads; convert dying infantry to credits via Conscription.

## Holes & compensation

- **No detection / no sniper / no stealth** → compensated by raw HP, cheapest costs, and the toughest static defense (Bunker, Cannon Tower). Hegemony doesn't out-play, it out-lasts.
- **No carrier, slow army** → punished by artillery and kiting; strong defensively, weak when forced to chase. The Colossus is the only ground finisher and it must be escorted against massed AT.
