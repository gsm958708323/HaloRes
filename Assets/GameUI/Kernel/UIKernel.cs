using System;
using System.Collections.Generic;
using System.Text;

namespace GameUI.Kernel
{
    public sealed class UIKernel
    {
        private readonly UIEntryTable _table = new UIEntryTable();
        private readonly UIFrame _frame = new UIFrame();
        private readonly IUILogSink _log;
        private readonly IUIClock _clock;
        private readonly List<IUIViewInstance> _visibleInstances = new List<IUIViewInstance>();
        private bool _disposed;
        private bool _disposing;
        private UIReconciler _reconciler;

        public UIKernel(IUIViewRegistry registry, IUIViewSource source, IUIPresentationSink sink, IUIClock clock, IUILogSink log = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            _clock = clock;
            _log = log ?? NullLogSink.Instance;
            Input = new UIInputGate();
            History = new UIHistory();
            NavigatorInternal = new UINavigator(this, _table, registry);
            _reconciler = new UIReconciler(this, _table, source, sink, clock, _log);
            Navigator = NavigatorInternal;
        }

        public IUINavigator Navigator { get; }
        internal UINavigator NavigatorInternal { get; }
        public UIInputGate Input { get; }
        public UIHistory History { get; }
        public bool IsDisposed { get { return _disposed; } }
        public UIPresentation Presentation { get { return _reconciler.Frame.Presentation; } }
        public IReadOnlyList<IUIViewInstance> VisibleInDrawOrder
        {
            get { return _visibleInstances; }
        }

        internal IReadOnlyList<UIEntry> VisibleEntries { get { return _reconciler.Frame.VisibleInDrawOrder; } }

        public event Action<UIChange> Changed;
        public event Action BackAtRoot;
        public event Action Disposed;

        public void Tick()
        {
            EnsureAlive();
            _reconciler.Tick();
        }

        internal void Request()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIKernel));
            _reconciler.Request();
        }
        internal void EnsureAlive() { if (_disposed || _disposing) throw new ObjectDisposedException(nameof(UIKernel)); }
        internal void Log(UILogLevel level, string message) { _log.Log(level, message); }
        internal void RaiseBackAtRoot() { BackAtRoot?.Invoke(); }
        internal void CompleteRejected(UIEntry entry, string message)
        {
            _log.Log(UILogLevel.Error, message);
            entry.Act.Reason = UICloseReason.Failed;
            entry.Act.Phase = UIPhase.Closed;
            entry.Opened.TrySetResult(new UIOpenResult(false, UICloseReason.Failed));
            entry.Closed.TrySetResult(UICloseReason.Failed);
            entry.Result.TrySetResult(new UIResultBox(false, null, UICloseReason.Failed));
        }

        internal void Record(UIEntry entry, UIPhase from, UIPhase to, string source)
        {
            var change = new UIChange(_clock.Frame, _clock.Now, entry.Id, entry.Info.Id, entry.Info.Mode, from, to, entry.Want.Intent, entry.Act.Reason, source);
            History.Write(change);
            if (Changed != null)
                foreach (Action<UIChange> observer in Changed.GetInvocationList())
                    try { observer(change); } catch (Exception error) { _log.Log(UILogLevel.Warning, "UI observer failed: " + error); }
        }

        internal void PublishFrame(UIFrame source)
        {
            _frame.Clear();
            foreach (var pair in source.Targets) _frame.Targets[pair.Key] = pair.Value;
            foreach (var pair in source.BaseOrders) _frame.BaseOrders[pair.Key] = pair.Value;
            _frame.VisibleInDrawOrder.AddRange(source.VisibleInDrawOrder);
            _frame.Presentation = source.Presentation;
            _visibleInstances.Clear();
            for (var i = 0; i < _frame.VisibleInDrawOrder.Count; i++)
            {
                var instance = _frame.VisibleInDrawOrder[i].Act.Instance;
                if (instance != null) _visibleInstances.Add(instance);
            }
        }

        internal void FailEntry(UIEntry entry, Exception error) { _reconciler.FailFromCallback(entry, error); Request(); }

        public void ReportViewFailure(IUIViewInstance instance, Exception error)
        {
            if (_disposed || instance == null || error == null) return;
            foreach (var entry in _table.ById.Values)
                if (ReferenceEquals(entry.Act.Instance, instance))
                { _reconciler.FailFromCallback(entry, error); Request(); return; }
        }

        public string Dump()
        {
            var builder = new StringBuilder();
            builder.Append("UIKernel disposed=").Append(_disposed).Append(" presentation.locked=").Append(Presentation.InputLocked).AppendLine();
            for (var i = 0; i < _table.Stack.Count; i++) AppendEntry(builder, _table.Stack[i]);
            for (var i = 0; i < _table.Overlays.Count; i++) AppendEntry(builder, _table.Overlays[i]);
            builder.AppendLine("History:").Append(History.Dump());
            return builder.ToString();
        }

        private void AppendEntry(StringBuilder builder, UIEntry entry)
        {
            UITarget target;
            _reconciler.Frame.Targets.TryGetValue(entry.Id, out target);
            builder.Append('#').Append(entry.Id).Append(' ').Append(entry.Info.Id).Append(' ')
                .Append(entry.Act.Phase).Append(" target=").Append(target).Append(" wanted=").Append(entry.Want.Wanted)
                .Append(" reason=").Append(entry.Act.Reason).Append(" intent=").Append(entry.Want.Intent)
                .Append(" order=").Append(_reconciler.Frame.BaseOrders.TryGetValue(entry.Id, out var order) ? order : 0).AppendLine();
        }

        public void Dispose()
        {
            if (_disposed || _disposing) return;
            _disposing = true;
            try { _reconciler.Shutdown(); }
            catch { CompleteDisposal(); throw; }
        }

        internal void CompleteDisposal()
        {
            if (_disposed) return;
            _disposed = true;
            _disposing = false;
            if (Disposed != null)
                foreach (Action observer in Disposed.GetInvocationList())
                    try { observer(); } catch (Exception error) { _log.Log(UILogLevel.Warning, "UI disposal observer failed: " + error); }
        }

        private sealed class NullLogSink : IUILogSink
        {
            internal static readonly NullLogSink Instance = new NullLogSink();
            public void Log(UILogLevel level, string message) { }
        }
    }
}
