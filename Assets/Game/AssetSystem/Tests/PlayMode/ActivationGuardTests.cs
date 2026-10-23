using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Game.AssetSystem.Patch;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.AssetSystem.Tests
{
    /// <summary>
    /// 4.7 activation guards: manifest data is untrusted, and the handle /
    /// safe-point checks must be authoritative at swap time, not only before
    /// the main-thread hop.
    /// </summary>
    public sealed class ActivationGuardTests
    {
        private const string Platform = "TestPlatform";

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

        [UnityTest]
        public IEnumerator L47_ActivationRejectsBundleNameEscapingRoot()
        {
            var service = new PatchActivationService();
            var task = service.ActivateAsync(
                Manifest("../escape.bundle"),
                _root,
                Platform,
                0,
                true);

            yield return WaitFor(task, "traversal activation");

            Assert.AreEqual(
                PatchActivationStatus.InvalidManifest,
                task.Result.Status,
                "a file name with a directory part must be rejected");

            Assert.IsNull(service.ActiveContext, "nothing may be activated");
        }

        [UnityTest]
        public IEnumerator L47_ActivationRejectsPlatformMismatch()
        {
            var service = new PatchActivationService();
            var task = service.ActivateAsync(
                Manifest("a.bundle"),
                _root,
                "SomeOtherPlatform",
                0,
                true);

            yield return WaitFor(task, "platform mismatch activation");

            Assert.AreEqual(
                PatchActivationStatus.PlatformMismatch,
                task.Result.Status);
            Assert.IsNull(service.ActiveContext);
        }

        [UnityTest]
        public IEnumerator L47_ActivationReportsMissingBundle()
        {
            var service = new PatchActivationService();
            var task = service.ActivateAsync(
                Manifest("missing.bundle"),
                _root,
                Platform,
                0,
                true);

            yield return WaitFor(task, "missing bundle activation");

            Assert.AreEqual(
                PatchActivationStatus.BundleMissing,
                task.Result.Status);
            Assert.IsNull(service.ActiveContext);
        }

        [UnityTest]
        public IEnumerator L47_ActivationReportsHashMismatch()
        {
            var path = Path.Combine(_root, "corrupt.bundle");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });

            var manifest = Manifest("corrupt.bundle");
            manifest.Bundles[0].SizeBytes = new FileInfo(path).Length;
            manifest.Bundles[0].Sha256Hex = new string('a', 64);

            var service = new PatchActivationService();
            var task = service.ActivateAsync(
                manifest,
                _root,
                Platform,
                0,
                true);

            yield return WaitFor(task, "hash mismatch activation");

            Assert.AreEqual(
                PatchActivationStatus.HashMismatch,
                task.Result.Status);
            Assert.IsNull(service.ActiveContext);
        }

        [UnityTest]
        public IEnumerator L47_ActivationRechecksHandlesInsideSwap()
        {
            var path = Path.Combine(_root, "good.bundle");
            var bytes = new byte[] { 7, 7, 7, 7, 7 };
            File.WriteAllBytes(path, bytes);

            var manifest = Manifest("good.bundle");
            manifest.Bundles[0].SizeBytes = bytes.Length;
            manifest.Bundles[0].Sha256Hex = Sha256Hex(bytes);

            var service = new PatchActivationService();

            // The pre-check sees 0 handles; by the time the swap runs, a
            // handle exists. That is exactly the window between the check and
            // the swap, so the swap itself must refuse.
            var probes = 0;
            Func<int> provider = () => ++probes >= 2 ? 1 : 0;

            var task = service.ActivateAsync(
                manifest,
                _root,
                Platform,
                0,
                true,
                default,
                provider);

            yield return WaitFor(task, "handle re-check activation");

            Assert.AreEqual(
                PatchActivationStatus.RejectedActiveHandles,
                task.Result.Status,
                "the swap must re-check the live handle count");

            Assert.GreaterOrEqual(probes, 2, "the handle count must be read twice");
            Assert.IsNull(service.ActiveContext);
            Assert.AreEqual(0, service.ActivationCount);
        }

        private static IEnumerator WaitFor(Task task, string what)
        {
            var deadline = Time.realtimeSinceStartup + 15f;

            while (!task.IsCompleted)
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("timed out waiting for " + what);
                }

                yield return null;
            }

            if (task.IsFaulted)
            {
                Assert.Fail(what + " faulted: " + task.Exception);
            }
        }

        private static PatchManifest Manifest(string fileName)
        {
            return new PatchManifest
            {
                SchemaVersion = 1,
                ContentVersion = "v1",
                PlatformKey = Platform,
                Bundles = new[]
                {
                    new PatchBundleDescriptor
                    {
                        BundleGroupName = "group",
                        FileName = fileName,
                        SizeBytes = 1,
                        Sha256Hex = new string('a', 64)
                    }
                },
                Assets = new[]
                {
                    new PatchAssetDescriptor
                    {
                        AssetKey = "asset",
                        BundleGroupName = "group",
                        InternalPath = "assets/asset"
                    }
                }
            };
        }

        private static string Sha256Hex(byte[] bytes)
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
