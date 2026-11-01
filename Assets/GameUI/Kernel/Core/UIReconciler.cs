using System;
using System.Collections.Generic;

namespace GameUI.Kernel
{
    internal sealed class UIReconciler
    {
        private readonly UIKernel _kernel;
        private readonly UIEntryTable _table;
        private readonly IUIViewSource _source;
        private readonly IUIPresentationSink _sink;
        private readonly IUIClock _clock;
        private readonly IUILogSink _log;
        private readonly UIFrame _frame = new UIFrame();
        private readonly List<UIEntry> _entriesSnapshot = new List<UIEntry>();
        private bool _running;
        private bool _rerun;
        private bool _finishTransitions;
        private bool _shutdownPending;

        internal UIReconciler(UIKernel kernel, UIEntryTable table, IUIViewSource source, IUIPresentationSink sink, IUIClock clock, IUILogSink log)
        {
            _kernel = kernel;
            _table = table;
            _source = source;
            _sink = sink;
            _clock = clock;
            _log = log;
        }

        internal UIFrame Frame { get { return _frame; } }

        internal void Request()
        {
            if (_running) { _rerun = true; return; }
            Reconcile();
        }

        internal void Tick()
        {
            if (_kernel.IsDisposed) return;
            Request();
        }

        internal void Shutdown()
        {
            _finishTransitions = true;
            _shutdownPending = true;
            _kernel.NavigatorInternal.CloseAllForShutdown();
            Request();
        }

        internal void FailFromCallback(UIEntry entry, Exception error)
        {
            if (entry != null && entry.Act.Phase != UIPhase.Closed) Fail(entry, error);
        }

        private void Reconcile()
        {
            if (_running) { _rerun = true; return; }
            _running = true;
            try
            {
                var progressed = false;
                for (var round = 0; round < 32; round++)
                {
                    _rerun = false;
                    UIResolver.Resolve(_table, _clock.Now, _kernel.Input.IsLocked, _frame);
                    progressed = false;
                    SnapshotEntries();
                    for (var i = 0; i < _entriesSnapshot.Count; i++)
                    {
                        UITarget target;
                        var entry = _entriesSnapshot[i];
                        if (!_frame.Targets.TryGetValue(entry.Id, out target)) target = UITarget.Absent;
                        if (entry.Act.Instance != null)
                        {
                            int order;
                            if (_frame.BaseOrders.TryGetValue(entry.Id, out order))
                                try { entry.Act.Instance.SetBaseOrder(order); } catch (Exception error) { Fail(entry, error); }
                        }
                        if (Step(entry, target)) progressed = true;
                        if (NotifyContext(entry)) progressed = true;
                        if (_rerun) break;
                    }

                    if (_rerun) continue;

                    for (var i = 0; i < _frame.BrokenChains.Count; i++) _kernel.NavigatorInternal.RestoreFromBrokenChain(_frame.BrokenChains[i]);
                    _sink.Apply(_frame.Presentation);
                    _kernel.PublishFrame(_frame);
                    RemoveClosed();
                    if (!progressed && !_rerun) break;
                    if (round == 31) _log.Log(UILogLevel.Error, "UI reconciliation did not converge within 32 rounds.\n" + _kernel.Dump());
                }
            }
            finally
            {
                _running = false;
                if (_shutdownPending) CompleteShutdown();
            }
        }

        private void CompleteShutdown()
        {
            _finishTransitions = false;
            _shutdownPending = false;
            _kernel.CompleteDisposal();
        }

        private void SnapshotEntries()
        {
            _entriesSnapshot.Clear();
            _entriesSnapshot.AddRange(_table.Stack);
            _entriesSnapshot.AddRange(_table.Overlays);
        }

        private bool Step(UIEntry entry, UITarget target)
        {
            if (entry.Act.Phase == UIPhase.Shown && target == UITarget.Shown) return false;
            if (entry.Act.Phase == UIPhase.Hidden && target == UITarget.Hidden) return false;
            if (entry.Act.Phase == UIPhase.Loading && target != UITarget.Absent
                && entry.Act.Load != null && !entry.Act.Load.IsDone) return false;
            return StepChanging(entry, target);
        }

