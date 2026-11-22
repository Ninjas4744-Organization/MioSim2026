# Uploading Turbo 1.0.1 to Steam Workshop

**Current status: ready for manual upload.** The final archive is `dist/Ninjas4744-Turbo2026-1.0.1-Steam.zip`; all three platforms are staged. Full-load retention/recovery and partially loaded transit checks pass. See [verification](Verification.md).

This updates the existing item **3843685878** for MoSim (**4398690**).
Publication is a separate manual step; preparing the release does not upload it.

The Windows build contains the net collision optimization, control variants and
Ninjas selector logo. Linux and macOS retain their published DLLs and asset
bundles, including their existing behavior and artwork. Their manifest version
labels change to 1.0.1 because the official uploader requires all three platform
manifests to match the release version. The original Steam files remain untouched.

## Upload the prepared update

Open the pinned Unity 2023.2.22f1 project at
`Artifacts/PumpingProject-20261121`. The prepared content is in:

```text
BuiltMods/_Staging/rebuilt_ninjas_4744_turbo_2026/
  standalonewindows64/
  standalonelinux64/
  standaloneosx/
```

The same certified content is archived in
`dist/Ninjas4744-Turbo2026-1.0.1-Steam.zip`. Upload the staged folder, rather than
the Windows-only trial ZIP.

1. Run Steam and sign in as the owner of the existing Workshop item.
2. In Unity, select `Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744/Ninjas4744.asset`.
   Open **Tools → Modding → Steam Workshop Uploader**.
3. Check **Published File Id = 3843685878**, **Displayed Version = 1.0.1**, and
   **Auto Bump Patch On Build** is off. Review the title, description, tags and
   visibility shown in the uploader; these listing fields are submitted too.
   A blank preview image preserves the existing item's Steam preview.
   The local `Ninjas4744_WorkshopMetadata.asset` already has the existing item ID
   and auto-bump setting configured.
4. Paste the change note from `BuildArtifacts/steam-change-note.txt` into
   **Change Note**. The content is already staged. **Build All Platforms + Stage**
   would replace the preserved Linux/macOS builds and is unnecessary for this update.
5. Click **Upload Staged Build to Steam**, verify the confirmation says it updates
   item **3843685878**, then click **Upload**. Wait for the successful result.
6. Open the existing Workshop page and check its version/change note. Restart
   Steam/MoSim after the download. For a subscriber smoke test, disable the local
   `Ninjas4744-Turbo2026` copy in MoSim's Mods menu so it cannot mask the Workshop copy.

The uploader uses Steam's existing-item update API. Keeping the same item ID
delivers the update to existing subscribers. See the
[Steam Workshop implementation guide](https://partner.steamgames.com/doc/features/workshop/implementation).

## Reproducing preparation

After all final-build fixtures and native cycles pass:

```powershell
python Tools/verify_release.py --project Artifacts/PumpingProject-20261121
# Generate all three version-matched native certificates first:
# verify_native_full_hopper.py, verify_native_trench.py, verify_native_bump.py.
.\Tools\Package.ps1 -MoSimProject Artifacts/PumpingProject-20261121
python Tools/prepare_steam.py --project Artifacts/PumpingProject-20261121
.\Tools\Invoke-Unity.ps1 -MoSimProject Artifacts/PumpingProject-20261121 `
  -UnityEditor <Unity2023.2.22f1/Editor/Unity.exe> -Task Steam
```

`Reports/steam-1.0.1.json` records every staged file hash and the corresponding
Linux/macOS source hash. The preparation tool rejects stale release certificates,
changed preserved binaries, missing platforms and unexpected staged files.
