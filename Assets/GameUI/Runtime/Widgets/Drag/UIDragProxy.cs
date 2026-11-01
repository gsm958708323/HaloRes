using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI.Runtime
{
    public sealed class UIDragCoordinator
    {
        private readonly HashSet<UIDragProxy> _active = new HashSet<UIDragProxy>();
        private readonly List<UIDragProxy> _cancelBuffer = new List<UIDragProxy>();

        public UIDragProxy Begin(UIScope scope, RectTransform source, Transform dragRoot, Vector2 pointerPosition, Camera camera = null)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (scope.IsDisposed) throw new ObjectDisposedException(nameof(scope));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (dragRoot == null) throw new ArgumentNullException(nameof(dragRoot));
            var proxyObject = UnityEngine.Object.Instantiate(source.gameObject, dragRoot, false);
            proxyObject.name = source.gameObject.name + "[Drag]";
            var graphics = proxyObject.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++) graphics[i].raycastTarget = false;
            var canvases = proxyObject.GetComponentsInChildren<Canvas>(true);
            for (var i = 0; i < canvases.Length; i++)
            {
                canvases[i].overrideSorting = true;
                canvases[i].sortingOrder = 8501 + i;
            }
            var group = source.GetComponent<CanvasGroup>();
            if (group == null) group = source.gameObject.AddComponent<CanvasGroup>();
            var proxy = new UIDragProxy(this, proxyObject.GetComponent<RectTransform>(), group, camera != null ? camera : UIBoot.Camera?.UICamera);
            _active.Add(proxy);
            scope.Add(proxy);
            proxy.SetScreenPosition(pointerPosition);
            return proxy;
        }

        public void CancelAll()
        {
            _cancelBuffer.Clear();
            _cancelBuffer.AddRange(_active);
            for (var i = 0; i < _cancelBuffer.Count; i++) _cancelBuffer[i].Cancel();
            _cancelBuffer.Clear();
        }

        internal void Remove(UIDragProxy proxy) { _active.Remove(proxy); }
    }

    public sealed class UIDragProxy : IDisposable
    {
        private readonly UIDragCoordinator _owner;
        private readonly RectTransform _rect;
        private readonly CanvasGroup _sourceGroup;
        private readonly Camera _camera;
        private readonly float _alpha;
        private readonly bool _interactable;
        private readonly bool _blocksRaycasts;
        private bool _closed;

        internal UIDragProxy(UIDragCoordinator owner, RectTransform proxy, CanvasGroup sourceGroup, Camera camera)
        {
            _owner = owner;
            _rect = proxy;
            _sourceGroup = sourceGroup;
            _camera = camera;
            _alpha = sourceGroup.alpha;
            _interactable = sourceGroup.interactable;
            _blocksRaycasts = sourceGroup.blocksRaycasts;
            _sourceGroup.alpha = 0f;
            _sourceGroup.interactable = false;
            _sourceGroup.blocksRaycasts = false;
        }

        public void SetScreenPosition(Vector2 position) { SetScreenPosition(position, _camera); }

        public void SetScreenPosition(Vector2 position, Camera camera)
        {
            if (_closed || _rect == null || _rect.parent == null) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect.parent as RectTransform, position, camera, out var local))
                _rect.anchoredPosition = local;
        }

        public void Complete() { Close(); }
        public void Cancel() { Close(); }
        public void Dispose() { Cancel(); }

        private void Close()
        {
            if (_closed) return;
            _closed = true;
            if (_sourceGroup != null)
            {
                _sourceGroup.alpha = _alpha;
                _sourceGroup.interactable = _interactable;
                _sourceGroup.blocksRaycasts = _blocksRaycasts;
            }
            if (_rect != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_rect.gameObject);
                else UnityEngine.Object.DestroyImmediate(_rect.gameObject);
            }
            _owner.Remove(this);
        }
    }
}
