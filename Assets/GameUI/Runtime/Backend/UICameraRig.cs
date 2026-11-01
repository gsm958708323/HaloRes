using UnityEngine;

namespace GameUI.Runtime
{
    public sealed class UICameraRig
    {
        private Camera _camera;
        private int _originalMask;

        public void Register(Camera camera)
        {
            _camera = camera;
            if (_camera != null) _originalMask = _camera.cullingMask;
        }

        public void SetWorldCovered(bool covered)
        {
            if (_camera == null) return;
            _camera.cullingMask = covered ? 0 : _originalMask;
        }
    }
}
