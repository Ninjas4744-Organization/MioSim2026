using System;
using System.Collections.Generic;
using System.Globalization;
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
using MoSimLib;
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
public static class Turbo4744SystemsBench
{
    [Serializable]
    private sealed class Result
    {
        public string kind;
        public string alliance;
        public int delivered;
        public float closedVisual,
            openedVisual,
            stowedVisual,
            closedFrameSpan,
            openedFrameSpan;
        public float visualMin = 100,
            visualMax = -100;
        public float range,
            spawnRange,
            wheelRatio,
            peakSpeed,
            forwardSpeed,
            reverseSpeed,
            strafeSpeed,
            maxTilt,
            minPump = 1,
            maxPump;
        public int payload,
            payloadAtStart,
            payloadAfterDrive,
            launched,
            scored,
            netSolvers,
            hardPumpCycles,
            jamRecoveries;
        public int automaticPumpSamples,
            automaticPreparePumpSamples;
        public float automaticPumpMin = 1,
            automaticPumpMax;
        public float automaticBinMin = 1,
            automaticBinMax;
        public bool hopperFloorContained = true;
        public bool pumpedWhileCollecting,
            pumpedAfterEmpty;
        public float minNetY,
            maxNetY,
            netSpan,
            closedNetSpan,
            openedNetSpan,
            backoffSeconds;
        public int backoffSamples;
        public int obstructionContacts;
        public float firstLaunchTime = -1;
        public float pumpRequestRecoveryAge,
            pumpRequestEncoder;
        public bool stable = true,
            stowed,
            netScaleOne;
        public Vector3 target;
        public List<string> samples = new List<string>();
        public List<string> shots = new List<string>();
        public List<string> shotContacts = new List<string>();
        public List<string> shotTrace = new List<string>();
        public List<string> fieldContacts = new List<string>();
        public List<string> wheelContacts = new List<string>();
        public string indexerCase;
        public int seeded,
            lowerEntrances,
            peakLowerContacts,
            peakUpperContacts,
            peakShooterContacts,
            expectedIdleSideSupports,
            idleSideSupports,
            idleSupportSamples;
        public float peakIndexerForce,
            minIdleFuelY = 99,
            pairExitDelta = -1;
        public List<float> muzzleCrossings = new List<float>();
        public List<string> mixedTrace = new List<string>();
        public List<string> loadedPositions = new List<string>();
        public List<string> idleGuideTrace = new List<string>();
        public List<string> idleGuideContacts = new List<string>();
        public List<string> obstructionTrace = new List<string>();
        public List<string> pairTrace = new List<string>();
        public List<string> pairContacts = new List<string>();
        public List<string> pairForces = new List<string>();
    }

    [Serializable]
    private sealed class Report
    {
        public bool passed;
        public string label;
        public string editorRuntimeDllSha256;
        public string prefabSource,
            bundleSha256,
            runtimeDllSha256;
        public float physicsStep,
            simulationSpeed;
        public string teamSourceCommit = "2bd34881f2c39f86a74cc7d3ea18ba6e00678493";
        public List<Result> cases = new List<Result>();
        public List<string> errors = new List<string>();
        public List<string> editorServiceErrors = new List<string>();
    }

    private sealed class Ball
    {
        public Rigidbody body;
        public Vector3 previous;
        public bool launched,
            crossed,
            scored,
            landed;
        public float began;
        public bool lowerEntry,
            muzzleCrossed,
            sideSupported;
        public int sideSupportSteps;
        public float previousMuzzleAlong,
            previousFixedTime;
    }

    private static readonly string Out = Path.GetFullPath(
        Application.dataPath + "/../BuildArtifacts"
    );
    private static readonly List<Ball> balls = new List<Ball>();
    private static readonly List<GameObject> jamFixture = new List<GameObject>();
    private static float backoffBegan = -1;
    private static float firstRecoveryEndedAt = -1;
    private static float nextObstructionSample;
    private static float nextShotSample;
    private static Report report;
    private static Result result;
    private static Turbo4744 robot;
    private static AssetBundle bundle;
    private static GameObject prefab;
    private static Rigidbody body;
    private static Turbo4744BenchDriver input;
    private static Keyboard keyboard;
    private static BaseTimerManager timer;
    private static int index,
        sample;
    private static float began;
    private static double wall;
    private static bool loaded,
        capturedClosed,
        capturedOpen,
        capturedLoaded,
        capturedPumpOut,
        capturedPumpIn,
        pulseShoot,
        pulsePump;
    private static float intakeClosed,
        hopperClosed;
    private static Vector3 hub;
    private static readonly float[] Ranges = { 2.25f, 3f, 4f, 4.5f, 5f };
    private static string[] Args => Environment.GetCommandLineArgs();

    private static bool Flag(string name) => Args.Contains(name);

    private static bool Matrix => Flag("-turboMatrix");
    internal static string LiveCase => result?.indexerCase ?? result?.kind ?? "starting";
    private static bool Indexer => Flag("-turboIndexer");
    private static readonly string[] IndexerCases =
    {
        "lower-pair",
        "upper-pair",
        "left-single",
        "right-single",
        "center-single",
        "side-guides",
        "outer-corners",
        "asymmetric-guides",
        "mixed-floors",
        "blocked-drop",
        "blocked-shooter",
    };
    private static bool indexerSeeded;
    private static bool RedCase => Flag("-turboDelivery") ? index >= 4 : Matrix && index >= 6;

    private static float Arg(string name, float fallback)
    {
        int i = Array.IndexOf(Args, name);
        return i >= 0 ? float.Parse(Args[i + 1], CultureInfo.InvariantCulture) : fallback;
    }

    private static float[] Ratios =>
        Flag("-turboRatios")
            ? Args[Array.IndexOf(Args, "-turboRatios") + 1]
                .Split(',')
                .Select(v => float.Parse(v, CultureInfo.InvariantCulture))
                .ToArray()
            : null;

    static Turbo4744SystemsBench()
    {
        EditorApplication.playModeStateChanged += Mode;
    }

    public static void Run()
    {
        SessionState.SetString("Turbo.SystemsCompany", PlayerSettings.companyName);
        SessionState.SetString("Turbo.SystemsProduct", PlayerSettings.productName);
        PlayerSettings.companyName = "Ninjas4744ModTests";
        PlayerSettings.productName = "TurboSystemsSandbox";
        SessionState.SetBool("Turbo.SystemsPending", true);
        EditorSceneManager.OpenScene("Assets/Scenes/Rebuilt/Rebuilt.unity");
        Object.FindFirstObjectByType<SelectedGameManager>().SelectedGame =
            Resources.Load<GameOption>("GameOptions/Rebuilt");
        EditorApplication.EnterPlaymode();
    }

    public static void AssembleAndRun()
    {
        Ninjas4744Builder.Assemble();
        Run();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InputSetup()
    {
        if (!SessionState.GetBool("Turbo.SystemsPending", false))
            return;
        InputSystem.settings = Object.Instantiate(InputSystem.settings);
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings
            .EditorInputBehaviorInPlayMode
            .AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>("TurboSystemsKeyboard");
    }

    private static void Mode(PlayModeStateChange state)
    {
        if (!SessionState.GetBool("Turbo.SystemsPending", false))
            return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            report = new Report
            {
                label =
                    Flag("-turboAutomaticPump") ? "automatic-pump"
                    : Indexer ? "indexer-after"
                    : Flag("-turboPulse") ? "pulse-after"
                    : Flag("-turboDelivery") ? "delivery-after"
                    : Flag("-turboMoving") ? "moving-after"
                    : Flag("-turboCrowded")
                        ? (Flag("-turboBefore") ? "crowded-before" : "crowded-after")
                    : Matrix ? (Flag("-turboBefore") ? "matrix-before" : "matrix-after")
                    : Flag("-turboJam") ? "jam"
                    : Flag("-turboBefore") ? "before"
                    : Ratios != null ? "calibration"
                    : "after",
            };
            Time.timeScale = Flag("-turboFast") ? 3f : 1f;
            report.physicsStep = Time.fixedDeltaTime;
            report.simulationSpeed = Time.timeScale;
            prefab = null;
            bundle = null;
            index =
                Flag("-turboCrowded") ? 1
                : Flag("-turboPulse")
                || Matrix
                || Flag("-turboMoving")
                || Flag("-turboDelivery")
                || Flag("-turboAutomaticPump")
                || Flag("-turboShotsOnly")
                || Flag("-turboJam")
                || Indexer
                    ? 2
                : 0;
            robot = null;
            if (Indexer && Flag("-turboIndexerCase"))
                index =
                    2
                    + Array.IndexOf(
                        IndexerCases,
                        Args[Array.IndexOf(Args, "-turboIndexerCase") + 1]
                    );
            wall = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            Application.logMessageReceived += Log;
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            PlayerSettings.companyName = SessionState.GetString(
                "Turbo.SystemsCompany",
                "CascadeStudios"
            );
            PlayerSettings.productName = SessionState.GetString("Turbo.SystemsProduct", "MoSim");
            SessionState.SetBool("Turbo.SystemsPending", false);
            EditorApplication.Exit(SessionState.GetInt("Turbo.SystemsExit", 1));
        }
    }

