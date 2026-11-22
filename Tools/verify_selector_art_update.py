"""Prove a selector-art update leaves every gameplay object and resource identical.

Requires UnityPy 1.25.4 (installed locally under Artifacts/Release101/unitypy). Does not
edit either bundle, images, raw test reports or installed-artifact receipts.
"""
import argparse
import hashlib
import json
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'Artifacts/Release101/unitypy'))
import UnityPy
from UnityPy.helpers.ResourceReader import get_resource_data

assert UnityPy.__version__ == '1.25.4'
parser = argparse.ArgumentParser()
parser.add_argument('--before', type=Path, required=True)
parser.add_argument('--project', type=Path, required=True)
args = parser.parse_args()
platform = args.project.resolve() / 'BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64'
load = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
digest = lambda data: hashlib.sha256(data).hexdigest()
manifest = load(platform / 'modpack.json')
after = platform / manifest['id']
baseline = load(ROOT / 'Reports/native-full-hopper-1.0.1.json')
tested = baseline.get('tested_artifacts', baseline['artifacts'])
assert digest(args.before.read_bytes()) == tested[manifest['id']], 'Wrong tested baseline bundle'
assert digest((platform / manifest['modDll']).read_bytes()) == tested[manifest['modDll']], 'Runtime DLL changed'
before_env, after_env = UnityPy.load(str(args.before)), UnityPy.load(str(after))
before = {o.path_id: o for o in before_env.objects}
current = {o.path_id: o for o in after_env.objects}
assert before.keys() == current.keys(), 'Objects added or removed'
assert len(before_env.assets) == len(after_env.assets) == 1
assert before_env.assets[0].unity_version == after_env.assets[0].unity_version
assert [x.path for x in before_env.assets[0].externals] == [x.path for x in after_env.assets[0].externals]


def normalize(value, obj):
    if isinstance(value, dict):
        if set(value) == {'offset', 'size', 'path'} and value['path']:
            payload = get_resource_data(value['path'], obj.assets_file, value['offset'], value['size'])
            return {'resource_size': len(payload), 'resource_sha256': digest(payload)}
        return {k: normalize(v, obj) for k, v in value.items()}
    if isinstance(value, list):
        return [normalize(v, obj) for v in value]
    if isinstance(value, bytes):
        return {'byte_count': len(value), 'sha256': digest(value)}
    return value


def payload(obj):
    # These are the only resource-stream-bearing classes in this bundle. All
    # other classes (including colliders, joints, scripts and native code) are
    # compared byte for byte, preserving every serialized pointer and setting.
    if obj.type.name in ('Texture2D', 'Mesh'):
        return json.dumps(normalize(obj.read_typetree(), obj), sort_keys=True,
                          separators=(',', ':')).encode('utf-8')
    return obj.get_raw_data()


types = Counter(o.type.name for o in before.values())
expected_types = {'GameObject', 'MeshFilter', 'Transform', 'MeshRenderer', 'Material', 'Shader',
                  'BoxCollider', 'Texture2D', 'MonoBehaviour', 'WheelCollider', 'CapsuleCollider',
                  'SphereCollider', 'Mesh', 'MonoScript', 'CanvasRenderer', 'PhysicMaterial',
                  'Sprite', 'MeshCollider', 'ConfigurableJoint', 'RectTransform', 'Rigidbody',
                  'TextAsset', 'AssetBundle', 'Animator'}
assert set(types) == expected_types, 'Review resource handling for an unexpected asset class'
changed = []
unchanged = hashlib.sha256()
for key in sorted(before):
    a, b = before[key], current[key]
    assert a.type.name == b.type.name
    pa, pb = payload(a), payload(b)
    if pa == pb:
        unchanged.update(f'{key}:{a.type.name}:'.encode() + hashlib.sha256(pa).digest())
        continue
    name = b.read_typetree().get('m_Name', '')
    assert a.type.name in ('Texture2D', 'Sprite') and name == 'selector-logo', f'Gameplay object changed: {key} {a.type.name} {name}'
    assert a.read_typetree()['m_Name'] == name
    changed.append({'path_id': key, 'type': a.type.name, 'name': name,
                    'before_sha256': digest(pa), 'after_sha256': digest(pb)})
assert {c['type'] for c in changed} == {'Sprite', 'Texture2D'} and len(changed) == 2
proof = {'passed': True, 'scope': 'selector-artwork-only', 'unitypy_version': UnityPy.__version__,
         'before_artifacts': tested,
         'after_artifacts': {manifest['modDll']: digest((platform / manifest['modDll']).read_bytes()),
                             manifest['id']: digest(after.read_bytes())},
         'runtime_dll_identical': True, 'object_count': len(before),
         'unchanged_object_count': len(before) - len(changed),
         'unchanged_objects_sha256': unchanged.hexdigest(), 'object_types': dict(types),
         'changed_objects': changed,
         'resource_comparison': 'Every Mesh and Texture2D streamed payload compared by content, resolving transport offsets/paths; all other objects compared as exact serialized bytes.'}
output = ROOT / 'Reports/selector-artwork-equivalence-1.0.1.json'
output.write_text(json.dumps(proof, indent=2) + '\n', encoding='utf-8')
print(json.dumps(proof, indent=2))
