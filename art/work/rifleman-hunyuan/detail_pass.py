"""CPU-only armor/detail pass over the Hunyuan silhouette; keeps its source intact."""
import bpy, math
from mathutils import Vector
from pathlib import Path

HERE = Path(__file__).resolve().parent
SRC = HERE / 'rifleman-hunyuan-shape.glb'
OUT = HERE / 'rifleman-hunyuan-detail-study.glb'
RENDER = HERE / 'inspection-detail-study.png'
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(SRC))
base = next(o for o in bpy.context.scene.objects if o.type == 'MESH')

# Materials are deliberately modeled as separate surface parts, not a generated texture.
def mat(name, color, metallic=0.5, rough=0.38, emission=None, strength=0.0):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    n=m.node_tree.nodes.get('Principled BSDF')
    n.inputs['Base Color'].default_value=(*color,1)
    n.inputs['Metallic'].default_value=metallic
    n.inputs['Roughness'].default_value=rough
    if emission:
        if 'Emission Color' in n.inputs: n.inputs['Emission Color'].default_value=(*emission,1)
        elif 'Emission' in n.inputs: n.inputs['Emission'].default_value=(*emission,1)
        n.inputs['Emission Strength'].default_value=strength
    return m
bodymat=mat('Armor | charcoal gunmetal',(0.075,0.09,0.115),.68,.34)
plate_dark=mat('Armor panels | blue steel',(0.12,0.16,0.205),.72,.31)
plate_light=mat('Armor inlays | titanium blue-grey',(0.30,0.37,0.43),.62,.34)
rubber=mat('Joint seals | black elastomer',(0.018,0.025,0.033),.12,.65)
blue=mat('Optics | electric blue',(0.006,0.035,0.22),.35,.19,(0.008,0.08,1.0),3.2)
blue_dim=mat('Indicators | cyan',(0.01,0.12,0.27),.35,.22,(0.0,0.23,0.95),2.0)
base.data.materials.clear(); base.data.materials.append(bodymat)

# Extruded, chamferable plate from a front-view polygon. Front faces point toward -Y.
def panel(name, outline, y, thickness, material, bevel=.012):
    n=len(outline)
    verts=[(x,y,z) for x,z in outline]+[(x,y+thickness,z) for x,z in outline]
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]
    for i in range(n):
        j=(i+1)%n; faces.append((i,j,n+j,n+i))
    mesh=bpy.data.meshes.new(name+'Mesh'); mesh.from_pydata(verts,[],faces); mesh.update()
    obj=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(obj); obj.data.materials.append(material)
    bpy.context.view_layer.objects.active=obj; obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.normals_make_consistent(inside=False); bpy.ops.object.mode_set(mode='OBJECT'); obj.select_set(False)
    if bevel:
        mod=obj.modifiers.new('Machined edge bevel','BEVEL'); mod.width=bevel; mod.segments=3; mod.limit_method='ANGLE'
        mod=obj.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL'); mod.keep_sharp=True
    return obj

def chamfer_rect(cx,cz,w,h,cut=.035):
    x0,x1=cx-w/2,cx+w/2; z0,z1=cz-h/2,cz+h/2
    return [(x0+cut,z0),(x1-cut,z0),(x1,z0+cut),(x1,z1-cut),(x1-cut,z1),(x0+cut,z1),(x0,z1-cut),(x0,z0+cut)]

def curve(name, points, radius, material):
    data=bpy.data.curves.new(name,'CURVE'); data.dimensions='3D'; data.bevel_depth=radius; data.bevel_resolution=3
    sp=data.splines.new('POLY'); sp.points.add(len(points)-1)
    for p,co in zip(sp.points,points): p.co=(*co,1)
    ob=bpy.data.objects.new(name,data); bpy.context.collection.objects.link(ob); ob.data.materials.append(material); return ob

def cylinder_front(name, center, radius, depth, material, vertices=32):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=center, rotation=(math.pi/2,0,0))
    o=bpy.context.object; o.name=name; o.data.materials.append(material)
    b=o.modifiers.new('Soft rim','BEVEL'); b.width=min(.009,radius*.18); b.segments=2
    o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return o

# Helmet: a curved visor insert, dark gasket, layered brow, and side optics.
# The shape base's head front is near y=-0.04 at visor height.
xc=-.085; zc=.775; halfw=.225; halfh=.073; yv=-.075
# Slightly bowed glass silhouette, with clipped upper/lower corners.
visor_outline=[(xc-halfw+.035,zc+halfh),(xc+halfw-.035,zc+halfh),(xc+halfw,zc+halfh-.025),
               (xc+halfw-.012,zc-halfh+.018),(xc+halfw-.045,zc-halfh),(xc-halfw+.045,zc-halfh),
               (xc-halfw+.012,zc-halfh+.018),(xc-halfw,zc+halfh-.025)]
