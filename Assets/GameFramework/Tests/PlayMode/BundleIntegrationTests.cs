using System;
using System.Collections;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework.Asset.Diagnostics;
using GameFramework.Asset.Manifest;
using GameFramework.Asset.Patch;
using GameFramework.Asset.Provider;
using GameFramework.Asset.Samples;
using GameFramework.Asset.Scope;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameFramework.Asset.Tests.PlayMode
{
    public sealed class BundleIntegrationTests
    {
        private string _root;
        private AssetManifest _manifest;
        private AssetSystem _system;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return UniTask.ToCoroutine(async () =>
            {
                _root = Path.Combine(Application.temporaryCachePath, "AssetTests", Guid.NewGuid().ToString("N"));
                var source = Path.Combine(Application.streamingAssetsPath, "GameFrameworkAsset", AssetPlatform.Current);
                Assert.That(File.Exists(Path.Combine(source, "version.json")), Is.True, "Run Game Framework/Assets/Stage Demo For Player before integration tests.");
                _manifest = await new PatchManager(_root, AssetPlatform.Current).RunAsync(source);
                _system = AssetSystem.Initialize(AssetSystemMode.Bundle, _manifest, new AssetPathResolver(_root));
            });
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            yield return UniTask.ToCoroutine(async () =>
            {
                if (_system != null) await _system.ShutdownAsync();
                if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, true);
                _system = null;
            });
        }

        [UnityTest]
        public IEnumerator CityAndBattleRetainSharedMaterialUntilLastOwnerLeaves() => UniTask.ToCoroutine(async () =>
        {
            var city = _system.CreateScope("MainCity"); var battle = _system.CreateScope("Battle");
            var cityInstance = await city.InstantiateAsync("UI/MainCityPanel");
            var battleInstance = await battle.InstantiateAsync("UI/BattlePanel");
            var cityMaterial = cityInstance.GetComponent<Renderer>().sharedMaterial;
            Assert.That(battleInstance.GetComponent<Renderer>().sharedMaterial, Is.SameAs(cityMaterial));
            Assert.That(_system.Tracker.Capture().Bundles.Single(b => b.Name == "ui_common").ReferenceCount, Is.EqualTo(2));
            battle.Dispose();
            await PumpUntil(() => _system.Tracker.Capture().Bundles.Count == 2);
            Assert.That(cityMaterial != null, Is.True);
            Assert.That(cityInstance.GetComponent<Renderer>().sharedMaterial, Is.SameAs(cityMaterial));
            Assert.That(_system.Tracker.Capture().Bundles.Single(b => b.Name == "ui_common").ReferenceCount, Is.EqualTo(1));
            city.Dispose(); await PumpUntil(() => _system.IsIdle);
            Assert.That(cityMaterial == null, Is.True, "Final bundle release must Unload(true).");
        });

        [UnityTest]
        public IEnumerator SceneAndHudShareHeroBundleAndUnloadSceneBeforeBundle() => UniTask.ToCoroutine(async () =>
        {
            var scope = _system.CreateScope("Battle");
            var sceneTask = scope.LoadSceneAsync("Scene/Battle_01").AsTask();
            var hudTask = scope.InstantiateAsync("Hero/Hero001_Hud").AsTask();
            var scene = await sceneTask; await hudTask;
            Assert.That(scene.isLoaded, Is.True);
            Assert.That(_system.Tracker.Capture().Bundles.Single(b => b.Name == "hero_001").ReferenceCount, Is.EqualTo(2));
            scope.ReleaseScene(scene); _system.Tick();
            var duringUnload = _system.Tracker.Capture();
            Assert.That(duringUnload.Bundles.Any(b => b.Name == "scene_battle01"), Is.True);
            await PumpUntil(() => !scene.IsValid() || !scene.isLoaded);
            await PumpUntil(() => _system.Tracker.Capture().Assets.Count == 1);
            Assert.That(_system.Tracker.Capture().Bundles.Single(b => b.Name == "hero_001").ReferenceCount, Is.EqualTo(1));
            scope.Dispose(); await PumpUntil(() => _system.IsIdle);
        });

        [UnityTest]
        public IEnumerator CancelledRealLoadReclaimsLateCompletion() => UniTask.ToCoroutine(async () =>
        {
            var scope = _system.CreateScope("Cancelled");
            var request = scope.LoadAsync<GameObject>("UI/BattlePanel").AsTask();
            scope.Dispose(); await LifecycleTests.ExpectCancelled(request);
            await PumpUntil(() => _system.IsIdle);
            Assert.That(_system.Tracker.Capture().Bundles, Is.Empty);
        });

        [UnityTest]
        public IEnumerator CorruptHeroRollsBackDependenciesWithoutUnloadingCity() => UniTask.ToCoroutine(async () =>
        {
            var city = _system.CreateScope("MainCity");
            var cityObject = await city.InstantiateAsync("UI/MainCityPanel");
            var hero = _manifest.Bundles.Single(bundle => bundle.Name == "hero_001");
            var path = new AssetPathResolver(_root).BundlePath(hero);
            var bytes = File.ReadAllBytes(path); bytes[0] ^= 0xff; File.WriteAllBytes(path, bytes);
            var battle = _system.CreateScope("Battle");
            var scene = battle.LoadSceneAsync("Scene/Battle_01").AsTask();
            var hud = battle.InstantiateAsync("Hero/Hero001_Hud").AsTask();
            await LifecycleTests.ExpectAssetFailure(scene, AssetErrorCode.HashMismatch);
            await LifecycleTests.ExpectAssetFailure(hud, AssetErrorCode.HashMismatch);
            battle.Dispose();
            await PumpUntil(() => _system.Tracker.Capture().Assets.Count == 1 && _system.Tracker.Capture().Bundles.Count == 2);
            Assert.That(cityObject.GetComponent<Renderer>().sharedMaterial != null, Is.True);
            Assert.That(_system.Tracker.Capture().Bundles.All(bundle => bundle.ReferenceCount > 0), Is.True);
        });

        [UnityTest]
        public IEnumerator EditorProviderUsesSameScopeAccountingForAssetsAndScenes() => UniTask.ToCoroutine(async () =>
        {
            await _system.ShutdownAsync();
            _system = AssetSystem.Initialize(AssetSystemMode.Editor, _manifest);
            var scope = _system.CreateScope("Editor");
            var instance = await scope.InstantiateAsync("UI/BattlePanel");
            var scene = await scope.LoadSceneAsync("Scene/Battle_01");
            Assert.That(instance != null && scene.isLoaded, Is.True);
            Assert.That(_system.Tracker.Capture().Assets.Count, Is.EqualTo(2));
            Assert.That(_system.Tracker.Capture().Bundles, Is.Empty);
            scope.Dispose(); await PumpUntil(() => _system.IsIdle);
            Assert.That(scene.IsValid() && scene.isLoaded, Is.False);
        });

        [UnityTest]
        public IEnumerator ReenterWhileSceneUnloadsKeepsNewSceneLease() => UniTask.ToCoroutine(async () =>
        {
            var first = _system.CreateScope("First");
            var oldScene = await first.LoadSceneAsync("Scene/Battle_01");
            first.Dispose(); _system.Tick();
            var next = _system.CreateScope("Next");
            var newScene = await next.LoadSceneAsync("Scene/Battle_01");
            await PumpUntil(() => _system.Tracker.Capture().Assets.Count == 1);
            Assert.That(newScene.isLoaded, Is.True);
            Assert.That(newScene.handle, Is.Not.EqualTo(oldScene.handle));
            next.Dispose(); await PumpUntil(() => _system.IsIdle);
        });

        [UnityTest]
        public IEnumerator MissingBundleFailureCanBeRepairedAndRetriedInSameScope() => UniTask.ToCoroutine(async () =>
        {
            var asset = _manifest.GetAsset("Hero/Hero001_Hud");
            var bundle = _manifest.GetBundle(asset.BundleIndex);
            var path = new AssetPathResolver(_root).BundlePath(bundle);
            var original = File.ReadAllBytes(path);
            File.Delete(path);
            var scope = _system.CreateScope("Repair");
            await LifecycleTests.ExpectAssetFailure(scope.LoadAsync<GameObject>(asset.Address).AsTask(), AssetErrorCode.BundleFileMissing);
            await PumpUntil(() => _system.IsIdle);
            File.WriteAllBytes(path, original);
            var prefab = await scope.LoadAsync<GameObject>(asset.Address);
            Assert.That(prefab != null, Is.True);
            scope.Dispose(); await PumpUntil(() => _system.IsIdle);
        });

        [UnityTest]
        public IEnumerator CancelledSceneLoadUnloadsTheLateScene() => UniTask.ToCoroutine(async () =>
        {
            var sceneCount = SceneManager.sceneCount;
            var scope = _system.CreateScope("LateScene");
            var request = scope.LoadSceneAsync("Scene/Battle_01").AsTask();
            scope.Dispose(); await LifecycleTests.ExpectCancelled(request);
            await PumpUntil(() => _system.IsIdle);
            Assert.That(SceneManager.sceneCount, Is.EqualTo(sceneCount));
        });

        private async UniTask PumpUntil(Func<bool> predicate)
        {
            await LifecycleTests.Until(() => { _system.Tick(); return predicate(); });
        }
    }
}
