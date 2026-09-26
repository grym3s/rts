"""Build a more detailed animated rifleman prototype in Blender.

Run from this folder with:
  blender --background --python build_rifleman_v2.py

Output: rifleman_v2.blend and rifleman_v2.glb in this directory.
Blender is Z-up; the soldier faces -Y; the export is glTF 2.0 with +Y up.
"""

import bpy
import math
import os
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
R = math.radians

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = "METRIC"
scene.render.fps = 24

collection = bpy.data.collections.new("Rifleman_V2")
scene.collection.children.link(collection)


def link(obj):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    collection.objects.link(obj)
    return obj


def material(name, color, metallic=0.0, roughness=0.55, emission=None, strength=0.0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1.0)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1.0)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = roughness
    if emission:
        shader.inputs["Emission Color"].default_value = (*emission, 1.0)
        shader.inputs["Emission Strength"].default_value = strength
    return mat


under = material("undersuit_charcoal", (0.025, 0.038, 0.050), 0.1, 0.82)
armor = material("armor_deep_teal", (0.075, 0.17, 0.205), 0.55, 0.38)
armor_light = material("armor_ceramic_blue", (0.16, 0.29, 0.34), 0.40, 0.38)
armor_mid = material("armor_midnight_blue", (0.035, 0.075, 0.105), 0.62, 0.31)
edge = material("brushed_alloy", (0.19, 0.27, 0.30), 0.72, 0.32)
visor_housing = material("visor_housing", (0.015, 0.045, 0.085), 0.72, 0.20)
visor = material("visor_cobalt_glass", (0.006, 0.10, 0.42), 0.48, 0.16,
                 emission=(0.015, 0.16, 0.85), strength=1.4)
cyan = material("cyan_status_lights", (0.01, 0.32, 0.55), 0.2, 0.25,
                emission=(0.02, 0.45, 1.0), strength=2.1)
gold = material("unit_identification_brass", (0.66, 0.36, 0.10), 0.66, 0.34)
cloth = material("webbing_olive", (0.11, 0.14, 0.105), 0.0, 0.91)
weapon = material("rifle_graphite", (0.027, 0.034, 0.040), 0.72, 0.3)
rubber = material("rubber", (0.018, 0.021, 0.024), 0.02, 0.94)


# The arms are extended into a two-handed low-ready firing stance.
sh_l = Vector((0.245, 0.005, 1.445))
el_l = Vector((0.245, -0.190, 1.285))
hand_l = Vector((0.075, -0.445, 1.285))
sh_r = Vector((-0.245, 0.005, 1.445))
el_r = Vector((-0.180, -0.170, 1.385))
hand_r = Vector((-0.075, -0.215, 1.275))

bones = [
    ("root", (0, 0, 0), (0, 0, 0.45), None, 0),
    ("hips", (0, 0, 0.88), (0, 0, 1.08), "root", 0),
    ("chest", (0, 0, 1.06), (0, 0, 1.54), "hips", 0),
    ("neck", (0, 0, 1.53), (0, 0, 1.67), "chest", 0),
    ("head", (0, 0, 1.66), (0, 0, 1.91), "neck", 0),
    ("arm_l", tuple(sh_l), tuple(el_l), "chest", -90),
    ("arm_r", tuple(sh_r), tuple(el_r), "chest", -90),
    ("fore_l", tuple(el_l), tuple(hand_l), "arm_l", -90),
    ("fore_r", tuple(el_r), tuple(hand_r), "arm_r", -90),
    ("thigh_l", (0.115, 0, 0.91), (0.115, 0, 0.56), "hips", -90),
    ("thigh_r", (-0.115, 0, 0.91), (-0.115, 0, 0.56), "hips", -90),
    ("shin_l", (0.115, 0, 0.56), (0.115, 0, 0.19), "thigh_l", -90),
    ("shin_r", (-0.115, 0, 0.56), (-0.115, 0, 0.19), "thigh_r", -90),
    ("foot_l", (0.115, 0, 0.19), (0.115, -0.15, 0.025), "shin_l", -90),
    ("foot_r", (-0.115, 0, 0.19), (-0.115, -0.15, 0.025), "shin_r", -90),
    ("rifle", (0, -0.28, 1.30), (0, -0.52, 1.30), "chest", 0),
]

