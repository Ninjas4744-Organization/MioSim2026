"""Audit all three newly built bundles against their Windows gameplay data."""
import argparse
import hashlib
import json
import sys
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--project', type=Path, required=True)
parser.add_argument('--module-path', type=Path, required=True)
parser.add_argument('--report', type=Path, required=True)
args = parser.parse_args()
sys.path.insert(0, str(args.module_path.resolve()))
import UnityPy

root = args.project / 'BuiltMods/rebuilt_ninjas_4744_turbo_2026'
targets = {'standalonewindows64': 19, 'standalonelinux64': 24, 'standaloneosx': 2}
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
results = {}
baseline = None
runtime = sha(root / 'standalonewindows64/Ninjas4744.dll')
guid = None
for platform, target in targets.items():
    folder = root / platform
    manifest = json.loads((folder / 'modpack.json').read_text(encoding='utf-8-sig'))
    assert manifest['version'] == '1.0.3'
    assert manifest['modDll'] == 'Ninjas4744.dll'
    assert sha(folder / manifest['modDll']) == runtime
    current_guid = manifest['modpack']['metadataGuid']
    guid = guid or current_guid
    assert current_guid == guid
    path = folder / manifest['id']
    objects = list(UnityPy.load(str(path)).objects)
    assert objects and all(int(o.assets_file.target_platform) == target for o in objects)
    values = {(o.type.name, o.path_id): o.read_typetree() for o in objects}
    shaders = [v for (kind, _), v in values.items() if kind == 'Shader']
    assert shaders and all(v['platforms'] and v['compressedBlob'] for v in shaders)
    packs = [v for (kind, _), v in values.items()
             if kind == 'MonoBehaviour' and 'modpackVersion' in v]
    assert len(packs) == 1 and packs[0]['modpackVersion'] == '1.0.3'
    baseline = baseline or values
    assert values.keys() == baseline.keys()
    changed = [k for k in baseline if baseline[k] != values[k]]
    assert all(kind == 'Shader' for kind, _ in changed), 'Non-shader platform data differs'
    results[platform] = {
        'target': target, 'bundle_sha256': sha(path), 'runtime_dll_sha256': runtime,
        'manifest_sha256': sha(folder / 'modpack.json'), 'objects': len(values),
        'identical_non_shader_objects': sum(k[0] != 'Shader' for k in values),
        'platform_shader_objects': len(shaders), 'changed_shader_objects': len(changed),
        'all_gameplay_objects_match_windows': True,
    }
report = {'passed': True, 'version': '1.0.3', 'platforms': results,
          'scope': 'Cross-built target headers, exact shared player assembly, metadata and every non-shader object match. Target-specific shaders contain compiled data. Native macOS/Linux gameplay was not run on this Windows host.'}
args.report.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
print(json.dumps(report, indent=2))
