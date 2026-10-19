using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Core;
using UnityEngine;

namespace Game.AssetSystem.Scheduling
{
    public sealed class AssetLoadScheduler
    {
        private readonly Queue<ScheduledLoadTaskBase>[] _pending =
        {
            new Queue<ScheduledLoadTaskBase>(),
            new Queue<ScheduledLoadTaskBase>(),
            new Queue<ScheduledLoadTaskBase>()
        };

        private int _runningCount;
        private int _frameNumber;
        private int _budgetFrame = -1;
        private int _startsThisFrame;
        private int _consecutiveHighStarts;
        private int _consecutiveNormalStarts;
        private int _startedTotal;
        private int _completedTotal;
        private int _skippedCanceledPendingTotal;
        private int _canceledRunningTotal;

        public int MaxStartsPerFrame { get; set; } = 2;

        public int MaxConcurrentLoads { get; set; } = 2;

        public int MaxConsecutiveHighStarts { get; set; } = 4;

        public int MaxConsecutiveNormalStarts { get; set; } = 8;

        public int MaxPendingScanPerFrame { get; set; } = 64;

        public int RunningCount
        {
            get { return _runningCount; }
        }

        public int PendingCount
        {
            get
            {
                var count = 0;

                for (var i = 0; i < _pending.Length; i++)
                {
                    count += _pending[i].Count;
                }

                return count;
            }
        }

        public int StartedTotal
        {
            get { return _startedTotal; }
        }

        public int CompletedTotal
        {
            get { return _completedTotal; }
        }

        public int SkippedCanceledPendingTotal
        {
            get { return _skippedCanceledPendingTotal; }
        }

        public int CanceledRunningTotal
        {
            get { return _canceledRunningTotal; }
        }

        public int LastFrameNumber
        {
            get { return _frameNumber; }
        }

        public ScheduledLoadTask<T> Enqueue<T>(
            string key,
            LoadScope scope,
            AssetLoadPriority priority,
            Func<CancellationToken, Task<T>> worker,
            Action<T> releaseAbandoned = null)
        {
            EnsureMainThread();

            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            if (worker == null)
            {
                throw new ArgumentNullException(nameof(worker));
            }

            var task = new ScheduledLoadTask<T>(
                key,
                scope,
                priority,
                worker,
                releaseAbandoned);

            scope.Track(task);

            if (!task.IsDone)
            {
                _pending[(int)priority].Enqueue(task);
            }

            return task;
        }

        public void Tick()
        {
            EnsureMainThread();

            BeginFrameIfNeeded();

            // Canceled tasks that are still sitting in a queue must not eat the
            // scan budget: otherwise a burst of canceled High tasks can starve
            // Normal/Low work for several frames. Dead entries are dequeued for
            // free, bounded by one pass over the current queue.
            var examined = 0;
            var examineCap = Math.Max(1, MaxPendingScanPerFrame) + PendingCount;

            while (_startsThisFrame < MaxStartsPerFrame &&
                   _runningCount < MaxConcurrentLoads &&
                   examined < examineCap)
            {
                if (!TryDequeueNext(out var task))
                {
                    break;
                }

                examined++;

                if (task.IsDone)
                {
                    if (task.Status == ScheduledLoadStatus.Canceled)
                    {
                        _skippedCanceledPendingTotal++;
                    }

                    continue;
                }

                if (task.Scope.IsDisposed)
                {
                    task.CancelFromScope();

                    if (task.IsDone)
                    {
                        _skippedCanceledPendingTotal++;
                    }

                    continue;
                }

                task.MarkRunning();
                _runningCount++;
                _startedTotal++;
                _startsThisFrame++;

                if (task.Priority == AssetLoadPriority.High)
                {
                    _consecutiveHighStarts++;
                    _consecutiveNormalStarts = 0;
                }
                else if (task.Priority == AssetLoadPriority.Normal)
                {
                    _consecutiveNormalStarts++;
                    _consecutiveHighStarts = 0;
                }
                else
                {
                    _consecutiveHighStarts = 0;
                    _consecutiveNormalStarts = 0;
                }

                StartWorker(task);
            }
        }

        /// <summary>
        /// The start budget is a per-Unity-frame budget, not a per-Tick() budget.
        /// Calling Tick() several times inside one frame may only spend the
        /// remaining quota of that frame.
        /// </summary>
        private void BeginFrameIfNeeded()
        {
            var frame = Time.frameCount;

            if (frame == _budgetFrame)
            {
                return;
            }

            _budgetFrame = frame;
            _startsThisFrame = 0;
            _frameNumber++;
        }

