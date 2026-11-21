using System;
using System.Collections;
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

[InitializeOnLoad]
public static class Turbo4744TrenchRegression
{
    private static double began;

    static Turbo4744TrenchRegression() => EditorApplication.playModeStateChanged += Mode;

    public static void Run()
    {
        SessionState.SetString("Turbo.Trench.Company", PlayerSettings.companyName);
        SessionState.SetString("Turbo.Trench.Product", PlayerSettings.productName);
        PlayerSettings.companyName = "Ninjas4744ModTests";
        PlayerSettings.productName = "TurboTrenchSandbox";
        SessionState.SetBool("Turbo.Trench.Pending", true);
        EditorSceneManager.OpenScene("Assets/Scenes/Rebuilt/Rebuilt.unity");
        Object.FindFirstObjectByType<SelectedGameManager>().SelectedGame =
            Resources.Load<GameOption>("GameOptions/Rebuilt");
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InputSetup()
    {
        if (!SessionState.GetBool("Turbo.Trench.Pending", false))
            return;
        InputSystem.settings = Object.Instantiate(InputSystem.settings);
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings
            .EditorInputBehaviorInPlayMode
            .AllDeviceInputAlwaysGoesToGameView;
        SelectedGameManager.PendingSelectedGameGuid = Resources
            .LoadAll<GameOption>("GameOptions")
            .First(g => g.GameYear == 2026)
            .GetGuid();
    }

    private static void Mode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool("Turbo.Trench.Pending", false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            began = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            PlayerSettings.companyName = SessionState.GetString(
                "Turbo.Trench.Company",
                "CascadeStudios"
            );
            PlayerSettings.productName = SessionState.GetString("Turbo.Trench.Product", "MoSim");
            SessionState.SetBool("Turbo.Trench.Pending", false);
            EditorApplication.Exit(SessionState.GetInt("Turbo.Trench.Exit", 1));
        }
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup - began < 5 || !BaseGameManager.Instance)
            return;
        EditorApplication.update -= Tick;
        var timer = BaseGameManager.Instance.GetComponent<BaseTimerManager>();
        timer.PauseTimer();
        typeof(BaseTimerManager)
            .GetProperty("CurrentRobotState")
            .SetValue(timer, RobotState.Enabled);
        foreach (var old in Object.FindObjectsByType<RobotBase>(FindObjectsSortMode.None))
            old.gameObject.SetActive(false);
        foreach (var camera in Camera.allCameras)
            camera.enabled = false;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 120;
        new GameObject("Trench contact regression").AddComponent<Turbo4744TrenchFixture>();
    }
}

public sealed class Turbo4744TrenchFixture : MonoBehaviour
{
    [Serializable]
    public sealed class Case
    {
        public int xSide,
            zSide,
            angle;
        public bool escaped,
            deployed,
            loaded,
            wheelSupportAtDrive,
            bothSidesContact,
            sideBlocked;
        public float displacement,
            maximumWallPenetration;
        public int compliantContacts;
        public List<string> contacts = new List<string>();
        public List<string> frames = new List<string>();
    }

    [Serializable]
    private sealed class Report
    {
        public bool passed;
        public bool controlledPinch,
            rigidPinch,
            baseline;
        public float physicsStep;
        public List<string> geometry = new List<string>();
        public List<Case> cases = new List<Case>();
        public List<string> errors = new List<string>();
    }

    private Turbo4744 robot;
    private AssetBundle sourceBundle;
    private Rigidbody body;
    private DriveController drive;
    private Keyboard keyboard;
    private Case current;
    private Report report = new Report();
    private Vector3 command;
    private bool driveReady;
    private float wallBegan;
    private readonly WaitForFixedUpdate step = new WaitForFixedUpdate();

