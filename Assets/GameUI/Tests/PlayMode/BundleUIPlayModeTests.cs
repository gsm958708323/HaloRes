using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using GameFramework.Asset;
using GameFramework.Asset.Manifest;
using GameUI.Demo;
using GameUI.Generated;
using GameUI.Kernel;
using GameUI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GameUI.Tests.PlayMode
{
    public sealed class BundleUIPlayModeTests
    {
        private AssetSystem _system;
        private GameObject _driverObject;
        private static string Packaged => Path.Combine(Application.streamingAssetsPath,"GameUIAsset",GameUIDemoBootstrap.PlatformName);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var path=Path.Combine(Packaged,"asset_manifest.json");
            Assert.That(File.Exists(path),Is.True,"Run GameUI > Stage Demo Bundles before Bundle UI tests.");
            var scene=SceneManager.LoadSceneAsync("Assets/GameUI/Demo/GameUIDemo.unity");
            while(!scene.isDone)yield return null;
            yield return Wait(()=>UIBoot.Kernel!=null && UI.IsOpen<DemoHomeView>() && !UIBoot.Kernel.Presentation.InputGateLocked);
            UIBoot.Shutdown();yield return null;
            var manifest=AssetManifest.FromJson(File.ReadAllText(path));
            _system=AssetSystem.Initialize(AssetSystemMode.Bundle,manifest,new AssetPathResolver(
                Path.Combine(Application.temporaryCachePath,"GameUIBundleTests"),Packaged));
            _driverObject=new GameObject("Bundle UI Test Driver");
            _driverObject.AddComponent<AssetSystemDriver>().Bind(_system);
            UIBoot.Install(new UIBootConfig {System=_system,Registry=GameUI_DemoRegistry.Create(),PoolCapacity=0});
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UIBoot.Shutdown();
            if(_system!=null)
            {
                var shutdown=_system.ShutdownAsync();
                yield return Wait(()=>shutdown.Status!=UniTaskStatus.Pending);
                shutdown.GetAwaiter().GetResult();
                Assert.That(_system.Tracker.Capture().Assets.Count,Is.Zero);
                Assert.That(_system.Tracker.Capture().Bundles.Count,Is.Zero);
            }
            if(_driverObject!=null)UnityEngine.Object.Destroy(_driverObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BundledViewsLoadReuseAndReleaseWithExternalSystem()
        {
            var home=UI.Open<DemoHomeView>();
            yield return Wait(()=>home.Phase==UIPhase.Shown);
            Assert.That(_system.Tracker.Capture().Bundles.Count,Is.GreaterThan(0));
            var popup=UI.Open<DemoPopupView>();yield return Wait(()=>popup.Phase==UIPhase.Shown);
            var first=GameObject.Find("DemoPopup[UI]");
            popup.Close();yield return Wait(()=>!popup.IsAlive);
            popup=UI.Open<DemoPopupView>();yield return Wait(()=>popup.Phase==UIPhase.Shown);
            Assert.That(GameObject.Find("DemoPopup[UI]"),Is.SameAs(first));
            UIBoot.Shutdown();yield return null;_system.Tick();
            Assert.That(_system.IsDisposed,Is.False);
        }

        [UnityTest]
        public IEnumerator BundledWidgetsKeepModelDependenciesAliveUntilDestruction()
        {
            var handle=UI.Open<DemoWidgetsView>();yield return Wait(()=>handle.Phase==UIPhase.Shown);
            var widgets=GameObject.Find("DemoWidgets[UI]");
            var image=widgets.transform.Find("Model").GetComponent<RawImage>();
            Assert.That(image.texture,Is.Not.Null);
            Assert.That(widgets.transform.Find("List/Viewport/Content").childCount,Is.LessThan(20));
            handle.Close();yield return Wait(()=>!handle.IsAlive);
            yield return null;yield return null;_system.Tick();
            Assert.That(GameObject.Find("DemoWidgets[UI]"),Is.Null);
            Assert.That(_system.Tracker.Capture().Assets.Count,Is.Zero,_system.Tracker.CreateLeakReport());
        }

        [UnityTest]
        public IEnumerator ClosingBundledViewDuringLoadLeavesNoLateInstance()
        {
            var handle=UI.Open<DemoWidgetsView>();handle.Close();
            yield return Wait(()=>!handle.IsAlive);
            for(var i=0;i<30;i++)yield return null;
            _system.Tick();
            Assert.That(GameObject.Find("DemoWidgets[UI]"),Is.Null);
            Assert.That(_system.Tracker.Capture().Assets.Count,Is.Zero,_system.Tracker.CreateLeakReport());
        }

        private static IEnumerator Wait(Func<bool> condition)
        {
            var deadline=Time.realtimeSinceStartup+20;
            while(!condition())
            {
                if(Time.realtimeSinceStartup>deadline)Assert.Fail(UIBoot.Kernel?.Dump()??"Bundle operation timed out.");
                yield return null;
            }
        }
    }
}
