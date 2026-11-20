using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Games.Rebuilt.FieldScripts;
using Games.Rebuilt.GameManagement;
using Games.Rebuilt.GamePieceSystem;
using GameSystems.Management;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.BaseClasses.GameManagement.TimerManagement;
using MoSimCore.Enums;
using MoSimCore.GameSelection;
using Ninjas4744.MoSim;
using RobotFramework;
using RobotFramework.Controllers.Drivetrain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// Reproduces travel from the shipped asset bundle, at real match headings,
/// through the game spawn handler, with disabled/reenabled match transitions.
[InitializeOnLoad]
public static class Turbo4744TravelRegression
{
    [Serializable]
    private sealed class Case
    {
        public int heading;
        public bool bounded,
            stowed,
            spawnedRealFuel,
            physicalLimits,
            impactApplied;
        public float maxIntake,
            maxHopper,
            minIntake,
            minHopper,
            maxLocalLateral,
            maxLocalRotation,
            maxRobotSpeed;
        public List<string> samples = new List<string>();
        public List<string> impactFrames = new List<string>();
    }

    [Serializable]
    private sealed class Report
    {
        public bool passed;
        public string prefabSource;
        public List<Case> cases = new List<Case>();
        public List<string> errors = new List<string>();
    }

    private static readonly int[] Headings = { 0, 90, 180, -90 };
    private static Report report;
    private static Case current;
    private static Turbo4744 robot;
    private static Turbo4744BenchDriver input;
    private static BaseTimerManager timer;
    private static Keyboard keyboard;
    private static AssetBundle bundle;
    private static GameObject prefab;
    private static Vector3 intakeClosed,
        hopperClosed;
    private static int caseIndex,
        phase,
        lastSample;
    private static float began;
    private static double wallStart;
    private static string label;
    private static float lastImpactSample = -1;
    private static readonly string Out = Path.GetFullPath(
        Application.dataPath + "/../BuildArtifacts"
    );

    static Turbo4744TravelRegression()
    {
        EditorApplication.playModeStateChanged += Mode;
    }

    public static void Run()
    {
        label = Environment.GetCommandLineArgs().Contains("-turboFixed") ? "after" : "before";
        SessionState.SetString("Turbo.TravelLabel", label);
        SessionState.SetString("Turbo.TravelOldCompany", PlayerSettings.companyName);
        SessionState.SetString("Turbo.TravelOldProduct", PlayerSettings.productName);
        PlayerSettings.companyName = "Ninjas4744ModTests";
        PlayerSettings.productName = "TurboTravelSandbox";
        SessionState.SetBool("Turbo.TravelPending", true);
        EditorSceneManager.OpenScene("Assets/Scenes/Rebuilt/Rebuilt.unity");
        Object.FindFirstObjectByType<SelectedGameManager>().SelectedGame =
            Resources.Load<GameOption>("GameOptions/Rebuilt");
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InputSetup()
    {
        if (!SessionState.GetBool("Turbo.TravelPending", false))
            return;
        InputSystem.settings = Object.Instantiate(InputSystem.settings);
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings
            .EditorInputBehaviorInPlayMode
            .AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>("TurboTravelKeyboard");
    }

    private static void Mode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool("Turbo.TravelPending", false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            report = new Report();
            caseIndex = 0;
            phase = -1;
            wallStart = EditorApplication.timeSinceStartup;
            label = SessionState.GetString("Turbo.TravelLabel", "before");
            EditorApplication.update += Tick;
            Application.logMessageReceived += Log;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            PlayerSettings.companyName = SessionState.GetString(
                "Turbo.TravelOldCompany",
                "CascadeStudios"
            );
            PlayerSettings.productName = SessionState.GetString("Turbo.TravelOldProduct", "MoSim");
            SessionState.SetBool("Turbo.TravelPending", false);
            EditorApplication.Exit(SessionState.GetInt("Turbo.TravelExit", 1));
        }
    }

