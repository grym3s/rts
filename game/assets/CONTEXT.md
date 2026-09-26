# game/assets — Godot-imported runtime resources only

Owns: nothing authored. Copies of prototype or approved animated 3D GLB exports from `art/export/<asset-id>/` that the game consumes, plus Godot's generated `.import` sidecars. Prototype status must remain explicit in the source manifest until visual and faction review are complete.
Reads: `art/export/`, `art/CONTEXT.md` (provenance).
Writes: nothing else. Sources live under `art/`; this folder is disposable from art exports.
Tests: `make godot-test` (import + headless run + smoke). `make check` enforces this CONTEXT.md.
Do NOT: store the only copy of a source model here; import anything not approved in `art/export/`; reference these assets from `sim/`.
Change impact: `game/render/CONTEXT.md` when a renderer starts consuming an asset; `docs/asset-pipeline.md` step 5.
Known limits (2026-09-26): `rifleman/` contains an older primitive GLB and still-sprite prototype. The 3D renderer now instantiates the GLB directly, with capsule fallback and optional `idle`/`move`/`fire` clips. The current GLB has no rig or animation, so its visible mesh is still a placeholder awaiting the parallel Blender art task.
