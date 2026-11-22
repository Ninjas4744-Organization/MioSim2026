"""Verify preserved installed-player trench cycles against the current release."""
import argparse
import csv
import hashlib
import json
from pathlib import Path
from release_artifact_binding import bind_artifacts

parser = argparse.ArgumentParser()
parser.add_argument('labels', nargs='+')
parser.add_argument('--project', type=Path, default=Path('Artifacts/PhysicsProject'))
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
reports = root / 'Reports/native'
platform = args.project.resolve() / 'BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64'
load = lambda path: json.loads(path.read_text(encoding='utf-8-sig'))
manifest = load(platform / 'modpack.json')
hashes = {name: hashlib.sha256((platform / name).read_bytes()).hexdigest()
          for name in [manifest['modDll'], manifest['id']]}
summary = {'version': manifest['version'], 'artifacts': hashes, 'cases': []}
boolean_overrides = ['disableJointProjection', 'solidBumper', 'discreteChassis',
                     'stopInterpolation', 'compliantFuel', 'netEnvironmentContacts']
numeric_overrides = ['railForceOverride', 'railDampingOverride', 'netRestScaleOverride',
                     'netStretchOverride', 'netShearOverride', 'netStrandRadiusOverride']
for label in args.labels:
    report = load(reports / (label + '.json'))
    request = load(reports / (label + '-request.json'))
    tested = load(reports / (label + '-artifacts.json'))
    proof = bind_artifacts(tested, hashes)
    if proof:
        summary.update(selector_artwork_equivalence=proof, tested_artifacts=tested)
    assert not any(request.get(k) for k in boolean_overrides + numeric_overrides), f'{label}: diagnostic override'
    assert request['requireTrenchSuccess'] and request['stowForReturn'] and request['returnLane'] != 0
    if tuple(map(int, manifest['version'].split('.'))) >= (1, 0, 1):
        assert report['runtimeVersion'] == manifest['version'], 'Wrong native runtime version'
        assert report['netBurstExecution'] and report['netNativeLibraryLoaded'], 'Native Burst path inactive'
        assert report['netBroadphaseEnabled'] and report['netAnchorCount'] == 68
        assert report['remainingCaptured'] == 0 and report['releasedCaptured'] >= report['transitStartCount'], 'Transit load did not shoot empty'
    assert report['completed'] and report['trenchPassed'] and report['transitCompleted'], label
    assert report['stowedBeforeReturn'] and not report['stowTimedOut'] and not report['overturned'], label
    assert 0 < request['stowTolerance'] <= .010
    assert max(abs(report[k]) for k in ('intakeExtensionAtReturn', 'hopperExtensionAtReturn')) < .010
    assert report['fieldFuel'] == 504 and abs(report['physicsStep'] - .0045) < .000001, label
    start = report['transitStartCount']
    # The real bin cannot close when full (confirmed by the robot's owner).
    # Full-load blocked stow/recovery is certified separately; crossbar travel
    # must start with a load that actually permits the original slides to close.
    assert 16 <= start <= 32 and start == report['transitEndCount'], label
    assert request['minimumTransitLoad'] == 16 and request['maximumTransitLoad'] == 32
    assert not report['transitEscapes'] and not report['failureReason'], label
    assert report['storedRetainedAfterStow'] == report['fullyStoredBeforeStow'], f'{label}: stored fuel lost during stow'
    assert report['maximumTrenchHoldSeconds'] < .25, label
    maximum_frame_ms = max(report[k] for k in ['maxCollectFrameMs', 'maxReturnFrameMs', 'maxShootFrameMs'])
    assert maximum_frame_ms < 250, f'{label}: collection/return/shoot frame stall'
    assert report['compliantContacts'] > 0 and report['netContactSteps'] > 0, label
    assert report['railForce'] == 350 and report['railDamping'] == 4200, label
    assert report['intakeForceBudget'] == 600 and report['intakeTractionBudget'] == 100, label
    assert abs(report['netStrandRadius'] - .00115) < .000001 and abs(report['netRestScale'] - 1.0) < .000001, label
    frames = list(csv.DictReader([report['frameColumns'], *report['frames']]))
    own_sign = 1 if report['alliance'] == 'Blue' else -1
    crossing = [r for r in frames if int(r['transitRetained']) > 0
                and abs(own_sign * float(r['x']) - 3.65) < .95]
    assert len(crossing) >= 10, f'{label}: no crossing trace'
    assert min(own_sign * float(r['x']) for r in crossing) < 2.9 \
        and max(own_sign * float(r['x']) for r in crossing) > 4.4, f'{label}: incomplete crossing trace'
    assert all(abs(float(r['z']) - request['returnLane']) < .15 for r in crossing), f'{label}: outside trench lane'
    assert min(int(r['transitRetained']) for r in crossing) == start, f'{label}: temporary loss at trench'
    hold = maximum_pose_hold = 0
    for previous, current in zip(crossing, crossing[1:]):
        dt = float(current['seconds']) - float(previous['seconds'])
        moved = abs(float(current['x']) - float(previous['x']))
        speed = abs(float(previous['vx']) + float(current['vx'])) * .5
        hold = hold + dt if speed > 1 and moved < .35 * speed * dt else 0
        maximum_pose_hold = max(maximum_pose_hold, hold)
    assert maximum_pose_hold < .25, f'{label}: chassis pose stopped despite drive velocity'
    rail_max = max(abs(float(r[k])) for r in crossing
                   for k in ['intakeExtension', 'hopperExtension'])
    assert rail_max < .015, f'{label}: mechanism extended under crossbar'
    summary['cases'].append({
        'native_burst': report.get('netBurstExecution', False),
        'simple_controls': report.get('simpleControls', False),
        'label': label, 'alliance': report['alliance'], 'return_gear': request['returnGear'],
        'start': start, 'retained': report['transitEndCount'], 'spills': 0,
        'fully_stored_before_stow': report['fullyStoredBeforeStow'],
        'stored_retained_after_stow': report['storedRetainedAfterStow'],
        'inlet_departures': len(report['inletDepartures']),
        'maximum_hold_seconds': report['maximumTrenchHoldSeconds'],
        'maximum_pose_hold_seconds': maximum_pose_hold,
        'minimum_trench_speed_mps': min(abs(float(r['vx'])) for r in crossing),
        'maximum_rail_extension_m': rail_max, 'maximum_tilt_degrees': report['maxTilt'],
        'maximum_fuel_compression_m': report['maximumFuelCompression'],
        'scored': report['scoredCaptured'], 'remaining': report['remainingCaptured'],
        'maximum_return_frame_ms': report['maxReturnFrameMs'],
        'maximum_recorded_frame_ms': maximum_frame_ms,
    })
assert len(summary['cases']) >= 3, 'Need repeat crossings, including slow and normal speeds'
assert {c['return_gear'] for c in summary['cases']} >= {1, 2}, 'Need slow and normal gears'
assert {c['alliance'] for c in summary['cases']} >= {'Red', 'Blue'}, 'Need both alliances'
if tuple(map(int, manifest['version'].split('.'))) >= (1, 0, 1):
    assert any(c['simple_controls'] for c in summary['cases']), 'Test the default simple variant too'
summary['scenario'] = 'Partially loaded stowed trench transit; full-load stow is physically load limited'
summary['passed'] = True
output = root / 'Reports' / f"native-trench-{manifest['version']}.json"
output.write_text(json.dumps(summary, indent=2) + '\n')
print(json.dumps(summary, indent=2))
