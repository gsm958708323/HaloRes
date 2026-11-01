using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI.Runtime
{
    public enum UIWorldHudMode { WorldSpace, ScreenSpace }

    public sealed class UIWorldHud : IDisposable
    {
        private static readonly Dictionary<int, Stack<RectTransform>> Pools = new Dictionary<int, Stack<RectTransform>>();
        private readonly RectTransform _rect;
        private readonly Canvas _canvas;
        private readonly Camera _worldCamera;
        private readonly Camera _uiCamera;
        private readonly Transform _target;
        private readonly Vector3 _offset;
        private readonly UIWorldHudMode _mode;
        private readonly int _templateId;
        private bool _disposed;

        public UIWorldHud(RectTransform template, Transform target, Transform parent, Camera worldCamera,
            Camera uiCamera, Vector3 offset, UIWorldHudMode mode = UIWorldHudMode.WorldSpace)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (target == null) throw new ArgumentNullException(nameof(target));
            _target = target;
            _worldCamera = worldCamera;
            _uiCamera = uiCamera;
            _offset = offset;
            _mode = mode;
            _templateId = template.GetInstanceID();
            Stack<RectTransform> pool;
            if (Pools.TryGetValue(_templateId, out pool) && pool.Count > 0) _rect = pool.Pop();
            if (_rect == null) _rect = UnityEngine.Object.Instantiate(template, parent, false);
            _rect.SetParent(parent, false);
            _rect.gameObject.name = template.gameObject.name + "[Hud]";
            _rect.gameObject.SetActive(true);
            _canvas = _rect.GetComponent<Canvas>();
            if (_canvas == null) _canvas = _rect.gameObject.AddComponent<Canvas>();
            _canvas.renderMode = mode == UIWorldHudMode.WorldSpace ? RenderMode.WorldSpace : RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = mode == UIWorldHudMode.WorldSpace ? worldCamera : uiCamera;
            _canvas.enabled = true;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 900;
            _rect.gameObject.layer = mode == UIWorldHudMode.WorldSpace ? 0 : UIRoot.UIRenderLayer;
            var graphics = _rect.GetComponentsInChildren<Graphic>(true);
            for (var i = 0; i < graphics.Length; i++) graphics[i].raycastTarget = false;
        }

        public RectTransform Rect { get { return _rect; } }

        public void LateUpdate()
        {
            if (_disposed || _rect == null) return;
            if (_target == null || _worldCamera == null) { _canvas.enabled = false; return; }
            var position = _target.position + _offset;
            if (_mode == UIWorldHudMode.WorldSpace)
            {
                _canvas.enabled = true;
                _rect.position = position;
                _rect.rotation = _worldCamera.transform.rotation;
                return;
            }
            var screen = _worldCamera.WorldToScreenPoint(position);
            _canvas.enabled = screen.z > 0f;
            if (screen.z <= 0f) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect.parent as RectTransform, screen, _uiCamera, out var local))
                _rect.anchoredPosition = local;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_rect == null) return;
            _rect.gameObject.SetActive(false);
            Stack<RectTransform> pool;
            if (!Pools.TryGetValue(_templateId, out pool))
            {
                pool = new Stack<RectTransform>();
                Pools.Add(_templateId, pool);
            }
            pool.Push(_rect);
        }

        public static void ClearPool()
        {
            foreach (var pool in Pools.Values)
                while (pool.Count > 0)
                {
                    var rect = pool.Pop();
                    if (rect == null) continue;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(rect.gameObject);
                    else UnityEngine.Object.DestroyImmediate(rect.gameObject);
                }
            Pools.Clear();
        }
    }
}
