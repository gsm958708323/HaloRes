using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Core;
using Game.AssetSystem.Loading;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// L3.2 — waiter cancellation on a shared load.
    ///
    /// Scenario 1 cancels one of two waiters while the backend keeps running.
    /// Scenario 2 cancels the only waiter, then lets the backend succeed late:
    /// the operation must release the abandoned payload instead of leaking it.
    /// </summary>
    public sealed class L32CancellationDemo : LessonDemoBase
    {
        public override string Title
        {
            get { return "L3.2 等待者取消与迟到成功"; }
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            Debug.Log(
                "[Lesson2] start mainThreadId=" + MainThreadPump.MainThreadId +
                " isMainThread=" + MainThreadPump.IsMainThread);

            await RunOneWaiterCanceledScenario(cancellationToken);
            ThrowIfCanceled(cancellationToken);
            await RunAllWaitersCanceledLateSuccessScenario(cancellationToken);

            Debug.Log(
                "[Lesson2] Finished. Experiment: remove the zero-waiter release block " +
                "and observe the abandoned payload leak in scenario 2.");
        }

        private static async Task RunOneWaiterCanceledScenario(
            CancellationToken cancellationToken)
        {
            var backend = new LessonAssetLoadBackend<LessonSharedAsset>(
                250,
                () => new LessonSharedAsset("Fx.Hit.Scenario1"));

            var abandonedReleaseCount = 0;

            var operation = new SharedAssetLoadOperation<LessonSharedAsset>(
                "Fx.Hit.Scenario1",
                1001,
                backend,
                asset =>
                {
                    abandonedReleaseCount++;
                    asset.Release("abandoned by operation");
                },
                null);

            if (!operation.TryAcquireWaiter("UI.Shop", out var uiTask) ||
                !operation.TryAcquireWaiter("Battle.Scene", out var battleTask))
            {
                Debug.LogError("[Scenario1] failed to acquire waiters.");
                return;
            }

            Debug.Log(
                "[Scenario1] acquired beginCount=" + backend.BeginCount +
                " activeWaiters=" + operation.ActiveWaiterCount +
                " isMainThread=" + MainThreadPump.IsMainThread);

            await DelayAsync(50, cancellationToken);

            uiTask.Cancel();

            var uiResult = await uiTask;
            var battleResult = await battleTask;

            Debug.Log(
                "[Scenario1] uiStatus=" + uiResult.Status +
                " battleStatus=" + battleResult.Status +
                " battleCompletionThreadId=" + battleTask.CompletionThreadId +
                " mainThreadId=" + MainThreadPump.MainThreadId +
                " beginCount=" + backend.BeginCount +
                " createdCount=" + backend.CreatedCount +
                " abandonedReleaseCount=" + abandonedReleaseCount +
                " isMainThread=" + MainThreadPump.IsMainThread);

            if (battleResult.IsSuccess && battleResult.Value != null)
            {
                // In the real framework this is AssetHandle.Dispose(),
                // not business code manually releasing the asset.
                battleResult.Value.Release("battle handle disposed");
            }
        }

        private static async Task RunAllWaitersCanceledLateSuccessScenario(
            CancellationToken cancellationToken)
        {
            var backend = new LessonAssetLoadBackend<LessonSharedAsset>(
                150,
                () => new LessonSharedAsset("Fx.Hit.Late"));

            var abandonedReleaseCount = 0;

            var operation = new SharedAssetLoadOperation<LessonSharedAsset>(
                "Fx.Hit.Late",
                1002,
                backend,
                asset =>
                {
                    abandonedReleaseCount++;
                    asset.Release("abandoned late success");
                },
                null);

            if (!operation.TryAcquireWaiter("UI.Shop", out var uiTask))
            {
                Debug.LogError("[Scenario2] failed to acquire waiter.");
                return;
            }

            Debug.Log(
                "[Scenario2] acquired beginCount=" + backend.BeginCount +
                " activeWaiters=" + operation.ActiveWaiterCount);

            uiTask.Cancel();

            var uiResult = await uiTask;

            Debug.Log(
                "[Scenario2] uiStatus=" + uiResult.Status +
                " waitAfterCancel");

            // Wait long enough for the backend to finish and the main-thread
            // commit to run.
            await DelayAsync(400, cancellationToken);

            Debug.Log(
                "[Scenario2] beginCount=" + backend.BeginCount +
                " createdCount=" + backend.CreatedCount +
                " ignoredCancellation=" + backend.IgnoredCancellationCount +
                " abandonedReleaseCount=" + abandonedReleaseCount +
                " opCompleted=" + operation.IsCompleted +
                " isMainThread=" + MainThreadPump.IsMainThread);
        }
    }
}
