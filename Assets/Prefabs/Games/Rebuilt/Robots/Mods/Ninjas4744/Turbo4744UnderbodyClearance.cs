using System;
using System.Collections.Generic;
using Games.Rebuilt.GamePieceSystem;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Grounded-only gameplay clearance for loose fuel trapped under
    /// the chassis. Rolls fuel toward free nearby floor; never moves a body by pose.</summary>
    [DefaultExecutionOrder(150)]
    public sealed class Turbo4744UnderbodyClearance : MonoBehaviour
    {
        private const float HalfWidth = .43f;
        private const float HalfLength = .48f;
        private const float ContactTolerance = .0025f;
        private readonly Dictionary<Collider, int> tires = new Dictionary<Collider, int>();
        private readonly GroundContact[] ground = new GroundContact[4];
        private readonly Collider[] nearby = new Collider[128];
        private readonly Collider[] placement = new Collider[96];
        private readonly RaycastHit[] path = new RaycastHit[64];
        private readonly HashSet<Rigidbody> visited = new HashSet<Rigidbody>();
        private readonly Vector3[] candidates = new Vector3[8];
        private readonly List<Escape> active = new List<Escape>(16);
        private Collider[] solids;
        private Turbo4744FuelKeeper keeper;
        private Rigidbody chassis;
        private int fieldMask,
            fuelMask,
            placementMask;
        private float nextCheck;
        private int searchCursor,
            robotMask;
        public int GroundedWheelCount { get; private set; }
        public int ActiveClearances => active.Count;
        public int IgnoredCollisionPairs { get; private set; }
        public int AssistedSteps { get; private set; }
        public int CancelledClearances { get; private set; }
        public int LastAssistGroundedWheels { get; private set; }
        public float LastClearSeconds { get; private set; }
        public int ClearedBalls { get; private set; }
        public int BlockedSearches { get; private set; }
        public int LastClearGroundedWheels { get; private set; }
        public Vector3 LastClearFrom { get; private set; }
        public Vector3 LastClearTo { get; private set; }
        public event Action<Rigidbody, Vector3, Vector3, int> Escaped;

        private struct CollisionPair
        {
            public Collider robot,
                fuel;
        }

        private sealed class Escape
        {
            public Rigidbody ball;
            public float radius,
                started,
                retargetAt;
            public Vector3 from,
                target;
            public bool needsTarget;
            public readonly List<CollisionPair> pairs = new List<CollisionPair>();
        }

        private struct GroundContact
        {
            public Collider tire,
                field;
            public Vector3 localPoint,
                localNormal;
        }

        private void Start()
        {
            chassis = GetComponent<Rigidbody>();
            keeper = GetComponent<Turbo4744FuelKeeper>();
            solids = GetComponentsInChildren<Collider>();
            fieldMask = LayerMask.GetMask("Field");
            // Query the stock outer fuel sphere; GamePieceAlt is its smaller
            // inner shell and would underestimate placement clearance by 5 mm.
            fuelMask = LayerMask.GetMask("GamePiece1", "GamePiece2");
            robotMask = LayerMask.GetMask("Robot");
            placementMask = fieldMask | fuelMask | robotMask;
            var wheels = GetComponentsInChildren<WheelCollider>();
            for (int i = 0; i < Mathf.Min(wheels.Length, ground.Length); i++)
                foreach (var tire in wheels[i].GetComponentsInChildren<MeshCollider>())
                    if (tire.enabled && !tire.isTrigger && tire.attachedRigidbody == chassis)
                        tires[tire] = i;
        }

        private void OnCollisionEnter(Collision collision) => RecordGround(collision);

        private void OnCollisionStay(Collision collision) => RecordGround(collision);

        private void RecordGround(Collision collision)
        {
            if (
                !isActiveAndEnabled
                || (fieldMask & (1 << collision.collider.gameObject.layer)) == 0
            )
                return;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                if (
                    !tires.TryGetValue(contact.thisCollider, out int wheel)
                    || Vector3.Dot(contact.normal, Vector3.up) < .5f
                )
                    continue;
                // Contact points can lie between surfaces while PhysX retains
                // a contact within its skin. Project onto the actual field so
                // that skin alone cannot authorize clearance for an airborne tire.
                if (
                    !contact.otherCollider.Raycast(
                        new Ray(contact.point + contact.normal * .03f, -contact.normal),
                        out var surface,
                        .06f
                    )
                )
                    continue;
                ground[wheel] = new GroundContact
                {
                    tire = contact.thisCollider,
                    field = contact.otherCollider,
                    localPoint =
                        Quaternion.Inverse(chassis.rotation) * (surface.point - chassis.position),
                    localNormal = contact.otherCollider.transform.InverseTransformDirection(
                        contact.normal
                    ),
                };
            }
        }

        private void FixedUpdate()
        {
            GroundedWheelCount = 0;
            foreach (var contact in ground)
            {
                if (
                    !contact.tire
                    || !contact.field
                    || !contact.tire.enabled
                    || !contact.field.enabled
                    || !contact.tire.gameObject.activeInHierarchy
                    || !contact.field.gameObject.activeInHierarchy
                )
                    continue;
                // Follow chassis translation/heading between physics callbacks,
                // then project onto the field again. A rolling tire must not lose
                // support merely because last step's contact is now behind it.
                var point = chassis.position + chassis.rotation * contact.localPoint;
                var normal = contact.field.transform.TransformDirection(contact.localNormal);
                if (
                    Vector3.Dot(normal, Vector3.up) >= .5f
                    && contact.field.Raycast(
                        new Ray(point + normal * .03f, -normal),
                        out var surface,
                        .06f
                    )
                    && (contact.tire.ClosestPoint(surface.point) - surface.point).sqrMagnitude
                        <= ContactTolerance * ContactTolerance
                )
                    GroundedWheelCount++;
            }
            bool allowed =
                chassis
                && GroundedWheelCount > 0
                && BaseGameManager.Instance
                && BaseGameManager.Instance.RobotState == RobotState.Enabled;
            if (!allowed)
            {
                StopAll();
                return;
            }
            AdvanceEscapes();
            if (Time.fixedTime < nextCheck)
                return;
            nextCheck = Time.fixedTime + .05f;
            int count = Physics.OverlapBoxNonAlloc(
                chassis.position + chassis.rotation * new Vector3(0, -.02f, 0),
                new Vector3(.47f, .22f, .53f),
                nearby,
                chassis.rotation,
                fuelMask,
                QueryTriggerInteraction.Ignore
            );
            visited.Clear();
            int started = 0,
                attempted = 0;
            foreach (var escape in active)
                if (escape.needsTarget && attempted < 4)
                {
                    attempted++;
                    var local =
                        Quaternion.Inverse(chassis.rotation)
                        * (escape.ball.position - chassis.position);
                    if (FindExit(escape.ball, local, escape.radius, out var target))
                        escape.target = target;
                    escape.needsTarget = false;
                    escape.retargetAt = Time.fixedTime + .25f;
                }
            // Bound expensive placement searches even when a wall or pile blocks
            // every exit. Rotate the start so one blocked ball cannot starve others.
            int start = count > 0 ? searchCursor % count : 0;
            for (int i = 0; i < count && started < 2 && active.Count < 16 && attempted < 4; i++)
            {
                searchCursor = start + i + 1;
                var sphere = nearby[(start + i) % count] as SphereCollider;
                var ball = sphere ? sphere.attachedRigidbody : null;
                if (
                    !ball
                    || ball.isKinematic
                    || !visited.Add(ball)
                    || IsEscaping(ball)
                    || !ball.GetComponent<RebuiltGamePieceController>()
                )
                    continue;
                // Stored hopper/feed fuel sits above this low underbody volume.
                // A stale captured flag must not exempt fuel that fell below it.
                var local =
                    Quaternion.Inverse(chassis.rotation) * (ball.position - chassis.position);
                if (
                    Mathf.Abs(local.x) > HalfWidth
                    || Mathf.Abs(local.z) > HalfLength
                    || local.y > .115f
                    || local.y < -.3f
                )
                    continue;
                var scale = sphere.transform.lossyScale;
                float radius =
                    sphere.radius
                    * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                attempted++;
                if (!FindExit(ball, local, radius, out var destination))
                {
                    BlockedSearches++;
                    continue;
                }
                BeginEscape(ball, radius, destination);
                started++;
            }
        }

        private bool IsEscaping(Rigidbody ball)
        {
            foreach (var escape in active)
                if (escape.ball == ball)
                    return true;
            return false;
        }

        private void BeginEscape(Rigidbody ball, float radius, Vector3 target)
        {
            var escape = new Escape
            {
                ball = ball,
                radius = radius,
                target = target,
                from = ball.position,
                started = Time.fixedTime,
                retargetAt = Time.fixedTime + .25f,
            };
            // Release only this robot's solid pinch while the ball rolls away.
            // Keep field/ball/other-robot contacts and the keeper's entry pairs.
            foreach (var fuel in ball.GetComponentsInChildren<Collider>())
                if (!fuel.isTrigger && fuel.enabled)
                    foreach (var solid in solids)
                        if (
                            solid
                            && solid.enabled
                            && !solid.isTrigger
                            && !(solid is WheelCollider)
                            && (robotMask & (1 << solid.gameObject.layer)) != 0
                            && solid.gameObject.activeInHierarchy
                            && (
                                !keeper
                                || solid != keeper.floor
                                    && solid != keeper.fixedFloor
                                    && solid != keeper.inlet
                                    && solid != keeper.frontPanel
                            )
                            && !Physics.GetIgnoreCollision(solid, fuel)
                        )
                        {
                            Physics.IgnoreCollision(solid, fuel, true);
                            escape.pairs.Add(new CollisionPair { robot = solid, fuel = fuel });
                            IgnoredCollisionPairs++;
                        }
            active.Add(escape);
            ball.WakeUp();
        }

        private void AdvanceEscapes()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var escape = active[i];
                var ball = escape.ball;
                if (!ball || !ball.gameObject.activeInHierarchy || ball.isKinematic)
                {
                    EndEscape(i, false);
                    continue;
                }
                var local =
                    Quaternion.Inverse(chassis.rotation) * (ball.position - chassis.position);
                if (
                    Mathf.Abs(local.x) > HalfWidth + escape.radius + .005f
                    || Mathf.Abs(local.z) > HalfLength + escape.radius + .005f
                )
                {
                    EndEscape(i, true);
                    continue;
                }
                if (local.y > .115f || local.y < -.4f || Time.fixedTime - escape.started > 2f)
                {
                    EndEscape(i, false);
                    continue;
                }
                var delta = Vector3.ProjectOnPlane(escape.target - ball.position, Vector3.up);
                var velocity = Vector3.ProjectOnPlane(ball.velocity, Vector3.up);
                float speed = Mathf.Min(1.1f, Mathf.Sqrt(12f * delta.magnitude));
                var desired = delta.normalized * speed;
                // Bounded forces/torque give continuous translation and rolling;
                // gravity and external collisions determine the vertical motion.
                ball.AddForce(
                    Vector3.ClampMagnitude((desired - velocity) / Time.fixedDeltaTime, 16f),
                    ForceMode.Acceleration
                );
                var spin = Vector3.Cross(Vector3.up, desired) / escape.radius;
                ball.AddTorque(
                    Vector3.ClampMagnitude(
                        Vector3.ProjectOnPlane(spin - ball.angularVelocity, Vector3.up)
                            / Time.fixedDeltaTime,
                        90f
                    ),
                    ForceMode.Acceleration
                );
                AssistedSteps++;
                LastAssistGroundedWheels = GroundedWheelCount;
                if (
                    Time.fixedTime >= escape.retargetAt
                    && (delta.magnitude < .07f || velocity.magnitude < .08f)
                )
                    escape.needsTarget = true;
            }
        }

        private void EndEscape(int index, bool cleared)
        {
            var escape = active[index];
            foreach (var pair in escape.pairs)
            {
                if (pair.robot && pair.fuel)
                    Physics.IgnoreCollision(pair.robot, pair.fuel, false);
                IgnoredCollisionPairs--;
            }
            active.RemoveAt(index);
            if (!cleared)
            {
                CancelledClearances++;
                return;
            }
            ClearedBalls++;
            LastClearGroundedWheels = GroundedWheelCount;
            LastClearSeconds = Time.fixedTime - escape.started;
            LastClearFrom = escape.from;
            LastClearTo = escape.ball.position;
            Escaped?.Invoke(escape.ball, escape.from, escape.ball.position, GroundedWheelCount);
        }

        private void StopAll()
        {
            while (active.Count > 0)
                EndEscape(active.Count - 1, false);
        }

        private bool FindExit(Rigidbody ball, Vector3 local, float radius, out Vector3 result)
        {
            var heading = Quaternion.Euler(0, chassis.rotation.eulerAngles.y, 0);
            for (int ring = 0; ring < 12; ring++)
            {
                float x = HalfWidth + radius + .015f + ring * .16f;
                float z = HalfLength + radius + .015f + ring * .16f;
                candidates[0] = new Vector3(x, 0, local.z);
                candidates[1] = new Vector3(-x, 0, local.z);
                candidates[2] = new Vector3(local.x, 0, z);
                candidates[3] = new Vector3(local.x, 0, -z);
                candidates[4] = new Vector3(x, 0, z);
                candidates[5] = new Vector3(-x, 0, z);
                candidates[6] = new Vector3(x, 0, -z);
                candidates[7] = new Vector3(-x, 0, -z);
                int tried = 0;
                for (int n = 0; n < candidates.Length; n++)
                {
                    int best = -1;
                    float distance = float.PositiveInfinity;
                    for (int j = 0; j < candidates.Length; j++)
                    {
                        if ((tried & (1 << j)) != 0)
                            continue;
                        float d = (candidates[j] - new Vector3(local.x, 0, local.z)).sqrMagnitude;
                        if (d < distance)
                        {
                            best = j;
                            distance = d;
                        }
                    }
                    tried |= 1 << best;
                    var target = chassis.position + heading * candidates[best];
                    var origin = new Vector3(target.x, ball.position.y + .3f, target.z);
                    if (
                        !Physics.Raycast(
                            origin,
                            Vector3.down,
                            out var floor,
                            1f,
                            fieldMask,
                            QueryTriggerInteraction.Ignore
                        )
                        || Vector3.Dot(floor.normal, Vector3.up) < .6f
                        || (floor.rigidbody && !floor.rigidbody.isKinematic)
                    )
                        continue;
                    target = floor.point + floor.normal * (radius + .003f);
                    var bounds = floor.collider.bounds;
                    if (
                        target.y > ball.position.y + .08f
                        || target.y < ball.position.y - .5f
                        || target.x < bounds.min.x + radius
                        || target.x > bounds.max.x - radius
                        || target.z < bounds.min.z + radius
                        || target.z > bounds.max.z - radius
                    )
                        continue;
                    bool free = true;
                    foreach (var escape in active)
                        if (
                            escape.ball != ball
                            && (escape.target - target).sqrMagnitude
                                < (escape.radius + radius + .004f)
                                    * (escape.radius + radius + .004f)
                        )
                            free = false;
                    if (!free)
                        continue;
                    int overlaps = Physics.OverlapSphereNonAlloc(
                        target,
                        radius + .001f,
                        placement,
                        placementMask,
                        QueryTriggerInteraction.Ignore
                    );
                    if (overlaps == placement.Length)
                        continue;
                    for (int k = 0; k < overlaps; k++)
                        if (placement[k].attachedRigidbody != ball)
                        {
                            free = false;
                            break;
                        }
                    if (!free)
                        continue;
                    var pathStart = new Vector3(
                        ball.position.x,
                        Mathf.Max(ball.position.y, target.y) + .005f,
                        ball.position.z
                    );
                    var delta = new Vector3(target.x, pathStart.y, target.z) - pathStart;
                    int hits = Physics.SphereCastNonAlloc(
                        pathStart,
                        radius,
                        delta.normalized,
                        path,
                        delta.magnitude,
                        fieldMask | robotMask,
                        QueryTriggerInteraction.Ignore
                    );
                    if (hits == path.Length)
                        continue;
                    for (int k = 0; k < hits; k++)
                    {
                        var hit = path[k];
                        if (hit.collider.transform.IsChildOf(transform))
                            continue;
                        if (
                            (fieldMask & (1 << hit.collider.gameObject.layer)) != 0
                            && hit.normal.y >= .6f
                        )
                            continue;
                        free = false;
                        break;
                    }
                    if (!free)
                        continue;
                    result = target;
                    return true;
                }
            }
            result = default;
            return false;
        }

        private void OnDisable()
        {
            StopAll();
            Array.Clear(ground, 0, ground.Length);
            GroundedWheelCount = 0;
        }
    }
}
