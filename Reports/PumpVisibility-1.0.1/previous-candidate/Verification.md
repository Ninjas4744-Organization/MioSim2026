# Version 1.0.1 tester verification

**Steam release remains on hold.** The tester's requested control fixes pass against the current Windows DLL and bundle: pumping runs only during a shot, pending/active pumps stop when shooting stops, and the intake remains deployed after the hopper empties and the shot ends. The owner reports that the original lag is gone or improved.

All 58 keyboard/gamepad/control checks pass, including physical three-ball shooting in both variants. All 14 editor fixture groups pass (52 cases plus 15 integration flags). The repeated Burst comparison passes 480 steps and 929,182 ordered impulses with bitwise matching state. These are fresh reports for this runtime; historical artwork equivalence is not used to bind old gameplay results to the changed DLL.

[Release hold](../Reports/release-status-1.0.1.json) · [Controls](../Reports/simple-controls.json) · [Editor certificate](../Reports/verification-1.0.1.json) · [Net comparison](../Reports/Release-1.0.1/net-collision-burst.json)

| Fixture group | Cases | Result |
| --- | ---: | --- |
| [Integration](../Reports/physics-diagnostics.json) | 0 | Pass |
| [Travel](../Reports/travel-after.json) | 4 | Pass |
| [Retention](../Reports/retention-after.json) | 2 | Pass |
| [Approach](../Reports/approach-after.json) | 6 | Pass |
| [Systems](../Reports/systems-after.json) | 7 | Pass |
| [Jam](../Reports/systems-jam.json) | 1 | Pass |
| [Indexer](../Reports/systems-indexer-after.json) | 11 | Pass |
| [Moving](../Reports/systems-moving-after.json) | 2 | Pass |
| [Delivery](../Reports/systems-delivery-after.json) | 4 | Pass |
| [Matrix](../Reports/systems-matrix-after.json) | 8 | Pass |
| [Capacity](../Reports/capacity-after.json) | 2 | Pass |
| [Pile](../Reports/pile-after.json) | 2 | Pass |
| [WheelPile](../Reports/wheel-pile-after.json) | 2 | Pass |
| [Pulse](../Reports/systems-pulse-after.json) | 1 | Pass |

## Installed full-hopper behavior

Each completed run retained its stored cohort during eight seconds of load-blocked stow, fired every stored ball, and ended empty. Collection did not pump. After the empty shot ended, pumping stopped and both slides stayed deployed during the three-second check; only explicit Stow closed them. The owner's clarification that a full hopper cannot physically retract remains the acceptance policy.

| Run | Controls | Stored | Released | Final empty-stow maximum (mm) | Strict <2 mm gate |
| --- | --- | ---: | ---: | ---: | --- |
| [101-controls-full-simple-final](../Reports/native/101-controls-full-simple-final.json) | Turbo | 58 | 58 | 1.8 | Pass |
| [101-controls-full-simple-60](../Reports/native/101-controls-full-simple-60.json) | Turbo | 61 | 61 | 2.1 | Hold |
| [101-controls-full-actual](../Reports/native/101-controls-full-actual.json) | Actual Turbo | 60 | 60 | 2.0 | Hold |

The trace records rail positions to 0.1 mm. The 2.0 and 2.1 mm readings do not pass the original strict below-2-mm test and are not relabeled as passing. The 58-ball run held final Stow for four seconds; the 60-ball Actual Turbo run held it for six. The controller has a 1.5 mm settling band on each slide and commands the hopper from measured intake travel; residuals adding across that coupling are a likely explanation. No precision, force or physics change was selected for this tester package.

## Loaded transit and remaining checks

The [Blue default-controls trench run](../Reports/native/101-controls-trench-simple-blue.json) retained 22/22 balls and fired all 22 afterward. Both-alliance and normal-gear trench recertification is incomplete for this runtime; the [current aggregate](../Reports/native-trench-1.0.1.json) is explicitly not passing.

The [default-controls bump run](../Reports/native/101-controls-bump-simple-blue.json) retained and delivered 23/23 balls, but peak rail deflection was 37.2 mm against the existing 35 mm limit. A [separate Actual Turbo comparison](../Reports/native/101-controls-bump-actual-blue-settled.json), requiring the slides to settle within 5 mm before departure, retained and delivered 22/22 with 33.8 mm peak deflection. That conditional comparison does not clear the earlier 23-ball departure failure. Normal gear, original forces and the 35 mm impact limit were preserved.

All native checks used the original 504 field fuel bodies, 4.5 ms physics step, normal inputs and disabled diagnostic overrides. The [serialized asset comparison](../Reports/Release-1.0.1/tester-controls-physics-assets.json) found all physics settings, colliders, joints, materials and streamed meshes/textures unchanged. Of 2,609 bundle objects, only three tutorial text fields and the rebuilt native Burst binary differ from the previous tester build. Runtime control behavior changed as documented.

## Preserved attempts and provenance

The [initial concurrent native attempt](../Reports/native/101-controls-full-simple.json) was interrupted after the editor took focus and paused MoSim. It is incomplete and excluded from certification. The [concurrent wheel-path attempt](../Reports/Release-1.0.1/wheel-pile-concurrent-attempt.json) retained and fired every ball but measured 3.20 degrees of heading drift against 3 degrees. Its isolated repeat passed with unchanged physics and thresholds. The native checks thereafter ran separately from Unity.

The earlier passing 1.0.1 certificates are archived under [pre-tester-fixes](../Reports/Release-1.0.1/pre-tester-fixes/release-status-1.0.1.json). They apply to the earlier DLL and are not current certification. Historical native reports and discarded loaded-stow investigations remain preserved.

Bundle SHA-256: `ca5935cb1ca55cd1937d7313b74e1950408b99eb0c3ede0f32e28b63e5ade941`

Player DLL SHA-256: `40cbd627854741e8b5d3e77452d563f2e9ee64fe3ca7f66cd73f88e961b2015c`

Linux/macOS binaries and bundles remain byte-identical to the published Steam builds; only their candidate manifest version labels change to 1.0.1. [Original platform fingerprints](../Reports/Release-1.0.1/preserved-platform-source.json). The current deliverable is a Windows tester ZIP and a held Steam candidate, not an approved upload.
