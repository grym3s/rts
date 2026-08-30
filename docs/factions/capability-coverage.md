# Capability coverage — proof of no holes

Every faction answers every job. This matrix is the check: **no cell is empty.** The rating shows *how well*, and the tool shows *how* — that's where identity lives (see `CONTEXT.md`, *Answers, not holes*). A blank cell here is a bug to fix, not a design choice.

**Rating:** ●●● strong · ●● standard · ● weak-but-present (worse/clumsier, never *can't*).

| Job (answer to…) | Coalition | Hegemony | Ascendant |
|---|---|---|---|
| **Anti-infantry** | ●● Rifleman, Grenadier | ●●● Conscript mass, Incendiary | ●● Adept, Resonator |
| **Anti-armor** | ●● Lancer, Missile Rover | ●● AT Squad + Bulwark (mass) | ●● Disruptor, Lance Platform |
| **Anti-air** | ●●● Lancer, SAM Rover, Falcon | ●● Flak Squad, Flak Track, MiG | ●● Glaive + Skyward (no interceptor) |
| **Detection** (reveal stealth) | ●●● drones, Marksman, Sensor Command | ●● Spotlight Bunker, Halftrack (positional) | ●● Eye, Seer (phasing) |
| **Stealth / concealment** | ●● Operative, Vulture | ●●● Tunnel Network (mass) | ●●● Phase + cloak |
| **Sniper / long-range infantry kill** | ●●● Marksman (mobile) | ● Sharpshooter Team (immobile) | ●● Seer (phasing) |
| **Siege / anti-static** | ●● Precision Battery, Orbital Lance | ●●● Rocket Barrage, Siege Cannon, Shore Gun | ●● Rift Lance, Rift |
| **Mobile logistics** | ●● Skylift | ●●● Troop Carrier, Tunnel Network | ●●● Teleport Network |
| **Base-breaking / deep strike** | ●●● Vulture (stealth bomber) | ●● Heavy Bomber, Colossus | ●● Singularity, Monolith |
| **Objective capture / repair** | ●● Engineer | ●● Sapper, Field Repair | ●● Weaver (repair) + Mindbender (capture) |
| **Scouting / vision** | ●●● Recon Buggy, Spotter Drone | ● Scout Halftrack (ground only) | ●●● Skimmer, Eye |
| **Support** (heal / shield / buff) | ●● Comms Rig (marks) | ●● Field Repair (heal) | ●● Anchor (shields) |
| **Top-end finisher** | ●● air/naval (Vulture, Vanguard) | ●●● Colossus superheavy | ●● Monolith superheavy |

Every column has strengths (●●●) and soft spots (●), and every row is filled across all three factions. That distribution — not any empty cell — is what creates identity.

## Reading the shape of each faction

- **Coalition** peaks at detection / anti-air / sniper / scouting / deep-strike (the information game) and dips at anti-infantry mass and the ground top-end.
- **Hegemony** peaks at anti-infantry / siege / logistics / stealth / superheavy (mass and attrition) and dips at the sniper and air scouting (tempo and reach).
- **Ascendant** peaks at stealth / logistics / scouting (mobility and trickery) and dips nowhere to zero — it spreads thin, paying in durability and cost rather than in any missing answer.

## Matchups: aim for even

We tune for **~50/50 across all three pairings** (the StarCraft goal), not a non-transitive "A beats B beats C" triangle. A faction that reliably beats another because of a capability gap is soft rock-paper-scissors — the thing this whole model exists to avoid. Differences between factions should be decided *in play* (positioning, composition, micro), not at the select screen.

Tune the MBT mirror first, then each cross-matchup, via `../../tools/scenario` (reference Part 8). When a unit or timing changes, re-run the balance scenario that covers the affected row.

## The deniability check

Every ●●● that is a *distinctive answer* (Tunnel Network, Teleport Network, Marking, Phase, cloak) must stay **deniable** — the opponent can contest the nodes, watch the tunnel mouths, kill the sensors, wait out the cooldown, or bring detection. An answer the opponent cannot interact with is a hole seen from the other side, and fails this file's test just as an empty cell would.
