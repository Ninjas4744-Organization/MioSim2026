# Version 1.0.1 pump visibility tester

**Windows tester build; Steam remains on hold.** The regular automatic stroke is now 8.5–10.5 motor rotations, approximately **41.7 mm instead of 20.9 mm** at the hopper. Its midpoint stays the same. The hard-pump stroke is unchanged. The mesh, rigid walls and net frame move together; no cosmetic offset separates them from the physical bin.

Force (350 N), damping (4200 N s/m), speed, acceleration, physical end stops, collision settings and net calibration are unchanged. [All 2,609 bundle objects and the native Burst payload match the prior tester](../Reports/PumpVisibility-1.0.1/asset-comparison.json); the changed runtime command determines the wider motion.

## Current checks

The [single-ball traction investigation](../Reports/Traction-1.0.1/README.md) adds 28 focused trials against the same current runtime: forward/reverse, intake stowed/deployed, fuel under each corner and the chassis center, plus clear-carpet and fully beached controls. All pass. Several cases drive with only two supported tires; all four fully clear produces no powered movement. These are empty-hopper fixtures, not a reproduction or resolution of the user's stuck case. No drive physics change was made. The installed Steam game's five relevant drivetrain method bodies also match the SDK player code used by the fixtures.

- [58 control checks](../Reports/simple-controls.json) pass for both variants, including shooting-only pumping, cancellation, keeping intake deployment after emptying, keyboard/gamepad and physical three-ball shooting.
- [Automatic hub](../Reports/PumpVisibility-1.0.1/automatic-hub.json): 3/3 fired and scored, with no pumping during collection or after emptying.
- [Automatic delivery](../Reports/PumpVisibility-1.0.1/automatic-delivery.json): all four cases delivered 3/3, with the same pumping checks.
- [Standard systems](../Reports/PumpVisibility-1.0.1/systems.json): all seven cases pass; the five shooting distances scored 8/8 each and completed three hard-pump cycles.
- [Installed full-load motion and shooting](../Reports/PumpVisibility-1.0.1/native-motion.json): Actual Turbo retained and fired all **58 fully stored balls**, ending empty with no manual hard-pump requests. Peak capture reached 60 including incoming fuel. Observed soft-pump movement spanned 46.4 mm including startup travel; it is not a measured steady-cycle amplification ratio.

The native test used the original 504 field fuel bodies and 4.5 ms step, with diagnostic overrides off. It took keyboard control at match start and held neutral for twenty seconds before ordinary collection and shooting. Two earlier default-controls setup attempts collected small loads and are preserved under labels `101-visible-pump-full-simple` and `101-visible-pump-full-neutral-start`; they are not full-hopper certification. The qualifying motion run used Actual Turbo's automatic intake deployment. Both variants share the changed pump motion code.

## Release scope

Only the current checks above apply to the new runtime. The earlier 14-group/52-case certificate applies to commit `8398c3a`; its [status](../Reports/PumpVisibility-1.0.1/previous-candidate/release-status-1.0.1.json) and [verification](../Reports/PumpVisibility-1.0.1/previous-candidate/verification-1.0.1.json) are archived. Existing root native certificates also fingerprint that older DLL and are not current certification.

Previously reported final empty-stow readings of 2.0–2.1 mm failed the strict below-2-mm gate. A 23-ball bump departure measured 37.2 mm against 35 mm; a separate, more fully settled 22-ball comparison passed at 33.8 mm. These limits were not relaxed or cleared by this pumping change. Full release and both-alliance transit checks remain required before Steam upload. [Current release hold](../Reports/release-status-1.0.1.json).

The updated Windows tester ZIP is the deliverable. Linux/macOS binaries and bundles remain unchanged. The prior held Steam candidate uses the earlier DLL and is archived; it is not an upload package for this stroke change.

Bundle SHA-256: `ca5935cb1ca55cd1937d7313b74e1950408b99eb0c3ede0f32e28b63e5ade941`

Player DLL SHA-256: `e5900e28f295b14c4d3426fe6e8552c976c31fa6759f4f93ffb2f2ce28a5b5c6`
