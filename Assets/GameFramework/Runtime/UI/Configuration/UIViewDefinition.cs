using System;
using System.Collections.Generic;

namespace GameFramework.UI
{
    /// <summary>A validated, immutable copy of one UI configuration, owned by a UIManager.</summary>
    public sealed class UIViewDefinition
    {
        internal UIViewDefinition(Type type, UIConfig config, UIChildDefinition[] children)
        {
            ViewType = type;
            PrefabAddress = config.PrefabAddress;
            LayerType = config.LayerType;
            IsPopup = config.IsPopup;
            Children = Array.AsReadOnly((UIChildDefinition[])children.Clone());
        }

        public Type ViewType { get; }
        public string PrefabAddress { get; }
        public LayerType LayerType { get; }
        public bool IsPopup { get; }
        public IReadOnlyList<UIChildDefinition> Children { get; }
    }
}
