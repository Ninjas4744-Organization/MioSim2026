using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Only the lower kicker swings. The box clears the bumper
    /// before deployment and waits for the kicker before retracting.</summary>
    public sealed class Turbo4744KickerRig : MonoBehaviour
    {
        public Turbo4744 robot;
        public Transform hopper,
            kicker;
        public Vector3 hopperClosedPosition;
        public float travel = .22946f,
            stowedAngle = 60f,
            swingSeconds = .35f,
            extensionTolerance = .0015f,
            pumpClearanceExtension = .125f;

        public float Deployment { get; private set; }

        public bool Retracted => Deployment <= .001f;

        private void OnEnable() => SetDeployment(0);

        private void LateUpdate() => UpdatePose();

        public bool PrepareRailMotion(bool retracting, float deltaTime)
        {
            if (!robot || !hopper || !kicker || travel <= 0)
                return true;
            float extension =
                transform.InverseTransformPoint(hopper.position).z - hopperClosedPosition.z;
            bool canDeploy =
                !retracting
                && robot.Deployed
                && extension >= pumpClearanceExtension
                && (Deployment > .001f || extension >= travel - extensionTolerance);
            SetDeployment(
                Mathf.MoveTowards(
                    Deployment,
                    canDeploy ? 1 : 0,
                    deltaTime / Mathf.Max(.01f, swingSeconds)
                )
            );
            return !retracting || Retracted;
        }

        public void SetDeployment(float deployment)
        {
            Deployment = Mathf.Clamp01(deployment);
            UpdatePose();
        }

        public void UpdatePose()
        {
            if (kicker)
                kicker.localRotation = Quaternion.Euler(stowedAngle * (1 - Deployment), 0, 0);
        }
    }
}
