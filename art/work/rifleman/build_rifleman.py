"""Build the coalition rifleman: skinned humanoid mesh + armature + idle/move/fire actions.

Run: blender --background --python build_rifleman.py
Outputs (next to this file): rifleman.blend, rifleman.glb

Authoring is fully procedural (no external references). The silhouette follows
art/references/armoured-infantry-reference.jpg: squat armoured infantry, domed
helmet with green visor slit, backpack, short rifle. Original design.

Coordinate contract:
  - Blender Z-up, origin at feet, unit faces -Y.
  - Bone convention: limbs point down (-Z); with roll -90 the local +Y axis is
    the pitch/swing axis (positive = limb swings forward/-Y), local Z is twist.
    Torso bones point up; local X pitches (positive = lean forward), local Z yaws.
  - glTF export +Y up: Godot sees forward = -Z (unit's -Y Blender), feet at y=0.
  - Single skinned mesh 'Rifleman'; one vertex group per bone, whole-part weights.
  - Actions: idle / move / fire (looping clips at 24 fps).
"""

import bpy
import math
import os
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'

COLLECTION = bpy.data.collections.new("Rifleman")
bpy.context.scene.collection.children.link(COLLECTION)


def link(obj):
    for c in obj.users_collection:
        c.objects.unlink(obj)
    COLLECTION.objects.link(obj)
    return obj


