using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Games.Rebuilt.GamePieceSystem;
using MoSimCore.Enums;
using Ninjas4744.MoSim;
using RobotFramework.Controllers.Drivetrain;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static class Turbo4744LandingRegression
{
    public static void Run() => Turbo4744TrenchRegression.Run();
}

// A copy of the official bump over raised carpet isolates the landing.
// Every trial starts before the bump. Robot, cargo and loose fuel then move
// only through normal simulation; no reset or recovery force is applied.
public sealed class Turbo4744LandingFixture : MonoBehaviour
{
    [Serializable]
    public sealed class Trial
    {
        public float ballX,
            ballBeyondBump,
            distance,
            maximumHeight,
            maximumTilt,
            longestAirborne,
            longestSupportedStall,
            finalSpeed,
            approachSpeed,
            heading;
        public int storedBefore,
            storedAfter,
            fuelContacts,
            undersideFuelContacts;
        public bool loaded,
            stowed,
            clearControl,
            crossedBump,
            recovered,
            passed;
        public List<string> frames = new List<string>();
        public List<string> contacts = new List<string>();
    }

    [Serializable]
    public sealed class Report
    {
        public bool passed,
            completed;
        public string bundleSha256,
            runtimeDllSha256,
            bumpBounds;
        public float physicsStep;
        public List<Trial> cases = new List<Trial>();
        public List<string> errors = new List<string>();
    }

