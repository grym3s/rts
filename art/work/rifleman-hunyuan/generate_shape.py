#!/usr/bin/env python3
"""Generate one isolated Hunyuan3D shape attempt from the canonical render."""
from pathlib import Path
import json, shutil, sys, time

ROOT = Path(__file__).resolve().parents[3]
INSTALL = Path.home() / ".local/opt/hunyuan3d-2.1-rocm"
SOURCE = ROOT / "art/references/elite-infantry-chat-render.jpg"
OUT = Path(__file__).resolve().parent

sys.path.insert(0, str(INSTALL))
import gradio_app as hy3d
from PIL import Image

started = time.time()
image = Image.open(SOURCE).convert("RGBA")
# Let Hunyuan's rembg isolate the soldier from the black render background.
glb_path, state, log, _ = hy3d.generate_3d(
    image=image, remove_bg=True, num_steps=30, guidance_scale=5.0,
    octree_resolution=192, seed=42, track_gpu=False, lang="en",
)
if not glb_path or state is None:
    raise RuntimeError(log)
shutil.copy2(glb_path, OUT / "rifleman-hunyuan-shape.glb")
state["image"].save(OUT / "conditioning-image.png")
report = {
    "source": str(SOURCE), "output": "rifleman-hunyuan-shape.glb",
    "backend": hy3d.backend_mod.describe(hy3d.BACKEND),
    "settings": {"steps": 30, "guidance_scale": 5.0, "octree_resolution": 192, "seed": 42, "remove_background": True},
    "vertices": int(len(state["original"].vertices)),
    "faces": int(len(state["original"].faces)),
    "elapsed_seconds": round(time.time()-started, 1),
    "generator_log": log,
    "status": "generated; visual review required; not approved for game use",
}
(OUT / "generation-report.json").write_text(json.dumps(report, indent=2)+"\n")
print(json.dumps(report, indent=2))
