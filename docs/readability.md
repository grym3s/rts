# Readability contract

Rules every unit, structure and effect must satisfy before it ships. Written before any art exists, deliberately.

**Why this file exists first.** Independent research into recent RTS failures ranks art identity and battlefield readability as the highest non-technical risk in the genre, and the one thing that cannot be patched late — Stormgate's dominant negative-review theme was art identity, not fidelity, and no amount of post-launch work recovered it. Two engineers will under-invest here by default. These rules are cheap to follow from the start and ruinous to retrofit.

Priority order inherited from `design-direction.md`: **FUN · READABILITY · FACTION IDENTITY · SPECTACLE.** When a rule here conflicts with a look we want, readability wins.

---

## 1. The luminance rule — the one that Dust Front got wrong

Dust Front is our aesthetic reference and its palette is also its most-cited player complaint: reviewers report being unable to parse the battlefield because everything is "washed out and greyscale". Worth being precise about the failure, because it is separable from the appeal.

They desaturated **hue** *and* collapsed **luminance** separation between units and terrain. Only the second one broke readability.

- **Terrain has a value ceiling.** Ground, rubble and debris stay in the lower-mid value band. Ground is backdrop; the eye budget belongs to units.
- **Units have a value floor.** Every unit must clear the terrain ceiling by a defined margin at gameplay zoom.
- **Test: the greyscale collapse.** Screenshot at default zoom, desaturate fully. If a unit disappears into the ground, the asset is wrong — not the lighting, not the post-process. This test is cheap, objective, and settles arguments.

Applying the four questions from `design-direction.md`:
1. **Inspiration** — Dust Front's near-monochrome soot palette with a single reserved accent.
2. **Why it works** — with no competing chroma, any saturated pixel is *guaranteed* to be information. It is why their red engine vents read so powerfully at distance.
3. **What we change** — desaturate hue, but enforce a minimum luminance delta between actor and background.
4. **Why ours is distinctive** — we keep the oppressive industrial tone that makes the screenshots sell, without the "where is my army" problem that dominates their reviews. And our accent budget has to carry *team colour*, which Dust Front never needed — it is single-player only. We are solving a problem they do not have.

## 2. Silhouette classes

Every unit belongs to exactly one silhouette class, and no two units in the same role share one.

- Shape carries the primary identification load, and it is carried by **proportion, not detail**. Detail becomes noise at RTS zoom.
- **Silhouette encodes the mechanical truth, not the brand.** C&C's Soviet units look heavy because they *are* heavy — Heavy Tank $950 against the Allied Light Tank $700. That is an honest signal, which is why it survives contact with play instead of being contradicted within an hour of learning the game.
- **Test: the 64px black-shape sheet.** Render the roster as flat black silhouettes at 64px. Any two that are confusable, one is wrong.

## 3. Tier is encoded in a countable feature

Dust Front's clearest piece of information design: barrel count encodes tier. One gun, one large gun, three or four in a row on siege units. You read a unit's threat class by counting tubes, at any zoom.

Counting is a pre-attentive judgement that survives small angular size, low contrast and partial occlusion — where silhouette recognition alone does not. Pick one repeated countable element per faction (emitters, stacks, limbs, armour segments) and let quantity mean rank.

## 4. Team colour is a budget, not a decoration

- **Two hues are reserved exclusively for team identity.** Nothing else may use them — not terrain, not effects, not UI, not resources. When everything is colourful, team colour stops being information.
- Team colour occupies a **consistent screen-area fraction** of every unit, in the **same anatomical location** across the roster.
- Author assets **neutral and tint at runtime**, rather than baking colour per team.

## 5. Weapon FX colour is faction identity

Under-used in the genre and nearly free. In C&C you identify the shooter from the *projectile* before you find the unit — Nod fires red, Allied prism fires blue, Tesla arcs blue-white.

Generals extends it to upgrade state: the Black Napalm upgrade flips Chinese fire from orange to violet, making "this player is upgraded" a global visual fact rather than a tooltip. **Adopt this**: faction-locked projectile hues, and let a major upgrade shift the hue so tech state is legible across the map.

## 6. State renders in worldspace, not in a panel

The strongest single example in C&C: China's horde bonus draws a **red star decal on the ground beneath the unit**, which gains a gold ring and then a white star as upgrades stack. An invisible stat buff rendered in the world, on the unit, readable at a glance.

The corresponding lesson from Tiberian Sun: its veterancy *scalars* (+25% and similar) were reported by players as unnoticeable, while its elite *capability unlocks* — an elite Titan gaining the ability to see stealth — were not. **A buff nobody can see is a buff that does not exist.**

Therefore:
- Veterancy, stance, damage state and buffs are visible **on the unit in the world**, never only in a selection panel.
- Prefer **discrete capability changes** over percentage scalars. A capability change alters how you use the unit; a percentage alters a spreadsheet.
- Effects must not obscure what they describe. Short bright muzzle flashes; explosions decaying fast to a low-contrast scorch; smoke capped in screen-space density.

## 7. Camera

Constrain **pitch and rotation**. Do **not** constrain zoom range.

Constraining the camera is a scope cut disguised as a design decision — assets only need to look good from one angle band, culling gets easier, and players keep their spatial memory. But Dust Front's single most-requested fix is the ability to zoom out: their tagline promises battalions and their camera frames a platoon. Take the low, horizon-visible framing as the **default**, not the **only**, camera.

## 8. The scale lie

Units are drawn deliberately oversized relative to buildings and terrain — realistic scale is unreadable at RTS zoom. **Pick the exaggeration factor early and write it here**, because it is baked into map scale, camera height and every asset authored afterwards.

## 9. Accessibility is part of readability, not an extra

Separate protanopia / deuteranopia / tritanopia palettes, not one "colourblind mode" toggle. If the design already obeys §1 and §2 — luminance separation and distinct silhouettes — most of this comes free, which is the point: **a design that only reads because of colour is already broken for everyone.**

---

## The checklist

No unit ships without passing all five:

| # | Test | Fails if |
|---|---|---|
| 1 | Greyscale collapse at default zoom | Unit merges into terrain |
| 2 | 64px black-shape sheet | Confusable with another unit in its role |
| 3 | Tier count | Threat class not readable without selecting |
| 4 | Team colour audit | Reserved hues appear anywhere but team identity; fraction or location inconsistent with roster |
| 5 | Minimap read | Faction not identifiable at minimap scale |

## Status

Unresolved and needing a decision before art begins: the exact luminance delta, the team-colour screen-area fraction, the scale exaggeration factor, and each faction's countable tier feature. These are numbers, and they should be set by looking at a test scene rather than argued about — see the MultiMesh prototype note in `design-direction.md`.