# ---------------------------------------------------------------- materials
def pbr(name, color, metallic=0.0, rough=0.75, emit=None, emit_strength=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = rough
    if emit is not None:
        bsdf.inputs["Emission Color"].default_value = (*emit, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emit_strength
    return mat


mat_armor = pbr("coalition_armor", (0.16, 0.32, 0.31), metallic=0.35, rough=0.55)
mat_plate = pbr("coalition_plate", (0.22, 0.41, 0.38), metallic=0.45, rough=0.45)
mat_fabric = pbr("coalition_fabric", (0.11, 0.15, 0.14), metallic=0.0, rough=0.92)
mat_dark = pbr("equipment_dark", (0.035, 0.04, 0.045), metallic=0.2, rough=0.6)
mat_visior = pbr("visor_glow", (0.05, 0.08, 0.07), metallic=0.1, rough=0.15,
                 emit=(0.35, 1.0, 0.75), emit_strength=2.5)
mat_gun = pbr("rifle_body", (0.05, 0.05, 0.055), metallic=0.5, rough=0.4)
mat_brass = pbr("insignia_brass", (0.65, 0.45, 0.14), metallic=0.8, rough=0.35)

# ---------------------------------------------------------------- skeleton
SHOULDER_L = Vector((0.235, 0.010, 1.285))
SHOULDER_R = Vector((-0.235, 0.010, 1.285))
ELBOW_L = SHOULDER_L + Vector((0.03, -0.04, -0.28))
ELBOW_R = SHOULDER_R + Vector((-0.03, -0.04, -0.28))
HAND_L = ELBOW_L + Vector((0.01, -0.10, -0.16))
HAND_R = ELBOW_R + Vector((-0.01, -0.10, -0.16))
RIFLE_ANCHOR = (HAND_L + HAND_R) / 2 + Vector((0.02, -0.02, -0.03))

# name, head, tail, parent, roll_deg (roll -90 for down-limbs -> local Y = pitch fwd)
BONES = [
    ("root", (0, 0, 0), (0, -0.25, 0), None, 0),
    ("hips", (0, 0, 0.95), (0, 0, 1.12), "root", 0),
    ("chest", (0, 0, 1.22), (0, 0, 1.60), "hips", 0),
    ("neck", (0, 0.010, 1.60), (0, 0.010, 1.70), "chest", 0),
    ("head", (0, 0.008, 1.70), (0, 0.008, 1.95), "chest", 0),
    ("arm_l", SHOULDER_L, ELBOW_L, "chest", -90),
    ("arm_r", SHOULDER_R, ELBOW_R, "chest", -90),
    ("fore_l", ELBOW_L, HAND_L, "arm_l", -90),
    ("fore_r", ELBOW_R, HAND_R, "arm_r", -90),
    ("thigh_l", (0.11, 0, 0.94), (0.11, 0, 0.58), "hips", -90),
    ("thigh_r", (-0.11, 0, 0.94), (-0.11, 0, 0.58), "hips", -90),
    ("shin_l", (0.11, 0, 0.56), (0.11, -0.02, 0.20), "thigh_l", -90),
    ("shin_r", (-0.11, 0, 0.56), (-0.11, -0.02, 0.20), "thigh_r", -90),
    ("foot_l", (0.11, -0.02, 0.19), (0.11, -0.16, 0.02), "shin_l", -90),
    ("foot_r", (-0.11, -0.02, 0.19), (-0.11, -0.16, 0.02), "shin_r", -90),
    ("rifle", RIFLE_ANCHOR + Vector((0, -0.16, 0)), RIFLE_ANCHOR + Vector((0, 0.22, 0.02)), "fore_r", 0),
]

arm_data = bpy.data.armatures.new("rifleman_arm")
rig = bpy.data.objects.new("Armature", arm_data)
link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
for name, head, tail, parent, roll in BONES:
    b = arm_data.edit_bones.new(name)
    b.head, b.tail = Vector(head), Vector(tail)
    b.roll = math.radians(roll)
    if parent:
        b.parent = arm_data.edit_bones[parent]
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------------------------------------------------------- parts
# (bone, material, primitive, args, world_centre, tag)
# primitive: 'box'(dx,dy,dz) | 'cyl'(r, depth, verts) | 'ico'(r)
PARTS = [
    ("thigh_l", mat_plate, "box", (0.15, 0.17, 0.36), (0.11, 0, 0.78), None),
    ("thigh_r", mat_plate, "box", (0.15, 0.17, 0.36), (-0.11, 0, 0.78), None),
    ("shin_l", mat_fabric, "box", (0.13, 0.15, 0.34), (0.11, 0, 0.40), None),
    ("shin_r", mat_fabric, "box", (0.13, 0.15, 0.34), (-0.11, 0, 0.40), None),
    ("foot_l", mat_dark, "box", (0.14, 0.26, 0.10), (0.11, -0.045, 0.05), None),
    ("foot_r", mat_dark, "box", (0.14, 0.26, 0.10), (-0.11, -0.045, 0.05), None),
    ("foot_l", mat_plate, "box", (0.145, 0.10, 0.13), (0.11, -0.115, 0.115), None),
    ("foot_r", mat_plate, "box", (0.145, 0.10, 0.13), (-0.11, -0.115, 0.115), None),
    ("hips", mat_fabric, "box", (0.30, 0.21, 0.16), (0, 0.010, 1.03), "bevel025"),
    ("chest", mat_armor, "box", (0.38, 0.25, 0.30), (0, 0.0, 1.40), "bevel04"),
    ("chest", mat_plate, "box", (0.34, 0.10, 0.24), (0, -0.135, 1.40), "bevel03"),
    ("hips", mat_dark, "box", (0.32, 0.22, 0.07), (0, 0.0, 0.965), None),
    ("hips", mat_brass, "box", (0.07, 0.02, 0.06), (0, -0.125, 0.965), None),
    ("chest", mat_dark, "box", (0.26, 0.16, 0.30), (0, 0.185, 1.43), "bevel03"),
    ("chest", mat_visior, "box", (0.18, 0.02, 0.025), (0, 0.268, 1.50), None),
    ("neck", mat_fabric, "cyl", (0.06, 0.09, 12), (0, 0.010, 1.65), None),
    ("head", mat_armor, "ico", (0.115,), (0, 0.008, 1.79), None),
    ("head", mat_plate, "ico", (0.135,), (0, 0.006, 1.82), "helm"),
    ("head", mat_brass, "box", (0.025, 0.20, 0.035), (0, -0.005, 1.945), None),
    ("head", mat_visior, "box", (0.16, 0.05, 0.055), (0, -0.108, 1.78), "visor"),
    ("arm_l", mat_fabric, "box", (0.10, 0.11, 0.26), (SHOULDER_L + ELBOW_L) / 2, None),
    ("arm_r", mat_fabric, "box", (0.10, 0.11, 0.26), (SHOULDER_R + ELBOW_R) / 2, None),
    ("fore_l", mat_plate, "box", (0.095, 0.20, 0.10), (ELBOW_L + HAND_L) / 2, None),
    ("fore_r", mat_plate, "box", (0.095, 0.20, 0.10), (ELBOW_R + HAND_R) / 2, None),
    ("rifle", mat_gun, "box", (0.05, 0.42, 0.075), RIFLE_ANCHOR, "rifle"),
    ("rifle", mat_gun, "box", (0.04, 0.09, 0.13), RIFLE_ANCHOR + Vector((0, 0.10, -0.10)), "rifle"),
    ("rifle", mat_dark, "box", (0.045, 0.14, 0.09), RIFLE_ANCHOR + Vector((0, 0.28, 0.01)), "rifle"),
    ("rifle", mat_dark, "box", (0.03, 0.06, 0.05), RIFLE_ANCHOR + Vector((0, -0.13, 0.05)), "rifle"),
]

created = []
for bone_name, material, primitive, args, centre, tag in PARTS:
    o = None
    if primitive == "box":
        bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, 0))
        o = bpy.context.active_object
        o.scale = args
        bpy.ops.object.transform_apply(scale=True)
        if tag in ("bevel025", "bevel04", "bevel03"):
            bev = o.modifiers.new("bevel", 'BEVEL')
            bev.width = {"bevel025": 0.025, "bevel04": 0.04, "bevel03": 0.03}[tag]
            bev.segments = 2
            bpy.ops.object.modifier_apply(modifier=bev.name)
    elif primitive == "cyl":
        bpy.ops.mesh.primitive_cylinder_add(radius=args[0], depth=args[1], vertices=args[2], location=(0, 0, 0))
        o = bpy.context.active_object
    elif primitive == "ico":
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=args[0], location=(0, 0, 0))
        o = bpy.context.active_object
        bpy.ops.object.shade_smooth()
        if tag == "helm":
            o.scale = (1.0, 1.06, 0.95)
            bpy.ops.object.transform_apply(scale=True)
    assert o is not None, primitive
    if tag in ("visor", "rifle"):
        o.rotation_euler = (math.radians(-8 if tag == "visor" else -6), 0, 0)
        bpy.ops.object.transform_apply(rotation=True)
    o.location = centre
    bpy.ops.object.transform_apply(location=True)
    o.data.materials.append(material)
    vg = o.vertex_groups.new(name=bone_name)
    vg.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
    created.append((bone_name, o))

