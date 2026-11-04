using UnityEngine;

namespace GameFramework.UI
{
    /// <summary>Unity 层对象：只持有该逻辑层对应的 Canvas，不保存导航历史。</summary>
    internal sealed class UILayer
    {
        internal UILayer(LayerType type, Canvas canvas) { LayerType = type; Canvas = canvas; }
        internal LayerType LayerType { get; }
        internal Canvas Canvas { get; }
    }
}
