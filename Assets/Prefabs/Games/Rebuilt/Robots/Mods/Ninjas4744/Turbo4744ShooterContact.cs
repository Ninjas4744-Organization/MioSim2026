using System.Collections.Generic;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Parameterized shooter/indexer traction for official rigid fuel.
    /// Applies bounded forces only while fuel is within the contact. It never
    /// sets a fuel transform/velocity or applies corrections after release.</summary>
    [DefaultExecutionOrder(1100)]
    public sealed class Turbo4744ShooterContact:MonoBehaviour
    {
        public Rigidbody robotBody;
        public float wheelRadius=.05f,rampRpmPerSecond=15000f;
        public float contactNear=-.04f,contactFar=.24f,halfWidth=.09f,normalClearance=.025f;
        public float maxTractionForce=240f,tractionResponse=100f;
        public float normalStiffness=2500f,normalDamping=45f,maxNormalForce=120f;
        public bool allowReverse,requiresCommand;
        public bool compensateNormalGravity;
        public bool capturedOnly;
        public float lateralResponse=40f,maxLateralForce=50f;
        [Tooltip("Shared motor/contact force budget across all touching fuel, in newtons. Zero preserves per-contact limits.")]
        public float maxTotalForce;
        public Turbo4744FuelKeeper entryKeeper;
        public float CurrentRpm { get; private set; }
        public float TargetRpm { get; private set; }
        public int Contacts { get; private set; }
        private readonly Collider[] overlaps=new Collider[64];
        private readonly HashSet<Rigidbody> previous=new HashSet<Rigidbody>();
        private readonly HashSet<Rigidbody> current=new HashSet<Rigidbody>();
        private readonly Rigidbody[] bodies=new Rigidbody[64];
        private readonly Vector3[] forces=new Vector3[64];
        private int fuelMask;
        private void Awake(){fuelMask=LayerMask.GetMask("GamePiece1","GamePiece2","GamePieceAlt");}
        public void SetRpm(float rpm)=>TargetRpm=allowReverse?rpm:Mathf.Max(0,rpm);
        public void Stop(){TargetRpm=CurrentRpm=0;Contacts=0;previous.Clear();}
        private void FixedUpdate()
        {
            if(!BaseGameManager.Instance||BaseGameManager.Instance.RobotState==RobotState.Disabled){Stop();return;}
            if(requiresCommand&&Mathf.Approximately(TargetRpm,0)){Stop();return;}
            CurrentRpm=Mathf.MoveTowards(CurrentRpm,TargetRpm,rampRpmPerSecond*Time.fixedDeltaTime);
            current.Clear();Contacts=0;
            float center=(contactNear+contactFar)*.5f;
            int count=Physics.OverlapBoxNonAlloc(transform.position-transform.forward*center,
                new Vector3(halfWidth+.075f,normalClearance+.075f,(contactFar-contactNear)*.5f+.075f),
                overlaps,transform.rotation,fuelMask,QueryTriggerInteraction.Ignore);
            Vector3 axis=-transform.forward,normal=transform.up,right=transform.right;
            float totalForce=0;
            for(int i=0;i<count;i++)
            {
                var rb=overlaps[i].attachedRigidbody;
                if(!rb||rb.isKinematic||!rb.detectCollisions||current.Contains(rb))continue;
                if(entryKeeper&&(capturedOnly?!entryKeeper.IsCaptured(rb):TargetRpm>=0&&entryKeeper.IsCaptured(rb)))continue;
                Vector3 local=transform.InverseTransformPoint(rb.worldCenterOfMass);
                float along=-local.z;
                if(Mathf.Abs(local.x)>halfWidth||Mathf.Abs(local.y)>normalClearance||along<contactNear||along>contactFar)continue;
                current.Add(rb);Contacts++;
                if(!previous.Contains(rb))CurrentRpm*=.98f;
                Vector3 relative=rb.velocity-robotBody.GetPointVelocity(rb.worldCenterOfMass);
                float surfaceSpeed=CurrentRpm*(2*Mathf.PI/60)*wheelRadius;
                float response=Mathf.Min(tractionResponse,.8f/Time.fixedDeltaTime);
                float tangentForce=Mathf.Clamp(rb.mass*((surfaceSpeed-Vector3.Dot(relative,axis))*response-Vector3.Dot(Physics.gravity,axis)),-maxTractionForce,maxTractionForce);
                float normalForce=Mathf.Clamp(-local.y*normalStiffness-Vector3.Dot(relative,normal)*normalDamping,-maxNormalForce,maxNormalForce);
                if(compensateNormalGravity)normalForce=Mathf.Clamp(normalForce-rb.mass*Vector3.Dot(Physics.gravity,normal),-maxNormalForce,maxNormalForce);
                float lateralForce=Mathf.Clamp(-rb.mass*Vector3.Dot(relative,right)*lateralResponse,-maxLateralForce,maxLateralForce);
                float widthContact=Mathf.Clamp01((halfWidth-Mathf.Abs(local.x))/.075f);
                Vector3 force=(axis*tangentForce+normal*normalForce+right*lateralForce)*widthContact;
                bodies[Contacts-1]=rb;forces[Contacts-1]=force;totalForce+=force.magnitude;
            }
            // One intake motor cannot multiply its available force by the
            // number of balls in a dense pile. Preserve reaction forces, but
            // share the mechanism's finite budget between its contacts.
            float scale=maxTotalForce>0&&totalForce>maxTotalForce?maxTotalForce/totalForce:1;
            for(int i=0;i<Contacts;i++)
            {
                var rb=bodies[i];var force=forces[i]*scale;
                rb.AddForceAtPosition(force,rb.worldCenterOfMass,ForceMode.Force);
                robotBody.AddForceAtPosition(-force,rb.worldCenterOfMass,ForceMode.Force);
            }
            previous.Clear();foreach(var rb in current)previous.Add(rb);
        }
    }
}
