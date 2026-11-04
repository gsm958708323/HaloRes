using System;

namespace GameFramework.UI
{
    [Serializable]
    public sealed class UIChildConfig
    {
        public string ViewType;
        public ResType ResType;
        public string NodePath;
        public bool LoadWithParent;
    }

    public sealed class UIChildDefinition
    {
        internal UIChildDefinition(Type type, UIChildConfig source)
        {
            ViewType = type;
            ResType = source.ResType;
            NodePath = source.NodePath;
            LoadWithParent = source.LoadWithParent;
        }
        public Type ViewType { get; }
        public ResType ResType { get; }
        public string NodePath { get; }
        public bool LoadWithParent { get; }
    }
}
