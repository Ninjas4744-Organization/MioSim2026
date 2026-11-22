"""Certify load-limited stow, retention, shooting and empty-stow recovery.

The owner confirmed that the real hopper stays extended when the load prevents
closure. This checks that behavior with ordinary keys and the original physics,
instead of treating a forced 60-ball closure as a release requirement.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path
from release_artifact_binding import bind_artifacts

parser = argparse.ArgumentParser()
parser.add_argument('labels', nargs='+')
parser.add_argument('--project', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
reports = root / 'Reports/native'
platform = args.project.resolve() / 'BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64'
load = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
manifest = load(platform / 'modpack.json')
hashes = {n: hashlib.sha256((platform / n).read_bytes()).hexdigest()
          for n in (manifest['modDll'], manifest['id'])}
summary = {'version': manifest['version'], 'artifacts': hashes,
           'scenario': 'Full load blocks closure; retain, shoot empty, then stow', 'cases': []}
overrides = ('disableJointProjection', 'solidBumper', 'discreteChassis', 'stopInterpolation',
             'compliantFuel', 'netEnvironmentContacts', 'railForceOverride', 'railDampingOverride',
             'netRestScaleOverride', 'netStretchOverride', 'netShearOverride', 'netStrandRadiusOverride')
for label in args.labels:
    r = load(reports / (label + '.json'))
    q = load(reports / (label + '-request.json'))
    tested = load(reports / (label + '-artifacts.json'))
    proof = bind_artifacts(tested, hashes)
    if proof:
        summary.update(selector_artwork_equivalence=proof, tested_artifacts=tested)
    assert r['completed'] and not r['failureReason'] and not r['overturned'], label
    assert r['runtimeVersion'] == manifest['version'] and r['fieldFuel'] == 504
    assert abs(r['physicsStep'] - .0045) < .000001
    assert r['netBurstExecution'] and r['netNativeLibraryLoaded'] and r['netBroadphaseEnabled']
    assert r['netAnchorCount'] == 68 and not any(q.get(k) for k in overrides)
    assert r['railForce'] == 350 and r['railDamping'] == 4200
    assert r['intakeForceBudget'] == 600 and r['intakeTractionBudget'] == 100
    assert abs(r['netStrandRadius'] - .00115) < .000001 and r['netRestScale'] == 1
    assert not q['stowForReturn'] and not q['returnToOwnZone'] and q['targetCaptured'] == 0
    segments = []
    end = 0
    for segment in q['inputTimeline'].split(';'):
        duration, keys = segment.split(':')
        start, end = end, end + float(duration)
        segments.append((start, end, set(filter(None, keys.split(',')))))
    stows = [(a, b) for a, b, keys in segments if keys == {'Z'}]
    assert len(stows) == 2 and stows[0][1] - stows[0][0] >= 8
    assert stows[-1][1] - stows[-1][0] >= 4 and stows[-1][1] == end
    assert any(q['shootKey'] in keys for _, _, keys in segments)
    assert abs(stows[0][0] - (2.5 + q['collectSeconds'])) < .001
    assert abs(stows[0][1] - (2.5 + q['collectSeconds'] + q['retreatSeconds'])) < .001
    frames = list(csv.DictReader([r['frameColumns'], *r['frames']]))
    blocked = [f for f in frames if stows[0][1] - 2 <= float(f['seconds']) < stows[0][1]]
    final = [f for f in frames if float(f['seconds']) >= end - 1]
    assert len(blocked) >= 50 and len(final) >= 25, f'{label}: missing stow traces'
    collecting = [f for f in frames if 2.5 < float(f['seconds']) < stows[0][0]]
    assert collecting and all(f['pump'] == 'Opened' for f in collecting), \
        f'{label}: pumping while collecting'
    # After firing, release the simple trigger or cancel the latched shot with
    # Intake. Leave three seconds before explicitly requesting final Stow.
    a, b, keys = segments[-2]
    assert b - a >= 3 and keys == (set() if r['simpleControls'] else {'LeftShift'})
    reopened = [f for f in frames if b - 1 <= float(f['seconds']) < b]
    assert len(reopened) >= 25 and all(int(f['captured']) == 0
        and f['pump'] == 'Opened' and f['state'] == 'Idle' and f['feeding'] == 'False'
        and float(f['intakeExtension']) > .22 and float(f['hopperExtension']) > .21
        for f in reopened), f'{label}: empty-shot release closed the intake or kept pumping'
    count = r['fullyStoredBeforeStow']
    # Arrival/braking can add a few balls after a nominal 60-ball fill. Keep
    # those harder, over-capacity holds eligible without calling a small load full.
    assert 58 <= count <= 64 and r['capturedAtShot'] == count
    assert all(int(f['captured']) == count and f['pump'] == 'Closed' for f in blocked)
    assert max(abs(float(f['intakeExtension'])) for f in blocked) < .002
    assert min(float(f['hopperExtension']) for f in blocked) > .015, 'No load-blocked closure observed'
    assert not r['transitEscapes'], f'{label}: fuel escaped during blocked stow'
    assert r['releasedCaptured'] >= count and r['remainingCaptured'] == 0
    assert all(int(f['captured']) == 0 and f['pump'] == 'Closed' for f in final)
    final_rail = max(abs(float(f[k])) for f in final
                     for k in ('intakeExtension', 'hopperExtension'))
    assert final_rail < .002, f'{label}: empty hopper did not recover full stow'
    peak_ms = max(r[k] for k in ('maxCollectFrameMs', 'maxReturnFrameMs', 'maxShootFrameMs'))
    assert peak_ms < 250, f'{label}: frame stall'
    summary['cases'].append({'label': label, 'simple_controls': r['simpleControls'],
                             'alliance': r['alliance'], 'full_load': count,
                             'retained_while_blocked': count, 'released': r['releasedCaptured'],
                             'remaining': 0, 'empty_stow_maximum_extension_m': final_rail,
                             'collection_does_not_pump': True,
                             'empty_shot_release_keeps_intake_open': True,
                             'maximum_frame_ms': peak_ms, 'native_burst': True})
assert {c['simple_controls'] for c in summary['cases']} == {False, True}, 'Need both control variants'
assert any(c['full_load'] >= 60 for c in summary['cases']), 'Need at least one actual 60+ ball load'
summary['passed'] = True
output = root / 'Reports' / f"native-full-hopper-{manifest['version']}.json"
output.write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
print(json.dumps(summary, indent=2))
