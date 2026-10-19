using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Patch = Game.AssetSystem.Patch;
using Storage = Game.AssetSystem.Storage;
using Support = Game.AssetSystem.Tests.Support;

namespace Game.AssetSystem.Tests
{
    /// <summary>
    /// PlayMode tests for the persistence layer: file transaction + hash
    /// verification, patch staging / resume / hash failure.
    /// </summary>
    public sealed class PersistenceScenarioTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {

            _root = Path.Combine(
                Application.persistentDataPath,
                "LessonTests",
                TestContext.CurrentContext.Test.Name);

            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }

            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch (Exception)
            {
                // best effort cleanup
            }
        }

        // ------------------------------------------------------------------
        // 3.4：Hash 不匹配不得提交；缺依赖不得激活；active 指针不能被破坏
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L34_HashGuardBlocksBadCommitAndBadActivation()
        {
            var store = new Storage.AssetFileStore(_root);
            store.EnsureDirectories();

            var goodTemp = store.CreateTempFilePath("bundle_core.bundle");
            File.WriteAllText(goodTemp, "bundle_core_v1_payload");
            var goodHash = Sha256File(goodTemp);

            var badTemp = store.CreateTempFilePath("bundle_core.bundle");
            File.WriteAllText(badTemp, "corrupted_payload");

            var badCommit = store.CommitBundleFromTemp(
                badTemp,
                "v1",
                "bundle_core.bundle",
                new string('0', 64));

            Assert.IsFalse(badCommit.Success, "wrong hash must not commit");
            Assert.IsFalse(
                File.Exists(store.GetBundlePath("v1", "bundle_core.bundle")),
                "corrupted payload must not land in content");

            var commit = store.CommitBundleFromTemp(
                goodTemp,
                "v1",
                "bundle_core.bundle",
                goodHash);

            Assert.IsTrue(commit.Success, "correct hash must commit: " + commit.Error);

            var activation = store.ActivateVersion(
                "v1",
                new List<Storage.BundleHashExpectation>
                {
                    new Storage.BundleHashExpectation("bundle_core.bundle", goodHash)
                });

            Assert.IsTrue(activation.Success, "activation should succeed: " + activation.Error);

            Assert.IsTrue(
                store.TryLoadActiveState(out var state, out var error),
                "active state should be readable: " + error.Message);

            Assert.AreEqual("v1", state.contentVersion);

            var missingActivation = store.ActivateVersion(
                "v2",
                new List<Storage.BundleHashExpectation>
                {
                    new Storage.BundleHashExpectation("bundle_missing.bundle", goodHash)
                });

            Assert.IsFalse(
                missingActivation.Success,
                "activation with a missing bundle must fail");

            Assert.IsTrue(store.TryLoadActiveState(out var stateAfter, out _));
            Assert.AreEqual(
                "v1",
                stateAfter.contentVersion,
                "failed activation must not move the active pointer");

            yield return null;
        }

        // ------------------------------------------------------------------
        // 3.5：下载中断保留 .part，重建控制器后续传，最终激活 v2
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L35_DownloadInterruptedThenResumes()
        {
            var store = new Storage.AssetFileStore(_root);
            store.EnsureDirectories();

            var staging = new Patch.PatchStagingStore(
                Path.Combine(_root, "staging"));

            var payload = Encoding.UTF8.GetBytes(new string('b', 8192));
            var hash = Sha256Bytes(payload);
            const string url = "cdn://bundle_fx";
            const string version = "v2";

            var manifest = new Patch.PatchManifest
            {
                schemaVersion = 1,
                contentVersion = version,
                bundles = new[]
                {
                    new Patch.PatchBundleDescriptor
                    {
                        bundleFileName = "bundle_fx",
                        url = url,
                        sizeBytes = payload.Length,
                        sha256Hex = hash
                    }
                }
            };

            var server = new Support.SimulatedPatchServer();
            server.AddFile(url, payload);
            server.SupportsRange = true;
            server.FailAfterBytesThisAttempt = 4096;

            var firstController = new Patch.PatchApplyController(
                store,
                staging,
                new Support.SimulatedPatchTransport(server),
                1);

            var first = firstController.ApplyAsync(
                manifest,
                new NoopProgress(),
                System.Threading.CancellationToken.None);

            yield return WaitTask(first);

            Assert.AreEqual(
                Patch.PatchApplyStatus.Failed,
                first.Result.Status,
                "interrupted download must fail");

            var partPath = staging.GetPartPath(version, "bundle_fx");

            Assert.IsTrue(File.Exists(partPath), ".part must be kept for resume");
            Assert.Greater(new FileInfo(partPath).Length, 0, ".part must contain bytes");

            var partBytes = new FileInfo(partPath).Length;

            // 模拟进程重启：丢弃控制器与 staging 对象，只保留磁盘内容
            var restartedStaging = new Patch.PatchStagingStore(
                Path.Combine(_root, "staging"));

            server.FailAfterBytesThisAttempt = -1;

            var secondController = new Patch.PatchApplyController(
                store,
                restartedStaging,
                new Support.SimulatedPatchTransport(server),
                3);

            var second = secondController.ApplyAsync(
                manifest,
                new NoopProgress(),
                System.Threading.CancellationToken.None);

            yield return WaitTask(second);

            Assert.AreEqual(
                Patch.PatchApplyStatus.Succeeded,
                second.Result.Status,
                "resumed download should succeed: " + second.Result.Message);

            Assert.AreEqual(
                partBytes,
                second.Result.ResumeOffsetTotal,
                "resume offset must equal the retained .part length");

            Assert.IsTrue(
                File.Exists(store.GetBundlePath(version, "bundle_fx")),
                "committed bundle must exist in content");

            Assert.IsTrue(store.TryLoadActiveState(out var state, out _));
            Assert.AreEqual(version, state.contentVersion, "v2 must be active after apply");

            yield return null;
        }

        private static IEnumerator WaitTask<T>(System.Threading.Tasks.Task<T> task, float timeoutSeconds = 20f)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;

            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("timed out waiting for patch apply");
                }

                yield return null;
            }

            if (task.IsFaulted && task.Exception != null)
            {
                throw task.Exception;
            }
        }

        private static string Sha256File(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        private static string Sha256Bytes(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);

            for (var i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }

            return builder.ToString();
        }

        private sealed class NoopProgress : IProgress<Patch.PatchProgress>
        {
            public void Report(Patch.PatchProgress value)
            {
            }
        }
    }
}
