"""Render 8-direction top-down orthographic sprite frames + assemble a sheet.

Run: ~/blender-env/.venv/bin/python render_sprites.py   (after build_blockout.py)
Frames: 96x96 px, transparent, row-major E..NE (8 dirs), idle only (blockout has no rig).
"""
import bpy, math, os, json
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ASSET_ID = os.environ.get("RTS_ASSET_ID", "rifleman")
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
EXPORT = os.path.join(ROOT, "art", "export", ASSET_ID)
BLEND = os.path.join(HERE, f"{ASSET_ID}.blend")
FRAME = 96
DIRS = ["E", "SE", "S", "SW", "W", "NW", "N", "NE"]  # sheet order (row-major, single row)

bpy.ops.wm.open_mainfile(filepath=BLEND)

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = FRAME
scene.render.resolution_y = FRAME
scene.render.resolution_percentage = 100
scene.render.film_transparent = True
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"

unit = bpy.data.objects[ASSET_ID]
unit.rotation_euler = (0, 0, 0)
# top-down: camera fixed looking straight down; sprite 'up' on screen = world -Y...
# unit faces +Y at rest. Screen-up for a -Z-looking camera with roll 0 is +Y,
# so rotating the unit by az makes it face screen direction az (0 = up... remapped below).
DIST, RANGE = 9.3, 2.1

cam_data = bpy.data.cameras.new("sprite_cam")
cam_data.type = "ORTHO"
cam = bpy.data.objects.new("sprite_cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam.location = (0, -5.5, 7.5)
cam.rotation_euler = (Vector((0, 0, 0.9)) - cam.location).to_track_quat("-Z", "Y").to_euler()

# sun fixed above so shading varies with the unit's rotation, not the camera's
sun_d = bpy.data.lights.new("sun", "SUN")
sun_d.energy = 3.0
sun = bpy.data.objects.new("sun", sun_d)
sun.rotation_euler = (math.radians(25), 0, 0)
scene.collection.objects.link(sun)

frame_paths = []
for i, d in enumerate(DIRS):
    # map compass direction to unit yaw: unit faces +Y at rest; N = +Y = yaw 0.
    yaw = math.radians(45 * (DIRS.index("N") - i))  # E=-270==90cw... E faces screen-right
    unit.rotation_euler = (0, 0, yaw)
    cam_data.ortho_scale = RANGE
    p = os.path.join(EXPORT, f"frame_{i:02d}_{d.lower()}.png")
    scene.render.filepath = p
    bpy.ops.render.render(write_still=True)
    frame_paths.append(p)

# Assemble sheet with PIL-free composite: use bpy image paste via numpy.
import numpy as np
sheet = bpy.data.images.new("rifleman_sheet", FRAME * len(DIRS), FRAME, alpha=True)
buf = bytearray(FRAME * len(DIRS) * FRAME * 4)
for i, p in enumerate(frame_paths):
    img = bpy.data.images.load(p)
    # Blender stores image channels as floats in [0, 1]; scale before quantizing.
    px = np.rint(np.array(img.pixels[:], dtype=np.float32) * 255.0).clip(0, 255).astype(np.uint8).reshape(FRAME, FRAME, 4)
    for y in range(FRAME):
        row_start = ((FRAME - 1 - y) * FRAME * len(DIRS) + i * FRAME) * 4
        srow = (y * FRAME * 4)
        buf[row_start:row_start + FRAME * 4] = px[y].tobytes()
    bpy.data.images.remove(img)
sheet.pixels = [b / 255 for b in buf]
sheet_path = os.path.join(EXPORT, f"{ASSET_ID}_sheet.png")
sheet.filepath_raw = sheet_path
sheet.file_format = "PNG"
sheet.save()
print("SHEET", sheet_path, os.path.getsize(sheet_path), "bytes", FRAME * len(DIRS), "x", FRAME)
for p in frame_paths:
    print("frame", os.path.basename(p), os.path.getsize(p), "bytes")

manifest = {
    "schemaVersion": 1,
    "assetId": ASSET_ID,
    "sheet": f"{ASSET_ID}_sheet.png",
    "projection": "orthographic RTS angle",
    "background": "transparent",
    "frameSizePx": [FRAME, FRAME],
    "layout": {"rows": 1, "columns": len(DIRS), "order": "row-major"},
    "directions": DIRS,
    "directionConvention": "compass bearing on the top-down map; sprite faces that screen direction (N = screen-up)",
    "animations": [{"name": "idle", "rows": [0], "framesPerDirection": 1, "framesPerSecond": 1}],
    "framesPerSecond": 1,
    "pixelsPerSimCellAtZoom1": 32,
    "modelHeightMeters": 1.81,
    "pivot": {"x": 0.5, "y": 0.92},
    "pivotNote": "bottom-centre of the frame; y measured from the top in texture space. Tune in-engine against the 32px/sim-cell footprint.",
    "limits": "Blockout: single idle pose per direction, no rig, flat materials. Not a production animation set."
}
with open(os.path.join(EXPORT, "sprite-manifest.json"), "w", encoding="utf-8") as f:
    json.dump(manifest, f, indent=2)
    f.write("\n")
print("MANIFEST", os.path.join(EXPORT, "sprite-manifest.json"))
