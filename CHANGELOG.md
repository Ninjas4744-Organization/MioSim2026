# Changelog

## 0.5.1

Addresses zone selection, moving aim, full-hopper contacts and visible pumping.

- Added automatic HUB/DELIVERY zone selection and live target changes for standard shooting.
- Added separately calibrated side passes and MoSim flight-time compensation.
- Paused feed while re-aiming and retained short control presses through performed events.
- Replaced the powered hopper bed with tray-speed transfer; shared intake/indexer force across all contacting fuel.
- Raised bounded rail actuator capacity and reduced the simulated textile mass from 1.6 kg to 160 g.
- Added rendered-mesh motion, moving-shot, both-alliance, delivery and thirty-ball collection/loaded-drive checks.
- Added an optional F8 local state panel.

## 0.4.0

Corrects net deployment, pumping, loaded motion, and shooting behavior against the team's 2026 source.

- Added a pinned, deforming MoSim NetSolver cloth surface that folds with the sliding hopper.
- Ported latched prepare/ready/feed states, hard and soft pumping, rotor-speed tables, delivery timing, moving aim calculation, and hub drive slowdown from team commit `2bd3488`.
- Coupled hopper travel to the intake's measured position and velocity, so pumping acts on stored fuel.
- Corrected drive motor gains from metres per second to rotor units; added the team's joystick curve with explicit zero handling.
- Revised the storage/indexer contact layout and physical release hood; calibrated wheel contact speed separately from source rotor goals.
- Added source snapshots and behavior mapping, loaded-drive measurements, cloth checks, pump-cycle checks, and actual hub-score tests.
- Applied regression drive commands every physics step and tied release verification to the built bundle and DLL.

## 0.3.0

Corrects fuel spilling back out after collection.

- Added a continuous moving storage floor across the extended hopper.
- Completed the intake belt's curved entry contact and replaced the stepped indexer contacts with a continuous rising feed surface.
- Added a reduced inward intake hold speed after the collection command is released, including during retraction.
- Added per-ball one-way intake contacts: incoming fuel passes the inlet, then gains solid floor and inlet contacts once fully inside. Reverse releases these contacts; fuel remains independent rigidbodies.
- Kept added storage contacts out of wheel suspension collisions.
- Added a packaged-mod fuel retention regression covering pickup, intake release, forward/reverse motion, turns, disable, and loaded stow.
- Packaging now verifies that the passing retention report matches both the asset bundle and mod DLL by SHA-256.

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