arm_data = bpy.data.armatures.new("rifleman_v2_skeleton")
rig = bpy.data.objects.new("Rifleman_Rig", arm_data)
collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
for name, head, tail, parent, roll in bones:
    b = arm_data.edit_bones.new(name)
    b.head, b.tail = Vector(head), Vector(tail)
    b.roll = R(roll)
    if parent:
        b.parent = arm_data.edit_bones[parent]
bpy.ops.object.mode_set(mode="OBJECT")
rig.select_set(False)

parts = []


def finish_part(obj, bone, mat, smooth=False):
    obj = link(obj)
    if bone == "rifle":
        obj.location.z += 0.12
    obj.name = f"{bone}_{mat.name}_{len(parts):03d}"
    obj.data.materials.append(mat)
    if smooth and obj.type == "MESH":
        for poly in obj.data.polygons:
            poly.use_smooth = True
    group = obj.vertex_groups.new(name=bone)
    group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    parts.append((bone, obj))
    return obj


def box(bone, mat, center, size, bevel=0.025, segments=3, rotation=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    obj = bpy.context.object
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if rotation:
        obj.rotation_euler = rotation
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    if bevel > 0:
        mod = obj.modifiers.new("rounded_hard_surface_edges", "BEVEL")
        mod.width, mod.segments = bevel, segments
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
        norm = obj.modifiers.new("weighted_corner_normals", "WEIGHTED_NORMAL")
        bpy.ops.object.modifier_apply(modifier=norm.name)
    return finish_part(obj, bone, mat)


def ellipsoid(bone, mat, center, scale, rotation=None, segments=24, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=1, location=center)
    obj = bpy.context.object
    obj.scale = scale
    if rotation:
        obj.rotation_euler = rotation
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return finish_part(obj, bone, mat, smooth=True)


def cylinder(bone, mat, a, b, radius, vertices=20, radius2=None):
    a, b = Vector(a), Vector(b)
    d = b - a
    if radius2 is None or abs(radius2 - radius) < 1e-5:
        bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=d.length, location=(a+b)/2)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius, radius2=radius2, depth=d.length, location=(a+b)/2)
    obj = bpy.context.object
    obj.rotation_euler = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_euler()
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    bevel = obj.modifiers.new("machined_edges", "BEVEL")
    bevel.width, bevel.segments = min(radius * 0.16, 0.008), 2
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    return finish_part(obj, bone, mat, smooth=True)


def limb(bone, mat, start, end, width, depth, extra=0.0):
    start, end = Vector(start), Vector(end)
    axis = end - start
    obj = ellipsoid(bone, mat, (start+end)/2, (width, depth, axis.length/2+extra),
                    rotation=Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_euler())
    return obj


# Under-suit and armored lower body.
for side, x, thigh, shin, foot in [
    ("l", .115, "thigh_l", "shin_l", "foot_l"),
    ("r", -.115, "thigh_r", "shin_r", "foot_r"),
]:
    limb(thigh, under, (x, 0.015, .91), (x, 0, .57), .082, .105, .045)
    limb(thigh, armor, (x, -.008, .86), (x, -.012, .65), .112, .128, .045)
    box(thigh, armor_light, (x, -.116, .785), (.174, .055, .235), .047)
    box(thigh, armor_mid, (x, -.151, .785), (.104, .018, .105), .024)
    box(thigh, gold, (x, -.164, .81), (.060, .012, .018), .006)
    limb(shin, under, (x, 0, .56), (x, 0, .20), .072, .088, .035)
    limb(shin, armor, (x, -.02, .47), (x, -.035, .27), .096, .105, .04)
    box(shin, armor_light, (x, -.105, .395), (.15, .05, .235), .038)
    box(shin, armor_mid, (x, -.139, .40), (.055, .018, .14), .012)
    box(shin, armor_mid, (x, -.104, .565), (.157, .075, .125), .038)
    box(shin, edge, (x, -.145, .570), (.065, .012, .018), .006, 1)
    box(foot, rubber, (x, -.070, .050), (.205, .345, .070), .032)
    ellipsoid(foot, under, (x, -.025, .135), (.092, .145, .115))
    box(foot, armor, (x, -.145, .132), (.205, .160, .105), .047)
    box(foot, armor_light, (x, -.218, .117), (.132, .032, .055), .018)
    for ridge in range(3):
        box(foot, rubber, (x, -.204 + ridge*.044, .012), (.150, .018, .008), .003, 1)

