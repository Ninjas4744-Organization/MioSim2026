"""Certify the owner-approved grounded-clearance patch on all three platforms."""
import hashlib
import json
from pathlib import Path

PLATFORMS = ('standalonewindows64', 'standalonelinux64', 'standaloneosx')
MOD = 'rebuilt_ninjas_4744_turbo_2026'


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify(root: Path, project: Path):
    release = root / 'Reports/Release-1.0.3'
    build = load(release / 'build.json')
    source = root / 'Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744'
    assert {p.name: sha(p) for p in source.glob('*.cs')} == build['source_sha256']
    assert all(sha(root / 'Assets/Editor' / n) == s
               for n, s in build['validation_source_sha256'].items())
    files = {}
    for platform in PLATFORMS:
        folder = project / 'BuiltMods' / MOD / platform
        actual = {p.name: sha(p) for p in folder.iterdir() if p.is_file()}
        assert len(actual) == 6 and actual == build['platform_files'][platform]
        manifest = load(folder / 'modpack.json')
        assert manifest['version'] == '1.0.3'
        for name, digest in actual.items():
            if not name.endswith('.manifest'):
                files[f'{platform}/{name}'] = digest
    assert len(files) == 12
    windows = build['platform_files'][PLATFORMS[0]]
    dll = windows['Ninjas4744.dll']
    bundle = windows[MOD + '_assets']
    trial_dir = root / 'Reports/SolidClearance-20261124'
    trial = load(trial_dir / 'verification.json')
    assert trial['passed'] and trial['native_feedback'] == 'Recovers smoothly; pocket clears.'
    assert all(sha(trial_dir / n) == h for n, h in trial['evidence_sha256'].items())
    runtime = load(release / 'runtime-equivalence.json')
    assets = load(release / 'asset-equivalence.json')
    assert runtime['passed'] and runtime['unchanged_recovery_and_drive_methods']
    assert runtime['windows_native_loader_body_unchanged']
    assert all(runtime['platform_guard'].values())
    assert runtime['before_dll_sha256'] == trial['files']['Ninjas4744.dll']
    assert runtime['after_dll_sha256'] == dll
    assert assets['passed'] and assets['native_net_executable_identical']
    assert assets['before_bundle_sha256'] == trial['files'][MOD + '_assets']
    assert assets['after_bundle_sha256'] == bundle
    platforms = load(release / 'platform-bundles.json')
    assert platforms['passed'] and set(platforms['platforms']) == set(PLATFORMS)
    for platform, audit in platforms['platforms'].items():
        assert audit['all_gameplay_objects_match_windows']
        assert audit['identical_non_shader_objects'] == 2602
        assert audit['runtime_dll_sha256'] == dll
        assert audit['bundle_sha256'] == build['platform_files'][platform][MOD + '_assets']
        assert audit['manifest_sha256'] == build['platform_files'][platform]['modpack.json']
    fresh = {}
    for name, count in [('underbody.json', 17), ('controls.json', 58), ('systems.json', 7)]:
        data = load(release / name)
        assert data['passed'] and not data['errors'], name
        assert len(data.get('checks', data.get('cases', []))) == count
        assert data['runtimeDllSha256'] == dll and data['bundleSha256'] == bundle
        if name == 'systems.json':
            assert data['simulationSpeed'] == 1
            assert data['burstEnabled']
            assert data['configuredTargetFps'] == 120
            assert all(c['observerSamples'] > 200 for c in data['cases'])
            assert all(c['maximumObserverStep'] < .2 for c in data['cases']
                       if c['kind'] == 'shoot')
        fresh[name] = {'checks_or_cases': count, 'sha256': sha(release / name)}
    for case in load(release / 'underbody.json')['cases']:
        assert case['allBallsPreserved'] and case['robotUnmoved']
        assert case['badGroundedClears'] == 0 and case['collisionPairsAlwaysSolid']
        assert case['visualsFinished'] and case['maximumVisibleStep'] < .04
    jam = load(release / 'jam.json')
    assert jam['passed'] and not jam['errors'] and jam['cases']
    assert jam['configuredTargetFps'] == 120
    assert jam['burstEnabled']
    assert jam['runtimeDllSha256'] == dll and jam['bundleSha256'] == bundle
    assert all(c['observerSamples'] > 200 for c in jam['cases'])
    fresh['jam.json'] = {'cases': len(jam['cases']), 'sha256': sha(release / 'jam.json')}
    aot = load(release / 'net-aot-audit.json')
    assert aot['managedSha256'] == dll and aot['embedded']
    for name, burst in [('net-burst.json', True), ('net-managed.json', False)]:
        data = load(release / name)
        assert data['passed'] and not data['error'] and data['comparedSteps'] == 480
        assert data['burstEnabled'] == burst
        assert data['burstSettingRestored']
        assert data['solverSha256'] == sha(source / 'Turbo4744NetSolver.cs')
        assert data['editorRuntimeDllSha256'] == aot['editorManagedSha256']
        fresh[name] = {'compared_steps': 480, 'sha256': sha(release / name)}
    native = load(release / 'local-install.json')
    capture = load(release / 'native-startup.json')
    assert native['native_runtime_verified'] and native['native_net_dispatch_verified']
    assert native['files'] == windows and native['startup_settings_unchanged']
    assert capture['runtimeModule'] == native['runtime_module']
    assert max(f['clearanceGroundedWheels'] for f in capture['frames']) > 0
    assert all(f['ignoredClearancePairs'] == f['assistedClearanceSteps'] == 0
               for f in capture['frames'])
    listing = load(release / 'live-before.json')
    assert listing['id'] == '3843685878' and listing['version'] == '1.0.2'
    previous = load(root / 'Reports/steam-upload-1.0.2.json')
    assert previous['success'] and previous['remoteVerified']
    inherited = {n: sha(trial_dir / n) for n in
                 ('traction-forward.json', 'traction-reverse-pair.json',
                  'collection-retention.json', 'shooting.json', 'corner-pile.json')}
    proof_names = ('build.json', 'runtime-equivalence.json', 'asset-equivalence.json',
                   'platform-bundles.json', 'local-install.json', 'native-startup.json',
                   'net-aot-audit.json', 'live-before.json', 'systems-first-failed.json',
                   'systems-first-failed-context.json', 'systems-cadence-failed.json',
                   'systems-frame-cap-failed.json')
    return {
        'passed': True, 'version': '1.0.3', 'workshop_item': '3843685878',
        'files': files, 'source_sha256': build['source_sha256'],
        'fresh_checks': fresh,
        'proof_sha256': {n: sha(release / n) for n in proof_names},
        'equivalent_trial_reports': inherited,
        'trial_certificate_sha256': sha(trial_dir / 'verification.json'),
        'owner_confirmation': trial['native_feedback'],
        'native_windows_module': native['runtime_module'],
        'limits': [
            'Native gameplay checks ran on Windows only; macOS and Linux were cross-built and structurally inspected.',
            'macOS and Linux use the existing portable net solver; Windows retains its AOT dispatch.',
            'Recovery clears the physical ball immediately and smooths its existing visible mesh for up to 0.65 seconds.',
            'The initial private corner run retained two balls at seven seconds; a repeat emptied both headings. Both observations remain in the trial evidence.',
        ],
    }
