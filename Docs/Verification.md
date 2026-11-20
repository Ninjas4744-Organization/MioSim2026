# Turbo 0.4.0 verification

Release scoring ran at normal simulation speed. Collection, travel and scoring load the Windows asset bundle; the integration check exercises the authoring prefab.

- Field collection: 18 collected / 18 retained across two headings; no escapes through driving, turning, disable and stow.
- Loaded drive: 20/20 retained; forward 4.60, reverse 4.62, strafe 3.06 m/s.
- Four-heading impact check: maximum intake 0.240001 m; hopper 0.229477 m. Original strict travel bounds passed.
- Jam recovery: 0.504 s reverse, then resumed scoring.

| Robot-center distance | Launched | Scored | Hard pump cycles |
| --- | --- | --- | --- |
| 2.25 m | 8/8 | 8/8 | 3 |
| 3 m | 8/8 | 8/8 | 3 |
| 4 m | 8/8 | 8/8 | 3 |
| 4.5 m | 8/8 | 8/8 | 3 |
| 5 m | 8/8 | 8/8 | 3 |

Shots begin with 30 degrees of heading error on the clear alliance side. The results cover these fixtures; the physical model remains a MoSim approximation.

Bundle SHA-256: `d5fdbb4c1773cfdb69cccd7ca23a4e5ddbdc91f4864b5295358b048a20d9a964`

DLL SHA-256: `facba85c3663271fca4235ba54632bdaacb51f439e36f0391f3803316645e518`

See `Reports/` and the [team-code mapping](RobotCodePort.md) for details.
