using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Core;
using GameFramework.Asset.Diagnostics;
using GameFramework.Asset.Manifest;
using UnityEngine;

namespace GameFramework.Asset.Bundle
{
    internal sealed class BundleManager
    {
        private readonly AssetManifest _manifest;
        private readonly AssetPathResolver _paths;
        private readonly Dictionary<int, BundleLoader> _loaders = new Dictionary<int, BundleLoader>();
        private readonly HashSet<BundleLoader> _pending = new HashSet<BundleLoader>();

        internal BundleManager(AssetManifest manifest, AssetPathResolver paths) { _manifest = manifest; _paths = paths; }
        internal bool IsIdle => _loaders.Count == 0;

        internal async UniTask AcquireAsync(AssetInfo info)
        {
            MainThreadGuard.Assert();
            var indices = Indices(info);
            var acquired = new List<BundleLoader>();
            try
            {
                foreach (var index in indices)
                {
                    var created = false;
                    if (!_loaders.TryGetValue(index, out var loader))
                    {
                        loader = new BundleLoader(index, _manifest.GetBundle(index));
                        _loaders.Add(index, loader);
                        created = true;
                    }
                    loader.ReferenceCount++;
                    acquired.Add(loader);
                    if (created) LoadAsync(loader).Forget();
                }
                // Report the first failed member while the remaining non-cancellable IO completes.
                await UniTask.WhenAll(acquired.Select(loader => loader.Completion.Task.AsUniTask()));
            }
            catch (Exception error)
            {
                foreach (var loader in acquired) Release(loader);
                if (error is AssetException assetError)
                    throw new AssetException(new AssetError(assetError.Error.Code, info.Address, assetError.Error.Bundle, assetError.Error.Detail));
                throw new AssetException(new AssetError(AssetErrorCode.BundleLoadFailed, info.Address, _manifest.GetBundle(info.BundleIndex).Name, error.Message));
            }
        }

        internal void Release(AssetInfo info)
        {
            foreach (var index in Indices(info)) Release(_loaders[index]);
        }

        private void Release(BundleLoader loader)
        {
            if (loader.ReferenceCount <= 0) throw new InvalidOperationException("Unbalanced bundle release: " + loader.Info.Name);
            loader.ReferenceCount--;
            if (loader.ReferenceCount == 0 && loader.State != BundleLoadState.Loading) _pending.Add(loader);
        }

        internal AssetBundle GetBundle(AssetInfo info) => _loaders[info.BundleIndex].Bundle;

        private IEnumerable<int> Indices(AssetInfo info)
        {
            yield return info.BundleIndex;
            foreach (var dependency in _manifest.GetBundle(info.BundleIndex).Dependencies) yield return dependency;
        }

        private async UniTask LoadAsync(BundleLoader loader)
        {
            try
            {
                var path = await _paths.ResolveVerifiedAsync(loader.Info);
                var bundle = await AssetBundle.LoadFromFileAsync(path);
                if (bundle == null)
                    throw new AssetException(new AssetError(AssetErrorCode.BundleLoadFailed, null, loader.Info.Name, path + " SHA-256=" + loader.Info.Hash));
                loader.Bundle = bundle;
                loader.State = BundleLoadState.Loaded;
                loader.Completion.SetResult(bundle);
            }
            catch (Exception error)
            {
                loader.Error = error;
                loader.State = BundleLoadState.Failed;
                loader.Completion.SetException(error);
            }
            if (loader.ReferenceCount == 0) _pending.Add(loader);
        }

        internal void ProcessPendingUnloads()
        {
            if (_pending.Count == 0) return;
            // Dependents first for DAGs. Cycles have no live owners and are released as a set.
            var pending = _pending.OrderByDescending(loader => loader.Info.Dependencies.Count).ToArray();
            _pending.Clear();
            foreach (var loader in pending)
            {
                if (loader.ReferenceCount > 0 || loader.State == BundleLoadState.Loading) continue;
                if (loader.Bundle != null) loader.Bundle.Unload(true);
                loader.State = BundleLoadState.Disposed;
                _loaders.Remove(loader.Index);
            }
        }

        internal IReadOnlyList<BundleSnapshot> Capture()
        {
            var snapshots = new List<BundleSnapshot>();
            foreach (var loader in _loaders.Values)
                snapshots.Add(new BundleSnapshot(loader.Info.Name, loader.State.ToString(), loader.ReferenceCount,
                    loader.Info.Size, loader.Info.Hash, loader.Error?.Message ?? string.Empty));
            return snapshots.AsReadOnly();
        }
    }
}
