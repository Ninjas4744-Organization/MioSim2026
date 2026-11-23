# Turbo 1.0.2 Steam patch

**Published and remotely verified as 1.0.2** on the existing [Turbo by 4744 Workshop item](https://steamcommunity.com/sharedfiles/filedetails/?id=3843685878), app 4398690.

This Windows patch extends the existing bounded bumper-foam response to ramp and carpet contacts. The owner tested the private trial and reported “Better in both cases” for bump recovery and a corner ball pile. Steering, motor tuning, tire grip and net physics are unchanged.

## Validation

The [patch certificate](../Reports/patch-release-1.0.2.json) records fresh checks against the final 1.0.2 files: 58 control checks, six captured-pose escape directions, two loaded corner shooting checks and seven mechanism/shooting cases. The trial's 38 module-contact cases, six fully unsupported controls and eight authoring trench cases are carried forward through strict runtime and asset equivalence proofs.

The [runtime proof](../Reports/Release-1.0.2/runtime-equivalence.json) compares all 338 method bodies to the owner-approved trial, allowing only the diagnostic release-version literal and identified compiler-generated names. The [asset proof](../Reports/Release-1.0.2/asset-equivalence.json) permits only the pack-version field and native PE build metadata; scene/physics objects and native net executable sections match. This is a targeted patch qualification, not a claim that the entire original fourteen-group release matrix was rerun. [Original investigation and limits](../Reports/Drivability-1.0.1/README.md).

## Preserved platforms and package

The source for Linux/macOS is the immutable copy of the published 1.0.1 content at `Artifacts/Drivability-20261123/published-1.0.1`, verified against the successful 1.0.1 upload receipt. All six non-manifest Linux/macOS files remain byte-identical. The SDK requires matching three-platform manifest versions, so only their JSON version labels advance to 1.0.2; their binaries, assets, behavior and artwork remain unchanged.

The [staging receipt](../Reports/steam-1.0.2.json) records all twelve payload hashes and the prior platform hashes. The upload archive is `dist/Ninjas4744-Turbo2026-1.0.2-Steam.zip`. The Windows install archive is `dist/Ninjas4744-Turbo2026-1.0.2-Windows.zip`.

## Submission

```powershell
python Tools/prepare_steam.py --project Artifacts/PumpingProject-20261121 --steam-item Artifacts/Drivability-20261123/published-1.0.1
.\Tools\Invoke-Unity.ps1 -MoSimProject Artifacts/PumpingProject-20261121 -UnityEditor ../davinci-creator/projects/mosim-4744-2026/tools/Unity2023/Editor/Unity.exe -Task Steam
.\Tools\Invoke-Unity.ps1 -MoSimProject Artifacts/PumpingProject-20261121 -UnityEditor ../davinci-creator/projects/mosim-4744-2026/tools/Unity2023/Editor/Unity.exe -Task SteamUpload
```

The last command publishes. It rechecks the certified twelve-file inventory, verifies the previous remote version, and submits only content, version and change note to item 3843685878. Title, description, ordinary tags, visibility and preview remain untouched. The final receipt is preserved at `Reports/steam-upload-1.0.2.json`. After publication, restart MoSim once Steam downloads the patch.
