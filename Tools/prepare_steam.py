"""Stage a checked Windows update with the existing Steam Linux/macOS binaries."""
import argparse
import hashlib
import json
import shutil
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PLATFORMS = ('standalonewindows64', 'standalonelinux64', 'standaloneosx')
MOD_ID = 'rebuilt_ninjas_4744_turbo_2026'
parser = argparse.ArgumentParser()
parser.add_argument('--project', type=Path, required=True)
parser.add_argument('--candidate', action='store_true',
                    help='Package a documented held candidate outside the uploader staging folder')
parser.add_argument('--steam-item', type=Path, default=Path(
    'C:/Program Files (x86)/Steam/steamapps/workshop/content/4398690/3843685878'))
args = parser.parse_args()
project = args.project.resolve()
load = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
build = project / 'BuiltMods' / MOD_ID
manifest = load(build / PLATFORMS[0] / 'modpack.json')
version = manifest['version']
assert version == '1.0.1', 'This release preparation targets 1.0.1'
hashes = {n: digest(build / PLATFORMS[0] / n)
          for n in (manifest['modDll'], manifest['id'])}
certificate = load(ROOT / f'Reports/verification-{version}.json')
assert certificate['bundle_sha256'] == hashes[manifest['id']]
assert certificate['runtime_dll_sha256'] == hashes[manifest['modDll']]
assert len(certificate['fixtures']) == 14
assert all(f['passed'] and f['embedded_hashes_match'] for f in certificate['fixtures'])
controls = load(ROOT / 'Reports/simple-controls.json')
assert controls['passed'] and len(controls['checks']) >= 34 and not controls['errors']
assert controls['bundleSha256'] == hashes[manifest['id']]
assert controls['runtimeDllSha256'] == hashes[manifest['modDll']]
net = load(ROOT / 'Reports/Release-1.0.1/net-collision-burst.json')
assert net['passed'] and net['burstEnabled'] and net['comparedSteps'] == 480
if args.candidate:
    status = load(ROOT / f'Reports/release-status-{version}.json')
    assert status['release_ready'] is False and status['blocking_checks']
    assert status['artifacts'] == hashes, 'Stale candidate status'
else:
    for name in ('native-trench', 'native-bump'):
        report = load(ROOT / f'Reports/{name}-{version}.json')
        assert report['passed'] and report['artifacts'] == hashes, f'Stale {name}'

baseline = load(ROOT / 'Reports/Release-1.0.1/preserved-platform-source.json')['files']
receipt = {'version': version, 'workshop_item': '3843685878', 'release_ready': not args.candidate,
           'policy': 'New Windows build; unchanged Linux/macOS binaries and bundles. '
                     'Only the preserved platform manifest version labels change.',
           'files': {}}
for platform in PLATFORMS:
    source = build / platform if platform == PLATFORMS[0] else args.steam_item / platform
    target = build / platform
    # Match the official staging utility, which omits Unity's text sidecars.
    expected = {p.relative_to(source) for p in source.rglob('*')
                if p.is_file() and p.suffix != '.manifest'}
    assert expected and Path('modpack.json') in expected
    original_manifest = load(source / 'modpack.json')
    assert original_manifest['modpack']['metadataGuid'] == manifest['modpack']['metadataGuid']
    for relative in sorted(expected):
        original = source / relative
        destination = target / relative
        before = digest(original)
        if platform != PLATFORMS[0]:
            assert before == baseline[f'{platform}/{relative.as_posix()}'], 'Original Steam platform changed'
        destination.parent.mkdir(parents=True, exist_ok=True)
        if destination.resolve() != original.resolve():
            shutil.copy2(original, destination)
        if relative == Path('modpack.json') and platform != PLATFORMS[0]:
            preserved = load(destination)
            preserved['version'] = version
            destination.write_text(json.dumps(preserved, indent=2) + '\n', encoding='utf-8')
        after = digest(destination)
        if relative != Path('modpack.json'):
            assert before == after, f'Preserved binary changed: {platform}/{relative}'
        receipt['files'][f'{platform}/{relative.as_posix()}'] = {
            'sha256': after, 'source_sha256': before,
            'preserved': platform != PLATFORMS[0],
            'manifest_version_only': relative == Path('modpack.json') and platform != PLATFORMS[0]}
    assert {p.relative_to(target) for p in target.rglob('*')
            if p.is_file() and p.suffix != '.manifest'} == expected

stage = ROOT / f'dist/steam-candidate-{version}/Content' if args.candidate \
    else project / 'BuiltMods/_Staging' / MOD_ID
stage.mkdir(parents=True, exist_ok=True)
for relative, record in receipt['files'].items():
    destination = stage / relative
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(build / relative, destination)
    assert digest(destination) == record['sha256']
assert {p.relative_to(stage).as_posix() for p in stage.rglob('*') if p.is_file()} == set(receipt['files'])
receipt['staging_folder'] = str(stage)
suffix = '-candidate' if args.candidate else ''
reports = ROOT / f'Reports/steam{suffix}-{version}.json'
reports.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
ready = {'release_ready': not args.candidate, 'version': version, 'artifacts': hashes}
(project / 'BuildArtifacts/steam-release-status.json').write_text(json.dumps(ready, indent=2) + '\n')
output = ROOT / f'dist/Ninjas4744-Turbo2026-{version}-Steam{suffix}.zip'
output.parent.mkdir(exist_ok=True)
with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
    for relative in sorted(receipt['files']):
        archive.write(stage / relative, ('Content/' if args.candidate else '') + relative)
    expected_entries = {('Content/' if args.candidate else '') + p for p in receipt['files']}
    if args.candidate:
        archive.write(ROOT / f'Reports/release-status-{version}.json', 'RELEASE-STATUS.json')
        archive.write(ROOT / 'Docs/Steam-1.0.1.md', 'UPLOAD.md')
        expected_entries.update(('RELEASE-STATUS.json', 'UPLOAD.md'))
    assert set(archive.namelist()) == expected_entries
with zipfile.ZipFile(output, 'r') as archive:
    assert archive.testzip() is None
    for relative, record in receipt['files'].items():
        entry = ('Content/' if args.candidate else '') + relative
        assert hashlib.sha256(archive.read(entry)).hexdigest() == record['sha256']
receipt['archive'] = {'filename': output.name, 'sha256': digest(output)}
reports.write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'staging_folder': str(stage), 'zip': str(output),
                  'sha256': digest(output), 'files': len(receipt['files'])}, indent=2))
