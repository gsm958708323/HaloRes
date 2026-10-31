using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Diagnostics;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Provider;
using UnityEngine;

namespace GameFramework.Asset.Core
{
    internal sealed class AssetManager
    {
        private readonly AssetManifest _manifest;
        private readonly IAssetProvider _provider;
        private readonly Dictionary<string, AssetHandle> _assets = new Dictionary<string, AssetHandle>(StringComparer.Ordinal);
        private readonly HashSet<AssetHandle> _pending = new HashSet<AssetHandle>();
        private readonly HashSet<AssetHandle> _unloading = new HashSet<AssetHandle>();
        private readonly List<(AssetHandle handle, string owner, GameObject instance)> _destroying = new List<(AssetHandle, string, GameObject)>();

        internal AssetManager(AssetManifest manifest, IAssetProvider provider)
        {
            _manifest = manifest;
            _provider = provider;
        }

        internal bool IsIdle => _assets.Count == 0 && _unloading.Count == 0 && _destroying.Count == 0;

        internal void ReleaseAfterDestroy(AssetHandle handle, string owner, GameObject instance)
        {
            _destroying.Add((handle, owner, instance));
        }

        internal void ReleaseDestroyedInstances()
        {
            for (var i = _destroying.Count - 1; i >= 0; i--)
            {
                var item = _destroying[i];
                if (item.instance != null) continue;
                _destroying.RemoveAt(i);
                Release(item.handle, item.owner);
            }
        }

        internal AssetHandle Acquire(string address, Type type, AssetKind kind, string owner)
        {
            MainThreadGuard.Assert();
            if (address == null || !_manifest.TryGetAsset(address, out var info))
                throw new AssetException(new AssetError(AssetErrorCode.AddressNotFound, address, null, "Address is absent from the manifest."));
            if (info.Kind != kind)
                throw new AssetException(new AssetError(AssetErrorCode.TypeMismatch, address, null, "Use LoadSceneAsync for a scene address."));

            var startLoad = false;
            if (!_assets.TryGetValue(address, out var handle))
            {
                handle = new AssetHandle(info, type);
                _assets.Add(address, handle);
                startLoad = true;
            }
            else if (kind == AssetKind.Asset && !type.IsAssignableFrom(handle.RequestedType))
            {
                throw new AssetException(new AssetError(AssetErrorCode.TypeMismatch, address, null,
                    "Address is already loading as " + handle.RequestedType.Name + "; requested " + type.Name));
            }

            handle.ReferenceCount++;
            handle.Owners.TryGetValue(owner, out var count);
            handle.Owners[owner] = count + 1;
            if (startLoad) LoadAsync(handle).Forget();
            return handle;
        }

        internal void Release(AssetHandle handle, string owner)
        {
            MainThreadGuard.Assert();
            if (handle.ReferenceCount <= 0 || !handle.Owners.TryGetValue(owner, out var count))
                throw new InvalidOperationException("Unbalanced asset lease: " + handle.Info.Address);
            handle.ReferenceCount--;
            if (count == 1) handle.Owners.Remove(owner);
            else handle.Owners[owner] = count - 1;
            if (handle.ReferenceCount == 0 && handle.State != AssetLoadState.Loading) _pending.Add(handle);
        }

        internal void TransferOwner(AssetHandle handle, string previous, string next)
        {
            MainThreadGuard.Assert();
            var previousCount = handle.Owners[previous];
            if (previousCount == 1) handle.Owners.Remove(previous);
            else handle.Owners[previous] = previousCount - 1;
            handle.Owners.TryGetValue(next, out var nextCount);
            handle.Owners[next] = nextCount + 1;
        }

        private async UniTask LoadAsync(AssetHandle handle)
        {
            try
            {
                handle.Result = await _provider.LoadAsync(handle.Info, handle.RequestedType);
                handle.State = AssetLoadState.Loaded;
                handle.Completion.SetResult(handle.Result);
            }
            catch (Exception error)
            {
                handle.Error = error;
                handle.State = AssetLoadState.Failed;
                handle.Completion.SetException(error);
            }
            if (handle.ReferenceCount == 0) _pending.Add(handle);
        }

        internal void ProcessPendingUnloads()
        {
            MainThreadGuard.Assert();
            if (_pending.Count == 0) return;
            var pending = new List<AssetHandle>(_pending);
            _pending.Clear();
            foreach (var handle in pending)
            {
                if (handle.ReferenceCount > 0 || handle.State == AssetLoadState.Loading) continue;
                _assets.Remove(handle.Info.Address);
                if (handle.State == AssetLoadState.Failed)
                {
                    handle.State = AssetLoadState.Disposed;
                    continue;
                }
                handle.State = AssetLoadState.Unloading;
                _unloading.Add(handle);
                UnloadAsync(handle).Forget();
            }
        }

        private async UniTask UnloadAsync(AssetHandle handle)
        {
            try
            {
                await _provider.UnloadAsync(handle.Info, handle.Result);
                handle.State = AssetLoadState.Disposed;
                _unloading.Remove(handle);
            }
            catch (Exception error)
            {
                // Keep the debt observable and retry next tick. A scene must outlive its bundles.
                handle.Error = error;
                handle.State = AssetLoadState.UnloadFailed;
            }
        }

        internal void RetryFailedUnloads()
        {
            if (_unloading.Count == 0) return;
            foreach (var handle in new List<AssetHandle>(_unloading))
            {
                if (handle.State != AssetLoadState.UnloadFailed) continue;
                handle.State = AssetLoadState.Unloading;
                UnloadAsync(handle).Forget();
            }
        }

        internal IReadOnlyList<AssetSnapshot> Capture()
        {
            var snapshots = new List<AssetSnapshot>();
            foreach (var handle in _assets.Values) snapshots.Add(Capture(handle));
            foreach (var handle in _unloading) snapshots.Add(Capture(handle));
            return snapshots.AsReadOnly();
        }

        private static AssetSnapshot Capture(AssetHandle handle)
        {
            var owners = new List<string>();
            foreach (var owner in handle.Owners) owners.Add(owner.Key + " x" + owner.Value);
            return new AssetSnapshot(handle.Info.Address, handle.State.ToString(), handle.ReferenceCount,
                owners.AsReadOnly(), handle.Error?.Message ?? string.Empty);
        }
    }
}
