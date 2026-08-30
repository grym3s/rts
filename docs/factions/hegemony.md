# Hegemony — mass & attrition

> Buries you under more than you can kill.

The Hegemony is the blunt instrument: the cheapest units in the game, the toughest structures, and the willingness to spend lives as a resource. Its owned verb — **Conscription** — turns its own infantry into fuel. Its holes are the **light-AT tank destroyer** and the **air scout drone** — slots where absence is a strategic weakness with counterplay, not an auto-loss. It still fields all four universal capabilities (detection, stealth, sniper, carrier), but its versions are deliberately **clumsy and positional** — dug in, slow, and brute-armored rather than mobile and precise — and it remains bad against artillery (reference Part 7, refined per the universal-capabilities rule in `CONTEXT.md`).

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
| Helipad | T2 | Industrial Works | 44s | 800 | high | − | aircraft (land-based; naval air only via the Bastion Carrier) |
| War Academy | T3 | Industrial Works | 85s | 2500 | very high | −− | T3 units, Superweapon |
| Siege Cannon (superweapon) | T3 | War Academy | 130s | 3000 | very high | −−− | massive static bombardment (charge power) |
| Bunker | T1 | Conscription Hall | 15s | 500 | very high | − | garrisonable static defense |
| Spotlight Bunker | T2 | Industrial Works | 30s | 800 | very high | − | **static detector** — reveals stealth in radius; garrisonable |
| Tunnel Network | T2 | War Factory | 30s | 800 | high | − | **stealth logistics** — ground units travel underground (concealed) between any two tunnels |
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
| Scout Halftrack | Scout **(+ mobile detector at T2)** | T1 | 12s | 400 | Light / Autocannon | vision, cheap; gains stealth-detection once Industrial Works is up |
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

*No air scout drone* — a real *air-recon* hole (the Hegemony scouts the map slower, on the ground). Stealth-detection itself is covered on the ground by the Spotlight Bunker and the T2 Scout Halftrack.

## Units — Specialists

| Unit | Role | Tier | Time | Cost | Built from | Notes |
|---|---|---|---|---|---|---|
| Sharpshooter Team | Sniper (+ spotter) | T2 | 16s | 475 | Conscription Hall | 2-man, **must deploy to fire**; short range, high HP — a dug-in attrition sniper, not a mobile assassin |
| Champion | Commando | T3 | 45s | 1200 | Conscription Hall | pop-capped high-value unit |
| Hijacker | Infiltrator | T2 | 16s | 650 | Conscription Hall | steals enemy vehicles |

The Hegemony sniper trades mobility for durability: the Sharpshooter Team roots to fire but survives what kills a Coalition Marksman, and it spots for the rest of the army.

## Units — Naval (post-slice; from Shipyard)

| Unit | Role | Tier | Time | Cost | Armor / Attack | Notes |
|---|---|---|---|---|---|---|
| Gunboat | Corvette | T1 | 22s | 475 | Light naval / Autocannon | cheap, tanky screen |
| Bastion-class | Destroyer | T2 | 44s | 950 | Heavy naval / Naval gun | slow, heavy guns; naval MBT |
| Pike-class | Submarine | T2 | 30s | 725 | Submerged / Torpedo | **cheapest deterrent**; surfaces to hit land |
| Flak Barge | AA cruiser | T2 | 40s | 875 | Heavy naval / Autocannon | fleet AA umbrella |
| Siege Barge | Bombardment | T3 | 55s | 1450 | Heavy naval / Naval gun | inaccurate, cheap; range 22 via spotters |
| Bastion Carrier | Carrier | T3 | 60s | 1750 | Heavy naval / — | slow, very high HP, **3-4 aircraft, no operational-radius bonus** — a brute that floats planes forward and soaks hits |
| Heavy Lander | Sea transport | T2 | 33s | 725 | Heavy naval / — | 12 capacity, any shoreline |

The Bastion Carrier is the differentiated Hegemony carrier: where Coalition's Vanguard *extends* air range strategically, the Bastion just parks and launches. The fleet is still short-legged — the carrier is armor, not reach — so the Hegemony must fight closer to shore (reference Part 7).

## The four universal capabilities

The Hegemony has all four, each done the *slow, positional, brute* way — it plants them and digs in rather than fielding a mobile, precise version.

| Capability | Hegemony version | Delivery |
|---|---|---|
| **Detection** | Spotlight Bunker + T2 Scout Halftrack | **static/positional** — it detects where it has fortified, not everywhere at once |
| **Stealth** | Tunnel Network | **mass concealment** — the whole army vanishes underground and re-emerges elsewhere; countered by watching the tunnel mouths |
| **Sniper** | Sharpshooter Team | dug-in, deploy-to-fire, high-HP anti-materiel team — short range but survivable |
| **Carrier** | Bastion Carrier | armored brute, no range bonus — floats planes forward and soaks hits |

## Build orders

**Slice (ground T1):** Furnace → Refinery → Conscription Hall → War Factory → Conscript ×N + Scout Halftrack → Bulwark. Cheapest, tankiest slice arsenal.

**Standard opening:** Furnace → Refinery → Conscription Hall (spam Conscripts as a floor) → War Factory → Industrial Works → mass Bulwarks + AT/Flak Squads; convert dying infantry to credits via Conscription. Add a Spotlight Bunker on the main and a Tunnel Network for a surprise flank.

## Holes & compensation

- **No light-AT tank destroyer, no air scout drone** → these are the real holes: the AT Squad + Bulwark cover armor slowly, and ground scouting is the price of no air recon. Both are strategic weaknesses with counterplay, not auto-losses.
- **Every universal capability is clumsy, not absent** → detection is positional (blind where it hasn't dug in), the sniper is immobile, stealth telegraphs at the tunnel mouths, the carrier has no reach. Opponents beat the Hegemony by moving faster than it can re-fortify, not by holding a capability it lacks.
- **Slow army, bad vs artillery** → strong defensively, weak when forced to chase. The Colossus is the only ground finisher and it must be escorted against massed AT.
