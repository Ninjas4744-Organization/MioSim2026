# Mechanisms and calibration

The native model is metres/Z-up, intake +X, shooter -X. Unity metres are `(-nativeY, nativeZ, nativeX - 0.0223825)`. Exported axis markers verify scale and handedness. The bumper envelope is 0.8884 m wide by 0.79548 m long.

## Bounded slides

Each slide's configurable joint connects directly to the chassis. X/Y translation and all angular motion are locked. Z translation is **Limited**, with a symmetric limit of half the stroke and the connected anchor located halfway between closed and open. This defines a physical range of 0…0.240 m for the intake and 0…0.22946 m for the hopper.

The runtime reads each moving body's position in the chassis rigidbody frame, rather than relying on a versioned encoder offset. Velocity commands use a braking-aware speed profile, limited to 0.16 m/s, 1.8 m/s², and 350 N. The lower pump speed reduces reversal overshoot within the short 8–11 / 9–10 rotation windows. The same finite force cap and physical stops remain active when disabled; the disable path commands zero velocity without re-homing the joint or restoring infinite force.

The joint's 5 mm contact tolerance exceeds the controller's 1.5 mm settling band, keeping the limit active at the settled open position. This follows Unity's guidance that larger contact tolerances help enforce limits at high speed. The impact regression retains the original strict travel bounds. [Unity Configurable Joint reference](https://docs.unity3d.com/2023.2/Documentation/Manual/class-ConfigurableJoint.html).

Turbo configures solver settings for its three robot bodies directly. The automatically attached JointStackStabilizer is disabled on this robot because it caches child fuel bodies before the spawn handler detaches them. Detached or destroyed fuel must not remain part of the robot's physics-helper cache. Other robots and the game framework are not modified.

The game model's fully deployed pose is the source pose. Closing translates each selected moving assembly toward the chassis. These stroke lengths are simulation estimates based on the CAD envelope, not a conversion of the team's motor-rotation limits.

The shooting/pumping footage supplied by the user shows a compact front roller assembly directly beneath the hopper. The earlier 13-degree downward adjustment in source asset 1.3.1 was inferred from photos; undoing that adjustment restores the original CAD alignment. The current game model reconstructs 59 original CAD parts and removes the invented supports, shaped clearance bump and arbitrary mesh cuts from earlier visual attempts. `Tools/Blender/restore_cad_kicker.py` records this reconstruction; `Reports/hopper-kicker-model.json` lists every part and its assembly.

The original structural plates, motor housings and upper pulleys stay rigidly attached to the hopper. Original lower bearing plates, bearings and shaft (parts 51, 54, 56, 64, 68, 78, 94 and 95) form the lower carrier; the original roller (part 82) remains separately driven. The deployed assembly retains its original CAD geometry, including its actual holes, fasteners and motor mounts.

The lower carrier stays deployed during normal hard and soft pump strokes, matching the footage. Full stow still raises it before either slide retracts. Initial opening retains the user's stated order: box out, then lower kicker down. A 60-degree visual retraction about native `(0.57204, 0, 0.24405)` m clears the bumper; this is clearance calibration, since the footage does not show a complete opening or closing. Fuel contacts and rail limits retain their calibration. The integration check verifies that the kicker remains down while pumping, and the mesh audit covers both the complete stow sequence and the pump window.

`Reports/Turbo4744-kicker-reference-comparison.png` compares the model against a frame from the supplied footage. `Reports/Turbo4744-kicker-pumping.gif` shows the matching compact assembly moving with the hopper during a short pump stroke.

## Fuel and shooting

Eight instances of the official fuel prefab are stored in `RebuiltRobotBase.preloads`. MoSim's `RebuiltSpawnInitializer` detaches them into the game-piece world and removes the matching number of field pieces. Empty markers are not valid preloads. Fuel layers and colliders are preserved.

