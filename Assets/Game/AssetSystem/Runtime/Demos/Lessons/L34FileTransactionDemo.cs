using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.AssetSystem.Storage;
using UnityEngine;

namespace Game.AssetSystem.Demos.Lessons
{
    /// <summary>
    /// L3.4 — bundle file transaction.
    ///
    /// Covers: commit with hash verification, activation with a missing bundle,
    /// crash recovery (orphan temp files + truncated active state), verifying the
    /// active bundle, and atomic v1 -> v2 -> rollback.
    /// </summary>
    public sealed class L34FileTransactionDemo : LessonDemoBase
    {
        private const string LessonRootName = "FileTransactionLesson";

        public override string Title
        {
            get { return "L3.4 文件事务：提交 / 激活 / 回滚 / 崩溃恢复"; }
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            var root = Path.Combine(
                Application.persistentDataPath,
                LessonRootName);

            CleanupRoot(root);

            var store = new AssetFileStore(root);
            store.EnsureDirectories();

            Debug.Log("[Lesson4] root=" + root);
            await DelayAsync(20, cancellationToken);

            var initialRecovery = Recover(store);
            LogRecovery("InitialRecovery", initialRecovery);

            var v1Expectations =
                await RunValidCommitAndActivateV1(store, cancellationToken);

            ThrowIfCanceled(cancellationToken);
            await RunHashMismatchCommit(store, cancellationToken);

            ThrowIfCanceled(cancellationToken);
            var v2CoreHash =
                await RunActivationFailsWhenBundleMissing(store, cancellationToken);

            ThrowIfCanceled(cancellationToken);
            var v2FxHash =
                await RunStagedButNotActivatedAndRecovery(store, cancellationToken);

            ThrowIfCanceled(cancellationToken);
            await RunVerifyActiveBundle(store, cancellationToken);

            ThrowIfCanceled(cancellationToken);
            await RunAtomicActivationAndRollback(
                store,
                v1Expectations,
                v2CoreHash,
                v2FxHash,
                cancellationToken);

            Debug.Log(
                "[Lesson4] Finished. Experiment: remove hash verification before commit " +
                "or activation, then corrupt a bundle and observe the missing guard.");
        }

        private static async Task<List<BundleHashExpectation>>
            RunValidCommitAndActivateV1(
                AssetFileStore store,
                CancellationToken cancellationToken)
        {
            var temp = WriteTempPayload(
                store,
                "bundle_core.bundle",
                "bundle_core_v1_payload");

            var hash = ComputeHashOrThrow(temp);

            var commit = store.CommitBundleFromTemp(
                temp,
                "v1",
                "bundle_core.bundle",
                hash);

            Debug.Log(
                "[Scenario1] commit v1 core success=" + commit.Success +
                " alreadyPresent=" + commit.AlreadyPresent +
                " target=" + commit.TargetPath +
                " error=" + commit.Error);

            var expectations = new List<BundleHashExpectation>
            {
                new BundleHashExpectation("bundle_core.bundle", hash)
            };

            var activation = store.ActivateVersion("v1", expectations);

            Debug.Log(
                "[Scenario1] activate v1 success=" + activation.Success +
                " activePath=" + activation.ActiveStatePath +
                " error=" + activation.Error);

            LogActiveState("Scenario1", store);

            await DelayAsync(20, cancellationToken);
            return expectations;
        }

        private static async Task RunHashMismatchCommit(
            AssetFileStore store,
            CancellationToken cancellationToken)
        {
            var temp = WriteTempPayload(
                store,
                "bundle_fx.bundle",
                "bundle_fx_v2_payload");

            var wrongHash = new string('0', 64);

            var commit = store.CommitBundleFromTemp(
                temp,
                "v2",
                "bundle_fx.bundle",
                wrongHash);

            Debug.Log(
                "[Scenario2] commit wrong hash success=" + commit.Success +
                " errorCode=" + commit.Error.Code +
                " message=" + commit.Error.Message +
                " tempExistsAfter=" + File.Exists(temp));

            LogActiveState("Scenario2", store);

            await DelayAsync(20, cancellationToken);
        }

