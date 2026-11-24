"""Certify and stage fresh Windows, Linux and macOS content for Steam 1.0.3."""
import argparse
import json
import shutil
import subprocess
import zipfile
from pathlib import Path
from clearance_release_checks import MOD, PLATFORMS, load, sha, verify

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--project', type=Path, required=True)
parser.add_argument('--check-only', action='store_true')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
project = args.project.resolve()
certificate = verify(root, project)
if args.check_only:
    print(json.dumps({'passed': True, 'version': '1.0.3', 'platforms': list(PLATFORMS),
                      'payload_files': len(certificate['files'])}, indent=2))
    raise SystemExit(0)
release = root / 'Reports/Release-1.0.3'
certificate['code_commit'] = subprocess.check_output(
    ['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
(release / 'verification.json').write_text(json.dumps(certificate, indent=2) + '\n',
                                         encoding='utf-8', newline='\n')
stage = project / 'BuiltMods/_Staging' / MOD
stage.mkdir(parents=True, exist_ok=True)
for relative, digest in certificate['files'].items():
    source = project / 'BuiltMods' / MOD / relative
    target = stage / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
    assert sha(target) == digest
assert {p.relative_to(stage).as_posix() for p in stage.rglob('*') if p.is_file()} == set(certificate['files'])
status = {'release_ready': True, 'version': '1.0.3',
          'files': [{'relativePath': n, 'sha256': h} for n, h in sorted(certificate['files'].items())]}
(project / 'BuildArtifacts/steam-release-status.json').write_text(
    json.dumps(status, indent=2) + '\n', encoding='utf-8', newline='\n')
archive = root / 'dist/Ninjas4744-Turbo2026-1.0.3-Steam.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
    for relative in sorted(certificate['files']):
        z.write(stage / relative, relative)
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None and set(z.namelist()) == set(certificate['files'])
    import hashlib
    assert all(hashlib.sha256(z.read(n)).hexdigest() == h for n, h in certificate['files'].items())
receipt = {'version': '1.0.3', 'workshop_item': '3843685878', 'release_ready': True,
           'code_commit': certificate['code_commit'],
           'policy': 'Fresh builds for Windows, Linux and macOS, sharing the approved recovery code.',
           'staging_folder': str(stage), 'files': certificate['files'],
           'archive': {'filename': archive.name, 'sha256': sha(archive)}}
(root / 'Reports/steam-1.0.3.json').write_text(json.dumps(receipt, indent=2) + '\n',
                                            encoding='utf-8', newline='\n')
print(json.dumps({'passed': True, 'files': len(certificate['files']),
                  'archive': str(archive), 'sha256': sha(archive)}, indent=2))
