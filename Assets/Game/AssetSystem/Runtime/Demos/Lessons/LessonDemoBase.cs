using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// Common plumbing for the restored lesson demos: title, cancellation and
    /// a no-op frame tick.
    /// </summary>
    public abstract class LessonDemoBase : ILessonDemo
    {
        public abstract string Title { get; }

        public abstract Task RunAsync(CancellationToken cancellationToken);

        public virtual void Tick()
        {
        }

        /// <summary>
        /// Throws when the demo was canceled, so the bootstrap can report a
        /// single "canceled" line instead of a per-scenario error.
        /// </summary>
        protected static void ThrowIfCanceled(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        protected static async Task DelayAsync(
            int milliseconds,
            CancellationToken cancellationToken)
        {
            if (milliseconds <= 0)
            {
                await Task.Yield();
                ThrowIfCanceled(cancellationToken);
                return;
            }

            await Task.Delay(milliseconds, cancellationToken);
        }

        protected static void Log(string message)
        {
            Debug.Log(message);
        }

        protected static void LogWarning(string message)
        {
            Debug.LogWarning(message);
        }

        protected static void LogError(string message)
        {
            Debug.LogError(message);
        }
    }
}
