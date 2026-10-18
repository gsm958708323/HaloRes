using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Bundles44 = Game.AssetSystem.Bundles.L44;
using Demo32 = Game.AssetSystem.Demo.L32;
using Demo33 = Game.AssetSystem.Demo.L33;
using Demo44 = Game.AssetSystem.Demo.L44;
using Infra31 = Game.AssetSystem.Infrastructure.L31;
using Load32 = Game.AssetSystem.Loading.L32;
using Load33 = Game.AssetSystem.Loading.L33;
using Sched36 = Game.AssetSystem.Scheduling.L36;

namespace Game.AssetSystem.Lessons.Tests
{
    /// <summary>
    /// PlayMode tests that turn the document's scenario walkthroughs
    /// (正常路径 / 失败路径 / 竞态) into executable assertions.
    /// </summary>
    public sealed class RuntimeScenarioTests
    {
        [SetUp]
        public void SetUp()
        {
            Game.AssetSystem.Lessons.LessonBootGate.DisableAll();
        }

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
            var task = new Infra31.MainThreadAssetTask<string>();

            Task.Run(() => task.CompleteSuccess("Fx.Hit", "worker"));

            yield return WaitUntil(() => task.IsDone, "main thread task completion");

            Assert.AreEqual(
                Infra31.UnityMainThreadRunner.MainThreadId,
                task.CompletionThreadId,
                "completion must be delivered on the Unity main thread");

            Assert.IsTrue(task.Result.IsSuccess, "task should succeed");
            Assert.AreEqual("Fx.Hit", task.Result.Value);
            Assert.IsTrue(
                Infra31.UnityMainThreadRunner.IsMainThread,
                "await continuation must resume on the main thread");
        }

