# Slice 5 — combat feedback and infantry display

Status: design input for GRY-286; engine-agnostic. This document defines presentation behavior only. The simulation remains the sole authority for combat rules and state (ADR 0006).

## Source contract and event inventory

The current combat system does not emit typed combat events. `SimEvent` contains only `(Tick, Subject, Target)`, and no code in `sim/combat/` calls `Emit` (`sim/core/SimWorld.cs`; `tools/sim-host/CONTEXT.md`). The events below are therefore required presentation triggers for GRY-286, not events already available in the current protocol. Do not invent a `SimEvent` kind from its entity IDs. Derive transitions from consecutive authoritative snapshots until the sim adds a typed event contract.

`SimWorld` runs at 20 ticks per second: one tick is 50 ms. Tick `T` systems run before the world increments to `T+1`; the host returns the post-step snapshot and captured events for that step. A presentation consumer must read the result before requesting the next step, because the event buffer clears at the start of the next step (`sim/core/SimWorld.cs`; `tools/sim-host/CONTEXT.md`). Combat resolves after navigation and before dead-unit cleanup (`sim/CONTEXT.md`).

| Semantic trigger needed by presentation | Current sim evidence and timing | Visual response | Audio response |
|---|---|---|---|
| Fire | A live unit with a live target in range fires when cooldown reaches zero. Damage is applied and cooldown is reset in the same combat step (`sim/combat/CombatSystem.cs:35-48`). No fire event is emitted. Infer a shot from an authoritative cooldown transition, as the existing renderer does (`game/render/UnitRenderer.cs:188-204`). | Play the infantry `fire` clip and a brief muzzle flash/projectile cue at the firing unit. Use the faction weapon-FX hue, separate from the two hues reserved for team identity. Keep the flash short so it does not hide unit shape. | Play one short, weapon-family shot at the firing unit. Do not play during cooldown or repeat per rendered frame. |
| Hit / damage | The target's HP decreases in the same tick as fire; damage uses the matrix with a minimum one-point chip (`CombatSystem.cs:41-47`). No separate hit event is emitted. Detect an HP decrease between snapshots; this cannot reliably identify the damage source or warhead. | Place a compact impact cue on the target at its last authoritative position. Distinguish impact families by shape/material response, not team-color hues. Add only a brief damage tint or reaction; preserve silhouette and health-state legibility. | Play a quiet, short impact cue. Avoid one loud sound per pellet or overlapping damage tick; prioritize or limit repeated cues. |
| Death | A unit becomes dead when HP reaches zero or less during combat, then `UnitStore.DespawnDead()` removes it in cleanup during that same world step (`sim/combat/CombatSystem.cs:15-18`; `sim/CONTEXT.md:15-16`). No death event is emitted. Detect an alive-to-dead transition or disappearance between snapshots. | Preserve the last known transform long enough to play the infantry `death` clip and a low-contrast, quickly settling residue. Do not let the effect replace the unit silhouette before the death reads. Warhead-specific corpse/death variation is a design direction, not current sim data; do not claim the current snapshots identify the killing warhead. | Play a short, restrained death cue. Do not use a unique warhead sound unless a future typed event supplies the warhead. |

Target acquisition, target loss, chase-order refresh, and cooldown countdown are combat state changes, not emitted events. They do not get one-shot VFX or audio in this slice. The existing target line and movement/facing presentation may show the current target and chase state (`game/render/UnitRenderer.cs:103-108,117-123`). Do not add feedback by polling every render frame.

## Infantry display states

Drive display from authoritative snapshots, not from a second combat simulation. Position and facing follow the sim snapshot and velocity. The existing presentation already interpolates position and selects `idle`, `move`, and `fire` clips when present (`docs/asset-pipeline.md:5-11`; `game/render/UnitRenderer.cs:54-61,99-108,188-204`). The death state must retain a presentation-only last transform after sim cleanup.