        private bool TryDequeueNext(out ScheduledLoadTaskBase task)
        {
            var maxHigh = Math.Max(1, MaxConsecutiveHighStarts);
            var maxNormal = Math.Max(1, MaxConsecutiveNormalStarts);

            if (_consecutiveHighStarts >= maxHigh)
            {
                if (TryDequeue(AssetLoadPriority.Normal, out task))
                {
                    _consecutiveHighStarts = 0;
                    return true;
                }

                if (_consecutiveNormalStarts >= maxNormal &&
                    TryDequeue(AssetLoadPriority.Low, out task))
                {
                    _consecutiveNormalStarts = 0;
                    return true;
                }

                if (TryDequeue(AssetLoadPriority.High, out task))
                {
                    return true;
                }

                if (TryDequeue(AssetLoadPriority.Low, out task))
                {
                    _consecutiveHighStarts = 0;
                    return true;
                }

                task = null;
                return false;
            }

            if (TryDequeue(AssetLoadPriority.High, out task))
            {
                return true;
            }

            if (_consecutiveNormalStarts >= maxNormal &&
                TryDequeue(AssetLoadPriority.Low, out task))
            {
                _consecutiveNormalStarts = 0;
                return true;
            }

            if (TryDequeue(AssetLoadPriority.Normal, out task))
            {
                return true;
            }

            if (TryDequeue(AssetLoadPriority.Low, out task))
            {
                _consecutiveNormalStarts = 0;
                return true;
            }

            task = null;
            return false;
        }

        private bool TryDequeue(
            AssetLoadPriority priority,
            out ScheduledLoadTaskBase task)
        {
            var queue = _pending[(int)priority];

            if (queue.Count == 0)
            {
                task = null;
                return false;
            }

            task = queue.Dequeue();
            return true;
        }

        private async void StartWorker(ScheduledLoadTaskBase task)
        {
            try
            {
                var result = await task
                    .ExecuteAsync(task.CancellationToken)
                    .ConfigureAwait(false);

                MainThreadPump.Post(
                    () => OnWorkerCompleted(task, result),
                    PumpPriority.High);
            }
            catch (OperationCanceledException)
            {
                MainThreadPump.Post(
                    () => OnWorkerCanceled(task),
                    PumpPriority.High);
            }
            catch (Exception exception)
            {
                MainThreadPump.Post(
                    () => OnWorkerFailed(task, exception.Message),
                    PumpPriority.High);
            }
        }

        private void OnWorkerCompleted(
            ScheduledLoadTaskBase task,
            object result)
        {
            EnsureMainThread();

            _runningCount = Math.Max(0, _runningCount - 1);
            _completedTotal++;

            if (task.CancelRequested)
            {
                _canceledRunningTotal++;

                // The worker already produced a value, but nobody will receive
                // it. Hand it to the abandonment hook so payloads do not leak.
                task.ReleaseAbandonedResult(result);

                task.CompleteCanceledOnMain(
                    task.CancelCode,
                    task.CancelMessage);

                return;
            }

            task.CompleteSuccessOnMain(result);
        }

        private void OnWorkerCanceled(ScheduledLoadTaskBase task)
        {
            EnsureMainThread();

            _runningCount = Math.Max(0, _runningCount - 1);
            _completedTotal++;

            if (task.CancelRequested)
            {
                _canceledRunningTotal++;

                task.CompleteCanceledOnMain(
                    task.CancelCode,
                    task.CancelMessage);

                return;
            }

            task.CompleteCanceledOnMain(
                ScheduledLoadErrorCode.Canceled,
                "Worker canceled");
        }

        private void OnWorkerFailed(
            ScheduledLoadTaskBase task,
            string message)
        {
            EnsureMainThread();

            _runningCount = Math.Max(0, _runningCount - 1);
            _completedTotal++;

            if (task.CancelRequested)
            {
                _canceledRunningTotal++;

                task.CompleteCanceledOnMain(
                    task.CancelCode,
                    task.CancelMessage);

                return;
            }

            task.CompleteFailureOnMain(message);
        }

        private static void EnsureMainThread()
        {
            if (!MainThreadPump.IsMainThread)
            {
                throw new InvalidOperationException(
                    "AssetLoadScheduler must be used on the Unity main thread.");
            }
        }
    }
}
