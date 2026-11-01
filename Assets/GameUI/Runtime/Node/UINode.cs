using System.Collections.Generic;

namespace GameUI.Runtime
{
    public abstract class UINode : UIElement
    {
        private readonly List<IUINodeChild> _children = new List<IUINodeChild>();
        private readonly UIScopeCycle _session = new UIScopeCycle("Session");
        private readonly UIScopeCycle _resume = new UIScopeCycle("Resume");
        private bool _dirty;
        private bool _resumed;

        public bool IsEffectivelyVisible { get { return _resumed; } }
        protected UIScope SessionScope { get { return _session.RequireCurrent(); } }
        protected UIScope ResumeScope { get { return _resume.RequireCurrent(); } }
        internal bool IsDirty { get { return _dirty; } }

        protected void AddChild(IUINodeChild child)
        {
            if (child != null && !_children.Contains(child)) _children.Add(child);
        }

        protected void Invalidate() { _dirty = true; }

        internal void HostOpen()
        {
            for (var i = 0; i < _children.Count; i++) _children[i].HostOpen();
            _session.Begin();
            OnOpen();
        }

        internal void HostResume()
        {
            if (_resumed) return;
            _resumed = true;
            _resume.Begin();
            for (var i = 0; i < _children.Count; i++) _children[i].HostVisibilityChanged(true);
            OnResume();
        }

        internal void HostPause()
        {
            if (!_resumed) return;
            OnPause();
            for (var i = _children.Count - 1; i >= 0; i--) _children[i].HostVisibilityChanged(false);
            _resume.End();
            _resumed = false;
        }

        internal void HostClose()
        {
            if (_resumed) HostPause();
            OnClose();
            _session.End();
            for (var i = _children.Count - 1; i >= 0; i--) _children[i].HostClose();
        }

        internal void HostDestroy()
        {
            for (var i = _children.Count - 1; i >= 0; i--) _children[i].HostDestroy();
            DestroyElement();
        }

        internal void RefreshTree()
        {
            if (_dirty && _resumed)
            {
                _dirty = false;
                OnRefresh();
            }
            for (var i = 0; i < _children.Count; i++) _children[i].HostRefresh();
        }

        internal bool HostBack()
        {
            for (var i = _children.Count - 1; i >= 0; i--) if (_children[i].HostBack()) return true;
            return OnBack();
        }

        protected virtual void OnOpen() { }
        protected virtual void OnResume() { }
        protected virtual void OnRefresh() { }
        protected virtual void OnPause() { }
        protected virtual void OnClose() { }
        protected virtual bool OnBack() { return false; }
    }
}
