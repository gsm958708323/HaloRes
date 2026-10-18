using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Infrastructure.L47;
using Game.AssetSystem.Patch.L47;
using UnityEngine;

namespace Game.AssetSystem.Demo.L47
{
    public sealed class PatchActivationLessonBootstrap : MonoBehaviour
    {
        private sealed class SimulatedHandleCounter : IActiveHandleCounter
        {
            private int _count;

            public int Count
            {
                get { return Volatile.Read(ref _count); }
            }

            public void Open()
            {
                Interlocked.Increment(ref _count);
            }

            public void Close()
            {
                Interlocked.Decrement(ref _count);
            }
        }

        private const string Platform = "StandaloneWindows64";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!Game.AssetSystem.Lessons.LessonBootGate.Enabled)
            {
                return;
            }

            var existing =
                UnityEngine.Object.FindFirstObjectByType<PatchActivationLessonBootstrap>();

            if (existing != null)
            {
                return;
            }

            var go = new GameObject("[PatchActivationLessonBootstrap]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<PatchActivationLessonBootstrap>();
        }

        private async void Start()
        {
            Debug.Log(
                "[Lesson47] start mainThreadId=" +
                UnityMainThreadPump.MainThreadId);

            var root = Path.Combine(
                Application.persistentDataPath,
                "PatchActivationLesson47");

            var platformRoot = Path.Combine(root, Platform);
            var v1Dir = Path.Combine(platformRoot, "v1");
            var v2Dir = Path.Combine(platformRoot, "v2");
            var v3Dir = Path.Combine(platformRoot, "v3");
            var corruptDir = Path.Combine(platformRoot, "v2-corrupt");

            CleanupRoot(root);

            var v1Bytes = new byte[4096];
            var v2Bytes = new byte[6144];
            var v3Bytes = new byte[8192];

            FillBytes(v1Bytes, 11);
            FillBytes(v2Bytes, 23);
            FillBytes(v3Bytes, 47);

            var v1Hash = ComputeSha256Hex(v1Bytes);
            var v2Hash = ComputeSha256Hex(v2Bytes);
            var v3Hash = ComputeSha256Hex(v3Bytes);

            WriteBundleFile(v1Dir, "bundle_core", v1Bytes);
            WriteBundleFile(v2Dir, "bundle_core", v2Bytes);
            WriteBundleFile(v3Dir, "bundle_core", v3Bytes);

            var corruptBytes = CloneBytes(v2Bytes);
            corruptBytes[0] ^= 0xFF;
            WriteBundleFile(corruptDir, "bundle_core", corruptBytes);

            var v1Manifest = BuildManifest(
                "v1", "bundle_core", v1Bytes.Length, v1Hash);

            var v2Manifest = BuildManifest(
                "v2", "bundle_core", v2Bytes.Length, v2Hash);

            var v3Manifest = BuildManifest(
                "v3", "bundle_core", v3Bytes.Length, v3Hash);

            var badV3Manifest = BuildManifest(
                "v3",
                "bundle_core",
                v3Bytes.Length,
                new string('f', 64));

            var handleCounter = new SimulatedHandleCounter();
            var controller = new PatchActivationController(handleCounter);

            // Scenario 1
            Debug.Log("[Scenario1] cold-start activate v1");
            var r1 = await controller.ActivateAsync(
                v1Manifest, v1Dir, Platform);
            LogResult("Scenario1", r1);
            LogContext("Scenario1", controller);

            // Scenario 2
            Debug.Log("[Scenario2] activate v2 with wrong platform");
            var r2 = await controller.ActivateAsync(
                v2Manifest, v2Dir, "Android");
            LogResult("Scenario2", r2);
            LogContext("Scenario2", controller);

            // Scenario 3
            Debug.Log("[Scenario3] activate v2 with corrupted file");
            var r3 = await controller.ActivateAsync(
                v2Manifest, corruptDir, Platform);
            LogResult("Scenario3", r3);
            LogContext("Scenario3", controller);

            // Scenario 4
            Debug.Log("[Scenario4] activate v2 while a Handle is alive");
            handleCounter.Open();
            var r4 = await controller.ActivateAsync(
                v2Manifest, v2Dir, Platform);
            LogResult("Scenario4", r4);
            LogContext("Scenario4", controller);

            // Scenario 5
            Debug.Log("[Scenario5] release Handle then activate v2");
            handleCounter.Close();
            var r5 = await controller.ActivateAsync(
                v2Manifest, v2Dir, Platform);
            LogResult("Scenario5", r5);
            LogContext("Scenario5", controller);

            // Scenario 6
            Debug.Log("[Scenario6] activate v2 again (AlreadyActive)");
            var r6 = await controller.ActivateAsync(
                v2Manifest, v2Dir, Platform);
            LogResult("Scenario6", r6);
            LogContext("Scenario6", controller);

            // Scenario 7
            Debug.Log("[Scenario7] activate v3 with wrong hash");
            var r7 = await controller.ActivateAsync(
                badV3Manifest, v3Dir, Platform);
            LogResult("Scenario7", r7);
            LogContext("Scenario7", controller);

            // Scenario 8
            Debug.Log("[Scenario8] activate v3 with correct hash");
            var r8 = await controller.ActivateAsync(
                v3Manifest, v3Dir, Platform);
            LogResult("Scenario8", r8);
            LogContext("Scenario8", controller);

            // Scenario 9
            Debug.Log("[Scenario9] resolve ui/root from active context");
            await ScenarioResolveActiveAsset(controller);

            // Scenario 10
            Debug.Log("[Scenario10] activate v2 with cancel token");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var r10 = await controller.ActivateAsync(
                    v2Manifest, v2Dir, Platform, cts.Token);
                LogResult("Scenario10", r10);
                LogContext("Scenario10", controller);
            }

