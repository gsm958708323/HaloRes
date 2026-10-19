using System;
using System.Collections;
using System.IO;
using Game.AssetSystem.Demos.FullFeature;
using Game.AssetSystem.Integration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.AssetSystem.Tests
{
    /// <summary>
    /// Drives <see cref="FullFeatureScenario"/> - the same calls the demo
    /// dashboard makes - against a throwaway client folder, so the four
    /// interaction paths are asserted instead of clicked.
    ///
    /// Content comes from the EditMode test
    /// <c>FullFeature_BuildsThreeVersionsWithSharedDependency</c> (or the
    /// Tools/Game Asset System/Build Full-Feature Demo Content menu).
    /// </summary>
    public sealed class FullFeatureScenarioTests
    {
        [UnityTest]
        public IEnumerator FullFeature_ShareInterruptResumeActivateReclaim()
        {
            if (!FullFeatureLayout.HasServerContent("v1") ||
                !FullFeatureLayout.HasServerContent("v2"))
            {
                Assert.Ignore(
                    "full-feature content missing. Run Tools/Game Asset System/" +
                    "Build Full-Feature Demo Content first.");
            }

            var clientRoot = Path.Combine(
                Application.temporaryCachePath,
                "FullFeatureScenario-" + Guid.NewGuid().ToString("N"));

            var session = new FullFeatureSession(clientRoot);

            try
            {
                var run = FullFeatureScenario.RunAsync(session);
                var deadline = Time.realtimeSinceStartup + 120f;

                while (!run.IsCompleted)
                {
                    // The load gate and the memory policy only make progress
                    // when the frame ticks, exactly like the dashboard.
                    session.Tick();

                    if (Time.realtimeSinceStartup > deadline)
                    {
                        Assert.Fail("full-feature scenario timed out");
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
                    "full-feature violations: " +
                    string.Join(" | ", report.Violations));

                Assert.AreEqual("v1", report.InitialVersion);
                Assert.AreEqual("v1/UI/Panel", report.PanelLabel);
                Assert.AreEqual("v1", report.SharedVersionV1);

                Assert.AreEqual(2, report.BundlesAfterPanel,
                    "ui_shop + shared loaded for the first panel");
                Assert.AreEqual(3, report.BundlesAfterFx,
                    "shared is reused by battle_fx");
                Assert.AreEqual(3, report.BundlesAfterCached,
                    "a cached asset must not load a bundle again");
                Assert.IsTrue(report.CachedHandleSharesInstance,
                    "two scopes share one asset instance");

                Assert.AreEqual(0, report.HandlesAfterRelease);
                Assert.AreEqual(2, report.IdleAssetsAfterRelease);
                Assert.AreEqual(3, report.ReadyBundlesAfterRelease,
                    "released assets keep their payload and bundle lease");
                Assert.AreEqual(3, report.LoadedPayloadsAfterRelease);
                Assert.IsTrue(report.ReclaimedAfterCritical,
                    "critical pressure reclaims every idle asset and bundle");

                Assert.AreNotEqual(
                    nameof(PatchApplyStatus.Succeeded),
                    report.InterruptedDownload);
                Assert.IsTrue(report.PartialPartKept,
                    "interrupted download keeps a .part file");
                Assert.AreEqual(
                    nameof(PatchApplyStatus.Succeeded),
                    report.FullDownload);
                Assert.Greater(report.ResumeBytes, 0,
                    "second run resumes from the .part length");

                Assert.AreEqual(
                    nameof(PatchActivationStatus.RejectedActiveHandles),
                    report.ActivationWithHandles);
                Assert.AreEqual(
                    nameof(PatchActivationStatus.RejectedUnsafePoint),
                    report.ActivationUnsafePoint);
                Assert.AreEqual(
                    nameof(PatchActivationStatus.Succeeded),
                    report.ActivationFinal);

                Assert.AreEqual("v2", report.ActiveVersionAfterSwitch);
                Assert.AreEqual("v2", report.SharedVersionV2,
                    "content really switched to v2");
                Assert.AreEqual(0, report.LoadedPayloadsAfterFinalReclaim,
                    "no bundle payload leaks after the final reclaim");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(clientRoot))
                    {
                        Directory.Delete(clientRoot, true);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[FullFeature] temp cleanup failed: " + exception.Message);
                }
            }
        }
    }
}
