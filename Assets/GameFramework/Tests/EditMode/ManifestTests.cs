using System;
using System.Collections.Generic;
using GameFramework.Asset.Editor.Build;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Patch;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace GameFramework.Asset.Tests.EditMode
{
    public sealed class ManifestTests
    {
        private static AssetManifestFile ValidFile() => new AssetManifestFile {
            version = 1,
            bundles = new[] { new ManifestBundleData { name = "main", hash = new string('a', 64), size = 42 } },
            assets = new[] { new ManifestAssetData { address = "Item", assetPath = "Assets/Item.prefab", bundleIndex = 0 } }
        };

        [Test]
        public void RoundTripPreservesAddressAndBundle()
        {
            var manifest = AssetManifest.FromJson(JsonUtility.ToJson(ValidFile()));
            var copy = AssetManifest.FromJson(manifest.ToJson());
            Assert.That(copy.GetAsset("Item").AssetPath, Is.EqualTo("Assets/Item.prefab"));
            Assert.That(copy.GetBundle(0).Size, Is.EqualTo(42));
            Assert.That(manifest.TryGetAsset(null, out _), Is.False);
        }

        [Test]
        public void RuntimeManifestDoesNotExposeMutableArrays()
        {
            var manifest = AssetManifest.FromJson(JsonUtility.ToJson(ValidFile()));
            Assert.Throws<NotSupportedException>(() => ((IList<BundleInfo>)manifest.Bundles)[0] = null);
            Assert.Throws<NotSupportedException>(() => ((IList<AssetInfo>)manifest.Assets)[0] = null);
        }

        [Test]
        public void DuplicateAddressIsRejected()
        {
            var file = ValidFile();
            file.assets = new[] { file.assets[0], file.assets[0] };
            Assert.Throws<ArgumentException>(() => AssetManifest.FromJson(JsonUtility.ToJson(file)));
        }

        [TestCase("Assets/../Item.prefab")]
        [TestCase("Outside/Item.prefab")]
        [TestCase("Assets\\Item.prefab")]
        public void UnsafeAssetPathIsRejected(string path)
        {
            var file = ValidFile(); file.assets[0].assetPath = path;
            Assert.Throws<ArgumentException>(() => AssetManifest.FromJson(JsonUtility.ToJson(file)));
        }

        [Test]
        public void HashCannotEscapeContentRoot()
        {
            var file = ValidFile(); file.bundles[0].hash = "../../malicious";
            Assert.Throws<ArgumentException>(() => AssetManifest.FromJson(JsonUtility.ToJson(file)));
        }

        [TestCase(-1)]
        [TestCase(1)]
        public void InvalidBundleIndexIsRejected(int index)
        {
            var file = ValidFile(); file.assets[0].bundleIndex = index;
            Assert.Throws<ArgumentException>(() => AssetManifest.FromJson(JsonUtility.ToJson(file)));
        }

        [Test]
        public void DuplicateOrSelfDependencyIsRejected()
        {
            var file = ValidFile(); file.bundles[0].dependencies = new[] { 0 };
            Assert.Throws<ArgumentException>(() => AssetManifest.FromJson(JsonUtility.ToJson(file)));
        }

        [Test]
        public void IncompleteTransitiveClosureIsRejected()
        {
            var file = ValidFile();
            file.bundles = new[] {
                new ManifestBundleData { name = "a", hash = new string('a',64), dependencies = new[] { 1 } },
                new ManifestBundleData { name = "b", hash = new string('b',64), dependencies = new[] { 2 } },
                new ManifestBundleData { name = "c", hash = new string('c',64) }
            };
            Assert.Throws<ArgumentException>(() => AssetManifest.FromJson(JsonUtility.ToJson(file)));
        }

        [Test]
        public void FlattenHandlesCyclesAndDiamondDependencies()
        {
            var graph = new[] { new[] { 1, 2 }, new[] { 0, 3 }, new[] { 3 }, Array.Empty<int>() };
            Assert.That(ManifestGenerator.FlattenDependencies(0, graph), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(ManifestGenerator.FlattenDependencies(1, graph), Is.EqualTo(new[] { 0, 2, 3 }));
        }

        [Test]
        public void VersionChecksPlatformAndHash()
        {
            var version = new VersionFile { version = 1, platform = "Windows", manifestHash = new string('a',64), manifestSize = 20 };
            Assert.That(VersionFile.Parse(version.ToJson(), "Windows").version, Is.EqualTo(1));
            Assert.Throws<Diagnostics.AssetException>(() => VersionFile.Parse(version.ToJson(), "Android"));
        }

        [TestCase(BuildTarget.Android, "Android")]
        [TestCase(BuildTarget.iOS, "iOS")]
        [TestCase(BuildTarget.StandaloneWindows64, "Windows")]
        [TestCase(BuildTarget.StandaloneOSX, "OSX")]
        [TestCase(BuildTarget.StandaloneLinux64, "Linux")]
        public void BuildContentUsesTargetPlatformInsteadOfEditorPlatform(BuildTarget target, string expected)
        {
            Assert.That(BundleBuilder.PlatformName(target), Is.EqualTo(expected));
        }
    }
}
