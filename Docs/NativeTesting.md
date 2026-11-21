# Installed MoSim tests for 0.6.2

The native player loads the same bundle and DLL validated by the fixture suite. Full-load trench results are recorded in the [native certificate](../Reports/native-trench-0.6.2.json), with adjacent requests, raw traces, screenshots and installed artifact fingerprints.

| Installed-player cycle | Alliance | Stowed / retained | Minimum trench speed (m/s) | Stored spills / inlet departures | Scored / remaining |
| --- | --- | --- | --- | --- | --- |
| [062-grip140-blue-full](../Reports/native/062-grip140-blue-full.json) | Blue | 57 → 57 | 1.50 | 0 / 0 | 57 / 0 |
| [062-grip140-blue-repeat](../Reports/native/062-grip140-blue-repeat.json) | Blue | 58 → 58 | 1.53 | 0 / 0 | 55 / 0 |
| [062-grip140-blue-target62](../Reports/native/062-grip140-blue-target62.json) | Blue | 60 → 60 | 1.52 | 0 / 5 | 58 / 0 |
| [062-grip140-red-normal](../Reports/native/062-grip140-red-normal.json) | Red | 56 → 56 | 3.15 | 0 / 0 | 54 / 0 |

The additional [native bump/delivery cycle](../Reports/native/062-grip140-blue-bump-delivery.json) carried 56 balls over the bump beside the hub and retained all 56, with zero stored spills. It released 56, delivered 56 within 1.25 m of the target, and ended empty. Peak tilt was 15.04°. The front carrier briefly deflected 31.9 mm during the impact, within the existing Travel fixture's 35 mm impact/stow envelope. Trench acceptance retains its stricter 15 mm rail-clearance limit. The [bump certificate](../Reports/native-bump-0.6.2.json) checks the raised route, retained bodies, normal input commands and matching artifacts.

Across these 4 trench cycles, recorded collection, return and shooting frames peaked at 16.64 ms. The checker rejects recorded phase frames at or above 250 ms and sustained pose/velocity holds under the trench.

`Turbo4744NativeReplay` is opt-in: place `Turbo4744-native-replay.json` in MoSim's persistent-data folder to run an input replay. Normal play adds no replay component when the file is absent. The replay temporarily scopes a virtual keyboard to this robot, drives ordinary action bindings, and restores the device scope at completion. Remove the owned request after testing and restore any temporary device/alliance selection.

The field retains its original 504 independent fuel bodies. The replay never seeds, moves, freezes or assigns velocities to fuel or the chassis. Default robot physics includes tensioned cloth, field-box contacts and bounded fuel-to-fuel compliance. Diagnostic comparison flags and numeric overrides must be false/zero for release acceptance.

Use `stowForReturn: true`, `requireTrenchSuccess: true`, `minimumTransitLoad: 50`, `maximumTransitLoad: 60`, and the physical trench lane (`returnLane: -3.3758` for the tested Red start or `3.3758` for Blue). Gear 1 is the slower return; gear 2 is normal. Target collection counts are braking triggers; the actual stowed cohort determines acceptance. Out-of-range loads and route timeouts fail, without a wrong-zone fallback shot.

The visible display shows phase, peak collection, actual stowed count, retained count, stored spills, incomplete inlet departures, roller contacts and scoring. Box-hitbox outlines show enabled live surfaces. A completed replay can reset the match for the next request. JSON contains a 22.5 ms motion trace at the 4.5 ms physics step. `frames` and `frameColumns` are authoritative because native serialization omits nested request/sample objects; the request is preserved separately.

The stowed cohort uses fuel centres inside local X ±0.36 m, Y 0.15–1.00 m and Z −0.17–0.64 m, including lower staging. Retention tracks those same bodies. Every crossing sample must retain the cohort. Rail extension must stay below 15 mm, and both velocity-based and pose-based stops under the bar must stay below .25 s. The lane trace must cross the actual trench. Scoring uses the game's per-body timestamp; muzzle observations can miss a fast crossing between samples.

After observing completion, preserve the report and screenshots with:

```powershell
python Tools/capture_native_replay.py --label LABEL --request PATH --require-trench
```

Once all release cycles are captured, run `Tools/verify_native_trench.py LABEL...`. It requires current artifact hashes, default calibration, both alliances, slow and normal gears, and an actual 60-ball crossing. `Tools/Package.ps1` rejects a release without that certificate, a matching native bump/delivery certificate and all fourteen matching fixture groups. The calibration includes contact-law approximations; these records establish the tested simulator routes.

Historical failures remain preserved. [The 0.6.1 native record](NativeTesting-0.6.1.md) contains the caught slow trench and nine-ball spill. Early 0.6.2 contact-only and net comparisons failed and remain under `Reports/native/`. The 5% textile-ease candidate lost stored fuel at the crossbar. A swept-node experiment then caught and stretched the roof, so it was rejected. The accepted build uses the tensioned rest scale 1.0 and analytic field-box contacts. Dense-pile calibration comparisons retain their original thresholds and failed outcomes in `Reports/wheel-pile-062-*.json`.

Earlier 1.3-grip comparisons remain visible too. [An over-range Red fill](../Reports/native/062-release-red-normal.json) stowed 62 balls and aborted before transit. [A fuller Blue fill](../Reports/native/062-release-blue-target64-slow.json) stopped at the replay's 2 mm stow gate after the front carrier settled 2.16 mm from its stop; it never attempted the crossing. Neither run is included in passing acceptance. Completed pumped/settled cycles are the recorded evidence; these results do not establish success for every possible arrival or stow sequence.

The separate [60-target capacity diagnostic](../Reports/capacity-062-target60-comparison.json) retained and released 57 and 59 balls, but failed the zero-degree 3.5 m/s loaded-speed criterion at 3.39 m/s. It is excluded from the release certificate. The package-required capacity fixture keeps its original target and thresholds; the native certificate separately checks actual 50–60-ball trench loads.
