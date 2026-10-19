using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Patch;
using Game.AssetSystem.Storage;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// L3.5 — patch download with resume.
    ///
    /// Scenario 1 interrupts the v2 fx bundle at 70% and resumes it with Range.
    /// Scenario 2 does the same for v3 but the server stops supporting Range, so
    /// the controller must fall back to a full re-download instead of failing.
    /// </summary>
    public sealed class L35PatchDownloadDemo : LessonDemoBase
    {
        public override string Title
        {
            get { return "L3.5 补丁下载：断点续传与 Range 回退"; }
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            var root = Path.Combine(
                Application.persistentDataPath,
                "PatchDownloadLesson");

            CleanupRoot(root);

            var fileStore = new AssetFileStore(root);
            fileStore.EnsureDirectories();

            var staging = new PatchStagingStore(root);

            Debug.Log("[Lesson5] root=" + root);

            CreateActiveV1(fileStore);
            LogActive("AfterCreateV1", fileStore);

            var server = new LessonPatchServer();

            var v2Core = CreateBytes(8192, 31);
            var v2Fx = CreateBytes(16384, 47);
            var v2CoreHash = BundleHashVerifier.ComputeSha256Hex(v2Core);
            var v2FxHash = BundleHashVerifier.ComputeSha256Hex(v2Fx);

            server.AddFile("sim://v2/bundle_core.bundle", v2Core);
            server.AddFile("sim://v2/bundle_fx.bundle", v2Fx);

            var v2Manifest = new PatchManifest
            {
                schemaVersion = 1,
                contentVersion = "v2",
                bundles = new[]
                {
                    new PatchBundleDescriptor
                    {
                        bundleFileName = "bundle_core.bundle",
                        url = "sim://v2/bundle_core.bundle",
                        sizeBytes = v2Core.Length,
                        sha256Hex = v2CoreHash
                    },
                    new PatchBundleDescriptor
                    {
                        bundleFileName = "bundle_fx.bundle",
                        url = "sim://v2/bundle_fx.bundle",
                        sizeBytes = v2Fx.Length,
                        sha256Hex = v2FxHash
                    }
                }
            };

            server.SupportsRange = true;
            server.FailAfterBytesThisAttempt = (int)(v2Fx.Length * 0.7);

            var firstV2 = await new PatchApplyController(
                    fileStore,
                    staging,
                    new LessonPatchTransport(server),
                    1)
                .ApplyAsync(
                    v2Manifest,
                    CreateProgress("V2-First"),
                    cancellationToken);

            LogResult("V2-First", firstV2);
            LogBundleReports("V2-First", firstV2);
            LogActive("AfterV2First", fileStore);

            var fxPartPath = staging.GetPartPath("v2", "bundle_fx.bundle");
            var fxPartLength = staging.GetPartLength("v2", "bundle_fx.bundle");

            Debug.Log(
                "[V2-First] fxPartExists=" + File.Exists(fxPartPath) +
                " fxPartBytes=" + fxPartLength +
                " expected=" + v2Fx.Length);

            server.FailAfterBytesThisAttempt = -1;
            server.SupportsRange = true;

            var secondV2 = await new PatchApplyController(
                    fileStore,
                    staging,
                    new LessonPatchTransport(server),
                    3)
                .ApplyAsync(
                    v2Manifest,
                    CreateProgress("V2-Second"),
                    cancellationToken);

            LogResult("V2-Second", secondV2);
            LogBundleReports("V2-Second", secondV2);
            LogActive("AfterV2Second", fileStore);

            var v3Audio = CreateBytes(12000, 61);
            var v3Hash = BundleHashVerifier.ComputeSha256Hex(v3Audio);

            server.AddFile("sim://v3/bundle_audio.bundle", v3Audio);

            var v3Manifest = new PatchManifest
            {
                schemaVersion = 1,
                contentVersion = "v3",
                bundles = new[]
                {
                    new PatchBundleDescriptor
                    {
                        bundleFileName = "bundle_audio.bundle",
                        url = "sim://v3/bundle_audio.bundle",
                        sizeBytes = v3Audio.Length,
                        sha256Hex = v3Hash
                    }
                }
            };

            server.SupportsRange = true;
            server.FailAfterBytesThisAttempt = (int)(v3Audio.Length * 0.5);

            var firstV3 = await new PatchApplyController(
                    fileStore,
                    staging,
                    new LessonPatchTransport(server),
                    1)
                .ApplyAsync(
                    v3Manifest,
                    CreateProgress("V3-First"),
                    cancellationToken);

            LogResult("V3-First", firstV3);
            LogActive("AfterV3First", fileStore);

            var audioPartPath = staging.GetPartPath(
                "v3",
                "bundle_audio.bundle");

            Debug.Log(
                "[V3-First] audioPartExists=" + File.Exists(audioPartPath) +
                " audioPartBytes=" +
                staging.GetPartLength("v3", "bundle_audio.bundle") +
                " expected=" + v3Audio.Length);

            server.FailAfterBytesThisAttempt = -1;
            server.SupportsRange = false;

            var secondV3 = await new PatchApplyController(
                    fileStore,
                    staging,
                    new LessonPatchTransport(server),
                    3)
                .ApplyAsync(
                    v3Manifest,
                    CreateProgress("V3-Second-NoRange"),
                    cancellationToken);

            LogResult("V3-Second-NoRange", secondV3);
            LogBundleReports("V3-Second-NoRange", secondV3);
            LogActive("AfterV3Second", fileStore);

            Debug.Log(
                "[Lesson5] Finished. " +
                "Experiment 1: force offset=0 in PreparePartForResume; " +
                "resume bytes disappears. " +
                "Experiment 2: remove the RangeNotSupported fallback; " +
                "the V3 second attempt fails.");
        }

        private static void CreateActiveV1(AssetFileStore fileStore)
        {
            var core = CreateBytes(4096, 11);
            var fx = CreateBytes(8192, 23);

            var coreHash = CommitSeedBundle(
                fileStore,
                "v1",
                "bundle_core.bundle",
                core);

            var fxHash = CommitSeedBundle(
                fileStore,
                "v1",
                "bundle_fx.bundle",
                fx);

            var activation = fileStore.ActivateVersion(
                "v1",
                new[]
                {
                    new BundleHashExpectation("bundle_core.bundle", coreHash),
                    new BundleHashExpectation("bundle_fx.bundle", fxHash)
                });

            if (!activation.Success)
            {
                throw new InvalidOperationException(
                    "Failed to activate v1: " + activation.Error);
            }
        }

        private static string CommitSeedBundle(
            AssetFileStore fileStore,
            string contentVersion,
            string bundleFileName,
            byte[] payload)
        {
            var tempPath = Path.Combine(
                Application.temporaryCachePath,
                bundleFileName + "." +
                Guid.NewGuid().ToString("N") + ".tmp");

            File.WriteAllBytes(tempPath, payload);

            var hash = BundleHashVerifier.ComputeSha256Hex(payload);

            var commit = fileStore.CommitBundleFromTemp(
                tempPath,
                contentVersion,
                bundleFileName,
                hash);

            if (!commit.Success)
            {
                throw new InvalidOperationException(
                    "Seed commit failed: " + commit.Error);
            }

            return hash;
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

        private static IProgress<PatchProgress> CreateProgress(string label)
        {
            return new Progress<PatchProgress>(progress =>
            {
                Debug.Log(
                    "[Progress][" + label + "] " +
                    progress.Phase +
                    " bundle=" +
                    (string.IsNullOrEmpty(progress.BundleFileName)
                        ? "-"
                        : progress.BundleFileName) +
                    " bytes=" + progress.DownloadedBytes +
                    "/" + progress.TotalBytes +
                    " " + progress.Message);
            });
        }

        private static void LogResult(
            string label,
            PatchApplyResult result)
        {
            Debug.Log(
                "[" + label + "] status=" + result.Status +
                " code=" + result.ErrorCode +
                " before=" + result.ActiveVersionBefore +
                " after=" + result.ActiveVersionAfter +
                " skipped=" + result.BundlesSkipped +
                " downloaded=" + result.BundlesDownloaded +
                " resumeTotal=" + result.ResumeOffsetTotal +
                " message=" + result.Message);
        }

        private static void LogBundleReports(
            string label,
            PatchApplyResult result)
        {
            if (result.Bundles == null)
            {
                return;
            }

            for (var i = 0; i < result.Bundles.Count; i++)
            {
                var report = result.Bundles[i];

                Debug.Log(
                    "[" + label + "] bundle=" + report.BundleFileName +
                    " resumeOffset=" + report.ResumeOffset +
                    " bytesThisRun=" + report.BytesTransferredThisRun +
                    " rangeFallback=" + report.RangeFallback +
                    " skipped=" + report.Skipped +
                    " committed=" + report.Committed +
                    " error=" + report.Error);
            }
        }

        private static void LogActive(
            string label,
            AssetFileStore fileStore)
        {
            if (!fileStore.TryLoadActiveState(out var state, out var error))
            {
                Debug.LogWarning(
                    "[" + label + "] active unreadable: " + error);
                return;
            }

            Debug.Log(
                "[" + label + "] activeVersion=" + state.contentVersion +
                " bundleCount=" +
                (state.bundles == null ? 0 : state.bundles.Length));
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
                    "[Lesson5] Failed to cleanup previous root: " +
                    exception.Message);
            }
        }
    }
}
