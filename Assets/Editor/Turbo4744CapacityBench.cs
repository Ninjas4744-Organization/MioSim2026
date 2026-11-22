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
public static class Turbo4744CapacityBench
{
    [Serializable]
    private sealed class Case
    {
        public int heading,
            offered,
            collected,
            retained;
        public int retainedAfterDrive,
            released;
        public bool stowed,
            stable,
            wheelSweepReached;
        public float maxRobotSpeed,
            maxLoadedNativeSpeed,
            loadedSpeedPeakAt,
            loadedForwardKeySeconds,
            loadedReverseKeySeconds,
            maxTilt,
            maxCollectionYaw,
            collectionYawPeakAt,
            collectionSpeedAtYawPeak,
            maxSettlingYaw,
            maxCrossTrack,
            collectionDistance,
            netPeakHeight;
        public int storedBeforeDrive;
        public bool measuredBeforeDrive;
        public List<string> escapes = new List<string>();
        public List<string> samples = new List<string>();
        public List<string> contacts = new List<string>();
        public List<string> obstacleContacts = new List<string>();
    }

    [Serializable]
    private sealed class Report
    {
        public bool passed;
        public string prefabSource,
            bundleSha256,
            runtimeDllSha256;
        public string editorRuntimeDllSha256;
        public float collectionEndSeconds;
        public string fixture =
            "Thirty ground balls; collection, native keyboard loaded drive, turning, disable, up to 25 seconds delivery through pumping, then empty stow";
        public List<Case> cases = new List<Case>();
        public float intakeForceOverride,
            intakeLateralOverride = -1;
        public float intakeTractionOverride;
        public float driveFrictionOverride;
        public int capacityCaptureTarget;
        public List<string> errors = new List<string>();
        public List<string> editorServiceErrors = new List<string>();
    }

    private sealed class Piece
    {
        public Rigidbody body;
        public float insideSince = -1;
        public bool collected,
            escaped,
            released;
    }

