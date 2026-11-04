using System;
using System.Threading.Tasks;

namespace GameFramework.UI
{
    /// <summary>一次共享的视图实例及其加载状态；导航记录只通过 ViewType 关联它。</summary>
    internal sealed class UIViewRecord
    {
        internal Type ViewType;
        internal UIGameView View;
        internal int References;
        internal bool Ready;
        internal Exception Error;
        internal Task Preparation;
        internal long ActiveEntryId;
    }
}
