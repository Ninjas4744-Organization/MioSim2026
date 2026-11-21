"""Check a preserved full-hopper bump/delivery cycle against the current build."""
import argparse
import csv
import hashlib
import json
import math
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('label')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
reports = root / 'Reports/native'
platform = root / 'Artifacts/PhysicsProject/BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64'
load = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
manifest = load(platform / 'modpack.json')
hashes = {name: hashlib.sha256((platform / name).read_bytes()).hexdigest()
          for name in [manifest['modDll'], manifest['id']]}
r = load(reports / (args.label + '.json'))
q = load(reports / (args.label + '-request.json'))
assert load(reports / (args.label + '-artifacts.json')) == hashes, 'Stale artifacts'
assert r['completed'] and r['transitCompleted'] and r['stowedBeforeReturn']
assert not r['overturned'] and not r['stowTimedOut'] and not r['failureReason']
assert q['stowForReturn'] and not q['requireTrenchSuccess'] and q['shootKey'] == 'E'
assert abs(abs(q['returnLane']) - 1.7) < .0001 and q['returnGear'] == 2
assert r['fieldFuel'] == 504 and abs(r['physicsStep'] - .0045) < .000001
for key in ['disableJointProjection', 'solidBumper', 'discreteChassis', 'stopInterpolation',
            'compliantFuel', 'netEnvironmentContacts', 'railForceOverride', 'railDampingOverride',
            'netRestScaleOverride', 'netStretchOverride', 'netShearOverride', 'netStrandRadiusOverride']:
    assert not q.get(key), 'Diagnostic override: ' + key
start = r['transitStartCount']
assert 50 <= start <= 60 and r['transitEndCount'] == start and not r['transitEscapes']
assert r['fullyStoredBeforeStow'] == r['storedRetainedAfterStow']
assert r['remainingCaptured'] == 0 and r['releasedCaptured'] == start
assert r['delivered'] >= math.ceil(start * 7 / 8), 'Delivery below the existing 7/8 criterion'
assert max(r[k] for k in ['maxCollectFrameMs', 'maxReturnFrameMs', 'maxShootFrameMs']) < 250
frames = list(csv.DictReader([r['frameColumns'], *r['frames']]))
sign = 1 if r['alliance'] == 'Blue' else -1
crossing = [f for f in frames if int(f['transitRetained']) > 0
            and abs(sign * float(f['x']) - 3.65) < .95]
assert len(crossing) >= 10
assert min(sign * float(f['x']) for f in crossing) < 2.9
assert max(sign * float(f['x']) for f in crossing) > 4.4
assert all(abs(float(f['z']) - q['returnLane']) < .15 for f in crossing)
assert all(int(f['transitRetained']) == start for f in crossing)
rail_max = max(abs(float(f[k])) for f in crossing for k in ['intakeExtension', 'hopperExtension'])
# Use the existing Travel fixture's 35 mm impact/stow envelope here.
# The separate trench checker retains its stricter 15 mm overhead-clearance gate.
assert rail_max < .035
assert max(float(f['y']) for f in crossing) > .05, 'No raised bump crossing'
assert 5 < max(float(f['tilt']) for f in crossing) < 30
summary = {'version': manifest['version'], 'artifacts': hashes, 'label': args.label,
           'route': 'Bump beside hub at world Z ' + str(q['returnLane']),
           'alliance': r['alliance'], 'stowed': start, 'retained': r['transitEndCount'],
           'stored_spills': len(r['transitEscapes']), 'inlet_departures': len(r['inletDepartures']),
           'released': r['releasedCaptured'], 'delivered_within_1_25_m': r['delivered'],
           'remaining': r['remainingCaptured'], 'maximum_tilt_degrees': r['maxTilt'],
           'maximum_rail_extension_m': rail_max, 'maximum_return_frame_ms': r['maxReturnFrameMs'],
           'passed': True}
output = root / 'Reports' / ('native-bump-' + manifest['version'] + '.json')
output.write_text(json.dumps(summary, indent=2) + '\n')
print(json.dumps(summary, indent=2))
