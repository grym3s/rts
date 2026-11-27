# T17: import the rifleman GLB (art pipeline output, game/assets/rifleman/rifleman.glb) into
# /Game/Units/Rifleman and report what the engine made of it: assets, clip names and lengths,
# bounds (for the in-game scale) and material slots (for faction tint). Run headless:
#   UnrealEditor-Cmd <project> -run=pythonscript -script=<this file> -unattended -nullrhi
# Re-running replaces the assets. Writes a JSON report next to the project (Saved/rifleman-import.json).
import json
import os
import unreal

repo = os.path.abspath(os.path.join(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_dir()), ".."))
src = os.environ.get("RIFLEMAN_GLB") or os.path.join(repo, "game", "assets", "rifleman", "rifleman.glb")
dest = "/Game/Units/Rifleman"

task = unreal.AssetImportTask()
task.filename = src
task.destination_path = dest
task.automated = True
task.replace_existing = True
task.save = True
unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])

report = {"source": src, "imported": list(task.imported_object_paths or []), "assets": [], "anims": [], "mesh": None}
registry = unreal.AssetRegistryHelpers.get_asset_registry()
for data in registry.get_assets_by_path(dest, recursive=True):
    path = str(data.package_name) + "." + str(data.asset_name)
    cls = str(data.asset_class_path.asset_name)
    report["assets"].append({"path": path, "class": cls})
    obj = unreal.load_asset(path)
    if isinstance(obj, unreal.AnimSequence):
        report["anims"].append({"path": path, "name": str(data.asset_name), "seconds": obj.get_play_length()})
    elif isinstance(obj, unreal.SkeletalMesh):
        box = obj.get_bounds().box_extent
        origin = obj.get_bounds().origin
        report["mesh"] = {
            "path": path,
            "bounds_origin": [origin.x, origin.y, origin.z],
            "bounds_extent": [box.x, box.y, box.z],
            "materials": [str(m.material_slot_name) for m in obj.materials],
        }

out = os.path.join(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_saved_dir()), "rifleman-import.json")
with open(out, "w") as f:
    json.dump(report, f, indent=2)
unreal.log("RIFLEMAN_IMPORT " + json.dumps(report))