# Hip section, segmented belt and front buckle.
ellipsoid("hips", under, (0, 0, 1.005), (.225, .145, .16))
box("hips", armor, (0, -.010, .997), (.425, .285, .19), .065)
box("hips", cloth, (0, -.164, 1.006), (.405, .034, .080), .015)
box("hips", edge, (0, -.191, 1.008), (.092, .023, .074), .015)
box("hips", gold, (0, -.205, 1.008), (.040, .009, .043), .008)
for x in (-.178, .178):
    box("hips", armor_mid, (x, -.116, .990), (.075, .075, .125), .020)
    box("hips", cloth, (x, -.158, .997), (.068, .025, .092), .012)

# Torso undersuit and layered chest armor.
ellipsoid("chest", under, (0, .015, 1.335), (.235, .145, .295))
box("chest", armor_mid, (0, .142, 1.375), (.34, .095, .325), .055)
box("chest", armor, (0, -.015, 1.365), (.465, .315, .395), .078)
box("chest", armor_light, (0, -.181, 1.401), (.385, .075, .286), .050)
box("chest", armor, (0, -.224, 1.407), (.286, .025, .202), .035)
box("chest", armor_mid, (0, -.244, 1.407), (.220, .022, .151), .028)
# Three raised chest magazines echo the reference without becoming tiny noise.
for x in (-.137, 0, .137):
    box("chest", armor_mid, (x, -.236, 1.334), (.094, .062, .128), .019)
    box("chest", armor_light, (x, -.274, 1.343), (.074, .018, .095), .012)
    box("chest", gold, (x, -.286, 1.382), (.034, .009, .012), .004, 1)
    box("chest", rubber, (x, -.286, 1.307), (.055, .010, .016), .004, 1)
box("chest", edge, (0, -.225, 1.526), (.30, .045, .045), .018)
box("chest", cyan, (0, -.251, 1.527), (.115, .012, .020), .006, 1)
# Collar and abdominal segmentation.
ellipsoid("chest", armor_mid, (0, 0, 1.575), (.210, .165, .085))
box("chest", edge, (0, -.006, 1.572), (.345, .245, .042), .018)
for z in (1.14, 1.095):
    box("hips", armor_mid, (0, -.131, z), (.244, .040, .025), .010)

# Layered shoulder shells, articulated arm under-suit, gauntlets, and gloves.
for side, shoulder, elbow, hand, arm_bone, fore_bone in [
    ("l", sh_l, el_l, hand_l, "arm_l", "fore_l"),
    ("r", sh_r, el_r, hand_r, "arm_r", "fore_r"),
]:
    limb(arm_bone, under, shoulder, elbow, .073, .080, .035)
    box(arm_bone, armor, (shoulder.x, shoulder.y-.008, shoulder.z+.018), (.292, .252, .190), .058)
    box(arm_bone, armor_light, (shoulder.x, shoulder.y-.139, shoulder.z+.022), (.225, .055, .130), .041)
    box(arm_bone, armor_mid, (shoulder.x, shoulder.y-.171, shoulder.z+.012), (.116, .022, .060), .018)
    box(arm_bone, cyan, (shoulder.x, shoulder.y-.186, shoulder.z+.012), (.043, .010, .018), .006, 1)
    limb(fore_bone, under, elbow, hand, .055, .062, .022)
    axis = (Vector(hand)-Vector(elbow)).normalized()
    center = (Vector(elbow)+Vector(hand))/2
    fore_rot = Vector((0, 0, 1)).rotation_difference(axis).to_euler()
    ellipsoid(fore_bone, armor_mid, center, (.070, .074, (Vector(hand)-Vector(elbow)).length*.57), fore_rot)
    box(fore_bone, armor_light, center + Vector((0, -.057, .008)),
        (.112, .070, (Vector(hand)-Vector(elbow)).length*.66), .025, rotation=fore_rot)
    box(fore_bone, edge, (hand.x, hand.y+.035, hand.z), (.132, .075, .038), .014)
    ellipsoid(fore_bone, rubber, hand, (.061, .071, .052))
    for k in range(3):
        box(fore_bone, armor_mid, (hand.x + (k-1)*.028, hand.y-.037, hand.z-.008), (.020, .050, .028), .008)

