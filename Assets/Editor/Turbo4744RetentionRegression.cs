using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
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

/// Collect field fuel, then hold it through driving, turning, disable and stow.
/// Loads the release bundle and never parents, teleports or freezes collected fuel.
[InitializeOnLoad]
public static class Turbo4744RetentionRegression
{
    [Serializable]
    private sealed class Case
    {
        public int heading,
            offered,
            collected,
            retained;
        public bool stowed,
            stable;
        public float maxRobotSpeed,
            approachSpeed,
            approachDistance,
            maxTilt,
            intakeRemaining,
            hopperRemaining;
        public List<string> escapes = new List<string>();
        public List<string> samples = new List<string>();
        public List<string> contacts = new List<string>();
    }

    [Serializable]
    private sealed class Report
    {
        public bool passed;
        public string editorRuntimeDllSha256;
        public string prefabSource,
            bundleSha256,
            runtimeDllSha256;
        public string fixture =
            "Nine field balls; held intake, release, forward/reverse, 90 deg/s turn, 60 deg/s arcs, disable, stow";
        public List<Case> cases = new List<Case>();
        public List<string> errors = new List<string>();
        public List<string> editorServiceErrors = new List<string>();
    }

    private sealed class Piece
    {
        public Rigidbody body;
        public float insideSince = -1;
        public bool collected,
            escaped;
    }

    private static bool Approach => Environment.GetCommandLineArgs().Contains("-turboApproach");
    private static bool Authoring => Environment.GetCommandLineArgs().Contains("-turboAuthoring");
    private static int[] Headings => Approach ? new[] { 0, 0, 90, 0, 90, 45 } : new[] { 0, 90 };
    private static readonly float[] ApproachSpeeds = { .5f, 2f, 2f, 4f, 4f, 2f };
    private static Vector3 approachStart;
    private static readonly string Out = Path.GetFullPath(
        Application.dataPath + "/../BuildArtifacts"
    );
    private static readonly List<Piece> fuel = new List<Piece>();
    private static Report report;
    private static Case current;
    private static Turbo4744 robot;
    private static Turbo4744BenchDriver input;
    private static BaseTimerManager timer;
    private static Keyboard keyboard;
    private static AssetBundle bundle;
    private static GameObject prefab;
    private static int caseIndex,
        lastSample;
    private static float began,
        hopperClosed,
        intakeClosed;
    private static float deploymentDelay;
    private static bool collectionStarted;
    private static float CaseTime => Time.time - began - deploymentDelay;
    private static double wallStart;
    private static string label;
    private static Turbo4744RetentionView liveView;
    private static bool Visible => Environment.GetCommandLineArgs().Contains("-turboVisible");

    static Turbo4744RetentionRegression()
    {
        EditorApplication.playModeStateChanged += Mode;
    }

    public static void Run()
    {
        SessionState.SetString(
            "Turbo.RetentionLabel",
            Environment.GetCommandLineArgs().Contains("-turboFixed") ? "after" : "before"
        );
        SessionState.SetString("Turbo.RetentionOldCompany", PlayerSettings.companyName);
        SessionState.SetString("Turbo.RetentionOldProduct", PlayerSettings.productName);
        PlayerSettings.companyName = "Ninjas4744ModTests";
        PlayerSettings.productName = "TurboRetentionSandbox";
        SessionState.SetBool("Turbo.RetentionPending", true);
        EditorSceneManager.OpenScene("Assets/Scenes/Rebuilt/Rebuilt.unity");
        Object.FindFirstObjectByType<SelectedGameManager>().SelectedGame =
            Resources.Load<GameOption>("GameOptions/Rebuilt");
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InputSetup()
    {
        if (!SessionState.GetBool("Turbo.RetentionPending", false))
            return;
        InputSystem.settings = Object.Instantiate(InputSystem.settings);
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings
            .EditorInputBehaviorInPlayMode
            .AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>("TurboRetentionKeyboard");
    }

    private static void Mode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool("Turbo.RetentionPending", false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            report = new Report();
            if (Approach)
                report.fixture =
                    "Nine field balls across the intake width; 0.5, 2 and 4 m/s approaches at four headings, braking, disable and loaded stow";
            caseIndex = 0;
            wallStart = EditorApplication.timeSinceStartup;
            robot = null;
            label = SessionState.GetString("Turbo.RetentionLabel", "before");
            EditorApplication.update += Tick;
            Application.logMessageReceived += Log;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            PlayerSettings.companyName = SessionState.GetString(
                "Turbo.RetentionOldCompany",
                "CascadeStudios"
            );
            PlayerSettings.productName = SessionState.GetString(
                "Turbo.RetentionOldProduct",
                "MoSim"
            );
            SessionState.SetBool("Turbo.RetentionPending", false);
            EditorApplication.Exit(SessionState.GetInt("Turbo.RetentionExit", 1));
        }
    }

