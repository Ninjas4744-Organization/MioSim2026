using System;
using System.Collections.Generic;
using Games.Rebuilt.GamePieceSystem;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Grounded-only gameplay clearance for loose fuel trapped under
    /// the chassis. Relocates fuel to free nearby floor; never moves the robot.</summary>
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
        private readonly Vector3[] reserved = new Vector3[2];
        private readonly float[] reservedRadii = new float[2];
        private Rigidbody chassis;
        private int fieldMask,
            fuelMask,
            placementMask;
        private float nextCheck;
        private int searchCursor,
            robotMask;
        public int GroundedWheelCount { get; private set; }
        public int ClearedBalls { get; private set; }
        public int BlockedSearches { get; private set; }
        public int LastClearGroundedWheels { get; private set; }
        public Vector3 LastClearFrom { get; private set; }
        public Vector3 LastClearTo { get; private set; }
        public event Action<Rigidbody, Vector3, Vector3, int> Relocated;

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
            if (
                !chassis
                || GroundedWheelCount == 0
                || Time.fixedTime < nextCheck
                || !BaseGameManager.Instance
                || BaseGameManager.Instance.RobotState != RobotState.Enabled
            )
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
            int moved = 0,
                attempted = 0;
            // Bound expensive placement searches even when a wall or pile blocks
            // every exit. Rotate the start so one blocked ball cannot starve others.
            int start = count > 0 ? searchCursor % count : 0;
            for (int i = 0; i < count && moved < reserved.Length && attempted < 4; i++)
            {
                searchCursor = start + i + 1;
                var sphere = nearby[(start + i) % count] as SphereCollider;
                var ball = sphere ? sphere.attachedRigidbody : null;
                if (
                    !ball
                    || ball.isKinematic
                    || !visited.Add(ball)
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
                if (!FindExit(ball, local, radius, moved, out var destination))
                {
                    BlockedSearches++;
                    continue;
                }
                Vector3 from = ball.position;
                var outward = Vector3
                    .ProjectOnPlane(destination - chassis.position, Vector3.up)
                    .normalized;
                float speed = Mathf.Clamp(Vector3.Dot(ball.velocity, outward), .6f, 1.5f);
                ball.position = destination;
                ball.velocity = outward * speed;
                ball.WakeUp();
                reserved[moved] = destination;
                reservedRadii[moved++] = radius;
                ClearedBalls++;
                LastClearGroundedWheels = GroundedWheelCount;
                LastClearFrom = from;
                LastClearTo = destination;
                Relocated?.Invoke(ball, from, destination, GroundedWheelCount);
            }
        }

        private bool FindExit(
            Rigidbody ball,
            Vector3 local,
            float radius,
            int moved,
            out Vector3 result
        )
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
                    for (int k = 0; k < moved; k++)
                        if (
                            (reserved[k] - target).sqrMagnitude
                            < (reservedRadii[k] + radius + .004f)
                                * (reservedRadii[k] + radius + .004f)
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
            Array.Clear(ground, 0, ground.Length);
            GroundedWheelCount = 0;
        }
    }
}
