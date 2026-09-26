"""Render preview views of rifleman.glb (roundtrip check). Writes previews.png sheet parts."""
import bpy, os, math, mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(HERE, "rifleman.glb"))

scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.render.resolution_x = 480
scene.render.resolution_y = 480
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'MATERIAL'
scene.display.shading.show_shadows = True

rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH')

# pick an action + frame per view to also proof animations exist
views = [
    ("front", math.radians(0), math.radians(72), "idle", 1),
    ("back", math.radians(180), math.radians(75), "idle", 12),
    ("left", math.radians(90), math.radians(75), "move", 6),
    ("right", math.radians(-90), math.radians(75), "move", 18),
    ("aim34", math.radians(-40), math.radians(70), "fire", 4),
]
print("ACTIONS:", [a.name for a in bpy.data.actions])

for name, yaw, pitch, action, frame in views:
    rig.animation_data.action = bpy.data.actions[action]
    scene.frame_set(frame)
    dist = 3.6
    direction = mathutils.Vector((math.sin(yaw) * math.cos(pitch),
                                  -math.cos(yaw) * math.cos(pitch),
                                  math.sin(pitch)))
    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = direction * dist + mathutils.Vector((0, 0, 1.0))
    # aim at chest height
    aim = mathutils.Vector((0, 0, 1.0)) - cam.location
    cam.rotation_euler = aim.to_track_quat('-Z', 'Y').to_euler()
    cam_data.lens = 60
    scene.camera = cam
    out = os.path.join(HERE, f"preview_{name}.png")
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("WROTE", out)
