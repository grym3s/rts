# 3D RTS art pipeline — concepts to animated Godot units

Blender is the authoring tool for meshes, materials, rigs, animation, and GLB export. Godot 4.7.1 .NET imports and renders those animated 3D scenes directly. Sprite sheets are optional review outputs; they are not the unit runtime format. The deterministic simulation remains engine-free.

## Presentation contract

- The game presents a high-angle, perspective 3D RTS battlefield. `game/camera/RtsCamera.cs` owns pan, cursor-anchored zoom, and ground-plane ray projection.
- Simulation positions use map X/Y. At the game boundary, map X becomes Godot world X, map Y becomes world Z, and Godot world Y is height. One map cell currently maps to one world unit.
- Unit models use Godot's Y-up convention, origin at ground-level feet, and local forward along -Z. Use a corrective transform only when an imported asset's metadata requires it, and record that offset in its manifest.
- `game/render/UnitRenderer.cs` interpolates sim positions, sets model yaw from velocity, loads `.glb` scenes, and plays `idle`, `move`, and `fire` clips when those clips are present. The simulation remains the authority for position and combat state.
- `game/orders/OrdersInput.cs` converts screen rays to map-plane points and emits `Command`s. No game presentation code writes sim state directly.

## Folders and source of truth

- `art/references/`: unmodified source images and concepts, with provenance in `art/CONTEXT.md`.
- `art/work/<asset-id>/`: editable `.blend`, model/rig scripts, textures, generated concept work, and a filled asset manifest.
- `art/export/<asset-id>/`: validated prototype or approved `.glb` files plus export manifest and optional review renders.
- `art/prompts/`: reusable project-specific generation and modeling instructions.
- `game/assets/<asset-id>/`: runtime copies of the GLB and Godot-generated import sidecars. Blender source stays under `art/work/`.

Binary formats are covered by Git LFS in `.gitattributes`. Preserve reference concepts and archive superseded generated outputs before replacing them. Do not check in editor caches or duplicate model versions in runtime folders.

## Local tools

Blender 5.0.1 is available at `~/.local/bin/blender`; verify it starts in background mode and has the glTF exporter before building. Godot 4.7.1 is available as `godot`. The local .NET SDK is under `~/.dotnet`; for Godot/.NET commands set `DOTNET_ROOT="$HOME/.dotnet"` and prepend `~/.dotnet` to `PATH`. Git LFS is installed.

ComfyUI at `http://127.0.0.1:8188` has Qwen Image 2.1 and its Qwen text encoder/VAE. It can generate raster concepts or turnarounds; it cannot create meshes, rigs, or animations. Check the local queue and resource headroom before asking it to generate. Preserve prompts and outputs as work-in-progress provenance.

## Per-asset workflow

1. **Choose and record.** Assign a stable kebab-case ID, role, faction (or explicitly undecided), source references, and canon requirements. Copy `art/asset-manifest.template.json` to `art/work/<asset-id>/manifest.json`. Do not treat a concept picture as exact geometry.
2. **Design and model.** Use existing concepts or optional Qwen-generated views as guidance. Model an original, readable silhouette; create useful topology, UVs, and PBR materials. Keep manual and generated parts distinguishable in provenance.
3. **Rig and animate.** Use a reusable skeleton for humanoids where appropriate. Name bones and actions clearly. At minimum, infantry should have `idle`, `move`, and `fire` clips. Check deformation, weapon grip, foot contact, and loop boundaries in Blender.
4. **Export and validate.** Save the editable `.blend`; export a self-contained glTF 2.0 `.glb` with applied transforms, materials/textures, armature, and clips. Re-import the GLB in Blender and verify bounds, scale, axes, materials, skeleton, and action names. Record the engine-facing origin and forward axis in the manifest.
5. **Import in Godot.** Copy an explicitly prototype-labelled or approved GLB to `game/assets/<asset-id>/` and import it with the pinned Godot project. Verify scene root, mesh visibility, materials, scale, axes, and animation names. Keep the 3D presentation code inside `game/`; do not add engine references to `sim/`.
6. **Review in the game.** Inspect the running 3D scene at close, default, and far zoom with the actual ground, lighting, team indicators, and nearby units. Check role readability, animation, shadows, clipping, and performance. An import log or headless run is not a visual review.
7. **Promote.** Only after visual and faction review, mark the asset approved. Keep the editable source and provenance; copy the approved GLB to the runtime folder and update affected `CONTEXT.md` files.

## Current vertical slice and next asset work

`game/Main.cs`, `game/camera/`, `game/orders/`, and `game/render/` now provide a 3D test field, high-angle camera, sim-position mapping, GLB instancing, optional clip playback, world-space selection/health indicators, and ray-to-ground order input. The current rifleman GLB is still the old primitive blockout and has no armature or clips; the renderer uses a capsule only if GLB loading fails. A local art task is producing the first designed, rigged, animated rifleman without editing `game/`.

The current `art/build-asset.sh` and `make asset-*` targets were originally written for the superseded sprite prototype. Update them to build/install validated animated GLBs; do not make sprite rendering a required stage.

## Completion checks

- Blender source opens; GLB re-imports with expected scale, axes, materials, skeleton, and named clips.
- Godot imports and instantiates the model in the actual 3D battlefield; movement/facing/animation are driven from read-only sim state.
- Selection and orders still work by camera-to-ground projection; game changes sim state only via `Command`s.
- Visual quality has been checked at several camera zoom levels in the running game.
- Run `make check test` and `make godot-test`; build the .NET game with the local SDK path above. Do not change deterministic simulation behavior as part of art integration.
