using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.UI.Samples
{
    /// <summary>Optional source-sample diagnostic. Disabled by default; private uGUI fields are version dependent.</summary>
    public sealed class CanvasRebuildListener : MonoBehaviour
    {
        private IEnumerable<ICanvasElement> _layout;
        private IEnumerable<ICanvasElement> _graphics;

        private void Start()
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(CanvasUpdateRegistry);
            _layout = type.GetField("m_LayoutRebuildQueue", flags)?.GetValue(CanvasUpdateRegistry.instance) as IEnumerable<ICanvasElement>;
            _graphics = type.GetField("m_GraphicRebuildQueue", flags)?.GetValue(CanvasUpdateRegistry.instance) as IEnumerable<ICanvasElement>;
            if (_layout == null || _graphics == null)
            {
                Debug.LogWarning("CanvasRebuildListener is unavailable for this uGUI version.");
                enabled = false;
            }
        }

        private void Update()
        {
            Report(_layout, "Layout");
            Report(_graphics, "Graphic");
        }

        private static void Report(IEnumerable<ICanvasElement> elements, string kind)
        {
            if (elements == null) return;
            foreach (var element in elements)
                if (element != null && !element.IsDestroyed() && element.transform != null)
                    Debug.Log(kind + " rebuild: " + element.transform.name);
        }
    }
}
