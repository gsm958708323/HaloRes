using System;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Integration;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// Content bundle payload double for the integration lessons (L5.1 / L5.2).
    /// The integration layer loads real bundles in production; the demo keeps
    /// the same lease/dependency semantics with an in-memory payload.
    /// </summary>
    public sealed class LessonContentPayloadFactory : IBundlePayloadFactory
    {
        private readonly int _delayMilliseconds;

        public LessonContentPayloadFactory(int delayMilliseconds = 60)
        {
            _delayMilliseconds = delayMilliseconds;
        }

        public async Task<IBundlePayload> LoadAsync(
            RuntimeBundle bundle,
            CancellationToken cancellationToken)
        {
            if (bundle == null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            Debug.Log(
                "[ContentLoader] begin " + bundle.BundleGroupName +
                " thread=" + Thread.CurrentThread.ManagedThreadId);

            if (_delayMilliseconds > 0)
            {
                await Task.Delay(_delayMilliseconds, cancellationToken);
            }
            else
            {
                await Task.Yield();
            }

            cancellationToken.ThrowIfCancellationRequested();

            Debug.Log(
                "[ContentLoader] created " + bundle.BundleGroupName +
                " thread=" + Thread.CurrentThread.ManagedThreadId);

            return new LessonContentBundlePayload(bundle);
        }
    }

    public sealed class LessonContentBundlePayload : IBundlePayload
    {
        private readonly RuntimeBundle _bundle;
        private int _releaseCount;

        public LessonContentBundlePayload(RuntimeBundle bundle)
        {
            _bundle = bundle ??
                      throw new ArgumentNullException(nameof(bundle));
        }

        public string DebugName
        {
            get { return _bundle.BundleGroupName; }
        }

        public int ReleaseCount
        {
            get { return Volatile.Read(ref _releaseCount); }
        }

        public object LoadAsset(string internalAssetPath)
        {
            Debug.Log(
                "[ContentLoader] load asset " + internalAssetPath +
                " from " + _bundle.BundleGroupName);

            return new LessonAssetPayload(internalAssetPath);
        }

        public void Release()
        {
            var count = Interlocked.Increment(ref _releaseCount);

            Debug.Log(
                "[ContentLoader] release " + _bundle.BundleGroupName +
                " count=" + count);
        }
    }
}
