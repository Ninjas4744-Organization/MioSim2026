using System;
using System.IO;
using GameSystems.Management;
using GameSystems.Modding.Authoring.GameSystems.Modding.Editor;
using UnityEditor;
using UnityEngine;

/// Prepares and validates the existing Workshop item without contacting Steam.
public static class Turbo4744SteamRelease
{
    public const string Version = Ninjas4744.MoSim.Turbo4744.ReleaseVersion;

    [Serializable]
    private sealed class ReleaseStatus
    {
        public bool release_ready;
    }

    public const string ChangeNote =
        "1.0.2: Improve Windows recovery when stopped on bumps or leaving a dense ball pile. "
        + "Extend the existing bounded bumper-foam response to ramp and carpet contacts. "
        + "Preserve steering, motor tuning, tire grip and net physics. "
        + "Keep existing Linux and macOS binaries.";

    [MenuItem("Tools/Modding/Ninjas 4744/Prepare Steam Upload")]
    public static void Prepare()
    {
        var pack = AssetDatabase.LoadAssetAtPath<BaseModpackSO>(Ninjas4744Builder.PackPath);
        if (!pack || pack.ModpackVersion != Version)
            throw new InvalidOperationException("Build the checked " + Version + " release first.");
        var metadata = ModpackWorkshopMetadataEditorUtility.FindMetadata(pack)
            ?? ModpackWorkshopMetadataEditorUtility.CreateMetadata(pack);
        if (metadata.PublishedFileId != 0 && metadata.PublishedFileId != 3843685878UL)
            throw new InvalidOperationException("Workshop metadata points at a different item.");
        metadata.PublishedFileId = 3843685878UL;
        var serialized = new SerializedObject(metadata);
        serialized.FindProperty("autoBumpPatchOnBuild").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(metadata);
        AssetDatabase.SaveAssets();
        string statusPath = Path.GetFullPath(
            Application.dataPath + "/../BuildArtifacts/steam-release-status.json"
        );
        if (
            !File.Exists(statusPath)
            || !JsonUtility.FromJson<ReleaseStatus>(File.ReadAllText(statusPath)).release_ready
        )
            throw new InvalidOperationException(
                "Steam release is held: complete the native release checks and run Tools/prepare_steam.py without --candidate."
            );
        var staging = ModBuildUtility.GetStagingPath(pack);
        var problems = WorkshopPublisher.Validate(metadata, staging, ChangeNote);
        if (problems.Count != 0)
            throw new InvalidOperationException(string.Join("\n", problems));
        var artifacts = Path.GetFullPath(Application.dataPath + "/../BuildArtifacts");
        File.WriteAllText(artifacts + "/steam-change-note.txt", ChangeNote + "\n");
        File.WriteAllText(
            artifacts + "/steam-upload-ready.txt",
            WorkshopPublisher.Describe(metadata, staging)
                + "\nContent: "
                + staging
                + "\nNot uploaded.\n"
        );
        Selection.activeObject = pack;
        Debug.Log("TURBO_STEAM_READY " + staging);
    }
}