# Helmet: domed shell, brow, panoramic blue visor, jaw armor, and side comms.
ellipsoid("head", under, (0, .010, 1.770), (.126, .128, .162))
ellipsoid("head", armor_mid, (0, .012, 1.796), (.158, .169, .170))
ellipsoid("head", armor_light, (0, .025, 1.852), (.143, .151, .119))
box("head", armor, (0, -.113, 1.828), (.238, .076, .050), .023)
box("head", visor_housing, (0, -.148, 1.777), (.257, .070, .105), .042)
ellipsoid("head", visor, (0, -.193, 1.779), (.106, .020, .037), segments=32, rings=16)
box("head", armor_light, (0, -.160, 1.711), (.198, .064, .056), .026)
box("head", armor_mid, (0, -.196, 1.708), (.124, .018, .031), .010)
for x in (-.145, .145):
    ellipsoid("head", armor, (x, .006, 1.780), (.037, .083, .086))
    cylinder("head", edge, (x*1.13, -.026, 1.78), (x*1.13, -.066, 1.78), .023, 16)
    box("head", cyan, (x*1.13, -.070, 1.78), (.018, .010, .030), .006, 1)
box("head", edge, (0, -.071, 1.945), (.095, .080, .025), .012)
box("head", gold, (0, -.116, 1.943), (.046, .018, .012), .004, 1)

# Compact modular backpack with hard shell, side canisters, vents, and beacon.
box("chest", armor_mid, (0, .213, 1.374), (.336, .205, .345), .055)
box("chest", armor, (0, .326, 1.378), (.275, .050, .285), .040)
box("chest", armor_light, (0, .355, 1.390), (.185, .022, .185), .028)
for x in (-.214, .214):
    cylinder("chest", armor, (x, .14, 1.255), (x, .14, 1.49), .054, 20, .045)
    cylinder("chest", edge, (x, .14, 1.458), (x, .14, 1.478), .047, 20)
    box("chest", cyan, (x, .087, 1.397), (.022, .012, .060), .008, 1)
for z in (1.315, 1.355, 1.395, 1.435):
    box("chest", rubber, (0, .381, z), (.155, .014, .012), .004, 1)
cylinder("chest", armor_light, (.116, .316, 1.57), (.116, .316, 1.75), .012, 12, .007)
ellipsoid("chest", cyan, (.116, .316, 1.75), (.018, .018, .018), segments=16, rings=8)

# Long rifle, pointing forward (-Y), with stock, receiver, magazine, optic,
# handguard rails, barrel, muzzle, trigger guard, and restrained blue indicator.
box("rifle", weapon, (-.005, -.328, 1.190), (.112, .370, .104), .023)
box("rifle", armor_mid, (-.005, -.305, 1.247), (.077, .245, .027), .010)
box("rifle", rubber, (-.005, -.105, 1.185), (.094, .145, .081), .024)
box("rifle", armor, (-.005, -.057, 1.176), (.105, .102, .092), .020)
box("rifle", rubber, (-.005, -.347, 1.105), (.062, .134, .148), .018)
box("rifle", rubber, (-.005, -.101, 1.132), (.038, .058, .124), .012, rotation=(R(-12), 0, 0))
cylinder("rifle", weapon, (-.005, -.480, 1.197), (-.005, -.670, 1.197), .018, 20, .014)
cylinder("rifle", edge, (-.005, -.650, 1.197), (-.005, -.681, 1.197), .025, 20, .019)
cylinder("rifle", rubber, (-.005, -.680, 1.197), (-.005, -.700, 1.197), .021, 20, .021)
box("rifle", armor_mid, (-.005, -.265, 1.263), (.078, .080, .050), .012)
box("rifle", visor, (-.005, -.267, 1.291), (.040, .035, .010), .004, 1)
box("rifle", weapon, (-.005, -.285, 1.302), (.042, .075, .024), .009)
for x in (-.053, .043):
    box("rifle", edge, (x, -.325, 1.254), (.010, .245, .012), .003, 1)
for y in (-.405, -.365, -.325, -.285, -.245):
    box("rifle", rubber, (-.005, y, 1.249), (.089, .012, .015), .004, 1)
box("rifle", cyan, (.056, -.290, 1.198), (.010, .042, .017), .004, 1)

# Join the rigidly weighted armor parts into one skinned mesh.
bpy.ops.object.select_all(action="DESELECT")
for _, obj in parts:
    obj.select_set(True)
bpy.context.view_layer.objects.active = parts[0][1]
bpy.ops.object.join()
mesh = bpy.context.object
mesh.name = "Rifleman_V2"
mesh.data.name = "Rifleman_V2_Mesh"
bpy.ops.object.select_all(action="DESELECT")
mesh.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type="ARMATURE_NAME")
weighted = sum(1 for v in mesh.data.vertices if v.groups)
print(f"MESH vertices={len(mesh.data.vertices)} weighted={weighted} groups={len(mesh.vertex_groups)}")
assert weighted == len(mesh.data.vertices)


