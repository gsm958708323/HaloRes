using System;
using GameFramework.Asset.Manifest;
using GameFramework.UI.Samples;
using NUnit.Framework;
using UnityEngine;

namespace GameFramework.UI.Tests
{
    public sealed class UIConfigurationTests
    {
        private UIConfigSO _config;
        private AssetManifest _manifest;
        [SetUp] public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<UIConfigSO>();
            _config.Views = new[] { Config<FullView1>(), Config<ChildView1>(), Config<ChildView2>() };
            _manifest = AssetManifest.FromJson(JsonUtility.ToJson(new AssetManifestFile {
                version = 1, bundles = new[] { new ManifestBundleData { name = "ui", hash = new string('0', 64) } },
                assets = new[] { new ManifestAssetData { address = "Prefab", assetPath = "Assets/Prefab.prefab" },
                    new ManifestAssetData { address = "Scene", assetPath = "Assets/Scene.unity", kind = AssetKind.Scene } }
            }));
        }
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(_config);
        private static UIConfig Config<T>() => new UIConfig { ViewType = typeof(T).AssemblyQualifiedName, PrefabAddress = "Prefab" };
        private static UIChildConfig Child<T>(string path = null) => new UIChildConfig {
            ViewType = typeof(T).AssemblyQualifiedName, NodePath = path, ResType = path == null ? ResType.Dynamic : ResType.Static };

        [Test] public void ConfigurationIsCopiedBeforeUse()
        {
            _config.Views[0].ChildList = new[] { Child<ChildView1>("Panel/Child") };
            var snapshot = _config.CreateSnapshot(_manifest);
            _config.Views[0].PrefabAddress = "Changed";
            _config.Views[0].ChildList[0].NodePath = "Changed";
            Assert.That(snapshot[typeof(FullView1)].PrefabAddress, Is.EqualTo("Prefab"));
            Assert.That(snapshot[typeof(FullView1)].Children[0].NodePath, Is.EqualTo("Panel/Child"));
        }
        [Test] public void DuplicateTypesAreRejected()
        { _config.Views = new[] { Config<FullView1>(), Config<FullView1>() }; Assert.Throws<ArgumentException>(() => _config.CreateSnapshot(_manifest)); }
        [Test] public void UnresolvedTypeIsRejected()
        { _config.Views[0].ViewType = "Missing, Missing"; Assert.Throws<ArgumentException>(() => _config.CreateSnapshot(_manifest)); }
        [Test] public void SceneCannotBeUsedAsPrefab()
        { _config.Views[0].PrefabAddress = "Scene"; Assert.Throws<ArgumentException>(() => _config.CreateSnapshot(_manifest)); }
        [Test] public void MissingChildConfigurationIsRejected()
        { _config.Views[0].ChildList = new[] { Child<ChildView3>() }; Assert.Throws<ArgumentException>(() => _config.CreateSnapshot(_manifest)); }
        [Test] public void ChildCyclesAreRejected()
        { _config.Views[1].ChildList = new[] { Child<ChildView2>() }; _config.Views[2].ChildList = new[] { Child<ChildView1>() }; Assert.Throws<ArgumentException>(() => _config.CreateSnapshot(_manifest)); }
        [TestCase("../Node")] [TestCase("/Node")] [TestCase("Node/../Other")] [TestCase("Node//Other")]
        public void InvalidStaticPathsAreRejected(string path)
        { _config.Views[0].ChildList = new[] { Child<ChildView1>(path) }; Assert.Throws<ArgumentException>(() => _config.CreateSnapshot(_manifest)); }
        [Test] public void OverlappingBorrowedSiblingsAreRejected()
        { _config.Views[0].ChildList = new[] { Child<ChildView1>("Panel"), Child<ChildView2>("Panel/Child") }; Assert.Throws<ArgumentException>(() => _config.CreateSnapshot(_manifest)); }
        [TestCase(0f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidResolutionIsRejected(float width)
        { _config.ReferenceResolution = new Vector2(width, 1080); Assert.Throws<ArgumentException>(() => _config.CreateSnapshot(_manifest)); }
        [Test] public void LifecycleNamesRemainAvailableForOverrides()
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (var name in new[] { "OnAwake", "OnStart", "OnEnable", "OnDisable", "OnDestroy" })
                Assert.That(typeof(UIView).GetMethod(name, flags)?.IsVirtual, Is.True, name);
        }
    }
}