    private IEnumerator Start()
    {
        report.physicsStep = Time.fixedDeltaTime;
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            if (
                !c.isTrigger
                && c.gameObject.layer == LayerMask.NameToLayer("Field")
                && Mathf.Abs(c.bounds.center.x - 3.65f) < .8f
                && c.bounds.center.z > 2.5f
            )
                report.geometry.Add(
                    c.name
                        + " "
                        + c.GetType().Name
                        + " "
                        + c.bounds
                        + " material="
                        + (c.sharedMaterial ? c.sharedMaterial.name : "default")
                );
        keyboard = InputSystem.AddDevice<Keyboard>("Turbo trench keyboard");
        bool rigidPinch = Environment.GetCommandLineArgs().Contains("-turboRigidPinch");
        bool pinch = rigidPinch || Environment.GetCommandLineArgs().Contains("-turboPinch");
        report.controlledPinch = pinch;
        report.rigidPinch = rigidPinch;
        report.baseline = Environment.GetCommandLineArgs().Contains("-turboBaseline");
        wallBegan = Time.realtimeSinceStartup;
        Application.logMessageReceived += Log;
        var fixtures = new List<GameObject>();
        if (pinch)
        {
            fixtures.Add(
                Box("Trench pinch carpet", new Vector3(10, .05f, 4), new Vector3(0, 1, 0))
            );
            // 1 mm on each opposite bumper corner at its maximum diagonal span.
            float halfGap = (.8884f + .79548f) * Mathf.Sqrt(.5f) * .5f - .001f;
            foreach (int side in rigidPinch ? Array.Empty<int>() : new[] { -1, 1 })
                fixtures.Add(
                    Box(
                        "Trench pinch side " + side,
                        new Vector3(10, .20f, .05f),
                        new Vector3(0, 1.12f, side * (halfGap + .025f))
                    )
                );
        }
        GameObject prefab;
        if (Environment.GetCommandLineArgs().Contains("-turboBundle"))
        {
            sourceBundle = AssetBundle.LoadFromFile(Path.GetFullPath(Application.dataPath
                + "/../BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64/rebuilt_ninjas_4744_turbo_2026_assets"));
            if (!sourceBundle) throw new InvalidOperationException("Built Turbo bundle missing");
            prefab = sourceBundle.LoadAsset<GameObject>(sourceBundle.GetAllAssetNames()
                .First(n => n.EndsWith("/turbo4744.prefab", StringComparison.OrdinalIgnoreCase)));
        }
        else
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744/Turbo4744.prefab");
        foreach (int xSide in new[] { -1, 1 })
        foreach (int zSide in new[] { -1, 1 })
        foreach (int angle in pinch ? new[] { 45 } : new[] { 30, -30 })
        {
            current = new Case
            {
                xSide = xSide,
                zSide = zSide,
                angle = angle,
                deployed = rigidPinch || zSide > 0,
                loaded = rigidPinch && zSide > 0,
            };
            report.cases.Add(current);
            robot = Object
                .Instantiate(
                    prefab,
                    pinch
                        ? new Vector3(0, 1.065f, 0)
                        : new Vector3(xSide * 3.65f, .04f, zSide * 3.3758f),
                    Quaternion.Euler(0, -90 + angle, 0)
                )
                .GetComponent<Turbo4744>();
            if (report.baseline)
            {
                var contacts = robot.GetComponent<Turbo4744BumperContacts>();
                if (!contacts)
                    contacts = robot.gameObject.AddComponent<Turbo4744BumperContacts>();
                contacts.enabled = false;
            }
            robot.SetAlliance(xSide < 0 ? Alliance.Red : Alliance.Blue);
            body = robot.GetComponent<Rigidbody>();
            var rigidWalls = new List<GameObject>();
            void CreateRigidPinchWalls()
            {
                // Pinch the rigid hopper/frame above the bumpers. Derive the
                // lane from the actual collision envelope at this height.
                Physics.SyncTransforms();
                var panels = robot
                    .GetComponentsInChildren<Collider>()
                    .Where(c =>
                        c.enabled
                        && !c.isTrigger
                        && !(c is WheelCollider)
                        && (c.excludeLayers.value & LayerMask.GetMask("Field")) == 0
                        && c.bounds.max.y > 1.30f
                        && c.bounds.min.y < 1.50f
                    )
                    .ToArray();
                float low = panels.Min(c => c.bounds.min.z) + .001f;
                float high = panels.Max(c => c.bounds.max.z) - .001f;
                rigidWalls.Add(
                    Box(
                        "Trench pinch side -1",
                        new Vector3(10, .20f, .05f),
                        new Vector3(0, 1.40f, low - .025f)
                    )
                );
                rigidWalls.Add(
                    Box(
                        "Trench pinch side 1",
                        new Vector3(10, .20f, .05f),
                        new Vector3(0, 1.40f, high + .025f)
                    )
                );
                // The production handler snapshots static field colliders at enable.
                var contacts = robot.GetComponent<Turbo4744BumperContacts>();
                if (!contacts)
                    contacts = robot.gameObject.AddComponent<Turbo4744BumperContacts>();
                contacts.enabled = false;
                contacts.enabled = !report.baseline;
            }
            drive = robot.GetComponent<DriveController>();
            var player = robot.GetComponent<PlayerInput>();
            player.neverAutoSwitchControlSchemes = true;
            player.actions.devices = new InputDevice[] { keyboard };
            // Sliding panels report collisions on their own bodies, rather
            // than forwarding them to the chassis.
            foreach (var part in robot.GetComponentsInChildren<Rigidbody>(true))
            {
                var witness = part.gameObject.AddComponent<Turbo4744TrenchWitness>();
                witness.record = current.contacts;
            }
            var manager = Object.FindFirstObjectByType<RebuiltFuelPreloadManager>();
            var removable = Object
                .FindObjectsByType<RebuiltGamePieceController>(FindObjectsSortMode.None)
                .Where(f => !f.transform.IsChildOf(robot.transform))
                .Take(8)
                .Select(f => f.gameObject)
                .ToArray();
            typeof(RebuiltFuelPreloadManager)
                .GetField("removableFuel", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(manager, removable);
            Object
                .FindFirstObjectByType<RebuiltSpawnInitializer>()
                .HandleSpawnedRobots(new[] { robot });
            foreach (var piece in robot.preloads)
                if (piece && !current.loaded)
                    Object.Destroy(piece);
            command = Vector3.zero;
            driveReady = false;
            yield return Wait(.5f);
            driveReady = true;
            Press(current.deployed ? Key.LeftShift : Key.Z);
            yield return Wait(2.5f);
            Press();
            if (rigidPinch)
            {
                // Size the pinch around the deployed bin after the chassis
                // settles. Its collision envelope changes during extension.
                CreateRigidPinchWalls();
                Physics.SyncTransforms();
                foreach (var part in robot.GetComponentsInChildren<Rigidbody>(true))
                    part.WakeUp();
                yield return Wait(.25f);
            }
            current.wheelSupportAtDrive = drive.IsTouchingGround;
            current.bothSidesContact =
                current.contacts.Any(c => c.Contains("Trench pinch side -1"))
                && current.contacts.Any(c => c.Contains("Trench pinch side 1"));
            // Approach through motor traction, then keep pressing into the lane.
            command = new Vector3(xSide * 1.5f, 0, 0);
            yield return Wait(1.2f);
            Vector3 stuck = body.position;
            command = new Vector3(-xSide * 1.5f, 0, 0);
            yield return Wait(1.2f);
            current.displacement = xSide * (stuck.x - body.position.x);
            current.escaped = current.displacement > .5f;
            if (pinch)
            {
                // Drive into a side after escaping. The compliant skin must
                // still stop the bumper at the authored solid wall.
                command = Vector3.forward * 1.5f;
                var bumpers = robot
                    .GetComponentsInChildren<BoxCollider>()
                    .Where(c => c.name.StartsWith("Bumper "))
                    .ToArray();
                float innerFace = (rigidPinch ? rigidWalls[1] : fixtures[2])
                    .GetComponent<Collider>()
                    .bounds.min.z;
                var wallContacts = rigidPinch
                    ? robot
                        .GetComponentsInChildren<Collider>()
                        .Where(c =>
                            c.enabled
                            && !c.isTrigger
                            && !(c is WheelCollider)
                            && (c.excludeLayers.value & LayerMask.GetMask("Field")) == 0
                            && c.bounds.max.y > 1.30f
                            && c.bounds.min.y < 1.50f
                        )
                        .ToArray()
                    : bumpers;
                for (float t = 0; t < .8f; t += Time.fixedDeltaTime)
                {
                    yield return step;
                    foreach (var bumper in wallContacts)
                        current.maximumWallPenetration = Mathf.Max(
                            current.maximumWallPenetration,
                            bumper.bounds.max.z - innerFace
                        );
                }
                current.sideBlocked = current.maximumWallPenetration < .025f;
            }
            current.compliantContacts = robot
                .GetComponent<Turbo4744BumperContacts>()
                .ModifiedContacts;
            command = Vector3.zero;
            Debug.Log("TURBO_TRENCH_CASE " + JsonUtility.ToJson(current));
            foreach (var outpost in Object.FindObjectsByType<Outpost>(FindObjectsSortMode.None))
                outpost.Robots.Remove(robot);
            robot.gameObject.SetActive(false);
            driveReady = false;
            // Spawn initialization detaches fuel from the robot hierarchy.
            // Remove this case's cohort before the next empty/loaded fixture.
            foreach (var piece in robot.preloads)
                if (piece)
                    Object.Destroy(piece);
            Object.Destroy(robot.gameObject);
            foreach (var wall in rigidWalls)
                Object.Destroy(wall);
            robot = null;
            yield return step;
        }
        InputSystem.RemoveDevice(keyboard);
        foreach (var fixture in fixtures)
            Object.Destroy(fixture);
        report.passed =
            report.errors.Count == 0
            && report.cases.All(c =>
                c.escaped
                && (
                    !pinch
                    || (
                        c.wheelSupportAtDrive
                        && c.bothSidesContact
                        && c.sideBlocked
                        && c.compliantContacts > 0
                    )
                )
            );
        string label = Environment.GetCommandLineArgs().Contains("-turboBaseline")
            ? "before"
            : "after";
        string path = Path.GetFullPath(
            Application.dataPath + "/../BuildArtifacts/trench-" + label + ".json"
        );
        File.WriteAllText(path, JsonUtility.ToJson(report, true));
        Application.logMessageReceived -= Log;
        SessionState.SetInt("Turbo.Trench.Exit", report.passed ? 0 : 1);
        EditorApplication.ExitPlaymode();
    }

    private static GameObject Box(string name, Vector3 size, Vector3 center)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = LayerMask.NameToLayer("Field");
        go.tag = "Field";
        go.transform.position = center;
        go.transform.localScale = size;
        return go;
    }

