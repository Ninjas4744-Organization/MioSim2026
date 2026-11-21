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
    parser.add_argument('--check-only', action='store_true')
    args = parser.parse_args()
    if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9._-]*', args.label):
        raise SystemExit('Use a simple filename label.')

    certificate = load(args.verification)
    receipt = certificate['installedNormalMod']
    if not certificate['passed'] or not receipt['normalRuntime']:
        raise SystemExit('A passing verification of the normal runtime is required.')
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

    evidence = args.verification.parent
    native = load(evidence / 'native-three-balls.json')
    summary = load(evidence / 'native-demo-summary.json')
    if not (native['completed'] and native['peakCaptured'] == 3
            and native['scoredCaptured'] == 3 and native['remainingCaptured'] == 0
            and len(native['launches']) == 3 and summary['passed']):
        raise SystemExit('The native three-ball shooting check is not passing.')
    for filename, count in [('trench.json', 8), ('trench-rigid-pinch.json', 4)]:
        report = load(evidence / filename)
        if not report['passed'] or report['errors'] or len(report['cases']) != count:
            raise SystemExit(f'The packaged trench check is not passing: {filename}')
        if not all(case['escaped'] for case in report['cases']):
            raise SystemExit(f'A trench case did not escape: {filename}')
    if args.check_only:
        print('Verified six build files, runtime source fingerprints, native shooting and twelve trench/pinch cases.')
        return
    if git('status', '--porcelain'):
        raise SystemExit('Commit the source and verification records before creating the archive.')

    manifest = load(args.platform / 'modpack.json')
    build = {
        'version': manifest['version'], 'buildLabel': args.label,
        'sourceCommit': git('rev-parse', 'HEAD'), 'sourceBranch': git('branch', '--show-current'),
        'validation': 'Focused test build; full release certification is separate.',
        'nativeShooting': summary, 'fieldTrenchCases': 8, 'loadedAndEmptyPinchCases': 4,
        'normalRuntime': True, 'files': list(expected.values()),
        'limitations': certificate['limitations'],
    }
    notes = (
        f"Ninjas 4744 Turbo 2026 {manifest['version']} / {args.label}\n\n"
        'This is the normal mod from the verified MoSim test. It contains the sliding hopper, '
        'automatic shooting/delivery pumping, bellypan contacts and trench contact fixes.\n'
        'Native verification scored all three balls. Eight field trench cases and four '
        'loaded/empty extended-hopper pinch cases passed.\n'
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
    for filename in ['native-demo-summary.json', 'trench.json', 'trench-rigid-pinch.json']:
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
