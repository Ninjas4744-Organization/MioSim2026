using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Games.Rebuilt.GamePieceSystem;
using Ninjas4744.MoSim;
using RobotFramework.Controllers.Drivetrain;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static class Turbo4744MobilityRegression
{
    public static void Run() => Turbo4744TrenchRegression.Run();
}

// Compare wheel contact settings on the exact published 1.0.1 assets. The
// legacy setting is a controlled counterfactual, not a complete 1.0.0 build.
public sealed class Turbo4744MobilityFixture : MonoBehaviour
{
    [Serializable]
    public sealed class Trial
    {
        public string mode,
            scenario;
        public string source;
        public float escapeSeconds;
        public float stopOffset,
            heading,
            direction,
            distance,
            peakTilt,
            longestStall;
        public bool reachedStop,
            escaped;
        public bool feedingObserved;
        public int minimumSupported = 4,
            maximumSupported,
            fuelCount;
        public int capturedBeforeShoot,
            capturedAfterShoot,
            tireFieldContacts,
            undersideFuelContacts;
        public List<string> frames = new List<string>();
        public List<string> contacts = new List<string>();
    }

    [Serializable]
    public sealed class Report
    {
        public string scope = "Published assets with isolated wheel-setting comparisons";
        public string runtimeDllSha256,
            bundleSha256;
        public bool completed,
            passed;
        public List<Trial> cases = new List<Trial>();
        public List<string> errors = new List<string>();
    }

    [Serializable]
    public sealed class CapturedPose
    {
        public string source;
        public Vector3 position,
            target;
        public Quaternion rotation;
    }

    [Serializable]
    public sealed class PoseList
    {
        public CapturedPose[] states;
    }

    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly Report report = new Report();
    private readonly WaitForFixedUpdate step = new WaitForFixedUpdate();
    private readonly List<GameObject> fuel = new List<GameObject>();
    private Turbo4744 robot;
    private DriveController drive;
    private Rigidbody body;
    private Keyboard keyboard;
    private GameObject prefab,
        fuelPrefab;
    private Bounds bumpBounds;
    private float lane,
        began;
    private Trial current;

