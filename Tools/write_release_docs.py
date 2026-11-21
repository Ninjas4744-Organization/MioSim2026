"""Write release notes from completed, matching fixture and native certificates."""
import hashlib
import json
import re
import shutil
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parents[1]
reports = root / 'Reports'
docs = root / 'Docs'
load = lambda path: json.loads(path.read_text(encoding='utf-8-sig'))
platform = root / 'Artifacts/PhysicsProject/BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64'
manifest = load(platform / 'modpack.json')
version = manifest['version']
certificate = load(reports / f'verification-{version}.json')
native = load(reports / f'native-trench-{version}.json')
bump = load(reports / f'native-bump-{version}.json')
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
assert certificate['bundle_sha256'] == digest(platform / manifest['id'])
assert certificate['runtime_dll_sha256'] == digest(platform / manifest['modDll'])
assert all(c['passed'] and c['embedded_hashes_match'] for c in certificate['fixtures'])
assert native['passed'] and native['artifacts'] == {
    manifest['modDll']: certificate['runtime_dll_sha256'],
    manifest['id']: certificate['bundle_sha256']}
assert bump['passed'] and bump['artifacts'] == native['artifacts']

rows = []
for item in certificate['fixtures']:
    report = load(reports / item['report'])
    cases = report.get('cases', [])
    totals = item['measured_totals']
    name = item['name']
    if name == 'Integration':
        detail = 'Pickup, same-ball feed/release, preloads, reverse support, disable and staged stow pass.'
    elif name == 'Travel':
        detail = 'Four headings; actuator impulse, disable and end stops pass.'
    elif name in ['Retention', 'Approach']:
        detail = f"{totals['collected']} collected and {totals['retained']} retained; zero escapes; loaded stow passes."
    elif name in ['Capacity', 'Pile', 'WheelPile']:
        detail = '; '.join(f"{c['heading']}°: {c['collected']}/{c['retainedAfterDrive']}/{c['released']}" for c in cases)
        detail += ' collected/retained/released; zero escapes.'
        if name == 'WheelPile':
            detail += ' Drift ' + '/'.join(f"{c['maxCollectionYaw']:.2f}°" for c in cases) + '.'
    elif name == 'Indexer':
        pairs = [c for c in cases if c['indexerCase'] in ['lower-pair', 'upper-pair']]
        detail = f"Eleven cases; {totals['scored']}/{totals['seeded']} scores; pair exits "
        detail += '/'.join(f"{c['pairExitDelta'] * 1000:.2f} ms" for c in pairs)
        detail += ', interpolated from 4.5 ms steps and both below 50 ms; passive guides, corners, singles and blocked paths pass.'
    elif name == 'Delivery':
        offered = sum(c.get('payload') or 8 for c in cases)
        detail = f"{totals['delivered']}/{offered} first landings within the unchanged 1.25 m tolerance."
    elif name == 'Jam':
        c = cases[0]
        detail = f"{c['jamRecoveries']} recoveries; {c['backoffSeconds']:.3f} s reverse; obstruction contact and resumed release pass."
    else:
        offered = sum(c.get('payload') or 8 for c in cases if c['range'] > 0)
        detail = f"{len(cases)} {'case' if len(cases) == 1 else 'cases'}; {totals['scored']}/{offered} scores; required pumping and movement checks pass."
    rows.append(f"| [{name}](../Reports/{item['report']}) | {detail} |")

native_rows = [
    f"| [{c['label']}](../Reports/native/{c['label']}.json) | {c['alliance']} | {c['start']} → {c['retained']} | "
    f"{c['minimum_trench_speed_mps']:.2f} | {c['spills']} / {c['inlet_departures']} | {c['scored']} / {c['remaining']} |"
    for c in native['cases']]
native_table = '\n'.join([
    '| Installed-player cycle | Alliance | Stowed / retained | Minimum trench speed (m/s) | Stored spills / inlet departures | Scored / remaining |',
    '| --- | --- | --- | --- | --- | --- |', *native_rows])
native_performance_text = (f"Across these {len(native['cases'])} trench cycles, recorded collection, return and shooting frames peaked at "
                           f"{max(c['maximum_recorded_frame_ms'] for c in native['cases']):.2f} ms. "
                           "The checker rejects recorded phase frames at or above 250 ms and sustained pose/velocity holds under the trench.")
