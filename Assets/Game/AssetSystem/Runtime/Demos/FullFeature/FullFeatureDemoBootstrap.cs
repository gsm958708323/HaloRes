using UnityEngine;

namespace Game.AssetSystem.Demos.FullFeature
{
    /// <summary>
    /// Scene entry point for <c>Demo_FullFeature.unity</c>.
    ///
    /// Nothing boots by itself: this component is the only switch, so other
    /// scenes stay empty and the demo never runs by accident.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FullFeatureDemoBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            FullFeatureDashboard.Spawn();
        }
    }
}
