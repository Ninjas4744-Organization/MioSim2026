"""Preserve an observed installed-player replay and its exact artifact fingerprints."""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--label', required=True)
parser.add_argument('--request', type=Path, required=True)
parser.add_argument('--require-trench', action='store_true')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
player = Path.home() / 'AppData/LocalLow/CascadeStudios/MoSim'
latest = max(player.glob('Turbo4744-native-replay-*.json'), key=lambda p: p.stat().st_mtime)
report = json.loads(latest.read_text(encoding='utf-8-sig'))
if report['label'] != args.label:
    raise SystemExit(f"Latest report is {report['label']}; expected {args.label}")
destination = root / 'Reports/native'
destination.mkdir(exist_ok=True)
preserved = destination / (args.label + '.json')
if preserved.exists() and preserved.read_bytes() != latest.read_bytes():
    raise SystemExit('This label already preserves a different run; use a new label.')
shutil.copy2(latest, preserved)
shutil.copy2(args.request, destination / (args.label + '-request.json'))
for path in player.glob(latest.stem + '-*.png'):
    shutil.copy2(path, destination / (args.label + path.name[len(latest.stem):]))
platform = player / 'Mods/Ninjas4744-Turbo2026/standalonewindows64'
hashes = {name: hashlib.sha256((platform / name).read_bytes()).hexdigest()
          for name in ['Ninjas4744.dll', 'rebuilt_ninjas_4744_turbo_2026_assets']}
(destination / (args.label + '-artifacts.json')).write_text(json.dumps(hashes, indent=2) + '\n')
keys = ['label', 'completed', 'trenchPassed', 'transitStartCount', 'transitEndCount',
        'stowedBeforeReturn', 'stowSeconds', 'fullyStoredBeforeStow',
        'storedRetainedAfterStow', 'netFieldContacts', 'failureReason',
        'peakCaptured', 'capturedAtShot', 'scoredCaptured', 'remainingCaptured', 'maxTilt',
        'maximumTrenchHoldSeconds', 'maximumFuelCompression']
print(json.dumps({k: report.get(k) for k in keys}, indent=2))
print('Transit escapes:', len(report['transitEscapes']))
print('Incomplete inlet departures:', len(report.get('inletDepartures', [])))
print('Field geometry:', report.get('fieldGeometry', []))
if args.require_trench and not (report.get('trenchPassed')
        and report.get('transitCompleted') and report.get('stowedBeforeReturn')
        and 50 <= report.get('transitStartCount', 0) <= 60
        and report['transitStartCount'] == report.get('transitEndCount')
        and report.get('maximumTrenchHoldSeconds', 0) < .25
        and not report['transitEscapes']):
    raise SystemExit('Native trench acceptance failed; evidence preserved.')