    private static void Log(string message, string stack, LogType type)
    {
        if (
            report != null
            && Turbo4744BenchLog.RecordEditorServiceError(
                message,
                stack,
                report.editorServiceErrors
            )
        )
            return;
        if (
            robot
            && (type == LogType.Exception || type == LogType.Error)
            && report.errors.Count < 15
        )
            report.errors.Add(message + "\n" + stack);
    }

    private static void Enabled(bool enabled) =>
        typeof(BaseTimerManager)
            .GetProperty("CurrentRobotState")
            .SetValue(timer, enabled ? RobotState.Enabled : RobotState.Disabled);

    private static void Press(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
    }

    private static void Spawn()
    {
        var parent = Object.FindFirstObjectByType<RobotSpawnController>().transform;
        robot = Object
            .Instantiate(
                prefab,
                new Vector3(-1.2f, .04f, -1.5f),
                Quaternion.Euler(0, Headings[caseIndex], 0),
                parent
            )
            .GetComponent<Turbo4744>();
        robot.SetAlliance(Alliance.Blue);
        var benchPlayer = robot.GetComponent<PlayerInput>();
        benchPlayer.neverAutoSwitchControlSchemes = true;
        benchPlayer.actions.devices = new InputDevice[] { keyboard };
        input = robot.gameObject.AddComponent<Turbo4744BenchDriver>();
        input.drive = robot.GetComponent<DriveController>();
        hopperClosed = robot.hopperRail.transform.localPosition.z;
        intakeClosed = robot.intakeRail.transform.localPosition.z;
        current = new Case
        {
            heading = Headings[caseIndex],
            approachSpeed = Approach ? ApproachSpeeds[caseIndex] : .5f,
            stable = true,
        };
        approachStart = robot.transform.position;
        report.cases.Add(current);
        if (Visible)
        {
            if (!liveView)
                liveView = new GameObject(
                    "Live collection test camera"
                ).AddComponent<Turbo4744RetentionView>();
            liveView.target = robot.transform;
            liveView.GetComponent<Camera>().enabled = true;
            liveView.caption = () =>
                "Turbo — LIVE COLLECTION REGRESSION\n"
                + Phase(Time.time - began)
                + " | "
                + (Time.time - began).ToString("F1")
                + " s\nCollected "
                + current.collected
                + " / "
                + current.offered
                + " | Escapes "
                + current.escapes.Count
                + "\nControls are scripted during this check.";
            EditorApplication.ExecuteMenuItem("Window/General/Game");
        }
        var manager = Object.FindFirstObjectByType<RebuiltFuelPreloadManager>();
        var fieldFuel = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
        );
        var removable = Enumerable
            .Range(0, 8)
            .Select(i =>
                Object.Instantiate(fieldFuel, new Vector3(7, .075f, 3), Quaternion.identity)
            )
            .ToArray();
        typeof(RebuiltFuelPreloadManager)
            .GetField("removableFuel", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(manager, removable);
        Object
            .FindFirstObjectByType<RebuiltSpawnInitializer>()
            .HandleSpawnedRobots(new[] { robot });
        foreach (var preload in robot.preloads)
            Object.Destroy(preload);
        // Keep the real field geometry, but isolate the offered packet from
        // unrelated field fuel so accidental pile contacts do not steer it away.
        foreach (
            var other in Object.FindObjectsByType<RebuiltGamePieceController>(
                FindObjectsSortMode.None
            )
        )
            other.gameObject.SetActive(false);
        fuel.Clear();
        for (int row = 0; row < 3; row++)
        for (int col = -1; col <= 1; col++)
        {
            var go = Object.Instantiate(
                fieldFuel,
                robot.transform.TransformPoint(
                    new Vector3(
                        col * (Approach && caseIndex > 0 ? .25f : .16f),
                        .075f,
                        .78f + row * .22f
                    )
                ),
                Quaternion.identity
            );
            go.transform.SetParent(GameObject.FindWithTag("GamePieceWorld").transform, true);
            var piece = new Piece { body = go.GetComponent<Rigidbody>() };
            fuel.Add(piece);
            int index = fuel.Count - 1;
            go.AddComponent<TurboFuelContactAudit>().record = message =>
            {
                if (current.contacts.Count < 120)
                    current.contacts.Add(
                        (Time.time - began).ToString("F2") + " fuel " + index + " " + message
                    );
            };
        }
        current.offered = fuel.Count;
        began = Time.time;
        deploymentDelay = 0;
        collectionStarted = false;
        lastSample = -1;
        Enabled(true);
    }