The feed path uses MoSim InteractionRoller contacts, bounded local traction for transfer/indexer/shooter, and simple collision surfaces. The retention contact opens after alignment and flywheel readiness. A short physical hood and continuous funnel/side guides constrain the release angle; wheel calibration reduces range error. The traction model applies forces and chassis reaction only within the mechanism, without setting fuel velocity or correcting flight. Fuel rigidbodies are excluded from robot self-collision ignoring. The actual CAD intake roller mesh rotates with its contact roller.

The storage contact bed is low and level so two layers of fuel fit below the closed roof. A short, narrower rising section lifts fuel into the indexer. Keeping the storage volume separate from that rising feed section avoids squeezing a second layer against the roof during retraction.

The extended hopper has a complete moving storage floor overlapping the fixed bed. A cylindrical entry contact completes the intake belt's curved nose; retracting-tray contact and inclined indexer contacts carry fuel toward the accelerator. Added storage contacts affect fuel only, avoiding suspension interference. The hopper follows the actual intake extension and velocity, so hard and soft pumping move the stored load as well as the external intake.

The intake uses an explicit one-way contact approximation. Incoming fuel follows a local curved contact path from the ground pickup into the tray. During entry, only this robot's fixed and moving floors, inlet keeper and front-panel contact pairs can open. The floors become solid as soon as the sphere clears them; the front gate closes after the sphere clears the inlet plane and settles vertically at the handoff. Reverse opens the front gate while keeping the floors under stored fuel solid. Pairs are also restored when fuel exits the area or the keeper is disabled. No global game-piece rules change. Fuel is never parented, frozen, repositioned, or assigned a storage velocity.

Floor clearance is latched until the whole sphere exits the front or side of the hopper. A bounce or compression from the load cannot reopen the floor. Powered intake contact follows a narrow 60 mm roller/throat path, rising over the upper kicker before handing fuel into storage. A continuous backing surface supports this belt path, preventing incoming ground fuel from rolling underneath the storage floor when the hopper fills. Normal force is bounded to 80 N and the shared mechanism budget to 600 N, with a 3.6 m/s surface-speed ceiling. Retention classification does not cut power before this handoff finishes. The stock fuel mass, diameter and collision material remain unchanged.

Turbo excludes fuel layers from the stock drivetrain's wheel-support queries and the rigid wheel-core contacts. The wheels continue to sample the field normally; bumpers, hopper surfaces and intake contacts still interact with fuel. This prevents separate modules from treating rolling balls as independent tread surfaces.

The net has 35% geometric slack, bounded stretch/shear compliance and 320 particles representing a 240 g textile. Its collision strands have a 12 mm radius. Thin frame-edge contacts complete the fixed lacing boundaries that the native cloth solver skips when both edge nodes are pinned. Triangular wedges fill the spaces outside the lower V guides, directing corner fuel into the feed basin without covering it.

Flow monitoring accumulates inward travel in 6 mm steps or upward travel in 25 mm steps. Slow but continuing tray motion counts as progress; a stationary obstruction still triggers the bounded reverse recovery. This avoids repeatedly reversing a lightly loaded hopper just because its fuel moves below the previous 4 cm/s threshold.

The backing surface ends at the kicker release, leaving the storage tray clear. It engages after full initial deployment, stays available during pumping and clears before full stow. Collection uses the 600 N / 3.6 m/s pickup profile; an active shot uses a gentler 140 N / 1.2 m/s transfer profile to return stored fuel from the entry ramp to the indexer. Retained fuel resting on the floor remains eligible for feeding, rather than requiring an air gap under the sphere.

This is a collision proxy for intake retention. The intake command stays latched at the team's 90 rotor RPS during collection. Disabled mode stops all rollers while retaining stored fuel through the solid contacts.

Drive and shooter tables come from the team's 2026 code. Motor-to-wheel conversion, contact geometry, friction, launch channel, and mass distribution are simulation calibration. The roof uses MoSim's deforming cloth solver with fixed and sliding frame attachments, rather than mesh scaling or a hard roof. [The code mapping](RobotCodePort.md) records the pinned source, active state transitions, and hardware limitations.

