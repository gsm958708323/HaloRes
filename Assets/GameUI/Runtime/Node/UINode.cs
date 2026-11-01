using System.Collections.Generic;
using GameUI.Components;
using UnityEngine;

namespace GameUI.Runtime
{
    public abstract class UINode : UIElement
    {
        private readonly List<IUINodeChild> _children = new List<IUINodeChild>();
        private readonly UIScopeCycle _session = new UIScopeCycle("Session");
        private readonly UIScopeCycle _resume = new UIScopeCycle("Resume");
        private readonly List<UIModelStage> _modelStages = new List<UIModelStage>();
        private bool _dirty;
        private bool _resumed;
        private bool _parentVisible;
        private bool _opened;
        private bool _refreshing;
        private bool _invalidatedDuringRefresh;
        private int _consecutiveRefreshInvalidations;

        public bool IsEffectivelyVisible { get { return _resumed; } }
        protected UIScope SessionScope { get { return _session.RequireCurrent(); } }
        protected UIScope ResumeScope { get { return _resume.RequireCurrent(); } }
        internal bool IsDirty { get { return _dirty; } }

        internal void AddChild(IUINodeChild child)
        {
            if (child != null && !_children.Contains(child)) _children.Add(child);
        }

        protected T AddPanel<T>(T panel, UIBindingData node) where T : UIPanel
        {
            if (panel == null) throw new System.ArgumentNullException(nameof(panel));
            if (node == null) throw new System.ArgumentNullException(nameof(node));
            var owner = this as UIView;
            if (owner == null && this is UIPanel parentPanel) owner = parentPanel.Owner;
            if (owner == null) throw new System.InvalidOperationException("A panel must belong to a view.");
            panel.BindOwner(owner);
            panel.SetParentVisibility(_resumed);
            panel.Initialize(node.gameObject);
            AddChild(panel);
            if (_opened)
            {
                panel.HostOpen();
                panel.SetParentVisibility(_resumed);
            }
            return panel;
        }

        protected void Invalidate()
        {
            _dirty = true;
            if (_refreshing) _invalidatedDuringRefresh = true;
        }

        public string DumpScopes()
        {
            return GetType().FullName + " Instance=" + (InstanceScope?.RegistrationCount ?? 0)
                + " Session=" + (_session.Current?.RegistrationCount ?? 0)
                + " Resume=" + (_resume.Current?.RegistrationCount ?? 0)
                + " Visible=" + _resumed + " Dirty=" + _dirty;
        }

        protected UIModelStage CreateModelStage(UIModelView view)
        {
            if (view == null) throw new System.ArgumentNullException(nameof(view));
            var stage = new UIModelStage(view, UIBoot.Root != null ? UIBoot.Root.ModelStageRoot : null);
            SessionScope.Add(stage);
            _modelStages.Add(stage);
            return stage;
        }

        internal void HostOpen()
        {
            if (_opened) return;
            _opened = true;
            for (var i = 0; i < _children.Count; i++) _children[i].HostOpen();
            _session.Begin();
            _dirty = true;
            OnOpen();
        }

        internal void HostResume()
        {
            if (_resumed || !_opened) return;
            _resumed = true;
            _resume.Begin();
            for (var i = 0; i < _modelStages.Count; i++) _modelStages[i].SetVisible(true);
            for (var i = 0; i < _children.Count; i++) _children[i].HostVisibilityChanged(true);
            OnResume();
        }

        internal void HostPause()
        {
            if (!_resumed) return;
            System.Exception failure = null;
            try { OnPause(); }
            catch (System.Exception error) { failure = error; }
            finally
            {
                _resume.End();
                _resumed = false;
                for (var i = 0; i < _modelStages.Count; i++) _modelStages[i].SetVisible(false);
                for (var i = _children.Count - 1; i >= 0; i--)
                    try { _children[i].HostVisibilityChanged(false); }
                    catch (System.Exception error) { if (failure == null) failure = error; else Debug.LogException(error); }
            }
            if (failure != null) throw failure;
        }

        internal void HostClose()
        {
            if (!_opened) return;
            System.Exception failure = null;
            try { if (_resumed) HostPause(); }
            catch (System.Exception error) { failure = error; }
            try { OnClose(); }
            catch (System.Exception error)
            {
                if (failure == null) failure = error;
                else Debug.LogException(error);
            }
            finally
            {
                _session.End();
                _modelStages.Clear();
                for (var i = _children.Count - 1; i >= 0; i--)
                {
                    try { _children[i].HostClose(); }
                    catch (System.Exception error) { if (failure == null) failure = error; else Debug.LogException(error); }
                }
                _opened = false;
            }
            if (failure != null) throw failure;
        }

        internal void HostDestroy()
        {
            try { if (_opened) HostClose(); }
            finally
            {
                try { DestroyElement(); }
                finally
                {
                    for (var i = _children.Count - 1; i >= 0; i--)
                    {
                        try { _children[i].HostDestroy(); }
                        catch (System.Exception error) { Debug.LogException(error); }
                    }
                    _children.Clear();
                }
            }
        }

        internal void SetParentVisibility(bool visible)
        {
            _parentVisible = visible;
            RefreshEffectiveVisibility();
        }

        protected void RefreshEffectiveVisibility()
        {
            var panel = this as UIPanel;
            var visible = _opened && _parentVisible && (panel == null || panel.IsShown);
            panel?.SetPresentation(visible);
            if (visible) HostResume(); else HostPause();
        }

        internal void RefreshTree()
        {
            if (_dirty && _resumed)
            {
                _dirty = false;
                _refreshing = true;
                _invalidatedDuringRefresh = false;
                try { OnRefresh(); }
                finally { _refreshing = false; }
                if (_invalidatedDuringRefresh)
                {
                    _dirty = true;
                    _consecutiveRefreshInvalidations++;
                    if (_consecutiveRefreshInvalidations == 3)
                        Debug.LogWarning("UI node was invalidated during OnRefresh for three consecutive frames: " + GetType().FullName);
                }
                else _consecutiveRefreshInvalidations = 0;
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
