# rifleman-v2 — rejected placeholder iteration

Owns: a separate, editable Blender placeholder that was rejected by the user for failing to match the rendered design. Retain for comparison only; not production art and not suitable for Unreal import.

Reads: `art/references/armoured-infantry-reference.jpg` for silhouette, blue visor, layered hard armor, chest equipment, backpack, and rifle language.

Writes: `rifleman_v2.blend`, `rifleman_v2.glb`, and preview renders. The source script is deterministic Blender Python. Keep the existing `art/work/rifleman/` blockout and runtime copy untouched.

Animation clips: `idle`, `move`, and `fire`. Rigging is rigid-per-part skinning to a reusable humanoid armature; review the clips and deformation before production use.

Known limits: crude manually authored procedural geometry; it does not match the reference renders. The clips are not an acceptable substitute for a properly modeled and rigged unit. Do not use this asset in the game.
