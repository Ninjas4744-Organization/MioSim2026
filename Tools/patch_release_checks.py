"""Validate the version-only promotion of the owner-approved bumper patch."""
import hashlib
import json
from pathlib import Path


def verify_patch_checks(root: Path, project: Path):
    load = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
    digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    platform = project / 'BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64'
    manifest = load(platform / 'modpack.json')
    assert manifest['version'] == '1.0.2'
    hashes = {n: digest(platform / n) for n in (manifest['modDll'], manifest['id'])}
    trial_dir = root / 'Reports/Drivability-1.0.1'
    trial = load(trial_dir / 'verification.json')
    assert trial['automated_checks_passed'] and trial['native_owner_confirmed_improvement']
    assert trial['native_owner_feedback'] == 'Better in both cases'
    assert trial['bumper_source_sha256'] == digest(root / 'Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744/Turbo4744BumperContacts.cs')
    release = root / 'Reports/Release-1.0.2'
    runtime = load(release / 'runtime-equivalence.json')
    assets = load(release / 'asset-equivalence.json')
    assert runtime['passed'] and runtime['compared_methods'] == 338
    assert not runtime['changed_behavior_methods'] and not runtime['removed_methods']
    assert runtime['before_dll_sha256'] == trial['runtime_dll_sha256']
    assert runtime['after_dll_sha256'] == hashes[manifest['modDll']]
    assert assets['passed'] and assets['native_net_executable_identical']
    assert assets['before_bundle_sha256'] == trial['bundle_sha256']
    assert assets['after_bundle_sha256'] == hashes[manifest['id']]
    inherited = []
    for matrix in trial['matrices']:
        path = trial_dir / matrix['file']
        assert digest(path) == matrix['sha256']
        data = load(path)
        assert data['passed'] and not data['errors']
        assert data['runtimeDllSha256'] == trial['runtime_dll_sha256']
        assert data['bundleSha256'] == trial['bundle_sha256']
        assert len(data['cases']) == matrix['cases']
        airborne = [c for c in data['cases'] if c['name'] == 'all tires clear']
        assert len(airborne) == 2 and all(c['maximumGrounded'] == 0 and c['distance'] < .05 and c['minimumTireClearance'] > .004 for c in airborne)
        inherited.append(matrix)
    assert sum(m['cases'] for m in inherited) == 38
    trench = load(trial_dir / 'candidate-trench.json')
    assert trench['passed'] and not trench['errors'] and len(trench['cases']) == 8
    fresh = []
    for filename, count in [('controls.json', 58), ('captured-poses.json', 6), ('corner-pile.json', 2), ('systems.json', 7)]:
        path = release / filename
        data = load(path)
        assert data['passed'] and not data['errors'], filename
        assert data['runtimeDllSha256'] == hashes[manifest['modDll']], filename
        assert data['bundleSha256'] == hashes[manifest['id']], filename
        assert len(data.get('checks', data.get('cases', []))) == count
        if filename == 'captured-poses.json':
            assert all(c['escaped'] and c['longestStall'] < .4 for c in data['cases'])
        if filename == 'corner-pile.json':
            assert all(c['capturedBeforeShoot'] > 0 and c['capturedAfterShoot'] == 0 and c['feedingObserved'] and c['escaped'] for c in data['cases'])
        if filename == 'systems.json':
            assert data['simulationSpeed'] == 1
            assert sum(c.get('scored', 0) for c in data['cases']) == 40
        fresh.append({'report': filename, 'cases_or_checks': count, 'sha256': digest(path)})
    certificate = {
        'version': '1.0.2', 'passed': True, 'artifacts': hashes,
        'scope': 'Version-only promotion of the owner-approved bumper-contact patch; targeted regression checks, not a repeat of the full original release matrix.',
        'fresh_checks': fresh, 'equivalent_trial_traction_cases': 38,
        'equivalent_trial_airborne_controls': 6, 'equivalent_authoring_trench_cases': 8,
        'trial_certificate_sha256': digest(trial_dir / 'verification.json'),
        'runtime_equivalence_sha256': digest(release / 'runtime-equivalence.json'),
        'asset_equivalence_sha256': digest(release / 'asset-equivalence.json'),
        'owner_confirmation': trial['owner_feedback'],
    }
    (root / 'Reports/patch-release-1.0.2.json').write_text(json.dumps(certificate, indent=2) + '\n', encoding='utf-8')
    return certificate
