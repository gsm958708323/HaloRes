using System;

namespace GameFramework.UI
{
    /// <summary>Serialized configuration. Type names include their assembly; resources use AssetManifest addresses.</summary>
    [Serializable]
    public sealed class UIConfig
    {
        public string ViewType;
        public string PrefabAddress;
        public LayerType LayerType = LayerType.NormalLayer;
        public bool IsPopup;
        public UIChildConfig[] ChildList = Array.Empty<UIChildConfig>();
    }
}
