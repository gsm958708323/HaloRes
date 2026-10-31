using GameFramework.Asset.Scope;
using UnityEngine;

namespace GameFramework.Asset.Tests.PlayMode
{
    public sealed class DisposeScopeOnAwake : MonoBehaviour
    {
        internal static AssetScope Target;
        private void Awake() { Target?.Dispose(); }
    }
}
