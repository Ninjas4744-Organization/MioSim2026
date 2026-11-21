using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Games.Rebuilt.GamePieceSystem;
using Ninjas4744.MoSim;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

public static class Turbo4744BellypanRegression
{
    public static Turbo4744BellypanFixture Begin(
        Turbo4744 robot,
        Action<Key[]> input,
        Action<bool> enable
    )
    {
        var check = robot.gameObject.AddComponent<Turbo4744BellypanFixture>();
        check.Begin(robot, input, enable);
        return check;
    }
}

/// Exercises the live robot's collision pairs after the normal integration cycle.
/// Fixtures reposition bodies only between trials; stock fuel remains dynamic.
public sealed class Turbo4744BellypanFixture : MonoBehaviour
{
    public bool Completed { get; private set; }
    public bool BlocksFuel { get; private set; } = true;
    public bool SupportsChassis { get; private set; }
    public bool FieldContact { get; private set; }
    public readonly List<string> samples = new List<string>();
    private Turbo4744 robot;
    private Rigidbody body;
    private GameObject fuelPrefab;
    private BoxCollider[] pan;
    private Action<Key[]> press;
    private Action<bool> enableRobot;
    private readonly WaitForFixedUpdate step = new WaitForFixedUpdate();

    public void Begin(Turbo4744 target, Action<Key[]> input, Action<bool> enable)
    {
        robot = target;
        body = robot.GetComponent<Rigidbody>();
        press = input;
        enableRobot = enable;
        fuelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Games/Rebuilt/Fuel.prefab"
        );
        pan = robot
            .GetComponentsInChildren<BoxCollider>()
            .Where(c => c.name.StartsWith("Bellypan "))
            .ToArray();
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        body.velocity = body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        MoveRig(new Vector3(body.position.x, 1.2f, body.position.z));
        // Rear, middle, front and both shared boundaries, independently of builder data.
        float[] positions = { -.2052525f, .0532175f, .2249475f, -.0869825f, .1934175f };
        string[] states = { "stowed", "collecting", "reverse", "disabled" };
        bool openedFloor = false;
        // The game's stock lower-chassis contact law can compress fuel without
        // changing its sphere. Read that allowance; never alter it for this test.
        var manager = new SerializedObject(RebuiltGamePieceManager.Instance);
        float compression = manager.FindProperty("fuelCompressionDepth")?.floatValue ?? 0f;
        samples.Add($"Stock chassis fuel compression allowance: {compression:F4} m");
        for (int state = 0; state < states.Length; state++)
        {
            enableRobot(state != 3);
            press(
                state == 0 ? new[] { Key.Z }
                : state == 1 ? new[] { Key.LeftShift }
                : state == 2 ? new[] { Key.U }
                : Array.Empty<Key>()
            );
            yield return Wait(state < 2 ? 2.6f : .2f);
            bool command =
                state == 0 ? robot.PumpMode == Turbo4744.RailState.Closed
                : state == 1 ? robot.Collecting
                : state == 2 ? robot.Reversing
                : MoSimCore.BaseClasses.GameManagement.BaseGameManager.Instance.RobotState
                    == MoSimCore.Enums.RobotState.Disabled;
            BlocksFuel &= command;
            samples.Add(states[state] + " command active: " + command);
            foreach (float z in positions)
            {
                var piece = Spawn(robot.transform.TransformPoint(new Vector3(0, -.0482f, z)));
                var sphere = piece
                    .GetComponentsInChildren<SphereCollider>()
                    .Where(c => !c.isTrigger)
                    .OrderByDescending(c => c.radius)
                    .First();
                var witness = piece.gameObject.AddComponent<Turbo4744BellypanWitness>();
                float radius = sphere.radius * sphere.transform.lossyScale.x;
                float highest = float.NegativeInfinity;
                bool solid = true;
                for (float t = 0; t < .3f; t += Time.fixedDeltaTime)
                {
                    piece.AddForce(Vector3.up * 20f);
                    yield return step;
                    float top =
                        robot
                            .transform.InverseTransformPoint(
                                sphere.transform.TransformPoint(sphere.center)
                            )
                            .y + radius;
                    highest = Mathf.Max(highest, top);
                    solid &=
                        pan.Length == 3
                        && pan.All(c =>
                            c.enabled && !c.isTrigger && !Physics.GetIgnoreCollision(c, sphere)
                        );
                    if (state == 1)
                        openedFloor |= Physics.GetIgnoreCollision(
                            robot.GetComponent<Turbo4744FuelKeeper>().floor,
                            sphere
                        );
                }
                bool passed =
                    solid
                    && witness.contacts.Count > 0
                    && highest < .0468f + compression + .01f
                    && highest - radius < .0368f;
                BlocksFuel &= passed;
                samples.Add(
                    $"{states[state]} z={z:F6} top={highest:F5} contacts={string.Join(",", witness.contacts)} passed={passed}"
                );
                Object.Destroy(piece.gameObject);
                yield return step;
            }
        }
        BlocksFuel &= openedFloor;
        samples.Add("Intake opened its floor pair during underside probes: " + openedFloor);

