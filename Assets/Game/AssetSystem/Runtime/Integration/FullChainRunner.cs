using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Assets;
using Game.AssetSystem.Bundles;
using Game.AssetSystem.Content;
using Game.AssetSystem.Diagnostics;
using Game.AssetSystem.Patch;
using Game.AssetSystem.Memory;
using Game.AssetSystem.Release;
using UnityEngine;

using Game.AssetSystem.Core;

namespace Game.AssetSystem.FullChain
{
    /// <summary>Machine readable result of one full-chain run.</summary>
    public sealed class FullChainReport
    {
        public bool ContentFound;
        public string PlatformKey;
        public string V1Activation;
        public string V2ActivationWithHandles;
        public string V2ActivationUnsafePoint;
        public string V2ActivationFinal;

        public int Owners;
        public int BundleLoadsV1;
        public int BundleLoadsV2;
        public bool SameAssetInstance;
        public int LiveHandlesAfterUiDispose;

        public string V1Label;
        public string V2Label;

        public int ReclaimedAssets;
        public int ReclaimedBundles;
        public int RemainingLiveHandles;

        public readonly List<string> Violations = new List<string>();

        public bool Passed
        {
            get { return ContentFound && Violations.Count == 0; }
        }

        public override string ToString()
        {
            return "v1=" + V1Activation +
                   " v2WithHandles=" + V2ActivationWithHandles +
                   " v2UnsafePoint=" + V2ActivationUnsafePoint +
                   " v2Final=" + V2ActivationFinal +
                   " owners=" + Owners +
                   " bundleLoadsV1=" + BundleLoadsV1 +
                   " bundleLoadsV2=" + BundleLoadsV2 +
                   " sameInstance=" + SameAssetInstance +
                   " liveHandlesAfterUiDispose=" + LiveHandlesAfterUiDispose +
                   " v1Label=" + V1Label +
                   " v2Label=" + V2Label +
                   " reclaimedAssets=" + ReclaimedAssets +
                   " reclaimedBundles=" + ReclaimedBundles +
                   " remainingHandles=" + RemainingLiveHandles +
                   " violations=" + Violations.Count;
        }
    }

    /// <summary>
    /// End-to-end chain: 4.3 build output -> real AssetBundle payload (5.2)
    /// -> 5.1 version context / BundleRuntime / AssetCache / memory reclaim.
    ///
    /// Lives in the runtime assembly (not in Demos) because the PlayMode test
    /// and the Editor builder both depend on it, and neither should have to
    /// reference demo code. There is no full-chain scene any more: the runner
    /// is exercised by <c>FullChainScenarioTests</c>.
    /// </summary>
    public static class FullChainRunner
    {
        public const string ShopRootKey = "ui/shop/shoproot";

