# Changelog

## 1.0.1 (2026-11-22)

- Correct release acceptance to match the real load-limited hopper: retain full loads during blocked stow, shoot empty and recover full retraction. Verify both control variants, partially loaded trench travel on both alliances, and normal-speed bump delivery. Keep the original physics and actuator limits.
- Speed up Windows full-hopper net collisions with conservative spatial filtering and a player-compatible Burst job, preserving the original contact order and physics.
- Add the standard Turbo controls and retain the original controls as Actual Turbo.
- Use the supplied Ninjas 4744 logo in the robot selector.
- Preserve the published Linux and macOS DLLs and asset bundles unchanged; update their manifest version labels to 1.0.1 for the Steam uploader.

## Unreleased

- Add four solid swerve module housings and restore passive tire-to-fuel collisions, preventing balls from passing through the modules into the bellypan cutouts. Keep fuel excluded from suspension and powered tread queries. Add packaged collision and clear-floor drive regressions.

- Correct hopper pumping to move the bin model, rigid walls and front roof frame with the physical slide. Keep the indexer fixed and telescope the tray overlap. The supplied footage supersedes the earlier fixed-bin interpretation.

- Prevent trench pinches with bounded lateral bumper compression and low-friction sliding against static field sides, including rigid hopper/frame panels. Added loaded/empty rigid-panel pinches, stowed/deployed bumper pinches, reverse-escape and solid-wall regressions.
- Restore automatic hopper pumping for partial loads during hub shooting and delivery, starting with aiming and spin-up while stored fuel remains. Stop when storage clears or shooting ends. Preserve the calibrated strokes, jam-recovery continuity and manual hard-pump priority.
- Added three solid CAD bellypan contacts, preserving the wheel cutouts. The underside collides with fuel and field obstacles during every mechanism state; balls can lift or beach the chassis.
- Added integration and packaging checks for underside fuel blocking, ball-supported chassis lift, and field contact.
- Added a reproducible ZIP of the verified sliding-hopper test build, including commit provenance, file checksums and validation notes.

## 0.6.2 (2026-11-21)

- Corrected the roof contact strand diameter from 24 mm to the model's estimated 2.3 mm cord, and tensioned the textile to rest scale 1.0. Rejected 5% ease and swept-node contact experiments after native crossbar failures.
- Calibrated tread friction to 1.4 after full-load speed and wheel-path drift comparisons, preserving hardware drive constants and the 100 N collection traction cap.
- Resolve fuel contacts on every cloth substep to reduce tunneling during crossbar compression.
- Added static field-box contact resolution for free net nodes, including the trench crossbar, with chassis reaction to the contact impulse.
- Added spring-limited fuel-to-fuel contact inside the hopper: 750 N/m stiffness, 8 N·s/m closing damping and a 20 mm hardening threshold. Original fuel mass, sphere geometry, field contacts and released flight are preserved.
- Separated the collection motor's 100 N tangential traction budget from passive intake support, retaining the 600 N overall contact cap and the existing 140 N feeding profile. The moving intake contact follows its motor command during retraction; floor support latches independently of front-wall clearance.
- Increased slide velocity damping from 1800 to 4200 N s/m after loaded native stow stalled about 15 mm short, retaining the 350 N force limit.
- Added strict native trench acceptance using the actual stowed cohort: 50–60 before transit, every ball retained afterward, zero spills and no sustained stop under the bar. Timeouts fail without a wrong-zone fallback shot.
- Display stowed load, retained count, stored spills and incomplete inlet departures during native replays; preserve entry/clear screenshots and matching installed artifact hashes. Correct mirrored lane steering for Blue tests.
- Verified all fourteen visible fixture groups against the installed release. Native stowed trench cycles retained 57, 58, 60 and 56 balls without stored spills; the bump/delivery cycle retained and delivered all 56.
- Schedule the capacity fixtures' existing 0.5 s forward / 1 s reverse keyboard pulses on physics steps and measure their completed-step velocities. Preserve the 3.5 m/s loaded-speed threshold and the short-pulse/frame-sampling failures.

## 0.6.1 (2026-11-21)