    private static void Log(string text, string stack, LogType type)
    {
        if (
            report != null
            && Turbo4744BenchLog.RecordEditorServiceError(text, stack, report.editorServiceErrors)
        )
            return;
        if (
            robot
            && (type == LogType.Error || type == LogType.Exception)
            && report.errors.Count < 20
        )
            report.errors.Add(text + "\n" + stack);
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

    private static void AddBall(GameObject go)
    {
        go.transform.SetParent(GameObject.FindWithTag("GamePieceWorld").transform, true);
        var rb = go.GetComponent<Rigidbody>();
        var ball = new Ball { body = rb, previous = rb.position };
        balls.Add(ball);
        if (Flag("-turboMoving") || Flag("-turboAlignHoods"))
            go.AddComponent<Turbo4744GuideProbe>().collided = contact =>
            {
                float t = Time.fixedTime - began;
                var local = robot.transform.InverseTransformPoint(rb.position);
                if (
                    t < 3
                    || t > 9
                    || local.y < .4f
                    || local.z > .15f
                    || local.z < -.7f
                    || result.shotContacts.Count >= 2500
                )
                    return;
                result.shotContacts.Add(
                    FormattableString.Invariant(
                        $"{t:F4} ball={balls.IndexOf(ball)} {contact.collider.name} impulse={contact.impulse:F4} local={local:F4} v={rb.velocity:F4} relative={contact.relativeVelocity:F4}"
                    )
                );
            };
        if (Flag("-turboDelivery"))
            go.AddComponent<Turbo4744DeliveryProbe>().landing = p => RecordDeliveryLanding(ball, p);
    }

    private static void RecordDeliveryLanding(Ball ball, Vector3 position)
    {
        if (!ball.launched || ball.landed)
            return;
        ball.landed = true;
        Vector3 delta = position - robot.AimTarget;
        delta.y = 0;
        if (
            delta.magnitude < 1.25f
            && Mathf.Abs(position.x) < 8.15f
            && Mathf.Abs(position.z) < 3.95f
        )
            result.delivered++;
        result.shots.Add(
            "Pass landing "
                + balls.IndexOf(ball)
                + " error "
                + delta.magnitude.ToString("F3")
                + " point "
                + position.ToString("F3")
        );
    }

    private static void Spawn()
    {
        hub = Object
            .FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .First(t =>
                t.gameObject.layer == LayerMask.NameToLayer("AutoAngleNodes")
                && t.name.ToLower().Contains(RedCase ? "red" : "blue")
                && t.name.ToLower().Contains("hub")
            )
            .position;
        var position = new Vector3(0, .04f, -2.7f);
        var rotation = Quaternion.identity;
        result = new Result
        {
            kind =
                Indexer ? "indexer"
                : Flag("-turboJam") ? "jam"
                : index < 2 ? "drive"
                : "shoot",
            payload = index == 1 ? (Flag("-turboCrowded") ? 30 : 20) : 0,
        };
        if (Indexer)
            result.indexerCase = IndexerCases[index - 2];
        if (index >= 2)
        {
            result.range = Arg(
                "-turboRange",
                Indexer ? 3f
                    : Flag("-turboBefore") ? 3f
                    : Matrix ? ((index - 2) % 4 < 2 ? 2.5f : 4f)
                    : Flag("-turboPulse") || Flag("-turboMoving") ? 3f
                    : Flag("-turboDelivery") ? 7f
                    : Ranges[index - 2]
            );
            // All firing positions use the clear alliance side of the hub.
            // Neutral-side fixtures hit the physical back panel before the
            // scoring opening and do not measure release calibration.
            Vector3 away = new Vector3(
                RedCase ? -.8f : .8f,
                0,
                (Matrix || Flag("-turboMoving")) && index % 2 == 1 ? -.6f : .6f
            );
            // The former 5 m corner spawned into the alliance boundary and
            // could rotate/stall before aiming. Keep the same radius on a
            // measured clear bearing; the field and robot stay intact.
            if (Mathf.Abs(result.range - 5) < .001f && !Flag("-turboDelivery"))
            {
                away.x = Mathf.Sign(away.x) * .77f;
                away.z = Mathf.Sign(away.z) * Mathf.Sqrt(1 - .77f * .77f);
            }
            if (Flag("-turboBearingX"))
            {
                float bearingX = Arg("-turboBearingX", Mathf.Abs(away.x));
                away.x = Mathf.Sign(away.x) * bearingX;
                away.z = Mathf.Sign(away.z) * Mathf.Sqrt(1 - bearingX * bearingX);
            }
            position = hub + away * result.range;
            position.y = .04f;
            if (Flag("-turboDelivery"))
                position = new Vector3(
                    RedCase ? (index == 4 ? 2 : 0) : (index == 2 ? -2 : 0),
                    .04f,
                    index % 2 == 0 ? 2.5f : -2.5f
                );
            Vector3 towards = hub - position;
            towards.y = 0;
            result.spawnRange = towards.magnitude;
            rotation = Quaternion.Euler(
                0,
                Mathf.Atan2(towards.x, towards.z) * Mathf.Rad2Deg + (Indexer ? 180 : 210),
                0
            );
        }
        robot = Object
            .Instantiate(
                prefab,
                position,
                rotation,
                Object.FindFirstObjectByType<RobotSpawnController>().transform
            )
            .GetComponent<Turbo4744>();
        robot.SetAlliance(RedCase ? Alliance.Red : Alliance.Blue);
        var player = robot.GetComponent<PlayerInput>();
        player.neverAutoSwitchControlSchemes = true;
        player.actions.devices = new InputDevice[] { keyboard };
        result.alliance = robot.Alliance.ToString();
        body = robot.GetComponent<Rigidbody>();
        if (Flag("-turboDiagnostic"))
        {
            int labelIndex = Array.IndexOf(Args, "-turboDiagnosticLabel");
            report.label = "diagnostic-" + (labelIndex >= 0 ? Args[labelIndex + 1] : "after");
        }
        if (index >= 2)
            foreach (var observedBody in robot.GetComponentsInChildren<Rigidbody>())
                observedBody.gameObject.AddComponent<Turbo4744GuideProbe>().collided = contact =>
                {
                    float t = Time.fixedTime - began;
                    if (
                        t > 3.2f
                        || contact.collider.name == "Carpet"
                        || contact.collider.transform.IsChildOf(robot.transform)
                        || contact.collider.GetComponentInParent<RebuiltGamePieceController>()
                        || contact.impulse.sqrMagnitude < .0025f
                        || result.fieldContacts.Count >= 1000
                    )
                        return;
                    result.fieldContacts.Add(
                        FormattableString.Invariant(
                            $"{t:F4} body={observedBody.name} hit={contact.collider.name} bounds={contact.collider.bounds} impulse={contact.impulse:F3}"
                        )
                    );
                };
        if (Flag("-turboSkipRearRim"))
        {
            report.label = "moving-no-rear-rim-comparison";
            var rearRim = robot
                .GetComponentsInChildren<BoxCollider>()
                .Single(c => c.name == "Rear net rim");
            rearRim.enabled = false;
        }
        if (Flag("-turboDirectBrake"))
        {
            report.label = "moving-direct-brake-comparison";
            robot.gameObject.AddComponent<Turbo4744AcceleratorBrakeProbe>().contact =
                robot.acceleratorContact;
        }
        if (Flag("-turboAlignHoods"))
        {
            if (!report.label.Contains("aligned-hoods-comparison"))
                report.label += "-aligned-hoods-comparison";
            var correction =
                robot.shooterContact.transform.localPosition - new Vector3(.0131f, .461f, -.055f);
            foreach (
                var hood in robot
                    .GetComponentsInChildren<BoxCollider>()
                    .Where(c => c.name.StartsWith("Shooter hood "))
            )
                hood.transform.localPosition += correction;
        }
        if (Flag("-turboClearRearMouth"))
        {
            if (!report.label.Contains("clear-mouth-comparison"))
                report.label += "-clear-mouth-comparison";
            var rim = robot
                .GetComponentsInChildren<BoxCollider>()
                .Single(c => c.name == "Rear net rim");
            var right = Object
                .Instantiate(rim.gameObject, rim.transform.parent)
                .GetComponent<BoxCollider>();
            float leftEnd = rim.transform.localPosition.x - rim.size.x * .5f;
            float rightEnd = rim.transform.localPosition.x + rim.size.x * .5f;
            float mouthLeft =
                robot.shooterContact.transform.localPosition.x - robot.shooterContact.halfWidth;
            float mouthRight =
                robot.shooterContact.transform.localPosition.x + robot.shooterContact.halfWidth;
            rim.name = "Rear net rim left";
            right.name = "Rear net rim right";
            rim.transform.localPosition = new Vector3(
                (leftEnd + mouthLeft) * .5f,
                .533f,
                .0036175f
            );
            right.transform.localPosition = new Vector3(
                (mouthRight + rightEnd) * .5f,
                .533f,
                .0036175f
            );
            rim.size = new Vector3(mouthLeft - leftEnd, .016f, .016f);
            right.size = new Vector3(rightEnd - mouthRight, .016f, .016f);
        }
        input = robot.gameObject.AddComponent<Turbo4744BenchDriver>();
        input.drive = robot.GetComponent<DriveController>();
        intakeClosed = robot.intakeRail.transform.localPosition.z;
        hopperClosed = robot.hopperRail.transform.localPosition.z;
        robot.shooterMotorToWheelRatio = Arg("-turboWheelRatio", robot.shooterMotorToWheelRatio);
        if (Ratios != null)
            robot.shooterMotorToWheelRatio = Ratios[index - 2];
        result.wheelRatio = robot.shooterMotorToWheelRatio;
        var fuel = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
        );
        var manager = Object.FindFirstObjectByType<RebuiltFuelPreloadManager>();
        var removable = Enumerable
            .Range(0, 8)
            .Select(i => Object.Instantiate(fuel, new Vector3(7, .075f, 3), Quaternion.identity))
            .ToArray();
        typeof(RebuiltFuelPreloadManager)
            .GetField("removableFuel", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(manager, removable);
        Object
            .FindFirstObjectByType<RebuiltSpawnInitializer>()
            .HandleSpawnedRobots(new[] { robot });
        foreach (
            var other in Object.FindObjectsByType<RebuiltGamePieceController>(
                FindObjectsSortMode.None
            )
        )
            if (!robot.preloads.Contains(other.gameObject))
                other.gameObject.SetActive(false);
        balls.Clear();
        foreach (var preload in robot.preloads)
        {
            if (index < 2 || Indexer || Flag("-turboAutomaticPump"))
            {
                preload.SetActive(false);
                Object.Destroy(preload);
            }
            else
                AddBall(preload);
        }
        if (Flag("-turboAutomaticPump"))
        {
            int count = (int)Arg("-turboPumpFuelCount", 3);
            if (count < 0 || count > 3)
                throw new ArgumentOutOfRangeException("-turboPumpFuelCount", "Use 0 to 3 balls.");
            for (int i = 0; i < count; i++)
                AddBall(
                    Object.Instantiate(
                        fuel,
                        robot.transform.TransformPoint(
                            new Vector3((i - (count - 1) * .5f) * .16f, .28f, .49f)
                        ),
                        Quaternion.identity
                    )
                );
            result.seeded = count;
        }
        jamFixture.Clear();
        backoffBegan = -1;
        firstRecoveryEndedAt = -1;
        nextObstructionSample = 0;
        nextShotSample = 0;
        indexerSeeded = false;
        if (Indexer)
            robot.gameObject.AddComponent<Turbo4744IndexerProbe>().sample = MeasureIndexerPhysics;
        if (Flag("-turboJam"))
            robot.gameObject.AddComponent<Turbo4744IndexerProbe>().sample = MeasureObstruction;
        if (Flag("-turboMoving") || Flag("-turboAlignHoods"))
            robot.gameObject.AddComponent<Turbo4744IndexerProbe>().sample = MeasureMovingPhysics;
        if (Flag("-turboJam"))
        {
            foreach (var ball in balls)
            {
                ball.body.gameObject.SetActive(false);
                Object.Destroy(ball.body.gameObject);
            }
            balls.Clear();
        }
        began = Time.time;
        loaded =
            capturedClosed =
            capturedOpen =
            capturedLoaded =
            capturedPumpOut =
            capturedPumpIn =
            pulseShoot =
            pulsePump =
                false;
        sample = -1;
        report.cases.Add(result);
        result.netSolvers = robot.GetComponentsInChildren<Turbo4744NetSolver>().Length;
        result.netScaleOne = robot.roofNet.localScale == Vector3.one;
        // Hold the initial cloth measurement disabled. A slow first editor
        // frame can otherwise arrive after the real 0.5 s auto-open command.
        if (index == 0)
            Enabled(false);
    }

    private static void Measure(float t)
    {
        result.netScaleOne &= robot.roofNet.localScale == Vector3.one;
        var lacing = robot.GetComponent<Turbo4744NetRig>();
        float frameSpan =
            robot.transform.InverseTransformPoint(lacing.frontLeft.position).z
            - robot.transform.InverseTransformPoint(lacing.rearLeft.position).z;
        if (index == 0 && t < .4f)
            result.closedFrameSpan = frameSpan;
        if (index == 0 && t > 2.4f && t < 2.6f)
            result.openedFrameSpan = frameSpan;
        var solver = robot.GetComponentInChildren<Turbo4744NetSolver>();
        if (solver)
        {
            var vertices = solver.GetRuntimePositions();
            if (vertices.Length > 0)
            {
                float minY = float.PositiveInfinity,
                    maxY = float.NegativeInfinity,
                    minZ = float.PositiveInfinity,
                    maxZ = float.NegativeInfinity;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 p = robot.transform.InverseTransformPoint((Vector3)vertices[i]);
                    if (!float.IsFinite(p.y))
                        result.stable = false;
                    minY = Mathf.Min(minY, p.y);
                    maxY = Mathf.Max(maxY, p.y);
                    minZ = Mathf.Min(minZ, p.z);
                    maxZ = Mathf.Max(maxZ, p.z);
                }
                result.minNetY = minY;
                result.maxNetY = maxY;
                result.netSpan = maxZ - minZ;
                result.stable &= minY > -.05f && maxY < .9f;
            }
        }
        var cycles = typeof(Turbo4744).GetProperty("CompletedHardPumpCycles");
        if (cycles != null)
            result.hardPumpCycles = (int)cycles.GetValue(robot);
        result.jamRecoveries = robot.JamRecoveries;
        bool backing = robot.ShootingState == Turbo4744.ShootState.SaveOuttakeBack;
        if (backing && backoffBegan < 0)
            backoffBegan = t;
        if (backing && robot.TargetIndexerMotorRps == -20 && robot.TargetAcceleratorMotorRps == 0)
            result.backoffSamples++;
        if (!backing && backoffBegan >= 0)
        {
            if (firstRecoveryEndedAt < 0)
                firstRecoveryEndedAt = t;
            result.backoffSeconds = t - backoffBegan;
            backoffBegan = -1;
        }
        var visual = robot
            .GetComponentsInChildren<MeshFilter>()
            .First(m => m.name == "Intake_Geometry");
        float visualZ = robot
            .transform.InverseTransformPoint(
                visual.transform.TransformPoint(visual.sharedMesh.bounds.center)
            )
            .z;
        if (t > 6.1f && t < 11.5f)
        {
            result.visualMin = Mathf.Min(result.visualMin, visualZ);
            result.visualMax = Mathf.Max(result.visualMax, visualZ);
        }
        if (t < .4f)
            result.closedVisual = visualZ;
        if (t > 2.4f && t < 2.6f)
            result.openedVisual = visualZ;
        if (t > 12f)
            result.stowedVisual = visualZ;
        result.peakSpeed = Mathf.Max(
            result.peakSpeed,
            new Vector2(body.velocity.x, body.velocity.z).magnitude
        );
        float tilt = Quaternion.Angle(
            body.rotation,
            Quaternion.Euler(0, body.rotation.eulerAngles.y, 0)
        );
        result.maxTilt = Mathf.Max(result.maxTilt, tilt);
        result.stable &=
            float.IsFinite(body.position.y) && Mathf.Abs(body.position.y) < .2f && tilt < 15;
        if (index < 2)
        {
            if (t > 3.8f && t < 4.4f)
                result.forwardSpeed = Mathf.Max(result.forwardSpeed, body.velocity.z);
            if (t > 5.6f && t < 6.2f)
                result.reverseSpeed = Mathf.Max(result.reverseSpeed, -body.velocity.z);
            if (t > 7.3f && t < 7.8f)
                result.strafeSpeed = Mathf.Max(result.strafeSpeed, body.velocity.x);
        }
        if (t > 6.1f && t < 11.5f)
        {
            // EditorApplication.update can observe a rendered chassis transform
            // one physics step behind its Rigidbody. Compare both bodies in the
            // same physics frame, as the actual rail controller does.
            float extension =
                (
                    Quaternion.Inverse(body.rotation)
                    * (robot.intakeRail.GetComponent<Rigidbody>().position - body.position)
                ).z - intakeClosed;
            result.minPump = Mathf.Min(result.minPump, extension);
            result.maxPump = Mathf.Max(result.maxPump, extension);
        }
        if (Flag("-turboAutomaticPump"))
        {
            var keeper = robot.GetComponent<Turbo4744FuelKeeper>();
            float binFront = robot
                .transform.InverseTransformPoint(
                    keeper.frontPanel.transform.TransformPoint(keeper.frontPanel.center)
                )
                .z;
            float floorRear =
                keeper.floor.transform.localPosition.z
                + keeper.floor.center.z
                - keeper.floor.size.z * .5f;
            float floorFront = floorRear + keeper.floor.size.z;
            result.hopperFloorContained &= floorRear >= .31f && floorFront <= binFront + .006f;
            bool pumping = robot.PumpMode == Turbo4744.RailState.SoftPumping;
            result.pumpedWhileCollecting |= t > 2.2f && t < 3 && pumping;
            bool remaining = balls.Any(b =>
                b.body
                && !b.launched
                && robot.transform.InverseTransformPoint(b.body.position).z > .03f
            );
            result.pumpedAfterEmpty |= t > 5 && !remaining && pumping;
            if (pumping && remaining)
            {
                if (
                    robot.ShootingState == Turbo4744.ShootState.PrepareHub
                    || robot.ShootingState == Turbo4744.ShootState.PrepareDelivery
                )
                    result.automaticPreparePumpSamples++;
                float extension = robot.IntakeEncoderRotations * robot.intakeTravel / 11f;
                result.automaticPumpSamples++;
                result.automaticBinMin = Mathf.Min(result.automaticBinMin, binFront);
                result.automaticBinMax = Mathf.Max(result.automaticBinMax, binFront);
                result.automaticPumpMin = Mathf.Min(result.automaticPumpMin, extension);
                result.automaticPumpMax = Mathf.Max(result.automaticPumpMax, extension);
            }
        }
        foreach (var ball in balls.Where(b => b.body))
        {
            var p = ball.body.position;
            var local = robot.transform.InverseTransformPoint(p);
            if (!ball.launched && local.y > .65f && local.z < -.2f)
            {
                if (result.firstLaunchTime < 0)
                    result.firstLaunchTime = t;
                ball.launched = true;
                ball.began = t;
                result.launched++;
                result.shots.Add(
                    "Exit "
                        + balls.IndexOf(ball)
                        + " t "
                        + t.ToString("F3")
                        + " p "
                        + p.ToString("F3")
                        + " v "
                        + ball.body.velocity.ToString("F3")
                        + " goalRPM "
                        + robot.TargetShooterRpm.ToString("F1")
                );
            }
            var controller = ball.body.GetComponent<RebuiltGamePieceController>();
            // scored is a presence flag and can clear between editor samples.
            // The native scorer's successful claim timestamp persists.
            float scoredAt = (float)
                typeof(RebuiltGamePieceController)
                    .GetField("_lastScoredTime", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(controller);
            if (scoredAt >= began && !ball.scored)
            {
                ball.scored = true;
                result.scored++;
            }
            if (
                ball.launched
                && !ball.crossed
                && ball.previous.y >= hub.y
                && p.y < hub.y
                && ball.body.velocity.y < 0
            )
            {
                float f = (hub.y - ball.previous.y) / (p.y - ball.previous.y);
                Vector3 hit = Vector3.Lerp(ball.previous, p, f);
                Vector3 miss = hit - hub;
                miss.y = 0;
                result.shots.Add(
                    "Landing "
                        + balls.IndexOf(ball)
                        + " error "
                        + miss.magnitude.ToString("F3")
                        + " point "
                        + hit.ToString("F3")
                        + " flight "
                        + (t - ball.began).ToString("F3")
                );
                ball.crossed = true;
            }
            if (
                Flag("-turboDelivery")
                && ball.launched
                && !ball.landed
                && ball.previous.y > .10f
                && p.y <= .10f
                && ball.body.velocity.y < 0
            )
            {
                RecordDeliveryLanding(ball, p);
            }
            ball.previous = p;
        }
        int s = (int)(t * 4);
        if (s == sample)
            return;
        sample = s;
        result.samples.Add(
            t.ToString("F2")
                + " p "
                + body.position.ToString("F3")
                + " v "
                + body.velocity.ToString("F3")
                + " yaw "
                + body.rotation.eulerAngles.y.ToString("F1")
                + " aim "
                + robot.autoAim.GetRotationError().ToString("F2")
                + " rpm "
                + robot.CurrentShooterRpm.ToString("F1")
                + " feed "
                + robot.Feeding
                + " rails "
                + robot.intakeRail.transform.localPosition.z.ToString("F4")
                + ","
                + robot.hopperRail.transform.localPosition.z.ToString("F4")
                + " pump "
                + robot.PumpMode
                + " encoder "
                + (t > .1f ? robot.IntakeEncoderRotations : 0).ToString("F3")
                + " command "
                + robot.intakeRail.targetVelocity.z.ToString("F3")
        );
        if (s % 4 == 0)
            foreach (var wheel in robot.GetComponentsInChildren<WheelCollider>())
                result.wheelContacts.Add(
                    t.ToString("F2")
                        + " "
                        + wheel.name
                        + " "
                        + (
                            wheel.GetGroundHit(out var hit)
                                ? hit.collider.name + " force " + hit.force.ToString("F1")
                                : "none"
                        )
                );
    }

    private static void SeedIndexer()
    {
        // A seeded fixture needs a collision-free initial cloth pose, as normal
        // preloads have at robot spawn. Do not insert bodies through an already
        // sagged empty net and measure its depenetration as roller behavior.
        robot.GetComponent<Turbo4744NetRig>().UpdatePins();
        robot.roofNet.GetComponent<Turbo4744NetSolver>().Rebuild();
        var fuel = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
        );
        var positions = new List<Vector3>();
        switch (result.indexerCase)
        {
            case "lower-pair":
                positions.Add(new Vector3(.0131f - .0765f, .29f, .27f));
                positions.Add(new Vector3(.0131f + .0765f, .29f, .27f));
                break;
            case "upper-pair":
                positions.Add(new Vector3(.0131f - .0765f, .413f, .13f));
                positions.Add(new Vector3(.0131f + .0765f, .413f, .13f));
                break;
            case "left-single":
                positions.Add(new Vector3(-.0634f, .29f, .27f));
                break;
            case "right-single":
                positions.Add(new Vector3(.0896f, .29f, .27f));
                break;
            case "center-single":
                positions.Add(new Vector3(.0131f, .29f, .27f));
                break;
            case "side-guides":
                positions.Add(new Vector3(-.22f, .32f, .23f));
                positions.Add(new Vector3(.22f, .32f, .23f));
                break;
            case "outer-corners":
                positions.Add(new Vector3(-.275f, .32f, .23f));
                positions.Add(new Vector3(.275f, .32f, .23f));
                break;
            case "asymmetric-guides":
                positions.Add(new Vector3(-.275f, .32f, .23f));
                positions.Add(new Vector3(-.0634f, .29f, .27f));
                positions.Add(new Vector3(.0896f, .29f, .27f));
                break;
            case "mixed-floors":
                foreach (float x in new[] { -.0634f, .0896f })
                {
                    positions.Add(new Vector3(x, .29f, .27f));
                    // Place the upper row on its CAD rollers, below the settled cloth.
                    positions.Add(new Vector3(x, .405f, .13f));
                }
                break;
            case "blocked-drop":
                positions.Add(new Vector3(.0131f, .405f, .107f));
                break;
            case "blocked-shooter":
                positions.Add(new Vector3(.0131f, .19f, .02f));
                break;
        }
        foreach (var position in positions)
        {
            AddBall(
                Object.Instantiate(
                    fuel,
                    robot.transform.TransformPoint(position),
                    Quaternion.identity
                )
            );
            if (result.indexerCase.EndsWith("-pair"))
            {
                var ball = balls.Last();
                var witness = ball.body.gameObject.AddComponent<Turbo4744GuideProbe>();
                witness.collided = contact =>
                {
                    if (Time.fixedTime - began > 3.25f || result.pairContacts.Count >= 120)
                        return;
                    var other = contact.collider;
                    result.pairContacts.Add(
                        (Time.fixedTime - began).ToString("F4")
                            + " fuel "
                            + balls.IndexOf(ball)
                            + " collider "
                            + other.name
                            + " id "
                            + other.GetInstanceID()
                            + " parent "
                            + other.transform.parent?.name
                            + " body "
                            + (other.attachedRigidbody ? other.attachedRigidbody.name : "static")
                            + " local "
                            + robot
                                .transform.InverseTransformPoint(other.bounds.center)
                                .ToString("F4")
                            + " impulse "
                            + contact.impulse.ToString("F4")
                    );
                };
            }
            if (Mathf.Abs(position.x) > .17f)
            {
                var ball = balls.Last();
                result.expectedIdleSideSupports++;
                var probe = ball.body.gameObject.AddComponent<Turbo4744GuideProbe>();
                probe.observed = detail =>
                {
                    if (Time.fixedTime - began < 3 && result.idleGuideContacts.Count < 40)
                        result.idleGuideContacts.Add("fuel " + balls.IndexOf(ball) + " " + detail);
                };
                probe.supported = () =>
                {
                    if (Time.fixedTime - began < 3)
                    {
                        ball.sideSupportSteps++;
                        ball.sideSupported = true;
                    }
                };
            }
        }
        result.seeded = positions.Count;
        if (result.indexerCase.StartsWith("blocked-"))
        {
            for (int axis = 0; axis < 3; axis++)
                foreach (int side in new[] { -1, 1 })
                {
                    var wall = new GameObject("Indexer obstruction fixture");
                    wall.transform.SetParent(robot.transform, false);
                    wall.layer = LayerMask.NameToLayer("Robot");
                    var offset = Vector3.zero;
                    offset[axis] = side * .08f;
                    wall.transform.localPosition = positions[0] + offset;
                    var box = wall.AddComponent<BoxCollider>();
                    var size = Vector3.one * .165f;
                    size[axis] = .005f;
                    box.size = size;
                    box.excludeLayers = ~LayerMask.GetMask(
                        "GamePiece1",
                        "GamePiece2",
                        "GamePieceAlt"
                    );
                    jamFixture.Add(wall);
                }
            WatchObstruction(balls[0]);
        }
        indexerSeeded = true;
    }