        // ------------------------------------------------------------------
        // 3.2 场景 1：一个 waiter 取消，另一个仍然成功，底层只加载一次
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L32_OneWaiterCanceled_OtherWaiterStillSucceeds()
        {
            var backend = new Demo32.SimulatedAssetLoadBackend<Demo32.SimulatedAsset>(
                120,
                () => new Demo32.SimulatedAsset("Fx.Hit.Scenario1"));

            var abandonedReleases = 0;

            var operation = new Load32.SharedAssetLoadOperation<Demo32.SimulatedAsset>(
                "Fx.Hit.Scenario1",
                1001,
                backend,
                asset =>
                {
                    abandonedReleases++;
                    asset.Release("abandoned by operation");
                });

            var uiTask = operation.AcquireWaiter("UI.Shop");
            var battleTask = operation.AcquireWaiter("Battle.Scene");

            Assert.AreEqual(1, backend.BeginCount, "backend must start once");
            Assert.AreEqual(2, operation.ActiveWaiterCount);

            uiTask.Cancel();

            yield return WaitUntil(
                () => uiTask.IsDone && battleTask.IsDone,
                "canceled + shared completion");

            Assert.AreEqual(
                Load32.AssetLoadStatus.Canceled,
                uiTask.Result.Status,
                "canceled waiter must report Canceled");

            Assert.AreEqual(
                Load32.AssetLoadStatus.Succeeded,
                battleTask.Result.Status,
                "surviving waiter must still succeed");

            Assert.AreEqual(
                Infra32MainThreadId(),
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
            var backend = new Demo32.SimulatedAssetLoadBackend<Demo32.SimulatedAsset>(
                120,
                () => new Demo32.SimulatedAsset("Fx.Hit.Late"));

            var abandonedReleases = 0;

            var operation = new Load32.SharedAssetLoadOperation<Demo32.SimulatedAsset>(
                "Fx.Hit.Late",
                1002,
                backend,
                asset =>
                {
                    abandonedReleases++;
                    asset.Release("abandoned late success");
                });

            var uiTask = operation.AcquireWaiter("UI.Shop");
            uiTask.Cancel();

            yield return WaitUntil(() => uiTask.IsDone, "canceled waiter");

            Assert.AreEqual(Load32.AssetLoadStatus.Canceled, uiTask.Result.Status);

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
            var backends = new List<Demo33.SimulatedAssetLoadBackend<Demo33.SimulatedAsset>>();

            var registry = new Load33.SharedLoadRegistry<Demo33.SimulatedAsset>(
                key =>
                {
                    var backend = new Demo33.SimulatedAssetLoadBackend<Demo33.SimulatedAsset>(
                        120,
                        () => new Demo33.SimulatedAsset(key + ".payload"));
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

            Assert.AreEqual(Load33.AssetLoadStatus.Succeeded, uiTask.Result.Status);
            Assert.AreEqual(Load33.AssetLoadStatus.Succeeded, battleTask.Result.Status);
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
            var backends = new List<Demo33.SimulatedAssetLoadBackend<Demo33.SimulatedAsset>>();

            var registry = new Load33.SharedLoadRegistry<Demo33.SimulatedAsset>(
                key =>
                {
                    var backend = new Demo33.SimulatedAssetLoadBackend<Demo33.SimulatedAsset>(
                        120,
                        () => new Demo33.SimulatedAsset(key + ".payload"));
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

            Assert.AreEqual(Load33.AssetLoadStatus.Succeeded, second.Result.Status);
        }

        // ------------------------------------------------------------------
        // 3.6 场景 1：优先级 + 双预算（每帧启动数、并发数）
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L36_HighPriorityStartsFirst_WithinBudget()
        {
            var scheduler = new Sched36.AssetLoadScheduler
            {
                MaxStartsPerFrame = 2,
                MaxConcurrentLoads = 2,
                MaxConsecutiveHighStarts = 3,
                MaxConsecutiveNormalStarts = 6
            };

            var scope = new Sched36.LoadScope("SchedulerBudgetScope");

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

            var low1 = scheduler.Enqueue("low_1", scope, Sched36.AssetLoadPriority.Low, ct => worker("low_1", ct));
            var low2 = scheduler.Enqueue("low_2", scope, Sched36.AssetLoadPriority.Low, ct => worker("low_2", ct));
            var normal1 = scheduler.Enqueue("normal_1", scope, Sched36.AssetLoadPriority.Normal, ct => worker("normal_1", ct));
            var normal2 = scheduler.Enqueue("normal_2", scope, Sched36.AssetLoadPriority.Normal, ct => worker("normal_2", ct));
            var high1 = scheduler.Enqueue("high_1", scope, Sched36.AssetLoadPriority.High, ct => worker("high_1", ct));
            var high2 = scheduler.Enqueue("high_2", scope, Sched36.AssetLoadPriority.High, ct => worker("high_2", ct));

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
        // 3.6 场景 2：Scope 释放时取消排队任务，运行中的任务收尾
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L36_ScopeDisposeCancelsQueuedLoads()
        {
            var scheduler = new Sched36.AssetLoadScheduler
            {
                MaxStartsPerFrame = 1,
                MaxConcurrentLoads = 1,
                MaxConsecutiveHighStarts = 4,
                MaxConsecutiveNormalStarts = 4
            };

            var scope = new Sched36.LoadScope("SchedulerCancelScope");
            var started = 0;

            Func<CancellationToken, Task<string>> worker = token =>
            {
                Interlocked.Increment(ref started);
                return Task.Delay(120, token).ContinueWith(_ => "done", TaskScheduler.Default);
            };

            var tasks = new List<Sched36.ScheduledLoadTaskBase>();

            for (var i = 0; i < 6; i++)
            {
                tasks.Add(scheduler.Enqueue(
                    "cancel_" + i,
                    scope,
                    Sched36.AssetLoadPriority.Normal,
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
                    Sched36.ScheduledLoadStatus.Canceled,
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
            var audio = new Bundles44.BundleId(0);
            var fx = new Bundles44.BundleId(1);
            var shop = new Bundles44.BundleId(2);

            var catalog = new Bundles44.BundleCatalog(
                new[]
                {
                    new Bundles44.BundleInfo
                    {
                        Id = audio,
                        Name = "battle_audio",
                        DirectDependencies = Array.Empty<Bundles44.BundleId>()
                    },
                    new Bundles44.BundleInfo
                    {
                        Id = fx,
                        Name = "battle_fx",
                        DirectDependencies = new[] { audio }
                    },
                    new Bundles44.BundleInfo
                    {
                        Id = shop,
                        Name = "ui_shop",
                        DirectDependencies = new[] { fx }
                    }
                });

            var loader = new Demo44.SimulatedBundleLoader(80);
            var runtime = new Bundles44.BundleRuntime(catalog, loader);

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
            Assert.AreEqual(Bundles44.BundleRuntimeState.Idle, Find(snapshot, "ui_shop").State);
            Assert.IsTrue(Find(snapshot, "ui_shop").HasPayload, "idle keeps the payload");

            Assert.IsTrue(runtime.TryUnloadIdle(shop), "idle shop can be unloaded");

            snapshot = runtime.CaptureSnapshot();
            Assert.AreEqual(Bundles44.BundleRuntimeState.NotLoaded, Find(snapshot, "ui_shop").State);
            Assert.AreEqual(0, Find(snapshot, "battle_fx").UserRefCount, "dependency lease released");
            Assert.AreEqual(Bundles44.BundleRuntimeState.Idle, Find(snapshot, "battle_fx").State);

            Assert.IsTrue(runtime.TryUnloadIdle(fx));
            Assert.IsTrue(runtime.TryUnloadIdle(audio));

            snapshot = runtime.CaptureSnapshot();

            Assert.AreEqual(Bundles44.BundleRuntimeState.NotLoaded, Find(snapshot, "battle_fx").State);
            Assert.AreEqual(Bundles44.BundleRuntimeState.NotLoaded, Find(snapshot, "battle_audio").State);
            Assert.AreEqual(0, snapshot.LoadedPayloadCount);
        }

        // ------------------------------------------------------------------
        // 4.4 场景 2：加载中取消一个 waiter，另一个仍然拿到 lease
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L44_CancelOneWaiter_OtherGetsLease()
        {
            var fx = new Bundles44.BundleId(0);

            var catalog = new Bundles44.BundleCatalog(
                new[]
                {
                    new Bundles44.BundleInfo
                    {
                        Id = fx,
                        Name = "battle_fx",
                        DirectDependencies = Array.Empty<Bundles44.BundleId>()
                    }
                });

            var loader = new Demo44.SimulatedBundleLoader(120);
            var runtime = new Bundles44.BundleRuntime(catalog, loader);

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
                Bundles44.BundleLoadErrorCode.Canceled,
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
            var broken = new Bundles44.BundleId(0);

            var catalog = new Bundles44.BundleCatalog(
                new[]
                {
                    new Bundles44.BundleInfo
                    {
                        Id = broken,
                        Name = "broken_bundle",
                        DirectDependencies = new[] { new Bundles44.BundleId(99) }
                    }
                });

            var loader = new Demo44.SimulatedBundleLoader(20);
            var runtime = new Bundles44.BundleRuntime(catalog, loader);

            LogAssert.Expect(
                LogType.Error,
                new Regex("\\[BundleRuntime\\] load failed broken_bundle: DependencyMissing.*"));

            var task = runtime.AcquireAsync(broken, "UI.Shop");

            yield return WaitUntil(() => task.IsDone, "broken bundle acquire");

            var result = task.CompletionTask.Result;

            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Lease);
            Assert.AreEqual(
                Bundles44.BundleLoadErrorCode.DependencyMissing,
                result.Error.Code);

            var snapshot = runtime.CaptureSnapshot();
            var entry = Find(snapshot, "broken_bundle");

            Assert.AreEqual(Bundles44.BundleRuntimeState.Failed, entry.State);
            Assert.AreEqual(0, entry.UserRefCount);
            Assert.IsFalse(entry.HasPayload);
        }

        private static bool AllDone(List<Sched36.ScheduledLoadTaskBase> tasks)
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

        private static int Infra32MainThreadId()
        {
            return Game.AssetSystem.Infrastructure.L32.UnityMainThreadRunner.MainThreadId;
        }

        private static Bundles44.BundleSnapshotEntry Find(
            Bundles44.BundleRuntimeSnapshot snapshot,
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
