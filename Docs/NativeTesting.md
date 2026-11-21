# Installed MoSim tests

`Turbo4744NativeReplay` is opt-in. It starts only in the native player when
`Turbo4744-native-replay.json` exists in MoSim's persistent-data folder. Normal
play has no replay component when that file is absent. Remove the request after
testing. The replay temporarily scopes a virtual keyboard to this robot, uses
ordinary action bindings, and restores the original device scope when finished.

The field keeps its original 504 independent fuel bodies. Collection uses the
intake; the replay does not seed, move, freeze or assign velocities to fuel or
the chassis. Optional `solidBumper`, `discreteChassis`, `disableJointProjection`
and `stopInterpolation` switches are isolated diagnostic comparisons. They stay
false when validating the packaged prefab.
`railForceOverride` and `railDampingOverride` are also diagnostic only; zero uses
the prefab calibration. Their temporary values are restored when the run ends.

The visible caption shows phase, peak stored load, tilt and scored fuel. **Show
box hitboxes** draws enabled solid box surfaces from the actual running robot.
Mesh and capsule surfaces are listed in the collision audit. **Run next requested
native cycle** resets the match and reads the next request.

Each run saves JSON and loaded, shooting, tilt-if-needed and finished screenshots
next to the request. JSON includes chassis observations every 22.5 ms at the
native 4.5 ms physics step, live collision
flags/pair exclusions, muzzle exits, transit escapes, delivery landings and final
remaining-fuel positions. Native Unity serialization omits the nested request
and sample classes, so the `frames` CSV strings and `frameColumns` are the
authoritative motion trace.

Fuel counted as stored has its centre within local X ±0.36 m, Y 0.20–1.00 m and
Z −0.10–0.64 m. This is a measured count, not the requested collection target.
Scoring uses the game's per-body scoring timestamp. Delivery success requires
the first ground landing to be within 1.25 m of the aim point and inside the field.
The driver waits for at least 12 seconds of active hub time before a scoring run;
the robot's shooting behavior itself is unchanged.

The dense-fuel A/B and subsequent cycles are summarized in
[native-dense-contact-audit.json](../Reports/native-dense-contact-audit.json).
The missing solid bumper and swept-CCD stutter were reproduced in the installed
player. A further native audit found zero roller definitions in the original
indexer component: the nested managed array worked in the editor but loaded
empty in the player. The replacement uses Unity-owned arrays and the installed
player now reports all nine measured rollers and positive traction on both floors.

The current bundle is `525f113be5c3fd0ba069b5db2914a7c2431789eae67b5c4ea9abd94b32697968`
and the direct-brake runtime is `f23e0b5e971a78f52771e9afa1877b6be287b7d0fcac7d68e05c4a23ded2d1de`.
All diagnostic runtime overrides are false in the following installed-player runs.
Adjacent request and artifact files identify the exact conditions.

| Current installed-player cycle | Measured outcome |
| --- | --- |
| Stowed bump, normal return gear | Peak 59; stow 1.81 s; 57 present before shooting; 57 scored; none remaining; no transit escapes; peak tilt 15.14°. |
| Stowed bump, moving-shot input | Peak 59; stow 1.81 s; 58 before shooting; 53 total scores; none remaining; no transit escapes; peak tilt 15.09°. Four muzzle observations occur at 0.32–0.45 m/s; the other shots are stationary. |
| Stowed trench, slower return gear | Peak 60; stow 1.84 s; caught under the crossbar and did not reach the own-zone shot position before timeout. The fallback shot command selected delivery in neutral. Route fails; its two incidental hub scores do not establish a successful scoring cycle. |
| Neutral delivery | Peak 58; 57 present before shooting; 56 recorded muzzle crossings and 56 successful first landings; none remaining; no transit escapes; peak tilt 1.10°. |
| Stowed trench, 50-ball collection target | Peak 54; stow 1.86 s; crossed into the alliance zone, but recorded nine transit escapes; 44 present before shooting; 43 scored; none remaining; peak tilt 12.61°. Retention fails. |

These traces record zero held time in the measured clear/flat neutral lanes.
The slow trench obstruction is a separate physical route failure. The rear
crossbar is still visible over the loaded roof in the preserved evidence.
The current MoSim cloth collision gather accepts sphere colliders; its roof
does not collide with the field's box/mesh crossbar. Fuel still contacts that
bar and transmits forces to the chassis. Changing the cloth layer mask alone would not add those
missing cloth/field contacts. Full-load trench behavior needs further physical
model work; these runs do not certify it.

