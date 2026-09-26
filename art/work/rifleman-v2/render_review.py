"""Round-trip the GLB, render clip stills and an animated review GIF."""

import bpy
import math
import os
import shutil
import subprocess
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent
GLB = HERE / "rifleman_v2.glb"
FRAMES = HERE / "_review_frames"
FRAMES.mkdir(exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(GLB))
scene = bpy.context.scene
rig = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
assert rig and meshes, "GLB must contain skinned meshes and an armature"
skinned = [o for o in meshes if any(m.type == "ARMATURE" and m.object == rig for m in o.modifiers)]
vertex_count = sum(len(o.data.vertices) for o in skinned)
assert vertex_count > 10000, f"unexpectedly simple skinned mesh: {vertex_count} vertices"
for helper in (o for o in meshes if o not in skinned):
    # Blender's importer can leave tiny unskinned helper objects beside the
    # mesh; the authored GLB itself is checked separately for one skinned node.
    print("IGNORING UNSKINNED IMPORT HELPER:", helper.name, len(helper.data.vertices))
    helper.hide_render = True
actions = {a.name.lower().split(".")[0]: a for a in bpy.data.actions}
for required in ("idle", "move", "fire"):
    assert required in actions, f"missing animation clip {required}; found {sorted(actions)}"
print("GLB ACTIONS:", sorted(actions), "SKINNED VERTICES:", vertex_count,
      "MESH OBJECTS:", len(meshes), "BONES:", len(rig.data.bones))

scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = 720
scene.render.resolution_y = 720
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.display.shading.light = "STUDIO"
scene.display.shading.studio_light = "paint.sl"
scene.display.shading.color_type = "MATERIAL"
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.display.shading.cavity_type = "BOTH"
scene.display.shading.curvature_ridge_factor = 1.15
scene.display.shading.curvature_valley_factor = 1.0
scene.display.shading.background_type = "WORLD"
scene.world = bpy.data.worlds.new("ReviewWorld")
scene.world.color = (0.075, 0.09, 0.11)
scene.view_settings.view_transform = "Standard"
floor_mat = bpy.data.materials.new("review_ground")
floor_mat.diffuse_color = (0.085, 0.105, 0.12, 1.0)
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, -0.012))
floor = bpy.context.object
floor.name = "ReviewGround"
floor.data.materials.append(floor_mat)

camera_data = bpy.data.cameras.new("ReviewCamera")
camera = bpy.data.objects.new("ReviewCamera", camera_data)
scene.collection.objects.link(camera)
camera_data.type = "ORTHO"
camera_data.ortho_scale = 2.28
camera.location = (2.8, -5.5, 2.55)
target = Vector((0, -0.12, 0.99))
camera.rotation_euler = (target-camera.location).to_track_quat("-Z", "Y").to_euler()
scene.camera = camera

clips = [("idle", 8), ("move", 8), ("fire", 8)]
frame_number = 0
still_frames = {"idle": 1, "move": 3, "fire": 2}
for clip, count in clips:
    action = actions[clip]
    rig.animation_data.action = action
    start, end = action.frame_range
    sample_count = max(8, count)
    sample_frames = [round(start + (end-start)*i/sample_count) for i in range(sample_count)]
    for i, frame in enumerate(sample_frames):
        scene.frame_set(frame)
        if i == still_frames[clip]:
            scene.render.filepath = str(HERE / f"preview_{clip}.png")
            bpy.ops.render.render(write_still=True)
        scene.render.filepath = str(FRAMES / f"frame_{frame_number:03d}.png")
        bpy.ops.render.render(write_still=True)
        frame_number += 1

gif = HERE / "rifleman_v2_animations.gif"
video = HERE / "rifleman_v2_animations.mp4"
sequence = str(FRAMES / "frame_%03d.png")
subprocess.run([
    "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
    "-framerate", "10", "-i", sequence, "-an", "-c:v", "libx264",
    "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p",
    "-movflags", "+faststart", str(video),
], check=True)
subprocess.run([
    "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
    "-framerate", "10", "-i", sequence,
    "-vf", "fps=10,scale=600:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse",
    "-loop", "0", str(gif),
], check=True)
shutil.rmtree(FRAMES)
print("WROTE", gif, video, "and three clip stills")
