using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Core;
using Game.AssetSystem.Integration;
using Game.AssetSystem.Release;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// L5.2 — release-time gates.
    ///
    /// Scenario 1: the load gate keeps bundle starts within a per-frame budget.
    /// Scenario 2: the safe-point gate refuses activation while a handle is live.
    /// Scenario 3: installed-version cleanup keeps the active version and the
    /// staging version.
    /// </summary>
    public sealed class L52ReleaseValidationDemo : LessonDemoBase
    {
        private AssetLoadGate _gate;
        private SafePointGate _safePoint;

        public override string Title
        {
            get { return "L5.2 发布校验：加载闸门 / 安全点 / 版本清理"; }
        }

        public override void Tick()
        {
            _gate?.Tick();
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            _gate = new AssetLoadGate
            {
                MaxStartsPerFrame = 1,
                MaxConcurrent = 2
            };

            _safePoint = new SafePointGate();

            Debug.Log(
                "[Lesson52] start mainThreadId=" + MainThreadPump.MainThreadId);

            var root = Path.Combine(
                Application.persistentDataPath,
                "ReleaseLesson52");

            CleanupRoot(root);

            var contentRoot = Path.Combine(root, "content");
            var stagingRoot = Path.Combine(root, "staging");

            Directory.CreateDirectory(contentRoot);
            Directory.CreateDirectory(stagingRoot);

            await DemoLoadGateBudgetWithSimulatedBackend(cancellationToken);
            ThrowIfCanceled(cancellationToken);
            await DemoSafePointGate(cancellationToken);
            ThrowIfCanceled(cancellationToken);
            await DemoInstalledVersionCleaner(cancellationToken);

            Debug.Log(
                "[Lesson52] Finished. " +
                "The real-bundle path requires content built by the L4.3 builder. " +
                "Swap LessonContentPayloadFactory for RealBundlePayloadFactory wrapped " +
                "by GatedBundlePayloadFactory and provide real AB files in content/vN/.");
        }

        private async Task DemoLoadGateBudgetWithSimulatedBackend(
            CancellationToken cancellationToken)
        {
            Debug.Log("[Scenario1] AssetLoadGate budget with a simulated backend");

            var simulated = new LessonContentPayloadFactory(40);
            var gated = new GatedBundlePayloadFactory(simulated, _gate);

            var emptyContext = new ActiveContentContext(
                "gate-demo",
                "local",
                Application.persistentDataPath,
                Array.Empty<RuntimeAsset>(),
                new[]
                {
                    new RuntimeBundle(new BundleId(0), "b0", "b0", "/tmp/b0", 0, ""),
                    new RuntimeBundle(new BundleId(1), "b1", "b1", "/tmp/b1", 0, ""),
                    new RuntimeBundle(new BundleId(2), "b2", "b2", "/tmp/b2", 0, ""),
                    new RuntimeBundle(new BundleId(3), "b3", "b3", "/tmp/b3", 0, ""),
                    new RuntimeBundle(new BundleId(4), "b4", "b4", "/tmp/b4", 0, ""),
                    new RuntimeBundle(new BundleId(5), "b5", "b5", "/tmp/b5", 0, "")
                });

            var bundles = new BundleRuntime(emptyContext, gated);

            var tasks = new Task<BundleAcquireResult>[6];

            for (var i = 0; i < tasks.Length; i++)
            {
                tasks[i] = bundles.AcquireAsync(
                    new BundleId(i),
                    "S1." + i);
            }

            Debug.Log(
                "[Scenario1] enqueued pending=" + _gate.PendingCount +
                " running=" + _gate.RunningCount);

            var results = await Task.WhenAll(tasks);

            var succeeded = 0;

            for (var i = 0; i < results.Length; i++)
            {
                if (results[i].Success)
                {
                    succeeded++;
                    results[i].Lease.Dispose();
                }
            }

            Debug.Log(
                "[Scenario1] succeeded=" + succeeded +
                " totalStarted=" + _gate.TotalStarted +
                " totalCompleted=" + _gate.TotalCompleted +
                " lastFrameStarts=" + _gate.LastFrameStarts);

            var reclaimReport = new AssetMemoryEvaluator(
                new AssetCache(emptyContext, bundles),
                bundles).Evaluate();

            Debug.Log("[Scenario1] reclaim=" + reclaimReport);

            await Task.Yield();
            ThrowIfCanceled(cancellationToken);
        }

        private async Task DemoSafePointGate(CancellationToken cancellationToken)
        {
            Debug.Log("[Scenario2] SafePointGate");

            var context = new ActiveContentContext(
                "safepoint-demo",
                "local",
                Application.persistentDataPath,
                new[]
                {
                    new RuntimeAsset(new AssetKey("a"), new BundleId(0), "assets/a")
                },
                new[]
                {
                    new RuntimeBundle(new BundleId(0), "b0", "b0", "/tmp/b0", 0, "")
                });

            var factory = new LessonContentPayloadFactory(20);
            var gated = new GatedBundlePayloadFactory(factory, _gate);
            var bundles = new BundleRuntime(context, gated);
            var cache = new AssetCache(context, bundles);
            var scope = cache.CreateScope("SafePointDemo");

            var loadResult = await cache.AcquireAsync<LessonAssetPayload>(
                new AssetKey("a"),
                scope,
                "SafePoint.Owner");

            Debug.Log(
                "[Scenario2] loaded=" + loadResult.Status +
                " liveHandles=" + cache.TotalLiveHandles +
                " hasSafePoint=" + _safePoint.IsSafePoint);

            var blockedBySafePoint =
                _safePoint.CanActivate(cache) == false &&
                cache.TotalLiveHandles > 0;

            _safePoint.Enter("MainMenu");

            Debug.Log(
                "[Scenario2] enter MainMenu -> safePoint=" + _safePoint.IsSafePoint +
                " canActivate=" + _safePoint.CanActivate(cache));

            if (loadResult.Handle != null)
            {
                loadResult.Handle.Dispose();
            }

            scope.Dispose();

            var canActivateAfterRelease = _safePoint.CanActivate(cache);

            Debug.Log(
                "[Scenario2] after release safePoint=" + _safePoint.IsSafePoint +
                " liveHandles=" + cache.TotalLiveHandles +
                " canActivate=" + canActivateAfterRelease +
                " blockedByHandleBefore=" + blockedBySafePoint);

            _safePoint.Enter("SafePoint.Pause");

            var blockedByPending =
                _safePoint.CanActivate(cache) == false &&
                cache.LoadingAssetCount > 0;

            Debug.Log("[Scenario2] blockedByPending=" + blockedByPending);

            _safePoint.Exit("SafePoint.Pause");
            _safePoint.Exit("MainMenu");

            await Task.Yield();
            ThrowIfCanceled(cancellationToken);
        }

        private static async Task DemoInstalledVersionCleaner(
            CancellationToken cancellationToken)
        {
            Debug.Log("[Scenario3] InstalledVersionCleaner");

            var root = Path.Combine(
                Application.persistentDataPath,
                "ReleaseLesson52Cleaner");

            CleanupRoot(root);

            var contentRoot = Path.Combine(root, "content");
            var stagingRoot = Path.Combine(root, "staging");

            Directory.CreateDirectory(contentRoot);
            Directory.CreateDirectory(stagingRoot);

            CreateFakeVersion(contentRoot, "v1");
            CreateFakeVersion(contentRoot, "v2");
            CreateFakeVersion(contentRoot, "v3");
            CreateFakeVersion(contentRoot, "v4");

            Directory.CreateDirectory(Path.Combine(stagingRoot, "v4"));

            var cleaner = new InstalledVersionCleaner();

            var report = cleaner.Clean(
                contentRoot,
                stagingRoot,
                "v3",
                1);

            Debug.Log("[Scenario3] report=" + report);
            Debug.Log(
                "[Scenario3] deleted=" + string.Join(",", report.DeletedVersions));
            Debug.Log(
                "[Scenario3] retained=" + string.Join(",", report.RetainedVersions));

            var activeExists = Directory.Exists(
                Path.Combine(contentRoot, "v3"));

            var stagingUsedExists = Directory.Exists(
                Path.Combine(contentRoot, "v4"));

            Debug.Log(
                "[Scenario3] activeExists=" + activeExists +
                " stagingUsedStillExists=" + stagingUsedExists);

            await Task.Yield();
            ThrowIfCanceled(cancellationToken);
        }

        private static void CreateFakeVersion(
            string contentRoot,
            string version)
        {
            var dir = Path.Combine(contentRoot, version);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(
                Path.Combine(dir, "bundle_core"),
                Encoding.UTF8.GetBytes("fake-" + version));
        }

        private static void CleanupRoot(string root)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            try
            {
                Directory.Delete(root, true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Lesson52] cleanup failed: " + exception.Message);
            }
        }
    }
}
