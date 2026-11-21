"""Package a focused test build without asserting full release certification."""
import argparse
import hashlib
import json
import re
import subprocess
import tempfile
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def digest(data):
    return hashlib.sha256(data).hexdigest()


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform', type=Path, default=ROOT / 'Artifacts/PhysicsProject/BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64')
    parser.add_argument('--verification', type=Path, default=ROOT / 'Reports/sliding-hopper-r3/verification.json')
    parser.add_argument('--label', default='sliding-hopper-r3')
    parser.add_argument('--profile', choices=['hopper', 'swerve'], default='hopper')
    parser.add_argument('--check-only', action='store_true')
    args = parser.parse_args()
    if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9._-]*', args.label):
        raise SystemExit('Use a simple filename label.')

    certificate = load(args.verification)
    evidence = args.verification.parent
    receipt = (load(evidence / 'install-receipt.json') if args.profile == 'swerve'
               else certificate['installedNormalMod'])
    if not certificate['passed'] or not receipt['normalRuntime']:
        raise SystemExit('A passing verification of the normal runtime is required.')
    if args.profile == 'swerve' and (not certificate['normalRuntime']
                                   or certificate['installedFiles'] != receipt['files']):
        raise SystemExit('The swerve certificate must match the installed normal build.')
    expected = {entry['file']: entry for entry in receipt['files']}
    if len(expected) != 6 or set(expected) != {p.name for p in args.platform.iterdir() if p.is_file()}:
        raise SystemExit('The verified platform must contain exactly the six recorded files.')
    for name, entry in expected.items():
        payload = (args.platform / name).read_bytes()
        if len(payload) != entry['bytes'] or digest(payload) != entry['sha256'].lower():
            raise SystemExit(f'The verified build does not match: {name}')
    source = ROOT / 'Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744'
    for name, sha256 in certificate['sourceSha256'].items():
        if digest((source / name).read_bytes()) != sha256:
            raise SystemExit(f'The runtime source changed after verification: {name}')

    for filename, sha256 in certificate.get('evidenceSha256', {}).items():
        if digest((evidence / filename).read_bytes()) != sha256:
            raise SystemExit(f'The verification evidence changed: {filename}')
    if args.profile == 'swerve':
        probes = load(evidence / 'swerve.json')
        shooting = load(evidence / 'partial-shooting.json')
        for report in [probes, shooting]:
            if (not report['passed']
                    or report['bundleSha256'] != expected['rebuilt_ninjas_4744_turbo_2026_assets']['sha256'].lower()
                    or report['runtimeDllSha256'] != expected['Ninjas4744.dll']['sha256'].lower()):
                raise SystemExit('Swerve and shooting reports must pass for this exact build.')
        if (len(probes['cases']) != 40
                or not all(c['blocked'] and c['touched'] and c['pairSolid'] for c in probes['cases'])
                or not probes['fuelExcludedFromTraction'] or not probes['clearFloorStable']
                or sum(c['seeded'] for c in shooting['cases']) != 3
                or sum(c['scored'] for c in shooting['cases']) != 3):
            raise SystemExit('All forty collision probes, driving and three-ball shooting must pass.')
        summary = {'moduleHousings': 4, 'collisionProbes': 40,
                   'clearFloorDriveMetres': probes['clearFloorDistance'],
                   'threeBallsScored': 3, 'emptyFloorTrenchCases': 8}
        profile_info = {'swerveChecks': summary}
        evidence_files = ['swerve.json', 'partial-shooting.json', 'trench.json']
        trench_checks = [('trench.json', 8)]
        validation_notes = (
            'Includes four solid swerve housings and physical tire-to-ball collisions, '
            'alongside the sliding hopper and bellypan.\n'
            'Forty collision probes, normal driving, three-ball shooting and eight '
            'empty-floor trench geometry cases passed on this exact packaged build.\n'
        )
    else:
        native = load(evidence / 'native-three-balls.json')
        summary = load(evidence / 'native-demo-summary.json')
        if not (native['completed'] and native['peakCaptured'] == 3
                and native['scoredCaptured'] == 3 and native['remainingCaptured'] == 0
                and len(native['launches']) == 3 and summary['passed']):
            raise SystemExit('The native three-ball shooting check is not passing.')
        profile_info = {'nativeShooting': summary, 'fieldTrenchCases': 8, 'loadedAndEmptyPinchCases': 4}
        evidence_files = ['native-demo-summary.json', 'trench.json', 'trench-rigid-pinch.json']
        trench_checks = [('trench.json', 8), ('trench-rigid-pinch.json', 4)]
        validation_notes = (
            'Includes the sliding hopper, automatic shooting/delivery pumping, '
            'bellypan contacts and trench contact fixes.\n'
            'Native verification scored all three balls. Eight field trench cases and '
            'four loaded/empty extended-hopper pinch cases passed.\n'
        )
    for filename, count in trench_checks:
        report = load(evidence / filename)
        if not report['passed'] or report['errors'] or len(report['cases']) != count:
            raise SystemExit(f'The packaged trench check is not passing: {filename}')
        if not all(case['escaped'] for case in report['cases']):
            raise SystemExit(f'A trench case did not escape: {filename}')
        if args.profile == 'swerve' and not report['isolatedFieldFuel']:
            raise SystemExit('The swerve trench geometry report must identify its empty-floor scope.')
    if args.check_only:
        print(f'Verified six build files, runtime source fingerprints and {args.profile} test evidence.')
        return
    if git('status', '--porcelain'):
        raise SystemExit('Commit the source and verification records before creating the archive.')

    manifest = load(args.platform / 'modpack.json')
    build = {
        'version': manifest['version'], 'buildLabel': args.label,
        'sourceCommit': git('rev-parse', 'HEAD'), 'sourceBranch': git('branch', '--show-current'),
        'validation': 'Focused test build; full release certification is separate.',
        'verificationProfile': args.profile,
        **profile_info,
        'normalRuntime': True, 'files': list(expected.values()),
        'limitations': certificate['limitations'],
    }
    notes = (
        f"Ninjas 4744 Turbo 2026 {manifest['version']} / {args.label}\n\n"
        + validation_notes +
        'Earlier fixed-bin full-load stow certification does not cover this sliding bin. '
        'This archive does not claim a new full release certification.\n\n'
        'Copy the Ninjas4744-Turbo2026 folder into MoSim/Mods, replacing the previous copy. '
        'Close MoSim before replacing mod files. See INSTALL-he.md for the full installation guide.\n'
    )
    prefix = 'Ninjas4744-Turbo2026/'
    payloads = {prefix + 'standalonewindows64/' + name: (args.platform / name).read_bytes() for name in expected}
    for name in ['README.md', 'CHANGELOG.md']:
        payloads[prefix + name] = (ROOT / name).read_bytes()
    payloads[prefix + 'INSTALL-he.md'] = (ROOT / 'Docs/Installation-Discord-he.md').read_bytes()
    payloads[prefix + 'BUILD-NOTES.txt'] = notes.encode('utf-8')
    payloads[prefix + 'BUILD-INFO.json'] = (json.dumps(build, indent=2) + '\n').encode('utf-8')
    for filename in evidence_files:
        payloads[prefix + 'Verification/' + filename] = (evidence / filename).read_bytes()
    dist = ROOT / 'dist'
    dist.mkdir(exist_ok=True)
    target = dist / f"Ninjas4744-Turbo2026-{manifest['version']}-{args.label}-Windows.zip"
    with tempfile.NamedTemporaryFile(dir=dist, prefix=target.stem + '-', suffix='.tmp', delete=False) as temp:
        temporary = Path(temp.name)
    try:
        with zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED) as archive:
            for name, data in payloads.items():
                archive.writestr(name, data)
        with zipfile.ZipFile(temporary) as archive:
            if archive.testzip() is not None or set(archive.namelist()) != set(payloads):
                raise SystemExit('Archive integrity check failed.')
            for name, data in payloads.items():
                if digest(archive.read(name)) != digest(data):
                    raise SystemExit(f'Archive entry mismatch: {name}')
        temporary.replace(target)
    finally:
        temporary.unlink(missing_ok=True)
    checksum = digest(target.read_bytes())
    target.with_suffix('.sha256').write_text(f'{checksum}  {target.name}\n', encoding='utf-8')
    print(target)
    print(f'SHA-256 {checksum}')
    print(f'Verified all {len(payloads)} archive entries; runtime matches the tested normal mod.')


if __name__ == '__main__':
    main()