| State | Entry condition | Display behavior | Exit / priority |
|---|---|---|---|
| Idle | Unit is alive and has no meaningful movement velocity; no fire trigger is active. | Play looping `idle`; face the last movement direction. | `move` when velocity becomes non-zero; `fire` on a fire transition; `death` when HP reaches zero or the unit disappears. |
| Move | Unit is alive and snapshot velocity is non-zero. | Play looping `move`; face velocity. Interpolate between completed-tick positions. | `idle` when movement stops; `fire` on a fire transition; `death` when HP reaches zero or the unit disappears. |
| Fire | A living unit fires on a cooldown transition. | Play the one-shot `fire` clip and muzzle response. Do not change sim position or block subsequent simulation ticks. | Return to `move` or `idle` when the one-shot ends; `death` interrupts immediately. |
| Death | HP reaches zero or the unit disappears from the next authoritative unit set. | Freeze at last known transform, play one `death` presentation, then remove the view and its effects after their bounded lifetime. Never recreate or control the dead sim unit. | Terminal. Death overrides fire, move, and idle. |

State priority is `death > fire > move > idle`. Missing animation assets must not break simulation or visibility; use a readable static pose/proxy and preserve the same transition rules. Infantry assets should provide `idle`, `move`, `fire`, and `death` clips for this slice; the current asset pipeline names only the first three as its minimum (`docs/asset-pipeline.md:31-35`).

## Readability and direction constraints

Apply the priority `FUN · READABILITY · FACTION IDENTITY · SPECTACLE`; readability wins when spectacle conflicts (`docs/design-direction.md:57-61`; `docs/readability.md:7`). These rules apply to every event response and infantry state:

- Preserve unit/terrain luminance separation in gameplay views, including impact, fire, and death frames. Check the desaturated default-zoom view; an effect must not make the unit merge into the ground (`docs/readability.md:11-25`).
- Keep each role's silhouette distinct. Keep muzzle flashes brief, explosions short, scorch low-contrast, and smoke limited in screen-space density (`docs/readability.md:27-33,53-62`).
- Reserve the two team-identity hues for team identity. Weapon effects may use faction identity hues, but those hues must not reuse the reserved team hues (`docs/readability.md:41-51`). Never use color as the only event or state signal (`docs/readability.md:74-90`).
- Show meaningful state in the world, not only in a selection panel. Keep visible health/damage state legible while units move, fire, and die (`docs/readability.md:53-62`).
- Keep the industrial tone and original faction identity. Use references as principles, not as instructions to copy a recognizable unit or effect (`docs/design-direction.md:22-30,43-55`).
- Make response weight follow player significance, not raw event count. Rate-limit repeated sounds and effects so a dense firefight does not become visual or audio noise (`docs/mechanics-playbook.md:61-65`).

## GRY-286 acceptance checks

Review the actual Unreal slice against these observable checks. A source-code or headless check does not replace visual review in the running scene.

1. Trigger a shot from a living infantry unit with a target in range. The `fire` clip, muzzle cue, and shot sound each occur once for that sim cooldown transition. No cue repeats on render-only frames.
2. Cause a non-lethal hit. The target's health changes from the sim snapshot, and a short impact response appears at the target without obscuring its role silhouette or health indicator.
3. Kill infantry. The view plays one death response at its last sim position after despawn; the view is then removed. The dead unit cannot move, fire, or return to an alive animation.
4. Exercise idle-to-move, move-to-fire, fire-to-move, and fire-to-idle transitions. Facing follows velocity, the fire one-shot returns to the correct locomotion state, and death interrupts every other state.
5. Advance ticks at the 20 Hz sim rate. A presentation transition follows the returned post-step snapshot for that tick; render interpolation does not advance, delay, or alter sim state. No effect is inferred from a `SimEvent` lacking a typed kind.
6. Inspect the full battle at default zoom and a close view, then inspect a desaturated default-zoom frame. Units remain separated from terrain, team identity remains clear, and fire/impact/death effects do not hide units or their health state (`docs/readability.md:11-25,41-62,80-90`).
7. Confirm weapon-FX colors differ from reserved team-identity hues, and that event type remains distinguishable without color alone (`docs/readability.md:41-51,74-90`).
8. Run a dense exchange. Repeated sound and effect cues remain rate-limited and do not obscure who is shooting whom; targeting remains legible at default zoom (`docs/game-design-pillars.md:7-12`; `docs/mechanics-playbook.md:61-65`).

## Current limitation

The current sim provides no typed fire, hit, death, or target-transition events, and `SimEvent` does not include event kind, damage amount/type, or position. This document does not add those sim changes. GRY-286 can use snapshot transitions for presentation, but reliable attacker/weapon attribution requires a future sim event contract that carries those fields.