panel('Visor gasket',visor_outline,yv-.012,.014,rubber,.008)
inner=[(x*0.985+xc*.015, zc+(z-zc)*.82) for x,z in visor_outline]
panel('Blue wraparound visor',inner,yv-.020,.009,blue,.004)
# bright central reflection line and cheek-side glass facets
curve('Visor reflected streak',[(xc-.15,yv-.031,zc+.024),(xc-.04,yv-.031,zc+.034),(xc+.08,yv-.031,zc+.029)],.006,blue_dim)
# Brow armor sits above the lens and gives it a pronounced mechanical hood.
panel('Helmet brow plate',[(xc-.24,.858),(xc-.19,.879),(xc+.16,.879),(xc+.235,.851),(xc+.205,.836),(xc-.20,.842)],-.045,.045,plate_dark,.016)
# crown segmentation and forehead fasteners
panel('Helmet crown center',[(xc-.105,.93),(xc-.08,.969),(xc+.08,.969),(xc+.115,.93),(xc+.075,.908),(xc-.075,.908)],.005,.025,plate_dark,.012)
for sx in (-1,1):
    x=xc+sx*.257
    cylinder_front('Helmet side optic housing',(x,-.028,.744),.043,.028,plate_dark)
    cylinder_front('Blue helmet side optic',(x,-.049,.744),.022,.012,blue)
    # a small cheek guard edge under each side of the visor
    panel('Helmet cheek guard',[(x-sx*.035,.715),(x+sx*.035,.715),(x+sx*.052,.666),(x,.646),(x-sx*.05,.672)],-.047,.034,plate_dark,.01)

# Shoulder shells, stepped inset faces, and the reference's pale slash markings.
for side,cx in [('L',-.36),('R',.245)]:
    shell=[(cx-.17,.485),(cx-.125,.565),(cx-.045,.596),(cx+.115,.565),(cx+.17,.50),(cx+.125,.432),(cx-.015,.414),(cx-.13,.44)]
    panel(side+' shoulder shell',shell,-.052,.060,plate_dark,.021)
    inset=[(cx-.118,.485),(cx-.083,.535),(cx+.078,.533),(cx+.125,.495),(cx+.09,.453),(cx-.02,.444),(cx-.10,.461)]
    panel(side+' shoulder inset',inset,-.070,.018,bodymat,.012)
    # angled pale armor stripe, deliberately narrow and flush to the shoulder.
    stripe=[(cx-.112,.540),(cx-.070,.552),(cx+.092,.457),(cx+.057,.443)]
    panel(side+' shoulder ivory-blue stripe',stripe,-.091,.012,plate_light,.004)
    # Shoulder-mounted blue lens and retaining bezel.
    cylinder_front(side+' shoulder beacon ring',(cx+(-.123 if side=='L' else .123),-.073,.523),.049,.027,rubber)
    cylinder_front(side+' shoulder beacon',(cx+(-.123 if side=='L' else .123),-.093,.523),.031,.012,blue)
    # exposed segmented armoured upper-arm and forearm plates
    acx=cx+(-.035 if side=='L' else .045)
    panel(side+' upper arm plate',chamfer_rect(acx,.315,.19,.19,.045),-.11,.045,plate_dark,.016)
    panel(side+' forearm guard',[(acx-.095,.205),(acx-.06,.267),(acx+.06,.26),(acx+.105,.20),(acx+.075,.105),(acx-.065,.105),(acx-.105,.15)],-.205,.045,plate_dark,.016)
    for zz in (.157,.183):
        curve(side+' gauntlet seam '+str(zz),[(acx-.065,-.254,zz),(acx+.065,-.254,zz)],.0035,plate_light)

# Three-piece chest armor with layered edges, inset panels, and blue status lights.
chest_centers=(-.275,-.085,.105)
for i,cx in enumerate(chest_centers):
    top=.493-(.015 if i==0 else 0)
    outline=[(cx-.092,top),(cx-.067,top+.035),(cx+.064,top+.035),(cx+.098,top-.006),
             (cx+.082,top-.172),(cx+.048,top-.198),(cx-.062,top-.192),(cx-.099,top-.155)]
    panel('Chest plate '+str(i+1),outline,-.135,.047,plate_dark,.014)
    inset=[(cx-.058,top-.025),(cx+.058,top-.025),(cx+.052,top-.118),(cx-.052,top-.118)]
    panel('Chest inset '+str(i+1),inset,-.153,.016,bodymat,.006)
    # short blue horizontal signal window near the bottom edge
    panel('Chest blue status bar '+str(i+1),chamfer_rect(cx,top-.148,.071,.014,.006),-.174,.008,blue_dim,.003)
