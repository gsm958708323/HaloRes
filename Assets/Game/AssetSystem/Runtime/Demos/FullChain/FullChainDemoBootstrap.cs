using UnityEngine;

namespace Game.AssetSystem.FullChain
{
    /// <summary>
    /// Scene entry point for <c>Demo_FullChain.unity</c>.
    ///
    /// The runner never boots itself: this component is the only switch, so any
    /// other scene stays empty. It fixes what the scene tests — the end-to-end
    /// chain build -> activate -> load -> reclaim, printed as [FullChain][A]..[G].
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FullChainDemoBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            if (FullChainScenarioDemo.IsRunning)
            {
                return;
            }

            FullChainScenarioDemo.Spawn();
        }
    }
}
