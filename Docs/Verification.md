# Turbo 0.5.1 verification

Release scoring ran at normal simulation speed. Collection, travel and scoring load the Windows asset bundle; the integration check exercises the authoring prefab.

- Field collection: 18 collected / 18 retained across two headings; no escapes through driving, turning, disable and stow.
- Loaded drive: 20/20 retained; forward 4.64, reverse 4.62, strafe 3.07 m/s.
- Dense-pile collection: 57/60 collected; every captured ball retained through loaded keyboard driving, then released through the shooter before empty stow. Loaded peak speeds 4.36–4.59 m/s.
- Between-frame control taps: 8/8 scored; 3 hard cycles from two short shoot-button pulses.
- Full-cycle release: 57/57 collected balls left through the shooter; both empty stow checks passed.
- Moving hub shots: 16/16 scored through normal keyboard drive actions.
- Both-alliance stationary shots: 62/64 scored.
- Automatic side delivery: 32/32 landed within 1.25 m of the selected side target and inside field boundaries.
- Rendered intake movement during hard pumping: 7.3 cm minimum; sampled on the actual mesh, not only the rail body.
- Four-heading impact check: maximum intake 0.240001 m; hopper 0.229463 m. Original strict travel bounds passed.
- Jam recovery: 0.504 s reverse, then resumed scoring.

| Robot-center distance | Launched | Scored | Hard pump cycles |
| --- | --- | --- | --- |
| 2.25 m | 8/8 | 8/8 | 3 |
| 3 m | 8/8 | 8/8 | 3 |
| 4 m | 8/8 | 8/8 | 3 |
| 4.5 m | 8/8 | 8/8 | 3 |
| 5 m | 8/8 | 8/8 | 3 |

Shots begin with 30 degrees of heading error on the clear alliance side. The results cover these fixtures; the physical model remains a MoSim approximation.

Bundle SHA-256: `740c21b798a25115fbf556a23c249f52503e896624722c0846494d5de9e0ee8c`

DLL SHA-256: `1059af067a016b052389b570db93299f22c8cabc6b456c42daad121fe4a8bfb3`

Native game: the v0.5 preview loaded in installed MoSim v26.6. The user stopped computer control with Escape before the final native playtest. Restart the game to load this final build and perform the remaining native playtest.

See `Reports/` and the [team-code mapping](RobotCodePort.md) for details.
