using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Games.Rebuilt.GamePieceSystem;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.BaseClasses.GameManagement.TimerManagement;
using MoSimCore.Enums;
using Ninjas4744.MoSim;
using RobotFramework.Components;
using RobotFramework.Controllers.Drivetrain;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static class Turbo4744SwerveRegression
{
    public static void Run() => Turbo4744TrenchRegression.Run();
}

public sealed class Turbo4744SwerveFixture : MonoBehaviour
{
    [Serializable]
    public sealed class Probe
    {
        public string module,
            state,
            surface;
        public int steering;
        public bool touched,
            pairSolid,
            blocked;
        public float minimumSideDistance;
    }

    [Serializable]
    private sealed class Report
    {
        public bool passed,
            fuelExcludedFromTraction,
            clearFloorStable;
        public float clearFloorDistance,
            physicsStep;
        public string bundleSha256,
            runtimeDllSha256;
        public List<Probe> cases = new List<Probe>();
        public List<string> errors = new List<string>();
    }

    private readonly Report report = new Report();
    private readonly WaitForFixedUpdate step = new WaitForFixedUpdate();
    private Keyboard keyboard;
    private Turbo4744 robot;
    private DriveController drive;
    private Collider[] contacts;
    private bool[] originalEnabled;
    private GameObject fuelPrefab;
    private float began;

