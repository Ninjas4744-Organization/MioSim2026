using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MoSimLib;
using Ninjas4744.MoSim;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// Compares the local solver with the unmodified framework solver, including ordered reactions.
[InitializeOnLoad]
public static class Turbo4744NetCollisionRegression
{
    private const string Pending = "Turbo4744.NetCollision";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Serializable]
    private sealed class Report
    {
        public bool passed,
            burstEnabled;
        public string cpu,
            error,
            solverSha256,
            editorRuntimeDllSha256;
        public int comparedSteps,
            comparedImpulses,
            candidates,
            narrowphaseChecks,
            unfilteredChecks;
        public double originalMicroseconds,
            optimizedMicroseconds;
        public List<string> checks = new List<string>();
    }

    static Turbo4744NetCollisionRegression() => EditorApplication.playModeStateChanged += Mode;

    public static void Run()
    {
        SessionState.SetBool(Pending, true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void Mode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Pending, false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
            EditorApplication.delayCall += Check;
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.Exit(SessionState.GetInt(Pending + ".exit", 1));
        }
    }

    private static Action Step(MonoBehaviour solver) =>
        (Action)
            solver
                .GetType()
                .GetMethod("FixedUpdate", Private)
                .CreateDelegate(typeof(Action), solver);

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, Private).GetValue(target);

    private static void Check()
    {
        var report = new Report { cpu = SystemInfo.processorType };
        GameObject fixture = null;
        try
        {
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            BurstCompiler.Options.EnableBurstCompilation = !Environment
                .GetCommandLineArgs()
                .Contains("-turboNetManaged");
            report.burstEnabled = BurstCompiler.Options.EnableBurstCompilation;
            Physics.simulationMode = SimulationMode.Script;
            fixture = new GameObject("Net equivalence fixture");
            fixture.AddComponent<Rigidbody>().isKinematic = true;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Ninjas4744Builder.Root + "/Turbo4744.prefab"
            );
            var source = prefab
                .GetComponent<Turbo4744>()
                .roofNet.GetComponent<Turbo4744NetSolver>();
            report.solverSha256 = Hash(
                AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(source))
            );
            report.editorRuntimeDllSha256 = Hash(typeof(Turbo4744NetSolver).Assembly.Location);
            var original = (NetSolver)Create(source, typeof(NetSolver), fixture.transform);
            var optimized = (Turbo4744NetSolver)Create(
                source,
                typeof(Turbo4744NetSolver),
                fixture.transform
            );
            Action originalStep = Step(original),
                optimizedStep = Step(optimized);
            Action originalComplete = () =>
                ((JobHandle)Field(original, "_lastJobHandle")).Complete();
            var fuelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
            );
            var fuel = new List<GameObject>();
            for (int i = 0; i < 128; i++)
            {
                var piece = Object.Instantiate(fuelPrefab, fixture.transform);
                foreach (var component in piece.GetComponentsInChildren<MonoBehaviour>())
                    component.enabled = false;
                piece.GetComponent<Rigidbody>().useGravity = false;
                piece.SetActive(false);
                fuel.Add(piece);
            }
            var random = new System.Random(4744);
            for (int scenario = 0; scenario < 5; scenario++)
            {
                int count =
                    scenario == 0 ? 0
                    : scenario == 4 ? 128
                    : 60;
                for (int i = 0; i < fuel.Count; i++)
                    fuel[i].SetActive(i < count);
                original.Rebuild();
                optimized.Rebuild();
                CompareState(original, optimized, report);
                for (int step = 0; step < 96; step++)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var point = new Vector3(
                            (i % 5 - 2) * .14f,
                            .26f + i / 20 * .135f,
                            .04f + i / 5 % 4 * .14f
                        );
                        if (scenario == 2)
                            point = new Vector3(
                                (float)random.NextDouble() * .9f - .45f,
                                .533f
                                    + ((i & 1) == 0 ? 1 : -1)
                                        * (.07615f + ((i % 3) - 1) * .000001f),
                                (float)random.NextDouble() * .9f - .15f
                            );
                        if (scenario == 3)
                            point += new Vector3(
                                Mathf.Sin(step * .13f + i) * .025f,
                                Mathf.Cos(step * .07f + i) * .04f,
                                .05f * Mathf.Sin(step * .11f)
                            );
                        fuel[i].transform.position = point;
                    }
                    if (scenario == 3)
                        MovePins(source, original, optimized, step);
                    Physics.SyncTransforms();
                    originalStep();
                    originalComplete();
                    optimizedStep();
                    optimized.CompleteSimulation();
                    CompareState(original, optimized, report);
                    report.comparedSteps++;
                }
                report.checks.Add(
                    "Scenario "
                        + scenario
                        + ": "
                        + count
                        + " fuel, 96 steps, bitwise state and ordered impulses equal"
                );
            }
            for (int i = 0; i < fuel.Count; i++)
            {
                fuel[i].SetActive(i < 60);
                fuel[i].transform.position = new Vector3(
                    (i % 5 - 2) * .14f,
                    .26f + i / 20 * .135f,
                    .04f + i / 5 % 4 * .14f
                );
            }
            Physics.SyncTransforms();
            original.Rebuild();
            optimized.Rebuild();
            report.originalMicroseconds = Measure(() =>
            {
                originalStep();
                originalComplete();
            });
            report.optimizedMicroseconds = Measure(() =>
            {
                optimizedStep();
                optimized.CompleteSimulation();
            });
            report.candidates = optimized.CollisionCandidates;
            report.narrowphaseChecks = optimized.NarrowphaseChecks;
            report.unfilteredChecks = optimized.UnfilteredChecks;
            original.enabled = optimized.enabled = false;
            original.enabled = optimized.enabled = true;
            originalStep();
            originalComplete();
            optimizedStep();
            optimized.CompleteSimulation();
            CompareState(original, optimized, report);
            report.checks.Add("Disable/re-enable and Rebuild preserve matching state");
            report.passed = true;
        }
        catch (Exception error)
        {
            report.error = error.ToString();
            UnityEngine.Debug.LogException(error);
        }
        finally
        {
            if (fixture)
                Object.DestroyImmediate(fixture);
            var suffix = report.burstEnabled ? "burst" : "managed";
            var output = Path.GetFullPath(
                Application.dataPath + "/../BuildArtifacts/net-collision-" + suffix + ".json"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            SessionState.SetInt(Pending + ".exit", report.passed ? 0 : 1);
            EditorApplication.ExitPlaymode();
        }
    }

    private static MonoBehaviour Create(Turbo4744NetSolver source, Type type, Transform parent)
    {
        var go = new GameObject(type.Name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent);
        go.SetActive(false);
        var solver = (MonoBehaviour)go.AddComponent(type);
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), solver);
        var anchors = new List<NetSolver.NodeAnchor>();
        foreach (var anchor in source.customAnchors)
        {
            var pin = new GameObject("Comparison pin").transform;
            pin.SetParent(go.transform);
            pin.position = anchor.targetTransform.position;
            anchors.Add(
                new NetSolver.NodeAnchor
                {
                    targetTransform = pin,
                    gridX = anchor.gridX,
                    gridY = anchor.gridY,
                    isReleased = anchor.isReleased,
                }
            );
        }
        type.GetField("customAnchors").SetValue(solver, anchors);
        go.SetActive(true);
        return solver;
    }

    private static void MovePins(
        Turbo4744NetSolver source,
        NetSolver original,
        Turbo4744NetSolver optimized,
        int step
    )
    {
        for (int i = 0; i < source.customAnchors.Count; i++)
        {
            Vector3 position = source.customAnchors[i].targetTransform.position;
            position.z +=
                Mathf.Sin(step * .09f)
                * .06f
                * source.customAnchors[i].gridY
                / (source.resolutionY - 1);
            original.customAnchors[i].targetTransform.position = position;
            optimized.customAnchors[i].targetTransform.position = position;
        }
    }

    private static void CompareState(
        NetSolver original,
        Turbo4744NetSolver optimized,
        Report report
    )
    {
        foreach (string name in new[] { "_pos", "_prevPos", "_vel", "_meshVertsNative" })
        {
            if (name == "_meshVertsNative")
            {
                var a = (NativeArray<Vector3>)Field(original, name);
                var b = (NativeArray<Vector3>)Field(optimized, name);
                for (int i = 0; i < a.Length; i++)
                    Equal(a[i], b[i], name + "[" + i + "]");
            }
            else
            {
                var a = (NativeArray<float3>)Field(original, name);
                var b = (NativeArray<float3>)Field(optimized, name);
                for (int i = 0; i < a.Length; i++)
                    Equal(a[i], b[i], name + "[" + i + "]");
            }
        }
        var originalLambda = (NativeArray<float>)Field(original, "_lambda");
        var optimizedLambda = (NativeArray<float>)Field(optimized, "_lambda");
        for (int i = 0; i < originalLambda.Length; i++)
            if (math.asuint(originalLambda[i]) != math.asuint(optimizedLambda[i]))
                throw new InvalidOperationException(
                    "Constraint lambda mismatch " + i + " at step " + report.comparedSteps
                );
        object aQueue = Field(original, "_impulseQueue"),
            bQueue = Field(optimized, "_impulseQueue");
        object aArray = aQueue
            .GetType()
            .GetMethod("ToArray")
            .Invoke(aQueue, new object[] { (AllocatorManager.AllocatorHandle)Allocator.Temp });
        object bArray = bQueue
            .GetType()
            .GetMethod("ToArray")
            .Invoke(bQueue, new object[] { (AllocatorManager.AllocatorHandle)Allocator.Temp });
        try
        {
            var a = ((IEnumerable)aArray).Cast<object>().ToArray();
            var b = ((IEnumerable)bArray).Cast<object>().ToArray();
            if (a.Length != b.Length)
                throw new InvalidOperationException(
                    "Reaction count mismatch at step " + report.comparedSteps
                );
            for (int i = 0; i < a.Length; i++)
            {
                if (
                    (int)a[i].GetType().GetField("InstanceID").GetValue(a[i])
                    != (int)b[i].GetType().GetField("InstanceID").GetValue(b[i])
                )
                    throw new InvalidOperationException("Reaction order mismatch");
                Equal(
                    (Vector3)a[i].GetType().GetField("Force").GetValue(a[i]),
                    (Vector3)b[i].GetType().GetField("Force").GetValue(b[i]),
                    "Reaction force"
                );
                Equal(
                    (Vector3)a[i].GetType().GetField("Point").GetValue(a[i]),
                    (Vector3)b[i].GetType().GetField("Point").GetValue(b[i]),
                    "Reaction point"
                );
            }
            report.comparedImpulses += a.Length;
        }
        finally
        {
            ((IDisposable)aArray).Dispose();
            ((IDisposable)bArray).Dispose();
        }
    }

    private static void Equal(float3 a, float3 b, string label)
    {
        if (math.any(math.asuint(a) != math.asuint(b)))
            throw new InvalidOperationException(label + " differs: " + a + " / " + b);
    }

    private static double Measure(Action step)
    {
        for (int i = 0; i < 64; i++)
            step();
        long began = Stopwatch.GetTimestamp();
        for (int i = 0; i < 256; i++)
            step();
        return (Stopwatch.GetTimestamp() - began) * 1000000.0 / Stopwatch.Frequency / 256;
    }

    private static string Hash(string path)
    {
        using (var hash = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter
                .ToString(hash.ComputeHash(stream))
                .Replace("-", "")
                .ToLowerInvariant();
    }
}
