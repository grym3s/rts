# art/export — generated asset deliverables

Owns: reproducible outputs from Blender builds: `.glb`, transparent runtime sprite sheets, individual review frames, and export manifests.
Reads: editable sources and provenance from `art/work/<asset-id>/`.
Writes: `export/<asset-id>/` via `art/build-asset.sh` or the asset's documented Blender builder; approved or explicitly labelled prototype copies may be installed under `game/assets/<asset-id>/`.
Do NOT: use this folder for editable source files, overwrite reference images, or mark an asset production-ready merely because it exports.
Change impact: update `art/CONTEXT.md`, `docs/asset-pipeline.md`, and `game/render/CONTEXT.md` when runtime consumption changes.
