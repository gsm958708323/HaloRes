using System;
using System.Collections.Generic;
using GameUI.Components;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI.Runtime
{
    public sealed class UIModelStage : IDisposable
    {
        private static readonly Dictionary<Vector2Int, Stack<RenderTexture>> Targets = new Dictionary<Vector2Int, Stack<RenderTexture>>();
        private static int _nextStage;
        private readonly Transform _root;
        private readonly GameObject _model;
        private readonly RenderTexture _target;
        private readonly Camera _camera;
        private readonly RawImage _image;
        private bool _disposed;

        public UIModelStage(UIModelView view, Transform parent = null, int layer = 31)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (view.modelPrefab == null) throw new InvalidOperationException("UIModelView.modelPrefab is required: " + view.name);
            if (view.target == null) throw new InvalidOperationException("UIModelView.target is required: " + view.name);

            _image = view.target;
            var width = Mathf.Max(16, view.rtSize.x);
            var height = Mathf.Max(16, view.rtSize.y);
            _target = Rent(width, height);
            _image.texture = _target;
            var rootObject = new GameObject("UIModelStage_" + view.stageTag, typeof(RectTransform));
            _root = rootObject.transform;
            _root.SetParent(parent, false);
            _root.position = new Vector3(10000f + ++_nextStage * 100f, 0f, 0f);
            _model = UnityEngine.Object.Instantiate(view.modelPrefab, _root, false);
            SetLayerRecursively(_root, layer);

            var cameraObject = new GameObject("Camera", typeof(Camera));
            cameraObject.transform.SetParent(_root, false);
            _camera = cameraObject.GetComponent<Camera>();
            _camera.targetTexture = _target;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.clear;
            _camera.cullingMask = 1 << layer;
            _camera.orthographic = true;
            _camera.nearClipPlane = 0.01f;
            _camera.farClipPlane = 1000f;
            _camera.enabled = false;
            FrameModel();
        }

        public RenderTexture Target { get { return _target; } }
        public Camera Camera { get { return _camera; } }
        public GameObject Model { get { return _model; } }

        public void SetVisible(bool visible)
        {
            if (!_disposed && _camera != null) _camera.enabled = visible;
        }

        private void FrameModel()
        {
            var renderers = _model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                _camera.transform.localPosition = new Vector3(0f, 0f, -5f);
                return;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var size = bounds.size;
            _camera.transform.position = bounds.center + new Vector3(0f, 0f, -Mathf.Max(size.x, size.y, size.z) * 2f - 0.1f);
            _camera.transform.rotation = Quaternion.identity;
            _camera.orthographicSize = Mathf.Max(size.y, size.x * _target.height / Mathf.Max(1f, _target.width)) * 0.6f;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_image != null && _image.texture == _target) _image.texture = null;
            if (_camera != null) { _camera.enabled = false; _camera.targetTexture = null; }
            var rootObject = _root != null ? _root.gameObject : null;
            if (rootObject != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(rootObject);
                else UnityEngine.Object.DestroyImmediate(rootObject);
            }
            Return(_target);
        }

        private static RenderTexture Rent(int width, int height)
        {
            var key = new Vector2Int(width, height);
            Stack<RenderTexture> pool;
            if (Targets.TryGetValue(key, out pool) && pool.Count > 0)
            {
                var reused = pool.Pop();
                reused.Create();
                return reused;
            }
            var target = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            {
                name = "GameUIModel_" + width + "x" + height,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear
            };
            target.Create();
            return target;
        }

        private static void Return(RenderTexture target)
        {
            if (target == null) return;
            target.Release();
            var key = new Vector2Int(target.width, target.height);
            Stack<RenderTexture> pool;
            if (!Targets.TryGetValue(key, out pool))
            {
                pool = new Stack<RenderTexture>();
                Targets.Add(key, pool);
            }
            if (pool.Count >= 8)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(target);
                else UnityEngine.Object.DestroyImmediate(target);
                return;
            }
            pool.Push(target);
        }

        public static void ClearPool()
        {
            foreach (var pool in Targets.Values)
                while (pool.Count > 0)
                {
                    var target = pool.Pop();
                    if (target == null) continue;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(target);
                    else UnityEngine.Object.DestroyImmediate(target);
                }
            Targets.Clear();
        }

        private static void SetLayerRecursively(Transform transform, int layer)
        {
            transform.gameObject.layer = layer;
            for (var i = 0; i < transform.childCount; i++) SetLayerRecursively(transform.GetChild(i), layer);
        }
    }
}
