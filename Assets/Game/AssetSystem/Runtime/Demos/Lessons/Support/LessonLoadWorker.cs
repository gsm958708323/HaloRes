using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>Value produced by <see cref="LessonLoadWorker"/>.</summary>
    public sealed class LessonPayload
    {
        public LessonPayload(string key)
        {
            Key = key;
        }

        public string Key { get; }

        public override string ToString()
        {
            return "Payload(" + Key + ")";
        }
    }

    /// <summary>
    /// Background worker double for L3.6. It sleeps in small steps and honours
    /// the scheduler's cancellation token, so the "cancel pending / cancel
    /// running" paths are both observable.
    /// </summary>
    public sealed class LessonLoadWorker
    {
        private int _startedCount;

        public int StartedCount
        {
            get { return Volatile.Read(ref _startedCount); }
        }

        public Task<LessonPayload> LoadAsync(
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

                return new LessonPayload(key);
            });
        }
    }
}
