"""Back up, stage and restore the unchanged robot builds for local CPU-budget tests."""
import argparse
import hashlib
import json
import os
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Artifacts/WeakCpuComparison'
DATA = Path(os.environ['USERPROFILE']) / 'AppData/LocalLow/CascadeStudios/MoSim'
TARGET = DATA / 'Mods/Ninjas4744-Turbo2026'
HELPER = DATA / 'Mods/Ninjas4744-WeakCpuObserver'
STEAM = Path('C:/Program Files (x86)/Steam/steamapps/workshop/content/4398690/3843685878/standalonewindows64')
PROJECT = ROOT / 'Artifacts/PumpingProject-20261121'
FILES = ['AppSettings.json', 'MatchSettings.json', 'Turbo4744-native-replay.json', 'Turbo4744-weak-cpu.json']
load = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def prepare():
    if (OUT / 'session.json').exists() or HELPER.exists():
        raise SystemExit('A benchmark session or helper installation already exists')
    OUT.mkdir(parents=True, exist_ok=True)
    shutil.copytree(TARGET, OUT / 'backup/mod')
    existed = {}
    for name in FILES:
        existed[name] = (DATA / name).exists()
        if existed[name]:
            shutil.copy2(DATA / name, OUT / 'backup' / name)
    helper_build = Path((PROJECT / 'BuildArtifacts/WeakCpuObserver/build-path.txt').read_text().strip())
    shutil.copytree(helper_build / 'standalonewindows64', HELPER / 'standalonewindows64')
    helper_manifest = load(HELPER / 'standalonewindows64/modpack.json')
    state = dict(existed=existed, helperGuid=helper_manifest['modpack']['metadataGuid'], runs=[], restored=False,
                 steamSources={p.name: digest(p) for p in STEAM.iterdir() if p.is_file()})
    write(OUT / 'session.json', state)
    print('Backed up installed trial/settings and installed the temporary observer.')


def stage(build, label):
    state = load(OUT / 'session.json')
    if state['restored']:
        raise SystemExit('Session was already restored')
    if any(r['label'] == label for r in state['runs']):
        raise SystemExit('Choose a fresh run label')
    source = STEAM if build == 'steam' else OUT / 'backup/mod/standalonewindows64'
    target = TARGET / 'standalonewindows64'
    for p in source.iterdir():
        if p.is_file():
            shutil.copy2(p, target / p.name)
            if digest(p) != digest(target / p.name):
                raise SystemExit('Installed source mismatch')
    match = load(OUT / 'backup/MatchSettings.json')
    player = match['playerSettings'][0]
    player.update(alliance=0, cameraMode=0, robotVariantIndex=0 if build == 'steam' else 1,
                  modpackGuid='651ec15a-e146-13ad-192f-f3dea3d0315a', robotGuid='052ab25d-f613-7169-5bec-b019de26f506')
    # Use the previously recorded successful native collection start and ordinary keys.
    spawn = dict(position=dict(x=3.8595166, y=0, z=3.3758280),
                 rotation=dict(x=0, y=0.70677036, z=0, w=-0.70744306))
    key = f"{player['robotVariantIndex']}:{player['robotGuid']}"
    entries = [entry for entry in player['RebuiltSpawnpoints'] if entry['key'] != key]
    entries.append(dict(key=key, spawn=spawn))
    player['RebuiltSpawnpoints'] = entries
    write(DATA / 'MatchSettings.json', match)
    settings = load(OUT / 'backup/AppSettings.json')
    settings.update(FpsLimitIndex=4, QualityLevel=0, ResolutionWidth=1920, ResolutionHeight=1080)
    write(DATA / 'AppSettings.json', settings)
    request = dict(label=label, collectSeconds=12, retreatSeconds=0, shootWhileRetreatingSeconds=.25,
                   observeSeconds=90, strafeInSeconds=4, strafeOutSeconds=0, returnToOwnZone=False,
                   shootKey='E', hardPumpAfterSeconds=5, waitForTeleop=False, targetCaptured=58,
                   collectGear=1, returnGear=1, waitForActiveHub=False, collectionSettleSeconds=300,
                   repeatHardPumpEverySeconds=10, stowForReturn=False,
                   collectionBackoffAfterSeconds=4, collectionBackoffSeconds=1.7, collectionPumpAfterSeconds=10)
    config = dict(label=label, build=build, outputDirectory=str(OUT.resolve()), minimumFuel=55,
                  profileSeconds=14, warmupSeconds=2)
    write(DATA / 'Turbo4744-native-replay.json', request)
    write(DATA / 'Turbo4744-weak-cpu.json', config)
    write(OUT / (label + '-request.json'), request)
    write(OUT / (label + '-config.json'), config)
    manifest = load(target / 'modpack.json')
    receipt = dict(label=label, build=build, version=manifest['version'],
                   bundleSha256=digest(target / manifest['id']), runtimeDllSha256=digest(target / manifest['modDll']),
                   helperDllSha256=digest(HELPER / 'standalonewindows64/Ninjas4744WeakCpuObserver.dll'))
    write(OUT / (label + '-receipt.json'), receipt)
    state['runs'].append(receipt)
    write(OUT / 'session.json', state)
    print(json.dumps(receipt, indent=2))


def restore():
    state = load(OUT / 'session.json')
    for p in (OUT / 'backup/mod').rglob('*'):
        if p.is_file():
            destination = TARGET / p.relative_to(OUT / 'backup/mod')
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(p, destination)
            if digest(p) != digest(destination):
                raise SystemExit('Restore mismatch: ' + str(destination))
    for name, existed in state['existed'].items():
        if existed:
            shutil.copy2(OUT / 'backup' / name, DATA / name)
            if digest(DATA / name) != digest(OUT / 'backup' / name):
                raise SystemExit('Settings restore mismatch')
        else:
            (DATA / name).unlink(missing_ok=True)
    # Only move the exact temporary helper directory this script installed.
    expected = (DATA / 'Mods/Ninjas4744-WeakCpuObserver').resolve()
    if HELPER.resolve() != expected or not HELPER.resolve().is_relative_to((DATA / 'Mods').resolve()):
        raise SystemExit('Unexpected helper cleanup path')
    if HELPER.exists():
        shutil.move(str(HELPER), str(OUT / 'removed-observer'))
    for name, original in state['steamSources'].items():
        if digest(STEAM / name) != original:
            raise SystemExit('Steam source changed during testing')
    state['restored'] = True
    write(OUT / 'session.json', state)
    print('Installed trial and original settings restored; helper and requests removed. Steam originals unchanged.')


parser = argparse.ArgumentParser()
parser.add_argument('action', choices=['prepare', 'stage', 'restore'])
parser.add_argument('--build', choices=['steam', 'trial'])
parser.add_argument('--label')
args = parser.parse_args()
if args.action == 'prepare': prepare()
elif args.action == 'stage': stage(args.build, args.label)
else: restore()
