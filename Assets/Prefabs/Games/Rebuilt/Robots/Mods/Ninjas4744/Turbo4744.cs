using System.Collections.Generic;
using Games.Rebuilt.Components;
using Games.Rebuilt.Robots;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.Drivetrain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ninjas4744.MoSim
{
    /// <summary>Command/state port from Robot-Code-2026, commit 2bd3488.
    /// Rotor units are retained at the command boundary; contact speeds and
    /// linear rail travel are explicit adapters to MoSim's physical model.</summary>
    public sealed class Turbo4744 : RebuiltRobotBase
    {
        public enum RailState
        {
            Closed,
            Opened,
            HardPumping,
            SoftPumping,
        }

        public enum ShootState
        {
            Idle,
            PrepareHub,
            Hub,
            PrepareDelivery,
            Delivery,
            SaveOuttakeBack,
            ReverseBalls,
        }

        [Header("Mechanisms")]
        public ConfigurableJoint intakeRail,
            hopperRail;
        public InteractionRoller[] intakeRollers,
            floorRollers,
            shooterRollers,
            acceleratorRollers;
        public PvffController[] flywheels;
        public Turbo4744ShooterContact shooterContact,
            indexerContact,
            intakeContact,
            storageContact;
        public RebuiltAutoAimController autoAim;
        public Transform muzzle,
            roofNet,
            intakeRollerVisual;
        public BoxCollider roofCollider,
            feedHold,
            intakeHold;
        public BoxCollider[] intakeBacking = System.Array.Empty<BoxCollider>();

        [Header("Linear travel adapter")]
        public float intakeTravel = .24f,
            hopperTravel = .22946f,
            netFixedZ = .0036175f;
        public float railMaxSpeed = .16f,
            railAcceleration = 1.8f,
            railMaxForce = 350f;

        [Header("Rotor-to-contact speed adapters")]
        public float intakeRpm = 2000f,
            floorRpm = 1600f,
            acceleratorRpm = 2200f;
        public float intakeHoldFraction = .2f;
        public float shooterMotorToWheelRatio = .354f;

        [Tooltip(
            "No CAN current is simulated. Stalled fuel flow stands in for the team's 82 A accelerator jam trigger."
        )]
        public float jamFlowTimeout = .85f;
        public float flywheelRampRpmPerSecond = 15000f,
            feedTolerance = .02f,
            headingToleranceDegrees = 3f;
        public bool movingShotCompensation = true;

        [Tooltip(
            "MoSim tuning for the team's Look Hub heading-rate feedforward path (source tunable defaults to zero)."
        )]
        public float lookHubFeedForward = 1f;
        public bool Deployed { get; private set; }
        public bool Feeding { get; private set; }
        public bool Collecting => _intakeRunning && !_reverse;
        public bool Reversing => _reverse;
        public float CurrentShooterRpm { get; private set; }
        public float TargetShooterRpm { get; private set; }
        public float TargetShooterMotorRps { get; private set; }
        public float TargetIntakeMotorRps { get; private set; }
        public float TargetIndexerMotorRps { get; private set; }
        public float TargetAcceleratorMotorRps { get; private set; }
        public float ShotDistance { get; private set; }
        public float IntakeEncoderRotations =>
            Extension(intakeRail, _intakeClosed) * 11f / intakeTravel;
        public RailState PumpMode { get; private set; } = RailState.Closed;
        public ShootState ShootingState { get; private set; } = ShootState.Idle;
        public int CompletedHardPumpCycles { get; private set; }
        public int JamRecoveries { get; private set; }
        public bool AutomaticDelivery { get; private set; }
        public bool AimReady { get; private set; }
        public Vector3 AimTarget { get; private set; }
        public float FlightTime { get; private set; }

        private Rigidbody _body;
        private Turbo4744KickerRig _kickerRig;
        private bool _backingExtended;
        private float _intakeCollectionSpeed,
            _intakeCollectionForce;
        private DriveController _drive;
        private Vector3 _intakeClosed,
            _hopperClosed;
        private float _intakeVelocity,
            _hopperVelocity,
            _pumpPhaseStart,
            _shotBegan,
            _feedBegan,
            _enableBegan = -1;
        private float _acceleratorSpeed,
            _pumpDirection = 1;
        private bool _reverse,
            _manual,
            _intakeRunning = true,
            _spinning,
            _autoSoftIssued,
            _pendingHard,
            _pendingSoft,
            _stowRequested;
        private bool _lastIntake,
            _lastShoot,
            _lastPass,
            _lastPump,
            _lastStow,
            _lastManual;
        private readonly Collider[] _flowOverlaps = new Collider[128];
        private readonly Dictionary<Rigidbody, Vector2> _flowProgress =
            new Dictionary<Rigidbody, Vector2>();
        private float _lastFlowTime,
            _saveBegan,
            _reverseReady = -1;
        private ShootState _resumeShot;
        private Vector3 _previousVelocity;
        private bool _automaticShot,
            _showStatus;
        private int _controlTicks;
        private float _nextStatus;
        private float _aimRate;
        private InputActionMap _boundInputs;
        private InputAction _statusInput;
        private readonly System.Collections.Generic.HashSet<string> _presses =
            new System.Collections.Generic.HashSet<string>();

        private static readonly float[] Distances =
        {
            0,
            2,
            2.25f,
            2.5f,
            2.75f,
            3,
            3.25f,
            3.5f,
            3.75f,
            4,
            4.25f,
            4.5f,
            4.75f,
            5,
            6,
        };
        private static readonly float[] ShootRps =
        {
            44.3f,
            52.8f,
            54.3f,
            55.3f,
            58.3f,
            59.8f,
            61.8f,
            63.3f,
            65.3f,
            66.3f,
            71.3f,
            72.3f,
            75.05f,
            77.8f,
            93.2f,
        };
        private static readonly float[] FlightDistances =
        {
            0,
            2,
            2.5f,
            3,
            3.5f,
            4,
            4.5f,
            5,
            5.5f,
            6,
        };
        private static readonly float[] FlightSeconds =
        {
            0,
            .82f,
            .91f,
            .92f,
            1.12f,
            1.29f,
            1.28f,
            1.44f,
            1.57f,
            1.7f,
        };
        private static readonly float[] ContactDistances = { 2.25f, 2.5f, 3f, 4f, 4.5f, 5f };
        private static readonly float[] ContactRpms =
        {
            1162.4f,
            1184f,
            1270.2f,
            1387.1f,
            1474.2f,
            1545.1f,
        };
        private static readonly float[] ContactFlightSeconds =
        {
            .658f,
            .72f,
            .847f,
            1.006f,
            1.105f,
            1.161f,
        };

        public static float TeamShootRps(float distance) =>
            Mathf.Clamp(Interpolate(distance, Distances, ShootRps), 0, 95);

        public static float TeamFlightTime(float distance) =>
            Interpolate(distance, FlightDistances, FlightSeconds);

        public static float TeamDeliveryRps(float elapsed) => Mathf.Clamp(58f - elapsed, 55f, 58f);

        protected override void Init()
        {
            base.Init();
            _body = GetComponent<Rigidbody>();
            if (intakeContact)
            {
                _intakeCollectionSpeed = intakeContact.maxSurfaceSpeed;
                _intakeCollectionForce = intakeContact.maxTotalForce;
            }
            _kickerRig = GetComponent<Turbo4744KickerRig>();
            _drive = GetComponent<DriveController>();
            var stabilizer = GetComponent<JointStackStabilizer>();
            if (stabilizer)
                stabilizer.enabled = false;
            foreach (
                var rb in new[]
                {
                    _body,
                    intakeRail.GetComponent<Rigidbody>(),
                    hopperRail.GetComponent<Rigidbody>(),
                }
            )
            {
                rb.solverIterations = 24;
                rb.solverVelocityIterations = 12;
                rb.maxAngularVelocity = 12;
            }
            _intakeClosed =
                Quaternion.Inverse(_body.rotation)
                * (intakeRail.GetComponent<Rigidbody>().position - _body.position);
            _hopperClosed =
                Quaternion.Inverse(_body.rotation)
                * (hopperRail.GetComponent<Rigidbody>().position - _body.position);
            ConfigureSlide(intakeRail, _intakeClosed, intakeTravel);
            ConfigureSlide(hopperRail, _hopperClosed, hopperTravel);
            // The per-robot drive adapter applies the team's 0.75 curve with
            // an explicit zero guard; the stock exponent path assumes >= 1.
            MoveSpeedExponent = 1;
            RotationSpeedMultiplier = 1;
            IsFieldCentric = true;
            Scheduler.AddSubsystems(ReadControls, AimAndSpin, Feed, MoveRails, MonitorFlow);
            Scheduler.AddProtectedSystem(Disabled);
            Scheduler.AddLateRecursive(AnimateRoller);
            _showStatus = System.Environment.GetEnvironmentVariable("TURBO4744_STATUS") == "1";
            BindInputs();
            _statusInput = new InputAction("Turbo status", InputActionType.Button, "<Keyboard>/f8");
            _statusInput.performed += ToggleStatus;
            _statusInput.Enable();
            Debug.Log(
                "[Turbo 4744 v0.5.5] Staged intake, zone-selected shooting and physical rail control enabled."
            );
        }

        protected override void LateInit()
        {
            base.LateInit();
            var ib = intakeRail.GetComponent<Rigidbody>();
            var hb = hopperRail.GetComponent<Rigidbody>();
            // Fuel uses the bounded transfer contact. Keep the intake cores
            // solid against field structures without double-counting their
            // contact impulses against the same incoming fuel.
            foreach (
                var roller in System.Linq.Enumerable.Concat(
                    System.Linq.Enumerable.Concat(intakeRollers, floorRollers),
                    acceleratorRollers
                )
            )
            foreach (var c in roller.GetComponentsInChildren<Collider>())
                if (!c.isTrigger)
                    c.excludeLayers |= LayerMask.GetMask(
                        "GamePiece1",
                        "GamePiece2",
                        "GamePieceAlt"
                    );
            var colliders = System.Array.FindAll(GetComponentsInChildren<Collider>(true), c =>
                c.attachedRigidbody == _body
                || c.attachedRigidbody == ib
                || c.attachedRigidbody == hb
            );
            for (int i = 0; i < colliders.Length; i++)
            for (int j = i + 1; j < colliders.Length; j++)
                if (!(colliders[i] is WheelCollider) && !(colliders[j] is WheelCollider))
                    Physics.IgnoreCollision(colliders[i], colliders[j]);
            _drive.RefreshRobotRigidbodies();
        }

        private void Open()
        {
            Deployed = true;
            _stowRequested = false;
            PumpMode = RailState.Opened;
            _pendingHard = _pendingSoft = false;
        }

        public void StartHardPumping()
        {
            if (!Deployed || IntakeEncoderRotations < 10.5f)
            {
                Open();
                _pendingHard = true;
                return;
            }
            PumpMode = RailState.HardPumping;
            CompletedHardPumpCycles = 0;
            _pumpDirection = 1;
            _pumpPhaseStart = Time.fixedTime;
        }

        private void Soft()
        {
            if (!Deployed || IntakeEncoderRotations < 8f)
            {
                Open();
                _pendingSoft = true;
                return;
            }
            PumpMode = RailState.SoftPumping;
            _pumpDirection = 1;
            _pumpPhaseStart = Time.fixedTime;
        }

        private bool ActiveShot =>
            ShootingState != ShootState.Idle && ShootingState != ShootState.ReverseBalls;
        private bool Delivery =>
            ShootingState == ShootState.PrepareDelivery
            || ShootingState == ShootState.Delivery
            || ShootingState == ShootState.SaveOuttakeBack && _resumeShot == ShootState.Delivery;
        private bool Preparing =>
            ShootingState == ShootState.PrepareHub || ShootingState == ShootState.PrepareDelivery;

        private void PrepareShot(bool delivery, bool manual = false, bool automatic = false)
        {
            if (intakeContact)
                intakeContact.Stop();
            _automaticShot = automatic;
            _aimRate = 0;
            _manual = manual;
            _spinning = false;
            _autoSoftIssued = false;
            _shotBegan = Time.fixedTime;
            _intakeRunning = true;
            if (!Deployed)
                Open();
            ShootingState = delivery ? ShootState.PrepareDelivery : ShootState.PrepareHub;
        }

        private void StopShot()
        {
            ShootingState = ShootState.Idle;
            Feeding = false;
            _spinning = false;
            _manual = false;
            MoveSpeedMultiplier = 1;
        }

        private void ReadControls()
        {
            BindInputs();
            _controlTicks++;
            if (_enableBegan < 0)
                _enableBegan = Time.fixedTime;
            bool intake = IntakeAction.IsPressed(),
                shoot = OuttakeAction.IsPressed(),
                pass = AutoPass.IsPressed();
            bool pump = RobotSpecial.IsPressed(),
                stow = StowAction.IsPressed(),
                manual = ManualShoot.IsPressed();
            // Preserve performed edges even if a quick keyboard/trigger press
            // begins and ends between render/physics polls.
            bool intakePress = _presses.Remove("Intake"),
                shootPress = _presses.Remove("AutoShoot"),
                passPress = _presses.Remove("AutoPass");
            bool pumpPress = _presses.Remove("RobotSpecial1"),
                stowPress = _presses.Remove("Stow");
            _reverse = FaceDown.IsPressed();
            // Triggers.configureTriggers uses alliance-relative X with a
            // 3.6 / 5.6 m hysteresis band across the trench and bump.
            float allianceX =
                8.27f - (Alliance == MoSimCore.Enums.Alliance.Blue ? 1 : -1) * _body.position.x;
            if (allianceX < 3.6f)
                AutomaticDelivery = false;
            else if (allianceX > 5.6f)
                AutomaticDelivery = true;
            if (_automaticShot && ActiveShot && Delivery != AutomaticDelivery)
                PrepareShot(AutomaticDelivery, false, true);
            if (
                !_stowRequested
                && PumpMode == RailState.Closed
                && Time.fixedTime - _enableBegan >= .5f
            )
                Open();
            if (intakePress || intake && !_lastIntake || IntakeAction.WasTriggered())
            {
                bool reverse =
                    ShootingState == ShootState.Hub || ShootingState == ShootState.Delivery;
                StopShot();
                if (reverse)
                {
                    ShootingState = ShootState.ReverseBalls;
                    _reverseReady = -1;
                }
                Open();
                _intakeRunning = true;
            }
            if (shootPress || shoot && !_lastShoot || OuttakeAction.WasTriggered())
            {
                if (ActiveShot && !_manual)
                    StartHardPumping();
                else
                    PrepareShot(AutomaticDelivery, false, true);
            }
            if (passPress || pass && !_lastPass || AutoPass.WasTriggered())
            {
                if (ActiveShot && Delivery)
                    StartHardPumping();
                else
                    PrepareShot(true);
            }
            if (manual && !_lastManual)
                PrepareShot(false, true);
            if (!manual && _lastManual && _manual)
                StopShot();
            if (pumpPress || pump && !_lastPump || RobotSpecial.WasTriggered())
                StartHardPumping();
            if (stowPress || stow && !_lastStow || StowAction.WasTriggered())
            {
                StopShot();
                PumpMode = RailState.Closed;
                Deployed = false;
                _stowRequested = true;
                _pendingHard = _pendingSoft = false;
            }
            _lastIntake = intake;
            _lastShoot = shoot;
            _lastPass = pass;
            _lastPump = pump;
            _lastStow = stow;
            _lastManual = manual;
            if (_pendingHard && IntakeEncoderRotations >= 10.5f)
            {
                _pendingHard = false;
                StartHardPumping();
            }
            if (_pendingSoft && IntakeEncoderRotations >= 10.5f)
            {
                _pendingSoft = false;
                Soft();
            }
        }

        private void AimAndSpin()
        {
            Vector3 acceleration = (_body.velocity - _previousVelocity) / Time.fixedDeltaTime;
            acceleration.y = 0;
            _previousVelocity = _body.velocity;
            TargetShooterMotorRps = TargetShooterRpm = 0;
            TargetAcceleratorMotorRps = 0;
            AimReady = false;
            if (ActiveShot && !_reverse)
            {
                // Standard shoot selects the team's zone-dependent mode;
                // AutoPass remains an explicit delivery override.
                if (Delivery)
                {
                    float ownSign = Alliance == MoSimCore.Enums.Alliance.Blue ? 1 : -1;
                    float allianceY = 4.0348f + ownSign * _body.position.z;
                    autoAim.SetCustomTarget(
                        new Vector3(
                            ownSign * (8.27f - 1.25f),
                            0,
                            ownSign * ((allianceY > 4 ? 7f : 1f) - 4.0348f)
                        )
                    );
                }
                else
                    autoAim.SetDefaultMap(DefaultMap.Hub);
                Vector3 target = autoAim.GetCurrentTarget(),
                    virtualTarget = target;
                // Fuel inherits the launch point's translation and rotation.
                // Keep heading feedforward independent of that angular lead
                // so the aim controller does not feed its own acceleration back.
                Vector3 launchVelocity = _body.GetPointVelocity(muzzle.position);
                launchVelocity.y = 0;
                if (movingShotCompensation && !_manual)
                    for (int i = 0; i < 5; i++)
                    {
                        Vector3 delta = virtualTarget - _body.position;
                        delta.y = 0;
                        FlightTime = Delivery
                            ? DeliveryFlightTime(delta.magnitude)
                            : MoSimHubFlightTime(delta.magnitude);
                        virtualTarget = target - launchVelocity * FlightTime;
                    }
                AimTarget = target;
                Vector3 offset = virtualTarget - _body.position;
                offset.y = 0;
                ShotDistance = _manual ? 3 : offset.magnitude;
                if (!_manual)
                {
                    autoAim.SetCustomTarget(virtualTarget);
                    Vector3 direction = virtualTarget - muzzle.position;
                    float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                    var bearing = target - _body.position;
                    bearing.y = 0;
                    float rate =
                        (bearing.x * _body.velocity.z - bearing.z * _body.velocity.x)
                        / Mathf.Max(.25f, bearing.sqrMagnitude)
                        * Mathf.Rad2Deg;
                    _aimRate = Mathf.Lerp(
                        _aimRate,
                        Mathf.Clamp(rate, -360, 360),
                        1 - Mathf.Exp(-Time.fixedDeltaTime / .04f)
                    );
                    autoAim.Aim(angle, Delivery ? 0 : _aimRate * lookHubFeedForward);
                }
                Vector3 hubDelta = target - _body.position;
                hubDelta.y = 0;
                float tolerance = Delivery ? 3f : Mathf.Max(.2f, 12f - 1.9f * hubDelta.magnitude);
                bool aligned = _manual || Mathf.Abs(autoAim.GetRotationError()) < tolerance;
                bool inRange = _manual || Delivery || ShotDistance > 2f;
                bool settled =
                    _manual
                    || Delivery
                    || (acceleration.magnitude < 3f && Mathf.Abs(_body.angularVelocity.y) < 1f);
                AimReady =
                    aligned
                    && inRange
                    && settled
                    && (
                        _manual
                        || Mathf.Abs(autoAim.GetRotationError()) < Mathf.Min(tolerance, 1.5f)
                    );
                if (aligned && inRange && settled && !_spinning)
                {
                    _spinning = true;
                    _shotBegan = Time.fixedTime;
                }
                if (_spinning)
                {
                    TargetShooterMotorRps = Delivery
                        ? TeamDeliveryRps(Time.fixedTime - _shotBegan)
                        : TeamShootRps(ShotDistance);
                    TargetShooterRpm = Delivery
                        ? DeliveryWheelRpm(ShotDistance, TargetShooterMotorRps)
                        : MoSimHubWheelRpm(ShotDistance)
                            * (TargetShooterMotorRps / TeamShootRps(ShotDistance))
                            * (shooterMotorToWheelRatio / .354f);
                    TargetAcceleratorMotorRps =
                        ShootingState == ShootState.SaveOuttakeBack ? 0 : 80;
                }
                MoveSpeedMultiplier = !Delivery && !_manual ? .3f : 1;
                bool shooterReady =
                    Mathf.Abs(CurrentShooterRpm - TargetShooterRpm)
                    <= Mathf.Max(10, TargetShooterRpm * feedTolerance);
                bool acceleratorReady = Mathf.Abs(_acceleratorSpeed - acceleratorRpm) < 25;
                if (
                    Preparing
                    && _spinning
                    && aligned
                    && inRange
                    && settled
                    && shooterReady
                    && acceleratorReady
                )
                {
                    ShootingState = Delivery ? ShootState.Delivery : ShootState.Hub;
                    _feedBegan = _lastFlowTime = Time.fixedTime;
                    _autoSoftIssued = false;
                    if (Delivery)
                        _shotBegan = Time.fixedTime;
                }
                if (!Preparing && !_autoSoftIssued && Time.fixedTime - _feedBegan >= .8f)
                {
                    _autoSoftIssued = true;
                    Soft();
                }
            }
            else
                MoveSpeedMultiplier = 1;
            float rpm = Mathf.MoveTowards(
                CurrentShooterRpm,
                TargetShooterRpm,
                flywheelRampRpmPerSecond * Time.fixedDeltaTime
            );
            if (shooterContact)
            {
                shooterContact.SetRpm(TargetShooterRpm);
                CurrentShooterRpm = shooterContact.CurrentRpm;
            }
            else if (flywheels != null && flywheels.Length > 0)
            {
                float actual = 0;
                foreach (var flywheel in flywheels)
                {
                    flywheel.SetRpm(-rpm);
                    actual += Mathf.Abs(flywheel.CurrentRpm);
                }
                CurrentShooterRpm = actual / flywheels.Length;
            }
            else
            {
                CurrentShooterRpm = rpm;
                SetRollers(shooterRollers, -rpm);
            }
            _acceleratorSpeed = Mathf.MoveTowards(
                _acceleratorSpeed,
                TargetAcceleratorMotorRps > 0 ? acceleratorRpm : 0,
                flywheelRampRpmPerSecond * Time.fixedDeltaTime
            );
            SetRollers(acceleratorRollers, -_acceleratorSpeed);
        }

        private void Feed()
        {
            if (!Deployed)
                _backingExtended = false;
            else if (
                IntakeEncoderRotations >= 10.5f
                && (!_kickerRig || _kickerRig.Deployment >= .99f)
            )
                _backingExtended = true;
            foreach (var surface in intakeBacking)
                if (surface)
                    surface.enabled = _backingExtended;
            // A latched shoot state must not continue ejecting fuel during a
            // large re-aim or zone transition in the simulator.
            bool speedReady =
                Mathf.Abs(CurrentShooterRpm - TargetShooterRpm)
                <= Mathf.Max(10, TargetShooterRpm * feedTolerance);
            Feeding =
                (ShootingState == ShootState.Hub || ShootingState == ShootState.Delivery)
                && AimReady
                && speedReady
                && !_reverse;
            TargetIntakeMotorRps = _intakeRunning ? 90 : 0;
            bool backing =
                ShootingState == ShootState.SaveOuttakeBack
                || ShootingState == ShootState.ReverseBalls;
            TargetIndexerMotorRps =
                backing ? -20
                : Feeding ? 78
                : 0;
            if (_reverse)
            {
                TargetIntakeMotorRps = -20;
                TargetIndexerMotorRps = -20;
                Feeding = false;
            }
            feedHold.enabled = !Feeding && !_reverse && !backing;
            SetRollers(intakeRollers, -intakeRpm * TargetIntakeMotorRps / 90f);
            if (intakeContact)
            {
                intakeContact.maxSurfaceSpeed = ActiveShot ? 1.2f : _intakeCollectionSpeed;
                intakeContact.maxTotalForce = ActiveShot ? 140f : _intakeCollectionForce;
                intakeContact.SetRpm(Deployed ? intakeRpm * TargetIntakeMotorRps / 90f : 0);
            }
            SetRollers(floorRollers, -floorRpm * TargetIndexerMotorRps / 78f);
            if (indexerContact)
                indexerContact.SetRpm(floorRpm * TargetIndexerMotorRps / 78f);
            if (storageContact)
            {
                // The hopper is not a powered conveyor on the real robot.
                // Transfer here represents contact with the retracting tray:
                // its speed follows the measured rail, rather than an invisible
                // high-speed belt carrying every stored ball to the shooter.
                var hb = hopperRail.GetComponent<Rigidbody>();
                float railSpeed = (
                    Quaternion.Inverse(_body.rotation)
                    * (hb.velocity - _body.GetPointVelocity(hb.position))
                ).z;
                float transferSpeed =
                    Feeding ? Mathf.Max(0, -railSpeed)
                    : backing || _reverse ? -.18f
                    : 0;
                storageContact.SetRpm(
                    transferSpeed * 60 / (2 * Mathf.PI * storageContact.wheelRadius)
                );
            }
        }

        private void MonitorFlow()
        {
            if (ShootingState == ShootState.ReverseBalls)
            {
                if (_acceleratorSpeed < acceleratorRpm * 30f / 80f && _reverseReady < 0)
                    _reverseReady = Time.fixedTime;
                if (_reverseReady >= 0 && Time.fixedTime - _reverseReady >= 1)
                    StopShot();
                return;
            }
            if (ShootingState == ShootState.SaveOuttakeBack)
            {
                if (Time.fixedTime - _saveBegan >= .5f)
                {
                    ShootingState = _resumeShot;
                    _flowProgress.Clear();
                    _feedBegan = _lastFlowTime = Time.fixedTime;
                    _autoSoftIssued = false;
                    if (Delivery)
                        _shotBegan = Time.fixedTime;
                }
                return;
            }
            if (!Feeding)
            {
                _lastFlowTime = Time.fixedTime;
                _flowProgress.Clear();
                return;
            }
            bool occupied = false,
                progress = false;
            int count = Physics.OverlapBoxNonAlloc(
                transform.TransformPoint(new Vector3(0, .30f, .27f)),
                new Vector3(.34f, .22f, .34f),
                _flowOverlaps,
                transform.rotation,
                LayerMask.GetMask("GamePiece1", "GamePiece2", "GamePieceAlt"),
                QueryTriggerInteraction.Ignore
            );
            for (int i = 0; i < count; i++)
            {
                var rb = _flowOverlaps[i].attachedRigidbody;
                if (!rb || rb.isKinematic)
                    continue;
                occupied = true;
                var position = transform.InverseTransformPoint(rb.position);
                if (!_flowProgress.TryGetValue(rb, out var previous))
                {
                    _flowProgress.Add(rb, new Vector2(position.z, position.y));
                    continue;
                }
                // Slow tray pumping is real progress even below 4 cm/s.
                // Accumulate travel instead of treating low instantaneous
                // speed as a jam and undoing every small forward stroke.
                if (position.z < previous.x - .006f)
                {
                    previous.x = position.z;
                    progress = true;
                }
                if (position.y > previous.y + .025f)
                {
                    previous.y = position.y;
                    progress = true;
                }
                _flowProgress[rb] = previous;
            }
            if (!occupied || progress)
            {
                _lastFlowTime = Time.fixedTime;
                return;
            }
            if (Time.fixedTime - _lastFlowTime < jamFlowTimeout)
                return;
            _resumeShot = ShootingState;
            ShootingState = ShootState.SaveOuttakeBack;
            _saveBegan = Time.fixedTime;
            JamRecoveries++;
        }

        private void MoveRails()
        {
            if (PumpMode == RailState.HardPumping || PumpMode == RailState.SoftPumping)
            {
                bool hard = PumpMode == RailState.HardPumping;
                float high = hard ? 11 : 10,
                    low = hard ? 8 : 9;
                bool reached =
                    _pumpDirection > 0
                        ? IntakeEncoderRotations >= high - .05f
                        : IntakeEncoderRotations <= low;
                if (reached || Time.fixedTime - _pumpPhaseStart >= .7f)
                {
                    if (_pumpDirection < 0 && hard && ++CompletedHardPumpCycles >= 3)
                    {
                        Soft();
                        hard = false;
                    }
                    else
                        _pumpDirection = -_pumpDirection;
                    _pumpPhaseStart = Time.fixedTime;
                }
                float duty = hard ? .25f : .15f;
                // The reference footage keeps the lower kicker deployed
                // during the short pump stroke, outside the bumper.
                _kickerRig?.PrepareRailMotion(false, Time.fixedDeltaTime);
                DriveVelocity(
                    intakeRail,
                    _pumpDirection * railMaxSpeed * duty / .25f,
                    ref _intakeVelocity
                );
            }
            else
            {
                if (WaitForKicker(PumpMode == RailState.Closed))
                    return;
                DrivePosition(
                    intakeRail,
                    _intakeClosed,
                    PumpMode == RailState.Closed ? 0 : intakeTravel,
                    0,
                    ref _intakeVelocity
                );
            }
            // The deployed intake/hopper are mechanically coupled in this CAD.
            // Box.java is not instantiated by the team's current RobotContainer.
            float extension = Mathf.Clamp(Extension(intakeRail, _intakeClosed), 0, intakeTravel);
            var irb = intakeRail.GetComponent<Rigidbody>();
            float velocity = (
                Quaternion.Inverse(_body.rotation)
                * (irb.velocity - _body.GetPointVelocity(irb.position))
            ).z;
            DrivePosition(
                hopperRail,
                _hopperClosed,
                extension * hopperTravel / intakeTravel,
                velocity * hopperTravel / intakeTravel,
                ref _hopperVelocity
            );
        }

        private bool WaitForKicker(bool retracting)
        {
            if (!_kickerRig || _kickerRig.PrepareRailMotion(retracting, Time.fixedDeltaTime))
                return false;
            // Hold both slides while the lower kicker folds up. The fixed
            // frame stays attached to the hopper throughout the sequence.
            DrivePosition(
                intakeRail,
                _intakeClosed,
                Extension(intakeRail, _intakeClosed),
                0,
                ref _intakeVelocity
            );
            DrivePosition(
                hopperRail,
                _hopperClosed,
                Extension(hopperRail, _hopperClosed),
                0,
                ref _hopperVelocity
            );
            return true;
        }

        private void DrivePosition(
            ConfigurableJoint joint,
            Vector3 closed,
            float target,
            float ff,
            ref float velocity
        )
        {
            float error = target - Extension(joint, closed);
            float brake = Mathf.Sqrt(2 * railAcceleration * Mathf.Abs(error));
            float desired = Mathf.Clamp(
                ff + (Mathf.Abs(error) < .0015f ? 0 : Mathf.Clamp(error * 8, -brake, brake)),
                -railMaxSpeed,
                railMaxSpeed
            );
            DriveVelocity(joint, desired, ref velocity);
        }

        private void DriveVelocity(ConfigurableJoint joint, float desired, ref float velocity)
        {
            velocity = Mathf.MoveTowards(velocity, desired, railAcceleration * Time.fixedDeltaTime);
            joint.targetVelocity = new Vector3(0, 0, -velocity);
            joint.zDrive = new JointDrive
            {
                positionSpring = 0,
                positionDamper = 1800,
                maximumForce = railMaxForce,
            };
        }

        private float Extension(ConfigurableJoint joint, Vector3 closed) =>
            (
                Quaternion.Inverse(_body.rotation)
                * (joint.GetComponent<Rigidbody>().position - _body.position)
            ).z - closed.z;

        private void ConfigureSlide(ConfigurableJoint joint, Vector3 closed, float travel)
        {
            joint.connectedBody = _body;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero;
            joint.connectedAnchor = closed + Vector3.forward * (travel * .5f);
            joint.axis = Vector3.right;
            joint.secondaryAxis = Vector3.up;
            joint.configuredInWorldSpace = false;
            joint.xMotion = joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Limited;
            joint.angularXMotion =
                joint.angularYMotion =
                joint.angularZMotion =
                    ConfigurableJointMotion.Locked;
            // Keep the limit active across the position controller's 1.5 mm
            // settling band, including sudden external actuator impulses.
            joint.linearLimit = new SoftJointLimit
            {
                limit = travel * .5f,
                contactDistance = .005f,
                bounciness = 0,
            };
            joint.linearLimitSpring = new SoftJointLimitSpring();
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = .002f;
            joint.projectionAngle = .5f;
            joint.enableCollision = false;
            joint.zDrive = new JointDrive
            {
                positionSpring = 0,
                positionDamper = 1800,
                maximumForce = railMaxForce,
            };
        }

        private void Disabled()
        {
            if (
                MoSimCore.BaseClasses.GameManagement.BaseGameManager.Instance.RobotState
                != MoSimCore.Enums.RobotState.Disabled
            )
                return;
            _presses.Clear();
            SetRollers(intakeRollers, 0);
            SetRollers(floorRollers, 0);
            SetRollers(shooterRollers, 0);
            SetRollers(acceleratorRollers, 0);
            if (flywheels != null)
                foreach (var flywheel in flywheels)
                    flywheel.SetRpm(0);
            if (shooterContact)
                shooterContact.Stop();
            if (indexerContact)
                indexerContact.Stop();
            if (storageContact)
                storageContact.Stop();
            if (intakeContact)
                intakeContact.Stop();
            CurrentShooterRpm =
                TargetShooterRpm =
                TargetShooterMotorRps =
                TargetIntakeMotorRps =
                TargetIndexerMotorRps =
                TargetAcceleratorMotorRps =
                    0;
            _acceleratorSpeed = 0;
            Feeding = false;
            feedHold.enabled = true;
            _intakeVelocity = _hopperVelocity = 0;
            intakeRail.targetVelocity = hopperRail.targetVelocity = Vector3.zero;
            _enableBegan = -1;
            _spinning = false;
            ShootingState = ShootState.Idle;
        }

        private void AnimateRoller()
        {
            if (intakeRollerVisual && intakeRollers.Length > 0)
                intakeRollerVisual.Rotate(
                    transform.right,
                    intakeRollers[0].realRpm * 6 * Time.deltaTime,
                    UnityEngine.Space.World
                );
        }

        // Delivery is calibrated independently from the elevated hub. Applying
        // the hub's wheel correction to a floor pass severely shortens it.
        // Keep the team's 58 -> 55 motor-RPS sweep at the command boundary.
        private static float DeliverySpeed(float distance)
        {
            const float angle = 58f * Mathf.Deg2Rad;
            float d = Mathf.Max(.5f, distance - .20f),
                height = .66f;
            return Mathf.Sqrt(
                9.81f
                    * d
                    * d
                    / (2 * Mathf.Cos(angle) * Mathf.Cos(angle) * (height + d * Mathf.Tan(angle)))
            );
        }

        public static float DeliveryWheelRpm(float distance, float motorRps) =>
            DeliverySpeed(distance) * 60f / (2 * Mathf.PI * .05f) * (motorRps / 58f) * 1.02f;

        public static float DeliveryFlightTime(float distance) =>
            Mathf.Max(.1f, distance) / (DeliverySpeed(distance) * Mathf.Cos(58f * Mathf.Deg2Rad));

        public static float MoSimHubWheelRpm(float distance)
        {
            if (distance < 2.25f)
                return TeamShootRps(distance) * 60 * .354f * 1.008f;
            if (distance <= 5)
                return Interpolate(distance, ContactDistances, ContactRpms);
            // Preserve the measured five-metre calibration, then extend its
            // range using the physical release angle instead of a flat rotor
            // correction that saturates on long moving virtual targets.
            return ContactRpms[5] + HubBallisticRpm(distance) - HubBallisticRpm(5);
        }

        private static float HubBallisticRpm(float distance)
        {
            float d = Mathf.Max(.5f, distance - .20f),
                a = 58f * Mathf.Deg2Rad;
            return Mathf.Sqrt(
                    9.81f
                        * d
                        * d
                        / (
                            2
                            * Mathf.Cos(a)
                            * Mathf.Cos(a)
                            * Mathf.Max(.1f, d * Mathf.Tan(a) - 1.278f)
                        )
                )
                * 60
                / (2 * Mathf.PI * .05f);
        }

        private static float HubBallisticTime(float rpm)
        {
            float vy = rpm * (2 * Mathf.PI * .05f / 60) * Mathf.Sin(58f * Mathf.Deg2Rad);
            return (vy + Mathf.Sqrt(Mathf.Max(0, vy * vy - 2 * 9.81f * 1.278f))) / 9.81f;
        }

        public static float MoSimHubFlightTime(float distance) =>
            distance <= 5
                ? Interpolate(distance, ContactDistances, ContactFlightSeconds)
                : ContactFlightSeconds[5]
                    + HubBallisticTime(MoSimHubWheelRpm(distance))
                    - HubBallisticTime(ContactRpms[5]);

        private void OnGUI()
        {
            if (!_showStatus)
                return;
            var style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 16,
            };
            GUI.Box(
                new Rect(12, 180, 370, 132),
                "Turbo 4744 v0.5.5  |  "
                    + ShootingState
                    + "\n"
                    + "Target: "
                    + (Delivery ? "side delivery" : "hub")
                    + "  "
                    + ShotDistance.ToString("F1")
                    + " m  |  "
                    + (AimReady ? "aligned" : "aligning")
                    + "\n"
                    + "Rail: "
                    + PumpMode
                    + "  "
                    + IntakeEncoderRotations.ToString("F1")
                    + " / 11  |  cycles "
                    + CompletedHardPumpCycles
                    + "\n"
                    + "Speed: "
                    + (_body ? _body.velocity.magnitude : 0).ToString("F1")
                    + " m/s  |  feed "
                    + Feeding
                    + "\n"
                    + "State: "
                    + MoSimCore.BaseClasses.GameManagement.BaseGameManager.Instance.RobotState
                    + "  |  controls "
                    + _controlTicks,
                style
            );
        }

        protected override void Update()
        {
            base.Update();
            if (_showStatus && _body && Time.unscaledTime >= _nextStatus)
            {
                _nextStatus = Time.unscaledTime + 1;
                Debug.Log(
                    "[Turbo status] state="
                        + ShootingState
                        + " pump="
                        + PumpMode
                        + " ticks="
                        + _controlTicks
                        + " position="
                        + _body.position.ToString("F3")
                        + " velocity="
                        + _body.velocity.ToString("F3")
                        + " rail="
                        + IntakeEncoderRotations.ToString("F2")
                        + " target="
                        + AimTarget.ToString("F3")
                        + " error="
                        + autoAim.GetRotationError().ToString("F2")
                        + " feed="
                        + Feeding
                        + " rpm="
                        + CurrentShooterRpm.ToString("F0")
                        + " step="
                        + Time.fixedDeltaTime.ToString("F5")
                );
            }
        }

        private void BindInputs()
        {
            if (_boundInputs == InputActionMap)
                return;
            if (_boundInputs != null)
                _boundInputs.actionTriggered -= InputPerformed;
            _boundInputs = InputActionMap;
            if (_boundInputs != null)
                _boundInputs.actionTriggered += InputPerformed;
        }

        private void InputPerformed(InputAction.CallbackContext context)
        {
            if (context.performed)
                _presses.Add(context.action.name);
        }

        private void ToggleStatus(InputAction.CallbackContext context) =>
            _showStatus = !_showStatus;

        private void OnDisable()
        {
            if (_boundInputs != null)
                _boundInputs.actionTriggered -= InputPerformed;
            _boundInputs = null;
            _presses.Clear();
            if (_statusInput != null)
            {
                _statusInput.performed -= ToggleStatus;
                _statusInput.Dispose();
                _statusInput = null;
            }
        }

        private static void SetRollers(InteractionRoller[] rollers, float rpm)
        {
            foreach (var roller in rollers)
                roller.SetRpm(rpm);
        }

        public static float Interpolate(float x, float[] xs, float[] ys)
        {
            if (x <= xs[0])
                return ys[0];
            for (int i = 1; i < xs.Length; i++)
                if (x <= xs[i])
                    return Mathf.Lerp(ys[i - 1], ys[i], Mathf.InverseLerp(xs[i - 1], xs[i], x));
            return ys[ys.Length - 1];
        }
    }
}