    private IEnumerator Start()
    {
        began = Time.realtimeSinceStartup;
        Application.logMessageReceived += Log;
        foreach (
            var fuel in Object.FindObjectsByType<RebuiltGamePieceController>(
                FindObjectsSortMode.None
            )
        )
            fuel.gameObject.SetActive(false);
        keyboard = InputSystem.AddDevice<Keyboard>("Swerve collision probes");
        string platform = Path.GetFullPath(
            Application.dataPath
                + "/../BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64/"
        );
        string bundlePath = platform + "rebuilt_ninjas_4744_turbo_2026_assets";
        report.bundleSha256 = Hash(bundlePath);
        report.runtimeDllSha256 = Hash(platform + "Ninjas4744.dll");
        report.physicsStep = Time.fixedDeltaTime;
        var bundle = AssetBundle.LoadFromFile(bundlePath);
        var prefab = bundle.LoadAsset<GameObject>(
            bundle.GetAllAssetNames().First(n => n.EndsWith("/turbo4744.prefab"))
        );
        robot = Object
            .Instantiate(prefab, Vector3.up, Quaternion.identity)
            .GetComponent<Turbo4744>();
        robot.SetAlliance(Alliance.Blue);
        foreach (var preload in robot.preloads)
            if (preload)
                preload.SetActive(false);
        // Keep normal state/collision gating, but isolate powered ball handling
        // so it cannot capture or pull a probe around a finite housing.
        var powered = robot
            .GetComponentsInChildren<InteractionRoller>()
            .Cast<Behaviour>()
            .Concat(
                new Behaviour[]
                {
                    robot.indexer,
                    robot.intakeContact,
                    robot.shooterContact,
                    robot.acceleratorContact,
                    robot.GetComponent<RebuiltRobotGamePieceController>(),
                }
            )
            .Where(c => c)
            .Select(c => (component: c, enabled: c.enabled))
            .ToArray();
        foreach (var actuator in powered)
            actuator.component.enabled = false;
        robot.GetComponent<PlayerInput>().actions.devices = new InputDevice[] { keyboard };
        drive = robot.GetComponent<DriveController>();
        fuelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
        );
        yield return Wait(.2f);
        var bodies = robot.GetComponentsInChildren<Rigidbody>();
        var kinematic = bodies.Select(b => b.isKinematic).ToArray();
        foreach (var body in bodies)
        {
            if (!body.isKinematic)
                body.velocity = body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }
        contacts = robot.GetComponentsInChildren<Collider>(true);
        originalEnabled = contacts.Select(c => c.enabled).ToArray();
        int fuelMask = LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
        int queryMask = (int)
            typeof(DriveController)
                .GetField("_fieldLayerMask", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(drive);
        var wheels = robot.GetComponentsInChildren<WheelCollider>();
        report.fuelExcludedFromTraction =
            (queryMask & fuelMask) == fuelMask
            && wheels.All(w =>
                (w.excludeLayers.value & fuelMask) == fuelMask
                && w.GetComponentsInChildren<Collider>()
                    .Where(c => !c.isTrigger && !(c is MeshCollider))
                    .All(c => (c.excludeLayers.value & fuelMask) == fuelMask)
            );
        foreach (string state in new[] { "stowed", "collecting", "reverse", "disabled" })
        {
            typeof(BaseTimerManager)
                .GetProperty("CurrentRobotState")
                .SetValue(
                    BaseGameManager.Instance.GetComponent<BaseTimerManager>(),
                    state == "disabled" ? RobotState.Disabled : RobotState.Enabled
                );
            drive.enabled = state != "disabled";
            Press(
                state == "stowed" ? new[] { Key.Z }
                : state == "collecting" ? new[] { Key.LeftShift }
                : state == "reverse" ? new[] { Key.U }
                : Array.Empty<Key>()
            );
            yield return Wait(.12f);
            drive.enabled = false;
            foreach (var wheel in wheels)
            {
                var housing = contacts
                    .OfType<BoxCollider>()
                    .Single(c => c.name == "Swerve " + wheel.name + " housing contact");
                foreach (var direction in new[] { Vector3.right, Vector3.forward })
                    yield return ProbeContact(housing, direction, wheel.name, state, "housing", 0);
            }
        }
        Press();
        foreach (var wheel in wheels)
        foreach (int steering in new[] { 0, 90 })
        {
            wheel.transform.localRotation = Quaternion.Euler(0, steering, 0);
            wheel.steerAngle = steering;
            var tire = wheel.GetComponentsInChildren<MeshCollider>().Single(c => !c.isTrigger);
            yield return ProbeContact(
                tire,
                wheel.transform.right,
                wheel.name,
                "disabled",
                "tire",
                steering
            );
        }
        for (int i = 0; i < contacts.Length; i++)
            contacts[i].enabled = originalEnabled[i];
        foreach (var actuator in powered)
            actuator.component.enabled = actuator.enabled;
        foreach (var wheel in wheels)
        {
            wheel.transform.localRotation = Quaternion.identity;
            wheel.steerAngle = 0;
        }
        typeof(BaseTimerManager)
            .GetProperty("CurrentRobotState")
            .SetValue(
                BaseGameManager.Instance.GetComponent<BaseTimerManager>(),
                RobotState.Enabled
            );
        var chassis = robot.GetComponent<Rigidbody>();
        var delta = new Vector3(6.1f, .04f, 1.6f) - chassis.position;
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].position += delta;
            bodies[i].isKinematic = kinematic[i];
        }
        Physics.SyncTransforms();
        drive.enabled = true;
        Press(Key.LeftShift);
        yield return Wait(.5f);
        Press();
        var start = chassis.position;
        for (float t = 0; t < 1.2f; t += Time.fixedDeltaTime)
        {
            drive.OverrideInput(1.5f, 0, 0, DriveController.DrivePerspective.FieldOriented);
            yield return step;
        }
        report.clearFloorDistance = Vector3.Distance(start, chassis.position);
        report.clearFloorStable =
            report.clearFloorDistance > .7f && Mathf.Abs(chassis.position.y) < .12f;
        report.passed =
            report.errors.Count == 0
            && report.cases.Count == 40
            && report.cases.All(c => c.blocked)
            && report.fuelExcludedFromTraction
            && report.clearFloorStable;
        string output = Path.GetFullPath(
            Application.dataPath + "/../BuildArtifacts/swerve-modules.json"
        );
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        Debug.Log(
            "TURBO_SWERVE_RESULT "
                + report.passed
                + " probes="
                + report.cases.Count
                + " drive="
                + report.clearFloorDistance
        );
        Application.logMessageReceived -= Log;
        robot.gameObject.SetActive(false);
        InputSystem.RemoveDevice(keyboard);
        SessionState.SetInt("Turbo.Trench.Exit", report.passed ? 0 : 1);
        EditorApplication.ExitPlaymode();
    }

    private IEnumerator ProbeContact(
        Collider target,
        Vector3 outward,
        string module,
        string state,
        string surface,
        int steering
    )
    {
        foreach (var contact in contacts)
            contact.enabled = contact == target;
        Physics.SyncTransforms();
        var bounds = target.bounds;
        float extent = Vector3.Dot(
            bounds.extents,
            new Vector3(Mathf.Abs(outward.x), Mathf.Abs(outward.y), Mathf.Abs(outward.z))
        );
        var ball = Object
            .Instantiate(
                fuelPrefab,
                bounds.center + outward * (extent + .10f),
                Quaternion.identity,
                GameObject.FindWithTag("GamePieceWorld").transform
            )
            .GetComponent<Rigidbody>();
        ball.useGravity = false;
        ball.constraints =
            RigidbodyConstraints.FreezePositionY
            | (
                Mathf.Abs(outward.x) > .5f
                    ? RigidbodyConstraints.FreezePositionZ
                    : RigidbodyConstraints.FreezePositionX
            );
        ball.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        ball.velocity = -outward;
        var witness = ball.gameObject.AddComponent<Turbo4744SwerveWitness>();
        witness.target = target;
        var sphere = ball.GetComponentsInChildren<SphereCollider>().First(c => !c.isTrigger);
        var result = new Probe
        {
            module = module,
            state = state,
            surface = surface,
            steering = steering,
            pairSolid = true,
            minimumSideDistance = float.PositiveInfinity,
        };
        for (float t = 0; t < .6f; t += Time.fixedDeltaTime)
        {
            yield return step;
            result.minimumSideDistance = Mathf.Min(
                result.minimumSideDistance,
                Vector3.Dot(ball.position - bounds.center, outward)
            );
            result.pairSolid &=
                target.enabled
                && !target.isTrigger
                && !Physics.GetIgnoreCollision(target, sphere)
                && (target.excludeLayers.value & (1 << sphere.gameObject.layer)) == 0;
        }
        result.touched = witness.touched;
        result.blocked = result.pairSolid && result.touched && result.minimumSideDistance > -.01f;
        report.cases.Add(result);
        Debug.Log("TURBO_SWERVE_PROBE " + JsonUtility.ToJson(result));
        Object.Destroy(ball.gameObject);
        for (int i = 0; i < contacts.Length; i++)
            contacts[i].enabled = originalEnabled[i];
        yield return step;
    }

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
        if ((type == LogType.Error || type == LogType.Exception) && report.errors.Count < 10)
            report.errors.Add(message + "\n" + stack);
    }

    private static string Hash(string path) =>
        BitConverter
            .ToString(
                System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(path))
            )
            .Replace("-", "")
            .ToLowerInvariant();

    private void Update()
    {
        if (Time.realtimeSinceStartup - began < 180)
            return;
        SessionState.SetInt("Turbo.Trench.Exit", 1);
        EditorApplication.ExitPlaymode();
    }
}

public sealed class Turbo4744SwerveWitness : MonoBehaviour
{
    public Collider target;
    public bool touched;

    private void OnCollisionEnter(Collision collision) => Record(collision);

    private void OnCollisionStay(Collision collision) => Record(collision);

    private void Record(Collision collision) =>
        touched |= collision.contacts.Any(c => c.otherCollider == target);
}
