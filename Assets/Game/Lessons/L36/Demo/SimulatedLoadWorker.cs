using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.AssetSystem.Demo.L36
{
    public sealed class SimulatedPayload
    {
        public SimulatedPayload(string key)
        {
            Key = key;
        }

        public string Key { get; }

        public override string ToString()
        {
            return "Payload(" + Key + ")";
        }
    }

    public sealed class SimulatedLoadWorker
    {
        private int _startedCount;

        public int StartedCount
        {
            get { return Volatile.Read(ref _startedCount); }
        }

        public Task<SimulatedPayload> LoadAsync(
            string key,
            int delayMilliseconds,
            bool honorCancellation,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _startedCount);

            Debug.Log(
                "[WorkerStart] key=" + key +
                " thread=" + Thread.CurrentThread.ManagedThreadId +
                " frame=" + Time.frameCount);

            return Task.Run(() =>
            {
                var elapsed = 0;
                const int stepMilliseconds = 20;

                while (elapsed < delayMilliseconds)
                {
                    if (honorCancellation &&
                        cancellationToken.IsCancellationRequested)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    var sleep = System.Math.Min(
                        stepMilliseconds,
                        delayMilliseconds - elapsed);

                    if (sleep > 0)
                    {
                        Thread.Sleep(sleep);
                        elapsed += sleep;
                    }
                }

                if (honorCancellation &&
                    cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return new SimulatedPayload(key);
            });
        }
    }
}
