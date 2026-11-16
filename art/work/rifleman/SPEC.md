# Rifleman production asset spec (faction-neutral)

Prepared for GRY-284 (Produce and visually approve the first rifleman production asset).
GRY-284 is blocked on the GRY-282 faction review. This spec fixes everything that does
not depend on faction colours, so GRY-284 can start rig, mesh, animation and pipeline
work the day GRY-282 lands. Documentation only: no modelling, no GPU generation, no
engine import was done here.

**Status: provisional preparation spec.** `docs/readability.md` (Status section) lists
four decisions that must be made before art begins: the luminance delta, the
team-colour screen-area fraction, the scale exaggeration factor, and each faction's
countable tier feature. The tier feature is physical geometry, so it can change the
mesh. This spec therefore does not promise that mesh or rig work is independent of
those decisions. It prepares the faction-neutral baseline; GRY-282-era decisions keep
their gates and can still revise the items marked provisional below.

Author: Magnus. Date: 2026-11-16. Revision base: main `133eb46`.

## 1. Scope and faction neutrality

In scope: scale, silhouette, budgets, armature, clips, material-slot structure, GLB
export contract, base-asset recommendation.

Deferred to GRY-282 (faction review), per `docs/readability.md` §4 and its Status
section:

- Faction palette and the two team-colour hues.
- The team-colour screen-area fraction and its exact anatomical location.
- The countable tier feature per faction (§3). This can add or change geometry, not
  just textures.
- The luminance delta number and the scale exaggeration factor (§8).
- The visor hue and any faction emblem or insignia geometry.

These remain open gates. Palette and team-hue work is texture-level. The tier feature
and the scale exaggeration factor can change geometry, so the mesh targets below are
working numbers, not final approvals. Production art sign-off waits for the four
readability decisions.

## 2. In-game scale and silhouette targets

Verified presentation facts (read from code, not assumed):

- `game/camera/RtsCamera.cs`: perspective camera, FOV 45° (`RtsCamera.cs:25`), fixed
  pitch 52° (`RtsCamera.cs:8`), orbit distance 18–112 m, default 92 m
  (`RtsCamera.cs:9-11`). `PlaceCamera` (`RtsCamera.cs:109-115`) puts the camera at
  `target + (0, sin 52°, cos 52°) * distance` and looks at the target.
- `docs/asset-pipeline.md`: one map cell = one world unit (1 m). Model origin at
  ground-level feet, Y-up, local forward −Z (Godot).
- `content/units/rifleman.json`: speed 1.8 (≈1.8 m/s), weapon cooldown 12 ticks.
  `sim/core/SimWorld.cs` sets `TicksPerSecond = 20`, so the cooldown is 0.6 s.
- `game/render/UnitRenderer.cs`: loads `res://assets/rifleman/rifleman.glb`, sets yaw
  from velocity, plays `idle`/`move` from velocity and `fire` on a cooldown
  0→positive edge. The health bar sits at y = 1.95 m.

Screen-size targets. Assumptions: 1080p viewport; the unit stands upright (vertical
along world Y) with its feet at the camera target point. The projected body height of
a 1.80 m unit is then

```
px = 1080 / (2·tan(45°/2)) · 1.8·cos(52°) / (distance − 1.8·sin(52°))
```

The `cos(52°)` term accounts for the upright body tilting against the pitched view
axis; the distance term shortens because the crown sits nearer the camera than the
target. This is the projected vertical extent of the body only. The full silhouette
bounds (rifle held forward, backpack, shoulder width) differ: horizontal extent is
not reduced by the pitch term, and parts nearer or farther than the target project
larger or smaller. Units away from the target project at their own distance.

| Zoom | Orbit distance | Projected 1.80 m body height |
|---|---|---|
| Max zoom-in | 18 m | ≈87 px |
| Default | 92 m | ≈16 px |
| Max zoom-out | 112 m | ≈13 px |

Targets:

- Model height: 1.80 m to the helmet crown, ≤1.96 m including backpack and raised
  rifle (matches the current blockout bounds: crown ≈1.70 m, max Y ≈1.96 m).
  `docs/readability.md` §8 (the scale lie) keeps units oversized; the exact
  exaggeration factor is an open decision required before art begins. 1.80 m is the
  working number until set.
- Silhouette class: basic infantry. Identification must come from proportion, not
  detail (`docs/readability.md` §2). At default zoom the body projects ≈16 px tall;
  detail below ≈2 px does not survive and must not carry information.
- Pass the 64px black-shape sheet (`docs/readability.md` §2 — an authoring render at
  a fixed 64px size, not the in-game projection) and the greyscale-collapse test (§1)
  at default zoom once the terrain value ceiling exists.
