using System;
using Game.AssetSystem.Integration;
using UnityEngine;

namespace Game.AssetSystem.Release
{
    public sealed class RealBundlePayload : IBundlePayload
    {
        private readonly AssetBundle _bundle;
        private int _releaseCount;
        private bool _released;

        public RealBundlePayload(AssetBundle bundle)
        {
            _bundle = bundle ??
                      throw new ArgumentNullException(nameof(bundle));
        }

        public string DebugName
        {
            get { return _bundle == null ? "<null>" : _bundle.name; }
        }

        public int ReleaseCount
        {
            get { return _releaseCount; }
        }

        public object LoadAsset(string internalAssetPath)
        {
            if (_released)
            {
                throw new ObjectDisposedException(
                    "RealBundlePayload",
                    "Payload has been released, cannot LoadAsset. bundle=" +
                    DebugName);
            }

            var loaded = _bundle.LoadAsset(internalAssetPath);

            if (loaded == null)
            {
                Debug.LogWarning(
                    "[RealBundlePayload] LoadAsset returned null. bundle=" +
                    DebugName + " path=" + internalAssetPath);
            }

            return loaded;
        }

        public void Release()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _releaseCount++;

            try
            {
                _bundle.Unload(false);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
