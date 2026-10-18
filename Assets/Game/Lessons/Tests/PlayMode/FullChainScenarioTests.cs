using System;
using System.Collections;
using System.Threading.Tasks;
using Game.AssetSystem.FullChain;
using Game.AssetSystem.Integration.L51;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Game.AssetSystem.Lessons.Tests
{
    /// <summary>
    /// Full-chain PlayMode test: real bundles built by 4.3, loaded through
    /// 5.2's RealBundlePayloadFactory, orchestrated by 5.1's version context,
    /// bundle runtime, asset cache and memory evaluator.
    /// Content is produced by the EditMode test FullChain_BuildsV1AndV2Bundles.
    /// </summary>
    public sealed class FullChainScenarioTests
    {
        [SetUp]
        public void SetUp()
        {
            Game.AssetSystem.Lessons.LessonBootGate.DisableAll();
        }

        [UnityTest]
        public IEnumerator FullChain_RealBundlesAcrossVersionSwitch()
        {
            if (!FullChainLayout.HasContent(FullChainLayout.V1Version) ||
                !FullChainLayout.HasContent(FullChainLayout.V2Version))
            {
                Assert.Ignore(
                    "full-chain content missing. Run Tools/Game Asset System/" +
                    "Build Full-Chain Demo Content (or the EditMode test) first.");
            }

            var run = FullChainRunner.RunAsync();

            var deadline = UnityEngine.Time.realtimeSinceStartup + 60f;

            while (!run.IsCompleted)
            {
                if (UnityEngine.Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("full-chain run timed out");
                }

                yield return null;
            }

            if (run.IsFaulted && run.Exception != null)
            {
                throw run.Exception;
            }

            var report = run.Result;

            Assert.IsTrue(report.ContentFound, "content must be present");
            Assert.IsTrue(
                report.Passed,
                "full-chain violations: " + string.Join(" | ", report.Violations));

            Assert.AreEqual(nameof(PatchActivationStatus.Succeeded), report.V1Activation);
            Assert.AreEqual(
                nameof(PatchActivationStatus.RejectedActiveHandles),
                report.V2ActivationWithHandles);
            Assert.AreEqual(
                nameof(PatchActivationStatus.RejectedUnsafePoint),
                report.V2ActivationUnsafePoint);
            Assert.AreEqual(
                nameof(PatchActivationStatus.Succeeded),
                report.V2ActivationFinal);

            Assert.AreEqual(2, report.Owners, "UI + Battle owners");
            Assert.AreEqual(1, report.BundleLoadsV1, "shared bundle must load once");
            Assert.IsTrue(report.SameAssetInstance, "both owners share one asset instance");
            Assert.AreEqual(1, report.LiveHandlesAfterUiDispose, "battle keeps its handle");

            Assert.AreEqual("ShopRoot", report.V1Label);
            Assert.AreEqual("ShopRoot" + FullChainLayout.V2LabelSuffix, report.V2Label);

            Assert.AreEqual(1, report.BundleLoadsV2, "v2 runtime loads its bundle once");
            Assert.GreaterOrEqual(report.ReclaimedAssets, 1, "asset reclaimed");
            Assert.GreaterOrEqual(report.ReclaimedBundles, 1, "bundle reclaimed");
            Assert.AreEqual(0, report.RemainingLiveHandles, "no leaked handles");
        }
    }
}
