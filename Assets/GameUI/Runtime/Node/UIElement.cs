using System;
using GameUI.Components;
using UnityEngine;

namespace GameUI.Runtime
{
    public abstract class UIElement
    {
        private bool _initialized;
        public GameObject GameObject { get; private set; }
        public RectTransform Rect { get; private set; }
        protected UIScope InstanceScope { get; private set; }

        internal void Initialize(GameObject gameObject)
        {
            if (_initialized) throw new InvalidOperationException("UI element is already initialized.");
            GameObject = gameObject ?? throw new ArgumentNullException(nameof(gameObject));
            Rect = gameObject.GetComponent<RectTransform>();
            InstanceScope = new UIScope();
            _initialized = true;
            try
            {
                var binding = gameObject.GetComponent<UIBindingData>();
                BindControls(binding);
                OnCreate();
            }
            catch
            {
                try { if (this is UINode node) node.HostDestroy(); else DestroyElement(); }
                catch (Exception cleanupError) { Debug.LogException(cleanupError); }
                throw;
            }
        }

        internal virtual void DestroyElement()
        {
            if (!_initialized) return;
            var scope = InstanceScope;
            try { OnDestroy(); }
            finally
            {
                try { scope.Dispose(); }
                finally { _initialized = false; GameObject = null; Rect = null; InstanceScope = null; }
            }
        }

        protected virtual void BindControls(UIBindingData data) { }
        protected virtual void OnCreate() { }
        protected virtual void OnDestroy() { }
    }
}
