using Games.Rebuilt.GameManagement;
using UnityEngine;

namespace Ninjas4744.Benchmark
{
    // A separate temporary mod observes either unchanged robot build.
    public sealed class WeakCpuProbeModpack : RebuiltModpackSO
    {
        private void OnEnable()
        {
            if (Application.isPlaying && !WeakCpuProbe.Active)
                new GameObject("Temporary full-hopper CPU benchmark").AddComponent<WeakCpuProbe>();
        }
    }
}