        public static async Task<FullChainReport> RunAsync()
        {
            MainThreadPump.EnsureMainThread(nameof(RunAsync));

            var report = new FullChainReport
            {
                PlatformKey = FullChainLayout.PlatformKey
            };

            if (!FullChainLayout.HasContent(FullChainLayout.V1Version) ||
                !FullChainLayout.HasContent(FullChainLayout.V2Version))
            {
                report.ContentFound = false;
                report.Violations.Add(
                    "build output missing. Run Tools/Game Asset System/" +
                    "Build Full-Chain Demo Content first.");
                return report;
            }

            report.ContentFound = true;

            var v1Dir = FullChainLayout.ContentDir(FullChainLayout.V1Version);
            var v2Dir = FullChainLayout.ContentDir(FullChainLayout.V2Version);

            var v1Manifest = LoadManifest(v1Dir);
            var v2Manifest = LoadManifest(v2Dir);

            // ---- 阶段 A：冷启动激活 v1（真实 Hash 校验） --------------------
            var activation = new PatchActivationService();

            var coldStart = await activation.ActivateAsync(
                v1Manifest,
                v1Dir,
                FullChainLayout.PlatformKey,
                0,
                true);

            report.V1Activation = coldStart.Status.ToString();

            if (activation.ActiveContext == null)
            {
                report.Violations.Add("v1 activation failed: " + coldStart.Status + " " + coldStart.Message);
                return report;
            }

            Debug.Log(
                "[FullChain][A] cold start " + coldStart.Status +
                " version=" + activation.ActiveContext.ContentVersion +
                " bundles=" + activation.ActiveContext.BundleCount +
                " assets=" + activation.ActiveContext.AssetCount);

            // ---- 阶段 B：UI 与 Battle 共享同一 Asset ------------------------
            var policy1 = new AssetMemoryPolicy();
            var diagnostics1 = new AssetRuntimeDiagnostics();
            var loader1 = new CountingBundleLoader();
            var runtime1 = new BundleRuntime(
                activation.ActiveContext.CreateBundleCatalog(),
                loader1,
                policy1,
                diagnostics1);
            var cache1 = new AssetCache(
                activation.ActiveContext.CreateAssetCatalog(),
                runtime1,
                policy1,
                diagnostics1);

            var uiScope = cache1.CreateScope("UI.Shop");
            var battleScope = cache1.CreateScope("Battle.Scene");
            var key = new AssetKey(ShopRootKey);

            var uiTask = cache1.AcquireAsync<DemoGraphNode>(key, uiScope, "UI.Shop");
            var battleTask = cache1.AcquireAsync<DemoGraphNode>(key, battleScope, "Battle.Scene");

            var uiResult = await uiTask.Completion;
            var battleResult = await battleTask.Completion;

            report.Owners = 2;
            report.BundleLoadsV1 = loader1.LoadCount;
            report.V1Label = uiResult.Value == null ? "<null>" : uiResult.Value.label;
            report.SameAssetInstance = ReferenceEquals(uiResult.Value, battleResult.Value);

            if (uiResult.Status != AssetLoadStatus.Succeeded ||
                battleResult.Status != AssetLoadStatus.Succeeded)
            {
                report.Violations.Add(
                    "shared asset load failed: ui=" + uiResult.Status +
                    " battle=" + battleResult.Status);
                return report;
            }

            if (loader1.LoadCount != 1)
            {
                report.Violations.Add("v1 bundle loaded " + loader1.LoadCount + " times, expected 1");
            }

            if (!report.SameAssetInstance)
            {
                report.Violations.Add("two owners got different asset instances");
            }

            Debug.Log(
                "[FullChain][B] shared load ui=" + uiResult.Status +
                " battle=" + battleResult.Status +
                " bundleLoads=" + loader1.LoadCount +
                " sameInstance=" + report.SameAssetInstance +
                " label=" + report.V1Label);

            // ---- 阶段 C：释放 UI，Battle 仍持有 -----------------------------
            uiScope.Dispose();

            report.LiveHandlesAfterUiDispose = cache1.TotalLiveHandles;

            if (report.LiveHandlesAfterUiDispose != 1)
            {
                report.Violations.Add(
                    "after UI dispose live handles=" +
                    report.LiveHandlesAfterUiDispose + ", expected 1");
            }

            Debug.Log(
                "[FullChain][C] UI scope disposed, liveHandles=" +
                report.LiveHandlesAfterUiDispose);

            // ---- 阶段 D：有活动 Handle 时激活 v2 必须被拒 -------------------
            var withHandles = await activation.ActivateAsync(
                v2Manifest,
                v2Dir,
                FullChainLayout.PlatformKey,
                cache1.TotalLiveHandles,
                true);

            report.V2ActivationWithHandles = withHandles.Status.ToString();

            if (withHandles.Status != PatchActivationStatus.RejectedActiveHandles)
            {
                report.Violations.Add(
                    "activation with live handles returned " + withHandles.Status +
                    ", expected RejectedActiveHandles");
            }

            Debug.Log("[FullChain][D] activate v2 with handles -> " + withHandles.Status);

            // ---- 阶段 E：安全点外激活必须被拒 -------------------------------
            battleResult.Handle.Dispose();

            var unsafePoint = await activation.ActivateAsync(
                v2Manifest,
                v2Dir,
                FullChainLayout.PlatformKey,
                0,
                false);

            report.V2ActivationUnsafePoint = unsafePoint.Status.ToString();

            if (unsafePoint.Status != PatchActivationStatus.RejectedUnsafePoint)
            {
                report.Violations.Add(
                    "activation outside safe point returned " + unsafePoint.Status +
                    ", expected RejectedUnsafePoint");
            }

            Debug.Log("[FullChain][E] activate v2 outside safe point -> " + unsafePoint.Status);

            // ---- 阶段 F：安全点内激活 v2，重建运行时 -----------------------
            var finalActivation = await activation.ActivateAsync(
                v2Manifest,
                v2Dir,
                FullChainLayout.PlatformKey,
                0,
                true);

            report.V2ActivationFinal = finalActivation.Status.ToString();

            if (finalActivation.Status != PatchActivationStatus.Succeeded ||
                activation.ActiveContext == null)
            {
                report.Violations.Add(
                    "v2 activation returned " + finalActivation.Status +
                    ", expected Succeeded");
                return report;
            }

            var policy2 = new AssetMemoryPolicy();
            var diagnostics2 = new AssetRuntimeDiagnostics();
            var loader2 = new CountingBundleLoader();
            var runtime2 = new BundleRuntime(
                activation.ActiveContext.CreateBundleCatalog(),
                loader2,
                policy2,
                diagnostics2);
            var cache2 = new AssetCache(
                activation.ActiveContext.CreateAssetCatalog(),
                runtime2,
                policy2,
                diagnostics2);

            // v1 运行时已完全丢弃：v1/v2 的 Bundle 里存在内容相同的资源，
            // 不彻底卸载会触发 "another AssetBundle with the same files" 冲突。
            AssetBundle.UnloadAllAssetBundles(true);

            Debug.Log(
                "[FullChain][F] v2 active, runtime rebuilt, version=" +
                activation.ActiveContext.ContentVersion);

            // ---- 阶段 G：v2 内容命中 + 回收 --------------------------------
            var v2Scope = cache2.CreateScope("UI.Shop.v2");
            var v2Task = cache2.AcquireAsync<DemoGraphNode>(key, v2Scope, "UI.Shop.v2");
            var v2Result = await v2Task.Completion;

            report.BundleLoadsV2 = loader2.LoadCount;
            report.V2Label = v2Result.Value == null ? "<null>" : v2Result.Value.label;

            if (v2Result.Status != AssetLoadStatus.Succeeded)
            {
                report.Violations.Add("v2 asset load failed: " + v2Result.Status);
                return report;
            }

            if (report.V2Label == report.V1Label)
            {
                report.Violations.Add(
                    "v2 asset label equals v1 label (" + report.V1Label +
                    "), content switch was not observed");
            }

            if (!string.IsNullOrEmpty(report.V2Label) &&
                !report.V2Label.EndsWith(FullChainLayout.V2LabelSuffix, StringComparison.Ordinal))
            {
                report.Violations.Add(
                    "v2 label '" + report.V2Label + "' does not carry the v2 marker");
            }

            v2Result.Handle.Dispose();
            v2Scope.Dispose();

            var evaluator = new AssetMemoryEvaluator(cache2, runtime2)
            {
                MaxUnloadsPerCall = 10
            };

            var firstReclaim = evaluator.Evaluate();
            var secondReclaim = evaluator.Evaluate();

            report.ReclaimedAssets = firstReclaim.AssetsUnloaded + secondReclaim.AssetsUnloaded;
            report.ReclaimedBundles = firstReclaim.BundlesUnloaded + secondReclaim.BundlesUnloaded;
            report.RemainingLiveHandles = cache2.TotalLiveHandles;

            if (report.ReclaimedAssets < 1)
            {
                report.Violations.Add("no asset was reclaimed");
            }

            if (report.ReclaimedBundles < 1)
            {
                report.Violations.Add("no bundle was reclaimed");
            }

            if (report.RemainingLiveHandles != 0)
            {
                report.Violations.Add(
                    "handles still alive after reclaim: " + report.RemainingLiveHandles);
            }

            Debug.Log(
                "[FullChain][G] v2 label=" + report.V2Label +
                " bundleLoadsV2=" + report.BundleLoadsV2 +
                " reclaimedAssets=" + report.ReclaimedAssets +
                " reclaimedBundles=" + report.ReclaimedBundles);

            Debug.Log("[FullChain] summary " + report);
            return report;
        }

        // ------------------------------------------------------------------
        // manifest: 4.3 build output -> 5.1 patch manifest
        // ------------------------------------------------------------------
        /// <summary>
        /// Reads the build pipeline manifest at
        /// <paramref name="contentDirectory"/>. Kept as a facade over
        /// <see cref="BuildManifestReader"/> so callers that only know the
        /// folder keep working.
        /// </summary>
        public static PatchManifest LoadManifest(string contentDirectory)
        {
            return BuildManifestReader.Read(
                Path.Combine(contentDirectory, FullChainLayout.ManifestFileName));
        }

        /// <summary>Counts real bundle loads so "load once" can be asserted.</summary>
        private sealed class CountingBundleLoader : IBundleLoader
        {
            private readonly RealBundleLoader _inner = new RealBundleLoader();
            private int _loadCount;

            public int LoadCount
            {
                get { return _loadCount; }
            }

            public Task<IBundlePayload> LoadAsync(
                BundleInfo bundle,
                CancellationToken cancellationToken)
            {
                _loadCount++;
                return _inner.LoadAsync(bundle, cancellationToken);
            }
        }
    }
}
