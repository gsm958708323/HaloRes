using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Assets;
using Game.AssetSystem.Bundles;
using Game.AssetSystem.Content;
using Game.AssetSystem.Core;
using Game.AssetSystem.Diagnostics;
using Game.AssetSystem.Memory;
using Game.AssetSystem.Patch;
using Game.AssetSystem.Simulation;
using Game.AssetSystem.Storage;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// L5.1 — end-to-end integration scenario A..H.
    ///
    /// A: concurrent acquire on v1. B: interrupted v2 download. C: resume after
    /// a simulated restart. D: activation rejected while a handle is alive.
    /// E: activation rejected outside a safe point. F: activation succeeds.
    /// G: a new runtime reads v2. H: reclaim.
    /// </summary>
    public sealed class L51IntegrationScenarioDemo : LessonDemoBase
    {
        private const string PlatformKey = "StandaloneWindows64";

        private static int _sessionSeq;

        public override string Title
        {
            get { return "L5.1 集成场景：下载 / 续传 / 激活 / 重建运行时"; }
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            Debug.Log(
                "[Lesson51] start mainThreadId=" + MainThreadPump.MainThreadId +
                " isMainThread=" + MainThreadPump.IsMainThread);

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

            // v1 is written directly to content/v1, as if shipped with the build.
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
                true,
                cancellationToken);

            LogActivation("A-ColdStart", coldStartResult);

            if (activation.ActiveContext == null)
            {
                Debug.LogError("[Lesson51] v1 activation failed. Aborting.");
                return;
            }

            var factory = new LessonContentPayloadFactory(60);

            await StageA_ConcurrentAcquire(
                activation.ActiveContext,
                factory,
                cancellationToken);

            ThrowIfCanceled(cancellationToken);

            // B: download v2, interrupted at 70%.
            var v2Cdn = new SimulatedCdnServer();
            v2Cdn.AddFile("bundle_core", v2Bytes);

            var v2Manifest = BuildSingleBundleManifest(
                "v2", "bundle_core", v2Bytes.Length, v2Hash);

            // AssetFileStore appends /content itself.
            var fileStore = new AssetFileStore(root);
            fileStore.EnsureDirectories();

            var staging1 = new PatchStagingStore(stagingRoot);

            v2Cdn.SupportsRange = true;
            v2Cdn.FailAfterBytesThisAttempt = (int)(v2Bytes.Length * 0.7);

            // maxAttemptsPerBundle = 1 so the interruption leaves a .part on
            // disk instead of being retried away; activateAfterApply = false so
            // PatchActivationService stays the only activation decision.
            var firstDownload = await new PatchApplyController(
                    fileStore,
                    staging1,
                    new SimulatedPatchTransport(v2Cdn),
                    1,
                    false)
                .ApplyAsync(v2Manifest, null, cancellationToken);

            LogApply("B-DownloadInterrupted", firstDownload);

            var partPath = staging1.GetPartPath("v2", "bundle_core");
            var partLengthAfterFailure = File.Exists(partPath)
                ? new FileInfo(partPath).Length
                : 0;

            Debug.Log(
                "[B-DownloadInterrupted] partExists=" + File.Exists(partPath) +
                " partBytes=" + partLengthAfterFailure +
                " activeStill=" + SafeVersion(activation.ActiveContext));

            // Simulated process kill: drop the service / staging / transport but
            // keep the staging bytes on disk, then resume.
            var staging2 = new PatchStagingStore(stagingRoot);

            v2Cdn.SupportsRange = true;
            v2Cdn.FailAfterBytesThisAttempt = -1;

            var resumeDownload = await new PatchApplyController(
                    new AssetFileStore(root),
                    staging2,
                    new SimulatedPatchTransport(v2Cdn),
                    3,
                    false)
                .ApplyAsync(v2Manifest, null, cancellationToken);

            LogApply("C-ResumeDownload", resumeDownload);

            Debug.Log(
                "[C-ResumeDownload] activeStill=" + SafeVersion(activation.ActiveContext) +
                " committed=" + resumeDownload.BundlesCommitted +
                " skipped=" + resumeDownload.BundlesSkipped +
                " resumeBytes=" + resumeDownload.ResumeBytesTotal);

            ThrowIfCanceled(cancellationToken);

            // D: hold a handle on v1 and try to activate v2.
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
                    "[Lesson51] failed to load v1 ui/root: " + battleHandle.Error);
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
                true,
                cancellationToken);

            LogActivation("D-ActivateWithLiveHandle", activationWithHandle);

            if (battleHandle.Handle != null)
            {
                battleHandle.Handle.Dispose();
            }

            battleScope.Dispose();

            runtime1.Evaluator.MaxUnloadsPerCall = 10;
            var v1Reclaim = runtime1.Evaluator.Evaluate();

            Debug.Log(
                "[D-ReclaimV1] " + v1Reclaim +
                " liveHandles=" + runtime1.Cache.TotalLiveHandles +
                " readyAssets=" + runtime1.Cache.ReadyAssetCount +
                " loadedBundles=" + runtime1.Bundles.LoadedPayloadCount);

            ThrowIfCanceled(cancellationToken);

            // E: activation outside a safe point.
            var activationUnsafe = await activation.ActivateAsync(
                v2Manifest,
                Path.Combine(contentRoot, "v2"),
                PlatformKey,
                0,
                false,
                cancellationToken);

            LogActivation("E-ActivateUnsafe", activationUnsafe);

            // F: activation inside a safe point.
            var activationSafe = await activation.ActivateAsync(
                v2Manifest,
                Path.Combine(contentRoot, "v2"),
                PlatformKey,
                0,
                true,
                cancellationToken);

            LogActivation("F-ActivateSafe", activationSafe);

            if (activationSafe.Status != PatchActivationStatus.Succeeded)
            {
                Debug.LogError("[Lesson51] v2 activation did not succeed.");
                return;
            }

            // G: the runtime must be rebuilt for the new content version.
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
                    "[Lesson51] failed to load v2 ui/root: " + shopHandle.Error);
                return;
            }

            Debug.Log(
                "[G-ActiveV2] runtimeSessionId=" + runtime2.SessionId +
                " contentVersion=" + activation.ActiveContext.ContentVersion +
                " payloadDebugName=" + shopHandle.Value.DebugName);

            // H: release and reclaim.
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
                "Experiment 1: remove the 'if (liveHandles > 0)' guard and observe " +
                "v2 activation while a v1 handle is alive. " +
                "Experiment 2: keep 'safePoint' false and observe activation always rejected. " +
                "Experiment 3: skip the delete of a corrupted candidate; the next download " +
                "commits over a broken file and activation fails.");
        }

        private static async Task StageA_ConcurrentAcquire(
            ActiveContentContext context,
            LessonContentPayloadFactory factory,
            CancellationToken cancellationToken)
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
                "[A-AfterUIClose] liveHandles=" + runtime.Cache.TotalLiveHandles +
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
            ThrowIfCanceled(cancellationToken);
        }

        private sealed class RuntimeSession
        {
            public int SessionId;
            public BundleRuntime Bundles;
            public AssetCache Cache;
            public AssetMemoryEvaluator Evaluator;
        }

        private static RuntimeSession CreateRuntime(
            ActiveContentContext context,
            IBundleLoader factory)
        {
            var session = new RuntimeSession
            {
                SessionId = ++_sessionSeq
            };

            var policy = new AssetMemoryPolicy();
            var diagnostics = new AssetRuntimeDiagnostics();

            session.Bundles = new BundleRuntime(
                context.CreateBundleCatalog(),
                factory,
                policy,
                diagnostics);

            session.Cache = new AssetCache(
                context.CreateAssetCatalog(),
                session.Bundles,
                policy,
                diagnostics);

            session.Evaluator = new AssetMemoryEvaluator(
                session.Cache,
                session.Bundles);

            Debug.Log(
                "[Runtime] created session=" + session.SessionId +
                " for content=" + context.ContentVersion +
                " bundleRoot=" + context.BundleRoot);

            return session;
        }

        private static async Task<AssetLoadResult<LessonAssetPayload>> AcquireAsset(
            AssetCache cache,
            AssetKey key,
            AssetScope scope,
            string owner)
        {
            return await cache.AcquireAsync<LessonAssetPayload>(
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
                        Url = bundleFileName,
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

        private static string SafeVersion(ActiveContentContext context)
        {
            return context == null ? "<none>" : context.ContentVersion;
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
