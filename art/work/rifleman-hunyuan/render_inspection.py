import bpy, math
from mathutils import Vector
from pathlib import Path

HERE = Path(__file__).resolve().parent
src = HERE / 'rifleman-hunyuan-shape.glb'
# Fresh scene; this is only a preview render, it does not alter the GLB.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(src))
mesh_objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not mesh_objects:
    raise RuntimeError('No mesh objects imported from GLB')
coords = [o.matrix_world @ Vector(corner) for o in mesh_objects for corner in o.bound_box]
lo = Vector(tuple(min(v[i] for v in coords) for i in range(3)))
hi = Vector(tuple(max(v[i] for v in coords) for i in range(3)))
center = (lo + hi) / 2
size = hi - lo
print(f'IMPORT mesh_objects={len(mesh_objects)} bounds_min={tuple(lo)} bounds_max={tuple(hi)} extents={tuple(size)}')

# Render-only neutral gunmetal material, for geometry readability.
mat = bpy.data.materials.new('Inspection gunmetal (render only)')
mat.diffuse_color = (0.28, 0.34, 0.42, 1)
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get('Principled BSDF')
bsdf.inputs['Base Color'].default_value = (0.20, 0.27, 0.36, 1)
bsdf.inputs['Metallic'].default_value = 0.55
bsdf.inputs['Roughness'].default_value = 0.36
for obj in mesh_objects:
    obj.data.materials.clear()
    obj.data.materials.append(mat)

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 20
scene.render.resolution_x = 800
scene.render.resolution_y = 800
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
scene.world.color = (0.025, 0.03, 0.04)

# Three-point studio lights.
def area(name, loc, power, color, size):
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = power
    data.color = color
    data.shape = 'DISK'
    data.size = size
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = (center - obj.location).to_track_quat('-Z', 'Y').to_euler()
    return obj
area('Key', center + Vector((3,-4,5)), 950, (0.78,0.86,1.0), 4)
area('Fill', center + Vector((-4,-2,1)), 600, (0.46,0.62,1.0), 3)
area('Rim', center + Vector((1,4,3)), 1100, (0.32,0.56,1.0), 3)

cam_data = bpy.data.cameras.new('InspectionCamera')
cam = bpy.data.objects.new('InspectionCamera', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.type = 'ORTHO'
cam.data.ortho_scale = max(size) * 1.18

def render(name, direction):
    direction = Vector(direction).normalized()
    cam.location = center + direction * (max(size) * 2.8)
    cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(HERE / f'{name}.png')
    bpy.ops.render.render(write_still=True)

render('inspection-front', (0,-1,0))
render('inspection-side', (1,0,0))
render('inspection-elevated', (4,-7,4))
print('RENDERED', HERE)
