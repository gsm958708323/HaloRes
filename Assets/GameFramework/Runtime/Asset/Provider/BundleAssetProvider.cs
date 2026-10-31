using System;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Bundle;
using GameFramework.Asset.Diagnostics;
using GameFramework.Asset.Manifest;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameFramework.Asset.Provider
{
    internal sealed class BundleAssetProvider : IAssetProvider
    {
        private readonly BundleManager _bundles;
        internal BundleAssetProvider(BundleManager bundles) { _bundles = bundles; }

        public async UniTask<LoadedAsset> LoadAsync(AssetInfo info, Type type)
        {
            await _bundles.AcquireAsync(info);
            try
            {
                if (info.Kind == AssetKind.Scene)
                    return new LoadedAsset(await SceneOperations.LoadAsync(info.AssetPath,
                        () => SceneManager.LoadSceneAsync(info.AssetPath, LoadSceneMode.Additive)));
                var bundle = _bundles.GetBundle(info);
                var request = bundle.LoadAssetAsync(info.AssetPath, typeof(UnityEngine.Object));
                var asset = await request;
                if (asset == null) throw Error(info, AssetErrorCode.AssetNotInBundle, info.AssetPath);
                if (!type.IsInstanceOfType(asset)) throw Error(info, AssetErrorCode.TypeMismatch, "Expected " + type.Name + ", found " + asset.GetType().Name);
                return new LoadedAsset(asset);
            }
            catch { _bundles.Release(info); throw; }
        }

        public async UniTask UnloadAsync(AssetInfo info, LoadedAsset loaded)
        {
            if (info.Kind == AssetKind.Scene) await SceneOperations.UnloadAsync(loaded.Scene);
            _bundles.Release(info);
        }

        private static AssetException Error(AssetInfo info, AssetErrorCode code, string detail) =>
            new AssetException(new AssetError(code, info.Address, null, detail));
    }
}
