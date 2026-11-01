using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GameUI.Runtime
{
    public sealed class UICameraRig : IDisposable
    {
        private readonly UIRoot _root;
        private readonly Camera _uiCamera;
        private readonly UniversalAdditionalCameraData _uiData;
        private Camera _worldCamera;
        private UniversalAdditionalCameraData _worldData;
        private int _originalWorldMask;
        private bool _originalWorldShadows;
        private bool _originalWorldPostProcessing;
        private CameraRenderType _originalWorldType;
        private int _uiLayerMask;
        private bool _worldCovered;
        private bool _worldWasRegistered;
        private bool _disposed;

        public UICameraRig(UIRoot root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            var layer = UIRoot.UIRenderLayer;
            _uiLayerMask = 1 << layer;
            var cameraObject = new GameObject("UICamera", typeof(Camera), typeof(UniversalAdditionalCameraData));
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(cameraObject);
            _uiCamera = cameraObject.GetComponent<Camera>();
            _uiCamera.clearFlags = CameraClearFlags.Depth;
            _uiCamera.cullingMask = _uiLayerMask;
            _uiCamera.orthographic = true;
            _uiCamera.nearClipPlane = 0.1f;
            _uiCamera.farClipPlane = 1000f;
            _uiData = cameraObject.GetComponent<UniversalAdditionalCameraData>();
            _uiData.renderType = CameraRenderType.Overlay;
            _uiData.renderPostProcessing = false;
            _uiData.renderShadows = false;
            _uiCamera.enabled = false;
        }

        public Camera UICamera { get { return _uiCamera; } }
        public Camera WorldCamera { get { return _worldCamera; } }

        public void Tick()
        {
            if (!_disposed && _worldWasRegistered && _worldCamera == null) Register(null);
        }

        public void Register(Camera camera)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UICameraRig));
            if (_worldCamera == camera && camera != null) return;
            RestoreWorldCamera();
            _worldCamera = camera;
            _worldWasRegistered = camera != null;

            if (_worldCamera == null)
            {
                _uiData.renderType = CameraRenderType.Base;
                _uiCamera.clearFlags = CameraClearFlags.SolidColor;
                _uiCamera.backgroundColor = Color.black;
                _uiCamera.enabled = true;
                _root.SetCamera(_uiCamera);
                return;
            }

            _worldData = _worldCamera.GetComponent<UniversalAdditionalCameraData>();
            if (_worldData == null) _worldData = _worldCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            _originalWorldMask = _worldCamera.cullingMask;
            _originalWorldShadows = _worldData.renderShadows;
            _originalWorldPostProcessing = _worldData.renderPostProcessing;
            _originalWorldType = _worldData.renderType;
            _worldData.renderType = CameraRenderType.Base;
            if (!_worldData.cameraStack.Contains(_uiCamera)) _worldData.cameraStack.Add(_uiCamera);
            _worldCamera.cullingMask &= ~_uiLayerMask;
            _uiCamera.clearFlags = CameraClearFlags.Depth;
            _uiCamera.targetDisplay = _worldCamera.targetDisplay;
            _uiCamera.enabled = true;
            _uiData.renderType = CameraRenderType.Overlay;
            _root.SetCamera(_uiCamera);
            ApplyWorldCoverage();
        }

        public void SetWorldCovered(bool covered)
        {
            if (_worldCovered == covered) return;
            _worldCovered = covered;
            ApplyWorldCoverage();
        }

        private void ApplyWorldCoverage()
        {
            if (_worldCamera == null || _worldData == null) return;
            _worldCamera.cullingMask = _worldCovered ? 0 : _originalWorldMask & ~_uiLayerMask;
            _worldData.renderShadows = _worldCovered ? false : _originalWorldShadows;
            _worldData.renderPostProcessing = _worldCovered ? false : _originalWorldPostProcessing;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RestoreWorldCamera();
            if (_uiCamera != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_uiCamera.gameObject);
                else UnityEngine.Object.DestroyImmediate(_uiCamera.gameObject);
            }
        }

        private void RestoreWorldCamera()
        {
            if (_worldCamera != null && _worldData != null)
            {
                _worldData.cameraStack.Remove(_uiCamera);
                _worldCamera.cullingMask = _originalWorldMask;
                _worldData.renderShadows = _originalWorldShadows;
                _worldData.renderPostProcessing = _originalWorldPostProcessing;
                _worldData.renderType = _originalWorldType;
            }
            _worldCamera = null;
            _worldData = null;
        }
    }
}
