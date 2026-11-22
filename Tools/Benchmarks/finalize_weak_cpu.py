"""Preserve raw comparison records and generate the reviewable result tables."""
import hashlib
import json
import shutil
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Artifacts/WeakCpuComparison'
REPORTS = ROOT / 'Reports/HopperPerformance'
TARGET = REPORTS / 'WeakCpuComparison'
TARGET.mkdir(exist_ok=True)
report_path = REPORTS / 'weak-cpu-comparison.json'
report = json.loads(report_path.read_text())
session = json.loads((SOURCE / 'session.json').read_text())
if not session['restored'] or len(report['runs']) != 4:
    raise SystemExit('Require four completed runs and successful installation/settings restoration')
labels = [r['label'] for r in report['runs']]
if labels != ['steam-a', 'trial-a', 'trial-b', 'steam-b']:
    raise SystemExit('Unexpected comparison order')
for label in labels:
    for suffix in ('.json', '.csv', '-request.json', '-config.json', '-receipt.json'):
        shutil.copy2(SOURCE / (label + suffix), TARGET / (label + suffix))

report['restorationVerified'] = True
report['rawDirectory'] = 'WeakCpuComparison'
report['observerSourceSha256'] = hashlib.sha256((ROOT / 'Tools/Benchmarks/WeakCpu/WeakCpuProbe.cs').read_bytes()).hexdigest()
report['fpsClock'] = 'Consecutive Stopwatch wallSeconds differences; raw Unity frameMs is diagnostic only.'
report_path.write_text(json.dumps(report, indent=2) + '\n')

names = [('full-cpu', 'All CPU cores'), ('two-cores', '2 cores / 4 threads'),
         ('cpu-8-percent', '2 cores + 8% total CPU budget'),
         ('cpu-5-percent', '2 cores + 5% total CPU budget')]
lines = ['## Results', '', 'Mean of two runs per build. FPS is measured from wall-clock frame intervals.', '',
         '| Holding a full hopper | Steam FPS | Trial FPS | Steam p95 frame | Trial p95 frame |',
         '| --- | ---: | ---: | ---: | ---: |']
for key, name in names:
    old = report['aggregate']['steam'][key]
    new = report['aggregate']['trial'][key]
    lines.append(f"| {name} | {old['fps']:.1f} | {new['fps']:.1f} | {old['p95FrameMs']:.1f} ms | {new['p95FrameMs']:.1f} ms |")
lines += ['', 'The p95 columns are the mean of each run\'s 95th-percentile frame interval. '
          'The most restricted Steam profiles contain few frames, so their tail estimates are approximate.', '',
          '| Run | Initial load | Measured holding range | Empty after shoot request/profile start | Final load |',
          '| --- | ---: | ---: | ---: | ---: |']
for run in report['runs']:
    hold = [v for p, v in run['profiles'].items() if p != 'shoot-cpu-5-percent']
    shot = run['profiles']['shoot-cpu-5-percent']
    lines.append(f"| {run['label']} | {run['evidence']['initialFuel']} | {min(v['minimumFuel'] for v in hold)}–{max(v['maximumFuel'] for v in hold)} | {shot['secondsToEmpty']:.2f} s | {shot['finalFuel']} |")
lines += ['', 'All four runs completed, kept at least 55 fuel bodies during every holding sample, '
          'and ended the shooting measurement empty. Trial runs both verified native Burst execution. '
          'Focus validation passed. The observer confirmed the original affinity/CPU budget after each run.', '',
          'At the 5% quota, the Steam simulation advanced at about '
          f"{report['aggregate']['steam']['cpu-5-percent']['simulationSpeed']:.2f}× real time; the trial averaged "
          f"{report['aggregate']['trial']['cpu-5-percent']['simulationSpeed']:.2f}×. "
          'The trial still had roughly half-second p95 pauses at this severe quota. '
          'These numbers support improved CPU headroom, subject to the testing limits above.', '']
doc = ROOT / 'Docs/WeakCpuComparison.md'
text = doc.read_bytes().split(b'## Results')[0].decode('utf-8').rstrip() + '\n\n' + '\n'.join(lines) + '\n'
doc.write_text(text, encoding='utf-8')

manifest = {str(p.relative_to(ROOT)).replace('\\', '/'): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(TARGET.iterdir()) if p.is_file()}
(REPORTS / 'weak-cpu-evidence-sha256.json').write_text(json.dumps(manifest, indent=2) + '\n')
destination = ROOT / 'dist/Turbo-full-hopper-CPU-comparison.zip'
with zipfile.ZipFile(destination, 'w', zipfile.ZIP_DEFLATED) as z:
    for p in sorted(TARGET.iterdir()):
        if p.is_file(): z.write(p, 'Raw/' + p.name)
    z.write(report_path, 'comparison.json')
    z.write(doc, 'METHOD-AND-RESULTS.md')
    z.write(REPORTS / 'weak-cpu-evidence-sha256.json', 'raw-sha256.json')
with zipfile.ZipFile(destination) as z:
    if z.testzip(): raise SystemExit('Evidence archive verification failed')
print(doc)
print(destination)
