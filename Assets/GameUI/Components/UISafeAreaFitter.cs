using UnityEngine;
namespace GameUI.Components
{
    [System.Flags]
    public enum UISafeEdges { None = 0, Top = 1, Bottom = 2, Left = 4, Right = 8, Sides = Left | Right, All = Top | Bottom | Sides }
    public sealed class UISafeAreaFitter : MonoBehaviour
    {
        public UISafeEdges edges = UISafeEdges.Top | UISafeEdges.Sides;
        public int flags;
        public new string tag;
        private void OnEnable() { UIComponentIndex<UISafeAreaFitter>.Register(this); }
        private void OnDisable() { UIComponentIndex<UISafeAreaFitter>.Unregister(this); }
    }
}
