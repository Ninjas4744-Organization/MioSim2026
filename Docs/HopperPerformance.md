# Full-hopper lag investigation

The subsequent [installed-player CPU-budget comparison](WeakCpuComparison.md)
tests the unmodified Steam robot and packaged trial with full hoppers and reduced
CPU availability. Its rendered frame results are separate from the editor net
microbenchmark below.

Investigated on 2026-11-21 using the supplied 11.6-second recording, the current
robot prefab, and Unity 2023.2.22f1. The leading explanation is CPU pressure from
fuel/net collision calculations, amplified by physics catch-up. This is a strong
candidate, not a confirmed profile of the affected player's computer or game build.

## Evidence

The recording begins paused. After resuming, movement is initially smooth and
then freezes progressively as fuel accumulates. Substantial view changes around
6–10 seconds become separated by approximately 0.07, 0.10, 0.13, 0.23, 0.33,
0.47, 0.63, 0.80 and 1.03 seconds. This describes the recording's visible cadence;
it is not an in-game frame-time measurement. The recording does not identify the
mod version or hardware. Collection and carrying are visible before shooting.

The test project's physics interval is 4.5 ms, approximately 222 steps per second.
The roof uses 320 particles, four substeps, and collision checks every substep.
MoSim's `NetSolver` tests nearby spheres against incident net edges. Its work grows
with the number of nearby spheres, including spheres too far below the net to
touch a particular edge.

Each stock fuel prefab has two solid collision spheres: the 75 mm outer sphere
on GamePiece1 and a 70 mm sphere on GamePieceAlt. The configured net queries both
layers, giving **120 candidate spheres for 60 balls**. The solver handles these
as separate contact surfaces and reactions.

An isolated editor benchmark, with synchronous Burst compilation and 512 measured
steps after warm-up, produced:

| Fuel bodies | Net candidates | Net solve and completion, mean |
| --- | --- | --- |
| 0 | 0 | 0.139 ms |
| 30 | 60 | 0.918 ms |
| 60 | 120 | 1.843 ms |
| 60, outer-sphere-only experiment | 60 | 1.049 ms |

