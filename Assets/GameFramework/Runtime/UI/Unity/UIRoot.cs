using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace GameFramework.UI
{
    /// <summary>拥有 UI 根对象与各层 Canvas；场景已有 EventSystem 时不接管它。</summary>
    internal sealed class UIRoot : IDisposable
    {
        private readonly Dictionary<LayerType, UILayer> _layers = new Dictionary<LayerType, UILayer>();
        private GameObject _gameObject;

        internal UIRoot(Vector2 resolution, Transform parent, Camera worldCamera)
        {
            if (!(resolution.x > 0) || !(resolution.y > 0)) throw new ArgumentOutOfRangeException(nameof(resolution));
            _gameObject = new GameObject("UIRoot", typeof(RectTransform));
            _gameObject.layer = 5;
            if (parent != null) _gameObject.transform.SetParent(parent, false);
            else UnityEngine.Object.DontDestroyOnLoad(_gameObject);

            foreach (LayerType type in Enum.GetValues(typeof(LayerType))) CreateLayer(type, resolution, worldCamera);
            CreateEventSystemIfMissing();
        }

        internal Transform Transform => _gameObject == null ? null : _gameObject.transform;
        internal UILayer GetLayer(LayerType type) => _layers[type];
        internal Canvas GetCanvas(LayerType type) => GetLayer(type).Canvas;

        private void CreateLayer(LayerType type, Vector2 resolution, Camera worldCamera)
        {
            var sceneLayer = type == LayerType.SceneLayer;
            var node = new GameObject(type.ToString(), typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            node.layer = sceneLayer ? 0 : 5;
            node.transform.SetParent(_gameObject.transform, false);
            var canvas = node.GetComponent<Canvas>();
            canvas.renderMode = sceneLayer ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = worldCamera;
            canvas.overrideSorting = true;
            ((RectTransform)node.transform).sizeDelta = resolution;
            if (sceneLayer) node.transform.localScale = Vector3.one * 0.001f;
            var scaler = node.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = resolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            _layers.Add(type, new UILayer(type, canvas));
        }

        private void CreateEventSystemIfMissing()
        {
            if (EventSystem.current != null) return;
            var events = new GameObject("EventSystem", typeof(EventSystem));
            events.transform.SetParent(_gameObject.transform, false);
#if ENABLE_INPUT_SYSTEM
            events.AddComponent<InputSystemUIInputModule>();
#else
            events.AddComponent<StandaloneInputModule>();
#endif
        }

        public void Dispose()
        {
            if (_gameObject != null) UnityEngine.Object.Destroy(_gameObject);
            _gameObject = null;
            _layers.Clear();
        }
    }
}