The following historical runs still moved the main hopper. They are preserved
as failures and comparisons, not evidence for the corrected fixed bin.

| Historical installed-player cycle | Measured outcome |
| --- | --- |
| Powered bump and hub | Peak 59 stored; 56 scored; none remaining; peak tilt 12.58°. No recorded transit escapes. |
| Powered neutral-zone delivery | Peak 60 stored; 56 recorded muzzle exits; 55 successful first landings; none remaining; peak tilt 1.01°. |
| Powered deployed trench and hub | Peak 58 stored; 16 recorded transit escapes; 40 in the observation box before shooting; 38 scored; none remaining; peak tilt 7.37°. Retention fails. |
| Stow before trench, 58-ball peak | Intake retracts to 1.5 mm; hopper stalls at 216.1 mm extension. Aborts before driving after 8 s. |
| Stow before trench, 35-ball peak | Intake retracts to 1.5 mm; hopper stalls at 147.5 mm extension. Aborts before driving after 8 s. This is a diagnostic, not full-load acceptance. |
| Stronger-drive comparison, 61-ball peak | Temporary 700 N / 6000 N·s/m drive still stalls at 163.8 mm hopper extension. Aborts before driving. The original 350 N / 1800 N·s/m calibration is retained. |

Both powered scoring/delivery runs recorded two concurrent shooter contacts and
reached the shared 320 N indexer limit. These are measured outcomes, not perfect
scoring or delivery passes. Raw reports, matching requests and screenshots are
under `Reports/native/`. Muzzle sampling can miss a fast exit between observations;
the native scoring timestamp is authoritative for the scored count.

The team confirms that the real robot normally returns over the bump, uses the
trench stowed, holds about 50–60 balls, and retracts only the intake/kicker.
The main hopper walls, floor and net frame now stay on the chassis. The legacy
`hopperRail` field names the moving intake/kicker carrier. The former moving-bin
floor contact is absent. The original finite 350 N / 1800 N·s/m drives are retained.

| Corrected fixed-hopper cycle | Measured outcome |
| --- | --- |
| Stowed bump, original 35% net slack | Peak 58; stow 1.86 s; 57 in the observation box before shooting; 55 scored; none remaining; no transit escapes; peak tilt 14.99°. |
| Stowed trench, original 35% net slack | Peak 59; stow 1.90 s; 47 before shooting; 43 scored; none remaining; 10 transit escapes; peak tilt 12.81°. Retention fails. |
| Neutral delivery, original 35% net slack | Peak 59; 56 recorded muzzle exits and 56 successful first landings; none remaining; peak tilt 1.25°. |

The following 5% net-slack comparison was rejected. Its larger-fill fixtures
collected only 48 and 49 balls, failing the unchanged 50-ball minimum. The
candidate retains the original 35% slack. These comparisons are not release
acceptance results.

| Rejected tighter-net comparison | Measured outcome |
| --- | --- |
| Stowed trench, 5% net slack comparison | Peak 55; stow 1.82 s; 46 before shooting; 44 scored; none remaining; 3 transit escapes; peak tilt 5.80°. Retention still fails. |
| Stowed bump, 5% net slack | Peak 55; 50 before shooting; 48 scored; none remaining; no transit escapes; peak tilt 15.30°. |
| Deployed bump moving-shot attempt, 5% net slack | Peak 54; stuck on bump before own zone; no muzzle exits or scores; 53 remaining. Route fails. |
| Stowed bump with moving-shot input, 5% net slack | Peak 56; 50 before shooting; 47 total scores; none remaining; one transit escape; peak tilt 15.52°. Four muzzle observations occur at horizontal chassis speeds of 0.4–1.6 m/s; the other shots are stationary. |
| Neutral delivery, 5% net slack | Peak 54; 52 before shooting; 51 recorded muzzle exits; 50 successful first landings; none remaining; peak tilt 9.96° during collection near the opposite bump. |

