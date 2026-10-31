using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameFramework.Asset.Provider
{
    internal static class SceneOperations
    {
        internal static async UniTask<Scene> LoadAsync(string path, Func<AsyncOperation> start)
        {
            var loaded = new UniTaskCompletionSource<Scene>();
            void OnLoaded(Scene scene, LoadSceneMode mode)
            {
                if (string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase)) loaded.TrySetResult(scene);
            }
            SceneManager.sceneLoaded += OnLoaded;
            try
            {
                var operation = start();
                if (operation == null) throw Failure(path, "Unity refused the additive scene load.");
                await operation;
                if (loaded.Task.Status != UniTaskStatus.Succeeded) throw Failure(path, "Scene load completed without a matching sceneLoaded event.");
                return await loaded.Task;
            }
            finally { SceneManager.sceneLoaded -= OnLoaded; }
        }

        internal static async UniTask UnloadAsync(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            // Unity cannot unload the last scene. Retain the bundle debt until a bootstrap scene exists.
            if (SceneManager.sceneCount <= 1) throw Failure(scene.path, "Keep a bootstrap scene loaded before releasing a content scene.");
            var operation = SceneManager.UnloadSceneAsync(scene);
            if (operation == null) throw Failure(scene.path, "Unity refused scene unload; bundle references are retained.");
            await operation;
            if (scene.IsValid() && scene.isLoaded) throw Failure(scene.path, "Scene is still loaded after unload completed.");
        }

        private static AssetException Failure(string path, string detail) =>
            new AssetException(new AssetError(AssetErrorCode.SceneLoadFailed, path, null, detail));
    }
}
