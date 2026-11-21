using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>The sliding bin pushes fuel with its rigid walls. Its overlapping
    /// tray bridges the moving front panel to the stationary indexer entrance.</summary>
    [DefaultExecutionOrder(-30)]
    public sealed class Turbo4744HopperRig : MonoBehaviour
    {
        public BoxCollider floor,
            frontPanel;
        public float floorRear = .3176175f;

        private Rigidbody chassis,
            hopper;
        private Vector3 panelLocal;

        private void Awake() => UpdateFloor();

        private void FixedUpdate() => UpdateFloor();

        public void UpdateFloor()
        {
            if (!floor || !frontPanel)
                return;
            if (!chassis || !hopper)
            {
                chassis = GetComponent<Rigidbody>();
                hopper = frontPanel.GetComponentInParent<Rigidbody>(true);
                panelLocal = hopper.transform.InverseTransformPoint(
                    frontPanel.transform.TransformPoint(frontPanel.center)
                );
            }
            var panelWorld = hopper.position + hopper.rotation * panelLocal;
            float end =
                (Quaternion.Inverse(chassis.rotation) * (panelWorld - chassis.position)).z
                - frontPanel.size.z * .5f;
            float length = Mathf.Max(.02f, end - floorRear);
            if (Mathf.Abs(floor.size.z - length) < .00001f)
                return;
            var size = floor.size;
            size.z = length;
            floor.size = size;
            var center = floor.center;
            center.z = floor
                .transform.InverseTransformPoint(
                    chassis.position
                        + chassis.rotation * new Vector3(0, 0, floorRear + length * .5f)
                )
                .z;
            floor.center = center;
        }
    }
}
