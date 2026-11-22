"""Summarize native frame/physics measurements; reject incomplete or unloaded runs."""
import argparse
import csv
import json
import statistics
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT = ROOT / 'Artifacts/WeakCpuComparison'


def percentile(values, p):
    values = sorted(values)
    position = (len(values) - 1) * p
    index = int(position)
    weight = position - index
    return values[index] * (1 - weight) + values[min(index + 1, len(values) - 1)] * weight


def summarize(folder, label):
    evidence = json.loads((folder / (label + '.json')).read_text(encoding='utf-8-sig'))
    receipt = json.loads((folder / (label + '-receipt.json')).read_text())
    if not evidence['completed'] or evidence['error'] or not evidence['quotaVerified'] or not evidence['affinityRestored']:
        raise ValueError(f'Incomplete/failed benchmark: {label}: {evidence.get("error")}')
    if receipt['build'] == 'trial' and not evidence['nativeBurstProbe']:
        raise ValueError('The trial did not verify native Burst execution')
    groups = defaultdict(list)
    with (folder / (label + '.csv')).open(encoding='utf-8-sig', newline='') as handle:
        rows = list(csv.DictReader(handle))
    previous_wall = None
    for row in rows:
        now = float(row['wallSeconds'])
        # Use actual elapsed Stopwatch time, including OS quota suspension.
        # The raw Unity delta is preserved for audit but can omit those pauses.
        row['wallFrameMs'] = (now - previous_wall) * 1000 if previous_wall is not None else 0
        previous_wall = now
        groups[row['profile']].append(row)
    result = dict(label=label, build=receipt['build'], receipt=receipt, evidence=evidence, profiles={})
    for profile, records in groups.items():
        begin = float(records[0]['wallSeconds'])
        if profile != 'shoot-cpu-5-percent':
            records = [r for r in records if float(r['wallSeconds']) >= begin + 2]
        if not records:
            raise ValueError('No measured frames after warmup: ' + label + '/' + profile)
        ms = [r['wallFrameMs'] for r in records]
        fuel = [int(r['fuel']) for r in records]
        elapsed = sum(ms) / 1000
        if profile != 'shoot-cpu-5-percent' and (min(fuel) < 55 or elapsed < 8):
            raise ValueError('Incomplete full-hopper window: ' + label + '/' + profile)
        focused = sum(int(r['focused']) for r in records) / len(records)
        if focused < .95:
            raise ValueError(f'Game lost focus during {label}/{profile}: {focused:.1%}')
        wall_delta = float(records[-1]['wallSeconds']) - float(records[0]['wallSeconds'])
        sim_delta = float(records[-1]['simSeconds']) - float(records[0]['simSeconds'])
        result['profiles'][profile] = dict(
            frames=len(ms), seconds=elapsed, fps=len(ms) / elapsed,
            medianFrameMs=statistics.median(ms), p95FrameMs=percentile(ms, .95),
            p99FrameMs=percentile(ms, .99), maxFrameMs=max(ms),
            framesOver50ms=sum(v > 50 for v in ms), framesOver100ms=sum(v > 100 for v in ms),
            simulationSpeed=sim_delta / wall_delta if wall_delta > 0 else 0,
            minimumFuel=min(fuel), maximumFuel=max(fuel), finalFuel=fuel[-1],
            released=int(records[-1]['released']), focusFraction=focused,
            cpuRate=int(records[-1]['cpuRate']), affinity=records[-1]['affinity'])
        if profile == 'shoot-cpu-5-percent':
            start = next(float(event.split()[0]) for event in evidence['events'] if 'Profile shoot-cpu-5-percent ' in event)
            empty = next((float(r['wallSeconds']) for r in records if int(r['fuel']) == 0), None)
            result['profiles'][profile]['startedWithFuel'] = fuel[0]
            result['profiles'][profile]['secondsToEmpty'] = empty - start if empty is not None else None
    if len(result['profiles']) != 5:
        raise ValueError('Missing CPU/shot profile: ' + label)
    return result


parser = argparse.ArgumentParser()
parser.add_argument('labels', nargs='+')
parser.add_argument('--folder', type=Path, default=DEFAULT)
args = parser.parse_args()
runs = [summarize(args.folder, label) for label in args.labels]
helper_hashes = {r['receipt']['helperDllSha256'] for r in runs}
if len(helper_hashes) != 1:
    raise SystemExit('Runs used different observer implementations')
summary = dict(method='Installed MoSim player, unchanged Steam and trial binaries, ordinary fuel pickup, game-only CPU quota and affinity.',
               limitations='CPU-budget stress test on one PC, not hardware emulation. Quotas create scheduling pauses. GPU/memory are unchanged. 200 FPS cap. First two seconds of full-hopper holding profiles excluded; shooting includes its start.',
               runs=runs, aggregate={})
for build in ('steam', 'trial'):
    selected = [r for r in runs if r['build'] == build]
    if not selected:
        continue
    summary['aggregate'][build] = {}
    for profile in selected[0]['profiles']:
        values = [r['profiles'][profile] for r in selected]
        summary['aggregate'][build][profile] = dict(
            runs=len(values), fps=statistics.mean(v['fps'] for v in values),
            p95FrameMs=statistics.mean(v['p95FrameMs'] for v in values),
            p99FrameMs=statistics.mean(v['p99FrameMs'] for v in values),
            simulationSpeed=statistics.mean(v['simulationSpeed'] for v in values),
            minimumFuel=min(v['minimumFuel'] for v in values), maximumFuel=max(v['maximumFuel'] for v in values),
            framesOver100ms=sum(v['framesOver100ms'] for v in values))
destination = ROOT / 'Reports/HopperPerformance/weak-cpu-comparison.json'
destination.write_text(json.dumps(summary, indent=2) + '\n')
for build, profiles in summary['aggregate'].items():
    print(build)
    for name, value in profiles.items():
        print(f"  {name}: {value['fps']:.1f} FPS, p95 {value['p95FrameMs']:.1f} ms, sim {value['simulationSpeed']:.2f}x, fuel {value['minimumFuel']}-{value['maximumFuel']}")
print(destination)
