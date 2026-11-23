"""Package the certified Windows 1.0.2 patch without changing release 1.0.1."""
import argparse
import hashlib
import json
import zipfile
from pathlib import Path
from patch_release_checks import verify_patch_checks

parser = argparse.ArgumentParser()
parser.add_argument('--project', type=Path, required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
project = args.project.resolve()
certificate = verify_patch_checks(root, project)
platform = project / 'BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64'
prefix = 'Ninjas4744-Turbo2026/'
files = {prefix + 'standalonewindows64/' + p.name: p
         for p in platform.iterdir() if p.is_file()}
assert len(files) == 6
files.update({prefix + n: root / n for n in ['README.md', 'CHANGELOG.md']})
files[prefix + 'INSTALL-he.md'] = root / 'Docs/Installation-Discord-he.md'
output = root / 'dist/Ninjas4744-Turbo2026-1.0.2-Windows.zip'
with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
    for name, source in sorted(files.items()):
        archive.write(source, name)
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
with zipfile.ZipFile(output) as archive:
    assert archive.testzip() is None and set(archive.namelist()) == set(files)
    for name, source in files.items():
        assert hashlib.sha256(archive.read(name)).hexdigest() == digest(source)
receipt = {'version': '1.0.2', 'archive': output.name,
           'sha256': digest(output), 'artifacts': certificate['artifacts'],
           'files': {name: digest(p) for name, p in sorted(files.items())}}
(root / 'Reports/package-1.0.2.json').write_text(json.dumps(receipt, indent=2) + '\n')
print(output)