The full-load net alone consumes about 41% of one 4.5 ms interval on this Ryzen 5
9600X in the editor. A slower CPU has less room for rigid-body contacts, wheels,
mechanism scripts and rendering. When frames take longer, Unity runs more physics
steps before the next render, potentially extending the stall. This interpretation
matches [Unity's explanation of fixed-step catch-up and maximum timestep limits](https://docs.unity3d.com/2023.2/Documentation/Manual/TimeFrameManagement.html).
The project permits a maximum timestep of one second.

These are component timings, **not player FPS**. Fuel positions are fixed and no
rigid-body simulation or rendering runs inside the timed loops. Net timings include
job completion, collider gathering, mesh updates and deferred reactions. A loaded
moving robot, the installed game's library, other robots, graphics and recording
overhead may produce different results. The fixture creates net pins from the
current prefab's authored positions; it does not replay the video or certify a
deployed/pumping pose. The source hashes are recorded with the reports.

## Changes made

`Turbo4744FuelCompliance` previously allocated a new snapshot, hash set and
dictionary every physics step, repeatedly growing both collections for a full
hopper. It now reuses snapshots. A short lock protects publication and reader
counts; physics callbacks can read concurrently, and a buffer cannot be reused
until its last reader releases it. Disabling still publishes an empty snapshot
and restores each collider's original flag. The fuel layer mask is cached.

`Turbo4744NetEnvironment` now completes the job through a typed `JobHandle` and
caches its layer mask, removing repeated reflected method lookup/invocation and
the mask's parameter-array allocation.

| Full-load measurement | Before | After |
| --- | --- | --- |
| Fuel compliance GC allocations per step | 30 | 0 |
| Fuel compliance mean CPU time | 105.2 µs | 95.7 µs |
| Net environment GC allocations per step | 9 | 6 |
| Net solve mean CPU time | 1.843 ms | 1.833 ms |

Together the changes remove about **7,333 managed allocations per simulated
second** at full load. This is an allocation count, not a byte or pause-time
measurement. The net remains the dominant measured cost. The benchmark itself
boxes one job handle per net sample; the net's reported allocation count includes
that instrumentation overhead.

The outer-sphere-only experiment reduced net time by about 43%. That mask change
is confined to the benchmark and restored afterward. It changes net reactions, so
it has not been applied to the robot prefab or builder. A larger improvement should
start with conservative spatial rejection in the framework net solver, or validate
a single fuel contact surface against full-load retention, pumping, shooting and
stowed trench travel. Raising the global physics timestep, freezing stored fuel,
or reducing net substeps would change other calibrated behavior.

## Validation and reproduction

The optimized fixture checks 60 fuel bodies, restoring pre-existing true and false
contact flags on exit and disable, two simultaneous snapshot leases across repeated
publication and disable, and 120 actual physics steps exercising modified contacts
after re-enable. These checks passed. The static capacity fixture also passed:
53 collected and all 53 retained at both 0° and 90°. Its scope does not certify
shooting or stowed trench travel. The editor logged a settings-service focus
exception during scene startup, before the robot fixture began; this is preserved
in the [validation notes](../Reports/HopperPerformance/validation-notes.json).

Raw results: [baseline](../Reports/HopperPerformance/baseline.json),
[optimized](../Reports/HopperPerformance/optimized.json),
[static loaded retention](../Reports/HopperPerformance/loaded-retention.json),
[source fingerprints](../Reports/HopperPerformance/source-manifest.json), and
[video cadence summary](../Reports/HopperPerformance/video-summary.json).

Run the benchmark against an existing official modding project:

```powershell
.\Tools\Invoke-Unity.ps1 -MoSimProject $project -UnityEditor $unity -Task Performance `
    -DiagnosticArguments @('-turboPerfLabel', 'current', '-turboPerfVerify', 'true')
```

It uses the authoring prefab, writes `BuildArtifacts/hopper-performance-current.json`
in that project, and copies reports to this repository's ignored `Artifacts/` folder.
Compare revisions with the same prefab, fuel asset, framework and benchmark. The
baseline here changes only the two optimized runtime files back to their HEAD
versions; the current robot assets are identical in both runs.

The initial allocation investigation above did not produce a build. The following
trial adds spatial filtering and an installable Windows package. Confirming the
reported freezes are resolved still requires testing on an affected computer.

## Net collision trial

`Turbo4744NetSolver` is a mod-local copy of the pinned framework solver, with an
8 × 8 grid of sphere bounds and a conservative edge/bounds rejection test. It
visits surviving spheres in the original order and runs the original contact
calculation. Bounds use the current edge endpoints, with a small outward guard;
invalid bounds fall back to the original complete candidate set. Both fuel
spheres remain active. Particle count, anchors, geometry, masses, integration,
substeps, constraints, damping and rigidbody reactions are unchanged.

The original framework source used for comparison has SHA-256
`1ab121a51993b8927d35ef76f8b2fdfb8708196094c4f1aad44527b57cddced6`.
The fork needs comparison again when the framework solver changes.

`NetCollision` compares all position, previous-position, velocity, mesh and
constraint-lambda values bit for bit, as well as ordered reaction body IDs,
forces and contact points. It exercises empty, packed, contact-boundary,
moving-anchor/pumping and 256-collider cases, plus rebuild and re-enable.
Reports include source and compiled assembly fingerprints.

On the Ryzen 5 9600X test machine, the final 60-ball Burst benchmark measured
1.897 ms per original net solve and 0.928 ms with filtering, about 51% less CPU
time. Detailed sphere/segment checks fell from 579,840 to 8,792 per solve (98.5%).
The managed and Burst comparisons each passed 480 steps, comparing 913,095 and
929,182 ordered impulses respectively. These are solver measurements, not a
prediction of whole-game FPS on another computer.

The MoSim SDK copies an editor assembly by default. This trial separately
compiles the player assembly and only the mod's native net entrypoint using
Burst 1.8.13 for Windows x64 SSE2. The native bytes are embedded in the bundle,
loaded through Unity's additional-library API, and dispatched through their own
export. An ABI size check and a Burst-discard execution probe verify the player
path. No host assemblies, global Burst flags or game-wide physics settings are
modified. Runtime implementations of Mono assemblies must precede reference
assemblies during native compilation: reference-only stubs can compile to aborts.

An earlier native test ran at 2 FPS because it executed the managed fallback.
That was a failed test, not a requested frame-rate cap. A later startup probe
caught the reference-stub compilation error. Neither is passing trial evidence.

The corrected installed-player probe completed 2,334 net steps with native
execution confirmed, all 68 anchors and filtering active. It released all eight
preloads; its largest sampled shooting frame was 7.82 ms. This probe verifies
native loading/dispatch and preload delivery, not a full-load player benchmark.

The final bundle fixtures retained 59/59 collected balls at 0° and 90°. The
loaded-drive/manual-shooting fixture also passed at both headings: 59/59 retained
through driving and turns, 59/59 released, no escapes, and empty stow complete.
Peak loaded speeds were 3.80 and 3.82 m/s. The fixtures reported no robot errors
or settings-service errors in these final runs.
The normal auto-aim shooting case at 225° also passed: all 60 collected balls
released, no escapes or overturning, and empty stow complete. This fixture
intentionally skips driving; its validation checks the loaded shooting behavior
rather than inheriting the manual fixture's driving-speed assertions.

Raw trial evidence: [managed comparison](../Reports/HopperPerformance/net-collision-managed.json),
[Burst comparison](../Reports/HopperPerformance/net-collision-burst.json),
[controls](../Reports/HopperPerformance/controls-trial.json),
[retention](../Reports/HopperPerformance/full-hopper-retention.json),
[loaded driving and manual shooting](../Reports/HopperPerformance/full-hopper-shooting.json),
[full-load auto-aim shooting](../Reports/HopperPerformance/loaded-auto-shooting.json),
and [installed-player probe](../Reports/HopperPerformance/native-trial.json).

Reproduce the trial from the current source:

```powershell
.\Tools\Invoke-Unity.ps1 -MoSimProject $project -UnityEditor $unity -Task Build
.\Tools\Invoke-Unity.ps1 -MoSimProject $project -UnityEditor $unity -Task NetCollision
.\Tools\Invoke-Unity.ps1 -MoSimProject $project -UnityEditor $unity -Task NetCollision `
    -DiagnosticArguments @('-turboNetManaged')
.\Tools\Invoke-Unity.ps1 -MoSimProject $project -UnityEditor $unity -Task Controls `
    -DiagnosticArguments @('-turboBundle')
.\Tools\Invoke-Unity.ps1 -MoSimProject $project -UnityEditor $unity -Task Load `
    -DiagnosticArguments @('-turboCapacityTarget','58','-turboFillExtraSeconds','6')
.\Tools\Invoke-Unity.ps1 -MoSimProject $project -UnityEditor $unity -Task Pile `
    -DiagnosticArguments @('-turboCapacityTarget','58','-turboFillExtraSeconds','6')
.\Tools\Invoke-Unity.ps1 -MoSimProject $project -UnityEditor $unity -Task LoadedAim -Heading90 `
    -DiagnosticArguments @('-turboCapacityTarget','58','-turboFillExtraSeconds','6')
```

`Load` measures retention of the collected full load. `Pile` then tests loaded
driving, turns, disable, held manual shooting/pumping and empty stow at two
headings. Fuel remains independent physics bodies. The installed-player test
separately verifies the packaged native path; the editor fixtures execute an
audited editor compilation of the same source, not the player DLL. This is a
focused performance trial, not complete mechanical release certification.

After recording the exact build's reports in `Reports/HopperPerformance`, run
`python Tools/package_net_trial.py --project $project`. The packager checks
report/build fingerprints, native execution, full loads and every ZIP entry.
