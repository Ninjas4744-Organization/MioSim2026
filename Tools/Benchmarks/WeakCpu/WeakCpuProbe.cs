using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ninjas4744.Benchmark
{
    [DefaultExecutionOrder(30000)]
    public sealed class WeakCpuProbe : MonoBehaviour
    {
        [Serializable]
        private sealed class Configuration
        {
            public string label,
                outputDirectory,
                build;
            public int minimumFuel = 55;
            public float profileSeconds = 14,
                warmupSeconds = 2;
        }

        [Serializable]
        private sealed class Evidence
        {
            public string label,
                build,
                robotAssembly,
                netType,
                error;
            public string cpu,
                gpu,
                unity,
                csv;
            public string startedUtc,
                finishedUtc;
            public int logicalProcessors,
                netNodes,
                netSubsteps,
                initialFuel;
            public string originalAffinity,
                limitedAffinity;
            public bool completed,
                nativeBurstProbe,
                quotaVerified,
                affinityRestored;
            public float fixedDeltaTime,
                maximumDeltaTime;
            public int width,
                height,
                vSync,
                targetFps;
            public List<string> events = new List<string>();
        }

        public static bool Active { get; private set; }
        private const BindingFlags Instance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private Configuration config;
        private Evidence evidence;
        private MonoBehaviour replay;
        private object replayReport,
            request;
        private FieldInfo fuelField,
            phaseField,
            beganField,
            stoppedField,
            finishedField;
        private FieldInfo releasedField,
            settleField;
        private MonoBehaviour net;
        private StreamWriter csv;
        private CpuBudget budget;
        private readonly Stopwatch wall = new Stopwatch();
        private int stage = -1,
            physicsSteps;
        private double profileBegan,
            nextSearch,
            nextFlush,
            emptySince = -1;
        private bool done;
        private string outputBase;
        private readonly string[] names =
        {
            "full-cpu",
            "two-cores",
            "cpu-8-percent",
            "cpu-5-percent",
            "shoot-cpu-5-percent",
        };

        private void Awake()
        {
            if (Active)
            {
                Destroy(gameObject);
                return;
            }
            string path = Path.Combine(Application.persistentDataPath, "Turbo4744-weak-cpu.json");
            if (Application.isEditor || !File.Exists(path))
            {
                Destroy(gameObject);
                return;
            }
            Active = true;
            DontDestroyOnLoad(gameObject);
            try
            {
                config = JsonUtility.FromJson<Configuration>(File.ReadAllText(path));
                Directory.CreateDirectory(config.outputDirectory);
                outputBase = Path.Combine(config.outputDirectory, config.label);
                if (File.Exists(outputBase + ".csv"))
                    throw new IOException("Benchmark label already has evidence");
                csv = new StreamWriter(outputBase + ".csv", false, new UTF8Encoding(false), 65536);
                csv.WriteLine(
                    "wallSeconds,profile,phase,simSeconds,frameMs,fuel,physicsSteps,focused,cpuRate,affinity,released"
                );
                evidence = new Evidence
                {
                    label = config.label,
                    build = config.build,
                    startedUtc = DateTime.UtcNow.ToString("O"),
                    cpu = SystemInfo.processorType,
                    gpu = SystemInfo.graphicsDeviceName,
                    unity = Application.unityVersion,
                    logicalProcessors = Environment.ProcessorCount,
                    csv = config.label + ".csv",
                    fixedDeltaTime = Time.fixedDeltaTime,
                    maximumDeltaTime = Time.maximumDeltaTime,
                    width = Screen.width,
                    height = Screen.height,
                    vSync = QualitySettings.vSyncCount,
                    targetFps = Application.targetFrameRate,
                };
                budget = new CpuBudget();
                evidence.originalAffinity = budget.OriginalAffinity.ToString("X");
                evidence.limitedAffinity = budget.LimitedAffinity.ToString("X");
                wall.Start();
                Event("Observer ready; neither robot bundle nor DLL is modified");
                Save();
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private static object Get(object target, string name) =>
            target.GetType().GetField(name, Instance).GetValue(target);

        private static int Number(object target, string name) => (int)Get(target, name);

        private void Bind()
        {
            foreach (var component in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (
                    !component
                    || component.GetType().FullName != "Ninjas4744.MoSim.Turbo4744NativeReplay"
                )
                    continue;
                object report = Get(component, "report");
                if (report == null)
                    continue;
                replay = component;
                replayReport = report;
                request = Get(component, "request");
                var type = component.GetType();
                fuelField = report.GetType().GetField("remainingCaptured", Instance);
                releasedField = report.GetType().GetField("releasedCaptured", Instance);
                phaseField = type.GetField("previousPhase", Instance);
                beganField = type.GetField("began", Instance);
                stoppedField = type.GetField("collectionStopped", Instance);
                finishedField = type.GetField("finished", Instance);
                settleField = request.GetType().GetField("collectionSettleSeconds", Instance);
                net = Get(component, "net") as MonoBehaviour;
                evidence.robotAssembly = type.Assembly.FullName;
                evidence.netType = net ? net.GetType().FullName : "missing";
                if (net)
                {
                    evidence.netNodes = Number(net, "resolutionX") * Number(net, "resolutionY");
                    evidence.netSubsteps = Number(net, "subSteps");
                }
                Event("Bound existing replay; net=" + evidence.netType);
                Save();
                return;
            }
        }

        private void FixedUpdate() => physicsSteps++;

        private void LateUpdate()
        {
            if (done || csv == null)
                return;
            try
            {
                if (!replay)
                {
                    if (wall.Elapsed.TotalSeconds > nextSearch)
                    {
                        nextSearch = wall.Elapsed.TotalSeconds + 1;
                        Bind();
                    }
                    return;
                }
                int fuel = (int)fuelField.GetValue(replayReport);
                int phase = (int)phaseField.GetValue(replay);
                float sim = Time.fixedTime - (float)beganField.GetValue(replay);
                float stopped = (float)stoppedField.GetValue(replay);
                double now = wall.Elapsed.TotalSeconds;
                if (stage < 0 && stopped >= 0 && sim - stopped >= 3)
                {
                    if (fuel < config.minimumFuel)
                        throw new InvalidOperationException("Not a full hopper: " + fuel);
                    evidence.initialFuel = fuel;
                    Enter(0);
                }
                if (stage < 0 && sim > 45)
                    throw new TimeoutException(
                        "The input replay did not fill a hopper in 45 simulated seconds"
                    );
                if (stage >= 0)
                {
                    csv.WriteLine(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:F6},{1},{2},{3:F6},{4:F6},{5},{6},{7},{8},{9:X},{10}",
                            now,
                            names[stage],
                            phase,
                            sim,
                            Time.unscaledDeltaTime * 1000,
                            fuel,
                            physicsSteps,
                            Application.isFocused ? 1 : 0,
                            budget.Rate,
                            budget.CurrentAffinity,
                            (int)releasedField.GetValue(replayReport)
                        )
                    );
                    if (stage < 4 && fuel < config.minimumFuel)
                        throw new InvalidOperationException(
                            "Full-hopper sample lost its load: " + fuel
                        );
                    if (stage < 4 && now - profileBegan >= config.profileSeconds)
                    {
                        Enter(stage + 1);
                        if (stage == 4)
                        {
                            // Schedule the existing replay's ordinary shoot input just ahead.
                            // This changes only the test's waiting time, not forces or state.
                            settleField.SetValue(request, sim - stopped + .2f);
                            Event(
                                "Released collection wait; existing replay will press its normal shoot binding"
                            );
                        }
                    }
                    else if (stage == 4)
                    {
                        if (fuel == 0 && emptySince < 0)
                            emptySince = now;
                        if ((emptySince >= 0 && now - emptySince >= 2) || now - profileBegan >= 45)
                        {
                            Finish(true);
                            return;
                        }
                    }
                }
                physicsSteps = 0;
                if (now > nextFlush)
                {
                    csv.Flush();
                    nextFlush = now + 1;
                }
            }
            catch (Exception error)
            {
                Fail(error);
            }
        }

        private void Enter(int next)
        {
            stage = next;
            profileBegan = wall.Elapsed.TotalSeconds;
            budget.Apply(
                next == 0 ? budget.OriginalAffinity : budget.LimitedAffinity,
                next < 2 ? 0u
                    : next == 2 ? 800u
                    : 500u
            );
            evidence.quotaVerified = true;
            Event(
                "Profile "
                    + names[next]
                    + " rate="
                    + budget.Rate
                    + " affinity="
                    + budget.CurrentAffinity.ToString("X")
            );
            Save();
        }

        private void Event(string message)
        {
            evidence?.events.Add(
                wall.Elapsed.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture)
                    + " "
                    + message
            );
            Debug.Log("[WeakCpu] " + message);
        }

        private void Fail(Exception error)
        {
            if (evidence != null)
                evidence.error = error.ToString();
            Debug.LogException(error);
            Finish(false);
        }

        private void Finish(bool complete)
        {
            if (done)
                return;
            done = true;
            try
            {
                try
                {
                    budget?.Dispose();
                }
                catch (Exception error)
                {
                    complete = false;
                    if (evidence != null)
                        evidence.error += "\nCPU reset: " + error;
                }
                if (evidence != null)
                {
                    evidence.affinityRestored = budget == null || budget.Restored;
                    var probe = net ? net.GetType().GetProperty("BurstExecution", Instance) : null;
                    evidence.nativeBurstProbe = probe != null && (bool)probe.GetValue(net);
                    evidence.completed = complete;
                    evidence.finishedUtc = DateTime.UtcNow.ToString("O");
                    Event("Finished=" + complete + "; CPU limits cleared");
                    Save();
                }
            }
            finally
            {
                csv?.Dispose();
                csv = null;
            }
        }

        private void Save()
        {
            if (evidence != null && outputBase != null)
                File.WriteAllText(outputBase + ".json", JsonUtility.ToJson(evidence, true));
        }

        private void OnApplicationQuit() => Finish(false);

        private void OnDestroy()
        {
            Finish(false);
            Active = false;
        }

        private sealed class CpuBudget : IDisposable
        {
            [StructLayout(LayoutKind.Sequential)]
            private struct RateControl
            {
                public uint flags,
                    rate;
            }

            [StructLayout(LayoutKind.Explicit, Size = 32)]
            private struct CoreInfo
            {
                [FieldOffset(0)]
                public ulong mask;

                [FieldOffset(8)]
                public int relationship;
            }

            [DllImport("kernel32", SetLastError = true)]
            private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

            [DllImport("kernel32", SetLastError = true)]
            private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

            [DllImport("kernel32", SetLastError = true)]
            private static extern bool SetInformationJobObject(
                IntPtr job,
                int kind,
                ref RateControl info,
                uint size
            );

            [DllImport("kernel32", SetLastError = true)]
            private static extern bool QueryInformationJobObject(
                IntPtr job,
                int kind,
                out RateControl info,
                uint size,
                IntPtr length
            );

            [DllImport("kernel32", SetLastError = true)]
            private static extern bool GetLogicalProcessorInformation(IntPtr info, ref uint length);

            [DllImport("kernel32", SetLastError = true)]
            private static extern bool GetProcessAffinityMask(
                IntPtr process,
                out UIntPtr mask,
                out UIntPtr system
            );

            [DllImport("kernel32", SetLastError = true)]
            private static extern bool SetProcessAffinityMask(IntPtr process, UIntPtr mask);

            [DllImport("kernel32")]
            private static extern IntPtr GetCurrentProcess();

            [DllImport("kernel32")]
            private static extern bool CloseHandle(IntPtr handle);

            private IntPtr job;
            public readonly ulong OriginalAffinity,
                LimitedAffinity;
            public ulong CurrentAffinity { get; private set; }
            public uint Rate { get; private set; }
            public bool Restored { get; private set; }

            private static void Check(bool success, string action)
            {
                if (!success)
                    throw new System.ComponentModel.Win32Exception(
                        Marshal.GetLastWin32Error(),
                        action
                    );
            }

            public CpuBudget()
            {
                Check(
                    GetProcessAffinityMask(GetCurrentProcess(), out var original, out _),
                    "Read game affinity"
                );
                OriginalAffinity = CurrentAffinity = original.ToUInt64();
                uint length = 0;
                GetLogicalProcessorInformation(IntPtr.Zero, ref length);
                IntPtr buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    Check(
                        GetLogicalProcessorInformation(buffer, ref length),
                        "Read processor topology"
                    );
                    int cores = 0;
                    for (int at = 0; at < length; at += 32)
                    {
                        var info = Marshal.PtrToStructure<CoreInfo>(IntPtr.Add(buffer, at));
                        if (
                            info.relationship == 0
                            && (info.mask & OriginalAffinity) != 0
                            && cores++ < 2
                        )
                            LimitedAffinity |= info.mask & OriginalAffinity;
                    }
                    if (LimitedAffinity == 0)
                        throw new InvalidOperationException("No usable core affinity");
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
                job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero)
                    Check(false, "Create game-only CPU job");
                try
                {
                    Check(
                        AssignProcessToJobObject(job, GetCurrentProcess()),
                        "Assign game-only CPU job"
                    );
                }
                catch
                {
                    CloseHandle(job);
                    job = IntPtr.Zero;
                    throw;
                }
            }

            public void Apply(ulong affinity, uint rate)
            {
                // A 100% system budget is effectively unrestricted. Windows rejects
                // the zero-flags/zero-rate reset on this machine (ERROR_INVALID_PARAMETER).
                uint actualRate = rate == 0 ? 10000u : rate;
                var info = new RateControl { flags = 5u, rate = actualRate };
                Check(SetInformationJobObject(job, 15, ref info, 8), "Set game CPU quota");
                Check(
                    QueryInformationJobObject(job, 15, out var actual, 8, IntPtr.Zero),
                    "Verify game CPU quota"
                );
                if (actual.flags != info.flags || actual.rate != actualRate)
                    throw new InvalidOperationException("CPU quota verification differs");
                Check(
                    SetProcessAffinityMask(GetCurrentProcess(), new UIntPtr(affinity)),
                    "Set game core affinity"
                );
                Check(
                    GetProcessAffinityMask(GetCurrentProcess(), out var mask, out _),
                    "Verify game core affinity"
                );
                if (mask.ToUInt64() != affinity)
                    throw new InvalidOperationException("Core affinity verification differs");
                Rate = actualRate;
                CurrentAffinity = affinity;
            }

            public void Dispose()
            {
                if (job == IntPtr.Zero)
                    return;
                try
                {
                    Apply(OriginalAffinity, 0);
                    Restored = true;
                }
                finally
                {
                    CloseHandle(job);
                    job = IntPtr.Zero;
                }
            }
        }
    }
}
