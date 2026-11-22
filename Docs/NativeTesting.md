# Native release checks

The real robot's hopper does not fully retract when a full load or another physical obstruction prevents it. The owner confirmed this on 2026-11-22. Version 1.0.1 keeps that physical behavior and the existing 350 N slides, 4200 N s/m damping, fuel compression response, collider geometry and net calibration.

Release acceptance checks two distinct operating conditions:

- A full or slightly overfilled hopper (58–64 balls, with at least one actual load of 60 or more) retains its entire load during eight seconds of blocked stow. Both control variants must then shoot empty and recover to less than 2 mm extension on both slides. `Tools/verify_native_full_hopper.py` checks the ordinary-key timeline and authoritative frame trace.
- A partially loaded hopper that physically permits closure completes three stowed trench crossings, covering both alliances, slow and normal gears, and the default control variant. The actual transit cohort must be 16–32 balls; every member must remain through every crossing sample. The loaded pre-transit settling limit is 10 mm, leaving margin to the unchanged 15 mm crossbar rail-extension limit, and sustained stops below 0.25 seconds. A separate normal-speed bump/delivery cycle retains the same cohort and delivers at least 7/8 within 1.25 m.

A collection target is a braking trigger, not the accepted load count. A larger partial load may also block full closure; the 2 mm requests that stopped with 29 and 23 balls are preserved. No force, contact, geometry, timestep or fuel overrides are eligible. The original field keeps all 504 independent fuel bodies. Native Burst execution, current installed DLL/bundle hashes, zero spills, complete shooting and recorded phase frames below 250 ms are checked.

## Reproduce the checks

Use the adjacent `Reports/native/*-request.json` files as the exact input requests. `Tools/release_native_session.py` backs up the user's settings and local mod before installing the verified Windows build. Its `--session-dir` supports a new, isolated backup for each test session. The `restore` action restores settings and removes the input-replay request while leaving the checked release installed.

Full-load requests use the existing `inputTimeline` capability: collect, settle, hold Z for eight seconds, shoot and pump, then hold Z for four seconds after emptying. `stowForReturn` is false because these are stationary retention/recovery checks, not transit attempts. The verifier reads actual `Closed` states, live load counts and both measured rail extensions; it does not relabel a timeout or set `stowedBeforeReturn` to true.

Trench requests use `stowForReturn: true`, `requireTrenchSuccess: true`, `stowTolerance: 0.010`, `minimumTransitLoad: 16` and `maximumTransitLoad: 32`. The real trench lane is world Z ±3.3758. The bump uses world Z ±1.7, normal gear 2 and delivery key E. All diagnostic overrides must be false or zero.

After each observed cycle, `Tools/capture_native_replay.py` preserves the raw report, exact request, screenshots and installed artifact fingerprints. Run all three native verifiers against the current build before packaging. `Tools/Package.ps1` and `Tools/prepare_steam.py` require matching full-hopper, trench and bump certificates in addition to all fourteen editor fixture groups.

The JSON `frames`/`frameColumns` trace is authoritative: the installed player's serializer omits nested request/sample objects, so requests are preserved separately. No test teleports, seeds, freezes or assigns velocities to fuel or the chassis.

The old forced full-load closure requests and experimental comparisons remain recorded. They are not release evidence. The [historical 0.6.2 records](NativeTesting-0.6.2.md) used the earlier fixed-bin model and do not certify the current sliding bin.

## Selector artwork follow-up

After the native suite, the owner requested removal of the 4744 text from the selector logo. `Tools/verify_selector_art_update.py` compares the tested and rebuilt bundles object by object. It requires an identical runtime DLL and object inventory, resolves every streamed Mesh/Texture2D payload, and permits changes only to the two `selector-logo` Sprite/Texture2D objects. All 2,607 other serialized objects/resources must match. The resulting [proof](../Reports/selector-artwork-equivalence-1.0.1.json) binds the original gameplay evidence to the final artwork bundle. Raw reports and installed-artifact receipts are preserved unchanged; the aggregate certificates explicitly record their original `tested_artifacts` and this equivalence proof. Exact embedded hashes remain distinguished from equivalent gameplay assets.

Reproduce this comparison with UnityPy 1.25.4 installed under `Artifacts/Release101/unitypy`, then pass the original tested bundle with `--before` and the rebuilt project with `--project`. The original bundle is also in the held candidate archive. The control suite and installed selector preview are checked again on the final artwork bundle.
