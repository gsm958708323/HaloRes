namespace GameUI.Runtime
{
    public abstract class UIPanel : UINode, IUINodeChild
    {
        private bool _shown = true;
        private UIView _owner;

        public UIView Owner { get { return _owner; } }
        public bool IsShown { get { return _shown; } }

        internal void BindOwner(UIView owner) { _owner = owner; }
        public void Show() { if (!_shown) { _shown = true; HostResume(); } }
        public void Hide() { if (_shown) { _shown = false; HostPause(); } }

        void IUINodeChild.HostOpen() { HostOpen(); }
        void IUINodeChild.HostVisibilityChanged(bool visible) { if (visible && _shown) HostResume(); else HostPause(); }
        void IUINodeChild.HostRefresh() { RefreshTree(); }
        void IUINodeChild.HostClose() { HostClose(); }
        void IUINodeChild.HostDestroy() { HostDestroy(); }
        bool IUINodeChild.HostBack() { return IsEffectivelyVisible && HostBack(); }
    }
}
