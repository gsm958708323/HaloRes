using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Assets;
using Game.AssetSystem.Core;
using Game.AssetSystem.Patch;
using Game.AssetSystem.Memory;

namespace Game.AssetSystem.Demos.FullFeature
{
    /// <summary>Machine readable result of one scripted demo run.</summary>
    public sealed class FullFeatureScenarioReport
    {
        public bool ContentFound;
        public string InitialVersion;

        public string PanelLabel;
        public string SharedVersionV1;
        public int BundlesAfterPanel;
        public int BundlesAfterFx;
        public int BundlesAfterCached;
        public bool SharedBundleReused;
        public bool CachedHandleSharesInstance;
        public int HandlesAfterRelease;
        public int IdleAssetsAfterRelease;
        public int ReadyBundlesAfterRelease;
        public int LoadedPayloadsAfterRelease;
        public bool ReclaimedAfterCritical;

        public string InterruptedDownload;
        public bool PartialPartKept;
        public string FullDownload;
        public long ResumeBytes;

        public string ActivationWithHandles;
        public string ActivationUnsafePoint;
        public string ActivationFinal;
        public string ActiveVersionAfterSwitch;
        public string SharedVersionV2;
        public int LoadedPayloadsAfterFinalReclaim;

        public readonly List<string> Violations = new List<string>();

        public bool Passed
        {
            get { return ContentFound && Violations.Count == 0; }
        }

        public override string ToString()
        {
            return "initial=" + InitialVersion +
                   " panelBundles=" + BundlesAfterPanel +
                   " fxBundles=" + BundlesAfterFx +
                   " cachedBundles=" + BundlesAfterCached +
                   " sharedReused=" + SharedBundleReused +
                   " sameInstance=" + CachedHandleSharesInstance +
                   " handlesAfterRelease=" + HandlesAfterRelease +
                   " idleAssets=" + IdleAssetsAfterRelease +
                   " readyBundles=" + ReadyBundlesAfterRelease +
                   " payloads=" + LoadedPayloadsAfterRelease +
                   " reclaimed=" + ReclaimedAfterCritical +
                   " interrupted=" + InterruptedDownload +
                   " partKept=" + PartialPartKept +
                   " full=" + FullDownload +
                   " resume=" + ResumeBytes +
                   " withHandles=" + ActivationWithHandles +
                   " unsafePoint=" + ActivationUnsafePoint +
                   " final=" + ActivationFinal +
                   " active=" + ActiveVersionAfterSwitch +
                   " sharedV2=" + SharedVersionV2 +
                   " payloadsAfterReclaim=" + LoadedPayloadsAfterFinalReclaim +
                   " violations=" + Violations.Count;
        }
    }

