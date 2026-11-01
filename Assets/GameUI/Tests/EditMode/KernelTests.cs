using System;
using System.Collections.Generic;
using GameUI.Kernel;
using NUnit.Framework;

namespace GameUI.Tests
{
    public sealed class KernelTests
    {
        private FakeSource _source;
        private FakeSink _sink;
        private ManualClock _clock;
        private UIKernel _kernel;

        [SetUp]
        public void SetUp()
        {
            _source = new FakeSource();
            _sink = new FakeSink();
            _clock = new ManualClock();
            var registry = new UIViewRegistry(new[]
            {
                Info("A", typeof(ViewA), UIMode.FullScreen, UILayer.Window),
                Info("B", typeof(ViewB), UIMode.Popup, UILayer.Window)
            });
            _kernel = new UIKernel(registry, _source, _sink, _clock, new TestLog());
        }

        [TearDown]
        public void TearDown() { _kernel.Dispose(); }

        [Test]
        public void PushLoadsAndShows()
        {
            var handle = _kernel.Navigator.OpenById("A");
            Assert.That(handle.Phase, Is.EqualTo(UIPhase.Loading));
            _source.Complete("A");
            _kernel.Tick();
            Assert.That(handle.Phase, Is.EqualTo(UIPhase.Shown));
            Assert.That(_source.Instance("A").Calls, Does.Contain("Open"));
            Assert.That(_source.Instance("A").Calls, Does.Contain("Resume"));
        }

        [Test]
        public void ReplaceFailureRestoresPreviousView()
        {
            var first = _kernel.Navigator.OpenById("A");
            _source.Complete("A"); _kernel.Tick();
            var second = _kernel.Navigator.OpenById("B", null, UIOpenPolicy.Replace);
            _source.Fail("B", new InvalidOperationException("broken"));
            _kernel.Tick();
            Assert.That(second.WaitClosed().GetAwaiter().GetResult(), Is.EqualTo(UICloseReason.Failed));
            Assert.That(first.Phase, Is.EqualTo(UIPhase.Shown));
        }

        [Test]
        public void CallbackNavigationIsDeferredUntilNextRound()
        {
            var instance = new CallbackInstance(() => _kernel.Navigator.OpenById("B"));
            _source.Set("A", instance);
            _kernel.Navigator.OpenById("A");
            _source.Complete("A"); _kernel.Tick();
            Assert.That(instance.Calls, Does.Contain("Open"));
            Assert.That(_kernel.Navigator.IsOpen(typeof(ViewB)), Is.True);
        }

        private static UIViewInfo Info(string id, Type viewType, UIMode mode, UILayer layer)
        {
            return new UIViewInfo { Id = id, ViewType = viewType, Mode = mode, Layer = layer, CoversWorld = mode == UIMode.FullScreen, Factory = () => new Logic() };
        }

        private class Logic : IUIViewLogic
        {
            public void BindSession(IUINavigator navigator, int entryId) { }
            public void Open(UIArgs args) { }
            public void Resume() { }
            public void Pause() { }
            public void Close() { }
            public bool Back() { return false; }
            public void ContextChanged(string context) { }
        }

        private sealed class ViewA : Logic { }
        private sealed class ViewB : Logic { }

        private sealed class FakeSource : IUIViewSource
        {
            private readonly Dictionary<string, FakeLoad> _loads = new Dictionary<string, FakeLoad>();
            private readonly Dictionary<string, FakeLoad> _prepared = new Dictionary<string, FakeLoad>();
            private readonly Dictionary<string, FakeInstance> _instances = new Dictionary<string, FakeInstance>();
            public IUILoadOp<IUIViewInstance> Acquire(UIViewInfo info)
            {
                FakeLoad load;
                if (!_prepared.TryGetValue(info.Id, out load)) load = new FakeLoad(info.Id);
                else _prepared.Remove(info.Id);
                _loads[info.Id] = load;
                _instances[info.Id] = load.Instance;
                return load;
            }
            public void Return(IUIViewInstance instance, bool reusable) { ((FakeInstance)instance).Calls.Add(reusable ? "Return" : "Destroy"); }
            public void Complete(string id) { _loads[id].Complete(); }
            public void Fail(string id, Exception error) { _loads[id].Fail(error); }
            public void Set(string id, FakeInstance instance) { _prepared[id] = new FakeLoad(id, instance); _instances[id] = instance; }
            public FakeInstance Instance(string id) { return _instances[id]; }
        }