    private void FixedUpdate()
    {
        if (drive && driveReady)
            drive.OverrideInput(
                command.x,
                command.z,
                0,
                DriveController.DrivePerspective.FieldOriented
            );
    }

    private void Update()
    {
        if (wallBegan > 0 && Time.realtimeSinceStartup - wallBegan > 300)
        {
            Application.logMessageReceived -= Log;
            Debug.LogError("Trench regression timed out");
            SessionState.SetInt("Turbo.Trench.Exit", 1);
            EditorApplication.ExitPlaymode();
            enabled = false;
        }
    }

    private void Log(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception) && report.errors.Count < 15)
            report.errors.Add(message + "\n" + stack);
    }

    private IEnumerator Wait(float seconds)
    {
        int sample = 0;
        for (float t = 0; t < seconds; t += Time.fixedDeltaTime)
        {
            yield return step;
            if (body && sample++ % 22 == 0)
                current.frames.Add(
                    $"{Time.fixedTime:F3} p={body.position:F4} v={body.velocity:F3} rotation={body.rotation.eulerAngles:F2} grounded={drive.IsTouchingGround} kinematic={body.isKinematic} constraints={body.constraints}"
                );
        }
    }

    private void Press(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
    }
}

public sealed class Turbo4744TrenchWitness : MonoBehaviour
{
    public List<string> record;

    private void OnCollisionEnter(Collision collision)
    {
        foreach (var contact in collision.contacts)
        {
            string text =
                contact.thisCollider.name
                + " / "
                + contact.otherCollider.name
                + " normal="
                + contact.normal.ToString("F2");
            if (record != null && !record.Contains(text) && record.Count < 100)
                record.Add(text);
        }
    }
}
