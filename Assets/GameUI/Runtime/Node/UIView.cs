using System;
using GameUI.Kernel;

namespace GameUI.Runtime
{
    public abstract class UIView : UINode, IUIViewLogic
    {
        private UIArgs _args;
        private IUINavigator _navigator;
        private int _entryId;

        protected internal IUINavigator Nav { get { return _navigator; } }
        protected UIArgs RawArgs { get { return _args; } }

        void IUIViewLogic.BindSession(IUINavigator navigator, int entryId)
        {
            _navigator = navigator;
            _entryId = entryId;
        }

        void IUIViewLogic.Open(UIArgs args)
        {
            _args = args;
            HostOpen();
        }

        void IUIViewLogic.Resume() { HostResume(); }
        void IUIViewLogic.Pause() { HostPause(); }
        void IUIViewLogic.Close() { HostClose(); }
        bool IUIViewLogic.Back() { return HostBack(); }
        void IUIViewLogic.ContextChanged(string context) { OnContextChanged(context); }

        protected void CloseSelf() { _navigator?.Close(_entryId); }
        protected void CompleteResult(object result) { _navigator?.Finish(_entryId, result); }
        protected virtual void OnContextChanged(string context) { }
    }

    public abstract class UIView<TArgs> : UIView where TArgs : UIArgs
    {
        protected TArgs Args { get { return (TArgs)RawArgs; } }
        protected sealed override void OnOpen() { OnOpen(Args); }
        protected abstract void OnOpen(TArgs args);
    }

    public abstract class UIView<TArgs, TResult> : UIView<TArgs> where TArgs : UIArgs
    {
        protected void Finish(TResult result)
        {
            if (Nav == null) throw new InvalidOperationException("View is not bound to a session.");
            CompleteResult(result);
        }
    }
}
