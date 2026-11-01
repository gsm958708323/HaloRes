using System;

namespace GameUI.Runtime
{
    internal interface IUIItemHost
    {
        void OnItemEvent(int index, int code, int bindingVersion);
    }

    public abstract class UIItem<TData> : UIElement
    {
        private IUIItemHost _host;
        private int _index = -1;
        private TData _data;
        private UIScope _bindScope;
        private bool _bound;
        private int _bindingVersion;

        public int Index { get { return _index; } }
        public int BindingVersion { get { return _bindingVersion; } }
        protected TData Data { get { return _data; } }
        protected UIScope BindScope
        {
            get
            {
                if (!_bound) throw new InvalidOperationException("The item has no active binding.");
                if (_bindScope == null) _bindScope = new UIScope();
                return _bindScope;
            }
        }

        internal void BindHost(IUIItemHost host) { _host = host; }

        internal void Bind(int index, TData data)
        {
            if (_bound) Unbind();
            _index = index;
            _data = data;
            _bindingVersion++;
            _bindScope = null;
            _bound = true;
            OnBind(data);
        }

        internal void Unbind()
        {
            if (!_bound) return;
            _bindingVersion++;
            try { OnUnbind(); }
            finally
            {
                _bindScope?.Dispose();
                _bindScope = null;
                _data = default(TData);
                _index = -1;
                _bound = false;
            }
        }

        protected void NotifyHost(int code = 0)
        {
            if (_bound && _host != null && _index >= 0) _host.OnItemEvent(_index, code, _bindingVersion);
        }

        protected void NotifyHost(int code, int expectedBindingVersion)
        {
            if (expectedBindingVersion == _bindingVersion) NotifyHost(code);
        }

        internal override void DestroyElement()
        {
            try { Unbind(); }
            finally { base.DestroyElement(); }
        }

        protected abstract void OnBind(TData data);
        protected virtual void OnUnbind() { }
    }
}
