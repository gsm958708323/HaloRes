using System.Threading.Tasks;
using Game.AssetSystem.Infrastructure.L32;
using Game.AssetSystem.Loading.L32;
using UnityEngine;

namespace Game.AssetSystem.Demo.L32
{
    public sealed class CancellationLessonBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!Game.AssetSystem.Lessons.LessonBootGate.Enabled)
            {
                return;
            }

            var existing = UnityEngine.Object.FindFirstObjectByType<CancellationLessonBootstrap>();
            if (existing != null)
            {
                return;
            }

            var go = new GameObject("[CancellationLessonBootstrap]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<CancellationLessonBootstrap>();
        }

        private async void Start()
        {
            Debug.Log(
                "[Lesson2] start mainThreadId=" +
                UnityMainThreadRunner.MainThreadId +
                " isMainThread=" +
                UnityMainThreadRunner.IsMainThread);

            await RunOneWaiterCanceledScenario();
            await RunAllWaitersCanceledLateSuccessScenario();

            Debug.Log(
                "[Lesson2] Finished. Experiment: remove the zero-waiter release block " +
                "and observe the abandoned payload leak in scenario 2.");
        }

        private static async Task RunOneWaiterCanceledScenario()
        {
            var backend = new SimulatedAssetLoadBackend<SimulatedAsset>(
                250,
                () => new SimulatedAsset("Fx.Hit.Scenario1"));

            var abandonedReleaseCount = 0;

            var operation = new SharedAssetLoadOperation<SimulatedAsset>(
                "Fx.Hit.Scenario1",
                1001,
                backend,
                asset =>
                {
                    abandonedReleaseCount++;
                    asset.Release("abandoned by operation");
                });

            var uiTask = operation.AcquireWaiter("UI.Shop");
            var battleTask = operation.AcquireWaiter("Battle.Scene");

            Debug.Log(
                "[Scenario1] acquired beginCount=" + backend.BeginCount +
                " activeWaiters=" + operation.ActiveWaiterCount +
                " isMainThread=" + UnityMainThreadRunner.IsMainThread);

            await Task.Delay(50);

            uiTask.Cancel();

            var uiResult = await uiTask;
            var battleResult = await battleTask;

            Debug.Log(
                "[Scenario1] uiStatus=" + uiResult.Status +
                " battleStatus=" + battleResult.Status +
                " battleCompletionThreadId=" + battleTask.CompletionThreadId +
                " mainThreadId=" + UnityMainThreadRunner.MainThreadId +
                " beginCount=" + backend.BeginCount +
                " createdCount=" + backend.CreatedCount +
                " abandonedReleaseCount=" + abandonedReleaseCount +
                " isMainThread=" + UnityMainThreadRunner.IsMainThread);

            if (battleResult.IsSuccess && battleResult.Value != null)
            {
                // In the real framework, this is AssetHandle.Dispose(),
                // not business code manually releasing the asset.
                battleResult.Value.Release("battle handle disposed");
            }
        }

        private static async Task RunAllWaitersCanceledLateSuccessScenario()
        {
            var backend = new SimulatedAssetLoadBackend<SimulatedAsset>(
                150,
                () => new SimulatedAsset("Fx.Hit.Late"));

            var abandonedReleaseCount = 0;

            var operation = new SharedAssetLoadOperation<SimulatedAsset>(
                "Fx.Hit.Late",
                1002,
                backend,
                asset =>
                {
                    abandonedReleaseCount++;
                    asset.Release("abandoned late success");
                });

            var uiTask = operation.AcquireWaiter("UI.Shop");

            Debug.Log(
                "[Scenario2] acquired beginCount=" + backend.BeginCount +
                " activeWaiters=" + operation.ActiveWaiterCount);

            uiTask.Cancel();

            var uiResult = await uiTask;

            Debug.Log(
                "[Scenario2] uiStatus=" + uiResult.Status +
                " waitAfterCancel");

            // Wait long enough for the backend to finish and the main-thread commit to run.
            await Task.Delay(400);

            Debug.Log(
                "[Scenario2] beginCount=" + backend.BeginCount +
                " createdCount=" + backend.CreatedCount +
                " ignoredCancellation=" + backend.IgnoredCancellationCount +
                " abandonedReleaseCount=" + abandonedReleaseCount +
                " opCompleted=" + operation.IsCompleted +
                " isMainThread=" + UnityMainThreadRunner.IsMainThread);
        }
    }
}
