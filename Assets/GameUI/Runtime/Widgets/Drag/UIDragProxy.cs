using UnityEngine;

namespace GameUI.Runtime
{
    public sealed class UIDragProxy
    {
        private readonly RectTransform _rect;
        public UIDragProxy(RectTransform rect) { _rect = rect; }
        public void SetScreenPosition(Vector2 position, Camera camera)
        {
            if (_rect == null) return;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect.parent as RectTransform, position, camera, out local)) _rect.anchoredPosition = local;
        }
        public void SetVisible(bool visible) { if (_rect != null) _rect.gameObject.SetActive(visible); }
    }
}
