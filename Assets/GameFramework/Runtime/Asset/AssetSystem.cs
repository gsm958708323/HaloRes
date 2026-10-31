using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Bundle;
using GameFramework.Asset.Core;
using GameFramework.Asset.Diagnostics;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Provider;
using GameFramework.Asset.Scope;
using UnityEngine;

namespace GameFramework.Asset
{
    public enum AssetSystemMode { Editor, Bundle }

    public sealed class AssetSystem : IDisposable
    {
        private readonly AssetManager _assets;
        private readonly BundleManager _bundles;
        private readonly HashSet<AssetScope> _scopes = new HashSet<AssetScope>();
        private bool _disposed;
        private bool _closed;

        private AssetSystem(AssetManifest manifest, IAssetProvider provider, BundleManager bundles)
        {
            MainThreadGuard.Capture();
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _bundles = bundles;
            _assets = new AssetManager(manifest, provider ?? throw new ArgumentNullException(nameof(provider)));
            Tracker = new AssetTracker(this);
        }

        public AssetManifest Manifest { get; }
        public AssetTracker Tracker { get; }
        public bool IsDisposed => _disposed;
        public bool IsIdle => _assets.IsIdle && (_bundles == null || _bundles.IsIdle);
        public bool IsClosed => _closed;

        public static AssetSystem Initialize(AssetSystemMode mode, AssetManifest manifest, AssetPathResolver paths = null)
        {
            MainThreadGuard.Capture();
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (mode == AssetSystemMode.Editor) return new AssetSystem(manifest, new EditorAssetProvider(), null);
            if (mode != AssetSystemMode.Bundle) throw new ArgumentOutOfRangeException(nameof(mode));
            var bundles = new BundleManager(manifest, paths ?? new AssetPathResolver(
                System.IO.Path.Combine(Application.persistentDataPath, "GameFrameworkAsset"),
                System.IO.Path.Combine(Application.streamingAssetsPath, "GameFrameworkAsset")));
            return new AssetSystem(manifest, new BundleAssetProvider(bundles), bundles);
        }

        public static AssetSystem WithProvider(AssetManifest manifest, IAssetProvider provider)
        {
            return new AssetSystem(manifest, provider, null);
        }

        public AssetScope CreateScope(string name)
        {
            MainThreadGuard.Assert();
            if (_disposed) throw new ObjectDisposedException(nameof(AssetSystem));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Scope name is required.", nameof(name));
            var scope = new AssetScope(name, name + "#" + Guid.NewGuid().ToString("N"), _assets, item => _scopes.Remove(item));
            _scopes.Add(scope);
            return scope;
        }

        public void Tick()
        {
            MainThreadGuard.Assert();
            _assets.ReleaseDestroyedInstances();
            _assets.RetryFailedUnloads();
            _assets.ProcessPendingUnloads();
            _bundles?.ProcessPendingUnloads();
            if (_disposed && IsIdle) _closed = true;
        }

        internal AssetSystemSnapshot Capture()
        {
            MainThreadGuard.Assert();
            return new AssetSystemSnapshot(_assets.Capture(), _bundles?.Capture() ?? Array.Empty<BundleSnapshot>());
        }

        public async UniTask ShutdownAsync(CancellationToken ct = default)
        {
            Dispose();
            while (!_closed)
            {
                ct.ThrowIfCancellationRequested();
                Tick();
                if (!_closed) await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, ct);
            }
        }

        public void Dispose()
        {
            MainThreadGuard.Assert();
            if (_disposed) return;
            _disposed = true;
            foreach (var scope in new List<AssetScope>(_scopes)) scope.Dispose();
            Tick();
        }
    }
}
