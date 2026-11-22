"""Package the fingerprint-matched net collision trial, without claiming release certification."""
import argparse
import hashlib
import json
import subprocess
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
load = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--label', default='net-collision-r1')
    args = parser.parse_args()
    project = args.project.resolve()
    evidence = ROOT / 'Reports/HopperPerformance'
    platform = project / 'BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64'
    manifest = load(platform / 'modpack.json')
    bundle_hash = digest(platform / manifest['id'])
    dll_hash = digest(platform / manifest['modDll'])
    native = load(project / 'BuildArtifacts/NetBurst/audit.json')
    if native['managedSha256'] != dll_hash or not native['embedded']:
        raise SystemExit('The player DLL does not match the compiled net library.')
    native_asset = project / 'Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744/Native/Ninjas4744NetBurst.bytes'
    if digest(native_asset) != native['nativeSha256']:
        raise SystemExit('The embedded native library is stale.')
    methods = [line for line in (project / 'BuildArtifacts/NetBurst/Ninjas4744NetBurst.txt').read_text().splitlines() if line.startswith('--method=')]
    if len(methods) != 1 or 'Ninjas4744.MoSim.Turbo4744NetSolver' not in methods[0] or 'ExecuteNativeStep' not in methods[0]:
        raise SystemExit('The native library must contain only the local net entrypoint.')
    if methods[0].rsplit('--', 1)[-1] != native['nativeEntryPoint']:
        raise SystemExit('The native symbol differs from the build audit.')
    solver_hash = digest(ROOT / 'Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744/Turbo4744NetSolver.cs')
    required = ['net-collision-managed.json', 'net-collision-burst.json', 'controls-trial.json',
                'full-hopper-retention.json', 'full-hopper-shooting.json', 'loaded-auto-shooting.json', 'native-trial.json']
    for name in required:
        report = load(evidence / name)
        if name.startswith('net-collision-'):
            if not report['passed'] or report['error'] or report['comparedSteps'] < 480 or report['solverSha256'] != solver_hash:
                raise SystemExit(f'Stale or failing net equivalence: {name}')
            if report['editorRuntimeDllSha256'] != native['editorManagedSha256']:
                raise SystemExit(f'Net equivalence used a different editor DLL: {name}')
            if report['optimizedMicroseconds'] >= report['originalMicroseconds']:
                raise SystemExit(f'No measured improvement in {name}')
        elif name == 'native-trial.json':
            if not report['completed'] or not report['netNativeLibraryLoaded'] or not report['netBurstExecution'] or report['netAnchorCount'] != 68 or not report['netBroadphaseEnabled']:
                raise SystemExit('The packaged native net was not verified in the installed player.')
            if report['failureReason'] or report['overturned'] or report['releasedCaptured'] < 8 or report['remainingCaptured']:
                raise SystemExit('The installed-player preload delivery smoke test did not pass.')
            receipt = load(evidence / 'native-install-receipt.json')
            if receipt['bundleSha256'] != bundle_hash or receipt['runtimeDllSha256'] != dll_hash:
                raise SystemExit('Native testing used a different build.')
        else:
            if not report['passed'] or report['errors'] or report['bundleSha256'] != bundle_hash or report['runtimeDllSha256'] != dll_hash:
                raise SystemExit(f'Failing or stale bundle check: {name}')
            if report['editorRuntimeDllSha256'] != native['editorManagedSha256']:
                raise SystemExit(f'Editor fixture used a different source compilation: {name}')
            if name == 'controls-trial.json' and len(report['checks']) < 34:
                raise SystemExit('The controls check is incomplete.')
            if name == 'loaded-auto-shooting.json':
                if len(report['cases']) != 1:
                    raise SystemExit('Expected the full-load automatic aiming case.')
                case = report['cases'][0]
                if (case['collected'] < 57 or case['released'] != case['collected']
                        or case['retained'] or case['escapes'] or not case['stowed'] or not case['stable']):
                    raise SystemExit('Full-load automatic aiming and shooting did not pass.')
            if name.startswith('full-hopper-'):
                if len(report['cases']) != 2 or report['capacityCaptureTarget'] < 57:
                    raise SystemExit(f'Incomplete full-load cases: {name}')
                for case in report['cases']:
                    if case['collected'] < 57 or case['escapes'] or not case['stable']:
                        raise SystemExit(f'Incomplete full load or lost fuel: {name}')
                    if name.endswith('retention.json') and case['retained'] != case['collected']:
                        raise SystemExit('Static full-load retention did not pass.')
                    if name.endswith('shooting.json') and (case['retainedAfterDrive'] != case['collected'] or case['released'] != case['collected'] or case['retained'] != 0 or not case['stowed']):
                        raise SystemExit('Full-load driving and shooting did not pass.')
    audit = load(project / 'BuildArtifacts/bundle-audit.json')
    if not audit['passed'] or any(name.startswith('UnityEditor') for name in audit['assemblyReferences']):
        raise SystemExit('The player bundle or runtime reference audit failed.')
    files = sorted(p for p in platform.iterdir() if p.is_file())
    if len(files) != 6:
        raise SystemExit('Expected six platform build files.')
    sources = {str(p.relative_to(ROOT)).replace('\\', '/'): digest(p)
               for p in (ROOT / 'Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744').glob('*.cs')}
    for relative, source_hash in sources.items():
        if digest(project / relative) != source_hash:
            raise SystemExit('Runtime source differs from the tested project: ' + relative)
    timing = load(evidence / 'net-collision-burst.json')
    retention = load(evidence / 'full-hopper-retention.json')
    shooting = load(evidence / 'full-hopper-shooting.json')
    automatic = load(evidence / 'loaded-auto-shooting.json')
    build = dict(version=manifest['version'], label=args.label,
                 sourceCommit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
                 workingTreeSources=sources, bundleSha256=bundle_hash, runtimeDllSha256=dll_hash,
                 nativeLibrarySha256=native['nativeSha256'], burstVersion=native['compilerVersion'], cpuTarget='X64_SSE2',
                 nativeNetOnly=True,
                 validation=dict(netOriginalMs=timing['originalMicroseconds'] / 1000,
                                 netOptimizedMs=timing['optimizedMicroseconds'] / 1000,
                                 retainedLoads=[c['retained'] for c in retention['cases']],
                                 shotLoads=[c['released'] for c in shooting['cases']],
                                 automaticShotLoad=automatic['cases'][0]['released']),
                 scope='Focused trial: exact net-state/reaction equivalence; editor bundle full-load retention, loaded driving, manual shooting and automatic aiming/shooting; installed-player native/Burst execution and 8-preload delivery smoke test. Editor and player DLLs are distinct audited compilations of the same source. No complete release recertification.')
    prefix = 'Ninjas4744-Turbo2026/'
    payloads = {prefix + 'standalonewindows64/' + p.name: p.read_bytes() for p in files}
    for name in required + ['native-install-receipt.json']:
        payloads[prefix + 'Verification/' + name] = (evidence / name).read_bytes()
    payloads[prefix + 'BUILD-INFO.json'] = (json.dumps(build, indent=2) + '\n').encode()
    payloads[prefix + 'README.md'] = (
        '# Turbo 2026 net collision trial\n\n'
        'This Windows build filters impossible net contacts while retaining the existing physics.\n'
        'See BUILD-NOTES.txt for installation and BUILD-INFO.json for exact build fingerprints.\n\n'
        f"The 60-ball Burst net benchmark measured {build['validation']['netOriginalMs']:.3f} ms before "
        f"and {build['validation']['netOptimizedMs']:.3f} ms after filtering. This measures net CPU time, not whole-game FPS.\n\n"
        f"Editor bundle tests retained complete loads of {build['validation']['retainedLoads']} balls, "
        f"and drove, turned and shot complete loads of {build['validation']['shotLoads']} balls without escapes.\n"
        f"Normal auto-aim shooting also released a full load of {build['validation']['automaticShotLoad']} balls.\n"
        'The installed-player probe separately verified native execution and eight-preload delivery.\n'
        'Raw reports are in Verification/. Please test on the computer that experienced the lag.\n'
    ).encode()
    payloads[prefix + 'Licenses/MoSim.txt'] = (ROOT / 'Licenses/MoSim.txt').read_bytes()
    for package in ('burst', 'collections', 'mathematics'):
        matches = list((project / 'Library/PackageCache').glob(f'com.unity.{package}@*'))
        if len(matches) != 1:
            raise SystemExit('Expected one pinned Unity package: ' + package)
        for name in ('LICENSE.md', 'Third Party Notices.md'):
            notice = matches[0] / name
            if notice.exists():
                payloads[prefix + f'Licenses/Unity-{package}/' + name] = notice.read_bytes()
    payloads[prefix + 'BUILD-NOTES.txt'] = (
        'Ninjas 4744 Turbo 2026 / net collision performance trial\n\n'
        'Close MoSim. Extract Ninjas4744-Turbo2026 into:\n'
        '%USERPROFILE%\\AppData\\LocalLow\\CascadeStudios\\MoSim\\Mods\\\n'
        'Replace the previous copy, keep one Turbo mod enabled, then restart MoSim.\n'
        'Select 4744 / Turbo. Actual Turbo retains the original latched controls.\n\n'
        'Spatial filtering skips impossible net contacts. Both fuel spheres, contact order,\n'
        'substeps, compliance, damping, geometry and reaction calculations are retained.\n'
        'The compiled net solver is included inside the asset bundle; no game files need replacing.\n\n'
        + build['scope'] + '\n\n'
        'Please compare filling, driving with a full hopper, and shooting on the PC that lagged.\n'
        'Report whether the freezes improve, plus CPU/GPU, game version and graphics settings.\n'
    ).encode()
    destination = ROOT / 'dist'
    destination.mkdir(exist_ok=True)
    target = destination / f"Ninjas4744-Turbo2026-{manifest['version']}-{args.label}-Windows.zip"
    with tempfile.NamedTemporaryFile(dir=destination, suffix='.tmp', delete=False) as handle:
        temporary = Path(handle.name)
    try:
        with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED) as archive:
            for name, data in payloads.items():
                archive.writestr(name, data)
        with zipfile.ZipFile(temporary) as archive:
            if archive.testzip() or set(archive.namelist()) != set(payloads):
                raise SystemExit('Archive integrity check failed.')
            for name, data in payloads.items():
                if archive.read(name) != data:
                    raise SystemExit('Archive mismatch: ' + name)
        temporary.replace(target)
    finally:
        temporary.unlink(missing_ok=True)
    target.with_suffix('.sha256').write_text(digest(target) + '  ' + target.name + '\n')
    (evidence / 'trial-build.json').write_text(json.dumps(build, indent=2) + '\n')
    print(target)
    print('Verified', len(payloads), 'ZIP entries. SHA-256', digest(target))


if __name__ == '__main__':
    main()
