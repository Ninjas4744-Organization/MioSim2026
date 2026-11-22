# Windows 1.0.1 contact correction candidate

**Installed and confirmed by the owner to recover from the reported landing. Steam remains held; nothing was uploaded.** This candidate aligns the traction probes with the rigid solid tires and uses the existing smooth feed-plastic friction profile on passive bellypan/module contacts. Tire grip, motor settings and solid collision shapes remain intact. All four fully lifted tires still provide no propulsion.

The native evidence showed a solid tire touching carpet while the drive model assigned only a small suspension load, alongside a lifted tire intermittently receiving a floor patch. A rigid-wheel-only candidate also reproduced a persistent two-ball trap. The underside friction correction clears that failed case. [Evidence, failed candidate and implementation scope](../Reports/BeachingFix-1.0.1/README.md).

## Current checks

- All **38 traction cases** pass: 16 inboard paired-ball recovery cases across all four corners, both drive directions and intake states; 10 centered/under-wheel fuel cases; six clear-carpet controls; six fully lifted controls.
- All **58 control checks** pass, including physical three-ball shooting in both variants, intake state, shooting-only pumping, cancellation, disabled states and rebinding.
- Probe/solid tire centers agree within 0.1 mm throughout the recorded fixtures. Fully lifted controls retain zero supported patches and negligible movement.
- The native Actual Turbo sequence collected a peak of 65 tagged balls, identified 59 fully stored balls before stow, began shooting with 60 captured balls, and released 62 tracked balls including later arrivals. No fully stored cohort escapes were recorded, and none remained captured after shooting. Maximum recorded collection/return/shoot frame time was below 9 ms. The final empty-stow measurement was below 2 mm in this run.
- The owner then tested the installed candidate and reported: **“It recovers now.”** Test automation and capture requests were removed, saved settings were restored byte-for-byte, and ordinary manual play was reopened.
- All 2,608 serialized scene/mechanism/mesh/material objects and the net's native executable code/data match the earlier bundle. The managed runtime intentionally changes wheel support and underside friction; serialized asset equality is not a claim that runtime physics parameters are unchanged.
- The eight published Linux/macOS files still match their preserved source hashes.

See the [fingerprint-bound verification](../Reports/BeachingFix-1.0.1/verification.json), [native functional check](../Reports/BeachingFix-1.0.1/native-full/summary.json), and [optional passive F9 captures](Landing-Capture.md).

## Release scope

These are focused contact-correction checks. The native full-hopper run includes an extra inlet ball and later arrivals; it is not substituted for the stricter complete release certificate. Both control variants/alliances and the full release fixture/transit matrix have not been recertified against this DLL. Earlier empty-stow and bump limits retain their historical results; one current empty-stow pass does not clear every release gate.

The Windows ZIP is a test candidate. The previous Steam staging and archive belong to earlier binaries. Linux/macOS are unchanged, and no Steam upload was prepared or performed for this candidate.

Player DLL SHA-256: `2533c62343f2ffc168aa54d8c156f2b0d016207ae56237ff3e1aa2405a9818c1`

Bundle SHA-256: `4ec30ddf9e758f25b0a8eb1cecb0900ede097bafa692c565b6dc44b6ab02f82e`
