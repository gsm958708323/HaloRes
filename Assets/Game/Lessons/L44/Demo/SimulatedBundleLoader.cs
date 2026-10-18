using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Bundles.L44;
using UnityEngine;

namespace Game.AssetSystem.Demo.L44
{
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

        public void Release()
        {
            if (Interlocked.Increment(ref _released) > 1)
            {
                return;
            }

            _loader.OnPayloadReleased(_id, DebugName);
        }
    }

    public sealed class SimulatedBundleLoader : IBundleLoader
    {
        private readonly int _delayMilliseconds;

        private readonly Dictionary<BundleId, int> _started =
            new Dictionary<BundleId, int>();

        private readonly Dictionary<BundleId, int> _created =
            new Dictionary<BundleId, int>();

        private readonly Dictionary<BundleId, int> _released =
            new Dictionary<BundleId, int>();

        private readonly object _lock = new object();

        public SimulatedBundleLoader(int delayMilliseconds = 150)
        {
            _delayMilliseconds = delayMilliseconds;
        }

        public int GetStartedCount(BundleId id)
        {
            lock (_lock)
            {
                return _started.TryGetValue(id, out var value)
                    ? value
                    : 0;
            }
        }

        public int GetCreatedCount(BundleId id)
        {
            lock (_lock)
            {
                return _created.TryGetValue(id, out var value)
                    ? value
                    : 0;
            }
        }

        public int GetReleasedCount(BundleId id)
        {
            lock (_lock)
            {
                return _released.TryGetValue(id, out var value)
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
                _started[bundle.Id] =
                    GetOrZero(_started, bundle.Id) + 1;
            }

            Debug.Log(
                "[BundleLoader] begin load " +
                bundle.Name +
                " thread=" +
                Thread.CurrentThread.ManagedThreadId);

            await Task.Delay(
                _delayMilliseconds,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                _created[bundle.Id] =
                    GetOrZero(_created, bundle.Id) + 1;
            }

            Debug.Log(
                "[BundleLoader] created " +
                bundle.Name +
                " thread=" +
                Thread.CurrentThread.ManagedThreadId);

            return new SimulatedBundlePayload(
                this,
                bundle.Id,
                bundle.Name);
        }

        public void OnPayloadReleased(
            BundleId id,
            string name)
        {
            lock (_lock)
            {
                _released[id] =
                    GetOrZero(_released, id) + 1;
            }

            Debug.Log("[BundleLoader] released " + name);
        }

        private static int GetOrZero(
            Dictionary<BundleId, int> dictionary,
            BundleId id)
        {
            return dictionary.TryGetValue(id, out var value)
                ? value
                : 0;
        }
    }
}
