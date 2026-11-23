# Post-release drivability investigation

The owner reported slower control on bumps and a ball trapped after corner shooting. Steam still contains the original published 1.0.1. This branch prepares a private Windows trial; native feedback on this trial is pending.

## Native evidence

The three [captured recordings](captures/) use published DLL module `069182be-8e39-44bb-b0a5-c0049bd7c821`. They show commanded movement with very low speed, one supported tire, and bumper contacts against the bump's ramp or carpet. Bodies are dynamic and unconstrained. These recordings capture the bump problem; they do not independently reproduce the reported corner ball trap.

The owner clarified that this occurrence could slowly reverse; complete lockups were observed in other matches. Preserve that distinction when interpreting the data.

## Scope of the correction

The existing bumper foam law previously applied only to near-horizontal contact normals, excluding ramp/carpet support. The trial applies that same low-friction, bounded 8 mm response to bumper contacts against all static field faces. Rigid panel normal support, tire friction, wheel queries/suspension, motor settings, joystick shaping, fuel contacts and robot contacts are unchanged. No recovery force, pose reset, fuel deletion or airborne propulsion is added to the robot.

The [asset comparison](asset-comparison.json) confirms that the entire bundle, including native net code and all 2,609 objects, remains byte-identical to the published 1.0.1 bundle. Only the managed robot DLL changes.

## Captured-pose comparison

The fixture initializes the recorded chassis poses on the SDK field, lets the robot settle, and tries both recorded drive directions. It stops after 1.5 m of escape, before reaching the far wall. It does not restore every mechanism, fuel and motor state from the live game, so this is an approximation requiring native feedback.

| Capture / direction | Published escape | Trial escape | Longest stationary pause, published / trial |
| --- | --- | --- | --- |
| 044725 / +1 | 2.70 s | 1.05 s | 0.958 / 0.004 s |
| 044725 / -1 | 0.63 s | 0.50 s | 0.013 / 0.004 s |
| 044736 / +1 | 1.32 s | 0.78 s | 0.067 / 0.004 s |
| 044736 / -1 | 1.21 s | 0.85 s | 0.202 / 0.004 s |
| 044743 / +1 | 0.90 s | 0.74 s | 0.009 / 0.000 s |
| 044743 / -1 | Not clear in 5 s | 3.01 s | 3.735 / 0.013 s |

[Corrected baseline/prototype comparison](mobility-comparison-targeted-captured.json), [built trial result](candidate-captured-poses.json).

The built trial clears all six directions. One escape still takes about three seconds; do not describe every recovery as immediate. Acceptance requires 1.2 m of movement and no stationary pause above 0.4 seconds. Earlier five-second comparisons continue to the far field wall and confound escape with later wall stops; their raw traces are retained but excluded from the latency conclusion.

## Other checks and limits

The corrected corner fixture uses 49 independent stock fuel bodies inside the game's active field bounds, two static walls, intake collection, manual shooting, and diagonal retreat. The two orientations load/feed 1 and 22 balls respectively, empty the captured cohort and escape. [Result](candidate-corner-pile.json). It does not reproduce a persistent under-module trap; that remains a native feedback case.

All 58 packaged control checks and 38 packaged module-contact cases pass. All six fully unsupported controls have zero powered patches and less than 0.05 m movement. The eight authoring trench/solid-wall cases also pass with zero recorded wall penetration. [Final automated certificate](verification.json), [controls](candidate-controls.json), [trench](candidate-trench.json).

The [338-method comparison](runtime-method-comparison.json) finds only the bumper contact resolver changed after canonicalizing the compiler-generated job-registration type identifier. The raw identifier differences are retained separately. The local trial is installed, its native module is verified running, and owner feedback remains pending.

The isolated wheel-setting comparison restores only the old wheel settings on the 1.0.1 robot. It is not a complete 1.0.0 simulation. The old asset's [wheel settings](1.0.0-wheel-settings.json) confirm 25 mm travel, 50,000 spring, 3,500 damping and 10 mm contact tolerance. The 1.0.1 runtime uses zero travel/spring and 2 mm tolerance. Restoring those settings alone did not clear every recorded pose; this does not contradict the owner's comparison of the complete builds.

The initial synthetic corner fixtures were outside the game's fuel bounds, obtained zero captured balls and are invalid shooting checks. [Invalid result](invalid-corner-outside-field.json). The corrected result above requires a nonempty captured cohort and observed feeding before accepting escape.

## Reproduction

Use the pinned project and editor from the repository instructions:

```powershell
.\Tools\Invoke-Unity.ps1 -MoSimProject $turboProject -UnityEditor $turboUnity -Task Mobility -Visible -DiagnosticArguments @('-turboMobilityCaptured','-turboMobilityAsBuiltOnly')
.\Tools\Invoke-Unity.ps1 -MoSimProject $turboProject -UnityEditor $turboUnity -Task Mobility -Visible -DiagnosticArguments @('-turboMobilityCornerOnly','-turboMobilityAsBuiltOnly')
```

The wrapper stages the tracked captured chassis poses. The private ZIP is `dist/Ninjas4744-Turbo2026-1.0.1-drivability-trial-Windows.zip`; its manifest retains 1.0.1 and the filename/fingerprint identify the trial. The official Steam package and all published Linux/macOS files remain preserved.
