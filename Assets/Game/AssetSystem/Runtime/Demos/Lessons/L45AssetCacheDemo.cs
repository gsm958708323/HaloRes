using System;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Assets;
using Game.AssetSystem.Bundles;
using Game.AssetSystem.Core;
using Game.AssetSystem.Diagnostics;
using Game.AssetSystem.Memory;
using Game.AssetSystem.Simulation;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// L4.5 — asset cache on top of the bundle runtime.
    ///
    /// Scenario 1: two scopes load the same key, one waiter cancels.
    /// Scenario 2: two scopes hold the same asset, releasing one keeps the other.
    /// Scenario 3: scope disposed before the load completes.
    /// Scenario 4: missing key.
    /// </summary>
    public sealed class L45AssetCacheDemo : LessonDemoBase
    {
        public override string Title
        {
            get { return "L4.5 资源缓存：共享加载 / 作用域 / 空闲卸载"; }
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            Debug.Log(
                "[Lesson45] start mainThreadId=" + MainThreadPump.MainThreadId +
                " isMainThread=" + MainThreadPump.IsMainThread);

            var bundleId = new BundleId(0);
            var key = new AssetKey("fx/hit");
            var missingKey = new AssetKey("fx/missing");
            const string internalPath = "assets/fx/hit.prefab";

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
                        Key = key,
                        BundleId = bundleId,
                        InternalAssetPath = internalPath
                    }
                });

            var policy = new AssetMemoryPolicy();
            var diagnostics = new AssetRuntimeDiagnostics();
            var bundleLoader = new SimulatedBundleLoader(150);

            var bundleRuntime = new BundleRuntime(
                bundleCatalog,
                bundleLoader,
                policy,
                diagnostics);

            var assetCache = new AssetCache(
                bundleRuntime,
                policy,
                diagnostics);

            var assetService = new AssetService(assetCatalog, assetCache);

            await RunConcurrentCancelScenario(
                assetService,
                bundleLoader,
                bundleId,
                key,
                internalPath,
                cancellationToken);

            ThrowIfCanceled(cancellationToken);
            await RunMultipleScopeScenario(
                assetService,
                assetCache,
                bundleLoader,
                bundleId,
                key,
                internalPath,
                cancellationToken);

            ThrowIfCanceled(cancellationToken);
            await RunScopeDisposedBeforeCompletionScenario(
                assetService,
                assetCache,
                bundleLoader,
                bundleId,
                key,
                internalPath,
                cancellationToken);

            ThrowIfCanceled(cancellationToken);
            await RunMissingKeyScenario(
                assetService,
                missingKey,
                cancellationToken);

            Debug.Log(
                "[Lesson45] Finished. " +
                "Experiment 1: release the bundle lease before the asset payload; " +
                "Experiment 2: create a handle on a failed load; " +
                "Experiment 3: remove scope cancellation.");
        }

        private static async Task RunConcurrentCancelScenario(
            AssetService assetService,
            SimulatedBundleLoader bundleLoader,
            BundleId bundleId,
            AssetKey key,
            string internalPath,
            CancellationToken cancellationToken)
        {
            var uiScope = assetService.CreateScope("UI.Shop");
            var battleScope = assetService.CreateScope("Battle.Scene");

            Debug.Log("[Scenario1] concurrent acquire with UI cancel");

            var uiTask = assetService.LoadAsync<LessonAssetPayload>(
                key,
                uiScope,
                "UI");

            var battleTask = assetService.LoadAsync<LessonAssetPayload>(
                key,
                battleScope,
                "Battle");

            await DelayAsync(50, cancellationToken);

            uiTask.Cancel();

            var uiResult = await uiTask;
            var battleResult = await battleTask;

            Debug.Log(
                "[Scenario1] ui=" + uiResult.Status +
                " battle=" + battleResult.Status +
                " bundleStarted=" + bundleLoader.GetStartedCount(bundleId) +
                " assetLoads=" + bundleLoader.GetAssetLoadCount(internalPath));

            if (battleResult.IsSuccess)
            {
                Debug.Log(
                    "[Scenario1] battle value=" + battleResult.Value.DebugName);

                battleResult.Handle.Dispose();
            }

            uiScope.Dispose();
            battleScope.Dispose();

            Debug.Log(
                "[Scenario1] after dispose bundleReleased=" +
                bundleLoader.GetReleasedCount(bundleId));
        }

        private static async Task RunMultipleScopeScenario(
            AssetService assetService,
            AssetCache assetCache,
            SimulatedBundleLoader bundleLoader,
            BundleId bundleId,
            AssetKey key,
            string internalPath,
            CancellationToken cancellationToken)
        {
            var scopeA = assetService.CreateScope("Scope.A");
            var scopeB = assetService.CreateScope("Scope.B");

            Debug.Log("[Scenario2] two scopes acquire the same asset");

            var taskA = assetService.LoadAsync<LessonAssetPayload>(
                key,
                scopeA,
                "A");

            var resultA = await taskA;

            var taskB = assetService.LoadAsync<LessonAssetPayload>(
                key,
                scopeB,
                "B");

            var resultB = await taskB;

            Debug.Log(
                "[Scenario2] a=" + resultA.Status +
                " b=" + resultB.Status +
                " bundleStarted=" + bundleLoader.GetStartedCount(bundleId) +
                " assetLoads=" + bundleLoader.GetAssetLoadCount(internalPath));

            scopeA.Dispose();

            Debug.Log(
                "[Scenario2] after dispose A, bStillValid=" +
                (resultB.Handle != null && resultB.Handle.IsValid));

            scopeB.Dispose();

            var unloaded = assetCache.TryUnloadIdle(key);

            Debug.Log(
                "[Scenario2] after dispose B, unloadIdle=" + unloaded +
                " bundleReleased=" + bundleLoader.GetReleasedCount(bundleId));

            await Task.Yield();
        }

        private static async Task RunScopeDisposedBeforeCompletionScenario(
            AssetService assetService,
            AssetCache assetCache,
            SimulatedBundleLoader bundleLoader,
            BundleId bundleId,
            AssetKey key,
            string internalPath,
            CancellationToken cancellationToken)
        {
            var scope = assetService.CreateScope("Scope.EarlyDispose");

            Debug.Log("[Scenario3] dispose scope before the load completes");

            var task = assetService.LoadAsync<LessonAssetPayload>(
                key,
                scope,
                "Early");

            await DelayAsync(30, cancellationToken);

            scope.Dispose();

            var result = await task;

            Debug.Log(
                "[Scenario3] result=" + result.Status +
                " error=" + result.Error +
                " bundleStarted=" + bundleLoader.GetStartedCount(bundleId) +
                " assetLoads=" + bundleLoader.GetAssetLoadCount(internalPath));

            await DelayAsync(250, cancellationToken);

            var unloaded = assetCache.TryUnloadIdle(key);

            Debug.Log(
                "[Scenario3] after late completion, unloadIdle=" + unloaded +
                " bundleReleased=" + bundleLoader.GetReleasedCount(bundleId));
        }

        private static async Task RunMissingKeyScenario(
            AssetService assetService,
            AssetKey missingKey,
            CancellationToken cancellationToken)
        {
            var scope = assetService.CreateScope("Scope.Missing");

            Debug.Log("[Scenario4] load a missing key");

            var result = await assetService.LoadAsync<LessonAssetPayload>(
                missingKey,
                scope,
                "Missing");

            Debug.Log(
                "[Scenario4] status=" + result.Status +
                " error=" + result.Error);

            scope.Dispose();

            await Task.Yield();
        }
    }
}
