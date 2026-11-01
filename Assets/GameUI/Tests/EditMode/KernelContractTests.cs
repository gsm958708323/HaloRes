using System;
using System.Collections.Generic;
using GameUI.Kernel;
using NUnit.Framework;

namespace GameUI.Tests
{
    public sealed class KernelContractTests
    {
        private UIKernel _kernel;
        private Source _source;
        private Clock _clock;
        private Sink _sink;
        private UIViewRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _source=new Source(); _clock=new Clock(); _sink=new Sink();
            _registry=new UIViewRegistry(new[] {
                Info("A",typeof(A),UIMode.FullScreen), Info("B",typeof(B),UIMode.Popup),
                Info("C",typeof(C),UIMode.FullScreen),
                Info("Overlay",typeof(Overlay),UIMode.Overlay,UILayer.Tip),
                Info("System",typeof(SystemView),UIMode.Overlay,UILayer.System),
                Info("Context",typeof(ContextView),UIMode.Overlay,UILayer.Info)
            });
            _registry.Get("A").Context="Home";
            _registry.Get("C").Context="Battle";
            _registry.Get("Context").Visibility=UIOverlayVisibility.WithContext;
            _registry.Get("Context").ContextTag="Home";
            _kernel=new UIKernel(_registry,_source,_sink,_clock);
        }
        [TearDown] public void TearDown() { _kernel.Dispose(); }
        private UIHandle Open(string id,UIOpenPolicy policy=UIOpenPolicy.Push)
        {
            var handle=_kernel.Navigator.OpenById(id,null,policy);
            _source.Complete(id); _kernel.Tick(); return handle;
        }

