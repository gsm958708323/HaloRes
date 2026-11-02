using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using GameFramework.Asset;
using GameFramework.Asset.Manifest;
using GameFramework.UI.Samples;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameFramework.UI.Tests
{
    public sealed class UIBundleIntegrationTests
    {
        [UnityTest] public IEnumerator RealBundlesReuseRootAndReleaseNativeInstancesBeforeDependencies() => UniTask.ToCoroutine(async () =>
        {
            var root = Path.GetFullPath("Build/UIContent/" + UIDemo.PlatformName + "/v1");
            Assert.That(File.Exists(Path.Combine(root, "asset_manifest.json")), Is.True, "Stage UI sample bundles before testing.");
            var assets = AssetSystem.Initialize(AssetSystemMode.Bundle, AssetManifest.FromFile(Path.Combine(root, "asset_manifest.json")), new AssetPathResolver(root));
            var owner = new GameObject("UI Bundle Test"); UIManager ui = null;
            try
            {
                using (var configScope = assets.CreateScope("Config"))
                {
                    var config = await configScope.LoadAsync<UIConfigSO>("UI/Config");
                    ui = new UIManager(assets, config, owner.transform);
                }
                var first = ui.Open<FullView1>(); var a = await first.Opened;
                var b1 = ui.Open<FullView2>(); var b = await b1.Opened;
                var c = ui.Open<ChildView2>(); await c.Opened;
                var b2 = ui.Open<FullView2>(); Assert.That(await b2.Opened, Is.SameAs(b));
                Assert.That(ui.InstanceCount, Is.EqualTo(3));
                await ui.PopAsync(LayerType.NormalLayer); Assert.That((await c.Opened).IsVisible, Is.True);
                await ui.PopAsync(LayerType.NormalLayer); Assert.That(b.ActiveNavigation, Is.SameAs(b1));
                await ui.PopAsync(LayerType.NormalLayer); Assert.That(a.IsVisible, Is.True);
                var child = await a.OpenOneChildUIAsync<ChildView1>(); Assert.That(child.IsVisible, Is.True);
                var popup = ui.Open<TipsView1>(); await popup.Opened; Assert.That(a.IsVisible, Is.True);
                ui.Dispose(); assets.Tick();
                Assert.That(assets.IsIdle, Is.False, "Prefab dependencies must survive deferred native destruction.");
                var deadline = Time.realtimeSinceStartup + 15;
                while (!assets.IsIdle && Time.realtimeSinceStartup < deadline) { await UniTask.NextFrame(); assets.Tick(); }
                Assert.That(assets.IsIdle, Is.True);
                Assert.That(assets.Tracker.Capture().Assets, Is.Empty);
                Assert.That(assets.Tracker.Capture().Bundles, Is.Empty);
            }
            finally
            {
                ui?.Dispose(); await assets.ShutdownAsync(); UnityEngine.Object.Destroy(owner);
            }
        });
    }
}
