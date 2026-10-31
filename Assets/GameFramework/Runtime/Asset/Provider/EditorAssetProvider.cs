using System;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Diagnostics;
using GameFramework.Asset.Manifest;
using UnityEngine.SceneManagement;

namespace GameFramework.Asset.Provider
{
    internal sealed class EditorAssetProvider : IAssetProvider
    {
        public async UniTask<LoadedAsset> LoadAsync(AssetInfo info, Type type)
        {
#if UNITY_EDITOR
            if (info.Kind == AssetKind.Scene)
                return new LoadedAsset(await SceneOperations.LoadAsync(info.AssetPath,
                    () => UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(info.AssetPath,
                        new LoadSceneParameters(LoadSceneMode.Additive))));
            var asset = UnityEditor.AssetDatabase.LoadMainAssetAtPath(info.AssetPath);
            if (asset == null) throw new AssetException(new AssetError(AssetErrorCode.AssetNotInBundle, info.Address, null, info.AssetPath));
            if (!type.IsInstanceOfType(asset)) throw new AssetException(new AssetError(AssetErrorCode.TypeMismatch, info.Address, null, type.Name));
            return new LoadedAsset(asset);
#else
            await UniTask.CompletedTask;
            throw new PlatformNotSupportedException("Editor provider is available only in the Unity Editor.");
#endif
        }

        public async UniTask UnloadAsync(AssetInfo info, LoadedAsset loaded)
        {
            if (info.Kind == AssetKind.Scene) await SceneOperations.UnloadAsync(loaded.Scene);
        }
    }
}
