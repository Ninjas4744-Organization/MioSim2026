# Solid-contact clearance and smooth presentation

The owner reported that the continuous-force roll-out recovered but made the chassis jump, then showed fuel caught in the rear frame pocket. The installed capture puts a ball at local (0.0256, 0.1258, -0.2656), resting on the rear bellypan. Those bellypan and bumper colliders already exist. The force roll-out temporarily ignored their contacts and could restore them while overlapping fuel; the automatic history does not contain the take-off impulse itself, so it cannot prove the exact jump timing. The broad bypass is a concrete unsafe contact transition and can also admit fuel above the bellypan.

This candidate removes that bypass entirely. Every structural hitbox stays enabled and its collision-pair state stays untouched. The physical clearance returns to the previously owner-approved instant safe relocation, gated by actual tire/field contact. The existing collider-free fuel mesh alone eases out with a rolling texture animation. No extra ball or renderer is spawned. This separates the presentation from collision resolution: for at most 0.65 seconds the visible mesh can trail the physical sphere. It is an explicit gameplay/presentation approximation, not a claim of fully physical rolling.

Low rear-pocket centres at z < -0.22 m, y between 0.115 and 0.20 m, and |x| <= 0.30 m are also eligible while a tire remains grounded. This is behind the accelerator/feed envelope, whose rear limit is -0.20 m. The hopper and normal feed path retain their existing rules. The original low underbody volume remains in effect. With no grounded tire, no new physical clearance occurs; an already-started visual exit finishes normally.

The contract checks compare collision-pair states during every physics step, check smooth visible motion separately from the immediate physical relocation, cover the pictured rear pocket, and interrupt the helper/ground support mid-presentation. Free-body recovery checks now measure upward velocity beginning when balls are introduced, including the settling interval the previous matrices did not bound.

Private Windows trial only. The published Steam package, prior trial ZIPs, Linux and macOS stay unchanged.

## Native trial

The installed player loaded module `59fcb563-73e9-4504-97a8-2fe2bb24cdde`. A 250-frame passive startup capture observed four grounded tires and zero recovery collision-bypass pairs or force-assistance steps. All six installed files match the build; both settings files remained byte-identical after startup. The owner then tested the running build and reported: "Recovers smoothly; pocket clears." Startup evidence and owner feedback establish different things and are recorded separately in `native-startup.json` and `local-install.json`.

## Reproduction and scope

Use `Tools/Invoke-Unity.ps1` with the pinned Unity 2023.2.22f1 editor and project `Artifacts/PumpingProject-20261121`. Run `Build`, then these tasks with `-Visible`: `Underbody`; `Traction`; `Traction -DiagnosticArguments @('-turboInboardPair','-turboReverse')`; `Systems -DiagnosticArguments @('-turboRange','3')`; `Capacity`; and `Mobility -DiagnosticArguments @('-turboMobilityCornerOnly','-turboMobilityAsBuiltOnly','-turboMobilityFinishFeed')`.

The 17 contract cases use a frozen chassis to isolate exact tire support counts, contact-pair state, ball preservation and visible motion. The 26 driving cases use a free chassis, including four fully unsupported controls. Upward velocity is measured through both settling and driving with a 0.5 m/s bound for supported cases; this permits small ordinary settling and is not a claim of zero vertical velocity. The targeted systems run covers empty/20-ball driving and eight shots at 3 m. Capacity covers two separate 30-ball collection, retention, driving and unloading cycles. The full 58-control matrix and full release suite were not repeated on this private candidate.

The corner fixture preserves its original seven-second shooting observation before testing escape. An initial run escaped both headings but retained two balls at seven seconds in one heading. That report is retained as `corner-pile-seven-second.json`. The added diagnostic records captured-ball positions at seven seconds and permits up to 20 seconds of residual feeding after driving clear of the pile, requiring zero captured fuel at the end. It does not rewrite the original shooting observation or alter runtime tuning.

Build files and runtime/editor sources are bound by `build.json`; the package verifier also checks published payloads and both prior private archives. Run `python Tools/package_underbody_trial.py --solid --check-only` to validate the evidence, or omit `--check-only` to create the separately named Windows trial ZIP.

## Results

All 17 contract cases pass. Collision-pair states remain unchanged during recovery; all seeded balls remain present, and the largest visible mesh step is 10.57 mm. Both rear-pocket cases obey the grounded-tire gate, and interrupted visual exits finish without further physical clearance.

All 26 free-body driving cases pass. The largest supported upward velocity through settling and driving is 0.485 m/s. Four fully unsupported controls receive no assistance. The two capacity runs collect, retain and release all 60 balls without escapes. The targeted systems run launches and scores all 8 shots, with zero recovery clearances/force steps in all three systems cases.

Both corner headings escape in the repeat, with peak upward speeds below 0.09 m/s and no captured fuel left after the original seven-second shooting interval or after escape. Because the repeat emptied in seven seconds, its optional extra feeding loop was not exercised. This does not establish when the two retained balls in the initial run would have cleared; that observation remains preserved, rather than being relabelled as a complete unload. The owner separately confirmed smooth recovery and clearance of the pictured pocket in the installed native trial.
