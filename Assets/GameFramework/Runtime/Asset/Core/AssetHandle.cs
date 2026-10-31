using System;
using System.Collections.Generic;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Provider;

namespace GameFramework.Asset.Core
{
    internal enum AssetLoadState { Loading, Loaded, Failed, Unloading, Disposed, UnloadFailed }

    internal sealed class AssetHandle
    {
        internal AssetHandle(AssetInfo info, Type type)
        {
            Info = info;
            RequestedType = type;
        }

        internal readonly AssetInfo Info;
        internal readonly Type RequestedType;
        internal readonly SharedCompletion<LoadedAsset> Completion = new SharedCompletion<LoadedAsset>();
        internal readonly Dictionary<string, int> Owners = new Dictionary<string, int>(StringComparer.Ordinal);
        internal AssetLoadState State = AssetLoadState.Loading;
        internal int ReferenceCount;
        internal LoadedAsset Result;
        internal Exception Error;
    }
}