    private static void MeasureIndexerPhysics()
    {
        if (!robot || !indexerSeeded)
            return;
        if (Time.fixedTime - began < 3)
        {
            result.idleSupportSamples++;
            foreach (var ball in balls)
                result.minIdleFuelY = Mathf.Min(
                    result.minIdleFuelY,
                    robot.transform.InverseTransformPoint(ball.body.position).y
                );
            result.idleSideSupports = balls.Count(b => b.sideSupported);
            if (result.expectedIdleSideSupports > 0 && Time.fixedTime - began > 2.9f)
                result.idleGuideTrace = balls
                    .Select(b =>
                        robot.transform.InverseTransformPoint(b.body.position).ToString("F4")
                        + " guide contact steps "
                        + b.sideSupportSteps
                    )
                    .ToList();
        }
        result.peakLowerContacts = Mathf.Max(result.peakLowerContacts, robot.indexer.LowerContacts);
        result.peakUpperContacts = Mathf.Max(result.peakUpperContacts, robot.indexer.UpperContacts);
        result.peakShooterContacts = Mathf.Max(
            result.peakShooterContacts,
            robot.shooterContact.Contacts
        );
        result.peakIndexerForce = Mathf.Max(result.peakIndexerForce, robot.indexer.AppliedForce);
        if (result.indexerCase.EndsWith("-pair") && Time.fixedTime - began < 2.35f)
        {
            var contactBodies =
                (HashSet<Rigidbody>)
                    typeof(Turbo4744ShooterContact)
                        .GetField("current", BindingFlags.NonPublic | BindingFlags.Instance)
                        .GetValue(robot.intakeContact);
            result.pairForces.Add(
                (Time.fixedTime - began).ToString("F4")
                    + " "
                    + string.Join(
                        " | ",
                        balls.Select(b =>
                            "force "
                            + b.body.GetAccumulatedForce().ToString("F3")
                            + " pickup "
                            + contactBodies.Contains(b.body)
                            + " captured "
                            + robot.indexer.keeper.IsCaptured(b.body)
                            + " com "
                            + robot
                                .transform.InverseTransformPoint(b.body.worldCenterOfMass)
                                .ToString("F4")
                        )
                    )
            );
        }
        if (
            result.indexerCase.EndsWith("-pair")
            && result.pairTrace.Count < 180
            && Time.fixedTime - began >= 2.25f + result.pairTrace.Count * .025f
        )
            result.pairTrace.Add(
                (Time.fixedTime - began).ToString("F4")
                    + " "
                    + robot.ShootingState
                    + " feeding "
                    + robot.Feeding
                    + " "
                    + string.Join(
                        " | ",
                        balls.Select(b =>
                            "p "
                            + robot.transform.InverseTransformPoint(b.body.position).ToString("F4")
                            + " v "
                            + (
                                Quaternion.Inverse(robot.transform.rotation) * b.body.velocity
                            ).ToString("F3")
                            + " touching "
                            + string.Join(
                                ",",
                                Physics
                                    .OverlapSphere(b.body.position, .077f)
                                    .Where(c => c.attachedRigidbody != b.body && !c.isTrigger)
                                    .Select(c => c.name)
                            )
                        )
                    )
            );
        if (
            result.indexerCase == "mixed-floors"
            && result.mixedTrace.Count < 18
            && Time.fixedTime - began > 2.3f + result.mixedTrace.Count * .1f
        )
        {
            var last = balls.Last().body;
            result.mixedTrace.Add(
                (Time.fixedTime - began).ToString("F3")
                    + " "
                    + robot.transform.InverseTransformPoint(last.position).ToString("F4")
                    + " v "
                    + (Quaternion.Inverse(robot.transform.rotation) * last.velocity).ToString("F3")
                    + " contacts "
                    + string.Join(
                        ",",
                        Physics
                            .OverlapSphere(last.position, .08f)
                            .Where(c => c.attachedRigidbody != last && !c.isTrigger)
                            .Select(c => c.name)
                    )
            );
        }
        if (result.shots.Count < 6 && Time.fixedTime - began > 3.9f + result.shots.Count * .4f)
        {
            foreach (var b in balls.Where(b => b.body).Take(1))
                result.shots.Add(
                    "Contact probe "
                        + robot.transform.InverseTransformPoint(b.body.position).ToString("F4")
                        + " velocity "
                        + (Quaternion.Inverse(robot.transform.rotation) * b.body.velocity).ToString(
                            "F3"
                        )
                        + " indexer "
                        + robot.indexer.TargetMotorRps
                        + "/"
                        + robot.indexer.CurrentSurfaceSpeed
                        + " contacts "
                        + robot.indexer.Contacts
                        + " force "
                        + robot.indexer.AppliedForce
                        + " captured "
                        + robot.indexer.keeper.IsCaptured(b.body)
                        + " touching "
                        + string.Join(
                            ",",
                            Physics
                                .OverlapSphere(b.body.position, .08f)
                                .Where(c => c.attachedRigidbody != b.body && !c.isTrigger)
                                .Select(c => c.name)
                        )
                );
        }
        foreach (var ball in balls.Where(b => b.body))
        {
            if (!ball.lowerEntry && robot.indexer.HasEnteredShooter(ball.body))
            {
                ball.lowerEntry = true;
                result.lowerEntrances++;
            }
            var muzzleLocal = robot.shooterContact.transform.InverseTransformPoint(
                ball.body.worldCenterOfMass
            );
            float along = -muzzleLocal.z;
            if (
                !ball.muzzleCrossed
                && ball.lowerEntry
                && ball.previousFixedTime > 0
                && ball.previousMuzzleAlong <= robot.shooterContact.contactFar
                && along > robot.shooterContact.contactFar
                && Mathf.Abs(muzzleLocal.y) < robot.shooterContact.normalClearance * 2
                && Mathf.Abs(muzzleLocal.x) < robot.shooterContact.halfWidth
            )
            {
                float fraction =
                    (robot.shooterContact.contactFar - ball.previousMuzzleAlong)
                    / (along - ball.previousMuzzleAlong);
                float time = Mathf.Lerp(ball.previousFixedTime, Time.fixedTime, fraction) - began;
                result.muzzleCrossings.Add(time);
                ball.muzzleCrossed = true;
            }
            ball.previousMuzzleAlong = along;
            ball.previousFixedTime = Time.fixedTime;
        }
        if (result.muzzleCrossings.Count >= 2)
            result.pairExitDelta = Mathf.Abs(result.muzzleCrossings[1] - result.muzzleCrossings[0]);
    }

