using System;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Loading.L33;

namespace Game.AssetSystem.Demo.L33
{
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
                // Simulate a non-cancellable file IO or third-party plugin.
                // Deliberately ignore cancellation to test late-success suppression.
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
