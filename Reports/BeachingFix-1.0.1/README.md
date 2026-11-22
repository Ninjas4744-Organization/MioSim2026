# Rigid tire contact investigation

Steam remains held. This Windows candidate corrects wheel support queries; native recovery and release checks must be evaluated against its own binary hashes.

## Native evidence

The owner reproduced the problem in installed MoSim. The [creeping capture](native-before/creeping.json) and [settled capture](native-before/settled.json) contain 250 frames each. They use diagnostic DLL `1809db2139f80e9857f759c9e2780c49cc7bdba2908d044476e809afc9c1e463`, module `4824a64d-dda6-4f2b-8471-128394fdbb47`, with the previous wheel physics.

In the settled capture, FL has physical carpet collisions and its solid tire lower bound is 0.24 mm above the carpet datum. Its drivetrain load is about 17 N while the other three patches have zero load. In the earlier creeping capture, BL intermittently receives a floor patch despite its solid tire lower bound being about 21 mm above carpet. These are inconsistencies between rigid tire geometry and the secondary moving WheelCollider probe. They do not establish that every trapped-ball case has the same cause.

The underside contact is just inboard of a corner, around local X=-0.27 m, Z=0.16 m. The recordings include bellypan, tire, module and nearby fuel contacts. They support testing all corners rather than applying a left-side-specific fix. In these old captures, `tireCenter` is the wheel transform pivot; use `tireMinimumY` and physical contact callbacks for solid tire clearance. Subsequent captures record the actual mesh bounds center and the other collider's position and rotation.

## Candidate change

Turbo has rigid module tires represented by solid convex mesh colliders. The WheelCollider query is now centered on that physical tire, with zero suspension travel/spring and a 2 mm tread query tolerance. The stock controller retains motor torque, gearing, speed limits, friction, slip, steering and per-wheel force calculation. Fuel remains excluded from powered tread and suspension queries, and still collides with tires, housings and bellypan. No recovery impulse, pose reset, fuel deletion or airborne propulsion is added.

The passive bellypan and four module housings also use a smooth contact material matching the existing feed-plastic profile: static/dynamic friction 0.03, Minimum combine, zero bounce. Their previous default collider material combined with the game's compressed-fuel friction multiplier could keep balls traveling beneath the frame. Tire and bumper materials are unchanged. Solid shapes, layer exclusions and collision blocking remain intact.

These changes alter the wheel support and passive underside friction models. The net, hopper, fuel compression law, shooter and mechanism geometry are unchanged by this correction.

## Regression scope

Earlier [single-ball](inboard-displacement-only.json) and [paired-ball](paired-displacement-only.json) checks only required total displacement greater than 1 m. Some traces showed substantial sideways travel while still tilted. Those passes are historical investigation evidence, not recovery certification.

The stricter inboard fixture commands a fixed world direction for up to eight seconds. A corner case must progress more than 2 m in that direction, end less than 1 m sideways, release both balls from the underside, and settle below two degrees of tilt. It covers each corner with intake stowed/deployed, clear carpet, and a stand lifting all tires clear. The stand must remain stationary. Successful cases may finish after two seconds once every recovery condition holds.

[Previous-physics single-ball recovery](inboard-recovery-before.json) passes these longer checks. It does not reproduce the sustained native multi-contact failure. A before/after performance improvement must not be inferred by comparing unlike placements or different test durations.

The [rigid-wheel-only reverse matrix](rigid-only-reverse.json) preserves a genuine failed case: FL, deployed, retained both underside balls and remained tilted 5.57 degrees after eight seconds. It traveled 9.95 m but did not recover. The [material prototype](underside-slide-prototype.json) reruns that exact placement with the existing feed-plastic material; it clears. That report explicitly marks its test-only material override. Final candidate matrices must use no such override.

The ordinary centered-fuel regression also distinguishes a physically beached robot from a missed query: a stationary center-ball case is acceptable only with zero supported patches throughout and measured clearance above 4 mm at every tire. Corner recovery cases keep their separate escape requirement. This implements the owner's requirement that a robot with all four tires clear may be beached.

## Recorder correction

The native player's Unity JSON serializer omitted the list of mod-defined frame records, despite editor captures passing. The recorder now writes its public data explicitly. F9 saves a history immediately, including creeping/turning and disabled cases, without requiring sustained drive input. A local request file supports the same snapshot without UI input. Recording remains opt-in and passive. No data is uploaded.

The final candidate passes [38 traction cases and 58 control checks](verification.json). Its [native full-hopper functional check](native-full/summary.json) retained the 59 fully stored cohort without recorded escapes and emptied the captured load. The owner tested the installed build and confirmed: “It recovers now.” Saved settings were restored and test requests removed before manual play reopened.

The final matrices use the final DLL and no test-only material override. The first reverse run had insufficient raised carpet and fell off its edge; that invalid run is excluded. The corrected reverse matrix and centered-fuel checks include measured physical clearance for every fully airborne control. Earlier release certificates remain tied to their original binaries. Complete Steam release recertification remains outstanding; Linux/macOS and publication are unchanged.

## Reproducing the focused checks

Use the pinned Unity project/editor described in the repository README, build the Windows mod, then run:

```powershell
.\Tools\Invoke-Unity.ps1 -MoSimProject $turboProject -UnityEditor $turboUnity -Task Build
.\Tools\Invoke-Unity.ps1 -MoSimProject $turboProject -UnityEditor $turboUnity -Task Traction -Visible -DiagnosticArguments '-turboInboardPair'
.\Tools\Invoke-Unity.ps1 -MoSimProject $turboProject -UnityEditor $turboUnity -Task Traction -Visible -DiagnosticArguments @('-turboInboardPair','-turboReverse')
.\Tools\Invoke-Unity.ps1 -MoSimProject $turboProject -UnityEditor $turboUnity -Task Traction -Visible
.\Tools\Invoke-Unity.ps1 -MoSimProject $turboProject -UnityEditor $turboUnity -Task Controls -Visible -DiagnosticArguments '-turboBundle'
```

The native request is preserved in `native-full/request.json`. It uses normal keyboard input, without physics overrides. Local native test setup/restore uses `Tools/release_native_session.py`; the original settings must be restored and the request removed before ordinary play.
