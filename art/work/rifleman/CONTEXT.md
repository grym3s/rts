# rifleman — first infantry source asset

Owns: the editable `rifleman.blend`, its deterministic hand-built blockout script, sprite-render script, `manifest.json`, and `SPEC.md` (faction-neutral production spec prepared for GRY-284; defers faction decisions to GRY-282).
Reads: `art/references/armoured-infantry-reference.jpg` as loose silhouette inspiration; the reference is not geometry or faction canon.
Writes: the matching `art/export/rifleman/` deliverables when built with `art/build-asset.sh rifleman`.
Do NOT: treat the asset as production art; its mesh is a blockout placeholder and its coalition styling is provisional (see `SPEC.md` §5 for the suspected fire-clip loop defect: the manifest declares `loop: true` and the clip spans 1.0 s, but the GLB records no loop flag and the imported runtime loop mode is unverified — GRY-284 must verify and fix).
Build: `art/build-asset.sh rifleman` regenerates the Blender source, GLB, eight directional PNGs, sheet, and sprite manifest. Add `--install` to copy the prototype to Godot and run headless import.
