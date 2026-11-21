using System.Collections.Generic;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Parameterized shooter/indexer traction for official rigid fuel.
    /// Applies bounded forces only while fuel is within the contact. It never
    /// sets a fuel transform/velocity or applies corrections after release.</summary>
    [DefaultExecutionOrder(1100)]
    public sealed class Turbo4744ShooterContact : MonoBehaviour
    {
        public Rigidbody robotBody;
        public float wheelRadius = .05f,
            rampRpmPerSecond = 15000f;
        public float contactNear = -.04f,
            contactFar = .24f,
            halfWidth = .09f,
            normalClearance = .025f;
        public float maxTractionForce = 240f,
            tractionResponse = 100f;
        public float normalStiffness = 2500f,
            normalDamping = 45f,
            maxNormalForce = 120f;
        public bool allowReverse,
            requiresCommand,
            brakeWhenStopped;
        public bool compensateNormalGravity;
        public bool capturedOnly;
        public float lateralResponse = 40f,
            maxLateralForce = 50f;

        [Tooltip(
            "Optional ball-center path, in this contact's local frame, from pickup to handoff."
        )]
        public Vector3[] centerline;
        public bool forwardTractionOnly;
        public bool unilateralContact;
        public float contactSkin = .012f;
        public float maxSurfaceSpeed;

        [Tooltip(
            "Shared motor/contact force budget across all touching fuel, in newtons. Zero preserves per-contact limits."
        )]
        public float maxTotalForce;

        [Tooltip(
            "Optional shared budget for powered tangential traction, before the overall contact-force cap."
        )]
        public float maxTotalTractionForce;
        public Turbo4744FuelKeeper entryKeeper;
        public Turbo4744Indexer lowerEntry;
        public float CurrentRpm { get; private set; }
        public float TargetRpm { get; private set; }
        public int Contacts { get; private set; }
        private readonly Collider[] overlaps = new Collider[256];
        private readonly HashSet<Rigidbody> previous = new HashSet<Rigidbody>();
        private readonly HashSet<Rigidbody> current = new HashSet<Rigidbody>();
        private readonly Rigidbody[] bodies = new Rigidbody[256];
        private readonly Vector3[] forces = new Vector3[256];
        private readonly Vector3[] tangentForces = new Vector3[256];
        private int fuelMask;

        private void Awake()
        {
            fuelMask = LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
        }

        public void SetRpm(float rpm) => TargetRpm = allowReverse ? rpm : Mathf.Max(0, rpm);

        public void Stop()
        {
            TargetRpm = CurrentRpm = 0;
            Contacts = 0;
            previous.Clear();
        }

        private void FixedUpdate()
        {
            if (
                !BaseGameManager.Instance
                || BaseGameManager.Instance.RobotState == RobotState.Disabled
            )
            {
                Stop();
                return;
            }
            if (requiresCommand && Mathf.Approximately(TargetRpm, 0) && !brakeWhenStopped)
            {
                Stop();
                return;
            }
            CurrentRpm = Mathf.MoveTowards(
                CurrentRpm,
                TargetRpm,
                rampRpmPerSecond * Time.fixedDeltaTime
            );
            current.Clear();
            Contacts = 0;
            float center = (contactNear + contactFar) * .5f;
            bool curved = centerline != null && centerline.Length >= 2;
            var query = new Bounds(
                new Vector3(0, 0, -center),
                new Vector3(halfWidth * 2, normalClearance * 2, contactFar - contactNear)
            );
            if (curved)
            {
                query = new Bounds(centerline[0], Vector3.zero);
                foreach (var point in centerline)
                    query.Encapsulate(point);
                query.Expand(new Vector3(halfWidth * 2, normalClearance * 2, normalClearance * 2));
            }
            int count = Physics.OverlapBoxNonAlloc(
                transform.TransformPoint(query.center),
                query.extents + Vector3.one * .075f,
                overlaps,
                transform.rotation,
                fuelMask,
                QueryTriggerInteraction.Ignore
            );
            Vector3 right = transform.right;
            float totalForce = 0;
            float totalTraction = 0;
            for (int i = 0; i < count; i++)
            {
                var rb = overlaps[i].attachedRigidbody;
                if (!rb || rb.isKinematic || !rb.detectCollisions || current.Contains(rb))
                    continue;
                if (lowerEntry && !lowerEntry.HasEnteredShooter(rb))
                    continue;
                // Once the whole ball clears the inlet and latches floor
                // support, collection traction has finished its handoff.
                // A packed stored ball may remain near the path indefinitely;
                // re-engaging it would turn the pickup into a storage conveyor.
                if (
                    entryKeeper
                    && (
                        capturedOnly
                            ? !entryKeeper.IsCaptured(rb)
                            : TargetRpm >= 0
                                && entryKeeper.IsCaptured(rb)
                                && !entryKeeper.robot.Feeding
                    )
                )
                    continue;
                Vector3 local = transform.InverseTransformPoint(rb.worldCenterOfMass);
                float along = -local.z;
                if (Mathf.Abs(local.x) > halfWidth)
                    continue;
                Vector3 axis = -transform.forward,
                    normal = transform.up;
                float normalOffset = local.y;
                if (curved)
                {
                    var flat = new Vector3(0, local.y, local.z);
                    float distance = float.PositiveInfinity;
                    Vector3 nearest = Vector3.zero,
                        tangent = Vector3.zero;
                    for (int segment = 1; segment < centerline.Length; segment++)
                    {
                        var start = centerline[segment - 1];
                        var delta = centerline[segment] - start;
                        if (delta.sqrMagnitude < .000001f)
                            continue;
                        float fraction = Vector3.Dot(flat - start, delta) / delta.sqrMagnitude;
                        // End caps must not attract balls ahead of the pickup
                        // or reach back into storage after the handoff.
                        if (
                            segment == 1 && fraction < 0
                            || segment == centerline.Length - 1 && fraction > 1
                        )
                            continue;
                        var point = start + delta * Mathf.Clamp01(fraction);
                        float squared = (flat - point).sqrMagnitude;
                        if (squared >= distance)
                            continue;
                        distance = squared;
                        nearest = point;
                        tangent = delta.normalized;
                    }
                    if (distance > normalClearance * normalClearance)
                        continue;
                    axis = transform.TransformDirection(tangent);
                    normal = Vector3.Cross(right, axis).normalized;
                    normalOffset = Vector3.Dot(
                        transform.TransformDirection(flat - nearest),
                        normal
                    );
                }
                else if (
                    Mathf.Abs(local.y) > normalClearance
                    || along < contactNear
                    || along > contactFar
                )
                    continue;
                // A single pickup belt can support/compress a ball, but cannot
                // pull an airborne ball down through a heap to its centerline.
                if (unilateralContact && normalOffset > contactSkin)
                    continue;
                current.Add(rb);
                Contacts++;
                Vector3 relative = rb.velocity - robotBody.GetPointVelocity(rb.worldCenterOfMass);
                float surfaceSpeed = CurrentRpm * (2 * Mathf.PI / 60) * wheelRadius;
                if (maxSurfaceSpeed > 0)
                    surfaceSpeed = Mathf.Clamp(surfaceSpeed, -maxSurfaceSpeed, maxSurfaceSpeed);
                float response = Mathf.Min(tractionResponse, .8f / Time.fixedDeltaTime);
                float tangentForce = Mathf.Clamp(
                    rb.mass
                        * (
                            (surfaceSpeed - Vector3.Dot(relative, axis)) * response
                            - Vector3.Dot(Physics.gravity, axis)
                        ),
                    -maxTractionForce,
                    maxTractionForce
                );
                // A fast approach can carry a ball inward faster than the
                // pickup belt. Do not brake it back into the front bumper.
                if (forwardTractionOnly && surfaceSpeed > 0)
                    tangentForce = Mathf.Max(0, tangentForce);
                float normalForce = Mathf.Clamp(
                    -normalOffset * normalStiffness - Vector3.Dot(relative, normal) * normalDamping,
                    -maxNormalForce,
                    maxNormalForce
                );
                if (compensateNormalGravity)
                    normalForce = Mathf.Clamp(
                        normalForce - rb.mass * Vector3.Dot(Physics.gravity, normal),
                        -maxNormalForce,
                        maxNormalForce
                    );
                if (unilateralContact)
                    normalForce = Mathf.Max(0, normalForce);
                float lateralForce = Mathf.Clamp(
                    -rb.mass * Vector3.Dot(relative, right) * lateralResponse,
                    -maxLateralForce,
                    maxLateralForce
                );
                float widthContact = Mathf.Clamp01((halfWidth - Mathf.Abs(local.x)) / .075f);
                Vector3 force =
                    (axis * tangentForce + normal * normalForce + right * lateralForce)
                    * widthContact;
                bodies[Contacts - 1] = rb;
                forces[Contacts - 1] = force;
                tangentForces[Contacts - 1] = axis * tangentForce * widthContact;
                totalTraction += Mathf.Abs(tangentForce * widthContact);
                totalForce += force.magnitude;
            }
            // The motor's traction limit must not remove the passive support
            // needed to lift a dense arrival packet over the intake lip.
            if (maxTotalTractionForce > 0 && totalTraction > maxTotalTractionForce)
            {
                float tractionScale = maxTotalTractionForce / totalTraction;
                totalForce = 0;
                for (int i = 0; i < Contacts; i++)
                {
                    forces[i] += tangentForces[i] * (tractionScale - 1);
                    totalForce += forces[i].magnitude;
                }
            }
            // One intake motor cannot multiply its available force by the
            // number of balls in a dense pile. Preserve reaction forces, but
            // share the mechanism's finite budget between its contacts.
            float scale =
                maxTotalForce > 0 && totalForce > maxTotalForce ? maxTotalForce / totalForce : 1;
            for (int i = 0; i < Contacts; i++)
            {
                var rb = bodies[i];
                var force = forces[i] * scale;
                rb.AddForceAtPosition(force, rb.worldCenterOfMass, ForceMode.Force);
                robotBody.AddForceAtPosition(-force, rb.worldCenterOfMass, ForceMode.Force);
            }
            // All balls in this physics step see the same wheel speed. Apply the
            // combined load after calculating forces so query order cannot favor one lane.
            int arrivals = 0;
            foreach (var rb in current)
                if (!previous.Contains(rb))
                    arrivals++;
            CurrentRpm *= Mathf.Pow(.98f, arrivals);
            previous.Clear();
            foreach (var rb in current)
                previous.Add(rb);
        }
    }
}
