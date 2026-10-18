using System.Collections.Generic;
using System.Threading.Tasks;
using Game.AssetSystem.Loading.L33;
using UnityEngine;

namespace Game.AssetSystem.Demo.L33
{
    public sealed class SharedLoadRegistryLessonBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!Game.AssetSystem.Lessons.LessonBootGate.Enabled)
            {
                return;
            }

            var existing =
                UnityEngine.Object.FindFirstObjectByType<SharedLoadRegistryLessonBootstrap>();

            if (existing != null)
            {
                return;
            }

            var go = new GameObject("[SharedLoadRegistryLessonBootstrap]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<SharedLoadRegistryLessonBootstrap>();
        }

        private async void Start()
        {
            Debug.Log(
                "[Lesson3] start mainThreadId=" +
                Game.AssetSystem.Infrastructure.L33.UnityMainThreadRunner.MainThreadId);

            await RunReuseScenario();
            await RunAbandonedAndReplaceScenario();

            Debug.Log(
                "[Lesson3] Finished. " +
                "Experiment 1: skip detach on last waiter cancel. " +
                "Experiment 2: skip detach on backend completion.");
        }

        private static async Task RunReuseScenario()
        {
            var backends = new List<SimulatedAssetLoadBackend<SimulatedAsset>>();
            var abandonedReleaseCount = 0;

            var registry = new SharedLoadRegistry<SimulatedAsset>(
                key =>
                {
                    var backend = new SimulatedAssetLoadBackend<SimulatedAsset>(
                        200,
                        () => new SimulatedAsset(key + ".payload"));

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
                // This demo only logs one final release for the shared simulated payload.
                battleResult.Value.Release("final shared handle disposed placeholder");
            }

            await Task.Delay(50);

            Debug.Log(
                "[RegistryScenario1] final activeOps=" + registry.ActiveOperationCount +
                " created=" + registry.CreatedOperationCount +
                " reused=" + registry.ReusedOperationCount +
                " detached=" + registry.DetachedOperationCount +
                " abandonedOps=" + registry.AbandonedOperationCount);
        }

        private static async Task RunAbandonedAndReplaceScenario()
        {
            var backends = new List<SimulatedAssetLoadBackend<SimulatedAsset>>();
            var abandonedReleaseCount = 0;
            var creationIndex = 0;

            var registry = new SharedLoadRegistry<SimulatedAsset>(
                key =>
                {
                    var index = creationIndex++;
                    var delay = index == 0 ? 250 : 60;

                    var backend = new SimulatedAssetLoadBackend<SimulatedAsset>(
                        delay,
                        () => new SimulatedAsset(key + ".gen" + index));

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

            // Wait for the old operation's late success after the new operation completed.
            await Task.Delay(500);

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
