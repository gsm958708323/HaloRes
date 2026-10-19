using UnityEngine;

namespace Game.AssetSystem.Demos.Interactive
{
    /// <summary>
    /// Scene entry point for <c>Demo_Interactive.unity</c>.
    ///
    /// The demo never boots itself: this component is the only switch, so any
    /// other scene stays empty. It fixes what the scene tests — the clickable
    /// asset flow (load / share / release / reclaim / version switch).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractiveDemoBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            if (AssetDemoController.IsRunning)
            {
                return;
            }

            AssetDemoController.Spawn();
        }
    }
}
