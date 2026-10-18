using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.AssetSystem.Assets.L45;
using Game.AssetSystem.Bundles.L45;
using Game.AssetSystem.Infrastructure.L45;
using UnityEngine;

namespace Game.AssetSystem.Demo.L45
{
    public sealed class AssetCacheLessonBootstrap : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!Game.AssetSystem.Lessons.LessonBootGate.Enabled)
            {
                return;
            }

            var existing =
                UnityEngine.Object.FindFirstObjectByType<AssetCacheLessonBootstrap>();

            if (existing != null)
            {
                return;
            }

            var go = new GameObject("[AssetCacheLessonBootstrap]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<AssetCacheLessonBootstrap>();
        }

        private async void Start()
        {
            Debug.Log(
                "[Lesson45] start mainThreadId=" +
                UnityMainThreadPump.MainThreadId +
                " isMainThread=" +
                UnityMainThreadPump.IsMainThread);

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

            var bundleLoader = new SimulatedAssetBundleLoader(150);
            var bundleRuntime = new BundleRuntime(
                bundleCatalog,
                bundleLoader);

            var assetCache = new AssetCache(bundleRuntime);
            var assetService = new AssetService(assetCatalog, assetCache);

            await RunConcurrentCancelScenario(
                assetService,
                bundleLoader,
                bundleId,
                key,
                internalPath);

            await RunMultipleScopeScenario(
                assetService,
                assetCache,
                bundleLoader,
                bundleId,
                key,
                internalPath);

            await RunScopeDisposedBeforeCompletionScenario(
                assetService,
                assetCache,
                bundleLoader,
                bundleId,
                key,
                internalPath);

            await RunMissingKeyScenario(
                assetService,
                missingKey);

            Debug.Log(
                "[Lesson45] Finished. " +
                "Experiment 1: release bundle lease before asset payload; " +
                "Experiment 2: create handle on failed load; " +
                "Experiment 3: remove scope cancellation.");
        }

        private static async Task RunConcurrentCancelScenario(
            AssetService assetService,
            SimulatedAssetBundleLoader bundleLoader,
            BundleId bundleId,
            AssetKey key,
            string internalPath)
        {
            var uiScope = assetService.CreateScope("UI.Shop");
            var battleScope = assetService.CreateScope("Battle.Scene");

            Debug.Log("[Scenario1] concurrent acquire with UI cancel");

            var uiTask = assetService.LoadAsync<SimulatedAsset>(
                key,
                uiScope,
                "UI");

            var battleTask = assetService.LoadAsync<SimulatedAsset>(
                key,
                battleScope,
                "Battle");

            await Task.Delay(50);

            uiTask.Cancel();

            var uiResult = await uiTask;
            var battleResult = await battleTask;

            Debug.Log(
                "[Scenario1] ui=" +
                uiResult.Status +
                " battle=" +
                battleResult.Status +
                " bundleStarted=" +
                bundleLoader.GetBundleStartedCount(bundleId) +
                " assetLoads=" +
                bundleLoader.GetAssetLoadCount(internalPath));

            if (battleResult.IsSuccess)
            {
                Debug.Log(
                    "[Scenario1] battle value=" +
                    battleResult.Value.DebugName);

                battleResult.Handle.Dispose();
            }

            uiScope.Dispose();
            battleScope.Dispose();

            Debug.Log(
                "[Scenario1] after dispose bundleReleased=" +
                bundleLoader.GetBundleReleasedCount(bundleId));
        }

        private static async Task RunMultipleScopeScenario(
            AssetService assetService,
            AssetCache assetCache,
            SimulatedAssetBundleLoader bundleLoader,
            BundleId bundleId,
            AssetKey key,
            string internalPath)
        {
            var scopeA = assetService.CreateScope("Scope.A");
            var scopeB = assetService.CreateScope("Scope.B");

            Debug.Log("[Scenario2] two scopes acquire same asset");

            var taskA = assetService.LoadAsync<SimulatedAsset>(
                key,
                scopeA,
                "A");

            var resultA = await taskA;

            var taskB = assetService.LoadAsync<SimulatedAsset>(
                key,
                scopeB,
                "B");

            var resultB = await taskB;

            Debug.Log(
                "[Scenario2] a=" +
                resultA.Status +
                " b=" +
                resultB.Status +
                " bundleStarted=" +
                bundleLoader.GetBundleStartedCount(bundleId) +
                " assetLoads=" +
                bundleLoader.GetAssetLoadCount(internalPath));

            scopeA.Dispose();

            Debug.Log(
                "[Scenario2] after dispose A, bStillValid=" +
                (resultB.Handle != null && resultB.Handle.IsValid));

            scopeB.Dispose();

            var unloaded = assetCache.TryUnloadIdle(key);

            Debug.Log(
                "[Scenario2] after dispose B, unloadIdle=" +
                unloaded +
                " bundleReleased=" +
                bundleLoader.GetBundleReleasedCount(bundleId));
        }

        private static async Task RunScopeDisposedBeforeCompletionScenario(
            AssetService assetService,
            AssetCache assetCache,
            SimulatedAssetBundleLoader bundleLoader,
            BundleId bundleId,
            AssetKey key,
            string internalPath)
        {
            var scope = assetService.CreateScope("Scope.EarlyDispose");

            Debug.Log("[Scenario3] dispose scope before load completes");

            var task = assetService.LoadAsync<SimulatedAsset>(
                key,
                scope,
                "Early");

            await Task.Delay(30);

            scope.Dispose();

            var result = await task;

            Debug.Log(
                "[Scenario3] result=" +
                result.Status +
                " bundleStarted=" +
                bundleLoader.GetBundleStartedCount(bundleId) +
                " assetLoads=" +
                bundleLoader.GetAssetLoadCount(internalPath));

            await Task.Delay(250);

            var unloaded = assetCache.TryUnloadIdle(key);

            Debug.Log(
                "[Scenario3] after late completion, unloadIdle=" +
                unloaded +
                " bundleReleased=" +
                bundleLoader.GetBundleReleasedCount(bundleId));
        }

        private static async Task RunMissingKeyScenario(
            AssetService assetService,
            AssetKey missingKey)
        {
            var scope = assetService.CreateScope("Scope.Missing");

            Debug.Log("[Scenario4] load missing key");

            var result = await assetService.LoadAsync<SimulatedAsset>(
                missingKey,
                scope,
                "Missing");

            Debug.Log(
                "[Scenario4] status=" +
                result.Status +
                " error=" +
                result.Error);

            scope.Dispose();
        }
    }
}