Peak counts include transient fuel in the observation box during collection;
they do not imply every counted ball was retained. The slack comparison changed
only rest lengths, retaining cloth particle mass, compliance and independent fuel.
Each corrected case has an adjacent `*-artifacts.json` with its installed DLL
and bundle hashes. The clear-floor trace of the original-slack corrected trench
run recorded zero held time and at most 0.651 mm pose/velocity residual per sample.

Earlier bumper-only tests, before correcting native roller data, collected 59
and scored 58 over the bump with one remaining; their deployed trench cycle
returned with 43 and scored 40 with three remaining. That trench result is a
retention failure. The corrected stowed trench returns and scores, but its
retention requirement remains unsatisfied.

The visible Unity fixture suite is separate from these installed-player cycles.
Its passing results do not replace native collection and traversal evidence.
Fixture reports record the specific Unity Package Manager authentication failure
in `editorServiceErrors`, separately from robot/runtime errors. The initial
capacity report that failed on this editor-service error is preserved. Physical
collection, retention, release and error acceptance thresholds remain unchanged.
The jam fixture now disables removed preloads immediately and waits for the
robot to settle before introducing its blocked ball. The failed earlier setup
expelled that ball during the first physics steps while preloads were still
overlapping it, before feeding began. Preserved reports include the failed
initial setup, collision-layer comparison and physics trace. Passing jam checks
also require real obstruction contact and no launch before the fixture is
removed, in addition to the existing reverse-duration and scoring limits.

Seeded indexer fixtures rebuild their empty cloth into its starting pose before
fuel is introduced. The former setup inserted bodies through an already sagged
net and produced a roughly 155 N one-step force before feeding. Paired-arrival
and mixed-floor fixtures seed when feeding is ready; passive-guide fixtures keep
their idle support interval. Runtime cloth, force budgets, pair timing and
scoring requirements are unchanged. Position, collision and force traces from
the failed setup are preserved.

Moving-shot fixture diagnostics are preserved in
`systems-moving-fixed-hopper-score-failure.json` and
`systems-moving-fixed-hopper-contact-trace.json`. The initial run scored 8/8 in
one direction and 6/8 in the other; the traced repeat scored 16/16. A separately
labelled rear-rim removal comparison still scored only 6/8 in one direction,
so the original net geometry is retained. The direct accelerator-brake comparison
scored 16/16. It exposed a delay between the re-aim interlock and the contact's
brake target while the motor reference ramped down. The runtime now commands that
existing finite brake directly. These comparisons remain separate from release
acceptance and installed-player testing.

Capacity diagnostics preserve the five-escape result in
`capacity-f23e-first-heading-failure.json` and the collision trace in
`capacity-boundary-impact-trace.json`. The original zero-degree drive reached
`Side2` at chassis Z 3.404 m and recorded a 220.323 N·s collision impulse at
10.648 s. Its start now moves from Z -1.5 m to -3 m, providing room for the
same collection and loaded-braking sequence. The 90-degree start, drive timing,
speed and fuel requirements are unchanged; the capacity check additionally
requires zero obstacle contacts during that drive segment.

The original 5 m firing corner also intersected the alliance boundary at spawn.
Its control trace records 288 contacts with `Side4`, `Wall`, `Wall (3)` and
`BlueFrontWall`, including impulses above 7000 N·s on the first physics step.
Some runs could not align; others fired inconsistently after that impact. The
range remains 5 m, with its bearing changed from `(0.8, 0.6)` to
`(0.77, sqrt(1 - 0.77²))`. Five stock-geometry repeats scored 40/40 with no
boundary contacts. This fixture now requires the actual 5 m spawn radius and
zero setup contacts. The field colliders remain enabled.
`systems-five-meter-original-corner.json`,
`systems-five-meter-clearance-diagnostic.json` and
`systems-five-meter-clearance-repeat.json` preserve the comparison.
The separate hood-alignment and rear-rim-opening hypotheses remain diagnostic
reports; the packaged shooter geometry is retained.

To replay a saved cycle, select keyboard control for this robot in MoSim, copy a
`Reports/native/*-request.json` file to
`%USERPROFILE%/AppData/LocalLow/CascadeStudios/MoSim/Turbo4744-native-replay.json`,
then start a normal Rebuilt match with this mod. The replay waits for teleop.
Remove that request file and restore your preferred controller after testing.
The saved stow requests stop before driving if either rail remains more than
15 mm from closed after eight seconds.
