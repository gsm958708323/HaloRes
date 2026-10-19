using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Core;
using Game.AssetSystem.Scheduling;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// L3.6 — the load scheduler.
    ///
    /// Scenario 1 mixes Low / Normal / High tasks and checks the start budget.
    /// Scenario 2 disposes the scope while tasks are pending / running.
    /// Scenario 3 floods the main-thread pump and checks the per-frame budget.
    /// </summary>
    public sealed class L36SchedulerDemo : LessonDemoBase
    {
        private AssetLoadScheduler _scheduler;

        public override string Title
        {
            get { return "L3.6 调度器：优先级 / 帧预算 / 作用域取消"; }
        }

        public override void Tick()
        {
            _scheduler?.Tick();
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            Debug.Log(
                "[Lesson6] start mainThreadId=" + MainThreadPump.MainThreadId +
                " frame=" + Time.frameCount);

            _scheduler = new AssetLoadScheduler();

            await RunPriorityAndBudgetScenario(cancellationToken);
            ThrowIfCanceled(cancellationToken);
            await RunCancelPendingScenario(cancellationToken);
            ThrowIfCanceled(cancellationToken);
            await RunPumpBudgetScenario(cancellationToken);

            Debug.Log(
                "[Lesson6] Finished. " +
                "Experiment 1: set MaxStartsPerFrame=10 to observe the startup burst. " +
                "Experiment 2: skip the Scope.IsDisposed check to observe canceled tasks starting.");
        }

        private async Task RunPriorityAndBudgetScenario(
            CancellationToken cancellationToken)
        {
            _scheduler.MaxStartsPerFrame = 2;
            _scheduler.MaxConcurrentLoads = 2;
            _scheduler.MaxConsecutiveHighStarts = 3;
            _scheduler.MaxConsecutiveNormalStarts = 6;

            var worker = new LessonLoadWorker();
            var scope = new LoadScope("PriorityScope");

            var low1 = _scheduler.Enqueue(
                "low_1",
                scope,
                AssetLoadPriority.Low,
                ct => worker.LoadAsync("low_1", 40, true, ct));

            var low2 = _scheduler.Enqueue(
                "low_2",
                scope,
                AssetLoadPriority.Low,
                ct => worker.LoadAsync("low_2", 40, true, ct));

            var normal1 = _scheduler.Enqueue(
                "normal_1",
                scope,
                AssetLoadPriority.Normal,
                ct => worker.LoadAsync("normal_1", 60, true, ct));

            var normal2 = _scheduler.Enqueue(
                "normal_2",
                scope,
                AssetLoadPriority.Normal,
                ct => worker.LoadAsync("normal_2", 60, true, ct));

            var high1 = _scheduler.Enqueue(
                "high_1",
                scope,
                AssetLoadPriority.High,
                ct => worker.LoadAsync("high_1", 80, true, ct));

            var high2 = _scheduler.Enqueue(
                "high_2",
                scope,
                AssetLoadPriority.High,
                ct => worker.LoadAsync("high_2", 80, true, ct));

            Debug.Log(
                "[Scenario1] enqueued activePending=" + _scheduler.PendingCount +
                " frame=" + Time.frameCount);

            var results = await Task.WhenAll(
                low1.CompletionTask,
                low2.CompletionTask,
                normal1.CompletionTask,
                normal2.CompletionTask,
                high1.CompletionTask,
                high2.CompletionTask);

            for (var i = 0; i < results.Length; i++)
            {
                var result = results[i];

                Debug.Log(
                    "[Scenario1] key=" + result.Key +
                    " priority=" + result.Priority +
                    " status=" + result.Status +
                    " value=" + result.Value +
                    " error=" + result.Error);
            }

            Debug.Log(
                "[Scenario1] summary started=" + _scheduler.StartedTotal +
                " completed=" + _scheduler.CompletedTotal +
                " running=" + _scheduler.RunningCount +
                " pending=" + _scheduler.PendingCount +
                " workerStarted=" + worker.StartedCount +
                " frame=" + Time.frameCount);

            scope.Dispose();

            await Task.Yield();
        }

        private async Task RunCancelPendingScenario(
            CancellationToken cancellationToken)
        {
            _scheduler.MaxStartsPerFrame = 1;
            _scheduler.MaxConcurrentLoads = 1;

            var worker = new LessonLoadWorker();
            var scope = new LoadScope("CancelScope");
            var tasks = new List<ScheduledLoadTask<LessonPayload>>();

            for (var i = 0; i < 6; i++)
            {
                var index = i;
                var key = "cancel_" + index;

                tasks.Add(_scheduler.Enqueue(
                    key,
                    scope,
                    AssetLoadPriority.Normal,
                    ct => worker.LoadAsync(key, 120, true, ct)));
            }

            Debug.Log(
                "[Scenario2] enqueued count=" + tasks.Count +
                " pending=" + _scheduler.PendingCount +
                " frame=" + Time.frameCount);

            await DelayAsync(50, cancellationToken);

            Debug.Log(
                "[Scenario2] before dispose running=" + _scheduler.RunningCount +
                " pending=" + _scheduler.PendingCount +
                " workerStarted=" + worker.StartedCount +
                " frame=" + Time.frameCount);

            scope.Dispose();

            var completionTasks =
                new Task<ScheduledLoadResult<LessonPayload>>[tasks.Count];

            for (var i = 0; i < tasks.Count; i++)
            {
                completionTasks[i] = tasks[i].CompletionTask;
            }

            var results = await Task.WhenAll(completionTasks);

            var canceled = 0;
            var succeeded = 0;
            var failed = 0;

            for (var i = 0; i < results.Length; i++)
            {
                var result = results[i];

                if (result.Status == ScheduledLoadStatus.Canceled)
                {
                    canceled++;
                }
                else if (result.Status == ScheduledLoadStatus.Succeeded)
                {
                    succeeded++;
                }
                else if (result.Status == ScheduledLoadStatus.Failed)
                {
                    failed++;
                }

                Debug.Log(
                    "[Scenario2] key=" + result.Key +
                    " status=" + result.Status +
                    " error=" + result.Error);
            }

            Debug.Log(
                "[Scenario2] summary canceled=" + canceled +
                " succeeded=" + succeeded +
                " failed=" + failed +
                " workerStarted=" + worker.StartedCount +
                " running=" + _scheduler.RunningCount +
                " pending=" + _scheduler.PendingCount +
                " canceledRunningTotal=" + _scheduler.CanceledRunningTotal +
                " skippedCanceledPending=" + _scheduler.SkippedCanceledPendingTotal +
                " frame=" + Time.frameCount);
        }

        private async Task RunPumpBudgetScenario(
            CancellationToken cancellationToken)
        {
            var pump = MainThreadPump.Instance;

            if (pump == null)
            {
                Debug.LogWarning(
                    "[Scenario3] MainThreadPump instance is not available.");
                return;
            }

            pump.MaxActionsPerFrame = 3;
            pump.MaxMillisecondsPerFrame = 5.0f;

            await Task.Run(() =>
            {
                for (var i = 0; i < 9; i++)
                {
                    var index = i;

                    MainThreadPump.Post(
                        () => Debug.Log(
                            "[PumpAction] index=" + index +
                            " frame=" + Time.frameCount),
                        PumpPriority.Normal);
                }
            }, cancellationToken);

            await DelayAsync(300, cancellationToken);

            Debug.Log(
                "[Scenario3] summary lastFrameProcessed=" +
                pump.LastFrameProcessed +
                " pending=" + pump.PendingCount +
                " frame=" + Time.frameCount);
        }
    }
}