    private static readonly int[] Headings = Environment
        .GetCommandLineArgs()
        .Contains("-turboHeading90")
        ? new[] { 90 }
        : new[] { 0, 90 };
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
    private static double wallStart;
    private static string label;
    private static Turbo4744CapacityView liveView;
    private static bool Visible => Environment.GetCommandLineArgs().Contains("-turboVisible");
    private static bool Pile => Environment.GetCommandLineArgs().Contains("-turboPile");
    private static bool Authoring => Environment.GetCommandLineArgs().Contains("-turboAuthoring");
    private static bool LoadOnly => Environment.GetCommandLineArgs().Contains("-turboLoadOnly");
    private static bool WheelPile => Environment.GetCommandLineArgs().Contains("-turboWheelPile");
    private static bool LoadedAim => Environment.GetCommandLineArgs().Contains("-turboLoadedAim");
    private static float IntakeForceOverride
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-turboIntakeForce");
            return at >= 0 && at + 1 < args.Length
                ? float.Parse(args[at + 1], System.Globalization.CultureInfo.InvariantCulture)
                : 0;
        }
    }
    private static float IntakeLateralOverride
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-turboIntakeLateral");
            return at >= 0 && at + 1 < args.Length
                ? float.Parse(args[at + 1], System.Globalization.CultureInfo.InvariantCulture)
                : -1;
        }
    }
    private static float IntakeTractionOverride
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-turboIntakeTraction");
            return at >= 0 && at + 1 < args.Length
                ? float.Parse(args[at + 1], System.Globalization.CultureInfo.InvariantCulture)
                : 0;
        }
    }
    private static int CapacityCaptureTarget
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-turboCapacityTarget");
            return at >= 0 && at + 1 < args.Length ? int.Parse(args[at + 1]) : 51;
        }
    }
    private static float DriveFrictionOverride
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-turboDriveFriction");
            return at >= 0 && at + 1 < args.Length
                ? float.Parse(args[at + 1], System.Globalization.CultureInfo.InvariantCulture)
                : 0;
        }
    }
    private static float FillExtraSeconds
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-turboFillExtraSeconds");
            return at >= 0 && at + 1 < args.Length
                ? Mathf.Max(
                    0,
                    float.Parse(args[at + 1], System.Globalization.CultureInfo.InvariantCulture)
                )
                : 0;
        }
    }
    private static float CycleOffset => Pile && !WheelPile ? 6 + FillExtraSeconds : 0;
    private static float CollectionEnd =>
        Pile && !WheelPile ? 14 + FillExtraSeconds
        // Finish the existing 3.4 m wheel-path target before loaded driving.
        // Dense fuel can slow the native drivetrain; the old eight-second
        // cutoff could end the commanded sweep before its distance target.
        : WheelPile ? 10
        : 7;
    private static Vector3 collectionStart;
    private static Quaternion collectionHeading;
    private static float lastCapacityOffer;
    private static float deploymentDelay;
    private static bool collectionStarted;
    private static float CaseTime => Time.time - began - deploymentDelay;

    static Turbo4744CapacityBench()
    {
        EditorApplication.playModeStateChanged += Mode;
    }

    public static void Run()
    {
        SessionState.SetString(
            "Turbo.CapacityLabel",
            Environment.GetCommandLineArgs().Contains("-turboFixed") ? "after" : "before"
        );
        SessionState.SetString("Turbo.CapacityOldCompany", PlayerSettings.companyName);
        SessionState.SetString("Turbo.CapacityOldProduct", PlayerSettings.productName);
        PlayerSettings.companyName = "Ninjas4744ModTests";
        PlayerSettings.productName = "TurboRetentionSandbox";
        SessionState.SetBool("Turbo.CapacityPending", true);
        EditorSceneManager.OpenScene("Assets/Scenes/Rebuilt/Rebuilt.unity");
        Object.FindFirstObjectByType<SelectedGameManager>().SelectedGame =
            Resources.Load<GameOption>("GameOptions/Rebuilt");
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InputSetup()
    {
        if (!SessionState.GetBool("Turbo.CapacityPending", false))
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
        if (!SessionState.GetBool("Turbo.CapacityPending", false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            report = new Report();
            report.intakeForceOverride = IntakeForceOverride;
            report.intakeLateralOverride = IntakeLateralOverride;
            report.intakeTractionOverride = IntakeTractionOverride;
            report.driveFrictionOverride = DriveFrictionOverride;
            report.capacityCaptureTarget = CapacityCaptureTarget;
            report.collectionEndSeconds = CollectionEnd;
            if (Pile)
                report.fixture =
                    $"Capacity fill: up to 64 official ground balls introduced individually at the pickup, with clearance between offers; stop offering after {CapacityCaptureTarget} captured, allowing the remaining incoming fuel to settle. At least 50 stored at each heading and at least one load above 50, then full-speed native loaded driving, turns, disable, complete shooter release and empty stow. Dense-pile acquisition is verified separately.";
            if (WheelPile)
                report.fixture =
                    "Ninety ground balls across six lanes including wheel paths; 3 m/s approach, under 3 degrees of heading drift and 0.20 m cross-track error, retain captured fuel through native loaded driving and turns, complete shooter release and empty stow; capacity is verified separately";
            if (LoadedAim)
                report.fixture =
                    "Collect over 50 official fuel bodies, keep the collection pose, then the normal AutoShoot input with automatic aiming, feed and pumping. No manual-shoot override. Record fixed-step pitch/roll throughout and require full release without overturning.";
            if (LoadOnly)
                report.fixture =
                    "Static capacity stage: up to 64 official ground balls introduced individually at the pickup, stopping after "
                    + CapacityCaptureTarget
                    + " captured; settle and retain the collected load; no shooter assertions in this stage";
            caseIndex = 0;
            wallStart = EditorApplication.timeSinceStartup;
            robot = null;
            label = SessionState.GetString("Turbo.CapacityLabel", "before");
            EditorApplication.update += Tick;
            Application.logMessageReceived += Log;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            PlayerSettings.companyName = SessionState.GetString(
                "Turbo.CapacityOldCompany",
                "CascadeStudios"
            );
            PlayerSettings.productName = SessionState.GetString(
                "Turbo.CapacityOldProduct",
                "MoSim"
            );
            SessionState.SetBool("Turbo.CapacityPending", false);
            EditorApplication.Exit(SessionState.GetInt("Turbo.CapacityExit", 1));
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
        var loadedAimPosition = Vector3.zero;
        if (LoadedAim)
        {
            var hub = Object
                .FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .First(t =>
                    t.gameObject.layer == LayerMask.NameToLayer("AutoAngleNodes")
                    && t.name.ToLower().Contains("blue")
                    && t.name.ToLower().Contains("hub")
                );
            loadedAimPosition = hub.position + new Vector3(2.2f, 0, 2.4f);
            loadedAimPosition.y = .04f;
        }
        robot = Object
            .Instantiate(
                prefab,
                LoadedAim ? loadedAimPosition
                    : Pile
                        ? WheelPile
                                ? Headings[caseIndex] == 0 ? new Vector3(-1.2f, .04f, -3f)
                                    : new Vector3(-2.5f, .04f, 0)
                            : Headings[caseIndex] == 0 ? new Vector3(-1.2f, .04f, .5f)
                            : new Vector3(1f, .04f, 0)
                    // Leave room for collection travel and the subsequent full-speed
                    // braking sequence. The former 0-degree origin hit Side2 with
                    // over 220 N.s, turning this capacity check into a wall impact.
                    : Headings[caseIndex] == 0 ? new Vector3(-1.2f, .04f, -3f)
                    : new Vector3(-1.2f, .04f, -1.5f),
                Quaternion.Euler(0, Headings[caseIndex] + (LoadedAim ? 135 : 0), 0),
                parent
            )
            .GetComponent<Turbo4744>();
        robot.SetAlliance(Alliance.Blue);
        if (IntakeForceOverride > 0)
            robot.intakeContact.maxTotalForce = IntakeForceOverride;
        if (IntakeLateralOverride >= 0)
            robot.intakeContact.lateralResponse = IntakeLateralOverride;
        if (IntakeTractionOverride > 0)
            robot.intakeContact.maxTotalTractionForce = IntakeTractionOverride;
        var benchPlayer = robot.GetComponent<PlayerInput>();
        benchPlayer.neverAutoSwitchControlSchemes = true;
        benchPlayer.actions.devices = new InputDevice[] { keyboard };
        input = robot.gameObject.AddComponent<Turbo4744BenchDriver>();
        input.drive = robot.GetComponent<DriveController>();
        if (DriveFrictionOverride > 0)
            typeof(DriveController)
                .GetField("frictionCoefficient", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(input.drive, DriveFrictionOverride);
        hopperClosed = robot.hopperRail.transform.localPosition.z;
        intakeClosed = robot.intakeRail.transform.localPosition.z;
        current = new Case { heading = Headings[caseIndex] + (LoadedAim ? 135 : 0), stable = true };
        collectionStart = robot.transform.position;
        collectionHeading = robot.transform.rotation;
        robot.gameObject.AddComponent<Turbo4744PileMotionProbe>().sample = () =>
            MeasureMotion(CaseTime);
        report.cases.Add(current);
        if (Visible)
        {
            if (!liveView)
                liveView = new GameObject(
                    "Live collection test camera"
                ).AddComponent<Turbo4744CapacityView>();
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
        foreach (var moving in robot.GetComponentsInChildren<Rigidbody>())
        {
            var observedBody = moving;
            moving.gameObject.AddComponent<Turbo4744GuideProbe>().collided = contact =>
            {
                float t = Time.time - began - CycleOffset;
                if (
                    t < 9.8f
                    || t > 12
                    || contact.collider.name == "Carpet"
                    || contact.collider.GetComponentInParent<RebuiltGamePieceController>()
                    || current.obstacleContacts.Count >= 500
                    || contact.impulse.sqrMagnitude < .0025f
                )
                    return;
                current.obstacleContacts.Add(
                    $"{t:F4} body={observedBody.name} hit={contact.collider.name} p={observedBody.position:F3} impulse={contact.impulse:F3}"
                );
            };
        }
        lastCapacityOffer = -1;
        for (
            int row = 0;
            row
                < (
                    WheelPile ? 15
                    : Pile ? 0
                    : 10
                );
            row++
        )
        for (int col = 0; col < (WheelPile ? 6 : 3); col++)
        {
            var go = Object.Instantiate(
                fieldFuel,
                robot.transform.TransformPoint(
                    new Vector3((col - (WheelPile ? 2.5f : 1f)) * .16f, .075f, .78f + row * .16f)
                ),
                Quaternion.identity
            );
            go.transform.SetParent(GameObject.FindWithTag("GamePieceWorld").transform, true);
            var piece = new Piece { body = go.GetComponent<Rigidbody>() };
            fuel.Add(piece);
            int index = fuel.Count - 1;
            go.AddComponent<TurboFuelContactAudit>().record = message =>
            {
                if (current.contacts.Count < 400 && !message.StartsWith("Carpet"))
                    current.contacts.Add(
                        (Time.time - began).ToString("F2") + " fuel " + index + " " + message
                    );
            };
        }
        current.offered = fuel.Count;
        began = Time.time;
        robot.gameObject.AddComponent<Turbo4744CapacityInputProbe>().sample = () =>
        {
            // Keep the short native-keyboard acceleration/reversal pulses on
            // physics time. Editor frames can otherwise shorten either pulse
            // while the speed acceptance window still advances normally.
            float rawTime = CaseTime;
            float t = rawTime - CycleOffset;
            if (LoadedAim || rawTime < CollectionEnd || t < 9.5f || t >= 12)
                return;
            if (t < 10)
            {
                Press(Key.LeftShift);
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 11.5f)
            {
                Press(t < 10.5f ? Key.W : Key.S);
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else
            {
                Press();
                Drive(0, 0, 90f, DriveController.DrivePerspective.RobotRelative);
            }
        };
        robot.gameObject.AddComponent<Turbo4744CapacityPhysicsProbe>().sample = () =>
        {
            MeasureFuel(CaseTime);
            MeasureLoadedSpeed(CaseTime);
        };
        deploymentDelay = 0;
        collectionStarted = false;
        lastSample = -1;
        Enabled(true);
    }

    private static void OfferCapacityFuel(float t)
    {
        if (
            !Pile
            || WheelPile
            || t < 2.5f
            || t >= CollectionEnd
            || fuel.Count >= 64
            || current.collected >= CapacityCaptureTarget
            || t - lastCapacityOffer < .16f
        )
            return;
        var point = robot.transform.TransformPoint(
            new Vector3((fuel.Count % 3 - 1) * .16f, .085f, .66f)
        );
        point.y = .08f;
        if (
            Physics.CheckSphere(
                point,
                .076f,
                LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt"),
                QueryTriggerInteraction.Ignore
            )
        )
            return;
        var go = Object.Instantiate(
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Fuel.prefab"),
            point,
            Quaternion.identity
        );
        go.transform.SetParent(GameObject.FindWithTag("GamePieceWorld").transform, true);
        fuel.Add(new Piece { body = go.GetComponent<Rigidbody>() });
        current.offered = fuel.Count;
        lastCapacityOffer = t;
    }

    private static void MeasureFuel(float t)
    {
        var rb = robot.GetComponent<Rigidbody>();
        foreach (var piece in fuel)
        {
            var p = Quaternion.Inverse(rb.rotation) * (piece.body.position - rb.position);
            bool inside =
                (
                    Mathf.Abs(p.x) < .31f
                    && p.y > .22f
                    && p.y < (Pile ? .95f : .52f)
                    && p.z > .08f
                    && p.z < (Pile ? .60f : .56f)
                    && (!Pile || robot.GetComponent<Turbo4744FuelKeeper>().IsCaptured(piece.body))
                ) || Turbo4744FeedBounds.Contains(p);
            if (inside)
            {
                if (piece.insideSince < 0)
                    piece.insideSince = t;
                if (t < 10 + CycleOffset && t - piece.insideSince > .2f && !piece.collected)
                {
                    piece.collected = true;
                    current.collected++;
                }
            }
            else
                piece.insideSince = -1;
            if (
                piece.collected
                && !piece.released
                && t >= 21 + CycleOffset
                && Mathf.Abs(p.x) < .22f
                && p.y > .62f
                && p.z < -.18f
                && piece.body.velocity.y > 1
            )
            {
                piece.released = true;
                current.released++;
            }
            if (
                piece.collected
                && !piece.escaped
                && !piece.released
                && (
                    Mathf.Abs(p.x) > .48f
                    || p.y < .12f
                    || p.y > (Pile ? 1.05f : .85f)
                    || p.z < -.2f
                    || p.z > .8f
                )
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
    }

    private static void MeasureLoadedSpeed(float t)
    {
        var rb = robot.GetComponent<Rigidbody>();
        // FixedUpdate reads the result of the preceding physics step. Attribute
        // that result to its command interval, including the final driven step.
        // The keyboard pulse bounds themselves remain 10..10.5 and 10.5..11.5.
        float completedStepStart = t - Time.fixedDeltaTime;
        if (completedStepStart > 10 + CycleOffset && completedStepStart < 11.5f + CycleOffset)
        {
            float speed = new Vector2(rb.velocity.x, rb.velocity.z).magnitude;
            if (speed > current.maxLoadedNativeSpeed)
            {
                current.maxLoadedNativeSpeed = speed;
                current.loadedSpeedPeakAt = t;
            }
        }
        if (t > 10 + CycleOffset && t < 11.5f + CycleOffset)
        {
            if (keyboard.wKey.isPressed)
                current.loadedForwardKeySeconds += Time.fixedDeltaTime;
            if (keyboard.sKey.isPressed)
                current.loadedReverseKeySeconds += Time.fixedDeltaTime;
        }
        current.maxRobotSpeed = Mathf.Max(current.maxRobotSpeed, rb.velocity.magnitude);
    }

    private static void Measure(float t)
    {
        var rb = robot.GetComponent<Rigidbody>();
        MeasureMotion(t);
        current.stable &=
            float.IsFinite(rb.position.y)
            && Mathf.Abs(rb.position.y) < .2f
            && rb.velocity.magnitude < 12;
        if (Pile && t >= 9.8f + CycleOffset && !current.measuredBeforeDrive)
        {
            current.measuredBeforeDrive = true;
            current.storedBeforeDrive = fuel.Count(piece =>
                piece.collected
                && !piece.escaped
                && InsideExpanded(robot.transform.InverseTransformPoint(piece.body.position))
            );
            Directory.CreateDirectory(Out);
            File.WriteAllText(Out + "/pile-progress.json", JsonUtility.ToJson(current, true));
            CaptureLoadedHopper();
            Debug.Log(
                $"TURBO_PILE_LOAD {current.heading} deg: {current.storedBeforeDrive} stored before loaded driving"
            );
        }
        int sample = (int)(t * 2);
        if (sample != lastSample)
        {
            lastSample = sample;
            if (Pile && t > 21 + CycleOffset && t < 30 + CycleOffset)
                Debug.Log(
                    $"TURBO_PILE_FLOW {t:F2}: {robot.ShootingState} feeding {robot.Feeding} rpm {robot.CurrentShooterRpm:F0}/{robot.TargetShooterRpm:F0} encoder {robot.IntakeEncoderRotations:F2} contacts {robot.indexer.Contacts}/{robot.shooterContact.Contacts}"
                );
            if (Pile)
                foreach (var vertex in robot.roofNet.GetComponent<MeshFilter>().sharedMesh.vertices)
                    current.netPeakHeight = Mathf.Max(
                        current.netPeakHeight,
                        robot
                            .transform.InverseTransformPoint(robot.roofNet.TransformPoint(vertex))
                            .y
                    );
            current.samples.Add(
                t.ToString("F2")
                    + " robot "
                    + rb.position.ToString("F3")
                    + " intake "
                    + robot.IntakeAction.IsPressed()
                    + " collected "
                    + current.collected
                    + " tilt "
                    + Vector3.Angle(robot.transform.up, Vector3.up).ToString("F2")
                    + " state "
                    + robot.ShootingState
                    + " omega "
                    + robot.GetComponent<Rigidbody>().angularVelocity.ToString("F2")
            );
            if (
                robot.intakeContact
                && t > 2.5f
                && t < CollectionEnd
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

    private static void MeasureMotion(float t)
    {
        if (!robot || current == null)
            return;
        var rb = robot.GetComponent<Rigidbody>();
        current.maxTilt = Mathf.Max(current.maxTilt, Vector3.Angle(robot.transform.up, Vector3.up));
        if (!Pile || t < 2.5f || t >= CollectionEnd)
            return;
        var offset = Quaternion.Inverse(collectionHeading) * (rb.position - collectionStart);
        current.collectionDistance = offset.z;
        current.wheelSweepReached |= offset.z >= 3.4f;
        float yaw = Mathf.Abs(Mathf.DeltaAngle(current.heading, rb.rotation.eulerAngles.y));
        float speed = new Vector2(rb.velocity.x, rb.velocity.z).magnitude;
        // Preserve every sample in the original eight-second observation,
        // and add any remaining sweep and braking. Extra stationary settling
        // after the completed sweep is reported separately, not as approach.
        if (WheelPile && t >= 8 && current.wheelSweepReached && input.vx == 0 && speed < .15f)
        {
            current.maxSettlingYaw = Mathf.Max(current.maxSettlingYaw, yaw);
            return;
        }
        current.maxCrossTrack = Mathf.Max(current.maxCrossTrack, Mathf.Abs(offset.x));
        if (yaw > current.maxCollectionYaw)
        {
            current.maxCollectionYaw = yaw;
            current.collectionYawPeakAt = t;
            current.collectionSpeedAtYawPeak = speed;
        }
    }

    private static void CaptureLoadedHopper()
    {
        var cameraObject = new GameObject("Loaded hopper evidence camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = .03f;
        camera.fieldOfView = 44;
        camera.transform.position = robot.transform.TransformPoint(new Vector3(1.3f, 1.25f, 1.5f));
        camera.transform.LookAt(robot.transform.TransformPoint(new Vector3(0, .35f, .25f)));
        var target = new RenderTexture(1280, 960, 24);
        var previous = RenderTexture.active;
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(1280, 960, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0);
        image.Apply();
        File.WriteAllBytes(
            Out + "/Turbo4744-pile-loaded-" + current.heading + ".png",
            image.EncodeToPNG()
        );
        RenderTexture.active = previous;
        camera.targetTexture = null;
        target.Release();
        Object.Destroy(image);
        Object.Destroy(target);
        Object.Destroy(cameraObject);
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

    private static string Phase(float rawTime)
    {
        if (rawTime < 2.5f)
            return "Deploying";
        if (rawTime < CollectionEnd)
            return WheelPile && current != null && current.wheelSweepReached
                ? "Finishing pickup and settling"
                : "Collecting fuel";
        float t = rawTime - CycleOffset;
        float feedEnd = Pile ? 66 : 46;
        return t < 10 ? "Holding fuel"
            : t < 19 ? "Driving and turning"
            : t < 21 ? "Disabled retention"
            : t < feedEnd ? "Feeding and pumping"
            : t < feedEnd + 3 ? "Stowing"
            : "Final retention";
    }

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
            if (EditorApplication.timeSinceStartup - wallStart > (Pile ? 480 : 300))
                throw new TimeoutException("Retention regression exceeded 300 seconds");
            if (!robot)
            {
                if (EditorApplication.timeSinceStartup - wallStart < 8 || !BaseGameManager.Instance)
                    return;
                timer = BaseGameManager.Instance.GetComponent<BaseTimerManager>();
                timer.PauseTimer();
                Object.FindFirstObjectByType<RobotSpawnController>().StopAllCoroutines();
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
                        "Built player DLL differs from the audited player compilation"
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
            float rawTime = CaseTime;
            if (!collectionStarted && rawTime >= 2.5f)
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
            OfferCapacityFuel(rawTime);
            Measure(rawTime);
            float t = rawTime - (rawTime >= CollectionEnd ? CycleOffset : 0);
            if (LoadOnly && rawTime >= 9 + CycleOffset)
            {
                Press();
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
                if (rawTime < 12 + CycleOffset)
                    return;
                current.retained = fuel.Count(piece =>
                    piece.collected
                    && !piece.escaped
                    && InsideExpanded(robot.transform.InverseTransformPoint(piece.body.position))
                );
                Debug.Log(
                    $"TURBO_LOAD_CASE {current.heading} deg: {current.collected} collected, {current.retained} retained, roof {current.netPeakHeight:F3}"
                );
                Cleanup();
                caseIndex++;
                if (caseIndex < Headings.Length)
                    Spawn();
                else
                    Finish();
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
            else if (rawTime < CollectionEnd)
            {
                Press(Key.LeftShift);
                Drive(
                    Pile
                        ? WheelPile && !current.wheelSweepReached
                            ? 3f
                            : 0
                        : .5f,
                    0,
                    0,
                    DriveController.DrivePerspective.RobotRelative
                );
            }
            else if (t < 10)
            {
                Press(Key.LeftShift);
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (LoadedAim && t < 21)
            {
                // Isolate the full-hopper turn requested by the normal shoot
                // button; existing capacity fixtures cover loaded translation.
                Press();
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 10.5f)
            {
                Press(Key.W);
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < 11.5f)
            {
                Press(Key.S);
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
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
                current.retainedAfterDrive = fuel.Count(piece =>
                    piece.collected
                    && !piece.escaped
                    && InsideExpanded(robot.transform.InverseTransformPoint(piece.body.position))
                );
                Press();
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
                Enabled(false);
            }
            else if (t < (Pile ? 66 : 46))
            {
                Enabled(true);
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
                if (t > 24 && t < 24.1f)
                    Press(
                        LoadedAim ? Key.K
                            : Pile ? Key.Q
                            : Key.K,
                        Key.V
                    );
                else
                    Press(
                        LoadedAim ? Key.K
                        : Pile ? Key.Q
                        : Key.K
                    );
            }
            else if (t < (Pile ? 69 : 49))
            {
                Press(Key.Z);
                Drive(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            }
            else if (t < (Pile ? 70 : 50))
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
                    && !piece.released
                    && InsideClosed(robot.transform.InverseTransformPoint(piece.body.position))
                );
                Debug.Log(
                    $"TURBO_CAPACITY_CASE {current.heading} deg: {current.collected}/{current.offered} collected, {current.retainedAfterDrive} retained, {current.released} released, {current.escapes.Count} escapes; tilt {current.maxTilt:F2}, yaw {current.maxCollectionYaw:F2}, cross-track {current.maxCrossTrack:F3}, roof {current.netPeakHeight:F3}"
                );
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

    // A full hopper can bulge the textile above the fixed rim. Keep the
    // original nine-ball regression's tighter height criterion unchanged.
    private static bool InsideClosed(Vector3 p) =>
        (
            Mathf.Abs(p.x) < .36f
            && p.y > .17f
            && p.y < (Pile ? 1.0f : .80f)
            && p.z > .03f
            && p.z < .45f
        ) || Turbo4744FeedBounds.Contains(p);

    private static bool InsideExpanded(Vector3 p) =>
        (
            Mathf.Abs(p.x) < .36f
            && p.y > .17f
            && p.y < (Pile ? 1.0f : .80f)
            && p.z > .03f
            && p.z < .65f
        ) || Turbo4744FeedBounds.Contains(p);

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Log;
        Press();
        report.passed =
            report.cases.Count == Headings.Length
            && report.cases.All(c =>
                LoadOnly
                    ? c.collected >= 52
                        && c.retained == c.collected
                        && c.escapes.Count == 0
                        && c.stable
                        && c.maxTilt < 12
                        && c.maxCollectionYaw < 5
                        && c.netPeakHeight < 1.15f
                : LoadedAim
                    ? c.collected >= 50
                        && c.storedBeforeDrive >= 50
                        && c.maxTilt < 12
                        && c.netPeakHeight < 1.15f
                        && c.released == c.collected
                        && c.retained == 0
                        && c.escapes.Count == 0
                        && c.stowed
                        && c.stable
                : c.collected
                    >= (
                        WheelPile ? 25
                        : Pile ? 50
                        : 25
                    )
                    && (
                        !Pile
                        || (WheelPile || c.storedBeforeDrive >= 50)
                            && c.maxTilt < 12
                            // Native swerve commands angular velocity, not
                            // absolute heading. Bound the residual heading
                            // drift from asymmetric intake reactions too.
                            && c.maxCollectionYaw < (WheelPile ? 3 : 5)
                            && c.maxCrossTrack < .20f
                            && (!WheelPile || c.collectionDistance > 3.1f)
                            && c.netPeakHeight < 1.15f
                    )
                    && c.maxLoadedNativeSpeed > 3.5f
                    && (Pile || c.obstacleContacts.Count == 0)
                    && c.retainedAfterDrive == c.collected
                    && c.released == c.collected
                    && c.retained == 0
                    && c.escapes.Count == 0
                    && c.stowed
                    && c.stable
            )
            && (!Pile || WheelPile || LoadOnly || report.cases.Any(c => c.storedBeforeDrive > 50))
            && report.errors.Count == 0;
        Directory.CreateDirectory(Out);
        File.WriteAllText(
            Out
                + (
                    LoadedAim ? "/loaded-aim-"
                    : LoadOnly ? "/load-"
                    : WheelPile ? "/wheel-pile-"
                    : Pile ? "/pile-"
                    : "/capacity-"
                )
                + label
                + ".json",
            JsonUtility.ToJson(report, true)
        );
        SessionState.SetInt("Turbo.CapacityExit", report.passed ? 0 : 1);
        EditorApplication.ExitPlaymode();
    }
}

[DefaultExecutionOrder(2000)]
public sealed class Turbo4744PileMotionProbe : MonoBehaviour
{
    public Action sample;

    private void FixedUpdate() => sample?.Invoke();
}

[DefaultExecutionOrder(1200)]
public sealed class Turbo4744CapacityPhysicsProbe : MonoBehaviour
{
    public Action sample;

    private void FixedUpdate() => sample?.Invoke();
}

[DefaultExecutionOrder(-2000)]
public sealed class Turbo4744CapacityInputProbe : MonoBehaviour
{
    public Action sample;

    private void FixedUpdate() => sample?.Invoke();
}

public sealed class Turbo4744CapacityView : MonoBehaviour
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
