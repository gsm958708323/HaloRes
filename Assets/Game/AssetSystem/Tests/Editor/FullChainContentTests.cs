using System.IO;
using Game.AssetSystem.Editor.Building;
using Game.AssetSystem.FullChain;
using NUnit.Framework;

namespace Game.AssetSystem.Tests
{
    /// <summary>
    /// Produces the assets the full-chain demo consumes: sample content plus
    /// two real AssetBundle sets (v1 / v2). Runs before the PlayMode chain test.
    /// </summary>
    public sealed class FullChainContentTests
    {
        [Test]
        public void FullChain_BuildsV1AndV2Bundles()
        {
            FullChainContentBuilder.BuildAll();

            Assert.IsTrue(
                FullChainLayout.HasContent(FullChainLayout.V1Version),
                "v1 manifest missing at " + FullChainLayout.ManifestPath(FullChainLayout.V1Version));

            Assert.IsTrue(
                FullChainLayout.HasContent(FullChainLayout.V2Version),
                "v2 manifest missing at " + FullChainLayout.ManifestPath(FullChainLayout.V2Version));

            var v1Dir = FullChainLayout.ContentDir(FullChainLayout.V1Version);

            Assert.IsTrue(File.Exists(Path.Combine(v1Dir, "ui_shop")), "ui_shop bundle missing");
            Assert.IsTrue(File.Exists(Path.Combine(v1Dir, "battle_fx")), "battle_fx bundle missing");
            Assert.IsTrue(File.Exists(Path.Combine(v1Dir, "battle_audio")), "battle_audio bundle missing");

            var v1Manifest = File.ReadAllText(FullChainLayout.ManifestPath(FullChainLayout.V1Version));
            var v2Manifest = File.ReadAllText(FullChainLayout.ManifestPath(FullChainLayout.V2Version));

            Assert.AreNotEqual(v1Manifest, v2Manifest, "v1 and v2 manifests must differ");
        }
    }
}
