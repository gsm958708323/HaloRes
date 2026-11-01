using GameUI.Kernel;
using UnityEngine;

namespace GameUI.Runtime
{
    public sealed class UILog : IUILogSink
    {
        public void Log(UILogLevel level, string message)
        {
            switch (level)
            {
                case UILogLevel.Error: Debug.LogError("[GameUI] " + message); break;
                case UILogLevel.Warning: Debug.LogWarning("[GameUI] " + message); break;
                default: Debug.Log("[GameUI] " + message); break;
            }
        }
    }

    public sealed class UnityUIClock : IUIClock
    {
        public double Now { get { return Time.unscaledTime; } }
    }
}
