using System;
using System.Collections.Generic;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Two CAD roller floors, one bounded motor budget, and a bottom-entry handoff.</summary>
    [DefaultExecutionOrder(1050)]
    public sealed class Turbo4744Indexer : MonoBehaviour
    {
        [Serializable]
        public sealed class Roller
        {
            public Transform visual;
            public Vector3 center;
            public float radius = .0125f,
                halfWidth = .28f;
            public bool upper;
        }

        public Rigidbody robotBody;
        public Turbo4744FuelKeeper keeper;
        public BoxCollider retention,
            releaseRetention;

        // Dynamically loaded mod assemblies do not register nested managed
        // serialization types in the native player. Store Unity-owned types
        // in the bundle and reconstruct the public definitions at runtime.
        [SerializeField]
        private Transform[] rollerVisuals = Array.Empty<Transform>();

        [SerializeField]
        private Vector3[] rollerCenters = Array.Empty<Vector3>();

        [SerializeField]
        private Vector3[] rollerGeometry = Array.Empty<Vector3>();
        private Roller[] runtimeRollers;

        public Roller[] rollers
        {
            get
            {
                if (runtimeRollers != null)
                    return runtimeRollers;
                int count = Mathf.Min(
                    rollerVisuals.Length,
                    Mathf.Min(rollerCenters.Length, rollerGeometry.Length)
                );
                runtimeRollers = new Roller[count];
                for (int i = 0; i < count; i++)
                    runtimeRollers[i] = new Roller
                    {
                        visual = rollerVisuals[i],
                        center = rollerCenters[i],
                        radius = rollerGeometry[i].x,
                        halfWidth = rollerGeometry[i].y,
                        upper = rollerGeometry[i].z > .5f,
                    };
                return runtimeRollers;
            }
            set
            {
                runtimeRollers = value ?? Array.Empty<Roller>();
                rollerVisuals = new Transform[runtimeRollers.Length];
                rollerCenters = new Vector3[runtimeRollers.Length];
                rollerGeometry = new Vector3[runtimeRollers.Length];
                for (int i = 0; i < runtimeRollers.Length; i++)
                {
                    var roller = runtimeRollers[i];
                    rollerVisuals[i] = roller.visual;
                    rollerCenters[i] = roller.center;
                    rollerGeometry[i] = new Vector3(
                        roller.radius,
                        roller.halfWidth,
                        roller.upper ? 1 : 0
                    );
                }
            }
        }

        [Tooltip("MoSim calibration, not a measured hardware transmission ratio.")]
        public float surfaceSpeedAt78Rps = 1.2f;
        public float acceleration = 8f,
            tractionResponse = 45f;
        public float maxTractionForce = 180f,
            maxTotalForce = 320f,
            contactSkin = .012f;
        public float TargetMotorRps { get; private set; }
        public float CurrentSurfaceSpeed { get; private set; }
        public int Contacts { get; private set; }
        public int LowerContacts { get; private set; }
        public int UpperContacts { get; private set; }
        public float AppliedForce { get; private set; }
        public int LowerEntrances { get; private set; }
        private bool holdRequested = true;
        private int fuelMask;
        private readonly Collider[] overlaps = new Collider[256];
        private readonly HashSet<Rigidbody> seen = new HashSet<Rigidbody>();
        private readonly HashSet<Rigidbody> admitted = new HashSet<Rigidbody>();
        private readonly List<Rigidbody> expired = new List<Rigidbody>();
        private readonly Rigidbody[] bodies = new Rigidbody[256];
        private readonly Vector3[] forces = new Vector3[256];

        private void Awake() =>
            fuelMask = LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");

        public void SetMotorRps(float rps) => TargetMotorRps = rps;

        public void SetRetention(bool hold) => holdRequested = hold;

        public bool HasEnteredShooter(Rigidbody fuel) => admitted.Contains(fuel);

        public void Stop()
        {
            TargetMotorRps = CurrentSurfaceSpeed = AppliedForce = 0;
            Contacts = LowerContacts = UpperContacts = 0;
            holdRequested = true;
        }

        private void FixedUpdate()
        {
            bool enabled =
                BaseGameManager.Instance
                && BaseGameManager.Instance.RobotState != RobotState.Disabled;
            if (!enabled)
                Stop();
            UpdateRetention();
            CurrentSurfaceSpeed = Mathf.MoveTowards(
                CurrentSurfaceSpeed,
                TargetMotorRps / 78f * surfaceSpeedAt78Rps,
                acceleration * Time.fixedDeltaTime
            );
            Contacts = LowerContacts = UpperContacts = 0;
            AppliedForce = 0;
            seen.Clear();
            expired.Clear();
            foreach (var fuel in admitted)
            {
                if (!fuel)
                {
                    expired.Add(fuel);
                    continue;
                }
                var p = transform.InverseTransformPoint(fuel.worldCenterOfMass);
                if (p.y > .72f || p.z < -.42f || p.z > .18f || Mathf.Abs(p.x) > .3f || p.y < .05f)
                    expired.Add(fuel);
            }
            foreach (var fuel in expired)
                admitted.Remove(fuel);
            int count = Physics.OverlapBoxNonAlloc(
                transform.TransformPoint(new Vector3(0, .30f, .10f)),
                new Vector3(.39f, .27f, .35f),
                overlaps,
                transform.rotation,
                fuelMask,
                QueryTriggerInteraction.Ignore
            );
            for (int i = 0; i < count; i++)
            {
                var fuel = overlaps[i].attachedRigidbody;
                if (!fuel || fuel.isKinematic || !fuel.detectCollisions || !seen.Add(fuel))
                    continue;
                Vector3 p = transform.InverseTransformPoint(fuel.worldCenterOfMass);
                float radius = .075f;
                foreach (var sphere in fuel.GetComponentsInChildren<SphereCollider>())
                    if (!sphere.isTrigger && sphere.enabled)
                        radius = Mathf.Max(
                            radius,
                            sphere.radius
                                * Mathf.Max(
                                    Mathf.Abs(sphere.transform.lossyScale.x),
                                    Mathf.Abs(sphere.transform.lossyScale.y),
                                    Mathf.Abs(sphere.transform.lossyScale.z)
                                )
                        );
                // Upper-deck fuel must physically drop before either accelerator or flywheel may grip it.
                // Contact begins when the sphere's lower surface crosses the
                // mouth top. Requiring its centre below that same plane misses
                // a partially entering ball, especially under a mixed load.
                if (
                    enabled
                    && p.y >= .13f
                    && p.y - radius <= .275f
                    && p.z < .105f
                    && p.z > -.14f
                    && Mathf.Abs(p.x - .0131f) <= .10f
                    && admitted.Add(fuel)
                )
                    LowerEntrances++;
                if (!enabled || Mathf.Abs(CurrentSurfaceSpeed) < .001f)
                    continue;
                Vector3 tangent = Vector3.zero;
                float weight = 0;
                bool upper = false,
                    lower = false;
                foreach (var roller in rollers)
                {
                    var delta = p - roller.center;
                    if (Mathf.Abs(delta.x) > roller.halfWidth || delta.y < roller.radius)
                        continue;
                    delta.x = 0;
                    float distance = delta.magnitude;
                    if (distance > radius + roller.radius + contactSkin)
                        continue;
                    float w = Mathf.Clamp01(
                        (radius + roller.radius + contactSkin - distance) / contactSkin
                    );
                    tangent += Vector3.Cross(delta.normalized, Vector3.right) * w;
                    weight += w;
                    upper |= roller.upper;
                    lower |= !roller.upper;
                }
                if (weight <= 0)
                    continue;
                Vector3 axis = transform.TransformDirection(tangent / weight).normalized;
                var relative = fuel.velocity - robotBody.GetPointVelocity(fuel.worldCenterOfMass);
                float response = Mathf.Min(tractionResponse, .8f / Time.fixedDeltaTime);
                float traction = Mathf.Clamp(
                    fuel.mass
                        * (
                            (CurrentSurfaceSpeed - Vector3.Dot(relative, axis)) * response
                            - Vector3.Dot(Physics.gravity, axis)
                        ),
                    -maxTractionForce,
                    maxTractionForce
                );
                bodies[Contacts] = fuel;
                forces[Contacts] = axis * traction;
                AppliedForce += Mathf.Abs(traction);
                Contacts++;
                if (upper)
                    UpperContacts++;
                if (lower)
                    LowerContacts++;
            }
            float scale = AppliedForce > maxTotalForce ? maxTotalForce / AppliedForce : 1;
            for (int i = 0; i < Contacts; i++)
            {
                var force = forces[i] * scale;
                bodies[i].AddForceAtPosition(force, bodies[i].worldCenterOfMass);
                robotBody.AddForceAtPosition(-force, bodies[i].worldCenterOfMass);
            }
            AppliedForce *= scale;
        }

        private void UpdateRetention()
        {
            UpdateRetention(retention);
            UpdateRetention(releaseRetention);
        }

        private void UpdateRetention(BoxCollider gate)
        {
            if (!gate)
                return;
            if (!holdRequested)
            {
                gate.enabled = false;
                return;
            }
            if (gate.enabled)
                return;
            // Never close a physical gate through a ball already crossing its plane.
            int count = Physics.OverlapBoxNonAlloc(
                gate.transform.TransformPoint(gate.center),
                Vector3.Scale(gate.size, gate.transform.lossyScale) * .5f + Vector3.one * .002f,
                overlaps,
                gate.transform.rotation,
                fuelMask,
                QueryTriggerInteraction.Ignore
            );
            if (count == 0)
                gate.enabled = true;
        }

        private void LateUpdate()
        {
            foreach (var roller in rollers)
                if (roller.visual)
                    roller.visual.Rotate(
                        transform.right,
                        -CurrentSurfaceSpeed / roller.radius * Mathf.Rad2Deg * Time.deltaTime,
                        Space.World
                    );
        }
    }
}
