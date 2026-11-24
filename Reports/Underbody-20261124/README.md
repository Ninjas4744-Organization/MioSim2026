# Grounded underbody clearance private trial

The owner reports that the published 1.0.2 contact changes still leave the robot unusable on loose fuel. The requested behavior is now an explicit gameplay rule: with at least one physical tire touching a field surface, move loose low underbody fuel to nearby free floor. Keep all fuel in play. With all four tires clear, do not clear fuel or move the robot.

`Turbo4744UnderbodyClearance` runs after the ordinary drivetrain. It reads actual tire/field collision contacts and validates the tire distance from the actual field surface, including sleeping contacts. It scans only the low chassis footprint (local y -0.30 to 0.115 m), below stored hopper/feed fuel. It searches nearby floor for a nonoverlapping destination on the same side of walls, checks other robots, and moves at most two balls per check with a small outward velocity. Failed searches retain the ball and retry within a bounded work budget. No steering, motor, tire grip, bumper foam, net, or shooter settings are changed.

The ground gate uses a 2.5 mm geometric tolerance, below the engine contact skin. All four supports lowered 6 mm and disabled support are explicit negative cases. Placement uses the stock 75 mm outer fuel sphere, rather than its smaller inner collision shell.

This is a private Windows trial. Its manifest retains 1.0.2; the label and hashes distinguish it from the published build. Steam and the published release archives are not changed. Linux and macOS are not rebuilt.

## Test scope

- Frozen chassis contract fixtures isolate exactly zero through four grounded tires, single balls, piles, blocked exits, and lost/cached support. Their purpose is to verify the rule, not to claim free-body driving performance.
- Free-body module contact fixtures test actual driving and recovery in both intake states; unsupported controls must remain unsupported.
- Packaged control, collection/retention, and shooting checks protect the existing robot behavior.
- Installed-player feedback remains a separate check. Editor results cannot guarantee every native pile or field pose.

The initial contact-limited run and the invalid chassis-lift fixture (the requested raised pose was not retained) are retained as failed evidence. Final results and artifact fingerprints are recorded separately after verification.

## Reproduction

Use the pinned Unity 2023.2.22f1 editor with `Tools/Invoke-Unity.ps1`, project `Artifacts/PumpingProject-20261121`. Build first, then run the following tasks with `-Visible`:

```text
Underbody
Traction
Traction -DiagnosticArguments @('-turboInboardPair','-turboReverse')
Controls
Systems
Capacity
Mobility -DiagnosticArguments @('-turboMobilityCornerOnly','-turboMobilityAsBuiltOnly')
```

Preserve each report from the project's `BuildArtifacts` before another run can replace it. The corner-only report is `mobility-comparison-candidate.json`. `build.json` binds the exact runtime sources, fixtures and six platform files. `Tools/package_underbody_trial.py --check-only` checks the evidence and file fingerprints; omitting `--check-only` creates the distinct private archive after verification.

## Verified results

The final build passes all 13 contact-contract cases, 26 free-body driving cases, 58 control checks, seven mechanism/shooting cases (40 scored balls), two collection/retention/release cases (30 balls per orientation, zero escapes), and two corner-pile shooting/escape cases. Four fully unsupported free-body controls have zero assistance. There are 32 recorded clearance events across the free-body driving cases; a ball can be cleared again if the moving robot encounters it again.

The local Windows trial is installed with a backup, and the native player loads module `98b00b51-cc15-44f6-b0c8-590faa21b183` / DLL SHA-256 `9f14b53acebba2fdc54dbd38221d58c95b615bba0db13b8827189058eb755ffc`. The native startup capture contains the new clearance telemetry and four grounded tires. This verifies loading and ground-contact observation, not a native pile-recovery trial. The owner then tested driving over balls and through a pile in this running native trial and reported **“Recovers reliably”**. This is owner-reported recovery in that test, not a guarantee for every possible fully obstructed field pose. Both user settings files are byte-identical after startup.

The batch runner initially looked for the wrong corner report filename after the Mobility task had passed. The completed report was preserved from `mobility-comparison-candidate.json` and checked independently, including both successful shooting/escape cases and exact build fingerprints.

The published Steam ZIP still matches every one of the twelve payload hashes in the 1.0.2 upload receipt. The new archive is `dist/Ninjas4744-Turbo2026-1.0.2-grounded-clearance-trial-Windows.zip`. No new Steam upload is performed.