- Keep the rifle held forward and inside a 0.65 m × 0.65 m ground footprint so unit
  spacing and selection rings (torus outer radius 0.38 m) do not clip.

## 3. Polygon and texture budget

Rationale: the unit projects ≈13–87 px tall at the camera target (§2). Geometry
beyond LOD0 pays only at the closest zoom.

| Item | Budget | Note |
|---|---|---|
| LOD0 triangles | ≤ 6,000 | close zoom only |
| LOD1 triangles | ≤ 2,000 | covers default zoom |
| LOD2 triangles | ≤ 600 | zoom-out and mass scenes |
| Rendered mesh primitives (surfaces) | ≤ 2 per unit for one active LOD | scope: the unit's opaque forward render pass; see §6 |
| Distinct material definitions | ≤ 2 per unit per LOD | separate check; material count alone does not prove the surface budget |
| Base-colour atlas | 1024×1024, sRGB | single atlas covering all 8 logical slots |
| Normal map | 1024×1024 | OpenGL +Y convention |
| ORM (AO/roughness/metallic packed) | 1024×1024 | linear |
| Emissive mask | ≤ 512×512 or constant colours | visor/lights |
| Total texture memory | ≤ 4 MB compressed per unit | power-of-two dimensions (4×4 block compression) |

The surface budget counts rendered mesh primitives, not material definitions. In
glTF, a mesh contains primitives and each primitive references a material (Khronos
glTF 2.0 specification §3.7.2.1). Two material definitions can still sit behind
seven primitives, and each primitive renders as its own surface, so an exported
material count alone can pass while the surface budget fails. The budget is at most
2 rendered mesh primitives across the unit model for one active LOD. Scope: the
unit's opaque forward render pass only. It is not a total-frame draw-call
guarantee: shadow/depth passes, other units and engine batching are outside it.
Neither the validator nor `make check` measures either number. GRY-284 verifies the
surface budget by counting mesh primitives in the exported GLB for the active LOD,
and keeps the ≤2 material-definition count as a separate check.

Reference points: `rifleman-v2` shipped 18,960 source vertices and 12 surface
materials (over budget, and rejected on looks); the Hunyuan shape is 69,819 vertices
and needs retopology before it can count as a production mesh at all.

## 4. Armature and bone list

Carry forward the existing 16-bone humanoid from `art/work/rifleman/` (present in both
`rifleman.glb` and `rifleman_v2.glb`, so clips transfer):

`root, hips, chest, neck, head, arm_l, arm_r, fore_l, fore_r, thigh_l, thigh_r, shin_l, shin_r, foot_l, foot_r, rifle`

Conventions (from `art/work/rifleman/manifest.json` and `docs/asset-pipeline.md`):

- Authoring in Blender: Z-up, origin at feet, facing −Y; limb bones rolled −90° so
  local +Y pitches forward.
- Export: glTF +Y up; Godot sees forward −Z, feet at y = 0.
- Single skinned mesh; every vertex weighted to exactly one bone is acceptable
  (rigid-per-part) for this style, but deformation loops are required at shoulders,
  elbows and knees for the fire pose and walk.
- The bone-name set above is a production requirement, not a validator check: the
  validator does not inspect bone names, and `UnitRenderer.cs` selects clips by
  animation name (`UnitRenderer.cs:200-207`), not by bones. Keep the names anyway:
  clips target bone nodes, so renaming bones forces clip re-authoring, and a shared
  skeleton is what makes the v2 clip data reusable.

## 5. Required animation clips

Names are exact and lowercase: `idle`, `move`, `fire`. These are the required minimum:
the validator fails a GLB missing any of them but accepts extra clips (§7). Frame
rate: 24 fps.

| Clip | Frames | Duration | Loop intent | Notes |
|---|---|---|---|---|
| idle | 48 | 2.0 s | yes | breathing/weight shift; no foot sliding (unit is planted) |
| move | 24 | 1.0 s | yes | full stride cycle; stride length must match 1.8 m/s to avoid foot slide |
| fire | ≤ 12 | ≤ 0.5 s | **no** | one-shot; must end on its own |

Fire duration rule: `fire` must stay under the 0.6 s weapon cooldown (12 ticks at
20 Hz) so the next shot is not visually late. `UnitRenderer.UpdateAnimation`
(`game/render/UnitRenderer.cs:188-204`) plays `fire` on the cooldown edge and refuses
locomotion until `CurrentAnimation` stops being `fire` (line 200). If the imported
fire animation loops, that guard never releases and the unit strands in the fire
pose.

Loop semantics — three distinct things, often conflated:

1. **Manifest loop intent**: the `loop` booleans in `manifest.json`. Authoring
   metadata only.
2. **Cyclic keyframes**: first and last keyframes match, so the clip *can* loop.
   glTF defines keyframe samplers, not runtime looping (Khronos glTF 2.0
   specification §3.11). Neither `rifleman.glb` nor `rifleman_v2.glb` carries
   animation `extras` recording any loop flag (verified: all animation `extras` are
   absent).
3. **Imported runtime loop mode**: the loop mode on the Godot `Animation` resource
   after import. Nothing in the GLB sets it; the manifest booleans are not proof of
   it.

Import contract for GRY-284: the import must preserve the exact clip names
`idle`/`move`/`fire` (UnitRenderer plays them by name) and must explicitly set loop
modes on the imported animations — `idle`/`move` looping, `fire` non-looping — via
import settings or a post-import step, then verify with a headless import check.
Do not rely on defaults.

Suspected current defect (not runtime-verified): the blockout manifest declares fire
as 24 frames with `loop: true`, and the GLB's fire clip spans 1.000 s — over the
0.6 s cooldown. Whether the imported Godot animation actually loops at runtime was
not checked here (no engine import in this task). GRY-284 must verify the actual
imported loop mode and fix duration and loop mode together.

## 6. Material slots (faction-swappable)

`docs/readability.md` §4 requires: author assets neutral and tint at runtime; team
colour occupies a consistent screen-area fraction in the same anatomical location on
every unit. Structure the slots so the faction palette changes only texture values
and tint masks, never geometry (the tier feature is a separate, possibly geometric,
decision — §1):

| Slot | Content | Faction treatment |
|---|---|---|
| `faction_primary` | main armour plates | runtime-tinted (team hue 1) |
| `faction_secondary` | trim, shoulder accents | runtime-tinted (team hue 2) |
| `faction_insignia` | small decal area on chest or shoulder | emblem art from GRY-282; may stay empty until then |
| `undersuit` | soft under-armour | neutral |
| `metal_alloy` | frame, joints, buckles | neutral |
| `visor_glass` | visor, emissive | hue reserved pending GRY-282; author neutral-cyan |
| `equipment` | webbing, pouches, backpack | neutral |
| `weapon` | rifle | neutral |

### Logical slots versus exported surfaces

The eight slots above are logical authoring regions, not eight runtime surfaces. A
mesh exported with one material per slot ships one mesh primitive per slot, and
each primitive renders as its own surface: the current blockout exports 7 material
primitives and `rifleman-v2` exports 12, so neither meets the ≤2 surface budget
(§3) as exported. A single atlas on a single mesh does not by itself merge
independently shaded slots.

Export contract for GRY-284:

- Author the 8 slots as distinct regions of one 1024 base-colour atlas.
- Export at most 2 rendered mesh primitives per LOD, referencing at most 2 material
  definitions:
  - one faction-tinted material covering `faction_primary`, `faction_secondary` and
    `faction_insignia`, using per-slot tint masks (one mask texture with distinct
    channels, or one mask per hue) so the runtime tints each region independently
    from the same surface;
  - one neutral material covering `undersuit`, `metal_alloy`, `equipment` and
    `weapon`, with colour variation baked into the atlas; `visor_glass` emissive
    moves into the neutral material's emissive map masked to the visor region, or
    into the tinted material if the visor hue becomes faction-owned.
- The surface budget scope is rendered mesh primitives per unit for one active LOD,
  in the unit's opaque forward render pass (§3). It does not restrict the number of
  logical slots or authoring-time Blender materials, and it is not a total-frame
  draw-call guarantee. No validator or CI check measures it; GRY-284 verifies by
  counting mesh primitives in the exported GLB for the active LOD, with the ≤2
  material-definition count kept as a separate check.

Rename the coalition-prefixed blockout materials (`coalition_armor`, `coalition_plate`,
`coalition_fabric`) to the neutral slot names above. Keep the tinted area small and in
one location per roster (`docs/readability.md` §4); the exact fraction is a GRY-282
number.

## 7. GLB export contract

`docs/asset-pipeline.md` (on main) plus `art/validate_glb.py` define the gate. Note:
`art/validate_glb.py` currently lives on branch
`gry-GRY-281-update-art-build-scripts-from-sprite-prototype-to-animated-glb`
(commit `6c808ee`), not on main yet. GRY-284 must build on main after GRY-281 merges,
or run the validator from that branch.

The delivered `.glb` must pass `python3 art/validate_glb.py <file>` (exit 0). The
validator enforces exactly:

