# Bump landing investigation

This is archived diagnostic evidence. The subsequent native captures and contact correction are recorded in [the current investigation](../BeachingFix-1.0.1/README.md).

The user's clarification was that the robot often sticks after landing on loose fuel beyond a bump. This differs from the earlier settled, empty-robot probes.

**One lasting beach was reproduced.** A rear-first, full-speed landing with 61 stored fuel and one loose ball at the centre left all four drivetrain patches empty for about 5.5 seconds. Drive input remained commanded. The ball contacted the bellypan; the robot retained all 61 stored balls. See [the raw failed escape case](full-stowed-rear-fast.json), case three. This failure is preserved, not marked fixed or converted into a passing escape check.

The drivetrain does not require four grounded tires. It already applies force independently at each wheel with support. In this reproduced case, it reported **zero supported wheels**, rather than disabling three grounded wheels because one was lifted. Physical tire clearance in that original run was not logged, so a missed contact query cannot be ruled out solely from those traces.

The Windows diagnostic build adds an opt-in passive recorder to distinguish those possibilities in the installed game. It does not alter inputs, forces, collider geometry, fuel compression, suspension, traction, motor settings or hopper behavior. The full landing issue is **not declared resolved**, and Steam remains held.

## Landing coverage

These fixtures copy the official BlueBump geometry onto raised carpet, with a crossing lane clear of the central hub gap and end supports. Other field fuel is disabled for isolation. Payload is collected through the ordinary intake from stock dynamic fuel on the carpet. Keyboard commands deploy/stow; stock drivetrain setpoints drive over the bump. No bodies are repositioned, constrained or given a recovery force during a trial. This is editor physics evidence, not native release certification.

| Profile | Cases | Stored before driving | Cleared landing |
| --- | ---: | --- | ---: |
| [Empty, intake deployed, three lanes and three ball distances](empty-deployed.json) | 10 | 0 | 10 |
| [Full, stow requested, front first](full60-stowed-front.json) | 4 | 60 | 4 |
| [Full, stow requested, rear first at 1.41 m/s](full-stowed-rear-slow.json) | 4 | 59–61 | 4 |
| [Full, stow requested, rear first at 4.7 m/s](full-stowed-rear-fast.json) | 4 | 60–61 | 3 |
| [Centre-ball repeats with the recorder prototype](current-full-center.json) | 3 | 58–60 | 3 |
| [Additional collection, centre-ball repeats](current-full-center-heavy.json) | 3 | 63 | 3 |

The raw reports fingerprint the exact DLLs used. The first four profiles use the previous pump-visibility runtime; the last two use the initial recorder prototype. Those profiles demonstrate the investigation and variability; they are not all certifications for the final diagnostic DLL. No stored cargo was lost in these runs. Some placements are pushed or collected rather than contacting the underside, which is why contact details and underside counts are retained.

## Local recording

The recorder is off unless the empty marker file `Turbo4744-capture-landings.enabled` exists in `%USERPROFILE%/AppData/LocalLow/CascadeStudios/MoSim/` before the robot spawns. It saves a short history and half a second after a sustained attempted-drive stall, at most twice per robot spawn, under `Turbo4744-LandingCaptures` in that same folder. Nothing is uploaded.

Frames include translation and turning input, commanded velocity, body motion/tilt, each wheel's cached contacts/normal load/tread speed, solid tire lower bounds, recent tire-to-field collision callbacks and underside collision points. Tire minimum Y is a world coordinate; compare it with the local carpet or ramp height. Suspension hits alone are insufficient because this robot's solid tire meshes can carry load.

To capture another occurrence, play normally and keep the attempted drive input held for about two seconds when stuck. Remove the marker and restart MoSim to disable capture. The file path is also printed in `Player.log`.

The final recorder also tracks turning attempts and keeps tire contact timestamps updated even when its bounded detail buffer is full. [Current validation](verification.json) records the final binary fingerprints and focused checks. Serialized physics assets are compared in [asset-comparison.json](asset-comparison.json). Previous pumping/native certificates retain their original fingerprints; Linux/macOS and the existing release holds remain unchanged.