def keyed_action(name, frame_count, pose_fn):
    action = bpy.data.actions.new(name)
    rig.animation_data_create()
    rig.animation_data.action = action
    pose = rig.pose.bones
    for frame in range(1, frame_count+2):
        t = (frame-1)/frame_count
        for bone in pose:
            bone.rotation_mode = "XYZ"
            bone.location = (0, 0, 0)
            bone.rotation_euler = (0, 0, 0)
        pose_fn(pose, t)
        for bone in pose:
            bone.keyframe_insert("location", frame=frame, group=bone.name)
            bone.keyframe_insert("rotation_euler", frame=frame, group=bone.name)
    # Blender 5 uses layered Actions and no longer exposes Action.fcurves.
    # Keyframe insertion uses Bezier interpolation by default, so leave the
    # generated curves in their native action-slot representation.
    action.use_fake_user = True
    return action


def idle_pose(p, t):
    breath = math.sin(t*math.tau)
    scan = math.sin(t*math.tau + .55)
    p["chest"].rotation_euler.x = R(1.2*breath)
    p["chest"].rotation_euler.z = R(.8*math.sin(t*math.tau))
    p["head"].rotation_euler.z = R(4.0*scan)
    p["head"].rotation_euler.x = R(1.6*math.sin(t*math.tau+.4))
    p["arm_l"].rotation_euler.x = R(-1.8*breath)
    p["arm_r"].rotation_euler.x = R(-1.8*breath)


def move_pose(p, t):
    cycle = math.tau*t
    swing = math.sin(cycle)
    bounce = abs(math.sin(cycle))
    p["hips"].location.z = .026*bounce-.013
    p["hips"].rotation_euler.z = R(-3.0*swing)
    p["chest"].rotation_euler.x = R(-4.5 + 1.5*bounce)
    p["chest"].rotation_euler.z = R(3.5*swing)
    p["head"].rotation_euler.x = R(4.0)
    p["thigh_l"].rotation_euler.y = R(25*swing)
    p["thigh_r"].rotation_euler.y = R(-25*swing)
    p["shin_l"].rotation_euler.y = R(max(0, -34*math.sin(cycle-.65)))
    p["shin_r"].rotation_euler.y = R(max(0, 34*math.sin(cycle-.65)))
    p["foot_l"].rotation_euler.y = R(-8*swing)
    p["foot_r"].rotation_euler.y = R(8*swing)
    p["arm_l"].rotation_euler.x = R(2*swing)
    p["arm_r"].rotation_euler.x = R(2*swing)


def fire_pose(p, t):
    # A clear single-shot recoil: anticipation, sharp kick, settle.
    if t < .18:
        kick = t/.18
    elif t < .46:
        kick = 1-(t-.18)/.28
    else:
        kick = 0.0
    p["chest"].rotation_euler.x = R(-1.5 + 8*kick)
    p["chest"].rotation_euler.z = R(2.0*kick)
    p["head"].rotation_euler.x = R(-4*kick)
    p["arm_l"].rotation_euler.x = R(-5*kick)
    p["arm_r"].rotation_euler.x = R(-8*kick)
    p["fore_l"].rotation_euler.z = R(2.5*kick)
    p["fore_r"].rotation_euler.z = R(-5*kick)
    p["hips"].location.z = -.024*kick
    p["rifle"].location.y = .065*kick
    p["rifle"].rotation_euler.x = R(-9*kick)


actions = [
    keyed_action("idle", 48, idle_pose),
    keyed_action("move", 32, move_pose),
    keyed_action("fire", 24, fire_pose),
]
rig.animation_data.action = actions[0]
scene.frame_start, scene.frame_end = 1, 48

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "rifleman_v2.blend"))
bpy.ops.object.select_all(action="DESELECT")
mesh.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.gltf(
    filepath=os.path.join(HERE, "rifleman_v2.glb"),
    export_format="GLB", use_selection=True, export_apply=False,
    export_yup=True, export_animations=True, export_skins=True,
    export_materials="EXPORT", export_extras=False,
)
print("WROTE", os.path.join(HERE, "rifleman_v2.blend"), "and rifleman_v2.glb")