- Added four solid bumper contacts at the measured CAD perimeter. The root bounds trigger remains hollow metadata.
- Replaced swept chassis CCD with discrete contacts at the native 4.5 ms step, removing the reproduced dense-fuel stop-and-jump motion.
- Stored roller definitions in player-safe Unity arrays after native inspection found the nested managed array loaded empty, leaving indexer traction inactive.
- Prevented the delayed soft-pump default after jam recovery from interrupting a pending or active hard-pump request.
- Corrected the assembly after the team confirmed only the intake/kicker retracts: the main hopper walls, floor and net frame now stay fixed. Removed the former moving-bin floor traction approximation.
- Detect lower-mouth entry when the ball surface reaches the existing mouth plane, so partially entering fuel receives the intended accelerator contact.
- Command the finite accelerator brake directly during re-aiming instead of waiting for the motor reference to slow down. Preserve the failed moving-shot report and isolated contact/brake comparisons.
- Preserved the original net slack after a tighter-net comparison failed the existing 50-ball fill minimum. Native trench retention failures remain recorded explicitly.
- Added an opt-in native MoSim input replay with visible status and actual box-collider outlines, collision-pair audit, muzzle crossings and delivery measurements.
- Require solid bumper surfaces in assembly and bundle checks; detect Unity execute-method failures even when the editor exits with code zero.
- Retain failed deployed trench retention and packed-hopper stow results in native evidence. A stronger finite slide-drive comparison also failed; the original drive calibration is preserved.
- Record actual powered bump scoring and delivery cycles separately from editor fixture acceptance.
- Preserve failed fixture reports and document the corrected preload, cloth-seeding and fixed-bin observation setups. Pair timing, collection, capacity and scoring thresholds remain unchanged.
- Moved the capacity fixture's zero-degree start inward after measuring an unintended 220 N·s field-boundary impact; preserve its driving sequence and require zero obstacle contacts during the loaded-drive segment.
- Kept the 5 m firing distance on a clear bearing after recording severe initial wall penetration at the old corner. Require zero setup contacts; five stock-geometry repeats scored 40/40.

## 0.6.0

- Replaced the rising indexer ramp with two functional roller floors at the CAD axes, including animated original roller meshes.
- Added passive support from the original triangular side-indexer envelopes and a lower shooter entrance.
- Widened the shooter to 0.313 m and made simultaneous contact independent of ball query order.
- Added a lower-entry accelerator path, safe gate closure and downward-transfer flow monitoring.
- Added dedicated floor, side-guide, single-ball and simultaneous-pair fixtures, with visible Unity views.
- Extended the simulator re-aim interlock to stop and brake staged accelerator fuel, with proportional speed calibration and separate accelerator ramp timing.
- Added bounded pump braking near the existing source encoder windows.
- Kept regenerated prefab assets synchronized with the authoring repository between assemble and test.

## 0.5.6

- Excluded fuel from Turbo's wheel support queries and rigid wheel cores, preventing moving balls from steering or lifting individual swerve modules.
- Confined powered intake contact to a narrow roller/throat path, raised its handoff over the kicker and bounded contact force and surface speed.
- Latched floor support after each ball clears the tray, so pressure and bouncing cannot reopen the floor beneath stored fuel.
- Added continuous intake backing support that clears the feed path when the kicker folds for stow.
- Added textile slack and physical frame-edge contacts; the net deforms around a larger load.
- Opened the upper rear storage bays and lowered the funnel lips, with sloped shoulder floors to return corner fuel to the indexer.
- Removed indexer downforce that pinned packed fuel against its support ramp, and shared tangential traction across the full load.
- Added separate single-hopper capacity and wide wheel-path pile checks, including loaded driving, braking and complete unloading.
- Corrected moving-shot lead for launch-point rotation while keeping heading feedforward stable.

## 0.5.5

- Replaced inferred supports and mesh cuts with 59 original CAD parts, restoring the compact front assembly by removing the source model's unsupported 13-degree down-tilt.
- Matched the supplied shooting footage by keeping the lower kicker deployed during short hopper pump strokes.
- Kept the hopper frame, front beam and upper bearing structure rigidly attached to the box; only the lower kicker swings.
- Added staged motion: hopper out first, then kicker down. Retraction raises the kicker before either slide moves inward.
- Added runtime sequence/pumping checks and a comparison against the supplied footage.

## 0.5.4

- Attached the transfer frame and lower kicker to the moving hopper with support rails, hinge bearings and mounting flanges.
- Matched the opened-assembly photo with a full-width aluminium beam above the roller and bolted clear side bearing plates.
- Folded the kicker back with actual hopper travel, lifting it before it crosses the bumper. Verified clearance through 101 positions.

## 0.5.3

- Added a local clearance rise in the deployed intake belt/support mesh so it passes 12 mm above the front bumper; the ground pickup roller stays in place.
- Reworked intake contact around a curved pickup-to-tray path and separated the front gate from storage-floor support, including during reverse.
- Removed duplicate fuel collisions and traction from helper rollers and limited indexer entry speed.
- Added continuous physical indexer support and a sloped return lip so fuel cannot drop below the feed path or jam loaded retraction.
- Added full-width pickup checks at 0.5, 2 and 4 m/s, including braking, disable and loaded stow.

## 0.5.2

- Made the hopper roof visible using MoSim's supplied net texture with larger openings and its original texture color.
- Rendered the net from both sides with alpha-clipped holes, depth and shadows. The cloth still folds with the sliding hopper.

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
