using System.Collections.Generic;
using Games.Rebuilt.GamePieceSystem;
using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>One-way contact approximation for the intake throat and tray.
    /// Only this robot's two keeper colliders change collision pairs. Fuel is
    /// never parented, repositioned, frozen, or assigned a storage velocity.</summary>
    public sealed class Turbo4744FuelKeeper : MonoBehaviour
    {
        public Turbo4744 robot;
        public BoxCollider floor,inlet;
        private sealed class Fuel
        {
            public Collider[] colliders;
            public bool captured,ignored;
        }
        private readonly Dictionary<Rigidbody,Fuel> tracked=new Dictionary<Rigidbody,Fuel>();
        private readonly List<Rigidbody> remove=new List<Rigidbody>();
        private readonly Collider[] overlaps=new Collider[256];
        private int fuelMask;
        public bool IsCaptured(Rigidbody body)=>tracked.TryGetValue(body,out var fuel)&&fuel.captured;

        private void Awake()=>fuelMask=LayerMask.GetMask("GamePiece1","GamePiece2","GamePieceAlt");
        private void FixedUpdate()
        {
            if(!robot||!floor||!inlet)return;
            bool enabled=MoSimCore.BaseClasses.GameManagement.BaseGameManager.Instance&&
                MoSimCore.BaseClasses.GameManagement.BaseGameManager.Instance.RobotState!=MoSimCore.Enums.RobotState.Disabled;
            bool reverse=enabled&&robot.Reversing;
            bool collecting=enabled&&(robot.Collecting||robot.Feeding);
            int count=Physics.OverlapBoxNonAlloc(robot.transform.TransformPoint(new Vector3(0,.28f,.34f)),
                new Vector3(.43f,.36f,.64f),overlaps,robot.transform.rotation,fuelMask,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var body=overlaps[i].attachedRigidbody;
                if(!body||tracked.ContainsKey(body)||!body.GetComponent<RebuiltGamePieceController>())continue;
                tracked.Add(body,new Fuel{colliders=body.GetComponentsInChildren<Collider>(true)});
            }
            float top=robot.transform.InverseTransformPoint(floor.transform.TransformPoint(floor.center)).y+floor.size.y*.5f;
            float front=robot.transform.InverseTransformPoint(inlet.transform.TransformPoint(inlet.center)).z-inlet.size.z*.5f;
            remove.Clear();
            foreach(var pair in tracked)
            {
                var body=pair.Key;var state=pair.Value;
                if(!body){remove.Add(body);continue;}
                var local=robot.transform.InverseTransformPoint(body.position);
                if(Mathf.Abs(local.x)>.8f||local.z<-.4f||local.z>1.15f||local.y<-.4f||local.y>1.1f)
                {
                    Ignore(state,false);remove.Add(body);continue;
                }
                float radius=.075f;
                foreach(var collider in state.colliders)
                    if(collider is SphereCollider sphere&&!sphere.isTrigger)
                    {
                        var scale=sphere.transform.lossyScale;
                        radius=sphere.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
                        break;
                    }
                // Engage only once the whole sphere clears both keeper planes.
                // Enabling contacts here cannot push a half-entered ball upward.
                if(!reverse&&Mathf.Abs(local.x)<floor.size.x*.5f-.01f&&local.y-radius>top+.002f&&
                    local.z>radius+.02f&&local.z+radius<front-.002f)state.captured=true;
                if(reverse)state.captured=false;
                bool pass=reverse||(!state.captured&&(collecting||state.ignored));
                Ignore(state,pass);
            }
            foreach(var body in remove)tracked.Remove(body);
        }
        private void Ignore(Fuel fuel,bool ignored)
        {
            if(fuel.ignored==ignored)return;
            foreach(var collider in fuel.colliders)
            {
                if(!collider)continue;
                if(floor)Physics.IgnoreCollision(floor,collider,ignored);
                if(inlet)Physics.IgnoreCollision(inlet,collider,ignored);
            }
            fuel.ignored=ignored;
        }
        private void OnDisable()
        {
            foreach(var fuel in tracked.Values)Ignore(fuel,false);
            tracked.Clear();
        }
    }
}
