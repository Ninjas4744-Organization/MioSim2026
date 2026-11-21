"""Split the existing indexer rollers and recover passive CAD side supports.

blender -b Models/robot.blend --python Tools/Blender/prepare_indexer.py -- --source ORIGINAL.blend
The reference is read through Blender's library API and is never saved.
"""
import argparse
import json
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Vector

repo = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--source', required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
root = bpy.data.objects['Turbo4744_Game']
chassis = bpy.data.objects['Chassis_Geometry']
materials = json.loads((repo / 'Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744/Model/materials.json').read_text(encoding='utf-8'))['materials']
carbon = next(m['name'] for m in materials if m['source_name'] == 'Turbo carbon fibre indexer tubes')
rollers = [
    ('IndexerLower01', .33680, -.00825, .19344, .34279),
    ('IndexerLower02', .28802, -.00825, .18884, .34279),
    ('IndexerLower03', .24077, -.00825, .17584, .34279),
    ('IndexerLower04', .19560, 0, .15685, .29155),
    ('IndexerLower05', .15029, .00375, .13745, .28780),
    ('IndexerUpper01', .15291, -.00375, .32162, .28780),
    ('IndexerUpper02', .10796, -.00375, .30210, .28780),
    ('IndexerUpper03', .06302, 0, .28257, .29155),
    ('IndexerUpper04', .07542, -.00825, .33873, .29230),
]
records = []
side_records = []


def evaluated_world(obj):
    # Library objects are outside the active dependency graph. Their cached
    # matrix_world can predate the CAD parent's coordinate normalization.
    if obj.parent:
        return evaluated_world(obj.parent) @ obj.matrix_parent_inverse @ obj.matrix_basis
    return obj.matrix_basis.copy()

for name, x, y, z, half_width in rollers:
    if name in bpy.data.objects:
        records.append(dict(name=name, center_native_m=[x, y, z], radius_m=.0125, half_width_m=half_width))
        continue
    bm = bmesh.new()
    bm.from_mesh(chassis.data)
    bm.faces.ensure_lookup_table()
    keep = set()
    for face in bm.faces:
        material = chassis.data.materials[face.material_index]
        if not material or material.name != carbon:
            continue
        center = chassis.matrix_world @ face.calc_center_median()
        if (center.x - x) ** 2 + (center.z - z) ** 2 < .016 ** 2 and abs(center.y - y) < half_width + .025:
            keep.add(face.index)
    if not keep:
        raise RuntimeError('No CAD roller faces found for ' + name)
    # Work on copies so material slots, UVs and the remaining chassis survive.
    split = bm.copy()
    split.faces.ensure_lookup_table()
    bmesh.ops.delete(split, geom=[f for f in split.faces if f.index not in keep], context='FACES')
    mesh = bpy.data.meshes.new(name + '_Geometry')
    split.to_mesh(mesh)
    split.free()
    for material in chassis.data.materials:
        mesh.materials.append(material)
    pivot = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(pivot)
    pivot.parent = root
    pivot.matrix_world = Matrix.Translation((x, y, z))
    obj = bpy.data.objects.new(name + '_Geometry', mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = pivot
    obj.matrix_world = chassis.matrix_world.copy()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index in keep], context='FACES')
    bm.to_mesh(chassis.data)
    bm.free()
    records.append(dict(name=name, center_native_m=[x, y, z], radius_m=.0125, half_width_m=half_width))

# The two original triangular belt/support envelopes are passive in MoSim.
# Their convex hulls support fuel without the former broad invented shoulders.
with bpy.data.libraries.load(str(Path(args.source).resolve()), link=False) as (source, target):
    target.objects = [n for n in source.objects if n.startswith(('Part0837_', 'Part0840_', 'Part1069_', 'Part1078_'))]
reference = [o for o in target.objects if o]
if len(reference) != 4:
    raise RuntimeError('Expected four original triangular side-indexer faces')
for side, prefixes in [('Left', ('Part0837_', 'Part0840_')), ('Right', ('Part1069_', 'Part1078_'))]:
    name = 'SideIndexer' + side + '_Support'
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    bm = bmesh.new()
    for obj in reference:
        if obj.name.startswith(prefixes):
            matrix = root.matrix_world.inverted() @ evaluated_world(obj)
            for vertex in obj.data.vertices:
                bm.verts.new(matrix @ vertex.co)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.0001)
    result = bmesh.ops.convex_hull(bm, input=list(bm.verts), use_existing_faces=False)
    bmesh.ops.delete(bm, geom=[v for v in result.get('geom_interior', []) if isinstance(v, bmesh.types.BMVert)], context='VERTS')
    bmesh.ops.dissolve_limit(bm, angle_limit=.005, verts=list(bm.verts), edges=list(bm.edges))
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = root
    obj['passive_fuel_support'] = True
    points = [root.matrix_world @ v.co for v in mesh.vertices]
    side_records.append(dict(
        name=name,
        source_parts=list(prefixes),
        minimum_native_m=[min(v[i] for v in points) for i in range(3)],
        maximum_native_m=[max(v[i] for v in points) for i in range(3)],
    ))
for obj in reference:
    bpy.data.objects.remove(obj, do_unlink=True)
bpy.context.view_layer.update()
(repo / 'Reports/indexer-model.json').write_text(json.dumps({
    'source': 'Original CAD; existing game roller faces preserved',
    'rollers': records,
    'side_supports': 'Convex envelopes of original 26-413-001 triangular faces; passive, no motor',
    'side_support_bounds': side_records,
    'shooter_clear_width_m': .313,
}, indent=2) + '\n', encoding='utf-8')
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(repo / 'Models/robot.blend'))
print('INDEXER_MODEL', json.dumps(records))
