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
        private bool _disposed;
        private UIReconciler _reconciler;

        public UIKernel(IUIViewRegistry registry, IUIViewSource source, IUIPresentationSink sink, IUIClock clock, IUILogSink log = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
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
            get
            {
                var list = new List<IUIViewInstance>();
                var entries = _reconciler.Frame.VisibleInDrawOrder;
                for (var i = 0; i < entries.Count; i++) if (entries[i].Act.Instance != null) list.Add(entries[i].Act.Instance);
                return list;
            }
        }

        internal IReadOnlyList<UIEntry> VisibleEntries { get { return _reconciler.Frame.VisibleInDrawOrder; } }

        public event Action<UIChange> Changed;
        public event Action BackAtRoot;

        public void Tick()
        {
            EnsureAlive();
            _reconciler.Tick();
        }

        internal void Request() { EnsureAlive(); _reconciler.Request(); }
        internal void EnsureAlive() { if (_disposed) throw new ObjectDisposedException(nameof(UIKernel)); }
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
            var change = new UIChange(History.Count + 1, 0, entry.Id, entry.Info.Id, entry.Info.Mode, from, to, entry.Want.Intent, entry.Act.Reason, source);
            History.Write(change);
            Changed?.Invoke(change);
        }

        internal void PublishFrame(UIFrame source)
        {
            _frame.Clear();
            foreach (var pair in source.Targets) _frame.Targets[pair.Key] = pair.Value;
            foreach (var pair in source.BaseOrders) _frame.BaseOrders[pair.Key] = pair.Value;
            _frame.VisibleInDrawOrder.AddRange(source.VisibleInDrawOrder);
            _frame.Presentation = source.Presentation;
        }

        internal void FailEntry(UIEntry entry, Exception error) { _log.Log(UILogLevel.Error, error.ToString()); }

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
                .Append(entry.Act.Phase).Append(" target=").Append(target).Append(" wanted=").Append(entry.Want.Wanted).AppendLine();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (var i = _table.Stack.Count - 1; i >= 0; i--) _table.Stack[i].Want.Wanted = false;
            for (var i = _table.Overlays.Count - 1; i >= 0; i--) _table.Overlays[i].Want.Wanted = false;
        }

        private sealed class NullLogSink : IUILogSink
        {
            internal static readonly NullLogSink Instance = new NullLogSink();
            public void Log(UILogLevel level, string message) { }
        }
    }
}
