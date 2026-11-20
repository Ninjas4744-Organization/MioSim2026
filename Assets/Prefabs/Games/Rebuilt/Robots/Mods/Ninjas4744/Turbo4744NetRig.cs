using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Border lacing follows the telescoping frame; free interior nodes
    /// fold and collide through MoSim's NetSolver. No textile mesh scaling.</summary>
    [DefaultExecutionOrder(-20)]
    public sealed class Turbo4744NetRig : MonoBehaviour
    {
        public Transform rearLeft,
            rearRight,
            frontLeft,
            frontRight;
        public Transform[] leftPins,
            rightPins;
        public BoxCollider leftRim,
            rightRim;

        private void FixedUpdate() => UpdatePins();

        public void UpdatePins()
        {
            for (int i = 0; i < leftPins.Length; i++)
            {
                float t = (float)i / (leftPins.Length - 1);
                leftPins[i].position = Vector3.Lerp(rearLeft.position, frontLeft.position, t);
                rightPins[i].position = Vector3.Lerp(rearRight.position, frontRight.position, t);
            }
            UpdateRim(leftRim, rearLeft.position, frontLeft.position);
            UpdateRim(rightRim, rearRight.position, frontRight.position);
        }

        private void UpdateRim(BoxCollider rim, Vector3 rear, Vector3 front)
        {
            if (!rim)
                return;
            var direction = front - rear;
            rim.transform.SetPositionAndRotation(
                (rear + front) * .5f,
                Quaternion.LookRotation(direction, transform.up)
            );
            rim.size = new Vector3(.016f, .016f, direction.magnitude + .016f);
        }
    }
}
