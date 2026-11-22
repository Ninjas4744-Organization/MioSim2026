using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Games.Rebuilt.GamePieceSystem;
using Ninjas4744.MoSim;
using RobotFramework.Controllers.Drivetrain;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static class Turbo4744TractionRegression
{
    public static void Run() => Turbo4744TrenchRegression.Run();
}

// A free chassis, stock dynamic fuel and a raised carpet isolate underside
// contact from the surrounding field. No body is constrained during a trial.
public sealed class Turbo4744TractionFixture : MonoBehaviour
{
    [Serializable]
    public sealed class Trial
    {
        public string name,
            direction;
        public bool deployed,
            passed;
        public int initialGrounded,
            minimumGrounded,
            maximumGrounded;
        public float distance,
            height,
            fuelMovement;
        public List<string> frames = new List<string>();
    }

    [Serializable]
    public sealed class Report
    {
        public bool passed;
        public float physicsStep;
        public string bundleSha256,
            runtimeDllSha256;
        public List<Trial> cases = new List<Trial>();
        public List<string> errors = new List<string>();
    }

    private readonly Report report = new Report();
    private readonly WaitForFixedUpdate step = new WaitForFixedUpdate();
    private Turbo4744 robot;
    private DriveController drive;
    private Rigidbody body;
    private WheelCollider[] wheels;
    private Keyboard keyboard;
    private GameObject fuelPrefab;
    private float began;
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

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
        keyboard = InputSystem.AddDevice<Keyboard>("Turbo tire support regression");
        string platform = Path.GetFullPath(
            Application.dataPath
                + "/../BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64/"
        );
        report.bundleSha256 = Hash(platform + "rebuilt_ninjas_4744_turbo_2026_assets");
        report.runtimeDllSha256 = Hash(platform + "Ninjas4744.dll");
        report.physicsStep = Time.fixedDeltaTime;
        var bundle = AssetBundle.LoadFromFile(platform + "rebuilt_ninjas_4744_turbo_2026_assets");
        var prefab = bundle.LoadAsset<GameObject>(
            bundle.GetAllAssetNames().First(n => n.EndsWith("/turbo4744simple.prefab"))
        );
        fuelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
        );
        Box("Traction carpet", new Vector3(9, .1f, 9), new Vector3(6, 2.95f, 2));
        foreach (bool deployed in new[] { false, true })
        foreach (int support in Enumerable.Range(-1, 7))
        {
            robot = Object
                .Instantiate(prefab, new Vector3(6, 3.3f, 2), Quaternion.identity)
                .GetComponent<Turbo4744>();
            foreach (var preload in robot.preloads)
                if (preload)
                    preload.SetActive(false);
            robot.GetComponent<PlayerInput>().actions.devices = new InputDevice[] { keyboard };
            body = robot.GetComponent<Rigidbody>();
            drive = robot.GetComponent<DriveController>();
            wheels = robot.GetComponentsInChildren<WheelCollider>();
            yield return Wait(.3f);
            Press(deployed ? new[] { Key.LeftShift } : new[] { Key.Z });
            yield return Wait(2.8f);
            Press();
            bool reverse = Environment.GetCommandLineArgs().Contains("-turboReverse");
            var trial = new Trial
            {
                name =
                    support == -1 ? "carpet"
                    : support < 4 ? wheels[support].name
                    : support == 4 ? "center fuel"
                    : "all tires clear",
                direction = reverse ? "reverse" : "forward",
                deployed = deployed,
                minimumGrounded = 4,
            };
            report.cases.Add(trial);
            Rigidbody fuel = null;
            GameObject stand = null;
            if (support >= 0)
            {
                MoveRig(new Vector3(6, 3.3f, 2));
                if (support < 5)
                {
                    Vector3 p = support < 4 ? wheels[support].transform.position : body.position;
                    fuel = Object
                        .Instantiate(
                            fuelPrefab,
                            new Vector3(p.x, 3.075f, p.z),
                            Quaternion.identity,
                            GameObject.FindWithTag("GamePieceWorld").transform
                        )
                        .GetComponent<Rigidbody>();
                }
                else
                    stand = Box(
                        "Beached chassis stand",
                        new Vector3(.3f, .18f, .3f),
                        new Vector3(6, 3.09f, 2)
                    );
            }
            yield return Wait(1.2f);
            trial.initialGrounded = Grounded();
            Vector3 start = body.position;
            Vector3 fuelStart = fuel ? fuel.position : Vector3.zero;
            for (float t = 0; t < 2; t += Time.fixedDeltaTime)
            {
                drive.OverrideInput(
                    reverse ? -1.5f : 1.5f,
                    0,
                    0,
                    DriveController.DrivePerspective.RobotRelative
                );
                yield return step;
                int grounded = Grounded();
                trial.minimumGrounded = Mathf.Min(trial.minimumGrounded, grounded);
                trial.maximumGrounded = Mathf.Max(trial.maximumGrounded, grounded);
                if (trial.frames.Count == 0 || t >= trial.frames.Count * .1f)
                    trial.frames.Add(
                        $"t={t:F2} pos={body.position.ToString("F4")} vel={body.velocity.ToString("F3")} supported={grounded} patches={Patches()}"
                    );
            }
            trial.distance = Vector3.ProjectOnPlane(body.position - start, Vector3.up).magnitude;
            trial.height = start.y - 3;
            trial.fuelMovement = fuel ? Vector3.Distance(fuelStart, fuel.position) : 0;
            trial.passed =
                support == 5
                    ? trial.initialGrounded == 0
                        && trial.maximumGrounded == 0
                        && trial.distance < .05f
                    : trial.distance > 1 && trial.maximumGrounded > 0;
            Debug.Log(
                $"TURBO_TRACTION {trial.name} deployed={deployed} ground={trial.initialGrounded} distance={trial.distance:F3} passed={trial.passed}"
            );
            // Field systems cache robot references. Retain an inactive object
            // until the scene unloads, as the existing trench fixture does.
            robot.gameObject.SetActive(false);
            if (fuel)
                Object.Destroy(fuel.gameObject);
            if (stand)
                Object.Destroy(stand);
            yield return step;
        }
        report.passed = report.errors.Count == 0 && report.cases.All(c => c.passed);
        string label = Environment.GetCommandLineArgs().Contains("-turboBaseline")
            ? "before"
            : "after";
        if (Environment.GetCommandLineArgs().Contains("-turboReverse"))
            label += "-reverse";
        File.WriteAllText(
            Path.GetFullPath(Application.dataPath + $"/../BuildArtifacts/traction-{label}.json"),
            JsonUtility.ToJson(report, true)
        );
        Application.logMessageReceived -= Log;
        InputSystem.RemoveDevice(keyboard);
        SessionState.SetInt("Turbo.Trench.Exit", report.passed ? 0 : 1);
        EditorApplication.ExitPlaymode();
    }

    private int Grounded()
    {
        // Solid tire meshes can support the chassis without a native
        // suspension hit. Count the drivetrain's actual carpet patches.
        var patches = (Array)
            typeof(DriveController).GetField("_contactPatches", Fields).GetValue(drive);
        return patches
            .Cast<object>()
            .Count(p => (int)p.GetType().GetField("FloorCount").GetValue(p) > 0);
    }

    private string Patches()
    {
        var patches = (Array)
            typeof(DriveController).GetField("_contactPatches", Fields).GetValue(drive);
        return string.Join(
            ";",
            patches
                .Cast<object>()
                .Select(p =>
                    $"{p.GetType().GetField("Count").GetValue(p)}/{p.GetType().GetField("NormalLoad").GetValue(p)}"
                )
        );
    }

    private void MoveRig(Vector3 position)
    {
        var bodies = robot.GetComponentsInChildren<Rigidbody>();
        Vector3 origin = body.position;
        Quaternion inverse = Quaternion.Inverse(body.rotation);
        var positions = bodies.Select(b => position + inverse * (b.position - origin)).ToArray();
        var rotations = bodies.Select(b => inverse * b.rotation).ToArray();
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].velocity = bodies[i].angularVelocity = Vector3.zero;
            bodies[i].position = positions[i];
            bodies[i].rotation = rotations[i];
        }
        Physics.SyncTransforms();
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
        if (Time.realtimeSinceStartup - began < 220)
            return;
        SessionState.SetInt("Turbo.Trench.Exit", 1);
        EditorApplication.ExitPlaymode();
    }
}
