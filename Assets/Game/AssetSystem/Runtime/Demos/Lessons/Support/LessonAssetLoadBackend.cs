using System;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Loading;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// Backend double for L3.2 / L3.3. It deliberately ignores cancellation so
    /// the late-success path can be exercised: the framework must suppress the
    /// result and release the abandoned payload.
    /// </summary>
    public sealed class LessonAssetLoadBackend<T> : IAssetLoadBackend<T>
    {
        private readonly int _delayMilliseconds;
        private readonly Func<T> _valueFactory;

        private int _beginCount;
        private int _createdCount;
        private int _ignoredCancellationCount;

        public LessonAssetLoadBackend(
            int delayMilliseconds,
            Func<T> valueFactory)
        {
            _delayMilliseconds = delayMilliseconds;
            _valueFactory = valueFactory ??
                            throw new ArgumentNullException(nameof(valueFactory));
        }

        public int BeginCount
        {
            get { return Volatile.Read(ref _beginCount); }
        }

        public int CreatedCount
        {
            get { return Volatile.Read(ref _createdCount); }
        }

        public int IgnoredCancellationCount
        {
            get { return Volatile.Read(ref _ignoredCancellationCount); }
        }

        public void BeginLoad(
            string key,
            CancellationToken cancellationToken,
            Action<AssetLoadBackendResult<T>> onCompleted)
        {
            Interlocked.Increment(ref _beginCount);

            Task.Run(() =>
            {
                // Simulated non-cancellable IO: sleep, then always succeed.
                Thread.Sleep(_delayMilliseconds);

                if (cancellationToken.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _ignoredCancellationCount);
                }

                var value = _valueFactory();
                Interlocked.Increment(ref _createdCount);

                onCompleted(AssetLoadBackendResult<T>.Success(value));
            });
        }
    }
}
