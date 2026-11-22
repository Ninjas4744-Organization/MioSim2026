# Version 1.0.1 landing diagnostic tester

**Windows diagnostic build; Steam remains on hold.** A loaded rear-first landing on one loose ball reproduced a beach. All four drive contact patches were empty for about 5.5 seconds while input remained commanded. That run did not measure solid tire clearance, so it does not conclusively distinguish a physically lifted chassis from missed contact queries. No physics fix is claimed.

The opt-in local recorder now measures each wheel's contacts, load, tread speed, solid tire lower bounds and recent physical field collisions alongside input, body movement and underside collisions. It never sets input, position, velocity, forces or contact properties. [Recording instructions and landing evidence](../Reports/Landing-1.0.1/README.md).

## Current checks

- [58 controls checks](../Reports/simple-controls.json) pass for both variants with recording off.
- [14 single-ball and all-tires-clear controls](../Reports/Landing-1.0.1/current-traction.json) pass with recording enabled. Partial support continues to drive; deliberately beached controls remain stationary.
- [Capture validation](../Reports/Landing-1.0.1/capture-validation.json) verifies two expected stall reports, including zero wheel load and measured tire clearance, with no false stall reports from the moving trials.
- [Asset comparison](../Reports/Landing-1.0.1/asset-comparison.json) finds all 2,608 scene, mechanism, collider, rigidbody, material, mesh and texture objects unchanged. The embedded Burst DLL differs only in linker/build metadata; its executable code and data match.
- [Native startup smoke](../Reports/Landing-1.0.1/native-smoke.json) records the installed runtime check separately; it is not a native full-hopper or transit certificate.

The landing investigation ran 28 trials on the earlier runtime and initial recorder prototype, including actual collection of 58–63 stored balls. Twenty-seven cleared, one remained beached, and every trial retained its stored cargo. Raw reports preserve their exact artifact hashes and the failed escape. Repeated trials vary with load and contact dynamics; the user report is not cleared by the passing cases.

## Release scope

Only the current focused checks above certify this diagnostic DLL. Prior automatic shooting, seven systems cases and native 58-ball shooting apply to the previous pump-visibility runtime, whose [status is archived](../Reports/Landing-1.0.1/previous-candidate/release-status-1.0.1.json). The earlier full 14-group certificate applies to commit `8398c3a`.

Final empty-stow readings of 2.0–2.1 mm failed the unchanged below-2-mm gate. A prior 23-ball bump departure measured 37.2 mm against 35 mm. These holds and both-alliance transit recertification remain outstanding. The new package is a diagnostic tester; no Steam upload is prepared or performed. Linux/macOS files remain unchanged.

Player DLL SHA-256: `a4466cab7be74b1e5e4adc8506689cc5c7defb0c34e7e275bd830fe4327d5e8f`

Bundle SHA-256: `2d11e7b3e7580f263c8fda0d4205570080ce001bc737bd9d740e0ac94a5a3583`
