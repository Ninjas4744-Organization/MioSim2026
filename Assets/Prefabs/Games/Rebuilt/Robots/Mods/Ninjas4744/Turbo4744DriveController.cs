using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using RobotFramework.Controllers.Drivetrain;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Team joystick shaping, scoped to Turbo. The stock controller
    /// retains motor, traction, collision, camera perspective and aim behavior.</summary>
    public sealed class Turbo4744DriveController : DriveController
    {
        private static float Deadband(float value) =>
            Mathf.Abs(value) <= .04f ? 0 : Mathf.Sign(value) * (Mathf.Abs(value) - .04f) / .96f;

        protected override void FixedUpdate()
        {
            if (
                _robotBase
                && _rb
                && _override == null
                && BaseGameManager.Instance
                && BaseGameManager.Instance.RobotState == RobotState.Enabled
            )
            {
                Vector2 raw = _robotBase.TranslateAction.ReadValue<Vector2>();
                var value = new Vector2(Deadband(raw.x), Deadband(raw.y));
                float length = Mathf.Min(1, value.magnitude);
                value = length > .0001f ? value.normalized * Mathf.Pow(length, .75f) : Vector2.zero;
                Vector3 input = new Vector3(value.y, 0, value.x);
                if (_robotBase.IsFieldCentric)
                {
                    float yaw = _rb.rotation.eulerAngles.y;
                    if (_robotBase.ThirdPersonCam != null)
                        yaw -= _robotBase.ThirdPersonCam.CameraObject.transform.eulerAngles.y;
                    else if (_robotBase.DriverStationCam != null)
                        yaw += _robotBase.Alliance == Alliance.Blue ? 90 : -90;
                    input = Quaternion.AngleAxis(yaw, Vector3.up) * input;
                }
                else if (_robotBase.IsCameraFlipped)
                    input = -input;
                float scale =
                    _driveMp * _actionKeyMp * _robotBase.MoveSpeedMultiplier * GetMaxVelocity();
                float turn = Deadband(_robotBase.RotateAction.ReadValue<float>());
                float omega =
                    -Mathf.Sign(turn)
                    * Mathf.Pow(Mathf.Abs(turn), .75f)
                    * _robotBase.RotationSpeedMultiplier
                    * _turnMp
                    * GetMaxOmega();
                OverrideInput(
                    input.x * scale,
                    input.z * scale,
                    omega,
                    DrivePerspective.RobotRelative
                );
            }
            base.FixedUpdate();
        }
    }
}
