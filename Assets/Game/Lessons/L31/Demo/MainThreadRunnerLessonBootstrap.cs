using System;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Infrastructure.L31;
using UnityEngine;

namespace Game.AssetSystem.Demo.L31
{
    public sealed class MainThreadRunnerLessonBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!Game.AssetSystem.Lessons.LessonBootGate.Enabled)
            {
                return;
            }

            var existing = UnityEngine.Object.FindFirstObjectByType<MainThreadRunnerLessonBootstrap>();
            if (existing != null)
            {
                return;
            }

            var go = new GameObject("MainThreadRunnerLessonBootstrap");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<MainThreadRunnerLessonBootstrap>();
        }

        private async void Start()
        {
            Debug.Log("[Lesson] mainThreadId=" + UnityMainThreadRunner.MainThreadId);
            await RunNaiveFailureAsync();
            await RunFixedAsync();

            Debug.Log(
                "[Lesson] Finished. Experiment: remove the IsMainThread guard in MainThreadAssetTask<T>.Complete " +
                "and observe the completion thread error.");
        }

        private static async Task RunNaiveFailureAsync()
        {
            Debug.Log("[Naive] Start: background completion without dispatcher.");

            var completion = new TaskCompletionSource<string>();
            var workerThreadId = -1;
            string unityApiError = null;

            await Task.Run(() =>
            {
                workerThreadId = Thread.CurrentThread.ManagedThreadId;

                try
                {
                    // A naive loader may try to touch a Unity object from a worker thread.
                    var go = new GameObject("IllegalFromWorkerThread");

                    // If it somehow succeeds, at least destroy it on the main thread.
                    UnityMainThreadRunner.Post(() => UnityEngine.Object.Destroy(go));
                }
                catch (Exception exception)
                {
                    unityApiError = exception.GetType().Name + ": " + exception.Message;
                }

                // Naive completion runs directly on the worker thread.
                completion.TrySetResult("naive-result");
            });

            var result = await completion.Task;

            Debug.Log(
                "[Naive] workerThreadId=" + workerThreadId +
                ", result=" + result +
                ", awaitingContinuationThreadId=" + Thread.CurrentThread.ManagedThreadId +
                ", mainThreadId=" + UnityMainThreadRunner.MainThreadId);

            if (!string.IsNullOrEmpty(unityApiError))
            {
                Debug.LogWarning(
                    "[Naive] Unity API from worker failed as expected: " + unityApiError);
            }
        }

        private static async Task RunFixedAsync()
        {
            Debug.Log("[Fixed] Start: background work posts completion through UnityMainThreadRunner.");

            var task = new MainThreadAssetTask<string>();
            var workerThreadId = -1;

            Task.Run(() =>
            {
                workerThreadId = Thread.CurrentThread.ManagedThreadId;

                // Simulate a file read or hash calculation.
                Thread.Sleep(100);

                // Do not touch runtime state directly here.
                // MainThreadAssetTask.Complete posts this result to the main thread.
                task.CompleteSuccess("Fx.Hit", "worker");
            });

            var result = await task;

            Debug.Log(
                "[Fixed] workerThreadId=" + workerThreadId +
                ", state=" + result.State +
                ", value=" + result.Value +
                ", completionThreadId=" + task.CompletionThreadId +
                ", mainThreadId=" + UnityMainThreadRunner.MainThreadId +
                ", isMainThread=" + UnityMainThreadRunner.IsMainThread);

            if (!UnityMainThreadRunner.IsMainThread)
            {
                Debug.LogError("[Fixed] Await continuation resumed off the Unity main thread.");
            }

            if (task.CompletionThreadId != UnityMainThreadRunner.MainThreadId)
            {
                Debug.LogError(
                    "[Fixed] Completion was delivered off the main thread. " +
                    "Check the IsMainThread guard.");
            }
        }
    }
}
