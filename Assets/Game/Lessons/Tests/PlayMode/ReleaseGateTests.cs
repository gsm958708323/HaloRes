using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Game.AssetSystem.Integration.L51;
using Game.AssetSystem.Integration.Release.L52;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.AssetSystem.Lessons.Tests
{
    /// <summary>
    /// PlayMode tests for the 5.2 release-hardening pieces:
    /// AssetLoadGate (start budget + concurrency) and InstalledVersionCleaner
    /// (retain N non-active versions, never delete staging or active content).
    /// </summary>
    public sealed class ReleaseGateTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            Game.AssetSystem.Lessons.LessonBootGate.DisableAll();

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
        // 5.2 场景 1：AssetLoadGate 每帧启动预算 + 并发上限
        // ------------------------------------------------------------------
        [UnityTest]
        public IEnumerator L52_AssetLoadGateRespectsStartAndConcurrencyBudget()
        {
            var gate = new AssetLoadGate
            {
                MaxStartsPerFrame = 2,
                MaxConcurrent = 2
            };

            var tasks = new List<Task<IBundlePayload>>();

            for (var i = 0; i < 6; i++)
            {
                tasks.Add(gate.Enqueue(() => SlowPayloadAsync(30), "owner_" + i));
            }

            Assert.AreEqual(6, gate.PendingCount, "all requests start queued");

            var deadline = Time.realtimeSinceStartup + 20f;

            while (gate.PendingCount > 0 || gate.RunningCount > 0)
            {
                gate.Tick();

                Assert.LessOrEqual(
                    gate.LastFrameStarts,
                    2,
                    "MaxStartsPerFrame must be respected");

                Assert.LessOrEqual(
                    gate.RunningCount,
                    2,
                    "MaxConcurrent must be respected");

                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("AssetLoadGate drained too slowly");
                }

                yield return null;
            }

            while (!AllDone(tasks))
            {
                yield return null;
            }

            Assert.AreEqual(6, gate.TotalStarted, "all requests started");
            Assert.AreEqual(6, gate.TotalCompleted, "all requests completed");
            Assert.AreEqual(0, gate.PendingCount);
            Assert.AreEqual(0, gate.RunningCount);
        }

        // ------------------------------------------------------------------
        // 5.2 场景 3：清理旧版本时保留 active + N 个非活动版本，staging 占用的版本不删
        // ------------------------------------------------------------------
        [Test]
        public void L52_InstalledVersionCleanerRetainsActiveAndNewest()
        {
            var contentRoot = Path.Combine(_root, "content");
            var stagingRoot = Path.Combine(_root, "staging");

            Directory.CreateDirectory(contentRoot);
            Directory.CreateDirectory(stagingRoot);

            var versions = new[] { "v1", "v2", "v3", "v4" };

            for (var i = 0; i < versions.Length; i++)
            {
                Directory.CreateDirectory(Path.Combine(contentRoot, versions[i]));
                Directory.CreateDirectory(Path.Combine(contentRoot, versions[i], "dummy"));
            }

            Directory.CreateDirectory(Path.Combine(stagingRoot, "v2"));

            var cleaner = new InstalledVersionCleaner();
            var report = cleaner.Clean(contentRoot, stagingRoot, "v3", 1);

            Assert.AreEqual("v3", report.ActiveVersion);
            Assert.AreEqual(0, report.ErrorMessages.Count, "cleanup must not error");

            Assert.IsTrue(
                Directory.Exists(Path.Combine(contentRoot, "v3")),
                "active version must be kept");

            Assert.IsTrue(
                Directory.Exists(Path.Combine(contentRoot, "v4")),
                "newest non-active version must be retained");

            Assert.IsFalse(
                Directory.Exists(Path.Combine(contentRoot, "v1")),
                "oldest version should be deleted");

            Assert.IsTrue(
                Directory.Exists(Path.Combine(contentRoot, "v2")),
                "version with staging content must not be deleted");

            Assert.AreEqual(1, report.Deleted, "one version deleted");
            Assert.AreEqual(1, report.SkippedBecauseStaging, "v2 skipped because of staging");
            CollectionAssert.Contains(report.RetainedVersions, "v4");
        }

        private static async Task<IBundlePayload> SlowPayloadAsync(int delayMs)
        {
            await Task.Delay(delayMs);
            return new FakePayload();
        }

        private static bool AllDone(List<Task<IBundlePayload>> tasks)
        {
            for (var i = 0; i < tasks.Count; i++)
            {
                if (!tasks[i].IsCompleted)
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class FakePayload : IBundlePayload
        {
            public string DebugName
            {
                get { return "fake"; }
            }

            public object LoadAsset(string internalAssetPath)
            {
                return null;
            }

            public void Release()
            {
            }
        }
    }
}
