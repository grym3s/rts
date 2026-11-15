# rifleman — first infantry source asset

Owns: the editable `rifleman.blend`, its deterministic hand-built blockout script, the animated build script (`build_rifleman.py`), sprite-render script, and `manifest.json`.
Reads: `art/references/armoured-infantry-reference.jpg` as loose silhouette inspiration; the reference is not geometry or faction canon.
Writes: the matching `art/export/rifleman/` deliverables when built with `art/build-asset.sh rifleman`.
Do NOT: treat the asset as production art; its coalition styling is provisional.
Build: `art/build-asset.sh rifleman` regenerates the Blender source and GLB (animated), validates the GLB (materials, armature, clips), and exports the GLB. Add `--install` to copy the GLB to Godot and run headless import.