    private IEnumerator Start()
    {
        began = Time.realtimeSinceStartup;
        Application.logMessageReceived += Log;
        foreach (
            var ball in Object.FindObjectsByType<RebuiltGamePieceController>(
                FindObjectsSortMode.None
            )
        )
            ball.gameObject.SetActive(false);
        keyboard = InputSystem.AddDevice<Keyboard>("Turbo stopped-bump comparison");
        string platform = Path.GetFullPath(
            Application.dataPath
                + "/../BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64/"
        );
        report.runtimeDllSha256 = Hash(platform + "Ninjas4744.dll");
        report.bundleSha256 = Hash(platform + "rebuilt_ninjas_4744_turbo_2026_assets");
        var bundle = AssetBundle.LoadFromFile(platform + "rebuilt_ninjas_4744_turbo_2026_assets");
        prefab = bundle.LoadAsset<GameObject>(
            bundle.GetAllAssetNames().First(n => n.EndsWith("/turbo4744simple.prefab"))
        );
        fuelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
        );
        if (Environment.GetCommandLineArgs().Contains("-turboMobilityCaptured"))
        {
            report.scope =
                "Recorded native chassis poses, initialized on the official field; free dynamics after spawn; isolated contact-setting comparisons";
            yield return CapturedCases();
            Complete();
            yield break;
        }
        Box("Mobility carpet", new Vector3(30, .1f, 30), new Vector3(6, 2.95f, 2));
        var bump = Object.Instantiate(
            AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Games/Rebuilt/Field/Blue/BlueBump.prefab"
            )
        );
        bumpBounds = BoundsOf(bump);
        if (bumpBounds.size.x < bumpBounds.size.z)
            bump.transform.rotation =
                Quaternion.AngleAxis(90, Vector3.up) * bump.transform.rotation;
        bumpBounds = BoundsOf(bump);
        bump.transform.position += new Vector3(6 - bumpBounds.center.x, 3, 2 - bumpBounds.center.z);
        bumpBounds = BoundsOf(bump);
        lane = bumpBounds.center.x + 1.6f;
        bool crest = Environment.GetCommandLineArgs().Contains("-turboMobilityCrest");
        bool deployed = Environment.GetCommandLineArgs().Contains("-turboMobilityDeployed");
        string[] modes =
            Environment.GetCommandLineArgs().Contains("-turboMobilityAsBuiltOnly")
                ? new[] { "as-built" }
            : Environment.GetCommandLineArgs().Contains("-turboMobilityPublishedOnly")
                ? new[] { "published" }
            : new[] { "published", "tolerance-only", "legacy-wheels" };
        foreach (string mode in modes)
        {
            if (!Environment.GetCommandLineArgs().Contains("-turboMobilityCornerOnly"))
                foreach (float offset in (crest ? new[] { 0f } : new[] { -.4f, -.1f, .2f }))
                foreach (float heading in (crest ? new[] { 0f, 45f } : new[] { 0f }))
                foreach (float direction in new[] { 1f, -1f })
                {
                    yield return Setup(
                        mode,
                        new Vector3(lane, 3.08f, bumpBounds.min.z - 1.05f),
                        heading
                    );
                    if (deployed)
                    {
                        Press(Key.LeftShift);
                        yield return Wait(3);
                        Press();
                    }
                    var trial = new Trial
                    {
                        mode = mode,
                        scenario = "stopped bump",
                        heading = heading,
                        stopOffset = offset,
                        direction = direction,
                    };
                    report.cases.Add(trial);
                    current = trial;
                    // Approach under motor power, brake to rest, then try either
                    // direction. No pose/velocity reset occurs after initial spawn.
                    for (
                        float t = 0;
                        t < 4 && body.position.z < bumpBounds.center.z + offset;
                        t += Time.fixedDeltaTime
                    )
                    {
                        drive.OverrideInput(
                            0,
                            .8f,
                            0,
                            DriveController.DrivePerspective.FieldOriented
                        );
                        yield return step;
                    }
                    trial.reachedStop = body.position.z >= bumpBounds.center.z + offset - .02f;
                    yield return DriveFor(trial, 0, 0, 1);
                    Vector3 start = body.position;
                    yield return DriveFor(trial, 0, direction * (crest ? 4.7f : 1.5f), 4);
                    trial.distance = (body.position.z - start.z) * direction;
                    trial.escaped = trial.reachedStop && trial.distance > 1.2f;
                    Finish(trial);
                }
            if (!Environment.GetCommandLineArgs().Contains("-turboMobilityBumpOnly"))
                foreach (float heading in new[] { 180f, 225f })
                {
                    yield return Setup(mode, new Vector3(7.3f, 3.08f, 3.3f), heading);
                    // Two static walls and independent stock fuel bodies create a
                    // corner concentration without constraining any dynamic body.
                    var wallA = Box(
                        "Corner back wall",
                        new Vector3(5, 1, .1f),
                        new Vector3(6, 3.5f, 1.45f)
                    );
                    var wallB = Box(
                        "Corner side wall",
                        new Vector3(.1f, 1, 5),
                        new Vector3(5.45f, 3.5f, 2)
                    );
                    Press(Key.LeftShift);
                    yield return Wait(3);
                    Press();
                    for (int row = 0; row < 7; row++)
                    for (int column = 0; column < 7; column++)
                    {
                        var p = new Vector3(5.57f + column * .154f, 3.085f, 1.57f + row * .154f);
                        fuel.Add(
                            Object.Instantiate(
                                fuelPrefab,
                                p,
                                Quaternion.identity,
                                GameObject.FindWithTag("GamePieceWorld").transform
                            )
                        );
                    }
                    var trial = new Trial
                    {
                        mode = mode,
                        scenario = "corner pile after shooting",
                        heading = heading,
                        fuelCount = fuel.Count,
                    };
                    report.cases.Add(trial);
                    current = trial;
                    for (
                        float t = 0;
                        t < 4 && (body.position.x > 6.1f || body.position.z > 2.1f);
                        t += Time.fixedDeltaTime
                    )
                    {
                        drive.OverrideInput(
                            -.65f,
                            -.65f,
                            0,
                            DriveController.DrivePerspective.FieldOriented
                        );
                        yield return step;
                    }
                    yield return DriveFor(trial, 0, 0, 1);
                    trial.capturedBeforeShoot = Captured();
                    Press(Key.Q);
                    yield return DriveFor(trial, 0, 0, 7);
                    Press();
                    trial.capturedAfterShoot = Captured();
                    Vector3 start = body.position;
                    yield return DriveFor(trial, 1.05f, 1.05f, 5, 1.8f);
                    trial.distance = Vector3
                        .ProjectOnPlane(body.position - start, Vector3.up)
                        .magnitude;
                    trial.escaped =
                        trial.distance > 1.5f
                        && trial.capturedBeforeShoot > 0
                        && trial.feedingObserved;
                    Finish(trial);
                    wallA.SetActive(false);
                    wallB.SetActive(false);
                    foreach (var ball in fuel)
                        if (ball)
                            ball.SetActive(false);
                    fuel.Clear();
                }
        }
        Complete();
    }

    private void Complete()
    {
        report.completed = true;
        report.passed =
            report.errors.Count == 0 && report.cases.All(c => c.escaped && c.longestStall < .4f);
        Save();
        Application.logMessageReceived -= Log;
        InputSystem.RemoveDevice(keyboard);
        SessionState.SetInt(
            "Turbo.Trench.Exit",
            report.errors.Count == 0
            && (
                !Environment.GetCommandLineArgs().Contains("-turboMobilityAsBuiltOnly")
                || report.passed
            )
                ? 0
                : 1
        );
        EditorApplication.ExitPlaymode();
    }

    private IEnumerator CapturedCases()
    {
        string path = Path.GetFullPath(
            Application.dataPath + "/../BuildArtifacts/mobility-captured-poses.json"
        );
        var states = JsonUtility.FromJson<PoseList>(File.ReadAllText(path)).states;
        foreach (
            string mode in Environment.GetCommandLineArgs().Contains("-turboMobilityAsBuiltOnly")
                ? new[] { "as-built" }
            : Environment.GetCommandLineArgs().Contains("-turboMobilityTargeted")
                ? new[] { "published", "bumper-foam" }
            : Environment.GetCommandLineArgs().Contains("-turboMobilityFoam")
                ? new[] { "ramp-foam", "bumper-foam" }
            : new[] { "published", "tolerance-only", "legacy-wheels", "bumper-slide" }
        )
        foreach (var state in states)
        foreach (float direction in new[] { 1f, -1f })
        {
            yield return Setup(mode, state.position, 0, state.rotation);
            var trial = new Trial
            {
                mode = mode,
                scenario = "captured bump pose",
                source = state.source,
                direction = direction,
            };
            report.cases.Add(trial);
            current = trial;
            Vector3 start = body.position;
            Vector3 command =
                Vector3.ProjectOnPlane(state.target, Vector3.up).normalized * direction * 4.7f;
            yield return DriveFor(trial, command.x, command.z, 5, 1.5f);
            trial.distance = Vector3.ProjectOnPlane(body.position - start, Vector3.up).magnitude;
            trial.escaped = trial.distance > 1.2f;
            Finish(trial);
        }
    }

    private IEnumerator Setup(
        string mode,
        Vector3 position,
        float heading,
        Quaternion? initialRotation = null
    )
    {
        robot = Object
            .Instantiate(prefab, position, initialRotation ?? Quaternion.Euler(0, heading, 0))
            .GetComponent<Turbo4744>();
        foreach (var ball in robot.preloads)
            if (ball)
                ball.SetActive(false);
        robot.GetComponent<PlayerInput>().actions.devices = new InputDevice[] { keyboard };
        drive = robot.GetComponent<DriveController>();
        body = robot.GetComponent<Rigidbody>();
        current = null;
        foreach (var rb in robot.GetComponentsInChildren<Rigidbody>())
            rb.gameObject.AddComponent<Turbo4744LandingWitness>().sample = ObserveCollision;
        yield return Wait(.3f);
        if (mode == "tolerance-only" || mode == "legacy-wheels")
            typeof(DriveController).GetField("contactTolerance", Private).SetValue(drive, .01f);
        if (mode == "legacy-wheels")
            foreach (var wheel in robot.GetComponentsInChildren<WheelCollider>())
            {
                wheel.center = Vector3.zero;
                wheel.suspensionDistance = .025f;
                wheel.suspensionSpring = new JointSpring
                {
                    spring = 50000,
                    damper = 3500,
                    targetPosition = .5f,
                };
            }
        if (mode == "bumper-slide")
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(
                "Assets/Prefabs/Games/Rebuilt/Robots/Mods/Ninjas4744/Materials/FeedPlastic.physicMaterial"
            );
            foreach (var collider in robot.GetComponentsInChildren<BoxCollider>())
                if (collider.name.StartsWith("Bumper ") && collider.name.EndsWith(" contact"))
                    collider.sharedMaterial = material;
        }
        if (mode == "ramp-foam" || mode == "bumper-foam")
            robot.gameObject.AddComponent<Turbo4744BumperFoamExperiment>().includeCarpet =
                mode == "bumper-foam";
        Press(Key.Z);
        yield return Wait(3);
        Press();
    }

    private IEnumerator DriveFor(Trial trial, float x, float z, float seconds, float stopAfter = 0)
    {
        float stalled = 0;
        int sample = 0;
        Vector3 origin = body.position;
        for (float t = 0; t < seconds; t += Time.fixedDeltaTime)
        {
            drive.OverrideInput(x, z, 0, DriveController.DrivePerspective.FieldOriented);
            yield return step;
            trial.feedingObserved |= robot.Feeding;
            var patches = (
                (Array)typeof(DriveController).GetField("_contactPatches", Private).GetValue(drive)
            )
                .Cast<object>()
                .ToArray();
            int supported = patches.Count(p =>
                (float)p.GetType().GetField("NormalLoad").GetValue(p) > 0
            );
            trial.minimumSupported = Mathf.Min(trial.minimumSupported, supported);
            trial.maximumSupported = Mathf.Max(trial.maximumSupported, supported);
            trial.peakTilt = Mathf.Max(
                trial.peakTilt,
                Vector3.Angle(body.rotation * Vector3.up, Vector3.up)
            );
            stalled =
                x * x + z * z > 0 && body.velocity.magnitude < .1f
                    ? stalled + Time.fixedDeltaTime
                    : 0;
            trial.longestStall = Mathf.Max(trial.longestStall, stalled);
            if (t >= sample * .1f)
            {
                sample++;
                trial.frames.Add(
                    $"t={t:F3} command={x:F2}/{z:F2} pos={body.position:F4} velocity={body.velocity:F3} support={supported} patches={string.Join(";", patches.Select(p => $"{p.GetType().GetField("Count").GetValue(p)}/{p.GetType().GetField("NormalLoad").GetValue(p)}"))}"
                );
            }
            if (
                stopAfter > 0
                && Vector3.ProjectOnPlane(body.position - origin, Vector3.up).magnitude >= stopAfter
            )
            {
                trial.escapeSeconds = t + Time.fixedDeltaTime;
                break;
            }
        }
    }

    private void Finish(Trial trial)
    {
        Debug.Log(
            $"TURBO_MOBILITY {trial.mode} {trial.scenario} offset={trial.stopOffset} direction={trial.direction} reached={trial.reachedStop} distance={trial.distance:F3} escaped={trial.escaped}"
        );
        Save();
        robot.gameObject.SetActive(false);
        current = null;
    }

    private int Captured() =>
        fuel.Count(f =>
            f && robot.GetComponent<Turbo4744FuelKeeper>().IsCaptured(f.GetComponent<Rigidbody>())
        );

    private void ObserveCollision(Collision collision)
    {
        if (current == null)
            return;
        bool ball = collision.collider.GetComponentInParent<RebuiltGamePieceController>();
        foreach (var contact in collision.contacts)
        {
            bool tire = contact.thisCollider.GetComponentInParent<DriveWheel>();
            if (tire && !ball)
                current.tireFieldContacts++;
            if (
                ball
                && (
                    tire
                    || contact.thisCollider.name.StartsWith("Bellypan ")
                    || contact.thisCollider.name.StartsWith("Swerve ")
                )
            )
                current.undersideFuelContacts++;
            if (current.contacts.Count < 120 && body.velocity.magnitude < .15f)
                current.contacts.Add(
                    $"{contact.thisCollider.name}/{contact.otherCollider.name} p={contact.point:F4} normal={contact.normal:F3}"
                );
        }
    }

    private void Save() =>
        File.WriteAllText(
            Path.GetFullPath(
                Application.dataPath
                    + "/../BuildArtifacts/mobility-comparison"
                    + (
                        Environment.GetCommandLineArgs().Contains("-turboMobilityAsBuiltOnly")
                            ? "-candidate"
                            : ""
                    )
                    + (
                        Environment.GetCommandLineArgs().Contains("-turboMobilityTargeted")
                            ? "-targeted"
                            : ""
                    )
                    + (
                        Environment.GetCommandLineArgs().Contains("-turboMobilityFoam")
                            ? "-foam"
                            : ""
                    )
                    + (
                        Environment.GetCommandLineArgs().Contains("-turboMobilityCaptured")
                            ? "-captured"
                            : ""
                    )
                    + (
                        Environment.GetCommandLineArgs().Contains("-turboMobilityCrest")
                            ? "-crest"
                            : ""
                    )
                    + (
                        Environment.GetCommandLineArgs().Contains("-turboMobilityDeployed")
                            ? "-deployed"
                            : ""
                    )
                    + ".json"
            ),
            JsonUtility.ToJson(report, true)
        );

    private IEnumerator Wait(float seconds)
    {
        for (float t = 0; t < seconds; t += Time.fixedDeltaTime)
            yield return step;
    }

    private void Press(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
    }

    private void Log(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception) && report.errors.Count < 20)
            report.errors.Add(message + "\n" + stack);
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup - began > 900)
        {
            Save();
            SessionState.SetInt("Turbo.Trench.Exit", 1);
            EditorApplication.ExitPlaymode();
        }
    }

    private static string Hash(string path) =>
        BitConverter
            .ToString(
                System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(path))
            )
            .Replace("-", "")
            .ToLowerInvariant();

    private static Bounds BoundsOf(GameObject go)
    {
        Physics.SyncTransforms();
        var colliders = go.GetComponentsInChildren<Collider>()
            .Where(c => c.enabled && !c.isTrigger)
            .ToArray();
        var bounds = colliders[0].bounds;
        foreach (var c in colliders)
            bounds.Encapsulate(c.bounds);
        return bounds;
    }

    private static GameObject Box(string name, Vector3 size, Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = LayerMask.NameToLayer("Field");
        go.tag = "Field";
        go.transform.position = position;
        go.transform.localScale = size;
        return go;
    }
}

