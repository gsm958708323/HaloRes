using System;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Manifest;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameFramework.Asset.Provider
{
    public readonly struct LoadedAsset
    {
        public LoadedAsset(UnityEngine.Object asset)
        {
            Asset = asset;
            Scene = default;
        }

        public LoadedAsset(Scene scene)
        {
            Asset = null;
            Scene = scene;
        }

        public UnityEngine.Object Asset { get; }
        public Scene Scene { get; }
    }

    // A successful load owns exactly one unload. A failed load must roll itself back.
    public interface IAssetProvider
    {
        UniTask<LoadedAsset> LoadAsync(AssetInfo info, Type type);
        UniTask UnloadAsync(AssetInfo info, LoadedAsset loaded);
    }
}