bump_text = (f"The additional [native bump/delivery cycle](../Reports/native/{bump['label']}.json) "
             f"carried {bump['stowed']} balls over the bump beside the hub and retained all "
             f"{bump['retained']}, with zero stored spills. It released {bump['released']}, "
             f"delivered {bump['delivered_within_1_25_m']} within 1.25 m of the target, and ended empty. "
             f"Peak tilt was {bump['maximum_tilt_degrees']:.2f}°. The front carrier briefly deflected "
             f"{bump['maximum_rail_extension_m'] * 1000:.1f} mm during the impact, within the existing "
             "Travel fixture's 35 mm impact/stow envelope. Trench acceptance retains its stricter "
             "15 mm rail-clearance limit. The [bump certificate](../Reports/native-bump-" + version + ".json) "
             "checks the raised route, retained bodies, normal input commands and matching artifacts.")

(docs / 'Verification.md').write_text(f'''# Turbo {version} verification

All fourteen complete fixture groups pass against the fingerprints below. Tests run in visible Game views using the built Windows bundle and normal action bindings. Shooting and indexer fixtures run at simulation speed 1 and the native 4.5 ms physics step. Existing pickup, capacity, retention, drift, scoring and pair-timing thresholds remain unchanged.

| Check | Measured result |
| --- | --- |
''' + '\n'.join(rows) + f'''

Capacity, Pile and WheelPile schedule their existing 0.5 s forward / 1 s reverse keyboard pulses on physics steps. Velocity samples include the result of the final commanded step: Unity advances physics after `FixedUpdate` ([execution order](https://docs.unity3d.com/2023.2/Documentation/Manual/ExecutionOrder.html)). The 3.5 m/s loaded-speed minimum is unchanged. The [frame-driven failure](../Reports/pile-062-loaded-speed-failure.json), [short-pulse physics measurement](../Reports/pile-062-physics-sampled-short-input.json), [pre-step endpoint comparison](../Reports/pile-062-physics-pulses-prestep.json) and [isolated corrected-clock check](../Reports/pile-062-physics-pulses-endpoint.json) remain preserved separately from the complete release groups above.

The installed-player trench cycles use the original 504 field fuel bodies. Actual stowed loads are counted before driving, and every member must remain through the crossing. Acceptance requires zero spills, full retraction, no sustained stop, matched installed artifact hashes and no diagnostic overrides. At least one cycle carries exactly 60 balls; both alliances and slow/normal gears are covered.

{native_table}

{bump_text}

{native_performance_text}

Inlet departures count arrivals that never completed storage; they are recorded separately from losses of stored fuel. Pre-stow stored fuel must also survive retraction. Scoring counts are actual game events; remaining means fuel still in the robot at the end. The native traces record the full collection, stow, transit and shooting cycle separately from the fixture suite. [Native procedures and evidence](NativeTesting.md) explain the measurements.

The powered floors and wide shooter retain shared force budgets and chassis reaction. Side triangles remain passive. The main hopper and roof frame stay fixed; only the intake/kicker retracts. Fuel stays independent, with original geometry and mass, no parenting, repositioning, freezing or assigned velocity. Contact compliance and textile/traction parameters are explicit simulator calibration; see [mechanisms](Mechanisms.md) and [calibration](../Reports/indexer-calibration.json).

![Side cutaway](../Reports/Turbo4744-v6-indexer-cutaway-side.png)

The cutaway hides panels and cloth rendering for inspection; their physics remain active.

![Two-ball shooter](../Reports/Turbo4744-v6-indexer-pair-top.png)

Bundle SHA-256: `{certificate['bundle_sha256']}`

DLL SHA-256: `{certificate['runtime_dll_sha256']}`

The [certificate](../Reports/verification-{version}.json) records every group and matching hash. Earlier results and fixture corrections remain in [0.6.1 verification](Verification-0.6.1.md).
''', encoding='utf-8')

old_native = docs / 'NativeTesting-0.6.1.md'
if not old_native.exists():
    shutil.copy2(docs / 'NativeTesting.md', old_native)
