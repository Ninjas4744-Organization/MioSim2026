"""Recover the compact front assembly from original CAD part boundaries.

Open the approved 1.3.1 source model. Save only the optimized game model to this
repository; never save over the reference. Remove its photo-inferred 13° tilt.
"""
from pathlib import Path
import json
import math

import bpy
from mathutils import Matrix, Vector

repo = Path(__file__).resolve().parents[2]
backup = repo / 'Artifacts/robot-before-bumper-clearance.blend'
source_objects = set(bpy.data.objects)
assignments = json.loads((repo / 'Reports/model-conversion-audit.json').read_text())['assembly_assignments']
wanted = {p['name'] for p in assignments if p['assembly'] in ('Intake', 'IntakeRoller')}
parts = [o for o in source_objects if o.type == 'MESH' and o.name in wanted]
if len(parts) != len(wanted):
    raise RuntimeError(f'Expected {len(wanted)} original CAD parts; found {len(parts)}')
undo_tilt = Matrix.Translation((.106, 0, .3535)) @ Matrix.Rotation(math.radians(-13), 4, 'Y') @ Matrix.Translation((-.106, 0, -.3535))
original_poses = {o: undo_tilt @ o.matrix_world for o in parts}

with bpy.data.libraries.load(str(backup)) as (source, target):
    target.objects = list(source.objects)
game_objects = list(target.objects)
for obj in game_objects:
    if obj and not obj.users_collection:
        bpy.context.scene.collection.objects.link(obj)
bpy.context.view_layer.update()
root = bpy.data.objects['Turbo4744_Game']
hopper = bpy.data.objects['Hopper']
transfer = bpy.data.objects['Intake']
roller = bpy.data.objects['IntakeRoller']
for name in ('Intake_Geometry', 'IntakeRoller_Geometry'):
    bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)

def parent_at_world(obj, parent, world=None):
    world = world.copy() if world is not None else obj.matrix_world.copy()
    obj.parent = parent
    obj.matrix_world = world

parent_at_world(transfer, hopper)
parent_at_world(roller, hopper)
upper_axis = Vector((.57204, 0, .24405))
kicker = bpy.data.objects.new('Kicker', None)
bpy.context.scene.collection.objects.link(kicker)
parent_at_world(kicker, hopper, Matrix.Translation(upper_axis))
bpy.context.view_layer.update()
parent_at_world(roller, kicker)

records = json.loads((repo / 'Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744/Model/materials.json').read_text())['materials']
material_names = {record['source_name']: record['name'] for record in records}
# These are complete original shafts, lower bearing plates and bearings. The
# upper motor housings and structural side plates remain on the hopper frame.
lower_parts = {51, 54, 56, 64, 68, 78, 94, 95}
groups = {'Intake_Geometry': [], 'KickerMount_Geometry': [], 'Kicker_Geometry': [], 'IntakeRoller_Geometry': []}
part_report = []
for original in sorted(parts, key=lambda o: o.name):
    number = int(original.name[4:8])
    obj = original.copy()
    obj.data = original.data.copy()
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = None
    obj.matrix_world = original_poses[original]
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    for modifier in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    if len(obj.data.polygons) > 2500:
        decimate = obj.modifiers.new('Original game LOD budget', 'DECIMATE')
        decimate.ratio = min(.7, max(.025, 1000 / len(obj.data.polygons)))
        bpy.ops.object.modifier_apply(modifier=decimate.name)
    for index, material in enumerate(obj.data.materials):
        if material:
            name = material_names.get(material.name)
            if name is None:
                raise RuntimeError('Unmapped original CAD material: ' + material.name)
            obj.data.materials[index] = bpy.data.materials[name]
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    points = [obj.matrix_world @ Vector(p) for p in obj.bound_box]
    if number == 82:
        key = 'IntakeRoller_Geometry'
        low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
        high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
        parent_at_world(roller, kicker, Matrix.Translation((low + high) / 2))
    elif number in lower_parts:
        key = 'Kicker_Geometry'
    elif min(p.x for p in points) > .45:
        key = 'KickerMount_Geometry'
    else:
        key = 'Intake_Geometry'
    groups[key].append(obj)
    part_report.append({'part': original.name, 'assembly': key})

for name, objects in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = obj.data.name = name
    parent_at_world(obj, roller if name == 'IntakeRoller_Geometry' else kicker if name == 'Kicker_Geometry' else transfer if name == 'Intake_Geometry' else hopper)

for obj in source_objects:
    bpy.data.objects.remove(obj, do_unlink=True)
bpy.ops.outliner.orphans_purge(do_recursive=True)
bpy.context.view_layer.update()
camera = next((o for o in game_objects if o and o.type == 'CAMERA'), None)
if camera:
    bpy.context.scene.camera = camera
root['kicker_reference'] = 'User shooting/pumping footage, 20 November 2026'
kicker['hinge_native_m'] = list(upper_axis)
kicker['stowed_angle_degrees'] = 60
report = {
    'method': 'Original complete CAD parts; original pose restored by undoing the inferred 13-degree tilt',
    'original_part_count': len(parts),
    'hinge_native_m': list(upper_axis),
    'stowed_angle_degrees': 60,
    'fixed_structure': 'Original clear structural plates, motor housings, pulleys and front frame',
    'moving_structure': 'Original lower bearing plates, bearings and shaft; original roller remains separately driven',
    'invented_supports_removed': True,
    'synthetic_mesh_cuts_removed': True,
    'lower_kicker_parts': sorted(lower_parts | {82}),
    'parts': part_report,
}
(repo / 'Reports/hopper-kicker-model.json').write_text(json.dumps(report, indent=2) + '\n')
bpy.ops.file.pack_all()
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(repo / 'Models/robot.blend'))
print('RESTORED_CAD_KICKER', json.dumps({k: v for k, v in report.items() if k != 'parts'}))