        [Test] public void WaitOpenedCompletesBeforeClose()
        {
            var handle=Open("A");
            Assert.That(handle.WaitOpened().Status,Is.EqualTo(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded));
            Assert.That(handle.IsAlive,Is.True);
        }
        [Test] public void SingleLoadingRequestsShareSession()
        {
            var first=_kernel.Navigator.OpenById("A");
            Assert.That(_kernel.Navigator.OpenById("A").EntryId,Is.EqualTo(first.EntryId));
            Assert.That(_source.Loads.Count,Is.EqualTo(1));
        }
        [Test] public void CloseLoadingCancelsWithoutOpening()
        {
            var handle=_kernel.Navigator.OpenById("A"); handle.Close();
            Assert.That(_source.Loads["A"].Cancelled,Is.True);
            Assert.That(handle.WaitClosed().GetAwaiter().GetResult(),Is.EqualTo(UICloseReason.Cancelled));
        }
        [Test] public void CloseAfterLoadCompletedReclaimsResult()
        {
            var handle=_kernel.Navigator.OpenById("A");
            _source.Complete("A"); handle.Close();
            Assert.That(_source.Returned,Is.EqualTo(1));
            Assert.That(_source.Loads["A"].Instance.Log.Calls,Does.Not.Contain("Open"));
        }
        [Test] public void ShutdownEndsVisibleSessionAndAllWaiters()
        {
            var handle=Open("A"); var logic=_source.Loads["A"].Instance.Log;
            _kernel.Dispose();
            Assert.That(logic.Calls,Is.EqualTo(new[]{"Open","Resume","Pause","Close"}));
            Assert.That(handle.WaitClosed().Status,Is.EqualTo(Cysharp.Threading.Tasks.UniTaskStatus.Succeeded));
            Assert.That(handle.IsAlive,Is.False);
        }
        [Test] public void ShutdownCancelsPendingLoad()
        {
            var handle=_kernel.Navigator.OpenById("A"); _kernel.Dispose();
            Assert.That(_source.Loads["A"].Cancelled,Is.True);
            Assert.That(handle.Phase,Is.EqualTo(UIPhase.Closed));
        }
        [Test] public void FullscreenCoverAndRestorePairsPauseResume()
        {
            var first=Open("A"); var second=Open("C");
            Assert.That(first.Phase,Is.EqualTo(UIPhase.Hidden)); second.Close();
            Assert.That(first.Phase,Is.EqualTo(UIPhase.Shown));
            Assert.That(_source.Loads["A"].Instance.Log.Calls,Is.EqualTo(new[]{"Open","Resume","Pause","Resume"}));
        }
        [Test] public void PopupRetainsLowerFullscreen()
        {
            var first=Open("A"); Open("B");
            Assert.That(first.Phase,Is.EqualTo(UIPhase.Shown));
            Assert.That(_sink.Last.MaskVisible,Is.True);
        }
        [Test] public void ReplacementWaitsUntilSuccessorTransitionFinishes()
        {
            var first=Open("A");
            _source.NextTransitionDone=false;
            var second=Open("C",UIOpenPolicy.Replace);
            Assert.That(first.IsAlive,Is.True);
            _source.Loads["C"].Instance.Done=true; _kernel.Tick();
            Assert.That(first.IsAlive,Is.False);
            Assert.That(second.Phase,Is.EqualTo(UIPhase.Shown));
        }
        [Test] public void ReplacementFailureRestoresOldSession()
        {
            var first=Open("A"); var second=_kernel.Navigator.OpenById("C",null,UIOpenPolicy.Replace);
            _source.Loads["C"].ErrorValue=new Exception("expected"); _source.Loads["C"].Done=true; _kernel.Tick();
            Assert.That(first.Phase,Is.EqualTo(UIPhase.Shown));
            Assert.That(second.IsAlive,Is.False);
        }
        [Test] public void ExplicitCloseDropsFallbackRelationship()
        {
            var old=Open("A");
            _kernel.Navigator.OpenById("C",null,UIOpenPolicy.Replace);
            old.Close();
            Assert.That(old.IsAlive,Is.False);
        }
        [Test] public void CallbackCloseDoesNotResumeClosedSession()
        {
            _source.NextOnOpen=()=>_kernel.Navigator.Close(_source.CurrentEntry);
            UIHandle handle=default;
            using (_kernel.Navigator.Batch()) { handle=_kernel.Navigator.OpenById("A"); _source.CurrentEntry=handle.EntryId; }
            _source.Complete("A"); _kernel.Tick();
            Assert.That(_source.Loads["A"].Instance.Log.Calls,Is.EqualTo(new[]{"Open","Close"}));
        }
        [Test] public void ResumeFailurePairsCleanupAndDestroys()
        {
            _source.NextThrowResume=true; var handle=Open("A");
            Assert.That(handle.Phase,Is.EqualTo(UIPhase.Closed));
            Assert.That(_source.Destroyed,Is.EqualTo(1));
            Assert.That(_source.Loads["A"].Instance.Log.Calls,Is.EqualTo(new[]{"Open","Resume","Pause","Close"}));
        }
        [Test] public void InputLockBlocksBack()
        {
            Open("A"); var popup=Open("B");
            using (_kernel.Input.Acquire("Movie")) { _kernel.Tick(); Assert.That(_kernel.Navigator.Back(),Is.False); }
            _kernel.Tick(); Assert.That(_kernel.Navigator.Back(),Is.True); Assert.That(popup.IsAlive,Is.False);
        }
        [Test] public void RootBackRaisesEvent()
        {
            Open("A"); var raised=false; _kernel.BackAtRoot+=()=>raised=true;
            _kernel.Navigator.Back(); Assert.That(raised,Is.True);
        }
        [Test] public void LoadingIndicatorUsesClockThreshold()
        {
            _kernel.Navigator.OpenById("A");
            _clock.Now=0.199; _kernel.Tick(); Assert.That(_sink.Last.LoadingIndicator,Is.False);
            _clock.Now=0.201; _kernel.Tick(); Assert.That(_sink.Last.LoadingIndicator,Is.True);
        }
        [Test] public void SystemOverlayHasSeparateHardInputLock()
        {
            Open("A"); Open("System");
            Assert.That(_sink.Last.InputLocked,Is.True);
            Assert.That(_sink.Last.InputGateLocked,Is.False);
        }
        [Test] public void ContextOverlayReceivesContextChanges()
        {
            Open("A"); Open("Context"); Open("C");
            Assert.That(_source.Loads["Context"].Instance.Log.Contexts,Is.EqualTo(new[]{"Home","Battle"}));
        }
        [Test] public void HistoryCarriesFrameAndTime()
        {
            _clock.Now=12.5; _clock.Frame=123; Open("A");
            var history=_kernel.History.Snapshot();
            Assert.That(history[0].Time,Is.EqualTo(12.5));
            Assert.That(history[0].Frame,Is.EqualTo(123));
        }
        [Test] public void WrongArgsDoNotMutateExistingNavigation()
        {
            var first=Open("A");
            var invalid=_kernel.Navigator.OpenById("C",new Args(),UIOpenPolicy.Reset);
            Assert.That(invalid.Phase,Is.EqualTo(UIPhase.Closed));
            Assert.That(first.IsAlive,Is.True);
        }
        [Test] public void StaleHandleCannotCloseReopenedSession()
        {
            var first=Open("A"); first.Close(); var second=Open("A"); first.Close();
            Assert.That(second.IsAlive,Is.True);
        }
        [Test] public void VisibleBufferIsReused()
        {
            Open("A"); var first=_kernel.VisibleInDrawOrder; _kernel.Tick();
            Assert.That(_kernel.VisibleInDrawOrder,Is.SameAs(first));
        }
        [Test] public void IdleKernelTickAllocatesNoManagedMemoryAfterWarmup()
        {
            Open("A");for(var i=0;i<20;i++)_kernel.Tick();
            var before=GC.GetAllocatedBytesForCurrentThread();
            for(var i=0;i<100;i++)_kernel.Tick();
            var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Assert.That(allocated,Is.Zero);
        }
        [Test] public void BoundedRandomNavigationAlwaysTerminates()
        {
            var random=new Random(54); var handles=new List<UIHandle>();
            for(var i=0;i<200;i++)
            {
                if(handles.Count>0 && random.Next(3)==0) handles[random.Next(handles.Count)].Close();
                else handles.Add(Open(random.Next(2)==0?"A":"B",(UIOpenPolicy)random.Next(3)));
                _kernel.Tick();
            }
            _kernel.Dispose(); foreach(var handle in handles) Assert.That(handle.IsAlive,Is.False);
        }

