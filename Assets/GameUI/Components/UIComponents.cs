using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace GameUI.Components
{
    public enum UISafeEdges { None = 0, Top = 1, Bottom = 2, Left = 4, Right = 8, Sides = Left | Right, All = Top | Bottom | Sides }

    [Serializable]
    public sealed class UIBindingEntry
    {
        public string name;
        public Component component;
    }

    [Serializable]
    public sealed class UIBindingNode
    {
        public string kind;
        public string typeName;
        public UIBindingData data;
    }

    public sealed class UIBindingData : MonoBehaviour
    {
        public List<UIBindingEntry> entries = new List<UIBindingEntry>();
        public List<UIBindingNode> nodes = new List<UIBindingNode>();
        public uint layoutHash;

        public T Get<T>(int index) where T : Component
        {
            if (index < 0 || index >= entries.Count) throw new IndexOutOfRangeException("Binding entry index: " + index);
            var component = entries[index].component as T;
            if (component == null) throw new InvalidOperationException("Binding entry " + index + " is not " + typeof(T).Name + ".");
            return component;
        }

        public UIBindingData Node(int index)
        {
            if (index < 0 || index >= nodes.Count || nodes[index] == null || nodes[index].data == null)
                throw new IndexOutOfRangeException("Binding node index: " + index);
            return nodes[index].data;
        }

        public void CheckLayout(uint expected)
        {
            if (layoutHash != 0 && layoutHash != expected)
                throw new InvalidOperationException("UI binding layout hash mismatch. Expected " + expected.ToString("X8") + ", got " + layoutHash.ToString("X8") + ".");
        }
    }

    public sealed class UISortingSlot : MonoBehaviour
    {
        [Range(1, 17)] public int offset = 1;
        public int flags;
        public string tag;
        private void OnEnable() { UIComponentIndex<UISortingSlot>.Register(this); }
        private void OnDisable() { UIComponentIndex<UISortingSlot>.Unregister(this); }
    }

    public sealed class UIViewTransitions : MonoBehaviour
    {
        public PlayableDirector enter;
        public PlayableDirector exit;
        private void OnEnable() { UIComponentIndex<UIViewTransitions>.Register(this); }
        private void OnDisable() { UIComponentIndex<UIViewTransitions>.Unregister(this); }
    }

    public sealed class UISafeAreaFitter : MonoBehaviour
    {
        public UISafeEdges edges = UISafeEdges.Top | UISafeEdges.Sides;
        public int flags;
        public string tag;
        private void OnEnable() { UIComponentIndex<UISafeAreaFitter>.Register(this); }
        private void OnDisable() { UIComponentIndex<UISafeAreaFitter>.Unregister(this); }
    }

    public sealed class UIModelView : MonoBehaviour
    {
        public RawImage target;
        public Vector2Int rtSize = new Vector2Int(512, 512);
        public string stageTag;
        private void OnEnable() { UIComponentIndex<UIModelView>.Register(this); }
        private void OnDisable() { UIComponentIndex<UIModelView>.Unregister(this); }
    }

    public sealed class UIWorldHudAnchor : MonoBehaviour
    {
        public Vector3 offset;
        public string tag;

        private void OnEnable() { UIComponentIndex<UIWorldHudAnchor>.Register(this); }
        private void OnDisable() { UIComponentIndex<UIWorldHudAnchor>.Unregister(this); }
    }

    public sealed class UIComponentIndex<T> where T : MonoBehaviour
    {
        private static readonly List<T> Items = new List<T>();
        public static IReadOnlyList<T> All { get { return Items; } }
        public static void Register(T item) { if (item != null && !Items.Contains(item)) Items.Add(item); }
        public static void Unregister(T item) { Items.Remove(item); }
    }
}
