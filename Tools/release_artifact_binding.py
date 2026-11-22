"""Accept exact artifacts or a narrowly verified selector-art-only bundle change."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROOF = ROOT / 'Reports/selector-artwork-equivalence-1.0.1.json'


def bind_artifacts(recorded, current):
    if recorded == current:
        return None
    proof = json.loads(PROOF.read_text(encoding='utf-8-sig'))
    assert proof['passed'] and proof['scope'] == 'selector-artwork-only'
    assert proof['runtime_dll_identical']
    assert proof['before_artifacts'] == recorded and proof['after_artifacts'] == current
    assert recorded['Ninjas4744.dll'] == current['Ninjas4744.dll']
    changed = proof['changed_objects']
    assert len(changed) == 2 and {c['type'] for c in changed} == {'Sprite', 'Texture2D'}
    assert all(c['name'] == 'selector-logo' for c in changed)
    assert proof['object_count'] == proof['unchanged_object_count'] + 2
    assert proof['unchanged_object_count'] > 2500
    return PROOF.name
