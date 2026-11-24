# Continuous underbody roll-out trial

The owner confirmed reliable recovery with the first grounded-clearance trial, then asked for a more realistic appearance because instant relocations look like balls spawning. This trial replaces pose changes with bounded horizontal forces and rolling torque toward a checked floor exit. Balls remain the original dynamic bodies, with gravity, field, other-robot and ball contacts intact. There are no cloned visuals, spawned replacements or ball-position assignments.

While at least one physical tire touches the field, the helper temporarily releases only this robot's pinch contacts for a low underbody ball. It restores its changes when the ball is clear, the helper is disabled, the robot is disabled, support disappears or the short assistance interval ends. Existing intake-keeper collision pairs keep their own state. Stored hopper/feed fuel stays above the assisted volume. The grounding check, steering, tires, bumpers, shooter and net settings are preserved.

The ball eases toward 1.1 m/s using at most 16 m/s² of horizontal acceleration and 90 rad/s² of rolling acceleration. Multiple active balls can roll out together, with at most two new searches started per check and a bounded search budget. This is a gameplay recovery assist; temporarily relieving the robot's own contacts is an approximation, rather than a new mechanical model of fuel deformation.

## Validation

The contract fixture measures every seeded ball's displacement on each physics step and requires no step above 40 mm. It also checks intermediate movement, exact tire support counts, zero assistance with no grounded tires, and restored collision pairs. Two added cases interrupt an active roll-out by losing support or disabling the helper. Free-body, control, collection/retention, shooting and corner-pile checks follow on the same build.

This private Windows trial retains manifest 1.0.2 and uses a distinct archive label. It does not replace the prior private ZIP or the published Steam build. Linux/macOS and the published archives stay unchanged.

## Reproduction

Use `Tools/Invoke-Unity.ps1` with the pinned Unity 2023.2.22f1 editor, project `Artifacts/PumpingProject-20261121`. Run `Build`, then these tasks with `-Visible`: `Underbody`; `Traction`; `Traction -DiagnosticArguments @('-turboInboardPair','-turboReverse')`; `Controls`; `Systems`; `Capacity`; and `Mobility -DiagnosticArguments @('-turboMobilityCornerOnly','-turboMobilityAsBuiltOnly')`.

The report names and fingerprints are bound by `build.json` and `verification.json`. `python Tools/package_underbody_trial.py --rolling --check-only` verifies the complete evidence. Omit `--check-only` to create the distinct `1.0.2-rolling-clearance-trial-Windows.zip` archive after installation/startup verification.

The contact-contract fixtures use a frozen chassis to isolate the support count. The free-body matrices and corner fixture test actual driving separately. The physical-step trace establishes continuous motion; native owner feedback is a separate visual and recovery check.

## Automated results

All 15 support/contact-contract cases pass. Single balls roll clear in about 0.549 seconds; the nine-ball piles complete their individual roll-outs within about 0.617 seconds in this fixture. The largest observed single physics-step displacement is 9.9 mm, including cancellation cases. Every checked collision pair returns to its prior state after completion, losing ground, and disabling the helper.

All 26 free-body driving cases, 58 control checks, two 30-ball collection/retention/release cases, and both corner-pile escapes pass. Four fully unsupported controls receive no clearance assistance. The seven-case mechanism/shooting run launches all 40 balls and scores 39, within its unchanged acceptance of at least seven of eight per shooting range. All 60 collected balls are retained through loaded driving and released without escapes.

A focused repeat at 3 m adds read-only clearance counters to the editor harness. Its eight balls all launch and score; clearance assistance stays at zero in both driving cases and the shooting case. This supports ordinary shot variation as the explanation for the earlier one-shot miss; it does not turn the first run into a 40/40 result. The initial seven-case report and focused repeat are both preserved. The repeat's report is `systems-diagnostic-after.json` because diagnostic arguments deliberately change the report label.

No runtime tuning changed after the build: the additional shooting counters belong only to the editor report. Native startup verification and owner visual feedback are recorded separately.

## Installed native trial

The installed player loads module `c922a9b4-791c-4efb-9c00-7b2e0623a5ce`. Its startup capture contains the new active-clearance, ignored-pair and assisted-step fields and observes four grounded tires. Both settings files remain byte-identical after startup. This confirms loading and ground-state observation; it is not a native visual/recovery test. The owner has been asked to try the running trial; feedback is pending.

The previous instant-clearance private ZIP and every published Steam payload remain unchanged. The new ZIP uses the separate `rolling-clearance-trial` filename. No Steam upload is performed.
