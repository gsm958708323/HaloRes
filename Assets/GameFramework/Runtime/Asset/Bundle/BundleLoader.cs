using System;
using GameFramework.Asset.Core;
using GameFramework.Asset.Manifest;
using UnityEngine;

namespace GameFramework.Asset.Bundle
{
    internal enum BundleLoadState { Loading, Loaded, Failed, Disposed }

    internal sealed class BundleLoader
    {
        internal BundleLoader(int index, BundleInfo info) { Index = index; Info = info; }
        internal readonly int Index;
        internal readonly BundleInfo Info;
        internal readonly SharedCompletion<AssetBundle> Completion = new SharedCompletion<AssetBundle>();
        internal BundleLoadState State = BundleLoadState.Loading;
        internal int ReferenceCount;
        internal AssetBundle Bundle;
        internal Exception Error;
    }
}
