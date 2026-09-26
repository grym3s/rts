# Local model task: make the first animated 3D RTS unit

Paste this prompt into the local coding model with the RTS repository mounted. The Codex task is implementing the Godot 3D camera/renderer separately; your scope is Blender art and GLB deliverables only.

---

Create a real, original, rigged and animated 3D infantry unit for the RTS. Do the work; do not stop after reading docs or producing a status summary.

## Visual direction

The game is a high-angle 3D RTS. Use the fidelity/readability bar of modern 3D RTS games such as Dust Front and StarCraft II as broad quality references. Create original armor, silhouette, materials, and weapon details; do not copy their units or recognizable franchise designs.

Use the preserved `art/references/armoured-infantry-reference.jpg` as loose inspiration for a regular rifle infantry silhouette. It is not ground-truth geometry. Keep the faction provisional until it is reconciled with the faction canon. Leave every reference image untouched.

## Scope and boundaries

- You own Blender work and deliverables under `art/work/rifleman/` and `art/export/rifleman/` only. You may update `art/CONTEXT.md`, the rifleman manifest, and `art/build-asset.sh` for the GLB workflow.
- **Do not edit `game/`, `game/assets/`, `sim/`, the Makefile, or `docs/asset-pipeline.md`.** Godot presentation and runtime installation are being implemented separately in the same shared checkout.
- Inspect `git status --short --branch` first. Preserve all existing uncommitted files. Before replacing any existing blockout, `.blend`, GLB, sprites, or manifest, copy the exact previous version into a clearly named archive under `art/work/rifleman/`. Never delete the existing `attempt-1/` archive or alter the reference images.
- Do not commit, push, reset, clean, or claim work that was not produced and verified.

## Required Blender asset

1. Read `AGENTS.md`, `art/CONTEXT.md`, `art/work/rifleman/CONTEXT.md`, `art/work/rifleman/manifest.json`, `docs/factions/`, and `docs/readability.md`. Check the `rifleman` content entry; preserve unresolved faction identity as provisional.
2. Verify Blender and its glTF exporter. Blender is available at `~/.local/bin/blender`. Keep the editable `.blend` and any scripts/textures in `art/work/rifleman/`.
3. ComfyUI is at `http://127.0.0.1:8188`. Local Qwen Image 2.1 models can make raster concept/turnaround images, but **cannot make 3D meshes**. Their files are in `~/ComfyUI/models/`. Check `/queue` and resource pressure before generating; do not start a competing model load. Preserve each generated image and its prompt as work-in-progress provenance.
4. Replace the primitive-only proxy with a designed, proportionate armored rifleman: readable head/torso/limbs, layered but coherent armor, distinctive original helmet, backpack, and properly held rifle. Build useful topology, UVs, and a consistent authored PBR material palette. Use a sensible real-world scale near 1.8 m and origin at the feet.
5. Create a reusable humanoid armature with clear bone names, suitable deformations, and at least `idle`, `move`, and `fire` actions. Check the rifle grip, foot contact, limb deformation, and loop boundaries. These should be authored animation actions, not posed stills.
6. Use this engine handoff convention: **Godot Y-up, model origin at ground-level feet, model forward along local -Z**. Make sure the GLB imports with those axes and no unexplained corrective rotation/scale.
7. Export a self-contained `art/export/rifleman/rifleman.glb` with mesh, armature, PBR materials/textures, and named animation clips. Re-import it in Blender and verify bounds, materials, skeleton, and clip names. Leave runtime installation under `game/assets/rifleman/` to the separate Godot integration task; do not edit Godot code or runtime files.
8. Update `art/work/rifleman/manifest.json` truthfully with authoring provenance, source paths, scale/origin, material info, animation names, and validation status. Do not mark visual review or faction approval complete without evidence.

## Verification and report

- Run Blender in background to open the saved `.blend`, export the GLB, re-import it, and print/check the mesh bounds and animation names.
- Verify the GLB is self-contained and the Blender re-import succeeds. Do not run or edit the Godot project; the separate presentation task will import it.
- Keep the previous sprite/blockout export archived exactly. Do not imply the model is in the live 3D game until the separate Godot presentation work has consumed it and been visually checked.
- Finish with a concise evidence report: files created, model/rig/clip details, Blender and Godot results, archive path, and any exact blocker. Do not replace the actual work with instructions for the user.
