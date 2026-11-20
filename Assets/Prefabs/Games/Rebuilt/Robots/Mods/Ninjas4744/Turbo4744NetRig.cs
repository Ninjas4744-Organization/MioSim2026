using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Border lacing follows the telescoping frame; free interior nodes
    /// fold and collide through MoSim's NetSolver. No textile mesh scaling.</summary>
    [DefaultExecutionOrder(-20)]
    public sealed class Turbo4744NetRig:MonoBehaviour
    {
        public Transform rearLeft,rearRight,frontLeft,frontRight;
        public Transform[] leftPins,rightPins;
        private void FixedUpdate()=>UpdatePins();
        public void UpdatePins()
        {
            for(int i=0;i<leftPins.Length;i++)
            {
                float t=(float)i/(leftPins.Length-1);
                leftPins[i].position=Vector3.Lerp(rearLeft.position,frontLeft.position,t);
                rightPins[i].position=Vector3.Lerp(rearRight.position,frontRight.position,t);
            }
        }
    }
}
