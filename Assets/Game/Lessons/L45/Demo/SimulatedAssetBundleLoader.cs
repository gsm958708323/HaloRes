using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Assets.L45;
using Game.AssetSystem.Bundles.L45;
using UnityEngine;

namespace Game.AssetSystem.Demo.L45
{
    public sealed class SimulatedAsset : IAssetPayload
    {
        private int _releaseCount;

        public SimulatedAsset(string debugName)
        {
            DebugName = debugName;
        }

        public string DebugName { get; }

        public int ReleaseCount
        {
            get { return _releaseCount; }
        }

        public void Release()
        {
            _releaseCount++;

            Debug.Log(
                "[SimulatedAsset] released " +
                DebugName +
                " count=" +
                _releaseCount);
        }
    }

    public sealed class SimulatedBundlePayload : IBundlePayload
    {
        private readonly SimulatedAssetBundleLoader _loader;
        private readonly BundleInfo _bundle;
        private int _assetLoadCount;
        private int _releaseCount;

        public SimulatedBundlePayload(
            SimulatedAssetBundleLoader loader,
            BundleInfo bundle)
        {
            _loader = loader;
            _bundle = bundle;
        }

        public string DebugName
        {
            get { return _bundle.Name; }
        }

        public int AssetLoadCount
        {
            get { return _assetLoadCount; }
        }

        public int ReleaseCount
        {
            get { return _releaseCount; }
        }

        public object LoadAsset(string internalAssetPath)
        {
            _assetLoadCount++;
            _loader.OnAssetLoaded(internalAssetPath);

            Debug.Log(
                "[BundlePayload] load asset " +
                internalAssetPath +
                " from " +
                _bundle.Name);

            return new SimulatedAsset(internalAssetPath);
        }

        public void Release()
        {
            _releaseCount++;
            _loader.OnBundlePayloadReleased(_bundle.Id, _bundle.Name);

            Debug.Log("[BundlePayload] released " + _bundle.Name);
        }
    }

    public sealed class SimulatedAssetBundleLoader : IBundleLoader
    {
        private readonly int _delayMilliseconds;

        private readonly Dictionary<BundleId, int> _bundleStarted =
            new Dictionary<BundleId, int>();

        private readonly Dictionary<BundleId, int> _bundleCreated =
            new Dictionary<BundleId, int>();

        private readonly Dictionary<BundleId, int> _bundleReleased =
            new Dictionary<BundleId, int>();

        private readonly Dictionary<string, int> _assetLoads =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly object _lock = new object();

        public SimulatedAssetBundleLoader(int delayMilliseconds = 150)
        {
            _delayMilliseconds = delayMilliseconds;
        }

        public int GetBundleStartedCount(BundleId id)
        {
            lock (_lock)
            {
                return _bundleStarted.TryGetValue(id, out var value)
                    ? value
                    : 0;
            }
        }

        public int GetBundleCreatedCount(BundleId id)
        {
            lock (_lock)
            {
                return _bundleCreated.TryGetValue(id, out var value)
                    ? value
                    : 0;
            }
        }

        public int GetBundleReleasedCount(BundleId id)
        {
            lock (_lock)
            {
                return _bundleReleased.TryGetValue(id, out var value)
                    ? value
                    : 0;
            }
        }

        public int GetAssetLoadCount(string internalAssetPath)
        {
            lock (_lock)
            {
                return _assetLoads.TryGetValue(
                    internalAssetPath,
                    out var value)
                    ? value
                    : 0;
            }
        }

        public async Task<IBundlePayload> LoadAsync(
            BundleInfo bundle,
            CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                _bundleStarted[bundle.Id] =
                    GetOrZero(_bundleStarted, bundle.Id) + 1;
            }

            Debug.Log(
                "[BundleLoader] begin " +
                bundle.Name +
                " thread=" +
                Thread.CurrentThread.ManagedThreadId);

            await Task.Delay(_delayMilliseconds, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                _bundleCreated[bundle.Id] =
                    GetOrZero(_bundleCreated, bundle.Id) + 1;
            }

            Debug.Log(
                "[BundleLoader] created " +
                bundle.Name +
                " thread=" +
                Thread.CurrentThread.ManagedThreadId);

            return new SimulatedBundlePayload(this, bundle);
        }

        internal void OnAssetLoaded(string internalAssetPath)
        {
            lock (_lock)
            {
                _assetLoads[internalAssetPath] =
                    GetOrZero(_assetLoads, internalAssetPath) + 1;
            }
        }

        internal void OnBundlePayloadReleased(
            BundleId id,
            string name)
        {
            lock (_lock)
            {
                _bundleReleased[id] =
                    GetOrZero(_bundleReleased, id) + 1;
            }
        }

        private static int GetOrZero(
            Dictionary<BundleId, int> dictionary,
            BundleId id)
        {
            return dictionary.TryGetValue(id, out var value)
                ? value
                : 0;
        }

        private static int GetOrZero(
            Dictionary<string, int> dictionary,
            string key)
        {
            return dictionary.TryGetValue(key, out var value)
                ? value
                : 0;
        }
    }
}
