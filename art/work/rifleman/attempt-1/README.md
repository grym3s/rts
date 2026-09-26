# Archived first sprite render

This folder preserves the first local-model output before correcting the sprite-sheet alpha bug. Its source PNGs are fully transparent: the render script cast Blender's float image channels (0–1) directly to unsigned bytes, reducing alpha to 0 or 1/255. Do not use these files as runtime art. The corrected files are in `art/export/rifleman/` and were copied into `game/assets/rifleman/` for Godot import verification.
