#!/usr/bin/env python3
"""Apply one conservative Hunyuan PBR texture pass to the reviewed mesh."""
from pathlib import Path
import json, shutil, sys, time

ROOT = Path(__file__).resolve().parents[3]
INSTALL = Path.home() / ".local/opt/hunyuan3d-2.1-rocm"
OUT = Path(__file__).resolve().parent
sys.path.insert(0, str(INSTALL))
import backend
import paint
import trimesh
from PIL import Image

mesh_path = OUT / "rifleman-hunyuan-shape.glb"
image_path = OUT / "conditioning-image.png"
image = Image.open(image_path).convert("RGBA")
mesh = trimesh.load(mesh_path, force="mesh")
if len(mesh.faces) < 1000:
    raise RuntimeError(f"unexpectedly small mesh: {len(mesh.faces)} faces")
b = backend.detect(force_raster="torch")
ok, reason = paint.availability()
if not ok:
    raise RuntimeError(f"PBR pipeline unavailable: {reason}")
safe, detail = paint.attention_preflight(b, 6, 256)
if not safe:
    raise RuntimeError(f"PBR safe preset refused: {detail}")
state = {"mesh": mesh, "original": mesh, "last_op": "original", "image": image}
started = time.time()
preview, gallery, tex_state, log = __import__("gradio_app").texture_3d_handler(
    state, image, "safe", 6, 256, "torch", False, "en", None
)
if not tex_state:
    raise RuntimeError(log)
source = tex_state.get("glb")
if source and Path(source).is_file():
    shutil.copy2(source, OUT / "rifleman-hunyuan-textured.glb")
map_dir = OUT / "pbr-maps"
map_dir.mkdir(exist_ok=True)
map_paths = {}
for name, path in tex_state.get("maps", {}).items():
    if path and Path(path).is_file():
        dst = map_dir / Path(path).name
        shutil.copy2(path, dst)
        map_paths[name] = str(dst.relative_to(OUT))
report = {
    "input_mesh": mesh_path.name, "conditioning_image": image_path.name,
    "backend": backend.describe(b), "preset": "safe",
    "views": 6, "view_resolution": 256, "texture_resolution": 2048,
    "preflight": detail, "output": "rifleman-hunyuan-textured.glb" if source else None,
    "pbr_maps": map_paths, "elapsed_seconds": round(time.time()-started, 1),
    "pipeline_log": log,
    "status": "texture output requires Blender visual review; not approved for game use",
}
(OUT / "texture-report.json").write_text(json.dumps(report, indent=2)+"\n")
print(json.dumps(report, indent=2))
