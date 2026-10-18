using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Integration.Release.L52;
using UnityEngine;

namespace Game.AssetSystem.Integration.L51
{
    public sealed class ReleaseValidationDemo : MonoBehaviour
    {
        private AssetLoadGate _gate;
        private SafePointGate _safePoint;
        private RealBundlePayloadFactory _realBackend;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!Game.AssetSystem.Lessons.LessonBootGate.Enabled)
            {
                return;
            }

            var existing =
                UnityEngine.Object.FindFirstObjectByType<ReleaseValidationDemo>();

            if (existing != null)
            {
                return;
            }

            var go = new GameObject("[ReleaseValidationDemo]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<ReleaseValidationDemo>();
        }

        private void Awake()
        {
            _gate = new AssetLoadGate
            {
                MaxStartsPerFrame = 1,
                MaxConcurrent = 2
            };

            _safePoint = new SafePointGate();
            _realBackend = new RealBundlePayloadFactory();
        }

        private async void Start()
        {
            Debug.Log(
                "[Lesson52] start mainThreadId=" +
                IntegrationMainThreadPump.MainThreadId);

            var root = Path.Combine(
                Application.persistentDataPath,
                "ReleaseLesson52");

            CleanupRoot(root);

            var contentRoot = Path.Combine(root, "content");
            var stagingRoot = Path.Combine(root, "staging");

            Directory.CreateDirectory(contentRoot);
            Directory.CreateDirectory(stagingRoot);

            await DemoLoadGateBudgetWithSimulatedBackend();
            await DemoSafePointGate();
            await DemoInstalledVersionCleaner();

            Debug.Log(
                "[Lesson52] Finished. " +
                "Real Bundle test requires bundling from lesson 4.3 first. " +
                "Switch Demo factory to RealBundlePayloadFactory wrapped by " +
                "GatedBundlePayloadFactory and provide real AB files in content/vN/.");
        }

        private void Update()
        {
            _gate?.Tick();
        }

        private async Task DemoLoadGateBudgetWithSimulatedBackend()
        {
            Debug.Log("[Scenario1] AssetLoadGate budget with simulated backend");

            var simulated = new SimulatedBundlePayloadFactory(40);
            var gated = new GatedBundlePayloadFactory(simulated, _gate);

            var emptyContext = new ActiveContentContext(
                "gate-demo",
                "local",
                "/tmp",
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
        }

        private async Task DemoSafePointGate()
        {
            Debug.Log("[Scenario2] SafePointGate");

            var context = new ActiveContentContext(
                "safepoint-demo",
                "local",
                "/tmp",
                new[]
                {
                    new RuntimeAsset(new AssetKey("a"), new BundleId(0), "assets/a")
                },
                new[]
                {
                    new RuntimeBundle(new BundleId(0), "b0", "b0", "/tmp/b0", 0, "")
                });

            var factory = new SimulatedBundlePayloadFactory(20);
            var gated = new GatedBundlePayloadFactory(factory, _gate);
            var bundles = new BundleRuntime(context, gated);
            var cache = new AssetCache(context, bundles);
            var scope = cache.CreateScope("SafePointDemo");

            var loadResult = await cache.AcquireAsync<SimulatedAsset>(
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
        }

        private async Task DemoInstalledVersionCleaner()
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
                activeVersion: "v3",
                retainNonActiveVersions: 1);

            Debug.Log("[Scenario3] report=" + report);
            Debug.Log(
                "[Scenario3] deleted=" + string.Join(",", report.DeletedVersions));
            Debug.Log(
                "[Scenario3] retained=" + string.Join(",", report.RetainedVersions));

            // Ensure active and staging survive
            var activeExists = Directory.Exists(
                Path.Combine(contentRoot, "v3"));
            var stagingUsedExists = Directory.Exists(
                Path.Combine(contentRoot, "v4"));

            Debug.Log(
                "[Scenario3] activeExists=" + activeExists +
                " stagingUsedStillExists=" + stagingUsedExists);

            await Task.Yield();
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

        private static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(bytes.Length * 2);

                for (var i = 0; i < bytes.Length; i++)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }
}