    private static void SeedJam()
    {
        indexerSeeded = true;
        var fuel = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
        );
        var center = new Vector3(0, .28f, .22f);
        AddBall(
            Object.Instantiate(fuel, robot.transform.TransformPoint(center), Quaternion.identity)
        );
        for (int axis = 0; axis < 3; axis++)
            foreach (int side in new[] { -1, 1 })
            {
                var wall = new GameObject("Benchmark jam obstruction " + axis + "/" + side);
                wall.transform.SetParent(robot.transform, false);
                wall.layer = LayerMask.NameToLayer("Robot");
                var offset = Vector3.zero;
                offset[axis] = side * .08f;
                wall.transform.localPosition = center + offset;
                var box = wall.AddComponent<BoxCollider>();
                var size = Vector3.one * .165f;
                size[axis] = .005f;
                box.size = size;
                box.excludeLayers = ~LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
                jamFixture.Add(wall);
            }
        WatchObstruction(balls[0]);
    }

    private static void WatchObstruction(Ball ball)
    {
        var probe = ball.body.gameObject.AddComponent<Turbo4744GuideProbe>();
        probe.observed = detail =>
        {
            if (Time.time - began < 7)
                result.obstructionTrace.Add(
                    (Time.time - began).ToString("F4") + " contact " + detail
                );
            if (
                Time.time - began < 7
                && (
                    detail.StartsWith("Benchmark jam obstruction")
                    || detail.StartsWith("Indexer obstruction fixture")
                )
            )
                result.obstructionContacts++;
        };
    }

    private static void MeasureObstruction()
    {
        float t = Time.fixedTime - began;
        if (t > 7.1f || t < nextObstructionSample || balls.Count == 0 || !balls[0].body)
            return;
        nextObstructionSample = t + .1f;
        var moving = balls[0].body;
        var wall = jamFixture.FirstOrDefault(o => o);
        if (!wall)
            return;
        var blocker = wall.GetComponent<Collider>();
        string shapes = string.Join(
            ";",
            moving
                .GetComponentsInChildren<Collider>()
                .Select(c =>
                    c.name
                    + " layer="
                    + LayerMask.LayerToName(c.gameObject.layer)
                    + " enabled="
                    + c.enabled
                    + " trigger="
                    + c.isTrigger
                    + " ignoreLayer="
                    + Physics.GetIgnoreLayerCollision(c.gameObject.layer, blocker.gameObject.layer)
                    + " ignorePair="
                    + Physics.GetIgnoreCollision(c, blocker)
                )
        );
        result.obstructionTrace.Add(
            FormattableString.Invariant(
                $"{t:F4} p={robot.transform.InverseTransformPoint(moving.position):F4} v={moving.velocity:F3} parent={(moving.transform.parent ? moving.transform.parent.name : "none")} body={moving.collisionDetectionMode} feed={robot.Feeding} rps={robot.TargetIndexerMotorRps} walls={jamFixture.Count(o => o && o.activeInHierarchy)} shapes={shapes}"
            )
        );
    }

    private static void Cleanup()
    {
        foreach (var outpost in Object.FindObjectsByType<Outpost>(FindObjectsSortMode.None))
            outpost.Robots.Remove(robot);
        foreach (var ball in balls)
            if (ball.body)
            {
                ball.body.gameObject.SetActive(false);
                Object.Destroy(ball.body.gameObject);
            }
        // Destroy is deferred. Disable the completed fixture before spawning
        // the next nearby robot so they cannot collide for one physics step.
        robot.gameObject.SetActive(false);
        Object.Destroy(robot.gameObject);
        robot = null;
    }

    private static int Retained() =>
        balls.Count(b =>
        {
            if (!b.body)
                return false;
            var p = robot.transform.InverseTransformPoint(b.body.position);
            return (Mathf.Abs(p.x) < .36f && p.y > .17f && p.y < .7f && p.z > 0 && p.z < .65f)
                || Turbo4744FeedBounds.Contains(p);
        });

    internal static GameObject CreateShooterCutaway(Turbo4744 robot)
    {
        // Retain the original shooter internals in the cutaway, removing
        // the merged chassis panels that would obscure the lower entrance.
        var chassis = robot
            .GetComponentsInChildren<MeshFilter>()
            .First(m => m.name == "Chassis_Geometry");
        // The bundle discards CPU mesh buffers. Read the matching imported
        // editor asset for this visualization, leaving the tested bundle intact.
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744/Model/Turbo4744.fbx"
        );
        var original = source
            .GetComponentsInChildren<MeshFilter>()
            .First(m => m.name == "Chassis_Geometry")
            .sharedMesh;
        if (
            original.vertexCount != chassis.sharedMesh.vertexCount
            || original.subMeshCount != chassis.sharedMesh.subMeshCount
        )
            throw new InvalidOperationException("Cutaway source does not match the tested model");
        var vertices = original.vertices;
        var shooterMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        shooterMesh.vertices = vertices;
        shooterMesh.normals = original.normals;
        shooterMesh.uv = original.uv;
        shooterMesh.subMeshCount = original.subMeshCount;
        for (int sub = 0; sub < original.subMeshCount; sub++)
        {
            var triangles = original.GetTriangles(sub);
            var selected = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                var center =
                    (
                        vertices[triangles[i]]
                        + vertices[triangles[i + 1]]
                        + vertices[triangles[i + 2]]
                    ) / 3;
                var p = robot.transform.InverseTransformPoint(
                    chassis.transform.TransformPoint(center)
                );
                if (Mathf.Abs(p.x - .0131f) < .14f && p.y > .24f && p.z < .025f)
                    selected.AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
            }
            shooterMesh.SetTriangles(selected, sub);
        }
        shooterMesh.RecalculateBounds();
        var shooterPreview = new GameObject("Cutaway original shooter internals");
        shooterPreview.layer = LayerMask.NameToLayer("Robot");
        shooterPreview.transform.SetParent(chassis.transform, false);
        shooterPreview.AddComponent<MeshFilter>().sharedMesh = shooterMesh;
        shooterPreview.AddComponent<MeshRenderer>().sharedMaterials = chassis
            .GetComponent<Renderer>()
            .sharedMaterials;
        return shooterPreview;
    }

    private static void Capture(string name)
    {
        bool cutaway = name.Contains("indexer");
        var renderers = robot.GetComponentsInChildren<Renderer>(true);
        var visibility = renderers.Select(r => r.enabled).ToArray();
        var supportMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        supportMaterial.color = new Color(.15f, .65f, .8f);
        var supports = renderers.Where(r => r.name.StartsWith("SideIndexer")).ToArray();
        var supportMaterials = supports.Select(r => r.sharedMaterials).ToArray();
        GameObject shooterPreview = null;
        Mesh shooterMesh = null;
        if (cutaway)
        {
            foreach (var renderer in renderers)
                renderer.enabled =
                    renderer.name.StartsWith("Indexer") || renderer.name.StartsWith("SideIndexer");
            foreach (var support in supports)
                support.sharedMaterial = supportMaterial;
            shooterPreview = CreateShooterCutaway(robot);
            shooterMesh = shooterPreview.GetComponent<MeshFilter>().sharedMesh;
        }
        var camera = new GameObject("Bench preview").AddComponent<Camera>();
        camera.transform.position = robot.transform.TransformPoint(
            name.Contains("top") ? new Vector3(.05f, 1.6f, .15f)
            : name.Contains("cutaway-side") ? new Vector3(1.4f, .35f, .05f)
            : new Vector3(1.3f, .8f, 1.25f)
        );
        camera.transform.LookAt(
            robot.transform.TransformPoint(new Vector3(0, .26f, .1f)),
            cutaway && name.Contains("top") ? robot.transform.forward : robot.transform.up
        );
        camera.orthographic = true;
        camera.orthographicSize = .6f;
        camera.nearClipPlane = .01f;
        camera.cullingMask = LayerMask.GetMask("Robot", "GamePiece1", "GamePiece2", "GamePieceAlt");
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.04f, .05f, .07f, 0);
        var rt = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(960, 720, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
        tex.Apply();
        Directory.CreateDirectory(Out);
        File.WriteAllBytes(
            Out + (cutaway ? "/Turbo4744-v6-" : "/Turbo4744-v5-") + name + ".png",
            tex.EncodeToPNG()
        );
        camera.targetTexture = null;
        RenderTexture.active = null;
        Object.Destroy(rt);
        Object.Destroy(tex);
        Object.Destroy(camera.gameObject);
        if (cutaway)
        {
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = visibility[i];
            for (int i = 0; i < supports.Length; i++)
                supports[i].sharedMaterials = supportMaterials[i];
        }
        Object.Destroy(supportMaterial);
        if (shooterPreview)
            Object.Destroy(shooterPreview);
        if (shooterMesh)
            Object.Destroy(shooterMesh);
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - wall > 600)
                throw new TimeoutException("Systems benchmark exceeded ten minutes");
            if (!robot)
            {
                if (EditorApplication.timeSinceStartup - wall < 5 || !BaseGameManager.Instance)
                    return;
                timer = BaseGameManager.Instance.GetComponent<BaseTimerManager>();
                timer.PauseTimer();
                Object.FindFirstObjectByType<RobotSpawnController>().StopAllCoroutines();
                typeof(BaseTimerManager)
                    .GetProperty("CurrentRobotState")
                    .SetValue(timer, RobotState.Enabled);
                foreach (var camera in Camera.allCameras)
                    camera.enabled = false;
                foreach (var old in Object.FindObjectsByType<RobotBase>(FindObjectsSortMode.None))
                    old.gameObject.SetActive(false);
                if (!prefab)
                {
                    if (Flag("-turboBundle") && !Flag("-turboAuthoring"))
                    {
                        string file = Path.GetFullPath(
                            Application.dataPath
                                + "/../BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64/rebuilt_ninjas_4744_turbo_2026_assets"
                        );
                        bundle = AssetBundle.LoadFromFile(file);
                        report.prefabSource = file;
                        report.bundleSha256 = Digest(file);
                        report.runtimeDllSha256 = Digest(
                            Path.Combine(Path.GetDirectoryName(file), "Ninjas4744.dll")
                        );
                        report.editorRuntimeDllSha256 = Digest(typeof(Turbo4744).Assembly.Location);
                        Turbo4744NetBurstBuilder.VerifyRuntime();
                        if (
                            report.runtimeDllSha256
                            != Digest(Turbo4744NetBurstBuilder.PlayerAssembly)
                        )
                            throw new InvalidOperationException(
                                "The built DLL differs from the runtime under test"
                            );
                        prefab = bundle
                            .LoadAllAssets<BaseModpackSO>()
                            .Single(p => p.ModpackName == "Ninjas 4744 Turbo 2026")
                            .Robots.Single()
                            .AlternateRobots.Single(v => v.DisplayName == "Actual Turbo")
                            .RobotPrefab;
                    }
                    else
                    {
                        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                            Ninjas4744Builder.RobotPath
                        );
                        report.prefabSource = Ninjas4744Builder.RobotPath;
                    }
                }
                hub = Object
                    .FindObjectsByType<Transform>(FindObjectsSortMode.None)
                    .First(t =>
                        t.gameObject.layer == LayerMask.NameToLayer("AutoAngleNodes")
                        && t.name.ToLower().Contains("blue")
                        && t.name.ToLower().Contains("hub")
                    )
                    .position;
                Debug.Log("TURBO_HUB_TARGET " + hub.ToString("F4"));
                Spawn();
                return;
            }
            float t = Time.time - began;
            if (
                Indexer
                && !indexerSeeded
                && t >= 2.25f
                && (
                    !result.indexerCase.EndsWith("-pair") && result.indexerCase != "mixed-floors"
                    || robot.Feeding
                )
            )
                SeedIndexer();
            if (Flag("-turboJam") && !indexerSeeded && t >= 2.25f)
                SeedJam();
            Measure(t);
            // Mesh inspection for the cutaway must run in the editor callback,
            // outside the physics/player loop where imported meshes are unreadable.
            if (
                Indexer
                && result.indexerCase == "lower-pair"
                && result.peakShooterContacts >= 2
                && !capturedOpen
            )
            {
                Capture("indexer-pair-top");
                Capture("indexer-cutaway-side");
                capturedOpen = true;
            }
            if (index == 0 && !capturedClosed)
            {
                if (t < .3f)
                    return;
                result.closedNetSpan = result.netSpan;
                var mesh = robot
                    .GetComponentsInChildren<MeshFilter>()
                    .First(m => m.name == "Intake_Geometry");
                result.closedVisual = robot
                    .transform.InverseTransformPoint(
                        mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center)
                    )
                    .z;
                Capture("closed");
                Capture("closed-top");
                capturedClosed = true;
                began = Time.time;
                Enabled(true);
                return;
            }
            if (robot.PumpMode == Turbo4744.RailState.HardPumping)
            {
                if (!capturedPumpOut && robot.IntakeEncoderRotations > 10.9f)
                {
                    Capture("pump-extended");
                    capturedPumpOut = true;
                }
                if (!capturedPumpIn && robot.IntakeEncoderRotations < 8.2f)
                {
                    Capture("pump-retracted");
                    capturedPumpIn = true;
                }
            }
            input.vx = input.vy = input.omega = 0;
            if (index < 2)
            {
                if (index == 0 && t < .35f)
                    Press();
                else if (t < 1.8f)
                    Press(Key.LeftShift);
                else if (!loaded)
                {
                    loaded = true;
                    Press();
                    if (index == 1)
                    {
                        var fuel = AssetDatabase.LoadAssetAtPath<GameObject>(
                            "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
                        );
                        foreach (
                            float y in Flag("-turboCrowded")
                                ? new[] { .27f, .425f, .58f }
                                : new[] { .27f, .425f }
                        )
                        foreach (float z in new[] { .225f, .375f, .525f })
                        foreach (
                            float x in z < .3f
                                ? new[] { -.075f, .075f }
                                : new[] { -.225f, -.075f, .075f, .225f }
                        )
                            AddBall(
                                Object.Instantiate(
                                    fuel,
                                    robot.transform.TransformPoint(new Vector3(x, y, z)),
                                    Quaternion.identity
                                )
                            );
                    }
                }
                if (index == 0 && t > 2.4f && !capturedOpen)
                {
                    result.openedNetSpan = result.netSpan;
                    Capture("opened");
                    Capture("opened-top");
                    capturedOpen = true;
                }
                if (index == 1 && t > 3.2f && !capturedLoaded)
                {
                    result.payloadAtStart = Retained();
                    Capture("loaded");
                    Capture("loaded-top");
                    capturedLoaded = true;
                }
                if (index == 1 && t > 9.2f && t < 10.2f)
                {
                    result.payloadAfterDrive = Retained();
                    if (t > 9.9f && result.loadedPositions.Count == 0)
                        foreach (var ball in balls.Where(b => b.body))
                            result.loadedPositions.Add(
                                robot
                                    .transform.InverseTransformPoint(ball.body.position)
                                    .ToString("F4")
                            );
                }
                if (t >= 3.3f && t < 4.4f)
                {
                    if (Flag("-turboNativeDrive"))
                        Press(Key.W);
                    else
                        input.vx = 4.5f;
                }
                else if (t >= 5.1f && t < 6.2f)
                {
                    if (Flag("-turboNativeDrive"))
                        Press(Key.S);
                    else
                        input.vx = -4.5f;
                }
                else if (t >= 6.8f && t < 7.8f)
                {
                    if (Flag("-turboNativeDrive"))
                        Press(Key.D);
                    else
                        input.vy = 3f;
                }
                else if (t >= 8.2f && t < 9.2f)
                {
                    if (Flag("-turboNativeDrive"))
                        Press(Key.A);
                    else
                        input.omega = 120;
                }
                else if (t >= 10.2f)
                    Press(Key.Z);
                else if (t >= 3.3f)
                    Press();
            }
            else
            {
                if (
                    (Flag("-turboJam") || Indexer && result.indexerCase.StartsWith("blocked-"))
                    && t > 7
                )
                    foreach (var obstacle in jamFixture)
                        if (obstacle)
                            Object.Destroy(obstacle);
                if (Flag("-turboPulse"))
                {
                    Press();
                    if (t >= 3 && !pulseShoot)
                    {
                        Press(Key.K);
                        Press();
                        pulseShoot = true;
                    }
                    if (t >= 6 && !pulsePump)
                    {
                        Press(Key.K);
                        Press();
                        pulsePump = true;
                    }
                }
                else if (t < 1)
                    Press();
                else if (t < 2.2f)
                    Press(Key.LeftShift);
                else if (t < 3)
                    Press();
                else if (
                    Flag("-turboJam")
                    && !pulsePump
                    && firstRecoveryEndedAt >= 0
                    && t - firstRecoveryEndedAt >= .72f
                )
                {
                    result.pumpRequestRecoveryAge = t - firstRecoveryEndedAt;
                    result.pumpRequestEncoder = robot.IntakeEncoderRotations;
                    Press(Key.K, Key.V);
                    pulsePump = true;
                }
                else if (t >= 6 && !pulsePump && !Flag("-turboJam") && !Flag("-turboAutomaticPump"))
                {
                    Press(Key.K, Key.V);
                    pulsePump = true;
                }
                else if (Flag("-turboMoving") && t > 3.2f && t < 10)
                {
                    // Begin toward field centre. Starting toward the side wall
                    // made the extended intake hit it during an accuracy test.
                    bool inward = ((int)((t - 3.2f) / 1.2f)) % 2 == 0;
                    Press(Key.K, (index % 2 == 0 ? inward : !inward) ? Key.S : Key.W);
                }
                else
                    Press(Flag("-turboAutomaticPump") && Flag("-turboDelivery") ? Key.E : Key.K);
                result.target = robot.autoAim.GetCurrentTarget();
            }
            if (
                t
                < (
                    index < 2 ? 12.5f
                    : Flag("-turboMoving") ? 24f
                    : 16f
                )
            )
                return;
            if (index >= 2)
            {
                if (Flag("-turboDelivery"))
                    foreach (var ball in balls.Where(b => b.body))
                        result.shots.Add(
                            "Final "
                                + balls.IndexOf(ball)
                                + " position "
                                + ball.body.position.ToString("F3")
                                + " landed "
                                + ball.landed
                        );
                foreach (var ball in balls.Where(b => b.body && !b.launched))
                    result.shots.Add(
                        "Remaining "
                            + balls.IndexOf(ball)
                            + " local "
                            + robot
                                .transform.InverseTransformPoint(ball.body.position)
                                .ToString("F3")
                    );
            }
            result.stowed =
                Mathf.Abs(robot.intakeRail.transform.localPosition.z - intakeClosed) < .035f
                && Mathf.Abs(robot.hopperRail.transform.localPosition.z - hopperClosed) < .035f;
            Debug.Log("TURBO_SYSTEMS_CASE " + JsonUtility.ToJson(result));
            Press();
            Cleanup();
            index++;
            if (
                Indexer ? Flag("-turboIndexerCase") || index >= 2 + IndexerCases.Length
                : Flag("-turboJam") ? true
                : Flag("-turboPulse") ? index >= 3
                : Flag("-turboDelivery") ? index >= 6
                : Flag("-turboAutomaticPump") ? index >= 3
                : Flag("-turboMoving") ? index >= 4
                : Flag("-turboCrowded") ? index >= 2
                : Matrix ? index >= 10
                : Ratios != null ? index >= 2 + Ratios.Length
                : Flag("-turboDriveOnly") ? index >= 2
                : Flag("-turboBefore") || Flag("-turboRange") ? index >= 3
                : index >= 7
            )
                Finish();
            else
                Spawn();
        }
        catch (Exception error)
        {
            report.errors.Add(error.ToString());
            Finish();
        }
    }

    private static void MeasureMovingPhysics()
    {
        float t = Time.fixedTime - began;
        if (t < 3.5f || t > 8.5f || t < nextShotSample)
            return;
        nextShotSample = t + .013f;
        foreach (var ball in balls)
        {
            if (!ball.body)
                continue;
            var local = Quaternion.Inverse(body.rotation) * (ball.body.position - body.position);
            if (local.y < .4f || local.z > .15f || local.z < -.7f)
                continue;
            result.shotTrace.Add(
                FormattableString.Invariant(
                    $"{t:F4} ball={balls.IndexOf(ball)} local={local:F4} v={ball.body.velocity:F4} chassis={body.GetPointVelocity(ball.body.position):F4} aim={robot.autoAim.GetRotationError():F3} rpm={robot.CurrentShooterRpm:F1}/{robot.TargetShooterRpm:F1} contacts={robot.shooterContact.Contacts}"
                )
            );
        }
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Log;
        Press();
        report.passed =
            report.errors.Count == 0
            && report.cases.All(c =>
                c.stable
                && c.netSolvers == 1
                && c.netScaleOne
                && (
                    Mathf.Abs(c.range - 5) > .001f
                    || c.fieldContacts.Count == 0 && Mathf.Abs(c.spawnRange - 5) < .001f
                )
                && (
                    c.kind == "drive"
                        ? c.forwardSpeed > 3.3f
                            && c.reverseSpeed > 3.3f
                            && c.strafeSpeed > 2.3f
                            && (
                                c.payload == 0
                                    ? c.closedFrameSpan > .36f
                                        && c.closedFrameSpan < .41f
                                        && c.openedFrameSpan > .59f
                                        && c.openedFrameSpan - c.closedFrameSpan > .20f
                                        && c.closedNetSpan >= c.closedFrameSpan - .02f
                                        && c.closedNetSpan < .8f
                                        && c.openedNetSpan > .59f
                                    : c.payloadAtStart == c.payload
                                        && c.payloadAfterDrive == c.payload
                            )
                    : c.kind == "indexer"
                        ? c.scored == c.seeded
                            && c.seeded > 0
                            && c.lowerEntrances == c.seeded
                            && c.peakIndexerForce <= 320.01f
                            && (
                                c.expectedIdleSideSupports == 0
                                || c.idleSupportSamples > 20
                                    && c.minIdleFuelY > .17f
                                    && c.idleSideSupports == c.expectedIdleSideSupports
                            )
                            && (
                                !c.indexerCase.StartsWith("blocked-")
                                || c.jamRecoveries > 0
                                    && c.obstructionContacts > 0
                                    && c.firstLaunchTime >= 7
                                    && c.backoffSamples > 0
                                    && c.backoffSeconds > .45f
                                    && c.backoffSeconds < .6f
                            )
                            && (c.indexerCase != "upper-pair" || c.peakUpperContacts >= 2)
                            && (
                                !c.indexerCase.EndsWith("pair")
                                || c.peakShooterContacts >= 2
                                    && c.pairExitDelta >= 0
                                    && c.pairExitDelta <= .05f
                            )
                    : c.kind == "jam"
                        ? c.jamRecoveries > 0
                            && c.obstructionContacts > 0
                            && c.firstLaunchTime >= 7
                            && c.backoffSamples > 0
                            && c.backoffSeconds > .45f
                            && c.backoffSeconds < .6f
                            && c.scored == 1
                            && c.pumpRequestRecoveryAge >= .72f
                            && c.pumpRequestRecoveryAge < .8f
                            && c.pumpRequestEncoder < 10.5f
                            && c.hardPumpCycles == 3
                    : Flag("-turboAutomaticPump")
                        ? (Flag("-turboDelivery") ? c.delivered == c.seeded : c.scored == c.seeded)
                            && c.hardPumpCycles == 0
                            && !c.pumpedWhileCollecting
                            && !c.pumpedAfterEmpty
                            && c.hopperFloorContained
                            && (
                                c.seeded == 0
                                || c.automaticPumpSamples > 10
                                    && c.automaticPreparePumpSamples > 0
                                    // Sparse loads can empty before the opening-to-low
                                    // travel finishes. Require real motion of both
                                    // slides while loaded, without requiring a full cycle.
                                    && c.automaticPumpMax - c.automaticPumpMin > .020f
                                    && c.automaticBinMax - c.automaticBinMin > .018f
                            )
                    : Flag("-turboDelivery") ? c.delivered >= 6
                    : c.scored >= 7
                        && c.hardPumpCycles == 3
                        && c.maxPump - c.minPump > .045f
                        && c.minPump > .145f
                        && c.maxPump < .245f
                )
            );
        Directory.CreateDirectory(Out);
        File.WriteAllText(
            Out + "/systems-" + report.label + ".json",
            JsonUtility.ToJson(report, true)
        );
        SessionState.SetInt("Turbo.SystemsExit", Flag("-turboStrict") && !report.passed ? 1 : 0);
        EditorApplication.ExitPlaymode();
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
}

