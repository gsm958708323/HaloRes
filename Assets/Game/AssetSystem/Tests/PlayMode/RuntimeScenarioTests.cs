using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Bundles = Game.AssetSystem.Bundles;
using Core = Game.AssetSystem.Core;
using Loading = Game.AssetSystem.Loading;
using Scheduling = Game.AssetSystem.Scheduling;
using Support = Game.AssetSystem.Tests.Support;

using Game.AssetSystem.Core;

namespace Game.AssetSystem.Tests
{
    /// <summary>
    /// PlayMode tests that turn the document's scenario walkthroughs
    /// (正常路径 / 失败路径 / 竞态) into executable assertions.
    /// </summary>
    public sealed class RuntimeScenarioTests
    {

        private static IEnumerator WaitUntil(
            Func<bool> condition,
            string message,
            float timeoutSeconds = 15f)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;

            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("Timeout waiting for: " + message);
                }

                yield return null;
            }
        }

        // ------------------------------------------------------------------
        // 3.1 主线程亲和性：后台完成必须被派发回主线程
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L31_WorkerCompletionIsDeliveredOnMainThread()
        {
            var task = new Core.MainThreadAssetTask<string>();

            Task.Run(() => task.CompleteSuccess("Fx.Hit", "worker"));

            yield return WaitUntil(() => task.IsDone, "main thread task completion");

            Assert.AreEqual(
                Core.MainThreadPump.MainThreadId,
                task.CompletionThreadId,
                "completion must be delivered on the Unity main thread");

            Assert.IsTrue(task.Result.IsSuccess, "task should succeed");
            Assert.AreEqual("Fx.Hit", task.Result.Value);
            Assert.IsTrue(
                Core.MainThreadPump.IsMainThread,
                "await continuation must resume on the main thread");
        }

        // ------------------------------------------------------------------
        // 3.2 场景 1：一个 waiter 取消，另一个仍然成功，底层只加载一次
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L32_OneWaiterCanceled_OtherWaiterStillSucceeds()
        {
            var backend = new Support.SimulatedAssetLoadBackend<Support.SimulatedAsset>(
                120,
                () => new Support.SimulatedAsset("Fx.Hit.Scenario1"));

            var abandonedReleases = 0;

            var operation = new Loading.SharedAssetLoadOperation<Support.SimulatedAsset>(
                "Fx.Hit.Scenario1",
                1001,
                backend,
                asset =>
                {
                    abandonedReleases++;
                    asset.Release("abandoned by operation");
                },
                (op, reason) => { });

            Assert.IsTrue(
                operation.TryAcquireWaiter("UI.Shop", out var uiTask),
                "first waiter must be accepted");

            Assert.IsTrue(
                operation.TryAcquireWaiter("Battle.Scene", out var battleTask),
                "second waiter must be accepted");

            Assert.AreEqual(1, backend.BeginCount, "backend must start once");
            Assert.AreEqual(2, operation.ActiveWaiterCount);

            uiTask.Cancel();

            yield return WaitUntil(
                () => uiTask.IsDone && battleTask.IsDone,
                "canceled + shared completion");

            Assert.AreEqual(
                Loading.AssetLoadStatus.Canceled,
                uiTask.Result.Status,
                "canceled waiter must report Canceled");

            Assert.AreEqual(
                Loading.AssetLoadStatus.Succeeded,
                battleTask.Result.Status,
                "surviving waiter must still succeed");

            Assert.AreEqual(
                MainThreadIdOfPump(),
                battleTask.CompletionThreadId,
                "completion must arrive on the main thread");

            Assert.AreEqual(1, backend.BeginCount, "backend must not restart");
            Assert.AreEqual(1, backend.CreatedCount, "payload created once");
            Assert.AreEqual(0, abandonedReleases, "no abandoned payload");
        }

        // ------------------------------------------------------------------
        // 3.2 场景 2：所有 waiter 取消后底层迟到成功，payload 必须被释放一次
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L32_AllWaitersCanceled_LateSuccessIsAbandoned()
        {
            var backend = new Support.SimulatedAssetLoadBackend<Support.SimulatedAsset>(
                120,
                () => new Support.SimulatedAsset("Fx.Hit.Late"));

            var abandonedReleases = 0;

            var operation = new Loading.SharedAssetLoadOperation<Support.SimulatedAsset>(
                "Fx.Hit.Late",
                1002,
                backend,
                asset =>
                {
                    abandonedReleases++;
                    asset.Release("abandoned late success");
                },
                (op, reason) => { });

            Assert.IsTrue(
                operation.TryAcquireWaiter("UI.Shop", out var uiTask),
                "waiter must be accepted");

            uiTask.Cancel();

            yield return WaitUntil(() => uiTask.IsDone, "canceled waiter");

            Assert.AreEqual(Loading.AssetLoadStatus.Canceled, uiTask.Result.Status);

            yield return WaitUntil(
                () => abandonedReleases > 0,
                "late success to be released as abandoned");

            Assert.AreEqual(
                1,
                abandonedReleases,
                "late success must release the payload exactly once");

            Assert.AreEqual(1, backend.CreatedCount);
            Assert.AreEqual(0, operation.ActiveWaiterCount);
        }

        // ------------------------------------------------------------------
        // 3.3 场景 1：两个 waiter 复用同一 operation，只创建一次 backend
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L33_TwoWaitersReuseOneOperation()
        {
            var backends = new List<Support.SimulatedAssetLoadBackend<Support.SimulatedAsset>>();

            var registry = new Loading.SharedLoadRegistry<Support.SimulatedAsset>(
                key =>
                {
                    var backend = new Support.SimulatedAssetLoadBackend<Support.SimulatedAsset>(
                        120,
                        () => new Support.SimulatedAsset(key + ".payload"));
                    backends.Add(backend);
                    return backend;
                },
                asset => asset.Release("abandoned by registry"));

            var uiTask = registry.Load("Fx.Hit.Reuse", "UI.Shop");
            var battleTask = registry.Load("Fx.Hit.Reuse", "Battle.Scene");

            Assert.AreEqual(1, registry.CreatedOperationCount, "one operation created");
            Assert.AreEqual(1, registry.ReusedOperationCount, "second waiter reused it");
            Assert.AreEqual(1, backends.Count, "one backend");

            yield return WaitUntil(
                () => uiTask.IsDone && battleTask.IsDone,
                "both waiters complete");

            Assert.AreEqual(Loading.AssetLoadStatus.Succeeded, uiTask.Result.Status);
            Assert.AreEqual(Loading.AssetLoadStatus.Succeeded, battleTask.Result.Status);
            Assert.AreEqual(1, backends[0].BeginCount, "backend started once");
            Assert.AreEqual(1, backends[0].CreatedCount, "payload created once");

            yield return null;

            Assert.AreEqual(
                0,
                registry.ActiveOperationCount,
                "completed operation must be detached from the registry");
        }

        // ------------------------------------------------------------------
        // 3.3 场景 2：最后一个 waiter 取消后，新请求必须创建新 operation
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L33_LastWaiterCanceled_NextLoadCreatesNewOperation()
        {
            var backends = new List<Support.SimulatedAssetLoadBackend<Support.SimulatedAsset>>();

            var registry = new Loading.SharedLoadRegistry<Support.SimulatedAsset>(
                key =>
                {
                    var backend = new Support.SimulatedAssetLoadBackend<Support.SimulatedAsset>(
                        120,
                        () => new Support.SimulatedAsset(key + ".payload"));
                    backends.Add(backend);
                    return backend;
                },
                asset => asset.Release("abandoned by registry"));

            var first = registry.Load("Fx.Hit.Replace", "UI.Shop");
            first.Cancel();

            yield return WaitUntil(() => first.IsDone, "first waiter canceled");

            Assert.AreEqual(0, registry.ActiveOperationCount, "operation detached on cancel");

            var second = registry.Load("Fx.Hit.Replace", "Battle.Scene");

            Assert.AreEqual(2, registry.CreatedOperationCount, "a new operation was created");
            Assert.AreEqual(0, registry.ReusedOperationCount, "no reuse after detach");

            yield return WaitUntil(() => second.IsDone, "second load completes");

            Assert.AreEqual(Loading.AssetLoadStatus.Succeeded, second.Result.Status);
        }

        // ------------------------------------------------------------------
        // 3.6 场景 1：优先级 + 双预算（每帧启动数、并发数）
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L36_HighPriorityStartsFirst_WithinBudget()
        {
            var scheduler = new Scheduling.AssetLoadScheduler
            {
                MaxStartsPerFrame = 2,
                MaxConcurrentLoads = 2,
                MaxConsecutiveHighStarts = 3,
                MaxConsecutiveNormalStarts = 6
            };

            var scope = new Scheduling.LoadScope("SchedulerBudgetScope");

            var startOrder = new List<string>();
            var startsPerFrame = new Dictionary<int, int>();

            Func<string, CancellationToken, Task<string>> worker = (key, token) =>
            {
                startOrder.Add(key);

                var frame = Time.frameCount;
                startsPerFrame.TryGetValue(frame, out var count);
                startsPerFrame[frame] = count + 1;

                return Task.Delay(30, token).ContinueWith(_ => key, TaskScheduler.Default);
            };

            var low1 = scheduler.Enqueue("low_1", scope, Scheduling.AssetLoadPriority.Low, ct => worker("low_1", ct));
            var low2 = scheduler.Enqueue("low_2", scope, Scheduling.AssetLoadPriority.Low, ct => worker("low_2", ct));
            var normal1 = scheduler.Enqueue("normal_1", scope, Scheduling.AssetLoadPriority.Normal, ct => worker("normal_1", ct));
            var normal2 = scheduler.Enqueue("normal_2", scope, Scheduling.AssetLoadPriority.Normal, ct => worker("normal_2", ct));
            var high1 = scheduler.Enqueue("high_1", scope, Scheduling.AssetLoadPriority.High, ct => worker("high_1", ct));
            var high2 = scheduler.Enqueue("high_2", scope, Scheduling.AssetLoadPriority.High, ct => worker("high_2", ct));

            Assert.AreEqual(6, scheduler.PendingCount);

            while (scheduler.PendingCount > 0 || scheduler.RunningCount > 0)
            {
                scheduler.Tick();

                Assert.LessOrEqual(
                    scheduler.RunningCount,
                    2,
                    "MaxConcurrentLoads must be respected");

                yield return null;
            }

            yield return WaitUntil(
                () => low1.IsDone && low2.IsDone && normal1.IsDone &&
                      normal2.IsDone && high1.IsDone && high2.IsDone,
                "all scheduled loads complete");

            Assert.AreEqual(6, scheduler.StartedTotal);
            Assert.AreEqual(6, scheduler.CompletedTotal);

            Assert.AreEqual("high_1", startOrder[0], "high priority starts first");
            Assert.AreEqual("high_2", startOrder[1], "high priority starts first");

            foreach (var pair in startsPerFrame)
            {
                Assert.LessOrEqual(
                    pair.Value,
                    2,
                    "frame " + pair.Key + " started more than MaxStartsPerFrame");
            }
        }

        // ------------------------------------------------------------------
        // 3.6 场景 3：worker 已经产出结果后才取消，结果必须被释放而不是丢掉
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L36_CancelAfterWorkerFinished_ReleasesAbandonedResult()
        {
            var scheduler = new Scheduling.AssetLoadScheduler
            {
                MaxStartsPerFrame = 1,
                MaxConcurrentLoads = 1
            };

            var scope = new Scheduling.LoadScope("SchedulerAbandonScope");
            var abandoned = new List<string>();

            var task = scheduler.Enqueue(
                "abandon_1",
                scope,
                Scheduling.AssetLoadPriority.High,
                ct => Task.FromResult("payload"),
                value => abandoned.Add(value));

            scheduler.Tick();

            // The worker already returned; cancel before the main-thread pump
            // gets to process the completion.
            task.Cancel();

            yield return WaitUntil(() => task.IsDone, "canceled task settles");

            Assert.AreEqual(
                Scheduling.ScheduledLoadStatus.Canceled,
                task.Status,
                "task must end canceled");

            Assert.AreEqual(1, abandoned.Count, "abandoned result must be released");
            Assert.AreEqual("payload", abandoned[0]);

            var result = task.CompletionTask.Result;
            Assert.AreEqual(
                Scheduling.ScheduledLoadStatus.Canceled,
                result.Status);
            Assert.IsNull(result.Value, "a canceled task never hands out the value");

            scope.Dispose();
        }

        // ------------------------------------------------------------------
        // 3.6 场景 2：Scope 释放时取消排队任务，运行中的任务收尾
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L36_ScopeDisposeCancelsQueuedLoads()
        {
            var scheduler = new Scheduling.AssetLoadScheduler
            {
                MaxStartsPerFrame = 1,
                MaxConcurrentLoads = 1,
                MaxConsecutiveHighStarts = 4,
                MaxConsecutiveNormalStarts = 4
            };

            var scope = new Scheduling.LoadScope("SchedulerCancelScope");
            var started = 0;

            Func<CancellationToken, Task<string>> worker = token =>
            {
                Interlocked.Increment(ref started);
                return Task.Delay(120, token).ContinueWith(_ => "done", TaskScheduler.Default);
            };

            var tasks = new List<Scheduling.ScheduledLoadTaskBase>();

            for (var i = 0; i < 6; i++)
            {
                tasks.Add(scheduler.Enqueue(
                    "cancel_" + i,
                    scope,
                    Scheduling.AssetLoadPriority.Normal,
                    worker));
            }

            scheduler.Tick();
            yield return null;

            Assert.AreEqual(1, started, "only one worker started before dispose");

            scope.Dispose();

            while (scheduler.PendingCount > 0 || scheduler.RunningCount > 0)
            {
                scheduler.Tick();
                yield return null;
            }

            yield return WaitUntil(
                () => AllDone(tasks),
                "all canceled tasks settle");

            Assert.AreEqual(6, scheduler.CanceledRunningTotal + scheduler.SkippedCanceledPendingTotal);
            Assert.AreEqual(1, started, "queued tasks must never reach the worker");
            Assert.AreEqual(0, scheduler.RunningCount);
            Assert.AreEqual(0, scheduler.PendingCount);

            for (var i = 0; i < tasks.Count; i++)
            {
                Assert.AreEqual(
                    Scheduling.ScheduledLoadStatus.Canceled,
                    tasks[i].Status,
                    "task " + tasks[i].Key + " should end canceled");
            }
        }

        // ------------------------------------------------------------------
        // 4.4 场景 1：共享 Bundle、引用计数与依赖租约
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L44_SharedBundleRefCountAndDependencyLeases()
        {
            var audio = new Bundles.BundleId(0);
            var fx = new Bundles.BundleId(1);
            var shop = new Bundles.BundleId(2);

            var catalog = new Bundles.BundleCatalog(
                new[]
                {
                    new Bundles.BundleInfo
                    {
                        Id = audio,
                        Name = "battle_audio",
                        DirectDependencies = Array.Empty<Bundles.BundleId>()
                    },
                    new Bundles.BundleInfo
                    {
                        Id = fx,
                        Name = "battle_fx",
                        DirectDependencies = new[] { audio }
                    },
                    new Bundles.BundleInfo
                    {
                        Id = shop,
                        Name = "ui_shop",
                        DirectDependencies = new[] { fx }
                    }
                });

            var loader = new Support.SimulatedBundleLoader(80);
            var runtime = new Bundles.BundleRuntime(catalog, loader);

            var first = runtime.AcquireAsync(shop, "UI.Shop.1");
            var second = runtime.AcquireAsync(shop, "UI.Shop.2");

            yield return WaitUntil(
                () => first.IsDone && second.IsDone,
                "two owners acquire ui_shop");

            var firstResult = first.CompletionTask.Result;
            var secondResult = second.CompletionTask.Result;

            Assert.IsTrue(firstResult.Success);
            Assert.IsTrue(secondResult.Success);
            Assert.AreEqual(1, loader.GetCreatedCount(shop), "ui_shop loaded once");

            var snapshot = runtime.CaptureSnapshot();

            Assert.AreEqual(2, Find(snapshot, "ui_shop").UserRefCount);
            Assert.AreEqual(1, Find(snapshot, "battle_fx").UserRefCount);
            Assert.AreEqual(1, Find(snapshot, "battle_audio").UserRefCount);
            Assert.AreEqual(
                4,
                snapshot.TotalUserRefs,
                "shop x2 + fx lease + audio lease");

            firstResult.Lease.Dispose();
            secondResult.Lease.Dispose();

            snapshot = runtime.CaptureSnapshot();

            Assert.AreEqual(0, Find(snapshot, "ui_shop").UserRefCount);
            Assert.AreEqual(Bundles.BundleRuntimeState.Idle, Find(snapshot, "ui_shop").State);
            Assert.IsTrue(Find(snapshot, "ui_shop").HasPayload, "idle keeps the payload");

            Assert.IsTrue(runtime.TryUnloadIdle(shop), "idle shop can be unloaded");

            snapshot = runtime.CaptureSnapshot();
            Assert.AreEqual(Bundles.BundleRuntimeState.NotLoaded, Find(snapshot, "ui_shop").State);
            Assert.AreEqual(0, Find(snapshot, "battle_fx").UserRefCount, "dependency lease released");
            Assert.AreEqual(Bundles.BundleRuntimeState.Idle, Find(snapshot, "battle_fx").State);

            Assert.IsTrue(runtime.TryUnloadIdle(fx));
            Assert.IsTrue(runtime.TryUnloadIdle(audio));

            snapshot = runtime.CaptureSnapshot();

            Assert.AreEqual(Bundles.BundleRuntimeState.NotLoaded, Find(snapshot, "battle_fx").State);
            Assert.AreEqual(Bundles.BundleRuntimeState.NotLoaded, Find(snapshot, "battle_audio").State);
            Assert.AreEqual(0, snapshot.LoadedPayloadCount);
        }

        // ------------------------------------------------------------------
        // 4.4 场景 2：加载中取消一个 waiter，另一个仍然拿到 lease
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L44_CancelOneWaiter_OtherGetsLease()
        {
            var fx = new Bundles.BundleId(0);

            var catalog = new Bundles.BundleCatalog(
                new[]
                {
                    new Bundles.BundleInfo
                    {
                        Id = fx,
                        Name = "battle_fx",
                        DirectDependencies = Array.Empty<Bundles.BundleId>()
                    }
                });

            var loader = new Support.SimulatedBundleLoader(120);
            var runtime = new Bundles.BundleRuntime(catalog, loader);

            var keep = runtime.AcquireAsync(fx, "Battle.Scene");
            var cancel = runtime.AcquireAsync(fx, "UI.Shop");

            cancel.Cancel();

            yield return WaitUntil(
                () => keep.IsDone && cancel.IsDone,
                "concurrent acquire with one cancel");

            Assert.IsTrue(keep.CompletionTask.Result.Success, "surviving waiter gets a lease");

            var canceledResult = cancel.CompletionTask.Result;

            Assert.IsFalse(canceledResult.Success, "canceled waiter must not get a lease");
            Assert.IsNull(canceledResult.Lease, "canceled waiter must not create a lease");
            Assert.AreEqual(
                Bundles.BundleLoadErrorCode.Canceled,
                canceledResult.Error.Code);

            Assert.AreEqual(1, loader.GetStartedCount(fx), "bundle started once");

            var snapshot = runtime.CaptureSnapshot();
            Assert.AreEqual(1, Find(snapshot, "battle_fx").UserRefCount);
        }

        // ------------------------------------------------------------------
        // 4.4 场景 3：缺失依赖结构化失败，不创建 lease
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L44_MissingDependencyFailsWithoutLease()
        {
            var broken = new Bundles.BundleId(0);

            var catalog = new Bundles.BundleCatalog(
                new[]
                {
                    new Bundles.BundleInfo
                    {
                        Id = broken,
                        Name = "broken_bundle",
                        DirectDependencies = new[] { new Bundles.BundleId(99) }
                    }
                });

            var loader = new Support.SimulatedBundleLoader(20);
            var runtime = new Bundles.BundleRuntime(catalog, loader);

            LogAssert.Expect(
                LogType.Error,
                new Regex("\\[BundleRuntime\\] load failed broken_bundle: DependencyMissing.*"));

            var task = runtime.AcquireAsync(broken, "UI.Shop");

            yield return WaitUntil(() => task.IsDone, "broken bundle acquire");

            var result = task.CompletionTask.Result;

            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Lease);
            Assert.AreEqual(
                Bundles.BundleLoadErrorCode.DependencyMissing,
                result.Error.Code);

            var snapshot = runtime.CaptureSnapshot();
            var entry = Find(snapshot, "broken_bundle");

            Assert.AreEqual(Bundles.BundleRuntimeState.Failed, entry.State);
            Assert.AreEqual(0, entry.UserRefCount);
            Assert.IsFalse(entry.HasPayload);
        }

        private static bool AllDone(List<Scheduling.ScheduledLoadTaskBase> tasks)
        {
            for (var i = 0; i < tasks.Count; i++)
            {
                if (!tasks[i].IsDone)
                {
                    return false;
                }
            }

            return true;
        }

        private static int MainThreadIdOfPump()
        {
            return MainThreadPump.MainThreadId;
        }

        private static Bundles.BundleSnapshotEntry Find(
            Bundles.BundleRuntimeSnapshot snapshot,
            string name)
        {
            for (var i = 0; i < snapshot.Entries.Count; i++)
            {
                if (snapshot.Entries[i].Name == name)
                {
                    return snapshot.Entries[i];
                }
            }

            Assert.Fail("bundle not found in snapshot: " + name);
            return null;
        }
    }
}