(docs / 'NativeTesting.md').write_text(f'''# Installed MoSim tests for {version}

The native player loads the same bundle and DLL validated by the fixture suite. Full-load trench results are recorded in the [native certificate](../Reports/native-trench-{version}.json), with adjacent requests, raw traces, screenshots and installed artifact fingerprints.

{native_table}

{bump_text}

{native_performance_text}

`Turbo4744NativeReplay` is opt-in: place `Turbo4744-native-replay.json` in MoSim's persistent-data folder to run an input replay. Normal play adds no replay component when the file is absent. The replay temporarily scopes a virtual keyboard to this robot, drives ordinary action bindings, and restores the device scope at completion. Remove the owned request after testing and restore any temporary device/alliance selection.

The field retains its original 504 independent fuel bodies. The replay never seeds, moves, freezes or assigns velocities to fuel or the chassis. Default robot physics includes tensioned cloth, field-box contacts and bounded fuel-to-fuel compliance. Diagnostic comparison flags and numeric overrides must be false/zero for release acceptance.

Use `stowForReturn: true`, `requireTrenchSuccess: true`, `minimumTransitLoad: 50`, `maximumTransitLoad: 60`, and the physical trench lane (`returnLane: -3.3758` for the tested Red start or `3.3758` for Blue). Gear 1 is the slower return; gear 2 is normal. Target collection counts are braking triggers; the actual stowed cohort determines acceptance. Out-of-range loads and route timeouts fail, without a wrong-zone fallback shot.

The visible display shows phase, peak collection, actual stowed count, retained count, stored spills, incomplete inlet departures, roller contacts and scoring. Box-hitbox outlines show enabled live surfaces. A completed replay can reset the match for the next request. JSON contains a 22.5 ms motion trace at the 4.5 ms physics step. `frames` and `frameColumns` are authoritative because native serialization omits nested request/sample objects; the request is preserved separately.

The stowed cohort uses fuel centres inside local X ±0.36 m, Y 0.15–1.00 m and Z −0.17–0.64 m, including lower staging. Retention tracks those same bodies. Every crossing sample must retain the cohort. Rail extension must stay below 15 mm, and both velocity-based and pose-based stops under the bar must stay below .25 s. The lane trace must cross the actual trench. Scoring uses the game's per-body timestamp; muzzle observations can miss a fast crossing between samples.

After observing completion, preserve the report and screenshots with:

```powershell
python Tools/capture_native_replay.py --label LABEL --request PATH --require-trench
```

Once all release cycles are captured, run `Tools/verify_native_trench.py LABEL...`. It requires current artifact hashes, default calibration, both alliances, slow and normal gears, and an actual 60-ball crossing. `Tools/Package.ps1` rejects a release without that certificate, a matching native bump/delivery certificate and all fourteen matching fixture groups. The calibration includes contact-law approximations; these records establish the tested simulator routes.

Historical failures remain preserved. [The 0.6.1 native record](NativeTesting-0.6.1.md) contains the caught slow trench and nine-ball spill. Early 0.6.2 contact-only and net comparisons failed and remain under `Reports/native/`. The 5% textile-ease candidate lost stored fuel at the crossbar. A swept-node experiment then caught and stretched the roof, so it was rejected. The accepted build uses the tensioned rest scale 1.0 and analytic field-box contacts. Dense-pile calibration comparisons retain their original thresholds and failed outcomes in `Reports/wheel-pile-062-*.json`.

Earlier 1.3-grip comparisons remain visible too. [An over-range Red fill](../Reports/native/062-release-red-normal.json) stowed 62 balls and aborted before transit. [A fuller Blue fill](../Reports/native/062-release-blue-target64-slow.json) stopped at the replay's 2 mm stow gate after the front carrier settled 2.16 mm from its stop; it never attempted the crossing. Neither run is included in passing acceptance. Completed pumped/settled cycles are the recorded evidence; these results do not establish success for every possible arrival or stow sequence.

The separate [60-target capacity diagnostic](../Reports/capacity-062-target60-comparison.json) retained and released 57 and 59 balls, but failed the zero-degree 3.5 m/s loaded-speed criterion at 3.39 m/s. It is excluded from the release certificate. The package-required capacity fixture keeps its original target and thresholds; the native certificate separately checks actual 50–60-ball trench loads.
''', encoding='utf-8')

provenance_path = docs / 'Provenance.json'
previous = load(provenance_path)
if previous['version'] == '0.5.1':
    archive = docs / 'Provenance-0.5.1.json'
    if not archive.exists():
        shutil.copy2(provenance_path, archive)
source = {k: previous[k] for k in ['schema', 'robot', 'source_model', 'tools', 'framework',
                                 'team_code', 'coordinate_conversion']}
