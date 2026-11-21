"""Collect passing, fingerprint-matched Unity reports for the current mod build."""
import hashlib
import json
import shutil
from pathlib import Path

root = Path(__file__).resolve().parents[1]
build = root / 'Artifacts/PhysicsProject/BuildArtifacts'
platform = root / 'Artifacts/PhysicsProject/BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64'
load = lambda path: json.loads(path.read_text(encoding='utf-8-sig'))
manifest = load(platform / 'modpack.json')
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
bundle_hash = digest(platform / manifest['id'])
dll_hash = digest(platform / manifest['modDll'])
checks = {
    'Integration': 'physics-diagnostics.json', 'Travel': 'travel-after.json',
    'Retention': 'retention-after.json', 'Approach': 'approach-after.json',
    'Systems': 'systems-after.json', 'Jam': 'systems-jam.json',
    'Indexer': 'systems-indexer-after.json', 'Moving': 'systems-moving-after.json',
    'Delivery': 'systems-delivery-after.json', 'Matrix': 'systems-matrix-after.json',
    'Capacity': 'capacity-after.json', 'Pile': 'pile-after.json',
    'WheelPile': 'wheel-pile-after.json', 'Pulse': 'systems-pulse-after.json',
}
integration = ['completed', 'finiteAndStable', 'driveMoved', 'railsDeployed', 'pickedUp',
               'shotReleased', 'disabledStopped', 'inputsBound', 'endToEndCycle',
               'stowed', 'preloadsRetained', 'reverseSupportsLoad']
summary = {'version': manifest['version'], 'bundle_sha256': bundle_hash,
           'runtime_dll_sha256': dll_hash, 'fixtures': []}
expected_cases = {'Integration': 0, 'Travel': 4, 'Retention': 2, 'Approach': 6,
                  'Systems': 7, 'Jam': 1, 'Indexer': 11, 'Moving': 2, 'Delivery': 4,
                  'Matrix': 8, 'Capacity': 2, 'Pile': 2, 'WheelPile': 2, 'Pulse': 1}
for name, filename in checks.items():
    report = load(build / filename)
    passed = (all(report.get(k) for k in integration) and not report.get('errors')) \
        if name == 'Integration' else report.get('passed', False)
    hashes = all(report.get(k) == value for k, value in
                 [('bundleSha256', bundle_hash), ('runtimeDllSha256', dll_hash)] if k in report)
    cases = report.get('cases', [])
    passed = passed and len(cases) == expected_cases[name]
    if name not in ['Integration', 'Travel']:
        passed = passed and 'BuiltMods' in report.get('prefabSource', '')
    passed = passed and report.get('intakeForceOverride', 0) == 0 \
        and report.get('intakeTractionOverride', 0) == 0 \
        and report.get('intakeLateralOverride', -1) < 0 \
        and report.get('capacityCaptureTarget', 51) == 51
    measured = {key: sum(c.get(key, 0) for c in cases) for key in
                ['scored', 'delivered', 'seeded', 'collected', 'retained',
                 'retainedAfterDrive', 'released', 'hardPumpCycles']}
    summary['fixtures'].append({'name': name, 'report': filename, 'passed': bool(passed),
        'embedded_hashes_match': hashes,
        'embedded_hashes_present': 'runtimeDllSha256' in report,
        'cases': len(cases), 'measured_totals': measured,
        'editor_service_errors': report.get('editorServiceErrors', [])})
failed = [c['name'] for c in summary['fixtures'] if not c['passed'] or not c['embedded_hashes_match']]
if failed:
    raise SystemExit('Current build checks failed or stale: ' + ', '.join(failed))
for filename in [*checks.values(), 'assembly-audit.json', 'bundle-audit.json']:
    shutil.copy2(build / filename, root / 'Reports' / filename)
for pattern in ['Turbo4744-v5-*.png', 'Turbo4744-v6-*.png', 'Turbo4744-pile-loaded-*.png']:
    for path in build.glob(pattern):
        shutil.copy2(path, root / 'Reports' / path.name)
(root / 'Reports' / f"verification-{manifest['version']}.json").write_text(
    json.dumps(summary, indent=2) + '\n')
for case in summary['fixtures']:
    print(case['name'], case['passed'], case['cases'], case['measured_totals'])
