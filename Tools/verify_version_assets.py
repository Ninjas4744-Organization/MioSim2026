"""Require exact asset identity except the 1.0.1 -> 1.0.2 pack version."""
import argparse
import hashlib
import json
import sys
import struct
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--before', type=Path, required=True)
parser.add_argument('--after', type=Path, required=True)
parser.add_argument('--report', type=Path, required=True)
parser.add_argument('--module-path', type=Path)
args = parser.parse_args()
if args.module_path:
    sys.path.insert(0, str(args.module_path.resolve()))
import UnityPy

digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()


def read(path):
    return {(o.type.name, o.path_id): o.read_typetree()
            for o in UnityPy.load(str(path)).objects}


def sections(data):
    header = struct.unpack_from('<I', data, 60)[0]
    count = struct.unpack_from('<H', data, header + 6)[0]
    optional = struct.unpack_from('<H', data, header + 20)[0]
    result = {}
    for i in range(count):
        offset = header + 24 + optional + 40 * i
        name = data[offset:offset + 8].rstrip(bytes([0])).decode()
        size, start = struct.unpack_from('<II', data, offset + 16)
        result[name] = hashlib.sha256(data[start:start + size]).hexdigest()
    return result

def normalized(data):
    result = bytearray(data)
    h = struct.unpack_from('<I', data, 60)[0]
    result[h + 8:h + 12] = bytes(4)
    n = struct.unpack_from('<H', data, h + 6)[0]
    opt = struct.unpack_from('<H', data, h + 20)[0]
    directories = 112 if struct.unpack_from('<H', data, h + 24)[0] == 523 else 96

    def address(rva):
        for i in range(n):
            p = h + 24 + opt + i * 40
            virtual_size, va, raw_size, raw = struct.unpack_from('<IIII', data, p + 8)
            if va <= rva < va + max(virtual_size, raw_size):
                return raw + rva - va
        raise ValueError('RVA outside PE sections')
    export_rva, export_size = struct.unpack_from('<II', data, h + 24 + directories)
    if export_rva and export_size:
        export = address(export_rva)
        result[export + 4:export + 8] = bytes(4)
        name_rva = struct.unpack_from('<I', data, export + 12)[0]
        name = address(name_rva)
        end = data.index(bytes([0]), name)
        result[name:end] = bytes(end - name)
    rva, size = struct.unpack_from('<II', data, h + 24 + directories + 6 * 8)
    debug = address(rva) if rva and size else 0
    for p in range(debug, debug + size, 28):
        result[p + 4:p + 8] = bytes(4)
        kind = struct.unpack_from('<I', data, p + 12)[0]
        pointer = struct.unpack_from('<I', data, p + 24)[0]
        if kind == 2 and data[pointer:pointer + 4] == b'RSDS':
            result[pointer + 4:pointer + 20] = bytes(16)
    return bytes(result)

before, after = read(args.before), read(args.after)
assert before.keys() == after.keys(), 'Objects were added or removed'
changed = []
for key in before:
    a, b = before[key], after[key]
    if a == b:
        continue
    if key[0] == 'TextAsset' and a.get('m_Name') == 'Ninjas4744NetBurst':
        to_bytes = lambda v: v.encode('utf-8','surrogateescape') if isinstance(v,str) else bytes(v)
        x,y=to_bytes(a['m_Script']),to_bytes(b['m_Script'])
        assert normalized(x)==normalized(y), 'Native net executable changed'
        assert all(sections(x)[n]==sections(y)[n] for n in ('.text','.data','.pdata'))
        changed.append({'type':'TextAsset','id':key[1],'field':'native PE build metadata only'})
        continue
    assert key[0] == 'MonoBehaviour', f'Unexpected changed asset: {key}'
    assert a.get('modpackVersion') == '1.0.1' and b.get('modpackVersion') == '1.0.2'
    a = dict(a, modpackVersion='<release-version>')
    b = dict(b, modpackVersion='<release-version>')
    assert a == b, f'Non-version asset fields changed: {key}'
    changed.append({'type': key[0], 'id': key[1], 'field': 'modpackVersion',
                    'before': '1.0.1', 'after': '1.0.2'})
assert sum(c['field']=='modpackVersion' for c in changed)==1, 'Expected exactly one pack version field change'
result = {'passed': True, 'before_bundle_sha256': digest(args.before),
          'after_bundle_sha256': digest(args.after), 'object_count': len(before),
          'changed': changed, 'unchanged_objects': len(before) - len(changed),
          'native_net_executable_identical': True,
          'scope': 'All scene and physics objects match exactly. Allow only the pack version field and native PE build metadata; native executable/data sections match and exported function names are not normalized.'}
args.report.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result, indent=2))
