using UnityEngine;
using UnityEngine.UI;

namespace Game.AssetSystem.DemoApp
{
    /// <summary>Small uGUI builder used by the interactive demo (no prefabs).</summary>
    public static class AssetDemoUiKit
    {
        public static readonly Color Background = new Color(0.043f, 0.055f, 0.078f, 1f);
        public static readonly Color Panel = new Color(0.078f, 0.094f, 0.125f, 1f);
        public static readonly Color PanelAlt = new Color(0.098f, 0.118f, 0.157f, 1f);
        public static readonly Color Border = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color TextPrimary = new Color(0.902f, 0.925f, 0.957f, 1f);
        public static readonly Color TextMuted = new Color(0.55f, 0.60f, 0.67f, 1f);
        public static readonly Color Accent = new Color(0.29f, 0.78f, 0.62f, 1f);
        public static readonly Color AccentDim = new Color(0.29f, 0.78f, 0.62f, 0.18f);
        public static readonly Color Warn = new Color(0.96f, 0.62f, 0.30f, 1f);
        public static readonly Color Danger = new Color(0.93f, 0.42f, 0.42f, 1f);
        public static readonly Color Idle = new Color(0.45f, 0.49f, 0.56f, 1f);

        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font != null)
                {
                    return _font;
                }

                var names = new[]
                {
                    "Microsoft YaHei",
                    "Microsoft YaHei UI",
                    "PingFang SC",
                    "Noto Sans CJK SC",
                    "SimHei",
                    "Arial"
                };

                _font = UnityEngine.Font.CreateDynamicFontFromOSFont(names, 16);

                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }

                return _font;
            }
        }

        public static RectTransform Node(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static Image PanelRect(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            Color color,
            float radius)
        {
            var rect = Node(parent, name, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            var sprite = RoundedSprite();
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }

            return image;
        }

        public static Text Label(
            Transform parent,
            string name,
            string text,
            float x,
            float y,
            float width,
            float height,
            int size,
            Color color,
            TextAnchor anchor = TextAnchor.UpperLeft,
            bool bold = false)
        {
            var rect = Node(parent, name, x, y, width, height);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.fontSize = size;
            label.color = color;
            label.text = text;
            label.alignment = anchor;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        public static Button Button(
            Transform parent,
            string name,
            string caption,
            float x,
            float y,
            float width,
            float height,
            Color normal,
            Color captionColor,
            System.Action onClick,
            int fontSize = 13)
        {
            var rect = Node(parent, name, x, y, width, height);

            var image = rect.gameObject.AddComponent<Image>();
            image.color = normal;
            image.raycastTarget = true;

            var sprite = RoundedSprite();
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;

            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            var label = Label(
                rect,
                "Caption",
                caption,
                0f,
                0f,
                width,
                height,
                fontSize,
                captionColor,
                TextAnchor.MiddleCenter,
                true);

            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            return button;
        }

        private static Sprite _rounded;

        private static Sprite RoundedSprite()
        {
            if (_rounded != null)
            {
                return _rounded;
            }

            const int size = 16;
            const float radius = 5f;

            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var center = (size - 1) * 0.5f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = Mathf.Max(Mathf.Abs(x - center) - (center - radius), 0f);
                    var dy = Mathf.Max(Mathf.Abs(y - center) - (center - radius), 0f);
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();

            _rounded = Sprite.Create(
                texture,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));

            _rounded.name = "AssetDemoRounded";
            return _rounded;
        }
    }
}