// Editor-only comparison of the existing bumper foam law on supporting field
// faces. It never modifies fuel/tire contacts, body poses or motor commands.
public sealed class Turbo4744BumperFoamExperiment : MonoBehaviour
{
    public bool includeCarpet;
    private readonly HashSet<int> bumpers = new HashSet<int>();
    private readonly HashSet<int> field = new HashSet<int>();
    private float step;

    private void Start()
    {
        step = Time.fixedDeltaTime;
        foreach (var c in GetComponentsInChildren<Collider>())
            if (!c.isTrigger && c.name.StartsWith("Bumper ") && c.name.EndsWith(" contact"))
                bumpers.Add(c.GetInstanceID());
        foreach (
            var c in Object.FindObjectsByType<Collider>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
            if (
                !c.isTrigger
                && !c.attachedRigidbody
                && c.gameObject.layer == LayerMask.NameToLayer("Field")
            )
                field.Add(c.GetInstanceID());
        Physics.ContactModifyEvent += Resolve;
        Physics.ContactModifyEventCCD += ResolveCCD;
    }

    private void Resolve(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs) =>
        Modify(pairs, 1);

    private void ResolveCCD(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs) =>
        Modify(pairs, -1);

    private void Modify(NativeArray<ModifiableContactPair> pairs, float direction)
    {
        foreach (var pair in pairs)
        {
            if (
                !(
                    bumpers.Contains(pair.colliderInstanceID)
                    && field.Contains(pair.otherColliderInstanceID)
                )
                && !(
                    bumpers.Contains(pair.otherColliderInstanceID)
                    && field.Contains(pair.colliderInstanceID)
                )
            )
                continue;
            for (int i = 0; i < pair.contactCount; i++)
            {
                var normal = pair.GetNormal(i);
                if (Mathf.Abs(normal.y) <= .25f || (!includeCarpet && Mathf.Abs(normal.y) > .99f))
                    continue;
                pair.SetStaticFriction(i, .03f);
                pair.SetDynamicFriction(i, .03f);
                float depth = Mathf.Max(0, -pair.GetSeparation(i));
                if (depth >= .008f)
                    continue;
                float closing = Mathf.Max(
                    0,
                    -Vector3.Dot(pair.bodyVelocity - pair.otherBodyVelocity, normal * direction)
                );
                pair.SetMaxImpulse(i, (12000 * depth + 120 * closing) * step / pair.contactCount);
            }
        }
    }

    private void OnDisable()
    {
        Physics.ContactModifyEvent -= Resolve;
        Physics.ContactModifyEventCCD -= ResolveCCD;
    }
}