        // First measure normal chassis height on a clear, elevated field fixture.
        enableRobot(false);
        press(Array.Empty<Key>());
        var floor = Fixture(
            "Bellypan test carpet",
            new Vector3(2, .05f, 2),
            new Vector3(body.position.x, .975f, body.position.z)
        );
        MoveRig(new Vector3(body.position.x, 1.25f, body.position.z));
        body.isKinematic = false;
        yield return Wait(1f);
        float baseline = body.position.y;

        body.isKinematic = true;
        MoveRig(new Vector3(body.position.x, 1.25f, body.position.z));
        var supports = new List<Rigidbody>();
        foreach (
            var offset in new[]
            {
                new Vector3(-.14f, 0, -.14f),
                new Vector3(.14f, 0, -.14f),
                new Vector3(0, 0, .13f),
            }
        )
        {
            var piece = Spawn(
                new Vector3(body.position.x + offset.x, 1.075f, body.position.z + offset.z)
            );
            piece.gameObject.AddComponent<Turbo4744BellypanWitness>();
            supports.Add(piece);
        }
        yield return step;
        body.isKinematic = false;
        yield return Wait(.7f);
        float raised = body.position.y - baseline;
        float impulse = supports.Sum(p =>
            p.GetComponent<Turbo4744BellypanWitness>().verticalImpulse
        );
        SupportsChassis =
            raised > .03f
            && impulse > 1f
            && supports.All(p => p.GetComponent<Turbo4744BellypanWitness>().contacts.Count > 0);
        samples.Add(
            $"Stock fuel support: baseline={baseline:F4} raised={raised:F4} impulse={impulse:F3} passed={SupportsChassis}"
        );
        foreach (var piece in supports)
            Object.Destroy(piece.gameObject);

        body.isKinematic = true;
        MoveRig(new Vector3(body.position.x, 1.25f, body.position.z));
        var obstacle = Fixture(
            "Bellypan field obstacle",
            new Vector3(.12f, .10f, .12f),
            new Vector3(body.position.x, 1.05f, body.position.z)
        );
        var obstacleWitness = obstacle.AddComponent<Turbo4744BellypanWitness>();
        yield return step;
        body.isKinematic = false;
        yield return Wait(.7f);
        raised = body.position.y - baseline;
        FieldContact =
            obstacleWitness.contacts.Count > 0
            && obstacleWitness.verticalImpulse > 1f
            && raised > .02f;
        samples.Add(
            $"Field support: raised={raised:F4} impulse={obstacleWitness.verticalImpulse:F3} passed={FieldContact}"
        );
        Object.Destroy(obstacle);
        Object.Destroy(floor);
        Completed = true;
    }

    private IEnumerator Wait(float seconds)
    {
        for (float t = 0; t < seconds; t += Time.fixedDeltaTime)
            yield return step;
    }

    private Rigidbody Spawn(Vector3 position) =>
        Object
            .Instantiate(
                fuelPrefab,
                position,
                Quaternion.identity,
                GameObject.FindWithTag("GamePieceWorld").transform
            )
            .GetComponent<Rigidbody>();

    private static GameObject Fixture(string name, Vector3 size, Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.layer = LayerMask.NameToLayer("Field");
        go.tag = "Field";
        go.transform.position = position;
        go.transform.localScale = size;
        return go;
    }

    private void MoveRig(Vector3 position)
    {
        var bodies = robot.GetComponentsInChildren<Rigidbody>();
        var origin = body.position;
        var rotation = Quaternion.Inverse(body.rotation);
        var positions = bodies.Select(b => position + rotation * (b.position - origin)).ToArray();
        var rotations = bodies.Select(b => rotation * b.rotation).ToArray();
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].isKinematic)
                bodies[i].velocity = bodies[i].angularVelocity = Vector3.zero;
            bodies[i].position = positions[i];
            bodies[i].rotation = rotations[i];
        }
        Physics.SyncTransforms();
    }
}

public sealed class Turbo4744BellypanWitness : MonoBehaviour
{
    public readonly HashSet<string> contacts = new HashSet<string>();
    public float verticalImpulse;

    private void OnCollisionEnter(Collision collision) => Record(collision);

    private void OnCollisionStay(Collision collision) => Record(collision);

    private void Record(Collision collision)
    {
        foreach (var contact in collision.contacts)
        {
            string name = contact.otherCollider.name;
            if (!name.StartsWith("Bellypan "))
                continue;
            contacts.Add(name);
            verticalImpulse += Mathf.Abs(collision.impulse.y);
            break;
        }
    }
}