# ---------------------------------------------------------------- join + bind
bpy.ops.object.select_all(action='DESELECT')
for _, o in created:
    o.select_set(True)
bpy.context.view_layer.objects.active = created[0][1]
bpy.ops.object.join()
mesh_obj = bpy.context.active_object
mesh_obj.name = "Rifleman"
mesh_obj.data.name = "Rifleman"

bpy.ops.object.select_all(action='DESELECT')
mesh_obj.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type='ARMATURE_NAME')  # keep our exact per-part groups

weighted = sum(1 for v in mesh_obj.data.vertices if v.groups)
print(f"SKIN groups={len(mesh_obj.vertex_groups)} weighted={weighted} meshverts={len(mesh_obj.data.vertices)}")
assert weighted == len(mesh_obj.data.vertices), "skinning lost vertices on bind"

# ---------------------------------------------------------------- actions
scene = bpy.context.scene
scene.render.fps = 24
rig.animation_data_create()
R = math.radians


def bake(name, duration_frames, pose_fn):
    act = bpy.data.actions.new(name)
    rig.animation_data.action = act
    pose = rig.pose.bones
    for f in range(1, duration_frames + 2):  # extra frame == frame 1 (seamless loop)
        t = ((f - 1) % duration_frames) / duration_frames
        for b in pose:
            b.location = (0, 0, 0)
            b.rotation_mode = 'XYZ'
            b.rotation_euler = (0, 0, 0)
        pose_fn(pose, t)
        for b in pose:
            b.keyframe_insert("location", frame=f)
            b.keyframe_insert("rotation_euler", frame=f)
    act.use_fake_user = True
    return act


