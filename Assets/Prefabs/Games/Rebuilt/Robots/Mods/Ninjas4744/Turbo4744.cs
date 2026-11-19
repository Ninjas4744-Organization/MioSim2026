using Games.Rebuilt.Components;
using Games.Rebuilt.Robots;
using MoSimLib;
using RobotFramework.Components;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Turbo's Rebuilt implementation. Geometry and drive constants are source-backed;
    /// roller/contact calibration and rail travel are editable simulation approximations.</summary>
    public sealed class Turbo4744 : RebuiltRobotBase
    {
        [Header("Turbo mechanisms")]
        public ConfigurableJoint intakeRail;
        public ConfigurableJoint hopperRail;
        public InteractionRoller[] intakeRollers;
        public InteractionRoller[] floorRollers;
        public InteractionRoller[] shooterRollers;
        public InteractionRoller[] acceleratorRollers;
        public RebuiltAutoAimController autoAim;
        public Transform muzzle;
        public Transform roofNet;
        public BoxCollider roofCollider;
        public BoxCollider feedHold;
        public BoxCollider intakeHold;
        public Transform intakeRollerVisual;

        [Header("Rail travel (metres; simulation calibration)")]
        public float intakeTravel = .24f;
        public float hopperTravel = .22946f;
        public float netFixedZ = .0036175f;
        [Min(.01f)] public float railMaxSpeed = .32f;
        [Min(.01f)] public float railAcceleration = 1.1f;
        [Min(1f)] public float railMaxForce = 120f;

        [Header("Roller calibration")]
        public float intakeRpm = 1300f;
        [Range(0f,1f)] public float intakeHoldFraction = .2f;
        public float floorRpm = 1600f;
        public float acceleratorRpm = 2200f;
        public float shooterMotorToWheelRatio = .52f;
        public float flywheelRampRpmPerSecond = 15000f;
        public float feedTolerance = .08f;
        public float headingToleranceDegrees = 3f;
        public bool movingShotCompensation = true;

        public bool Deployed { get; private set; }
        public bool Feeding { get; private set; }
        public float CurrentShooterRpm { get; private set; }
        public float TargetShooterRpm { get; private set; }
        public float ShotDistance { get; private set; }
        public bool Collecting => _intaking;
        public bool Reversing => _reverse;

        private Rigidbody _body;
        private Vector3 _intakeClosedPose,_hopperClosedPose;
        private float _intakeVelocity,_hopperVelocity;
        private bool _reverse;
        private bool _agitate;
        private bool _shooting;
        private bool _manual;
        private bool _passing;
        private bool _intaking;
        private float _intakeTarget;
        private float _hopperTarget;

        // The team's PositionsConstants.java stores motor rotations/second.
        private static readonly float[] Distances = {0f,2f,2.25f,2.5f,2.75f,3f,3.25f,3.5f,3.75f,4f,4.25f,4.5f,4.75f,5f,6f};
        private static readonly float[] ShootRps = {44.3f,52.8f,54.3f,55.3f,58.3f,59.8f,61.8f,63.3f,65.3f,66.3f,71.3f,72.3f,75.05f,77.8f,93.2f};
        private static readonly float[] FlightDistances = {0f,2f,2.5f,3f,3.5f,4f,4.5f,5f,5.5f,6f};
        private static readonly float[] FlightSeconds = {0f,.82f,.91f,.92f,1.12f,1.29f,1.28f,1.44f,1.57f,1.7f};

        protected override void Init()
        {
            base.Init();
            _body = GetComponent<Rigidbody>();
            // The framework helper caches child fuel bodies in Awake, before
            // the normal spawn handler detaches preloads. Configure this small,
            // fully constrained assembly explicitly instead.
            var stabilizer=GetComponent<JointStackStabilizer>();
            if(stabilizer)stabilizer.enabled=false;
            foreach(var body in new[]{_body,intakeRail.GetComponent<Rigidbody>(),hopperRail.GetComponent<Rigidbody>()})
            {
                body.solverIterations=24;
                body.solverVelocityIterations=12;
                body.maxAngularVelocity=12f;
            }
            _intakeClosedPose=Quaternion.Inverse(_body.rotation)*(intakeRail.GetComponent<Rigidbody>().position-_body.position);
            _hopperClosedPose=Quaternion.Inverse(_body.rotation)*(hopperRail.GetComponent<Rigidbody>().position-_body.position);
            ConfigureSlide(intakeRail,_intakeClosedPose,intakeTravel);
            ConfigureSlide(hopperRail,_hopperClosedPose,hopperTravel);
            Scheduler.AddSubsystems(ReadControls, AimAndSpin, Feed, MoveRails);
            Scheduler.AddProtectedSystem(UpdateDisabledState);
            Scheduler.AddLateRecursive(UpdateNet);
        }

        protected override void LateInit()
        {
            base.LateInit();
            // Runtime roller cores are generated in Start, so configure collision
            // ignores only after all mechanism and roller components initialize.
            var intakeBody=intakeRail.GetComponent<Rigidbody>();
            var hopperBody=hopperRail.GetComponent<Rigidbody>();
            var colliders = System.Array.FindAll(GetComponentsInChildren<Collider>(true),
                c=>c.attachedRigidbody==_body||c.attachedRigidbody==intakeBody||c.attachedRigidbody==hopperBody);
            for (int i=0; i<colliders.Length; i++)
                for (int j=i+1; j<colliders.Length; j++)
                    if (!(colliders[i] is WheelCollider) && !(colliders[j] is WheelCollider))
                        Physics.IgnoreCollision(colliders[i], colliders[j]);
            GetComponent<RobotFramework.Controllers.Drivetrain.DriveController>().RefreshRobotRigidbodies();
        }

        private void ReadControls()
        {
            _intaking = IntakeAction.IsPressed();
            _manual = ManualShoot.IsPressed();
            _passing = AutoPass.IsPressed();
            _shooting = OuttakeAction.IsPressed() || _passing || _manual;
            _reverse = FaceDown.IsPressed();
            _agitate = RobotSpecial.IsPressed();
            if (_intaking || _shooting || _agitate) Deployed = true;
            if (StowAction.IsPressed()) Deployed = false;
            _intakeTarget = Deployed ? intakeTravel : 0f;
            _hopperTarget = Deployed ? hopperTravel : 0f;
            if (_agitate && Deployed)
                _intakeTarget -= intakeTravel * .16f * (.5f + .5f*Mathf.Sin(Time.fixedTime*12f));
        }

        private void AimAndSpin()
        {
            TargetShooterRpm = 0f;
            if (_shooting)
            {
                if (_passing || !InAllianceZone()) autoAim.SetPassMapClosest();
                else autoAim.SetDefaultMap(DefaultMap.Hub);
                var target = autoAim.GetCurrentTarget();
                Vector3 virtualTarget = target;
                if (movingShotCompensation && !_manual)
                    for (int i=0; i<5; i++)
                    {
                        var delta = virtualTarget-muzzle.position;
                        delta.y=0;
                        virtualTarget=target-_body.velocity*Interpolate(delta.magnitude,FlightDistances,FlightSeconds);
                    }
                var offset = virtualTarget-muzzle.position;
                offset.y=0;
                ShotDistance = _manual ? 3f : offset.magnitude;
                if (!_manual)
                {
                    autoAim.SetCustomTarget(virtualTarget);
                    autoAim.Aim();
                }
                float motorRps = _passing ? Mathf.Clamp(1.14f*ShotDistance*ShotDistance+.423f*ShotDistance+30f,30f,95f)
                    : Interpolate(ShotDistance,Distances,ShootRps);
                TargetShooterRpm = motorRps*60f*shooterMotorToWheelRatio;
            }
            CurrentShooterRpm = Mathf.MoveTowards(CurrentShooterRpm,TargetShooterRpm,flywheelRampRpmPerSecond*Time.fixedDeltaTime);
            SetRollers(shooterRollers,-CurrentShooterRpm);
            SetRollers(acceleratorRollers,_shooting ? -acceleratorRpm : 0f);
        }

        private void Feed()
        {
            bool spunUp = TargetShooterRpm>0f && Mathf.Abs(CurrentShooterRpm-TargetShooterRpm)<=TargetShooterRpm*feedTolerance;
            bool aligned = _manual || Mathf.Abs(autoAim.GetRotationError())<=headingToleranceDegrees;
            Feeding = Deployed && _shooting && spunUp && aligned;
            // Collision proxy for the stopped indexer's retaining contact.
            feedHold.enabled=!Feeding;
            // The keeper controls per-fuel contact pairs, so collecting new
            // balls never opens a path back out for fuel already stored.
            // Keep the transfer belt drawing inward after the pickup command
            // is released. Its open inlet otherwise becomes a downhill exit.
            bool intakeOpen=Deployed||SlideExtension(intakeRail,_intakeClosedPose)>.015f;
            float intake = intakeOpen ? -intakeRpm*((_intaking||Feeding)?1f:intakeHoldFraction) : 0f;
            float floor = Feeding ? -floorRpm : 0f;
            if (_reverse)
            {
                intake = intakeRpm*.6f;
                floor = floorRpm*.6f;
                Feeding = false;
                feedHold.enabled=false;
            }
            SetRollers(intakeRollers,intake);
            SetRollers(floorRollers,floor);
        }

        private void MoveRails()
        {
            DriveSlide(intakeRail,_intakeClosedPose,intakeTravel,_intakeTarget,ref _intakeVelocity);
            DriveSlide(hopperRail,_hopperClosedPose,hopperTravel,_hopperTarget,ref _hopperVelocity);
        }

        private void ConfigureSlide(ConfigurableJoint joint,Vector3 closed,float travel)
        {
            joint.connectedBody=_body;
            joint.autoConfigureConnectedAnchor=false;
            joint.anchor=Vector3.zero;
            // Unity's linear limit is symmetric about connectedAnchor. Centre it
            // halfway along the stroke, so the physical range is [0, travel].
            joint.connectedAnchor=closed+Vector3.forward*(travel*.5f);
            joint.axis=Vector3.right;
            joint.secondaryAxis=Vector3.up;
            joint.configuredInWorldSpace=false;
            joint.xMotion=joint.yMotion=ConfigurableJointMotion.Locked;
            joint.zMotion=ConfigurableJointMotion.Limited;
            joint.angularXMotion=joint.angularYMotion=joint.angularZMotion=ConfigurableJointMotion.Locked;
            joint.linearLimit=new SoftJointLimit{limit=travel*.5f,contactDistance=.001f,bounciness=0f};
            joint.linearLimitSpring=new SoftJointLimitSpring{spring=0f,damper=0f};
            joint.zDrive=new JointDrive{positionSpring=0f,positionDamper=600f,maximumForce=railMaxForce};
            joint.projectionMode=JointProjectionMode.PositionAndRotation;
            joint.projectionDistance=.002f;
            joint.projectionAngle=.5f;
            joint.enableCollision=false;
        }

        private float SlideExtension(ConfigurableJoint joint,Vector3 closed)
        {
            var local=Quaternion.Inverse(_body.rotation)*(joint.GetComponent<Rigidbody>().position-_body.position);
            return local.z-closed.z;
        }

        private void DriveSlide(ConfigurableJoint joint,Vector3 closed,float travel,float target,ref float velocity)
        {
            float extension=SlideExtension(joint,closed);
            float error=Mathf.Clamp(target,0f,travel)-extension;
            float brakingSpeed=Mathf.Sqrt(2f*railAcceleration*Mathf.Abs(error));
            float speedLimit=Mathf.Min(railMaxSpeed,brakingSpeed);
            float wanted=Mathf.Abs(error)<.0015f?0f:Mathf.Clamp(error*8f,-speedLimit,speedLimit);
            velocity=Mathf.MoveTowards(velocity,wanted,railAcceleration*Time.fixedDeltaTime);
            // Use metre-based native joint control. GenericJoint's versioned PID
            // and disable re-homing are unnecessary for a bounded slide.
            joint.targetVelocity=new Vector3(0f,0f,-velocity);
            joint.zDrive=new JointDrive{positionSpring=0f,positionDamper=600f,maximumForce=railMaxForce};
        }

        private void UpdateDisabledState()
        {
            if (MoSimCore.BaseClasses.GameManagement.BaseGameManager.Instance.RobotState == MoSimCore.Enums.RobotState.Disabled)
            {
                SetRollers(intakeRollers,0f);
                SetRollers(floorRollers,0f);
                SetRollers(shooterRollers,0f);
                SetRollers(acceleratorRollers,0f);
                CurrentShooterRpm=0f;
                TargetShooterRpm=0f;
                Feeding=false;
                feedHold.enabled=true;
                if(intakeHold)intakeHold.enabled=true;
                _intakeVelocity=_hopperVelocity=0f;
                intakeRail.targetVelocity=hopperRail.targetVelocity=Vector3.zero;
            }
        }

        private void UpdateNet()
        {
            // A retracting textile proxy: only the separately authored net is
            // compressed. Original CAD panels and structural meshes keep scale.
            float extension = Mathf.Clamp(SlideExtension(hopperRail,_hopperClosedPose),0f,hopperTravel);
            float span=.612f-hopperTravel+extension;
            roofNet.localScale=new Vector3(1f,1f,span/.612f);
            roofCollider.transform.localPosition=new Vector3(0f,.533f,netFixedZ+span*.5f);
            roofCollider.center=Vector3.zero;
            roofCollider.size=new Vector3(.71f,.006f,span);
            if(intakeRollerVisual&&intakeRollers.Length>0)
                intakeRollerVisual.Rotate(transform.right,intakeRollers[0].realRpm*6f*Time.deltaTime,UnityEngine.Space.World);
        }

        private static void SetRollers(InteractionRoller[] rollers,float rpm)
        {
            foreach (var roller in rollers) roller.SetRpm(rpm);
        }

        public static float Interpolate(float x,float[] xs,float[] ys)
        {
            if (x<=xs[0]) return ys[0];
            for (int i=1;i<xs.Length;i++)
                if (x<=xs[i]) return Mathf.Lerp(ys[i-1],ys[i],Mathf.InverseLerp(xs[i-1],xs[i],x));
            return ys[ys.Length-1];
        }
    }

}
