using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Keep lateral field contacts from suspending the robot on its
    /// bumper foam or rigid hopper panels. Vertical support stays unchanged.</summary>
    public sealed class Turbo4744BumperContacts : MonoBehaviour
    {
        private const float CompressionLimit = .008f;
        private const float Stiffness = 12000f;
        private const float Damping = 120f;
        private const float PanelFriction = .03f;
        private readonly Dictionary<Collider, bool> originalFlags =
            new Dictionary<Collider, bool>();

        private sealed class Snapshot
        {
            public readonly HashSet<int> bumpers = new HashSet<int>();
            public readonly HashSet<int> surfaces = new HashSet<int>();
            public readonly HashSet<int> field = new HashSet<int>();
            public float step;
        }

        private Snapshot snapshot = new Snapshot();
        private int modifiedContacts;
        private bool driveReady;
        public int ModifiedContacts => Volatile.Read(ref modifiedContacts);
        public string DiagnosticState =>
            "bumpers="
            + snapshot.bumpers.Count
            + ",surfaces="
            + snapshot.surfaces.Count
            + ",field="
            + snapshot.field.Count
            + ",modified="
            + ModifiedContacts;

        private void OnEnable()
        {
            if (driveReady)
                Register();
        }

        private IEnumerator Start()
        {
            // Child wheel components can create their solid contact proxies
            // after the root drive's Start. Include those as well.
            yield return null;
            yield return new WaitForFixedUpdate();
            RefreshContacts();
        }

        private void Register()
        {
            var next = new Snapshot { step = Time.fixedDeltaTime };
            int robotLayer = LayerMask.NameToLayer("Robot");
            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                if (
                    collider.gameObject.layer != robotLayer
                    || collider.isTrigger
                    || collider is WheelCollider
                    || !collider.attachedRigidbody
                    || (collider.excludeLayers.value & LayerMask.GetMask("Field")) != 0
                )
                    continue;
                int id = collider.GetInstanceID();
                next.surfaces.Add(id);
                if (collider.name.StartsWith("Bumper ") && collider.name.EndsWith(" contact"))
                    next.bumpers.Add(id);
                originalFlags.Add(collider, collider.hasModifiableContacts);
                collider.hasModifiableContacts = true;
            }
            int fieldMask = LayerMask.GetMask("Field");
            foreach (
                var collider in FindObjectsByType<Collider>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
            )
                if (
                    !collider.isTrigger
                    && !collider.attachedRigidbody
                    && (fieldMask & (1 << collider.gameObject.layer)) != 0
                )
                    next.field.Add(collider.GetInstanceID());
            Volatile.Write(ref snapshot, next);
            Physics.ContactModifyEvent += Resolve;
            Physics.ContactModifyEventCCD += ResolveCCD;
            Debug.Log("[Turbo trench] contact handler enabled: " + DiagnosticState);
        }

        public void RefreshContacts()
        {
            if (!isActiveAndEnabled)
                return;
            // The drive framework creates additional wheel contact surfaces in
            // Start, after a serialized component's first OnEnable snapshot.
            OnDisable();
            driveReady = true;
            Register();
        }

        private void Resolve(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs) =>
            ResolvePairs(pairs, 1f);

        private void ResolveCCD(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs) =>
            ResolvePairs(pairs, -1f);

        private void ResolvePairs(NativeArray<ModifiableContactPair> pairs, float normalDirection)
        {
            var data = Volatile.Read(ref snapshot);
            int changed = 0;
            foreach (var pair in pairs)
            {
                if (
                    !(
                        data.surfaces.Contains(pair.colliderInstanceID)
                        && data.field.Contains(pair.otherColliderInstanceID)
                    )
                    && !(
                        data.surfaces.Contains(pair.otherColliderInstanceID)
                        && data.field.Contains(pair.colliderInstanceID)
                    )
                )
                    continue;
                for (int i = 0; i < pair.contactCount; i++)
                {
                    var normal = pair.GetNormal(i);
                    // Keep hard vertical support on carpet, bump tops and other
                    // surfaces. Only the sides of static field structures soften.
                    if (Mathf.Abs(normal.y) > .25f)
                        continue;
                    // Rigid hopper/frame contacts need to slide along trench
                    // sides rather than clamp the wheels above the carpet.
                    // Keep their normal response rigid, and leave fuel/robot
                    // contacts and upward-facing field support untouched.
                    pair.SetStaticFriction(i, PanelFriction);
                    pair.SetDynamicFriction(i, PanelFriction);
                    changed++;
                    if (
                        !data.bumpers.Contains(pair.colliderInstanceID)
                        && !data.bumpers.Contains(pair.otherColliderInstanceID)
                    )
                        continue;
                    float depth = Mathf.Max(0, -pair.GetSeparation(i));
                    if (depth >= CompressionLimit)
                        continue;
                    float closing = Mathf.Max(
                        0,
                        -Vector3.Dot(
                            pair.bodyVelocity - pair.otherBodyVelocity,
                            normal * normalDirection
                        )
                    );
                    // Share the force budget across the manifold: more contact
                    // points must not make a shallow pinch support the whole robot.
                    float force = Stiffness * depth + Damping * closing;
                    pair.SetMaxImpulse(i, force * data.step / pair.contactCount);
                }
            }
            Interlocked.Add(ref modifiedContacts, changed);
        }

        private void OnDisable()
        {
            Physics.ContactModifyEvent -= Resolve;
            Physics.ContactModifyEventCCD -= ResolveCCD;
            Volatile.Write(ref snapshot, new Snapshot());
            foreach (var pair in originalFlags)
                if (pair.Key)
                    pair.Key.hasModifiableContacts = pair.Value;
            originalFlags.Clear();
        }
    }
}
