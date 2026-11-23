# Turbo 1.0.1 Steam Workshop update

Historical release record. See the [1.0.2 patch instructions](Steam-1.0.2.md) for the current update.

**Published on Steam Workshop as 1.0.1.**

This updates the existing public [Turbo by 4744 item](https://steamcommunity.com/sharedfiles/filedetails/?id=3843685878), item **3843685878** for MoSim **4398690**. The Windows release contains the performance, control, artwork, module-contact and slide-feedback corrections. Linux/macOS keep their published DLLs and bundles, including their existing behavior and artwork. Only their manifest version labels become 1.0.1 to satisfy the official three-platform staging checks.

## Prepared files

The checked Steam archive is `dist/Ninjas4744-Turbo2026-1.0.1-Steam.zip`. Its 12 files are staged at `Artifacts/PumpingProject-20261121/BuiltMods/_Staging/rebuilt_ninjas_4744_turbo_2026`: four files for each platform. [Staging receipt](../Reports/steam-1.0.1.json) records each hash and the preserved platform source hashes. The original published files are backed up at `Artifacts/Steam101-20261122/published-before`.

The Windows installer ZIP is `dist/Ninjas4744-Turbo2026-1.0.1-Windows.zip`. Earlier contact-correction and diagnostic archives remain historical test builds. [Release verification](Verification.md) describes the tested limits and retained failed evidence.

## Upload procedure

Run Steam signed in as the owner of the existing item. Use the pinned Unity 2023.2.22f1 project and editor. After all current certificates pass, run:

```powershell
.\Tools\Package.ps1 -MoSimProject Artifacts/PumpingProject-20261121
python Tools/prepare_steam.py --project Artifacts/PumpingProject-20261121 --steam-item Artifacts/Steam101-20261122/published-before
.\Tools\Invoke-Unity.ps1 -MoSimProject Artifacts/PumpingProject-20261121 -UnityEditor ../davinci-creator/projects/mosim-4744-2026/tools/Unity2023/Editor/Unity.exe -Task Steam
.\Tools\Invoke-Unity.ps1 -MoSimProject Artifacts/PumpingProject-20261121 -UnityEditor ../davinci-creator/projects/mosim-4744-2026/tools/Unity2023/Editor/Unity.exe -Task SteamInspect
# The following command publishes the update to the existing item:
.\Tools\Invoke-Unity.ps1 -MoSimProject Artifacts/PumpingProject-20261121 -UnityEditor ../davinci-creator/projects/mosim-4744-2026/tools/Unity2023/Editor/Unity.exe -Task SteamUpload
```

`Steam` validates preparation without contacting Steam. `SteamInspect` reads the existing listing. `SteamUpload` uses the official Steam SDK, rechecks the exact 12-file hash inventory, submits content plus the 1.0.1 version tag and change note, and verifies the remote version and listing fields. It preserves the existing title, description, regular tags, visibility and preview. The local metadata asset has the existing item ID and auto-bump disabled.

The raw result is written to the project's `BuildArtifacts/steam-upload-receipt.json` and preserved in `Reports/steam-upload-1.0.1.json` after publication. A timeout has an unknown outcome: inspect the current remote item before attempting another submission.

Restart Steam/MoSim after the update downloads. Testers using the manually installed test ZIP should disable that local copy in MoSim's Mods menu to use the Workshop copy. This avoids an older local copy taking priority over the subscription.
