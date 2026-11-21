using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Games.Rebuilt.FieldScripts;
using Games.Rebuilt.GamePieceSystem;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Ninjas4744.MoSim
{
    /// <summary>Opt-in native-game input replay. The ordinary robot never adds this component.
    /// It drives only through a scoped keyboard and records the game's unmodified field physics.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class Turbo4744NativeReplay : MonoBehaviour
    {
        [Serializable]
        private sealed class Request
        {
            public string label = "native-cycle";
            public string shootKey = "K";
            public float collectSeconds = 1.8f;
            public float retreatSeconds = 1.8f;
            public float shootWhileRetreatingSeconds = .2f;
            public float observeSeconds = 15;
            public float strafeInSeconds,
                strafeOutSeconds;
            public float collectionBackoffAfterSeconds,
                collectionBackoffSeconds;
            public bool returnToOwnZone;
            public float hardPumpAfterSeconds = 5;
            public bool waitForTeleop;
            public int targetCaptured,
                collectGear,
                returnGear;
            public float returnLane;
            public bool waitForActiveHub,
                disableJointProjection;
            public bool stopInterpolation;
            public bool discreteChassis;
            public bool solidBumper;
            public float collectionPumpAfterSeconds;
            public float collectionSettleSeconds;
            public float minimumActiveSeconds = 12;
            public float repeatHardPumpEverySeconds;
            public bool stowForReturn;
            public float railForceOverride;
            public float railDampingOverride;
            public float netRestScaleOverride,
                netStretchOverride,
                netShearOverride;
            public bool compliantFuel;
            public float netStrandRadiusOverride;
            public bool netEnvironmentContacts;
            public bool requireTrenchSuccess;
            public float stowTolerance = .015f;
            public int minimumTransitLoad = 50,
                maximumTransitLoad = 60;
        }

        [Serializable]
        private sealed class Sample
        {
            public float seconds,
                tilt,
                speed,
                intake,
                rpm,
                targetRpm,
                indexerForce;
            public Vector3 position,
                angularVelocity;
            public string state,
                pump;
            public bool feeding;
            public int captured,
                indexerContacts,
                acceleratorContacts,
                shooterContacts;
        }

        [Serializable]
        private sealed class Report
        {
            public Request request;
            public string label;
            public string alliance;
            public string activeHubAtShot;
            public bool jointProjectionDisabled;
            public bool stowRequested,
                stowedBeforeReturn,
                stowTimedOut;
            public float stowSeconds,
                intakeExtensionAtReturn,
                hopperExtensionAtReturn,
                railForce,
                railDamping;
            public List<string> components = new List<string>();
            public List<string> colliderAudit = new List<string>();
            public List<string> launches = new List<string>();
            public List<string> remainingFuel = new List<string>();
            public List<string> deliveryLandings = new List<string>();
            public List<string> transitEscapes = new List<string>();
            public List<string> inletDepartures = new List<string>();
            public List<string> stowFuel = new List<string>();
            public int fullyStoredBeforeStow,
                enteringBeforeStow,
                storedRetainedAfterStow;
            public List<string> fieldGeometry = new List<string>();
            public List<string> netContactTrace = new List<string>();
            public int transitStartCount,
                transitEndCount;
            public bool transitCompleted,
                trenchPassed,
                netEnvironmentContacts;
            public string failureReason;
            public int netFieldContacts,
                netContactSteps,
                netMaximumOverlaps;
            public int compliantContacts;
            public float maximumFuelCompression,
                maximumTrenchHoldSeconds;
            public float intakeForceBudget,
                intakeTractionBudget,
                netStrandRadius,
                netRestScale;
            public int delivered;
            public int peakLowerContacts,
                peakUpperContacts,
                peakShooterContacts;
            public float peakIndexerForce;
            public float maxBumperPenetration,
                maxPoseResidual;
            public string frameColumns =
                "seconds,tilt,captured,x,y,z,vx,vy,vz,wx,wy,wz,state,pump,feeding,rpm,targetRpm,indexerContacts,acceleratorContacts,shooterContacts,indexerForce,intakeExtension,hopperExtension,hopperVelocity,hopperTargetVelocity,transitRetained,netMaxLocalY,fuelMaxLocalY,netFieldContacts";
            public List<string> frames = new List<string>();
            public string environment =
                "Installed MoSim player; complete original field; keyboard replay only";
            public string runtimeVersion = "0.6.2";
            public float physicsStep,
                maxTilt;
            public bool completed,
                overturned;
            public int fieldFuel,
                peakCaptured;
            public int capturedAtShot,
                scoredCaptured;
            public int releasedCaptured,
                remainingCaptured;
            public bool shotInOwnZone;
            public float maxCollectFrameMs,
                maxReturnFrameMs,
                maxShootFrameMs;
            public List<string> slowFrames = new List<string>();
            public List<Sample> samples = new List<Sample>();
            public List<string> contacts = new List<string>();
        }

        private Turbo4744 robot;
        private Rigidbody body;
        private Turbo4744FuelKeeper keeper;
        private PlayerInput player;
        private Keyboard keyboard;
        private InputDevice[] originalDevices;
        private bool originalAutoSwitch;
        private Request request;
        private Report report;
        private RebuiltGamePieceController[] fuel;
        private string output;
        private float began = -1,
            nextSample,
            shotAt = -1,
            collectionStopped = -1;
        private bool laneReady;
        private bool arrivedHome;
        private float previousSampleTime = -1,
            nextBumperAudit;
        private Vector3 previousSamplePosition,
            previousSampleVelocity;
        private bool recordedPoseGap,
            showBoxes;
        private readonly HashSet<RebuiltGamePieceController> capturedFuel =
            new HashSet<RebuiltGamePieceController>();
        private readonly HashSet<RebuiltGamePieceController> scoredFuel =
            new HashSet<RebuiltGamePieceController>();
        private readonly HashSet<RebuiltGamePieceController> releasedFuel =
            new HashSet<RebuiltGamePieceController>();
        private readonly HashSet<RebuiltGamePieceController> muzzleFuel =
            new HashSet<RebuiltGamePieceController>();
        private readonly Dictionary<RebuiltGamePieceController, float> previousMuzzle =
            new Dictionary<RebuiltGamePieceController, float>();
        private readonly HashSet<RebuiltGamePieceController> landedFuel =
            new HashSet<RebuiltGamePieceController>();
        private readonly HashSet<RebuiltGamePieceController> returnFuel =
            new HashSet<RebuiltGamePieceController>();
        private readonly HashSet<RebuiltGamePieceController> storedBeforeStow =
            new HashSet<RebuiltGamePieceController>();
        private readonly HashSet<RebuiltGamePieceController> departedInlet =
            new HashSet<RebuiltGamePieceController>();
        private readonly HashSet<RebuiltGamePieceController> escapedFuel =
            new HashSet<RebuiltGamePieceController>();
        private readonly HashSet<RebuiltGamePieceController> transitFuel =
            new HashSet<RebuiltGamePieceController>();
        private Turbo4744NetEnvironment netEnvironment;
        private NetSolver net;
        private Turbo4744FuelCompliance compliance;
        private float nextNetTrace;
        private float trenchHoldSeconds;
        private bool capturedTrenchEntry,
            capturedTrenchClear;
        private static readonly FieldInfo LastScored = typeof(RebuiltGamePieceController).GetField(
            "_lastScoredTime",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        private bool capturedTiltEvidence;
        private int previousPhase = -1;
        private bool finished;
        private float originalRailForce,
            originalRailDamping;

        public static void AttachIfRequested(Turbo4744 robot)
        {
            string path = Path.Combine(
                Application.persistentDataPath,
                "Turbo4744-native-replay.json"
            );
            if (
                !Application.isEditor
                && File.Exists(path)
                && !robot.GetComponent<Turbo4744NativeReplay>()
            )
            {
                var replay = robot.gameObject.AddComponent<Turbo4744NativeReplay>();
                replay.robot = robot;
                replay.request = JsonUtility.FromJson<Request>(File.ReadAllText(path));
            }
        }

        private void Begin()
        {
            if (request.stowTolerance <= 0)
                request.stowTolerance = .015f;
            if (request.minimumTransitLoad <= 0)
                request.minimumTransitLoad = 50;
            if (request.maximumTransitLoad <= 0)
                request.maximumTransitLoad = 60;
            body = robot.GetComponent<Rigidbody>();
            keeper = robot.GetComponent<Turbo4744FuelKeeper>();
            player = robot.GetComponent<PlayerInput>();
            originalDevices = player.actions.devices?.ToArray();
            originalAutoSwitch = player.neverAutoSwitchControlSchemes;
            keyboard = InputSystem.AddDevice<Keyboard>("Turbo native replay keyboard");
            player.neverAutoSwitchControlSchemes = true;
            player.actions.devices = new InputDevice[] { keyboard };
            fuel = FindObjectsByType<RebuiltGamePieceController>(FindObjectsSortMode.None);
            report = new Report
            {
                request = request,
                label = request.label,
                alliance = robot.Alliance.ToString(),
                physicsStep = Time.fixedDeltaTime,
                fieldFuel = fuel.Length,
            };
            report.jointProjectionDisabled = request.disableJointProjection;
            report.stowRequested = request.stowForReturn;
            net = robot.roofNet.GetComponent<NetSolver>();
            report.intakeForceBudget = robot.intakeContact.maxTotalForce;
            report.intakeTractionBudget = robot.intakeContact.maxTotalTractionForce;
            report.netStrandRadius = net.collisionRadius;
            report.netRestScale = net.restLengthScale;
            netEnvironment = net.GetComponent<Turbo4744NetEnvironment>();
            compliance = robot.GetComponent<Turbo4744FuelCompliance>();
            if (request.compliantFuel)
                compliance = compliance
                    ? compliance
                    : robot.gameObject.AddComponent<Turbo4744FuelCompliance>();
            if (request.netRestScaleOverride > 0)
            {
                net.restLengthScale = request.netRestScaleOverride;
                net.stretchCompliance = request.netStretchOverride;
                net.shearCompliance = request.netShearOverride;
                if (request.netStrandRadiusOverride > 0)
                    net.collisionRadius = request.netStrandRadiusOverride;
                net.Rebuild();
            }
            if (request.netEnvironmentContacts && !netEnvironment)
                netEnvironment = net.gameObject.AddComponent<Turbo4744NetEnvironment>();
            report.netEnvironmentContacts = netEnvironment;
            foreach (int side in new[] { -1, 1 })
            foreach (int lane in new[] { -1, 1 })
            {
                var origin = new Vector3(side * 3.65f, .30f, lane * 3.3758f);
                foreach (
                    var hit in Physics.RaycastAll(
                        origin,
                        Vector3.up,
                        1.2f,
                        LayerMask.GetMask("Default", "Field"),
                        QueryTriggerInteraction.Ignore
                    )
                )
                    if (!hit.collider.attachedRigidbody)
                        report.fieldGeometry.Add(
                            $"ray={origin:F4} hitY={hit.point.y:F4} collider={hit.collider.name} type={hit.collider.GetType().Name} bounds={hit.collider.bounds}"
                        );
            }
            originalRailForce = robot.railMaxForce;
            originalRailDamping = robot.railVelocityDamping;
            if (request.railForceOverride > 0)
                robot.railMaxForce = request.railForceOverride;
            if (request.railDampingOverride > 0)
                robot.railVelocityDamping = request.railDampingOverride;
            report.railForce = robot.railMaxForce;
            report.railDamping = robot.railVelocityDamping;
            foreach (var c in robot.GetComponents<MonoBehaviour>())
                report.components.Add(c.GetType().FullName + " enabled=" + c.enabled);
            if (request.disableJointProjection)
            {
                robot.intakeRail.projectionMode = JointProjectionMode.None;
                robot.hopperRail.projectionMode = JointProjectionMode.None;
            }
            if (request.stopInterpolation)
                robot.GetComponent<CustomRigidbodyInterpolation>()?.StopInterpolation();
            if (request.discreteChassis)
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            if (request.solidBumper)
            {
                // Candidate CAD perimeter: do not turn the filled bounds trigger into a solid box.
                var bounds = robot.GetComponent<BoxCollider>();
                const float thickness = .0572f;
                foreach (int side in new[] { -1, 1 })
                foreach (bool across in new[] { false, true })
                {
                    var go = new GameObject(
                        "Candidate bumper " + (across ? "end " : "side ") + side
                    );
                    go.transform.SetParent(robot.transform, false);
                    go.layer = robot.gameObject.layer;
                    var c = go.AddComponent<BoxCollider>();
                    c.center =
                        bounds.center
                        + (
                            across
                                ? Vector3.forward * (bounds.size.z - thickness)
                                : Vector3.right * (bounds.size.x - thickness)
                        ) * (.5f * side);
                    c.size = across
                        ? new Vector3(bounds.size.x - 2 * thickness, bounds.size.y, thickness)
                        : new Vector3(thickness, bounds.size.y, bounds.size.z);
                    foreach (var own in robot.GetComponentsInChildren<Collider>())
                        if (own != c && own.attachedRigidbody && own.attachedRigidbody != body)
                            Physics.IgnoreCollision(c, own);
                }
            }
            AuditColliders("begin");
            output = Path.Combine(
                Application.persistentDataPath,
                "Turbo4744-native-replay-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"
            );
            began = Time.fixedTime;
            Debug.Log(
                "[Turbo native replay] Started with " + fuel.Length + " field fuel; " + output
            );
        }

        private void FixedUpdate()
        {
            if (
                finished
                || !robot
                || !BaseGameManager.Instance
                || BaseGameManager.Instance.RobotState != RobotState.Enabled
            )
                return;
            if (began < 0)
            {
                if (request.waitForTeleop && BaseGameManager.Instance.GameState != GameState.Teleop)
                    return;
                Begin();
            }
            float t = Time.fixedTime - began;
            float collectEnd = 2.5f + request.collectSeconds;
            if (request.targetCaptured > 0)
            {
                if (
                    collectionStopped < 0
                    && t >= 2.5f
                    && (report.remainingCaptured >= request.targetCaptured || t >= collectEnd)
                )
                    collectionStopped = t;
                collectEnd =
                    collectionStopped < 0
                        ? float.PositiveInfinity
                        : collectionStopped + request.collectionSettleSeconds;
            }
            float retreatEnd = collectEnd + request.retreatSeconds;
            float ownSign = robot.Alliance == Alliance.Blue ? 1 : -1;
            if (request.returnToOwnZone)
            {
                arrivedHome |= ownSign * body.position.x >= 5.8f;
                if (
                    request.requireTrenchSuccess
                    && report.stowedBeforeReturn
                    && !arrivedHome
                    && t >= collectEnd + request.retreatSeconds
                )
                {
                    report.failureReason = "Transit timed out before reaching the alliance zone";
                    Finish(false);
                    return;
                }
                bool active =
                    (
                        RebuiltShifts.ActiveHub == ActiveHub.Both
                        || RebuiltShifts.ActiveHub
                            == (robot.Alliance == Alliance.Blue ? ActiveHub.Blue : ActiveHub.Red)
                    )
                    && RebuiltShifts.SecondsUntilNextShift >= request.minimumActiveSeconds;
                if (
                    shotAt < 0
                    && t >= collectEnd
                    && (
                        (arrivedHome && (!request.waitForActiveHub || active))
                        || (!arrivedHome && t >= retreatEnd)
                    )
                )
                    shotAt = t;
                retreatEnd = shotAt < 0 ? float.PositiveInfinity : shotAt;
            }
            float shootEnd = retreatEnd + request.shootWhileRetreatingSeconds;
            int phase =
                t < 2.5f ? 0
                : t < collectEnd ? 1
                : t < retreatEnd ? 2
                : t < shootEnd ? 3
                : 4;
            Key[] keys =
                phase == 0 ? new[] { Key.LeftShift }
                : phase == 1
                    ? (
                        t - 2.5f < request.strafeInSeconds
                            ? new[] { Key.W, Key.A }
                            : new[] { Key.W }
                    )
                : phase == 2
                    ? (
                        t - collectEnd < request.strafeOutSeconds
                            ? new[] { Key.S, Key.D }
                            : new[] { Key.S }
                    )
                : phase == 3 ? new[] { Key.S, (Key)Enum.Parse(typeof(Key), request.shootKey) }
                : t - shootEnd >= request.hardPumpAfterSeconds
                && t - shootEnd < request.hardPumpAfterSeconds + .2f
                    ? new[] { Key.V }
                : Array.Empty<Key>();
            if (
                phase == 1
                && collectionStopped < 0
                && request.collectionBackoffAfterSeconds > 0
                && t - 2.5f >= request.collectionBackoffAfterSeconds
                && t - 2.5f
                    < request.collectionBackoffAfterSeconds + request.collectionBackoffSeconds
            )
                keys = new[] { Key.S };
            if (phase == 1 && collectionStopped >= 0 && t >= collectionStopped)
                keys = Array.Empty<Key>();
            if (
                phase == 4
                && request.repeatHardPumpEverySeconds > 0
                && t - shootEnd >= request.hardPumpAfterSeconds
                && (t - shootEnd - request.hardPumpAfterSeconds)
                    % request.repeatHardPumpEverySeconds
                    < .2f
            )
                keys = new[] { Key.V };
            if (phase == 2 && request.returnLane != 0)
            {
                float dz = request.returnLane - body.position.z;
                laneReady |= Mathf.Abs(dz) < .10f && Mathf.Abs(body.velocity.z) < .3f;
                var routeKeys = new List<Key>();
                if (laneReady)
                    routeKeys.Add(Key.S);
                if (Mathf.Abs(dz) > (laneReady ? .08f : .10f))
                    routeKeys.Add((dz > 0) == (ownSign < 0) ? Key.A : Key.D);
                int gear = !laneReady && Mathf.Abs(dz) < .5f ? 1 : request.returnGear;
                if (gear > 0)
                    routeKeys.Add(
                        gear == 1 ? Key.Digit1
                        : gear == 2 ? Key.Digit2
                        : Key.Digit3
                    );
                keys = routeKeys.ToArray();
            }
            if (phase == 2 && arrivedHome)
                keys = Array.Empty<Key>();
            else if (phase == 1 && request.collectGear > 0)
                keys = keys.Concat(
                        new[]
                        {
                            request.collectGear == 1 ? Key.Digit1
                            : request.collectGear == 2 ? Key.Digit2
                            : Key.Digit3,
                        }
                    )
                    .ToArray();
            if (
                phase == 1
                && request.collectionPumpAfterSeconds > 0
                && t - 2.5f >= request.collectionPumpAfterSeconds
                && t - 2.5f < request.collectionPumpAfterSeconds + .2f
            )
                keys = keys.Concat(new[] { Key.V }).ToArray();
            if (phase == 2 && request.stowForReturn && !report.stowedBeforeReturn)
            {
                float intakeExtension = RailExtension(robot.intakeRail, robot.intakeTravel);
                float hopperExtension = RailExtension(robot.hopperRail, robot.hopperTravel);
                report.stowSeconds = t - collectEnd;
                if (
                    report.stowSeconds >= .25f
                    && Mathf.Abs(intakeExtension) < request.stowTolerance
                    && Mathf.Abs(hopperExtension) < request.stowTolerance
                )
                {
                    report.stowedBeforeReturn = true;
                    report.intakeExtensionAtReturn = intakeExtension;
                    report.hopperExtensionAtReturn = hopperExtension;
                    foreach (var piece in fuel)
                        if (piece && InsideTransit(piece.GetComponent<Rigidbody>().position))
                            transitFuel.Add(piece);
                    report.transitStartCount = transitFuel.Count;
                    returnFuel.UnionWith(transitFuel);
                    report.storedRetainedAfterStow = storedBeforeStow.Count(piece =>
                        piece && InsideTransit(piece.GetComponent<Rigidbody>().position)
                    );
                    if (
                        request.requireTrenchSuccess
                        && (
                            transitFuel.Count < request.minimumTransitLoad
                            || transitFuel.Count > request.maximumTransitLoad
                        )
                    )
                    {
                        report.failureReason =
                            "Actual stowed load is outside the requested transit range";
                        Finish(false);
                        return;
                    }
                }
                else
                {
                    keys = new[] { Key.Z };
                    if (report.stowSeconds > 8)
                    {
                        report.stowTimedOut = true;
                        report.failureReason = "Stow timed out before full retraction";
                        report.intakeExtensionAtReturn = intakeExtension;
                        report.hopperExtensionAtReturn = hopperExtension;
                        Finish(false);
                        return;
                    }
                }
            }
            if (report.stowedBeforeReturn && arrivedHome && !report.transitCompleted)
            {
                report.transitCompleted = true;
                report.transitEndCount = TransitRetained();
                report.trenchPassed =
                    report.transitStartCount >= request.minimumTransitLoad
                    && report.transitStartCount <= request.maximumTransitLoad
                    && report.transitEndCount == report.transitStartCount
                    && report.storedRetainedAfterStow == report.fullyStoredBeforeStow
                    && report.transitEscapes.Count == 0
                    && report.maximumTrenchHoldSeconds < .25f;
                if (!report.trenchPassed && request.requireTrenchSuccess)
                    report.failureReason = "Transit lost stored fuel";
            }
            if (report.stowedBeforeReturn && !arrivedHome && laneReady && request.returnLane != 0)
            {
                bool underBar = Mathf.Abs(ownSign * body.position.x - 3.65f) < .95f;
                trenchHoldSeconds =
                    underBar && Mathf.Abs(body.velocity.x) < .1f
                        ? trenchHoldSeconds + Time.fixedDeltaTime
                        : 0;
                report.maximumTrenchHoldSeconds = Mathf.Max(
                    report.maximumTrenchHoldSeconds,
                    trenchHoldSeconds
                );
                if (!capturedTrenchEntry && ownSign * body.position.x > 2.9f)
                {
                    capturedTrenchEntry = true;
                    ScreenCapture.CaptureScreenshot(
                        Path.ChangeExtension(output, null) + "-trench-entry.png"
                    );
                }
                if (!capturedTrenchClear && ownSign * body.position.x > 4.7f)
                {
                    capturedTrenchClear = true;
                    ScreenCapture.CaptureScreenshot(
                        Path.ChangeExtension(output, null) + "-trench-clear.png"
                    );
                }
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
            if (phase != previousPhase)
            {
                previousPhase = phase;
                if (phase == 2)
                {
                    foreach (var piece in fuel)
                    {
                        if (!piece)
                            continue;
                        var p = robot.transform.InverseTransformPoint(
                            piece.GetComponent<Rigidbody>().position
                        );
                        if (
                            Mathf.Abs(p.x) < .36f
                            && p.y > .20f
                            && p.y < 1
                            && p.z > -.10f
                            && p.z < .64f
                        )
                        {
                            returnFuel.Add(piece);
                            var rb = piece.GetComponent<Rigidbody>();
                            bool captured =
                                keeper.IsCaptured(rb) || robot.indexer.HasEnteredShooter(rb);
                            if (captured)
                            {
                                report.fullyStoredBeforeStow++;
                                storedBeforeStow.Add(piece);
                            }
                            else
                                report.enteringBeforeStow++;
                            report.stowFuel.Add(
                                FormattableString.Invariant(
                                    $"id={piece.GetInstanceID()} local={p:F3} captured={captured}"
                                )
                            );
                        }
                    }
                    AuditColliders("after collection");
                    ScreenCapture.CaptureScreenshot(
                        Path.ChangeExtension(output, null) + "-loaded.png"
                    );
                }
                Debug.Log(
                    "[Turbo native replay] Phase "
                        + phase
                        + " at "
                        + t.ToString("F3")
                        + " position "
                        + body.position.ToString("F3")
                );
            }
            if (phase == 3 && report.capturedAtShot == 0)
            {
                report.capturedAtShot =
                    report.samples.Count > 0
                        ? report.samples[report.samples.Count - 1].captured
                        : 0;
                report.shotInOwnZone = 8.27f - ownSign * body.position.x < 3.6f;
                report.activeHubAtShot = RebuiltShifts.ActiveHub.ToString();
                ScreenCapture.CaptureScreenshot(Path.ChangeExtension(output, null) + "-shoot.png");
            }
            float tilt = Vector3.Angle(robot.transform.up, Vector3.up);
            report.peakLowerContacts = Mathf.Max(
                report.peakLowerContacts,
                robot.indexer.LowerContacts
            );
            report.peakUpperContacts = Mathf.Max(
                report.peakUpperContacts,
                robot.indexer.UpperContacts
            );
            report.peakShooterContacts = Mathf.Max(
                report.peakShooterContacts,
                robot.shooterContact.Contacts
            );
            report.peakIndexerForce = Mathf.Max(
                report.peakIndexerForce,
                robot.indexer.AppliedForce
            );
            if (phase == 2)
                foreach (var piece in returnFuel)
                {
                    if (!piece)
                        continue;
                    bool stored = storedBeforeStow.Contains(piece) || transitFuel.Contains(piece);
                    if (stored ? escapedFuel.Contains(piece) : departedInlet.Contains(piece))
                        continue;
                    var rb = piece.GetComponent<Rigidbody>();
                    var p = robot.transform.InverseTransformPoint(rb.position);
                    if (
                        Mathf.Abs(p.x) > .5f
                        || p.z > .9f
                        || p.z < -.45f
                        || p.y > 1.1f
                        || p.y < .05f
                    )
                    {
                        string departure = FormattableString.Invariant(
                            $"{t:F3} id={piece.GetInstanceID()} local={p:F3} world={rb.position:F3} v={rb.velocity:F3} chassis={body.position:F3}"
                        );
                        if (stored)
                        {
                            escapedFuel.Add(piece);
                            report.transitEscapes.Add(departure);
                        }
                        else
                        {
                            departedInlet.Add(piece);
                            report.inletDepartures.Add(departure);
                        }
                    }
                }
            report.maxTilt = Mathf.Max(report.maxTilt, tilt);
            report.overturned |= tilt > 90;
            if (tilt > 20 && !capturedTiltEvidence)
            {
                capturedTiltEvidence = true;
                ScreenCapture.CaptureScreenshot(Path.ChangeExtension(output, null) + "-tilt.png");
            }
            if (phase >= 3)
                foreach (var piece in capturedFuel)
                {
                    if (!piece)
                        continue;
                    var rb = piece.GetComponent<Rigidbody>();
                    var local = robot.shooterContact.transform.InverseTransformPoint(
                        rb.worldCenterOfMass
                    );
                    float along = -local.z;
                    if (
                        !muzzleFuel.Contains(piece)
                        && robot.indexer.HasEnteredShooter(rb)
                        && previousMuzzle.TryGetValue(piece, out float oldAlong)
                        && oldAlong <= robot.shooterContact.contactFar
                        && along > robot.shooterContact.contactFar
                        && Mathf.Abs(local.y) < robot.shooterContact.normalClearance * 2
                        && Mathf.Abs(local.x) < robot.shooterContact.halfWidth
                    )
                    {
                        muzzleFuel.Add(piece);
                        report.launches.Add(
                            FormattableString.Invariant(
                                $"{t:F4} id={piece.GetInstanceID()} p={rb.position:F3} v={rb.velocity:F3} local={local:F3} distance={robot.ShotDistance:F3} rpm={robot.CurrentShooterRpm:F1} aim={robot.autoAim.GetRotationError():F3}"
                            )
                        );
                    }
                    previousMuzzle[piece] = along;
                    if (
                        request.shootKey == "E"
                        && muzzleFuel.Contains(piece)
                        && rb.position.y < .16f
                        && rb.velocity.y < 0
                        && landedFuel.Add(piece)
                    )
                    {
                        var delta = rb.position - robot.AimTarget;
                        delta.y = 0;
                        bool delivered =
                            delta.magnitude < 1.25f
                            && Mathf.Abs(rb.position.x) < 8.15f
                            && Mathf.Abs(rb.position.z) < 3.95f;
                        if (delivered)
                            report.delivered++;
                        report.deliveryLandings.Add(
                            FormattableString.Invariant(
                                $"id={piece.GetInstanceID()} p={rb.position:F3} target={robot.AimTarget:F3} error={delta.magnitude:F3} delivered={delivered}"
                            )
                        );
                    }
                }
            if (t >= nextSample)
            {
                nextSample = t + .02f;
                int captured = 0;
                foreach (var piece in fuel)
                {
                    if (!piece)
                        continue;
                    var p = robot.transform.InverseTransformPoint(
                        piece.GetComponent<Rigidbody>().position
                    );
                    bool physicallyStored =
                        Mathf.Abs(p.x) < .36f
                        && p.y > .20f
                        && p.y < 1.0f
                        && p.z > -.10f
                        && p.z < .64f;
                    if (physicallyStored)
                    {
                        captured++;
                        capturedFuel.Add(piece);
                    }
                }
                foreach (var piece in capturedFuel)
                {
                    if (piece && previousPhase >= 3)
                    {
                        var rb = piece.GetComponent<Rigidbody>();
                        var p = robot.transform.InverseTransformPoint(rb.position);
                        if (p.z < -.18f && p.y > .55f && rb.velocity.y > 1)
                            releasedFuel.Add(piece);
                    }
                    if (
                        piece
                        && LastScored != null
                        && previousPhase >= 3
                        && (float)LastScored.GetValue(piece) >= began + retreatEnd
                    )
                        scoredFuel.Add(piece);
                }
                report.scoredCaptured = scoredFuel.Count;
                report.releasedCaptured = releasedFuel.Count;
                report.remainingCaptured = captured;
                var p0 = body.position;
                var v0 = body.velocity;
                var w0 = body.angularVelocity;
                if (previousSampleTime >= 0)
                {
                    var expected = (v0 + previousSampleVelocity) * .5f * (t - previousSampleTime);
                    float residual = ((p0 - previousSamplePosition) - expected).magnitude;
                    report.maxPoseResidual = Mathf.Max(report.maxPoseResidual, residual);
                    if (residual > .03f && !recordedPoseGap && captured >= 20)
                    {
                        recordedPoseGap = true;
                        AuditColliders("first pose gap " + t.ToString("F3"));
                    }
                }
                previousSampleTime = t;
                previousSamplePosition = p0;
                previousSampleVelocity = v0;
                report.frames.Add(
                    FormattableString.Invariant(
                        $"{t:F4},{tilt:F3},{captured},{p0.x:F4},{p0.y:F4},{p0.z:F4},{v0.x:F3},{v0.y:F3},{v0.z:F3},{w0.x:F3},{w0.y:F3},{w0.z:F3},{robot.ShootingState},{robot.PumpMode},{robot.Feeding},{robot.CurrentShooterRpm:F1},{robot.TargetShooterRpm:F1},{robot.indexer.Contacts},{robot.acceleratorContact.Contacts},{robot.shooterContact.Contacts},{robot.indexer.AppliedForce:F1},{RailExtension(robot.intakeRail, robot.intakeTravel):F4},{RailExtension(robot.hopperRail, robot.hopperTravel):F4},{RailVelocity(robot.hopperRail):F4},{-robot.hopperRail.targetVelocity.z:F4},{TransitRetained()},{NetMaximumHeight():F4},{FuelMaximumHeight():F4},{(netEnvironment ? netEnvironment.ContactNodes : 0)}"
                    )
                );
                report.peakCaptured = Mathf.Max(report.peakCaptured, captured);
                report.samples.Add(
                    new Sample
                    {
                        seconds = t,
                        tilt = tilt,
                        speed = body.velocity.magnitude,
                        position = body.position,
                        angularVelocity = body.angularVelocity,
                        intake = robot.IntakeEncoderRotations,
                        state = robot.ShootingState.ToString(),
                        pump = robot.PumpMode.ToString(),
                        feeding = robot.Feeding,
                        rpm = robot.CurrentShooterRpm,
                        targetRpm = robot.TargetShooterRpm,
                        captured = captured,
                        indexerForce = robot.indexer.AppliedForce,
                        indexerContacts = robot.indexer.Contacts,
                        acceleratorContacts = robot.acceleratorContact.Contacts,
                        shooterContacts = robot.shooterContact.Contacts,
                    }
                );
            }
            if (netEnvironment)
            {
                report.netFieldContacts = netEnvironment.TotalContacts;
                report.netContactSteps = netEnvironment.Steps;
                report.netMaximumOverlaps = netEnvironment.MaximumOverlaps;
                if (netEnvironment.ContactNodes > 0 && t >= nextNetTrace)
                {
                    nextNetTrace = t + .1f;
                    report.netContactTrace.Add(
                        $"{t:F4} nodes={netEnvironment.ContactNodes} total={netEnvironment.TotalContacts} depth={netEnvironment.MaximumPenetration:F4} p={body.position:F3}"
                    );
                }
            }
            if (compliance)
            {
                report.compliantContacts = compliance.ModifiedContacts;
                report.maximumFuelCompression = compliance.MaximumObservedCompression;
            }
            if (t >= nextBumperAudit)
            {
                nextBumperAudit = t + .1f;
                var bumper = robot.GetComponent<BoxCollider>();
                foreach (var piece in fuel)
                {
                    if (!piece || bumper.bounds.SqrDistance(piece.transform.position) > .08f * .08f)
                        continue;
                    foreach (var sphere in piece.GetComponentsInChildren<SphereCollider>())
                        if (
                            sphere.enabled
                            && !sphere.isTrigger
                            && Physics.ComputePenetration(
                                bumper,
                                bumper.transform.position,
                                bumper.transform.rotation,
                                sphere,
                                sphere.transform.position,
                                sphere.transform.rotation,
                                out var direction,
                                out float depth
                            )
                            && depth > report.maxBumperPenetration
                        )
                        {
                            report.maxBumperPenetration = depth;
                            report.colliderAudit.Add(
                                "bumper overlap "
                                    + t.ToString("F3")
                                    + " depth "
                                    + depth.ToString("F4")
                                    + " fuel "
                                    + piece.transform.position.ToString("F3")
                                    + " ignored="
                                    + Physics.GetIgnoreCollision(bumper, sphere)
                            );
                        }
                }
            }
            if (t >= shootEnd + request.observeSeconds)
                Finish(true);
        }

        private float RailExtension(ConfigurableJoint joint, float travel) =>
            robot.transform.InverseTransformPoint(joint.GetComponent<Rigidbody>().position).z
            - joint.connectedAnchor.z
            + travel * .5f;

        private bool InsideTransit(Vector3 world)
        {
            var p = robot.transform.InverseTransformPoint(world);
            return Mathf.Abs(p.x) < .36f && p.y > .15f && p.y < 1f && p.z > -.17f && p.z < .64f;
        }

        private int TransitRetained() =>
            transitFuel.Count(p => p && InsideTransit(p.GetComponent<Rigidbody>().position));

        private float NetMaximumHeight()
        {
            float maximum = 0;
            var vertices = net.GetMeshVertices();
            if (vertices != null)
                foreach (var vertex in vertices)
                    maximum = Mathf.Max(maximum, robot.transform.InverseTransformPoint(vertex).y);
            return maximum;
        }

        private float FuelMaximumHeight()
        {
            float maximum = 0;
            foreach (var piece in returnFuel)
                if (piece && InsideTransit(piece.GetComponent<Rigidbody>().position))
                    maximum = Mathf.Max(
                        maximum,
                        robot
                            .transform.InverseTransformPoint(
                                piece.GetComponent<Rigidbody>().position
                            )
                            .y + .075f
                    );
            return maximum;
        }

        private float RailVelocity(ConfigurableJoint joint)
        {
            var moving = joint.GetComponent<Rigidbody>();
            return Vector3.Dot(
                moving.velocity - body.GetPointVelocity(moving.position),
                robot.transform.forward
            );
        }

        private void OnCollisionEnter(Collision contact)
        {
            if (
                report != null
                && Time.fixedTime - began > 2.5f
                && contact.impulse.sqrMagnitude > 4
                && report.contacts.Count < 250
            )
                report.contacts.Add(
                    (Time.fixedTime - began).ToString("F3")
                        + " "
                        + contact.collider.name
                        + " impulse "
                        + contact.impulse.ToString("F3")
                );
        }

        private void Finish(bool complete)
        {
            if (finished)
                return;
            finished = true;
            if (report != null)
            {
                AuditColliders("finish");
                report.completed = complete;
                foreach (var piece in fuel)
                {
                    if (!piece)
                        continue;
                    var rb = piece.GetComponent<Rigidbody>();
                    var p = robot.transform.InverseTransformPoint(rb.position);
                    if (Mathf.Abs(p.x) < .45f && p.y > .16f && p.y < 1 && p.z > -.3f && p.z < .8f)
                        report.remainingFuel.Add(
                            FormattableString.Invariant(
                                $"id={piece.GetInstanceID()} local={p:F3} v={rb.velocity:F3} captured={keeper.IsCaptured(rb)} lowerEntry={robot.indexer.HasEnteredShooter(rb)} touching={string.Join(",", Physics.OverlapSphere(rb.position, .08f).Where(c => !c.isTrigger && c.attachedRigidbody != rb).Select(c => c.name))}"
                            )
                        );
                }
                File.WriteAllText(output, JsonUtility.ToJson(report, true));
                ScreenCapture.CaptureScreenshot(
                    Path.ChangeExtension(output, null) + "-finished.png"
                );
                Debug.Log(
                    "[Turbo native replay] Finished; max tilt "
                        + report.maxTilt.ToString("F2")
                        + "; captured "
                        + report.peakCaptured
                        + "; overturned "
                        + report.overturned
                );
            }
            robot.railMaxForce = originalRailForce;
            robot.railVelocityDamping = originalRailDamping;
            if (keyboard != null)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                if (player)
                {
                    player.actions.devices = originalDevices;
                    player.neverAutoSwitchControlSchemes = originalAutoSwitch;
                }
                InputSystem.RemoveDevice(keyboard);
                keyboard = null;
            }
        }

        private void OnDisable()
        {
            if (began >= 0)
                Finish(false);
        }

        private void AuditColliders(string stage)
        {
            int mask = LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
            foreach (var moving in robot.GetComponentsInChildren<Rigidbody>())
            {
                if (moving.GetComponent<RebuiltGamePieceController>())
                    continue;
                var joint = moving.GetComponent<ConfigurableJoint>();
                report.colliderAudit.Add(
                    FormattableString.Invariant(
                        $"{stage} body={moving.name} mode={moving.collisionDetectionMode} mass={moving.mass:F2} velocity={moving.velocity:F4} transformGap={moving.transform.position - moving.position:F4} targetVelocity={(joint ? joint.targetVelocity : Vector3.zero):F4} driveForce={(joint ? joint.zDrive.maximumForce : 0):F1} driveDamper={(joint ? joint.zDrive.positionDamper : 0):F1}"
                    )
                );
            }
            var liveIndexer = robot.GetComponent<Turbo4744Indexer>();
            report.colliderAudit.Add(
                stage
                    + " indexer sameInstance="
                    + (liveIndexer == robot.indexer)
                    + " rollers="
                    + liveIndexer.rollers.Length
                    + " command="
                    + liveIndexer.TargetMotorRps
                    + " speed="
                    + liveIndexer.CurrentSurfaceSpeed
                    + " fuelMask="
                    + typeof(Turbo4744Indexer)
                        .GetField("fuelMask", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(liveIndexer)
            );
            foreach (var roller in liveIndexer.rollers)
                report.colliderAudit.Add(
                    stage
                        + " roller center="
                        + roller.center.ToString("F4")
                        + " radius="
                        + roller.radius
                        + " halfWidth="
                        + roller.halfWidth
                        + " upper="
                        + roller.upper
                        + " visual="
                        + (roller.visual ? roller.visual.name : "missing")
                );
            var piece = fuel.Where(p => p && !robot.preloads.Contains(p.gameObject))
                .OrderBy(p => (p.transform.position - body.position).sqrMagnitude)
                .FirstOrDefault();
            var sphere = piece
                ? piece.GetComponentsInChildren<SphereCollider>().FirstOrDefault(c => !c.isTrigger)
                : null;
            report.colliderAudit.Add(
                stage
                    + " root layer="
                    + LayerMask.LayerToName(robot.gameObject.layer)
                    + " transformGap="
                    + (robot.transform.position - body.position).ToString("F4")
                    + " collisionMode="
                    + body.collisionDetectionMode
            );
            var interpolation = robot.GetComponent<CustomRigidbodyInterpolation>();
            foreach (string name in new[] { "isInterpolatingPosition", "isInterpolatingRotation" })
                report.colliderAudit.Add(
                    stage
                        + " "
                        + name
                        + "="
                        + typeof(CustomRigidbodyInterpolation)
                            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                            ?.GetValue(interpolation)
                );
            foreach (var c in robot.GetComponentsInChildren<Collider>(true))
            {
                var rb = c.attachedRigidbody;
                report.colliderAudit.Add(
                    stage
                        + " "
                        + c.name
                        + "/"
                        + c.GetType().Name
                        + " enabled="
                        + c.enabled
                        + " trigger="
                        + c.isTrigger
                        + " layer="
                        + LayerMask.LayerToName(c.gameObject.layer)
                        + " excludesFuel="
                        + (c.excludeLayers.value & mask)
                        + " rbExcludesFuel="
                        + (rb ? rb.excludeLayers.value & mask : 0)
                        + " ignoreLayer="
                        + (
                            sphere
                            && Physics.GetIgnoreLayerCollision(
                                c.gameObject.layer,
                                sphere.gameObject.layer
                            )
                        )
                        + " ignorePair="
                        + (sphere && Physics.GetIgnoreCollision(c, sphere))
                        + " body="
                        + (rb ? rb.name : "none")
                        + " bounds="
                        + c.bounds.center.ToString("F3")
                        + "/"
                        + c.bounds.size.ToString("F3")
                );
            }
        }

        private void Update()
        {
            if (report == null || finished)
                return;
            float ms = Time.unscaledDeltaTime * 1000;
            if (previousPhase == 1)
                report.maxCollectFrameMs = Mathf.Max(report.maxCollectFrameMs, ms);
            if (previousPhase == 2)
                report.maxReturnFrameMs = Mathf.Max(report.maxReturnFrameMs, ms);
            if (previousPhase >= 3)
                report.maxShootFrameMs = Mathf.Max(report.maxShootFrameMs, ms);
            if (ms > 40 && report.slowFrames.Count < 100)
                report.slowFrames.Add(
                    (Time.fixedTime - began).ToString("F3")
                        + " phase "
                        + previousPhase
                        + " frameMs "
                        + ms.ToString("F2")
                        + " focused "
                        + Application.isFocused
                );
        }

        private void OnGUI()
        {
            if (report == null)
                return;
            GUI.Box(
                new Rect(12, 12, 650, 155),
                "NATIVE MoSim input replay — "
                    + (finished ? "finished" : "phase " + previousPhase)
                    + "\nCaptured "
                    + report.peakCaptured
                    + " | peak tilt "
                    + report.maxTilt.ToString("F1")
                    + "°\nScored captured fuel: "
                    + report.scoredCaptured
                    + " | "
                    + robot.ShootingState
                    + " | "
                    + robot.PumpMode
                    + "\nRoller contacts lower/upper "
                    + robot.indexer.LowerContacts
                    + "/"
                    + robot.indexer.UpperContacts
                    + " | indexer "
                    + robot.indexer.AppliedForce.ToString("F1")
                    + " N\nStowed transit load "
                    + report.transitStartCount
                    + " | retained "
                    + (report.transitCompleted ? report.transitEndCount : TransitRetained())
                    + " | spills "
                    + report.transitEscapes.Count
                    + " | inlet departures "
                    + report.inletDepartures.Count
                    + "\nTransit "
                    + (report.trenchPassed ? "PASS" : report.failureReason ?? "in progress"),
                new GUIStyle(GUI.skin.box) { fontSize = 19, alignment = TextAnchor.UpperLeft }
            );
            if (
                finished
                && GUI.Button(new Rect(12, 172, 280, 40), "Run next requested native cycle")
            )
                BaseGameManager.Instance.StartCoroutine(BaseGameManager.Instance.ResetMatch());
            showBoxes = GUI.Toggle(new Rect(312, 172, 220, 40), showBoxes, "Show box hitboxes");
            var view = Camera.main
                ? Camera.main
                : Camera.allCameras.FirstOrDefault(c =>
                    c.enabled && c.cameraType == CameraType.Game
                );
            if (showBoxes && Event.current.type == EventType.Repaint && view)
                foreach (var box in robot.GetComponentsInChildren<BoxCollider>())
                {
                    if (!box.enabled || box.isTrigger)
                        continue;
                    var corners = new Vector3[8];
                    for (int i = 0; i < 8; i++)
                        corners[i] = view.WorldToScreenPoint(
                            box.transform.TransformPoint(
                                box.center
                                    + Vector3.Scale(
                                        box.size * .5f,
                                        new Vector3(
                                            (i & 1) == 0 ? -1 : 1,
                                            (i & 2) == 0 ? -1 : 1,
                                            (i & 4) == 0 ? -1 : 1
                                        )
                                    )
                            )
                        );
                    for (int i = 0; i < 8; i++)
                    for (int axis = 0; axis < 3; axis++)
                    {
                        int j = i ^ (1 << axis);
                        if (j <= i || corners[i].z <= 0 || corners[j].z <= 0)
                            continue;
                        var a = new Vector2(corners[i].x, Screen.height - corners[i].y);
                        var b = new Vector2(corners[j].x, Screen.height - corners[j].y);
                        var matrix = GUI.matrix;
                        var color = GUI.color;
                        GUI.color = new Color(0, .95f, .8f, .75f);
                        GUIUtility.RotateAroundPivot(
                            Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg,
                            a
                        );
                        GUI.DrawTexture(
                            new Rect(a.x, a.y, Vector2.Distance(a, b), 1.5f),
                            Texture2D.whiteTexture
                        );
                        GUI.matrix = matrix;
                        GUI.color = color;
                    }
                }
        }
    }
}