        private sealed class FakeLoad : IUILoadOp<IUIViewInstance>
        {
            public readonly FakeInstance Instance;
            private bool _done;
            private Exception _error;
            public FakeLoad(string id) { Instance = new FakeInstance(); }
            public FakeLoad(string id, FakeInstance instance) { Instance = instance; }
            public bool IsDone { get { return _done; } }
            public IUIViewInstance Result { get { return Instance; } }
            public Exception Error { get { return _error; } }
            public void Cancel() { _done = true; _error = new OperationCanceledException(); }
            public void Complete() { _done = true; }
            public void Fail(Exception error) { _done = true; _error = error; }
        }

        private class FakeInstance : IUIViewInstance
        {
            public readonly List<string> Calls = new List<string>();
            public virtual IUIViewLogic Logic { get { return new RecordingLogic(Calls); } }
            public bool HandoffRequested { get; set; }
            public bool IsTransitionDone { get { return true; } }
            public void SetBaseOrder(int baseOrder) { Calls.Add("Order"); }
            public void SetVisible(bool on) { Calls.Add(on ? "Visible" : "Hidden"); }
            public void BeginEnter() { Calls.Add("Enter"); }
            public void BeginExit() { Calls.Add("Exit"); }
            public void CompleteTransition() { Calls.Add("Finish"); }
        }

        private sealed class CallbackInstance : FakeInstance
        {
            private readonly Action _callback;
            public CallbackInstance(Action callback) { _callback = callback; }
            public override IUIViewLogic Logic { get { return new CallbackLogic(_callback, Calls); } }
        }

        private sealed class TestLogic : IUIViewLogic
        {
            public void BindSession(IUINavigator navigator, int entryId) { }
            public void Open(UIArgs args) { }
            public void Resume() { }
            public void Pause() { }
            public void Close() { }
            public bool Back() { return false; }
            public void ContextChanged(string context) { }
        }

        private sealed class RecordingLogic : IUIViewLogic
        {
            private readonly List<string> _calls;
            public RecordingLogic(List<string> calls) { _calls = calls; }
            public void BindSession(IUINavigator navigator, int entryId) { _calls.Add("Bind"); }
            public void Open(UIArgs args) { _calls.Add("Open"); }
            public void Resume() { _calls.Add("Resume"); }
            public void Pause() { _calls.Add("Pause"); }
            public void Close() { _calls.Add("Close"); }
            public bool Back() { return false; }
            public void ContextChanged(string context) { _calls.Add("Context"); }
        }

        private sealed class CallbackLogic : IUIViewLogic
        {
            private readonly Action _callback; private readonly List<string> _calls;
            public CallbackLogic(Action callback, List<string> calls) { _callback = callback; _calls = calls; }
            public void BindSession(IUINavigator navigator, int entryId) { }
            public void Open(UIArgs args) { _calls.Add("Open"); _callback(); }
            public void Resume() { _calls.Add("Resume"); }
            public void Pause() { _calls.Add("Pause"); }
            public void Close() { _calls.Add("Close"); }
            public bool Back() { return false; }
            public void ContextChanged(string context) { }
        }

        private sealed class ManualClock : IUIClock { public double Now { get; private set; } public void Advance(double seconds) { Now += seconds; } }
        private sealed class FakeSink : IUIPresentationSink { public UIPresentation Last; public void Apply(in UIPresentation presentation) { Last = presentation; } }
        private sealed class TestLog : IUILogSink { public void Log(UILogLevel level, string message) { } }
    }
}