def pose_idle(p, t):
    breathe = math.sin(t * 2 * math.pi)
    p["chest"].rotation_euler = (R(1.2 * breathe), 0, R(0.8 * math.sin(t * math.pi)))
    p["head"].rotation_euler = (R(1.5 * math.sin(t * 2 * math.pi + 0.7)), 0,
                                R(2.0 * math.sin(t * math.pi)))
    p["arm_l"].rotation_euler = (R(-3), 0, R(-4 + 1.2 * breathe))
    p["arm_r"].rotation_euler = (R(-3), 0, R(4 - 1.2 * breathe))


def pose_move(p, t):
    cyc = t * 2 * math.pi
    swing = math.sin(cyc)
    p["hips"].location = (0, 0, 0.022 * abs(math.sin(cyc)) - 0.011)
    p["hips"].rotation_euler = (0, 0, R(-4 * swing))
    p["chest"].rotation_euler = (R(-6), 0, R(4 * swing))
    p["head"].rotation_euler = (R(6), 0, 0)
    p["thigh_l"].rotation_euler = (0, R(32 * swing), 0)
    p["thigh_r"].rotation_euler = (0, R(-32 * swing), 0)
    p["shin_l"].rotation_euler = (0, R(max(0.0, -42 * math.sin(cyc - 0.9))), 0)
    p["shin_r"].rotation_euler = (0, R(max(0.0, 42 * math.sin(cyc - 0.9))), 0)
    p["arm_l"].rotation_euler = (0, R(-26 * swing), R(-5))
    p["arm_r"].rotation_euler = (0, R(26 * swing), R(5))


def pose_fire(p, t):
    kick = t / 0.15 if t < 0.15 else max(0.0, 1.0 - (t - 0.15) / 0.5)
    p["arm_l"].rotation_euler = (0, R(-58 - 6 * kick), R(-26))
    p["fore_l"].rotation_euler = (0, R(-42), R(8))
    p["arm_r"].rotation_euler = (0, R(-52 - 10 * kick), R(24 + 12 * kick))
    p["fore_r"].rotation_euler = (0, R(-38), R(-14 - 6 * kick))
    p["chest"].rotation_euler = (R(2 * kick), 0, R(4 * kick))
    p["head"].rotation_euler = (R(-3 * kick), 0, 0)
    p["hips"].location = (0, 0, -0.015 * kick)


actions = {
    "idle": bake("idle", 48, pose_idle),
    "move": bake("move", 24, pose_move),
    "fire": bake("fire", 24, pose_fire),
}
scene.frame_start = 1
scene.frame_end = 48
rig.animation_data.action = actions["idle"]

# ---------------------------------------------------------------- save + export
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "rifleman.blend"))

bpy.ops.object.select_all(action='DESELECT')
mesh_obj.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.gltf(
    filepath=os.path.join(HERE, "rifleman.glb"),
    export_format='GLB',
    use_selection=True,
    export_apply=False,
    export_yup=True,
    export_animations=True,
    export_skins=True,
    export_materials='EXPORT',
    export_extras=False,
)
print("WROTE", os.path.join(HERE, "rifleman.blend"), "+ rifleman.glb")