    private static void Measure(float t)
    {
        var rb = robot.GetComponent<Rigidbody>();
        current.maxRobotSpeed = Mathf.Max(current.maxRobotSpeed, rb.velocity.magnitude);
        current.maxTilt = Mathf.Max(current.maxTilt, Vector3.Angle(robot.transform.up, Vector3.up));
        if (Approach && t < 7)
            current.approachDistance = Vector3.Dot(
                rb.position - approachStart,
                Quaternion.Euler(0, current.heading, 0) * Vector3.forward
            );
        current.stable &=
            float.IsFinite(rb.position.y)
            && Mathf.Abs(rb.position.y) < .2f
            && rb.velocity.magnitude < 12;
        foreach (var piece in fuel)
        {
            var p = robot.transform.InverseTransformPoint(piece.body.position);
            bool inside =
                (Mathf.Abs(p.x) < .31f && p.y > .22f && p.y < .52f && p.z > .08f && p.z < .56f)
                || Turbo4744FeedBounds.Contains(p);
            if (inside)
            {
                if (piece.insideSince < 0)
                    piece.insideSince = t;
                if (t - piece.insideSince > .2f && !piece.collected)
                {
                    piece.collected = true;
                    current.collected++;
                }
            }
            else
                piece.insideSince = -1;
            if (
                piece.collected
                && !piece.escaped
                && (Mathf.Abs(p.x) > .48f || p.y < .12f || p.y > .68f || p.z < -.2f || p.z > .8f)
            )
            {
                piece.escaped = true;
                current.escapes.Add(
                    t.ToString("F2")
                        + " fuel "
                        + fuel.IndexOf(piece)
                        + " local "
                        + p.ToString("F3")
                        + " velocity "
                        + piece.body.velocity.ToString("F3")
                        + " intake rpm "
                        + string.Join(
                            ",",
                            robot.intakeRollers.Select(r => r.realRpm.ToString("F0"))
                        )
                );
            }
        }
        int sample = (int)(t * 2);
        if (sample != lastSample)
        {
            lastSample = sample;
            current.samples.Add(
                t.ToString("F2")
                    + " robot "
                    + rb.position.ToString("F3")
                    + " intake "
                    + robot.IntakeAction.IsPressed()
                    + " collected "
                    + current.collected
            );
            if (
                robot.intakeContact
                && t > 2.5f
                && t < 7
                && Environment.GetCommandLineArgs().Contains("-turboInspect")
            )
            {
                var c = robot.intakeContact;
                var b = fuel[0].body;
                Debug.Log(
                    "TURBO_ENTRY "
                        + t.ToString("F2")
                        + " rail "
                        + robot.IntakeEncoderRotations.ToString("F2")
                        + " deployed "
                        + robot.Deployed
                        + " contact "
                        + robot.transform.InverseTransformPoint(c.transform.position).ToString("F3")
                        + " rpm "
                        + c.TargetRpm
                        + "/"
                        + c.CurrentRpm
                        + " hits "
                        + c.Contacts
                        + " ball "
                        + c.transform.InverseTransformPoint(b.position).ToString("F3")
                        + " velocity "
                        + b.velocity.ToString("F3")
                        + " captured "
                        + robot.GetComponent<Turbo4744FuelKeeper>().IsCaptured(b)
                );
            }
            foreach (var piece in fuel)
                current.samples.Add(
                    t.ToString("F2")
                        + " fuel "
                        + fuel.IndexOf(piece)
                        + " local "
                        + robot.transform.InverseTransformPoint(piece.body.position).ToString("F3")
                        + " captured "
                        + piece.collected
                );
        }
    }