        private static UIViewInfo Info(string id,Type type,UIMode mode,UILayer layer=UILayer.Window)
        { return new UIViewInfo {Id=id,ViewType=type,Mode=mode,Layer=layer,CoversWorld=mode==UIMode.FullScreen,Factory=()=>new Logic(),BackBehavior=UIBackBehavior.Close}; }
        private sealed class A { } private sealed class B { } private sealed class C { }
        private sealed class Overlay { } private sealed class ContextView { } private sealed class SystemView { }
        private sealed class Args : UIArgs { }
        private sealed class Clock : IUIClock { public double Now {get;set;} public int Frame {get;set;} }
        private sealed class Sink : IUIPresentationSink { public UIPresentation Last; public void Apply(in UIPresentation p) { Last=p; } }
        private sealed class Source : IUIViewSource
        {
            internal readonly Dictionary<string,Load> Loads=new Dictionary<string,Load>();
            internal bool NextTransitionDone=true,NextThrowResume;
            internal Action NextOnOpen;
            internal int Returned,Destroyed,CurrentEntry;
            public IUILoadOp<IUIViewInstance> Acquire(UIViewInfo info)
            {
                var load=new Load {Instance=new Instance {Done=NextTransitionDone, Log=new Logic {OnOpen=NextOnOpen,ThrowResume=NextThrowResume}}};
                NextOnOpen=null;NextThrowResume=false;NextTransitionDone=true;Loads[info.Id]=load;return load;
            }
            internal void Complete(string id) { Loads[id].Done=true; }
            public void Return(IUIViewInstance instance,bool reusable) { if(reusable)Returned++;else Destroyed++; }
        }
        private sealed class Load : IUILoadOp<IUIViewInstance>
        {
            internal Instance Instance; internal bool Done,Cancelled;internal Exception ErrorValue;
            public bool IsDone=>Done;public IUIViewInstance Result=>Instance;public Exception Error=>ErrorValue;
            public void Cancel() { Cancelled=true;Done=true; }
        }
        private sealed class Instance : IUIViewInstance
        {
            internal Logic Log; internal bool Done;
            public IUIViewLogic Logic=>Log; public bool HandoffRequested=>false;public bool IsTransitionDone=>Done;
            public void SetBaseOrder(int order){} public void SetVisible(bool value){} public void BeginEnter(){}
            public void BeginExit(){} public void CompleteTransition(){}
        }
        private sealed class Logic : IUIViewLogic
        {
            internal readonly List<string> Calls=new List<string>();internal readonly List<string> Contexts=new List<string>();
            internal Action OnOpen;internal bool ThrowResume;
            public void BindSession(IUINavigator nav,int id){}
            public void Open(UIArgs args){Calls.Add("Open");OnOpen?.Invoke();}
            public void Resume(){Calls.Add("Resume");if(ThrowResume)throw new Exception("expected");}
            public void Pause(){Calls.Add("Pause");} public void Close(){Calls.Add("Close");}
            public bool Back()=>false;public void ContextChanged(string ctx){Contexts.Add(ctx);}
        }
    }
}