        private static async Task<string> RunActivationFailsWhenBundleMissing(
            AssetFileStore store,
            CancellationToken cancellationToken)
        {
            var temp = WriteTempPayload(
                store,
                "bundle_core.bundle",
                "bundle_core_v2_payload");

            var hash = ComputeHashOrThrow(temp);

            var commit = store.CommitBundleFromTemp(
                temp,
                "v2",
                "bundle_core.bundle",
                hash);

            Debug.Log(
                "[Scenario3] commit v2 core success=" + commit.Success +
                " alreadyPresent=" + commit.AlreadyPresent +
                " error=" + commit.Error);

            var incompleteExpectations = new List<BundleHashExpectation>
            {
                new BundleHashExpectation("bundle_core.bundle", hash),
                new BundleHashExpectation(
                    "bundle_missing.bundle",
                    new string('1', 64))
            };

            var activation = store.ActivateVersion(
                "v2",
                incompleteExpectations);

            Debug.Log(
                "[Scenario3] activate v2 incomplete success=" + activation.Success +
                " errorCode=" + activation.Error.Code +
                " message=" + activation.Error.Message);

            LogActiveState("Scenario3", store);

            await DelayAsync(20, cancellationToken);
            return hash;
        }

        private static async Task<string>
            RunStagedButNotActivatedAndRecovery(
                AssetFileStore store,
                CancellationToken cancellationToken)
        {
            var temp = WriteTempPayload(
                store,
                "bundle_fx.bundle",
                "bundle_fx_v2_payload");

            var hash = ComputeHashOrThrow(temp);

            var commit = store.CommitBundleFromTemp(
                temp,
                "v2",
                "bundle_fx.bundle",
                hash);

            Debug.Log(
                "[Scenario4] commit staged v2 fx success=" + commit.Success +
                " alreadyPresent=" + commit.AlreadyPresent +
                " error=" + commit.Error);

            var orphanTemp = Path.Combine(
                store.TempDirectory,
                "orphan_from_crash.part");

            File.WriteAllText(orphanTemp, "partial download", Encoding.UTF8);
            File.WriteAllText(
                store.ActiveStateTempPath,
                "{ partial active state",
                Encoding.UTF8);

            Debug.Log(
                "[Scenario4] before recovery orphanTempExists=" +
                File.Exists(orphanTemp) +
                " activeTempExists=" +
                File.Exists(store.ActiveStateTempPath));

            var recovery = Recover(store);
            LogRecovery("Scenario4", recovery);

            Debug.Log(
                "[Scenario4] after recovery orphanTempExists=" +
                File.Exists(orphanTemp) +
                " activeTempExists=" +
                File.Exists(store.ActiveStateTempPath));

            LogActiveState("Scenario4", store);

            await DelayAsync(20, cancellationToken);
            return hash;
        }

        private static async Task RunVerifyActiveBundle(
            AssetFileStore store,
            CancellationToken cancellationToken)
        {
            if (!store.TryLoadActiveState(out var state, out var error))
            {
                Debug.LogWarning(
                    "[Scenario5] active state unavailable: " + error);

                await DelayAsync(20, cancellationToken);
                return;
            }

            var bundle = state.bundles[0];
            var path = store.GetBundlePath(
                state.contentVersion,
                bundle.bundleFileName);

            var verified = BundleHashVerifier.VerifySha256Hex(
                path,
                bundle.sha256Hex,
                out var actualHash,
                out var verifyError);

            Debug.Log(
                "[Scenario5] verify active bundle version=" +
                state.contentVersion +
                " file=" + bundle.bundleFileName +
                " verified=" + verified +
                " actual=" + actualHash +
                " expected=" + bundle.sha256Hex +
                " error=" + verifyError);

            await DelayAsync(20, cancellationToken);
        }

