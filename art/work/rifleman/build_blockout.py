"""Rifleman blockout: named-part humanoid from primitives, ~1.8 m, origin at feet.

Run: ~/blender-env/.venv/bin/python build_blockout.py
No image-to-3D generator is available locally (see manifest provenance), so this
is an honest manual blockout inspired by art/references/armoured-infantry-reference.jpg.
"""
import bpy, math, os

OUT = os.path.dirname(os.path.abspath(__file__))
ASSET_ID = os.environ.get("RTS_ASSET_ID", "rifleman")

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
meshes = bpy.data.meshes
objs = bpy.data.objects

def part(name, prim, loc, scale, rot=(0, 0, 0), verts=16):
    if prim == "cube":
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    elif prim == "cyl":
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=0.5, depth=1, location=loc)
    elif prim == "sph":
        bpy.ops.mesh.primitive_uv_sphere_add(segments=verts, ring_count=verts // 2, radius=0.5, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    o.rotation_euler = rot
    return o

# Coalition-ish infantry blockout. Z-up, metres, feet at Z=0, facing +Y.
parts = [
    part("legs",        "cube", (0, 0, 0.42),  (0.34, 0.24, 0.84)),
    part("torso",       "cube", (0, 0, 1.12),  (0.52, 0.32, 0.62)),
    part("chest_plate", "cube", (0, 0.17, 1.16), (0.46, 0.10, 0.44)),
    part("shoulder_l",  "cube", (-0.36, 0, 1.32), (0.18, 0.30, 0.22)),
    part("shoulder_r",  "cube", ( 0.36, 0, 1.32), (0.18, 0.30, 0.22)),
    part("arm_l",       "cube", (-0.36, -0.02, 0.92), (0.13, 0.13, 0.50)),
    part("arm_r",       "cube", ( 0.36, -0.02, 0.92), (0.13, 0.13, 0.50)),
    part("helmet",      "sph",  (0, 0.02, 1.66), (0.30, 0.34, 0.30)),
    part("visor",       "cube", (0, 0.17, 1.66), (0.24, 0.06, 0.10)),
    part("backpack",    "cube", (0, -0.24, 1.14), (0.34, 0.16, 0.42)),
]
# Rifle held across the chest, muzzle toward +Y.
rifle = part("rifle", "cube", (0.22, 0.28, 1.18), (0.07, 0.72, 0.09), rot=(math.radians(-12), 0, 0))

# Join into a single 'rifleman' mesh, keep it editable.
bpy.ops.object.select_all(action="DESELECT")
for p in parts + [rifle]:
    p.select_set(True)
bpy.context.view_layer.objects.active = parts[1]
bpy.ops.object.join()
unit = bpy.context.active_object
unit.name = ASSET_ID

# Origin at feet-center.
bpy.context.scene.cursor.location = (0, 0, 0)
bpy.ops.object.origin_set(type="ORIGIN_CURSOR")

# Two flat materials: armour body + accent visor. Separate visor back out as its own object.
mat_body = bpy.data.materials.new(f"{ASSET_ID}_armour")
mat_body.diffuse_color = (0.28, 0.32, 0.36, 1)
mat_accent = bpy.data.materials.new(f"{ASSET_ID}_visor")
mat_accent.diffuse_color = (0.1, 0.45, 0.9, 1)
unit.data.materials.append(mat_body)

bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="DESELECT")
bpy.ops.object.mode_set(mode="OBJECT")

# Origin already at world Z=0, unit faces +Y. Save editable blend.
blend = os.path.join(OUT, f"{ASSET_ID}.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend)

# glb export (y-up is handled by the exporter default).
glb = os.path.join(OUT, "..", "..", "export", ASSET_ID, f"{ASSET_ID}.glb")
glb = os.path.abspath(glb)
os.makedirs(os.path.dirname(glb), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_apply=True,
                          export_yup=True, export_materials="EXPORT")

print("BLEND", blend)
print("GLB", glb, os.path.getsize(glb), "bytes")

# Roundtrip: re-import the glb into a fresh factory scene and report bounds.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
imp = [o for o in bpy.context.scene.objects if o.type == "MESH"]
print("reimported meshes:", [o.name for o in imp])
if imp:
    o = imp[0]
    xs = [ (o.matrix_world @ v.co) for v in o.data.vertices ]
    zs = [p.z for p in xs]; ys=[p.y for p in xs]
    print("height_m", round(max(zs)-min(zs),3), "min_z", round(min(zs),3), "depth_y", round(max(ys)-min(ys),3))
