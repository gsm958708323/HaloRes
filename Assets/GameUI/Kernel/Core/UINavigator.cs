using System;
using System.Threading;

namespace GameUI.Kernel
{
    public interface IUINavigator
    {
        UIHandle Open(Type viewType, UIArgs args = null, UIOpenPolicy policy = UIOpenPolicy.Push);
        UIHandle OpenById(string id, UIArgs args = null, UIOpenPolicy policy = UIOpenPolicy.Push);
        void Close(int entryId);
        void Finish(int entryId, object result);
        bool Back();
        bool IsOpen(Type viewType);
        IDisposable Batch();
    }

    internal sealed class UINavigator : IUINavigator
    {
        private static int _nextId;
        private readonly UIKernel _kernel;
        private readonly UIEntryTable _table;
        private readonly IUIViewRegistry _registry;
        private int _batchDepth;

        internal UINavigator(UIKernel kernel, UIEntryTable table, IUIViewRegistry registry)
        {
            _kernel = kernel;
            _table = table;
            _registry = registry;
        }

        public UIHandle Open(Type viewType, UIArgs args = null, UIOpenPolicy policy = UIOpenPolicy.Push)
        {
            return OpenInfo(_registry.Get(viewType), args, policy);
        }

        public UIHandle OpenById(string id, UIArgs args = null, UIOpenPolicy policy = UIOpenPolicy.Push)
        {
            return OpenInfo(_registry.Get(id), args, policy);
        }

        private UIHandle OpenInfo(UIViewInfo info, UIArgs args, UIOpenPolicy policy)
        {
            _kernel.EnsureAlive();
            info.Validate();
            if (args != null && (info.ArgsType == null || !info.ArgsType.IsInstanceOfType(args)))
            {
                var rejected = new UIEntry(Interlocked.Increment(ref _nextId), info, args, "InvalidArgs");
                _kernel.CompleteRejected(rejected, "Argument type mismatch for " + info.Id);
                return new UIHandle(_kernel, rejected);
            }
            var list = info.IsOverlay ? _table.Overlays : _table.Stack;
            UIEntry existing = null;
            if (info.IsSingle)
                for (var i = list.Count - 1; i >= 0; i--)
                    if (list[i].Info.ViewType == info.ViewType && list[i].Want.Wanted && list[i].Act.Phase < UIPhase.Exiting)
                    { existing = list[i]; break; }

            if (existing != null && policy != UIOpenPolicy.Reset)
            {
                if (!info.IsOverlay)
                {
                    var index = list.IndexOf(existing);
                    for (var i = index + 1; i < list.Count; i++) SetAbsent(list[i], UIExitIntent.Normal, 0, "PopTo");
                }
                if (args == null)
                {
                    Request();
                    return new UIHandle(_kernel, existing);
                }
            }

            var id = Interlocked.Increment(ref _nextId);
            if (id <= 0) throw new InvalidOperationException("UI session id exhausted.");
            var entry = new UIEntry(id, info, args, policy.ToString());
            if (info.ArgsType != null && args != null && !info.ArgsType.IsInstanceOfType(args))
            {
                _kernel.CompleteRejected(entry, "Argument type mismatch: expected " + info.ArgsType.FullName + ", received " + args.GetType().FullName);
                return new UIHandle(_kernel, entry);
            }

            if (existing != null && policy != UIOpenPolicy.Reset)
                SetAbsent(existing, UIExitIntent.Replaced, id, "ReplaceArgs");
            else if (!info.IsOverlay && policy == UIOpenPolicy.Replace)
            {
                var top = TopWanted();
                if (top != null) SetAbsent(top, UIExitIntent.Replaced, id, "Replace");
            }
            else if (!info.IsOverlay && policy == UIOpenPolicy.Reset)
                for (var i = 0; i < list.Count; i++)
                    if (list[i].Act.Phase < UIPhase.Exiting) SetAbsent(list[i], UIExitIntent.Reset, id, "Reset");

            list.Add(entry);
            _table.ById.Add(id, entry);
            _kernel.Record(entry, UIPhase.Pending, UIPhase.Pending, "Open");
            Request();
            return new UIHandle(_kernel, entry);
        }

