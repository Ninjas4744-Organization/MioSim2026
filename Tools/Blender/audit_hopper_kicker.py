"""Check the actual mesh against the front bumper through the complete fold."""
from pathlib import Path
import json
import math

import bpy
import numpy as np

repo = Path(__file__).resolve().parents[2]
model = json.loads((repo / 'Reports/hopper-kicker-model.json').read_text())
pivot = np.array(model['hinge_native_m'])
stowed_angle = model['stowed_angle_degrees']
groups = []
for name, folds in (('Intake_Geometry', False), ('KickerMount_Geometry', False),
                    ('Kicker_Geometry', True), ('IntakeRoller_Geometry', True)):
    obj = bpy.data.objects[name]
    obj.data.calc_loop_triangles()
    vertices = np.array([obj.matrix_world @ v.co for v in obj.data.vertices])
    triangles = np.array([t.vertices for t in obj.data.loop_triangles])
    groups.append((name, folds, vertices, triangles))

def clip_x(polygon, boundary, sign):
    output = []
    for a, b in zip(polygon, polygon[1:] + polygon[:1]):
        inside_a, inside_b = sign * (a[0] - boundary) >= 0, sign * (b[0] - boundary) >= 0
        if inside_a:
            output.append(a)
        if inside_a != inside_b:
            output.append(a + (b - a) * ((boundary - a[0]) / (b[0] - a[0])))
    return output

failures, poses = [], []
sequence = []
for step in range(41):
    sequence.append(('hopper_extending', step / 40, 0))
for step in range(31):
    sequence.append(('kicker_deploying', 1, step / 30))
for step in range(31):
    sequence.append(('kicker_retracting', 1, 1 - step / 30))
for step in range(41):
    sequence.append(('hopper_retracting', 1 - step / 40, 0))
for step in range(41):
    # Team pump window: eight to eleven intake motor rotations. The compact
    # lower assembly stays down throughout this outside-the-bumper stroke.
    open_fraction = 8 / 11 + (1 - 8 / 11) * step / 40
    sequence.append(('pumping_kicker_stays_deployed', open_fraction, 1))
for step, (phase, open_fraction, deployment) in enumerate(sequence):
    angle = math.radians(-stowed_angle * (1 - deployment))
    c, s = math.cos(angle), math.sin(angle)
    rotation = np.array(((c, 0, s), (0, 1, 0), (-s, 0, c)))
    shift = np.array((-.22946 * (1 - open_fraction), 0, 0))
    moving_points = []
    for name, folds, vertices, triangles in groups:
        points = ((vertices - pivot) @ rotation.T + pivot if folds else vertices) + shift
        if folds:
            moving_points.append(points)
        faces = points[triangles]
        candidates = np.where(
            (faces[:, :, 0].max(axis=1) >= .3629047)
            & (faces[:, :, 0].min(axis=1) <= .4201052)
            & (faces[:, :, 2].min(axis=1) < .159299)
            & (faces[:, :, 2].max(axis=1) > .032264)
        )[0]
        intersections = 0
        for triangle in faces[candidates]:
            polygon = clip_x(list(triangle), .3629047, 1)
            polygon = clip_x(polygon, .4201052, -1)
            if polygon and min(p[2] for p in polygon) < .159299 and max(p[2] for p in polygon) > .032264:
                intersections += 1
        if intersections:
            failures.append({'phase': phase, 'open': open_fraction, 'deployment': deployment,
                             'mesh': name, 'triangles': intersections})
    if step in (0, 20, 40, 56, 71, 87, 102, 123, 143):
        points = np.concatenate(moving_points)
        poses.append({'phase': phase, 'open': open_fraction, 'deployment': deployment,
                      'angle_degrees': -math.degrees(angle),
                      'min_native_m': points.min(axis=0).tolist(),
                      'max_native_m': points.max(axis=0).tolist()})

report = {'passed': not failures, 'samples': len(sequence), 'front_bumper_intersections': failures,
          'sequence': ['Hopper out with kicker tucked', 'Kicker down with hopper held out',
                       'Kicker up with hopper held out', 'Hopper in with kicker tucked',
                       'Short pump strokes with kicker staying deployed'],
          'hopper_attachment': bpy.data.objects['Intake'].parent.name,
          'roller_attachment': bpy.data.objects['IntakeRoller'].parent.name, 'poses': poses}
(repo / 'Reports' / 'hopper-kicker-motion.json').write_text(json.dumps(report, indent=2) + '\n')
print('HOPPER_KICKER_MOTION', json.dumps(report))
assert not failures, 'Kicker intersects the bumper during retraction'
