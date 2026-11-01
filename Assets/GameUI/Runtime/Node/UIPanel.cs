namespace GameUI.Runtime
{
    public abstract class UIPanel : UINode, IUINodeChild
    {
        private bool _shown = true;
        private UIView _owner;
        private UnityEngine.CanvasGroup _group;

        public UIView Owner { get { return _owner; } }
        public bool IsShown { get { return _shown; } }

        internal void BindOwner(UIView owner) { _owner = owner; }
        internal void SetPresentation(bool visible)
        {
            if (GameObject == null) return;
            if (_group == null) _group = GameObject.GetComponent<UnityEngine.CanvasGroup>();
            if (_group == null) _group = GameObject.AddComponent<UnityEngine.CanvasGroup>();
            _group.alpha = visible ? 1f : 0f;
            _group.interactable = visible;
            _group.blocksRaycasts = visible;
        }
        public void Show() { if (!_shown) { _shown = true; RefreshEffectiveVisibility(); } }
        public void Hide() { if (_shown) { _shown = false; RefreshEffectiveVisibility(); } }

        void IUINodeChild.HostOpen() { _shown = true; SetParentVisibility(false); HostOpen(); }
        void IUINodeChild.HostVisibilityChanged(bool visible) { SetParentVisibility(visible); }
        void IUINodeChild.HostRefresh() { RefreshTree(); }
        void IUINodeChild.HostClose() { HostClose(); }
        void IUINodeChild.HostDestroy() { HostDestroy(); }
        bool IUINodeChild.HostBack() { return IsEffectivelyVisible && HostBack(); }
    }
}