        public void Close(int entryId)
        {
            if (!_table.ById.TryGetValue(entryId, out var entry))
            { _kernel.Log(UILogLevel.Warning, "Ignoring closed UI handle " + entryId); return; }
            SetAbsent(entry, UIExitIntent.Normal, 0, "Close");
            Request();
        }

        public void Finish(int entryId, object result)
        {
            if (!_table.ById.TryGetValue(entryId, out var entry)) return;
            if (entry.Info.ResultType == null || (result == null ? entry.Info.ResultType.IsValueType : !entry.Info.ResultType.IsInstanceOfType(result)))
                throw new ArgumentException("Invalid UI result for " + entry.Info.Id, nameof(result));
            entry.Want.Result = result;
            entry.Want.HasResult = true;
            SetAbsent(entry, UIExitIntent.Normal, 0, "Finish");
            Request();
        }

        internal void SetAbsent(UIEntry entry, UIExitIntent intent, int successor, string source)
        {
            if (entry.Act.Phase >= UIPhase.Exiting) return;
            entry.Want.Wanted = false;
            entry.Want.ReplacedBy = intent == UIExitIntent.Normal ? 0 : successor;
            entry.Want.Intent = intent;
            entry.Want.Source = source;
            _kernel.Record(entry, entry.Act.Phase, entry.Act.Phase, source);
        }

        internal void RestoreFromBrokenChain(UIEntry entry)
        {
            if (entry.Act.Phase >= UIPhase.Exiting) return;
            entry.Want.Wanted = true;
            entry.Want.ReplacedBy = 0;
            entry.Want.Intent = UIExitIntent.Normal;
            entry.Want.Source = "Restore";
            _kernel.Record(entry, entry.Act.Phase, entry.Act.Phase, "Restore");
        }

        internal void CloseAllForShutdown()
        {
            for (var i = _table.Stack.Count - 1; i >= 0; i--)
                if (_table.Stack[i].Act.Phase < UIPhase.Exiting) SetAbsent(_table.Stack[i], UIExitIntent.Normal, 0, "Shutdown");
            for (var i = _table.Overlays.Count - 1; i >= 0; i--)
                if (_table.Overlays[i].Act.Phase < UIPhase.Exiting) SetAbsent(_table.Overlays[i], UIExitIntent.Normal, 0, "Shutdown");
        }

        public bool Back()
        {
            _kernel.EnsureAlive();
            if (_kernel.Presentation.InputLocked || _kernel.Presentation.SystemOverlayVisible) return false;
            var visible = _kernel.VisibleEntries;
            for (var i = visible.Count - 1; i >= 0; i--)
            {
                var entry = visible[i];
                if (!entry.Info.IsOverlay || !entry.Want.Wanted) continue;
                if (entry.Info.BackBehavior == UIBackBehavior.Consume) return true;
                if (entry.Info.BackBehavior == UIBackBehavior.Close) { Close(entry.Id); return true; }
            }

            var top = TopWanted();
            if (top == null) return false;
            try { if (top.Act.Instance != null && top.Act.Instance.Logic.Back()) return true; }
            catch (Exception error) { _kernel.FailEntry(top, error); return true; }
            var count = 0;
            for (var i = 0; i < _table.Stack.Count; i++) if (_table.Stack[i].Want.Wanted) count++;
            if (count <= 1) { _kernel.RaiseBackAtRoot(); return true; }
            Close(top.Id);
            return true;
        }

        public bool IsOpen(Type viewType)
        {
            foreach (var entry in _table.ById.Values)
                if (entry.Info.ViewType == viewType && entry.Want.Wanted && entry.Act.Phase < UIPhase.Exiting) return true;
            return false;
        }

        internal UIEntry TopWanted()
        {
            for (var i = _table.Stack.Count - 1; i >= 0; i--)
                if (_table.Stack[i].Want.Wanted && _table.Stack[i].Act.Phase < UIPhase.Exiting) return _table.Stack[i];
            return null;
        }

        public IDisposable Batch() { _batchDepth++; return new BatchLease(this); }
        private void Request() { if (_batchDepth == 0) _kernel.Request(); }
        private void EndBatch() { if (--_batchDepth == 0) _kernel.Request(); }

        private sealed class BatchLease : IDisposable
        {
            private UINavigator _owner;
            internal BatchLease(UINavigator owner) { _owner = owner; }
            public void Dispose() { var owner = _owner; _owner = null; owner?.EndBatch(); }
        }
    }
}
