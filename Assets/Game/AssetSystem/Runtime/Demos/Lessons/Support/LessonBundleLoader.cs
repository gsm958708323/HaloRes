using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Bundles;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// Bundle loader double used by L4.4 (dependency leases) and L4.5 / L4.6
    /// (asset cache + memory policy). It counts bundle starts / creations /
    /// releases and asset loads so each scenario can assert on them.
    /// </summary>
    public sealed class LessonBundleLoader : IBundleLoader
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

        public LessonBundleLoader(int delayMilliseconds = 150)
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
                "[LessonBundleLoader] begin " + bundle.Name +
                " thread=" + Thread.CurrentThread.ManagedThreadId);

            await Task.Delay(_delayMilliseconds, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                _created[bundle.Id] = GetOrZero(_created, bundle.Id) + 1;
            }

            Debug.Log(
                "[LessonBundleLoader] created " + bundle.Name +
                " thread=" + Thread.CurrentThread.ManagedThreadId);

            return new LessonBundlePayload(this, bundle);
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

            Debug.Log("[LessonBundleLoader] released " + name);
        }

        private static int GetOrZero(
            Dictionary<BundleId, int> dictionary,
            BundleId id)
        {
            return dictionary.TryGetValue(id, out var value) ? value : 0;
        }
    }

    /// <summary>Bundle payload double returned by <see cref="LessonBundleLoader"/>.</summary>
    public sealed class LessonBundlePayload : IBundlePayload
    {
        private readonly LessonBundleLoader _loader;
        private readonly BundleInfo _bundle;
        private int _releaseCount;

        public LessonBundlePayload(
            LessonBundleLoader loader,
            BundleInfo bundle)
        {
            _loader = loader;
            _bundle = bundle;
        }

        public string DebugName
        {
            get { return _bundle.Name; }
        }

        public int ReleaseCount
        {
            get { return Volatile.Read(ref _releaseCount); }
        }

        public object LoadAsset(string internalAssetPath)
        {
            _loader.OnAssetLoaded(internalAssetPath);

            Debug.Log(
                "[LessonBundlePayload] load asset " + internalAssetPath +
                " from " + _bundle.Name);

            return new LessonAssetPayload(internalAssetPath);
        }

        public void Release()
        {
            Interlocked.Increment(ref _releaseCount);
            _loader.OnPayloadReleased(_bundle.Id, _bundle.Name);
        }
    }
}