    private static void Cleanup()
    {
        foreach (var outpost in Object.FindObjectsByType<Outpost>(FindObjectsSortMode.None))
            outpost.Robots.Remove(robot);
        foreach (var piece in fuel)
            if (piece.body)
            {
                piece.body.gameObject.SetActive(false);
                Object.Destroy(piece.body.gameObject);
            }
        robot.gameObject.SetActive(false);
        Object.Destroy(robot.gameObject);
        robot = null;
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

    private static string Phase(float t) =>
        t < 2.5f ? "Deploying"
        : t < 7 ? "Collecting fuel"
        : t < 10 ? "Holding fuel"
        : t < 19 ? "Driving and turning"
        : t < 21 ? "Disabled retention"
        : t < 23 ? "Stowing"
        : "Final retention";

    private static string Digest(string file)
    {
        using (var stream = File.OpenRead(file))
        using (var hash = SHA256.Create())
            return BitConverter
                .ToString(hash.ComputeHash(stream))
                .Replace("-", "")
                .ToLowerInvariant();
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - wallStart > (Approach ? 480 : 300))
                throw new TimeoutException("Collection regression exceeded its time budget");
            if (!robot)
            {
                if (EditorApplication.timeSinceStartup - wallStart < 5 || !BaseGameManager.Instance)
                    return;
                timer = BaseGameManager.Instance.GetComponent<BaseTimerManager>();
                timer.PauseTimer();
                foreach (var camera in Camera.allCameras)
                    camera.enabled = false;
                foreach (var old in Object.FindObjectsByType<RobotBase>(FindObjectsSortMode.None))
                    old.gameObject.SetActive(false);
                if (Authoring)
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Ninjas4744Builder.RobotPath);
                    report.prefabSource = Ninjas4744Builder.RobotPath;
                    Spawn();
                    return;
                }
                string file = Path.Combine(
                    Application.dataPath,
                    "../BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64/rebuilt_ninjas_4744_turbo_2026_assets"
                );
                bundle = AssetBundle.LoadFromFile(file);
                report.bundleSha256 = Digest(file);
                report.runtimeDllSha256 = Digest(
                    Path.Combine(Path.GetDirectoryName(file), "Ninjas4744.dll")
                );
                report.editorRuntimeDllSha256 = Digest(typeof(Turbo4744).Assembly.Location);
                Turbo4744NetBurstBuilder.VerifyRuntime();
                if (report.runtimeDllSha256 != Digest(Turbo4744NetBurstBuilder.PlayerAssembly))
                    throw new InvalidOperationException(
                        "Built mod DLL differs from the runtime being tested. Build again."
                    );
                prefab = bundle
                    .LoadAllAssets<BaseModpackSO>()
                    .Single(p => p.ModpackName == "Ninjas 4744 Turbo 2026")
                    .Robots.Single()
                    .AlternateRobots.Single(v => v.DisplayName == "Actual Turbo")
                    .RobotPrefab;
                report.prefabSource = file;
                Spawn();
                return;
            }
            float t = CaseTime;
            if (!collectionStarted && t >= 2.5f)
            {
                var kicker = robot.GetComponent<Turbo4744KickerRig>();
                if (robot.IntakeEncoderRotations < 10.5f || (kicker && kicker.Deployment < .99f))
                {
                    deploymentDelay = Time.time - began - 2.5f;
                    Press(Key.LeftShift);
                    Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
                    return;
                }
                collectionStarted = true;
            }
            Measure(t);
            if (Approach)
            {
                TickApproach(t);
                return;
            }
            var drive = robot.GetComponent<DriveController>();
            if (t < 1)
            {
                Press();
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 2.5f)
            {
                Press(Key.LeftShift);
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 7)
            {
                Press(Key.LeftShift);
                Drive(.5f, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 10)
            {
                Press(Key.LeftShift);
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 10.5f)
            {
                Press();
                Drive(.8f, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 11.5f)
            {
                Press();
                Drive(-.8f, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 13)
            {
                Press();
                Drive(0, 0, 90f, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 16)
            {
                Press();
                Drive(.3f, 0, 60f, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 19)
            {
                Press();
                Drive(0, .35f, -60f, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 21)
            {
                Press();
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
                Enabled(false);
            }
            else if (t < 23)
            {
                Enabled(true);
                Press(Key.Z);
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 25)
            {
                Press();
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else
            {
                current.stowed =
                    Mathf.Abs(robot.intakeRail.transform.localPosition.z - intakeClosed) < .035f
                    && Mathf.Abs(robot.hopperRail.transform.localPosition.z - hopperClosed) < .035f;
                current.retained = fuel.Count(piece =>
                    piece.collected
                    && !piece.escaped
                    && InsideClosed(robot.transform.InverseTransformPoint(piece.body.position))
                );
                Debug.Log("TURBO_RETENTION_CASE " + JsonUtility.ToJson(current));
                Press();
                Cleanup();
                caseIndex++;
                if (
                    caseIndex < Headings.Length
                    && !Environment.GetCommandLineArgs().Contains("-turboInspect")
                )
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

    private static bool InsideClosed(Vector3 p)
    {
        var panel = robot.GetComponent<Turbo4744FuelKeeper>().frontPanel;
        float front = robot
            .transform.InverseTransformPoint(panel.transform.TransformPoint(panel.center))
            .z;
        return (
                Mathf.Abs(p.x) < .36f
                && p.y > .17f
                && p.y < .55f
                && p.z > .03f
                && p.z < front - .065f
            ) || Turbo4744FeedBounds.Contains(p);
    }

    private static void TickApproach(float t)
    {
        // Match retention: enable bindings during a neutral first second, then
        // press intake. A key held before Init can lose its performed event.
        Press(
            t < 1 ? Array.Empty<Key>()
            : t < 8 ? new[] { Key.LeftShift }
            : t >= 9 ? new[] { Key.Z }
            : Array.Empty<Key>()
        );
        Enabled(t < 8 || t >= 9);
        Drive(
            t >= 2.5f && t < 7 && current.approachDistance < 1.35f ? current.approachSpeed : 0,
            0,
            0,
            DriveController.DrivePerspective.RobotRelative
        );
        if (t < 13)
            return;
        current.intakeRemaining = Mathf.Abs(
            robot.intakeRail.transform.localPosition.z - intakeClosed
        );
        current.hopperRemaining = Mathf.Abs(
            robot.hopperRail.transform.localPosition.z - hopperClosed
        );
        current.stowed = current.intakeRemaining < .035f && current.hopperRemaining < .035f;
        current.retained = fuel.Count(piece =>
            piece.collected
            && !piece.escaped
            && InsideClosed(robot.transform.InverseTransformPoint(piece.body.position))
        );
        Debug.Log(
            $"TURBO_APPROACH_CASE {current.heading} deg {current.approachSpeed:F1} m/s: {current.collected}/{current.offered} collected, {current.retained} retained, {current.maxTilt:F2} deg tilt"
        );
        Press();
        Cleanup();
        caseIndex++;
        if (caseIndex < Headings.Length)
            Spawn();
        else
            Finish();
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Log;
        Press();
        report.passed =
            report.cases.Count == Headings.Length
            && report.cases.All(c =>
                c.collected >= (Approach ? c.offered : 7)
                && c.retained == c.collected
                && c.escapes.Count == 0
                && c.stowed
                && c.stable
                && (!Approach || c.approachDistance >= 1.1f && c.maxTilt < 12)
            )
            && report.errors.Count == 0;
        Directory.CreateDirectory(Out);
        File.WriteAllText(
            Out + (Approach ? "/approach-" : "/retention-") + label + ".json",
            JsonUtility.ToJson(report, true)
        );
        SessionState.SetInt("Turbo.RetentionExit", report.passed ? 0 : 1);
        EditorApplication.ExitPlaymode();
    }
}

public sealed class Turbo4744RetentionView : MonoBehaviour
{
    public Transform target;
    public Func<string> caption;
    private Camera view;

    private void Awake()
    {
        view = gameObject.AddComponent<Camera>();
        view.depth = 1000;
        view.fieldOfView = 45;
        view.nearClipPlane = .03f;
    }

    private void LateUpdate()
    {
        if (!target)
            return;
        transform.position = target.TransformPoint(new Vector3(1.35f, 1.05f, 1.65f));
        transform.LookAt(target.TransformPoint(new Vector3(0, .22f, .25f)));
    }

    private void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 19,
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(12, 12, 10, 10),
        };
        GUI.Box(
            new Rect(14, 14, 570, 125),
            caption?.Invoke() ?? "Starting collection check",
            style
        );
    }
}
