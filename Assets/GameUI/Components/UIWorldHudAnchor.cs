using UnityEngine;
namespace GameUI.Components
{
    public sealed class UIWorldHudAnchor : MonoBehaviour
    {
        public RectTransform hud;
        public Vector3 offset;
        public int flags;
        public new string tag;
        private void OnEnable() { UIComponentIndex<UIWorldHudAnchor>.Register(this); }
        private void OnDisable() { UIComponentIndex<UIWorldHudAnchor>.Unregister(this); }
    }
}