    private readonly Report report = new Report();
    private readonly WaitForFixedUpdate step = new WaitForFixedUpdate();
    private readonly List<Rigidbody> cargo = new List<Rigidbody>();
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly FieldInfo patchesField = typeof(DriveController).GetField(
        "_contactPatches",
        Private
    );
    private Turbo4744 robot;
    private DriveController drive;
    private Rigidbody body;
    private Keyboard keyboard;
    private GameObject fuelPrefab;
    private Trial current;
    private float began,
        driveBegan;
    private bool recording;
    private string output;
    private bool heavy;

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
        keyboard = InputSystem.AddDevice<Keyboard>("Turbo bump landing regression");
        string platform = Path.GetFullPath(
            Application.dataPath
                + "/../BuiltMods/rebuilt_ninjas_4744_turbo_2026/standalonewindows64/"
        );
        report.bundleSha256 = Hash(platform + "rebuilt_ninjas_4744_turbo_2026_assets");
        report.runtimeDllSha256 = Hash(platform + "Ninjas4744.dll");
        report.physicsStep = Time.fixedDeltaTime;
        bool loaded = Environment.GetCommandLineArgs().Contains("-turboLandingLoaded");
        bool stowed = Environment.GetCommandLineArgs().Contains("-turboLandingStowed");
        bool reverse = Environment.GetCommandLineArgs().Contains("-turboLandingReverse");
        bool slow = Environment.GetCommandLineArgs().Contains("-turboLandingSlow");
        bool centerOnly = Environment.GetCommandLineArgs().Contains("-turboLandingCenterOnly");
        heavy = Environment.GetCommandLineArgs().Contains("-turboLandingHeavy");
        float speed = slow ? 1.41f : 4.7f;
        string label = Environment.GetCommandLineArgs().Contains("-turboBaseline")
            ? "before"
            : "after";
        output = Path.GetFullPath(
            Application.dataPath
                + $"/../BuildArtifacts/landing-{label}-{(loaded ? "loaded" : "empty")}{(stowed ? "-stowed" : "")}{(reverse ? "-rear" : "")}{(slow ? "-slow" : "")}{(centerOnly ? "-center" : "")}{(heavy ? "-heavy" : "")}.json"
        );
        var bundle = AssetBundle.LoadFromFile(platform + "rebuilt_ninjas_4744_turbo_2026_assets");
        var prefab = bundle.LoadAsset<GameObject>(
            bundle.GetAllAssetNames().First(n => n.EndsWith("/turbo4744simple.prefab"))
        );
        fuelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
        );
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Landing test carpet";
        floor.layer = LayerMask.NameToLayer("Field");
        floor.tag = "Field";
        floor.transform.position = new Vector3(6, 2.95f, 2);
        floor.transform.localScale = new Vector3(12, .1f, 22);
        var bump = Object.Instantiate(
            AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Games/Rebuilt/Field/Blue/BlueBump.prefab"
            )
        );
        Bounds bounds = BoundsOf(bump);
        if (bounds.size.x < bounds.size.z)
            bump.transform.rotation =
                Quaternion.AngleAxis(90, Vector3.up) * bump.transform.rotation;
        bounds = BoundsOf(bump);
        // Original prefab Y=0 is carpet height. Some end supports extend
        // below carpet; their aggregate lower bound is not the floor datum.
        bump.transform.position += new Vector3(6 - bounds.center.x, 3, 2 - bounds.center.z);
        bounds = BoundsOf(bump);
        report.bumpBounds = bounds.ToString("F4");
        // The prefab spans both ramps and the hub gap between them. Cross
        // the middle of a ramp, not the empty central lane.
        float laneX = bounds.center.x + 1.6f;
        Debug.Log("TURBO_LANDING_BUMP " + report.bumpBounds);
        var positions = new List<Vector2> { new Vector2(9, .5f) };
        foreach (float x in new[] { -.297375f, 0, .297375f })
        foreach (
            float z in loaded ? new[] { slow || reverse ? .25f : .5f } : new[] { .25f, .5f, .8f }
        )
            positions.Add(new Vector2(x, z));
        if (centerOnly)
            positions = new List<Vector2>
            {
                new Vector2(0, .25f),
                new Vector2(0, .25f),
                new Vector2(0, .25f),
            };
        foreach (var position in positions)
        {
            current = new Trial
            {
                loaded = loaded,
                stowed = stowed,
                approachSpeed = speed,
                heading = reverse ? 180 : 0,
                clearControl = position.x == 9,
                ballX = position.x,
                ballBeyondBump = position.y,
            };
            report.cases.Add(current);
            recording = false;
            robot = Object
                .Instantiate(
                    prefab,
                    new Vector3(laneX, 3.08f, bounds.min.z - 1.05f),
                    Quaternion.Euler(0, reverse ? 180 : 0, 0)
                )
                .GetComponent<Turbo4744>();
            robot.SetAlliance(Alliance.Blue);
            foreach (var preload in robot.preloads)
                if (preload)
                    preload.SetActive(false);
            robot.GetComponent<PlayerInput>().actions.devices = new InputDevice[] { keyboard };
            body = robot.GetComponent<Rigidbody>();
            drive = robot.GetComponent<DriveController>();
            foreach (var rb in robot.GetComponentsInChildren<Rigidbody>())
                rb.gameObject.AddComponent<Turbo4744LandingWitness>().sample = Collision;
            yield return Wait(.3f);
            Press(Key.LeftShift);
            yield return Wait(2.8f);
            Press();
            cargo.Clear();
            if (loaded)
                yield return Fill();
            if (stowed)
            {
                Press(Key.Z);
                yield return Wait(loaded ? 8 : 3);
                Press();
            }
            current.storedBefore = Stored();
            Rigidbody loose = null;
            if (!current.clearControl)
                loose = Spawn(new Vector3(laneX + position.x, 3.075f, bounds.max.z + position.y));
            yield return Wait(.3f);
            Vector3 start = body.position;
            driveBegan = Time.fixedTime;
            recording = true;
            float airborne = 0,
                stalled = 0;
            bool captured = false;
            for (float t = 0; t < (slow ? 14 : 7); t += Time.fixedDeltaTime)
            {
                drive.OverrideInput(0, speed, 0, DriveController.DrivePerspective.FieldOriented);
                yield return step;
                var patches = (Array)patchesField.GetValue(drive);
                int supported = patches
                    .Cast<object>()
                    .Count(p => (float)p.GetType().GetField("NormalLoad").GetValue(p) > 0);
                float measuredSpeed = Vector3.ProjectOnPlane(body.velocity, Vector3.up).magnitude;
                airborne = supported == 0 ? airborne + Time.fixedDeltaTime : 0;
                stalled =
                    t > 1 && supported > 0 && measuredSpeed < .1f
                        ? stalled + Time.fixedDeltaTime
                        : 0;
                current.longestAirborne = Mathf.Max(current.longestAirborne, airborne);
                if (!captured && airborne > 1 && measuredSpeed < .15f)
                {
                    captured = true;
                    CaptureUnderside();
                }
                current.longestSupportedStall = Mathf.Max(current.longestSupportedStall, stalled);
                current.maximumHeight = Mathf.Max(current.maximumHeight, body.position.y - 3);
                current.maximumTilt = Mathf.Max(
                    current.maximumTilt,
                    Vector3.Angle(body.rotation * Vector3.up, Vector3.up)
                );
                current.crossedBump |= body.position.z > bounds.max.z;
                if (current.frames.Count == 0 || t >= current.frames.Count * .05f)
                    current.frames.Add(
                        $"t={t:F3} pos={body.position.ToString("F4")} vel={body.velocity.ToString("F3")} tilt={Vector3.Angle(body.rotation * Vector3.up, Vector3.up):F2} support={supported} patches={string.Join(";", patches.Cast<object>().Select(p => $"{p.GetType().GetField("Count").GetValue(p)}/{p.GetType().GetField("NormalLoad").GetValue(p)}"))}"
                    );
                if (body.position.z > bounds.max.z + 2.5f)
                    break;
            }
            current.distance = Vector3.ProjectOnPlane(body.position - start, Vector3.up).magnitude;
            current.finalSpeed = Vector3.ProjectOnPlane(body.velocity, Vector3.up).magnitude;
            current.storedAfter = Stored();
            current.recovered = body.position.z > bounds.max.z + 1.8f;
            current.passed =
                current.recovered
                && current.maximumHeight > .09f
                && (current.clearControl || current.fuelContacts > 0)
                && (!loaded || current.storedBefore >= 50);
            Debug.Log(
                $"TURBO_LANDING loaded={loaded} ball={position} stored={current.storedBefore}/{current.storedAfter} crossed={current.crossedBump} recovered={current.recovered} stall={current.longestSupportedStall:F2} height={current.maximumHeight:F3} contacts={current.fuelContacts}"
            );
            Save();
            robot.gameObject.SetActive(false);
            foreach (var fuel in cargo)
                if (fuel)
                    fuel.gameObject.SetActive(false);
            if (loose)
                loose.gameObject.SetActive(false);
            Press();
            yield return step;
        }
        report.completed = true;
        report.passed = report.errors.Count == 0 && report.cases.All(c => c.passed);
        Save();
        Application.logMessageReceived -= Log;
        InputSystem.RemoveDevice(keyboard);
        SessionState.SetInt("Turbo.Trench.Exit", report.passed ? 0 : 1);
        EditorApplication.ExitPlaymode();
    }

    private IEnumerator Fill()
    {
        float last = -1;
        for (float t = 0; t < (heavy ? 30 : 25); t += Time.fixedDeltaTime)
        {
            drive.OverrideInput(0, 0, 0, DriveController.DrivePerspective.RobotRelative);
            if (cargo.Count < (heavy ? 72 : 64) && t - last >= .16f && Stored() < (heavy ? 61 : 59))
            {
                var point = robot.transform.TransformPoint(
                    new Vector3((cargo.Count % 3 - 1) * .16f, .085f, .66f)
                );
                point.y = 3.08f;
                if (
                    !Physics.CheckSphere(
                        point,
                        .076f,
                        LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt"),
                        QueryTriggerInteraction.Ignore
                    )
                )
                {
                    cargo.Add(Spawn(point));
                    last = t;
                }
            }
            yield return step;
            if (Stored() >= (heavy ? 61 : 59) && t - last > 2)
                break;
        }
        Debug.Log($"TURBO_LANDING_LOAD offered={cargo.Count} stored={Stored()}");
    }

    private int Stored() =>
        cargo.Count(fuel =>
        {
            if (!fuel)
                return false;
            var p = robot.transform.InverseTransformPoint(fuel.position);
            return robot.GetComponent<Turbo4744FuelKeeper>().IsCaptured(fuel)
                && Mathf.Abs(p.x) < .31f
                && p.y > .22f
                && p.y < .95f
                && p.z > .08f
                && p.z < .60f;
        });

    private void Collision(Collision collision)
    {
        if (!recording || current == null || !body)
            return;
        bool fuel = collision.collider.GetComponentInParent<RebuiltGamePieceController>();
        bool loose = fuel && !cargo.Contains(collision.rigidbody);
        if (fuel && !loose)
            return;
        if (loose)
        {
            current.fuelContacts++;
            current.undersideFuelContacts += collision.contacts.Count(c =>
                c.thisCollider.name.StartsWith("Bellypan ")
                || c.thisCollider.name.StartsWith("Swerve ")
                || c.thisCollider.GetComponentInParent<DriveWheel>()
            );
        }
        if (
            current.contacts.Count < 300
            && (fuel || Vector3.ProjectOnPlane(body.velocity, Vector3.up).magnitude < .15f)
        )
            foreach (var contact in collision.contacts.Take(4))
                current.contacts.Add(
                    $"t={Time.fixedTime - driveBegan:F3} {contact.thisCollider.name}/{contact.otherCollider.name} p={contact.point.ToString("F4")} n={contact.normal.ToString("F3")} impulse={collision.impulse.ToString("F3")}"
                );
    }

    private Rigidbody Spawn(Vector3 point) =>
        Object
            .Instantiate(
                fuelPrefab,
                point,
                Quaternion.identity,
                GameObject.FindWithTag("GamePieceWorld").transform
            )
            .GetComponent<Rigidbody>();

    private static Bounds BoundsOf(GameObject go)
    {
        var colliders = go.GetComponentsInChildren<Collider>()
            .Where(c => !c.isTrigger && c.enabled)
            .ToArray();
        Physics.SyncTransforms();
        var bounds = colliders[0].bounds;
        foreach (var c in colliders)
            bounds.Encapsulate(c.bounds);
        return bounds;
    }

    private void Save() => File.WriteAllText(output, JsonUtility.ToJson(report, true));

    private void CaptureUnderside()
    {
        var go = new GameObject("Landing underside evidence");
        var camera = go.AddComponent<Camera>();
        camera.nearClipPlane = .02f;
        camera.orthographic = true;
        camera.orthographicSize = .45f;
        var targetPoint = new Vector3(body.position.x, 3.25f, body.position.z);
        camera.transform.position = targetPoint + Vector3.right * 1.6f;
        camera.transform.LookAt(targetPoint);
        var texture = new RenderTexture(1600, 900, 24);
        var previous = RenderTexture.active;
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture.active = texture;
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        image.Apply();
        File.WriteAllBytes(
            Path.ChangeExtension(output, null) + $"-case-{report.cases.Count}.png",
            image.EncodeToPNG()
        );
        RenderTexture.active = previous;
        camera.targetTexture = null;
        texture.Release();
        Object.Destroy(image);
        Object.Destroy(texture);
        Object.Destroy(go);
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
        if (Time.realtimeSinceStartup - began < 600)
            return;
        SessionState.SetInt("Turbo.Trench.Exit", 1);
        EditorApplication.ExitPlaymode();
    }
}

public sealed class Turbo4744LandingWitness : MonoBehaviour
{
    public Action<Collision> sample;

    private void OnCollisionEnter(Collision collision) => sample?.Invoke(collision);

    private void OnCollisionStay(Collision collision) => sample?.Invoke(collision);
}
