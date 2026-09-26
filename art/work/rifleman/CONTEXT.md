# rifleman — first infantry source asset

Owns: the editable `rifleman.blend`, its deterministic hand-built blockout script, sprite-render script, and `manifest.json`.
Reads: `art/references/armoured-infantry-reference.jpg` as loose silhouette inspiration; the reference is not geometry or faction canon.
Writes: the matching `art/export/rifleman/` deliverables when built with `art/build-asset.sh rifleman`.
Do NOT: treat the asset as production art; it has no skeleton/animation clips and its coalition styling is provisional.
Build: `art/build-asset.sh rifleman` regenerates the Blender source, GLB, eight directional PNGs, sheet, and sprite manifest. Add `--install` to copy the prototype to Godot and run headless import.
