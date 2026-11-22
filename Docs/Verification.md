# Turbo 1.0.1 verification

**Release checks complete; prepared for the authorized Steam upload.** All certificates below match the Windows DLL and bundle fingerprints at the end of this page. Linux/macOS retain their published binaries and bundles.

## Release fixtures

| Fixture | Cases | Result |
| --- | --- | --- |
| Integration | Full cycle | Passed |
| Travel | 4 | Passed |
| Retention | 2 | Passed |
| Approach | 6 | Passed |
| Systems | 7 | Passed |
| Jam | 1 | Passed |
| Indexer | 11 | Passed |
| Moving | 2 | Passed |
| Delivery | 4 | Passed |
| Matrix | 8 | Passed |
| Capacity | 2 | Passed |
| Pile | 2 | Passed |
| WheelPile | 2 | Passed |
| Pulse | 1 | Passed |

The [complete fixture certificate](../Reports/verification-1.0.1.json) includes the existing pickup, retention, drift, scoring, impact and pair-timing gates. Tests use the packaged assets, normal simulation speed and the 4.5 ms physics step.

## Installed-game checks

| Full hopper | Stored and retained during blocked stow | Released | Final empty stow |
| --- | --- | --- | --- |
| Turbo | 58 | 58 | 1.5 mm |
| Actual Turbo | 62 | 62 | 1.4 mm |

Both variants retain their full stored cohort during load-blocked closure, shoot empty, leave the intake open after shoot release/cancellation, and retract only after an explicit final Stow. Collection does not pump. [Full-hopper certificate](../Reports/native-full-hopper-1.0.1.json).

| Trench cycle | Gear | Stored / retained | Maximum extension under crossbar |
| --- | --- | --- | --- |
| Blue / Turbo | 1 | 23 / 23 | 6.3 mm |
| Red / Actual Turbo | 2 | 24 / 24 | 5.1 mm |
| Blue / Actual Turbo | 2 | 23 / 23 | 3.7 mm |

All three cycles preserve every stored ball, record zero stored spills or sustained holds, and shoot empty. The existing limits remain 10 mm before transit and 15 mm under the crossbar. These partial loads permit stow; the full hopper remains physically load limited. [Trench certificate](../Reports/native-trench-1.0.1.json).

The [bump/delivery check](../Reports/native-bump-1.0.1.json) retains 23/23, releases 23, delivers 22 within the unchanged 1.25 m target tolerance, and ends empty. Maximum slide extension is 9.5 mm against the existing 35 mm impact limit.

## Corrections and focused checks

The slide controller now compares both rigidbodies' velocities at the same world point. The previous centre-of-mass/origin comparison interpreted chassis pitching as rail movement and commanded the closed hopper forward. A [36-case regression](../Reports/RailVelocity-1.0.1/rail-velocity-invariant.json) covers four headings, both pitch directions and genuine forward/reverse slide motion. The old calculation errs by up to 0.6 m/s; the corrected calculation stays within 0.00000009 m/s. The same 23-ball native bump changes from 45.5 mm to 9.5 mm maximum deflection, preserving the 350 N force, 4200 N s/m damping, speed, acceleration and travel limits. The [pre-fix trace](../Reports/native/101-steam-bump-simple-blue.json) remains preserved.

All [38 contact-recovery cases and 58 packaged control checks](../Reports/RailVelocity-1.0.1/verification.json) pass, including every module in both drive directions and intake states. All six fully unsupported controls retain zero powered ground patches. Probe/tire centres stay within 0.1 mm. The [asset comparison](../Reports/RailVelocity-1.0.1/asset-comparison.json) preserves all 2,608 scene/mechanism/material/mesh/texture objects and the net's executable code; only native build metadata differs in the bundle. Native net equivalence passes 480 steps and 929,182 compared impulses.

The [jam-fixture correction](../Reports/Release-1.0.1/jam-fixture-correction.json) disables the artificial support cage immediately and wakes its resting fuel once after removal. The original sleeping-ball failures remain preserved. This is an editor fixture change; it adds no production recovery force. Invalid late-start native routes and the stricter 2 mm partial-load stow timeout remain in the raw native evidence; accepted trench runs use the existing 10/15 mm protocol with input takeover from enable.

The original field contains 504 independent fuel bodies. Native checks use normal keys with no diagnostic force, geometry, compliance, net or drivetrain overrides. These are measurements on this Windows PC, not a guarantee for every hardware configuration. [Earlier weak-CPU comparison](WeakCpuComparison.md).

Player DLL SHA-256: `588232c7cdd68fd46f83624e124c4d4cfe6c135ee0934bfddc2134ce487f258c`

Bundle SHA-256: `0c0dc2b59a93bc17e5519030af81b5acd2556b760cf8edf2a58ddcc8683b35b5`