The hopper roof material is derived from MoSim's `Assets/Materials/Net/Net.mat` and uses its supplied `ActualTileableNet.png`. The texture already contains multiple openings, so the roof tiles it twice in each direction. The earlier 18 by 16 tiling made the holes too small to remain visible at normal camera distances. Both sides render with alpha-clipped openings, depth writes and shadows; the texture supplies the strand color without an additional dark tint.

Incoming fuel receives bounded transfer traction until the whole sphere clears the storage floor and inlet. The contact follows the intake rail and applies its reaction to that moving body. Its edge falloff spans the tray width, keeping support beneath a ball against either side guide. Captured fuel is released from entry assistance; disabled and stowed states stop the contact. Fuel remains an independent rigidbody.

All helper intake, floor and accelerator roller cores exclude fuel, and their native traction is disabled. Their custom contacts own fuel forces without duplicate wheel-core impulses. The indexer acts only on captured fuel and limits contact speed to 1.2 m/s before the existing shooter release contact takes over. The pickup does not brake incoming fuel that is already moving inward faster than the belt.

A continuous low-friction ramp supports the inclined indexer path independently of traction commands. The indexer shares a 320 N tangential traction budget across its contacts; it does not press fuel down onto the ramp with a centering spring. Fuel remains supported while the indexer stops, reverses or recovers a jam. The sloped lower return lip lifts the front row into a second storage layer during loaded retraction, rather than wedging a single layer against a vertical wall.

The approach regression offers nine ground balls at 0.5, 2 and 4 m/s. The faster cases include balls at both intake edges and multiple robot headings. It measures retained pickups, travel and chassis tilt through braking, disable and loaded stow. Use `-Task Approach -Authoring` during contact tuning or `-Task Approach` to check the built bundle; `-Baseline` writes a separate before report.

## Verification

The integration check uses the real preload spawn handler and normal keyboard action bindings. It exercises retained preloads while driving, deployment, pickup, the same ball feeding through the shooter, disabled shutdown, and stowing.

The travel regression loads the Windows asset bundle, rather than the authoring prefab. It samples travel every physics step at 0°, 90°, 180°, and -90° headings. Each case includes a held intake command, driving/turning, disable/enable, and stow. An equal-and-opposite 40 N·s actuator impulse deliberately loads the intake end stop. This fault-injection test checks that physical bounds remain effective even when a controller alone could not stop the mechanism immediately.

The retention regression starts empty and collects a group of nine official field fuel objects. It follows each captured ball through a held intake command, release, forward/reverse driving, 90°/s turns, curved motion, disable, and loaded stow at two initial headings. It requires at least seven pickups, retention of every collected ball, no escapes, stable robot motion, and successful stow. Its report records the tested asset bundle hash. The earlier 0.2.0 reproduction captured nine balls in its first case and subsequently lost all nine. The revised test uses clear neutral-zone carpet with unrelated fuel disabled, keeping field structures and loose background fuel out of the collection route.

The systems benchmark measures driving with an empty hopper and a seeded 20-piece load, checking how many remain before and after maneuvering. It also starts eight real preloads at each shooting distance, requests a three-cycle hard pump, and counts actual scoring events in the hub. Drive commands are applied at every physics step. Cloth scale, deployment span, finite particles, and rail motion are recorded alongside scoring and speed measurements. Reports fingerprint the packaged bundle and runtime DLL.

Version 0.5 limits total intake force across a dense pile, rather than applying a full motor budget to each ball. Hopper transfer follows actual tray retraction. The visual audit samples the transformed Intake_Geometry mesh center, independently of the rail encoder, and saves extended/retracted pump frames. F8 toggles a local status panel and per-second diagnostic log for native-game verification.

The funnel guide collision proxies extend to the hopper rim. The earlier lower guides let an upper-layer ball roll into the open rear corner beside the shooter during a full-load feed. This hidden guide geometry is a simulation proxy; the approved CAD meshes remain unchanged.
