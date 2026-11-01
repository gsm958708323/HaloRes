using GameUI.Components;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI.Runtime
{
    public enum UIScreenFitMode { None, Expand, LetterboxHeight, LetterboxWidth, LetterboxAuto }

    public sealed class UIScreenFitter
    {
        private readonly RectTransform _content;
        private readonly CanvasScaler _scaler;
        private readonly Vector2 _referenceResolution;
        private readonly UIScreenFitMode _mode;
        private static readonly Vector3[] Corners = new Vector3[4];

        public UIScreenFitter(RectTransform content, CanvasScaler scaler, Vector2 referenceResolution, UIScreenFitMode mode)
        {
            _content = content;
            _scaler = scaler;
            _referenceResolution = new Vector2(Mathf.Max(1f, referenceResolution.x), Mathf.Max(1f, referenceResolution.y));
            _mode = mode;
            ContentScreenRect = new Rect(0f, 0f, Screen.width, Screen.height);
        }

        public Rect ContentScreenRect { get; private set; }

        public bool Apply(Rect safeArea, Vector2Int screenSize, IReadOnlyList<UISafeAreaFitter> fitters, Camera eventCamera = null)
        {
            if (_content == null || screenSize.x <= 0 || screenSize.y <= 0) return false;
            var contentRect = CalculateContentRect(screenSize);
            var changed = ContentScreenRect != contentRect;
            ContentScreenRect = contentRect;

            if (_scaler != null)
            {
                _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                _scaler.referenceResolution = _referenceResolution;
                _scaler.screenMatchMode = _mode == UIScreenFitMode.Expand
                    ? CanvasScaler.ScreenMatchMode.Expand : CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                _scaler.matchWidthOrHeight = 0.5f;
            }

            var min = new Vector2(contentRect.xMin / screenSize.x, contentRect.yMin / screenSize.y);
            var max = new Vector2(contentRect.xMax / screenSize.x, contentRect.yMax / screenSize.y);
            _content.anchorMin = min;
            _content.anchorMax = max;
            _content.offsetMin = Vector2.zero;
            _content.offsetMax = Vector2.zero;

            if (fitters == null) return changed;
            for (var i = 0; i < fitters.Count; i++)
                if (fitters[i] != null) ApplySafeArea(fitters[i], safeArea, contentRect, screenSize, eventCamera);
            return changed;
        }

        private Rect CalculateContentRect(Vector2Int screenSize)
        {
            var full = new Rect(0f, 0f, screenSize.x, screenSize.y);
            var mode = _mode;
            if (mode == UIScreenFitMode.None || mode == UIScreenFitMode.Expand) return full;

            var designAspect = _referenceResolution.x / _referenceResolution.y;
            var screenAspect = (float)screenSize.x / screenSize.y;
            if (mode == UIScreenFitMode.LetterboxAuto)
                mode = screenAspect >= designAspect ? UIScreenFitMode.LetterboxHeight : UIScreenFitMode.LetterboxWidth;

            if (mode == UIScreenFitMode.LetterboxHeight)
            {
                var width = screenSize.y * designAspect;
                return new Rect((screenSize.x - width) * 0.5f, 0f, width, screenSize.y);
            }

            var height = screenSize.x / designAspect;
            return new Rect(0f, (screenSize.y - height) * 0.5f, screenSize.x, height);
        }

        private static void ApplySafeArea(UISafeAreaFitter fitter, Rect safeArea, Rect contentRect, Vector2Int screenSize, Camera eventCamera)
        {
            var rect = fitter.transform as RectTransform;
            var parent = rect != null ? rect.parent as RectTransform : null;
            if (rect == null || parent == null) return;

            var bounds = ScreenBounds(parent, eventCamera);
            var safe = Intersect(Intersect(safeArea, contentRect), bounds);
            var edges = fitter.edges;
            if ((edges & UISafeEdges.Left) == 0) safe.xMin = Mathf.Max(contentRect.xMin, bounds.xMin);
            if ((edges & UISafeEdges.Right) == 0) safe.xMax = Mathf.Min(contentRect.xMax, bounds.xMax);
            if ((edges & UISafeEdges.Bottom) == 0) safe.yMin = Mathf.Max(contentRect.yMin, bounds.yMin);
            if ((edges & UISafeEdges.Top) == 0) safe.yMax = Mathf.Min(contentRect.yMax, bounds.yMax);

            if (safe.width <= 0f || safe.height <= 0f) return;
            var min = new Vector2(Mathf.InverseLerp(bounds.xMin, bounds.xMax, safe.xMin), Mathf.InverseLerp(bounds.yMin, bounds.yMax, safe.yMin));
            var max = new Vector2(Mathf.InverseLerp(bounds.xMin, bounds.xMax, safe.xMax), Mathf.InverseLerp(bounds.yMin, bounds.yMax, safe.yMax));
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Rect ScreenBounds(RectTransform rect, Camera camera)
        {
            rect.GetWorldCorners(Corners);
            var first = RectTransformUtility.WorldToScreenPoint(camera, Corners[0]);
            var min = first;
            var max = first;
            for (var i = 1; i < Corners.Length; i++)
            {
                var point = RectTransformUtility.WorldToScreenPoint(camera, Corners[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Rect Intersect(Rect left, Rect right)
        {
            return Rect.MinMaxRect(
                Mathf.Max(left.xMin, right.xMin), Mathf.Max(left.yMin, right.yMin),
                Mathf.Min(left.xMax, right.xMax), Mathf.Min(left.yMax, right.yMax));
        }
    }
}
