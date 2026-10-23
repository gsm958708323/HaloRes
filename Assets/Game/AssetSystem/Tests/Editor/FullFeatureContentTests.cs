using System.IO;
using Game.AssetSystem.Demos.FullFeature;
using Game.AssetSystem.Editor.Building;
using NUnit.Framework;

namespace Game.AssetSystem.Tests
{
    /// <summary>
    /// Produces the content the full-feature demo consumes: three versions of
    /// the shared / panel / hit assets packed into three bundles each, with the
    /// shared dependency declared in the manifest.
    /// </summary>
    public sealed class FullFeatureContentTests
    {
        [Test]
        public void FullFeature_BuildsThreeVersionsWithSharedDependency()
        {
            FullFeatureContentBuilder.BuildAll();

            for (var i = 0; i < FullFeatureLayout.Versions.Length; i++)
            {
                var version = FullFeatureLayout.Versions[i];
                var directory = FullFeatureLayout.ServerVersionDir(version);

                Assert.IsTrue(
                    FullFeatureLayout.HasServerContent(version),
                    version + " manifest missing at " +
                    FullFeatureLayout.ServerManifestPath(version));

                Assert.IsTrue(
                    File.Exists(Path.Combine(directory, FullFeatureContent.UiShopGroup)),
                    version + " ui_shop bundle missing");

                Assert.IsTrue(
                    File.Exists(Path.Combine(directory, FullFeatureContent.BattleFxGroup)),
                    version + " battle_fx bundle missing");

                Assert.IsTrue(
                    File.Exists(Path.Combine(directory, FullFeatureContent.SharedGroup)),
                    version + " shared bundle missing");

                var manifest = FullFeatureLayout.LoadManifest(
                    FullFeatureLayout.ServerManifestPath(version));

                Assert.AreEqual(version, manifest.ContentVersion);
                Assert.AreEqual(3, manifest.Bundles.Length, "three bundles");
                Assert.AreEqual(3, manifest.Assets.Length, "three assets");

                var shared = FindBundle(manifest, FullFeatureContent.SharedGroup);

                Assert.IsNotNull(shared, "shared bundle in manifest");
                Assert.IsNotNull(shared.Dependencies);
                Assert.AreEqual(0, shared.Dependencies.Length,
                    "shared must not depend on anything");

                AssertDependsOnShared(
                    FindBundle(manifest, FullFeatureContent.UiShopGroup),
                    "ui_shop");

                AssertDependsOnShared(
                    FindBundle(manifest, FullFeatureContent.BattleFxGroup),
                    "battle_fx");
            }
        }

        private static void AssertDependsOnShared(
            Patch.PatchBundleDescriptor bundle,
            string group)
        {
            Assert.IsNotNull(bundle, group + " bundle in manifest");
            Assert.IsNotNull(bundle.Dependencies, group + " dependencies");
            Assert.AreEqual(1, bundle.Dependencies.Length,
                group + " must declare exactly one dependency");
            Assert.AreEqual(FullFeatureContent.SharedGroup, bundle.Dependencies[0]);
        }

        private static Patch.PatchBundleDescriptor FindBundle(
            Patch.PatchManifest manifest,
            string group)
        {
            for (var i = 0; i < manifest.Bundles.Length; i++)
            {
                if (string.Equals(
                    manifest.Bundles[i].BundleGroupName,
                    group,
                    System.StringComparison.Ordinal))
                {
                    return manifest.Bundles[i];
                }
            }

            return null;
        }
    }
}
