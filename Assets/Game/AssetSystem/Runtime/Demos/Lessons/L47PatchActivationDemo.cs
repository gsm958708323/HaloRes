using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Assets;
using Game.AssetSystem.Core;
using Game.AssetSystem.Patch;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// L4.7 — patch activation guards.
    ///
    /// Ten scenarios: cold start, platform mismatch, corrupted bundle, live
    /// handle, release then activate, already active, wrong hash, correct hash,
    /// resolve an asset from the active context, and a pre-canceled token.
    /// </summary>
    public sealed class L47PatchActivationDemo : LessonDemoBase
    {
        private const string Platform = "StandaloneWindows64";

        public override string Title
        {
            get { return "L4.7 补丁激活：平台 / 哈希 / 活跃句柄 / 安全点"; }
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            Debug.Log(
                "[Lesson47] start mainThreadId=" + MainThreadPump.MainThreadId);

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

            var controller = new PatchActivationService();

            Debug.Log("[Scenario1] cold-start activate v1");
            var r1 = await controller.ActivateAsync(
                v1Manifest, v1Dir, Platform, 0, true, cancellationToken);
            LogResult("Scenario1", r1);
            LogContext("Scenario1", controller);

            ThrowIfCanceled(cancellationToken);

            Debug.Log("[Scenario2] activate v2 with the wrong platform");
            var r2 = await controller.ActivateAsync(
                v2Manifest, v2Dir, "Android", 0, true, cancellationToken);
            LogResult("Scenario2", r2);
            LogContext("Scenario2", controller);

            ThrowIfCanceled(cancellationToken);

            Debug.Log("[Scenario3] activate v2 with a corrupted file");
            var r3 = await controller.ActivateAsync(
                v2Manifest, corruptDir, Platform, 0, true, cancellationToken);
            LogResult("Scenario3", r3);
            LogContext("Scenario3", controller);

            ThrowIfCanceled(cancellationToken);

            Debug.Log("[Scenario4] activate v2 while a handle is alive");
            var r4 = await controller.ActivateAsync(
                v2Manifest, v2Dir, Platform, 1, true, cancellationToken);
            LogResult("Scenario4", r4);
            LogContext("Scenario4", controller);

            ThrowIfCanceled(cancellationToken);

            Debug.Log("[Scenario5] release the handle, then activate v2");
            var r5 = await controller.ActivateAsync(
                v2Manifest, v2Dir, Platform, 0, true, cancellationToken);
            LogResult("Scenario5", r5);
            LogContext("Scenario5", controller);

            ThrowIfCanceled(cancellationToken);

            Debug.Log("[Scenario6] activate v2 again (AlreadyActive)");
            var r6 = await controller.ActivateAsync(
                v2Manifest, v2Dir, Platform, 0, true, cancellationToken);
            LogResult("Scenario6", r6);
            LogContext("Scenario6", controller);

            ThrowIfCanceled(cancellationToken);

            Debug.Log("[Scenario7] activate v3 with a wrong hash");
            var r7 = await controller.ActivateAsync(
                badV3Manifest, v3Dir, Platform, 0, true, cancellationToken);
            LogResult("Scenario7", r7);
            LogContext("Scenario7", controller);

            ThrowIfCanceled(cancellationToken);

            Debug.Log("[Scenario8] activate v3 with the correct hash");
            var r8 = await controller.ActivateAsync(
                v3Manifest, v3Dir, Platform, 0, true, cancellationToken);
            LogResult("Scenario8", r8);
            LogContext("Scenario8", controller);

            ThrowIfCanceled(cancellationToken);

            Debug.Log("[Scenario9] resolve ui/root from the active context");
            ScenarioResolveActiveAsset(controller);

            Debug.Log("[Scenario10] activate v2 with a pre-canceled token");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                var r10 = await controller.ActivateAsync(
                    v2Manifest, v2Dir, Platform, 0, true, cts.Token);

                LogResult("Scenario10", r10);
                LogContext("Scenario10", controller);
            }

            Debug.Log(
                "[Lesson47] Finished. " +
                "Experiment 1: bypass the live-handle check; activation with a live handle " +
                "will succeed and show how the active context gets replaced incorrectly. " +
                "Experiment 2: mutate ActiveContext after a successful activation; " +
                "the runtime will see a partial content mutation.");
        }

        private static void LogResult(string label, PatchActivationResult result)
        {
            Debug.Log(
                "[" + label + "] status=" + result.Status +
                " before=" + (result.ActiveVersionBefore ?? "<none>") +
                " after=" + (result.ActiveVersionAfter ?? "<none>") +
                " bundlesChecked=" + result.BundlesChecked +
                " message=" + result.Message);
        }

        private static void LogContext(
            string label,
            PatchActivationService controller)
        {
            var context = controller.ActiveContext;

            if (context == null)
            {
                Debug.Log("[" + label + "] activeVersion=<none>");
                return;
            }

            Debug.Log(
                "[" + label + "] activeVersion=" + context.ContentVersion +
                " platform=" + context.PlatformKey +
                " assetCount=" + context.AssetCount +
                " bundleCount=" + context.BundleCount +
                " activationCount=" + controller.ActivationCount);
        }

        private static void ScenarioResolveActiveAsset(
            PatchActivationService controller)
        {
            if (controller.ActiveContext == null)
            {
                Debug.LogError("[Scenario9] no active context.");
                return;
            }

            var context = controller.ActiveContext;

            if (!context.TryGetAsset(new AssetKey("ui/root"), out var asset))
            {
                Debug.LogError("[Scenario9] ui/root not found in the active context.");
                return;
            }

            if (!context.TryGetBundle(asset.BundleId, out var bundle))
            {
                Debug.LogError(
                    "[Scenario9] bundle " + asset.BundleId + " not found.");
                return;
            }

            Debug.Log(
                "[Scenario9] ui/root -> bundleGroup=" + bundle.BundleGroupName +
                " fileName=" + bundle.FileName +
                " absolute=" + bundle.AbsolutePath +
                " internal=" + asset.InternalPath);
        }

        private static PatchManifest BuildManifest(
            string contentVersion,
            string bundleFileName,
            long sizeBytes,
            string sha256Hex)
        {
            return new PatchManifest
            {
                SchemaVersion = 1,
                ContentVersion = contentVersion,
                PlatformKey = Platform,
                Bundles = new[]
                {
                    new PatchBundleDescriptor
                    {
                        BundleGroupName = bundleFileName,
                        FileName = bundleFileName,
                        SizeBytes = sizeBytes,
                        Sha256Hex = sha256Hex
                    }
                },
                Assets = new[]
                {
                    new PatchAssetDescriptor
                    {
                        AssetKey = "ui/root",
                        BundleGroupName = bundleFileName,
                        InternalPath = "assets/ui/root"
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
            for (var i = 0; i < buffer.Length; i++)
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

                for (var i = 0; i < hash.Length; i++)
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