source.update({'version': version, 'prepared_utc': datetime.now(timezone.utc).isoformat(),
    'physics': {
        'robot_mass_kg': 62, 'main_body_kg': 54, 'intake_kg': 5.5, 'kicker_carrier_kg': 2.5,
        'hopper': 'Fixed walls, storage floor and roof frame; intake/kicker retracts',
        'intake_stroke_m': .24, 'kicker_carrier_stroke_m': .22946,
        'actuator_force_limit_n': 350, 'slide_speed_limit_mps': .16,
        'slide_velocity_damping_ns_per_m': 4200, 'slide_acceleration_mps2': 1.8, 'chassis_collision': 'Discrete at 4.5 ms',
        'tread_friction_coefficient': 1.4, 'tread_bite_factor': .32,
        'net': '320 nodes at 0.00075 kg; 1.15 mm strand radius; rest scale 1.0; stretch 3e-7; shear 1.5e-7; bend .002; fuel contact every substep; static field-box contact',
        'fuel_contacts': 'Original stock mass and sphere geometry; hopper fuel-to-fuel spring 750 N/m, closing damping 8 N s/m, hardening at 20 mm contact penetration',
        'force_budgets_n': {'collection_traction': 100, 'collection_overall': 600,
                            'feeding_intake': 140, 'indexer_both_floors': 320, 'shooter': 480},
        'pumping': 'Source 11/8 hard x3 and 10/9 soft windows move only the intake/kicker',
    },
    'verification': {'fixture_certificate': f'Reports/verification-{version}.json',
                     'native_certificate': f'Reports/native-trench-{version}.json',
                     'native_bump_certificate': f'Reports/native-bump-{version}.json',
                     'fixture_groups': len(certificate['fixtures']), 'native_cycles': native['cases']},
    'calibration_limits': ['Textile, foam contact law, motor-force mapping and release speeds are simulator calibration, not a measured hardware fit',
                           'No current sensing, vision estimation, autonomous path execution or climbing simulation',
                           'Native evidence covers the recorded routes and loads'],
    'publication': 'Local repository and local game installation; no push or Workshop publication',
})
paths = ['Models/robot.blend']
paths += ['Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744/' + n for n in
          ['Turbo4744.cs', 'Turbo4744.prefab', 'Turbo4744Indexer.cs', 'Turbo4744ShooterContact.cs',
           'Turbo4744FuelKeeper.cs', 'Turbo4744FuelCompliance.cs', 'Turbo4744NetEnvironment.cs',
           'Turbo4744DriveController.cs', 'Turbo4744NetRig.cs', 'Model/Turbo4744.fbx']]
source['file_hashes'] = {path: digest(root / path) for path in paths}
provenance_path.write_text(json.dumps(source, indent=2) + '\n', encoding='utf-8')
full_case = next(c for c in native['cases'] if c['start'] == 60)
picture = f"Reports/native/{full_case['label']}-trench-clear.png"
assert (root / picture).exists(), 'Missing native full-load crossing image'
readme = root / 'README.md'
text = readme.read_text(encoding='utf-8')
text = re.sub(r'!\[Turbo carrying a full load in installed MoSim\]\([^\n]+\)',
              f'![Turbo carrying a full load in installed MoSim]({picture})', text)
section = f'''## What changed in {version}

- Added field-box contacts for the roof net and calibrated its cord size and tension.
- Added bounded fuel-to-fuel compliance inside the fixed hopper, preserving stock fuel geometry and mass.
- Separated powered intake traction from passive support and completed the moving inlet handoff during stow.
- Increased slide velocity damping while preserving the 350 N force limit.
- Calibrated full-load tread grip to 1.4 while preserving the team drive constants.
- Verified native stowed trench crossings with actual loads of {', '.join(str(c['start']) for c in native['cases'])} balls, retaining every member through the crossing. Both alliances, slow and normal returns, and an actual 60-ball load are covered.

All fourteen required fixture groups pass against this release's bundle and DLL. [Measured results](Docs/Verification.md) · [Native requests, traces and screenshots](Docs/NativeTesting.md). Incomplete arrivals at the inlet are reported separately; pre-stow stored fuel must survive retraction as well as transit. Earlier failed runs remain preserved.

'''
text = re.sub(r'## What changed in ' + re.escape(version) + r'\n.*?(?=## What changed in 0\.6\.1)', '', text, flags=re.S)
text = text.replace('## What changed in 0.6.1', section + '## What changed in 0.6.1', 1)
text = text.replace("The current build's stowed bump cycle", "The 0.6.1 stowed bump cycle")
text = text.replace('Full-load trench traversal remains unresolved.', 'Full-load trench traversal remained unresolved in 0.6.1.')
readme.write_text(text, encoding='utf-8')
print('Wrote verified release documentation for', version)