/// <summary>Observe real passive support before powered feeding begins.</summary>
public sealed class Turbo4744GuideProbe : MonoBehaviour
{
    public Action supported;
    public Action<string> observed;
    public Action<Collision> collided;
    private readonly HashSet<string> seen = new HashSet<string>();

    private void OnCollisionEnter(Collision contact) => OnCollisionStay(contact);

    private void OnCollisionStay(Collision contact)
    {
        collided?.Invoke(contact);
        if (seen.Add(contact.collider.name))
            observed?.Invoke(
                contact.collider.name
                    + " normal "
                    + (
                        contact.contactCount > 0
                            ? contact.GetContact(0).normal.ToString("F3")
                            : "none"
                    )
            );
        if (contact.collider.name.StartsWith("SideIndexer"))
            for (int i = 0; i < contact.contactCount; i++)
                if (contact.GetContact(i).normal.y > .1f)
                {
                    supported?.Invoke();
                    break;
                }
    }
}

/// <summary>Observe the first carpet impact even when an editor frame spans an entire bounce.</summary>
public sealed class Turbo4744DeliveryProbe : MonoBehaviour
{
    public Action<Vector3> landing;

    private void OnCollisionEnter(Collision contact)
    {
        if (contact.collider.name == "Carpet")
            landing?.Invoke(GetComponent<Rigidbody>().position);
    }
}

[DefaultExecutionOrder(1200)]
public sealed class Turbo4744IndexerProbe : MonoBehaviour
{
    public Action sample;

    private void FixedUpdate() => sample?.Invoke();
}

[DefaultExecutionOrder(1075)]
public sealed class Turbo4744AcceleratorBrakeProbe : MonoBehaviour
{
    public Turbo4744ShooterContact contact;

    private void FixedUpdate()
    {
        if (contact.brakeWhenStopped)
            contact.SetRpm(0);
    }
}

[DefaultExecutionOrder(-1000)]
public sealed class Turbo4744BenchDriver : MonoBehaviour
{
    public DriveController drive;
    public float vx,
        vy,
        omega;

    private void FixedUpdate()
    {
        if (drive && (vx != 0 || vy != 0 || omega != 0))
            drive.OverrideInput(vx, vy, omega, DriveController.DrivePerspective.RobotRelative);
    }
}
