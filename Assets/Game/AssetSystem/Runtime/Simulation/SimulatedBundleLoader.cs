using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Bundles;
using UnityEngine;

namespace Game.AssetSystem.Simulation
{
    /// <summary>
    /// Bundle payload double. Counts releases so a test can assert that a
    /// bundle was unloaded exactly once.
    /// </summary>
    public sealed class SimulatedBundlePayload : IBundlePayload
    {
        private readonly SimulatedBundleLoader _loader;
        private readonly BundleId _id;
        private int _released;

        public SimulatedBundlePayload(
            SimulatedBundleLoader loader,
            BundleId id,
            string name)
        {
            _loader = loader;
            _id = id;
            DebugName = name;
        }

        public string DebugName { get; }

        public int ReleaseCount
        {
            get { return Volatile.Read(ref _released); }
        }

        public object LoadAsset(string internalAssetPath)
        {
            return DebugName + "::" + internalAssetPath;
        }

        public void Release()
        {
            if (Interlocked.Increment(ref _released) > 1)
            {
                return;
            }

            _loader.OnPayloadReleased(_id, DebugName);
        }
    }

    /// <summary>
    /// The one bundle loader double.
    ///
    /// There used to be two, one in the lessons and one in the tests, written
    /// almost line for line: same counters, same delay, same payload. Any change
    /// to the runtime invariant had to be re-verified in both. Behaviour that
    /// differs between scenarios is now a constructor argument or a field, not
    /// a new class.
    /// </summary>
    public sealed class SimulatedBundleLoader : IBundleLoader
    {
        private readonly int _delayMilliseconds;

        private readonly Dictionary<BundleId, int> _started =
            new Dictionary<BundleId, int>();

        private readonly Dictionary<BundleId, int> _created =
            new Dictionary<BundleId, int>();

        private readonly Dictionary<BundleId, int> _released =
            new Dictionary<BundleId, int>();

        private readonly Dictionary<string, int> _assetLoads =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly object _lock = new object();

        public SimulatedBundleLoader(int delayMilliseconds = 150)
        {
            _delayMilliseconds = delayMilliseconds;
        }

        public int GetStartedCount(BundleId id)
        {
            lock (_lock)
            {
                return GetOrZero(_started, id);
            }
        }

        public int GetCreatedCount(BundleId id)
        {
            lock (_lock)
            {
                return GetOrZero(_created, id);
            }
        }

        public int GetReleasedCount(BundleId id)
        {
            lock (_lock)
            {
                return GetOrZero(_released, id);
            }
        }

        public int GetAssetLoadCount(string internalAssetPath)
        {
            lock (_lock)
            {
                return _assetLoads.TryGetValue(internalAssetPath, out var value)
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
                _started[bundle.Id] = GetOrZero(_started, bundle.Id) + 1;
            }

            Debug.Log(
                "[SimulatedBundleLoader] begin " + bundle.Name +
                " thread=" + Thread.CurrentThread.ManagedThreadId);

            await Task.Delay(_delayMilliseconds, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                _created[bundle.Id] = GetOrZero(_created, bundle.Id) + 1;
            }

            Debug.Log(
                "[SimulatedBundleLoader] created " + bundle.Name +
                " thread=" + Thread.CurrentThread.ManagedThreadId);

            return new SimulatedBundlePayload(this, bundle.Id, bundle.Name);
        }

        internal void OnAssetLoaded(string internalAssetPath)
        {
            lock (_lock)
            {
                _assetLoads[internalAssetPath] =
                    _assetLoads.TryGetValue(internalAssetPath, out var value)
                        ? value + 1
                        : 1;
            }
        }

        internal void OnPayloadReleased(BundleId id, string name)
        {
            lock (_lock)
            {
                _released[id] = GetOrZero(_released, id) + 1;
            }

            Debug.Log("[SimulatedBundleLoader] released " + name);
        }

        private static int GetOrZero(
            Dictionary<BundleId, int> dictionary,
            BundleId id)
        {
            return dictionary.TryGetValue(id, out var value) ? value : 0;
        }
    }
}
