# Rifleman production asset spec (faction-neutral)

Prepared for GRY-284 (Produce and visually approve the first rifleman production asset).
GRY-284 is blocked on the GRY-282 faction review. This spec fixes everything that does
not depend on faction colours, so GRY-284 can start rig, mesh, animation and pipeline
work the day GRY-282 lands. Documentation only: no modelling, no GPU generation, no
engine import was done here.

Author: Magnus. Date: 2026-11-16. Revision base: main `133eb46`.

## 1. Scope and faction neutrality

In scope: scale, silhouette, budgets, armature, clips, material-slot structure, GLB
export contract, base-asset recommendation.

Deferred to GRY-282 (faction review), per `docs/readability.md` §4 and its Status
section:

- Faction palette and the two team-colour hues.
- The team-colour screen-area fraction and its exact anatomical location.
- The countable tier feature per faction (§3).
- The luminance delta number and the scale exaggeration factor (§8).
- The visor hue and any faction emblem or insignia geometry.

None of these block the mesh, rig, clips or export pipeline. They change texture
values and one decal area, not structure.

## 2. In-game scale and silhouette targets

Verified presentation facts (read from code, not assumed):

- `game/camera/RtsCamera.cs`: perspective camera, FOV 45°, fixed pitch 52°, orbit
  distance 18–112 m, default 92 m.
- `docs/asset-pipeline.md`: one map cell = one world unit (1 m). Model origin at
  ground-level feet, Y-up, local forward −Z (Godot).
- `content/units/rifleman.json`: speed 1.8 (≈1.8 m/s), weapon cooldown 12 ticks.
  `sim/core/SimWorld.cs` sets `TicksPerSecond = 20`, so the cooldown is 0.6 s.
- `game/render/UnitRenderer.cs`: loads `res://assets/rifleman/rifleman.glb`, sets yaw
  from velocity, plays `idle`/`move` from velocity and `fire` on a cooldown
  0→positive edge. The health bar sits at y = 1.95 m.

Screen-size targets (computed from the camera numbers, 1080p viewport):

| Zoom | Orbit distance | Visible height | px per metre | 1.8 m unit height |
|---|---|---|---|---|
| Max zoom-in | 18 m | 14.9 m | ≈72 | ≈130 px |
| Default | 92 m | 76.2 m | ≈14 | ≈25 px |
| Max zoom-out | 112 m | 92.8 m | ≈12 | ≈21 px |

Targets:

- Model height: 1.80 m to the helmet crown, ≤1.96 m including backpack and raised
  rifle (matches the current blockout bounds: crown ≈1.70 m, max Y ≈1.96 m).
  `docs/readability.md` §8 (the scale lie) keeps units oversized; the exact exaggeration
  factor is an open GRY-282-era decision. 1.80 m is the working number until set.
- Silhouette class: basic infantry. Identification must come from proportion, not
  detail (`docs/readability.md` §2). At default zoom the unit is ≈25 px tall; detail
  below ≈3 px does not survive and must not carry information.
- Pass the 64px black-shape sheet (§2 test) and the greyscale-collapse test (§1) at
  default zoom once the terrain value ceiling exists.
- Keep the rifle held forward and inside a 0.65 m × 0.65 m ground footprint so unit
  spacing and selection rings (torus outer radius 0.38 m) do not clip.

## 3. Polygon and texture budget

Rationale: the unit renders ≈21–130 px tall. Geometry beyond LOD0 pays only at the
closest zoom.

| Item | Budget | Note |
|---|---|---|
| LOD0 triangles | ≤ 6,000 | close zoom only |
| LOD1 triangles | ≤ 2,000 | covers default zoom |
| LOD2 triangles | ≤ 600 | zoom-out and mass scenes |
| Draw calls per unit | ≤ 2 | one skinned mesh per LOD preferred |
| Base-colour atlas | 1024×1024, sRGB | single atlas for all slots |
| Normal map | 1024×1024 | OpenGL +Y convention |
| ORM (AO/roughness/metallic packed) | 1024×1024 | linear |
| Emissive mask | ≤ 512×512 or constant colours | visor/lights |
| Total texture memory | ≤ 4 MB compressed per unit | power-of-two dimensions (4×4 block compression) |

Reference points: `rifleman-v2` shipped 18,960 source vertices (over budget, and
rejected on looks); the Hunyuan shape is 69,819 vertices and needs retopology before
it can count as a production mesh at all.

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
- Do not add or rename bones without re-authoring the clips: the validator and the
  runtime both key on this skeleton.

## 5. Required animation clips

Names are exact and lowercase: `idle`, `move`, `fire`. The validator fails any other
set. Frame rate: 24 fps.

