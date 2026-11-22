# Version 1.0.1 verification — release held

The 1.0.1 candidate passes all 14 editor fixture groups (52 cases plus the 15 integration flags), all 34 control checks, and the 480-step Burst net equivalence regression. Installed-player loaded stow failed, including in the unchanged Steam 1.0.0 control. Full native trench/bump certification is therefore incomplete. Steam publication remains on hold.

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

The full-hopper editor cases retained and released all 52 and 53 collected balls through driving, turns, disable and empty stow after shooting. The unchanged 3.5 m/s loaded-speed threshold passed. These checks use the built bundle and an audited editor compilation of the same runtime source; the player DLL is a distinct audited compilation.

## Native release hold

| Run | Peak captured | Stored before stow | Final intake / hopper extension | Result |
| --- | ---: | ---: | --- | --- |
| [101-blue-full](../Reports/native/101-blue-full.json) (candidate 1.0.1) | 59 | 59 | 0.0000 / 0.2247 m | Stow timeout |
| [101-blue-full-2](../Reports/native/101-blue-full-2.json) (candidate 1.0.1) | 56 | 50 | 0.0016 / 0.1756 m | Stow timeout |
| [101-steam-stow-control](../Reports/native/101-steam-stow-control.json) (Steam 1.0.0) | 57 | 55 | 0.0015 / 0.2065 m | Stow timeout |

The shortened collection request was identical between the second candidate run and the Steam control apart from its label; both used the same saved spawn and original control scheme. Both timed out after eight seconds. This reproduces the failure in the published build; it does not complete candidate certification. The tests keep the 2 mm pre-transit stow gate, 15 mm trench envelope, 50–60-ball load gate and all original force/contact settings. No native trench or bump certificate was fabricated or carried forward from 0.6.2.

The first approach-fixture attempt held intake before input initialization and stalled before collection. A one-second neutral start matching the retention fixture corrected the harness; all six approach cases then passed with unchanged physical thresholds. [Preserved failed attempt](../Reports/Release-1.0.1/approach-initial-input-failure.json).

The native 8-preload simple-control smoke test released all eight and ended empty. Its first collection route missed the field pile, so it is not counted as a full-hopper test. The corrected [default-control full-load run](../Reports/native/101-simple-full-shoot-2.json) collected 60, began shooting with 60, released all 60 and ended empty, with native Burst execution confirmed. Peak collection/shooting frames were 12.92/8.45 ms; peak tilt was 0.62 degrees. This stationary collection/holding/shooting test does not certify stowed transit.

## Artifacts and platforms

Bundle SHA-256: `bc74119c92bb3de639c4fefa43d48a6733bcc64bdda6815a404d0664a12d55fe`

Player DLL SHA-256: `e96d2ee52cfc0801d2d0f25ccb7f911ef8ece421245e288e658a7054ee4278e6`

The supplied Ninjas logo is used by the Windows robot selector and was checked visually in installed MoSim. All Linux/macOS DLLs, bundles and Unity bundle manifests are preserved from Steam; only their JSON version labels change to satisfy the uploader. Their existing behavior and artwork remain. [Original platform fingerprints](../Reports/Release-1.0.1/preserved-platform-source.json).

[Steam preparation and upload steps](Steam-1.0.1.md) · [Historical 0.6.2 verification](Verification-0.6.2.md) · [Reduced CPU comparison](WeakCpuComparison.md)
