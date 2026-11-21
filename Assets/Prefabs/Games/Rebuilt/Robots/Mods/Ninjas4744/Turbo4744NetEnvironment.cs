using System;
using System.Reflection;
using MoSimLib;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Adds static field-box contacts to the stock net's sphere-only collision solve.</summary>
    [DefaultExecutionOrder(50)]
    public sealed class Turbo4744NetEnvironment : MonoBehaviour
    {
        public NetSolver solver;
        public int ContactNodes { get; private set; }
        public int TotalContacts { get; private set; }
        public float MaximumPenetration { get; private set; }
        public int Steps { get; private set; }
        public int MaximumOverlaps { get; private set; }
        public float friction = .05f;
        private readonly Collider[] overlaps = new Collider[64];
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo Positions = typeof(NetSolver).GetField("_pos", Private);
        private static readonly FieldInfo Previous = typeof(NetSolver).GetField(
            "_prevPos",
            Private
        );
        private static readonly FieldInfo Velocities = typeof(NetSolver).GetField("_vel", Private);
        private static readonly FieldInfo InverseMass = typeof(NetSolver).GetField(
            "_invMass",
            Private
        );
        private static readonly FieldInfo LastJob = typeof(NetSolver).GetField(
            "_lastJobHandle",
            Private
        );

        private void Awake() => solver = GetComponent<NetSolver>();

        private void FixedUpdate()
        {
            ContactNodes = 0;
            Steps++;
            if (!solver || !solver.enabled)
                return;
            // NetSolver 26.6 owns these buffers in a job. Join before resolving
            // the missing contact type, and re-read buffers after any Rebuild.
            if (
                Positions == null
                || Previous == null
                || Velocities == null
                || InverseMass == null
                || LastJob == null
            )
                throw new InvalidOperationException("Unsupported MoSim NetSolver buffer layout");
            object handle = LastJob.GetValue(solver);
            handle.GetType().GetMethod("Complete", Type.EmptyTypes).Invoke(handle, null);
            LastJob.SetValue(solver, handle);
            var positions = (NativeArray<float3>)Positions.GetValue(solver);
            if (!positions.IsCreated || positions.Length == 0)
                return;
            var previous = (NativeArray<float3>)Previous.GetValue(solver);
            var velocities = (NativeArray<float3>)Velocities.GetValue(solver);
            var inverseMass = (NativeArray<float>)InverseMass.GetValue(solver);
            var bounds = new Bounds((Vector3)positions[0], Vector3.zero);
            for (int i = 1; i < positions.Length; i++)
                bounds.Encapsulate((Vector3)positions[i]);
            float radius = solver.collisionRadius;
            bounds.Expand(radius * 2);
            int count = Physics.OverlapBoxNonAlloc(
                bounds.center,
                bounds.extents,
                overlaps,
                Quaternion.identity,
                LayerMask.GetMask("Default", "Field"),
                QueryTriggerInteraction.Ignore
            );
            MaximumOverlaps = Mathf.Max(MaximumOverlaps, count);
            var chassis = solver.GetComponentInParent<Rigidbody>();
            for (int i = 0; i < positions.Length; i++)
            {
                if (inverseMass[i] <= 0)
                    continue;
                var position = (Vector3)positions[i];
                bool touched = false;
                for (int j = 0; j < count; j++)
                {
                    var field = overlaps[j];
                    if (
                        field.attachedRigidbody
                        || field.bounds.SqrDistance(position) > radius * radius
                    )
                        continue;
                    if (
                        !(field is BoxCollider box)
                        || !SphereBox(position, radius, box, out var normal, out float depth)
                    )
                        continue;
                    var correction = normal * depth;
                    position += correction;
                    positions[i] = position;
                    previous[i] += (float3)correction;
                    var frameVelocity = chassis ? chassis.GetPointVelocity(position) : Vector3.zero;
                    var velocity = (Vector3)velocities[i] + frameVelocity;
                    float inward = Vector3.Dot(velocity, normal);
                    if (inward < 0)
                    {
                        velocity -= normal * inward;
                        float speed = velocity.magnitude;
                        if (speed > .0001f)
                            velocity -= velocity / speed * Mathf.Min(speed, -inward * friction);
                        var impulse =
                            (velocity - (Vector3)velocities[i] - frameVelocity) / inverseMass[i];
                        velocities[i] = velocity - frameVelocity;
                        if (chassis && !chassis.isKinematic)
                            chassis.AddForceAtPosition(impulse, position, ForceMode.Impulse);
                    }
                    MaximumPenetration = Mathf.Max(MaximumPenetration, depth);
                    touched = true;
                }
                if (touched)
                {
                    ContactNodes++;
                    TotalContacts++;
                }
            }
        }

        private static bool SphereBox(
            Vector3 point,
            float radius,
            BoxCollider box,
            out Vector3 normal,
            out float depth
        )
        {
            // Work in the box's rotated metric frame, including authored scale.
            var rotation = box.transform.rotation;
            var offset =
                Quaternion.Inverse(rotation) * (point - box.transform.TransformPoint(box.center));
            var extent = Vector3.Scale(box.size * .5f, box.transform.lossyScale);
            extent = new Vector3(Mathf.Abs(extent.x), Mathf.Abs(extent.y), Mathf.Abs(extent.z));
            var closest = new Vector3(
                Mathf.Clamp(offset.x, -extent.x, extent.x),
                Mathf.Clamp(offset.y, -extent.y, extent.y),
                Mathf.Clamp(offset.z, -extent.z, extent.z)
            );
            var delta = offset - closest;
            float distance = delta.magnitude;
            normal = Vector3.zero;
            depth = 0;
            if (distance >= radius)
                return false;
            if (distance > .000001f)
            {
                normal = rotation * (delta / distance);
                depth = radius - distance;
                return true;
            }
            var clearance =
                extent - new Vector3(Mathf.Abs(offset.x), Mathf.Abs(offset.y), Mathf.Abs(offset.z));
            int axis = clearance.x < clearance.y ? 0 : 1;
            if (clearance.z < clearance[axis])
                axis = 2;
            var localNormal = Vector3.zero;
            localNormal[axis] = offset[axis] >= 0 ? 1 : -1;
            normal = rotation * localNormal;
            depth = clearance[axis] + radius;
            return true;
        }
    }
}
