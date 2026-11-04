using System;

namespace GameFramework.UI
{
    internal enum NavigationEntryStatus { Loading, Ready, Open, Closed, Failed }

    /// <summary>一次访问记录；不持有 UIView、UIViewRecord 或 Unity 对象。</summary>
    internal sealed class UINavigationEntry
    {
        internal long Id;
        internal Type ViewType;
        internal LayerType Layer;
        internal bool IsPopup;
        internal object[] Arguments;
        internal object SavedState;
        internal bool HasSavedState;
        internal NavigationEntryStatus Status;
    }
}
