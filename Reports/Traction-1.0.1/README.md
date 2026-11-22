# Single-ball traction investigation

The reported permanent beaching on one ball was **not reproduced**. No runtime, prefab, physics setting, installed mod file or tester ZIP was changed. Steam remains held.

All 28 empty-hopper fixtures pass on the existing Windows tester (`be5c115`): 14 [forward](forward.json) and 14 [reverse](reverse.json). They use default Turbo; Actual Turbo shares its drivetrain. Each direction covers intake stowed and deployed, clear carpet, one stock dynamic fuel body below each of the four wheels, one below the bellypan center, and a static support stand lifting all tires clear. The robot and ball remain unconstrained during settling and driving, at the original 4.5 ms physics step. Fixture bodies are placed only before the settling phase. Cargo acquisition and loaded beaching are not covered.

The robot moves 1.22–3.29 m in two seconds in single-ball trials. Several trials begin with only two supported tires; others regain carpet contact as the ball rolls or the chassis rocks. All four stand controls maintain zero supported tires and drift less than 0.4 mm. This checks that partial support can still drive, while sustained total loss of carpet support does not receive artificial propulsion. It does not prove every possible ball position can be escaped, or that touching carpet guarantees sufficient normal load in every situation.

Support counts come from the drivetrain's actual floor-contact patches, rather than only `WheelCollider.GetGroundHit`: solid tire meshes also support this robot. The raw frame traces retain the per-wheel patch counts and normal loads. Dynamic fuel stays excluded from powered tread queries, while the solid tires, modules and bellypan still collide with it.

[Runtime comparison](runtime-comparison.json) confirms that normalized player IL matches the installed Steam v26.6.0 for `DriveSwerve`, `UpdateWheelContacts`, `ApplyDriveTorque`, `AddObstacleContacts` and `UpdatePassiveTraction`. The drive applies torque per wheel, and reports grounded when any patch has positive load. There is no requirement for all four wheels to be grounded. This was a read-only assembly inspection; native single-ball driving was not tested.

All six installed Windows mod files match the tested platform files. The DLL and bundle hashes are embedded in both raw reports. The existing tester ZIP still contains those same binaries. Linux/macOS and all earlier release holds are unchanged.

Reproduce with the pinned Unity project:

```powershell
.\Tools\Invoke-Unity.ps1 -MoSimProject $project -UnityEditor $unity -Task Traction -Visible -Baseline
.\Tools\Invoke-Unity.ps1 -MoSimProject $project -UnityEditor $unity -Task Traction -Visible -Baseline -DiagnosticArguments '-turboReverse'
```

The next useful evidence is the stuck robot's ball position, load, intake state and attempted drive direction, ideally in a short clip. A targeted fix requires reproducing that case; the passing fixtures do not clear the user report.
