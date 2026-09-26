"""Create a clearly labeled, non-destructive material-study GLB from the shape mesh."""
import bpy
from mathutils import Vector
from pathlib import Path

HERE = Path(__file__).resolve().parent
SRC = HERE / 'rifleman-hunyuan-shape.glb'
OUT = HERE / 'rifleman-hunyuan-material-study.glb'
RENDER = HERE / 'inspection-material-study.png'
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(SRC))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if len(meshes) != 1:
    raise RuntimeError(f'Expected one fused mesh; found {len(meshes)}')
obj = meshes[0]
body = bpy.data.materials.new('Manual review: dark gunmetal')
body.diffuse_color = (0.09, 0.11, 0.14, 1)
body.use_nodes = True
bsdf = body.node_tree.nodes.get('Principled BSDF')
bsdf.inputs['Base Color'].default_value = (0.075, 0.09, 0.115, 1)
bsdf.inputs['Metallic'].default_value = 0.58
bsdf.inputs['Roughness'].default_value = 0.42
visor = bpy.data.materials.new('Manual review: blue visor region')
visor.diffuse_color = (0.0, 0.12, 0.95, 1)
visor.use_nodes = True
vbsdf = visor.node_tree.nodes.get('Principled BSDF')
vbsdf.inputs['Base Color'].default_value = (0.0, 0.025, 0.18, 1)
vbsdf.inputs['Metallic'].default_value = 0.32
vbsdf.inputs['Roughness'].default_value = 0.22
if 'Emission Color' in vbsdf.inputs:
    vbsdf.inputs['Emission Color'].default_value = (0.0, 0.04, 0.8, 1)
    vbsdf.inputs['Emission Strength'].default_value = 2.5
elif 'Emission' in vbsdf.inputs:
    vbsdf.inputs['Emission'].default_value = (0.0, 0.04, 0.8, 1)
obj.data.materials.clear()
obj.data.materials.append(body)
obj.data.materials.append(visor)
# Heuristic face-region assignment for a visual study only. It deliberately
# does not edit the source GLB or claim to be a finished UV texture.
selected = 0
mw = obj.matrix_world
normal_matrix = mw.to_3x3()
for poly in obj.data.polygons:
    c = mw @ poly.center
    n = (normal_matrix @ poly.normal).normalized()
    front_facing = n.y < -0.18
    if front_facing and c.y < 0.10 and 0.70 < c.z < 0.84 and abs(c.x) < 0.25:
        poly.material_index = 1
        selected += 1
print(f'MATERIAL_STUDY blue visor polygons={selected}')
if selected < 5:
    raise RuntimeError('Could not identify the front helmet visor region; refusing to export study')

# CPU Eevee studio render.
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 24
scene.render.resolution_x = scene.render.resolution_y = 900
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.world.color = (0.018, 0.022, 0.03)
coords = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
lo = Vector(tuple(min(v[i] for v in coords) for i in range(3)))
hi = Vector(tuple(max(v[i] for v in coords) for i in range(3)))
center = (lo + hi) / 2
size = hi - lo

def area(name, offset, energy, color, size):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy, data.color, data.shape, data.size = energy, color, 'DISK', size
    light = bpy.data.objects.new(name, data)
    scene.collection.objects.link(light)
    light.location = center + Vector(offset)
    light.rotation_euler = (center - light.location).to_track_quat('-Z', 'Y').to_euler()

area('Key', (3,-4,5), 1100, (0.82,0.88,1.0), 4)
area('Fill', (-4,-2,1), 650, (0.42,0.58,1.0), 3)
area('Rim', (1,4,3), 1400, (0.26,0.48,1.0), 3)
cam_data = bpy.data.cameras.new('ReviewCamera')
cam = bpy.data.objects.new('ReviewCamera', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.type = 'ORTHO'
cam.data.ortho_scale = max(size) * 1.16
cam.location = center + Vector((0,-1,0)) * max(size) * 2.8
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()
scene.render.filepath = str(RENDER)
bpy.ops.render.render(write_still=True)
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.gltf(filepath=str(OUT), export_format='GLB', use_selection=True)
print('WROTE', OUT, RENDER)
