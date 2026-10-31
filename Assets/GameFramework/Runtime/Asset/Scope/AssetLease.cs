using GameFramework.Asset.Core;
using UnityEngine;

namespace GameFramework.Asset.Scope
{
    internal sealed class AssetLease
    {
        internal AssetLease(AssetHandle handle) { Handle = handle; }
        internal readonly AssetHandle Handle;
        internal GameObject Instance;
        internal bool Returned;
        internal bool DestructionRequested;
        internal bool CreatingInstance;
        internal bool ReturnRequested;
    }
}
