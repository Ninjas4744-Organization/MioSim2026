using System.Collections.Generic;
using System.Threading;
using Games.Rebuilt.GamePieceSystem;
using Unity.Collections;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Bounded foam compression between stored fuel bodies. Field and
    /// mechanism contacts, sphere geometry and mass retain the game's values.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class Turbo4744FuelCompliance : MonoBehaviour
    {
        public float stiffness = 750;
        public float damping = 8;
        public float maximumCompression = .020f;
        private int modifiedContacts;
        private int compressionMicrometres;
        public int ModifiedContacts => Volatile.Read(ref modifiedContacts);
        public float MaximumObservedCompression =>
            Volatile.Read(ref compressionMicrometres) * .000001f;
        private readonly Collider[] overlaps = new Collider[256];
        private readonly Dictionary<Collider, bool> originalFlags =
            new Dictionary<Collider, bool>();
        private readonly List<Collider> removed = new List<Collider>();
        private readonly object snapshotGate = new object();
        private readonly Stack<Snapshot> available = new Stack<Snapshot>();
        private int fuelMask;

        private sealed class Snapshot
        {
            public readonly HashSet<int> colliders = new HashSet<int>();
            public readonly Dictionary<int, Vector3> velocity = new Dictionary<int, Vector3>();
            public float step,
                stiffness,
                damping,
                limit;
            public int readers;
        }

        private Snapshot snapshot = new Snapshot();

        private void Awake() =>
            fuelMask = LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");

        private Snapshot RentSnapshot()
        {
            Snapshot next;
            lock (snapshotGate)
                next = available.Count > 0 ? available.Pop() : new Snapshot();
            next.colliders.Clear();
            next.velocity.Clear();
            return next;
        }

        private void Publish(Snapshot next)
        {
            lock (snapshotGate)
            {
                var previous = snapshot;
                snapshot = next;
                if (previous.readers == 0)
                    available.Push(previous);
            }
        }

        private Snapshot AcquireSnapshot()
        {
            lock (snapshotGate)
            {
                snapshot.readers++;
                return snapshot;
            }
        }

        private void ReleaseSnapshot(Snapshot data)
        {
            lock (snapshotGate)
            {
                data.readers--;
                if (data.readers == 0 && data != snapshot)
                    available.Push(data);
            }
        }

        private void OnEnable()
        {
            Physics.ContactModifyEvent += Resolve;
            Physics.ContactModifyEventCCD += Resolve;
        }

        private void FixedUpdate()
        {
            // Reuse storage only after every physics callback has released it.
            // Published snapshots remain immutable, including across disable.
            var next = RentSnapshot();
            next.step = Time.fixedDeltaTime;
            next.stiffness = stiffness;
            next.damping = damping;
            next.limit = maximumCompression;
            int count = Physics.OverlapBoxNonAlloc(
                transform.TransformPoint(new Vector3(0, .45f, .26f)),
                new Vector3(.40f, .33f, .44f),
                overlaps,
                transform.rotation,
                fuelMask,
                QueryTriggerInteraction.Ignore
            );
            for (int i = 0; i < count; i++)
            {
                var collider = overlaps[i];
                var body = collider.attachedRigidbody;
                if (!body || !body.GetComponent<RebuiltGamePieceController>())
                    continue;
                // The overlap query is only a broadphase. Restrict the material
                // adapter to centres inside the hopper, leaving ground arrivals
                // and released shooter fuel on the stock contact law.
                var local = transform.InverseTransformPoint(body.position);
                if (
                    Mathf.Abs(local.x) > .36f
                    || local.y < .13f
                    || local.y > .85f
                    || local.z < -.17f
                    || local.z > .64f
                )
                    continue;
                next.colliders.Add(collider.GetInstanceID());
                next.velocity[body.GetInstanceID()] = body.velocity;
                if (!originalFlags.ContainsKey(collider))
                {
                    originalFlags.Add(collider, collider.hasModifiableContacts);
                    collider.hasModifiableContacts = true;
                }
            }
            Publish(next);
            removed.Clear();
            foreach (var pair in originalFlags)
                if (!pair.Key || !next.colliders.Contains(pair.Key.GetInstanceID()))
                {
                    if (pair.Key)
                        pair.Key.hasModifiableContacts = pair.Value;
                    removed.Add(pair.Key);
                }
            foreach (var collider in removed)
                originalFlags.Remove(collider);
        }

        private void Resolve(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            var data = AcquireSnapshot();
            try
            {
                Resolve(data, pairs);
            }
            finally
            {
                ReleaseSnapshot(data);
            }
        }

        private void Resolve(Snapshot data, NativeArray<ModifiableContactPair> pairs)
        {
            int changed = 0;
            foreach (var pair in pairs)
            {
                if (
                    !data.colliders.Contains(pair.colliderInstanceID)
                    || !data.colliders.Contains(pair.otherColliderInstanceID)
                    || !data.velocity.TryGetValue(pair.bodyInstanceID, out var a)
                    || !data.velocity.TryGetValue(pair.otherBodyInstanceID, out var b)
                )
                    continue;
                for (int i = 0; i < pair.contactCount; i++)
                {
                    float compression = Mathf.Max(0, -pair.GetSeparation(i));
                    int micrometres = Mathf.RoundToInt(compression * 1000000);
                    int prior = Volatile.Read(ref compressionMicrometres);
                    while (micrometres > prior)
                    {
                        int observed = Interlocked.CompareExchange(
                            ref compressionMicrometres,
                            micrometres,
                            prior
                        );
                        if (observed == prior)
                            break;
                        prior = observed;
                    }
                    // Restore hard contact at the hardening depth. Solver-step
                    // transients can exceed this material transition depth.
                    if (compression >= data.limit)
                        continue;
                    float closing = Mathf.Max(0, -Vector3.Dot(a - b, pair.GetNormal(i)));
                    float force = data.stiffness * compression + data.damping * closing;
                    pair.SetMaxImpulse(i, force * data.step);
                    changed++;
                }
            }
            Interlocked.Add(ref modifiedContacts, changed);
        }

        private void OnDisable()
        {
            Physics.ContactModifyEvent -= Resolve;
            Physics.ContactModifyEventCCD -= Resolve;
            Publish(RentSnapshot());
            foreach (var pair in originalFlags)
                if (pair.Key)
                    pair.Key.hasModifiableContacts = pair.Value;
            originalFlags.Clear();
        }
    }
}
