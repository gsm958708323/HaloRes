using System;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Loading;

namespace Game.AssetSystem.Simulation
{
    /// <summary>
    /// Asset backend double for the loading lessons and the shared-load tests.
    ///
    /// It deliberately ignores cancellation: the point is to exercise the
    /// framework's late-success suppression, where a worker that cannot be
    /// cancelled still must not hand a value to a canceled waiter. The lesson
    /// and the test used to keep separate, identical copies of this class.
    /// </summary>
    public sealed class SimulatedAssetLoadBackend<T> : IAssetLoadBackend<T>
    {
        private readonly int _delayMilliseconds;
        private readonly Func<T> _valueFactory;

        private int _beginCount;
        private int _createdCount;
        private int _ignoredCancellationCount;

        public SimulatedAssetLoadBackend(
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
