using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Core;
using GameFramework.Asset.Diagnostics;
using GameFramework.Asset.Manifest;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GameFramework.Asset.Scope
{
    public sealed class AssetScope : IDisposable
    {
        private readonly AssetManager _assets;
        private readonly Action<AssetScope> _onDisposed;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly List<AssetLease> _leases = new List<AssetLease>();
        private readonly List<AssetScope> _children = new List<AssetScope>();
        private readonly string _owner;
        private readonly AssetScope _parent;
        private bool _disposed;

        internal AssetScope(string name, string owner, AssetManager assets, Action<AssetScope> onDisposed, AssetScope parent = null)
        {
            Name = name;
            _owner = owner;
            _assets = assets;
            _onDisposed = onDisposed;
            _parent = parent;
        }

        public string Name { get; }
        public bool IsDisposed => _disposed;

        public AssetScope CreateChild(string name)
        {
            CheckAlive();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Scope name is required.", nameof(name));
            var child = new AssetScope(name, _owner + "/" + name + "#" + Guid.NewGuid().ToString("N"), _assets, null, this);
            _children.Add(child);
            return child;
        }

        public async UniTask<T> LoadAsync<T>(string address, CancellationToken ct = default) where T : Object
        {
            var lease = Borrow(address, typeof(T), AssetKind.Asset, ct);
            try
            {
                var loaded = await WaitAsync(lease, ct);
                if (!(loaded.Asset is T result))
                    throw new AssetException(new AssetError(AssetErrorCode.TypeMismatch, address, null, typeof(T).FullName));
                return result;
            }
            catch { Return(lease); throw; }
        }

        public async UniTask<GameObject> InstantiateAsync(string address, Transform parent = null, CancellationToken ct = default)
        {
            var lease = Borrow(address, typeof(GameObject), AssetKind.Asset, ct);
            try
            {
                var loaded = await WaitAsync(lease, ct);
                if (!(loaded.Asset is GameObject prefab))
                    throw new AssetException(new AssetError(AssetErrorCode.TypeMismatch, address, null, "Expected a prefab."));
                lease.CreatingInstance = true;
                try { lease.Instance = Object.Instantiate(prefab, parent); }
                finally { lease.CreatingInstance = false; }
                // Awake can dispose the scope before Instantiate returns its instance.
                if (_disposed || lease.ReturnRequested || ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);
                return lease.Instance;
            }
            catch { Return(lease); throw; }
        }

        public async UniTask<Scene> LoadSceneAsync(string address, CancellationToken ct = default)
        {
            var lease = Borrow(address, typeof(Scene), AssetKind.Scene, ct);
            try { return (await WaitAsync(lease, ct)).Scene; }
            catch { Return(lease); throw; }
        }

        public void Release(Object asset)
        {
            CheckAlive();
            if (ReferenceEquals(asset, null)) throw new ArgumentNullException(nameof(asset));
            foreach (var lease in _leases)
            {
                if (!lease.Returned && lease.Handle.Info.Kind == AssetKind.Asset && lease.Handle.State == AssetLoadState.Loaded
                    && ReferenceEquals(lease.Instance, null) && ReferenceEquals(lease.Handle.Result.Asset, asset))
                { Return(lease); return; }
            }
            throw new ArgumentException("This scope does not hold the asset.", nameof(asset));
        }

        public void ReleaseScene(Scene scene)
        {
            CheckAlive();
            if (!scene.IsValid()) throw new ArgumentException("Scene is invalid.", nameof(scene));
            foreach (var lease in _leases)
            {
                if (!lease.Returned && lease.Handle.State == AssetLoadState.Loaded
                    && lease.Handle.Info.Kind == AssetKind.Scene && lease.Handle.Result.Scene == scene)
                { Return(lease); return; }
            }
            throw new ArgumentException("This scope does not hold the scene.", nameof(scene));
        }

        public void ReleaseInstance(GameObject instance)
        {
            CheckAlive();
            Return(FindInstance(instance));
        }

        public void TransferInstance(GameObject instance, AssetScope target)
        {
            CheckAlive();
            if (target == null) throw new ArgumentNullException(nameof(target));
            target.CheckAlive();
            if (!ReferenceEquals(_assets, target._assets)) throw new ArgumentException("Scopes belong to different systems.", nameof(target));
            var lease = FindInstance(instance);
            if (target == this) return;
            _assets.TransferOwner(lease.Handle, _owner, target._owner);
            _leases.Remove(lease);
            target._leases.Add(lease);
        }

        private AssetLease FindInstance(GameObject instance)
        {
            if (ReferenceEquals(instance, null)) throw new ArgumentNullException(nameof(instance));
            foreach (var lease in _leases)
                if (!lease.Returned && ReferenceEquals(lease.Instance, instance)) return lease;
            throw new ArgumentException("This scope does not own the instance.", nameof(instance));
        }

        private AssetLease Borrow(string address, Type type, AssetKind kind, CancellationToken ct)
        {
            CheckAlive();
            ct.ThrowIfCancellationRequested();
            var lease = new AssetLease(_assets.Acquire(address, type, kind, _owner));
            _leases.Add(lease);
            return lease;
        }

        private async UniTask<Provider.LoadedAsset> WaitAsync(AssetLease lease, CancellationToken ct)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, ct))
            {
                var loaded = await lease.Handle.Completion.WaitAsync(linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                if (_disposed || lease.Returned) throw new OperationCanceledException(linked.Token);
                return loaded;
            }
        }

        private void Return(AssetLease lease)
        {
            if (lease.Returned) return;
            if (lease.CreatingInstance) { lease.ReturnRequested = true; return; }
            lease.Returned = true;
            _leases.Remove(lease);
            if (lease.Instance != null)
            {
                if (!lease.DestructionRequested) Object.Destroy(lease.Instance);
                // Destroy is deferred by Unity: keep assets alive until the native instance is gone.
                _assets.ReleaseAfterDestroy(lease.Handle, _owner, lease.Instance);
            }
            else _assets.Release(lease.Handle, _owner);
        }

        private void CheckAlive()
        {
            MainThreadGuard.Assert();
            if (_disposed) throw new AssetException(new AssetError(AssetErrorCode.ScopeDisposed, null, null, Name));
        }

        public void Dispose()
        {
            MainThreadGuard.Assert();
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            foreach (var child in _children.ToArray()) child.Dispose();
            _children.Clear();
            // Destroy all instances before returning any leases.
            foreach (var lease in _leases)
            {
                if (lease.Instance == null) continue;
                Object.Destroy(lease.Instance);
                lease.DestructionRequested = true;
            }
            foreach (var lease in _leases.ToArray()) Return(lease);
            _lifetime.Dispose();
            _parent?._children.Remove(this);
            _onDisposed?.Invoke(this);
        }
    }
}
