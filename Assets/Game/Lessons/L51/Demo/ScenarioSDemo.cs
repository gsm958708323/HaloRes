using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.AssetSystem.Integration.L51
{
    public sealed class ScenarioSDemo : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!Game.AssetSystem.Lessons.LessonBootGate.Enabled)
            {
                return;
            }

            var existing =
                UnityEngine.Object.FindFirstObjectByType<ScenarioSDemo>();

            if (existing != null)
            {
                return;
            }

            var go = new GameObject("[ScenarioSDemo]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<ScenarioSDemo>();
        }

        private const string PlatformKey = "StandaloneWindows64";

        private async void Start()
        {
            Debug.Log(
                "[Lesson51] start mainThreadId=" +
                IntegrationMainThreadPump.MainThreadId +
                " isMainThread=" +
                IntegrationMainThreadPump.IsMainThread);

            var root = Path.Combine(
                Application.persistentDataPath,
                "IntegrationLesson51");

            CleanupRoot(root);

            var contentRoot = Path.Combine(root, "content");
            var stagingRoot = Path.Combine(root, "staging");

            Directory.CreateDirectory(contentRoot);
            Directory.CreateDirectory(stagingRoot);

            var v1Bytes = CreateBytes(8192, 11);
            var v2Bytes = CreateBytes(12288, 23);
            var v1Hash = ComputeSha256(v1Bytes);
            var v2Hash = ComputeSha256(v2Bytes);

            // v1 直接写入 content/v1/bundle_core（模拟安装包内置版本）
            var v1Dir = Path.Combine(contentRoot, "v1");
            Directory.CreateDirectory(v1Dir);
            File.WriteAllBytes(Path.Combine(v1Dir, "bundle_core"), v1Bytes);

            var v1Manifest = BuildSingleBundleManifest(
                "v1", "bundle_core", v1Bytes.Length, v1Hash);

            var activation = new PatchActivationService();
            var coldStartResult = await activation.ActivateAsync(
                v1Manifest,
                v1Dir,
                PlatformKey,
                0,
                true);
            LogActivation("A-ColdStart", coldStartResult);

            if (activation.ActiveContext == null)
            {
                Debug.LogError("[Lesson51] v1 activation failed. Aborting.");
                return;
            }

            var factory = new SimulatedBundlePayloadFactory(60);

            // 阶段 A：v1 上 UI 与 Battle 并发请求 ui/root
            await StageA_ConcurrentAcquire(activation.ActiveContext, factory);

            // 阶段 B：下载 v2 中断，然后模拟进程重启后续传
            var v2Cdn = new SimulatedCdnServer();
            v2Cdn.Put("bundle_core", v2Bytes);

            var v2Manifest = BuildSingleBundleManifest(
                "v2", "bundle_core", v2Bytes.Length, v2Hash);

            var staging1 = new PatchStagingStore(stagingRoot);
            var download1 = new PatchDownloadService(staging1, contentRoot);
            var transport1 = new SimulatedContentTransport
            {
                ServerSupportsRange = true,
                FailAfterBytesThisAttempt = (int)(v2Bytes.Length * 0.7)
            };

            var firstDownload = await download1.ApplyAsync(
                v2Manifest,
                v2Cdn,
                transport1,
                CancellationToken.None);

            LogApply("B-DownloadInterrupted", firstDownload);

            var partPath = staging1.GetPartPath("v2", "bundle_core");
            var partLengthAfterFailure = File.Exists(partPath)
                ? new FileInfo(partPath).Length
                : 0;

            Debug.Log(
                "[B-DownloadInterrupted] partExists=" +
                File.Exists(partPath) +
                " partBytes=" + partLengthAfterFailure +
                " activeStill=" +
                safeVersion(activation.ActiveContext));

            // 模拟进程被杀：丢弃 PatchDownloadService / PatchStagingStore / transport
            // 重新创建它们，但 staging 磁盘内容保留
            var staging2 = new PatchStagingStore(stagingRoot);
            var download2 = new PatchDownloadService(staging2, contentRoot);
            var transport2 = new SimulatedContentTransport
            {
                ServerSupportsRange = true,
                FailAfterBytesThisAttempt = -1
            };

            var resumeDownload = await download2.ApplyAsync(
                v2Manifest,
                v2Cdn,
                transport2,
                CancellationToken.None);

            LogApply("C-ResumeDownload", resumeDownload);

            // 阶段 C：v2 下载完成，但 active 仍为 v1
            Debug.Log(
                "[C-ResumeDownload] activeStill=" +
                safeVersion(activation.ActiveContext) +
                " committed=" + resumeDownload.BundlesCommitted +
                " skipped=" + resumeDownload.BundlesSkipped +
                " resumeBytes=" + resumeDownload.ResumeBytesTotal);

            // 阶段 D：用 runtime-1 持有 Handle，尝试激活 v2 -> RejectedActiveHandles
            var runtime1 = CreateRuntime(activation.ActiveContext, factory);
            var battleScope = runtime1.Cache.CreateScope("Battle.Scene");
            var battleHandle = await AcquireAsset(
                runtime1.Cache,
                new AssetKey("ui/root"),
                battleScope,
                "Battle");

            if (!battleHandle.IsSuccess)
            {
                Debug.LogError(
                    "[Lesson51] failed to load v1 ui/root: " +
                    battleHandle.Error);
                return;
            }

            Debug.Log(
                "[D-ActivateWithLiveHandle] liveHandles=" +
                runtime1.Cache.TotalLiveHandles);

            var activationWithHandle = await activation.ActivateAsync(
                v2Manifest,
                Path.Combine(contentRoot, "v2"),
                PlatformKey,
                runtime1.Cache.TotalLiveHandles,
                true);

            LogActivation("D-ActivateWithLiveHandle", activationWithHandle);

            // 释放 Handle，准备切换
            if (battleHandle.Handle != null)
            {
                battleHandle.Handle.Dispose();
            }

            battleScope.Dispose();

            // 回收 v1 Idle 资源
            runtime1.Evaluator.MaxUnloadsPerCall = 10;
            var v1Reclaim = runtime1.Evaluator.Evaluate();

            Debug.Log(
                "[D-ReclaimV1] " + v1Reclaim +
                " liveHandles=" + runtime1.Cache.TotalLiveHandles +
                " readyAssets=" + runtime1.Cache.ReadyAssetCount +
                " loadedBundles=" + runtime1.Bundles.LoadedPayloadCount);

            // 阶段 E：安全点外尝试激活 -> RejectedUnsafePoint
            var activationUnsafe = await activation.ActivateAsync(
                v2Manifest,
                Path.Combine(contentRoot, "v2"),
                PlatformKey,
                0,
                false);

            LogActivation("E-ActivateUnsafe", activationUnsafe);

            // 阶段 F：安全点内激活 -> Succeeded
            var activationSafe = await activation.ActivateAsync(
                v2Manifest,
                Path.Combine(contentRoot, "v2"),
                PlatformKey,
                0,
                true);

            LogActivation("F-ActivateSafe", activationSafe);

            if (activationSafe.Status != PatchActivationStatus.Succeeded)
            {
                Debug.LogError("[Lesson51] v2 activation did not succeed.");
                return;
            }

            // 阶段 G：必须重建运行时
            var runtime2 = CreateRuntime(activation.ActiveContext, factory);

            var shopScope = runtime2.Cache.CreateScope("UI.Shop");
            var shopHandle = await AcquireAsset(
                runtime2.Cache,
                new AssetKey("ui/root"),
                shopScope,
                "Shop");

            if (!shopHandle.IsSuccess)
            {
                Debug.LogError(
                    "[Lesson51] failed to load v2 ui/root: " +
                    shopHandle.Error);
                return;
            }

            Debug.Log(
                "[G-ActiveV2] runtimeSessionId=" +
                runtime2.SessionId +
                " contentVersion=" +
                activation.ActiveContext.ContentVersion +
                " payloadDebugName=" +
                shopHandle.Value.DebugName);

            // 阶段 H：释放 v2，回收
            if (shopHandle.Handle != null)
            {
                shopHandle.Handle.Dispose();
            }

            shopScope.Dispose();

            var v2Reclaim = runtime2.Evaluator.Evaluate();

            Debug.Log(
                "[H-ReclaimV2] " + v2Reclaim +
                " idleAssets=" + runtime2.Cache.IdleAssetCount +
                " idleBundles=" + runtime2.Bundles.IdleBundleCount +
                " loadedBundles=" + runtime2.Bundles.LoadedPayloadCount);

            Debug.Log(
                "[Lesson51] Finished. " +
                "Experiment 1: remove 'if (liveHandles > 0)' guard, " +
                "observe v2 activation while v1 handle is alive. " +
                "Experiment 2: keep 'safePoint' false and observe activation always rejected. " +
                "Experiment 3: skip 'File.Delete(targetPath)' on corrupted candidate; " +
                "next download will commit over broken file and fail activation.");
        }

        private static async Task StageA_ConcurrentAcquire(
            ActiveContentContext context,
            SimulatedBundlePayloadFactory factory)
        {
            var runtime = CreateRuntime(context, factory);

            var battleScope = runtime.Cache.CreateScope("Battle.Scene");
            var uiScope = runtime.Cache.CreateScope("UI.Shop");
            var key = new AssetKey("ui/root");

            var battleResult = await AcquireAsset(
                runtime.Cache,
                key,
                battleScope,
                "Battle");

            var uiResult = await AcquireAsset(
                runtime.Cache,
                key,
                uiScope,
                "UI");

            Debug.Log(
                "[A-ConcurrentAcquire] battle=" + battleResult.Status +
                " ui=" + uiResult.Status +
                " liveHandles=" + runtime.Cache.TotalLiveHandles +
                " readyAssets=" + runtime.Cache.ReadyAssetCount +
                " readyBundles=" + runtime.Bundles.ReadyBundleCount);

            uiScope.Dispose();

            Debug.Log(
                "[A-AfterUIClose] liveHandles=" +
                runtime.Cache.TotalLiveHandles +
                " battleStillHasHandle=" +
                (battleResult.Handle != null && battleResult.Handle.IsValid));

            if (battleResult.Handle != null)
            {
                battleResult.Handle.Dispose();
            }

            battleScope.Dispose();

            runtime.Evaluator.MaxUnloadsPerCall = 10;
            var reclaim = runtime.Evaluator.Evaluate();

            Debug.Log(
                "[A-RuntimeReleased] " + reclaim +
                " idleAssets=" + runtime.Cache.IdleAssetCount +
                " idleBundles=" + runtime.Bundles.IdleBundleCount +
                " loadedBundles=" + runtime.Bundles.LoadedPayloadCount);

            await Task.Yield();
        }

        private sealed class RuntimeSession
        {
            public int SessionId;
            public BundleRuntime Bundles;
            public AssetCache Cache;
            public AssetMemoryEvaluator Evaluator;
        }

        private static int _sessionSeq;

        private static RuntimeSession CreateRuntime(
            ActiveContentContext context,
            IBundlePayloadFactory factory)
        {
            var session = new RuntimeSession
            {
                SessionId = ++_sessionSeq
            };

            session.Bundles = new BundleRuntime(context, factory);
            session.Cache = new AssetCache(context, session.Bundles);
            session.Evaluator = new AssetMemoryEvaluator(
                session.Cache,
                session.Bundles);

            Debug.Log(
                "[Runtime] created session=" + session.SessionId +
                " for content=" + context.ContentVersion +
                " bundleRoot=" + context.BundleRoot);

            return session;
        }

        private static async Task<AssetLoadResult<SimulatedAsset>> AcquireAsset(
            AssetCache cache,
            AssetKey key,
            AssetScope scope,
            string owner)
        {
            return await cache.AcquireAsync<SimulatedAsset>(
                key,
                scope,
                owner);
        }

        private static PatchManifest BuildSingleBundleManifest(
            string contentVersion,
            string bundleFileName,
            long sizeBytes,
            string sha256)
        {
            return new PatchManifest
            {
                SchemaVersion = 1,
                ContentVersion = contentVersion,
                PlatformKey = PlatformKey,
                Bundles = new[]
                {
                    new PatchBundleDescriptor
                    {
                        BundleGroupName = bundleFileName,
                        FileName = bundleFileName,
                        SizeBytes = sizeBytes,
                        Sha256Hex = sha256
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

        private static void LogApply(string label, PatchApplyResult result)
        {
            Debug.Log(
                "[" + label + "] status=" + result.Status +
                " message=" + result.Message +
                " contentVersion=" + result.ContentVersion +
                " committed=" + result.BundlesCommitted +
                " skipped=" + result.BundlesSkipped +
                " resumeBytes=" + result.ResumeBytesTotal);
        }

        private static void LogActivation(
            string label,
            PatchActivationResult result)
        {
            Debug.Log(
                "[" + label + "] status=" + result.Status +
                " before=" + (result.ActiveVersionBefore ?? "<none>") +
                " after=" + (result.ActiveVersionAfter ?? "<none>") +
                " bundlesChecked=" + result.BundlesChecked +
                " message=" + result.Message);
        }

        private static string safeVersion(ActiveContentContext ctx)
        {
            return ctx == null ? "<none>" : ctx.ContentVersion;
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
                    "[Lesson51] cleanup failed: " + exception.Message);
            }
        }

        private static byte[] CreateBytes(int size, byte seed)
        {
            var bytes = new byte[size];

            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)((i * 31 + seed) & 0xFF);
            }

            return bytes;
        }

        private static string ComputeSha256(byte[] bytes)
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
    }
}
