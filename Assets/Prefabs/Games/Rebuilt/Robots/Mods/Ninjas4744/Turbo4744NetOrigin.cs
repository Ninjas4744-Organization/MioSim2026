using UnityEngine;

namespace Ninjas4744.MoSim
{
    /// <summary>Retains NetParentFollower's world-space rendering for the local solver.</summary>
    [ExecuteAlways]
    public sealed class Turbo4744NetOrigin : MonoBehaviour
    {
        private void OnEnable() => Follow();

        private void LateUpdate() => Follow();

        private void Follow()
        {
            if (!transform.parent)
                return;
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }
    }
}
