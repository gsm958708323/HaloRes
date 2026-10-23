using System.Collections;
using System.IO;
using System.Threading;
using Game.AssetSystem.Bundles;
using Game.AssetSystem.Content;
using Game.AssetSystem.FullChain;
using Game.AssetSystem.Release;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Game.AssetSystem.Tests
{
    /// <summary>
    /// Guards the one property the release loader exists for: a bundle load
    /// must not be a single-frame block.
    ///
    /// Content is the full-chain build output (EditMode test
    /// <c>FullChain_BuildsV1AndV2Bundles</c> or the matching menu item), so the
    /// file really comes off disk through Unity's async bundle reader.
    /// </summary>
    public sealed class RealBundleLoaderTests
    {
        [UnityTest]
        public IEnumerator RealBundleLoader_CompletesAcrossFrames()
        {
            if (!FullChainLayout.HasContent(FullChainLayout.V1Version))
            {
                Assert.Ignore(
                    "full-chain content missing. Run Tools/Game Asset System/" +
                    "Build Full-Chain Demo Content first.");
            }

            var contentDir = FullChainLayout.ContentDir(FullChainLayout.V1Version);
            var manifest = BuildManifestReader.Read(
                FullChainLayout.ManifestPath(FullChainLayout.V1Version));

            Assert.Greater(manifest.Bundles.Length, 0, "manifest has no bundles");

            var descriptor = manifest.Bundles[0];
            var info = new BundleInfo
            {
                Id = new BundleId(0),
                Name = descriptor.BundleGroupName,
                SourcePath = Path.Combine(contentDir, descriptor.FileName)
            };

            var loader = new RealBundleLoader();
            var task = loader.LoadAsync(info, CancellationToken.None);

            // LoadFromFileAsync cannot finish in the frame it was started in,
            // and the loader only polls from the next frame on. If this ever
            // becomes true, the loader has silently gone back to a blocking
            // LoadFromFile.
            Assert.IsFalse(
                task.IsCompleted,
                "bundle load completed inside the starting frame; " +
                "the loader is blocking again");

            var frames = 0;

            while (!task.IsCompleted && frames < 300)
            {
                frames++;
                yield return null;
            }

            Assert.IsTrue(task.IsCompleted, "bundle load never completed");
            Assert.GreaterOrEqual(
                frames,
                1,
                "bundle load must span at least one frame boundary");
            Assert.IsNull(task.Exception, "bundle load faulted: " + task.Exception);

            var payload = task.Result;

            Assert.IsNotNull(payload, "loader returned a null payload");
            Assert.IsInstanceOf<RealBundlePayload>(payload);

            payload.Release();
        }
    }
}