1. Container: magic `glTF`, version 2, declared length equals file size, parseable
   JSON chunk.
2. Self-contained: no external buffer or image URIs (data URIs allowed); image
   `bufferView` references valid; BIN chunk covers every buffer's `byteLength`;
   every `bufferView` fits its buffer.
3. Materials: `materials` array non-empty (count and names unchecked).
4. Armature: `skins` non-empty; each skin has non-empty `joints` with in-range node
   indices; `inverseBindMatrices` (if present) is a valid accessor of type `MAT4`,
   componentType FLOAT, extent inside its bufferView, count ≥ joint count (bone
   names unchecked).
5. Clips: an `animations` array containing all of `idle`, `move`, `fire` (extra
   clips pass); each clip has channels with valid target nodes and samplers whose
   input accessors are non-empty SCALAR FLOAT and output accessors are non-empty
   FLOAT, with extents inside their bufferViews (frame counts, fps and loop intent
   unchecked).

Production requirements the validator does NOT enforce — GRY-284 must check these
separately:

- the exact 16-bone name set (§4);
- clip frame counts and 24 fps sampling (§5);
- loop intent and imported loop modes (§5);
- rendered mesh primitives ≤ 2 per active LOD (§3, §6);
- distinct material definitions ≤ 2 per LOD (§6);
- texture sizes and memory budget (§3).

Export settings from `docs/asset-pipeline.md`: applied transforms; materials and
textures embedded; armature and named actions included; re-import the GLB in Blender
and verify bounds, scale, axes, skeleton and action names before handoff. Record the
engine-facing origin and forward axis in `manifest.json`.

## 8. Existing work folders: comparison and recommendation

Validator results below were produced by running the pinned validator
(`6c808ee`, read-only via `git show`) against the existing files.

| Folder | State | Mesh | Rig + clips | Validator | Verdict |
|---|---|---|---|---|---|
| `rifleman` | `animated-production`, in `game/assets/` | 423 verts, blocky primitives, 7 coalition-named materials | 16 bones; idle/move/fire present (fire 1.0 s, manifest loop=true) | PASS | best pipeline base |
| `rifleman-v2` | `rejected` by the user (does not match the chat renders) | 18,960 verts, 12 materials | same 16 bones; fire non-looping per manifest, still 1.0 s | PASS | keep for comparison only |
| `rifleman-hunyuan` | visual study, not approved | 69,819-vert generated shape; no materials, no rig, no clips (verified in the GLB) | none | FAIL (materials/armature/clips) | silhouette reference value only |
| `rifleman-trellis2` | runner ready, no mesh generated | none | none | n/a | future candidate, not a base |

Both `rifleman` and the rejected `rifleman-v2` pass the validator, so validator
success alone does not justify a base choice. The recommendation rests on pipeline
suitability and the recorded user rejection.

Recommendation: carry forward `art/work/rifleman/` as the base.

1. It is the only candidate installed at the runtime path (`game/assets/`) with a
   build script, a manifest, and a full rig + named-clip set — the reusable
   foundation the spec above standardises. It also passes the validator.
2. Its mesh is a placeholder by design (blockout). GRY-284 replaces the mesh while
   keeping the armature, so the clips and pipeline survive the swap.
3. Use `rifleman-hunyuan`'s shape and its Qwen rear/left views as the high-fidelity
   silhouette reference when modelling the production mesh, after retopology.
4. Do not carry `rifleman-v2` geometry forward: the user rejected its looks (recorded
   in its `CONTEXT.md`), and it exceeds the poly and surface budgets. Reuse at most
   its non-looping fire intent; its fire duration (1.0 s) still violates §5.
5. Re-evaluate `rifleman-trellis2` once its generation actually runs; if it produces a
   better mesh, retarget it onto the same 16-bone skeleton rather than switching bases.

## 9. Open items and owners

| Item | Owner | Blocks? |
|---|---|---|
| Faction palette, team-hue fraction/location, visor hue, insignia | GRY-282 (in review) | texture values and masks; insignia geometry if it is modelled |
| Countable tier feature per faction | GRY-282-era decision (`docs/readability.md` §3, Status) | can change mesh geometry; required before art begins |
| Scale exaggeration factor and luminance delta numbers | decision required before art begins (`docs/readability.md` §8, Status) | final mesh scale and shading; required before art begins |
| `art/validate_glb.py` + GLB-first `art/build-asset.sh` on main | GRY-281 (in progress) | validation gate for GRY-284 |
| `make asset-*` targets still call the sprite-era script on main | fixed by GRY-281 | build path for GRY-284 |
| Imported loop modes and fire duration verification | GRY-284 (§5) | runtime animation correctness |
