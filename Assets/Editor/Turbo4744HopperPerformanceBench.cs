using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using MoSimLib;
using Ninjas4744.MoSim;
using Unity.Burst;
using Unity.Jobs;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Isolated CPU/allocation measurements, not an FPS or retention certification.
/// Fuel stays at fixed sample positions to compare identical workloads between builds.</summary>
[InitializeOnLoad]
public static class Turbo4744HopperPerformanceBench
{
    private const string Pending = "Turbo4744.HopperPerformance";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Samples = 512;

    [Serializable]
    private sealed class Measurement
    {
        public string component;
        public int fuel,
            samples,
            netCandidateColliders;
        public double meanMicroseconds,
            p95Microseconds,
            gcAllocationsPerStep;
    }

    [Serializable]
    private sealed class Report
    {
        public string unity,
            cpu,
            label;
        public float fixedDeltaTime;
        public int netNodes,
            netSubsteps;
        public string scope =
            "Editor microbenchmark: fixed stock fuel positions; timed loops exclude rendering, driving, and Physics.Simulate. Net timings include job completion and deferred impulses. Optional checks simulate physics separately. Not player FPS.";
        public List<Measurement> measurements = new List<Measurement>();
        public List<string> checks = new List<string>();
        public string error;
    }

    static Turbo4744HopperPerformanceBench()
    {
        EditorApplication.playModeStateChanged += Mode;
    }

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
            EditorApplication.delayCall += Measure;
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.Exit(SessionState.GetInt(Pending + ".exit", 1));
        }
    }

    private static Action Step(object target) =>
        (Action)
            target
                .GetType()
                .GetMethod("FixedUpdate", Private)
                .CreateDelegate(typeof(Action), target);

    private static void Measure()
    {
        var report = new Report
        {
            unity = Application.unityVersion,
            cpu = SystemInfo.processorType,
            fixedDeltaTime = Time.fixedDeltaTime,
            label = Argument("-turboPerfLabel", "baseline"),
        };
        GameObject fixture = null;
        try
        {
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            Physics.simulationMode = SimulationMode.Script;
            fixture = new GameObject("Hopper performance fixture");
            fixture.AddComponent<Rigidbody>().isKinematic = true;
            var compliance = fixture.AddComponent<Turbo4744FuelCompliance>();
            var complianceStep = Step(compliance);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Ninjas4744Builder.Root + "/Turbo4744.prefab"
            );
            var source = prefab
                .GetComponent<Turbo4744>()
                .roofNet.GetComponent<Turbo4744NetSolver>();
            report.netNodes = source.resolutionX * source.resolutionY;
            report.netSubsteps = source.subSteps;
            var fuelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
            );
            var fuel = new List<GameObject>();
            for (int i = 0; i < 60; i++)
            {
                var piece = Object.Instantiate(fuelPrefab, fixture.transform);
                piece.transform.position = new Vector3(
                    (i % 5 - 2) * .14f,
                    .26f + i / 20 * .135f,
                    .04f + i / 5 % 4 * .14f
                );
                foreach (var behaviour in piece.GetComponentsInChildren<MonoBehaviour>())
                    behaviour.enabled = false;
                piece.GetComponent<Rigidbody>().useGravity = false;
                piece.SetActive(false);
                fuel.Add(piece);
            }
            foreach (int count in new[] { 0, 30, 60 })
            {
                for (int i = 0; i < fuel.Count; i++)
                    fuel[i].SetActive(i < count);
                Physics.SyncTransforms();
                report.measurements.Add(Sample("FuelCompliance", count, complianceStep));
                var net = CreateNet(source, fixture.transform);
                var solver = net.GetComponent<Turbo4744NetSolver>();
                var netStep = Step(solver);
                var handle = typeof(Turbo4744NetSolver).GetField("_lastJobHandle", Private);
                Action solve = () =>
                {
                    netStep();
                    ((JobHandle)handle.GetValue(solver)).Complete();
                };
                var netResult = Sample("NetSolver + completion", count, solve);
                netResult.netCandidateColliders = CandidateCount(solver);
                report.measurements.Add(netResult);
                if (count == 60)
                {
                    // Diagnostic only: the shipped net sees both stock fuel spheres.
                    // Changing this mask alters contact reactions and needs mechanical validation.
                    int mask = solver.collisionLayers;
                    solver.collisionLayers = LayerMask.GetMask("GamePiece1", "GamePiece2");
                    var singleSphere = Sample(
                        "NetSolver outer sphere only (diagnostic)",
                        count,
                        solve
                    );
                    singleSphere.netCandidateColliders = CandidateCount(solver);
                    report.measurements.Add(singleSphere);
                    solver.collisionLayers = mask;
                }
                var environment = net.AddComponent<Turbo4744NetEnvironment>();
                report.measurements.Add(
                    Sample("NetEnvironment (completed net)", count, Step(environment))
                );
                Object.DestroyImmediate(net);
            }
            if (Argument("-turboPerfVerify", "false") == "true")
                VerifyCompliance(compliance, complianceStep, fuel, report);
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
            string output = Path.GetFullPath(
                Application.dataPath
                    + "/../BuildArtifacts/hopper-performance-"
                    + report.label
                    + ".json"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            UnityEngine.Debug.Log("Hopper performance report: " + output);
            SessionState.SetInt(Pending + ".exit", report.error == null ? 0 : 1);
            EditorApplication.ExitPlaymode();
        }
    }

    private static int CandidateCount(Turbo4744NetSolver solver)
    {
        var candidates = typeof(Turbo4744NetSolver)
            .GetField("_activeColliders", Private)
            .GetValue(solver);
        return (int)candidates.GetType().GetProperty("Length").GetValue(candidates);
    }

    private static GameObject CreateNet(Turbo4744NetSolver source, Transform parent)
    {
        var net = new GameObject("Net sample", typeof(MeshFilter), typeof(MeshRenderer));
        net.transform.SetParent(parent);
        net.SetActive(false);
        var solver = net.AddComponent<Turbo4744NetSolver>();
        EditorUtility.CopySerialized(source, solver);
        var anchors = new List<NetSolver.NodeAnchor>();
        foreach (var anchor in source.customAnchors)
        {
            var pin = new GameObject("Sample pin").transform;
            pin.SetParent(net.transform);
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
        solver.customAnchors = anchors;
        net.SetActive(true);
        return net;
    }

    private static Measurement Sample(string component, int fuel, Action step)
    {
        for (int i = 0; i < 64; i++)
            step();
        var elapsed = new double[Samples];
        // Unity's Mono runtime reports zero from GetAllocatedBytesForCurrentThread.
        using var allocations = ProfilerRecorder.StartNew(
            ProfilerCategory.Internal,
            "GC.Alloc",
            1,
            ProfilerRecorderOptions.SumAllSamplesInFrame
                | ProfilerRecorderOptions.CollectOnlyOnCurrentThread
        );
        long sum = 0;
        for (int i = 0; i < Samples; i++)
        {
            long start = Stopwatch.GetTimestamp();
            step();
            long ticks = Stopwatch.GetTimestamp() - start;
            sum += ticks;
            elapsed[i] = ticks * 1000000.0 / Stopwatch.Frequency;
        }
        allocations.Stop();
        long allocated = allocations.Count == 0 ? 0 : allocations.GetSample(0).Count;
        Array.Sort(elapsed);
        return new Measurement
        {
            component = component,
            fuel = fuel,
            samples = Samples,
            meanMicroseconds = sum * 1000000.0 / Stopwatch.Frequency / Samples,
            p95Microseconds = elapsed[(int)(Samples * .95)],
            gcAllocationsPerStep = (double)allocated / Samples,
        };
    }

    private static void VerifyCompliance(
        Turbo4744FuelCompliance compliance,
        Action step,
        List<GameObject> fuel,
        Report report
    )
    {
        compliance.enabled = false;
        var first = fuel[0].GetComponentInChildren<SphereCollider>();
        var second = fuel[1].GetComponentInChildren<SphereCollider>();
        first.hasModifiableContacts = true;
        second.hasModifiableContacts = false;
        compliance.enabled = true;
        step();
        if (!first.hasModifiableContacts || !second.hasModifiableContacts)
            throw new InvalidOperationException("Stored fuel did not enable contact modification");
        var acquire = typeof(Turbo4744FuelCompliance).GetMethod("AcquireSnapshot", Private);
        var release = typeof(Turbo4744FuelCompliance).GetMethod("ReleaseSnapshot", Private);
        var held = acquire.Invoke(compliance, null);
        var alsoHeld = acquire.Invoke(compliance, null);
        var colliders = (HashSet<int>)held.GetType().GetField("colliders").GetValue(held);
        int heldCount = colliders.Count;
        var velocities =
            (Dictionary<int, Vector3>)held.GetType().GetField("velocity").GetValue(held);
        if (velocities.Count != 60 || heldCount < 60 || !ReferenceEquals(held, alsoHeld))
            throw new InvalidOperationException("Snapshot fixture did not capture 60 fuel");
        fuel[0].transform.position = new Vector3(3, 1, 0);
        fuel[1].transform.position = new Vector3(3, 1, 1);
        Physics.SyncTransforms();
        for (int i = 0; i < 16; i++)
            step();
        if (
            !first.hasModifiableContacts
            || second.hasModifiableContacts
            || colliders.Count != heldCount
        )
            throw new InvalidOperationException(
                "Leaving fuel lost its original flag or a reader's snapshot changed"
            );
        release.Invoke(compliance, new[] { held });
        for (int i = 0; i < 16; i++)
            step();
        compliance.enabled = false;
        if (colliders.Count != heldCount)
            throw new InvalidOperationException("Disable reused an in-flight snapshot");
        release.Invoke(compliance, new[] { alsoHeld });
        compliance.enabled = true;
        step();
        for (int i = 0; i < 120; i++)
        {
            step();
            Physics.Simulate(Time.fixedDeltaTime);
        }
        if (compliance.ModifiedContacts == 0)
            throw new InvalidOperationException(
                "Real physics simulation did not exercise the contact callback"
            );
        compliance.enabled = false;
        if (!first.hasModifiableContacts || second.hasModifiableContacts)
            throw new InvalidOperationException("Disable did not restore original contact flags");
        report.checks.Add("60 fuel captured; original contact flags restored on exit and disable");
        report.checks.Add(
            "Two overlapping callback readers retain immutable snapshots across publication and disable"
        );
        report.checks.Add(
            "120 real physics steps exercised modified fuel contacts after re-enable"
        );
    }

    private static string Argument(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
}