        private bool StepChanging(UIEntry entry, UITarget target)
        {
            switch (entry.Act.Phase)
            {
                case UIPhase.Pending:
                    if (target == UITarget.Absent) return CloseWithoutInstance(entry, UICloseReason.Cancelled);
                    return StartLoad(entry);
                case UIPhase.Loading:
                    if (target == UITarget.Absent)
                    {
                        if (entry.Act.Load != null && entry.Act.Load.IsDone && entry.Act.Load.Result != null)
                            _source.Return(entry.Act.Load.Result, true);
                        else entry.Act.Load?.Cancel();
                        return CloseWithoutInstance(entry, UICloseReason.Cancelled);
                    }
                    if (entry.Act.Load == null || !entry.Act.Load.IsDone) return false;
                    if (entry.Act.Load.Error != null) return Fail(entry, entry.Act.Load.Error);
                    if (entry.Act.Load.Result == null) return Fail(entry, new InvalidOperationException("UIView source completed without an instance: " + entry.Info.Id));
                    entry.Act.Instance = entry.Act.Load.Result;
                    return Guard(entry, delegate
                    {
                        entry.Act.Instance.Logic.BindSession(_kernel.Navigator, entry.Id);
                        entry.Act.OpenAttempted = true;
                        entry.Act.Instance.Logic.Open(entry.Want.Args);
                        entry.OpenCompleted = true;
                        entry.Opened.TrySetResult(new UIOpenResult(true, UICloseReason.Normal));
                    }, UIPhase.Hidden);
                case UIPhase.Hidden:
                    if (target == UITarget.Absent) return CloseHidden(entry);
                    if (target == UITarget.Shown) return Guard(entry, delegate
                    {
                        entry.Act.ResumeAttempted = true;
                        entry.Act.Instance.Logic.Resume();
                        if (!entry.Act.EnteredThisSession)
                        {
                            entry.Act.EnteredThisSession = true;
                            entry.Act.Instance.BeginEnter();
                            entry.Act.Phase = UIPhase.Entering;
                        }
                        else
                        {
                            entry.Act.Instance.SetVisible(true);
                            entry.Act.Phase = UIPhase.Shown;
                        }
                    }, null);
                    return false;
                case UIPhase.Entering:
                    if (target == UITarget.Shown && entry.Act.Instance.IsTransitionDone)
                        return Guard(entry, delegate { entry.Act.Instance.CompleteTransition(); entry.Act.Phase = UIPhase.Shown; }, null);
                    if (target == UITarget.Hidden)
                        return Guard(entry, delegate { entry.Act.Instance.CompleteTransition(); Pause(entry); entry.Act.Instance.SetVisible(false); entry.Act.Phase = UIPhase.Hidden; }, null);
                    if (target == UITarget.Absent)
                        return Guard(entry, delegate { entry.Act.Instance.CompleteTransition(); Pause(entry); entry.Act.Instance.BeginExit(); entry.Act.Phase = UIPhase.Exiting; }, null);
                    return false;
                case UIPhase.Shown:
                    if (target == UITarget.Shown) return false;
                    if (target == UITarget.Hidden)
                        return Guard(entry, delegate { Pause(entry); entry.Act.Instance.SetVisible(false); entry.Act.Phase = UIPhase.Hidden; }, null);
                    return Guard(entry, delegate { Pause(entry); entry.Act.Instance.BeginExit(); entry.Act.Phase = UIPhase.Exiting; }, null);
                case UIPhase.Exiting:
                    if (entry.Act.Instance == null) return CloseWithoutInstance(entry, entry.Want.Intent == UIExitIntent.Replaced ? UICloseReason.Replaced : UICloseReason.Normal);
                    if (!_finishTransitions && !entry.Act.Instance.HandoffRequested && !entry.Act.Instance.IsTransitionDone) return false;
                    return Guard(entry, delegate
                    {
                        entry.Act.Instance.CompleteTransition();
                        entry.Act.Instance.SetVisible(false);
                        CloseSession(entry);
                        var reason = entry.Want.Intent == UIExitIntent.Replaced ? UICloseReason.Replaced : entry.Want.Intent == UIExitIntent.Reset ? UICloseReason.Reset : UICloseReason.Normal;
                        _source.Return(entry.Act.Instance, true);
                        entry.Act.Instance = null;
                        entry.Act.Reason = reason;
                        entry.Act.Phase = UIPhase.Closed;
                    }, null);
                case UIPhase.Closed: return false;
                default: return false;
            }
        }

        private bool NotifyContext(UIEntry entry)
        {
            if (!entry.Info.IsOverlay || entry.Info.Visibility != UIOverlayVisibility.WithContext || entry.Act.Instance == null || !entry.OpenCompleted)
                return false;
            var context = _frame.Presentation.TopContext;
            if (entry.Act.HasContext && string.Equals(entry.Act.LastContext, context, StringComparison.Ordinal)) return false;
            entry.Act.HasContext = true;
            entry.Act.LastContext = context;
            try { entry.Act.Instance.Logic.ContextChanged(context); return true; }
            catch (Exception error) { return Fail(entry, error); }
        }

