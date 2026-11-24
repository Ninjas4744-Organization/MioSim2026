using System.Collections.Generic;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using RobotFramework.Controllers.Drivetrain;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Team joystick shaping and rigid tire/underside contact calibration.
    /// The stock controller retains motor, slip, steering and aim calculations.</summary>
    public sealed class Turbo4744DriveController : DriveController
    {
        private bool _fuelContactsConfigured;
        private int _fuelMask;
        private PhysicMaterial _undersideMaterial;
        private readonly Dictionary<Collider, PhysicMaterial> _undersideOriginals =
            new Dictionary<Collider, PhysicMaterial>();

        protected override void Start()
        {
            base.Start();
            ConfigureWheelContacts();
            // Child rollers can create support cores later in Start.
            _fuelContactsConfigured = false;
            var contacts = GetComponent<Turbo4744BumperContacts>();
            if (!contacts)
                contacts = gameObject.AddComponent<Turbo4744BumperContacts>();
            contacts.RefreshContacts();
            if (!GetComponent<Turbo4744UnderbodyClearance>())
                gameObject.AddComponent<Turbo4744UnderbodyClearance>();
            Turbo4744LandingTrace.AttachIfRequested(this);
        }

        private void ConfigureWheelContacts()
        {
            _fuelMask = LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt");
            // Fuel is excluded from suspension/traction queries, but the solid
            // tire meshes still collide with it through ordinary physics.
            foreach (var wheel in GetComponentsInChildren<WheelCollider>())
            {
                foreach (var collider in wheel.GetComponentsInChildren<Collider>())
                {
                    if (collider.isTrigger)
                        continue;
                    if (collider is MeshCollider tire)
                    {
                        collider.excludeLayers &= ~_fuelMask;
                        // These modules have rigid, solid tires. A second,
                        // moving suspension probe can miss a loaded solid tire
                        // or sample carpet below a lifted tire. Query the same
                        // center as the physical tread, without a second spring.
                        wheel.center = wheel.transform.InverseTransformPoint(tire.bounds.center);
                        wheel.suspensionDistance = 0;
                        wheel.suspensionSpring = new JointSpring();
                    }
                    else
                        collider.excludeLayers |= _fuelMask;
                }
            }
            contactTolerance = Mathf.Min(contactTolerance, .002f);
            if (!_undersideMaterial)
                _undersideMaterial = new PhysicMaterial("Turbo smooth underside")
                {
                    staticFriction = .03f,
                    dynamicFriction = .03f,
                    bounciness = 0,
                    frictionCombine = PhysicMaterialCombine.Minimum,
                    bounceCombine = PhysicMaterialCombine.Minimum,
                };
            foreach (var collider in GetComponentsInChildren<BoxCollider>())
                if (
                    collider.attachedRigidbody == _rb
                    && (
                        collider.name.StartsWith("Bellypan ") || collider.name.StartsWith("Swerve ")
                    )
                )
                {
                    // The default material becomes very grippy when the game's
                    // compressed-fuel modifier multiplies its friction. Use the
                    // existing feed-plastic profile for passive sliding plates,
                    // without changing tire grip, bumpers or contact geometry.
                    if (!_undersideOriginals.ContainsKey(collider))
                        _undersideOriginals.Add(collider, collider.sharedMaterial);
                    collider.sharedMaterial = _undersideMaterial;
                }
            _fuelContactsConfigured = true;
        }

        protected override void OnDestroy()
        {
            foreach (var pair in _undersideOriginals)
                if (pair.Key)
                    pair.Key.sharedMaterial = pair.Value;
            if (_undersideMaterial)
            {
                if (Application.isPlaying)
                    Destroy(_undersideMaterial);
                else
                    DestroyImmediate(_undersideMaterial);
            }
            base.OnDestroy();
        }

        private static float Deadband(float value) =>
            Mathf.Abs(value) <= .04f ? 0 : Mathf.Sign(value) * (Mathf.Abs(value) - .04f) / .96f;

        protected override void FixedUpdate()
        {
            if (!_fuelContactsConfigured)
                ConfigureWheelContacts();
            _fieldLayerMask |= _fuelMask;
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
