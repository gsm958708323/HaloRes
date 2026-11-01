using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameUI.Components
{
    [Serializable]
    public sealed class UIBindingEntry { public string name; public Component component; }
    [Serializable]
    public sealed class UIBindingNode { public string kind; public string typeName; public UIBindingData data; }

    public sealed class UIBindingData : MonoBehaviour
    {
        public List<UIBindingEntry> entries = new List<UIBindingEntry>();
        public List<UIBindingNode> nodes = new List<UIBindingNode>();
        public uint layoutHash;
        public string logicType;

        public T Get<T>(int index) where T : Component
        {
            if (index < 0 || index >= entries.Count) throw new IndexOutOfRangeException("Binding entry index: " + index);
            var result = entries[index].component as T;
            if (result == null) throw new InvalidOperationException("Binding entry " + index + " is not " + typeof(T).Name);
            return result;
        }

        public UIBindingData Node(int index)
        {
            if (index < 0 || index >= nodes.Count || nodes[index] == null || nodes[index].data == null)
                throw new IndexOutOfRangeException("Binding node index: " + index);
            return nodes[index].data;
        }

        public void CheckLayout(uint expected)
        {
            if (layoutHash != expected) throw new InvalidOperationException("UI binding layout hash mismatch. Expected "
                + expected.ToString("X8") + ", got " + layoutHash.ToString("X8"));
        }
    }
}
