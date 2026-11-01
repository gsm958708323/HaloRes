using System;
using UnityEngine;

namespace GameUI.Runtime
{
    public sealed class UIModelStage : IDisposable
    {
        private readonly RenderTexture _target;
        private readonly Camera _camera;
        public UIModelStage(int width, int height)
        {
            _target = new RenderTexture(Mathf.Max(16, width), Mathf.Max(16, height), 16, RenderTextureFormat.ARGB32);
            _target.Create();
            var go = new GameObject("UIModelStageCamera");
            _camera = go.AddComponent<Camera>();
            _camera.targetTexture = _target;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.clear;
        }
        public RenderTexture Target { get { return _target; } }
        public Camera Camera { get { return _camera; } }
        public void Dispose()
        {
            if (_camera != null) UnityEngine.Object.Destroy(_camera.gameObject);
            if (_target != null) { _target.Release(); UnityEngine.Object.Destroy(_target); }
        }
    }
}
