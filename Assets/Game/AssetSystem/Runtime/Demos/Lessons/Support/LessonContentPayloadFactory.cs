using System;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Bundles;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// Content bundle payload double for the integration lessons (L5.1 / L5.2).
    /// The integration layer loads real bundles in production; the demo keeps
    /// the same lease/dependency semantics with an in-memory payload.
    ///
    /// It implements the same <see cref="IBundleLoader"/> seam as
    /// <c>RealBundleLoader</c>, so a lesson exercises the real runtime and only
    /// swaps the byte source.
    /// </summary>
    public sealed class LessonContentPayloadFactory : IBundleLoader
    {
        private readonly int _delayMilliseconds;

        public LessonContentPayloadFactory(int delayMilliseconds = 60)
        {
            _delayMilliseconds = delayMilliseconds;
        }

        public async Task<IBundlePayload> LoadAsync(
            BundleInfo bundle,
            CancellationToken cancellationToken)
        {
            if (bundle == null)
            {
                throw new ArgumentNullException(nameof(bundle));
            }

            Debug.Log(
                "[ContentLoader] begin " + bundle.Name +
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
                "[ContentLoader] created " + bundle.Name +
                " thread=" + Thread.CurrentThread.ManagedThreadId);

            return new LessonContentBundlePayload(bundle);
        }
    }

    public sealed class LessonContentBundlePayload : IBundlePayload
    {
        private readonly BundleInfo _bundle;
        private int _releaseCount;

        public LessonContentBundlePayload(BundleInfo bundle)
        {
            _bundle = bundle ??
                      throw new ArgumentNullException(nameof(bundle));
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
            Debug.Log(
                "[ContentLoader] load asset " + internalAssetPath +
                " from " + _bundle.Name);

            return new LessonAssetPayload(internalAssetPath);
        }

        public void Release()
        {
            var count = Interlocked.Increment(ref _releaseCount);

            Debug.Log(
                "[ContentLoader] release " + _bundle.Name +
                " count=" + count);
        }
    }
}
