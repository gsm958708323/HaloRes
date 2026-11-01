using GameUI.Components;
using UnityEngine;

namespace GameUI.Runtime
{
    public sealed class UIScreenFitter
    {
        private readonly RectTransform _content;
        private readonly UISafeAreaFitter _config;
        private Rect _last;

        public UIScreenFitter(RectTransform content, UISafeAreaFitter config)
        { _content = content; _config = config; _last = new Rect(float.NaN, 0f, 0f, 0f); }

        public bool Apply(Rect safeArea, Vector2Int screenSize)
        {
            if (_content == null) return false;
            if (_last.x == safeArea.x && _last.y == safeArea.y && _last.width == safeArea.width && _last.height == safeArea.height) return false;
            _last = safeArea;
            var min = new Vector2(safeArea.x / screenSize.x, safeArea.y / screenSize.y);
            var max = new Vector2((safeArea.x + safeArea.width) / screenSize.x, (safeArea.y + safeArea.height) / screenSize.y);
            if (_config != null)
            {
                if ((_config.edges & UISafeEdges.Left) == 0) min.x = 0f;
                if ((_config.edges & UISafeEdges.Right) == 0) max.x = 1f;
                if ((_config.edges & UISafeEdges.Bottom) == 0) min.y = 0f;
                if ((_config.edges & UISafeEdges.Top) == 0) max.y = 1f;
            }
            _content.anchorMin = min; _content.anchorMax = max; _content.offsetMin = Vector2.zero; _content.offsetMax = Vector2.zero;
            return true;
        }
    }
}
