# Changelog

## 0.2.0

Corrects the intake/hopper runaway failure reported during gameplay.

- Replaced unbounded GenericJoint slides with native configurable joints and physical end stops.
- Added finite 120 N actuator limits, a 0.32 m/s speed limit, acceleration and braking control.
- Preserved rail constraints through disable/enable transitions.
- Fixed preload metadata: eight real fuel objects replace empty position markers.
- Lowered the transport contact bed so a loaded hopper can retract without trapping a second layer of fuel against the roof.
- Preserved fuel collision layers and excluded fuel bodies from robot self-collision ignores.
- Added visible intake roller motion and polished the authoring repository and build workflow.
- Added packaged-bundle travel regression tests at four headings, including an actuator-load impulse.

## 0.1.0

Initial CAD conversion, drivetrain, mechanism contacts, controls, metadata, and Windows build. Superseded because the rail setup had no physical travel limits and the preload test bypassed the game's actual spawn handler.