        private static async Task RunAtomicActivationAndRollback(
            AssetFileStore store,
            IReadOnlyList<BundleHashExpectation> v1Expectations,
            string v2CoreHash,
            string v2FxHash,
            CancellationToken cancellationToken)
        {
            var v2Expectations = new List<BundleHashExpectation>
            {
                new BundleHashExpectation("bundle_core.bundle", v2CoreHash),
                new BundleHashExpectation("bundle_fx.bundle", v2FxHash)
            };

            var activateV2 = store.ActivateVersion("v2", v2Expectations);

            Debug.Log(
                "[Scenario6] activate v2 success=" + activateV2.Success +
                " error=" + activateV2.Error);

            LogActiveState("Scenario6-AfterV2", store);

            var v1CorePath = store.GetBundlePath(
                "v1",
                "bundle_core.bundle");

            Debug.Log(
                "[Scenario6] old v1 directory still exists=" +
                File.Exists(v1CorePath));

            var rollbackV1 = store.ActivateVersion("v1", v1Expectations);

            Debug.Log(
                "[Scenario6] rollback v1 success=" + rollbackV1.Success +
                " error=" + rollbackV1.Error);

            LogActiveState("Scenario6-AfterRollback", store);

            await DelayAsync(20, cancellationToken);
        }

        /// <summary>
        /// Cold-start repair, now provided by the framework itself
        /// (<c>AssetFileStore.RecoverOnStartup</c>): drop orphan temp files,
        /// drop a truncated active-state temp file, then read the active state
        /// and count missing bundles.
        /// </summary>
        private static RecoveryReport Recover(AssetFileStore store)
        {
            return store.RecoverOnStartup();
        }

        private static string WriteTempPayload(
            AssetFileStore store,
            string bundleFileName,
            string payload)
        {
            var tempPath = store.CreateTempFilePath(bundleFileName);
            File.WriteAllText(tempPath, payload, Encoding.UTF8);
            return tempPath;
        }

        private static string ComputeHashOrThrow(string path)
        {
            if (!BundleHashVerifier.TryComputeSha256Hex(
                path,
                out var hash,
                out var error))
            {
                throw new InvalidOperationException(
                    "Failed to compute hash: " + error);
            }

            return hash;
        }

        private static void LogRecovery(
            string label,
            RecoveryReport report)
        {
            Debug.Log(
                "[" + label + "] recovery" +
                " deletedTemp=" + report.DeletedTempFileCount +
                " deletedStateTemp=" + report.DeletedStateTempFileCount +
                " activeReadable=" + report.ActiveStateReadable +
                " activeVersion=" + report.ActiveVersion +
                " missingBundles=" + report.MissingBundleCount +
                " error=" + report.Error);
        }

        private static void LogActiveState(
            string label,
            AssetFileStore store)
        {
            if (!store.TryLoadActiveState(out var state, out var error))
            {
                Debug.LogWarning(
                    "[" + label + "] active state unreadable: " + error);
                return;
            }

            Debug.Log(
                "[" + label + "] activeVersion=" + state.contentVersion +
                " bundleCount=" +
                (state.bundles == null ? 0 : state.bundles.Length));

            if (state.bundles == null)
            {
                return;
            }

            for (var i = 0; i < state.bundles.Length; i++)
            {
                var bundle = state.bundles[i];
                var path = store.GetBundlePath(
                    state.contentVersion,
                    bundle.bundleFileName);

                Debug.Log(
                    "[" + label + "] bundle=" + bundle.bundleFileName +
                    " exists=" + File.Exists(path) +
                    " hash=" + bundle.sha256Hex);
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
                    "[Lesson4] Failed to clean previous lesson root: " +
                    exception.Message);
            }
        }

    }
}
