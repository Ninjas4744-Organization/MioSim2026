# Version 1.0.1 verification

All release gates pass for the Windows 1.0.1 build. The 14 editor fixture groups cover 52 cases plus 15 integration flags; all 34 control checks and the 480-step Burst net-equivalence regression pass. The latter compared 929,182 ordered impulses with bitwise matching state. No experimental force, material, geometry or physics changes were selected. A later selector-logo edit removed the 4744 text. The runtime DLL is byte-identical; all 2,607 non-logo bundle objects and every streamed mesh/texture resource are identical in content. Only the selector Sprite and Texture2D changed. [Artwork equivalence proof](../Reports/selector-artwork-equivalence-1.0.1.json) binds the original test artifacts to the final bundle; raw reports and installed-artifact receipts remain unchanged. The 34 control checks and selector preview were repeated on the final artwork bundle.

[Release status](../Reports/release-status-1.0.1.json) · [Editor certificate](../Reports/verification-1.0.1.json) · [Controls](../Reports/simple-controls.json) · [Net equivalence](../Reports/Release-1.0.1/net-collision-burst.json)

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

The full-hopper editor cases retained and released all 52 and 53 collected balls through driving, turns, disable and empty stow. The existing 3.5 m/s loaded-speed threshold remains unchanged. Every certificate fingerprints the packaged Windows DLL and bundle; the audited editor compilation is a distinct assembly.

## Native load-limited stow and recovery

The owner confirmed that the real hopper does not fully retract when a full load or another physical obstruction prevents closure. The earlier release hold required a physically inappropriate 60-ball closure. Acceptance now checks the actual intended behavior: retain the full load during eight seconds of blocked stow, shoot empty, then fully close both slides to within 2 mm. Both control variants are covered by [the full-hopper certificate](../Reports/native-full-hopper-1.0.1.json).

| Run | Controls | Stored | Retained during blocked stow | Released | Empty-stow maximum extension (mm) |
| --- | --- | ---: | ---: | ---: | ---: |
| [101-final-full-simple](../Reports/native/101-final-full-simple.json) | Turbo | 62 | 62 | 62 | 1.4 |
| [101-final-full-simple-60](../Reports/native/101-final-full-simple-60.json) | Turbo | 59 | 59 | 59 | 1.5 |
| [101-final-full-actual](../Reports/native/101-final-full-actual.json) | Actual Turbo | 58 | 58 | 58 | 1.4 |

Every case ended empty with zero stored spills. Native Burst execution was confirmed. Recorded phase frames in these full-load checks peaked at 9.79 ms on this computer; this is a local observation, not a hardware-independent performance guarantee.

## Native loaded transit

The [trench certificate](../Reports/native-trench-1.0.1.json) covers three partially loaded crossings, both alliances, slow and normal gears, and the default Turbo controls. The loaded pre-transit settling limit is 10 mm; the actual crossbar-clearance limit stays below 15 mm, with every stored body retained and no sustained stop under the bar. Empty-stow recovery retains the separate 2 mm criterion.

| Run | Alliance | Gear | Stored / retained | Maximum rail extension under bar (mm) | Maximum recorded phase frame (ms) |
| --- | --- | ---: | ---: | ---: | ---: |
| [101-final-trench-simple-blue-clearance](../Reports/native/101-final-trench-simple-blue-clearance.json) | Blue | 1 | 22 / 22 | 3.1 | 8.34 |
| [101-final-trench-actual-blue](../Reports/native/101-final-trench-actual-blue.json) | Blue | 2 | 23 / 23 | 2.9 | 8.46 |
| [101-final-trench-actual-red](../Reports/native/101-final-trench-actual-red.json) | Red | 2 | 24 / 24 | 10.7 | 10.00 |

The [normal-speed bump/delivery cycle](../Reports/native-bump-1.0.1.json) retained 22/22 balls, released all 22, delivered 21 within 1.25 m, and ended empty. This exceeds the existing 7/8 delivery criterion. Peak tilt was 15.14 degrees and impact rail deflection 34.5 mm, within the existing 35 mm Travel impact envelope. This bump envelope does not replace the stricter trench limit.

All native acceptance uses the original 504 independent field fuel bodies, a 4.5 ms physics step and ordinary input actions, with all diagnostic overrides disabled. Rail force/damping remain 350 N / 4200 N s/m; intake budgets remain 600 N total and 100 N traction. The net calibration and bounded fuel response are unchanged. See [native test reproduction](NativeTesting.md).

## Investigation history

The [held candidate status](../Reports/Release-1.0.1/held-candidate-status.json) preserves the earlier aborted full-closure requests, including the unchanged Steam 1.0.0 control. Those requests timed out before any transit attempt; they are not relabeled as passing crossings. The [isolated comparisons](../Reports/LoadedStow/investigation-summary.json) were discarded after the real behavior was confirmed. The subsequent 2 mm requests with 29 and 23 balls retained their load but settled 8.3 and 4.7 mm short; their raw failures also remain preserved. The final transit requests use the stated 10 mm settling envelope and must still pass the 15 mm measured crossing limit.

The first approach-fixture attempt held intake before input initialization. A one-second neutral start corrected that harness; all six approach cases passed with unchanged physical thresholds. [Preserved attempt](../Reports/Release-1.0.1/approach-initial-input-failure.json). Historical 0.6.2 full-load trench records used the earlier fixed-bin geometry and are not evidence for the current sliding hopper.

## Artifacts and platforms

Bundle SHA-256: `546b25f35e76b30ccee677ab20442152a7fc13028780958f7273b09ced6de41f`

Player DLL SHA-256: `e96d2ee52cfc0801d2d0f25ccb7f911ef8ece421245e288e658a7054ee4278e6`

The supplied Ninjas logo, with the 4744 text removed, was verified in the installed Windows robot selector. Linux/macOS DLLs, asset bundles and binary Unity bundle manifests are preserved byte for byte from Steam; only their JSON version labels change to 1.0.1 for the uploader. Their existing behavior and artwork remain. [Original fingerprints](../Reports/Release-1.0.1/preserved-platform-source.json).

[Steam preparation and upload](Steam-1.0.1.md) · [Reduced CPU comparison](WeakCpuComparison.md)
