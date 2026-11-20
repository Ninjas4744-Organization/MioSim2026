# Team code and MoSim behavior

Source: [Ninjas4744-Organization/Robot-Code-2026, commit 2bd34881f2c39f86a74cc7d3ea18ba6e00678493](https://github.com/Ninjas4744-Organization/Robot-Code-2026/tree/2bd34881f2c39f86a74cc7d3ea18ba6e00678493). The relevant Java files are retained in `TeamCode/`. This is a translation of command/state behavior into MoSim's robot framework, not a WPILib runtime.

| Source | Behavior carried into Turbo |
| --- | --- |
| `RobotContainer.java`, `Triggers.java` | Intake starts running, rail opens after 0.5 s; shoot is latched; a second shoot press starts hard pumping; cancel returns to collection and briefly backs the indexer out after firing. |
| `IntakeRail.java`, `PositionsConstants.java` | Closed/open goals 0/11 motor rotations; three hard cycles at ±0.25 between 11 and 8, then soft cycles at ±0.15 between 10 and 9; 0.7 s segment timeouts. |
| `Intake.java` | Normal intake command 90 motor RPS; reverse command −20 motor RPS. |
| `ShootMachine.java` | Prepare waits for heading, starts shooter and accelerator, then feeds after readiness; soft pumping starts 0.8 s after the firing state; jam recovery backs the indexer at −20 RPS for 0.5 s. |
| `Shooter.java`, `PositionsConstants.java` | Hub lookup table is preserved at the rotor-command boundary, including the 95 RPS cap. Delivery ramps from 58 to 55 RPS over 3 s; its timer begins when automatic velocity starts and restarts on entering DELIVERY. |
| `ShootCalculator.java` | Five iterations of target minus field velocity times interpolated flight time; distance comes from the robot center. |
| `SwerveSubsystem.java`, `SubsystemConstants.java` | 4.7 m/s real-robot limit, 8.5 rad/s rotation limit, 0.04 deadband, 0.75 joystick exponent, field-centric drive, aim gain 5.5, and 30% translation during hub aiming. Drive gains are converted from m/s to rotor RPS. |

Hub heading tolerance follows `12 − 1.9 × distance` degrees, with a 0.2 degree numerical floor and the source minimum distance of 2 m. Delivery uses 3 degrees. Once firing begins, it stays latched as in the source's active transitions; commented-out transitions are not treated as active behavior. Manual Q shooting is a MoSim testing/driver override.

## Physical adapters

Motor rotations do not specify a rail length without the transmission geometry. The 0–11 rotation range is mapped to the existing CAD-based 0.240 m intake stroke. The hopper follows that measured extension over its 0.22946 m stroke. Force, speed, acceleration, and physical end stops remain finite. The source's `Box.java` is retained for reference, but `RobotContainer.java` does not construct a Box subsystem; its unused state machine is not presented as active robot behavior.

The source RPS commands are exposed separately from the simulated contact RPM. A wheel conversion and range correction curve calibrate MoSim's official 0.65 kg fuel and release hood. The lookup table itself is unchanged. A parameterized local traction adapter represents symmetric shooter tires and indexer grip: wheel RPM determines surface speed, fuel receives bounded forces while inside the mechanism, and the chassis receives the opposite reaction. Forces stop at release. No fuel transform or velocity is assigned, and no correction follows a ball in flight. This avoids speed spikes from competing point-contact impulses and balls spinning in place on a single inclined contact. The storage conveyor acts on captured fuel near the floor only while feeding. Incoming fuel receives separate transfer traction until fully above the tray, keeping storage contact cores out of the entry path. The funnel joins the narrow shooter guides at one continuous edge. These are contact approximations, not additional visible motors or alterations of the CAD.

The shooter waits for the modeled wheels to settle within 2% before initial feeding. That physical adapter is stricter than the real rotor controller's tolerance. Fuel remains an independent physical body: no storage parenting, launch teleportation, velocity assignment, homing projectile, or correction in flight is used.

MoSim does not supply TalonFX current measurements. The source's 82 A accelerator jam trigger is represented by occupied, stalled fuel flow, followed by the source's 0.5 s indexer reverse and resume sequence. The 75 A intake current trigger, real limit switches, encoder homing, vision measurements, and hardware faults are not faithfully measurable here. Physical rail position/stops replace the encoder/limit-switch inputs. The source's current-based intake save-outtake is not claimed as implemented hardware behavior.

Delivery coordinates are mirrored from alliance-relative field coordinates into MoSim's centered field. The hub target comes from the actual MoSim hub node. MoSim's action rebinding remains available; the real PS5 button layout is not copied over the game's standard bindings. The normal simulation-speed release checks are authoritative; faster calibration runs are retained only as development evidence.

## Net

The installed 4414 robot was inspected in the game as a deployment reference. Its protected robot files were not decoded or copied. Turbo uses the public framework's `NetSolver` and `NetParentFollower`: rear lacing follows the fixed frame, front lacing follows the sliding hopper, and side lacing follows the telescoping span. Free nodes fold and collide with fuel. The roof transform keeps unit scale, and the former flat roof collision proxy is disabled. The original Blender model is preserved.

## Verification scope

Release reports record actual field-fuel collection and retention, physical rail bounds, empty and loaded carpet driving, cloth span, completed pump cycles, and actual hub-scoring events at 2.25, 3, 4, 4.5, and 5 m. Distance is measured from the robot center; each shooting case starts 30 degrees away from the required heading and uses the normal preload spawn flow. Firing positions are on the clear alliance side of the hub. Earlier neutral-side fixtures hit the hub's physical back panel and were excluded from release calibration. A separate obstruction fixture verifies the stalled-flow recovery command, its half-second reverse, and resumed scoring. The release tests use the packaged asset bundle and verify its DLL hash.

The measured results cover those fixtures. They do not establish real-world shot accuracy, unlimited hopper capacity, every collision or field obstacle, or an exact match to hardware timing under all loads. WPILib, autonomous path execution, vision estimation, and climbing are outside this mod's implementation.