| Clip | Frames | Duration | Loop | Notes |
|---|---|---|---|---|
| idle | 48 | 2.0 s | yes | breathing/weight shift; no foot sliding (unit is planted) |
| move | 24 | 1.0 s | yes | full stride cycle; stride length must match 1.8 m/s to avoid foot slide |
| fire | ≤ 12 | ≤ 0.5 s | **no** | one-shot; must end on its own |

Why `fire` must not loop: `UnitRenderer.UpdateAnimation` (`game/render/UnitRenderer.cs`)
plays `fire` on the cooldown edge and refuses locomotion until the clip stops
(`if (animator.IsPlaying() && animator.CurrentAnimation == "fire") return;`). A looping
fire clip strands the unit in the fire pose. The fire duration must also stay under the
0.6 s weapon cooldown so the next shot is not visually late. The current blockout
violates both rules (fire = 24 frames, loop=true); fixing it is part of GRY-284.

## 6. Material slots (faction-swappable)

`docs/readability.md` §4 requires: author assets neutral and tint at runtime; team
colour occupies a consistent screen-area fraction in the same anatomical location on
every unit. Structure the slots so GRY-282 changes only texture values and one tint
mask, never geometry:

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

Rules: at most 8 slots (current blockout has 7, `rifleman-v2` has 12 — merge down).
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
validator enforces:

1. Container: magic `glTF`, version 2, declared length equals file size, parseable
   JSON chunk.
2. Self-contained: no external buffer or image URIs (data URIs allowed); image
   `bufferView` references valid; BIN chunk covers every buffer's `byteLength`;
   every `bufferView` fits its buffer.
3. Materials: `materials` array non-empty.
4. Armature: `skins` non-empty; each skin has non-empty `joints` with in-range node
   indices; `inverseBindMatrices` (if present) is a valid accessor of type `MAT4`,
   componentType FLOAT, extent inside its bufferView, count ≥ joint count.
5. Clips: an `animations` array containing all of `idle`, `move`, `fire`; each clip
   has channels with valid target nodes and samplers whose input accessors are
   non-empty SCALAR FLOAT and output accessors are non-empty FLOAT, with extents
   inside their bufferViews.

Export settings from `docs/asset-pipeline.md`: applied transforms; materials and
textures embedded; armature and named actions included; re-import the GLB in Blender
and verify bounds, scale, axes, skeleton and action names before handoff. Record the
engine-facing origin and forward axis in `manifest.json`.

## 8. Existing work folders: comparison and recommendation

| Folder | State | Mesh | Rig + clips | Verdict |
|---|---|---|---|---|
| `rifleman` | `animated-production`, in `game/assets/` | 423 verts, blocky primitives, 7 coalition-named materials | 16 bones; idle/move/fire present (fire loop flag wrong) | only asset that satisfies the pipeline today |
| `rifleman-v2` | `rejected` by the user (does not match the chat renders) | 18,960 verts, 12 materials | same 16 bones; fire already non-looping | keep for comparison only |
| `rifleman-hunyuan` | visual study, not approved | 69,819-vert generated shape; no materials, no rig, no clips (verified in the GLB) | none | silhouette reference value only |
| `rifleman-trellis2` | runner ready, no mesh generated | none | none | future candidate, not a base |

Recommendation: carry forward `art/work/rifleman/` as the base.

1. It is the only folder whose asset passes the GLB contract (rig + named clips +
   runtime install path + build script). Its 16-bone skeleton and clip set are the
   reusable foundation the spec above standardises.
2. Its mesh is a placeholder by design (blockout). GRY-284 replaces the mesh while
   keeping the armature, so the clips and pipeline survive the swap.
3. Use `rifleman-hunyuan`'s shape and its Qwen rear/left views as the high-fidelity
   silhouette reference when modelling the production mesh, after retopology.
4. Do not carry `rifleman-v2` geometry forward (user rejection on record in its
   `CONTEXT.md`); reuse at most its non-looping fire-clip timing.
5. Re-evaluate `rifleman-trellis2` once its generation actually runs; if it produces a
   better mesh, retarget it onto the same 16-bone skeleton rather than switching bases.

## 9. Open items and owners

| Item | Owner | Blocks? |
|---|---|---|
| Faction palette, team-hue fraction/location, tier count feature, visor hue, insignia | GRY-282 (in review) | texture values only |
| Scale exaggeration factor and luminance delta numbers | decision noted in `docs/readability.md` Status | final polish only |
| `art/validate_glb.py` + GLB-first `art/build-asset.sh` on main | GRY-281 (in progress) | validation gate for GRY-284 |
| `make asset-*` targets still call the sprite-era script on main | fixed by GRY-281 | build path for GRY-284 |