            Debug.Log(
                "[Lesson47] Finished. " +
                "Experiment 1: bypass handle counter check; activation with a live handle " +
                "will Succeed and show how the active context gets replaced incorrectly. " +
                "Experiment 2: mutate ActiveContext after a successful activation; " +
                "the runtime will see partial content mutation.");
        }

        private static void LogResult(string label, PatchActivationResult r)
        {
            Debug.Log(
                "[" + label + "] status=" + r.Status +
                " code=" + r.ErrorCode +
                " before=" + (r.ActiveVersionBefore ?? "<none>") +
                " after=" + (r.ActiveVersionAfter ?? "<none>") +
                " bundlesChecked=" + r.BundlesChecked +
                " message=" + r.Message);
        }

        private static void LogContext(
            string label,
            PatchActivationController controller)
        {
            var ctx = controller.ActiveContext;

            if (ctx == null)
            {
                Debug.Log("[" + label + "] activeVersion=<none>");
                return;
            }

            Debug.Log(
                "[" + label + "] activeVersion=" + ctx.ContentVersion +
                " platform=" + ctx.PlatformKey +
                " assetCount=" + ctx.AssetCount +
                " bundleCount=" + ctx.BundleCount +
                " activationCount=" + controller.ActivationCount);
        }

        private static async Task ScenarioResolveActiveAsset(
            PatchActivationController controller)
        {
            await Task.Yield();

            if (controller.ActiveContext == null)
            {
                Debug.LogError("[Scenario9] no active context.");
                return;
            }

            var ctx = controller.ActiveContext;

            if (!ctx.TryGetAsset(new RuntimeAssetKey("ui/root"), out var asset))
            {
                Debug.LogError("[Scenario9] ui/root not found in active context.");
                return;
            }

            if (!ctx.TryGetBundle(asset.BundleId, out var bundle))
            {
                Debug.LogError(
                    "[Scenario9] bundle " + asset.BundleId + " not found.");
                return;
            }

            Debug.Log(
                "[Scenario9] ui/root -> bundleGroup=" + bundle.BundleGroupName +
                " fileName=" + bundle.FileName +
                " absolute=" + bundle.AbsolutePath +
                " internal=" + asset.InternalAssetPath);
        }

        private static PatchManifestDescriptor BuildManifest(
            string contentVersion,
            string bundleFileName,
            long sizeBytes,
            string sha256Hex)
        {
            return new PatchManifestDescriptor
            {
                schemaVersion = 1,
                contentVersion = contentVersion,
                targetPlatformKey = Platform,
                bundles = new[]
                {
                    new PatchBundleDescriptor
                    {
                        bundleFileName = bundleFileName,
                        bundleGroupName = bundleFileName,
                        sizeBytes = sizeBytes,
                        sha256Hex = sha256Hex
                    }
                },
                assets = new[]
                {
                    new PatchAssetDescriptor
                    {
                        assetKey = "ui/root",
                        bundleGroupName = bundleFileName,
                        internalAssetPath = "assets/ui/root"
                    }
                }
            };
        }

        private static void WriteBundleFile(
            string directory,
            string fileName,
            byte[] bytes)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, fileName), bytes);
        }

        private static void FillBytes(byte[] buffer, byte seed)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (byte)((i * 31 + seed) & 0xFF);
            }
        }

        private static byte[] CloneBytes(byte[] source)
        {
            var clone = new byte[source.Length];
            Buffer.BlockCopy(source, 0, clone, 0, source.Length);
            return clone;
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);

                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
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
                    "[Lesson47] cleanup failed: " + exception.Message);
            }
        }
    }
}