    /// <summary>
    /// Walks the demo's four interaction paths (A share/release, B interrupt /
    /// resume / activate, C activation rejection, D memory reclaim) against a
    /// <see cref="FullFeatureSession"/>.
    ///
    /// The dashboard performs exactly the same calls; this exists so the paths
    /// can be asserted in PlayMode instead of clicked by hand.
    /// </summary>
    public static class FullFeatureScenario
    {
        public static async Task<FullFeatureScenarioReport> RunAsync(
            FullFeatureSession session)
        {
            MainThreadPump.EnsureMainThread(nameof(RunAsync));

            var report = new FullFeatureScenarioReport();

            if (!await session.InitializeAsync())
            {
                report.Violations.Add("initialize failed: " + session.InitError);
                return report;
            }

            report.ContentFound = true;
            report.InitialVersion = session.ActiveVersion;

            if (!string.Equals(
                report.InitialVersion,
                FullFeatureLayout.BaseVersion,
                StringComparison.Ordinal))
            {
                report.Violations.Add(
                    "initial version is " + report.InitialVersion +
                    ", expected " + FullFeatureLayout.BaseVersion);
            }

            // ---- 路径 A：共享加载 ---------------------------------------
            var panelA = await session.Load(FullFeatureContent.PanelKey, "Scenario.A");

            if (panelA.Status != AssetLoadStatus.Succeeded)
            {
                report.Violations.Add("panel load failed: " + panelA.Error);
                return report;
            }

            report.PanelLabel = panelA.Value.label;
            report.SharedVersionV1 = panelA.Value.shared == null
                ? "<null>"
                : panelA.Value.shared.version;
            report.BundlesAfterPanel = session.Bundles.LoadedPayloadCount;

            if (report.BundlesAfterPanel != 2)
            {
                report.Violations.Add(
                    "ui_shop load loaded " + report.BundlesAfterPanel +
                    " payloads, expected 2 (ui_shop + shared)");
            }

            var hitB = await session.Load(FullFeatureContent.HitKey, "Scenario.B");

            if (hitB.Status != AssetLoadStatus.Succeeded)
            {
                report.Violations.Add("fx load failed: " + hitB.Error);
                return report;
            }

            report.BundlesAfterFx = session.Bundles.LoadedPayloadCount;
            report.SharedBundleReused = report.BundlesAfterFx == 3;

            if (!report.SharedBundleReused)
            {
                report.Violations.Add(
                    "after battle_fx payloads=" + report.BundlesAfterFx +
                    ", expected 3 (shared must not load twice)");
            }

            var panelC = await session.Load(FullFeatureContent.PanelKey, "Scenario.C");

            if (panelC.Status != AssetLoadStatus.Succeeded)
            {
                report.Violations.Add("cached load failed: " + panelC.Error);
                return report;
            }

            report.CachedHandleSharesInstance =
                ReferenceEquals(panelA.Value, panelC.Value);
            report.BundlesAfterCached = session.Bundles.LoadedPayloadCount;

            if (!report.CachedHandleSharesInstance)
            {
                report.Violations.Add(
                    "second ui/panel handle got a different asset instance");
            }

            if (report.BundlesAfterCached != 3)
            {
                report.Violations.Add(
                    "cached hit loaded a bundle again: payloads=" +
                    report.BundlesAfterCached);
            }

            report.HandlesAfterRelease = session.ReleaseAllScopes();
            report.IdleAssetsAfterRelease = session.Cache.IdleAssetCount;
            report.ReadyBundlesAfterRelease = session.Bundles.ReadyBundleCount;
            report.LoadedPayloadsAfterRelease = session.Bundles.LoadedPayloadCount;

            if (report.HandlesAfterRelease != 0)
            {
                report.Violations.Add(
                    "handles after release=" + report.HandlesAfterRelease);
            }

            if (report.IdleAssetsAfterRelease != 2)
            {
                report.Violations.Add(
                    "idle assets=" + report.IdleAssetsAfterRelease +
                    ", expected 2 (ui/panel, fx/hit)");
            }

            // Releasing a handle does not unload anything: the cache keeps the
            // payload and its bundle lease so the next acquire is a cache hit.
            if (report.ReadyBundlesAfterRelease != 3)
            {
                report.Violations.Add(
                    "ready bundles after release=" +
                    report.ReadyBundlesAfterRelease +
                    ", expected 3 (payloads retained for reuse)");
            }

            if (report.LoadedPayloadsAfterRelease != 3)
            {
                report.Violations.Add(
                    "loaded payloads after release=" +
                    report.LoadedPayloadsAfterRelease + ", expected 3");
            }

            // ---- 路径 D（前半）：Critical 回收 ---------------------------
            session.SetMemoryPressure(MemoryPressureLevel.Critical);
            ReclaimUntilSettled(session);

            report.ReclaimedAfterCritical =
                session.Cache.IdleAssetCount == 0 &&
                session.Bundles.LoadedPayloadCount == 0;

            if (!report.ReclaimedAfterCritical)
            {
                report.Violations.Add(
                    "critical reclaim left idleAssets=" +
                    session.Cache.IdleAssetCount +
                    " payloads=" + session.Bundles.LoadedPayloadCount);
            }

            // ---- 路径 B：中断 → 续传 → 激活 -----------------------------
            using (var cancellation = new CancellationTokenSource())
            {
                var interrupted = await session.DownloadAsync(
                    "v2",
                    70,
                    true,
                    cancellation.Token);

                report.InterruptedDownload = interrupted.Status.ToString();
                report.PartialPartKept = HasPartialFile(session, "v2");

                if (interrupted.Status == PatchApplyStatus.Succeeded)
                {
                    report.Violations.Add(
                        "70% interrupt completed the download anyway");
                }

                if (!report.PartialPartKept)
                {
                    report.Violations.Add(
                        "interrupted download left no .part file for resume");
                }

                var full = await session.DownloadAsync(
                    "v2",
                    0,
                    true,
                    cancellation.Token);

                report.FullDownload = full.Status.ToString();
                report.ResumeBytes = full.ResumeBytes;

                if (full.Status != PatchApplyStatus.Succeeded)
                {
                    report.Violations.Add(
                        "full download failed: " + full.Message);
                }

                if (full.ResumeBytes <= 0)
                {
                    report.Violations.Add(
                        "resume bytes=" + full.ResumeBytes +
                        ", expected the .part length to be reused");
                }
            }

            // ---- 路径 C：激活拒绝与成功 ---------------------------------
            var held = await session.Load(FullFeatureContent.PanelKey, "Scenario.Hold");

            if (held.Status != AssetLoadStatus.Succeeded)
            {
                report.Violations.Add("hold load failed: " + held.Error);
                return report;
            }

            var withHandles = await session.ActivateAsync("v2");
            report.ActivationWithHandles = withHandles.Status.ToString();

            if (withHandles.Status != PatchActivationStatus.RejectedActiveHandles)
            {
                report.Violations.Add(
                    "activation with live handles returned " +
                    withHandles.Status + ", expected RejectedActiveHandles");
            }

            session.ReleaseAllScopes();
            session.SafePoint.Exit("MainMenu");

            var unsafePoint = await session.ActivateAsync("v2");
            report.ActivationUnsafePoint = unsafePoint.Status.ToString();

            if (unsafePoint.Status != PatchActivationStatus.RejectedUnsafePoint)
            {
                report.Violations.Add(
                    "activation outside safe point returned " +
                    unsafePoint.Status + ", expected RejectedUnsafePoint");
            }

            session.SafePoint.Enter("MainMenu");

            var final = await session.ActivateAsync("v2");
            report.ActivationFinal = final.Status.ToString();
            report.ActiveVersionAfterSwitch = session.ActiveVersion;

            if (final.Status != PatchActivationStatus.Succeeded)
            {
                report.Violations.Add(
                    "activation returned " + final.Status +
                    ": " + final.Message);
                return report;
            }

            var panelV2 = await session.Load(FullFeatureContent.PanelKey, "Scenario.V2");

            if (panelV2.Status != AssetLoadStatus.Succeeded)
            {
                report.Violations.Add("v2 load failed: " + panelV2.Error);
                return report;
            }

            report.SharedVersionV2 = panelV2.Value.shared == null
                ? "<null>"
                : panelV2.Value.shared.version;

            if (!string.Equals(report.SharedVersionV2, "v2", StringComparison.Ordinal))
            {
                report.Violations.Add(
                    "shared version after switch is " + report.SharedVersionV2 +
                    ", expected v2");
            }

            if (string.Equals(
                report.PanelLabel,
                panelV2.Value.label,
                StringComparison.Ordinal))
            {
                report.Violations.Add(
                    "v2 panel label equals v1 label (" + report.PanelLabel +
                    "), content switch was not observed");
            }

            // ---- 路径 D（后半）：切换后再回收 ---------------------------
            session.ReleaseAllScopes();
            session.SetMemoryPressure(MemoryPressureLevel.Critical);
            ReclaimUntilSettled(session);

            report.LoadedPayloadsAfterFinalReclaim =
                session.Bundles.LoadedPayloadCount;

            if (report.LoadedPayloadsAfterFinalReclaim != 0)
            {
                report.Violations.Add(
                    "payloads still loaded after final reclaim: " +
                    report.LoadedPayloadsAfterFinalReclaim);
            }

            return report;
        }

        /// <summary>
        /// Runs the frame budget until the pressure policy settles. One pass is
        /// not enough: unloading a bundle releases its dependency leases, which
        /// makes the shared bundle idle only on the next pass.
        /// </summary>
        private static void ReclaimUntilSettled(FullFeatureSession session)
        {
            for (var i = 0;
                 i < 16 && session.Pressure != MemoryPressureLevel.Normal;
                 i++)
            {
                session.Tick();
            }
        }

        private static bool HasPartialFile(
            FullFeatureSession session,
            string version)
        {
            var directory = Path.Combine(session.StagingRoot, version);

            if (!Directory.Exists(directory))
            {
                return false;
            }

            return Directory.GetFiles(directory, "*.part").Length > 0;
        }
    }
}
