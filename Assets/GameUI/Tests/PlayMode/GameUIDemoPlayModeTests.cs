using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameUI.Demo;
using GameUI.Runtime;
using GameUI.Kernel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

namespace GameUI.Tests.PlayMode
{
    public sealed class GameUIDemoPlayModeTests
    {
        private const string ScenePath="Assets/GameUI/Demo/GameUIDemo.unity";

        [UnitySetUp]
        public IEnumerator LoadDemoScene()
        {
            Time.timeScale=1;
            var operation=SceneManager.LoadSceneAsync(ScenePath,LoadSceneMode.Single);
            Assert.That(operation,Is.Not.Null);
            while(!operation.isDone) yield return null;
            yield return WaitFor(()=>UIBoot.Kernel!=null && UI.IsOpen<DemoHomeView>() && IsVisible("DemoHome[UI]") && !UIBoot.Kernel.Presentation.InputGateLocked,"Home did not load.");
        }
        [UnityTearDown]
        public IEnumerator ShutdownDemo()
        {
            Time.timeScale=1;
            UIBoot.Shutdown();
            yield return null;
        }
        [UnityTest]
        public IEnumerator DemoHomeLoadsAndWiresButtons()
        {
            var home=GameObject.Find("DemoHome[UI]");
            Assert.That(home.transform.Find("OpenPopup").GetComponent<Button>(),Is.Not.Null);
            Assert.That(UI.IsOpen<DemoHomeView>(),Is.True);
            Assert.That(UIBoot.Root.RootCanvas.renderMode,Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(UIBoot.Camera.WorldCamera.GetComponent<UniversalAdditionalCameraData>().cameraStack,Does.Contain(UIBoot.Camera.UICamera));
            yield return null;
        }
        [UnityTest]
        public IEnumerator OpenPopupButtonShowsPopup()
        {
            Click("DemoHome[UI]","OpenPopup");
            yield return Settled(()=>UI.IsOpen<DemoPopupView>());
            Assert.That(IsVisible("DemoPopup[UI]"),Is.True);
            Click("DemoPopup[UI]","ClosePopup");
            yield return Settled(()=>!UI.IsOpen<DemoPopupView>());
            Assert.That(IsVisible("DemoPopup[UI]"),Is.False);
            Assert.That(GameObject.Find("DemoPopup[UI]").activeSelf,Is.True);
        }
        [UnityTest]
        public IEnumerator OpenOverlayButtonShowsOverlay()
        {
            Click("DemoHome[UI]","OpenOverlay");
            yield return Settled(()=>UI.IsOpen<DemoOverlayView>());
            Click("DemoOverlay[UI]","CloseOverlay");
            yield return Settled(()=>!UI.IsOpen<DemoOverlayView>());
            Assert.That(IsVisible("DemoOverlay[UI]"),Is.False);
        }
        [UnityTest]
        public IEnumerator MaskBlocksLowerViewAndClickClosesPopup()
        {
            UI.Open<DemoPopupView>();
            yield return Settled(()=>UI.IsOpen<DemoPopupView>());
            Assert.That(UIBoot.Root.Mask.raycastTarget,Is.True);
            var hits=RaycastAt(new Vector2(5,5));
            Assert.That(hits.Exists(h=>h.gameObject==UIBoot.Root.Mask.gameObject),Is.True,"Mask must intercept raycasts outside the popup.");
            UIBoot.Root.Mask.GetComponent<Button>().onClick.Invoke();
            yield return Settled(()=>!UI.IsOpen<DemoPopupView>());
        }
        [UnityTest]
        public IEnumerator SystemOverlayKeepsOwnButtonInteractive()
        {
            UI.Open<DemoSystemView>();
            yield return Settled(()=>UI.IsOpen<DemoSystemView>());
            Assert.That(UIBoot.Kernel.Presentation.InputLocked,Is.True);
            Assert.That(UIBoot.Kernel.Presentation.InputGateLocked,Is.False);
            var button=GameObject.Find("DemoSystem[UI]").transform.Find("CloseSystem").GetComponent<Button>();
            var point=RectTransformUtility.WorldToScreenPoint(UIBoot.Camera.UICamera,button.transform.position);
            var hits=RaycastAt(point);
            Assert.That(hits.Count,Is.GreaterThan(0));
            Assert.That(hits[0].gameObject.GetComponentInParent<Button>(),Is.SameAs(button));
            button.onClick.Invoke();
            yield return Settled(()=>!UI.IsOpen<DemoSystemView>());
        }
        [UnityTest]
        public IEnumerator TypedResultCompletesWithValue()
        {
            var handle=UI.Open<DemoConfirmView,bool>(new DemoConfirmArgs{Message="Test question"});
            yield return Settled(()=>UI.IsOpen<DemoConfirmView>());
            Assert.That(GameObject.Find("DemoConfirm[UI]").transform.Find("Message").GetComponent<Text>().text,Is.EqualTo("Test question"));
            var result=handle.WaitResult();
            Click("DemoConfirm[UI]","Confirm");
            yield return Settled(()=>!UI.IsOpen<DemoConfirmView>());
            Assert.That(result.Status,Is.EqualTo(UniTaskStatus.Succeeded));
            var value=result.GetAwaiter().GetResult();
            Assert.That(value.HasValue,Is.True);Assert.That(value.Value,Is.True);
        }
        [UnityTest]
        public IEnumerator TimelineRunsAtZeroTimeScale()
        {
            Time.timeScale=0;
            var handle=UI.Open<DemoPopupView>();
            yield return Settled(()=>handle.Phase==UIPhase.Shown);
            handle.Close();
            yield return Settled(()=>!handle.IsAlive);
        }
        [UnityTest]
        public IEnumerator WidgetsUseBoundedListAndVisibleModel()
        {
            var handle=UI.Open<DemoWidgetsView>();
            yield return Settled(()=>handle.Phase==UIPhase.Shown);
            var widgets=GameObject.Find("DemoWidgets[UI]");
            var content=widgets.transform.Find("List/Viewport/Content");
            Assert.That(content.childCount,Is.LessThan(20));
            var raw=widgets.transform.Find("Model").GetComponent<RawImage>();
            Assert.That(raw.texture,Is.Not.Null);
            var modelCamera=GameObject.Find("UIModelStage_").GetComponentInChildren<Camera>();
            Assert.That(modelCamera.enabled,Is.True);
            var scroll=widgets.transform.Find("List").GetComponent<ScrollRect>();
            scroll.verticalNormalizedPosition=0;
            yield return null;yield return null;
            Assert.That(content.childCount,Is.LessThan(20));
            handle.Close();yield return Settled(()=>!handle.IsAlive);
            Assert.That(raw.texture,Is.Null);
        }
        [UnityTest]
        public IEnumerator ShutdownPreservesExternalAssetSystemAndRestoresCamera()
        {
            var owner=UnityEngine.Object.FindFirstObjectByType<GameUIDemoBootstrap>();
            var system=owner.System;var world=UIBoot.Camera.WorldCamera;
            UIBoot.Shutdown();yield return null;system.Tick();
            Assert.That(system.IsDisposed,Is.False);
            Assert.That(world.cullingMask,Is.Not.EqualTo(0));
        }
        [UnityTest]
        public IEnumerator ShutdownFromWaitClosedContinuationDefersPoolDisposal()
        {
            var popup=UI.Open<DemoPopupView>();yield return Settled(()=>popup.Phase==UIPhase.Shown);
            var kernel=UIBoot.Kernel;
            var shutdown=ShutdownWhenClosed(popup);popup.Close();
            yield return WaitFor(()=>shutdown.Status!=UniTaskStatus.Pending && UIBoot.Kernel==null,"Reentrant shutdown did not finish.");
            shutdown.GetAwaiter().GetResult();
            Assert.That(kernel.IsDisposed,Is.True);
            yield return null;
        }
        private static async UniTask ShutdownWhenClosed(UIHandle handle)
        {
            await handle.WaitClosed();
            UIBoot.Shutdown();
        }
        [UnityTest]
        public IEnumerator PreloadDoesNotChangeNavigation()
        {
            var before=UIBoot.Kernel.History.Count;
            var preload=UI.Preload<DemoPopupView>();
            yield return WaitFor(()=>preload.Status!=UniTaskStatus.Pending,"Preload did not finish");
            preload.GetAwaiter().GetResult();
            Assert.That(UI.IsOpen<DemoPopupView>(),Is.False);
            Assert.That(UIBoot.Kernel.History.Count,Is.EqualTo(before));
        }
        private static List<RaycastResult> RaycastAt(Vector2 point)
        {
            Canvas.ForceUpdateCanvases();
            var hits=new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
            return hits;
        }
        private static void Click(string root,string child){GameObject.Find(root).transform.Find(child).GetComponent<Button>().onClick.Invoke();}
        private static bool IsVisible(string name)
        {
            var view=GameObject.Find(name);return view!=null && view.GetComponent<Canvas>()!=null && view.GetComponent<Canvas>().enabled;
        }
        private static IEnumerator Settled(Func<bool> condition)
        {
            yield return WaitFor(()=>condition() && UIBoot.Kernel!=null && !UIBoot.Kernel.Presentation.InputGateLocked,"Navigation did not settle.");
        }
        private static IEnumerator WaitFor(Func<bool> condition,string failure)
        {
            var deadline=Time.realtimeSinceStartup+10;
            while(!condition())
            {
                if(Time.realtimeSinceStartup>=deadline)Assert.Fail(failure+"\n"+(UIBoot.Kernel?.Dump()??"No kernel"));
                yield return null;
            }
        }
    }
}
