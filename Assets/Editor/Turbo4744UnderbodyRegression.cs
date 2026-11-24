using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Games.Rebuilt.GamePieceSystem;
using Ninjas4744.MoSim;
using RobotFramework.Controllers.Drivetrain;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static class Turbo4744UnderbodyRegression
{
    public static void Run() => Turbo4744TrenchRegression.Run();
}

// Frozen chassis fixtures isolate the contact-count contract. Separate free-body
// traction regressions validate natural recovery and genuinely beached controls.
public sealed class Turbo4744UnderbodyFixture : MonoBehaviour
{
    [Serializable]
    public sealed class Trial
    {
        public string name;
        public int expectedGrounded,
            observedGrounded,
            seeded,
            cleared,
            remaining,
            badGroundedClears;
        public bool passed,
            robotUnmoved,
            allBallsPreserved;
        public List<string> relocations = new List<string>();
    }

    [Serializable]
    public sealed class Report
    {
        public bool passed;
        public string scope =
            "Frozen chassis contact-contract fixtures; actual tire/field collision callbacks; independent dynamic fuel";
        public string runtimeDllSha256,
            bundleSha256;
        public List<Trial> cases = new List<Trial>();
        public List<string> errors = new List<string>();
    }

    private readonly Report report = new Report();
    private readonly List<GameObject> props = new List<GameObject>();
    private readonly List<Rigidbody> balls = new List<Rigidbody>();
    private readonly WaitForFixedUpdate step = new WaitForFixedUpdate();
    private Keyboard keyboard;
    private GameObject prefab,
        fuel;
    private Turbo4744 robot;
    private Rigidbody body;
    private Turbo4744UnderbodyClearance assist;
    private Trial current;
    private float began;

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
        keyboard = InputSystem.AddDevice<Keyboard>("Turbo grounded clearance contract");
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
        fuel = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
        );
        Box("Clearance carpet", new Vector3(6, .1f, 6), new Vector3(6, 2.95f, 2));
        foreach (int supported in new[] { 0, 1, 2, 3, 4 })
        foreach (bool pile in new[] { false, true })
        {
            yield return Setup(supported);
            current = new Trial
            {
                name = pile ? "underbody pile" : "single underbody ball",
                expectedGrounded = supported,
            };
            report.cases.Add(current);
            assist.Relocated += Relocated;
            int side = pile ? 3 : 1;
            for (int x = 0; x < side; x++)
            for (int z = 0; z < side; z++)
                Spawn(new Vector3(6 + (x - side / 2) * .16f, 3.15f, 2 + (z - side / 2) * .16f));
            current.seeded = balls.Count;
            yield return Wait(1.5f);
            current.observedGrounded = assist.GroundedWheelCount;
            current.cleared = assist.ClearedBalls;
            current.remaining = balls.Count(b => IsUnder(b.position));
            current.robotUnmoved =
                Vector3.Distance(body.position, new Vector3(6, 3.18f, 2)) < .001f;
            current.allBallsPreserved = balls.All(b =>
                b && b.gameObject.activeInHierarchy && !b.isKinematic
            );
            current.passed =
                current.observedGrounded == supported
                && current.badGroundedClears == 0
                && current.robotUnmoved
                && current.allBallsPreserved
                && (
                    supported == 0
                        ? current.cleared == 0
                        : current.remaining == 0 && current.cleared == current.seeded
                );
            Save();
            Debug.Log(
                $"TURBO_UNDERBODY {current.name} support={supported}/{current.observedGrounded} cleared={current.cleared}/{current.seeded} passed={current.passed}"
            );
            Clean();
            yield return step;
        }
        // A wall blocks the nearest exit. The ball must remain on this side of
        // the wall and find another free floor location rather than phase through.
        yield return Setup(1);
        Box("Clearance blocked exit", new Vector3(.1f, .8f, 3), new Vector3(6.60f, 3.4f, 2));
        current = new Trial
        {
            name = "blocked nearest exit",
            expectedGrounded = 1,
            seeded = 1,
        };
        report.cases.Add(current);
        assist.Relocated += Relocated;
        Spawn(new Vector3(6.22f, 3.15f, 2));
        yield return Wait(1.5f);
        current.observedGrounded = assist.GroundedWheelCount;
        current.cleared = assist.ClearedBalls;
        current.remaining = balls.Count(b => IsUnder(b.position));
        current.robotUnmoved = Vector3.Distance(body.position, new Vector3(6, 3.18f, 2)) < .001f;
        current.allBallsPreserved = balls.All(b =>
            b && b.gameObject.activeInHierarchy && !b.isKinematic
        );
        current.passed =
            current.robotUnmoved
            && current.allBallsPreserved
            && current.observedGrounded == 1
            && current.cleared == 1
            && current.remaining == 0
            && balls[0].position.x <= 6.48f
            && current.badGroundedClears == 0;
        Save();
        Clean();
        // Cached collision points must stop authorizing clearance after support
        // disappears, including a small all-wheel air gap after real contacts.
        foreach (bool lift in new[] { false, true })
        {
            yield return Setup(lift ? 4 : 1);
            current = new Trial
            {
                name = lift ? "support lowered 6 mm" : "support disabled",
                expectedGrounded = 0,
                seeded = 1,
            };
            report.cases.Add(current);
            assist.Relocated += Relocated;
            foreach (var prop in props)
                if (prop && prop.activeInHierarchy && prop.name.StartsWith("Actual tire support"))
                    if (lift)
                        prop.transform.position -= Vector3.up * .006f;
                    else
                        prop.SetActive(false);
            Physics.SyncTransforms();
            yield return Wait(.1f);
            Spawn(new Vector3(6, 3.15f, 2));
            yield return Wait(.5f);
            current.observedGrounded = assist.GroundedWheelCount;
            current.cleared = assist.ClearedBalls;
            current.remaining = balls.Count(b => IsUnder(b.position));
            current.robotUnmoved =
                Vector3.Distance(body.position, new Vector3(6, 3.18f, 2)) < .001f;
            current.allBallsPreserved = balls.All(b =>
                b && b.gameObject.activeInHierarchy && !b.isKinematic
            );
            current.passed =
                current.observedGrounded == 0
                && current.cleared == 0
                && current.badGroundedClears == 0
                && current.robotUnmoved
                && current.allBallsPreserved;
            Save();
            Clean();
            yield return step;
        }
        report.passed = report.errors.Count == 0 && report.cases.All(c => c.passed);
        Save();
        Application.logMessageReceived -= Log;
        InputSystem.RemoveDevice(keyboard);
        SessionState.SetInt("Turbo.Trench.Exit", report.passed ? 0 : 1);
        EditorApplication.ExitPlaymode();
    }

    private IEnumerator Setup(int supported)
    {
        robot = Object
            .Instantiate(prefab, new Vector3(6, 3.18f, 2), Quaternion.identity)
            .GetComponent<Turbo4744>();
        foreach (var preload in robot.preloads)
            if (preload)
                preload.SetActive(false);
        robot.GetComponent<PlayerInput>().actions.devices = new InputDevice[] { keyboard };
        body = robot.GetComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.FreezeAll;
        yield return step;
        assist = robot.GetComponent<Turbo4744UnderbodyClearance>();
        var tires = robot.GetComponentsInChildren<WheelCollider>();
        for (int i = 0; i < supported; i++)
        {
            var mesh = tires[i].GetComponentInChildren<MeshCollider>();
            float top = mesh.bounds.min.y + .0005f;
            Box(
                "Actual tire support " + i,
                new Vector3(.04f, top - 3, .04f),
                new Vector3(mesh.bounds.center.x, (top + 3) * .5f, mesh.bounds.center.z)
            );
        }
        Box("Loose fuel support bed", new Vector3(.6f, .075f, .6f), new Vector3(6, 3.0375f, 2));
        robot.GetComponent<Turbo4744BumperContacts>().RefreshContacts();
        yield return Wait(.2f);
    }

    private void Relocated(Rigidbody ball, Vector3 from, Vector3 to, int grounded)
    {
        if (grounded < 1 || grounded != current.expectedGrounded)
            current.badGroundedClears++;
        current.relocations.Add($"grounded={grounded} from={from:F4} to={to:F4}");
    }

    private void Spawn(Vector3 point) =>
        balls.Add(
            Object
                .Instantiate(
                    fuel,
                    point,
                    Quaternion.identity,
                    GameObject.FindWithTag("GamePieceWorld").transform
                )
                .GetComponent<Rigidbody>()
        );

    private bool IsUnder(Vector3 point)
    {
        var p = Quaternion.Inverse(body.rotation) * (point - body.position);
        return Mathf.Abs(p.x) <= .43f && Mathf.Abs(p.z) <= .48f;
    }

    private GameObject Box(string name, Vector3 size, Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = LayerMask.NameToLayer("Field");
        go.tag = "Field";
        go.transform.position = position;
        go.transform.localScale = size;
        props.Add(go);
        return go;
    }

    private void Clean()
    {
        robot.gameObject.SetActive(false);
        foreach (var ball in balls)
            if (ball)
                ball.gameObject.SetActive(false);
        foreach (var prop in props)
            if (prop && prop.name != "Clearance carpet")
                prop.SetActive(false);
        balls.Clear();
    }

    private IEnumerator Wait(float seconds)
    {
        for (float t = 0; t < seconds; t += Time.fixedDeltaTime)
        {
            robot
                .GetComponent<DriveController>()
                .OverrideInput(0, 0, 0, DriveController.DrivePerspective.FieldOriented);
            yield return step;
        }
    }

    private void Save() =>
        File.WriteAllText(
            Path.GetFullPath(Application.dataPath + "/../BuildArtifacts/underbody-clearance.json"),
            JsonUtility.ToJson(report, true)
        );

    private static string Hash(string path) =>
        BitConverter
            .ToString(
                System.Security.Cryptography.SHA256.Create().ComputeHash(File.ReadAllBytes(path))
            )
            .Replace("-", "")
            .ToLowerInvariant();

    private void Log(string message, string stack, LogType type)
    {
        if ((type == LogType.Error || type == LogType.Exception) && report.errors.Count < 15)
            report.errors.Add(message + "\n" + stack);
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup - began > 240)
        {
            Save();
            SessionState.SetInt("Turbo.Trench.Exit", 1);
            EditorApplication.ExitPlaymode();
        }
    }
}
