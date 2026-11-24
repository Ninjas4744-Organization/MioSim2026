using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Presentation only. Uses the existing collider-free fuel mesh;
    /// never changes a collider, rigidbody pose, force, material or hierarchy.</summary>
    [DefaultExecutionOrder(300)]
    public sealed class Turbo4744ClearanceVisual : MonoBehaviour
    {
        private Rigidbody body;
        private Transform mesh;
        private Vector3 restPosition,
            offset,
            previous;
        private Quaternion displayedRotation;
        private float began,
            radius;
        public bool Playing { get; private set; }
        public float Duration { get; private set; }
        private Vector3 Goal =>
            body.position + body.rotation * Vector3.Scale(restPosition, transform.lossyScale);
        public Vector3 VisiblePosition
        {
            get
            {
                Initialize();
                if (!mesh)
                    return body ? body.position : transform.position;
                if (!Playing)
                    return mesh.position;
                float t = Mathf.Clamp01((Time.time - began) / Duration);
                return Goal + offset * (1 - t * t * (3 - 2 * t));
            }
        }
        public Quaternion VisibleRotation
        {
            get
            {
                Initialize();
                return mesh ? mesh.rotation : transform.rotation;
            }
        }

        private void Initialize()
        {
            if (body)
                return;
            body = GetComponent<Rigidbody>();
            var renderer = GetComponentInChildren<MeshRenderer>();
            // Moving a mesh that owns a collider would change physics. The
            // stock fuel has separate visual and physical children.
            if (
                renderer
                && renderer.transform != transform
                && renderer.GetComponentsInChildren<Collider>(true).Length == 0
            )
            {
                mesh = renderer.transform;
                restPosition = mesh.localPosition;
            }
        }

        public bool RollFrom(Vector3 position, Quaternion rotation, float fuelRadius)
        {
            Initialize();
            if (!mesh || !body)
                return false;
            radius = Mathf.Max(.01f, fuelRadius);
            offset = position - Goal;
            Duration = Mathf.Clamp(offset.magnitude / 1.1f, .25f, .65f);
            began = Time.time;
            previous = position;
            displayedRotation = rotation;
            Playing = true;
            mesh.SetPositionAndRotation(position, rotation);
            return true;
        }

        private void LateUpdate()
        {
            if (!Playing || !mesh || !body)
                return;
            var position = VisiblePosition;
            var movement = Vector3.ProjectOnPlane(position - previous, Vector3.up);
            if (movement.sqrMagnitude > .00000001f)
                displayedRotation =
                    Quaternion.AngleAxis(
                        movement.magnitude / radius * Mathf.Rad2Deg,
                        Vector3.Cross(Vector3.up, movement.normalized)
                    ) * displayedRotation;
            mesh.SetPositionAndRotation(position, displayedRotation);
            previous = position;
            if (Time.time >= began + Duration)
            {
                mesh.localPosition = restPosition;
                // Retain the accumulated texture orientation so the visual
                // handoff back to ordinary rigidbody rolling has no snap.
                Playing = false;
            }
        }

        private void OnDisable()
        {
            if (mesh)
                mesh.localPosition = restPosition;
            Playing = false;
        }
    }
}
