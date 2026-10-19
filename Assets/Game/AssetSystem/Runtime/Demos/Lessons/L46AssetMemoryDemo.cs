using System;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Assets;
using Game.AssetSystem.Bundles;
using Game.AssetSystem.Core;
using Game.AssetSystem.Diagnostics;
using Game.AssetSystem.Memory;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// L4.6 — memory policy and diagnostics.
    ///
    /// Scenario 1 reclaims idle assets under a one-per-evaluation budget.
    /// Scenario 2 shows Critical pressure with a bigger budget.
    /// Scenario 3 holds a live handle and shows the leak view plus why
    /// TryUnloadIdle refuses to unload a held asset.
    /// </summary>
    public sealed class L46AssetMemoryDemo : LessonDemoBase
    {
        public override string Title
        {
            get { return "L4.6 内存策略：空闲预算回收 / 诊断泄漏视图"; }
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            Debug.Log(
                "[Lesson46] start mainThreadId=" + MainThreadPump.MainThreadId +
                " isMainThread=" + MainThreadPump.IsMainThread);

            var bundleId = new BundleId(0);

            var policy = new AssetMemoryPolicy();
            var diagnostics = new AssetRuntimeDiagnostics();
            var loader = new LessonBundleLoader(80);

            var bundleCatalog = new BundleCatalog(
                new[]
                {
                    new BundleInfo
                    {
                        Id = bundleId,
                        Name = "bundle_fx",
                        DirectDependencies = Array.Empty<BundleId>()
                    }
                });

            var assetCatalog = new AssetCatalog(
                new[]
                {
                    new AssetEntry
                    {
                        Key = new AssetKey("fx/hit"),
                        BundleId = bundleId,
                        InternalAssetPath = "assets/fx/hit.prefab"
                    },
                    new AssetEntry
                    {
                        Key = new AssetKey("fx/spark"),
                        BundleId = bundleId,
                        InternalAssetPath = "assets/fx/spark.prefab"
                    },
                    new AssetEntry
                    {
                        Key = new AssetKey("fx/trail"),
                        BundleId = bundleId,
                        InternalAssetPath = "assets/fx/trail.prefab"
                    }
                });

            var bundleRuntime = new BundleRuntime(
                bundleCatalog,
                loader,
                policy,
                diagnostics);

            var assetCache = new AssetCache(
                bundleRuntime,
                policy,
                diagnostics);

            policy.Configure(
                assetCache.TryUnloadIdle,
                bundleRuntime.TryUnloadIdle);

            var assetService = new AssetService(assetCatalog, assetCache);

            await RunIdleBudgetScenario(
                assetService,
                policy,
                diagnostics,
                loader,
                bundleId,
                cancellationToken);

            ThrowIfCanceled(cancellationToken);
            await RunCriticalBudgetScenario(
                assetService,
                policy,
                diagnostics,
                loader,
                bundleId,
                cancellationToken);

            ThrowIfCanceled(cancellationToken);
            await RunDiagnosticsLeakScenario(
                assetService,
                assetCache,
                policy,
                diagnostics,
                loader,
                bundleId,
                cancellationToken);

            Debug.Log(
                "[Lesson46] Finished. " +
                "Experiment 1: set MaxUnloadsPerFrame=10 and observe a one-frame burst. " +
                "Experiment 2: remove policy.MarkAssetIdle from AssetCache.ReleaseAsset " +
                "and observe Idle assets never reclaimed. " +
                "Experiment 3: keep a handle alive and observe TryUnloadIdle returning false.");
        }

        private static async Task RunIdleBudgetScenario(
            AssetService assetService,
            AssetMemoryPolicy policy,
            AssetRuntimeDiagnostics diagnostics,
            LessonBundleLoader loader,
            BundleId bundleId,
            CancellationToken cancellationToken)
        {
            Debug.Log("[Scenario1] idle budget = 1");

            await MakeIdle(assetService, "Scope.Hit", "fx/hit", "S1.Hit");
            await MakeIdle(assetService, "Scope.Spark", "fx/spark", "S1.Spark");
            await MakeIdle(assetService, "Scope.Trail", "fx/trail", "S1.Trail");

            LogSnapshot("Scenario1.BeforeReclaim", diagnostics);

            policy.MaxUnloadsPerFrame = 1;

            for (var i = 0; i < 4; i++)
            {
                var report = policy.Evaluate(
                    Time.realtimeSinceStartup,
                    MemoryPressureLevel.High);

                Debug.Log("[Scenario1] evaluate[" + i + "] " + report);

                LogSnapshot("Scenario1.AfterReclaim" + i, diagnostics);

                await DelayAsync(10, cancellationToken);
            }

            Debug.Log(
                "[Scenario1] bundleReleased=" +
                loader.GetReleasedCount(bundleId));
        }

        private static async Task RunCriticalBudgetScenario(
            AssetService assetService,
            AssetMemoryPolicy policy,
            AssetRuntimeDiagnostics diagnostics,
            LessonBundleLoader loader,
            BundleId bundleId,
            CancellationToken cancellationToken)
        {
            Debug.Log("[Scenario2] critical pressure with budget = 10");

            await MakeIdle(assetService, "Scope.Hit2", "fx/hit", "S2.Hit");
            await MakeIdle(assetService, "Scope.Spark2", "fx/spark", "S2.Spark");

            policy.MaxUnloadsPerFrame = 10;

            var report = policy.Evaluate(
                Time.realtimeSinceStartup,
                MemoryPressureLevel.Critical);

            Debug.Log("[Scenario2] evaluate " + report);
            LogSnapshot("Scenario2.AfterCritical", diagnostics);

            Debug.Log(
                "[Scenario2] bundleReleased=" +
                loader.GetReleasedCount(bundleId));

            await Task.Yield();
        }

        private static async Task RunDiagnosticsLeakScenario(
            AssetService assetService,
            AssetCache assetCache,
            AssetMemoryPolicy policy,
            AssetRuntimeDiagnostics diagnostics,
            LessonBundleLoader loader,
            BundleId bundleId,
            CancellationToken cancellationToken)
        {
            Debug.Log("[Scenario3] live handle diagnostics");

            var key = new AssetKey("fx/hit");
            var scope = assetService.CreateScope("Leaked.UI");

            var result = await assetService.LoadAsync<LessonAssetPayload>(
                key,
                scope,
                "Leaked.UI.Owner");

            Debug.Log("[Scenario3] load status=" + result.Status);

            var snapshot = diagnostics.CaptureSnapshot();

            Debug.Log(
                "[Scenario3] snapshot liveHandles=" + snapshot.LiveHandleCount +
                " suspectLeaks=" + snapshot.SuspectLeakHandleCount +
                " idleAssets=" + snapshot.IdleAssetCount +
                " idleBundles=" + snapshot.IdleBundleCount);

            for (var i = 0; i < snapshot.LiveHandles.Count; i++)
            {
                var handle = snapshot.LiveHandles[i];

                Debug.Log(
                    "[Scenario3] handle key=" + handle.Key.Value +
                    " owner=" + handle.Owner +
                    " scope=" + handle.ScopeName +
                    " scopeDisposed=" + handle.ScopeDisposed);
            }

            var unloadWhileHeld = assetCache.TryUnloadIdle(key);

            Debug.Log(
                "[Scenario3] try unload while handle held=" + unloadWhileHeld);

            if (result.IsSuccess && result.Handle != null)
            {
                result.Handle.Dispose();
            }

            scope.Dispose();

            var unloadAfterRelease = assetCache.TryUnloadIdle(key);

            Debug.Log(
                "[Scenario3] try unload after release=" + unloadAfterRelease);

            policy.MaxUnloadsPerFrame = 10;

            var report = policy.Evaluate(
                Time.realtimeSinceStartup,
                MemoryPressureLevel.Critical);

            Debug.Log("[Scenario3] reclaim " + report);

            var finalSnapshot = diagnostics.CaptureSnapshot();

            Debug.Log(
                "[Scenario3] final liveHandles=" + finalSnapshot.LiveHandleCount +
                " idleAssets=" + finalSnapshot.IdleAssetCount +
                " idleBundles=" + finalSnapshot.IdleBundleCount +
                " bundleReleased=" + loader.GetReleasedCount(bundleId));

            await Task.Yield();
        }

        private static async Task MakeIdle(
            AssetService assetService,
            string scopeName,
            string keyValue,
            string owner)
        {
            var scope = assetService.CreateScope(scopeName);
            var key = new AssetKey(keyValue);

            var result = await assetService.LoadAsync<LessonAssetPayload>(
                key,
                scope,
                owner);

            Debug.Log(
                "[MakeIdle] key=" + keyValue +
                " status=" + result.Status +
                " owner=" + owner);

            if (result.IsSuccess && result.Handle != null)
            {
                result.Handle.Dispose();
            }

            scope.Dispose();
        }

        private static void LogSnapshot(
            string label,
            AssetRuntimeDiagnostics diagnostics)
        {
            var snapshot = diagnostics.CaptureSnapshot();

            Debug.Log(
                "[Snapshot][" + label + "] " +
                "idleAssets=" + snapshot.IdleAssetCount +
                " idleBundles=" + snapshot.IdleBundleCount +
                " liveHandles=" + snapshot.LiveHandleCount +
                " suspectLeaks=" + snapshot.SuspectLeakHandleCount);

            for (var i = 0; i < snapshot.Assets.Count; i++)
            {
                var asset = snapshot.Assets[i];

                Debug.Log(
                    "[Snapshot][" + label + "] asset=" + asset.Key.Value +
                    " state=" + asset.State +
                    " userRefs=" + asset.UserRefCount +
                    " waiting=" + asset.WaitingCount +
                    " bundleLease=" + asset.HasBundleLease);
            }

            for (var i = 0; i < snapshot.Bundles.Count; i++)
            {
                var bundle = snapshot.Bundles[i];

                Debug.Log(
                    "[Snapshot][" + label + "] bundle=" + bundle.Name +
                    " state=" + bundle.State +
                    " userRefs=" + bundle.UserRefCount +
                    " hasPayload=" + bundle.HasPayload);
            }
        }
    }
}
