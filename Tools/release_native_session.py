"""Preserve user settings while testing the final release in installed MoSim."""
import argparse
import hashlib
import json
import os
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Artifacts/Release101/native-session'
DATA = Path(os.environ['USERPROFILE']) / 'AppData/LocalLow/CascadeStudios/MoSim'
TARGET = DATA / 'Mods/Ninjas4744-Turbo2026'
NAMES = ('AppSettings.json', 'MatchSettings.json', 'Turbo4744-native-replay.json')
load = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()


def write(path, data):
    path.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')


def prepare():
    assert not (OUT / 'session.json').exists(), 'Session already exists'
    OUT.mkdir(parents=True, exist_ok=True)
    shutil.copytree(TARGET, OUT / 'backup/mod')
    existed = {name: (DATA / name).exists() for name in NAMES}
    for name in NAMES:
        if existed[name]:
            shutil.copy2(DATA / name, OUT / 'backup' / name)
    platform = ROOT / 'Artifacts/PumpingProject-20261121/BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64'
    manifest = load(platform / 'modpack.json')
    assert manifest['version'] == '1.0.1'
    files = {p.name: digest(p) for p in platform.iterdir() if p.is_file()}
    for name, sha in files.items():
        shutil.copy2(platform / name, TARGET / 'standalonewindows64' / name)
        assert digest(TARGET / 'standalonewindows64' / name) == sha
    for name in ('README.md', 'CHANGELOG.md'):
        shutil.copy2(ROOT / name, TARGET / name)
    write(OUT / 'session.json', {'existed': existed, 'release_files': files, 'restored': False})
    print('Backed up the trial and settings; installed the exact 1.0.1 Windows build.')


def request(template, label, alliance, variant):
    state = load(OUT / 'session.json')
    assert not state['restored']
    match = load(OUT / 'backup/MatchSettings.json')
    player = match['playerSettings'][0]
    player.update(alliance=alliance, cameraMode=0, robotVariantIndex=variant,
                  modpackGuid='651ec15a-e146-13ad-192f-f3dea3d0315a',
                  robotGuid='052ab25d-f613-7169-5bec-b019de26f506')
    key = str(variant) + ':' + player['robotGuid']
    player['RebuiltSpawnpoints'] = [s for s in player['RebuiltSpawnpoints'] if s['key'] != key]
    player['RebuiltSpawnpoints'].append({'key': key, 'spawn': {
        'position': {'x': 3.8595166, 'y': 0, 'z': 3.3758280},
        'rotation': {'x': 0, 'y': .70677036, 'z': 0, 'w': -.70744306}}})
    write(DATA / 'MatchSettings.json', match)
    settings = load(OUT / 'backup/AppSettings.json')
    settings.update(FpsLimitIndex=4, QualityLevel=0, ResolutionWidth=1920, ResolutionHeight=1080)
    write(DATA / 'AppSettings.json', settings)
    q = load(template)
    q['label'] = label
    q['returnLane'] = abs(q['returnLane']) * (1 if alliance == 0 else -1)
    write(DATA / 'Turbo4744-native-replay.json', q)
    write(OUT / (label + '-request.json'), q)
    print('Staged ordinary input cycle:', label)


def restore():
    state = load(OUT / 'session.json')
    for name, existed in state['existed'].items():
        if existed:
            shutil.copy2(OUT / 'backup' / name, DATA / name)
            assert digest(DATA / name) == digest(OUT / 'backup' / name)
        else:
            (DATA / name).unlink(missing_ok=True)
    for name, sha in state['release_files'].items():
        assert digest(TARGET / 'standalonewindows64' / name) == sha
    state['restored'] = True
    write(OUT / 'session.json', state)
    print('Restored user settings and removed the test request; 1.0.1 remains installed.')


parser = argparse.ArgumentParser()
parser.add_argument('action', choices=('prepare', 'request', 'restore'))
parser.add_argument('--session-dir', type=Path, default=OUT)
parser.add_argument('--template', type=Path)
parser.add_argument('--label')
parser.add_argument('--alliance', type=int, choices=(0, 1), default=0)
parser.add_argument('--variant', type=int, choices=(0, 1), default=1)
args = parser.parse_args()
OUT = args.session_dir.resolve()
assert OUT.is_relative_to((ROOT / 'Artifacts').resolve()), 'Session backup must remain under workspace Artifacts'
if args.action == 'prepare':
    prepare()
elif args.action == 'request':
    request(args.template, args.label, args.alliance, args.variant)
else:
    restore()
