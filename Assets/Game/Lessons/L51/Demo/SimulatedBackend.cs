using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.AssetSystem.Integration.L51
{
    public sealed class SimulatedAsset : IReleasableAssetPayload
    {
        private int _releaseCount;

        public string DebugName { get; }

        public int ReleaseCount
        {
            get { return _releaseCount; }
        }

        public SimulatedAsset(string debugName)
        {
            DebugName = debugName;
        }

        public void ReleasePayload()
        {
            _releaseCount++;
            Debug.Log(
                "[SimulatedAsset] released " +
                DebugName +
                " count=" + _releaseCount);
        }
    }

    public sealed class SimulatedBundlePayload : IBundlePayload
    {
        private readonly RuntimeBundle _bundle;
        private readonly Dictionary<string, SimulatedAsset> _loaded =
            new Dictionary<string, SimulatedAsset>(StringComparer.Ordinal);

        private int _releaseCount;

        public SimulatedBundlePayload(RuntimeBundle bundle)
        {
            _bundle = bundle;
        }

        public string DebugName
        {
            get { return _bundle.FileName; }
        }

        public int ReleaseCount
        {
            get { return _releaseCount; }
        }

        public object LoadAsset(string internalAssetPath)
        {
            if (_loaded.TryGetValue(internalAssetPath, out var existing))
            {
                return existing;
            }

            Debug.Log(
                "[SimulatedBundlePayload] load " +
                internalAssetPath +
                " from " + _bundle.FileName +
                " (" + _bundle.AbsolutePath + ")");

            var created = new SimulatedAsset(
                _bundle.FileName + "::" + internalAssetPath);

            _loaded[internalAssetPath] = created;
            return created;
        }

        public void Release()
        {
            _releaseCount++;
            Debug.Log("[SimulatedBundlePayload] released " + _bundle.FileName + " count=" + _releaseCount);
        }
    }

    public sealed class SimulatedBundlePayloadFactory : IBundlePayloadFactory
    {
        private readonly int _delayMilliseconds;

        public SimulatedBundlePayloadFactory(int delayMilliseconds = 60)
        {
            _delayMilliseconds = delayMilliseconds;
        }

        public async Task<IBundlePayload> LoadAsync(
            RuntimeBundle bundle,
            CancellationToken cancellationToken)
        {
            await Task.Delay(_delayMilliseconds, cancellationToken);
            return new SimulatedBundlePayload(bundle);
        }
    }
}
