using System;
using System.Collections.Generic;
using GameUI.Components;
using GameUI.Kernel;
using GameUI.Runtime;
using GameUI.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI.Tests
{
    public sealed class RuntimeContractTests
    {
        private readonly List<GameObject> _objects=new List<GameObject>();
        [TearDown] public void Cleanup()
        {
            UIBoot.Shutdown();
            foreach(var go in _objects) if(go != null) UnityEngine.Object.DestroyImmediate(go);
            _objects.Clear(); UIModelStage.ClearPool(); UIWorldHud.ClearPool();
        }
        private GameObject Object(string name,params Type[] types)
        {
            var go=new GameObject(name,types);_objects.Add(go);return go;
        }
        private GameObject Prefab()
        {
            var go=Object("ViewPrefab",typeof(RectTransform),typeof(Canvas),typeof(CanvasGroup),typeof(GraphicRaycaster),typeof(UIBindingData));
            go.SetActive(false); return go;
        }
        private UIViewInfo Info(string id="A",bool keepAlive=false)
        { return new UIViewInfo{Id=id,ViewType=typeof(View),Mode=UIMode.FullScreen,Layer=UILayer.Window,PrefabPath=id,KeepAlive=keepAlive,Factory=()=>new View()}; }

        [Test] public void ShutdownNodeClosesAllScopesAndChildren()
        {
            var view=new View();view.Initialize(Object("View",typeof(RectTransform)));
            view.HostOpen();view.HostResume();view.HostDestroy();
            Assert.That(view.Calls,Is.EqualTo(new[]{"Open","Resume","Pause","Close","Destroy"}));
            Assert.That(view.SessionDisposed,Is.True);Assert.That(view.ResumeDisposed,Is.True);
        }
        [Test] public void PanelCannotResumeWhileParentHidden()
        {
            var view=new View();view.Initialize(Object("View",typeof(RectTransform)));
            var panel=view.Panel(Object("Panel",typeof(RectTransform),typeof(UIBindingData)).GetComponent<UIBindingData>());
            view.HostOpen();view.HostResume();panel.Hide();view.HostPause();panel.Show();
            Assert.That(panel.IsEffectivelyVisible,Is.False);
            view.HostResume();Assert.That(panel.IsEffectivelyVisible,Is.True);
            view.HostDestroy();
        }
        [Test] public void ScopeCancellationFailureDoesNotSkipOtherReleases()
        {
            var scope=new UIScope();var released=false;
            scope.Add(()=>released=true);
            scope.Token.Register(()=>throw new Exception("cancel callback"));
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception,new System.Text.RegularExpressions.Regex("cancel callback"));
            scope.Dispose();Assert.That(released,Is.True);
        }
        [Test] public void MessagesQueueNestedCrossTypePublication()
        {
            var calls=new List<string>();var scope=new UIScope();
            UIMessageHub.Subscribe<MessageA>(a=>{calls.Add("A1");UIMessageHub.Publish(new MessageB());calls.Add("A2");},scope);
            UIMessageHub.Subscribe<MessageB>(b=>calls.Add("B"),scope);
            UIMessageHub.Publish(new MessageA());scope.Dispose();
            Assert.That(calls,Is.EqualTo(new[]{"A1","A2","B"}));
        }
        [Test] public void UnsubscribeDuringDispatchSkipsRemovedSubscriber()
        {
            var calls=0;var first=new UIScope();var second=new UIScope();
            UIMessageHub.Subscribe<MessageA>(a=>second.Dispose(),first);
            UIMessageHub.Subscribe<MessageA>(a=>calls++,second);
            UIMessageHub.Publish(new MessageA());first.Dispose();
            Assert.That(calls,Is.Zero);
        }
        [Test] public void ItemListReusesInstancesAndDestroysShrunkItems()
        {
            var host=new View();host.Initialize(Object("Host",typeof(RectTransform)));
            var template=Object("Template",typeof(RectTransform),typeof(UIBindingData)).GetComponent<UIBindingData>();
            var created=0;
            var list=new UIItemList<Item,int>(host,template,()=>{created++;return new Item();});
            list.SetItems(new[]{1,2,3});host.HostOpen();host.HostResume();
            list.SetItems(new[]{7});list.SetItems(new[]{8,9});
            Assert.That(created,Is.EqualTo(3));host.HostDestroy();
        }
        [Test] public void LoopListUsesViewportPoolAndOnlyEmitsRealEntry()
        {
            var host=new View();host.Initialize(Object("Host",typeof(RectTransform)));
            var scroll=Object("Scroll",typeof(RectTransform),typeof(ScrollRect)).GetComponent<ScrollRect>();
            var viewport=Object("Viewport",typeof(RectTransform)).GetComponent<RectTransform>();
            viewport.sizeDelta=new Vector2(200,200);scroll.viewport=viewport;
            var content=Object("Content",typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport,false);scroll.content=content;scroll.horizontal=false;scroll.vertical=true;
            var template=Object("Template",typeof(RectTransform),typeof(UIBindingData)).GetComponent<UIBindingData>();
            template.transform.SetParent(content,false);((RectTransform)template.transform).sizeDelta=new Vector2(200,50);
            var created=0;var entered=new List<int>();
            var list=new UILoopList<Item,int>(host,scroll,template,()=>{created++;return new Item();});
            list.ItemEnteredView+=entered.Add;
            var data=new int[100];for(var i=0;i<data.Length;i++)data[i]=i;
            list.SetItems(data);host.HostOpen();host.HostResume();
            Assert.That(created,Is.LessThanOrEqualTo(6));
            Assert.That(entered,Is.EqualTo(new[]{0,1,2,3}));
            entered.Clear();list.RefreshAt(1);Assert.That(entered,Is.Empty);
            list.ScrollTo(30);Assert.That(created,Is.LessThanOrEqualTo(6));Assert.That(entered,Does.Contain(30));
            host.HostDestroy();
        }
        [Test] public void ItemBindingVersionRejectsLateInteraction()
        {
            var item=new Item();item.Initialize(Object("Item",typeof(RectTransform)));
            var host=new ItemHost();item.BindHost(host);item.Bind(0,1);var version=item.BindingVersion;
            item.Bind(1,2);item.Notify(0,version);Assert.That(host.Calls,Is.Zero);
            item.Notify(0,item.BindingVersion);Assert.That(host.Calls,Is.EqualTo(1));item.Unbind();item.DestroyElement();
        }
        [Test] public void VirtualListScrollReusesSlotsWithoutManagedAllocation()
        {
            var host=new View();host.Initialize(Object("Host",typeof(RectTransform)));
            var scroll=Object("Scroll",typeof(RectTransform),typeof(ScrollRect)).GetComponent<ScrollRect>();
            var viewport=Object("Viewport",typeof(RectTransform)).GetComponent<RectTransform>();
            viewport.sizeDelta=new Vector2(200,200);scroll.viewport=viewport;
            var content=Object("Content",typeof(RectTransform)).GetComponent<RectTransform>();content.SetParent(viewport,false);
            scroll.content=content;scroll.horizontal=false;scroll.vertical=true;
            var template=Object("Template",typeof(RectTransform),typeof(UIBindingData)).GetComponent<UIBindingData>();
            template.transform.SetParent(content,false);((RectTransform)template.transform).sizeDelta=new Vector2(200,50);
            var list=new UILoopList<Item,int>(host,scroll,template,()=>new Item());
            list.SetItems(new int[100]);host.HostOpen();host.HostResume();
            for(var i=0;i<30;i++)list.ScrollTo(i%20+1);
            var before=GC.GetAllocatedBytesForCurrentThread();
            for(var i=0;i<100;i++)list.ScrollTo(i%20+1);
            var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            host.HostDestroy();Assert.That(allocated,Is.Zero);
        }
        [Test] public void PoolPreloadCreatesWithoutOpeningAndReuses()
        {
            var service=new AssetService(Prefab());
            using(var pool=new UIViewPool(service,info=>null))
            {
                var info=Info("A",true);pool.Preload(info).GetAwaiter().GetResult();
                Assert.That(View.OpenCount,Is.Zero);
                var instance=pool.Acquire(info).Result;
                Assert.That(service.Loads,Is.EqualTo(1));pool.Return(instance,true);
                Assert.That(pool.Acquire(info).Result,Is.SameAs(instance));
            }
            Assert.That(service.Releases,Is.EqualTo(1));
        }
        [Test] public void PoolEvictsLeastRecentlyUsedNonResidentInstance()
        {
            var service=new AssetService(Prefab());
            using(var pool=new UIViewPool(service,info=>null,1))
            {
                var a=pool.Acquire(Info("A")).Result;pool.Return(a,true);
                var b=pool.Acquire(Info("B")).Result;pool.Return(b,true);
                Assert.That(service.Releases,Is.EqualTo(1));
                Assert.That(pool.Acquire(Info("B")).Result,Is.SameAs(b));
            }
        }
        [Test] public void PoolCleansInstanceWhenOnCreateFails()
        {
            var prefab=Prefab();var service=new AssetService(prefab);
            using(var pool=new UIViewPool(service,info=>null))
            {
                var info=Info();info.Factory=()=>new BrokenView();
                var load=pool.Acquire(info);
                Assert.That(load.Error,Is.Not.Null);Assert.That(service.Releases,Is.EqualTo(1));
                foreach(var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                    Assert.That(go.name,Is.Not.EqualTo("ViewPrefab[UI]"));
            }
        }
        [Test] public void HandoffReusesLogicWithoutRepeatingOnCreate()
        {
            var service=new AssetService(Prefab());
            using(var pool=new UIViewPool(service,info=>null))
            {
                var info=Info();var first=(UGUIViewInstance)pool.Acquire(info).Result;
                var logic=first.Logic;first.BeginExit();
                var next=pool.Acquire(info);Assert.That(next.IsDone,Is.False);Assert.That(first.HandoffRequested,Is.True);
                pool.Return(first,true);Assert.That(next.Result,Is.SameAs(first));Assert.That(first.Logic,Is.SameAs(logic));
            }
        }
        [Test] public void HiddenViewKeepsObjectActiveAndDisablesRendering()
        {
            var service=new AssetService(Prefab());
            using(var pool=new UIViewPool(service,info=>null))
            {
                var view=(UGUIViewInstance)pool.Acquire(Info()).Result;view.SetVisible(true);view.SetVisible(false);
                Assert.That(view.GameObject.activeSelf,Is.True);
                Assert.That(view.GameObject.GetComponent<Canvas>().enabled,Is.False);
            }
        }
        [Test] public void HiddenViewDisablesNestedRaycastersAndRestoresAuthoredVisibility()
        {
            var prefab=Prefab();
            prefab.GetComponent<CanvasGroup>().alpha=0.6f;
            var enabledChild=Object("Enabled",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));enabledChild.transform.SetParent(prefab.transform,false);
            var disabledChild=Object("Disabled",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));disabledChild.transform.SetParent(prefab.transform,false);
            disabledChild.GetComponent<Canvas>().enabled=false;disabledChild.GetComponent<GraphicRaycaster>().enabled=false;
            using(var pool=new UIViewPool(new AssetService(prefab),info=>null))
            {
                var view=(UGUIViewInstance)pool.Acquire(Info()).Result;
                foreach(var caster in view.GameObject.GetComponentsInChildren<GraphicRaycaster>(true))Assert.That(caster.enabled,Is.False);
                view.SetVisible(true);
                Assert.That(view.GameObject.transform.Find("Enabled").GetComponent<GraphicRaycaster>().enabled,Is.True);
                Assert.That(view.GameObject.transform.Find("Disabled").GetComponent<Canvas>().enabled,Is.False);
                Assert.That(view.GameObject.transform.Find("Disabled").GetComponent<GraphicRaycaster>().enabled,Is.False);
                Assert.That(view.GameObject.GetComponent<CanvasGroup>().alpha,Is.EqualTo(0.6f));
                view.SetVisible(false);view.SetVisible(false);view.SetVisible(true);
                Assert.That(view.GameObject.transform.Find("Enabled").GetComponent<Canvas>().enabled,Is.True);
            }
        }
        [Test] public void LetterboxKeepsDesignAspectRatio()
        {
            var content=Object("Content",typeof(RectTransform)).GetComponent<RectTransform>();
            var scaler=Object("Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)).GetComponent<CanvasScaler>();
            var fitter=new UIScreenFitter(content,scaler,new Vector2(1920,1080),UIScreenFitMode.LetterboxAuto);
            fitter.Apply(new Rect(0,0,2400,1080),new Vector2Int(2400,1080),null);
            Assert.That(fitter.ContentScreenRect,Is.EqualTo(new Rect(240,0,1920,1080)));
        }
        [Test] public void ModelStageBindsAndReturnsRenderTarget()
        {
            var component=Object("ModelView",typeof(RectTransform),typeof(RawImage),typeof(UIModelView)).GetComponent<UIModelView>();
            component.target=component.GetComponent<RawImage>();component.modelPrefab=Object("Model");
            var stage=new UIModelStage(component);
            Assert.That(component.target.texture,Is.SameAs(stage.Target));
            stage.SetVisible(true);Assert.That(stage.Camera.enabled,Is.True);
            stage.Dispose();Assert.That(component.target.texture,Is.Null);
        }
        [Test] public void DragCancellationRestoresSource()
        {
            var source=Object("Source",typeof(RectTransform),typeof(CanvasGroup)).GetComponent<RectTransform>();
            var coordinator=new UIDragCoordinator();var scope=new UIScope();
            coordinator.Begin(scope,source,Object("DragRoot",typeof(RectTransform)).transform,Vector2.zero);
            Assert.That(source.GetComponent<CanvasGroup>().alpha,Is.Zero);
            coordinator.CancelAll();Assert.That(source.GetComponent<CanvasGroup>().alpha,Is.EqualTo(1));scope.Dispose();
        }
        [Test] public void BindingHashChangesWithComponentIdentity()
        {
            var root=Object("Binding",typeof(RectTransform),typeof(UIBindingData));
            var data=root.GetComponent<UIBindingData>();
            data.entries.Add(new UIBindingEntry{name="rect",component=root.transform});
            var first=UIBindingEmitter.ComputeLayoutHash(data);
            data.entries[0].name="other";
            Assert.That(UIBindingEmitter.ComputeLayoutHash(data),Is.Not.EqualTo(first));
        }

        [Test] public void ItemFailuresDoNotSkipOtherItemsOrNativeDestruction()
        {
            var host=new View();host.Initialize(Object("Host",typeof(RectTransform)));
            var template=Object("Template",typeof(RectTransform),typeof(UIBindingData)).GetComponent<UIBindingData>();
            var items=new List<FailingItem>();
            var list=new UIItemList<FailingItem,int>(host,template,()=>{var item=new FailingItem();items.Add(item);return item;});
            list.SetItems(new[]{1,2,3});host.HostOpen();host.HostResume();
            var natives=new List<GameObject>();foreach(var item in items)natives.Add(item.GameObject);
            Assert.Throws<InvalidOperationException>(()=>host.HostClose());
            foreach(var item in items)Assert.That(item.Released,Is.True);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception,new System.Text.RegularExpressions.Regex("expected item destroy"));
            host.HostDestroy();
            foreach(var item in items)Assert.That(item.Destroyed,Is.True);
            foreach(var native in natives)Assert.That(native==null,Is.True);
        }

        [Test] public void CameraRegistrationPreservesWorldCoverageAndRestoresBothCameras()
        {
            var root=Object("Root",typeof(RectTransform),typeof(UIRoot)).GetComponent<UIRoot>();
            var first=Object("First",typeof(Camera)).GetComponent<Camera>();
            var second=Object("Second",typeof(Camera)).GetComponent<Camera>();
            var firstMask=first.cullingMask;var secondMask=second.cullingMask;
            using(var rig=new UICameraRig(root))
            {
                rig.Register(first);rig.SetWorldCovered(true);rig.Register(second);
                Assert.That(first.cullingMask,Is.EqualTo(firstMask));Assert.That(second.cullingMask,Is.Zero);
                UnityEngine.Object.DestroyImmediate(second.gameObject);rig.Tick();
                Assert.That(rig.UICamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderType,
                    Is.EqualTo(UnityEngine.Rendering.Universal.CameraRenderType.Base));
            }
            Assert.That(first.cullingMask,Is.EqualTo(firstMask));Assert.That(secondMask,Is.Not.Zero);
        }

        [Test] public void BindingLayoutIgnoresPrefabInstanceRootName()
        {
            var root=Object("Original",typeof(RectTransform),typeof(UIBindingData));var data=root.GetComponent<UIBindingData>();
            data.entries.Add(new UIBindingEntry{name="rect",component=root.transform});
            var before=UIBindingEmitter.ComputeLayoutHash(data);root.name="Renamed";
            Assert.That(UIBindingEmitter.ComputeLayoutHash(data),Is.EqualTo(before));
        }

        private struct MessageA { } private struct MessageB { }
        private sealed class ItemHost : IUIItemHost { internal int Calls;public void OnItemEvent(int index,int code,int version){Calls++;} }
        private sealed class Item : UIItem<int> { protected override void OnBind(int data){} internal void Notify(int code,int version){NotifyHost(code,version);} }
        private sealed class FailingItem : UIItem<int>
        {
            internal bool Released,Destroyed;private bool _fail;
            protected override void OnBind(int data){_fail=data==1;BindScope.Add(()=>Released=true);}
            protected override void OnUnbind(){if(_fail)throw new InvalidOperationException("expected item unbind");}
            protected override void OnDestroy(){Destroyed=true;if(_fail)throw new InvalidOperationException("expected item destroy");}
        }
        private sealed class Panel : UIPanel {}
        private sealed class View : UIView
        {
            internal static int OpenCount;
            internal readonly List<string> Calls=new List<string>();internal bool SessionDisposed,ResumeDisposed;
            internal View() {OpenCount=0;}
            internal Panel Panel(UIBindingData data){return AddPanel(new Panel(),data);}
            protected override void OnOpen(){Calls.Add("Open");OpenCount++;SessionScope.Add(()=>SessionDisposed=true);}
            protected override void OnResume(){Calls.Add("Resume");ResumeScope.Add(()=>ResumeDisposed=true);}
            protected override void OnPause(){Calls.Add("Pause");}
            protected override void OnClose(){Calls.Add("Close");}
            protected override void OnDestroy(){Calls.Add("Destroy");}
        }
        private sealed class BrokenView : UIView { protected override void OnCreate(){throw new Exception("expected");} }
        private sealed class AssetService : IUIAssetService
        {
            private readonly GameObject _prefab;internal int Loads,Releases;
            internal AssetService(GameObject prefab){_prefab=prefab;}
            public IUILoadOp<IUIAssetHandle> LoadPrefab(string path){Loads++;return new Load(new Handle(_prefab,()=>Releases++));}
        }
        private sealed class Handle : IUIAssetHandle
        {
            private Action _release;internal Handle(GameObject prefab,Action release){Asset=prefab;_release=release;}
            public UnityEngine.Object Asset{get;private set;}
            public void Release(){var release=_release;_release=null;Asset=null;release?.Invoke();}
        }
        private sealed class Load : IUILoadOp<IUIAssetHandle>
        {
            internal Load(IUIAssetHandle handle){Result=handle;}
            public bool IsDone=>true;public IUIAssetHandle Result{get;}public Exception Error=>null;public void Cancel(){}
        }
    }
}
