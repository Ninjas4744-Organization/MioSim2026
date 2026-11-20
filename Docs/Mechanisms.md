# Mechanisms and calibration

The native model is metres/Z-up, intake +X, shooter -X. Unity metres are `(-nativeY, nativeZ, nativeX - 0.0223825)`. Exported axis markers verify scale and handedness. The bumper envelope is 0.8884 m wide by 0.79548 m long.

## Bounded slides

Each slide's configurable joint connects directly to the chassis. X/Y translation and all angular motion are locked. Z translation is **Limited**, with a symmetric limit of half the stroke and the connected anchor located halfway between closed and open. This defines a physical range of 0…0.240 m for the intake and 0…0.22946 m for the hopper.

The runtime reads each moving body's position in the chassis rigidbody frame, rather than relying on a versioned encoder offset. Velocity commands use a braking-aware speed profile, limited to 0.16 m/s, 1.8 m/s², and 350 N. The lower pump speed reduces reversal overshoot within the short 8–11 / 9–10 rotation windows. The same finite force cap and physical stops remain active when disabled; the disable path commands zero velocity without re-homing the joint or restoring infinite force.

The joint's 5 mm contact tolerance exceeds the controller's 1.5 mm settling band, keeping the limit active at the settled open position. This follows Unity's guidance that larger contact tolerances help enforce limits at high speed. The impact regression retains the original strict travel bounds. [Unity Configurable Joint reference](https://docs.unity3d.com/2023.2/Documentation/Manual/class-ConfigurableJoint.html).

Turbo configures solver settings for its three robot bodies directly. The automatically attached JointStackStabilizer is disabled on this robot because it caches child fuel bodies before the spawn handler detaches them. Detached or destroyed fuel must not remain part of the robot's physics-helper cache. Other robots and the game framework are not modified.

The game model's fully deployed pose is the source pose. Closing translates each selected moving assembly toward the chassis. These stroke lengths are simulation estimates based on the CAD envelope, not a conversion of the team's motor-rotation limits.

## Fuel and shooting

Eight instances of the official fuel prefab are stored in `RebuiltRobotBase.preloads`. MoSim's `RebuiltSpawnInitializer` detaches them into the game-piece world and removes the matching number of field pieces. Empty markers are not valid preloads. Fuel layers and colliders are preserved.

The feed path uses MoSim InteractionRoller contacts, bounded local traction for transfer/indexer/shooter, and simple collision surfaces. The retention contact opens after alignment and flywheel readiness. A short physical hood and continuous funnel/side guides constrain the release angle; wheel calibration reduces range error. The traction model applies forces and chassis reaction only within the mechanism, without setting fuel velocity or correcting flight. Fuel rigidbodies are excluded from robot self-collision ignoring. The actual CAD intake roller mesh rotates with its contact roller.

The storage contact bed is low and level so two layers of fuel fit below the closed roof. A short, narrower rising section lifts fuel into the indexer. Keeping the storage volume separate from that rising feed section avoids squeezing a second layer against the roof during retraction.

The extended hopper has a complete moving storage floor overlapping the fixed bed. A cylindrical entry contact completes the intake belt's curved nose; retracting-tray contact and inclined indexer contacts carry fuel toward the accelerator. Added storage contacts affect fuel only, avoiding suspension interference. The hopper follows the actual intake extension and velocity, so hard and soft pumping move the stored load as well as the external intake.

The intake uses an explicit one-way contact approximation. During collection, an incoming ball temporarily ignores only this robot's moving storage floor and inlet keeper. Once the whole sphere is above the floor and behind the inlet plane, those contacts become solid for that ball. Collection stays open to new fuel while already stored fuel has a continuous floor and front barrier. Reverse releases the contact pairs; pairs are also restored when fuel exits the area or the keeper is disabled. No global game-piece rules change. Fuel is never parented, frozen, repositioned, or assigned a storage velocity.

This is a collision proxy for intake retention. The intake command stays latched at the team's 90 rotor RPS during collection. Disabled mode stops all rollers while retaining stored fuel through the solid contacts.

Drive and shooter tables come from the team's 2026 code. Motor-to-wheel conversion, contact geometry, friction, launch channel, and mass distribution are simulation calibration. The roof uses MoSim's deforming cloth solver with fixed and sliding frame attachments, rather than mesh scaling or a hard roof. [The code mapping](RobotCodePort.md) records the pinned source, active state transitions, and hardware limitations.

Incoming fuel receives bounded transfer traction until the whole sphere clears the storage floor and inlet. The contact follows the intake rail and applies its reaction to that moving body. Its edge falloff spans the tray width, keeping support beneath a ball against either side guide. Captured fuel is released from entry assistance; disabled and stowed states stop the contact. Fuel remains an independent rigidbody.

## Verification

The integration check uses the real preload spawn handler and normal keyboard action bindings. It exercises retained preloads while driving, deployment, pickup, the same ball feeding through the shooter, disabled shutdown, and stowing.

The travel regression loads the Windows asset bundle, rather than the authoring prefab. It samples travel every physics step at 0°, 90°, 180°, and -90° headings. Each case includes a held intake command, driving/turning, disable/enable, and stow. An equal-and-opposite 40 N·s actuator impulse deliberately loads the intake end stop. This fault-injection test checks that physical bounds remain effective even when a controller alone could not stop the mechanism immediately.

The retention regression starts empty and collects a group of nine official field fuel objects. It follows each captured ball through a held intake command, release, forward/reverse driving, 90°/s turns, curved motion, disable, and loaded stow at two initial headings. It requires at least seven pickups, retention of every collected ball, no escapes, stable robot motion, and successful stow. Its report records the tested asset bundle hash. The earlier 0.2.0 reproduction captured nine balls in its first case and subsequently lost all nine. The revised test uses clear neutral-zone carpet with unrelated fuel disabled, keeping field structures and loose background fuel out of the collection route.

The systems benchmark measures driving with an empty hopper and a seeded 20-piece load, checking how many remain before and after maneuvering. It also starts eight real preloads at each shooting distance, requests a three-cycle hard pump, and counts actual scoring events in the hub. Drive commands are applied at every physics step. Cloth scale, deployment span, finite particles, and rail motion are recorded alongside scoring and speed measurements. Reports fingerprint the packaged bundle and runtime DLL.

Version 0.5 limits total intake force across a dense pile, rather than applying a full motor budget to each ball. Hopper transfer follows actual tray retraction. The visual audit samples the transformed Intake_Geometry mesh center, independently of the rail encoder, and saves extended/retracted pump frames. F8 toggles a local status panel and per-second diagnostic log for native-game verification.

The funnel guide collision proxies extend to the hopper rim. The earlier lower guides let an upper-layer ball roll into the open rear corner beside the shooter during a full-load feed. This hidden guide geometry is a simulation proxy; the approved CAD meshes remain unchanged.