    private static void Log(string message, string stack, LogType type)
    {
        if (
            (type == LogType.Exception || type == LogType.Error)
            && phase >= 0
            && report.errors.Count < 15
        )
            report.errors.Add(message + "\n" + stack);
    }

    private static void Enabled(bool enabled)
    {
        typeof(BaseTimerManager)
            .GetProperty("CurrentRobotState")
            .SetValue(timer, enabled ? RobotState.Enabled : RobotState.Disabled);
    }

    private static void Press(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
    }

    private static void Spawn()
    {
        var parent = Object.FindFirstObjectByType<RobotSpawnController>().transform;
        var instance = Object.Instantiate(
            prefab,
            new Vector3(-6f, .04f, -1.5f),
            Quaternion.Euler(0, Headings[caseIndex], 0),
            parent
        );
        robot = instance.GetComponent<Turbo4744>();
        robot.SetAlliance(Alliance.Blue);
        input = robot.gameObject.AddComponent<Turbo4744BenchDriver>();
        input.drive = robot.GetComponent<DriveController>();
        intakeClosed = robot.intakeRail.transform.localPosition;
        hopperClosed = robot.hopperRail.transform.localPosition;
        current = new Case { heading = Headings[caseIndex], bounded = true };
        report.cases.Add(current);
        lastImpactSample = -1;
        current.physicalLimits =
            robot.intakeRail.GetComponent<ConfigurableJoint>().zMotion
                == ConfigurableJointMotion.Limited
            && robot.hopperRail.GetComponent<ConfigurableJoint>().zMotion
                == ConfigurableJointMotion.Limited;
        current.spawnedRealFuel =
            robot.preloads.Length == 8
            && robot.preloads.All(p => p.GetComponent<Rigidbody>() != null);
        var manager = Object.FindFirstObjectByType<RebuiltFuelPreloadManager>();
        var removable = Object
            .FindObjectsByType<RebuiltGamePieceController>(FindObjectsSortMode.None)
            .Where(f => f.transform.position.x > 0)
            .Take(8)
            .Select(f => f.gameObject)
            .ToArray();
        typeof(RebuiltFuelPreloadManager)
            .GetField("removableFuel", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(manager, removable);
        Object
            .FindFirstObjectByType<RebuiltSpawnInitializer>()
            .HandleSpawnedRobots(new[] { robot });
        began = Time.time;
        phase = 0;
        lastSample = -1;
        Enabled(true);
        var expected = robot;
        var probe = instance.AddComponent<Turbo4744TravelProbe>();
        probe.Measure = () =>
        {
            if (robot == expected)
                Measure();
        };
        probe.ApplyImpact = () =>
        {
            if (robot != expected)
                return;
            // Equal-and-opposite actuator impulse tests the physical stops,
            // rather than relying solely on a healthy position controller.
            Vector3 impulse = robot.transform.forward * 40f;
            robot.intakeRail.GetComponent<Rigidbody>().AddForce(impulse, ForceMode.Impulse);
            robot.GetComponent<Rigidbody>().AddForce(-impulse, ForceMode.Impulse);
            current.impactApplied = true;
        };
        probe.Begin = Time.time;
    }

    private static void Measure()
    {
        if (!robot || current == null)
            return;
        var rb = robot.GetComponent<Rigidbody>();
        Vector3 i =
            robot.transform.InverseTransformPoint(
                robot.intakeRail.GetComponent<Rigidbody>().position
            ) - intakeClosed;
        Vector3 h =
            robot.transform.InverseTransformPoint(
                robot.hopperRail.GetComponent<Rigidbody>().position
            ) - hopperClosed;
        float angle = Mathf.Max(
            Quaternion.Angle(robot.intakeRail.transform.rotation, robot.transform.rotation),
            Quaternion.Angle(robot.hopperRail.transform.rotation, robot.transform.rotation)
        );
        current.maxIntake = Mathf.Max(current.maxIntake, i.z);
        current.minIntake = Mathf.Min(current.minIntake, i.z);
        current.maxHopper = Mathf.Max(current.maxHopper, h.z);
        current.minHopper = Mathf.Min(current.minHopper, h.z);
        current.maxLocalLateral = Mathf.Max(
            current.maxLocalLateral,
            Mathf.Abs(i.x),
            Mathf.Abs(h.x),
            Mathf.Abs(i.y),
            Mathf.Abs(h.y)
        );
        current.maxLocalRotation = Mathf.Max(current.maxLocalRotation, angle);
        current.maxRobotSpeed = Mathf.Max(current.maxRobotSpeed, rb.velocity.magnitude);
        float elapsed = Time.time - began;
        if (elapsed > 7.49f && elapsed < 7.58f && Time.fixedTime != lastImpactSample)
        {
            lastImpactSample = Time.fixedTime;
            float physical = (
                Quaternion.Inverse(rb.rotation)
                    * (robot.intakeRail.GetComponent<Rigidbody>().position - rb.position)
                - intakeClosed
            ).z;
            string trace =
                elapsed.ToString("F5")
                + " transform "
                + i.z.ToString("F6")
                + " physical "
                + physical.ToString("F6")
                + " velocity "
                + robot.intakeRail.GetComponent<Rigidbody>().velocity.ToString("F3")
                + " interpolation "
                + rb.interpolation;
            current.impactFrames.Add(trace);
            if (Environment.GetCommandLineArgs().Contains("-turboInspect"))
                Debug.Log("TURBO_LIMIT " + trace);
        }
        current.bounded &=
            i.z < .245f
            && i.z > -.005f
            && h.z < .235f
            && h.z > -.005f
            && current.maxLocalLateral < .015f
            && angle < 3f;
    }

    private static void Cleanup()
    {
        foreach (var outpost in Object.FindObjectsByType<Outpost>(FindObjectsSortMode.None))
            outpost.Robots.Remove(robot);
        foreach (var fuel in robot.preloads)
            if (fuel)
            {
                fuel.SetActive(false);
                Object.Destroy(fuel);
            }
        robot.gameObject.SetActive(false);
        Object.Destroy(robot.gameObject);
    }

    private static void Drive(
        float x,
        float y,
        float omega,
        DriveController.DrivePerspective perspective
    )
    {
        input.vx = x;
        input.vy = y;
        input.omega = omega;
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - wallStart > 300)
                throw new TimeoutException("Travel regression exceeded 300 seconds");
            if (phase < 0)
            {
                if (EditorApplication.timeSinceStartup - wallStart < 5 || !BaseGameManager.Instance)
                    return;
                timer = BaseGameManager.Instance.GetComponent<BaseTimerManager>();
                timer.PauseTimer();
                foreach (var camera in Camera.allCameras)
                    camera.enabled = false;
                foreach (var old in Object.FindObjectsByType<RobotBase>(FindObjectsSortMode.None))
                    old.gameObject.SetActive(false);
                string file = Path.Combine(
                    Application.dataPath,
                    "../BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64/rebuilt_ninjas_4744_turbo_2026_assets"
                );
                bundle = AssetBundle.LoadFromFile(file);
                var pack = bundle
                    .LoadAllAssets<BaseModpackSO>()
                    .Single(p => p.ModpackName == "Ninjas 4744 Turbo 2026");
                prefab = pack.Robots.Single().RobotPrefab;
                report.prefabSource = file;
                Spawn();
                return;
            }
            float t = Time.time - began;
            var rb = robot.GetComponent<Rigidbody>();
            Vector3 i =
                robot.transform.InverseTransformPoint(
                    robot.intakeRail.GetComponent<Rigidbody>().position
                ) - intakeClosed;
            Vector3 h =
                robot.transform.InverseTransformPoint(
                    robot.hopperRail.GetComponent<Rigidbody>().position
                ) - hopperClosed;
            float angle = Mathf.Max(
                Quaternion.Angle(robot.intakeRail.transform.rotation, robot.transform.rotation),
                Quaternion.Angle(robot.hopperRail.transform.rotation, robot.transform.rotation)
            );
            current.maxIntake = Mathf.Max(current.maxIntake, i.z);
            current.minIntake = Mathf.Min(current.minIntake, i.z);
            current.maxHopper = Mathf.Max(current.maxHopper, h.z);
            current.minHopper = Mathf.Min(current.minHopper, h.z);
            current.maxLocalLateral = Mathf.Max(
                current.maxLocalLateral,
                Mathf.Abs(i.x),
                Mathf.Abs(h.x),
                Mathf.Abs(i.y),
                Mathf.Abs(h.y)
            );
            current.maxLocalRotation = Mathf.Max(current.maxLocalRotation, angle);
            current.maxRobotSpeed = Mathf.Max(current.maxRobotSpeed, rb.velocity.magnitude);
            Measure();
            int sample = (int)(t * 2);
            if (sample != lastSample)
            {
                lastSample = sample;
                current.samples.Add(
                    t.ToString("F2")
                        + " intake "
                        + i.ToString("F4")
                        + " hopper "
                        + h.ToString("F4")
                        + " body "
                        + rb.position.ToString("F3")
                        + " yaw "
                        + rb.rotation.eulerAngles.y.ToString("F1")
                );
            }
            var drive = robot.GetComponent<DriveController>();
            if (t < 1)
            {
                Press();
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 8)
            {
                Press(Key.LeftShift);
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 11)
            {
                Press(Key.LeftShift);
                Drive(.6f, 0, 1f, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 12)
            {
                Press();
                Enabled(false);
            }
            else if (t < 15)
            {
                Enabled(true);
                Press(Key.LeftShift);
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 17)
            {
                Press(Key.Z);
            }
            else
            {
                current.stowed = Mathf.Abs(i.z) < .035f && Mathf.Abs(h.z) < .035f;
                Press();
                Cleanup();
                Debug.Log("TURBO_TRAVEL_CASE " + JsonUtility.ToJson(current));
                caseIndex++;
                if (
                    caseIndex < Headings.Length
                    && !Environment.GetCommandLineArgs().Contains("-turboInspect")
                )
                    Spawn();
                else
                    Finish();
            }
            if (i.magnitude > 3 || h.magnitude > 3 || !float.IsFinite(i.z))
            {
                current.bounded = false;
                current.samples.Add("Runaway threshold reached");
                Press();
                Cleanup();
                caseIndex++;
                if (caseIndex < Headings.Length)
                    Spawn();
                else
                    Finish();
            }
        }
        catch (Exception error)
        {
            report.errors.Add(error.ToString());
            Finish();
        }
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Log;
        Press();
        report.passed =
            report.cases.Count == 4
            && report.cases.All(c =>
                c.bounded && c.stowed && c.spawnedRealFuel && c.physicalLimits && c.impactApplied
            )
            && report.errors.Count == 0;
        Directory.CreateDirectory(Out);
        File.WriteAllText(Out + "/travel-" + label + ".json", JsonUtility.ToJson(report, true));
        SessionState.SetInt("Turbo.TravelExit", report.passed ? 0 : 1);
        EditorApplication.ExitPlaymode();
    }
}

[DefaultExecutionOrder(20000)]
public sealed class Turbo4744TravelProbe : MonoBehaviour
{
    public Action Measure,
        ApplyImpact;
    public float Begin;
    private bool applied;

    private void FixedUpdate()
    {
        Measure?.Invoke();
        if (!applied && Time.time - Begin > 7.5f)
        {
            applied = true;
            ApplyImpact?.Invoke();
        }
    }
}
