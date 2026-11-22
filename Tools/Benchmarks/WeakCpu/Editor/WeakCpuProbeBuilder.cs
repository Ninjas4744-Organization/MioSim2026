using System;
using System.IO;
using System.Linq;
using GameSystems.Modding.Authoring.GameSystems.Modding.Editor;
using Ninjas4744.Benchmark;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

public static class WeakCpuProbeBuilder
{
    public static void Build()
    {
        const string path = "Assets/WeakCpuObserver/WeakCpuProbe.asset";
        var pack = AssetDatabase.LoadAssetAtPath<WeakCpuProbeModpack>(path);
        if (!pack)
        {
            pack = ScriptableObject.CreateInstance<WeakCpuProbeModpack>();
            AssetDatabase.CreateAsset(pack, path);
        }
        var serialized = new SerializedObject(pack);
        serialized.FindProperty("modpackName").stringValue = "Temporary CPU Benchmark Observer";
        serialized.FindProperty("modpackVersion").stringValue = "0.1.0";
        serialized.FindProperty("authorName").stringValue = "Ninjas 4744";
        serialized.FindProperty("description").stringValue =
            "Temporary local measurement tool; zero robots; removed after testing.";
        serialized.FindProperty("robots").arraySize = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        string player = Path.GetFullPath("BuildArtifacts/WeakCpuObserver/PlayerScripts");
        Directory.CreateDirectory(player);
        var result = PlayerBuildInterface.CompilePlayerScripts(
            new ScriptCompilationSettings
            {
                group = BuildTargetGroup.Standalone,
                target = BuildTarget.StandaloneWindows64,
                options = ScriptCompilationOptions.None,
            },
            player
        );
        string dll = Path.Combine(player, "Ninjas4744WeakCpuObserver.dll");
        if (result.assemblies.Count == 0 || !File.Exists(dll))
            throw new InvalidOperationException("Probe player compile failed");
        if (!ModBuildUtility.TryBuildModpack(pack, true, out var built))
            throw new InvalidOperationException("Probe bundle failed");
        File.Copy(
            dll,
            Path.Combine(built, "standalonewindows64/Ninjas4744WeakCpuObserver.dll"),
            true
        );
        // This observer has no robot prefab. Supply its independent DLL and
        // bundle explicitly, and activate it only when the Ninjas pack is selected.
        File.WriteAllText(
            Path.Combine(built, "standalonewindows64/modpack.json"),
            "{\"id\":\"rebuilt_temporary_cpu_benchmark_observer_assets\","
                + "\"name\":\"Temporary CPU Benchmark Observer\",\"version\":\"0.1.0\","
                + "\"author\":\"Ninjas 4744\",\"gameYear\":2026,\"gameId\":\"Rebuilt\","
                + "\"gameName\":\"Rebuilt\",\"gameType\":\"FRC\","
                + "\"modDll\":\"Ninjas4744WeakCpuObserver.dll\",\"modpack\":{"
                + "\"metadataGuid\":\"651ec15a-e146-13ad-192f-f3dea3d0315a\","
                + "\"metadataName\":\"Temporary CPU Benchmark Observer\"}}"
        );
        File.WriteAllText("BuildArtifacts/WeakCpuObserver/build-path.txt", built);
        Debug.Log("WEAK_CPU_PROBE_BUILT " + built + " GUID=" + pack.GetGuid());
    }
}