# central sternum/collar plate and lower abdomen scales
panel('Armoured gorget',[(xc-.16,.564),(xc-.12,.617),(xc+.12,.617),(xc+.16,.564),(xc+.105,.523),(xc-.105,.523)],-.076,.05,plate_dark,.018)
for j,z in enumerate((.266,.194,.126)):
    panel('Abdominal scale '+str(j+1),chamfer_rect(-.08,z,.34,.062,.03),-.225+j*.012,.034,plate_dark,.012)
# central harness clasp
panel('Harness clasp',chamfer_rect(-.08,.08,.09,.105,.018),-.274,.022,plate_light,.008)

# Belt pouches and articulated knee/shin plates. These remain separate meshes for later rigging.
for i,x in enumerate((-.38,-.24,.09,.23)):
    panel('Utility pouch '+str(i+1),chamfer_rect(x,.015,.105,.17,.024),-.265,.045,plate_dark,.012)
    curve('Pouch lid seam '+str(i),[(x-.034,-.316,.025),(x+.034,-.316,.025)],.0035,plate_light)
for side,x in [('L',-.245),('R',.155)]:
    # thigh armor
    thigh=[(x-.105,-.105),(x-.078,-.215),(x-.07,-.315),(x-.035,-.375),(x+.067,-.365),(x+.11,-.30),(x+.092,-.16),(x+.06,-.10)]
    panel(side+' thigh plate',thigh,-.342,.055,plate_dark,.02)
    # knee cap with a bright inset edge
    knee=[(x-.105,-.342),(x-.072,-.405),(x,-.428),(x+.078,-.405),(x+.104,-.352),(x+.068,-.307),(x-.07,-.307)]
    panel(side+' knee shell',knee,-.355,.062,plate_dark,.018)
    panel(side+' knee inset',[(x-.056,-.35),(x,-.389),(x+.057,-.35),(x+.035,-.326),(x-.038,-.326)],-.379,.012,bodymat,.008)
    # shin plate and a narrow pale stripe down its outside edge
    shin=[(x-.082,-.46),(x-.112,-.55),(x-.078,-.755),(x-.045,-.81),(x+.065,-.79),(x+.098,-.72),(x+.072,-.52),(x+.045,-.46)]
    panel(side+' shin guard',shin,-.288,.052,plate_dark,.018)
    sx=x+(-.061 if side=='L' else .059)
    panel(side+' shin inlay',[(sx-.014,-.535),(sx+.009,-.515),(sx+.015,-.724),(sx-.012,-.758)],-.313,.012,plate_light,.004)
    # blue lower-leg identifier light
    panel(side+' shin blue marker',chamfer_rect(x,-.696,.036,.095,.012),-.326,.009,blue_dim,.003)

# Extra mechanical seams, fasteners, and backpack canisters.
for x,z,y in [(-.41,.468,-.101),(-.30,.48,-.103),(.20,.475,-.101),(.31,.458,-.099),
              (-.34,-.322,-.402),(-.15,-.327,-.402),(.08,-.333,-.395),(.25,-.32,-.393)]:
    cylinder_front('Flush armor fastener',(x,y,z),.009,.006,plate_light,16)
# Rear backpack modules (the antenna remains from the generated shape).
panel('Backpack center casing',[(-.29,.46),(-.27,.63),(-.18,.69),(.10,.69),(.22,.63),(.23,.45),(.12,.40),(-.20,.40)],.30,.09,plate_dark,.025)
panel('Backpack service panel',[(-.17,.59),(.10,.59),(.10,.48),(-.17,.48)],.285,.02,bodymat,.01)
for x in (-.24,.18):
    cylinder_front('Backpack canister',(x,.43,.57),.06,.24,plate_dark,24)

# Save a 3D artifact and render the canonical front view. No GPU rendering.
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.device='CPU'; scene.cycles.samples=20
scene.render.resolution_x=scene.render.resolution_y=1000; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.world.color=(.012,.016,.022)
# Studio lighting
lo=Vector((-.60,-.65,-1.02)); hi=Vector((.75,.65,1.03)); center=(lo+hi)/2
for name,offset,power,color,size in [('Key',(3,-4,5),1050,(.78,.85,1),4),('Fill',(-4,-2,1),620,(.42,.58,1),3),('Rim',(1,4,3),1350,(.23,.44,1),3)]:
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.color=color;d.shape='DISK';d.size=size
    o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=center+Vector(offset);o.rotation_euler=(center-o.location).to_track_quat('-Z','Y').to_euler()
camd=bpy.data.cameras.new('DetailStudyCamera');cam=bpy.data.objects.new('DetailStudyCamera',camd);scene.collection.objects.link(cam);scene.camera=cam
camd.type='ORTHO';camd.ortho_scale=2.48;cam.location=center+Vector((0,-1,0))*5.8;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(RENDER);bpy.ops.render.render(write_still=True)
# Export all visual parts as an isolated GLB study.
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=str(OUT),export_format='GLB',use_selection=True)
print('WROTE',OUT,RENDER,'objects',len(scene.objects))
