using UnityEngine;

namespace GameUI.Runtime
{
    public sealed class UIWorldHud
    {
        private readonly RectTransform _rect;
        private readonly Camera _worldCamera;
        private readonly Camera _uiCamera;
        private readonly Vector3 _offset;

        public UIWorldHud(RectTransform rect, Camera worldCamera, Camera uiCamera, Vector3 offset)
        {
            _rect = rect; _worldCamera = worldCamera; _uiCamera = uiCamera; _offset = offset;
        }

        public void LateUpdate(Vector3 worldPosition)
        {
            if (_rect == null || _worldCamera == null) return;
            var screen = _worldCamera.WorldToScreenPoint(worldPosition + _offset);
            if (screen.z < 0f) { _rect.gameObject.SetActive(false); return; }
            _rect.gameObject.SetActive(true);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect.parent as RectTransform, screen, _uiCamera, out var local);
            _rect.anchoredPosition = local;
        }
    }
}
