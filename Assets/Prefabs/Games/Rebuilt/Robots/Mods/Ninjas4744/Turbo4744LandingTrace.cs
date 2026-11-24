using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using RobotFramework.Controllers.Drivetrain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ninjas4744.MoSim
{
    /// <summary>Opt-in, local-only stall evidence. Reads the existing drivetrain
    /// contact cache; never changes input, bodies, contacts or drive settings.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class Turbo4744LandingTrace : MonoBehaviour
    {
        public const string OptInFile = "Turbo4744-capture-landings.enabled";
        public const string CaptureRequestFile = "Turbo4744-capture-now.txt";
        private const int Capacity = 250;
        private const float SampleInterval = .02f;

        [Serializable]
        public sealed class Contact
        {
            public string robotCollider,
                otherCollider;
            public int otherLayer;
            public Vector3 point,
                normal,
                otherPosition;
            public Quaternion otherRotation;
        }

        [Serializable]
        public sealed class Frame
        {
            public float time,
                tilt;
            public Vector3 position,
                velocity,
                angularVelocity,
                target;
            public Quaternion rotation;
            public Vector3[] wheelQueryCenter = new Vector3[4],
                tireCenter = new Vector3[4],
                suspensionPoint = new Vector3[4],
                suspensionNormal = new Vector3[4];
            public Vector2 playerInput;
            public float playerTurn;
            public bool kinematic;
            public int constraints;
            public int clearanceGroundedWheels,
                clearedUnderbodyBalls,
                blockedClearanceSearches,
                lastClearGroundedWheels;
            public int activeClearances,
                ignoredClearancePairs,
                assistedClearanceSteps;
            public Vector3 lastClearFrom,
                lastClearTo;
            public int[] contacts = new int[4],
                floorContacts = new int[4];
            public float[] normalLoad = new float[4],
                treadSpeed = new float[4];
            public float[] tireMinimumY = new float[4],
                suspensionForce = new float[4];
            public bool[] tireFieldCollision = new bool[4];
            public Contact[] collisions;
        }

        [Serializable]
        public sealed class Capture
        {
            public string reason = "Drive requested, speed below 0.15 m/s for 0.8 seconds";
            public string robot,
                runtimeModule;
            public float physicsStep;
            public string[] wheels;
            public List<Frame> frames = new List<Frame>();
        }

        private struct CollisionSample
        {
            public Collider own,
                other;
            public Vector3 point,
                normal;
        }

        private readonly Frame[] ring = new Frame[Capacity];
        private readonly CollisionSample[] collisions = new CollisionSample[12];
        private readonly HashSet<Collider> underside = new HashSet<Collider>();
        private DriveController drive;
        private Turbo4744 robot;
        private Turbo4744UnderbodyClearance clearance;
        private Rigidbody body;
        private Array patches;
        private DriveWheel[] wheels;
        private MeshCollider[][] tires;
        private readonly Dictionary<Collider, int> tireIndex = new Dictionary<Collider, int>();
        private readonly float[] tireContactTime = { -100, -100, -100, -100 };
        private FieldInfo count,
            floorCount,
            load,
            tread;
        private int cursor,
            samples,
            collisionCount,
            captures;
        private float nextSample,
            lastSample,
            stalled,
            writeAt = -1,
            cooldown;
        private float nextRequestCheck;
        private DateTime lastRequestWrite;
        public string LastCapturePath { get; private set; }

        public static void AttachIfRequested(Turbo4744DriveController controller)
        {
            bool enabled = Application.isEditor
                ? Array.IndexOf(Environment.GetCommandLineArgs(), "-turboCaptureLandings") >= 0
                : File.Exists(Path.Combine(Application.persistentDataPath, OptInFile));
            if (enabled && !controller.GetComponent<Turbo4744LandingTrace>())
                controller.gameObject.AddComponent<Turbo4744LandingTrace>();
        }

        private void Start()
        {
            try
            {
                drive = GetComponent<DriveController>();
                robot = GetComponent<Turbo4744>();
                clearance = GetComponent<Turbo4744UnderbodyClearance>();
                body = GetComponent<Rigidbody>();
                patches = (Array)
                    typeof(DriveController)
                        .GetField("_contactPatches", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(drive);
                wheels = (DriveWheel[])
                    typeof(DriveController)
                        .GetField("_wheels", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(drive);
                tires = new MeshCollider[wheels.Length][];
                for (int i = 0; i < wheels.Length; i++)
                {
                    tires[i] = wheels[i].GetComponentsInChildren<MeshCollider>();
                    foreach (var tire in tires[i])
                        tireIndex[tire] = i;
                }
                Type patch = patches.GetType().GetElementType();
                count = patch.GetField("Count");
                floorCount = patch.GetField("FloorCount");
                load = patch.GetField("NormalLoad");
                tread = patch.GetField("SurfaceSpeed");
                if (
                    patches.Length != 4
                    || count == null
                    || floorCount == null
                    || load == null
                    || tread == null
                )
                    throw new InvalidOperationException("Unsupported drivetrain contact cache");
                foreach (var collider in GetComponentsInChildren<Collider>())
                    if (
                        collider.attachedRigidbody == body
                        && (
                            collider.name.StartsWith("Bellypan ")
                            || collider.name.StartsWith("Bumper ")
                            || collider.name.StartsWith("Swerve ")
                            || collider.GetComponentInParent<DriveWheel>()
                        )
                    )
                        underside.Add(collider);
                Debug.Log(
                    "[Turbo landing capture] Armed; F9 saves wheel contacts immediately. Remove "
                        + OptInFile
                        + " to disable."
                );
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Turbo landing capture] Disabled: " + exception.Message);
                enabled = false;
            }
        }

        private void Update()
        {
            if (!drive || !body)
                return;
            bool requested = Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame;
            if (Time.unscaledTime >= nextRequestCheck)
            {
                nextRequestCheck = Time.unscaledTime + .5f;
                string path = Path.Combine(Application.persistentDataPath, CaptureRequestFile);
                DateTime written = File.Exists(path)
                    ? File.GetLastWriteTimeUtc(path)
                    : DateTime.MinValue;
                if (written > lastRequestWrite)
                {
                    lastRequestWrite = written;
                    requested = true;
                }
            }
            if (requested)
            {
                Sample();
                Save("Manual snapshot; no movement or enabled state required", false);
            }
        }

        private void FixedUpdate()
        {
            if (!drive || !body || !BaseGameManager.Instance)
                return;
            if (BaseGameManager.Instance.RobotState != RobotState.Enabled)
            {
                stalled = 0;
                writeAt = -1;
                lastSample = Time.fixedTime;
                return;
            }
            if (Time.fixedTime < nextSample)
                return;
            float elapsed = lastSample > 0 ? Time.fixedTime - lastSample : 0;
            lastSample = Time.fixedTime;
            nextSample = Time.fixedTime + SampleInterval;
            Frame frame = Sample();
            if (captures >= 2)
                return;
            bool requested =
                frame.target.sqrMagnitude > .25f
                || frame.playerInput.sqrMagnitude > .25f
                || Mathf.Abs(frame.playerTurn) > .5f;
            bool stopped = Vector3.ProjectOnPlane(frame.velocity, Vector3.up).sqrMagnitude < .0225f;
            stalled = requested && stopped ? stalled + elapsed : 0;
            if (writeAt < 0 && stalled >= .8f && Time.fixedTime >= cooldown)
                writeAt = Time.fixedTime + .5f;
            if (writeAt >= 0 && Time.fixedTime >= writeAt)
            {
                Save("Drive requested, speed below 0.15 m/s for 0.8 seconds", true);
                writeAt = -1;
                cooldown = Time.fixedTime + 10;
                stalled = 0;
            }
        }

        private Frame Sample()
        {
            var frame = new Frame
            {
                time = Time.fixedTime,
                position = body.position,
                rotation = body.rotation,
                velocity = body.velocity,
                angularVelocity = body.angularVelocity,
                target = drive.GetTargetWorldVelocity(),
                playerInput = robot.TranslateAction.ReadValue<Vector2>(),
                playerTurn = robot.RotateAction.ReadValue<float>(),
                tilt = Vector3.Angle(body.rotation * Vector3.up, Vector3.up),
                kinematic = body.isKinematic,
                constraints = (int)body.constraints,
                clearanceGroundedWheels = clearance ? clearance.GroundedWheelCount : 0,
                clearedUnderbodyBalls = clearance ? clearance.ClearedBalls : 0,
                blockedClearanceSearches = clearance ? clearance.BlockedSearches : 0,
                lastClearGroundedWheels = clearance ? clearance.LastClearGroundedWheels : 0,
                activeClearances = clearance ? clearance.ActiveClearances : 0,
                ignoredClearancePairs = clearance ? clearance.IgnoredCollisionPairs : 0,
                assistedClearanceSteps = clearance ? clearance.AssistedSteps : 0,
                lastClearFrom = clearance ? clearance.LastClearFrom : Vector3.zero,
                lastClearTo = clearance ? clearance.LastClearTo : Vector3.zero,
                collisions = new Contact[collisionCount],
            };
            for (int i = 0; i < 4; i++)
            {
                object patch = patches.GetValue(i);
                frame.contacts[i] = (int)count.GetValue(patch);
                frame.floorContacts[i] = (int)floorCount.GetValue(patch);
                frame.normalLoad[i] = (float)load.GetValue(patch);
                frame.treadSpeed[i] = (float)tread.GetValue(patch);
                frame.tireMinimumY[i] = float.PositiveInfinity;
                wheels[i].Collider.GetWorldPose(out var wheelCenter, out _);
                frame.wheelQueryCenter[i] = wheelCenter;
                int tireCount = 0;
                foreach (var tire in tires[i])
                    if (tire && tire.enabled && !tire.isTrigger)
                    {
                        frame.tireMinimumY[i] = Mathf.Min(frame.tireMinimumY[i], tire.bounds.min.y);
                        frame.tireCenter[i] += tire.bounds.center;
                        tireCount++;
                    }
                frame.tireCenter[i] =
                    tireCount > 0 ? frame.tireCenter[i] / tireCount : wheels[i].transform.position;
                if (float.IsPositiveInfinity(frame.tireMinimumY[i]))
                    frame.tireMinimumY[i] = body.position.y;
                frame.tireFieldCollision[i] =
                    Time.fixedTime - tireContactTime[i] <= 2 * Time.fixedDeltaTime;
                if (wheels[i].Collider.GetGroundHit(out var ground))
                {
                    frame.suspensionForce[i] = ground.force;
                    frame.suspensionPoint[i] = ground.point;
                    frame.suspensionNormal[i] = ground.normal;
                }
            }
            for (int i = 0; i < collisionCount; i++)
            {
                var contact = collisions[i];
                frame.collisions[i] = new Contact
                {
                    robotCollider = contact.own ? contact.own.name : "destroyed",
                    otherCollider = contact.other ? contact.other.name : "destroyed",
                    otherLayer = contact.other ? contact.other.gameObject.layer : -1,
                    point = contact.point,
                    normal = contact.normal,
                    otherPosition = contact.other ? contact.other.transform.position : Vector3.zero,
                    otherRotation = contact.other
                        ? contact.other.transform.rotation
                        : Quaternion.identity,
                };
            }
            collisionCount = 0;
            ring[cursor] = frame;
            cursor = (cursor + 1) % Capacity;
            samples = Mathf.Min(Capacity, samples + 1);
            return frame;
        }

        private void Save(string reason, bool automatic)
        {
            try
            {
                var report = new Capture
                {
                    reason = reason,
                    robot = robot.name,
                    runtimeModule =
                        typeof(Turbo4744).Assembly.ManifestModule.ModuleVersionId.ToString(),
                    physicsStep = Time.fixedDeltaTime,
                    wheels = Array.ConvertAll(wheels, wheel => wheel.name),
                };
                for (int i = 0; i < samples; i++)
                    report.frames.Add(ring[(cursor - samples + i + Capacity) % Capacity]);
                string folder = Application.isEditor
                    ? Path.GetFullPath(Application.dataPath + "/../BuildArtifacts/landing-captures")
                    : Path.Combine(Application.persistentDataPath, "Turbo4744-LandingCaptures");
                Directory.CreateDirectory(folder);
                LastCapturePath = Path.Combine(
                    folder,
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")
                        + "-"
                        + Guid.NewGuid().ToString("N").Substring(0, 6)
                        + ".json"
                );
                // Native MoSim loads the mod after the player is built. Its
                // Unity serializer omits lists of mod-defined nested records.
                // Write public data fields directly so native reports retain
                // the same wheel/frame evidence as editor reports.
                var json = new StringBuilder();
                WriteJson(json, report);
                File.WriteAllText(LastCapturePath, json.ToString());
                if (automatic)
                    captures++;
                Debug.Log("[Turbo landing capture] Saved " + LastCapturePath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Turbo landing capture] Write failed; capture disabled: " + exception.Message
                );
                enabled = false;
            }
        }

        private static void WriteJson(StringBuilder json, object value)
        {
            if (value == null)
                json.Append("null");
            else if (value is string text)
            {
                json.Append('"');
                foreach (char c in text)
                {
                    if (c == '"' || c == '\\')
                        json.Append('\\').Append(c);
                    else if (c < ' ')
                        json.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        json.Append(c);
                }
                json.Append('"');
            }
            else if (value is bool flag)
                json.Append(flag ? "true" : "false");
            else if (value is float number)
                json.Append(
                    float.IsNaN(number) || float.IsInfinity(number)
                        ? "null"
                        : number.ToString("R", CultureInfo.InvariantCulture)
                );
            else if (value is int integer)
                json.Append(integer.ToString(CultureInfo.InvariantCulture));
            else if (value is IEnumerable items)
            {
                json.Append('[');
                bool first = true;
                foreach (object item in items)
                {
                    if (!first)
                        json.Append(',');
                    first = false;
                    WriteJson(json, item);
                }
                json.Append(']');
            }
            else
            {
                json.Append('{');
                bool first = true;
                foreach (
                    var field in value
                        .GetType()
                        .GetFields(BindingFlags.Public | BindingFlags.Instance)
                )
                {
                    if (!first)
                        json.Append(',');
                    first = false;
                    WriteJson(json, field.Name);
                    json.Append(':');
                    WriteJson(json, field.GetValue(value));
                }
                json.Append('}');
            }
        }

        private void OnCollisionEnter(Collision collision) => Record(collision);

        private void OnCollisionStay(Collision collision) => Record(collision);

        private void Record(Collision collision)
        {
            if (!enabled)
                return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                if (!underside.Contains(contact.thisCollider))
                    continue;
                if (
                    tireIndex.TryGetValue(contact.thisCollider, out int wheel)
                    && contact.otherCollider.gameObject.layer == LayerMask.NameToLayer("Field")
                )
                    tireContactTime[wheel] = Time.fixedTime;
                if (collisionCount >= collisions.Length)
                    continue;
                collisions[collisionCount++] = new CollisionSample
                {
                    own = contact.thisCollider,
                    other = contact.otherCollider,
                    point = contact.point,
                    normal = contact.normal,
                };
            }
        }
    }
}
