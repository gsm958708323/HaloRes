using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Core;
using Game.AssetSystem.Loading;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// L3.3 — the shared load registry.
    ///
    /// Scenario 1 proves two owners share one backend start. Scenario 2 cancels
    /// the only waiter, waits for the old operation to detach, then loads the
    /// same key again and checks that a fresh operation was created and the
    /// late success of the abandoned one was released.
    /// </summary>
    public sealed class L33SharedLoadRegistryDemo : LessonDemoBase
    {
        public override string Title
        {
            get { return "L3.3 共享加载注册表：复用 / 摘除 / 替换"; }
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            Debug.Log(
                "[Lesson3] start mainThreadId=" + MainThreadPump.MainThreadId);

            await RunReuseScenario(cancellationToken);
            ThrowIfCanceled(cancellationToken);
            await RunAbandonedAndReplaceScenario(cancellationToken);

            Debug.Log(
                "[Lesson3] Finished. " +
                "Experiment 1: skip detach on last waiter cancel. " +
                "Experiment 2: skip detach on backend completion.");
        }

        private static async Task RunReuseScenario(
            CancellationToken cancellationToken)
        {
            var backends = new List<LessonAssetLoadBackend<LessonSharedAsset>>();
            var abandonedReleaseCount = 0;

            var registry = new SharedLoadRegistry<LessonSharedAsset>(
                key =>
                {
                    var backend = new LessonAssetLoadBackend<LessonSharedAsset>(
                        200,
                        () => new LessonSharedAsset(key + ".payload"));

                    backends.Add(backend);
                    return backend;
                },
                asset =>
                {
                    abandonedReleaseCount++;
                    asset.Release("abandoned by registry reuse scenario");
                });

            var uiTask = registry.Load("Fx.Hit.Reuse", "UI.Shop");
            var battleTask = registry.Load("Fx.Hit.Reuse", "Battle.Scene");

            Debug.Log(
                "[RegistryScenario1] afterLoad created=" + registry.CreatedOperationCount +
                " reused=" + registry.ReusedOperationCount +
                " activeOps=" + registry.ActiveOperationCount +
                " backends=" + backends.Count +
                " backend0Begin=" + (backends.Count > 0 ? backends[0].BeginCount : 0));

            var uiResult = await uiTask;
            var battleResult = await battleTask;

            Debug.Log(
                "[RegistryScenario1] uiStatus=" + uiResult.Status +
                " battleStatus=" + battleResult.Status +
                " battleCompletionThreadId=" + battleTask.CompletionThreadId +
                " activeOps=" + registry.ActiveOperationCount +
                " created=" + registry.CreatedOperationCount +
                " reused=" + registry.ReusedOperationCount +
                " detached=" + registry.DetachedOperationCount +
                " abandonedOps=" + registry.AbandonedOperationCount +
                " backends=" + backends.Count +
                " backend0Begin=" + backends[0].BeginCount +
                " backend0Created=" + backends[0].CreatedCount +
                " abandonedReleases=" + abandonedReleaseCount);

            if (battleResult.IsSuccess && battleResult.Value != null)
            {
                // In the real framework this is the final AssetHandle release path.
                battleResult.Value.Release("final shared handle disposed placeholder");
            }

            await DelayAsync(50, cancellationToken);

            Debug.Log(
                "[RegistryScenario1] final activeOps=" + registry.ActiveOperationCount +
                " created=" + registry.CreatedOperationCount +
                " reused=" + registry.ReusedOperationCount +
                " detached=" + registry.DetachedOperationCount +
                " abandonedOps=" + registry.AbandonedOperationCount);
        }

        private static async Task RunAbandonedAndReplaceScenario(
            CancellationToken cancellationToken)
        {
            var backends = new List<LessonAssetLoadBackend<LessonSharedAsset>>();
            var abandonedReleaseCount = 0;
            var creationIndex = 0;

            var registry = new SharedLoadRegistry<LessonSharedAsset>(
                key =>
                {
                    var index = creationIndex++;
                    var delay = index == 0 ? 250 : 60;

                    var backend = new LessonAssetLoadBackend<LessonSharedAsset>(
                        delay,
                        () => new LessonSharedAsset(key + ".gen" + index));

                    backends.Add(backend);
                    return backend;
                },
                asset =>
                {
                    abandonedReleaseCount++;
                    asset.Release("abandoned late success");
                });

            var firstTask = registry.Load("Fx.Hit.Late", "UI.Shop");
            firstTask.Cancel();

            var firstResult = await firstTask;

            Debug.Log(
                "[RegistryScenario2] firstStatus=" + firstResult.Status +
                " activeOpsAfterCancel=" + registry.ActiveOperationCount +
                " created=" + registry.CreatedOperationCount +
                " detached=" + registry.DetachedOperationCount +
                " abandonedOps=" + registry.AbandonedOperationCount +
                " backends=" + backends.Count);

            var secondTask = registry.Load("Fx.Hit.Late", "Battle.Scene");
            var secondResult = await secondTask;

            Debug.Log(
                "[RegistryScenario2] secondStatus=" + secondResult.Status +
                " activeOpsAfterSecond=" + registry.ActiveOperationCount +
                " created=" + registry.CreatedOperationCount +
                " reused=" + registry.ReusedOperationCount +
                " detached=" + registry.DetachedOperationCount +
                " abandonedOps=" + registry.AbandonedOperationCount +
                " staleRemoved=" + registry.StaleOperationRemovedCount +
                " backends=" + backends.Count);

            if (secondResult.IsSuccess && secondResult.Value != null)
            {
                secondResult.Value.Release("battle handle disposed placeholder");
            }

            // Wait for the old operation's late success after the new operation
            // completed.
            await DelayAsync(500, cancellationToken);

            Debug.Log(
                "[RegistryScenario2] final" +
                " activeOps=" + registry.ActiveOperationCount +
                " created=" + registry.CreatedOperationCount +
                " reused=" + registry.ReusedOperationCount +
                " detached=" + registry.DetachedOperationCount +
                " abandonedOps=" + registry.AbandonedOperationCount +
                " staleRemoved=" + registry.StaleOperationRemovedCount +
                " abandonedReleases=" + abandonedReleaseCount +
                " backends=" + backends.Count);

            if (backends.Count > 0)
            {
                Debug.Log(
                    "[RegistryScenario2] backend0 begin=" + backends[0].BeginCount +
                    " created=" + backends[0].CreatedCount +
                    " ignoredCancel=" + backends[0].IgnoredCancellationCount);
            }

            if (backends.Count > 1)
            {
                Debug.Log(
                    "[RegistryScenario2] backend1 begin=" + backends[1].BeginCount +
                    " created=" + backends[1].CreatedCount +
                    " ignoredCancel=" + backends[1].IgnoredCancellationCount);
            }
        }
    }
}
