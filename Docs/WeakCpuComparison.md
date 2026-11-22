# Full-hopper CPU-budget comparison

Tested in the installed MoSim 26.6.0 player on 2026-11-22, using the Steam
Workshop robot as the baseline and the already packaged net-collision trial.
This approximates limited CPU availability on the current PC; it does not emulate
an older CPU, GPU, memory system or operating system.

## Builds and measurement

The old build is the byte-identical Windows payload from Workshop item
3843685878, manifest version 1.0.0. Its net component is `MoSimLib.NetSolver`.
The new build is `0.6.2 / net-collision-r1`, with original team controls selected
through the Actual Turbo variant. Its net is `Turbo4744NetSolver`, and the probe
verifies native Burst execution. Both use 320 net nodes and four substeps.

Neither robot DLL nor bundle is instrumented or rebuilt for this comparison.
A separate temporary observer mod reads the existing opt-in replay's load and
phase counters. It records one CSV row per rendered frame, elapsed Stopwatch
time, simulated time, physics steps, focus, active CPU quota and affinity. The
ordinary robot input replay collects the original independent field fuel.
Nothing seeds, freezes or moves fuel directly.

The observer applies CPU limits only to the game process using a Windows job
object. Processor topology identifies the first two physical cores and their
four SMT threads (affinity mask `0xF`; normal mask `0xFFF`). Quotas of 8% and 5%
refer to the machine's total CPU capacity. Windows suspends a job that has used
its allowance until the next scheduling interval; this can create harsher pauses
than an older CPU with a steady lower clock. See Microsoft's
[CPU-rate control documentation](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_cpu_rate_control_information).
Every quota and affinity change is read back and checked.

The PC is a Ryzen 5 9600X with 12 logical processors and an RTX 5070. Each run uses
1920 × 1080, quality level 0, a 200 FPS limit, the same Blue spawn, camera mode,
input sequence and collection target. Physics stays at the player's 4.5 ms step
and 1-second maximum catch-up interval. Actual settled loads are recorded, not
assumed from the requested collection target.

After collection stops and settles for three simulated seconds, each run measures
14 wall-clock seconds in each condition: all cores, two cores, two cores with an
8% budget, and two cores with a 5% budget. The first two seconds of each holding
condition are excluded. The observer then ends only the replay's waiting period,
allowing its ordinary delivery-shoot input under the same 5% budget. That stage
ends two seconds after emptying, or after 45 wall-clock seconds.

**Frame intervals come from consecutive Stopwatch timestamps**, including quota
suspension. The raw CSV also retains Unity's reported delta, which did not always
include the imposed pauses. The game's FPS display therefore cannot substitute
for the measured wall-clock frame rate in this experiment.

## Evidence and scope

The run order is Steam A, trial A, trial B, Steam B. Both builds receive the same
observer DLL and protocol. The analyzer rejects incomplete runs, missing profiles,
loads below 55 during holding, less than eight measured seconds per holding
condition, focus below 95%, or an unverified native path in the trial.

This compares complete shipped robot builds in a rendered game. Physics is
chaotic and collected loads/stop positions can differ slightly. It is not an
isolated same-position net benchmark; the separate 60-ball equivalence/timing
test provides that evidence. The CPU quotas also produce artificial scheduling
pauses, and the GPU and memory remain those of this PC. The results demonstrate
behavior under these budgets, not expected FPS for a named low-end computer.

Raw measurements, build receipts and requests are retained with
[the comparison report](../Reports/HopperPerformance/weak-cpu-comparison.json).
The failed initial observer setup is preserved separately and excluded.
The installed trial and original settings are restored, and the temporary helper
and input requests removed, after the comparison.

## Results

Mean of two runs per build. FPS is measured from wall-clock frame intervals.

| Holding a full hopper | Steam FPS | Trial FPS | Steam p95 frame | Trial p95 frame |
| --- | ---: | ---: | ---: | ---: |
| All CPU cores | 183.7 | 198.6 | 8.0 ms | 6.6 ms |
| 2 cores / 4 threads | 153.3 | 187.7 | 8.8 ms | 7.4 ms |
| 2 cores + 8% total CPU budget | 11.0 | 65.8 | 262.6 ms | 31.6 ms |
| 2 cores + 5% total CPU budget | 0.7 | 14.8 | 1855.2 ms | 501.5 ms |

The p95 columns are the mean of each run's 95th-percentile frame interval. The most restricted Steam profiles contain few frames, so their tail estimates are approximate.

| Run | Initial load | Measured holding range | Empty after shoot request/profile start | Final load |
| --- | ---: | ---: | ---: | ---: |
| steam-a | 61 | 61–61 | 4.66 s | 0 |
| trial-a | 56 | 56–56 | 3.64 s | 0 |
| trial-b | 60 | 60–60 | 4.22 s | 0 |
| steam-b | 62 | 58–62 | 4.69 s | 0 |

All four runs completed, kept at least 55 fuel bodies during every holding sample, and ended the shooting measurement empty. Trial runs both verified native Burst execution. Focus validation passed. The observer confirmed the original affinity/CPU budget after each run.

At the 5% quota, the Steam simulation advanced at about 0.66× real time; the trial averaged 0.99×. The trial still had roughly half-second p95 pauses at this severe quota. These numbers support improved CPU headroom, subject to the testing limits above.
