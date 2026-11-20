# Turbo 0.5.6 physics verification

Verified against the exact packaged Windows bundle and DLL on 20 November 2026. Counts below are per hopper, not totals across runs.

- Heading 0 degrees: 53 stored before driving; 53 retained through braking, turning and disable; 53 released through the shooter, zero escapes and empty stow.
- Heading 90 degrees: 53 stored before driving; 53 retained through braking, turning and disable; 53 released through the shooter, zero escapes and empty stow.

The separate wheel-path fixture approaches ninety balls across six lanes at 3 m/s. Both headings must stay within three degrees of heading drift, retain every captured ball through loaded driving and unload completely.

Stationary shots scored 40/40; alliance matrix 64/64; moving shots 15/16; delivery 32/32.

The integration, rail-impact, approach, retention, jam-recovery and pulse-control checks also passed. Loaded rail stow is allowed four seconds, matching the retention fixture, so the kicker can fold before the slides retract. The initial two-second result is preserved in travel-initial-two-second.json. Fuel uses the official independent rigidbodies, with unchanged size and mass. Nothing is parented, frozen, teleported or assigned a storage velocity.

Bundle SHA-256: `14c11ee817683954e30eeff4cb2085cd5eae5da7b8df0432488ec4636f0acd06`

DLL SHA-256: `81b2af550ae2c02cf810fc583df0548384ad3dc1df7d1c3168a39388a13ef8cf`

The initial dense-pile run drifted 2.46 and 2.07 degrees, with 12 mm and 8 mm maximum cross-track error. It failed the initial 2-degree heading criterion while retaining and unloading all 51 and 45 captured balls. The final fixture permits 3 degrees because native swerve commands angular velocity rather than absolute heading; the initial measurements remain in wheel-pile-initial-2deg.json. Detailed fixtures and samples are in the JSON reports. The loaded-hopper images show the deforming net at each capacity heading.