        private bool StartLoad(UIEntry entry)
        {
            try
            {
                entry.Act.LoadStart = _clock.Now;
                entry.Act.Load = _source.Acquire(entry.Info);
                if (entry.Act.Load == null) return Fail(entry, new InvalidOperationException("UIView source returned null load operation: " + entry.Info.Id));
                entry.Act.Phase = UIPhase.Loading;
                _kernel.Record(entry, UIPhase.Pending, UIPhase.Loading, "Acquire");
                return true;
            }
            catch (Exception error) { return Fail(entry, error); }
        }

        private bool CloseHidden(UIEntry entry)
        {
            return Guard(entry, delegate
            {
                CloseSession(entry);
                _source.Return(entry.Act.Instance, true);
                entry.Act.Instance = null;
                entry.Act.Reason = ReasonFor(entry);
                entry.Act.Phase = UIPhase.Closed;
            }, null);
        }

        private bool CloseWithoutInstance(UIEntry entry, UICloseReason reason)
        {
            var before = entry.Act.Phase;
            entry.Act.Reason = reason;
            entry.Act.Phase = UIPhase.Closed;
            _kernel.Record(entry, before, UIPhase.Closed, "Close");
            return true;
        }

        private bool Fail(UIEntry entry, Exception error)
        {
            var before = entry.Act.Phase;
            _log.Log(UILogLevel.Error, "UIView '" + entry.Info.Id + "' failed: " + error);
            try
            {
                if (entry.Act.Instance != null)
                {
                    if (entry.Act.ResumeAttempted)
                    {
                        try { Pause(entry); } catch (Exception closeError) { _log.Log(UILogLevel.Warning, closeError.ToString()); }
                    }
                    if (entry.Act.OpenAttempted)
                    {
                        try { CloseSession(entry); } catch (Exception closeError) { _log.Log(UILogLevel.Warning, closeError.ToString()); }
                    }
                    try { entry.Act.Instance.SetVisible(false); } catch (Exception closeError) { _log.Log(UILogLevel.Warning, closeError.ToString()); }
                    try { _source.Return(entry.Act.Instance, false); } catch (Exception closeError) { _log.Log(UILogLevel.Warning, closeError.ToString()); }
                    entry.Act.Instance = null;
                }
                entry.Act.Load?.Cancel();
            }
            finally
            {
                entry.Act.Reason = UICloseReason.Failed;
                entry.Act.Phase = UIPhase.Closed;
                _kernel.Record(entry, before, UIPhase.Closed, "Failed");
            }
            return true;
        }

        private bool Guard(UIEntry entry, Action action, UIPhase? next)
        {
            var before = entry.Act.Phase;
            try
            {
                action();
                if (next.HasValue) entry.Act.Phase = next.Value;
                _kernel.Record(entry, before, entry.Act.Phase, "Reconcile");
                return true;
            }
            catch (Exception error) { return Fail(entry, error); }
        }

        private static void Pause(UIEntry entry)
        {
            entry.Act.ResumeAttempted = false;
            entry.Act.Instance.Logic.Pause();
        }

        private static void CloseSession(UIEntry entry)
        {
            entry.Act.OpenAttempted = false;
            entry.Act.Instance.Logic.Close();
        }

        private UICloseReason ReasonFor(UIEntry entry)
        {
            switch (entry.Want.Intent)
            {
                case UIExitIntent.Replaced: return UICloseReason.Replaced;
                case UIExitIntent.Reset: return UICloseReason.Reset;
                default: return UICloseReason.Normal;
            }
        }

        private void RemoveClosed()
        {
            for (var i = _table.Stack.Count - 1; i >= 0; i--) if (_table.Stack[i].Act.Phase == UIPhase.Closed) FinalizeClosed(_table.Stack[i], _table.Stack);
            for (var i = _table.Overlays.Count - 1; i >= 0; i--) if (_table.Overlays[i].Act.Phase == UIPhase.Closed) FinalizeClosed(_table.Overlays[i], _table.Overlays);
        }

        private void FinalizeClosed(UIEntry entry, List<UIEntry> owner)
        {
            owner.Remove(entry);
            _table.ById.Remove(entry.Id);
            var succeeded = entry.Act.Reason == UICloseReason.Normal || entry.Act.Reason == UICloseReason.Replaced || entry.Act.Reason == UICloseReason.Reset;
            entry.Opened.TrySetResult(new UIOpenResult(entry.OpenCompleted && entry.Act.Reason != UICloseReason.Failed && entry.Act.Reason != UICloseReason.Cancelled, entry.Act.Reason));
            entry.Closed.TrySetResult(entry.Act.Reason);
            entry.Result.TrySetResult(new UIResultBox(entry.Want.HasResult, entry.Want.Result, entry.Act.Reason));
            _kernel.Record(entry, UIPhase.Closed, UIPhase.Closed, "Removed");
        }
    }
}
