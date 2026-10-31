using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameFramework.Asset
{
    [DefaultExecutionOrder(32000)]
    public sealed class AssetSystemDriver : MonoBehaviour
    {
        public AssetSystem System { get; private set; }
        public void Bind(AssetSystem system)
        {
            if (System != null) throw new InvalidOperationException("Driver is already bound.");
            System = system ?? throw new ArgumentNullException(nameof(system));
        }

        private void LateUpdate() { System?.Tick(); }

        private void OnDestroy()
        {
            if (System != null && !System.IsClosed) System.ShutdownAsync().Forget(Debug.LogException);
        }
    }
}
