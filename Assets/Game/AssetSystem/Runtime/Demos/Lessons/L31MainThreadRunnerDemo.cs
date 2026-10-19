using System;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Core;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// L3.1 — why a completion raised on a worker thread must be dispatched back
    /// to the Unity main thread.
    ///
    /// Scenario 1 is the naive version: the worker touches a Unity object and
    /// completes the task on its own thread. Scenario 2 routes the completion
    /// through <see cref="MainThreadPump"/>.
    /// </summary>
    public sealed class L31MainThreadRunnerDemo : LessonDemoBase
    {
        public override string Title
        {
            get { return "L3.1 主线程完成：朴素失败 vs Pump 派发"; }
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            Debug.Log("[Lesson] mainThreadId=" + MainThreadPump.MainThreadId);

            await RunNaiveFailureAsync(cancellationToken);
            ThrowIfCanceled(cancellationToken);
            await RunFixedAsync(cancellationToken);

            Debug.Log(
                "[Lesson] Finished. Experiment: remove the IsMainThread guard in " +
                "MainThreadAssetTask<T>.Complete and observe the completion thread error.");
        }

        private static async Task RunNaiveFailureAsync(
            CancellationToken cancellationToken)
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
                    // A naive loader may try to touch a Unity object off-thread.
                    var go = new GameObject("IllegalFromWorkerThread");

                    // If it somehow succeeds, at least destroy it on the main thread.
                    MainThreadPump.Post(() => UnityEngine.Object.Destroy(go));
                }
                catch (Exception exception)
                {
                    unityApiError = exception.GetType().Name + ": " + exception.Message;
                }

                // Naive completion runs directly on the worker thread.
                completion.TrySetResult("naive-result");
            }, cancellationToken);

            var result = await completion.Task;

            Debug.Log(
                "[Naive] workerThreadId=" + workerThreadId +
                ", result=" + result +
                ", awaitingContinuationThreadId=" + Thread.CurrentThread.ManagedThreadId +
                ", mainThreadId=" + MainThreadPump.MainThreadId);

            if (!string.IsNullOrEmpty(unityApiError))
            {
                Debug.LogWarning(
                    "[Naive] Unity API from worker failed as expected: " + unityApiError);
            }
        }

        private static async Task RunFixedAsync(
            CancellationToken cancellationToken)
        {
            Debug.Log(
                "[Fixed] Start: background work posts completion through MainThreadPump.");

            var task = new MainThreadAssetTask<string>();
            var workerThreadId = -1;

            await Task.Run(() =>
            {
                workerThreadId = Thread.CurrentThread.ManagedThreadId;

                // Simulate a file read or hash calculation.
                Thread.Sleep(100);

                // Do not touch runtime state directly here.
                // MainThreadAssetTask.Complete posts this result to the main thread.
                task.CompleteSuccess("Fx.Hit", "worker");
            }, cancellationToken);

            var result = await task;

            Debug.Log(
                "[Fixed] workerThreadId=" + workerThreadId +
                ", state=" + result.State +
                ", value=" + result.Value +
                ", completionThreadId=" + task.CompletionThreadId +
                ", mainThreadId=" + MainThreadPump.MainThreadId +
                ", isMainThread=" + MainThreadPump.IsMainThread);

            if (!MainThreadPump.IsMainThread)
            {
                Debug.LogError(
                    "[Fixed] Await continuation resumed off the Unity main thread.");
            }

            if (task.CompletionThreadId != MainThreadPump.MainThreadId)
            {
                Debug.LogError(
                    "[Fixed] Completion was delivered off the main thread. " +
                    "Check the IsMainThread guard.");
            }
        }
    }
}
